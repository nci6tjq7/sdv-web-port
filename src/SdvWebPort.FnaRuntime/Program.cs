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

        Console.WriteLine("[SdvWebPort.FnaRuntime] Starting Stardew Valley (FNA WASM)...");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] .NET version: {Environment.Version}");

        try
        {
            // Set the working directory to /deps so SDV can find Content/ files.
            // SDV sets Content.RootDirectory = "Content" and FNA's TitleContainer
            // resolves paths relative to the current directory.
            // In WASM with -sWASMFS, the virtual filesystem root is /.
            // Our Content files are deployed at /deps/Content/ (served as static files
            // from wwwroot/deps/Content/).
            // Setting CurrentDirectory to /deps makes "Content/Data/BigCraftables.xnb"
            // resolve to /deps/Content/Data/BigCraftables.xnb.
            try
            {
                Environment.CurrentDirectory = "/deps";
                Console.WriteLine($"[SdvWebPort.FnaRuntime] Working directory: {Environment.CurrentDirectory}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SdvWebPort.FnaRuntime] Failed to set working directory: {ex.Message}");
            }

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

        Console.WriteLine("[SdvWebPort.FnaRuntime] Main returned, keeping alive...");
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    [JSImport("globalThis.SDV.onReady")]
    public static partial void OnReady();

    [JSImport("globalThis.SDV.error")]
    public static partial void OnError(string msg);
}
