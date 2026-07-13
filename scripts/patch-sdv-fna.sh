#!/usr/bin/env bash
set -e
SRC_DIR="${1:-/tmp/sdv-fna-src}"
SDV_DIR="/tmp/sdv-extract/Stardew Valley"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "[+] Applying FNA compat patches to $SRC_DIR (script dir: ${SCRIPT_DIR})"

# Defensive cleanup: replace broken ILSpy 8.2 patterns:
#   ((Type)(ref EXPR))            -> EXPR
#   ((Type1)(Type2)(ref EXPR))    -> EXPR   (nested casts)
#   ((??)EXPR) ?? Y               -> EXPR ?? Y
# These are invalid C# syntax emitted for constrained callvirt on value
# types and nullable coalescing on value types.
echo "[+] Cleaning up broken ILSpy patterns..."
python3 << PYEOF
import os, re
# Match ((Type)(ref EXPR)) or ((Type1)(Type2)(ref EXPR)) — one outer paren,
# then one or more (Type) cast groups, then (ref EXPR), then outer close.
# EXPR can be a variable, dotted member access, array index, or method call
# (allowing one level of nested parens for method arguments).
REF_PAT = re.compile(r'\((?:\([A-Za-z_][A-Za-z0-9_.]*\))+(?:\(ref\s+([^()]*(?:\([^()]*\)[^()]*)*?)\))\)')
NULL_PAT = re.compile(r'\(\(\?\?\)([^)]+?)\)')
# Match start of VAR..ctor( - we'll find the matching ) by counting parens
CTOR_START_PAT = re.compile(r'(\b[A-Za-z_]\w*)\s*\.\.ctor\s*\(')
fixed = 0
files_fixed = 0
for root, dirs, files in os.walk('${SRC_DIR}'):
    for fn in files:
        if not fn.endswith('.cs'):
            continue
        p = os.path.join(root, fn)
        with open(p, 'r', encoding='utf-8', errors='replace') as f:
            c = f.read()
        # Apply REF_PAT and NULL_PAT iteratively
        total = 0
        while True:
            new_c, n1 = REF_PAT.subn(r'\1', c)
            new_c, n2 = NULL_PAT.subn(r'\1', new_c)
            c = new_c
            total += n1 + n2
            if n1 + n2 == 0:
                break
        # Now apply CTOR cleanup with proper paren matching
        # We process matches in reverse order so positions don't shift
        matches = list(CTOR_START_PAT.finditer(c))
        if matches:
            # Process in reverse order
            replacements = []
            for m in matches:
                var = m.group(1)
                # Find matching ) by counting parens
                start = m.end() - 1  # position of opening (
                depth = 0
                end = -1
                i = start
                while i < len(c):
                    if c[i] == '(':
                        depth += 1
                    elif c[i] == ')':
                        depth -= 1
                        if depth == 0:
                            end = i
                            break
                    i += 1
                if end == -1:
                    continue  # Unbalanced; skip
                # Extract args (between start+1 and end)
                args = c[start+1:end]
                # Look for ';' after end
                after = c[end+1:].lstrip()
                if not after.startswith(';'):
                    continue  # Not a statement; skip
                # Find declaration of `var` before this match
                decl_pat = re.compile(r'\b([A-Z][A-Za-z0-9_]*(?:\.[A-Z][A-Za-z0-9_]*)*(?:<[^>]+>)?)\s+' + re.escape(var) + r'\s*(?:=|;)')
                before = c[:m.start()]
                decl_match = None
                for dm in re.finditer(decl_pat, before):
                    decl_match = dm
                if decl_match:
                    var_type = decl_match.group(1)
                    new_text = f'{var} = new {var_type}({args});'
                else:
                    new_text = f'/* {var}..ctor({args}); -- type not inferred */'
                # Save replacement range
                stmt_end = end + 1 + (len(c[end+1:]) - len(after)) + 1  # include ';'
                replacements.append((m.start(), stmt_end, new_text))
            # Apply replacements in reverse order
            for start, end, new_text in reversed(replacements):
                c = c[:start] + new_text + c[end:]
                total += 1
        if total > 0:
            with open(p, 'w', encoding='utf-8') as f:
                f.write(c)
            fixed += total
            files_fixed += 1
print(f"  Cleaned {fixed} occurrences in {files_fixed} files")
PYEOF

# Normalise directory layout: nested (StardewValley/Internal/X.cs) is the
# default for our custom decompiler. The patches below use the nested layout.
# If the decompiler output is flat (StardewValley.Internal/X.cs), bail out.
if [ ! -d "$SRC_DIR/StardewValley/Internal" ] && [ -d "$SRC_DIR/StardewValley.Internal" ]; then
  echo "[!] Detected flat layout (StardewValley.Internal/). Convert to nested layout..." >&2
  # Move StardewValley.X -> StardewValley/X for all top-level StardewValley.* dirs
  for d in "$SRC_DIR"/StardewValley.*; do
    [ -d "$d" ] || continue
    base=$(basename "$d")
    sub="${base#StardewValley.}"
    mkdir -p "$SRC_DIR/StardewValley/$sub"
    cp -a "$d/." "$SRC_DIR/StardewValley/$sub/"
    rm -rf "$d"
  done
fi

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

# GlobalUsings.cs: disambiguate types that exist in both Microsoft.Xna.Framework.* and xTile.Dimensions.
# ILSpy often drops fully-qualified names, causing CS0104 (ambiguous reference) errors.
# Default to the XNA types since SDV uses them ~95% of the time.
cat > "$SRC_DIR/GlobalUsings.cs" << 'EOF'
// Disambiguate types that appear in both Microsoft.Xna.Framework.* and xTile.Dimensions.
// ILSpy sometimes drops fully-qualified names; these aliases default to XNA types.
global using Rectangle = Microsoft.Xna.Framework.Rectangle;
global using Color = Microsoft.Xna.Framework.Color;
global using Vector2 = Microsoft.Xna.Framework.Vector2;
global using Vector3 = Microsoft.Xna.Framework.Vector3;
global using Vector4 = Microsoft.Xna.Framework.Vector4;
global using Point = Microsoft.Xna.Framework.Point;
EOF

# Also add per-file using aliases to files that have 'using xTile.Dimensions;'
# (global using should take precedence, but if it doesn't, per-file aliases definitely do)
echo "[+] Adding per-file using aliases to files with 'using xTile.Dimensions;'..."
python3 << PYEOF
import os, re
ALIASES = """using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Color = Microsoft.Xna.Framework.Color;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Point = Microsoft.Xna.Framework.Point;"""
fixed = 0
for root, dirs, files in os.walk('${SRC_DIR}'):
    for fn in files:
        if not fn.endswith('.cs'): continue
        p = os.path.join(root, fn)
        with open(p, 'r', encoding='utf-8', errors='replace') as f:
            c = f.read()
        # If file has 'using xTile.Dimensions;' and doesn't already have the alias
        if 'using xTile.Dimensions;' in c and 'using Rectangle = Microsoft.Xna.Framework.Rectangle;' not in c:
            # Insert the aliases right after the last 'using ...;' line at the top
            # Find the position right after the namespace declaration if file-scoped
            m = re.search(r'^(namespace [^;]+;)\s*\n', c, re.MULTILINE)
            if m:
                # File-scoped namespace - insert after namespace
                pos = m.end()
                c = c[:pos] + ALIASES + '\n' + c[pos:]
            else:
                # Insert after the last 'using ...;' at the start
                usings = list(re.finditer(r'^using [^;]+;\s*$', c, re.MULTILINE))
                if usings:
                    pos = usings[-1].end()
                    c = c[:pos] + '\n' + ALIASES + c[pos:]
            with open(p, 'w', encoding='utf-8') as f:
                f.write(c)
            fixed += 1
print(f"  Added aliases to {fixed} files")
PYEOF

# FnaCompat.cs
cp "${SCRIPT_DIR}/FnaCompat.cs" "$SRC_DIR/FnaCompat.cs"

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

# Apply source fixes (paths use nested layout: StardewValley/Internal/X.cs)
cd "$SRC_DIR"

# Fix ForEachItemHelper.cs: replace broken <>c__DisplayClass4_0 compiler-generated
# identifier with a valid name. The class declaration and all casts use the same
# name, so a global replace in this file is safe.
python3 << 'PYEOF'
p = 'StardewValley/Internal/ForEachItemHelper.cs'
with open(p, 'r') as f: c = f.read()
# Replace the compiler-generated class name with a valid identifier.
# '<>c__DisplayClass4_0' -> '_c__DisplayClass4_0'
c = c.replace('<>c__DisplayClass4_0', '_c__DisplayClass4_0')
# Also fix the GetPath() method body that uses CombinePath with this-casts.
# Replace the entire return statement with a simple empty list (the path
# is only used for debug logging in single-player).
import re
c = re.sub(
    r'return CombinePath \([^;]+;',
    'return new System.Collections.Generic.List<object>();',
    c
)
with open(p, 'w') as f: f.write(c)
print(f"  Fixed {p}")
PYEOF

# Fix NPC.cs: broken '(?.X)' pattern from ILSpy's null-conditional decompilation.
# Example: 'GetData (?.MugShotSourceRect)' should be 'GetData ()?.MugShotSourceRect'
python3 << 'PYEOF'
import re
p = 'StardewValley/NPC.cs'
with open(p, 'r') as f: c = f.read()
# Pattern: 'MethodName (?.X)' -> 'MethodName ()?.X'
c, n = re.subn(r'(\w+)\s*\(\?\.\s*([^)]+)\)', r'\1 ()?.\2', c)
with open(p, 'w') as f: f.write(c)
if n > 0: print(f"  Fixed {n} (?.X) pattern(s) in {p}")
PYEOF

# Fix Preconditions.cs: broken '? val;' type-less variable declaration.
# ILSpy sometimes emits this when it can't infer the type. Since the variable
# is later cast with (Point)val, 'object' is a safe fallback type.
python3 << 'PYEOF'
import re
p = 'StardewValley/Preconditions.cs'
with open(p, 'r') as f: c = f.read()
# Replace '\t? val;' or '    ? val;' with '\tobject val;'
c, n = re.subn(r'^([ \t]+)\?\s+(\w+)\s*;', r'\1object \2;', c, flags=re.MULTILINE)
with open(p, 'w') as f: f.write(c)
if n > 0: print(f"  Fixed {n} '? var;' pattern(s) in {p}")
PYEOF

sed -i '1i using StardewValley.Internal;' StardewValley/SaveMigrations/SaveMigrator_1_6.cs
sed -i 's/Utility.ForEachItemContext(HandleItem)/Utility.ForEachItemContext((in ForEachItemContext context) => true)/' StardewValley/SaveMigrations/SaveMigrator_1_6.cs
sed -i '/TextureTuckAmount/d' StardewValley/GameRunner.cs
sed -i 's/soundEffect = new OggStreamSoundEffect(filePath)/soundEffect = null; \/\/ WASM/' StardewValley/Audio/AudioCueModificationManager.cs
sed -i 's/SoundEffect\.FromStream(stream, flag2)/SoundEffect.FromStream(stream)/' StardewValley/Audio/AudioCueModificationManager.cs
sed -i 's/, SurfaceFormat.Color)/)/g' StardewValley/Game1.cs StardewValley/DebugMetricsComponent.cs
sed -i 's/\.ActualWidth/.Width/g; s/\.ActualHeight/.Height/g' StardewValley/Extensions/FrameworkExtensions.cs
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
sed -i 's/RequestLock(ContinueDemolish, BuildingLockFailed)/RequestLock(() => {}, BuildingLockFailed)/' StardewValley/Menus/CarpenterMenu.cs
python3 -c "
with open('StardewValley/Menus/EmoteMenu.cs', 'r') as f: c = f.read()
old = 'Vector2.Dot(value2: new Vector2((float)_emoteButtons[i].bounds.Center.X - ((float)xPositionOnScreen + (float)width / 2f), (float)_emoteButtons[i].bounds.Center.Y - ((float)yPositionOnScreen + (float)height / 2f)), value1: value)'
new = 'Vector2.Dot(value, new Vector2((float)_emoteButtons[i].bounds.Center.X - ((float)xPositionOnScreen + (float)width / 2f), (float)_emoteButtons[i].bounds.Center.Y - ((float)yPositionOnScreen + (float)height / 2f)))'
c = c.replace(old, new)
with open('StardewValley/Menus/EmoteMenu.cs', 'w') as f: f.write(c)
"
sed -i 's/value\.Length > 0/value.ToString().Length > 0/' StardewValley/GameLocation.cs
sed -i '1i using xTile.ObjectModel;' StardewValley/GameLocation.cs StardewValley/InteriorDoor.cs StardewValley/Pathfinding/PathFindController.cs 2>/dev/null
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
sed -i 's/!GameRunner.instance.Window.CenterOnDisplay/startupPreferences.displayIndex/' StardewValley/Menus/TitleMenu.cs 2>/dev/null
sed -i 's/cue\.Volume/cue.get_Volume()/g; s/cue\.Pitch/cue.get_Pitch()/g; s/cue\.IsPitchBeingControlledByRPC/cue.get_IsPitchBeingControlledByRPC()/g' StardewValley/CueWrapper.cs
sed -i 's/cue.get_Volume() = value;/cue.set_Volume(value);/' StardewValley/CueWrapper.cs
sed -i 's/cue.get_Pitch() = value;/cue.set_Pitch(value);/' StardewValley/CueWrapper.cs

# Fix FNA Buttons enum bitwise operators (FNA's Buttons doesn't have [Flags] attribute,
# so | and & with int literals fail with CS0019).
# Pattern: (Buttons)(VAR | 0xNNN) → (Buttons)((int)VAR | 0xNNN)
# Pattern: (VAR & 0xNNN) where VAR is Buttons → ((int)VAR & 0xNNN)
# Only apply to known files to avoid breaking other code.
for f in StardewValley/ButtonCollection.cs; do
  if [ -f "$f" ]; then
    # Fix: (Buttons)(_pressed | 0xNNN) → (Buttons)((int)_pressed | 0xNNN)
    sed -i -E 's/\(Buttons\)\((_pressed|oldPadState\.\w+|padState\.\w+)\s*\|\s*(0x[0-9A-Fa-f]+|[0-9]+)\)/(Buttons)((int)\1 | \2)/g' "$f"
    # Fix: (_pressed & (1 << N)) → ((int)_pressed & (1 << N))
    sed -i -E 's/\((_pressed|oldPadState\.\w+|padState\.\w+)\s*&\s*\(([^)]+)\)/((int)\1 \& (\2)/g' "$f"
  fi
done

# Fix (SpriteEffects)(bool_expr) → (SpriteEffects)((bool_expr) ? 1 : 0)
# FNA's SpriteEffects enum can't be cast from bool directly.
python3 << 'PYEOF'
import os, re
# Match (SpriteEffects)(BOOLEAN_EXPR) where BOOLEAN_EXPR contains comparison operators
# This is risky - only do it for specific patterns we've seen
PAT = re.compile(r'\(SpriteEffects\)\(([^()]*[<>!=]=?[^()]*)\)')
fixed = 0
for root, dirs, files in os.walk('.'):
    for fn in files:
        if not fn.endswith('.cs'): continue
        p = os.path.join(root, fn)
        with open(p, 'r', encoding='utf-8', errors='replace') as f: c = f.read()
        new_c, n = PAT.subn(r'(SpriteEffects)((\1) ? 1 : 0)', c)
        if n > 0:
            with open(p, 'w', encoding='utf-8') as f: f.write(new_c)
            fixed += n
print(f"  SpriteEffects bool cast: fixed {fixed}")
PYEOF

# Fix AudioCueModificationManager: SoundEffect.FromStream with 2 args
# FNA only has FromStream(Stream), not FromStream(Stream, bool)
if [ -f "StardewValley/Audio/AudioCueModificationManager.cs" ]; then
  sed -i 's/SoundEffect\.FromStream(stream, flag2)/SoundEffect.FromStream(stream)/g' StardewValley/Audio/AudioCueModificationManager.cs
  sed -i 's/soundEffect = new OggStreamSoundEffect(filePath)/soundEffect = null; \/\/ WASM: OggStream not available/g' StardewValley/Audio/AudioCueModificationManager.cs
  # Also fix the cast issue: (SoundEffect)OggStreamSoundEffect is invalid
  sed -i 's/(SoundEffect)soundEffect/soundEffect/g' StardewValley/Audio/AudioCueModificationManager.cs 2>/dev/null
fi

echo "[+] Patches applied"
