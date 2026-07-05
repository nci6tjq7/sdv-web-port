using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace SdvWebPort.PoC.SdvBlazor.Pages;

public partial class Home : ComponentBase
{
    private static readonly HttpClient _http = new HttpClient();
    private Game? _game;
    private bool _loadAttempted;

    // JsRuntime is injected via @inject in Home.razor — no need to redeclare here.

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);

        if (firstRender)
        {
            // Register this component as a .NET object reference so JS can call
            // back into our TickDotNet method via invokeMethodAsync('TickDotNet').
            // This is the KNI Blazor pattern (Phase 2.5b): the game loop is
            // driven by JS requestAnimationFrame, not by Game.Run() blocking.
            JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
        }
    }

    [JSInvokable]
    public async Task TickDotNet()
    {
        // First call: load the SDV DLL, instantiate Game1, call Run().
        if (!_loadAttempted)
        {
            _loadAttempted = true;
            Console.WriteLine("[Home.TickDotNet] First tick — loading SDV DLL");
            try
            {
                _game = await LoadAndInstantiateGame1();
                if (_game != null)
                {
                    Console.WriteLine("[Home.TickDotNet] Calling game.Run() (Initialize + LoadContent)");
                    _game.Run();
                    Console.WriteLine("[Home.TickDotNet] Run() returned — game initialized");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Home.TickDotNet] FATAL: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"[Home.TickDotNet] Stack: {ex.StackTrace}");
            }
        }

        // Every call (after init): tick the game loop manually (Update + Draw).
        // This is the external game loop driven by JS requestAnimationFrame.
        if (_game != null)
        {
            _game.Tick();
        }
    }

    private static async Task<Game?> LoadAndInstantiateGame1()
    {
        // 1. Fetch "Stardew Valley.dll" from wwwroot.
        //    For testing, this is MockSdv.dll (a real Game1 subclass compiled against
        //    MonoGame.Framework). For real SDV, the user copies their GOG
        //    "Stardew Valley.dll" here.
        const string sdvUrl = "Stardew Valley.dll";
        Console.WriteLine($"[+] Fetching SDV from: {sdvUrl}");
        byte[] sdvBytes;
        try
        {
            // Use a relative URL — Blazor's HttpClient will resolve against the base address.
            sdvBytes = await _http.GetByteArrayAsync(sdvUrl);
            Console.WriteLine($"[+] Fetched SDV: {sdvBytes.Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Could not fetch {sdvUrl}: {ex.Message}");
            Console.WriteLine("[!] Place 'Stardew Valley.dll' (or MockSdv.dll renamed) in wwwroot/");
            return null;
        }

        // 2. Verify the MonoGame.Framework facade is loaded (for TypeForwardedTo resolution).
        //    On net8.0 BlazorWebAssembly, the facade should be auto-loaded because it's
        //    a ProjectReference. Check anyway.
        Assembly? facadeAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "MonoGame.Framework");
        if (facadeAssembly == null)
        {
            Console.WriteLine("[+] Facade not yet loaded — loading explicitly...");
            try
            {
                facadeAssembly = AssemblyLoadContext.Default.LoadFromAssemblyName(
                    new AssemblyName("MonoGame.Framework"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FAIL] Could not load facade: {ex.Message}");
                return null;
            }
        }
        Console.WriteLine($"[+] Facade assembly: {facadeAssembly.FullName}");

        // 3. Load the SDV DLL into the DEFAULT ALC.
        //    TypeForwardedTo resolution requires the SDV assembly + facade + KNI
        //    to all be in the same ALC (the default ALC).
        Assembly sdvAsm;
        try
        {
            Console.WriteLine("[+] Loading SDV into default ALC...");
            sdvAsm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(sdvBytes));
            Console.WriteLine($"[+] Loaded: {sdvAsm.FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SDV load threw: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        // 4. Find StardewValley.Game1 via reflection.
        Type? game1Type = sdvAsm.GetTypes().FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (game1Type == null)
        {
            Console.WriteLine("[FAIL] StardewValley.Game1 not found in SDV assembly");
            Console.WriteLine("[!] Types found:");
            foreach (var t in sdvAsm.GetTypes().Take(20))
                Console.WriteLine($"    - {t.FullName}");
            return null;
        }
        Console.WriteLine($"[+] Found: {game1Type.FullName}");
        Console.WriteLine($"[+] Base type: {game1Type.BaseType?.FullName} (asm: {game1Type.BaseType?.Assembly.GetName().Name})");

        // 5. Instantiate Game1 via Activator.CreateInstance.
        object? game1Instance;
        try
        {
            game1Instance = Activator.CreateInstance(game1Type);
            Console.WriteLine($"[+] Game1 instantiated: {game1Instance?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Game1 instantiation threw: {ex.GetType().Name}: {ex.Message}");
            var inner = ex.InnerException ?? ex;
            Console.WriteLine($"    Inner: {inner.GetType().Name}: {inner.Message}");
            Console.WriteLine($"    Stack: {inner.StackTrace}");
            return null;
        }

        return (Game?)game1Instance;
    }
}
