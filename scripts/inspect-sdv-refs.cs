// Inspect Stardew Valley.dll: AssemblyRefs + interesting type forwards/refs.
// Usage: dotnet script inspect-sdv-refs.cs -- <path-to-sdv.dll>
//
// Quick + dirty: build as a console exe via `dotnet run` from a temp project.
// Simpler: shell out to dotnet-cil-inspect. For now we just dump AssemblyRefs
// by reading PE metadata via Mono.Cecil.

#r "nuget: Mono.Cecil, 0.11.6"

using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

var path = args.Length > 0 ? args[0] : "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll";
Console.WriteLine($"[+] Loading: {path}");
Console.WriteLine($"    Size: {new FileInfo(path).Length:N0} bytes");

using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = false, InMemory = true });
Console.WriteLine($"[+] Assembly: {asm.FullName}");
Console.WriteLine($"[+] Target runtime: {asm.MainModule.Runtime}");
Console.WriteLine($"[+] Entry point: {(asm.EntryPoint == null ? "(none)" : asm.EntryPoint.DeclaringType.FullName + "::" + asm.EntryPoint.Name)}");

Console.WriteLine();
Console.WriteLine($"=== AssemblyRefs ({asm.MainModule.AssemblyReferences.Count}) ===");
foreach (var ar in asm.MainModule.AssemblyReferences)
    Console.WriteLine($"  - {ar.FullName}");

Console.WriteLine();
Console.WriteLine($"=== Module references ({asm.MainModule.ModuleReferences.Count}) ===");
foreach (var mr in asm.MainModule.ModuleReferences)
    Console.WriteLine($"  - {mr.Name}");

Console.WriteLine();
Console.WriteLine($"=== Top-level types count: {asm.MainModule.Types.Count} ===");
foreach (var t in asm.MainModule.Types.Take(20))
    Console.WriteLine($"  - {t.FullName}");
