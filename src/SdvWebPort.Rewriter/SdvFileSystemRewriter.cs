using Mono.Cecil;
using Mono.Cecil.Cil;
using SdvWebPort.Vfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        var vfsAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "SdvWebPort.Vfs");
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

        // Fallback: create an AssemblyReference to SdvWebPort.Vfs and a
        // TypeReference scoped to it (less reliable — return types may not match).
        Console.WriteLine($"[Rewriter] WARN: SdvWebPort.Vfs assembly not found in AppDomain — using fallback TypeReference");
        var vfsAsmRef = new AssemblyNameReference("SdvWebPort.Vfs", new Version(1, 0, 0, 0));
        var existingRef = module.AssemblyReferences.FirstOrDefault(ar => ar.Name == "SdvWebPort.Vfs");
        if (existingRef != null)
            vfsAsmRef = existingRef;
        else
            module.AssemblyReferences.Add(vfsAsmRef);

        shimTypeRef = new TypeReference(
            @namespace: "SdvWebPort.Vfs",
            name: "SdvFileShim",
            module: module,
            scope: vfsAsmRef);

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (method.Body == null) continue;
                rewrites += RewriteMethodFallback(method, shimTypeRef);
            }
        }
        return rewrites;
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

    private static int RewriteMethodFallback(MethodDefinition method, TypeReference shimTypeRef)
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

            if (!_rewriteMap.TryGetValue((declaringType, callee.Name, callee.Parameters.Count), out var shimMethod))
                continue;

            var newCallee = new MethodReference(shimMethod, callee.ReturnType, shimTypeRef);
            foreach (var param in callee.Parameters)
                newCallee.Parameters.Add(param);

            instr.Operand = newCallee;
            rewrites++;
            Console.WriteLine($"[Rewriter] {method.DeclaringType.FullName}::{method.Name}: {declaringType}.{callee.Name} → SdvFileShim.{shimMethod} (fallback)");
        }
        return rewrites;
    }
}
