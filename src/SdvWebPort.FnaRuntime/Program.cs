using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.FnaRuntime;

public static partial class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("[SdvWebPort.FnaRuntime] Starting Stardew Valley (FNA WASM)...");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] .NET version: {Environment.Version}");
        Console.WriteLine($"[SdvWebPort.FnaRuntime] OS: {Environment.OSVersion}");

        try
        {
            // Initialize JS interop for canvas/input
            RuntimeInit();
            Console.WriteLine("[SdvWebPort.FnaRuntime] JS runtime initialized");

            // Boot Stardew Valley
            // SDV's Program.Main creates Game1 and calls Run()
            Console.WriteLine("[SdvWebPort.FnaRuntime] Booting StardewValley.Program.Main...");
            StardewValley.Program.Main(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdvWebPort.FnaRuntime] FATAL: {ex}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("[SdvWebPort.FnaRuntime] Main returned, keeping alive...");
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    [JSImport("globalThis.SDV.init")]
    public static partial void RuntimeInit();
}
