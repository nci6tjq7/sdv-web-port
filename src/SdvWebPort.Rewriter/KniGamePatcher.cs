using Mono.Cecil;
using System;
using System.IO;
using System.Linq;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Patches KNI's Xna.Framework.Game.dll to fix method accessibility.
/// KNI declares Game.Initialize, Game.UnloadContent, Game.Update, Game.Draw
/// as "protected internal" (familyOrAssembly), but SDV's GameRunner (in a
/// different assembly) needs to override them with just "protected".
/// This patcher changes those methods to "protected" (family only) so
/// SDV can override them.
/// </summary>
public static class KniGamePatcher
{
    public static byte[] Patch(byte[] assemblyBytes)
    {
        Console.WriteLine("[KniGamePatcher] Loading assembly (" + assemblyBytes.Length + " bytes)");
        using var inputMs = new MemoryStream(assemblyBytes);
        using var asmDef = AssemblyDefinition.ReadAssembly(inputMs, new ReaderParameters { InMemory = true });

        var gameType = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.Game");
        if (gameType == null)
        {
            Console.WriteLine("[KniGamePatcher] Game type not found — skipping");
            return assemblyBytes;
        }

        string[] methodsToFix = { "Initialize", "UnloadContent", "Update", "Draw" };
        int patched = 0;
        foreach (var name in methodsToFix)
        {
            var method = gameType.Methods.FirstOrDefault(m => m.Name == name);
            if (method == null)
            {
                Console.WriteLine($"[KniGamePatcher] {name} not found — skipping");
                continue;
            }

            // Change from "protected internal" (familyOrAssembly) to "protected" (family)
            if (method.IsFamilyOrAssembly)
            {
                method.IsFamilyOrAssembly = false;
                method.IsFamily = true;
                patched++;
                Console.WriteLine($"[KniGamePatcher] Game.{name}: protected internal → protected");
            }
        }

        Console.WriteLine($"[KniGamePatcher] Patched {patched} methods");

        using var outputMs = new MemoryStream();
        asmDef.Write(outputMs);
        var result = outputMs.ToArray();
        Console.WriteLine("[KniGamePatcher] Patched assembly: " + result.Length + " bytes");
        return result;
    }
}
