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
        Console.WriteLine("[SdvLoad] Starting — Cecil type-forward rewrite");
        try
        {
            using var http = new HttpClient();
            var baseUrl = JsInterop.GetCurrentBaseUrl();

            byte[] sdvBytes = await http.GetByteArrayAsync(baseUrl + "sdv-dlls/Stardew Valley.dll");
            byte[] kniBytes = await http.GetByteArrayAsync(baseUrl + "sdv-dlls/Xna.Framework.dll");
            Console.WriteLine($"[SdvLoad] SDV: {sdvBytes.Length:N0}, KNI: {kniBytes.Length:N0} ✓");

            // Rewrite KNI: rename + add type forwards for Game types
            Console.WriteLine("[SdvLoad] Rewriting KNI with type forwards...");
            var kniDef = AssemblyDefinition.ReadAssembly(new MemoryStream(kniBytes));
            kniDef.Name.Name = "MonoGame.Framework";
            kniDef.Name.Version = new Version(3, 8, 0, 1641);
            foreach (var ar in kniDef.MainModule.AssemblyReferences)
            {
                if (ar.Name.StartsWith("Xna.Framework.")) ar.Name = ar.Name.Replace("Xna.Framework", "MonoGame.Framework");
            }
            // Add type forwards: tell CLR "these types live in MonoGame.Framework.Game"
            var gameRef = new AssemblyNameReference("MonoGame.Framework.Game", new Version(3, 8, 0, 1641));
            kniDef.MainModule.AssemblyReferences.Add(gameRef);
            var gameTypes = new[] { "DrawableGameComponent", "Game", "GameServiceContainer", "GameComponent", "GameTime" };
            foreach (var typeName in gameTypes)
            {
                var ts = new TypeReference("Microsoft.Xna.Framework", typeName, kniDef.MainModule, gameRef);
                kniDef.MainModule.ExportedTypes.Add(new ExportedType(ts.Namespace, ts.Name, kniDef.MainModule)
                {
                    Implementation = ts,
                });
            }
            var kniMs = new MemoryStream();
            kniDef.Write(kniMs);
            byte[] kniAliased = kniMs.ToArray();
            Console.WriteLine($"[SdvLoad] KNI rewritten with type forwards: {kniAliased.Length:N0} bytes ✓");

            // Load aliased KNI
            var alc = new AssemblyLoadContext("SdvLoad", isCollectible: true);
            alc.LoadFromStream(new MemoryStream(kniAliased));
            Console.WriteLine("[SdvLoad] Aliased KNI loaded ✓");

            // Load other KNI assemblies
            Assembly.Load("Xna.Framework.Game");
            Assembly.Load("Xna.Framework.Graphics");
            Assembly.Load("Xna.Framework.Content");

            // Resolving: redirect MonoGame.Framework.* → Xna.Framework.*
            alc.Resolving += (ctx, name) =>
            {
                var map = new[] { "MonoGame.Framework.Game", "MonoGame.Framework.Graphics", "MonoGame.Framework.Content",
                                  "Microsoft.Xna.Framework.Game", "Microsoft.Xna.Framework.Graphics", "Microsoft.Xna.Framework.Content" };
                if (map.Contains(name.Name))
                {
                    var kniName = name.Name.Replace("MonoGame.Framework", "Xna.Framework").Replace("Microsoft.Xna.Framework", "Xna.Framework");
                    var found = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == kniName);
                    if (found != null) { Console.WriteLine($"[SdvLoad] {name.Name} → {found.GetName().Name}"); return found; }
                }
                return null;
            };

            // Load SDV
            Console.WriteLine("[SdvLoad] Loading SDV...");
            var sdvAsm = alc.LoadFromStream(new MemoryStream(sdvBytes));
            Console.WriteLine($"[SdvLoad] Loaded: {sdvAsm.FullName} ✓");

            // GetTypes
            Console.WriteLine("[SdvLoad] Inspecting types...");
            Type[] types = Array.Empty<Type>();
            try { types = sdvAsm.GetTypes(); Console.WriteLine($"[SdvLoad] ALL {types.Length} types ✓"); }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
                Console.WriteLine($"[SdvLoad] Partial: {types.Length}/{ex.Types.Length}");
                var errors = ex.LoaderExceptions
                    .Where(le => le != null)
                    .Select(le => le.Message?.Split('\n')[0] ?? "?")
                    .GroupBy(m => m.Substring(0, Math.Min(80, m.Length)))
                    .OrderByDescending(g => g.Count())
                    .Take(5);
                foreach (var g in errors) Console.WriteLine($"  {g.Count()}x: {g.Key}");
            }

            var programType = types.FirstOrDefault(t => t?.Name == "Program");
            var game1Type = types.FirstOrDefault(t => t?.Name == "Game1");
            Console.WriteLine($"[SdvLoad] Program: {programType?.FullName ?? "not found"}");
            Console.WriteLine($"[SdvLoad] Game1: {game1Type?.FullName ?? "not found"}");
            Console.WriteLine($"[SdvLoad] Types: {types.Length}");
            if (types.Length > 100) Console.WriteLine("[SdvLoad] === SDV DLL LOAD PASS ===");
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
