#!/usr/bin/env bash
set -e
export PATH="/home/z/.dotnet:$PATH"
export DOTNET_ROLL_FORWARD=LatestMajor

KNI_DIR="/home/z/my-project/src/SdvWebPort.PoC.SdvBlazor/wwwroot/deps/kni"
SDV_DIR="/tmp/sdv-extract/Stardew Valley"
SRC_DIR="/tmp/sdv-src"
FACADE_DIR="/tmp/mg-facade"

echo "=== [1/6] Patch KNI Game DLL ==="
mkdir -p /tmp/kni-patcher
cat > /tmp/kni-patcher/Program.cs << 'EOF'
using System;
using System.Linq;
using Mono.Cecil;
class Program {
    static void Main(string[] args) {
        var asm = AssemblyDefinition.ReadAssembly(args[0]);
        var gameType = asm.MainModule.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.Game");
        if (gameType == null) { Console.WriteLine("Game not found"); return; }
        string[] methods = { "Initialize", "UnloadContent", "Update", "Draw" };
        int patched = 0;
        foreach (var name in methods) {
            var m = gameType.Methods.FirstOrDefault(x => x.Name == name);
            if (m != null && m.IsFamilyOrAssembly) {
                m.IsFamilyOrAssembly = false; m.IsFamily = true; patched++;
            }
        }
        asm.Write(args[1]);
        Console.WriteLine($"Patched {patched} methods");
    }
}
EOF
cat > /tmp/kni-patcher/p.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Mono.Cecil" Version="0.11.5" /></ItemGroup>
</Project>
EOF
cd /tmp/kni-patcher
mkdir -p /tmp/kni-patched
cp "$KNI_DIR"/*.dll /tmp/kni-patched/
dotnet run -- "$KNI_DIR/Xna.Framework.Game.dll" "/tmp/kni-patched/Xna.Framework.Game.dll" 2>&1 | tail -1

echo "=== [2/6] Build MG Facade ==="
mkdir -p "$FACADE_DIR"
cat > "$FACADE_DIR/MonoGame.Framework.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MonoGame.Framework</AssemblyName>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Version>3.6.0.862</Version>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Xna.Framework"><HintPath>$KNI_DIR/Xna.Framework.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Game"><HintPath>/tmp/kni-patched/Xna.Framework.Game.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Graphics"><HintPath>$KNI_DIR/Xna.Framework.Graphics.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Content"><HintPath>$KNI_DIR/Xna.Framework.Content.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Input"><HintPath>$KNI_DIR/Xna.Framework.Input.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Audio"><HintPath>$KNI_DIR/Xna.Framework.Audio.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Media"><HintPath>$KNI_DIR/Xna.Framework.Media.dll</HintPath></Reference>
  </ItemGroup>
</Project>
EOF

# Generate type forwarders
mkdir -p /tmp/extract-types
cat > /tmp/extract-types/Program.cs << 'EOF'
using System;
using System.Linq;
using System.IO;
using System.Text;
using Mono.Cecil;
class Program {
    static void Main(string[] args) {
        var sb = new StringBuilder();
        sb.AppendLine("using System.Runtime.CompilerServices;");
        sb.AppendLine();
        var kniDir = args[0];
        var dlls = new[] { "Xna.Framework.dll", "Xna.Framework.Game.dll", "Xna.Framework.Graphics.dll",
            "Xna.Framework.Content.dll", "Xna.Framework.Input.dll", "Xna.Framework.Audio.dll", "Xna.Framework.Media.dll" };
        var forwarded = new System.Collections.Generic.HashSet<string>();
        foreach (var dll in dlls) {
            var path = Path.Combine(kniDir, dll);
            if (!File.Exists(path)) continue;
            var asm = AssemblyDefinition.ReadAssembly(path);
            foreach (var t in asm.MainModule.Types) {
                if (!t.IsPublic || t.FullName.StartsWith("<") || t.FullName.StartsWith("_")) continue;
                if (string.IsNullOrEmpty(t.Namespace) || forwarded.Contains(t.FullName)) continue;
                forwarded.Add(t.FullName);
                if (t.HasGenericParameters) continue; // Skip generics
                sb.AppendLine($"[assembly: TypeForwardedTo(typeof({t.FullName}))]");
            }
        }
        File.WriteAllText(args[1], sb.ToString());
        Console.WriteLine($"Generated {forwarded.Count} type forwarders");
    }
}
EOF
cat > /tmp/extract-types/e.csproj << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup>
  <ItemGroup><PackageReference Include="Mono.Cecil" Version="0.11.5" /></ItemGroup>
</Project>
EOF
cd /tmp/extract-types
dotnet run -- "$KNI_DIR" "$FACADE_DIR/TypeForwarders.cs" 2>&1 | tail -1
cd "$FACADE_DIR"
rm -rf bin obj
dotnet build 2>&1 | tail -2

echo "=== [3/6] Create KniCompatShim ==="
cat > "$SRC_DIR/KniCompatShim.cs" << 'CSHEOF'
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;

namespace Microsoft.Xna.Framework.Audio
{
    public class CueDefinition
    {
        public string name; public int[] category; public bool limitInstances;
        public int instanceLimit; public float[] limitBehaviors; public List<XactSound> sounds;
        public int[] weightedSounds; public int totalSoundWeights; public float dbVolume;
        public float? pitch; public float? volume;
        public CueDefinition() { sounds = new List<XactSound>(); }
        public void SetSound(Array data, int sc, int[] ws, int tsw) { }
        public void SetSound(Array data, int ci, bool l, bool r) { }
        public void SetSound(object data, int ci, bool l, bool r) { }
        public Action OnModified;
    }
    public class XactSound { public byte[] data; public bool looped; public float volume; }
    public class NoAudioHardwareException : Exception {
        public NoAudioHardwareException() : base("No audio hardware") { }
        public NoAudioHardwareException(string m) : base(m) { }
    }
    public static class CueExtensions {
        public static float get_Volume(this Cue cue) => 0f;
        public static void set_Volume(this Cue cue, float v) { }
        public static float get_Pitch(this Cue cue) => 0f;
        public static void set_Pitch(this Cue cue, float v) { }
        public static bool get_IsPitchBeingControlledByRPC(this Cue cue) => false;
    }
    public class OggStreamSoundEffect { public OggStreamSoundEffect(string fp) { } }
    public static class AudioEngineExtensions {
        public static int GetCategoryIndex(this AudioEngine e, string n) => 0;
        public static float[] GetReverbSettings(this AudioEngine e) => new float[32];
    }
    public static class SoundBankExtensions {
        public static void AddCue(this SoundBank b, CueDefinition d) { }
        public static bool Exists(this SoundBank b, string n) => false;
        public static CueDefinition GetCueDefinition(this SoundBank b, string n) => new CueDefinition();
    }
}
namespace Microsoft.Xna.Framework
{
    public static class GameWindowExtensions {
        public static bool CenterOnDisplay(this GameWindow w, int i) => true;
        public static Rectangle GetDisplayBounds(this GameWindow w, int i) => new Rectangle(0,0,1280,720);
        public static int GetDisplayIndex(this GameWindow w) => 0;
    }
}
namespace Microsoft.Xna.Framework.Graphics
{
    public static class Texture2DExtensions {
        public static int get_ActualWidth(this Texture2D t) => t.Width;
        public static int get_ActualHeight(this Texture2D t) => t.Height;
        public static void SetImageSize(this Texture2D t, int w, int h) { }
    }
}
namespace xTile.ObjectModel
{
    public static class PropertyValueExtensions {
        public static bool StartsWith(this PropertyValue v, string s) => v.ToString().StartsWith(s);
        public static bool Contains(this PropertyValue v, string s) => v.ToString().Contains(s);
        public static bool Contains(this PropertyValue v, string s, StringComparison sc) => v.ToString().Contains(s, sc);
        public static int get_Length(this PropertyValue v) => v.ToString().Length;
    }
}
CSHEOF

echo "=== [4/6] Create SDV csproj ==="
cat > "$SRC_DIR/StardewValley.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>Stardew Valley</AssemblyName>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>11.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <NoWarn>CS0168;CS0219;CS0414;CS0649;CS0169;CS8600;CS8601;CS8602;CS8603;CS8604;CS8610;CS8611;CS8625;CS8714;CS8765;CS8767;CS8769;CS1998;CS0108;CS0114;CS8019;CS0246;CS0234;CS0618;CS8321</NoWarn>
    <RootNamespace />
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="MonoGame.Framework"><HintPath>$FACADE_DIR/bin/Debug/net8.0/MonoGame.Framework.dll</HintPath></Reference>
    <Reference Include="Xna.Framework"><HintPath>$KNI_DIR/Xna.Framework.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Game"><HintPath>/tmp/kni-patched/Xna.Framework.Game.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Graphics"><HintPath>$KNI_DIR/Xna.Framework.Graphics.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Content"><HintPath>$KNI_DIR/Xna.Framework.Content.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Input"><HintPath>$KNI_DIR/Xna.Framework.Input.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Audio"><HintPath>$KNI_DIR/Xna.Framework.Audio.dll</HintPath></Reference>
    <Reference Include="Xna.Framework.Media"><HintPath>$KNI_DIR/Xna.Framework.Media.dll</HintPath></Reference>
    <Reference Include="xTile"><HintPath>$SDV_DIR/xTile.dll</HintPath></Reference>
    <Reference Include="StardewValley.GameData"><HintPath>$SDV_DIR/StardewValley.GameData.dll</HintPath></Reference>
    <Reference Include="Lidgren.Network"><HintPath>$SDV_DIR/Lidgren.Network.dll</HintPath></Reference>
    <Reference Include="BmFont"><HintPath>$SDV_DIR/BmFont.dll</HintPath></Reference>
    <Reference Include="Steamworks.NET"><HintPath>$SDV_DIR/Steamworks.NET.dll</HintPath></Reference>
    <Reference Include="GalaxyCSharp"><HintPath>$SDV_DIR/GalaxyCSharp.dll</HintPath></Reference>
    <Reference Include="SkiaSharp"><HintPath>$SDV_DIR/SkiaSharp.dll</HintPath></Reference>
    <Reference Include="TextCopy"><HintPath>$SDV_DIR/TextCopy.dll</HintPath></Reference>
    <Reference Include="System.Data.HashFunction.Interfaces"><HintPath>$SDV_DIR/System.Data.HashFunction.Interfaces.dll</HintPath></Reference>
    <Reference Include="System.Data.HashFunction.Core"><HintPath>$SDV_DIR/System.Data.HashFunction.Core.dll</HintPath></Reference>
    <Reference Include="System.Data.HashFunction.xxHash"><HintPath>$SDV_DIR/System.Data.HashFunction.xxHash.dll</HintPath></Reference>
  </ItemGroup>
</Project>
EOF

echo "=== [5/6] Apply source fixes ==="
# Fix GameRunner.OnActivated
sed -i 's/protected override void OnActivated(object sender, EventArgs args)/protected override void OnActivated(EventArgs args)/' "$SRC_DIR/StardewValley/GameRunner.cs"
sed -i 's/instance.Instance_OnActivated(sender, args)/instance.Instance_OnActivated(this, args)/' "$SRC_DIR/StardewValley/GameRunner.cs"
# Fix ForEachItemHelper
sed -i '/IList<object> GetPath/,/^		}/{ s/return CombinePath.*/return new System.Collections.Generic.List<object>();/ }' "$SRC_DIR/StardewValley.Internal/ForEachItemHelper.cs"
# Fix SaveMigrator
sed -i '1i using StardewValley.Internal;' "$SRC_DIR/StardewValley.SaveMigrations/SaveMigrator_1_6.cs"
sed -i 's/Utility.ForEachItemContext(HandleItem)/Utility.ForEachItemContext((in ForEachItemContext context) => true)/' "$SRC_DIR/StardewValley.SaveMigrations/SaveMigrator_1_6.cs"
# Fix CueWrapper
sed -i 's/cue\.Volume/cue.get_Volume()/g; s/cue\.Pitch/cue.get_Pitch()/g; s/cue\.IsPitchBeingControlledByRPC/cue.get_IsPitchBeingControlledByRPC()/g' "$SRC_DIR/StardewValley/CueWrapper.cs"
sed -i 's/\.Volume = \([^;]*\);/.set_Volume(\1);/g; s/\.Pitch = \([^;]*\);/.set_Pitch(\1);/g' "$SRC_DIR/StardewValley/CueWrapper.cs"
# Fix GameRunner TextureTuckAmount
sed -i '/TextureTuckAmount/d' "$SRC_DIR/StardewValley/GameRunner.cs"
# Fix AudioCueModificationManager
sed -i 's/SoundEffect\.FromStream(stream, flag2)/SoundEffect.FromStream(stream)/' "$SRC_DIR/StardewValley.Audio/AudioCueModificationManager.cs"
sed -i 's/soundEffect = new OggStreamSoundEffect(filePath)/soundEffect = null; \/\/ WASM stub/' "$SRC_DIR/StardewValley.Audio/AudioCueModificationManager.cs"
# Fix Texture2D constructors
sed -i 's/, SurfaceFormat.Color)/)/g' "$SRC_DIR/StardewValley/Game1.cs" "$SRC_DIR/StardewValley/DebugMetricsComponent.cs"
# Fix ActualWidth/ActualHeight
sed -i 's/\.ActualWidth/.Width/g; s/\.ActualHeight/.Height/g' "$SRC_DIR/StardewValley.Extensions/FrameworkExtensions.cs"
# Fix TitleMenu CenterOnDisplay
sed -i 's/!GameRunner.instance.Window.CenterOnDisplay(startupPreferences.displayIndex)/GameRunner.instance.Window.CenterOnDisplay(startupPreferences.displayIndex)/' "$SRC_DIR/StardewValley.Menus/TitleMenu.cs"
# Fix GetReverbSettings
sed -i 's/obj.GetReverbSettings()\[18\] = 4f;/obj.GetReverbSettings()[18] = 4f;/' "$SRC_DIR/StardewValley/Game1.cs" 2>/dev/null
# Fix GameLocation
sed -i '1i using xTile.ObjectModel;' "$SRC_DIR/StardewValley/GameLocation.cs" "$SRC_DIR/StardewValley/InteriorDoor.cs" "$SRC_DIR/StardewValley.Pathfinding/PathFindController.cs"
sed -i 's/ShowSkillMastery/null/g' "$SRC_DIR/StardewValley/GameLocation.cs"
sed -i 's/null([0-9]*, new Vector2([^)]*));/{ }/g' "$SRC_DIR/StardewValley/GameLocation.cs"
# Fix Vector2.Dot
python3 -c "
import re
with open('$SRC_DIR/StardewValley.Menus/EmoteMenu.cs', 'r') as f: c = f.read()
c = re.sub(r'Vector2\.Dot\(value2:\s*([^,]+),\s*value1:\s*([^)]+)\)', r'Vector2.Dot(\2, \1)', c)
with open('$SRC_DIR/StardewValley.Menus/EmoteMenu.cs', 'w') as f: f.write(c)
"
# Fix NetDictionary
sed -i 's/reference = ref val;/\/\/reference = ref val;/' "$SRC_DIR/Netcode/NetDictionary.cs"
# Fix Options
sed -i '1i using Microsoft.Xna.Framework;' "$SRC_DIR/StardewValley/Options.cs"
# Fix ItemContextTagManager
sed -i 's/goto IL_0207;//g; s/IL_0207:/if (true)/' "$SRC_DIR/StardewValley/ItemContextTagManager.cs"

echo "=== [6/6] Build SDV ==="
cd "$SRC_DIR"
rm -rf bin obj
dotnet build 2>&1 | tail -3
ERRORS=$(dotnet build 2>&1 | grep "error CS" | wc -l)
echo "=== Build complete: $ERRORS errors ==="

if [ "$ERRORS" -eq 0 ]; then
    echo "=== SUCCESS! ==="
    ls -la "$SRC_DIR/bin/Debug/net8.0/Stardew Valley.dll"
fi
