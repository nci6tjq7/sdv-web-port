// Bisect GameRunner..ctor() — patch out steps to find which causes the Mono assertion.
// Usage: dotnet run --project <this> -- <input.dll> <output.dll> <mode>
//   mode 0: no patching (control)
//   mode 1: patch out everything after Game..ctor() (IL_002D)
//   mode 2: patch out everything after GraphicsDeviceManager (IL_0056)
//   mode 3: patch out everything after LocalMultiplayer.Initialize (IL_00BC)
//   mode 4: patch out everything after ItemRegistry.RegisterItemTypes (IL_00C1)
//   mode 5: patch out everything after Window.AllowUserResizing (IL_00D7)
using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

var inputPath = args[0];
var outputPath = args[1];
var mode = int.Parse(args[2]);

using var asm = AssemblyDefinition.ReadAssembly(inputPath, new ReaderParameters { InMemory = true, ReadWrite = false });
var mod = asm.MainModule;

var gameRunner = mod.Types.FirstOrDefault(t => t.FullName == "StardewValley.GameRunner");
if (gameRunner == null) { Console.WriteLine("GameRunner not found"); return; }

var ctor = gameRunner.Methods.FirstOrDefault(m => m.Name == ".ctor");
if (ctor == null) { Console.WriteLine(".ctor not found"); return; }

Console.WriteLine($"=== Mode {mode}: patching GameRunner..ctor() ===");
Console.WriteLine($"Original IL: {ctor.Body.Instructions.Count} instructions");

// The IL offsets we want to patch to (everything after this point becomes ret):
// Mode 1: after Game..ctor() call (IL_002D) — keeps only List inits + base ctor
// Mode 2: after GraphicsDeviceManager + event setup (IL_00BC) — keeps through LocalMultiplayer.Initialize call site
// Mode 3: after LocalMultiplayer.Initialize (IL_00C1)
// Mode 4: after ItemRegistry.RegisterItemTypes (IL_00C6)
// Mode 5: after Window.AllowUserResizing (IL_00DC)

var patchAfterOffset = mode switch
{
    1 => 0x002D,  // after call Game::.ctor
    2 => 0x00BC,  // after call LocalMultiplayer::Initialize (just before it)
    3 => 0x00C1,  // after call LocalMultiplayer::Initialize
    4 => 0x00C6,  // after call ItemRegistry::RegisterItemTypes
    5 => 0x00DC,  // after set_AllowUserResizing
    _ => -1,      // no patching
};

if (patchAfterOffset >= 0)
{
    var instrs = ctor.Body.Instructions;
    var patchIndex = -1;
    for (int i = 0; i < instrs.Count; i++)
    {
        if (instrs[i].Offset >= patchAfterOffset)
        {
            patchIndex = i;
            break;
        }
    }

    if (patchIndex >= 0)
    {
        Console.WriteLine($"Patching: removing instructions from IL_{patchAfterOffset:X4} (index {patchIndex}) onwards");
        Console.WriteLine($"  First instruction to remove: {instrs[patchIndex].OpCode} {instrs[patchIndex].Operand}");

        // Remove all instructions from patchIndex onwards
        while (instrs.Count > patchIndex)
            instrs.RemoveAt(instrs.Count - 1);

        // Add ret
        instrs.Add(Instruction.Create(OpCodes.Ret));

        Console.WriteLine($"Patched IL: {instrs.Count} instructions");
    }
    else
    {
        Console.WriteLine($"WARN: no instruction found at offset IL_{patchAfterOffset:X4}");
    }
}

// Also need to fix the exception handlers if any reference removed instructions
ctor.Body.ExceptionHandlers.Clear();

// Write
using var outputMs = new MemoryStream();
asm.Write(outputMs);
File.WriteAllBytes(outputPath, outputMs.ToArray());
Console.WriteLine($"Wrote: {outputPath} ({outputMs.Length:N0} bytes)");
