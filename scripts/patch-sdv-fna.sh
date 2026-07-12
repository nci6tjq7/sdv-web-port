#!/usr/bin/env bash
set -e
SRC_DIR="${1:-/tmp/sdv-fna-src}"
SDV_DIR="/tmp/sdv-extract/Stardew Valley"

echo "[+] Applying FNA compat patches to $SRC_DIR"

# Remove duplicate root-level .cs files
for f in "$SRC_DIR"/*.cs; do
  [ -f "$f" ] || continue
  base=$(basename "$f")
  [ -f "$SRC_DIR/StardewValley/$base" ] && rm "$f"
done

# DontLoadDefaultSetting
cat > "$SRC_DIR/DontLoadDefaultSetting.cs" << 'EOF'
using System;namespace StardewValley{[AttributeUsage(AttributeTargets.Field,AllowMultiple=false)]public class DontLoadDefaultSettingAttribute:Attribute{}}
EOF

# FnaCompat.cs
cp /home/z/my-project/scripts/FnaCompat.cs "$SRC_DIR/FnaCompat.cs"

# Create csproj
cat > "$SRC_DIR/StardewValley.csproj" << CSPROJ
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
    <ProjectReference Include="/tmp/FNA/FNA.Core.csproj" />
    <Reference Include="MonoGame.Framework"><HintPath>/tmp/mg-facade-fna/bin/Debug/net8.0/MonoGame.Framework.dll</HintPath></Reference>
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
CSPROJ

# Apply source fixes
cd "$SRC_DIR"
python3 -c "
with open('StardewValley.Internal/ForEachItemHelper.cs', 'r') as f: c = f.read()
c = c.replace('return CombinePath(((_003C_003Ec__DisplayClass4_0<TItem>)this).getParentPath, ((_003C_003Ec__DisplayClass4_0<TItem>)this).list);', 'return new System.Collections.Generic.List<object>();')
with open('StardewValley.Internal/ForEachItemHelper.cs', 'w') as f: f.write(c)
"
sed -i '1i using StardewValley.Internal;' StardewValley.SaveMigrations/SaveMigrator_1_6.cs
sed -i 's/Utility.ForEachItemContext(HandleItem)/Utility.ForEachItemContext((in ForEachItemContext context) => true)/' StardewValley.SaveMigrations/SaveMigrator_1_6.cs
sed -i '/TextureTuckAmount/d' StardewValley/GameRunner.cs
sed -i 's/soundEffect = new OggStreamSoundEffect(filePath)/soundEffect = null; \/\/ WASM/' StardewValley.Audio/AudioCueModificationManager.cs
sed -i 's/SoundEffect\.FromStream(stream, flag2)/SoundEffect.FromStream(stream)/' StardewValley.Audio/AudioCueModificationManager.cs
sed -i 's/, SurfaceFormat.Color)/)/g' StardewValley/Game1.cs StardewValley/DebugMetricsComponent.cs
sed -i 's/\.ActualWidth/.Width/g; s/\.ActualHeight/.Height/g' StardewValley.Extensions/FrameworkExtensions.cs
sed -i 's/, mipmap: false//g; s/, mipmap: true//g' StardewValley/Game1.cs StardewValley/DebugMetricsComponent.cs
sed -i '/HardwareModeSwitch/d' StardewValley/Game1.cs StardewValley/Options.cs
sed -i 's/\.TextInput += /\/\/.TextInput += /g; s/\.TextInput -= /\/\/.TextInput -= /g' StardewValley/KeyboardDispatcher.cs
sed -i 's/_window\/\/.TextInput -= Event_TextInput;/\/\/ _window.TextInput -= Event_TextInput;/' StardewValley/KeyboardDispatcher.cs
sed -i 's/window\/\/.TextInput += Event_TextInput;/\/\/ window.TextInput += Event_TextInput;/' StardewValley/KeyboardDispatcher.cs
sed -i 's/reference = ref val;/\/\/reference = ref val;/' Netcode/NetDictionary.cs
sed -i '1i using Microsoft.Xna.Framework;' StardewValley/Options.cs
python3 -c "
with open('StardewValley/Program.cs', 'r') as f: c = f.read()
c = c.replace('_sdk = new SteamHelper ();\n\t\t\t\tif (_sdk == null) {\n\t\t\t\t\t_sdk = new NullSDKHelper ();\n\t\t\t\t}', '_sdk = new NullSDKHelper ();')
with open('StardewValley/Program.cs', 'w') as f: f.write(c)
"
sed -i 's/RequestLock(ContinueDemolish, BuildingLockFailed)/RequestLock(() => {}, BuildingLockFailed)/' StardewValley.Menus/CarpenterMenu.cs
python3 -c "
with open('StardewValley.Menus/EmoteMenu.cs', 'r') as f: c = f.read()
old = 'Vector2.Dot(value2: new Vector2((float)_emoteButtons[i].bounds.Center.X - ((float)xPositionOnScreen + (float)width / 2f), (float)_emoteButtons[i].bounds.Center.Y - ((float)yPositionOnScreen + (float)height / 2f)), value1: value)'
new = 'Vector2.Dot(value, new Vector2((float)_emoteButtons[i].bounds.Center.X - ((float)xPositionOnScreen + (float)width / 2f), (float)_emoteButtons[i].bounds.Center.Y - ((float)yPositionOnScreen + (float)height / 2f)))'
c = c.replace(old, new)
with open('StardewValley.Menus/EmoteMenu.cs', 'w') as f: f.write(c)
"
sed -i 's/value\.Length > 0/value.ToString().Length > 0/' StardewValley/GameLocation.cs
sed -i '1i using xTile.ObjectModel;' StardewValley/GameLocation.cs StardewValley/InteriorDoor.cs StardewValley.Pathfinding/PathFindController.cs 2>/dev/null
python3 -c "
with open('StardewValley/ItemContextTagManager.cs', 'r') as f: c = f.read()
import re
c = c.replace('goto IL_0207;', '')
c = c.replace('IL_0207:', 'if (true)')
c = re.sub(r'(\t\t\t\})\n(\t\t\tcase .\(H\).:)', r'\1\n\t\t\t\tbreak;\n\2', c)
c = c.replace('if (true)\n\t\t\t\tif (!objectData.CanBeGivenAsGift)\n\t\t\t\t{\n\t\t\t\t\tvalue.Add(.not_giftable.);\n\t\t\t\t}\n\t\t\t\tbreak.', '')
with open('StardewValley/ItemContextTagManager.cs', 'w') as f: f.write(c)
"
sed -i 's/DontLoadDefaultSetting>/DontLoadDefaultSettingAttribute>/g' StardewValley/Options.cs
sed -i 's/ShowSkillMastery/null/g' StardewValley/GameLocation.cs
sed -i 's/null([0-9]*, new Vector2([^)]*));/{ }/g' StardewValley/GameLocation.cs
sed -i 's/!GameRunner.instance.Window.CenterOnDisplay/startupPreferences.displayIndex/' StardewValley.Menus/TitleMenu.cs 2>/dev/null
sed -i 's/cue\.Volume/cue.get_Volume()/g; s/cue\.Pitch/cue.get_Pitch()/g; s/cue\.IsPitchBeingControlledByRPC/cue.get_IsPitchBeingControlledByRPC()/g' StardewValley/CueWrapper.cs
sed -i 's/cue.get_Volume() = value;/cue.set_Volume(value);/' StardewValley/CueWrapper.cs
sed -i 's/cue.get_Pitch() = value;/cue.set_Pitch(value);/' StardewValley/CueWrapper.cs

echo "[+] Patches applied"
