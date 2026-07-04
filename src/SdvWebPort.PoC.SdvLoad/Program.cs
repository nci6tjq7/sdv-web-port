using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Mono.Cecil;

namespace SdvWebPort.PoC.SdvLoad;

public static partial class Program
{
    public static async Task<int> Main()
    {
        Console.WriteLine("[SdvLoad] Starting — Cecil-based assembly name rewrite");
        try
        {
            using var http = new HttpClient();
            var baseUrl = JsInterop.GetCurrentBaseUrl();

            // Fetch SDV DLL
            byte[] sdvBytes = await http.GetByteArrayAsync(baseUrl + "sdv-dlls/Stardew Valley.dll");
            Console.WriteLine($"[SdvLoad] SDV: {sdvBytes.Length:N0} bytes ✓");

            // Fetch KNI DLL
            byte[] kniBytes = await http.GetByteArrayAsync(baseUrl + "sdv-dlls/Xna.Framework.dll");
            Console.WriteLine($"[SdvLoad] KNI: {kniBytes.Length:N0} bytes ✓");

            // Step 1: Rewrite KNI assembly name to "MonoGame.Framework"
            Console.WriteLine("[SdvLoad] Rewriting KNI assembly name → MonoGame.Framework...");
            var kniDef = AssemblyDefinition.ReadAssembly(new MemoryStream(kniBytes));
            Console.WriteLine($"[SdvLoad] KNI original name: {kniDef.Name.Name}");
            kniDef.Name.Name = "MonoGame.Framework";
            kniDef.MainModule.AssemblyReferences.Clear(); // Remove all refs to avoid resolution
            var kniRewrittenMs = new MemoryStream();
            kniDef.Write(kniRewrittenMs); // KNI is small, Write should work
            byte[] kniAliasedBytes = kniRewrittenMs.ToArray();
            Console.WriteLine($"[SdvLoad] KNI rewritten: {kniAliasedBytes.Length:N0} bytes ✓");

            // Step 2: Create ALC and load aliased KNI first
            var alc = new AssemblyLoadContext("SdvLoad", isCollectible: true);
            var aliasedKni = alc.LoadFromStream(new MemoryStream(kniAliasedBytes));
            Console.WriteLine($"[SdvLoad] Aliased KNI loaded: {aliasedKni.FullName} ✓");

            // Step 3: Set up resolving for other KNI assemblies
            Assembly.Load("Xna.Framework.Game");
            Assembly.Load("Xna.Framework.Graphics");
            Assembly.Load("Xna.Framework.Content");
            alc.Resolving += (ctx, name) =>
            {
                Console.WriteLine($"[SdvLoad] Resolving: {name.Name}");
                var redirect = new[] { "Xna.Framework.Game", "Xna.Framework.Graphics", "Xna.Framework.Content" };
                if (redirect.Contains(name.Name))
                {
                    var found = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == name.Name);
                    if (found != null) { Console.WriteLine($"  → {found.GetName().Name}"); return found; }
                }
                return null;
            };

            // Step 4: Load SDV
            Console.WriteLine("[SdvLoad] Loading SDV...");
            var sdvAsm = alc.LoadFromStream(new MemoryStream(sdvBytes));
            Console.WriteLine($"[SdvLoad] Loaded: {sdvAsm.FullName} ✓");

            // Step 5: Inspect types
            Console.WriteLine("[SdvLoad] Inspecting types...");
            Type[] types;
            try { types = sdvAsm.GetTypes(); Console.WriteLine($"[SdvLoad] ALL {types.Length} types loaded ✓"); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
                Console.WriteLine($"[SdvLoad] Partial: {types.Length}/{ex.Types.Length}");
                foreach (var le in ex.LoaderExceptions.Take(3))
                    Console.WriteLine($"  Error: {le?.Message?.Split('\n')[0]}");
            }

            var programType = types.FirstOrDefault(t => t?.Name == "Program");
            var game1Type = types.FirstOrDefault(t => t?.Name == "Game1");
            Console.WriteLine($"[SdvLoad] Program: {programType?.FullName ?? "not found"}");
            Console.WriteLine($"[SdvLoad] Game1: {game1Type?.FullName ?? "not found"}");
            Console.WriteLine($"[SdvLoad] Types: {types.Length}");
            if (types.Length > 100)
                Console.WriteLine("[SdvLoad] === SDV DLL LOAD PASS ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdvLoad] FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace?.Split('\n').Take(5).Aggregate("", (a, b) => a + b + "\n"));
        }
        return 0;
    }
}

internal static partial class JsInterop
{
    [JSImport("globalThis.getCurrentBaseUrl")]
    public static partial string GetCurrentBaseUrl();
}
