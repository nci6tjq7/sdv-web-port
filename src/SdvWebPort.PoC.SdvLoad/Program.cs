using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace SdvWebPort.PoC.SdvLoad;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[PoC.SdvLoad] Starting SDV load PoC");
        Console.WriteLine($"[PoC.SdvLoad] .NET version: {Environment.Version}");
        Console.WriteLine($"[PoC.SdvLoad] Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

        // 1. Fetch Stardew Valley.dll from wwwroot (HTTP fetch — no native filesystem in WASM)
        const string sdvUrl = "Stardew Valley.dll";
        Console.WriteLine($"[+] Fetching SDV from: {sdvUrl}");

        byte[] sdvBytes;
        using (var http = new HttpClient())
        {
            try
            {
                var baseUri = new Uri(JsInterop.GetCurrentBaseUrl());
                var absoluteUri = new Uri(baseUri, sdvUrl);
                Console.WriteLine($"[+] Absolute URL: {absoluteUri}");
                sdvBytes = await http.GetByteArrayAsync(absoluteUri);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not fetch {sdvUrl}: {ex.Message}");
                Console.WriteLine("[!] Place 'Stardew Valley.dll' from your GOG install into:");
                Console.WriteLine("    src/SdvWebPort.PoC.SdvLoad/");
                Console.WriteLine("    The file is gitignored.");
                await KeepAlive();
                return 2;
            }
        }
        Console.WriteLine($"[+] Fetched SDV: {sdvBytes.Length:N0} bytes ({sdvBytes.Length / 1024.0 / 1024.0:F2} MB)");

        // 2. The MonoGame.Framework facade is referenced by this project and
        //    loaded into the default ALC at startup. The KNI assemblies
        //    (Xna.Framework, .Game, .Graphics, etc.) are also in the default
        //    ALC because they're referenced via the facade's TypeForwardedTo
        //    attributes.
        Assembly? facadeAssembly = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "MonoGame.Framework")
            {
                facadeAssembly = asm;
                break;
            }
        }
        if (facadeAssembly == null)
        {
            Console.WriteLine("[+] Facade not yet loaded — attempting explicit AssemblyLoadContext.Default.Load...");
            try
            {
                facadeAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(
                    new AssemblyName("MonoGame.Framework"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not load facade by name: {ex.Message}");
                await KeepAlive();
                return 3;
            }
        }
        Console.WriteLine($"[+] Facade assembly: {facadeAssembly.FullName}");
        Console.WriteLine($"[+] Facade ALC: {AssemblyLoadContext.GetLoadContext(facadeAssembly)?.Name ?? "(default)"}");

        // 3. Load SDV into the DEFAULT ALC. Both the facade and KNI live in
        //    the default ALC, so type resolution should find them all.
        //    (See plan doc for known limitation: TypeForwardedTo may not
        //    resolve in Mono WASM runtime — Phase 2.5 will introduce a
        //    Cecil-based AssemblyRef rewriter to address this.)
        Assembly sdvAsm;
        try
        {
            Console.WriteLine("[+] Loading Stardew Valley.dll into default ALC...");
            sdvAsm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(sdvBytes));
            Console.WriteLine($"[+] Loaded: {sdvAsm.FullName}");
            Console.WriteLine($"[+] Version: {sdvAsm.GetName().Version}");
            Console.WriteLine($"[+] SDV ALC: {AssemblyLoadContext.GetLoadContext(sdvAsm)?.Name ?? "(default)"}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SDV load threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            await KeepAlive();
            return 1;
        }

        // 4. Enumerate types (this is where TypeForwardedTo limitation manifests)
        Console.WriteLine("");
        Console.WriteLine("[+] === Type Enumeration ===");
        Type[] allTypes;
        try
        {
            allTypes = sdvAsm.GetTypes();
            Console.WriteLine($"[+] Total types resolved: {allTypes.Length}");
        }
        catch (ReflectionTypeLoadException ex)
        {
            allTypes = ex.Types.Where(t => t != null).ToArray()!;
            Console.WriteLine($"[!] Partial load: {allTypes.Length} OK, {ex.Types.Length - allTypes.Length} failed");
            Console.WriteLine("[!] Loader exceptions (first 10):");
            foreach (var le in ex.LoaderExceptions.Take(10))
            {
                Console.WriteLine($"    - {le?.GetType().Name}: {le?.Message?.Split('\n')[0]}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] GetTypes() threw: {ex.GetType().Name}: {ex.Message?.Split('\n')[0]}");
            Console.WriteLine("[!] This is the known TypeForwardedTo limitation in Mono WASM.");
            Console.WriteLine("[!] See plan doc § 'Known Limitations' for details.");
            Console.WriteLine("[!] Phase 2.5 will introduce a Cecil-based AssemblyRef rewriter.");
            await KeepAlive();
            return 1;
        }

        // 5. Look for known SDV entry-point types
        Console.WriteLine("");
        Console.WriteLine("[+] === Searching for SDV entry types ===");
        string[] knownTypes = new[]
        {
            "StardewValley.Program",
            "StardewValley.Game1",
            "StardewValley.GameLocation",
            "StardewValley.Farmer",
            "StardewValley.Object",
            "StardewValley.Tools.Tool",
        };
        int foundCount = 0;
        foreach (var name in knownTypes)
        {
            var t = allTypes.FirstOrDefault(x => x.FullName == name);
            if (t != null)
            {
                Console.WriteLine($"    FOUND: {t.FullName}");
                foundCount++;
            }
            else
            {
                Console.WriteLine($"    MISSING: {name}");
            }
        }

        // 6. Inspect Game1's base type — verify it resolves to KNI's Game class
        var game1 = allTypes.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1 != null)
        {
            Console.WriteLine("");
            Console.WriteLine("[+] === Game1 base type chain ===");
            var bt = game1.BaseType;
            while (bt != null)
            {
                Console.WriteLine($"    -> {bt.FullName}  (asm: {bt.Assembly.GetName().Name} v{bt.Assembly.GetName().Version})");
                bt = bt.BaseType;
            }
        }

        // 7. Inspect StardewValley.Program.Main
        var program = allTypes.FirstOrDefault(t => t.FullName == "StardewValley.Program");
        if (program != null)
        {
            Console.WriteLine("");
            Console.WriteLine("[+] === StardewValley.Program methods ===");
            var methods = program.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            foreach (var m in methods.Take(20))
            {
                var ps = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                Console.WriteLine($"    - {m.ReturnType.Name} {m.Name}({ps})  [{(m.IsStatic ? "static" : "instance")}]");
            }
        }

        // 8. Final verdict
        Console.WriteLine("");
        if (foundCount == knownTypes.Length)
        {
            Console.WriteLine($"[PASS] All {knownTypes.Length} known SDV entry types resolved!");
            Console.WriteLine("[PASS] MonoGame.Framework -> KNI facade pattern WORKS.");
            Console.WriteLine("[NEXT] Ready for Phase 2.5: invoke Program.Main() or instantiate Game1.");
            await KeepAlive();
            return 0;
        }
        else
        {
            Console.WriteLine($"[PARTIAL] {foundCount}/{knownTypes.Length} entry types resolved.");
            Console.WriteLine("[!] Some SDV types failed to load — check loader exceptions above.");
            Console.WriteLine("[!] If you see 'Could not resolve type with token ...', this is the");
            Console.WriteLine("[!] known TypeForwardedTo limitation in Mono WASM. See plan doc.");
            await KeepAlive();
            return 1;
        }
    }

    private static async Task KeepAlive()
    {
        Console.WriteLine("[PoC.SdvLoad] Keeping runtime alive for 5s to allow log capture...");
        await Task.Delay(5000);
    }
}

internal static partial class JsInterop
{
    [System.Runtime.InteropServices.JavaScript.JSImport("globalThis.getCurrentBaseUrl")]
    public static partial string GetCurrentBaseUrl();
}
