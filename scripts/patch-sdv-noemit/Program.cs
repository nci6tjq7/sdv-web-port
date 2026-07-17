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
        // 
        // CRITICAL: We can't just NOP it — AddGameInstance calls
        // Activator.CreateInstance(LocalMultiplayer.StaticVarHolderType) which would
        // throw ArgumentNullException if StaticVarHolderType is null.
        // 
        // Fix: replace the method body with:
        //   LocalMultiplayer.StaticVarHolderType = typeof(object);
        //   return;
        methodsNopped += PatchGenerateDynamicMethodsForStatics(asm);

        // GameRunner.SetInstanceDefaults — uses Force.DeepCloner to deep-clone game
        // state objects. Force.DeepCloner uses Expression.Compile (Linq.Expressions
        // interpreter) which fails with NullReferenceException in WASM when cloning
        // Nullable<T> struct fields (the interpreter can't handle null boxed structs).
        // 
        // Fix: NOP SetInstanceDefaults — it's used to clone default player settings
        // into new game instances, but for the title screen demo we don't need it.
        // The game will still boot; player defaults will be set later by Game1.
        methodsNopped += NopMethod(asm, "StardewValley.GameRunner", "SetInstanceDefaults");

        // KeyboardInput.Initialize — uses SetWindowsHookEx-style callback (HookProc)
        // which requires native-to-managed transition. In WASM, this throws:
        //   PlatformNotSupportedException: No native to managed transition for method
        //   'KeyboardInput.HookProc', missing [UnmanagedCallersOnly] attribute.
        // 
        // Fix: NOP KeyboardInput.Initialize — keyboard input is handled by SDL3
        // directly via FNA's event loop, not by Windows hooks. The browser's
        // keyboard events are captured by our main.js setupInput() handler.
        methodsNopped += NopMethod(asm, "StardewValley.KeyboardInput", "Initialize");

        // Options.setToDefaults — calls GraphicsAdapter.SupportedDisplayModes.First()
        // to pick a default display mode. In WASM/WebGL, SupportedDisplayModes is
        // empty (no desktop display modes), causing InvalidOperationException: NoElements.
        // 
        // Fix: NOP setToDefaults — options will be set to default values by SDV's
        // configuration loading later. The title screen doesn't need display mode
        // selection (we hardcode 1280x720 in the canvas).
        methodsNopped += NopMethod(asm, "StardewValley.Options", "setToDefaults");

        // LocalizedContentManager.GetContentRoot — accesses TitleContainer.Location
        // which is a MonoGame API not present in FNA. SDV throws:
        //   InvalidOperationException: Can't get TitleContainer.Location property from MonoGame
        // 
        // Fix: patch GetContentRoot to return "Content" directly.
        methodsNopped += PatchGetContentRoot(asm);

        // LocalizedContentManager.DoesAssetExist — uses File.Exists to check if an
        // asset exists before loading. In WASM, File.Exists always returns false.
        // Fix: patch DoesAssetExist to always return true — let OpenStream handle
        // the actual file loading via HTTP.
        methodsNopped += PatchDoesAssetExist(asm);

        Console.WriteLine($"[+] Methods patched: {methodsNopped}");

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

    /// <summary>
    /// Patch LocalizedContentManager.DoesAssetExist to always return true.
    /// In WASM, File.Exists always returns false, so DoesAssetExist would
    /// report all assets as missing. By returning true, we let the actual
    /// OpenStream/Load path handle file access (via HttpTitleContainer).
    /// </summary>
    static int PatchDoesAssetExist(AssemblyDefinition asm)
    {
        foreach (var module in asm.Modules)
        {
            TypeDefinition FindType(TypeDefinition parent, string name)
            {
                if (parent.FullName == name) return parent;
                foreach (var nested in parent.NestedTypes)
                {
                    var found = FindType(nested, name);
                    if (found != null) return found;
                }
                return null;
            }

            TypeDefinition targetType = null;
            foreach (var topType in module.Types)
            {
                targetType = FindType(topType, "StardewValley.LocalizedContentManager");
                if (targetType != null) break;
            }

            if (targetType == null)
            {
                Console.WriteLine("  [!] LocalizedContentManager type not found for DoesAssetExist");
                return 0;
            }

            // DoesAssetExist is a generic method. Find all variants.
            int patched = 0;
            foreach (var method in targetType.Methods)
            {
                if (method.Name == "DoesAssetExist")
                {
                    var instrs = method.Body.Instructions;
                    instrs.Clear();
                    method.Body.ExceptionHandlers.Clear();
                    instrs.Add(Instruction.Create(OpCodes.Ldc_I4_1)); // true
                    instrs.Add(Instruction.Create(OpCodes.Ret));
                    method.Body.InitLocals = true;
                    Console.WriteLine($"  [-] Patched DoesAssetExist<{method.GenericParameters.Count} params> → return true");
                    patched++;
                }
            }

            if (patched == 0)
            {
                Console.WriteLine("  [!] DoesAssetExist method not found");
            }
            return patched;
        }
        return 0;
    }
    /// 
    /// Original method accesses TitleContainer.Location (a MonoGame API not in FNA),
    /// causing: InvalidOperationException: Can't get TitleContainer.Location property
    /// 
    /// Patched method body:
    ///   ldstr "Content"
    ///   ret
    /// </summary>
    static int PatchGetContentRoot(AssemblyDefinition asm)
    {
        foreach (var module in asm.Modules)
        {
            // GetContentRoot might be in StardewValley.LocalizedContentManager
            // or a nested class. Search recursively.
            TypeDefinition FindType(TypeDefinition parent, string name)
            {
                if (parent.FullName == name) return parent;
                foreach (var nested in parent.NestedTypes)
                {
                    var found = FindType(nested, name);
                    if (found != null) return found;
                }
                return null;
            }

            TypeDefinition targetType = null;
            foreach (var topType in module.Types)
            {
                targetType = FindType(topType, "StardewValley.LocalizedContentManager");
                if (targetType != null) break;
            }

            if (targetType == null)
            {
                Console.WriteLine("  [!] LocalizedContentManager type not found");
                return 0;
            }

            var method = targetType.Methods.FirstOrDefault(m => m.Name == "GetContentRoot");
            if (method == null)
            {
                Console.WriteLine("  [!] GetContentRoot method not found");
                return 0;
            }

            // Replace body: return "Content"
            var stringType = module.ImportReference(typeof(string));
            var instrs = method.Body.Instructions;
            instrs.Clear();
            method.Body.ExceptionHandlers.Clear();
            instrs.Add(Instruction.Create(OpCodes.Ldstr, "Content"));
            instrs.Add(Instruction.Create(OpCodes.Ret));
            method.Body.InitLocals = true;

            Console.WriteLine("  [-] Patched LocalizedContentManager::GetContentRoot → return \"Content\"");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Patch LocalMultiplayer.GenerateDynamicMethodsForStatics to set
    /// StaticVarHolderType = typeof(object) instead of using Reflection.Emit.
    /// 
    /// Original method uses AssemblyBuilder/ModuleBuilder/TypeBuilder to create
    /// a dynamic type with static field accessors. This fails in WASM (no JIT).
    /// 
    /// Patched method body:
    ///   LocalMultiplayer.StaticVarHolderType = typeof(System.Object);
    ///   return;
    /// 
    /// This preserves the non-null invariant on StaticVarHolderType, which is
    /// required by GameRunner.AddGameInstance (calls Activator.CreateInstance on it).
    /// The StaticSave/StaticLoad delegates stay null, but they're only invoked
    /// when IsLocalMultiplayer is true (multiplayer mode), which is false for
    /// single-player title screen.
    /// </summary>
    static int PatchGenerateDynamicMethodsForStatics(AssemblyDefinition asm)
    {
        foreach (var module in asm.Modules)
        {
            var type = module.Types.FirstOrDefault(t => t.FullName == "StardewValley.LocalMultiplayer");
            if (type == null) continue;
            var method = type.Methods.FirstOrDefault(m => m.Name == "GenerateDynamicMethodsForStatics");
            if (method == null) continue;

            // Find the StaticVarHolderType field
            var field = type.Fields.FirstOrDefault(f => f.Name == "StaticVarHolderType");
            if (field == null)
            {
                Console.WriteLine("  [!] StaticVarHolderType field not found!");
                return 0;
            }

            // Import System.Object type reference
            var objectTypeRef = module.ImportReference(typeof(object));
            // Import Type.GetTypeFromHandle method
            var getTypeFromHandle = module.ImportReference(typeof(Type).GetMethod("GetTypeFromHandle"));

            // Build new method body:
            //   ldtoken System.Object
            //   call Type.GetTypeFromHandle(RuntimeTypeHandle)
            //   stsfld LocalMultiplayer.StaticVarHolderType
            //   ret
            var instrs = method.Body.Instructions;
            instrs.Clear();
            method.Body.ExceptionHandlers.Clear();

            // ldtoken System.Object — pushes RuntimeTypeHandle for System.Object
            instrs.Add(Instruction.Create(OpCodes.Ldtoken, objectTypeRef));
            // call Type.GetTypeFromHandle(RuntimeTypeHandle) — converts to Type
            instrs.Add(Instruction.Create(OpCodes.Call, getTypeFromHandle));
            // stsfld LocalMultiplayer.StaticVarHolderType — stores in static field
            instrs.Add(Instruction.Create(OpCodes.Stsfld, field));
            // ret
            instrs.Add(Instruction.Create(OpCodes.Ret));

            method.Body.InitLocals = true;

            Console.WriteLine($"  [-] Patched LocalMultiplayer::GenerateDynamicMethodsForStatics");
            Console.WriteLine($"      → StaticVarHolderType = typeof(object); ret");
            return 1;
        }
        Console.WriteLine($"  [!] Method not found: LocalMultiplayer::GenerateDynamicMethodsForStatics");
        return 0;
    }
}
