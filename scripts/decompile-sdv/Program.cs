using System;
using System.IO;
using System.Linq;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

class Program
{
    static void Main(string[] args)
    {
        var dllPath = args.Length > 0 ? args[0] : "/tmp/sdv-extract/Stardew Valley/Stardew Valley.dll";
        var outDir = args.Length > 1 ? args[1] : "/tmp/sdv-decompiled";

        Console.WriteLine($"[+] Decompiling: {dllPath}");
        Console.WriteLine($"[+] Output dir: {outDir}");

        Directory.CreateDirectory(outDir);

        var settings = new DecompilerSettings(LanguageVersion.Latest)
        {
            ThrowOnAssemblyResolveErrors = false,
            // RemoveDeadCode/Stores = true causes ILSpy to inline single-use
            // temporaries like 'Color val = base.Value; return ((Color)(ref val)).R;'
            // → 'return base.Value.R;', which avoids the broken (ref X) syntax.
            RemoveDeadCode = true,
            RemoveDeadStores = true,
            UseDebugSymbols = false,
            ShowDebugInfo = false,
            UseLambdaSyntax = true,
            FileScopedNamespaces = true,
            UseRefLocalsForRefReturns = false,
            AlwaysCastTargetsOfExplicitInterfaceImplementationCall = false,
            ShowILInstructions = false,
        };

        var decompiler = new CSharpDecompiler(dllPath, settings);
        var assembly = decompiler.TypeSystem.MainModule;

        Console.WriteLine($"[+] Assembly: {assembly.AssemblyName}");
        var topLevelTypes = assembly.TopLevelTypeDefinitions.ToList();
        Console.WriteLine($"[+] Top-level type count: {topLevelTypes.Count}");

        int fileCount = 0;
        int errorCount = 0;
        var allNamespaces = new System.Collections.Generic.HashSet<string>();

        foreach (var typeDef in topLevelTypes)
        {
            try
            {
                var fullName = typeDef.FullName;
                var ns = typeDef.Namespace ?? "";
                var name = typeDef.Name;
                allNamespaces.Add(ns);

                // Skip compiler-generated types that can't stand alone
                if (name.StartsWith("<") || name.StartsWith("$"))
                {
                    continue;
                }

                // Create namespace directory
                var nsDir = string.IsNullOrEmpty(ns) ? outDir : Path.Combine(outDir, ns.Replace('.', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(nsDir);

                // Sanitize filename (remove generic arity suffix)
                var fileName = name;
                var filePath = Path.Combine(nsDir, fileName + ".cs");

                // Decompile this type using its metadata handle
                var syntaxTree = decompiler.Decompile(typeDef.MetadataToken);
                var code = syntaxTree.ToString();

                File.WriteAllText(filePath, code);
                fileCount++;

                if (fileCount % 200 == 0)
                {
                    Console.WriteLine($"[+] Decompiled {fileCount} types...");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                if (errorCount <= 10)
                    Console.WriteLine($"[!] Error decompiling {typeDef.FullName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        Console.WriteLine($"[+] Decompilation complete:");
        Console.WriteLine($"    Files generated: {fileCount}");
        Console.WriteLine($"    Errors: {errorCount}");
        Console.WriteLine($"    Namespaces: {allNamespaces.Count}");
        Console.WriteLine($"[+] Output: {outDir}");

        // Show namespace breakdown
        Console.WriteLine("[+] Top namespaces (by type count):");
        var nsCounts = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var ns in allNamespaces)
        {
            var nsDir = string.IsNullOrEmpty(ns) ? outDir : Path.Combine(outDir, ns.Replace('.', Path.DirectorySeparatorChar));
            var count = Directory.Exists(nsDir) ? Directory.GetFiles(nsDir, "*.cs", SearchOption.TopDirectoryOnly).Length : 0;
            nsCounts[ns] = count;
        }
        foreach (var kv in nsCounts.OrderByDescending(k => k.Value).Take(15))
        {
            Console.WriteLine($"    {kv.Key}: {kv.Value} types");
        }
    }
}
