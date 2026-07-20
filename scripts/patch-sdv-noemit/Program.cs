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
        // Read into memory first to avoid file handle issues when writing back
        // (Cecil tries to re-read embedded resources from the original file during write,
        // which fails if we're writing to the same path).
        // Use a custom assembly resolver that includes the input file's directory,
        // so Cecil can resolve referenced assemblies (MonoGame.Framework, FNA, etc.)
        // when constructing method references and writing metadata.
        var resolver = new DefaultAssemblyResolver();
        var inputDir = Path.GetDirectoryName(inputPath);
        if (!string.IsNullOrEmpty(inputDir))
        {
            resolver.AddSearchDirectory(inputDir);
            Console.WriteLine($"[+] Assembly search directory: {inputDir}");
        }
        var asmBytes = File.ReadAllBytes(inputPath);
        var asm = AssemblyDefinition.ReadAssembly(new MemoryStream(asmBytes), new ReaderParameters
        {
            AssemblyResolver = resolver,
        });

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
        //
        // ATTEMPTED FIX: Patch DoesAssetExist to always return true.
        // RESULT: StackOverflow! The IL `call ContentManager::Load<!!T>` in LoadImpl
        // is treated by Mono WASM as virtual dispatch, so LoadImpl → Load (override)
        // → Load(string, LanguageCode) → LoadImpl → ... infinite recursion.
        // The original comment was CORRECT — this cannot be enabled.
        //
        // The proper fix is to make the manifest loading work (PatchContentHashParserReadAllText).
        // methodsNopped += PatchDoesAssetExist(asm);

        // NEW FIX: Patch LoadImpl to call base.ReadAsset directly instead of base.Load.
        // This bypasses BOTH DoesAssetExist AND the virtual Load dispatch (which causes
        // stack overflow in Mono WASM). ReadAsset calls OpenStream directly.
        // This is the nuclear option — skip the manifest check entirely.
        methodsNopped += PatchLoadImplToCallReadAsset(asm);

        // NEW FIX: Patch SDV's Program.Main to remove the finally { Dispose() } block.
        // When RunPlatformMainLoop returns immediately (instead of blocking), Game.Run()
        // returns, and Program.Main's finally block disposes the game runner.
        // This destroys the game before JS can drive frames via RunOneFrame.
        // Fix: NOP the Dispose call so the game stays alive.
        methodsNopped += PatchProgramMainRemoveDispose(asm);

        // Instead: patch all File.Exists AND File.OpenRead calls in LocalizedContentManager.
        // File.Exists → return true (so asset is considered to exist)
        // File.OpenRead → redirect to HttpTitleContainer.OpenStream (fetch via HTTP)
        methodsNopped += PatchFileOperationsInLocalizedContentManager(asm);

        // Patch ContentHashParser.ParseFromFile to use HTTP fetch instead of File.ReadAllText.
        // SDV's LocalizedContentManager.DoesAssetExist checks a manifest loaded from
        // ContentHashes.json. The manifest is loaded via ContentHashParser.ParseFromFile,
        // which calls File.ReadAllText. In WASM, File.ReadAllText throws (no filesystem).
        // Result: _manifest stays empty, DoesAssetExist always returns false, every Load fails.
        // Fix: redirect File.ReadAllText → TitleContainer.ReadAllText (HTTP fetch).
        methodsNopped += PatchContentHashParserReadAllText(asm);

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
    /// Patch all File.Exists AND File.OpenRead calls in LocalizedContentManager.
    /// - File.Exists → return true (file is considered to exist)
    /// - File.OpenRead → redirect to HttpTitleContainer.OpenStream (HTTP fetch)
    /// </summary>
    static int PatchFileOperationsInLocalizedContentManager(AssemblyDefinition asm)
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
                Console.WriteLine("  [!] LocalizedContentManager type not found");
                return 0;
            }

            // Find HttpTitleContainer.OpenStream method reference
            var httpTitleType = module.GetType("Microsoft.Xna.Framework.HttpTitleContainer");
            MethodReference httpOpenStream = null;
            if (httpTitleType != null)
            {
                httpOpenStream = httpTitleType.Methods.FirstOrDefault(m => m.Name == "OpenStream");
            }

            int fileExistsPatched = 0;
            int fileOpenReadPatched = 0;

            Console.WriteLine($"  [i] LocalizedContentManager methods ({targetType.Methods.Count}):");
            foreach (var m in targetType.Methods)
            {
                if (m.Body == null) continue;
                Console.WriteLine($"    - {m.Name} (params: {m.Parameters.Count}, body instrs: {m.Body.Instructions.Count})");
            }
            foreach (var method in targetType.Methods)
            {
                if (method.Body == null) continue;
                int methodExists = 0, methodOpenRead = 0;
                var instrs = method.Body.Instructions;
                for (int i = 0; i < instrs.Count; i++)
                {
                    var instr = instrs[i];
                    if (instr.OpCode == OpCodes.Call && instr.Operand is MethodReference mr)
                    {
                        // File.Exists → return true (with stack balance!)
                        // Original: ldarg fileName; call File.Exists(string) → bool
                        //   stack effect: -1 string, +1 bool = 0
                        // Buggy:     ldarg fileName; ldc.i4.1
                        //   stack effect: +1 string, +1 bool = +2 (IMBALANCED!)
                        // Fixed:     ldarg fileName; pop; ldc.i4.1
                        //   stack effect: +1 string, -1 string, +1 bool = +1 (matches bool return)
                        if (mr.Name == "Exists" && mr.DeclaringType?.FullName == "System.IO.File")
                        {
                            // Replace the `call File.Exists` instruction with `pop`
                            // (this consumes the string argument that was loaded for File.Exists)
                            instr.OpCode = OpCodes.Pop;
                            instr.Operand = null;
                            // Insert a new `ldc.i4.1` instruction AFTER the pop
                            // (this pushes the bool result true)
                            var ldcInstr = Instruction.Create(OpCodes.Ldc_I4_1);
                            instrs.Insert(i + 1, ldcInstr);
                            fileExistsPatched++;
                            methodExists++;
                            i++; // Skip the newly inserted instruction
                        }
                        // File.OpenRead → HttpTitleContainer.OpenStream
                        if (mr.Name == "OpenRead" && mr.DeclaringType?.FullName == "System.IO.File")
                        {
                            if (httpOpenStream != null)
                            {
                                instr.Operand = httpOpenStream;
                                fileOpenReadPatched++;
                                methodOpenRead++;
                            }
                            else
                            {
                                Console.WriteLine($"  [!] HttpTitleContainer.OpenStream not found — cannot redirect File.OpenRead");
                            }
                        }
                    }
                }
                if (methodExists > 0 || methodOpenRead > 0)
                {
                    Console.WriteLine($"  [-] {method.Name}: {methodExists} File.Exists→true, {methodOpenRead} File.OpenRead→OpenStream");
                }
            }

            Console.WriteLine($"  [-] Total: {fileExistsPatched} File.Exists → true (with pop), {fileOpenReadPatched} File.OpenRead → HttpTitleContainer.OpenStream");
            return (fileExistsPatched + fileOpenReadPatched) > 0 ? 1 : 0;
        }
        return 0;
    }

    /// <summary>
    /// Replace ContentHashParser.ParseFromFile's ENTIRE body with custom IL
    /// that calls TitleContainer.ReadAllText instead of File.ReadAllText.
    ///
    /// SDV's LocalizedContentManager loads a manifest of all asset file paths
    /// from ContentHashes.json. DoesAssetExist checks this manifest before
    /// attempting to load any asset. Without the manifest, every Load fails
    /// with "Could not load X asset!".
    ///
    /// Original ParseFromFile IL:
    ///   ldarg.0
    ///   call File.ReadAllText(string) → string
    ///   call CHJsonParser.ParseJson(string) → object
    ///   isinst Dictionary<string, object>
    ///   ret
    ///
    /// Patched IL (replace File.ReadAllText with TitleContainer.ReadAllText):
    ///   ldarg.0
    ///   call TitleContainer.ReadAllText(string) → string
    ///   call CHJsonParser.ParseJson(string) → object
    ///   isinst Dictionary<string, object>
    ///   ret
    ///
    /// We REPLACE THE ENTIRE BODY rather than redirecting individual calls,
    /// because this is more reliable (no need to search for File.ReadAllText
    /// call sites, no risk of missing one, and we control the exact IL).
    /// </summary>
    static int PatchContentHashParserReadAllText(AssemblyDefinition asm)
    {
        foreach (var module in asm.Modules)
        {
            var parserType = module.GetType("ContentManifest.ContentHashParser");
            if (parserType == null)
            {
                Console.WriteLine("  [!] ContentManifest.ContentHashParser type not found");
                continue;
            }

            var parseFromFileMethod = parserType.Methods.FirstOrDefault(m => m.Name == "ParseFromFile");
            if (parseFromFileMethod == null || parseFromFileMethod.Body == null)
            {
                Console.WriteLine("  [!] ContentHashParser.ParseFromFile method not found");
                continue;
            }

            Console.WriteLine($"  [i] Found ParseFromFile: {parseFromFileMethod.Body.Instructions.Count} instructions");

            // Find TitleContainer type reference in this module's referenced assemblies.
            TypeReference titleContainerRef = null;
            foreach (var tr in module.GetTypeReferences())
            {
                if (tr.FullName == "Microsoft.Xna.Framework.TitleContainer")
                {
                    titleContainerRef = tr;
                    break;
                }
            }

            if (titleContainerRef == null)
            {
                Console.WriteLine("  [!] Microsoft.Xna.Framework.TitleContainer not found in type references");
                continue;
            }

            Console.WriteLine($"  [i] Found TitleContainer reference: {titleContainerRef.FullName} (from {titleContainerRef.Scope})");

            // Find CHJsonParser.ParseJson method reference (already used in the original body).
            MethodReference parseJsonRef = null;
            foreach (var instr in parseFromFileMethod.Body.Instructions)
            {
                if (instr.OpCode == OpCodes.Call && instr.Operand is MethodReference mr &&
                    mr.Name == "ParseJson" && mr.DeclaringType?.FullName == "ContentManifest.CHJsonParser")
                {
                    parseJsonRef = mr;
                    break;
                }
            }

            if (parseJsonRef == null)
            {
                Console.WriteLine("  [!] CHJsonParser.ParseJson not found in ParseFromFile body");
                continue;
            }

            Console.WriteLine($"  [i] Found CHJsonParser.ParseJson reference");

            // Find Dictionary<string, object> type reference (used in isinst instruction).
            TypeReference dictTypeRef = null;
            foreach (var instr in parseFromFileMethod.Body.Instructions)
            {
                if (instr.OpCode == OpCodes.Isinst && instr.Operand is TypeReference tr &&
                    tr.FullName.StartsWith("System.Collections.Generic.Dictionary"))
                {
                    dictTypeRef = tr;
                    break;
                }
            }

            if (dictTypeRef == null)
            {
                Console.WriteLine("  [!] Dictionary<string, object> type reference not found");
                continue;
            }

            Console.WriteLine($"  [i] Found Dictionary type reference: {dictTypeRef.FullName}");

            // Construct MethodReference for TitleContainer.GetManifestJson() → string
            // This is a parameterless static method that returns the preloaded
            // ContentHashes.json string from JS (globalThis.__manifestJson).
            var stringType = module.TypeSystem.String;
            var getManifestJsonRef = new MethodReference("GetManifestJson", stringType, titleContainerRef)
            {
                HasThis = false,  // static method
            };
            // No parameters — parameterless method

            Console.WriteLine($"  [i] Constructed MethodReference: TitleContainer.GetManifestJson() → string");

            // REPLACE THE ENTIRE METHOD BODY with:
            //   ldstr "[PATCH] ParseFromFile called"
            //   call Console.WriteLine(string)
            //   call TitleContainer.GetManifestJson  // → string (ignores ldarg.0 path)
            //   call CHJsonParser.ParseJson          // → object
            //   isinst Dictionary<string,object>     // cast
            //   ret
            //
            // The Console.WriteLine at the start is a diagnostic marker — if it
            // appears in the browser console, we know the patched body IS being
            // executed. If not, the patched DLL isn't being used.
            //
            // Note: we intentionally DON'T use ldarg.0 (the path parameter) because
            // GetManifestJson() takes no arguments — it returns the preloaded manifest.
            // The path argument is ignored (the manifest is always at Content/ContentHashes.json).

            // Find Console.WriteLine(string) method reference.
            // Search existing type references for System.Console to avoid
            // TypeLoadException from wrong assembly scope.
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
            var writeLineRef = new MethodReference("WriteLine", module.TypeSystem.Void, consoleType)
            {
                HasThis = false,
            };
            writeLineRef.Parameters.Add(new ParameterDefinition(stringType));

            var instrs = parseFromFileMethod.Body.Instructions;
            instrs.Clear();
            parseFromFileMethod.Body.ExceptionHandlers.Clear();

            instrs.Add(Instruction.Create(OpCodes.Ldstr, "[PATCH] ParseFromFile called — patched body executing"));
            instrs.Add(Instruction.Create(OpCodes.Call, writeLineRef));
            instrs.Add(Instruction.Create(OpCodes.Call, getManifestJsonRef));
            instrs.Add(Instruction.Create(OpCodes.Call, parseJsonRef));
            instrs.Add(Instruction.Create(OpCodes.Isinst, dictTypeRef));
            instrs.Add(Instruction.Create(OpCodes.Ret));

            parseFromFileMethod.Body.InitLocals = true;
            parseFromFileMethod.Body.MaxStackSize = 2;

            Console.WriteLine($"  [-] REPLACED ParseFromFile body: WriteLine(marker) → GetManifestJson() → CHJsonParser.ParseJson → isinst → ret");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Patch LocalizedContentManager.LoadImpl to call base.ReadAsset directly
    /// instead of base.Load. This bypasses:
    /// 1. DoesAssetExist check (which fails because manifest is empty)
    /// 2. Virtual Load dispatch (which causes stack overflow in Mono WASM:
    ///    LoadImpl → Load(override) → Load(string,lang) → LoadImpl → ...)
    ///
    /// Original LoadImpl IL:
    ///   ldarg.0
    ///   ldarg.2
    ///   callvirt DoesAssetExist<!!T>(string) → bool
    ///   brtrue.s L_load
    ///   ldstr "Could not load "
    ///   ldarg.2
    ///   ldstr " asset!"
    ///   call String.Concat
    ///   newobj ContentLoadException
    ///   throw
    /// L_load:
    ///   ldarg.0
    ///   ldarg.2
    ///   call ContentManager::Load<!!T>(string) → !!T  ← causes stack overflow!
    ///   ret
    ///
    /// Patched LoadImpl IL:
    ///   ldstr "[PATCH] LoadImpl calling ReadAsset directly"
    ///   call Console.WriteLine
    ///   ldarg.0
    ///   ldarg.2
    ///   ldnull
    ///   call ContentManager::ReadAsset<!!T>(string, Action<IDisposable>) → !!T
    ///   ret
    ///
    /// ReadAsset is protected virtual but NOT overridden by LocalizedContentManager.
    /// Even if Mono WASM treats `call` as `callvirt`, there's no override to dispatch to,
    /// so it calls the base ContentManager.ReadAsset, which calls OpenStream.
    /// No recursion, no manifest check, no stack overflow.
    /// </summary>
    static int PatchLoadImplToCallReadAsset(AssemblyDefinition asm)
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
                Console.WriteLine("  [!] LocalizedContentManager type not found for LoadImpl patch");
                return 0;
            }

            var loadImplMethod = targetType.Methods.FirstOrDefault(m => m.Name == "LoadImpl");
            if (loadImplMethod == null || loadImplMethod.Body == null)
            {
                Console.WriteLine("  [!] LoadImpl method not found");
                return 0;
            }

            Console.WriteLine($"  [i] Found LoadImpl: {loadImplMethod.Body.Instructions.Count} instructions, {loadImplMethod.GenericParameters.Count} generic params");

            // Find the ContentManager.ReadAsset method reference.
            // ReadAsset is: protected virtual T ReadAsset<T>(string assetName, Action<IDisposable> recordDisposableObject)
            // We need to find it in the module's type references.
            TypeReference contentManagerRef = null;
            foreach (var tr in module.GetTypeReferences())
            {
                if (tr.FullName == "Microsoft.Xna.Framework.Content.ContentManager")
                {
                    contentManagerRef = tr;
                    break;
                }
            }

            if (contentManagerRef == null)
            {
                Console.WriteLine("  [!] ContentManager type reference not found");
                return 0;
            }

            Console.WriteLine($"  [i] Found ContentManager reference: {contentManagerRef.FullName} (from {contentManagerRef.Scope})");

            // Resolve the ContentManager type to find the ReadAsset method definition.
            // With the assembly resolver configured, Cecil can follow type forwarders
            // from MonoGame.Framework to FNA and find the actual method.
            TypeDefinition contentManagerDef = null;
            try
            {
                contentManagerDef = contentManagerRef.Resolve();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [!] Could not resolve ContentManager: {ex.Message}");
                return 0;
            }

            if (contentManagerDef == null)
            {
                Console.WriteLine("  [!] ContentManager.Resolve() returned null");
                return 0;
            }

            // Find the ReadAsset method in ContentManager
            var readAssetDef = contentManagerDef.Methods.FirstOrDefault(m =>
                m.Name == "ReadAsset" && m.HasGenericParameters && m.Parameters.Count == 2);

            if (readAssetDef == null)
            {
                Console.WriteLine("  [!] ReadAsset method not found in ContentManager!");
                Console.WriteLine("  [i] Available methods in ContentManager:");
                foreach (var m in contentManagerDef.Methods)
                {
                    Console.WriteLine($"      - {m.Name}({m.Parameters.Count} params, generic={m.HasGenericParameters})");
                }
                return 0;
            }

            Console.WriteLine($"  [i] Found ReadAsset: {readAssetDef.FullName} (generic params: {readAssetDef.GenericParameters.Count})");

            // Import the method into the SDV module (this handles type forwarders correctly)
            var readAssetImported = module.ImportReference(readAssetDef);

            // Create a generic instance method: ReadAsset<!!T> where !!T is LoadImpl's generic param
            var tParam = loadImplMethod.GenericParameters[0];
            var readAssetGeneric = new GenericInstanceMethod(readAssetImported);
            readAssetGeneric.GenericArguments.Add(tParam);

            Console.WriteLine($"  [i] Created generic instance: ReadAsset<{tParam}>");

            // Find Console.WriteLine(string) method reference.
            // We search the module's existing type references for System.Console
            // (constructing it manually with CoreLibrary scope doesn't work because
            // Console might be in a different assembly than CoreLibrary).
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
                // Fallback: try constructing with System.Runtime scope
                consoleType = new TypeReference("System", "Console", module, module.TypeSystem.CoreLibrary);
                Console.WriteLine("  [!] System.Console not found in type refs, using fallback");
            }
            var stringType = module.TypeSystem.String;
            var voidType = module.TypeSystem.Void;
            var writeLineRef = new MethodReference("WriteLine", voidType, consoleType)
            {
                HasThis = false,
            };
            writeLineRef.Parameters.Add(new ParameterDefinition(stringType));

            // REPLACE THE ENTIRE METHOD BODY with:
            //   ldstr "[PATCH] LoadImpl → ReadAsset direct"
            //   call Console.WriteLine
            //   ldarg.0          // this
            //   ldarg.2          // localizedAssetName (string)
            //   ldnull           // null for Action<IDisposable>
            //   call ContentManager.ReadAsset<!!T>(string, Action<IDisposable>)
            //   ret
            var instrs = loadImplMethod.Body.Instructions;
            instrs.Clear();
            loadImplMethod.Body.ExceptionHandlers.Clear();

            instrs.Add(Instruction.Create(OpCodes.Ldstr, "[PATCH] LoadImpl → ReadAsset direct (bypassing DoesAssetExist + virtual Load)"));
            instrs.Add(Instruction.Create(OpCodes.Call, writeLineRef));
            instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
            instrs.Add(Instruction.Create(OpCodes.Ldarg_2));
            instrs.Add(Instruction.Create(OpCodes.Ldnull));
            instrs.Add(Instruction.Create(OpCodes.Call, readAssetGeneric));
            instrs.Add(Instruction.Create(OpCodes.Ret));

            loadImplMethod.Body.InitLocals = true;
            loadImplMethod.Body.MaxStackSize = 4;

            Console.WriteLine($"  [-] REPLACED LoadImpl body: WriteLine(marker) → ReadAsset<!!T>(localizedAssetName, null) → ret");
            return 1;
        }
        return 0;
    }

    /// <summary>
    /// Patch SDV's Program.Main to NOP the Dispose call in the finally block.
    ///
    /// SDV's Program.Main IL:
    ///   .try {
    ///     IL_0031: ldloc.0
    ///     IL_0032: stsfld GameRunner::instance
    ///     IL_0037: ldloc.0
    ///     IL_0038: callvirt Game::Run()
    ///     IL_003d: leave.s IL_0049
    ///   } finally {
    ///     IL_003f: ldloc.0
    ///     IL_0040: brfalse.s IL_0048
    ///     IL_0042: ldloc.0
    ///     IL_0043: callvirt IDisposable::Dispose()  ← NOP this
    ///     IL_0048: endfinally
    ///   }
    ///   IL_0049: ret
    ///
    /// When RunPlatformMainLoop returns immediately (patched to not block),
    /// Game.Run() returns, and the finally block disposes the game runner.
    /// This destroys the game before JS can drive frames via RunOneFrame.
    /// Fix: Replace `callvirt IDisposable::Dispose()` with `pop` (consume
    /// the ldloc.0 on stack) so Dispose is never called.
    /// </summary>
    static int PatchProgramMainRemoveDispose(AssemblyDefinition asm)
    {
        foreach (var module in asm.Modules)
        {
            var programType = module.GetType("StardewValley.Program");
            if (programType == null)
            {
                Console.WriteLine("  [!] StardewValley.Program type not found");
                continue;
            }

            var mainMethod = programType.Methods.FirstOrDefault(m => m.Name == "Main");
            if (mainMethod == null || mainMethod.Body == null)
            {
                Console.WriteLine("  [!] Program.Main method not found");
                continue;
            }

            Console.WriteLine($"  [i] Found Program.Main: {mainMethod.Body.Instructions.Count} instructions, {mainMethod.Body.ExceptionHandlers.Count} handlers");

            // Find the Dispose call in the finally handler
            int patched = 0;
            var instrs = mainMethod.Body.Instructions;
            for (int i = 0; i < instrs.Count; i++)
            {
                var instr = instrs[i];
                if (instr.OpCode == OpCodes.Callvirt && instr.Operand is MethodReference mr &&
                    mr.Name == "Dispose" && mr.DeclaringType?.FullName == "System.IDisposable")
                {
                    // Replace `callvirt Dispose()` with `pop` (consume the object on stack)
                    instr.OpCode = OpCodes.Pop;
                    instr.Operand = null;
                    patched++;
                    Console.WriteLine($"  [-] NOP'd IDisposable.Dispose() in Program.Main finally block (replaced with pop)");
                }
            }

            if (patched == 0)
            {
                Console.WriteLine("  [!] No IDisposable.Dispose() calls found in Program.Main");
            }

            return patched > 0 ? 1 : 0;
        }
        return 0;
    }

    /// <summary>
    /// Patch LocalizedContentManager.DoesAssetExist to always return true.
    /// DISABLED — causes stack overflow. See PatchLoadImplToCallReadAsset instead.
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
