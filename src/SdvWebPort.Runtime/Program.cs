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

        // Call JS to clear canvas to a solid color.
        // [JSImport] is .NET 8+'s official WASM JS interop; it resolves
        // "globalThis.clearCanvas" at runtime to a JS function on the
        // global scope (see wwwroot/index.html).
        JsInterop.ClearCanvas(0x33, 0x66, 0x99); // RGB = #336699

        Console.WriteLine("[SdvWebPort] Canvas cleared to #336699");
        Console.WriteLine("[SdvWebPort] Phase 0 skeleton ready. Runtime will stay alive.");

        // Keep runtime alive so the browser tab stays interactive.
        await Task.Delay(Timeout.Infinite);
        return 0;
    }
}

/// <summary>
/// JS interop via [JSImport] (.NET 8+ WASM JS interop).
/// The runtime resolves the import path to a JS function on the global scope.
/// </summary>
internal static partial class JsInterop
{
    [JSImport("globalThis.clearCanvas")]
    internal static partial void ClearCanvas(int r, int g, int b);
}
