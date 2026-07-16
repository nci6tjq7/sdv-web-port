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
        // .NET WASM doesn't support System.Reflection.Emit (browsers don't allow JIT),
        // but Mono WASM still reports IsDynamicCodeSupported=true. This causes
        // XmlSerializer to try Reflection.Emit and crash with MissingMethodException.
        // Setting these switches forces XmlSerializer into ReflectionOnly mode.
        // Must happen before SDV's static constructors run (SerializableDictionary..cctor
        // creates an XmlSerializer in its static init).
        AppContext.SetSwitch("System.Runtime.RuntimeFeature.IsDynamicCodeSupported", false);
        // Also try the direct XmlSerializer reflection-only switch
        AppContext.SetSwitch("System.Xml.Serialization.XmlSerializer.IsReflectionOnly", true);
        Console.WriteLine("[SdvWebPort.FnaRuntime] Set IsDynamicCodeSupported=false + IsReflectionOnly=true");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] RuntimeInformation.IsDynamicCodeSupported = {System.Runtime.InteropServices.RuntimeInformation.IsDynamicCodeSupported}");

        Console.WriteLine("[SdvWebPort.FnaRuntime] Starting Stardew Valley (FNA WASM)...");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] .NET version: {Environment.Version}");

        try
        {
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
