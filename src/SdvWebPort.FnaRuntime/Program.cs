using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.FnaRuntime;

public static partial class Program
{
    // BlazorWebAssembly SDK auto-invokes this Main method.
    private static async Task Main(string[] args)
    {
        Console.WriteLine("[SdvWebPort.FnaRuntime] Starting Stardew Valley (FNA WASM)...");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] .NET version: {Environment.Version}");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] OS: {Environment.OSVersion}");

        try
        {
            // Signal JS that we're ready
            Console.WriteLine("[SdvWebPort.FnaRuntime] Signaling JS...");
            OnReady();
            Console.WriteLine("[SdvWebPort.FnaRuntime] JS notified");

            // Boot Stardew Valley
            // SDV's Program.Main creates Game1 and calls Run()
            Console.WriteLine("[SdvWebPort.FnaRuntime] Booting StardewValley.Program.Main...");
            StardewValley.Program.Main(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdvWebPort.FnaRuntime] FATAL: {ex}");
            Console.WriteLine(ex.StackTrace);
           OnError(ex.ToString());
        }

        Console.WriteLine("[SdvWebPort.FnaRuntime] Main returned, keeping alive...");
        await Task.Delay(Timeout.Infinite);
    }

    [JSImport("globalThis.SDV.onReady")]
    public static partial void OnReady();

    [JSImport("globalThis.SDV.error")]
    public static partial void OnError(string msg);
}
