using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Cecil pass that rewrites SDV's AssemblyRefs and TypeRefs to resolve
/// correctly in the BlazorWebAssembly runtime:
///
/// 1. AssemblyRef version rewriting:
///    System.* v6.0.0.0 → v8.0.0.0  (BlazorWebAssembly runtime is v8.0.0;
///                                     Mono's WASM loader does NOT do unified
///                                     binding for System.* refs by default)
///    MonoGame.Framework v3.8.0.1641 → v3.8.5.0  (our facade's version)
///
/// 2. TypeRef scope rewriting (Phase 2.8 fix):
///    The BlazorWebAssembly trimmer strips "unused" type-forwards from
///    System.Runtime.wasm. E.g., System.IComparable's type-forward to
///    System.Private.CoreLib is stripped because our app doesn't reference
///    it via System.Runtime (the compiler resolves typeof() directly to
///    System.Private.CoreLib). But SDV's typerefs reference these types
///    via System.Runtime, and resolution fails at runtime.
///
///    Fix: for each typeref pointing at System.Runtime (or other facade
///    assemblies), look up where the type is actually forwarded to (using
///    our embedded ref assemblies), and rewrite the typeref's scope to
///    point at the target assembly directly.
/// </summary>
public static class SdvAssemblyRefRewriter
{
    /// <summary>
    /// Map of simple name → target version. Any AssemblyRef whose simple name
    /// matches gets its version rewritten.
    /// </summary>
    private static readonly Dictionary<string, Version> _systemRefVersions = new()
    {
        { "System.Runtime", new Version(8, 0, 0, 0) },
        { "System.Collections", new Version(8, 0, 0, 0) },
        { "System.Runtime.InteropServices", new Version(8, 0, 0, 0) },
        { "System.Xml.XmlSerializer", new Version(8, 0, 0, 0) },
        { "System.Linq.Expressions", new Version(8, 0, 0, 0) },
        { "System.Collections.Concurrent", new Version(8, 0, 0, 0) },
        { "System.Xml.ReaderWriter", new Version(8, 0, 0, 0) },
        { "System.Linq", new Version(8, 0, 0, 0) },
        { "System.Diagnostics.Process", new Version(8, 0, 0, 0) },
        { "System.Text.RegularExpressions", new Version(8, 0, 0, 0) },
        { "System.Threading.Thread", new Version(8, 0, 0, 0) },
        { "System.ComponentModel", new Version(8, 0, 0, 0) },
        { "System.Reflection.Emit.Lightweight", new Version(8, 0, 0, 0) },
        { "System.Reflection.Emit", new Version(8, 0, 0, 0) },
        { "System.Reflection.Emit.ILGeneration", new Version(8, 0, 0, 0) },
        { "System.Diagnostics.StackTrace", new Version(8, 0, 0, 0) },
        { "System.Net.Primitives", new Version(8, 0, 0, 0) },
        { "System.Threading", new Version(8, 0, 0, 0) },
        { "System.Runtime.InteropServices.RuntimeInformation", new Version(8, 0, 0, 0) },
        { "System.Reflection.Primitives", new Version(8, 0, 0, 0) },
        { "System.Net.NameResolution", new Version(8, 0, 0, 0) },
        { "System.Console", new Version(8, 0, 0, 0) },
        { "System.Private.CoreLib", new Version(8, 0, 0, 0) },
    };

    /// <summary>
    /// MonoGame.Framework version to rewrite to (must match our facade).
    /// </summary>
    private static readonly Version _monoGameFrameworkVersion = new(3, 8, 5, 0);

    /// <summary>
    /// Bisection mode for GameRunner..ctor() debugging.
    /// 0 = no patching (production mode)
    /// 1 = patch out everything after List inits + Game..ctor() call
    /// 2 = patch out everything after GraphicsDeviceManager (before LocalMultiplayer.Initialize)
    /// 3 = patch out everything after LocalMultiplayer.Initialize
    /// 4 = patch out everything after ItemRegistry.RegisterItemTypes
    /// 5 = patch out everything after Window.AllowUserResizing
    /// </summary>
    public static int BisectMode { get; set; } = 0;

    /// <summary>
    /// Cached forward map: (source assembly name, type full name) → (target assembly name, target type full name).
    /// Populated lazily from the embedded RUNTIME assemblies (which have type-forwards).
    /// </summary>
    private static readonly Dictionary<(string SourceAsm, string TypeFullName), (string TargetAsm, string? TargetType)> _forwardCache = new();

    /// <summary>
    /// Cached runtime AssemblyDefinitions (loaded from embedded runtime resources).
    /// Keyed by simple assembly name.
    /// </summary>
    private static readonly Dictionary<string, AssemblyDefinition> _runtimeAsmCache = new();
    private static readonly object _runtimeAsmLock = new();

    /// <summary>
    /// Load the RUNTIME version of an assembly from embedded resources (not the REF version).
    /// Runtime assemblies have type-forwards; ref assemblies have direct type defs.
    /// For MonoGame.Framework (our facade), the runtime version IS the facade itself
    /// (loaded from SDVDeps folder).
    /// Returns null if not found.
    /// </summary>
    private static AssemblyDefinition? LoadRuntimeAssembly(string simpleName)
    {
        lock (_runtimeAsmLock)
        {
            if (_runtimeAsmCache.TryGetValue(simpleName, out var cached))
                return cached;
        }

        var thisAssembly = typeof(SdvAssemblyRefRewriter).Assembly;
        var resourceNames = thisAssembly.GetManifestResourceNames();
        // Try Runtime folder first (System.* runtime assemblies)
        var matchingName = resourceNames.FirstOrDefault(n =>
            n.EndsWith($".Runtime.{simpleName}.dll", StringComparison.OrdinalIgnoreCase));
        // If not found, try SDVDeps folder (MonoGame.Framework facade + SDV deps)
        if (matchingName == null)
            matchingName = resourceNames.FirstOrDefault(n =>
                n.EndsWith($".SDVDeps.{simpleName}.dll", StringComparison.OrdinalIgnoreCase));
        // If not found, try KniDeps folder (KNI Xna.Framework.* assemblies)
        if (matchingName == null)
            matchingName = resourceNames.FirstOrDefault(n =>
                n.EndsWith($".KniDeps.{simpleName}.dll", StringComparison.OrdinalIgnoreCase));

        if (matchingName == null)
            return null;

        try
        {
            using var stream = thisAssembly.GetManifestResourceStream(matchingName);
            if (stream == null) return null;
            var bytes = new byte[stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes, read, bytes.Length - read);
                if (n <= 0) break;
                read += n;
            }
            var ms = new MemoryStream(bytes);
            var asmDef = AssemblyDefinition.ReadAssembly(ms, new ReaderParameters { InMemory = true, ReadWrite = false });
            lock (_runtimeAsmLock)
            {
                _runtimeAsmCache[simpleName] = asmDef;
            }
            return asmDef;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rewrite AssemblyRefs + TypeRefs in the given assembly bytes.
    /// Returns the rewritten assembly bytes.
    /// </summary>
    public static byte[] Rewrite(byte[] assemblyBytes)
    {
        Console.WriteLine($"[AssemblyRefRewriter] Loading assembly ({assemblyBytes.Length:N0} bytes)");
        using var inputMs = new MemoryStream(assemblyBytes);
        var resolver = new RefAssemblyResolver();
        var parameters = new ReaderParameters { AssemblyResolver = resolver, InMemory = true };
        using var asmDef = AssemblyDefinition.ReadAssembly(inputMs, parameters);

        // Pass 1: rewrite AssemblyRef versions (v6→v8, MG v3.8.0.1641→v3.8.5.0)
        int refRewrites = 0;
        var assemblyRefs = asmDef.MainModule.AssemblyReferences;
        for (int i = 0; i < assemblyRefs.Count; i++)
        {
            var ar = assemblyRefs[i];
            var simpleName = ar.Name ?? "";

            Version? targetVersion = null;
            if (simpleName == "MonoGame.Framework")
                targetVersion = _monoGameFrameworkVersion;
            else if (_systemRefVersions.TryGetValue(simpleName, out var sysVersion))
                targetVersion = sysVersion;

            if (targetVersion != null && ar.Version != targetVersion)
            {
                Console.WriteLine($"[AssemblyRefRewriter] AssemblyRef {simpleName}: {ar.Version} → {targetVersion}");
                ar.Version = targetVersion;
                refRewrites++;
            }
        }
        Console.WriteLine($"[AssemblyRefRewriter] AssemblyRef rewrites: {refRewrites}");

        // Pass 2: rewrite TypeRef scopes to bypass broken type-forwards.
        // For each typeref pointing at a System.* facade, look up where the
        // type is actually forwarded to and rewrite the scope.
        // NOTE: We only iterate TYPE DEFINITIONS (not method bodies) because
        // iterating method bodies triggers lazy loading that causes Cecil to
        // try to resolve types it otherwise wouldn't, leading to spurious
        // ResolutionExceptions during Write.
        int typeRefRewrites = 0;
        var typeRefsToRewrite = new List<TypeReference>();
        foreach (var type in asmDef.MainModule.GetTypes())
        {
            CollectTypeRefs(type, typeRefsToRewrite);
            // Collect typerefs from base type, interfaces, fields (signature only)
            if (type.BaseType != null) CollectTypeRefs(type.BaseType, typeRefsToRewrite);
            foreach (var iface in type.Interfaces)
                CollectTypeRefs(iface.InterfaceType, typeRefsToRewrite);
            foreach (var field in type.Fields)
                CollectTypeRefs(field.FieldType, typeRefsToRewrite);
            foreach (var prop in type.Properties)
                CollectTypeRefs(prop.PropertyType, typeRefsToRewrite);
            foreach (var ev in type.Events)
                CollectTypeRefs(ev.EventType, typeRefsToRewrite);
            // Collect typerefs from method signatures + bodies (instructions)
            // We need this because some types (like ContentTypeReader`1) are only
            // referenced in method bodies, not in field/property signatures.
            foreach (var method in type.Methods)
            {
                if (method.ReturnType != null)
                    CollectTypeRefs(method.ReturnType, typeRefsToRewrite);
                foreach (var param in method.Parameters)
                    CollectTypeRefs(param.ParameterType, typeRefsToRewrite);
                if (method.HasBody)
                {
                    foreach (var local in method.Body.Variables)
                        CollectTypeRefs(local.VariableType, typeRefsToRewrite);
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.Operand is TypeReference tr)
                            CollectTypeRefs(tr, typeRefsToRewrite);
                        if (instr.Operand is MethodReference mr && mr.DeclaringType != null)
                            CollectTypeRefs(mr.DeclaringType, typeRefsToRewrite);
                        if (instr.Operand is FieldReference fr && fr.DeclaringType != null)
                            CollectTypeRefs(fr.DeclaringType, typeRefsToRewrite);
                    }
                }
            }
        }

        // Process ALL typerefs (no dedup — each TypeReference is a separate object
        // in the module's metadata, and we need to modify ALL of them, not just one
        // per unique (scope, fullname) pair. Dedup would miss duplicate typerefs
        // that point at the same type but are separate objects.)
        Console.WriteLine($"[AssemblyRefRewriter] Total typerefs to scan: {typeRefsToRewrite.Count}");
        int errors = 0;
        foreach (var tr in typeRefsToRewrite)
        {
            try
            {
                var rewritten = TryRewriteTypeRefScope(tr, resolver);
                if (rewritten) typeRefRewrites++;
            }
            catch (Exception ex)
            {
                errors++;
                if (errors <= 3)
                    Console.WriteLine($"[AssemblyRefRewriter] ERROR rewriting {tr.FullName} in {tr.Scope?.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        Console.WriteLine($"[AssemblyRefRewriter] TypeRef scope rewrites: {typeRefRewrites} ({errors} errors)");

        // Pass 3 (optional): patch GameRunner..ctor() for bisection debugging.
        // This is used to isolate which step in GameRunner..ctor() triggers the
        // Mono runtime assertion (exception.c:172, condition `method' not met).
        if (BisectMode > 0)
        {
            PatchGameRunnerCtor(asmDef);
        }

        // Pass 4: patch Program.get_sdk() to remove the SteamHelper branch.
        PatchProgramGetSdk(asmDef);

        // Pass 5: patch Game1..cctor() (static constructor) to be a no-op.
        // Game1's .cctor triggers TypeLoadException because it has fields with
        // types (Action`7, Lazy<T>, etc.) that were stripped from
        // System.Private.CoreLib by the BlazorWebAssembly trimmer. The
        // TypeLoadException then causes a Mono assertion (exception.c:172)
        // because the exception handler can't find the method.
        //
        // Fix: replace .cctor body with just `ret`. Game1's static fields
        // won't be initialized, but that's acceptable for Phase 2.8 — we
        // just need to get past the ctor to prove the game loop works.
        PatchGame1Cctor(asmDef);

        // Pass 6: rewrite high-arity Action/Func typerefs to use our replacement
        // delegate types. The BlazorWebAssembly trimmer strips Action`7..`16 and
        // Func`6..`17 from System.Private.CoreLib. We define equivalent delegates
        // in SdvWebPort.Vfs.DelegateReplacements and rewrite SDV's typerefs to
        // point at them.
        ReplaceMissingDelegates(asmDef);

        // Pass 7: patch out instructions in GameRunner..ctor() that reference
        // fields/methods KNI doesn't have. SDV was built against MonoGame.Framework
        // v3.8.0.1641; KNI v4.2.9001 is a fork with some API differences.
        //
        // Known-broken instructions:
        //   ldc.r4 0.001; stsfld SpriteBatch::TextureTuckAmount  (KNI removed this field)
        PatchGameRunnerCtorBrokenInstructions(asmDef);

        // Pass 8: rewrite stripped collection typerefs to use our replacement types.
        ReplaceMissingCollections(asmDef);

        // Pass 9: patch out method calls that fail at runtime due to KNI/MonoGame
        // API differences (e.g., add_TextInput event accessor).
        PatchBrokenMethodCalls(asmDef);

        using var outputMs = new MemoryStream();
        asmDef.Write(outputMs);
        var result = outputMs.ToArray();
        Console.WriteLine($"[AssemblyRefRewriter] Rewritten assembly: {result.Length:N0} bytes");
        return result;
    }

    /// <summary>
    /// Patch GameRunner..ctor() to skip steps after the bisection mode's cutoff.
    /// Used for debugging the Mono assertion.
    /// </summary>
    private static void PatchGameRunnerCtor(AssemblyDefinition asmDef)
    {
        var gameRunner = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.GameRunner");
        if (gameRunner == null)
        {
            Console.WriteLine($"[AssemblyRefRewriter] BisectMode {BisectMode}: GameRunner type not found");
            return;
        }
        var ctor = gameRunner.Methods.FirstOrDefault(m => m.Name == ".ctor");
        if (ctor == null)
        {
            Console.WriteLine($"[AssemblyRefRewriter] BisectMode {BisectMode}: GameRunner..ctor not found");
            return;
        }

        // IL offsets (from inspect-gamerunner.cs output):
        // IL_002D: call Game::.ctor  (after List inits)
        // IL_003C: ldsfld releaseBuild (after get_sdk + EarlyInitialize)
        // IL_005B: ldsfld graphics (after new GraphicsDeviceManager + stsfld)
        // IL_00B2: ldc.r4 0.001 (after all GDM property sets + Content.RootDirectory)
        // IL_00BC: call LocalMultiplayer::Initialize
        // IL_00C1: call ItemRegistry::RegisterItemTypes
        // IL_00D7: callvirt set_AllowUserResizing
        var patchAfterOffset = BisectMode switch
        {
            1 => 0x0032,  // after Game::.ctor returns (before get_sdk)
            2 => 0x00BC,  // before LocalMultiplayer::Initialize
            3 => 0x00C1,  // after LocalMultiplayer::Initialize (before ItemRegistry)
            4 => 0x00C6,  // after ItemRegistry::RegisterItemTypes
            5 => 0x00DC,  // after set_AllowUserResizing
            6 => 0x003C,  // after get_sdk + EarlyInitialize
            7 => 0x005B,  // after new GraphicsDeviceManager
            8 => 0x00B2,  // after GDM property sets + Content.RootDirectory
            _ => -1,
        };

        if (patchAfterOffset < 0) return;

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

        if (patchIndex < 0)
        {
            Console.WriteLine($"[AssemblyRefRewriter] BisectMode {BisectMode}: no instruction at IL_{patchAfterOffset:X4}");
            return;
        }

        Console.WriteLine($"[AssemblyRefRewriter] BisectMode {BisectMode}: removing from IL_{patchAfterOffset:X4} (idx {patchIndex}): {instrs[patchIndex].OpCode} {instrs[patchIndex].Operand}");
        while (instrs.Count > patchIndex)
            instrs.RemoveAt(instrs.Count - 1);
        instrs.Add(Instruction.Create(OpCodes.Ret));
        ctor.Body.ExceptionHandlers.Clear();
        Console.WriteLine($"[AssemblyRefRewriter] BisectMode {BisectMode}: patched to {instrs.Count} instructions");
    }

    /// <summary>
    /// Patch Program.get_sdk() to just `ldsfld _sdk; ret`.
    /// Removes the SteamHelper and NullSDKHelper fallback branches because:
    /// 1. We pre-set _sdk = NullSDKHelper via reflection before calling get_sdk()
    /// 2. Mono's JIT eagerly resolves type refs in dead branches, causing
    ///    TypeLoadException for SteamHelper (which has Steamworks.NET fields)
    /// </summary>
    private static void PatchProgramGetSdk(AssemblyDefinition asmDef)
    {
        var program = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Program");
        if (program == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] Program type not found — skipping get_sdk patch");
            return;
        }
        var getSdk = program.Methods.FirstOrDefault(m => m.Name == "get_sdk");
        if (getSdk == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] Program.get_sdk not found — skipping patch");
            return;
        }

        // Find the _sdk field reference (manual loop — avoid LINQ FirstOrDefault
        // because the trimmer may strip the generic method)
        FieldReference? sdkField = null;
        foreach (var ins in getSdk.Body.Instructions)
        {
            if (ins.Operand is FieldReference fr && fr.Name == "_sdk")
            {
                sdkField = fr;
                break;
            }
        }

        if (sdkField == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] _sdk field not found in get_sdk — skipping patch");
            return;
        }

        // Rewrite: ldsfld _sdk; ret
        var instrs = getSdk.Body.Instructions;
        instrs.Clear();
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, sdkField));
        instrs.Add(Instruction.Create(OpCodes.Ret));
        getSdk.Body.ExceptionHandlers.Clear();

        Console.WriteLine($"[AssemblyRefRewriter] Patched Program.get_sdk() → ldsfld _sdk; ret (removed SteamHelper branch)");
    }

    /// <summary>
    /// Patch Game1..cctor() (static constructor) to be a no-op (just `ret`).
    /// Game1's .cctor fails because it has fields with types (Action`7, Lazy<T>,
    /// etc.) that were stripped from System.Private.CoreLib by the trimmer.
    /// The resulting TypeLoadException causes a Mono assertion.
    /// </summary>
    private static void PatchGame1Cctor(AssemblyDefinition asmDef)
    {
        var game1 = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1 == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] Game1 type not found — skipping .cctor patch");
            return;
        }
        var cctor = game1.Methods.FirstOrDefault(m => m.Name == ".cctor");
        if (cctor == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] Game1..cctor not found — skipping patch");
            return;
        }

        var instrs = cctor.Body.Instructions;
        instrs.Clear();
        instrs.Add(Instruction.Create(OpCodes.Ret));
        cctor.Body.ExceptionHandlers.Clear();

        Console.WriteLine($"[AssemblyRefRewriter] Patched Game1..cctor() → ret (no-op, {instrs.Count} instructions)");
    }

    /// <summary>
    /// Patch out instructions in GameRunner..ctor() that reference fields/methods
    /// KNI doesn't have. SDV was built against MonoGame.Framework v3.8.0.1641;
    /// KNI v4.2.9001 is a fork with some API differences.
    ///
    /// Known-broken instructions (removed by setting to Nop):
    ///   ldc.r4 0.001; stsfld SpriteBatch::TextureTuckAmount
    ///     — KNI removed this static field. We Nop both instructions to skip the
    ///       field set. The field is a render tweak; skipping it is harmless.
    /// </summary>
    private static void PatchGameRunnerCtorBrokenInstructions(AssemblyDefinition asmDef)
    {
        var gameRunner = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.GameRunner");
        if (gameRunner == null) return;
        var ctor = gameRunner.Methods.FirstOrDefault(m => m.Name == ".ctor");
        if (ctor == null) return;

        var instrs = ctor.Body.Instructions;
        int patched = 0;
        for (int i = 0; i < instrs.Count; i++)
        {
            var ins = instrs[i];
            // Look for: stsfld SpriteBatch::TextureTuckAmount
            if (ins.OpCode == OpCodes.Stsfld && ins.Operand is FieldReference fr
                && fr.Name == "TextureTuckAmount"
                && fr.DeclaringType?.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch")
            {
                ins.OpCode = OpCodes.Nop;
                ins.Operand = null;
                patched++;
                if (i > 0 && instrs[i - 1].OpCode == OpCodes.Ldc_R4)
                {
                    instrs[i - 1].OpCode = OpCodes.Nop;
                    instrs[i - 1].Operand = null;
                    patched++;
                }
                Console.WriteLine($"[AssemblyRefRewriter] Patched out stsfld SpriteBatch::TextureTuckAmount");
            }
        }
        if (patched > 0)
            Console.WriteLine($"[AssemblyRefRewriter] GameRunner..ctor() broken-instruction patches: {patched}");
    }

    /// <summary>
    /// Patch out method calls that fail at runtime due to KNI/MonoGame API differences.
    /// These are calls that compile fine but fail at runtime with MissingMethodException
    /// because the method signature or declaring type doesn't match exactly.
    ///
    /// Known-broken calls:
    ///   GameWindow::add_TextInput — KNI has the method but the runtime can't find it
    ///     (likely due to EventHandler<T> scope mismatch). We Nop the call + its args.
    /// </summary>
    private static void PatchBrokenMethodCalls(AssemblyDefinition asmDef)
    {
        int patched = 0;
        foreach (var type in asmDef.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                var instrs = method.Body.Instructions;
                for (int i = 0; i < instrs.Count; i++)
                {
                    var ins = instrs[i];
                    if (ins.OpCode != OpCodes.Call && ins.OpCode != OpCodes.Callvirt) continue;
                    if (ins.Operand is not MethodReference mr) continue;

                    // Patch out add_TextInput / remove_TextInput calls
                    // These are event accessors: callvirt GameWindow::add_TextInput(EventHandler<TextInputEventArgs>)
                    // Stack before call: [this_GameWindow] [delegate] → void
                    // Replace callvirt with 2x pop to consume the 2 stack items
                    if (mr.Name == "add_TextInput" || mr.Name == "remove_TextInput")
                    {
                        Console.WriteLine($"[AssemblyRefRewriter] Patching out {mr.Name} in {type.FullName}::{method.Name} (replaced with pop;pop)");
                        // Replace the call with pop; pop (consume this + delegate)
                        var pop1 = Instruction.Create(OpCodes.Pop);
                        var pop2 = Instruction.Create(OpCodes.Pop);
                        ins.OpCode = pop1.OpCode;
                        ins.Operand = null;
                        // Insert pop2 after the current instruction
                        instrs.Insert(i + 1, pop2);
                        patched++;
                        i++; // skip the inserted instruction
                    }
                }
            }
        }
        if (patched > 0)
            Console.WriteLine($"[AssemblyRefRewriter] Broken method call patches: {patched}");
    }

    /// <summary>
    /// Rewrite high-arity Action/Func typerefs to use our replacement delegate types.
    /// The BlazorWebAssembly trimmer strips Action`7..`16 and Func`6+1..`16+1 from
    /// System.Private.CoreLib. We define equivalent delegates in
    /// SdvWebPort.Vfs.DelegateReplacements and rewrite SDV's typerefs to point at them.
    /// </summary>
    private static void ReplaceMissingDelegates(AssemblyDefinition asmDef)
    {
        // Find SdvWebPort.Vfs assembly in AppDomain to import replacement types
        var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
        if (vfsAsm == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] SdvWebPort.Vfs not loaded — skipping delegate replacement");
            return;
        }

        var replacementNamespace = "SdvWebPort.Vfs.DelegateReplacements";
        var module = asmDef.MainModule;

        // Build a cache of replacement TypeReferences by (delegateName, arity)
        // delegateName is "Action" or "Func"
        // arity is the number of generic parameters
        var replacementCache = new Dictionary<(string Name, int Arity), TypeReference>();

        TypeReference? GetReplacement(string name, int arity)
        {
            var key = (name, arity);
            lock (replacementCache)
            {
                if (replacementCache.TryGetValue(key, out var cached))
                    return cached;
            }

            // Build the full type name: e.g., "SdvWebPort.Vfs.DelegateReplacements.Action`7"
            var fullName = replacementNamespace + "." + name + "`" + arity.ToString();
            var replacementType = vfsAsm.GetType(fullName);
            if (replacementType == null)
                return null;

            var typeRef = module.ImportReference(replacementType);
            lock (replacementCache)
            {
                replacementCache[key] = typeRef;
            }
            return typeRef;
        }

        int rewrites = 0;

        // Walk all typerefs in the module. We need to find GenericInstanceType
        // instances whose ElementType is a TypeRef pointing at System.Action`N or
        // System.Func`N with N in the stripped range.
        //
        // Stripped ranges (from WASM CoreLib diagnostic):
        //   Action: arity 7-16 (Action`7 through Action`16)
        //   Func: arity 6-17 (Func`6 through Func`17) — Func`N has N args + TResult
        //
        // We collect from: type fields, base types, interfaces, method signatures,
        // method bodies (instructions with TypeReference operands).
        var genericInstancesToRewrite = new List<GenericInstanceType>();

        foreach (var type in module.GetTypes())
        {
            // Fields
            foreach (var field in type.Fields)
                CollectGenericInstances(field.FieldType, genericInstancesToRewrite);
            // Base type + interfaces
            if (type.BaseType != null)
                CollectGenericInstances(type.BaseType, genericInstancesToRewrite);
            foreach (var iface in type.Interfaces)
                CollectGenericInstances(iface.InterfaceType, genericInstancesToRewrite);
            // Properties + events
            foreach (var prop in type.Properties)
                CollectGenericInstances(prop.PropertyType, genericInstancesToRewrite);
            foreach (var ev in type.Events)
                CollectGenericInstances(ev.EventType, genericInstancesToRewrite);
            // Methods: return type, parameter types, body instructions
            foreach (var method in type.Methods)
            {
                if (method.ReturnType != null)
                    CollectGenericInstances(method.ReturnType, genericInstancesToRewrite);
                foreach (var param in method.Parameters)
                    CollectGenericInstances(param.ParameterType, genericInstancesToRewrite);
                if (method.HasBody)
                {
                    foreach (var instr in method.Body.Instructions)
                    {
                        if (instr.Operand is TypeReference tr)
                            CollectGenericInstances(tr, genericInstancesToRewrite);
                        if (instr.Operand is MethodReference mr && mr.DeclaringType != null)
                            CollectGenericInstances(mr.DeclaringType, genericInstancesToRewrite);
                        if (instr.Operand is FieldReference fr && fr.DeclaringType != null)
                            CollectGenericInstances(fr.DeclaringType, genericInstancesToRewrite);
                    }
                    foreach (var local in method.Body.Variables)
                        CollectGenericInstances(local.VariableType, genericInstancesToRewrite);
                }
            }
        }

        // Dedupe by reference identity (each GenericInstanceType is unique)
        var seen = new HashSet<GenericInstanceType>();
        foreach (var git in genericInstancesToRewrite)
        {
            if (!seen.Add(git)) continue;

            // Check if this is a stripped delegate type
            var elementType = git.ElementType;
            if (elementType == null) continue;

            string? delegateName = null;
            int arity = git.GenericArguments.Count;

            // elementType.FullName for Action`7 is "System.Action`1" ... no wait,
            // for a generic instance, ElementType is the OPEN generic type.
            // For System.Action`7, ElementType.FullName is "System.Action`7".
            var elementFullName = elementType.FullName ?? "";

            if (elementFullName == "System.Action`7" || elementFullName == "System.Action`8" ||
                elementFullName == "System.Action`9" || elementFullName == "System.Action`10" ||
                elementFullName == "System.Action`11" || elementFullName == "System.Action`12" ||
                elementFullName == "System.Action`13" || elementFullName == "System.Action`14" ||
                elementFullName == "System.Action`15" || elementFullName == "System.Action`16")
            {
                delegateName = "Action";
                // arity should match the `N in the name
            }
            else if (elementFullName == "System.Func`6" || elementFullName == "System.Func`7" ||
                     elementFullName == "System.Func`8" || elementFullName == "System.Func`9" ||
                     elementFullName == "System.Func`10" || elementFullName == "System.Func`11" ||
                     elementFullName == "System.Func`12" || elementFullName == "System.Func`13" ||
                     elementFullName == "System.Func`14" || elementFullName == "System.Func`15" ||
                     elementFullName == "System.Func`16" || elementFullName == "System.Func`17")
            {
                delegateName = "Func";
            }
            else
                continue;

            // Get replacement type (just to verify it exists)
            var replacement = GetReplacement(delegateName, arity);
            if (replacement == null)
            {
                Console.WriteLine($"[AssemblyRefRewriter] WARN: no replacement for {delegateName}`{arity}");
                continue;
            }

            // The git.ElementType may be a different object than the typeref
            // stored in the module's metadata. We need to find the ACTUAL
            // TypeReference in module.GetTypeReferences() that matches, and
            // modify THAT one.
            //
            // Actually, a simpler approach: the module's GetTypeReferences()
            // returns all typerefs. We modify ALL that match System.Action`N
            // or System.Func`N in the stripped range. This is done ONCE, not
            // per-generic-instance.
            // (Handled below, after the loop)
        }

        // Now do the actual modification: walk module.GetTypeReferences() and
        // rewrite any System.Action`N / System.Func`N in the stripped range.
        var vfsAsmRef = module.AssemblyReferences.FirstOrDefault(a => a.Name == "SdvWebPort.Vfs");
        if (vfsAsmRef == null)
        {
            vfsAsmRef = new AssemblyNameReference("SdvWebPort.Vfs", new Version(1, 0, 0, 0))
            {
                PublicKeyToken = null!,
            };
            module.AssemblyReferences.Add(vfsAsmRef);
        }

        foreach (var tr in module.GetTypeReferences())
        {
            var fn = tr.FullName ?? "";
            // System.Action`N — stripped for N=7..16
            if (fn.StartsWith("System.Action`"))
            {
                var arityStr = fn.Substring("System.Action`".Length);
                if (int.TryParse(arityStr, out var n) && n >= 7 && n <= 16)
                {
                    var oldNs = tr.Namespace;
                    var oldScope = tr.Scope?.Name;
                    tr.Namespace = replacementNamespace;
                    tr.Scope = vfsAsmRef;
                    rewrites++;
                    if (rewrites <= 5)
                        Console.WriteLine($"[AssemblyRefRewriter] TypeRef {fn}: scope {oldScope} → SdvWebPort.Vfs (ns {oldNs} → {replacementNamespace})");
                }
            }
            // System.Func`N — stripped for N=6..17
            else if (fn.StartsWith("System.Func`"))
            {
                var arityStr = fn.Substring("System.Func`".Length);
                if (int.TryParse(arityStr, out var n) && n >= 6 && n <= 17)
                {
                    var oldNs = tr.Namespace;
                    var oldScope = tr.Scope?.Name;
                    tr.Namespace = replacementNamespace;
                    tr.Scope = vfsAsmRef;
                    rewrites++;
                    if (rewrites <= 5)
                        Console.WriteLine($"[AssemblyRefRewriter] TypeRef {fn}: scope {oldScope} → SdvWebPort.Vfs (ns {oldNs} → {replacementNamespace})");
                }
            }
        }

        Console.WriteLine($"[AssemblyRefRewriter] Delegate replacements: {rewrites}");
    }

    /// <summary>
    /// Rewrite stripped collection typerefs to use our replacement types.
    /// The BlazorWebAssembly trimmer strips types from System.Collections.wasm
    /// (Stack&lt;T&gt;, SortedSet&lt;T&gt;, etc.) even though they're defined there in source.
    /// We define equivalent types in SdvWebPort.Vfs.CollectionReplacements.
    ///
    /// Like ReplaceMissingDelegates, we walk module.GetTypeReferences() and
    /// rewrite matching typerefs' namespace + scope.
    /// </summary>
    private static void ReplaceMissingCollections(AssemblyDefinition asmDef)
    {
        var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
        if (vfsAsm == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] SdvWebPort.Vfs not loaded — skipping collection replacement");
            return;
        }

        var replacementNamespace = "SdvWebPort.Vfs.CollectionReplacements";
        var module = asmDef.MainModule;

        // Map of (namespace, name) → replacement arity
        // These are the collection types stripped from WASM System.Collections
        var strippedTypes = new HashSet<string>
        {
            "System.Collections.Generic.Stack`1",
            "System.Collections.Generic.SortedSet`1",
            "System.Collections.Generic.LinkedList`1",
            "System.Collections.Generic.SortedDictionary`2",
            "System.Collections.Generic.SortedList`2",
            "System.Collections.ObjectModel.ObservableCollection`1",
            "System.Collections.Concurrent.ConcurrentDictionary`2",
            "System.Collections.Concurrent.ConcurrentStack`1",
            "System.Collections.Concurrent.ConcurrentBag`1",
            "System.Collections.Concurrent.ConcurrentQueue`1",
        };

        // Find or create the SdvWebPort.Vfs AssemblyNameReference
        var vfsAsmRef = module.AssemblyReferences.FirstOrDefault(a => a.Name == "SdvWebPort.Vfs");
        if (vfsAsmRef == null)
        {
            vfsAsmRef = new AssemblyNameReference("SdvWebPort.Vfs", new Version(1, 0, 0, 0))
            {
                PublicKeyToken = null!,
            };
            module.AssemblyReferences.Add(vfsAsmRef);
        }

        int rewrites = 0;
        foreach (var tr in module.GetTypeReferences())
        {
            var fn = tr.FullName ?? "";
            if (!strippedTypes.Contains(fn)) continue;

            // Verify replacement exists
            var replacementFullName = replacementNamespace + "." + tr.Name;
            var replacementType = vfsAsm.GetType(replacementFullName);
            if (replacementType == null)
            {
                Console.WriteLine($"[AssemblyRefRewriter] WARN: no replacement collection for {fn}");
                continue;
            }

            var oldNs = tr.Namespace;
            var oldScope = tr.Scope?.Name;
            tr.Namespace = replacementNamespace;
            tr.Scope = vfsAsmRef;
            rewrites++;
            if (rewrites <= 10)
                Console.WriteLine($"[AssemblyRefRewriter] Collection {fn}: scope {oldScope} → SdvWebPort.Vfs (ns {oldNs} → {replacementNamespace})");
        }

        Console.WriteLine($"[AssemblyRefRewriter] Collection replacements: {rewrites}");
    }

    private static void CollectGenericInstances(TypeReference tr, List<GenericInstanceType> list)
    {
        if (tr == null) return;
        if (tr is GenericInstanceType git)
        {
            list.Add(git);
            // Recurse into generic arguments
            foreach (var arg in git.GenericArguments)
                CollectGenericInstances(arg, list);
            // Recurse into element type
            if (git.ElementType != null)
                CollectGenericInstances(git.ElementType, list);
        }
        else if (tr is TypeSpecification spec)
        {
            CollectGenericInstances(spec.ElementType, list);
        }
        if (tr.DeclaringType != null)
            CollectGenericInstances(tr.DeclaringType, list);
    }

    private static void CollectTypeRefs(TypeReference tr, List<TypeReference> list)
    {
        if (tr == null) return;
        list.Add(tr);
        // Recurse into element type for ALL TypeSpecifications (arrays, byrefs,
        // pointers, AND generic instances). For GenericInstanceType, the ElementType
        // is the open generic (e.g., Lazy`1) — we need to rewrite its scope too.
        if (tr is TypeSpecification spec)
        {
            CollectTypeRefs(spec.ElementType, list);
        }
        // Recurse into generic arguments
        if (tr is GenericInstanceType git)
        {
            foreach (var arg in git.GenericArguments)
                CollectTypeRefs(arg, list);
        }
        // Recurse into declaring type (for nested types)
        if (tr.DeclaringType != null)
            CollectTypeRefs(tr.DeclaringType, list);
    }

    /// <summary>
    /// If the typeref points at a System.* facade assembly, look up where the
    /// type is actually forwarded to (using the embedded ref assemblies) and
    /// rewrite the typeref's scope to point at the target assembly.
    ///
    /// Returns true if the scope was rewritten.
    /// </summary>
    private static bool TryRewriteTypeRefScope(TypeReference tr, RefAssemblyResolver resolver)
    {
        // Diagnostic: log ContentTypeReader + TextInput typerefs (BEFORE the TypeSpecification skip)
        if (tr.FullName.Contains("ContentTypeReader") || tr.FullName.Contains("TextInputEventArgs"))
        {
            Console.WriteLine($"[AssemblyRefRewriter] TryRewriteTypeRefScope ENTER: {tr.FullName} scope={tr.Scope?.Name} isSpec={tr is TypeSpecification} type={tr.GetType().Name}");
        }

        // Skip TypeSpecification (arrays, byrefs, pointers, generics) — their
        // scope is derived from their element type. Modifying the element type
        // (which we also collect and rewrite) automatically updates the spec's scope.
        if (tr is TypeSpecification) return false;

        if (tr.Scope is not AssemblyNameReference scopeAsm) return false;
        var scopeName = scopeAsm.Name;

        // Rewrite typerefs pointing at:
        // 1. System.* facades (trimmer strips type-forwards)
        // 2. MonoGame.Framework facade (our facade — TypeForwardedTo may be stripped by trimmer)
        bool isSystemFacade = scopeName.StartsWith("System.") && scopeName != "System.Private.CoreLib";
        bool isMonoGameFacade = scopeName == "MonoGame.Framework";
        if (!isSystemFacade && !isMonoGameFacade)
            return false;

        // Look up where the type is actually forwarded to.
        var targetScope = ResolveForwardedScope(scopeName, tr.FullName, resolver);
        if (targetScope == null)
        {
            // Log first 5 unresolved typerefs for diagnostic purposes
            if (_unresolvedTypeRefLogs < 5)
            {
                Console.WriteLine($"[AssemblyRefRewriter] TypeRef {tr.FullName} in {scopeName}: NO FORWARD FOUND (not in runtime ExportedTypes)");
                _unresolvedTypeRefLogs++;
            }
            return false;
        }
        if (targetScope == scopeName)
            return false;

        // Find or create the target AssemblyNameReference in the module.
        var module = tr.Module;
        if (module == null) return false;
        var targetAsmRef = module.AssemblyReferences.FirstOrDefault(a => a.Name == targetScope);
        if (targetAsmRef == null)
        {
            // Create a new AssemblyNameReference for the target.
            var pkt = scopeAsm.PublicKeyToken;
            var version = scopeAsm.Version ?? new Version(8, 0, 0, 0);
            if (targetScope == "System.Private.CoreLib")
            {
                pkt = new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e };
                version = new Version(8, 0, 0, 0);
            }
            else if (targetScope.StartsWith("Xna.Framework"))
            {
                // KNI assemblies: v4.2.9001.0, null PKT
                pkt = null!;
                version = new Version(4, 2, 9001, 0);
            }
            targetAsmRef = new AssemblyNameReference(targetScope, version)
            {
                PublicKeyToken = pkt,
                Culture = scopeAsm.Culture,
            };
            module.AssemblyReferences.Add(targetAsmRef);
        }

        if (_rewrittenTypeRefLogs < 10)
        {
            Console.WriteLine($"[AssemblyRefRewriter] TypeRef {tr.FullName}: scope {scopeName} → {targetScope}");
            _rewrittenTypeRefLogs++;
        }
        tr.Scope = targetAsmRef;

        // Also check if the type's namespace needs to be updated (KNI may have
        // moved the type to a different namespace than MonoGame had).
        // E.g., TextInputEventArgs moved from Microsoft.Xna.Framework to
        // Microsoft.Xna.Framework.Input.
        if (targetScope.StartsWith("Xna.Framework"))
        {
            var targetAsm = LoadRuntimeAssembly(targetScope);
            if (targetAsm != null)
            {
                var typeName = tr.Name;
                var correctType = targetAsm.MainModule.Types.FirstOrDefault(t => t.Name == typeName);
                if (correctType != null && correctType.Namespace != tr.Namespace)
                {
                    Console.WriteLine($"[AssemblyRefRewriter] Namespace fix: {tr.FullName} → {correctType.FullName}");
                    tr.Namespace = correctType.Namespace;
                }
            }
        }

        return true;
    }

    private static int _unresolvedTypeRefLogs = 0;
    private static int _rewrittenTypeRefLogs = 0;

    /// <summary>
    /// Resolve where a type is forwarded to, starting from the given source
    /// assembly. Walks the type-forward chain through the embedded RUNTIME
    /// assemblies (which have type-forwards, unlike ref assemblies which have
    /// direct type defs).
    ///
    /// Returns the simple name of the assembly where the type is actually
    /// forwarded to (the final target of the chain), or null if not found
    /// or if the type is defined directly in the source assembly.
    /// </summary>
    private static string? ResolveForwardedScope(string sourceAsmName, string typeFullName, RefAssemblyResolver resolver)
    {
        var key = (sourceAsmName, typeFullName);
        lock (_forwardCache)
        {
            if (_forwardCache.TryGetValue(key, out var cached))
                return cached.TargetAsm == sourceAsmName ? null : cached.TargetAsm;
        }

        // Diagnostic: log ContentTypeReader + TextInput resolution
        if (typeFullName.Contains("ContentTypeReader") || typeFullName.Contains("TextInputEventArgs"))
        {
            Console.WriteLine($"[AssemblyRefRewriter] ResolveForwardedScope: source={sourceAsmName}, type={typeFullName}");
        }

        var visited = new HashSet<string>();
        var currentAsm = sourceAsmName;
        string? result = null;

        while (visited.Add(currentAsm))
        {
            // Diagnostic for TextInputEventArgs
            if (typeFullName.Contains("TextInputEventArgs"))
            {
                Console.WriteLine($"[AssemblyRefRewriter]   ResolveForwardedScope loop: currentAsm={currentAsm}");
            }

            // Use the RUNTIME assembly (has type-forwards), not the ref assembly
            // (which has direct type defs — Cecil's resolver uses those).
            var asmDef = LoadRuntimeAssembly(currentAsm);
            if (typeFullName.Contains("TextInputEventArgs"))
            {
                Console.WriteLine($"[AssemblyRefRewriter]   LoadRuntimeAssembly({currentAsm}) = {(asmDef == null ? "NULL" : asmDef.FullName)}");
            }
            if (asmDef == null)
            {
                // No runtime assembly for this source. If this is the INITIAL
                // source assembly, fall back to CoreLib. If we got here by
                // following a forward, trust the forward target — the runtime
                // will resolve it.
                if (currentAsm == sourceAsmName)
                    result = "System.Private.CoreLib";
                else
                    result = currentAsm;
                break;
            }

            // Check if the type is DEFINED DIRECTLY in this assembly (Types).
            var directMatch = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == typeFullName);
            if (typeFullName.Contains("TextInputEventArgs"))
            {
                Console.WriteLine($"[AssemblyRefRewriter]   directMatch = {(directMatch == null ? "NULL" : directMatch.FullName)}");
            }
            if (directMatch != null)
            {
                result = currentAsm;
                break;
            }

            // Check if the type is FORWARDED (ExportedTypes).
            var exported = asmDef.MainModule.ExportedTypes.FirstOrDefault(et => et.FullName == typeFullName);
            if (typeFullName.Contains("TextInputEventArgs"))
            {
                Console.WriteLine($"[AssemblyRefRewriter]   exported = {(exported == null ? "NULL" : exported.FullName + " -> " + exported.Scope?.Name)}");
                Console.WriteLine($"[AssemblyRefRewriter]   ExportedTypes count = {asmDef.MainModule.ExportedTypes.Count}");
                // List first 5 exported types for diagnostic
                foreach (var et2 in asmDef.MainModule.ExportedTypes.Take(5))
                    Console.WriteLine($"[AssemblyRefRewriter]     - {et2.FullName} -> {et2.Scope?.Name}");
            }
            if (exported == null)
            {
                // For open generic types (e.g., ContentTypeReader`1), the facade's
                // generator script skips them (TypeForwardedTo can't forward open
                // generics). Try matching by stripping the `N suffix — if the
                // non-generic version is forwarded, the generic version is in the
                // SAME target assembly.
                var tickIdx = typeFullName.IndexOf('`');
                if (tickIdx > 0)
                {
                    var baseName = typeFullName.Substring(0, tickIdx);
                    var baseExported = asmDef.MainModule.ExportedTypes.FirstOrDefault(et => et.FullName == baseName);
                    if (baseExported != null)
                    {
                        exported = baseExported;
                        Console.WriteLine($"[AssemblyRefRewriter] Open generic {typeFullName} → using non-generic forward target {baseExported.Scope?.Name}");
                    }
                }
            }
            if (exported != null)
            {
                // Forwarded to another assembly — follow the chain
                var forwardTarget = exported.Scope?.Name;
                if (forwardTarget == null || forwardTarget == currentAsm)
                {
                    result = currentAsm;
                    break;
                }
                currentAsm = forwardTarget;
                continue;
            }

            // Type not found in this assembly's Types or ExportedTypes.
            // For MonoGame.Framework source, search KNI Xna.Framework.* assemblies
            // directly (the facade may be missing the forward for some types).
            if (currentAsm == "MonoGame.Framework")
            {
                var kniTarget = SearchKniAssembliesForType(typeFullName);
                if (kniTarget != null)
                {
                    Console.WriteLine($"[AssemblyRefRewriter] MG type {typeFullName} found in KNI assembly: {kniTarget}");
                    result = kniTarget;
                    break;
                }
            }

            // If this is the INITIAL source, fall back to CoreLib (most System.*
            // types are there). Otherwise, trust the current assembly as the
            // target (the forward chain led us here — the type should be defined
            // here even if we can't verify because we don't have the runtime assembly).
            if (currentAsm == sourceAsmName)
                result = "System.Private.CoreLib";
            else
                result = currentAsm;
            break;
        }

        lock (_forwardCache)
        {
            _forwardCache[key] = (result ?? sourceAsmName, null);
        }
        return result == sourceAsmName ? null : result;
    }

    /// <summary>
    /// Search all KNI Xna.Framework.* assemblies for a type by full name.
    /// Returns the assembly simple name if found, null otherwise.
    /// Used as a fallback for MonoGame.Framework types that aren't in the facade's
    /// ExportedTypes (e.g., open generics that the generator script skipped).
    /// Also searches by type NAME (ignoring namespace) to handle cases where
    /// KNI moved a type to a different namespace (e.g., TextInputEventArgs
    /// moved from Microsoft.Xna.Framework to Microsoft.Xna.Framework.Input).
    /// </summary>
    private static string? SearchKniAssembliesForType(string typeFullName)
    {
        string[] kniAssemblies = new[]
        {
            "Xna.Framework",
            "Xna.Framework.Game",
            "Xna.Framework.Graphics",
            "Xna.Framework.Content",
            "Xna.Framework.Input",
            "Xna.Framework.Audio",
            "Xna.Framework.Media",
            "Xna.Framework.XR",
        };
        // Extract the type name (without namespace) for fuzzy matching
        var typeName = typeFullName;
        var lastDot = typeFullName.LastIndexOf('.');
        if (lastDot >= 0)
            typeName = typeFullName.Substring(lastDot + 1);

        foreach (var name in kniAssemblies)
        {
            var asmDef = LoadRuntimeAssembly(name);
            if (asmDef == null) continue;
            // Check direct types — exact full name match first
            var direct = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == typeFullName);
            if (direct != null)
                return name;
            // Fuzzy match by type name only (handles namespace changes between MG and KNI)
            var fuzzy = asmDef.MainModule.Types.FirstOrDefault(t => t.Name == typeName);
            if (fuzzy != null)
            {
                Console.WriteLine($"[AssemblyRefRewriter] Fuzzy match: {typeFullName} → {fuzzy.FullName} in {name}");
                return name;
            }
        }
        return null;
    }
}
