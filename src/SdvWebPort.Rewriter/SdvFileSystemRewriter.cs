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
        { ("System.IO.File", "OpenRead", 1),       "OpenRead" },
        { ("System.IO.File", "Exists", 1),          "Exists" },
        { ("System.IO.File", "ReadAllBytes", 1),    "ReadAllBytes" },
        { ("System.IO.File", "ReadAllText", 1),     "ReadAllText" },
        { ("System.IO.Directory", "GetFiles", 1),   "GetFiles" },
        { ("System.IO.Directory", "GetFiles", 2),   "GetFiles" },
        { ("System.IO.Directory", "Exists", 1),     "DirectoryExists" },
    };

    /// <summary>
    /// Rewrite the given assembly bytes, redirecting File/Directory calls to SdvFileShim.
    /// Returns the rewritten assembly bytes.
    /// </summary>
    public static byte[] Rewrite(byte[] assemblyBytes)
    {
        Console.WriteLine($"[Rewriter] Loading assembly ({assemblyBytes.Length:N0} bytes)");
        using var inputMs = new MemoryStream(assemblyBytes);
        var resolver = new DefaultAssemblyResolver();
        // Cecil needs to resolve references to System.IO.File etc. — use the current AppDomain.
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
        var parameters = new ReaderParameters { AssemblyResolver = resolver };
        using var asmDef = AssemblyDefinition.ReadAssembly(inputMs, parameters);

        int totalRewrites = 0;
        foreach (var module in asmDef.Modules)
        {
            totalRewrites += RewriteModule(module);
        }
        Console.WriteLine($"[Rewriter] Total rewrites: {totalRewrites}");

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
