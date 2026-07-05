// Inspect Game1 base type + ctor + Program.cctor to understand init flow.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

var path = "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll";
using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = false, InMemory = true });
var mod = asm.MainModule;

Console.WriteLine("=== Game1 type ===");
var game1 = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
if (game1 != null)
{
    Console.WriteLine($"Found: {game1.FullName}");
    Console.WriteLine($"Base: {game1.BaseType?.FullName} (asm: {(game1.BaseType as TypeReference)?.Module?.Assembly?.Name?.FullName})");
    Console.WriteLine($"Interfaces: {string.Join(", ", game1.Interfaces.Select(i => i.InterfaceType.FullName))}");
    Console.WriteLine($"Methods count: {game1.Methods.Count}");
    var ctor = game1.Methods.FirstOrDefault(m => m.Name == ".ctor");
    if (ctor != null)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Game1..ctor() IL ({ctor.Body.Instructions.Count} instrs) ===");
        foreach (var ins in ctor.Body.Instructions)
            Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
    }
    else
    {
        Console.WriteLine("Game1 has no .ctor");
    }
}

Console.WriteLine();
Console.WriteLine("=== Program..cctor (static constructor) ===");
var program = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.Program");
if (program != null)
{
    var cctor = program.Methods.FirstOrDefault(m => m.Name == ".cctor");
    if (cctor != null)
    {
        Console.WriteLine($"Instructions: {cctor.Body.Instructions.Count}");
        foreach (var ins in cctor.Body.Instructions)
            Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Program.get_sdk IL ===");
var getSdk = program?.Methods.FirstOrDefault(m => m.Name == "get_sdk");
if (getSdk != null)
{
    foreach (var ins in getSdk.Body.Instructions)
        Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
}

Console.WriteLine();
Console.WriteLine("=== SDKHelper type ===");
var sdkHelper = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.SDKs.SDKHelper");
if (sdkHelper != null)
{
    Console.WriteLine($"Found: {sdkHelper.FullName}");
    Console.WriteLine($"Methods: {sdkHelper.Methods.Count}");
    foreach (var m in sdkHelper.Methods.Take(20))
        Console.WriteLine($"  - {m.FullName} (abstract={m.IsAbstract})");
}

string FormatOperand(object operand) => operand switch
{
    null => "",
    MethodReference mr => $"{mr.DeclaringType.FullName}::{mr.Name}",
    TypeReference tr => tr.FullName,
    FieldReference fr => $"{fr.DeclaringType.FullName}::{fr.Name}",
    Instruction ins => $"IL_{ins.Offset:X4}",
    _ => operand.ToString()!,
};
