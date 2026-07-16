using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: patch-sdv-noemit <input.dll> [output.dll]");
            Console.WriteLine("If output omitted, overwrites input.");
            return 1;
        }

        var inputPath = args[0];
        var outputPath = args.Length > 1 ? args[1] : inputPath;

        Console.WriteLine($"[+] Reading: {inputPath}");
        var asm = AssemblyDefinition.ReadAssembly(inputPath);

        int methodsNopped = 0;

        // LocalMultiplayer.GenerateDynamicMethodsForStatics() — uses Reflection.Emit
        // (DynamicMethod, ILGenerator) to generate static field accessors at runtime.
        // This throws PlatformNotSupportedException in WASM (no JIT).
        // Patch to NOP since the generated methods are only used for save serialization
        // which is not needed for the title screen demo.
        methodsNopped += NopMethod(asm, "StardewValley.LocalMultiplayer", "GenerateDynamicMethodsForStatics");

        Console.WriteLine($"[+] Methods NOP'd: {methodsNopped}");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        asm.Write(outputPath);
        Console.WriteLine($"[+] Written: {outputPath}");
        return 0;
    }

    static int NopMethod(AssemblyDefinition asm, string typeFullName, string methodName)
    {
        foreach (var module in asm.Modules)
        {
            var type = module.Types.FirstOrDefault(t => t.FullName == typeFullName);
            if (type == null) continue;
            var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
            if (method == null) continue;

            // Replace body with just 'ret'
            var instrs = method.Body.Instructions;
            instrs.Clear();
            method.Body.ExceptionHandlers.Clear();
            instrs.Add(Instruction.Create(OpCodes.Ret));
            method.Body.InitLocals = true;

            Console.WriteLine($"  [-] NOP'd {typeFullName}::{methodName}");
            return 1;
        }
        Console.WriteLine($"  [!] Method not found: {typeFullName}::{methodName}");
        return 0;
    }
}
