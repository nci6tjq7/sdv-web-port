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
        Console.WriteLine("[SdvWebPort.FnaRuntime] Build: +setMainLoopCallback JSImport (e28038b)");

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

            // Register the RunOneFrame callback with JS BEFORE booting SDV.
            // JS will call this callback via requestAnimationFrame each frame.
            // This replaces FNA's emscripten_set_main_loop P/Invoke.
            Console.WriteLine("[SdvWebPort.FnaRuntime] Registering RunOneFrame callback with JS...");
            SetMainLoopCallback(RunOneFrameCallback);
            Console.WriteLine("[SdvWebPort.FnaRuntime] Callback registered");

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

    [JSImport("globalThis.SDV.setMainLoopCallback")]
    public static partial void SetMainLoopCallback([JSMarshalAs<JSType.Function>] Action callback);

    /// <summary>
    /// Called by JS each requestAnimationFrame to run one frame of the game.
    /// This method is registered as a callback via SetMainLoopCallback.
    /// </summary>
    public static void RunOneFrameCallback()
    {
        try
        {
            // Call SDL3_FNAPlatform.RunOneFrameJS via reflection
            var fnaAsm = Array.Find(AppDomain.CurrentDomain.GetAssemblies(),
                a => a.GetName().Name == "FNA");
            if (fnaAsm == null) return;
            var platformType = fnaAsm.GetType("Microsoft.Xna.Framework.SDL3_FNAPlatform");
            if (platformType == null) return;
            var method = platformType.GetMethod("RunOneFrameJS",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("[RunOneFrameCallback] Error: " + e);
        }
    }
}
