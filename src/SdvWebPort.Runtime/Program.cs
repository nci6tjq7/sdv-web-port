using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace SdvWebPort.Runtime;

public static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[SdvWebPort] Runtime initialized");
        Console.WriteLine($"[SdvWebPort] .NET version: {Environment.Version}");
        Console.WriteLine($"[SdvWebPort] Runtime: {RuntimeInformation.FrameworkDescription}");

        // Clear canvas to #336699 (RGB: 0x33, 0x66, 0x99) via JS interop
        JsInterop.ClearCanvas(0x33, 0x66, 0x99);
        Console.WriteLine("[SdvWebPort] Canvas cleared to #336699");
        Console.WriteLine("[SdvWebPort] Phase 0 skeleton ready (Blazor WASM host).");

        // Keep runtime alive for browser inspection
        await Task.Delay(-1);
        return 0;
    }
}

internal static partial class JsInterop
{
    [JSImport("globalThis.clearCanvas")]
    internal static partial void ClearCanvas(int r, int g, int b);
}
