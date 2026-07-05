using Mono.Cecil;
using Mono.Cecil.Cil;
using SdvWebPort.Vfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Cecil-based IL rewriter that redirects System.IO.File.* and
/// System.IO.Directory.* calls to SdvWebPort.Vfs.SdvFileShim.* calls.
///
/// This runs IN-MEMORY on the fetched SDV DLL bytes. The user's SDV.dll
/// file on disk is never modified.
///
/// Rewriting rules:
///   System.IO.File::OpenRead(string)          → SdvFileShim::OpenRead(string)
///   System.IO.File::Exists(string)            → SdvFileShim::Exists(string)
///   System.IO.File::ReadAllBytes(string)      → SdvFileShim::ReadAllBytes(string)
///   System.IO.File::ReadAllText(string)       → SdvFileShim::ReadAllText(string)
///   System.IO.Directory::GetFiles(string)     → SdvFileShim::GetFiles(string)
///   System.IO.Directory::GetFiles(string,str) → SdvFileShim::GetFiles(string,str)
///   System.IO.Directory::Exists(string)       → SdvFileShim::DirectoryExists(string)
/// </summary>
public static class SdvFileSystemRewriter
{
    // Types that exist in KNI .dll but are stripped from KNI's Blazor.GL .wasm version.
    // These are XACT audio types (browser doesn't support XACT audio).
    // The facade (MonoGame.Framework) has stub definitions for these.
    private static readonly HashSet<string> MissingFromKniWasm = new()
    {
        "Microsoft.Xna.Framework.Audio.WaveBank",
        "Microsoft.Xna.Framework.Audio.AudioEngine",
        "Microsoft.Xna.Framework.Audio.Cue",
        "Microsoft.Xna.Framework.Audio.SoundBank",
        "Microsoft.Xna.Framework.Audio.AudioCategory",
        "Microsoft.Xna.Framework.Audio.AudioChannels",
        "Microsoft.Xna.Framework.Audio.AudioStopOptions",
        "Microsoft.Xna.Framework.Audio.InstancePlayLimitException",
        "Microsoft.Xna.Framework.Audio.NoAudioHardwareException",
        "Microsoft.Xna.Framework.Audio.NoMicrophoneConnectedException",
        "Microsoft.Xna.Framework.Audio.Microphone",
        "Microsoft.Xna.Framework.Audio.MicrophoneState",
        "Microsoft.Xna.Platform.Audio.MicrophoneStrategy",
    };

    // Maps (DeclaringTypeFullName, MethodName, ParameterCount) → ShimMethodName
    //
    // Phase 2.75 PoC — covers MockSdv.Target's File.OpenRead pattern.
    // EXPAND for real SDV (see spec v2 R6, MEMORY.md Phase 2.8 Next Steps).
    // Likely missing methods real SDV uses:
    //   - File.Open / File.Create / File.Delete / File.Copy / File.Move
    //   - File.WriteAllBytes / File.WriteAllText / File.ReadAllLines
    //   - Directory.CreateDirectory / Directory.GetDirectories / Directory.Delete
    //   - FileStream constructor (for new FileStream(path, ...) patterns)
    // Phase 2.8 strategy: run rewriter on real SDV, observe MissingMethodException
    // for un-routed calls, add entries iteratively.
    private static readonly Dictionary<(string Type, string Method, int ParamCount), string> _rewriteMap = new()
    {
        // Phase 2.75 PoC — original 7 entries
        { ("System.IO.File", "OpenRead", 1),       "OpenRead" },
        { ("System.IO.File", "Exists", 1),          "Exists" },
        { ("System.IO.File", "ReadAllBytes", 1),    "ReadAllBytes" },
        { ("System.IO.File", "ReadAllText", 1),     "ReadAllText" },
        { ("System.IO.Directory", "GetFiles", 1),   "GetFiles" },
        { ("System.IO.Directory", "GetFiles", 2),   "GetFiles" },
        { ("System.IO.Directory", "Exists", 1),     "DirectoryExists" },
        // Phase 2.8 — gaps discovered by static analysis of real SDV.dll v1.6.15
        // See docs/superpowers/analysis/2026-07-05-sdv-io-coverage.md
        { ("System.IO.File", "Open", 2),            "Open" },           // File.Open(string, FileMode)
        { ("System.IO.File", "Open", 3),            "Open" },           // File.Open(string, FileMode, FileAccess)
        { ("System.IO.File", "ReadAllLines", 1),    "ReadAllLines" },
        { ("System.IO.File", "AppendAllText", 2),   "AppendAllText" },
        { ("System.IO.File", "Create", 1),          "Create" },
        { ("System.IO.File", "CreateText", 1),      "CreateText" },
        { ("System.IO.File", "Delete", 1),          "Delete" },
        { ("System.IO.File", "Move", 3),            "Move" },           // File.Move(string, string, bool)
        { ("System.IO.File", "WriteAllText", 2),    "WriteAllText" },
        { ("System.IO.Directory", "CreateDirectory", 1), "CreateDirectory" },
        { ("System.IO.Directory", "Delete", 2),     "DeleteDirectory" },// Directory.Delete(string, bool) → renamed
        { ("System.IO.Directory", "EnumerateDirectories", 1), "EnumerateDirectories" },
        // Note: Path.* methods are NOT in the map — they're pure string ops, work fine in WASM
        // Note: FileStream constructor needs special handling (Newobj opcode, not Call) — TODO
    };

    /// <summary>
    /// Rewrite the given assembly bytes, redirecting File/Directory calls to SdvFileShim.
    /// Returns the rewritten assembly bytes.
    /// </summary>
    public static byte[] Rewrite(byte[] assemblyBytes)
    {
        Console.WriteLine($"[Rewriter] Loading assembly ({assemblyBytes.Length:N0} bytes)");
        using var inputMs = new MemoryStream(assemblyBytes);

        // Custom resolver that maps .NET 6 version refs → .NET 8 runtime.
        // Real SDV references System.Runtime v6.0.0.0; our runtime is v8.0.0.0.
        // Without this mapping, Cecil's Write() fails with AssemblyResolutionException.
        var resolver = new DefaultAssemblyResolver();
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var path = asm.Location;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    resolver.AddSearchDirectory(Path.GetDirectoryName(path));
            }
            catch { /* skip */ }
        }

        // Register a custom resolve handler that maps version 6.0.0.0 → 8.0.0.0
        // for framework assemblies (System.Runtime, System.Collections, etc.)
        resolver.ResolveFailure += (sender, name) =>
        {
            // If the requested version is 6.0.0.0 or similar, try loading the
            // current runtime's version (which may have a different version number)
            if (name.Version != null && name.Version.Major < 8)
            {
                Console.WriteLine($"[Rewriter] Resolving {name.Name} v{name.Version} → trying runtime version");
                var runtimeAsm = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name.Name);
                if (runtimeAsm != null)
                {
                    Console.WriteLine($"[Rewriter] Found {name.Name} in runtime: {runtimeAsm.FullName}");
                    return AssemblyDefinition.ReadAssembly(
                        runtimeAsm.Location,
                        new ReaderParameters { AssemblyResolver = resolver });
                }
            }
            return null;
        };

        var parameters = new ReaderParameters { AssemblyResolver = resolver };
        using var asmDef = AssemblyDefinition.ReadAssembly(inputMs, parameters);

        int totalRewrites = 0;
        foreach (var module in asmDef.Modules)
        {
            // Rewrite AssemblyRef entries for framework assemblies.
            // Real SDV (compiled against .NET 6) references System.Runtime v6.0.0.0 etc.
            // Our WASM runtime is .NET 8 (v8.0.0.0).
            //
            // Two strategies:
            // 1. System.Runtime → redirect to System.Private.CoreLib (where types are actually
            //    defined; System.Runtime is a facade and the WASM native type loader doesn't
            //    follow type forwarders for dynamically loaded assemblies)
            // 2. Other System.* → just fix version to 8.0.0.0 (these are standalone assemblies
            //    with their own type definitions; version fix is sufficient)
            int asmRefRewrites = 0;
            // Known facade assemblies that forward ALL their types to CoreLib.
            // IMPORTANT: Only include assemblies that are TRUE facades (no type definitions
            // of their own). System.Collections has Stack<T>, System.Threading has Thread, etc.
            // — these are NOT facades and must NOT be redirected to CoreLib.
            // Verified via: typeof(Stack<>).Assembly.GetName() → System.Collections (not CoreLib)
            var facadeAssemblies = new HashSet<string>
            {
                "System.Runtime",               // facade → CoreLib (all types forwarded)
                "System.Runtime.Extensions",    // facade → CoreLib
                "System.Runtime.InteropServices", // facade → CoreLib
                "System.Runtime.Loader",        // facade → CoreLib
                "System.Diagnostics.Debug",     // facade → CoreLib
                "System.Diagnostics.StackTrace", // facade → CoreLib
                "System.Diagnostics.Process",   // facade → CoreLib
                "System.Globalization",         // facade → CoreLib
                "System.Resources.ResourceManager", // facade → CoreLib
                "System.Reflection",            // facade → CoreLib
                "System.Reflection.Primitives", // facade → CoreLib
                "System.Reflection.Emit",       // facade → CoreLib
                "System.Reflection.Emit.Lightweight", // facade → CoreLib
                "System.Reflection.Emit.ILGeneration", // facade → CoreLib
                "System.Text.Encoding",         // facade → CoreLib
                "System.Text.Encoding.Extensions", // facade → CoreLib
                "System.Threading.ThreadPool",  // facade → CoreLib
                // NOT facades (have own type definitions):
                // System.Collections (Stack<T>, Dictionary<T> etc.)
                // System.Threading (Thread, Monitor etc.)
                // System.Threading.Tasks (Task etc.)
                // System.Linq (Enumerable etc.)
                // System.Text.RegularExpressions (Regex etc.)
                // System.ObjectModel
                // System.ComponentModel
                // System.Console
                // System.Net.Primitives
                // System.Xml.* 
                // System.Data.*
            };
            foreach (var asmRef in module.AssemblyReferences)
            {
                if (asmRef.Version != null && asmRef.Version.Major < 8 &&
                    (asmRef.Name.StartsWith("System.") || asmRef.Name == "System" ||
                     asmRef.Name == "netstandard" || asmRef.Name == "mscorlib"))
                {
                    var oldName = asmRef.Name;
                    var oldVersion = asmRef.Version;

                    if (facadeAssemblies.Contains(asmRef.Name))
                    {
                        // Redirect facade → CoreLib (types are actually defined there)
                        // Also fix PublicKeyToken: SDV's ref has b03f5f7f11d50a3a (System.Runtime's
                        // token), but CoreLib uses 7cec85d7bea7798e. Without matching the token,
                        // the WASM runtime rejects the assembly reference.
                        asmRef.Name = "System.Private.CoreLib";
                        asmRef.Version = new Version(8, 0, 0, 0);
                        asmRef.PublicKeyToken = new byte[] { 0x7c, 0xec, 0x85, 0xd7, 0xbe, 0xa7, 0x79, 0x8e };
                        Console.WriteLine($"[Rewriter] AssemblyRef: {oldName} v{oldVersion} → System.Private.CoreLib v8.0.0.0 (facade + PKT fix)");
                    }
                    else
                    {
                        // Just fix version (standalone assembly with its own types)
                        asmRef.Version = new Version(8, 0, 0, 0);
                        Console.WriteLine($"[Rewriter] AssemblyRef: {oldName} v{oldVersion} → v8.0.0.0 (version fix)");
                    }
                    asmRefRewrites++;
                }
            }
            if (asmRefRewrites > 0)
                Console.WriteLine($"[Rewriter] Rewrote {asmRefRewrites} AssemblyRef entries");

            // TypeRef scope rewriting is DISABLED for now.
            // In default ALC, the facade's TypeForwardedTo should work (same ALC as KNI).
            // Loading KNI .wasm into default ALC breaks CoreLib type resolution (Action`7 issue).
            // The facade has stubs for types missing from KNI .wasm (WaveBank etc.).
            // If TypeForwardedTo doesn't work, re-enable this block.
            // int typeRefRewrites = 0;
            // ... (TypeRef rewriting code disabled) ...

            totalRewrites += RewriteModule(module);
        }
        Console.WriteLine($"[Rewriter] Total rewrites: {totalRewrites}");

        // Remove all parameter constants (default parameter values) before Write.
        // Cecil's Write() tries to resolve the type of each constant, which fails
        // for System.Runtime v6.0.0.0 types in WASM (only v8.0.0.0 available).
        // Removing constants is safe — default parameter values are not needed
        // for our use case (we're only rewriting File/Directory calls, not signatures).
        int constantsRemoved = 0;
        foreach (var module in asmDef.Modules)
        {
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    foreach (var param in method.Parameters)
                    {
                        if (param.HasConstant)
                        {
                            param.Constant = null;
                            constantsRemoved++;
                        }
                    }
                    // Also remove property constants
                    if (method.HasBody)
                    {
                        foreach (var var in method.Body.Variables)
                        {
                            // Don't remove variable types, just constants
                        }
                    }
                }
            }
        }
        if (constantsRemoved > 0)
            Console.WriteLine($"[Rewriter] Removed {constantsRemoved} parameter constants (prevents type resolution during Write)");

        using var outputMs = new MemoryStream();
        asmDef.Write(outputMs);
        var result = outputMs.ToArray();
        Console.WriteLine($"[Rewriter] Rewritten assembly: {result.Length:N0} bytes");
        return result;
    }

    private static int RewriteModule(ModuleDefinition module)
    {
        int rewrites = 0;

        // Import the SdvFileShim type reference properly.
        // We need to find the SdvWebPort.Vfs assembly in the resolver and
        // create a TypeReference that points at it (not at CoreLibrary).
        TypeReference? shimTypeRef = null;

        // Try to find the SdvWebPort.Vfs assembly via the resolver.
        // First check if it's already loaded in the current AppDomain.
        // If not, try to load it by name (it may be referenced but not yet loaded).
        var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
        if (vfsAsm == null)
        {
            try
            {
                vfsAsm = Assembly.Load("SdvWebPort.Vfs");
                Console.WriteLine($"[Rewriter] Loaded SdvWebPort.Vfs assembly: {vfsAsm.FullName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Rewriter] Could not load SdvWebPort.Vfs: {ex.Message}");
            }
        }
        if (vfsAsm != null)
        {
            var shimType = vfsAsm.GetType("SdvWebPort.Vfs.SdvFileShim");
            if (shimType != null)
            {
                // Import the type through the module's ImportReference, which
                // correctly resolves the assembly reference.
                shimTypeRef = module.ImportReference(shimType);
                Console.WriteLine($"[Rewriter] Imported SdvFileShim from {vfsAsm.GetName().Name}");

                // Build a map of shim method references by (name, paramCount).
                // We import each shim method via reflection so the return types
                // and parameter types are correct (e.g., OpenRead returns Stream,
                // not FileStream — the original File.OpenRead returns FileStream).
                var shimMethodsByName = new Dictionary<(string Name, int ParamCount), MethodReference>();
                foreach (var mi in shimType.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    var key = (mi.Name, mi.GetParameters().Length);
                    if (!shimMethodsByName.ContainsKey(key))
                    {
                        var imported = module.ImportReference(mi);
                        shimMethodsByName[key] = imported;
                        Console.WriteLine($"[Rewriter] Imported shim method: {mi.Name}({mi.GetParameters().Length} params) → {imported.ReturnType.FullName}");
                    }
                }

                foreach (var type in module.GetTypes())
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body == null) continue;
                        rewrites += RewriteMethod(method, shimTypeRef, shimMethodsByName);
                    }
                }
                return rewrites;
            }
        }

        // Fallback: SdvWebPort.Vfs assembly not found in AppDomain.
        // This is an error — the rewriter cannot produce correct IL without
        // importing the real shim method signatures (return types differ:
        // File.OpenRead returns FileStream, SdvFileShim.OpenRead returns Stream).
        // The old fallback used callee.ReturnType which caused MissingMethodException.
        // See MEMORY.md Critical Knowledge #15.
        throw new InvalidOperationException(
            "SdvWebPort.Vfs assembly must be loaded in the AppDomain before rewriting. " +
            "Call SdvFileShim.SetVfs() first, or ensure SdvWebPort.Vfs is referenced " +
            "by the host project. The rewriter needs to import the real shim method " +
            "signatures via module.ImportReference(MethodInfo) to avoid return-type " +
            "mismatches (e.g., FileStream vs Stream).");
    }

    private static int RewriteMethod(MethodDefinition method, TypeReference shimTypeRef, Dictionary<(string Name, int ParamCount), MethodReference> shimMethods)
    {
        int rewrites = 0;
        var instructions = method.Body.Instructions;
        for (int i = 0; i < instructions.Count; i++)
        {
            var instr = instructions[i];
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
            if (instr.Operand is not MethodReference callee) continue;

            var declaringType = callee.DeclaringType?.FullName;
            if (declaringType == null) continue;

            // Check if this call is in our rewrite map
            if (!_rewriteMap.TryGetValue((declaringType, callee.Name, callee.Parameters.Count), out var shimMethod))
                continue;

            // Look up the imported shim method reference (has correct return type)
            if (!shimMethods.TryGetValue((shimMethod, callee.Parameters.Count), out var newCallee))
            {
                Console.WriteLine($"[Rewriter] WARN: shim method {shimMethod}({callee.Parameters.Count}) not found in imports");
                continue;
            }

            // Replace the operand
            instr.Operand = newCallee;
            rewrites++;
            Console.WriteLine($"[Rewriter] {method.DeclaringType.FullName}::{method.Name}: {declaringType}.{callee.Name} → SdvFileShim.{shimMethod}");
        }
        return rewrites;
    }
}
