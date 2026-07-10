#!/usr/bin/env bash
# Patch KNI's Xna.Framework.Graphics.dll to fix IsProfileSupported before AOT compilation.
# This runs as a pre-build step to modify the NuGet package DLL in-place.
set -euo pipefail
export PATH="/home/z/.dotnet:$PATH"
export DOTNET_ROOT="/home/z/.dotnet"

GRAPHICS_DLL=$(find /home/z/.nuget/packages/nkast.xna.framework.graphics -name "Xna.Framework.Graphics.dll" -path "*/net8.0/*" 2>/dev/null | head -1)
if [ -z "$GRAPHICS_DLL" ]; then
  echo "[patch-kni-graphics] Xna.Framework.Graphics.dll not found — skipping"
  exit 0
fi

echo "[patch-kni-graphics] Patching: $GRAPHICS_DLL"

# Use dotnet script to patch via Cecil
cat > /tmp/patch-kni.cs << 'CSHARP'
using System;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

var path = args[0];
var bytes = File.ReadAllBytes(path);
using var ms = new MemoryStream(bytes);
using var asm = AssemblyDefinition.ReadAssembly(ms);

var adapter = asm.MainModule.Types.FirstOrDefault(t => t.Name == "GraphicsAdapter");
if (adapter == null) { Console.WriteLine("GraphicsAdapter not found"); return; }

// Patch IsProfileSupported → return true
var method = adapter.Methods.FirstOrDefault(m => m.Name == "IsProfileSupported");
if (method != null)
{
    var instrs = method.Body.Instructions;
    instrs.Clear();
    method.Body.ExceptionHandlers.Clear();
    instrs.Add(Instruction.Create(OpCodes.Ldarg_1));
    instrs.Add(Instruction.Create(OpCodes.Pop));
    instrs.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    instrs.Add(Instruction.Create(OpCodes.Ret));
    Console.WriteLine("Patched IsProfileSupported → return true");
}

// Patch Platform_IsProfileSupported → return true
var platformMethod = adapter.Methods.FirstOrDefault(m => m.Name == "Platform_IsProfileSupported");
if (platformMethod != null)
{
    var pInstrs = platformMethod.Body.Instructions;
    pInstrs.Clear();
    platformMethod.Body.ExceptionHandlers.Clear();
    pInstrs.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    pInstrs.Add(Instruction.Create(OpCodes.Ret));
    Console.WriteLine("Patched Platform_IsProfileSupported → return true");
}

using var outMs = new MemoryStream();
asm.Write(outMs);
File.WriteAllBytes(path, outMs.ToArray());
Console.WriteLine($"Written: {path} ({outMs.Length} bytes)");
CSHARP

# Create a temp project to run the patch
mkdir -p /tmp/kni-patcher
cat > /tmp/kni-patcher/kni-patcher.csproj << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Mono.Cecil" Version="0.11.6" />
  </ItemGroup>
</Project>
PROJ
cp /tmp/patch-kni.cs /tmp/kni-patcher/Program.cs

dotnet run --project /tmp/kni-patcher "$GRAPHICS_DLL" 2>&1
