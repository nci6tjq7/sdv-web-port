using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Patches KNI's Xna.Framework.Graphics.dll to fix Blazor.GL issues.
/// </summary>
public static class KniGraphicsPatcher
{
    /// <summary>
    /// Patch GraphicsAdapter.IsProfileSupported to always return true.
    /// KNI's Platform_IsProfileSupported creates an OffscreenCanvas which
    /// fails in Blazor.GL. We bypass the check.
    /// </summary>
    public static byte[] Patch(byte[] assemblyBytes)
    {
        Console.WriteLine("[KniGraphicsPatcher] Loading assembly (" + assemblyBytes.Length + " bytes)");
        using var inputMs = new MemoryStream(assemblyBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(inputMs, new ReaderParameters { InMemory = true });

        // Find GraphicsAdapter.IsProfileSupported
        var adapter = asmDef.MainModule.Types.FirstOrDefault(t => t.Name == "GraphicsAdapter");
        if (adapter == null)
        {
            Console.WriteLine("[KniGraphicsPatcher] GraphicsAdapter not found — skipping");
            using var passMs = new MemoryStream();
            asmDef.Write(passMs);
            return passMs.ToArray();
        }

        var method = adapter.Methods.FirstOrDefault(m => m.Name == "IsProfileSupported");
        if (method == null)
        {
            Console.WriteLine("[KniGraphicsPatcher] IsProfileSupported not found — skipping");
            using var passMs = new MemoryStream();
            asmDef.Write(passMs);
            return passMs.ToArray();
        }

        Console.WriteLine("[KniGraphicsPatcher] Patching IsProfileSupported → return true");
        var instrs = method.Body.Instructions;
        instrs.Clear();
        method.Body.ExceptionHandlers.Clear();

        // New body: return true
        // (pop the 'this' and 'profile' args, push true, ret)
        // Actually, IsProfileSupported is an instance method with 1 param (GraphicsProfile)
        // Stack on entry: [this] [profile]
        // We need to pop both and push true
        instrs.Add(Instruction.Create(OpCodes.Ldarg_1));  // load profile (to consume it)
        instrs.Add(Instruction.Create(OpCodes.Pop));       // pop it
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_1)); // push true
        instrs.Add(Instruction.Create(OpCodes.Ret));

        // Also patch Platform_IsProfileSupported (the virtual method that creates OffscreenCanvas)
        var platformMethod = adapter.Methods.FirstOrDefault(m => m.Name == "Platform_IsProfileSupported");
        if (platformMethod != null)
        {
            Console.WriteLine("[KniGraphicsPatcher] Patching Platform_IsProfileSupported → return true");
            var pInstrs = platformMethod.Body.Instructions;
            pInstrs.Clear();
            platformMethod.Body.ExceptionHandlers.Clear();
            pInstrs.Add(Instruction.Create(OpCodes.Ldc_I4_1)); // push true
            pInstrs.Add(Instruction.Create(OpCodes.Ret));
        }

        using var outputMs = new MemoryStream();
        asmDef.Write(outputMs);
        var result = outputMs.ToArray();
        Console.WriteLine("[KniGraphicsPatcher] Patched assembly: " + result.Length + " bytes");
        return result;
    }
}
