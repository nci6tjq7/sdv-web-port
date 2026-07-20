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
            Console.WriteLine("  Patches SDL3_FNAPlatform.RunPlatformMainLoop to use JS interop");
            Console.WriteLine("  instead of [DllImport(\"__Native\")] emscripten_set_main_loop.");
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

        var platformType = module.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.SDL3_FNAPlatform");
        if (platformType == null)
        {
            Console.WriteLine("[!] SDL3_FNAPlatform type not found!");
            return 1;
        }

        var runMainLoopMethod = platformType.Methods.FirstOrDefault(m => m.Name == "RunPlatformMainLoop");
        if (runMainLoopMethod == null || runMainLoopMethod.Body == null)
        {
            Console.WriteLine("[!] RunPlatformMainLoop method not found!");
            return 1;
        }
        Console.WriteLine($"[i] Found RunPlatformMainLoop: {runMainLoopMethod.Body.Instructions.Count} instructions");

        // Also find and patch Game.RunLoop to return immediately (skip while loop and OnExiting)
        // Game.RunLoop is in Microsoft.Xna.Framework.Game
        var gameType = module.Types.FirstOrDefault(t => t.FullName == "Microsoft.Xna.Framework.Game");
        if (gameType == null)
        {
            Console.WriteLine("[!] Game type not found!");
            return 1;
        }
        var runLoopMethod = gameType.Methods.FirstOrDefault(m => m.Name == "RunLoop");
        if (runLoopMethod == null || runLoopMethod.Body == null)
        {
            Console.WriteLine("[!] RunLoop method not found!");
            return 1;
        }
        Console.WriteLine($"[i] Found RunLoop: {runLoopMethod.Body.Instructions.Count} instructions");

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

        // Find Thread.Sleep
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

        // === APPROACH ===
        // Patch BOTH RunPlatformMainLoop AND RunLoop:
        // - RunPlatformMainLoop: set emscriptenGame, return (no block)
        // - RunLoop: return immediately (skip while loop AND OnExiting)
        //
        // FNA's RunLoop:
        //   if (NeedsPlatformMainLoop()) { RunPlatformMainLoop(this); }
        //   while (RunApplication) { Tick(); }
        //   OnExiting(this, EventArgs.Empty);
        //
        // If we only patch RunPlatformMainLoop, RunLoop continues to while loop
        // and OnExiting. OnExiting fires SDV's GameRunner.Exiting event which
        // calls Process.GetCurrentProcess() → PlatformNotSupportedException.
        //
        // Fix: Patch RunLoop to return immediately after RunPlatformMainLoop.
        // Actually, just replace RunLoop's body with just `ret`.

        // === Patch RunPlatformMainLoop: set emscriptenGame, return ===
        var instrs = runMainLoopMethod.Body.Instructions;
        instrs.Clear();
        runMainLoopMethod.Body.ExceptionHandlers.Clear();

        // ldstr; call Console.WriteLine
        instrs.Add(Instruction.Create(OpCodes.Ldstr, "[PATCH] RunPlatformMainLoop — setting emscriptenGame, returning"));
        instrs.Add(Instruction.Create(OpCodes.Call, writeLineRef));

        // ldarg.0; stsfld emscriptenGame
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Stsfld, gameField));

        // ret
        instrs.Add(Instruction.Create(OpCodes.Ret));

        runMainLoopMethod.Body.InitLocals = true;
        runMainLoopMethod.Body.MaxStackSize = 2;

        Console.WriteLine("[+] Replaced RunPlatformMainLoop body (set emscriptenGame, return)");

        // === Patch RunLoop: just ret (skip everything) ===
        var runLoopInstrs = runLoopMethod.Body.Instructions;
        runLoopInstrs.Clear();
        runLoopMethod.Body.ExceptionHandlers.Clear();
        runLoopInstrs.Add(Instruction.Create(OpCodes.Ret));
        runLoopMethod.Body.InitLocals = true;
        runLoopMethod.Body.MaxStackSize = 1;

        Console.WriteLine("[+] Replaced RunLoop body (just ret — skip while loop and OnExiting)");

        // Add a new public static method RunOneFrameJS that JS can call.
        var runOneFrameJsMethod = platformType.Methods.FirstOrDefault(m => m.Name == "RunOneFrameJS");
        if (runOneFrameJsMethod != null)
        {
            Console.WriteLine("[i] RunOneFrameJS already exists, replacing body");
            runOneFrameJsMethod.Body.Instructions.Clear();
            runOneFrameJsMethod.Body.ExceptionHandlers.Clear();
        }
        else
        {
            runOneFrameJsMethod = new MethodDefinition("RunOneFrameJS",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                voidType);
            runOneFrameJsMethod.Body = new MethodBody(runOneFrameJsMethod);
            platformType.Methods.Add(runOneFrameJsMethod);
        }

        // Body:
        //   ldsfld emscriptenGame
        //   brfalse ret
        //   ldsfld emscriptenGame
        //   callvirt Game.RunOneFrame()
        //   ret
        var runOneFrameRef = new MethodReference("RunOneFrame", voidType, gameType)
        {
            HasThis = true,
        };
        var newInstrs = runOneFrameJsMethod.Body.Instructions;
        var retInstr = Instruction.Create(OpCodes.Ret);
        newInstrs.Add(Instruction.Create(OpCodes.Ldsfld, gameField));
        newInstrs.Add(Instruction.Create(OpCodes.Brfalse_S, retInstr));
        newInstrs.Add(Instruction.Create(OpCodes.Ldsfld, gameField));
        newInstrs.Add(Instruction.Create(OpCodes.Callvirt, runOneFrameRef));
        newInstrs.Add(retInstr);
        runOneFrameJsMethod.Body.InitLocals = true;
        runOneFrameJsMethod.Body.MaxStackSize = 2;
        Console.WriteLine("[+] Added RunOneFrameJS method");

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
