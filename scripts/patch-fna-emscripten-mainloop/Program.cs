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
            Console.WriteLine("  Patches SDL3_FNAPlatform.RunPlatformMainLoop to block forever");
            Console.WriteLine("  instead of calling [DllImport(\"__Native\")] emscripten_set_main_loop.");
            Console.WriteLine("  JS side (main.js) drives the game loop via requestAnimationFrame,");
            Console.WriteLine("  calling SDL3_FNAPlatform.RunOneFrame() each frame.");
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

        var gameField = platformType.Fields.FirstOrDefault(f => f.Name == "emscriptenGame");
        if (gameField == null)
        {
            Console.WriteLine("[!] emscriptenGame field not found!");
            return 1;
        }

        var stringType = module.TypeSystem.String;
        var voidType = module.TypeSystem.Void;
        var int32Type = module.TypeSystem.Int32;

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
        // Thread.Sleep(int milliseconds)
        var sleepRef = new MethodReference("Sleep", voidType, threadType)
        {
            HasThis = false,
        };
        sleepRef.Parameters.Add(new ParameterDefinition(int32Type));

        // Find Timeout.InfiniteTimeSpan (Timeout is System.Threading.Timeout)
        TypeReference timeoutType = null;
        foreach (var tr in module.GetTypeReferences())
        {
            if (tr.FullName == "System.Threading.Timeout")
            {
                timeoutType = tr;
                break;
            }
        }
        if (timeoutType == null)
        {
            timeoutType = new TypeReference("System.Threading", "Timeout", module, module.TypeSystem.CoreLibrary);
        }
        // Timeout.InfiniteTimeSpan is a static property of type TimeSpan
        // We use Thread.Sleep(int) with -1 instead (Timeout.Infinite)
        // Thread.Sleep(Timeout.Infinite) = Thread.Sleep(-1)

        // Replace RunPlatformMainLoop body with:
        //   Console.WriteLine("[PATCH] RunPlatformMainLoop — blocking, JS drives loop");
        //   emscriptenGame = game;
        //   while (true) { Thread.Sleep(1000); }
        //
        // This blocks the C# main thread forever. JS (main.js) sets up
        // requestAnimationFrame loop and calls SDL3_FNAPlatform.RunOneFrame()
        // (or a new RunOneFrameJS method) each frame.
        var instrs = runMainLoopMethod.Body.Instructions;
        instrs.Clear();
        runMainLoopMethod.Body.ExceptionHandlers.Clear();

        // ldstr; call Console.WriteLine
        instrs.Add(Instruction.Create(OpCodes.Ldstr, "[PATCH] RunPlatformMainLoop — blocking C# thread, JS drives loop via requestAnimationFrame"));
        instrs.Add(Instruction.Create(OpCodes.Call, writeLineRef));

        // ldarg.0; stsfld emscriptenGame
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Stsfld, gameField));

        // Loop: Thread.Sleep(1000); jmp Loop
        var loopLabel = Instruction.Create(OpCodes.Nop);
        instrs.Add(loopLabel);
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 1000));  // 1000 ms
        instrs.Add(Instruction.Create(OpCodes.Call, sleepRef));
        instrs.Add(Instruction.Create(OpCodes.Br_S, loopLabel));
        // No ret — infinite loop

        runMainLoopMethod.Body.InitLocals = true;
        runMainLoopMethod.Body.MaxStackSize = 2;

        Console.WriteLine("[+] Replaced RunPlatformMainLoop body (infinite Thread.Sleep loop)");

        // Add a new public static method RunOneFrameJS that JS can call.
        // Check if it already exists
        var runOneFrameJsMethod = platformType.Methods.FirstOrDefault(m => m.Name == "RunOneFrameJS");
        if (runOneFrameJsMethod != null)
        {
            Console.WriteLine("[i] RunOneFrameJS already exists, skipping");
        }
        else
        {
            runOneFrameJsMethod = new MethodDefinition("RunOneFrameJS",
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                voidType);
            runOneFrameJsMethod.Body = new MethodBody(runOneFrameJsMethod);

            // Body:
            //   ldsfld emscriptenGame
            //   brfalse ret
            //   ldsfld emscriptenGame
            //   callvirt Game.RunOneFrame()
            //   ret
            var gameType = gameField.FieldType;
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
            platformType.Methods.Add(runOneFrameJsMethod);
            Console.WriteLine("[+] Added RunOneFrameJS method");
        }

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
