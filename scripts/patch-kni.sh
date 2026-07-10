#!/usr/bin/env bash
# Patch KNI DLLs before AOT compilation:
# 1. Xna.Framework.Graphics.dll: IsProfileSupported → return true
# 2. Xna.Framework.Content.dll: TitleContainer.OpenStream → SdvFileShim.TitleContainerOpenStream
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$(dirname "$SCRIPT_DIR")"

echo "[patch-kni] Restoring NuGet packages..."
dotnet restore "$PROJECT_DIR/src/SdvWebPort.PoC.SdvBlazor/SdvWebPort.PoC.SdvBlazor.csproj" 2>/dev/null || true

# Patch Graphics DLL
GRAPHICS_DLL=$(find ~/.nuget/packages/nkast.xna.framework.graphics -name "Xna.Framework.Graphics.dll" -path "*/net8.0/*" 2>/dev/null | head -1)
if [ -n "$GRAPHICS_DLL" ]; then
  echo "[patch-kni] Patching Graphics: $GRAPHICS_DLL"
  # Use the existing SdvWebPort.Rewriter project to run KniGraphicsPatcher
  dotnet run --project "$PROJECT_DIR/src/SdvWebPort.Rewriter" --no-build -- patch-kni-graphics "$GRAPHICS_DLL" 2>/dev/null || \
  bash "$SCRIPT_DIR/patch-kni-graphics.sh" 2>/dev/null || true
else
  echo "[patch-kni] Xna.Framework.Graphics.dll not found — skipping"
fi

# Patch Content DLL using SdvFileSystemRewriter
CONTENT_DLL=$(find ~/.nuget/packages/nkast.xna.framework.content -name "Xna.Framework.Content.dll" -path "*/net8.0/*" 2>/dev/null | head -1)
if [ -n "$CONTENT_DLL" ]; then
  echo "[patch-kni] Patching Content (FS redirect): $CONTENT_DLL"
  # Build and run a small patcher that uses SdvFileSystemRewriter
  mkdir -p /tmp/kni-content-patcher
  cat > /tmp/kni-content-patcher/Program.cs << 'CSHARP'
using System;
using System.IO;
using System.Linq;
var path = args[0];
var bytes = File.ReadAllBytes(path);
var rewritten = SdvWebPort.Rewriter.SdvFileSystemRewriter.Rewrite(bytes);
File.WriteAllBytes(path, rewritten);
Console.WriteLine($"[Content Patcher] FS-rewritten: {path} ({rewritten.Length} bytes)");
CSHARP
  cat > /tmp/kni-content-patcher/kni-content-patcher.csproj << 'PROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="PROJECT_DIR/src/SdvWebPort.Rewriter/SdvWebPort.Rewriter.csproj" />
    <PackageReference Include="Mono.Cecil" Version="0.11.6" />
  </ItemGroup>
</Project>
PROJ
  sed -i "s|PROJECT_DIR|$PROJECT_DIR|" /tmp/kni-content-patcher/kni-content-patcher.csproj
  dotnet run --project /tmp/kni-content-patcher "$CONTENT_DLL" 2>&1
else
  echo "[patch-kni] Xna.Framework.Content.dll not found — skipping"
fi

echo "[patch-kni] Done."
