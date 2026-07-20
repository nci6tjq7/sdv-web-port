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
            Console.WriteLine("Usage: patch-fna-emscripten-mainloop <FNA.dll>");
            return 1;
        }

        var fnaPath = args[0];
        Console.WriteLine($"[+] Reading FNA: {fnaPath}");
        var fnaBytes = File.ReadAllBytes(fnaPath);
        var resolver = new DefaultAssemblyResolver();
        var fnaDir = Path.GetDirectoryName(fnaPath);
        if (!string.IsNullOrEmpty(fnaDir))
        {
            resolver.AddSearchDirectory(fnaDir);
        }
        var fnaAsm = AssemblyDefinition.ReadAssembly(new MemoryStream(fnaBytes), new ReaderParameters
        {
            AssemblyResolver = resolver,
        });

        var module = fnaAsm.MainModule;

        // Find SDL3_FNAPlatform type
        var platformType = module.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.SDL3_FNAPlatform");
        if (platformType == null)
        {
            Console.WriteLine("[!] SDL3_FNAPlatform type not found!");
            return 1;
        }

        // Find RunPlatformMainLoop method
        var runMainLoopMethod = platformType.Methods.FirstOrDefault(m => m.Name == "RunPlatformMainLoop");
        if (runMainLoopMethod == null || runMainLoopMethod.Body == null)
        {
            Console.WriteLine("[!] RunPlatformMainLoop method not found!");
            return 1;
        }
        Console.WriteLine($"[i] Found RunPlatformMainLoop: {runMainLoopMethod.Body.Instructions.Count} instructions");

        // Find Game type (for RunLoop, RunOneFrame)
        var gameType = module.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.Game");
        if (gameType == null)
        {
            Console.WriteLine("[!] Game type not found!");
            return 1;
        }

        // Find RunLoop method on Game
        var runLoopMethod = gameType.Methods.FirstOrDefault(m => m.Name == "RunLoop");
        if (runLoopMethod == null || runLoopMethod.Body == null)
        {
            Console.WriteLine("[!] RunLoop method not found!");
            return 1;
        }
        Console.WriteLine($"[i] Found RunLoop: {runLoopMethod.Body.Instructions.Count} instructions");

        // Find emscriptenGame field (static, on SDL3_FNAPlatform)
        var gameField = platformType.Fields.FirstOrDefault(f => f.Name == "emscriptenGame");
        if (gameField == null)
        {
            Console.WriteLine("[!] emscriptenGame field not found!");
            return 1;
        }

        var stringType = module.TypeSystem.String;
        var voidType = module.TypeSystem.Void;
        var int32Type = module.TypeSystem.Int32;
        var boolType = module.TypeSystem.Boolean;

        // Find Console.WriteLine
        TypeReference consoleType = null;
        foreach (var tr in module.GetTypeReferences())
        {
            if (tr.FullName == "System.Console")
            {
                consoleType = tr;
                break;
            }
        }
        if (consoleType == null)
        {
            consoleType = new TypeReference("System", "Console", module, module.TypeSystem.CoreLibrary);
        }
        var writeLineRef = new MethodReference("WriteLine", voidType, consoleType)
        {
            HasThis = false,
        };
        writeLineRef.Parameters.Add(new ParameterDefinition(stringType));

        // Find Thread.Sleep(int)
        TypeReference threadType = null;
        foreach (var tr in module.GetTypeReferences())
        {
            if (tr.FullName == "System.Threading.Thread")
            {
                threadType = tr;
                break;
            }
        }
        if (threadType == null)
        {
            threadType = new TypeReference("System.Threading", "Thread", module, module.TypeSystem.CoreLibrary);
        }
        var sleepRef = new MethodReference("Sleep", voidType, threadType)
        {
            HasThis = false,
        };
        sleepRef.Parameters.Add(new ParameterDefinition(int32Type));

        // Find Game.RunOneFrame method reference
        var runOneFrameRef = new MethodReference("RunOneFrame", voidType, gameType)
        {
            HasThis = true,
        };

        // === Patch RunPlatformMainLoop ===
        // C# drives the main loop itself (no JS callback needed).
        //
        // New body:
        //   Console.WriteLine("[PATCH] RunPlatformMainLoop — C# driven loop")
        //   emscriptenGame = game
        //   Loop:
        //     if (emscriptenGame == null) goto EndLoop
        //     emscriptenGame.RunOneFrame()
        //     Thread.Sleep(0)  // yield to other threads/JS
        //     goto Loop
        //   EndLoop:
        //     ret
        //
        // This blocks the C# main thread (deputy worker) forever,
        // running RunOneFrame each iteration. Thread.Sleep(0) yields
        // so the worker doesn't monopolize the CPU.
        //
        // The canvas was transferred to the worker via the celeste-wasm
        // sed patch, so WebGL calls happen on the worker. This is fine.
        //
        // JS doesn't need to call into C# — the loop is entirely C#-driven.
        var instrs = runMainLoopMethod.Body.Instructions;
        instrs.Clear();
        runMainLoopMethod.Body.ExceptionHandlers.Clear();

        // ldstr; call Console.WriteLine
        instrs.Add(Instruction.Create(OpCodes.Ldstr, "[PATCH] RunPlatformMainLoop — C# driven loop (RunOneFrame + Sleep(0))"));
        instrs.Add(Instruction.Create(OpCodes.Call, writeLineRef));

        // ldarg.0; stsfld emscriptenGame
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Stsfld, gameField));

        // Loop label
        var loopLabel = Instruction.Create(OpCodes.Nop);
        instrs.Add(loopLabel);

        // ldsfld emscriptenGame; brfalse EndLoop
        var endLoopLabel = Instruction.Create(OpCodes.Ret);
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, gameField));
        instrs.Add(Instruction.Create(OpCodes.Brfalse_S, endLoopLabel));

        // ldsfld emscriptenGame; callvirt RunOneFrame
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, gameField));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, runOneFrameRef));

        // Thread.Sleep(0)
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        instrs.Add(Instruction.Create(OpCodes.Call, sleepRef));

        // goto Loop
        instrs.Add(Instruction.Create(OpCodes.Br_S, loopLabel));

        // EndLoop: ret
        instrs.Add(endLoopLabel);

        runMainLoopMethod.Body.InitLocals = true;
        runMainLoopMethod.Body.MaxStackSize = 2;

        Console.WriteLine("[+] Replaced RunPlatformMainLoop body (C# driven loop)");

        // === Patch RunLoop ===
        // Original RunLoop:
        //   if (NeedsPlatformMainLoop()) { RunPlatformMainLoop(this); }
        //   while (RunApplication) { Tick(); }
        //   OnExiting(this, EventArgs.Empty);
        //
        // RunPlatformMainLoop now blocks forever (our loop). So the while
        // loop and OnExiting never run. But we still need RunLoop to CALL
        // RunPlatformMainLoop. Original code already does this via the if.
        // But we previously patched RunLoop to just ret. Let's restore it
        // to call RunPlatformMainLoop then ret (skip while + OnExiting).
        //
        // New RunLoop body:
        //   ldarg.0
        //   call RunPlatformMainLoop(this)  // blocks forever
        //   ret
        //
        // Find RunPlatformMainLoop method reference on SDL3_FNAPlatform
        var runPlatformMainLoopRef = new MethodReference("RunPlatformMainLoop", voidType, platformType)
        {
            HasThis = true,  // instance method? Let me check
        };
        // Actually, RunPlatformMainLoop is static. Let me check the signature.
        // From FNA source: public static void RunPlatformMainLoop(Game game)
        // So it's static, takes a Game parameter.
        runPlatformMainLoopRef.HasThis = false;
        runPlatformMainLoopRef.Parameters.Add(new ParameterDefinition(gameType));

        var runLoopInstrs = runLoopMethod.Body.Instructions;
        runLoopInstrs.Clear();
        runLoopMethod.Body.ExceptionHandlers.Clear();
        // ldarg.0 (this - the Game); call RunPlatformMainLoop(this); ret
        runLoopInstrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        runLoopInstrs.Add(Instruction.Create(OpCodes.Call, runPlatformMainLoopRef));
        runLoopInstrs.Add(Instruction.Create(OpCodes.Ret));
        runLoopMethod.Body.InitLocals = true;
        runLoopMethod.Body.MaxStackSize = 2;

        Console.WriteLine("[+] Replaced RunLoop body (call RunPlatformMainLoop, ret)");

        // Save
        var tempPath = fnaPath + ".tmp";
        fnaAsm.Write(tempPath);
        fnaAsm.Dispose();
        File.Copy(tempPath, fnaPath, overwrite: true);
        File.Delete(tempPath);
        Console.WriteLine($"[+] Written: {fnaPath}");
        return 0;
    }
}
