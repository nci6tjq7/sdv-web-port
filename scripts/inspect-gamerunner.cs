// Inspect SDV GameRunner..ctor() IL + P/Invoke sites + Program class shape.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

var path = args.Length > 0 ? args[0] : "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll";
using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = false, InMemory = true });
var mod = asm.MainModule;

Console.WriteLine("=== GameRunner type ===");
var gameRunner = mod.Types.FirstOrDefault(t => t.Name == "GameRunner");
if (gameRunner == null)
{
    Console.WriteLine("GameRunner not found at top level — searching nested...");
    foreach (var t in mod.Types)
        foreach (var nt in t.NestedTypes)
            if (nt.Name == "GameRunner")
                gameRunner = nt;
}
if (gameRunner != null)
{
    Console.WriteLine($"Found: {gameRunner.FullName}");
    Console.WriteLine($"Base: {gameRunner.BaseType?.FullName}");
    Console.WriteLine($"Methods: {gameRunner.Methods.Count}");
    var ctor = gameRunner.Methods.FirstOrDefault(m => m.Name == ".ctor");
    if (ctor != null)
    {
        Console.WriteLine();
        Console.WriteLine($"=== GameRunner..ctor() IL ({ctor.Body.Instructions.Count} instrs) ===");
        foreach (var ins in ctor.Body.Instructions)
            Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
    }
    else
    {
        Console.WriteLine("No .ctor found");
    }
}
else
{
    Console.WriteLine("GameRunner not found anywhere");
    Console.WriteLine("Top-level types containing 'Runner':");
    foreach (var t in mod.Types.Where(t => t.Name.Contains("Runner") || t.Name.Contains("Game1")))
        Console.WriteLine($"  - {t.FullName}");
}

Console.WriteLine();
Console.WriteLine("=== Program type ===");
var program = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.Program");
if (program != null)
{
    Console.WriteLine($"Found: {program.FullName}");
    Console.WriteLine($"Fields:");
    foreach (var f in program.Fields)
        Console.WriteLine($"  - {f.FieldType.FullName} {f.Name} (static={f.IsStatic})");
    Console.WriteLine($"Methods:");
    foreach (var m in program.Methods.Where(m => m.IsStatic || m.Name == ".cctor"))
        Console.WriteLine($"  - {m.FullName}");
}

Console.WriteLine();
Console.WriteLine("=== P/Invoke sites ===");
var pinvokes = new System.Collections.Generic.HashSet<string>();
foreach (var t in mod.Types)
    foreach (var m in t.Methods)
        if (m.PInvokeInfo != null)
            pinvokes.Add($"{m.PInvokeInfo.Module.Name}!{t.FullName}::{m.Name}");
foreach (var p in pinvokes.OrderBy(x => x))
    Console.WriteLine($"  - {p}");
Console.WriteLine($"Total P/Invoke sites: {pinvokes.Count}");

string FormatOperand(object operand) => operand switch
{
    null => "",
    MethodReference mr => $"{mr.DeclaringType.FullName}::{mr.Name}",
    TypeReference tr => tr.FullName,
    FieldReference fr => $"{fr.DeclaringType.FullName}::{fr.Name}",
    Instruction ins => $"IL_{ins.Offset:X4}",
    _ => operand.ToString()!,
};
