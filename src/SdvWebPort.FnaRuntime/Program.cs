using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.FnaRuntime;

public static partial class Program
{
    // Microsoft.NET.Sdk.WebAssembly invokes this Main method.
    private static int Main(string[] args)
    {
        // CRITICAL: Disable dynamic code BEFORE any XmlSerializer is touched.
        // .NET WASM doesn't support System.Reflection.Emit (browsers don't allow JIT).
        // Mono WASM is also missing the RuntimeTypeBuilder.propagate_parent_native icall.
        // Setting this switch forces XmlSerializer into ReflectionOnly mode.
        // CORRECT switch name (verified from dotnet/runtime source):
        //   "System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"
        //   (must include .CompilerServices segment — without it, the switch is silently ignored)
        AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);
        Console.WriteLine("[SdvWebPort.FnaRuntime] Set IsDynamicCodeSupported=false (with .CompilerServices)");

        Console.WriteLine("[SdvWebPort.FnaRuntime] Starting Stardew Valley (FNA WASM, XMLHttpRequest Content loading)...");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] .NET version: {Environment.Version}");
        Console.WriteLine("[SdvWebPort.FnaRuntime] Build: +RunLoop ret (skip OnExiting) (bb87001)");

        try
        {
            // Initialize the HTTP-based TitleContainer with the correct base URL.
            // Content files are served from /deps/ (wwwroot/deps/Content/).
            // FNA's TitleContainer.OpenStream has been patched (via Mono.Cecil)
            // to call HttpTitleContainer.OpenStream instead of File.OpenRead.
            // This fetches XNB files via HTTP instead of the virtual filesystem.
            Microsoft.Xna.Framework.HttpTitleContainer.SetBaseUrl("/deps/");

            // Signal JS that we're ready
            OnReady();
            Console.WriteLine("[SdvWebPort.FnaRuntime] JS notified");

            // Boot Stardew Valley
            Console.WriteLine("[SdvWebPort.FnaRuntime] Booting StardewValley.Program.Main...");
            StardewValley.Program.Main(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdvWebPort.FnaRuntime] FATAL: {ex}");
            OnError(ex.ToString());
        }

        Console.WriteLine("[SdvWebPort.FnaRuntime] Main returned — runtime stays alive via dotnet.create()");
        return 0;
    }

    [JSImport("globalThis.SDV.onReady")]
    public static partial void OnReady();

    [JSImport("globalThis.SDV.error")]
    public static partial void OnError(string msg);

    /// <summary>
    /// Called by JS (via getAssemblyExports) each frame to run one frame of the game.
    /// This is the JS-driven main loop, replacing FNA's emscripten_set_main_loop
    /// P/Invoke which fails with DllNotFoundException: __Native in WASM.
    ///
    /// The patched SDL3_FNAPlatform.RunPlatformMainLoop sets emscriptenGame and
    /// blocks forever (Thread.Sleep loop). JS calls this method via
    /// dotnetInstance.getAssemblyExports("SdvWebPort.FnaRuntime").Program.RunOneFrame()
    /// every requestAnimationFrame.
    /// </summary>
    [JSExport]
    public static void RunOneFrame()
    {
        try
        {
            // Call SDL3_FNAPlatform.RunOneFrameJS which calls emscriptenGame.RunOneFrame()
            // We use reflection to avoid a hard dependency on FNA internals at compile time.
            // The patched FNA.dll has RunOneFrameJS as a public static method.
            var fnaAsm = Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
                a => a.GetName().Name == "FNA");
            if (fnaAsm == null)
            {
                Console.Error.WriteLine("[Program.RunOneFrame] FNA assembly not found");
                return;
            }
            var platformType = fnaAsm.GetType("Microsoft.Xna.Framework.SDL3_FNAPlatform");
            if (platformType == null)
            {
                Console.Error.WriteLine("[Program.RunOneFrame] SDL3_FNAPlatform type not found");
                return;
            }
            var method = platformType.GetMethod("RunOneFrameJS",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                Console.Error.WriteLine("[Program.RunOneFrame] RunOneFrameJS method not found");
                return;
            }
            method.Invoke(null, null);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[Program.RunOneFrame] Error: " + e);
        }
    }
}
