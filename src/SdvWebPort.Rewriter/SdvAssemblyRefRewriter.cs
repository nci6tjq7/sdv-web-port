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
            // Collect typerefs from base type, interfaces, fields (signature only — not method bodies)
            if (type.BaseType != null) CollectTypeRefs(type.BaseType, typeRefsToRewrite);
            foreach (var iface in type.Interfaces)
                CollectTypeRefs(iface.InterfaceType, typeRefsToRewrite);
            foreach (var field in type.Fields)
                CollectTypeRefs(field.FieldType, typeRefsToRewrite);
            foreach (var prop in type.Properties)
                CollectTypeRefs(prop.PropertyType, typeRefsToRewrite);
            foreach (var ev in type.Events)
                CollectTypeRefs(ev.EventType, typeRefsToRewrite);
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

        var visited = new HashSet<string>();
        var currentAsm = sourceAsmName;
        string? result = null;

        while (visited.Add(currentAsm))
        {
            // Use the RUNTIME assembly (has type-forwards), not the ref assembly
            // (which has direct type defs — Cecil's resolver uses those).
            var asmDef = LoadRuntimeAssembly(currentAsm);
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
            if (directMatch != null)
            {
                result = currentAsm;
                break;
            }

            // Check if the type is FORWARDED (ExportedTypes).
            var exported = asmDef.MainModule.ExportedTypes.FirstOrDefault(et => et.FullName == typeFullName);
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
}
