using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;

namespace SdvWebPort.FnaRuntime;

public static partial class Program
{
    // P/Invoke SDL_SetHint directly from SDL3.a (linked into WASM).
    // SDL3 no longer reads environment variables for hints (unlike SDL2).
    // We need to call SDL_SetHint to set FNA3D_OPENGL_FORCE_ES3=1 so that
    // FNA3D's SDL_GetHintBoolean("FNA3D_OPENGL_FORCE_ES3", 0) returns true.
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SDL_SetHint(string name, string value);

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
        // This is a backup for the .csproj's <DynamicCodeSupport>false</DynamicCodeSupport>
        // and <RuntimeHostConfigurationOption>, in case the WASM SDK doesn't process them.
        AppContext.SetSwitch("System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported", false);
        Console.WriteLine("[SdvWebPort.FnaRuntime] Set IsDynamicCodeSupported=false (with .CompilerServices)");

        // Force FNA3D to use OpenGL ES 3.0 (which maps to WebGL 2.0 in emscripten).
        // FNA3D checks SDL_GetPlatform() and forces ES3 only if it returns "Emscripten".
        // But in .NET WASM, SDL_GetPlatform() returns "Unknown" (not "Emscripten"),
        // so FNA3D defaults to desktop OpenGL 2.1 — which emscripten maps to WebGL 1.0.
        // WebGL 1.0 = OpenGL ES 2.0, but SDV requires OpenGL ES 3.0 features.
        //
        // SDL3 no longer reads environment variables for hints (unlike SDL2).
        // So we must call SDL_SetHint directly to set FNA3D_OPENGL_FORCE_ES3=1.
        // This makes FNA3D's SDL_GetHintBoolean("FNA3D_OPENGL_FORCE_ES3", 0) return true,
        // which causes it to request OpenGL ES 3.0 context attributes
        // (SDL_GL_CONTEXT_MAJOR_VERSION=3, SDL_GL_CONTEXT_PROFILE_MASK=ES).
        try
        {
            var result = SDL_SetHint("FNA3D_OPENGL_FORCE_ES3", "1");
            Console.WriteLine($"[SdvWebPort.FnaRuntime] SDL_SetHint(FNA3D_OPENGL_FORCE_ES3, 1) = {result}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SdvWebPort.FnaRuntime] SDL_SetHint failed: {ex.Message}");
        }

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
