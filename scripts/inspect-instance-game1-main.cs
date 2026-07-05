// Inspect InstanceGame + Game1 0-arg ctor + Program.Main + GameRunner usage.
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

var path = "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll";
using var asm = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { ReadWrite = false, InMemory = true });
var mod = asm.MainModule;

Console.WriteLine("=== InstanceGame type ===");
var instanceGame = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.InstanceGame");
if (instanceGame != null)
{
    Console.WriteLine($"Base: {instanceGame.BaseType?.FullName} (asm: {(instanceGame.BaseType as TypeReference)?.Module?.Assembly?.Name?.FullName})");
    var ctor = instanceGame.Methods.FirstOrDefault(m => m.Name == ".ctor");
    if (ctor != null)
    {
        Console.WriteLine($"=== InstanceGame..ctor() IL ({ctor.Body.Instructions.Count} instrs) ===");
        foreach (var ins in ctor.Body.Instructions)
            Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
    }
}

Console.WriteLine();
Console.WriteLine("=== Game1 0-arg ctor ===");
var game1 = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
var game1Ctor0 = game1?.Methods.FirstOrDefault(m => m.Name == ".ctor" && !m.HasParameters);
if (game1Ctor0 != null)
{
    Console.WriteLine($"=== Game1..ctor() IL ({game1Ctor0.Body.Instructions.Count} instrs) ===");
    foreach (var ins in game1Ctor0.Body.Instructions)
        Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
}

Console.WriteLine();
Console.WriteLine("=== Program.Main ===");
var program = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.Program");
var main = program?.Methods.FirstOrDefault(m => m.Name == "Main");
if (main != null)
{
    Console.WriteLine($"=== Program.Main IL ({main.Body.Instructions.Count} instrs) ===");
    foreach (var ins in main.Body.Instructions)
        Console.WriteLine($"  IL_{ins.Offset:X4}: {ins.OpCode} {FormatOperand(ins.Operand)}");
}

Console.WriteLine();
Console.WriteLine("=== Where is GameRunner referenced? ===");
int refs = 0;
foreach (var t in mod.Types)
    foreach (var m in t.Methods)
        if (m.HasBody)
            foreach (var ins in m.Body.Instructions)
                if (ins.Operand is TypeReference tr && tr.FullName.Contains("GameRunner"))
                {
                    Console.WriteLine($"  - {t.FullName}::{m.Name} IL_{ins.Offset:X4}: {ins.OpCode} {tr.FullName}");
                    refs++;
                }
                else if (ins.Operand is MethodReference mr && mr.DeclaringType.FullName.Contains("GameRunner"))
                {
                    if (refs < 30)
                        Console.WriteLine($"  - {t.FullName}::{m.Name} IL_{ins.Offset:X4}: {ins.OpCode} {mr.DeclaringType.FullName}::{mr.Name}");
                    refs++;
                }
Console.WriteLine($"Total GameRunner refs: {refs}");

string FormatOperand(object operand) => operand switch
{
    null => "",
    MethodReference mr => $"{mr.DeclaringType.FullName}::{mr.Name}",
    TypeReference tr => tr.FullName,
    FieldReference fr => $"{fr.DeclaringType.FullName}::{fr.Name}",
    Instruction ins => $"IL_{ins.Offset:X4}",
    _ => operand.ToString()!,
};
