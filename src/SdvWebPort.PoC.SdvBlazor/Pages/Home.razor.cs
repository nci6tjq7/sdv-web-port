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
            // Register an ALC.Resolving handler that maps .NET 6 version refs → .NET 8.
            // Real SDV references System.Runtime v6.0.0.0; our runtime is v8.0.0.0.
            // AppDomain.AssemblyResolve doesn't fire in Blazor WASM — use ALC.Resolving.
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                if (name.Version != null && name.Version.Major < 8)
                {
                    var runtimeAsm = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == name.Name);
                    if (runtimeAsm != null)
                    {
                        Console.WriteLine($"[ALC.Resolving] {name.Name} v{name.Version} → {runtimeAsm.GetName().Version}");
                        return runtimeAsm;
                    }
                }
                return null;
            };

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

        // 5. Pre-fetch SDV dependencies before loading SDV.
        //    ALC.Load is sync — can't do async HttpClient in WASM.
        Console.WriteLine("[+] Pre-fetching SDV dependencies...");
        var deps = new Dictionary<string, byte[]>();
        foreach (var depName in new[] { "xTile", "StardewValley.GameData" })
        {
            try
            {
                var depUri = new Uri(new Uri(HostEnv.BaseAddress), $"{depName}.dll");
                var depBytes = await _http!.GetByteArrayAsync(depUri);
                Console.WriteLine($"[+] Fetched {depName}.dll: {depBytes.Length:N0} bytes");
                deps[depName] = SdvWebPort.Rewriter.SdvFileSystemRewriter.Rewrite(depBytes);
            }
            catch (Exception ex) { Console.WriteLine($"[!] Could not fetch {depName}.dll: {ex.Message}"); }
        }

        // 6. Load the rewritten SDV DLL into a CUSTOM ALC with pre-loaded deps.
        Assembly sdvAsm;
        try
        {
            Console.WriteLine("[+] Loading rewritten SDV into SdvLoadContext...");
            var alc = new SdvLoadContext(deps);
            sdvAsm = alc.LoadFromStream(new MemoryStream(rewrittenBytes));
            Console.WriteLine($"[+] Loaded: {sdvAsm.FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SDV load threw: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        // 6. Find the Game type via reflection.
        // Use GetType(string) instead of GetTypes() — GetType lazily resolves
        // only the requested type, while GetTypes() eagerly resolves ALL type
        // references (which fails for System.Guid in System.Runtime v6.0.0.0).
        Type? gameType = sdvAsm.GetType("StardewValley.FileSystemTestGame")
                         ?? sdvAsm.GetType("StardewValley.Game1");
        if (gameType == null)
        {
            Console.WriteLine("[FAIL] No Game type found via GetType. Trying GetTypes() with catch...");
            // Last resort: try GetTypes() with per-type error handling
            Type[] allTypes;
            try
            {
                allTypes = sdvAsm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                allTypes = ex.Types.Where(t => t != null).ToArray()!;
                Console.WriteLine($"[!] Partial type load: {allTypes.Length} OK, {ex.Types.Length - allTypes.Length} failed");
            }
            gameType = allTypes.FirstOrDefault(t => t.FullName == "StardewValley.FileSystemTestGame")
                     ?? allTypes.FirstOrDefault(t => t.FullName == "StardewValley.Game1");
            if (gameType == null)
            {
                Console.WriteLine("[FAIL] No Game type found. Types found:");
                foreach (var t in allTypes.Take(20))
                    Console.WriteLine($"    - {t.FullName}");
                return null;
            }
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

/// <summary>
/// Custom AssemblyLoadContext that resolves framework assembly references
/// (System.*, mscorlib, netstandard) by returning the runtime's already-loaded
/// versions. This solves the cross-ALC type resolution issue in Blazor WASM:
/// the runtime's System.Runtime is in a different internal ALC, and dynamically
/// loaded assemblies can't reference its types without this bridge.
/// </summary>
internal sealed class SdvLoadContext : AssemblyLoadContext
{
    private readonly Dictionary<string, byte[]> _preloadedDeps;
    private readonly Dictionary<string, Assembly> _loaded = new();

    public SdvLoadContext(Dictionary<string, byte[]> preloadedDeps) : base("SdvLoadContext", isCollectible: false)
    {
        _preloadedDeps = preloadedDeps;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? "";

        // Framework assemblies: redirect to CoreLib (where types are actually defined)
        if (name.StartsWith("System.") || name == "System" || name == "mscorlib" || name == "netstandard")
        {
            var coreLib = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "System.Private.CoreLib");
            if (coreLib != null) return coreLib;
            var runtimeAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);
            if (runtimeAsm != null) return runtimeAsm;
        }

        // MonoGame.Framework → facade
        if (name == "MonoGame.Framework")
        {
            var facade = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MonoGame.Framework");
            if (facade != null) return facade;
        }

        // SDV dependencies — return pre-loaded bytes (fetched before ALC.Load)
        if (_preloadedDeps.TryGetValue(name, out var depBytes))
        {
            if (_loaded.TryGetValue(name, out var cached)) return cached;
            Console.WriteLine($"[SdvLoadContext] Loading pre-fetched: {name}");
            var asm = LoadFromStream(new MemoryStream(depBytes));
            _loaded[name] = asm;
            return asm;
        }

        return null;
    }
}
