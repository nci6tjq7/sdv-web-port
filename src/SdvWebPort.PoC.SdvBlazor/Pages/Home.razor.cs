using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
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
    private HttpClient? _http;
    private Game? _game;
    private bool _loadAttempted;

    [Inject]
    public IWebAssemblyHostEnvironment HostEnv { get; set; } = null!;

    [Inject]
    public HttpClient Http
    {
        get => _http!;
        set => _http = value;
    }

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

    private async Task<Game?> LoadAndInstantiateGame1()
    {
        // 1. Fetch "Stardew Valley.dll" from wwwroot.
        const string sdvUrl = "Stardew Valley.dll";
        Console.WriteLine($"[+] Fetching SDV from: {sdvUrl}");
        Console.WriteLine($"[+] Base address: {HostEnv.BaseAddress}");
        byte[] sdvBytes;
        try
        {
            var absoluteUri = new Uri(new Uri(HostEnv.BaseAddress), sdvUrl);
            Console.WriteLine($"[+] Absolute URL: {absoluteUri}");
            sdvBytes = await _http!.GetByteArrayAsync(absoluteUri);
            Console.WriteLine($"[+] Fetched SDV: {sdvBytes.Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Could not fetch {sdvUrl}: {ex.Message}");
            return null;
        }

        // 2. Set up the VFS with a test file (simulates user's uploaded GOG files).
        //    In production, this is where the user's FSA/OPFS-uploaded files go.
        var vfs = new SdvWebPort.Vfs.InMemoryVfs();
        await vfs.WriteFileAsync("Content/test.txt", System.Text.Encoding.UTF8.GetBytes("Hello from VFS!"));
        SdvWebPort.Vfs.SdvFileShim.SetVfs(vfs);
        Console.WriteLine("[+] VFS set up with Content/test.txt");

        // 3. Run the Cecil rewriter on the SDV bytes (in-memory, user's file untouched).
        byte[] rewrittenBytes;
        try
        {
            Console.WriteLine("[+] Running Cecil rewriter (redirect File/Directory → SdvFileShim)...");
            rewrittenBytes = SdvWebPort.Rewriter.SdvFileSystemRewriter.Rewrite(sdvBytes);
            Console.WriteLine($"[+] Rewritten: {rewrittenBytes.Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Rewriter threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            // Fall back to original bytes (rewriter failed — SDV's File calls will throw at runtime)
            rewrittenBytes = sdvBytes;
        }

        // 4. Verify the MonoGame.Framework facade is loaded.
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

        // 5. Load the rewritten SDV DLL into the DEFAULT ALC.
        Assembly sdvAsm;
        try
        {
            Console.WriteLine("[+] Loading rewritten SDV into default ALC...");
            sdvAsm = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(rewrittenBytes));
            Console.WriteLine($"[+] Loaded: {sdvAsm.FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SDV load threw: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        // 6. Find the Game type via reflection.
        //    Try FileSystemTestGame first (for Phase 2.75 testing), fall back to Game1.
        Type? gameType = sdvAsm.GetTypes().FirstOrDefault(t => t.FullName == "StardewValley.FileSystemTestGame")
                         ?? sdvAsm.GetTypes().FirstOrDefault(t => t.FullName == "StardewValley.Game1");
        if (gameType == null)
        {
            Console.WriteLine("[FAIL] No Game type found in SDV assembly");
            foreach (var t in sdvAsm.GetTypes().Take(20))
                Console.WriteLine($"    - {t.FullName}");
            return null;
        }
        Console.WriteLine($"[+] Found: {gameType.FullName}");
        Console.WriteLine($"[+] Base type: {gameType.BaseType?.FullName} (asm: {gameType.BaseType?.Assembly.GetName().Name})");

        // 7. Instantiate the Game via Activator.CreateInstance.
        object? gameInstance;
        try
        {
            gameInstance = Activator.CreateInstance(gameType);
            Console.WriteLine($"[+] Game instantiated: {gameInstance?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Game instantiation threw: {ex.GetType().Name}: {ex.Message}");
            var inner = ex.InnerException ?? ex;
            Console.WriteLine($"    Inner: {inner.GetType().Name}: {inner.Message}");
            Console.WriteLine($"    Stack: {inner.StackTrace}");
            return null;
        }

        return (Game?)gameInstance;
    }
}
