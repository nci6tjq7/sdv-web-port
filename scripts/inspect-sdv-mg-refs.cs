using System;
using Mono.Cecil;

var path = "/tmp/sdv-blazor-publish/wwwroot/Stardew Valley.dll";
using var ad = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { InMemory = true, ReadWrite = false });
Console.WriteLine($"Assembly: {ad.FullName}");
Console.WriteLine($"=== AssemblyRefs ({ad.MainModule.AssemblyReferences.Count}) ===");
foreach (var ar in ad.MainModule.AssemblyReferences)
{
    if (ar.Name.Contains("MonoGame") || ar.Name.Contains("Xna"))
        Console.WriteLine($"  - {ar.FullName}");
}
Console.WriteLine();
Console.WriteLine("=== TypeRefs with MonoGame scope ===");
foreach (var tr in ad.MainModule.GetTypeReferences())
{
    if (tr.Scope is AssemblyNameReference anr && anr.Name.Contains("MonoGame"))
    {
        Console.WriteLine($"  - {tr.FullName} (scope: {anr.FullName})");
    }
}
