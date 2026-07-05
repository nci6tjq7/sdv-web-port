using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Cecil assembly resolver that reads .NET 8 reference assemblies from
/// embedded resources. Needed because in BlazorWebAssembly, Assembly.Location
/// is empty for runtime-loaded assemblies, so Cecil's default resolver cannot
/// find System.Runtime etc. when writing the rewritten SDV assembly.
///
/// The ref assemblies (metadata-only, ~1.6 MB total) are embedded into the
/// Rewriter DLL at build time. At runtime, this resolver returns
/// AssemblyDefinitions read from the embedded bytes.
///
/// Resolution order:
/// 1. Check embedded ref assemblies (by simple name — version ignored).
/// 2. Fall back to DefaultAssemblyResolver (file-based, won't find anything in WASM).
///
/// Version matching is BY SIMPLE NAME ONLY. Ref assemblies are all v8.0.0.0,
/// but SDV references v6.0.0.0. Cecil uses the resolved assembly to look up
/// type definitions (e.g., "is System.Guid a ValueType?"), which works
/// regardless of version — the type's layout doesn't change between v6 and v8.
/// </summary>
public sealed class RefAssemblyResolver : BaseAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly DefaultAssemblyResolver _fallback = new();
    private readonly Assembly _thisAssembly = typeof(RefAssemblyResolver).Assembly;

    public RefAssemblyResolver()
    {
        Console.WriteLine("[RefAssemblyResolver] Initialized — will read ref assemblies from embedded resources");
    }

    public override AssemblyDefinition? Resolve(AssemblyNameReference name, ReaderParameters? parameters)
    {
        var simpleName = name.Name ?? "";

        // 1. Check cache
        lock (_cache)
        {
            if (_cache.TryGetValue(simpleName, out var cached))
                return cached;
        }

        // 2. Try embedded ref assembly
        var embedded = TryLoadFromEmbeddedResources(name);
        if (embedded != null)
        {
            lock (_cache)
            {
                _cache[simpleName] = embedded;
            }
            return embedded;
        }

        // 3. Fall back to default resolver (file-based — works in desktop, not WASM)
        try
        {
            var fallbackResult = _fallback.Resolve(name, parameters);
            if (fallbackResult != null)
            {
                lock (_cache)
                {
                    _cache[simpleName] = fallbackResult;
                }
                return fallbackResult;
            }
        }
        catch
        {
            // Ignore — we'll return null below
        }

        Console.WriteLine($"[RefAssemblyResolver] UNRESOLVED: {name.FullName}");
        return null;
    }

    private AssemblyDefinition? TryLoadFromEmbeddedResources(AssemblyNameReference name)
    {
        var simpleName = name.Name ?? "";
        var resourceNames = _thisAssembly.GetManifestResourceNames();

        // Match resource name. Pattern: {any prefix}.{simpleName}.dll
        // E.g., simpleName="StardewValley.GameData" matches
        //        "SdvWebPort.Rewriter.RefAssemblies.SDVDeps.StardewValley.GameData.dll"
        string? matchingName = null;
        foreach (var n in resourceNames)
        {
            if (n.EndsWith($".{simpleName}.dll", StringComparison.OrdinalIgnoreCase))
            {
                matchingName = n;
                break;
            }
        }
        // Also accept exact match (rare)
        if (matchingName == null && resourceNames.Contains(simpleName))
            matchingName = simpleName;

        if (matchingName == null)
        {
            Console.WriteLine($"[RefAssemblyResolver] No embedded resource matches '{simpleName}' (checked {resourceNames.Length} resources)");
            return null;
        }

        try
        {
            using var stream = _thisAssembly.GetManifestResourceStream(matchingName);
            if (stream == null)
            {
                Console.WriteLine($"[RefAssemblyResolver] GetManifestResourceStream returned null for {matchingName}");
                return null;
            }
            var bytes = new byte[stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = stream.Read(bytes, read, bytes.Length - read);
                if (n <= 0) break;
                read += n;
            }
            var ms = new MemoryStream(bytes);
            var parameters = new ReaderParameters
            {
                AssemblyResolver = this,
                InMemory = true,
                ReadWrite = false,
            };
            var asmDef = AssemblyDefinition.ReadAssembly(ms, parameters);
            Console.WriteLine($"[RefAssemblyResolver] Loaded {simpleName} from {matchingName} ({bytes.Length:N0} bytes)");
            return asmDef;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[RefAssemblyResolver] Failed to load {simpleName} from {matchingName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_cache)
            {
                foreach (var asm in _cache.Values)
                    asm.Dispose();
                _cache.Clear();
            }
            _fallback.Dispose();
        }
        base.Dispose(disposing);
    }
}
