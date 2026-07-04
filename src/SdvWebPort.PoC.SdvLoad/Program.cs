using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace SdvWebPort.PoC.SdvLoad;

public static partial class Program
{
    public static async Task<int> Main()
    {
        Console.WriteLine("[SdvLoad] Starting — stub deps + Main() attempt");
        try
        {
            using var http = new HttpClient();
            var baseUrl = JsInterop.GetCurrentBaseUrl();

            var allDlls = new[] {
                "MonoGame.Framework.dll", "Xna.Framework.dll", "Xna.Framework.Game.dll",
                "Xna.Framework.Graphics.dll", "Xna.Framework.Content.dll",
                "Xna.Framework.Input.dll", "Xna.Framework.Audio.dll",
                "GalaxyCSharp.dll", "Steamworks.NET.dll",
                "xTile.dll", "BmFont.dll", "CPExtBmFont.dll", "Lidgren.Network.dll",
                "TMXTile.dll", "TextCopy.dll", "StardewValley.GameData.dll",
            };

            var alc = new AssemblyLoadContext("SdvLoad", isCollectible: true);

            // Load all DLLs
            foreach (var name in allDlls) {
                try {
                    alc.LoadFromStream(new MemoryStream(await http.GetByteArrayAsync(baseUrl + "sdv-dlls/" + name)));
                } catch (Exception ex) {
                    Console.WriteLine($"[SdvLoad] Skip {name}: {ex.Message.Split('\n')[0]}");
                }
            }
            Console.WriteLine("[SdvLoad] All deps loaded ✓");

            alc.Resolving += (ctx, name) =>
                ctx.Assemblies.FirstOrDefault(a => a.GetName().Name == name.Name) ??
                AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);

            // Load SDV
            var sdvAsm = alc.LoadFromStream(new MemoryStream(await http.GetByteArrayAsync(baseUrl + "sdv-dlls/Stardew Valley.dll")));
            Console.WriteLine($"[SdvLoad] SDV: v{sdvAsm.GetName().Version} ✓");

            // GetTypes
            Type[] types;
            try { types = sdvAsm.GetTypes(); Console.WriteLine($"[SdvLoad] ALL {types.Length} types ✓"); }
            catch (ReflectionTypeLoadException ex) {
                types = ex.Types.Where(t => t != null).ToArray()!;
                Console.WriteLine($"[SdvLoad] Partial: {types.Length}/{ex.Types.Length}");
            }

            // Find + call Program.Main
            var programType = types.FirstOrDefault(t => t?.Name == "Program");
            if (programType != null) {
                Console.WriteLine($"[SdvLoad] Program: {programType.FullName} ✓");
                var mainMethod = programType.GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (mainMethod != null) {
                    Console.WriteLine("[SdvLoad] === ATTEMPTING Program.Main() ===");
                    try {
                        mainMethod.Invoke(null, new object[] { new string[] { "" } });
                        Console.WriteLine("[SdvLoad] === MAIN() PASS ===");
                    }
                    catch (TargetInvocationException tie) {
                        var inner = tie.InnerException;
                        Console.WriteLine($"[SdvLoad] Main() threw: {inner?.GetType().Name}: {inner?.Message}");
                        if (inner?.StackTrace != null)
                            foreach (var line in inner.StackTrace.Split('\n').Take(10))
                                Console.WriteLine($"  {line.Trim()}");
                        Console.WriteLine("[SdvLoad] === EXPECTED FAILURE ===");
                    }
                }
            } else {
                Console.WriteLine("[SdvLoad] Program type not found");
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"[SdvLoad] FATAL: {ex.GetType().Name}: {ex.Message}");
            if (ex.StackTrace != null)
                foreach (var line in ex.StackTrace.Split('\n').Take(5))
                    Console.WriteLine($"  {line.Trim()}");
        }
        return 0;
    }
}

internal static partial class JsInterop
{
    [JSImport("globalThis.getCurrentBaseUrl")]
    public static partial string GetCurrentBaseUrl();
}
