using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;
using Microsoft.Xna.Platform;
using Microsoft.Xna.Platform.Input;

namespace SdvWebPort.PoC.SdvLoad;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[PoC.SdvLoad] Starting Phase 2.5 — Game1 invoke + render");
        Console.WriteLine($"[PoC.SdvLoad] .NET version: {Environment.Version}");
        Console.WriteLine($"[PoC.SdvLoad] Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");

        // 1. Register KNI factories BEFORE any Game instantiation.
        //    Without this, Game1's constructor fails with "no game factory registered".
        Console.WriteLine("[+] Registering KNI ConcreteGameFactory...");
        try
        {
            GameFactory.RegisterGameFactory(new ConcreteGameFactory());
            Console.WriteLine("[+] ConcreteGameFactory registered");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Could not register ConcreteGameFactory: {ex.Message}");
            Console.WriteLine("[!] Is nkast.Kni.Platform.Blazor.GL package referenced?");
            await KeepAlive();
            return 5;
        }
        Console.WriteLine("[+] Registering KNI ConcreteInputFactory...");
        try
        {
            InputFactory.RegisterInputFactory(new ConcreteInputFactory());
            Console.WriteLine("[+] ConcreteInputFactory registered");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Could not register ConcreteInputFactory: {ex.Message}");
            await KeepAlive();
            return 6;
        }

        // 2. Fetch Stardew Valley.dll from wwwroot (HTTP fetch — no native filesystem in WASM)
        const string sdvUrl = "Stardew Valley.dll";
        Console.WriteLine($"[+] Fetching SDV from: {sdvUrl}");

        byte[] sdvBytes;
        using (var http = new HttpClient())
        {
            try
            {
                var baseUri = new Uri(JsInterop.GetCurrentBaseUrl());
                var absoluteUri = new Uri(baseUri, sdvUrl);
                Console.WriteLine($"[+] Absolute URL: {absoluteUri}");
                sdvBytes = await http.GetByteArrayAsync(absoluteUri);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not fetch {sdvUrl}: {ex.Message}");
                Console.WriteLine("[!] Place 'Stardew Valley.dll' (or MockSdv.dll renamed) into:");
                Console.WriteLine("    src/SdvWebPort.PoC.SdvLoad/wwwroot/");
                await KeepAlive();
                return 2;
            }
        }
        Console.WriteLine($"[+] Fetched SDV: {sdvBytes.Length:N0} bytes ({sdvBytes.Length / 1024.0 / 1024.0:F2} MB)");

        // 3. Verify facade assembly is loaded (for TypeForwardedTo resolution)
        Assembly? facadeAssembly = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name == "MonoGame.Framework")
            {
                facadeAssembly = asm;
                break;
            }
        }
        if (facadeAssembly == null)
        {
            Console.WriteLine("[+] Facade not yet loaded — attempting explicit AssemblyLoadContext.Default.Load...");
            try
            {
                facadeAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(
                    new AssemblyName("MonoGame.Framework"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not load facade by name: {ex.Message}");
                await KeepAlive();
                return 3;
            }
        }
        Console.WriteLine($"[+] Facade assembly: {facadeAssembly.FullName}");

        // 4. Load SDV into the DEFAULT ALC
        Assembly sdvAsm;
        try
        {
            Console.WriteLine("[+] Loading Stardew Valley.dll into default ALC...");
            sdvAsm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(sdvBytes));
            Console.WriteLine($"[+] Loaded: {sdvAsm.FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SDV load threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            await KeepAlive();
            return 1;
        }

        // 5. Find StardewValley.Game1 type via reflection
        Console.WriteLine("");
        Console.WriteLine("[+] === Locating StardewValley.Game1 ===");
        Type? game1Type = sdvAsm.GetTypes().FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1Type == null)
        {
            Console.WriteLine("[FAIL] StardewValley.Game1 type not found in SDV assembly");
            Console.WriteLine("[!] Types found:");
            foreach (var t in sdvAsm.GetTypes().Take(20))
                Console.WriteLine($"    - {t.FullName}");
            await KeepAlive();
            return 7;
        }
        Console.WriteLine($"[+] Found: {game1Type.FullName}");
        Console.WriteLine($"[+] Base type: {game1Type.BaseType?.FullName} (asm: {game1Type.BaseType?.Assembly.GetName().Name})");

        // 6. Instantiate Game1 via Activator.CreateInstance
        Console.WriteLine("");
        Console.WriteLine("[+] === Instantiating Game1 ===");
        object? game1Instance;
        try
        {
            game1Instance = Activator.CreateInstance(game1Type);
            Console.WriteLine($"[+] Game1 instantiated: {game1Instance?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Game1 instantiation threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"    Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Console.WriteLine($"    Inner Stack: {ex.InnerException.StackTrace}");
            }
            await KeepAlive();
            return 8;
        }

        // 7. Call game.Run() via reflection — this starts the game loop.
        //    KNI's Blazor.GL platform drives the loop via requestAnimationFrame,
        //    so Run() returns control to Main after the first frame is scheduled.
        //    We then keep Main alive to let the loop continue.
        Console.WriteLine("");
        Console.WriteLine("[+] === Calling Game1.Run() ===");
        var runMethod = game1Type.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance);
        if (runMethod == null)
        {
            Console.WriteLine("[FAIL] Run() method not found on Game1");
            await KeepAlive();
            return 9;
        }
        Console.WriteLine($"[+] Run() method: {runMethod.ReturnType.Name} {runMethod.Name}()");
        try
        {
            runMethod.Invoke(game1Instance, null);
            Console.WriteLine("[+] Run() returned normally — game loop is running");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Run() threw: {ex.GetType().Name}: {ex.Message}");
            var inner = ex.InnerException ?? ex;
            Console.WriteLine($"    Inner: {inner.GetType().Name}: {inner.Message}");
            Console.WriteLine($"    Stack: {inner.StackTrace}");
            await KeepAlive();
            return 10;
        }

        // 8. Final verdict
        Console.WriteLine("");
        Console.WriteLine("[PASS] Game1 instantiated + Run() called!");
        Console.WriteLine("[PASS] Full pipeline: DLL load → facade → KNI → GraphicsDevice → Game loop");
        Console.WriteLine("[INFO] Game loop should now be rendering frames to the canvas.");
        Console.WriteLine("[INFO] Check the browser — you should see a CornflowerBlue background");
        Console.WriteLine("[INFO] with a red box bouncing around.");

        // Keep Main alive so the game loop can continue
        Console.WriteLine("[PoC.SdvLoad] Keeping runtime alive for game loop...");
        await Task.Delay(-1);
        return 0;
    }

    private static async Task KeepAlive()
    {
        Console.WriteLine("[PoC.SdvLoad] Keeping runtime alive for 5s to allow log capture...");
        await Task.Delay(5000);
    }
}

internal static partial class JsInterop
{
    [System.Runtime.InteropServices.JavaScript.JSImport("globalThis.getCurrentBaseUrl")]
    public static partial string GetCurrentBaseUrl();
}

/// <summary>
/// JS-callable .NET method invoker. Exposed via [JSExport] so the JS-side
/// `DotNet` shim can route `DotNet.invokeMethod(assemblyName, methodName, ...args)`
/// calls into .NET 10's runtime.
///
/// This bridges the gap between KNI's nkast.Wasm.* JS layer (written for the
/// .NET 8 Blazor WASM host model which provided `DotNet.invokeMethod`) and
/// .NET 10's Microsoft.NET.Sdk.WebAssembly (which does NOT provide `DotNet`).
///
/// KNI's JS calls patterns like:
///   DotNet.invokeMethod('nkast.Wasm.Dom', 'JsWindowOnAnimationFrame', uid, ci, time)
///   DotNet.invokeMethod('nkast.Wasm.JSInterop', 'JsPromiseOnCompleted', uid)
///
/// We route these to InvokeStaticMethod, which uses reflection to find and
/// call the static method on the named assembly.
/// </summary>
public static partial class DotNetInvoker
{
    [System.Runtime.InteropServices.JavaScript.JSExport]
    public static int InvokeStaticMethod(string assemblyName, string methodName, int arg1, int arg2, double arg3)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName);
            if (asm == null)
            {
                Console.Error.WriteLine($"[DotNetInvoker] Assembly not found: {assemblyName}");
                return -1;
            }

            // Look for a public static method with the given name that takes (int, int, double)
            var type = asm.GetTypes().FirstOrDefault(t => t.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(int), typeof(int), typeof(double) }, null) != null);
            if (type == null)
            {
                Console.Error.WriteLine($"[DotNetInvoker] Type with method {methodName}(int,int,double) not found in {assemblyName}");
                return -2;
            }

            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(int), typeof(int), typeof(double) }, null);
            method?.Invoke(null, new object[] { arg1, arg2, arg3 });
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DotNetInvoker] Error: {ex.Message}");
            return -99;
        }
    }

    [System.Runtime.InteropServices.JavaScript.JSExport]
    public static int InvokeStaticMethodIntInt(string assemblyName, string methodName, int arg1, int arg2)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName);
            if (asm == null) return -1;

            var type = asm.GetTypes().FirstOrDefault(t => t.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(int), typeof(int) }, null) != null);
            if (type == null) return -2;

            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(int), typeof(int) }, null);
            method?.Invoke(null, new object[] { arg1, arg2 });
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DotNetInvoker] IntInt Error: {ex.Message}");
            return -99;
        }
    }

    [System.Runtime.InteropServices.JavaScript.JSExport]
    public static int InvokeStaticMethodInt(string assemblyName, string methodName, int arg1)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == assemblyName);
            if (asm == null) return -1;

            var type = asm.GetTypes().FirstOrDefault(t => t.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(int) }, null) != null);
            if (type == null) return -2;

            var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(int) }, null);
            method?.Invoke(null, new object[] { arg1 });
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DotNetInvoker] Int Error: {ex.Message}");
            return -99;
        }
    }
}
