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
        Console.WriteLine("[SdvWebPort.FnaRuntime] Build: +Resolve() re-enabled + MG 3.6 (9e46144)");

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

            // Boot Stardew Valley.
            // The patched RunPlatformMainLoop runs a C#-driven loop:
            //   while(true) { emscriptenGame.RunOneFrame(); Thread.Sleep(0); }
            // This blocks the deputy worker forever, running the game.
            // No JS callback needed — the loop is entirely C#-driven.
            // The canvas was transferred to the worker via celeste-wasm sed
            // patch (transferredCanvasNames=[".canvas"]), so WebGL calls
            // happen on the worker.
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
}
