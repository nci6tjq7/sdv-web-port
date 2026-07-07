using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace SdvWebPort.PoC.SdvBlazor;

/// <summary>
/// Loads the real Stardew Valley.dll + its dependency DLLs into the default
/// AssemblyLoadContext in dependency order. Mimics what Program.Main does in
/// the unmodified SDV executable.
///
/// Phase 2.8 — works with the real GOG SDV.dll (v1.6.15.24356).
/// </summary>
public static class SdvLoader
{
    /// <summary>
    /// Dependencies in load order (lowest-level first).
    /// These are fetched from wwwroot/deps/ and loaded into default ALC.
    /// </summary>
    private static readonly string[] _dependencyOrder = new[]
    {
        "System.Data.HashFunction.Interfaces.dll",
        "System.Data.HashFunction.Core.dll",
        "System.Data.HashFunction.xxHash.dll",
        "BmFont.dll",
        "Lidgren.Network.dll",
        "xTile.dll",
        "StardewValley.GameData.dll",
    };

    /// <summary>
    /// Assemblies we cannot ship (native deps fail in WASM).
    /// Loaded on-demand via a resolving handler that returns a dynamic empty stub.
    /// </summary>
    private static readonly HashSet<string> _stubAssemblyNames = new()
    {
        "Steamworks.NET",
        "GalaxyCSharp",
        "SkiaSharp",
        "TextCopy",
    };

    /// <summary>
    /// System.* assemblies SDV references. The BlazorWebAssembly trimmer may
    /// strip these if our app doesn't directly use them. We explicitly load
    /// them via LoadFromAssemblyName to make sure they're in the AppDomain
    /// before SDV's GetTypes() runs (which triggers type resolution that
    /// requires all referenced assemblies to be available).
    ///
    /// If Assembly.Load fails for any of these (because the trimmer stripped
    /// the assembly from the bundle), we'll log a warning but continue —
    /// SDV's GetTypes() will then throw TypeLoadException for the missing
    /// types, which is the diagnostic we need.
    /// </summary>
    private static readonly string[] _systemRefsToPreload = new[]
    {
        "System.Runtime",
        "System.Collections",
        "System.Collections.Concurrent",
        "System.ComponentModel",
        "System.Console",
        "System.Diagnostics.Process",
        "System.Diagnostics.StackTrace",
        "System.Linq",
        "System.Linq.Expressions",
        "System.Net.NameResolution",
        "System.Net.Primitives",
        "System.Reflection.Emit",
        "System.Reflection.Emit.ILGeneration",
        "System.Reflection.Emit.Lightweight",
        "System.Reflection.Primitives",
        "System.Runtime.InteropServices",
        "System.Runtime.InteropServices.RuntimeInformation",
        "System.Text.RegularExpressions",
        "System.Threading",
        "System.Threading.Thread",
        "System.Xml.ReaderWriter",
        "System.Xml.XmlSerializer",
    };

    /// <summary>
    /// Preload KNI Xna.Framework.* assemblies by fetching them from wwwroot/deps/kni/
    /// as static files. This bypasses the BlazorWebAssembly trimmer, which strips
    /// types from these assemblies even with PublishTrimmed=false + TrimmerRootAssembly
    /// + TrimmerRootDescriptor (linker.xml preserve="all").
    ///
    /// The trimmer's behavior is fundamentally incompatible with runtime-loaded
    /// assemblies (SDV) that reference types not directly used by the host app.
    /// Loading the unmodified KNI DLLs via LoadFromStream gives us the FULL type
    /// definitions, not the trimmed ones.
    /// </summary>
    private static async Task PreloadKniAssembliesAsync(HttpClient http, string baseAddress)
    {
        Console.WriteLine("[SdvLoader] Preloading KNI Xna.Framework.* assemblies (from /deps/kni/)...");
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
        int loaded = 0, already = 0, failed = 0;
        foreach (var name in kniAssemblies)
        {
            try
            {
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);
                if (existing != null)
                {
                    // Already loaded (possibly trimmed). Unload isn't supported in default ALC,
                    // so we just log and continue. The trimmed version may still work for some types.
                    already++;
                    Console.WriteLine($"[SdvLoader]   {name}: already loaded (may be trimmed)");
                    continue;
                }
                var url = new Uri(new Uri(baseAddress), $"deps/kni/{name}.dll");
                var bytes = await http.GetByteArrayAsync(url);

                // Run FileSystem rewriter on Xna.Framework.Content to redirect
                // TitleContainer.OpenStream → SdvFileShim.TitleContainerOpenStream
                if (name == "Xna.Framework.Content")
                {
                    try
                    {
                        bytes = SdvWebPort.Rewriter.SdvFileSystemRewriter.Rewrite(bytes);
                        Console.WriteLine($"[SdvLoader]   {name}: FS-rewritten ({bytes.Length:N0} bytes)");
                        // Cache for resolving handler (in case bundle already loaded it)
                        _preloadedStubs[name] = bytes;
                    }
                    catch (Exception rex)
                    {
                        Console.WriteLine($"[SdvLoader]   {name}: FS rewrite failed: {rex.Message}");
                    }
                }

                var existing2 = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);
                if (existing2 != null)
                {
                    already++;
                    Console.WriteLine($"[SdvLoader]   {name}: already loaded (bundle version, FS-rewrite cached for resolver)");
                    continue;
                }

                var asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
                loaded++;
                Console.WriteLine($"[SdvLoader]   {name}: loaded {bytes.Length:N0} bytes (untrimmed)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SdvLoader]   could not preload {name}: {ex.GetType().Name}: {ex.Message}");
                failed++;
            }
        }
        Console.WriteLine($"[SdvLoader] KNI preload: {loaded} loaded, {already} already there, {failed} failed");
    }

    private static void PreloadKniAssemblies()
    {
        // Legacy sync version — kept for backwards compat but no longer used.
        // PreloadKniAssembliesAsync is the active path.
        throw new NotSupportedException("Use PreloadKniAssembliesAsync instead.");
    }

    private static async Task PreloadSystemAssembliesAsync(HttpClient http, string baseAddress)
    {
        Console.WriteLine("[SdvLoader] Preloading System.* assemblies (from /deps/sysrt/)...");
        int loaded = 0, already = 0, failed = 0;
        foreach (var name in _systemRefsToPreload)
        {
            try
            {
                var existing = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);
                if (existing != null)
                {
                    already++;
                    continue;
                }
                // Try loading from static files first (untrimmed)
                try
                {
                    var url = new Uri(new Uri(baseAddress), "deps/sysrt/" + name + ".dll");
                    var bytes = await http.GetByteArrayAsync(url);
                    var asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bytes));
                    loaded++;
                    continue;
                }
                catch
                {
                    // Fall through to LoadFromAssemblyName
                }
                // Fallback: load from bundle (may be trimmed)
                AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name));
                loaded++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SdvLoader]   could not preload " + name + ": " + ex.GetType().Name + ": " + ex.Message);
                failed++;
            }
        }
        Console.WriteLine("[SdvLoader] System.* preload: " + loaded + " loaded, " + already + " already there, " + failed + " failed");

        // Force the trimmer to keep type-forwards for these types. The trimmer
        // strips "unused" type-forwards from System.Runtime — e.g., it strips
        // System.IComparable's forward because our app doesn't directly use it.
        // But SDV's typerefs reference these types via System.Runtime, and
        // resolution fails at runtime. Touching the types here forces the
        // trimmer to keep their type-forwards.
        Console.WriteLine("[SdvLoader] Touching types to preserve type-forwards...");
        _ = typeof(System.IComparable);
        _ = typeof(System.IComparable<>);
        _ = typeof(System.Guid);
        _ = typeof(System.DateTime);
        _ = typeof(System.DateTimeOffset);
        _ = typeof(System.TimeSpan);
        _ = typeof(System.Enum);
        _ = typeof(System.Array);
        _ = typeof(System.Decimal);
        _ = typeof(System.IntPtr);
        _ = typeof(System.UIntPtr);
        _ = typeof(System.Nullable<>);
        _ = typeof(System.Func<>);
        _ = typeof(System.Action<>);
        _ = typeof(System.EventHandler<>);
        // SDV uses high-arity Action/Func delegates. The trimmer strips these
        // from System.Private.CoreLib even with PublishTrimmed=false. Touching
        // them forces the trimmer to keep them.
        _ = typeof(System.Action<,,>);
        _ = typeof(System.Action<,,,>);
        _ = typeof(System.Action<,,,,>);
        _ = typeof(System.Action<,,,,,>);
        _ = typeof(System.Action<,,,,,,>);
        _ = typeof(System.Action<,,,,,,,>);
        _ = typeof(System.Func<,,,,>);
        _ = typeof(System.Func<,,,,,>);
        _ = typeof(System.Func<,,,,,,>);
        _ = typeof(System.Func<,,,,,,,>);
        _ = typeof(System.Func<,,,,,,,,>);
        // SDV also uses Lazy<T>
        _ = typeof(System.Lazy<>);
        _ = typeof(System.Lazy<int>);
        // SDV uses various System.* types that may be stripped
        _ = typeof(System.Collections.Generic.Dictionary<,>);
        _ = typeof(System.Collections.Generic.List<>);
        _ = typeof(System.Collections.Generic.HashSet<>);
        _ = typeof(System.Collections.Generic.Queue<>);
        _ = typeof(System.Collections.Generic.Stack<>);
        _ = typeof(System.Collections.Generic.Stack<int>);
        _ = typeof(System.Collections.Generic.KeyValuePair<,>);
        _ = typeof(System.Xml.Serialization.IXmlSerializable);
        _ = typeof(System.Xml.Serialization.XmlSerializer);
        _ = typeof(System.Xml.XmlReader);
        _ = typeof(System.Xml.XmlWriter);
        _ = typeof(System.Text.RegularExpressions.Regex);
        _ = typeof(System.Net.IPAddress);
        // SDV uses these types that may be stripped
        _ = typeof(System.Collections.Generic.SortedDictionary<,>);
        _ = typeof(System.Collections.Generic.SortedList<,>);
        _ = typeof(System.Collections.Generic.SortedSet<>);
        _ = typeof(System.Collections.Generic.LinkedList<>);
        _ = typeof(System.Collections.ObjectModel.Collection<>);
        _ = typeof(System.Collections.ObjectModel.ObservableCollection<>);
        _ = typeof(System.Collections.Concurrent.ConcurrentDictionary<,>);
        _ = typeof(System.Collections.Concurrent.ConcurrentQueue<>);
        _ = typeof(System.Collections.Concurrent.ConcurrentStack<>);
        _ = typeof(System.Collections.Concurrent.ConcurrentBag<>);
        _ = typeof(System.Threading.CancellationTokenSource);
        _ = typeof(System.IO.StringReader);
        _ = typeof(System.IO.StringWriter);
        _ = typeof(System.Text.StringBuilder);
        _ = typeof(System.Globalization.CultureInfo);
        _ = typeof(System.IDisposable);
        _ = typeof(System.IFormattable);
        _ = typeof(System.IConvertible);
        _ = typeof(System.IEquatable<>);
        _ = typeof(System.IFormatProvider);
        _ = typeof(System.IServiceProvider);
        _ = typeof(System.Threading.CancellationToken);
        _ = typeof(System.Threading.Tasks.Task);
        _ = typeof(System.Threading.Tasks.Task<>);
        _ = typeof(System.Collections.IEnumerable);
        _ = typeof(System.Collections.IEnumerator);
        _ = typeof(System.Collections.ICollection);
        _ = typeof(System.Collections.IList);
        _ = typeof(System.Collections.IDictionary);
        _ = typeof(System.Collections.Generic.IEnumerable<>);
        _ = typeof(System.Collections.Generic.IEnumerator<>);
        _ = typeof(System.Collections.Generic.ICollection<>);
        _ = typeof(System.Collections.Generic.IList<>);
        _ = typeof(System.Collections.Generic.IDictionary<,>);
        _ = typeof(System.Collections.Generic.IReadOnlyCollection<>);
        _ = typeof(System.Collections.Generic.IReadOnlyList<>);
        _ = typeof(System.Collections.Generic.IReadOnlyDictionary<,>);
        _ = typeof(System.Collections.Generic.IEqualityComparer<>);
        _ = typeof(System.Collections.Generic.IComparer<>);
        _ = typeof(System.Collections.Generic.KeyValuePair<,>);
        _ = typeof(System.Collections.Generic.List<>);
        _ = typeof(System.Collections.Generic.Dictionary<,>);
        _ = typeof(System.Collections.Generic.HashSet<>);
        _ = typeof(System.Collections.Generic.Queue<>);
        _ = typeof(System.Collections.Generic.Stack<>);
        _ = typeof(System.Collections.Generic.LinkedList<>);
        _ = typeof(System.Collections.Generic.SortedDictionary<,>);
        _ = typeof(System.Collections.Generic.SortedList<,>);
        _ = typeof(System.Collections.Generic.SortedSet<>);
        _ = typeof(System.IO.Stream);
        _ = typeof(System.IO.TextReader);
        _ = typeof(System.IO.TextWriter);
        _ = typeof(System.IO.BinaryReader);
        _ = typeof(System.IO.BinaryWriter);
        _ = typeof(System.IO.StreamReader);
        _ = typeof(System.IO.StreamWriter);
        _ = typeof(System.IO.MemoryStream);
        _ = typeof(System.IO.File);
        _ = typeof(System.IO.Directory);
        _ = typeof(System.IO.FileStream);
        _ = typeof(System.IO.FileMode);
        _ = typeof(System.IO.FileAccess);
        _ = typeof(System.IO.FileShare);
        _ = typeof(System.IO.SeekOrigin);
        _ = typeof(System.Text.Encoding);
        _ = typeof(System.Text.StringBuilder);
        _ = typeof(System.Text.RegularExpressions.Regex);
        _ = typeof(System.Globalization.CultureInfo);
        _ = typeof(System.Reflection.Assembly);
        _ = typeof(System.Reflection.AssemblyName);
        _ = typeof(System.Reflection.MethodInfo);
        _ = typeof(System.Reflection.FieldInfo);
        _ = typeof(System.Reflection.PropertyInfo);
        _ = typeof(System.Reflection.TypeInfo);
        _ = typeof(System.Reflection.BindingFlags);
        _ = typeof(System.Attribute);
        _ = typeof(System.AttributeUsageAttribute);
        _ = typeof(System.Exception);
        _ = typeof(System.ArgumentException);
        _ = typeof(System.ArgumentNullException);
        _ = typeof(System.InvalidOperationException);
        _ = typeof(System.NotImplementedException);
        _ = typeof(System.NotSupportedException);
        _ = typeof(System.IO.FileNotFoundException);
        _ = typeof(System.IO.DirectoryNotFoundException);
        _ = typeof(System.Xml.XmlReader);
        _ = typeof(System.Xml.XmlWriter);
        Console.WriteLine("[SdvLoader] Type touches complete");
    }

    /// <summary>
    /// Fetch + load all dependencies, then load the (already-rewritten) SDV bytes.
    /// Returns the loaded SDV assembly.
    /// </summary>
    public static async Task<Assembly> LoadSdvWithDependenciesAsync(
        HttpClient http, string baseAddress, byte[] sdvBytes)
    {
        Console.WriteLine("[SdvLoader] === Phase 2.8 — real SDV load ===");

        // 1. Preload stub assembly bytes (Steamworks.NET, GalaxyCSharp, SkiaSharp, TextCopy).
        //    These have native deps but the managed DLL loads fine — native methods
        //    only fail when called. We preload bytes because the resolving event
        //    can't be async.
        await PreloadStubAssembliesAsync(http, baseAddress);

        // 1a. Register a resolving handler that returns preloaded stubs for
        //     assemblies with native dependencies.
        RegisterStubResolver();

        // 1b. Preload System.* assemblies that SDV references but the trimmer
        //     may have stripped. Load from /deps/sysrt/ static files (untrimmed).
        await PreloadSystemAssembliesAsync(http, baseAddress);

        // 1c. Preload KNI Xna.Framework.* assemblies (Audio, Media, etc.) by fetching
        //     them from wwwroot/deps/kni/ as static files. This bypasses the trimmer,
        //     which strips types from these assemblies even with all preservation settings.
        await PreloadKniAssembliesAsync(http, baseAddress);

        // 2. Preload MonoGame.Framework facade (already in default ALC by virtue of
        //    project reference, but ensure it's loaded before SDV).
        var facadeAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "MonoGame.Framework");
        if (facadeAsm == null)
        {
            Console.WriteLine("[SdvLoader] Loading MonoGame.Framework facade...");
            facadeAsm = AssemblyLoadContext.Default.LoadFromAssemblyName(
                new AssemblyName("MonoGame.Framework"));
        }
        Console.WriteLine($"[SdvLoader] Facade: {facadeAsm.FullName}");

        // 3. Fetch + load each dependency in order.
        // IMPORTANT: Each dependency DLL is also run through the AssemblyRef rewriter
        // because deps like xTile.dll and BmFont.dll have typerefs to
        // MonoGame.Framework (e.g., ContentTypeReader`1) that need scope rewriting
        // to point at KNI assemblies. Without this, GetTypes() on these deps fails.
        Console.WriteLine($"[SdvLoader] Loading {_dependencyOrder.Length} dependency DLLs (with rewriter)...");
        foreach (var depName in _dependencyOrder)
        {
            try
            {
                var url = new Uri(new Uri(baseAddress), $"deps/{depName}");
                var bytes = await http.GetByteArrayAsync(url);
                Console.WriteLine($"[SdvLoader]   fetched {depName}: {bytes.Length:N0} bytes");

                // Run AssemblyRef rewriter on the dependency
                byte[] rewrittenDep;
                try
                {
                    rewrittenDep = SdvWebPort.Rewriter.SdvAssemblyRefRewriter.Rewrite(bytes);
                    Console.WriteLine($"[SdvLoader]   rewritten: {rewrittenDep.Length:N0} bytes");
                }
                catch (Exception rex)
                {
                    Console.WriteLine($"[SdvLoader]   rewriter FAILED for {depName}: {rex.Message} — using original");
                    rewrittenDep = bytes;
                }

                var asm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rewrittenDep));
                Console.WriteLine($"[SdvLoader]   loaded: {asm.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SdvLoader]   FAILED {depName}: {ex.GetType().Name}: {ex.Message}");
                // Continue — some deps may not be needed for the ctor path we test.
            }
        }

        // 4. Load SDV itself.
        Console.WriteLine($"[SdvLoader] Loading SDV ({sdvBytes.Length:N0} bytes)...");
        var sdvAsm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(sdvBytes));
        Console.WriteLine($"[SdvLoader] SDV loaded: {sdvAsm.FullName}");
        Console.WriteLine($"[SdvLoader] SDV location: {sdvAsm.Location}");

        // Diagnostic: list loaded assemblies (only System.* and key ones to avoid log spam)
        Console.WriteLine("[SdvLoader] === Loaded assemblies (System.* + SDV-related) ===");
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.StartsWith("System.") == true
                     || a.GetName().Name == "System.Runtime"
                     || a.GetName().Name == "System.Private.CoreLib"
                     || a.GetName().Name?.Contains("Stardew") == true
                     || a.GetName().Name == "MonoGame.Framework"
                     || a.GetName().Name?.StartsWith("Xna.") == true)
            .OrderBy(a => a.GetName().Name))
        {
            Console.WriteLine($"  - {a.FullName}");
        }

        // Diagnostic: verify key types are resolvable
        Console.WriteLine("[SdvLoader] === Type resolution check ===");
        var coreLib = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "System.Private.CoreLib");
        if (coreLib != null)
        {
            // Check Action/Func delegate arities — use string concat, no interpolation
            Console.WriteLine("[SdvLoader] Checking Action/Func delegate arities:");
            for (int arity = 1; arity <= 17; arity++)
            {
                string actionName = arity == 1 ? "System.Action" : "System.Action`" + arity.ToString();
                string funcName = "System.Func`" + (arity + 1).ToString();
                var action = coreLib.GetType(actionName);
                var func = coreLib.GetType(funcName);
                if (action == null || func == null)
                {
                    string msg = "  arity " + arity.ToString() + ": Action=" + (action != null).ToString() + " Func=" + (func != null).ToString() + " <-- MISSING!";
                    Console.WriteLine(msg);
                }
            }
            // Check collection types SDV uses
            Console.WriteLine("[SdvLoader] Checking collection types:");
            string[] collectionTypes = new[]
            {
                "System.Collections.Generic.Stack`1",
                "System.Collections.Generic.Queue`1",
                "System.Collections.Generic.HashSet`1",
                "System.Collections.Generic.SortedSet`1",
                "System.Collections.Generic.LinkedList`1",
                "System.Collections.Generic.SortedDictionary`2",
                "System.Collections.Generic.SortedList`2",
                "System.Collections.ObjectModel.Collection`1",
                "System.Collections.ObjectModel.ObservableCollection`1",
                "System.Collections.Concurrent.ConcurrentDictionary`2",
                "System.Collections.Concurrent.ConcurrentQueue`1",
                "System.Collections.Concurrent.ConcurrentStack`1",
                "System.Collections.Concurrent.ConcurrentBag`1",
                "System.Lazy`1",
                "System.Lazy`2",
                "System.Threading.CancellationTokenSource",
            };
            foreach (var typeName in collectionTypes)
            {
                var t = coreLib.GetType(typeName);
                if (t == null)
                    Console.WriteLine("  " + typeName + ": MISSING  <--!");
            }
        }

        return sdvAsm;
    }

    /// <summary>
    /// Register a resolving handler that LOGS when the CLR asks for any
    /// assembly we haven't preloaded. We do NOT return a stub here — instead
    /// we let the default FileNotFoundException surface, which gives us a
    /// clear diagnostic of what SDV tried to access.
    ///
    /// Systematic-debugging Phase 1: gather evidence before fixing. We'll see
    /// exactly which assembly SDV's GameRunner..ctor() path requires, and can
    /// then decide whether to ship a stub or IL-rewrite the calls.
    /// </summary>
    private static HttpClient? _resolverHttp;
    private static string? _resolverBaseAddress;

    /// <summary>
    /// Preload stub assembly bytes from /deps/ static files. These assemblies
    /// (Steamworks.NET, GalaxyCSharp, SkiaSharp, TextCopy) have native deps
    /// but the managed DLL loads fine — native methods only fail when called.
    /// We preload bytes because the resolving event can't be async.
    /// </summary>
    private static async Task PreloadStubAssembliesAsync(HttpClient http, string baseAddress)
    {
        Console.WriteLine("[SdvLoader] Preloading stub assembly bytes...");
        foreach (var name in _stubAssemblyNames)
        {
            try
            {
                var url = new Uri(new Uri(baseAddress), "deps/" + name + ".dll");
                var bytes = await http.GetByteArrayAsync(url);
                _preloadedStubs[name] = bytes;
                Console.WriteLine("[SdvLoader]   preloaded " + name + ": " + bytes.Length + " bytes");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SdvLoader]   could not preload " + name + ": " + ex.Message);
            }
        }
    }

    private static void RegisterStubResolver()
    {
        if (_resolverRegistered) return;
        _resolverRegistered = true;

        var alc = AssemblyLoadContext.Default;
        alc.Resolving += (ctx, name) =>
        {
            // Log every unresolved assembly request for diagnostic purposes.
            // Only log names we haven't seen yet to avoid log spam.
            lock (_seenUnresolved)
            {
                if (!_seenUnresolved.Add(name.FullName))
                    return null;
            }
            var isStub = _stubAssemblyNames.Contains(name.Name ?? "");
            Console.WriteLine($"[SdvLoader] RESOLVING: {name.FullName}  (stub-expected={isStub})");

            // For stub-expected assemblies, check if we have preloaded bytes.
            // (The resolving event can't be async, so we preload stub bytes before
            // loading SDV and cache them here.)
            if (isStub && _preloadedStubs.TryGetValue(name.Name ?? "", out var stubBytes))
            {
                try
                {
                    Console.WriteLine($"[SdvLoader]   loading stub {name.Name} from preloaded bytes ({stubBytes.Length:N0} bytes)");
                    return AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(stubBytes));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SdvLoader]   stub load failed for {name.Name}: {ex.Message}");
                }
            }
            return null;
        };
        Console.WriteLine("[SdvLoader] Resolving handler registered (loads preloaded stub assemblies)");
    }

    private static bool _resolverRegistered = false;
    private static readonly HashSet<string> _seenUnresolved = new();
    private static readonly Dictionary<string, byte[]> _preloadedStubs = new();
}