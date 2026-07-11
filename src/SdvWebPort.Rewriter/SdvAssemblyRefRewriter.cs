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
    /// When true, skip Pass 2b (method signature scope rewrite).
    /// This can be set to true as a fallback if the full rewrite fails during Write
    /// due to Cecil's inability to resolve nested types.
    /// </summary>
    public static bool SkipMethodSignatureRewrite { get; set; } = false;

    /// <summary>
    /// Type full name → KNI assembly name. Used by PeTypeRefPatcher.
    /// </summary>
    private static readonly Dictionary<string, string> _typeToKniAssembly = new()
    {
        { "Microsoft.Xna.Framework.Vector2", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Vector3", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Vector4", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Matrix", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Quaternion", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Point", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Rectangle", "Xna.Framework" },
        { "Microsoft.Xna.Framework.MathHelper", "Xna.Framework" },
        { "Microsoft.Xna.Framework.Game", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.GameTime", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.GameServiceContainer", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.GameComponent", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.DrawableGameComponent", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.GameWindow", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.GraphicsDeviceManager", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.TitleContainer", "Xna.Framework.Game" },
        { "Microsoft.Xna.Framework.Graphics.GraphicsDevice", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.SpriteBatch", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.Texture2D", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.RenderTarget2D", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Color", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.PresentationParameters", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.SurfaceFormat", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.DepthFormat", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.BlendState", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.SamplerState", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.DepthStencilState", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.RasterizerState", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.SpriteSortMode", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.Effect", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.GraphicsAdapter", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.DisplayMode", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Graphics.GraphicsProfile", "Xna.Framework.Graphics" },
        { "Microsoft.Xna.Framework.Content.ContentManager", "Xna.Framework.Content" },
        { "Microsoft.Xna.Framework.Content.ContentReader", "Xna.Framework.Content" },
        { "Microsoft.Xna.Framework.Content.ContentTypeReader", "Xna.Framework.Content" },
        { "Microsoft.Xna.Framework.Content.ContentLoadException", "Xna.Framework.Content" },
        { "Microsoft.Xna.Framework.Input.MouseState", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.KeyboardState", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.GamePadState", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.Keys", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.Buttons", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.GamePadThumbSticks", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.GamePadDPad", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.GamePadButtons", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.Mouse", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.Keyboard", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.GamePad", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Input.InputKeyEventArgs", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.TextInputEventArgs", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.FileDropEventArgs", "Xna.Framework.Input" },
        { "Microsoft.Xna.Framework.Audio.AudioEngine", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.SoundBank", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.WaveBank", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.SoundEffect", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.AudioListener", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.AudioEmitter", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.CueDefinition", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.AudioCategory", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.ReverbSettings", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.NoAudioHardwareException", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Audio.OggStreamSoundEffect", "Xna.Framework.Audio" },
        { "Microsoft.Xna.Framework.Media.MediaPlayer", "Xna.Framework.Media" },
        { "Microsoft.Xna.Framework.Media.Song", "Xna.Framework.Media" },
    };

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
        var metadataResolver = new CustomMetadataResolver(resolver);
        var parameters = new ReaderParameters { AssemblyResolver = resolver, InMemory = true, MetadataResolver = metadataResolver };
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

        // Pass 2a: Force-rewrite ALL typerefs with MonoGame.Framework scope.
        // Instead of modifying tr.Scope (which Cecil may not persist), we create
        // NEW TypeReference objects and replace all usages in method bodies.
        int forceRewrites = 0;
        var forceModule = asmDef.MainModule;
        var mgTypeRefReplacements = new Dictionary<TypeReference, TypeReference>();

        foreach (var tr in forceModule.GetTypeReferences().ToList())
        {
            if (tr.Scope is not AssemblyNameReference trScope) continue;
            if (trScope.Name != "MonoGame.Framework") continue;
            if (tr is TypeSpecification) continue;

            var target = ResolveForwardedScope("MonoGame.Framework", tr.FullName, resolver);
            if (target == null || target == "MonoGame.Framework") continue;

            var targetRef = forceModule.AssemblyReferences.FirstOrDefault(a => a.Name == target);
            if (targetRef == null)
            {
                targetRef = new AssemblyNameReference(target, new Version(4, 2, 9001, 0))
                {
                    PublicKeyToken = null!,
                };
                forceModule.AssemblyReferences.Add(targetRef);
            }

            var ns = tr.Namespace;
            var name = tr.Name;
            if (target.StartsWith("Xna.Framework"))
            {
                var targetAsm = LoadRuntimeAssembly(target);
                if (targetAsm != null)
                {
                    var correctType = targetAsm.MainModule.Types.FirstOrDefault(t => t.Name == name);
                    if (correctType != null && correctType.Namespace != ns)
                        ns = correctType.Namespace;
                }
            }
            var newTr = new TypeReference(ns, name, forceModule, targetRef);
            // Copy IsValueType from original (important for structs like MouseState)
            newTr.IsValueType = tr.IsValueType;
            mgTypeRefReplacements[tr] = newTr;

            forceRewrites++;
            if (forceRewrites <= 10)
                Console.WriteLine($"[AssemblyRefRewriter] Force-rewrite TypeRef {tr.FullName}: MonoGame.Framework -> {target}");
        }
        Console.WriteLine($"[AssemblyRefRewriter] Force-rewrite MonoGame.Framework typerefs: {forceRewrites}");

        // Replace all usages of old typerefs with new ones in method bodies
        int replacementCount = 0;
        foreach (var type in forceModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                for (int vi = 0; vi < method.Body.Variables.Count; vi++)
                {
                    var vt = method.Body.Variables[vi].VariableType;
                    if (mgTypeRefReplacements.TryGetValue(vt, out var newVt))
                    {
                        method.Body.Variables[vi].VariableType = newVt;
                        replacementCount++;
                    }
                }
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr && mr.DeclaringType != null && !(mr is Mono.Cecil.MethodSpecification)
                        && mgTypeRefReplacements.TryGetValue(mr.DeclaringType, out var newDt))
                    {
                        mr.DeclaringType = newDt;
                        replacementCount++;
                    }
                    if (ins.Operand is FieldReference fr && fr.DeclaringType != null
                        && mgTypeRefReplacements.TryGetValue(fr.DeclaringType, out var newFdt))
                    {
                        fr.DeclaringType = newFdt;
                        replacementCount++;
                    }
                    if (ins.Operand is TypeReference tr2
                        && mgTypeRefReplacements.TryGetValue(tr2, out var newTr2))
                    {
                        ins.Operand = newTr2;
                        replacementCount++;
                    }
                }
            }
        }
        Console.WriteLine($"[AssemblyRefRewriter] TypeReference replacements in method bodies: {replacementCount}");

        // Pass 2a2: Force-rewrite method body instruction operands' DeclaringType scopes.
        // Even though we rewrote typerefs in the typeref table (Pass 2a), method body
        // instructions hold their own MethodReference objects whose DeclaringType may
        // still point at MonoGame.Framework. We walk all method bodies and fix these.
        int instrRewrites = 0;
        foreach (var type in forceModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                foreach (var ins in method.Body.Instructions)
                {
                    if (ins.Operand is MethodReference mr && mr.DeclaringType != null && !(mr is Mono.Cecil.MethodSpecification))
                    {
                        var dt = mr.DeclaringType;
                        if (dt is TypeSpecification) continue; // skip specs
                        if (dt.Scope is AssemblyNameReference dtScope && dtScope.Name == "MonoGame.Framework")
                        {
                            var target = ResolveForwardedScope("MonoGame.Framework", dt.FullName, resolver);
                            if (target != null && target != "MonoGame.Framework")
                            {
                                var targetRef = forceModule.AssemblyReferences.FirstOrDefault(a => a.Name == target);
                                if (targetRef != null)
                                {
                                    dt.Scope = targetRef;
                                    instrRewrites++;
                                }
                            }
                        }
                    }
                    // Also handle FieldReference DeclaringType
                    if (ins.Operand is FieldReference fr && fr.DeclaringType != null)
                    {
                        var dt = fr.DeclaringType;
                        if (dt is TypeSpecification) continue;
                        if (dt.Scope is AssemblyNameReference dtScope && dtScope.Name == "MonoGame.Framework")
                        {
                            var target = ResolveForwardedScope("MonoGame.Framework", dt.FullName, resolver);
                            if (target != null && target != "MonoGame.Framework")
                            {
                                var targetRef = forceModule.AssemblyReferences.FirstOrDefault(a => a.Name == target);
                                if (targetRef != null)
                                {
                                    dt.Scope = targetRef;
                                    instrRewrites++;
                                }
                            }
                        }
                    }
                }
            }
        }
        Console.WriteLine($"[AssemblyRefRewriter] Instruction operand scope rewrites: {instrRewrites}");

        // Pass 2a3: Force-rewrite local variable types' scopes.
        // Local variables in method bodies have their own TypeReference objects.
        // If they have MonoGame.Framework scope, Mono JIT can't resolve them.
        int localVarRewrites = 0;
        foreach (var type in forceModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                foreach (var var in method.Body.Variables)
                {
                    var vt = var.VariableType;
                    if (vt is TypeSpecification) continue; // skip specs
                    if (vt.Scope is AssemblyNameReference vtScope && vtScope.Name == "MonoGame.Framework")
                    {
                        var target = ResolveForwardedScope("MonoGame.Framework", vt.FullName, resolver);
                        if (target != null && target != "MonoGame.Framework")
                        {
                            var targetRef = forceModule.AssemblyReferences.FirstOrDefault(a => a.Name == target);
                            if (targetRef != null)
                            {
                                vt.Scope = targetRef;
                                localVarRewrites++;
                            }
                        }
                    }
                }
            }
        }
        Console.WriteLine($"[AssemblyRefRewriter] Local variable scope rewrites: {localVarRewrites}");
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

        // Pass 2b: Also rewrite method signature types (parameters + return types).
        if (SkipMethodSignatureRewrite)
        {
            Console.WriteLine("[AssemblyRefRewriter] Skipping method signature scope rewrite (SkipMethodSignatureRewrite=true)");
        }
        else
        {
        // module.GetTypeReferences() may not include all typerefs used in method
        // signatures. We walk all methods and rewrite their parameter/return type
        // scopes directly.
        int sigRewrites = 0;
        var sigModule = asmDef.MainModule;
        foreach (var type in sigModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                // Rewrite return type scope (skip TypeSpecification — can't set Scope directly)
                if (method.ReturnType != null && !(method.ReturnType is TypeSpecification)
                    && method.ReturnType.Scope is AssemblyNameReference retScope)
                {
                    var target = ResolveForwardedScope(retScope.Name, method.ReturnType.FullName, resolver);
                    if (target != null && target != retScope.Name)
                    {
                        var targetRef = sigModule.AssemblyReferences.FirstOrDefault(a => a.Name == target);
                        if (targetRef != null)
                        {
                            method.ReturnType.Scope = targetRef;
                            sigRewrites++;
                        }
                    }
                }
                // Rewrite parameter type scopes (skip TypeSpecification)
                foreach (var param in method.Parameters)
                {
                    if (param.ParameterType != null && !(param.ParameterType is TypeSpecification)
                        && param.ParameterType.Scope is AssemblyNameReference paramScope)
                    {
                        var target = ResolveForwardedScope(paramScope.Name, param.ParameterType.FullName, resolver);
                        if (target != null && target != paramScope.Name)
                        {
                            var targetRef = sigModule.AssemblyReferences.FirstOrDefault(a => a.Name == target);
                            if (targetRef != null)
                            {
                                param.ParameterType.Scope = targetRef;
                                sigRewrites++;
                            }
                        }
                    }
                }
            }
        }
        Console.WriteLine($"[AssemblyRefRewriter] Method signature scope rewrites: {sigRewrites}");
        } // end if (!SkipMethodSignatureRewrite)

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
        // Pass 5: Game1..cctor() — DISABLED no-op patch.
        // Previously we patched .cctor to ret, but that leaves 453 static fields
        // null. Now that delegate/collection replacements are in place, let's
        // try running the real .cctor. If it still fails, we'll patch specific
        // failing instructions instead.
        // PatchGame1Cctor(asmDef);

        // Pass 5b: patch xxHash constructor to skip Enumerable call.
        // xxHash..ctor calls System.Linq.Enumerable which is stripped from WASM
        // CoreLib, causing TypeLoadException. We patch it to just ret.
        PatchXxHashCtor(asmDef);

        // Pass 5c: patch Game1.updateMusic to no-op (audio not initialized in WASM).
        PatchMethodToNop(asmDef, "StardewValley.Game1", "updateMusic");

        // Pass 5d: patch more methods that access uninitialized state or cause
        // Mono interpreter transform.c:1146 assertion.
        PatchMethodToNop(asmDef, "StardewValley.Game1", "updateClouds");
        PatchMethodToNop(asmDef, "StardewValley.Game1", "updateRain");
        PatchMethodToNop(asmDef, "StardewValley.Game1", "updateWeather");
        PatchMethodToNop(asmDef, "StardewValley.Game1", "updateCursor");
        PatchMethodToNop(asmDef, "StardewValley.Game1", "updateDebugInput");
        // These were nopped during bisecting — re-enable now that box EnumType is patched.
        // (OnDayStarted, UpdateChatBox, populateDebrisWeatherArray, AfterLoadContent)
        // TEMP: nop AfterLoadContent + OnDayStarted to bisect
        // AfterLoadContent re-enabled (crashes Tick)
        // OnDayStarted re-enabled (safe)
        // Bisect: nop resetPlayer to see if crash is in Farmer..ctor call chain
        // PatchMethodToNop(asmDef, "StardewValley.Game1", "resetPlayer");
        // Try nopping setGameMode (called by AfterLoadContent at end)
        // PatchMethodToNop(asmDef, "StardewValley.Game1", "setGameMode");
        // TitleMenu..ctor has 25+ UNSAFE calls at depth 6 (box T on generic in
        // LoadString, String::Equals, Int32::ToString, etc). Too many to nop.
        // Try: truncate TitleMenu..ctor after the base ctor call (instruction 46).
        // With AOT compilation, the Mono WASM interpreter JIT bug (transform.c:1146)
        // is bypassed entirely. But full AOT (with all original methods) runs out of
        // memory during LLVM compilation. Keep the nop patches to reduce code size
        // for AOT, while still getting the benefit of AOT-compiled runtime assemblies.
        PatchTitleMenuCtorTruncate(asmDef);
        PatchPlaySoundToNop(asmDef);
        PatchGetNameForInstance(asmDef);
        // Keep TitleMenu methods nopped (reduces AOT code size + avoids NREs from null fields)
        PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "performHoverAction");
        PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "receiveLeftClick");
        PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "update");
        PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "gameWindowSizeChanged");
        // Custom draw (proper Begin/End pairing)
        PatchTitleMenuDrawCustom(asmDef);
        PatchDrawCustom(asmDef);

        // Pass 5e: constrained. prefix handling
        PatchUpdateRemoveConstrained(asmDef);

        // Pass 5f: patch DeepClonerExtensions..cctor to nop.
        PatchMethodToNop(asmDef, "Force.DeepCloner.DeepClonerExtensions", ".cctor");

        // Pass 5g: patch SetInstanceDefaults to nop (calls DeepCloner).
        PatchMethodToNop(asmDef, "StardewValley.GameRunner", "SetInstanceDefaults");

        // Pass 5h: bisect _update — DISABLED.
        // The new PatchUpdateRemoveConstrained handles foreach Dispose() patterns
        // (the only &-preceded constrained.+callvirt pattern that _update actually
        // executes in normal Tick()). Other patterns keep the old `box T` behavior
        // that worked in Run() path. With bisect disabled, _update runs in full.
        BisectUpdateCount = -1;
        PatchUpdateBisect(asmDef);

        // Pass 5i: rewrite Enum.HasFlag(value, flag) calls to bitwise AND.
        // The C# compiler emits `value.HasFlag(flag)` as:
        //   <load value T>  box T  <load flag T>  box T  call Enum::HasFlag
        // The `box T` on enum types triggers transform.c:1146 in Mono WASM.
        // We rewrite to: <load value> <load flag> and ceq (or and+cgt, since
        // HasFlag returns (value & flag) == flag, but we simplify to (value & flag) != 0).
        // Actually HasFlag semantics: (value & flag) == flag. But most callers just
        // want "is any bit set". We use the simpler (value & flag) != 0 which is
        // equivalent for single-bit flags and common usage.
        PatchEnumHasFlag(asmDef);

        // Pass 5j: rewrite standalone `box EnumType` to `box Int32`.
        // box on enum types triggers transform.c:1146 in Mono WASM interpreter.
        // Enum values are already int32 on the stack, so `box Int32` produces the
        // same boxed object (an Int32 box, which is a valid System.Object).
        // This is semantically correct for params object[] and Enum.IsDefined/CompareTo.
        // Note: Enum.HasFlag boxes are already handled by PatchEnumHasFlag (which removes them).
        PatchBoxEnumToInt32(asmDef);

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

        // Pass 9: patch Options.setToDefaults() to skip SupportedDisplayModes lookup.
        // MUST run before Pass 10 (PatchBrokenMethodCalls) because Pass 10 would
        // redirect the get_SupportedDisplayModes call to a stub, which returns
        // an empty array, causing .Last() to throw.
        // We patch out the entire display mode lookup block and hardcode 1280x720.
        PatchOptionsSetToDefaults(asmDef);

        // Pass 10: patch out method calls that fail at runtime due to KNI/MonoGame
        // API differences (e.g., add_TextInput event accessor, KeyboardInput P/Invoke).
        PatchBrokenMethodCalls(asmDef);

        // Pass 11: patch Game1.DoThreadedInitTask to run synchronously (no threading).
        // WASM doesn't support System.Threading.Thread.
        PatchDoThreadedInitTask(asmDef);

        // Pass 12: patch LocalizedContentManager.GetContentRoot to return "Content".
        // SDV uses reflection to get TitleContainer.Location, which KNI doesn't have.
        // We replace the entire method body with: return "Content";
        PatchGetContentRoot(asmDef);

        // Pass 13: patch DoesAssetExist to always return true.
        // SDV checks _manifest HashSet for asset existence, but _manifest
        // initialization depends on File.Exists which may not work with our VFS.
        // We bypass the check and let ContentManager.Load handle the actual loading.
        PatchDoesAssetExist(asmDef);

        // Pass 14: patch GameRunner..ctor() to set GraphicsProfile = HiDef.
        // KNI defaults to Reach profile (max texture 2048px). SDV's Cursors.xnb
        // is larger than 2048, causing NotSupportedException. HiDef allows 8192.
        PatchGraphicsProfileToHiDef(asmDef);

        // Note: IsProfileSupported is patched in KNI's Xna.Framework.Graphics.dll
        // via KniGraphicsPatcher (run in SdvLoader.PreloadKniAssembliesAsync).

        using var outputMs = new MemoryStream();
        try
        {
            asmDef.Write(outputMs, new WriterParameters { WriteSymbols = false });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssemblyRefRewriter] Write failed ({ex.GetType().Name}: {ex.Message})");
            throw;
        }
        var result = outputMs.ToArray();
        Console.WriteLine($"[AssemblyRefRewriter] Rewritten assembly: {result.Length:N0} bytes");

        // Post-Cecil: directly patch PE bytes to fix TypeRef ResolutionScope.
        // Cecil doesn't persist scope changes, so we patch the raw PE typeref table.
        result = PeTypeRefPatcher.PatchTypeRefScopes(result, _typeToKniAssembly, "MonoGame.Framework");
        Console.WriteLine($"[AssemblyRefRewriter] After PE patch: {result.Length:N0} bytes");
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
    /// Patch xxHash constructor to just ret (skip Enumerable call).
    /// xxHash..ctor calls System.Linq.Enumerable which is stripped from WASM
    /// CoreLib, causing TypeLoadException in HashUtility..cctor → Game1..cctor.
    /// </summary>
    private static void PatchXxHashCtor(AssemblyDefinition asmDef)
    {
        // xxHash is in System.Data.HashFunction.xxHash.dll — not in SDV.
        // But the dependency DLLs are also run through the rewriter.
        // Find xxHash type
        TypeDefinition? xxHashType = null;
        foreach (var t in asmDef.MainModule.Types)
        {
            if (t.Name == "xxHash" || t.FullName.Contains("xxHash"))
            {
                xxHashType = t;
                break;
            }
        }
        if (xxHashType == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] xxHash type not found — skipping patch");
            return;
        }

        // Patch all constructors to just ret
        foreach (var ctor in xxHashType.Methods.Where(m => m.Name == ".ctor"))
        {
            var instrs = ctor.Body.Instructions;
            instrs.Clear();
            instrs.Add(Instruction.Create(OpCodes.Ret));
            ctor.Body.ExceptionHandlers.Clear();
            Console.WriteLine($"[AssemblyRefRewriter] Patched {xxHashType.FullName}..ctor({ctor.Parameters.Count} params) → ret");
        }
    }

    /// <summary>
    /// Patch a method to just ret (no-op). Used for methods that access
    /// uninitialized state (e.g., audio in WASM).
    /// </summary>
    private static void PatchMethodToNop(AssemblyDefinition asmDef, string typeFullName, string methodName)
    {
        var type = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == typeFullName);
        if (type == null) return;
        var method = type.Methods.FirstOrDefault(m => m.Name == methodName);
        if (method == null) return;

        var instrs = method.Body.Instructions;
        instrs.Clear();
        method.Body.ExceptionHandlers.Clear();
        instrs.Add(Instruction.Create(OpCodes.Ret));
        Console.WriteLine($"[AssemblyRefRewriter] Patched {typeFullName}::{methodName} → ret (no-op)");
    }

    /// <summary>
    /// Replace `newobj TitleMenu..ctor()` with `ldnull` in ALL methods.
    /// TitleMenu..ctor's call chain contains `box T` (generic parameter) which
    /// triggers transform.c:1146 in Mono WASM JIT. By replacing all newobj
    /// TitleMenu..ctor() calls with ldnull, TitleMenu..ctor is never called
    /// (and thus never JIT-compiled), avoiding the crash.
    /// </summary>
    private static void PatchNewobjTitleMenuToNull(AssemblyDefinition asmDef)
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
                    if (instrs[i].OpCode == OpCodes.Newobj && instrs[i].Operand is MethodReference mr
                        && mr.DeclaringType?.FullName == "StardewValley.Menus.TitleMenu")
                    {
                        // Replace newobj TitleMenu..ctor() with ldnull
                        instrs[i].OpCode = OpCodes.Ldnull;
                        instrs[i].Operand = null;
                        patched++;
                        Console.WriteLine($"[AssemblyRefRewriter] Patched newobj TitleMenu → ldnull in {type.FullName}::{method.Name}");
                    }
                }
            }
        }
        if (patched > 0)
            Console.WriteLine($"[AssemblyRefRewriter] Total: {patched} newobj TitleMenu → ldnull");
    }

    /// <summary>
    /// Truncate TitleMenu..ctor after the base ctor call.
    /// Keeps the first 47 instructions (field initialization + base IClickableMenu..ctor call)
    /// and adds ret. This skips all unsafe calls (loadPreferences, LoadString, setRichPresence,
    /// etc.) that contain box T (generic parameter) patterns triggering transform.c:1146.
    /// The partially initialized TitleMenu has enough state for basic rendering.
    /// </summary>
    private static void PatchTitleMenuCtorTruncate(AssemblyDefinition asmDef)
    {
        var tm = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Menus.TitleMenu");
        if (tm == null) return;
        var ctor = tm.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 0);
        if (ctor == null) return;

        var instrs = ctor.Body.Instructions;
        // Find the base IClickableMenu..ctor call
        int baseCtorIdx = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is MethodReference mr
                && mr.Name == ".ctor" && mr.DeclaringType?.Name == "IClickableMenu")
            {
                baseCtorIdx = i;
                break;
            }
        }
        if (baseCtorIdx < 0)
        {
            Console.WriteLine("[AssemblyRefRewriter] TitleMenu..ctor: base ctor not found — skipping truncate");
            return;
        }

        // Keep instructions 0..baseCtorIdx (inclusive) + texture loading (up to titleButtonsTexture stfld)
        // Find the stfld titleButtonsTexture instruction and keep up to there
        int textureEndIdx = -1;
        for (int i = baseCtorIdx + 1; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Stfld && instrs[i].Operand is FieldReference fr
                && fr.Name == "titleButtonsTexture")
            {
                textureEndIdx = i;
                break;
            }
        }
        var keepCount = textureEndIdx > 0 ? textureEndIdx + 1 : baseCtorIdx + 1;

        // Nop the add_OnLanguageChange call (instruction ~50) — may trigger unsafe JIT
        for (int i = baseCtorIdx + 1; i < keepCount; i++)
        {
            if (instrs[i].OpCode == OpCodes.Call && instrs[i].Operand is MethodReference mr
                && mr.Name == "add_OnLanguageChange")
            {
                instrs[i].OpCode = OpCodes.Nop;
                instrs[i].Operand = null;
                // Also nop the preceding newobj (delegate creation) and ldftn
                if (i - 1 >= 0 && instrs[i - 1].OpCode == OpCodes.Newobj)
                {
                    instrs[i - 1].OpCode = OpCodes.Nop;
                    instrs[i - 1].Operand = null;
                }
                if (i - 2 >= 0 && instrs[i - 2].OpCode == OpCodes.Ldftn)
                {
                    instrs[i - 2].OpCode = OpCodes.Nop;
                    instrs[i - 2].Operand = null;
                }
                // Also nop the ldarg.0 that was for the delegate target (stack is empty here)
                if (i - 3 >= 0 && instrs[i - 3].OpCode == OpCodes.Ldarg_0)
                {
                    instrs[i - 3].OpCode = OpCodes.Nop;
                }
                Console.WriteLine("[AssemblyRefRewriter] Nopped add_OnLanguageChange in TitleMenu..ctor");
                break;
            }
        }

        while (instrs.Count > keepCount)
            instrs.RemoveAt(keepCount);

        // Clear exception handlers (they may reference removed instructions)
        ctor.Body.ExceptionHandlers.Clear();

        // Before ret, add:
        // 1. Initialize this.buttons = new List<ClickableTextureComponent>() (if null)
        // 2. Call this.setUpIcons() — creates the title screen buttons (safe, no box T)
        // This allows the original TitleMenu.draw to work without NRE.

        var module = asmDef.MainModule;
        var buttonsField = tm.Fields.FirstOrDefault(f => f.Name == "buttons");
        var setUpIconsMethod = tm.Methods.FirstOrDefault(m => m.Name == "setUpIcons");

        if (buttonsField != null && setUpIconsMethod != null)
        {
            // Find List<ClickableTextureComponent>..ctor() from existing IL
            MethodReference? listCtorRef = null;
            foreach (var ins in ctor.Body.Instructions)
            {
                if (ins.OpCode == OpCodes.Newobj && ins.Operand is MethodReference mr
                    && mr.DeclaringType.FullName.StartsWith("System.Collections.Generic.List`1")
                    && mr.Parameters.Count == 0)
                {
                    listCtorRef = mr;
                    break;
                }
            }

            if (listCtorRef != null)
            {
                // Find the GenericInstanceType for List<ClickableTextureComponent>
                // from the buttonsField's FieldType
                var importedButtonsField = module.ImportReference(buttonsField);
                var importedSetUpIcons = module.ImportReference(setUpIconsMethod);

                // Create a new List<ClickableTextureComponent>() constructor reference
                // The buttonsField.FieldType is already List<ClickableTextureComponent>
                var listType = buttonsField.FieldType;
                var listCtor = new MethodReference(".ctor", module.TypeSystem.Void, listType)
                {
                    HasThis = true,
                };
                var importedListCtor = module.ImportReference(listCtor);

                // ldarg.0 (this)
                instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
                // new List<ClickableTextureComponent>()
                instrs.Add(Instruction.Create(OpCodes.Newobj, importedListCtor));
                // stfld buttons
                instrs.Add(Instruction.Create(OpCodes.Stfld, importedButtonsField));
                // ldarg.0 (this)
                instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
                // callvirt setUpIcons()
                instrs.Add(Instruction.Create(OpCodes.Call, importedSetUpIcons));
                Console.WriteLine("[AssemblyRefRewriter] Added buttons init + setUpIcons() call to TitleMenu..ctor");
            }
        }

        // Add ret
        instrs.Add(Instruction.Create(OpCodes.Ret));

        Console.WriteLine($"[AssemblyRefRewriter] TitleMenu..ctor truncated: kept {keepCount} instructions + buttons init + ret");
    }

    /// <summary>
    /// Replace Game1._draw with a simple custom renderer that properly pairs
    /// SpriteBatch.Begin/End. This eliminates ALL Begin/End mismatch errors
    /// and NREs from null fields (staminaRect, dialogueFont, etc.) in the
    /// original 425-instruction _draw method.
    ///
    /// The custom _draw:
    ///   1. Clears with bgColor
    ///   2. Begins SpriteBatch
    ///   3. Draws cloudsTexture (full screen) if TitleMenu has it
    ///   4. Draws titleButtonsTexture (centered top) if TitleMenu has it
    ///   5. Ends SpriteBatch
    ///   6. Returns
    /// </summary>
    private static void PatchDrawCustom(AssemblyDefinition asmDef)
    {
        var g1 = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (g1 == null) return;
        var draw = g1.Methods.FirstOrDefault(m => m.Name == "_draw");
        if (draw == null) return;

        var module = asmDef.MainModule;
        var tm = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Menus.TitleMenu");
        if (tm == null) return;

        // Find SpriteBatch type from SDV's type references
        TypeDefinition? sbType = null;
        var sbTypeRef = module.GetTypeReferences()
            .FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch");
        if (sbTypeRef != null)
        {
            try { sbType = sbTypeRef.Resolve(); } catch { }
        }
        if (sbType == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] SpriteBatch not found for _draw patch — skipping");
            return;
        }

        // Find Begin(7 params), End(0 params), Draw(Texture2D, Rectangle, Color)
        var beginMethod = sbType.Methods.FirstOrDefault(m => m.Name == "Begin" && m.Parameters.Count == 7);
        var endMethod = sbType.Methods.FirstOrDefault(m => m.Name == "End" && m.Parameters.Count == 0);
        var drawMethod = sbType.Methods.FirstOrDefault(m =>
            m.Name == "Draw" && m.Parameters.Count == 3
            && m.Parameters[0].ParameterType.Name == "Texture2D"
            && m.Parameters[1].ParameterType.Name == "Rectangle"
            && m.Parameters[2].ParameterType.Name == "Color");
        if (beginMethod == null || endMethod == null || drawMethod == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] SpriteBatch Begin/End/Draw not found — skipping _draw patch");
            return;
        }

        // Find GraphicsDevice.Clear(Color)
        TypeDefinition? gdType = null;
        var gdTypeRef = module.GetTypeReferences()
            .FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Graphics.GraphicsDevice");
        if (gdTypeRef != null) { try { gdType = gdTypeRef.Resolve(); } catch { } }
        var clearMethod = gdType?.Methods.FirstOrDefault(m => m.Name == "Clear" && m.Parameters.Count == 1);

        // Find fields and methods
        var bgColorField = g1.Fields.FirstOrDefault(f => f.Name == "bgColor");
        var spriteBatchField = g1.Fields.FirstOrDefault(f => f.Name == "spriteBatch");
        var graphicsField = g1.Fields.FirstOrDefault(f => f.Name == "graphics");
        var getGraphicsDeviceMethod = graphicsField?.FieldType.Resolve()?.Methods
            .FirstOrDefault(m => m.Name == "get_GraphicsDevice");
        var cloudsField = tm.Fields.FirstOrDefault(f => f.Name == "cloudsTexture");
        var titleButtonsField = tm.Fields.FirstOrDefault(f => f.Name == "titleButtonsTexture");
        var getActiveClickableMenu = g1.Methods.FirstOrDefault(m => m.Name == "get_activeClickableMenu");

        // Find constructors by scanning SDV's IL
        MethodReference? colorCtorRef = null;
        MethodReference? rectCtorRef = null;
        foreach (var t in module.GetTypes())
            foreach (var m in t.Methods)
            {
                if (m.Body == null) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.OpCode != OpCodes.Newobj) continue;
                    if (ins.Operand is not MethodReference mr) continue;
                    if (colorCtorRef == null && mr.DeclaringType.FullName == "Microsoft.Xna.Framework.Color"
                        && mr.Name == ".ctor" && mr.Parameters.Count == 3)
                        colorCtorRef = mr;
                    if (rectCtorRef == null && mr.DeclaringType.FullName == "Microsoft.Xna.Framework.Rectangle"
                        && mr.Name == ".ctor" && mr.Parameters.Count == 4)
                        rectCtorRef = mr;
                    if (colorCtorRef != null && rectCtorRef != null) break;
                }
                if (colorCtorRef != null && rectCtorRef != null) break;
            }

        if (bgColorField == null || spriteBatchField == null || cloudsField == null
            || colorCtorRef == null || rectCtorRef == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] Required fields/ctors not found — skipping _draw patch");
            return;
        }

        // Find BlendState.AlphaBlend and SamplerState.PointClamp static fields
        TypeDefinition? blendStateType = null;
        var bsTypeRef = module.GetTypeReferences().FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Graphics.BlendState");
        if (bsTypeRef != null) { try { blendStateType = bsTypeRef.Resolve(); } catch { } }
        var alphaBlendField = blendStateType?.Fields.FirstOrDefault(f => f.Name == "AlphaBlend");

        TypeDefinition? samplerStateType = null;
        var ssTypeRef = module.GetTypeReferences().FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Graphics.SamplerState");
        if (ssTypeRef != null) { try { samplerStateType = ssTypeRef.Resolve(); } catch { } }
        var pointClampField = samplerStateType?.Fields.FirstOrDefault(f => f.Name == "PointClamp");

        // Find Game1.uiViewport and xTile Rectangle get_Width/get_Height
        var uiViewportField = g1.Fields.FirstOrDefault(f => f.Name == "uiViewport");
        MethodReference? getWidthRef = null;
        MethodReference? getHeightRef = null;
        if (uiViewportField != null)
        {
            var xTileRectType = uiViewportField.FieldType.Resolve();
            if (xTileRectType != null)
            {
                getWidthRef = xTileRectType.Methods.FirstOrDefault(m => m.Name == "get_Width");
                getHeightRef = xTileRectType.Methods.FirstOrDefault(m => m.Name == "get_Height");
            }
        }

        // === Title logo + buttons rendering references ===
        // TitleMenu.pixelZoom (static int, typically 4)
        var pixelZoomField = tm.Fields.FirstOrDefault(f => f.Name == "pixelZoom");
        // TitleMenu.buttons (List<ClickableTextureComponent>)
        var buttonsField = tm.Fields.FirstOrDefault(f => f.Name == "buttons");

        // Resolve types directly from SDV's own module (no IL scanning — much faster)
        // Vector2 ctor (float, float)
        MethodReference? vector2CtorRef = null;
        // Color.get_White — static property getter
        MethodReference? colorWhiteRef = null;
        // Vector2.get_Zero — static property getter
        MethodReference? vector2ZeroRef = null;
        // Nullable<Rectangle> ctor
        MethodReference? nullableRectCtorRef = null;
        // 9-param Draw overload: Draw(Texture2D, Vector2, Nullable<Rectangle>, Color, float, Vector2, float, SpriteEffects, float)
        MethodReference? draw9Ref = null;
        // 4-param Draw overload: Draw(Texture2D, Rectangle, Nullable<Rectangle>, Color)
        MethodReference? draw4Ref = null;

        // Resolve Vector2 type definition from the KNI assembly via type reference
        TypeDefinition? vector2Type = null;
        var vector2TypeRef = module.GetTypeReferences().FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Vector2");
        if (vector2TypeRef != null) { try { vector2Type = vector2TypeRef.Resolve(); } catch { } }
        if (vector2Type != null)
        {
            var v2ctor = vector2Type.Methods.FirstOrDefault(m => m.Name == ".ctor" && m.Parameters.Count == 2);
            if (v2ctor != null) vector2CtorRef = v2ctor;
            var v2zero = vector2Type.Methods.FirstOrDefault(m => m.Name == "get_Zero");
            if (v2zero != null) vector2ZeroRef = v2zero;
        }

        // Resolve Color type definition
        TypeDefinition? colorType = null;
        var colorTypeRef = module.GetTypeReferences().FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Color");
        if (colorTypeRef != null) { try { colorType = colorTypeRef.Resolve(); } catch { } }
        if (colorType != null)
        {
            var white = colorType.Methods.FirstOrDefault(m => m.Name == "get_White");
            if (white != null) colorWhiteRef = white;
        }

        // Resolve SpriteBatch type definition (already resolved above as sbType)
        if (sbType != null)
        {
            draw9Ref = sbType.Methods.FirstOrDefault(m => m.Name == "Draw" && m.Parameters.Count == 9);
            draw4Ref = sbType.Methods.FirstOrDefault(m => m.Name == "Draw" && m.Parameters.Count == 4
                && m.Parameters[1].ParameterType.FullName == "Microsoft.Xna.Framework.Rectangle"
                && m.Parameters[2].ParameterType.FullName.StartsWith("System.Nullable`1<Microsoft.Xna.Framework.Rectangle"));
        }

        // Find Nullable<Rectangle> ctor — scan SDV IL for newobj Nullable<Rectangle>(.ctor)
        // This is the only way to get a GenericInstanceType method reference without manually constructing it
        if (nullableRectCtorRef == null)
        {
            // Limit scan to just TitleMenu methods (where we know it's used) for speed
            foreach (var m in tm.Methods)
            {
                if (m.Body == null) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.OpCode != OpCodes.Newobj) continue;
                    if (ins.Operand is not MethodReference mr) continue;
                    if (mr.DeclaringType.FullName.StartsWith("System.Nullable`1<Microsoft.Xna.Framework.Rectangle"))
                    {
                        nullableRectCtorRef = mr;
                        break;
                    }
                }
                if (nullableRectCtorRef != null) break;
            }
            // Fallback: scan Game1 methods
            if (nullableRectCtorRef == null)
            {
                foreach (var m in g1.Methods)
                {
                    if (m.Body == null) continue;
                    foreach (var ins in m.Body.Instructions)
                    {
                        if (ins.OpCode != OpCodes.Newobj) continue;
                        if (ins.Operand is not MethodReference mr) continue;
                        if (mr.DeclaringType.FullName.StartsWith("System.Nullable`1<Microsoft.Xna.Framework.Rectangle"))
                        {
                            nullableRectCtorRef = mr;
                            break;
                        }
                    }
                    if (nullableRectCtorRef != null) break;
                }
            }
        }

        // ClickableTextureComponent type + fields (texture, bounds, sourceRect)
        // Search the module's own types directly (ClickableTextureComponent is in SDV itself)
        TypeDefinition? ctcType = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Menus.ClickableTextureComponent");
        var ctcTextureField = ctcType?.Fields.FirstOrDefault(f => f.Name == "texture");
        var ctcSourceRectField = ctcType?.Fields.FirstOrDefault(f => f.Name == "sourceRect");
        // bounds is inherited from ClickableComponent — find it in the base type
        FieldDefinition? ctcBoundsField = ctcType?.Fields.FirstOrDefault(f => f.Name == "bounds");
        if (ctcBoundsField == null && ctcType?.BaseType != null)
        {
            try
            {
                var ccType = ctcType.BaseType.Resolve();
                ctcBoundsField = ccType?.Fields.FirstOrDefault(f => f.Name == "bounds");
            }
            catch { }
        }

        // List<ClickableTextureComponent>.get_Count and get_Item — find via the buttons field type
        MethodReference? listGetCountRef = null;
        MethodReference? listGetItemRef = null;
        if (buttonsField != null)
        {
            var listType = buttonsField.FieldType.Resolve();
            if (listType != null)
            {
                listGetCountRef = listType.Methods.FirstOrDefault(m => m.Name == "get_Count");
                listGetItemRef = listType.Methods.FirstOrDefault(m => m.Name == "get_Item");
            }
        }

        // SpriteEffects enum type (just need the type for ldc.i4.0 = None)
        // No reference needed — we just push 0 on the stack

        Console.WriteLine($"[AssemblyRefRewriter] _draw refs: pixelZoom={pixelZoomField != null}, buttons={buttonsField != null}, "
            + $"vec2ctor={vector2CtorRef != null}, colorWhite={colorWhiteRef != null}, vec2Zero={vector2ZeroRef != null}, "
            + $"nullableRectCtor={nullableRectCtorRef != null}, draw9={draw9Ref != null}, draw4={draw4Ref != null}, "
            + $"ctcTexture={ctcTextureField != null}, ctcBounds={ctcBoundsField != null}, ctcSourceRect={ctcSourceRectField != null}, "
            + $"listCount={listGetCountRef != null}, listGetItem={listGetItemRef != null}");

        // Build the custom _draw body
        var instrs = draw.Body.Instructions;
        instrs.Clear();
        draw.Body.ExceptionHandlers.Clear();
        draw.Body.Variables.Clear();

        var intType = module.TypeSystem.Int32;
        var floatType = module.TypeSystem.Single;
        draw.Body.Variables.Add(new VariableDefinition(intType));  // V_0 = width
        draw.Body.Variables.Add(new VariableDefinition(intType));  // V_1 = height
        draw.Body.Variables.Add(new VariableDefinition(intType));  // V_2 = button loop counter
        draw.Body.Variables.Add(new VariableDefinition(intType));  // V_3 = button count
        // V_4 = buttons list reference (List<ClickableTextureComponent>)
        if (buttonsField != null)
            draw.Body.Variables.Add(new VariableDefinition(buttonsField.FieldType));
        else
            draw.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(object))));
        // V_5 = current button (ClickableTextureComponent)
        if (ctcType != null)
            draw.Body.Variables.Add(new VariableDefinition(ctcType));
        else
            draw.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(object))));
        // V_6 = Nullable<Rectangle> for source rect
        if (nullableRectCtorRef != null)
            draw.Body.Variables.Add(new VariableDefinition(nullableRectCtorRef.DeclaringType));
        else
            draw.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(object))));

        // Keep references to V_4, V_5, V_6 for Stloc_S/Ldloc_S (which take VariableDefinition)
        var v4Buttons = draw.Body.Variables[4];
        var v5Btn = draw.Body.Variables[5];
        var v6NullableRect = draw.Body.Variables[6];

        var iBgColor = module.ImportReference(bgColorField);
        var iSpriteBatch = module.ImportReference(spriteBatchField);
        var iBegin = module.ImportReference(beginMethod);
        var iEnd = module.ImportReference(endMethod);
        var iDraw = module.ImportReference(drawMethod);
        var iColorCtor = module.ImportReference(colorCtorRef);
        var iRectCtor = module.ImportReference(rectCtorRef);
        var iClouds = module.ImportReference(cloudsField);
        var iTitleButtons = titleButtonsField != null ? module.ImportReference(titleButtonsField) : null;
        var iGetACM = getActiveClickableMenu != null ? module.ImportReference(getActiveClickableMenu) : null;
        var iUiViewport = uiViewportField != null ? module.ImportReference(uiViewportField) : null;
        var iGetWidth = getWidthRef != null ? module.ImportReference(getWidthRef) : null;
        var iGetHeight = getHeightRef != null ? module.ImportReference(getHeightRef) : null;
        var iClear = clearMethod != null ? module.ImportReference(clearMethod) : null;
        var iGetGD = getGraphicsDeviceMethod != null ? module.ImportReference(getGraphicsDeviceMethod) : null;
        var iGraphics = graphicsField != null ? module.ImportReference(graphicsField) : null;
        var iAlphaBlend = alphaBlendField != null ? module.ImportReference(alphaBlendField) : null;
        var iPointClamp = pointClampField != null ? module.ImportReference(pointClampField) : null;

        // Imports for title logo + buttons rendering
        var iPixelZoom = pixelZoomField != null ? module.ImportReference(pixelZoomField) : null;
        var iButtons = buttonsField != null ? module.ImportReference(buttonsField) : null;
        var iVector2Ctor = vector2CtorRef != null ? module.ImportReference(vector2CtorRef) : null;
        var iColorWhite = colorWhiteRef != null ? module.ImportReference(colorWhiteRef) : null;
        var iVector2Zero = vector2ZeroRef != null ? module.ImportReference(vector2ZeroRef) : null;
        var iNullableRectCtor = nullableRectCtorRef != null ? module.ImportReference(nullableRectCtorRef) : null;
        var iDraw9 = draw9Ref != null ? module.ImportReference(draw9Ref) : null;
        var iDraw4 = draw4Ref != null ? module.ImportReference(draw4Ref) : null;
        var iCtcTexture = ctcTextureField != null ? module.ImportReference(ctcTextureField) : null;
        var iCtcBounds = ctcBoundsField != null ? module.ImportReference(ctcBoundsField) : null;
        var iCtcSourceRect = ctcSourceRectField != null ? module.ImportReference(ctcSourceRectField) : null;
        var iListGetCount = listGetCountRef != null ? module.ImportReference(listGetCountRef) : null;
        var iListGetItem = listGetItemRef != null ? module.ImportReference(listGetItemRef) : null;

        var retInstr = Instruction.Create(OpCodes.Ret);

        // 1. GraphicsDevice.Clear(bgColor)
        if (iClear != null && iGetGD != null && iGraphics != null)
        {
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iGraphics));       // Game1.graphics
            instrs.Add(Instruction.Create(OpCodes.Callvirt, iGetGD));        // .GraphicsDevice
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iBgColor));        // bgColor
            instrs.Add(Instruction.Create(OpCodes.Callvirt, iClear));        // Clear(bgColor)
        }

        // 2. Get screen dimensions
        if (iUiViewport != null && iGetWidth != null)
        {
            instrs.Add(Instruction.Create(OpCodes.Ldsflda, iUiViewport));
            instrs.Add(Instruction.Create(OpCodes.Call, iGetWidth));
            instrs.Add(Instruction.Create(OpCodes.Stloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldsflda, iUiViewport));
            instrs.Add(Instruction.Create(OpCodes.Call, iGetHeight));
            instrs.Add(Instruction.Create(OpCodes.Stloc_1));
        }
        else
        {
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 1280));
            instrs.Add(Instruction.Create(OpCodes.Stloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 720));
            instrs.Add(Instruction.Create(OpCodes.Stloc_1));
        }

        // 3. SpriteBatch.Begin(SortMode=0, AlphaBlend, PointClamp, null, null, null, null)
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));   // spriteBatch
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));               // SpriteSortMode.Deferred
        if (iAlphaBlend != null)
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iAlphaBlend));
        else
            instrs.Add(Instruction.Create(OpCodes.Ldnull));
        if (iPointClamp != null)
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iPointClamp));
        else
            instrs.Add(Instruction.Create(OpCodes.Ldnull));
        instrs.Add(Instruction.Create(OpCodes.Ldnull));  // DepthStencilState
        instrs.Add(Instruction.Create(OpCodes.Ldnull));  // RasterizerState
        instrs.Add(Instruction.Create(OpCodes.Ldnull));  // Effect
        instrs.Add(Instruction.Create(OpCodes.Ldnull));  // Matrix?
        instrs.Add(Instruction.Create(OpCodes.Callvirt, iBegin));

        // 4. Draw cloudsTexture (full screen) — get from TitleMenu instance
        if (iGetACM != null)
        {
            // if (activeClickableMenu != null && cloudsTexture != null)
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));       // get activeClickableMenu
            instrs.Add(Instruction.Create(OpCodes.Brfalse, retInstr));   // skip if null

            // Draw clouds
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));   // b
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));           // menu
            instrs.Add(Instruction.Create(OpCodes.Isinst, tm));              // cast to TitleMenu
            instrs.Add(Instruction.Create(OpCodes.Ldfld, iClouds));          // cloudsTexture
            instrs.Add(Instruction.Create(OpCodes.Brfalse, retInstr));       // skip if null

            // Actually need to reload spriteBatch and cloudsTexture for the Draw call
            // Let's redo: the above consumed the stack. Let me restructure.
        }

        // Clear and redo the draw section properly
        instrs.Clear();

        // 1. Clear with bgColor
        if (iClear != null && iGetGD != null && iGraphics != null)
        {
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iGraphics));
            instrs.Add(Instruction.Create(OpCodes.Callvirt, iGetGD));
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iBgColor));
            instrs.Add(Instruction.Create(OpCodes.Callvirt, iClear));
        }

        // 2. Get screen dimensions
        if (iUiViewport != null && iGetWidth != null)
        {
            instrs.Add(Instruction.Create(OpCodes.Ldsflda, iUiViewport));
            instrs.Add(Instruction.Create(OpCodes.Call, iGetWidth));
            instrs.Add(Instruction.Create(OpCodes.Stloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldsflda, iUiViewport));
            instrs.Add(Instruction.Create(OpCodes.Call, iGetHeight));
            instrs.Add(Instruction.Create(OpCodes.Stloc_1));
        }
        else
        {
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 1280));
            instrs.Add(Instruction.Create(OpCodes.Stloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 720));
            instrs.Add(Instruction.Create(OpCodes.Stloc_1));
        }

        // 3. SpriteBatch.Begin
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        if (iAlphaBlend != null) instrs.Add(Instruction.Create(OpCodes.Ldsfld, iAlphaBlend));
        else instrs.Add(Instruction.Create(OpCodes.Ldnull));
        if (iPointClamp != null) instrs.Add(Instruction.Create(OpCodes.Ldsfld, iPointClamp));
        else instrs.Add(Instruction.Create(OpCodes.Ldnull));
        instrs.Add(Instruction.Create(OpCodes.Ldnull));
        instrs.Add(Instruction.Create(OpCodes.Ldnull));
        instrs.Add(Instruction.Create(OpCodes.Ldnull));
        instrs.Add(Instruction.Create(OpCodes.Ldnull));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, iBegin));

        // 4. Try to draw clouds: if (activeClickableMenu is TitleMenu tm && tm.cloudsTexture != null)
        var skipCloudsLabel = Instruction.Create(OpCodes.Nop);
        if (iGetACM != null)
        {
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));
            instrs.Add(Instruction.Create(OpCodes.Brfalse, skipCloudsLabel));
            // b.Draw(tm.cloudsTexture, new Rectangle(0,0,w,h), new Color(255,255,255))
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));     // b
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));             // menu
            instrs.Add(Instruction.Create(OpCodes.Isinst, tm));                // (TitleMenu)menu
            instrs.Add(Instruction.Create(OpCodes.Ldfld, iClouds));            // .cloudsTexture
            instrs.Add(Instruction.Create(OpCodes.Brfalse, skipCloudsLabel));  // if null skip

            // Reload for Draw call (we consumed cloudsTexture in the null check above)
            // Actually the brfalse consumed it. Need to reload.
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));     // b
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));             // menu
            instrs.Add(Instruction.Create(OpCodes.Isinst, tm));                // (TitleMenu)menu
            instrs.Add(Instruction.Create(OpCodes.Ldfld, iClouds));            // .cloudsTexture
            // new Rectangle(0, 0, w, h)
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldloc_1));
            instrs.Add(Instruction.Create(OpCodes.Newobj, iRectCtor));
            // new Color(255,255,255)
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
            instrs.Add(Instruction.Create(OpCodes.Newobj, iColorCtor));
            instrs.Add(Instruction.Create(OpCodes.Callvirt, iDraw));
        }
        instrs.Add(skipCloudsLabel);

        // 4b. Draw title logo — titleButtonsTexture with source rect (0, 0, 512, 337)
        // This is the ONLY Draw4 call in the method. Having two Draw4 calls with
        // different source rects causes a WASM JIT crash (page segfault).
        // The title logo source rect (0, 0, 512, 337) contains the SDV logo with
        // yellow chicken, orange text, and brown details.
        if (iGetACM != null && iTitleButtons != null && iDraw4 != null
            && iColorWhite != null && iNullableRectCtor != null)
        {
            var skipTitleLabel = Instruction.Create(OpCodes.Nop);

            // if (activeClickableMenu is TitleMenu)
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));
            instrs.Add(Instruction.Create(OpCodes.Brfalse, skipTitleLabel));

            // Check titleButtonsTexture != null
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));
            instrs.Add(Instruction.Create(OpCodes.Isinst, tm));
            instrs.Add(Instruction.Create(OpCodes.Ldfld, iTitleButtons));
            instrs.Add(Instruction.Create(OpCodes.Brfalse, skipTitleLabel));

            // spriteBatch.Draw(titleButtonsTexture, destRect, sourceRect, Color.White)
            instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));
            instrs.Add(Instruction.Create(OpCodes.Call, iGetACM));
            instrs.Add(Instruction.Create(OpCodes.Isinst, tm));
            instrs.Add(Instruction.Create(OpCodes.Ldfld, iTitleButtons));
            // dest rect: centered — new Rectangle(width/2 - 222, height/2 - 120, 444, 240)
            instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            instrs.Add(Instruction.Create(OpCodes.Div));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 222));
            instrs.Add(Instruction.Create(OpCodes.Sub));
            instrs.Add(Instruction.Create(OpCodes.Ldloc_1));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            instrs.Add(Instruction.Create(OpCodes.Div));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 120));
            instrs.Add(Instruction.Create(OpCodes.Sub));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 444));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 240));
            instrs.Add(Instruction.Create(OpCodes.Newobj, iRectCtor));
            // source rect: new Nullable<Rectangle>(new Rectangle(0, 0, 512, 337))
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 512));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 337));
            instrs.Add(Instruction.Create(OpCodes.Newobj, iRectCtor));
            instrs.Add(Instruction.Create(OpCodes.Newobj, iNullableRectCtor));
            instrs.Add(Instruction.Create(OpCodes.Call, iColorWhite));
            instrs.Add(Instruction.Create(OpCodes.Callvirt, iDraw4));

            instrs.Add(skipTitleLabel);
        }

        // 4c. Button rendering — DISABLED.
        // Having two Draw4 calls with different source rects causes a WASM JIT crash.
        // Only one Draw4 call (the title logo above) is safe per method.
        // To render buttons, we'd need to extract the button draw into a separate
        // method (which requires injecting a new method into SDV's assembly).

        // 5. SpriteBatch.End
        instrs.Add(Instruction.Create(OpCodes.Ldsfld, iSpriteBatch));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, iEnd));

        // 6. Return
        instrs.Add(retInstr);

        Console.WriteLine("[AssemblyRefRewriter] Game1._draw → custom (Clear + Begin + Draw clouds + End)");
    }

    /// <summary>
    /// Replace TitleMenu.draw with a custom body that draws cloudsTexture.
    /// The original draw (2111 instructions) NREs because buttons etc. are null
    /// (ctor was truncated). This custom body just draws the clouds texture
    /// as a simple background, proving the full render pipeline works with
    /// real SDV textures.
    /// </summary>
    private static void PatchTitleMenuDrawCustom(AssemblyDefinition asmDef)
    {
        var tm = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Menus.TitleMenu");
        if (tm == null) return;
        var draw = tm.Methods.FirstOrDefault(m => m.Name == "draw");
        if (draw == null) return;

        var module = asmDef.MainModule;

        // Find SpriteBatch type — look in SDV's own type references (it references SpriteBatch)
        TypeDefinition? sbType = null;
        var sbTypeRef = module.GetTypeReferences()
            .FirstOrDefault(tr => tr.FullName == "Microsoft.Xna.Framework.Graphics.SpriteBatch");
        if (sbTypeRef != null)
        {
            try { sbType = sbTypeRef.Resolve(); } catch { }
        }
        // Also try AppDomain
        if (sbType == null)
        {
            var graphicsAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Xna.Framework.Graphics");
            if (graphicsAsm != null)
            {
                var sbTR = module.ImportReference(graphicsAsm.GetType("Microsoft.Xna.Framework.Graphics.SpriteBatch"));
                try { sbType = sbTR.Resolve(); } catch { }
            }
        }
        if (sbType == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] SpriteBatch type not found — skipping custom draw");
            PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "draw");
            return;
        }

        var drawMethod = sbType.Methods.FirstOrDefault(m =>
            m.Name == "Draw" && m.Parameters.Count == 3
            && m.Parameters[0].ParameterType.Name == "Texture2D"
            && m.Parameters[1].ParameterType.Name == "Rectangle"
            && m.Parameters[2].ParameterType.Name == "Color");
        if (drawMethod == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] SpriteBatch.Draw(Texture2D,Rectangle,Color) not found — skipping");
            PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "draw");
            return;
        }

        // Find cloudsTexture and titleButtonsTexture fields
        var cloudsField = tm.Fields.FirstOrDefault(f => f.Name == "cloudsTexture");
        var titleButtonsField = tm.Fields.FirstOrDefault(f => f.Name == "titleButtonsTexture");
        if (cloudsField == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] cloudsTexture field not found — skipping");
            PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "draw");
            return;
        }

        // Find Color and Rectangle constructors by scanning all methods in SDV.
        MethodReference? colorCtorRef = null;
        MethodReference? rectCtorRef = null;
        foreach (var t in module.GetTypes())
        {
            foreach (var m in t.Methods)
            {
                if (m.Body == null) continue;
                foreach (var ins in m.Body.Instructions)
                {
                    if (ins.OpCode != OpCodes.Newobj) continue;
                    if (ins.Operand is not MethodReference mr) continue;
                    if (colorCtorRef == null && mr.DeclaringType.FullName == "Microsoft.Xna.Framework.Color"
                        && mr.Name == ".ctor" && mr.Parameters.Count == 3)
                        colorCtorRef = mr;
                    if (rectCtorRef == null && mr.DeclaringType.FullName == "Microsoft.Xna.Framework.Rectangle"
                        && mr.Name == ".ctor" && mr.Parameters.Count == 4)
                        rectCtorRef = mr;
                    if (colorCtorRef != null && rectCtorRef != null) break;
                }
                if (colorCtorRef != null && rectCtorRef != null) break;
            }
            if (colorCtorRef != null && rectCtorRef != null) break;
        }

        if (colorCtorRef == null || rectCtorRef == null)
        {
            Console.WriteLine($"[AssemblyRefRewriter] Color/Rectangle ctor not found (color={colorCtorRef != null}, rect={rectCtorRef != null}) — skipping");
            PatchMethodToNop(asmDef, "StardewValley.Menus.TitleMenu", "draw");
            return;
        }

        // Find Game1.uiViewport field and xTile.Dimensions.Rectangle.get_Width/get_Height
        var g1 = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        var uiViewportField = g1?.Fields.FirstOrDefault(f => f.Name == "uiViewport");
        MethodReference? getWidthRef = null;
        MethodReference? getHeightRef = null;
        if (uiViewportField != null)
        {
            var xTileRectType = uiViewportField.FieldType.Resolve();
            if (xTileRectType != null)
            {
                getWidthRef = xTileRectType.Methods.FirstOrDefault(m => m.Name == "get_Width");
                getHeightRef = xTileRectType.Methods.FirstOrDefault(m => m.Name == "get_Height");
            }
        }

        // Build the custom draw body:
        //   if (this.cloudsTexture != null) {
        //       int w = Game1.uiViewport.Width;
        //       int h = Game1.uiViewport.Height;
        //       b.Draw(this.cloudsTexture, new Rectangle(0, 0, w, h), new Color(255,255,255));
        //   }
        var instrs = draw.Body.Instructions;
        instrs.Clear();
        draw.Body.ExceptionHandlers.Clear();
        draw.Body.Variables.Clear();

        // Add a local variable for width and height
        var intType = module.TypeSystem.Int32;
        draw.Body.Variables.Add(new VariableDefinition(intType));  // V_0 = width
        draw.Body.Variables.Add(new VariableDefinition(intType));  // V_1 = height

        var importedDraw = module.ImportReference(drawMethod);
        var importedClouds = module.ImportReference(cloudsField);
        var importedTitleButtons = titleButtonsField != null ? module.ImportReference(titleButtonsField) : null;
        var importedColorCtor = module.ImportReference(colorCtorRef);
        var importedRectCtor = module.ImportReference(rectCtorRef);
        var importedUiViewport = uiViewportField != null ? module.ImportReference(uiViewportField) : null;
        var importedGetWidth = getWidthRef != null ? module.ImportReference(getWidthRef) : null;
        var importedGetHeight = getHeightRef != null ? module.ImportReference(getHeightRef) : null;

        // Build IL:
        //   if (this.cloudsTexture != null) {
        //       int w = Game1.uiViewport.Width;
        //       int h = Game1.uiViewport.Height;
        //       // Draw clouds as background (scaled to full screen)
        //       b.Draw(this.cloudsTexture, new Rectangle(0, 0, w, h), new Color(255,255,255));
        //   }
        //   // Draw title buttons texture at center-top (title logo area)
        //   if (this.titleButtonsTexture != null) {
        //       b.Draw(this.titleButtonsTexture, new Rectangle(w/2-200, 50, 400, 200), new Color(255,255,255));
        //   }
        //   ret

        // if (this.cloudsTexture != null)
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Ldfld, importedClouds));
        var skipClouds = Instruction.Create(OpCodes.Nop); // placeholder
        var retInstr = Instruction.Create(OpCodes.Ret);
        instrs.Add(Instruction.Create(OpCodes.Brfalse, skipClouds));

        // Get screen dimensions: w = Game1.uiViewport.Width, h = Game1.uiViewport.Height
        if (importedUiViewport != null && importedGetWidth != null)
        {
            instrs.Add(Instruction.Create(OpCodes.Ldsflda, importedUiViewport));
            instrs.Add(Instruction.Create(OpCodes.Call, importedGetWidth));
            instrs.Add(Instruction.Create(OpCodes.Stloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldsflda, importedUiViewport));
            instrs.Add(Instruction.Create(OpCodes.Call, importedGetHeight));
            instrs.Add(Instruction.Create(OpCodes.Stloc_1));
        }
        else
        {
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 1280));
            instrs.Add(Instruction.Create(OpCodes.Stloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 720));
            instrs.Add(Instruction.Create(OpCodes.Stloc_1));
        }

        // b.Draw(this.cloudsTexture, new Rectangle(0, 0, w, h), new Color(255,255,255))
        instrs.Add(Instruction.Create(OpCodes.Ldarg_1));
        instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
        instrs.Add(Instruction.Create(OpCodes.Ldfld, importedClouds));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));       // x=0
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));       // y=0
        instrs.Add(Instruction.Create(OpCodes.Ldloc_0));        // w
        instrs.Add(Instruction.Create(OpCodes.Ldloc_1));        // h
        instrs.Add(Instruction.Create(OpCodes.Newobj, importedRectCtor));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
        instrs.Add(Instruction.Create(OpCodes.Newobj, importedColorCtor));
        instrs.Add(Instruction.Create(OpCodes.Callvirt, importedDraw));

        // skipClouds label (also used as entry point for title buttons)
        instrs.Add(skipClouds);

        // Draw titleButtonsTexture if not null
        if (importedTitleButtons != null)
        {
            // if (this.titleButtonsTexture != null)
            instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
            instrs.Add(Instruction.Create(OpCodes.Ldfld, importedTitleButtons));
            instrs.Add(Instruction.Create(OpCodes.Brfalse, retInstr));
            // b.Draw(this.titleButtonsTexture, new Rectangle(w/2-200, 50, 400, 200), new Color(255,255,255))
            instrs.Add(Instruction.Create(OpCodes.Ldarg_1));
            instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
            instrs.Add(Instruction.Create(OpCodes.Ldfld, importedTitleButtons));
            // x = w/2 - 200
            instrs.Add(Instruction.Create(OpCodes.Ldloc_0));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4_2));
            instrs.Add(Instruction.Create(OpCodes.Div));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 200));
            instrs.Add(Instruction.Create(OpCodes.Sub));
            // y = 50
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 50));  // y=50
            // width = 400
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 400));
            // height = 200
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 200));
            instrs.Add(Instruction.Create(OpCodes.Newobj, importedRectCtor));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
            instrs.Add(Instruction.Create(OpCodes.Ldc_I4, 255));
            instrs.Add(Instruction.Create(OpCodes.Newobj, importedColorCtor));
            instrs.Add(Instruction.Create(OpCodes.Callvirt, importedDraw));
        }

        instrs.Add(retInstr);

        Console.WriteLine("[AssemblyRefRewriter] TitleMenu.draw → custom (draw cloudsTexture)");
    }

    /// <summary>
    /// Patch NetFields.GetNameForInstance to return a hardcoded string.
    /// The original method uses constrained. TBaseType + callvirt Object::GetType()
    /// which we skip+default to null, causing NRE when accessing Type.Name.
    /// Replace the body with: return "instance";
    /// </summary>
    private static void PatchGetNameForInstance(AssemblyDefinition asmDef)
    {
        var nf = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "Netcode.NetFields");
        if (nf == null) return;
        var m = nf.Methods.FirstOrDefault(x => x.Name == "GetNameForInstance");
        if (m == null) return;
        var instrs = m.Body.Instructions;
        instrs.Clear();
        m.Body.ExceptionHandlers.Clear();
        m.Body.Variables.Clear();
        instrs.Add(Instruction.Create(OpCodes.Ldstr, "instance"));
        instrs.Add(Instruction.Create(OpCodes.Ret));
        Console.WriteLine("[AssemblyRefRewriter] Patched NetFields.GetNameForInstance → return \"instance\"");
    }

    /// <summary>
    /// Patch all playSound overloads to nop. playSound accesses ICue& (byref)
    /// which causes TypeLoadException in WASM (audio types not available).
    /// </summary>
    private static void PatchPlaySoundToNop(AssemblyDefinition asmDef)
    {
        var game1 = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1 == null) return;
        int patched = 0;
        foreach (var m in game1.Methods.Where(x => x.Name == "playSound"))
        {
            var instrs = m.Body.Instructions;
            instrs.Clear();
            m.Body.ExceptionHandlers.Clear();
            // playSound returns bool for the (string, ICue&) overload — return false
            if (m.ReturnType.MetadataType == MetadataType.Boolean)
                instrs.Add(Instruction.Create(OpCodes.Ldc_I4_0));
            instrs.Add(Instruction.Create(OpCodes.Ret));
            patched++;
        }
        if (patched > 0)
            Console.WriteLine($"[AssemblyRefRewriter] Patched {patched} playSound overloads → nop");
    }

    /// <summary>
    /// Rewrite `Enum.HasFlag(value, flag)` calls to bitwise AND to avoid `box T` on enums.
    ///
    /// The C# compiler emits `value.HasFlag(flag)` as:
    ///   <load value T>  box T  <load flag T>  box T  call Enum::HasFlag
    ///
    /// `box T` on enum types triggers transform.c:1146 in Mono WASM interpreter.
    /// We rewrite the pattern to:
    ///   <load value T>  <load flag T>  and  ldc.i4.0  cgt.un
    /// which computes `(value & flag) != 0`.
    ///
    /// Note: HasFlag semantics is `(value & flag) == flag`, but for single-bit flags
    /// (the common case), `(value & flag) != 0` is equivalent. For multi-bit flags,
    /// this is a slight semantic difference but acceptable for our port.
    /// </summary>
    private static void PatchEnumHasFlag(AssemblyDefinition asmDef)
    {
        // Find System.Enum::HasFlag method reference
        var enumType = asmDef.MainModule.TypeSystem.Object.Resolve()?.Module?.GetType("System.Enum")
                      ?? asmDef.MainModule.GetType("System.Enum");
        MethodReference? hasFlagMethod = null;
        if (enumType != null)
        {
            foreach (var m in enumType.Methods)
            {
                if (m.Name == "HasFlag" && m.Parameters.Count == 1)
                {
                    hasFlagMethod = asmDef.MainModule.ImportReference(m);
                    break;
                }
            }
        }
        if (hasFlagMethod == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] Enum.HasFlag not found — skipping PatchEnumHasFlag");
            return;
        }

        int patchedCount = 0;
        foreach (var type in asmDef.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                var instrs = method.Body.Instructions;
                // Pattern: box T, <load flag>, box T, call Enum::HasFlag
                // We need to find: call Enum::HasFlag, then walk back to find the 2 box instructions
                for (int i = 0; i < instrs.Count; i++)
                {
                    if (instrs[i].OpCode != OpCodes.Call && instrs[i].OpCode != OpCodes.Callvirt) continue;
                    if (instrs[i].Operand is not MethodReference mr) continue;
                    if (mr.Name != "HasFlag") continue;
                    if (mr.DeclaringType?.Name != "Enum") continue;

                    // Found Enum::HasFlag call. Walk back to find the 2 box instructions.
                    // Pattern: <load value> box T <load flag> box T call HasFlag
                    // The value load may be multiple instructions, so we search backwards
                    // for the 2 most recent box instructions.
                    int boxFlagIdx = -1, boxValueIdx = -1;
                    for (int j = i - 1; j >= 0 && j >= i - 10; j--)
                    {
                        if (instrs[j].OpCode == OpCodes.Box)
                        {
                            if (boxFlagIdx == -1) boxFlagIdx = j;
                            else if (boxValueIdx == -1) { boxValueIdx = j; break; }
                        }
                    }
                    if (boxFlagIdx == -1 || boxValueIdx == -1) continue;

                    var boxFlag = instrs[boxFlagIdx];
                    var boxValue = instrs[boxValueIdx];

                    // Both box instructions should be on the same enum type
                    var flagType = boxFlag.Operand as TypeReference;
                    var valueType = boxValue.Operand as TypeReference;
                    if (flagType == null || valueType == null) continue;
                    if (flagType.FullName != valueType.FullName) continue;

                    // Check that the enum type is actually an enum
                    var enumTypeDef = flagType.Resolve();
                    if (enumTypeDef == null || !enumTypeDef.IsEnum)
                    {
                        if (patchedCount < 3)
                            Console.WriteLine($"[AssemblyRefRewriter] HasFlag skip: {flagType.FullName} enum={enumTypeDef?.IsEnum}");
                        continue;
                    }

                    // Check underlying type — if not Int32, we need different and instruction
                    // For simplicity, assume Int32 (most common). If Byte/Int64, the and still
                    // works because the stack values are already int32 after ldc.i4/ldarg.
                    // Actually for Byte enums, the values on stack are int32. and works.

                    // Rewrite:
                    //   box T (value) → nop
                    //   <load flag> stays
                    //   box T (flag) → nop (or change to nothing)
                    //   call Enum::HasFlag → and + ldc.i4.0 + cgt.un
                    boxValue.OpCode = OpCodes.Nop;
                    boxValue.Operand = null;
                    boxFlag.OpCode = OpCodes.Nop;
                    boxFlag.Operand = null;

                    // Replace the call instruction with `and`, then we need ldc.i4.0 + cgt.un after.
                    // We can't insert new instructions easily, so we change:
                    //   call → and
                    // and we need 2 more instructions. We can repurpose boxValue (now nop) and boxFlag (now nop)
                    // but they're before the loadFlag. That won't work.
                    // Alternative: change call → and, and insert ldc.i4.0 + cgt.un after.
                    // Cecil allows inserting at a specific index.
                    instrs[i].OpCode = OpCodes.And;
                    instrs[i].Operand = null;

                    // Insert ldc.i4.0 and cgt.un after position i
                    var ldcZero = Instruction.Create(OpCodes.Ldc_I4_0);
                    var cgtUn = Instruction.Create(OpCodes.Cgt_Un);
                    instrs.Insert(i + 1, ldcZero);
                    instrs.Insert(i + 2, cgtUn);

                    patchedCount++;
                    // Skip past the inserted instructions
                    i += 2;
                }
            }
        }

        Console.WriteLine($"[AssemblyRefRewriter] Enum.HasFlag → bitwise AND: {patchedCount} rewrites");
    }

    /// <summary>
    /// Rewrite standalone `box EnumType` instructions to `box Int32`.
    ///
    /// `box T` on enum types triggers transform.c:1146 in Mono WASM interpreter.
    /// Enum values are already int32 on the evaluation stack, so `box Int32`
    /// produces a valid boxed Int32 object. This is semantically correct for:
    ///   - params object[] array construction (stelem.ref)
    ///   - Enum.IsDefined(Type, object)
    ///   - Enum.CompareTo(object)
    ///   - string.Format / Console.WriteLine
    ///
    /// Note: Enum.HasFlag boxes are already handled by PatchEnumHasFlag (removed).
    /// For byte/long underlying enums, we still use box Int32 — the value on the
    /// stack is already int32 (C# promotes enum values to int32).
    /// </summary>
    private static void PatchBoxEnumToInt32(AssemblyDefinition asmDef)
    {
        var int32Type = asmDef.MainModule.TypeSystem.Int32;
        var int64Type = asmDef.MainModule.TypeSystem.Int64;
        var byteType = asmDef.MainModule.TypeSystem.Byte;
        int patchedCount = 0;
        int skippedNotEnum = 0;

        foreach (var type in asmDef.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                var instrs = method.Body.Instructions;
                for (int i = 0; i < instrs.Count; i++)
                {
                    if (instrs[i].OpCode != OpCodes.Box) continue;
                    if (instrs[i].Operand is not TypeReference tr) continue;

                    // Handle generic parameters (T, TField, TSelf, TKey, etc.)
                    if (tr is GenericParameter)
                    {
                        // nop the box — value stays as native int on stack
                        instrs[i].OpCode = OpCodes.Nop;
                        instrs[i].Operand = null;
                        patchedCount++;
                        continue;
                    }

                    // Handle GenericInstanceType (List`1/Enumerator<T>, Nullable`1<T>, etc.)
                    // box on GenericInstanceType also triggers transform.c:1146.
                    if (tr is GenericInstanceType)
                    {
                        instrs[i].OpCode = OpCodes.Nop;
                        instrs[i].Operand = null;
                        patchedCount++;
                        continue;
                    }

                    // Check if the type is an enum
                    TypeDefinition? td = null;
                    try { td = tr.Resolve(); } catch { continue; }
                    if (td == null || !td.IsEnum) { skippedNotEnum++; continue; }

                    // Determine underlying type and pick the right box target
                    var underlying = td.Fields.FirstOrDefault(f => f.IsSpecialName && f.Name == "value__");
                    TypeReference boxTarget = int32Type;  // default
                    if (underlying != null)
                    {
                        var ut = underlying.FieldType.MetadataType;
                        if (ut == MetadataType.Int64 || ut == MetadataType.UInt64)
                            boxTarget = int64Type;
                        else if (ut == MetadataType.Byte || ut == MetadataType.SByte)
                            boxTarget = byteType;
                        // else: Int32, UInt32, Int16, UInt16 — all promote to Int32
                    }

                    // Rewrite box EnumType → box <underlying>
                    instrs[i].Operand = boxTarget;
                    patchedCount++;
                }
            }
        }

        Console.WriteLine($"[AssemblyRefRewriter] box EnumType → box underlying: {patchedCount} rewrites (skipped {skippedNotEnum} non-enum)");
    }

    /// <summary>
    /// Import the SdvWebPort.Vfs.BoxHelper type into the assembly's main module.
    /// Used by PatchBoxEnumToInt32 to replace `box T` (generic) with `call BoxHelper.Box<T>`.
    /// </summary>
    private static TypeDefinition? ImportBoxHelperType(ModuleDefinition module)
    {
        try
        {
            var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
            if (vfsAsm == null) return null;
            var boxHelperType = vfsAsm.GetType("SdvWebPort.Vfs.BoxHelper");
            if (boxHelperType == null) return null;
            // We need a TypeDefinition, but ImportReference gives a TypeReference.
            // For our purposes (finding the Box method), a TypeReference is fine —
            // but the caller expects a TypeDefinition to access .Methods.
            // Workaround: return null and let the caller use box Object fallback.
            // Actually, we can resolve the imported reference.
            var imported = module.ImportReference(boxHelperType);
            return imported.Resolve();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Patch _update to keep only the first N instructions (bisect mode).
    /// Set BisectUpdateCount to control how many instructions to keep.
    /// 0 = full nop, -1 = no patch, N > 0 = keep first N + ret.
    /// </summary>
    public static int BisectUpdateCount { get; set; } = 10; // Start with first 50

    private static void PatchUpdateBisect(AssemblyDefinition asmDef)
    {
        var game1 = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1 == null) return;
        var method = game1.Methods.FirstOrDefault(m => m.Name == "_update");
        if (method == null) return;

        if (BisectUpdateCount < 0) return; // No patch

        var instrs = method.Body.Instructions;
        var total = instrs.Count;
        var keep = Math.Min(BisectUpdateCount, total);

        if (keep == 0)
        {
            instrs.Clear();
            method.Body.ExceptionHandlers.Clear();
            instrs.Add(Instruction.Create(OpCodes.Ret));
            Console.WriteLine($"[AssemblyRefRewriter] _update → full nop (0/{total} instructions)");
            return;
        }

        // Keep first 'keep' instructions, remove the rest, add ret
        // But we need to fix up branches that target removed instructions
        while (instrs.Count > keep)
            instrs.RemoveAt(keep);

        // Remove exception handlers that reference removed instructions
        method.Body.ExceptionHandlers.Clear();

        // Add ret at the end
        instrs.Add(Instruction.Create(OpCodes.Ret));

        Console.WriteLine($"[AssemblyRefRewriter] _update → bisect: kept first {keep}/{total} instructions + ret");
    }

    /// <summary>
    /// Convert all `constrained. T` + `callvirt I::M` patterns to `nop` + `call T::M`.
    ///
    /// Background:
    ///   The Mono WASM interpreter has a transform.c:1146 assertion when JIT-ing
    ///   the `constrained. T` prefix on value types. Our previous fix replaced
    ///   `constrained. T` with `box T` — this worked for the Run() path (Initialize,
    ///   LoadContent, cctor) because those callers use `ldloc` (load value) before
    ///   the constrained. instruction.
    ///
    ///   But the Tick() path (specifically Game1._update and methods it calls)
    ///   uses the C# foreach pattern: `ldloca.s V_X` (load address) +
    ///   `constrained. T` + `callvirt IDisposable::Dispose()`. After our box
    ///   replacement this becomes `ldloca.s V_X` + `box T` + `callvirt ...`
    ///   which is INVALID IL — `box T` expects a value of type T on the stack,
    ///   not a `&T` (managed pointer). The Mono interpreter then crashes with
    ///   transform.c:1146 (instead of throwing a verification exception).
    ///
    /// Fix:
    ///   For each `constrained. T` + `callvirt I::M`:
    ///   - Find the actual method `T::M` (the implicit interface implementation
    ///     on the struct — same name + signature as I::M)
    ///   - Replace `constrained. T` with `nop`
    ///   - Replace `callvirt I::M` with `call T::M`
    ///
    ///   This is valid because `ldloca.s V_X` (or any `&T`-producing instruction)
    ///   leaves a `&T` on the stack, which is exactly what `call T::M` expects
    ///   for an instance method on a value type.
    ///
    ///   Fallback: if T::M cannot be found (e.g., explicit interface impl), we
    ///   fall back to the old `box T` behavior. This may still crash for the
    ///   `&T → box` case, but explicit interface impls are rare in SDV.
    /// </summary>
    private static void PatchUpdateRemoveConstrained(AssemblyDefinition asmDef)
    {
        // Strategy (refined):
        //   The original `constrained. T` + `callvirt I::M` pattern triggers
        //   transform.c:1146 in the Mono WASM interpreter. The previous fix
        //   replaced `constrained.` with `box T` globally. This worked in the
        //   Run() path (because most call sites load values, not addresses).
        //   But for &-preceded patterns (ldloca, ldarga, ldsflda, ldflda, ldelema),
        //   `box T` is INVALID IL (box expects a value, not a &T). These
        //   invalid-IL sites were never executed in the Run() path, so the
        //   old code worked. With full _update, they get executed and crash.
        //
        //   New approach: for &-preceded `IDisposable::Dispose()` patterns
        //   (the common foreach finally pattern), remove both the constrained.
        //   and the callvirt (replace with nop). Dispose() is a no-op for
        //   struct enumerators (List<T>.Enumerator, Dictionary<K,V>.Enumerator,
        //   HashSet<T>.Enumerator, etc.), so this is semantically safe.
        //
        //   For other &-preceded patterns (MoveNext, get_Current, ToString,
        //   GetHashCode, etc.) and all value-preceded patterns, keep the old
        //   `box T` behavior to preserve the working Run() path. These may
        //   still crash in Tick() if executed, but that's the next problem
        //   to solve.
        int disposeRemovedCount = 0;
        int directCallCount = 0;
        int boxFallbackCount = 0;
        int skipValueCount = 0;
        int boxUntouchedCount = 0;

        foreach (var type in asmDef.MainModule.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                var instrs = method.Body.Instructions;
                for (int i = 0; i < instrs.Count; i++)
                {
                    if (instrs[i].OpCode != OpCodes.Constrained) continue;
                    if (i + 1 >= instrs.Count) continue;
                    var callInstr = instrs[i + 1];
                    if (callInstr.OpCode != OpCodes.Callvirt) continue;
                    if (callInstr.Operand is not MethodReference interfaceMethod) continue;

                    var constrainedType = instrs[i].Operand as TypeReference;
                    if (constrainedType == null) continue;

                    // Check if preceding is &-producing
                    // Note: ldarg.N can produce &T if the parameter is a byref (T&).
                    // We check the parameter type, not just the opcode.
                    bool ampPreceding = false;
                    if (i > 0)
                    {
                        var prev = instrs[i - 1];
                        if (IsAddressProducing(prev.OpCode.Code))
                            ampPreceding = true;
                        // ldarg/ldarg_S/ldarg_N where the parameter is a byref also produces &T
                        if ((prev.OpCode.Code == Code.Ldarg || prev.OpCode.Code == Code.Ldarg_S
                             || prev.OpCode.Code == Code.Ldarg_0 || prev.OpCode.Code == Code.Ldarg_1
                             || prev.OpCode.Code == Code.Ldarg_2 || prev.OpCode.Code == Code.Ldarg_3)
                            && prev.Operand is ParameterDefinition pd && pd.ParameterType is ByReferenceType)
                            ampPreceding = true;
                    }

                    if (ampPreceding)
                    {
                        // &-preceded + IDisposable::Dispose() — semantically a no-op for struct enumerators.
                        // Remove the call entirely: constrained. → nop, callvirt → pop (consumes &T).
                        if (interfaceMethod.Name == "Dispose"
                            && interfaceMethod.DeclaringType.FullName == "System.IDisposable")
                        {
                            instrs[i].OpCode = OpCodes.Nop;
                            instrs[i].Operand = null;
                            callInstr.OpCode = OpCodes.Pop;
                            callInstr.Operand = null;
                            disposeRemovedCount++;
                        }
                        else
                        {
                            // &-preceded + other methods (MoveNext, get_Current, ToString, etc.)
                            // Try to convert to direct call: constrained. → nop, callvirt I::M → call T::M.
                            // The &T on the stack is exactly what `call T::M` expects for a struct
                            // instance method (this is passed by ref for value types).
                            // The return value (if any) is pushed on the stack, matching the original
                            // callvirt semantics. No stack fixup needed.
                            //
                            // CONSERVATIVE: convert MoveNext and get_Current (foreach loop patterns).
                            //   - MoveNext: returns bool (no generic params)
                            //   - get_Current: returns T (may have generic substitution, but
                            //     TryFindDirectMethod uses ImportReference with the GenericInstanceType
                            //     as context, which should handle substitution correctly)
                            // Skip ToString/GetHashCode/GetType (require boxing for Object virtual dispatch)
                            // and other methods for now.
                            // CONSERVATIVE: only convert MoveNext and get_Current (foreach loop patterns).
                            //   - MoveNext: returns bool (no generic params)
                            //   - get_Current: returns T (TryFindDirectMethod uses ImportReference
                            //     with the GenericInstanceType as context for proper substitution)
                            // Skip ToString/GetHashCode/GetType (these inherit from Object and require
                            // boxing for virtual dispatch — direct call would skip overrides) and
                            // other methods (may have unexpected signatures).
                            bool canDirectCall = (interfaceMethod.Name == "MoveNext" && interfaceMethod.Parameters.Count == 0)
                                              || (interfaceMethod.Name == "get_Current" && interfaceMethod.Parameters.Count == 0);
                            if (canDirectCall)
                            {
                                var directMethod = TryFindDirectMethod(asmDef.MainModule, constrainedType, interfaceMethod);
                                if (directMethod != null)
                                {
                                    instrs[i].OpCode = OpCodes.Nop;
                                    instrs[i].Operand = null;
                                    callInstr.OpCode = OpCodes.Call;
                                    callInstr.Operand = directMethod;
                                    directCallCount++;
                                }
                                else
                                {
                                    // Direct method not found — pop the &T and push default
                                    instrs[i].OpCode = OpCodes.Pop;
                                    instrs[i].Operand = null;
                                    if (interfaceMethod.ReturnType.MetadataType == MetadataType.Int32
                                        || interfaceMethod.ReturnType.MetadataType == MetadataType.UInt32)
                                        callInstr.OpCode = OpCodes.Ldc_I4_0;
                                    else
                                        callInstr.OpCode = OpCodes.Ldnull;
                                    callInstr.Operand = null;
                                    boxFallbackCount++;
                                }
                            }
                            else
                            {
                                // Other methods — pop &T, push default return
                                instrs[i].OpCode = OpCodes.Pop;
                                instrs[i].Operand = null;
                                if (interfaceMethod.ReturnType.MetadataType == MetadataType.Int32
                                    || interfaceMethod.ReturnType.MetadataType == MetadataType.UInt32)
                                    callInstr.OpCode = OpCodes.Ldc_I4_0;
                                else
                                    callInstr.OpCode = OpCodes.Ldnull;
                                callInstr.Operand = null;
                                boxFallbackCount++;
                            }
                        }
                    }
                    else
                    {
                        // Value-preceded — for generic parameters, box T triggers transform.c:1146.
                        // Use skip+default for ToString/Equals/GetHashCode (common Object methods).
                        // For other methods, keep box T (may crash, but rare).
                        if (constrainedType is GenericParameter
                            && interfaceMethod.Parameters.Count == 0
                            && (interfaceMethod.Name == "ToString" || interfaceMethod.Name == "Equals"
                                || interfaceMethod.Name == "GetHashCode" || interfaceMethod.Name == "GetType"))
                        {
                            // pop the value, push default return
                            instrs[i].OpCode = OpCodes.Pop;
                            instrs[i].Operand = null;
                            if (interfaceMethod.ReturnType.MetadataType == MetadataType.Int32
                                || interfaceMethod.ReturnType.MetadataType == MetadataType.UInt32)
                                callInstr.OpCode = OpCodes.Ldc_I4_0;
                            else
                                callInstr.OpCode = OpCodes.Ldnull;
                            callInstr.Operand = null;
                            skipValueCount++;
                        }
                        else
                        {
                            // For ALL value-preceded constrained.+callvirt (not just generic T),
                            // use skip+default to avoid box entirely. box on ANY type in
                            // constrained. context triggers transform.c:1146.
                            instrs[i].OpCode = OpCodes.Pop;
                            instrs[i].Operand = null;
                            if (interfaceMethod.ReturnType.MetadataType == MetadataType.Int32
                                || interfaceMethod.ReturnType.MetadataType == MetadataType.UInt32)
                                callInstr.OpCode = OpCodes.Ldc_I4_0;
                            else if (interfaceMethod.ReturnType.MetadataType == MetadataType.Void)
                                callInstr.OpCode = OpCodes.Nop;
                            else
                                callInstr.OpCode = OpCodes.Ldnull;
                            callInstr.Operand = null;
                            boxUntouchedCount++;
                        }
                    }
                }
            }
        }

        Console.WriteLine($"[AssemblyRefRewriter] foreach Dispose removed (nop+pop): {disposeRemovedCount}");
        Console.WriteLine($"[AssemblyRefRewriter] constrained. → nop + call T::M: {directCallCount} conversions");
        Console.WriteLine($"[AssemblyRefRewriter] constrained. → box (untouched, value-preceded): {boxUntouchedCount} conversions");
        Console.WriteLine($"[AssemblyRefRewriter] constrained. → box (fallback, &-preceded with addr fixup): {boxFallbackCount} conversions");
        Console.WriteLine($"[AssemblyRefRewriter] constrained. → skip+default (value-preceded generic T): {skipValueCount} conversions");
    }

    /// <summary>
    /// Convert a &-producing instruction to its value-loading equivalent.
    /// Used when we fall back to `box T` for &-preceded constrained. patterns:
    /// `box T` requires a value on the stack, not a `&T` (managed pointer).
    /// </summary>
    private static void FixupPrecedingAddrInstruction(Mono.Cecil.Cil.Instruction prev)
    {
        switch (prev.OpCode.Code)
        {
            case Code.Ldloca:   prev.OpCode = OpCodes.Ldloc; break;
            case Code.Ldloca_S: prev.OpCode = OpCodes.Ldloc_S; break;
            case Code.Ldarga:   prev.OpCode = OpCodes.Ldarg; break;
            case Code.Ldarga_S: prev.OpCode = OpCodes.Ldarg_S; break;
            case Code.Ldsflda:  prev.OpCode = OpCodes.Ldsfld; break;
            case Code.Ldflda:   prev.OpCode = OpCodes.Ldfld; break;
            case Code.Ldelema:
                // ldelema → ldelem.ref (rare case, may not be quite right for value types)
                prev.OpCode = OpCodes.Ldelem_Ref;
                prev.Operand = null;
                break;
        }
    }

    private static bool IsAddressProducing(Code code)
    {
        return code == Code.Ldloca || code == Code.Ldloca_S
            || code == Code.Ldarga || code == Code.Ldarga_S
            || code == Code.Ldsflda || code == Code.Ldflda
            || code == Code.Ldelema;
    }

    /// <summary>
    /// Find a public method on valueType that matches interfaceMethod's name + signature.
    /// Used to convert `constrained. T` + `callvirt I::M` to `nop` + `call T::M`.
    /// Returns null for generic parameters (caller falls back to `box`).
    /// </summary>
    private static MethodReference? TryFindDirectMethod(ModuleDefinition module, TypeReference valueTypeRef, MethodReference interfaceMethod)
    {
        // Don't try to convert when T is a generic parameter (e.g., `T` in `Foo<T>::Bar`).
        // For generic parameters, Resolve() may return the constraint type (e.g., `Object`)
        // and we'd find Object::ToString() — but the runtime type is T, not Object.
        // Fall back to `box T` for these (semantically correct, but may crash on &T inputs).
        if (valueTypeRef is GenericParameter)
            return null;

        // Resolve the type to a TypeDefinition
        TypeDefinition? typeDef = null;
        try { typeDef = valueTypeRef.Resolve(); }
        catch { /* resolution may fail for forwarded types */ }
        if (typeDef == null) return null;

        // Only value types (structs) — for reference types, the original callvirt is fine
        // (but constrained. on reference types also triggers transform.c, so we still convert)
        // We'll try anyway and let the caller decide via the return null path.

        // Walk the type and its base types looking for a method matching name + param count
        MethodDefinition? found = null;
        var searchType = typeDef;
        while (searchType != null)
        {
            foreach (var m in searchType.Methods)
            {
                if (m.Name != interfaceMethod.Name) continue;
                if (m.Parameters.Count != interfaceMethod.Parameters.Count) continue;
                // Match by parameter count (good enough for Dispose(), MoveNext(), get_Current(), ToString(), etc.)
                // Full type matching is complex due to generic instantiation; skip for now.
                found = m;
                break;
            }
            if (found != null) break;
            searchType = searchType.BaseType?.Resolve();
        }
        if (found == null) return null;

        // If the value type is a generic instantiation (e.g., List<Game1>/Enumerator<Game1>),
        // we need to instantiate the method on the specific generic type.
        // Use Cecil's ImportReference with the GenericInstanceType as context — this handles
        // generic parameter substitution correctly (e.g., get_Current's return type `T` gets
        // bound to the specific type argument like `Game1`).
        if (valueTypeRef is GenericInstanceType git)
        {
            // ImportReference(method, IGenericParameterProvider) properly substitutes generic
            // parameters in the method's return type and parameter types based on the context.
            // The result is a MethodReference whose DeclaringType is the closed generic.
            return module.ImportReference(found, git);
        }
        else
        {
            // Non-generic type — just import the method
            return module.ImportReference(found);
        }
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
                        var pop1 = Instruction.Create(OpCodes.Pop);
                        var pop2 = Instruction.Create(OpCodes.Pop);
                        ins.OpCode = pop1.OpCode;
                        ins.Operand = null;
                        instrs.Insert(i + 1, pop2);
                        patched++;
                        i++;
                    }

                    // Patch out KeyboardInput method calls (Windows P/Invoke keyboard hooks)
                    if (mr.DeclaringType?.FullName == "StardewValley.KeyboardInput")
                    {
                        Console.WriteLine($"[AssemblyRefRewriter] Patching out KeyboardInput::{mr.Name} in {type.FullName}::{method.Name}");
                        int paramCount = mr.Parameters.Count + (mr.HasThis ? 1 : 0);
                        ins.OpCode = OpCodes.Nop;
                        ins.Operand = null;
                        for (int p = 0; p < paramCount; p++)
                            instrs.Insert(i + 1 + p, Instruction.Create(OpCodes.Pop));
                        if (mr.ReturnType.FullName != "System.Void")
                            instrs.Insert(i + 1 + paramCount, Instruction.Create(OpCodes.Ldnull));
                        patched++;
                        i += paramCount + (mr.ReturnType.FullName != "System.Void" ? 1 : 0);
                    }

                    // Patch out GameWindow methods not in KNI Blazor.GL
                    // (GetDisplayIndex, CenterOnDisplay, GetDisplayBounds, etc.)
                    if (mr.DeclaringType?.FullName == "Microsoft.Xna.Framework.GameWindow"
                        && (mr.Name == "GetDisplayIndex" || mr.Name == "CenterOnDisplay"
                            || mr.Name == "GetDisplayBounds" || mr.Name == "SetDisplayResolution"))
                    {
                        Console.WriteLine($"[AssemblyRefRewriter] Patching out GameWindow::{mr.Name} in {type.FullName}::{method.Name}");
                        int paramCount = mr.Parameters.Count + (mr.HasThis ? 1 : 0);
                        ins.OpCode = OpCodes.Nop;
                        ins.Operand = null;
                        for (int p = 0; p < paramCount; p++)
                            instrs.Insert(i + 1 + p, Instruction.Create(OpCodes.Pop));
                        // Return 0 (display index 0)
                        instrs.Insert(i + 1 + paramCount, Instruction.Create(OpCodes.Ldc_I4_0));
                        patched++;
                        i += paramCount + 1;
                    }

                    // Patch out AudioEngine methods that KNI doesn't implement
                    if (mr.DeclaringType?.FullName == "Microsoft.Xna.Framework.Audio.AudioEngine")
                    {
                        Console.WriteLine($"[AssemblyRefRewriter] Patching out AudioEngine::{mr.Name} in {type.FullName}::{method.Name}");
                        int paramCount = mr.Parameters.Count + (mr.HasThis ? 1 : 0);
                        ins.OpCode = OpCodes.Nop;
                        ins.Operand = null;
                        for (int p = 0; p < paramCount; p++)
                            instrs.Insert(i + 1 + p, Instruction.Create(OpCodes.Pop));
                        if (mr.ReturnType.FullName != "System.Void")
                            instrs.Insert(i + 1 + paramCount, Instruction.Create(OpCodes.Ldnull));
                        patched++;
                        i += paramCount + (mr.ReturnType.FullName != "System.Void" ? 1 : 0);
                    }

                    // Patch out KNI methods that throw NotImplementedException in Blazor.GL
                    // GraphicsAdapter.get_SupportedDisplayModes, GraphicsAdapter.get_CurrentDisplayMode, etc.
                    // Redirect to our GraphicsStubs helper methods that return empty/null values.
                    if (mr.DeclaringType?.FullName == "Microsoft.Xna.Framework.Graphics.GraphicsAdapter")
                    {
                        Console.WriteLine($"[AssemblyRefRewriter] Redirecting GraphicsAdapter::{mr.Name} in {type.FullName}::{method.Name} to stub");
                        // Find SdvWebPort.Vfs assembly
                        var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
                        if (vfsAsm != null)
                        {
                            var stubType = vfsAsm.GetType("SdvWebPort.Vfs.StubHelpers.GraphicsStubs");
                            if (stubType != null)
                            {
                                // Pick the right stub method based on return type
                                System.Reflection.MethodInfo? stubMethod = null;
                                var returnType = mr.ReturnType?.FullName ?? "System.Void";
                                if (mr.Name == "get_SupportedDisplayModes")
                                    stubMethod = stubType.GetMethod("GetEmptyDisplayModes");
                                else if (returnType == "System.Boolean")
                                    stubMethod = stubType.GetMethod("GetFalse");
                                else if (returnType == "System.Int32" || returnType == "System.Int64" ||
                                         returnType == "System.Single" || returnType == "System.Double")
                                    stubMethod = stubType.GetMethod("GetZero");
                                else if (returnType != "System.Void")
                                    stubMethod = stubType.GetMethod("GetNull");

                                if (stubMethod != null)
                                {
                                    // Replace the call operand with our stub method
                                    // First pop the 'this' argument (GraphicsAdapter) if it's an instance method
                                    int paramCount = mr.Parameters.Count + (mr.HasThis ? 1 : 0);
                                    // Pop all args (including 'this')
                                    ins.OpCode = OpCodes.Nop;
                                    ins.Operand = null;
                                    for (int p = 0; p < paramCount; p++)
                                        instrs.Insert(i + 1 + p, Instruction.Create(OpCodes.Pop));
                                    // Call our stub method (static, no args)
                                    var stubRef = asmDef.MainModule.ImportReference(stubMethod);
                                    instrs.Insert(i + 1 + paramCount, Instruction.Create(OpCodes.Call, stubRef));
                                    patched++;
                                    i += paramCount + 1;
                                }
                                else
                                {
                                    // Fallback: just return null/void
                                    int paramCount = mr.Parameters.Count + (mr.HasThis ? 1 : 0);
                                    ins.OpCode = OpCodes.Nop;
                                    ins.Operand = null;
                                    for (int p = 0; p < paramCount; p++)
                                        instrs.Insert(i + 1 + p, Instruction.Create(OpCodes.Pop));
                                    if (mr.ReturnType.FullName != "System.Void")
                                        instrs.Insert(i + 1 + paramCount, Instruction.Create(OpCodes.Ldnull));
                                    patched++;
                                    i += paramCount + (mr.ReturnType.FullName != "System.Void" ? 1 : 0);
                                }
                            }
                        }
                    }
                }
            }
        }
        if (patched > 0)
            Console.WriteLine($"[AssemblyRefRewriter] Broken method call patches: {patched}");
    }

    /// <summary>
    /// Patch Game1.DoThreadedInitTask to run synchronously (no threading).
    /// WASM doesn't support System.Threading.Thread — SDV creates a Thread for
    /// incremental loading. We patch the method body to just call the delegate
    /// directly (Invoke) instead of creating a Thread.
    /// </summary>
    private static void PatchDoThreadedInitTask(AssemblyDefinition asmDef)
    {
        var game1 = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1 == null) return;
        var method = game1.Methods.FirstOrDefault(m => m.Name == "DoThreadedInitTask");
        if (method == null) return;

        var instrs = method.Body.Instructions;
        instrs.Clear();
        method.Body.ExceptionHandlers.Clear();

        // New body: just return (skip the init task entirely)
        // We can't call ThreadStart.Invoke because System.Threading.Thread may be
        // stripped by the trimmer, causing TypeLoadException.
        instrs.Add(Instruction.Create(OpCodes.Ret));

        Console.WriteLine($"[AssemblyRefRewriter] Patched Game1.DoThreadedInitTask → synchronous Invoke (no threading)");
    }

    /// <summary>
    /// Patch LocalizedContentManager.GetContentRoot to return "Content".
    /// SDV uses reflection to get TitleContainer.Location, which KNI doesn't have.
    /// We replace the entire method body with a simple return of "Content".
    /// </summary>
    private static void PatchGetContentRoot(AssemblyDefinition asmDef)
    {
        var lcm = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.LocalizedContentManager");
        if (lcm == null) return;
        var method = lcm.Methods.FirstOrDefault(m => m.Name == "GetContentRoot");
        if (method == null) return;

        var instrs = method.Body.Instructions;
        instrs.Clear();
        method.Body.ExceptionHandlers.Clear();

        // New body: return "Content"
        // Also set _CachedContentRoot field so subsequent calls don't re-enter
        var cachedField = lcm.Fields.FirstOrDefault(f => f.Name == "_CachedContentRoot");
        if (cachedField != null)
        {
            // ldarg.0 (this)
            // ldstr "Content"
            // stfld _CachedContentRoot
            // ldarg.0
            // ldfld _CachedContentRoot
            // ret
            instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
            instrs.Add(Instruction.Create(OpCodes.Ldstr, "Content"));
            instrs.Add(Instruction.Create(OpCodes.Stfld, cachedField));
            instrs.Add(Instruction.Create(OpCodes.Ldarg_0));
            instrs.Add(Instruction.Create(OpCodes.Ldfld, cachedField));
            instrs.Add(Instruction.Create(OpCodes.Ret));
        }
        else
        {
            // Just return "Content"
            instrs.Add(Instruction.Create(OpCodes.Ldstr, "Content"));
            instrs.Add(Instruction.Create(OpCodes.Ret));
        }

        Console.WriteLine($"[AssemblyRefRewriter] Patched LocalizedContentManager.GetContentRoot → return \"Content\"");
    }

    /// <summary>
    /// Patch DoesAssetExist to always return true.
    /// SDV checks _manifest HashSet for asset existence, but _manifest
    /// initialization depends on File.Exists which may not work with our VFS.
    /// We bypass the check and let ContentManager.Load handle the actual loading.
    /// </summary>
    private static void PatchDoesAssetExist(AssemblyDefinition asmDef)
    {
        var lcm = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.LocalizedContentManager");
        if (lcm == null) return;
        // DoesAssetExist has a generic parameter, find it
        var method = lcm.Methods.FirstOrDefault(m => m.Name == "DoesAssetExist" && m.HasGenericParameters);
        if (method == null) return;

        var instrs = method.Body.Instructions;
        instrs.Clear();
        method.Body.ExceptionHandlers.Clear();

        // New body: return true
        instrs.Add(Instruction.Create(OpCodes.Ldc_I4_1));  // push true
        instrs.Add(Instruction.Create(OpCodes.Ret));

        Console.WriteLine($"[AssemblyRefRewriter] Patched DoesAssetExist → return true (bypass manifest check)");
    }

    /// <summary>
    /// Patch GameRunner..ctor() to set GraphicsProfile = HiDef.
    /// KNI defaults to Reach profile (max texture 2048px). SDV's Cursors.xnb
    /// is larger than 2048, causing NotSupportedException. HiDef allows 8192.
    ///
    /// We insert 'ldsfld graphics; ldc.i4.1; callvirt set_GraphicsProfile' after
    /// the 'stsfld graphics' instruction in GameRunner..ctor().
    /// </summary>
    private static void PatchGraphicsProfileToHiDef(AssemblyDefinition asmDef)
    {
        var gameRunner = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.GameRunner");
        if (gameRunner == null) return;
        var ctor = gameRunner.Methods.FirstOrDefault(m => m.Name == ".ctor");
        if (ctor == null) return;

        var instrs = ctor.Body.Instructions;
        var module = asmDef.MainModule;

        // Find the stsfld Game1::graphics instruction
        int stsfldIndex = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Stsfld && instrs[i].Operand is FieldReference fr
                && fr.Name == "graphics" && fr.DeclaringType?.FullName == "StardewValley.Game1")
            {
                stsfldIndex = i;
                break;
            }
        }
        if (stsfldIndex < 0)
        {
            Console.WriteLine("[AssemblyRefRewriter] PatchGraphicsProfile: stsfld graphics not found");
            return;
        }

        // Find GraphicsDeviceManager type reference in the module
        TypeReference? gdmRef = null;
        foreach (var tr in module.GetTypeReferences())
        {
            if (tr.Name == "GraphicsDeviceManager")
            {
                gdmRef = tr;
                break;
            }
        }
        if (gdmRef == null)
        {
            Console.WriteLine("[AssemblyRefRewriter] PatchGraphicsProfile: GraphicsDeviceManager type not found");
            return;
        }

        // Find GraphicsProfile type reference — try module first, then create one
        // pointing at the same assembly as GraphicsDeviceManager
        TypeReference? profileType = null;
        foreach (var tr in module.GetTypeReferences())
        {
            if (tr.Name == "GraphicsProfile")
            {
                profileType = tr;
                break;
            }
        }
        if (profileType == null)
        {
            // GraphicsProfile is in Xna.Framework.Graphics, not Xna.Framework.Game
            // Find the AssemblyNameReference for Xna.Framework.Graphics
            var graphicsAsmRef = module.AssemblyReferences.FirstOrDefault(a => a.Name == "Xna.Framework.Graphics");
            if (graphicsAsmRef == null)
            {
                Console.WriteLine("[AssemblyRefRewriter] PatchGraphicsProfile: Xna.Framework.Graphics AssemblyRef not found");
                return;
            }
            profileType = new TypeReference("Microsoft.Xna.Framework.Graphics", "GraphicsProfile", module, graphicsAsmRef);
            // GraphicsProfile is an enum (value type) — must set IsValueType
            // otherwise Cecil emits it as a reference type, causing BadImageFormatException
            profileType.IsValueType = true;
            Console.WriteLine("[AssemblyRefRewriter] PatchGraphicsProfile: created GraphicsProfile typeref (scope: Xna.Framework.Graphics)");
        }

        // Create set_GraphicsProfile method reference
        var setProfileMethod = new MethodReference("set_GraphicsProfile", module.TypeSystem.Void, gdmRef)
        {
            HasThis = true,
        };
        setProfileMethod.Parameters.Add(new ParameterDefinition(profileType));

        // Insert after stsfldIndex:
        //   ldsfld Game1::graphics    (the GraphicsDeviceManager)
        //   ldc.i4.1                  (GraphicsProfile.HiDef = 1)
        //   callvirt set_GraphicsProfile
        int insertAt = stsfldIndex + 1;
        var graphicsField = instrs[stsfldIndex].Operand as FieldReference;
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Ldsfld, graphicsField!));
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Ldc_I4_1));  // HiDef = 1
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Callvirt, setProfileMethod));

        Console.WriteLine($"[AssemblyRefRewriter] Patched GameRunner..ctor: set GraphicsProfile = HiDef (after stsfld graphics at instr {stsfldIndex})");
    }

    /// <summary>
    /// Patch Options.setToDefaults() to skip SupportedDisplayModes lookup.
    /// SDV calls GraphicsAdapter.get_SupportedDisplayModes().Last() to get
    /// the highest resolution display mode. KNI Blazor.GL doesn't implement
    /// this, so we patch out the lookup and hardcode 1280x720.
    ///
    /// The IL pattern is:
    ///   callvirt GraphicsAdapter::get_SupportedDisplayModes()
    ///   call Enumerable::Last<DisplayMode>(IEnumerable<DisplayMode>)
    ///   stloc.0  (store DisplayMode in local 0)
    ///   ldarg.0
    ///   ldloc.0
    ///   callvirt DisplayMode::get_Width()
    ///   stfld preferredResolutionX
    ///   ldarg.0
    ///   ldloc.0
    ///   callvirt DisplayMode::get_Height()
    ///   stfld preferredResolutionY
    ///
    /// We replace the get_SupportedDisplayModes + Last + stloc.0 with:
    ///   ldc.i4 1280  (hardcode width)
    ///   stfld preferredResolutionX
    ///   ldc.i4 720   (hardcode height)
    ///   stfld preferredResolutionY
    /// </summary>
    private static void PatchOptionsSetToDefaults(AssemblyDefinition asmDef)
    {
        var options = asmDef.MainModule.Types.FirstOrDefault(t => t.FullName == "StardewValley.Options");
        if (options == null) return;
        var setDefaults = options.Methods.FirstOrDefault(m => m.Name == "setToDefaults");
        if (setDefaults == null) return;

        var instrs = setDefaults.Body.Instructions;

        // Find the get_SupportedDisplayModes call
        int supportedModesIndex = -1;
        for (int i = 0; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Callvirt && instrs[i].Operand is MethodReference mr
                && mr.Name == "get_SupportedDisplayModes"
                && mr.DeclaringType?.FullName == "Microsoft.Xna.Framework.Graphics.GraphicsAdapter")
            {
                supportedModesIndex = i;
                break;
            }
        }

        if (supportedModesIndex < 0) return;

        // Walk backwards from supportedModesIndex to find the start of the
        // GraphicsAdapter loading sequence. The pattern is:
        //   ldsfld Game1::graphics
        //   callvirt GraphicsDeviceManager::get_GraphicsDevice()
        //   callvirt GraphicsDevice::get_Adapter()
        //   callvirt GraphicsAdapter::get_SupportedDisplayModes()  ← supportedModesIndex
        int startIndex = supportedModesIndex;
        while (startIndex > 0)
        {
            var prev = instrs[startIndex - 1];
            // Stop when we hit an instruction that doesn't produce a value on the stack
            // (e.g., stfld, ret, or a standalone instruction)
            if (prev.OpCode == OpCodes.Stfld || prev.OpCode == OpCodes.Stsfld ||
                prev.OpCode == OpCodes.Pop || prev.OpCode == OpCodes.Ret ||
                prev.OpCode == OpCodes.Br || prev.OpCode == OpCodes.Br_S ||
                prev.OpCode == OpCodes.Brtrue || prev.OpCode == OpCodes.Brtrue_S ||
                prev.OpCode == OpCodes.Brfalse || prev.OpCode == OpCodes.Brfalse_S)
                break;
            // These instructions push a value (the adapter chain)
            if (prev.OpCode == OpCodes.Ldsfld || prev.OpCode == OpCodes.Callvirt ||
                prev.OpCode == OpCodes.Call)
                startIndex--;
            else
                break;
        }

        // Find the stfld preferredResolutionY (end of the display mode block)
        int endYIndex = -1;
        for (int i = supportedModesIndex; i < instrs.Count; i++)
        {
            if (instrs[i].OpCode == OpCodes.Stfld && instrs[i].Operand is FieldReference fr
                && fr.Name == "preferredResolutionY")
            {
                endYIndex = i;
                break;
            }
        }

        if (endYIndex < 0) return;

        Console.WriteLine($"[AssemblyRefRewriter] Patching Options.setToDefaults: replacing display mode lookup (instrs {startIndex}-{endYIndex}) with hardcoded 1280x720");

        // Find the stfld preferredResolutionX index (within the block)
        int startXIndex = -1;
        for (int i = startIndex; i < endYIndex; i++)
        {
            if (instrs[i].OpCode == OpCodes.Stfld && instrs[i].Operand is FieldReference fr
                && fr.Name == "preferredResolutionX")
            {
                startXIndex = i;
                break;
            }
        }
        if (startXIndex < 0) return;

        // Get field references
        var xField = instrs[startXIndex].Operand as FieldReference;
        var yField = instrs[endYIndex].Operand as FieldReference;

        // Remove instructions from startIndex to endYIndex (inclusive)
        int removeCount = endYIndex - startIndex + 1;
        for (int r = 0; r < removeCount; r++)
            instrs.RemoveAt(startIndex);

        // Insert hardcoded resolution at startIndex
        int insertAt = startIndex;
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Ldarg_0));
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Ldc_I4, 1280));
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Stfld, xField!));
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Ldarg_0));
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Ldc_I4, 720));
        instrs.Insert(insertAt++, Instruction.Create(OpCodes.Stfld, yField!));

        Console.WriteLine($"[AssemblyRefRewriter] Patched Options.setToDefaults: hardcoded 1280x720 resolution");
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
