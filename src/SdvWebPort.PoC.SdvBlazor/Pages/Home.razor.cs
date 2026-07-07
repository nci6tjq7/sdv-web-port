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

    protected override void OnAfterRender(bool firstRender)
    {
        base.OnAfterRender(firstRender);

        if (firstRender)
        {
            JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
        }
    }

    [JSInvokable]
    public async Task TickDotNet()
    {
        if (!_loadAttempted)
        {
            _loadAttempted = true;
            Console.WriteLine("[Home.TickDotNet] First tick — loading real SDV (Phase 2.8)");
            try
            {
                _game = await LoadRealSdvAsync();
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
                // Log all nested inner exceptions
                var inner = ex;
                int depth = 0;
                while (inner.InnerException != null && depth < 5)
                {
                    inner = inner.InnerException;
                    depth++;
                    Console.WriteLine($"[Home.TickDotNet] Inner[{depth}]: {inner.GetType().Name}: {inner.Message}");
                    Console.WriteLine($"[Home.TickDotNet] Inner[{depth}] Stack: {inner.StackTrace}");
                }
            }
        }

        if (_game != null)
        {
            _game.Tick();
        }
    }

    /// <summary>
    /// Load the real GOG SDV.dll + dependencies, set up VFS + NullSDKHelper,
    /// then instantiate GameRunner (mimicking Program.Main).
    /// </summary>
    private async Task<Game?> LoadRealSdvAsync()
    {
        // 1. Set up the VFS — use HttpVfs to fetch Content from /deps/content/
        SdvWebPort.Vfs.HttpVfs.SetJsRuntime(JsRuntime);
        var vfs = new SdvWebPort.Vfs.HttpVfs(_http!, HostEnv.BaseAddress);
        SdvWebPort.Vfs.SdvFileShim.SetVfs(vfs);
        Console.WriteLine("[+] VFS set up (HttpVfs → /deps/content/)");

        // 2. Fetch SDV.
        const string sdvUrl = "Stardew Valley.dll";
        Console.WriteLine($"[+] Fetching SDV from: {sdvUrl}");
        var absoluteUri = new Uri(new Uri(HostEnv.BaseAddress), sdvUrl);
        var sdvBytes = await _http!.GetByteArrayAsync(absoluteUri);
        Console.WriteLine($"[+] Fetched SDV: {sdvBytes.Length:N0} bytes");

        // 3. Run Cecil preprocessors on SDV:
        //    a. AssemblyRef rewriter: System.* v6→v8, MonoGame.Framework v3.8.0.1641→v3.8.5.0
        //    b. FileSystem rewriter: File/Directory → SdvFileShim
        byte[] rewrittenBytes;
        try
        {
            // Set bisection mode from URL query param (?bisect=N) for debugging
            var bisectMode = await ReadBisectModeFromUrlAsync();
            SdvWebPort.Rewriter.SdvAssemblyRefRewriter.BisectMode = bisectMode;
            if (bisectMode > 0)
                Console.WriteLine($"[+] BISECT MODE {bisectMode} — patching GameRunner..ctor() for debugging");

            Console.WriteLine("[+] Running Cecil AssemblyRef rewriter (System.* v6→v8, MG v3.8.0.1641→v3.8.5.0)...");
            var refRewritten = SdvWebPort.Rewriter.SdvAssemblyRefRewriter.Rewrite(sdvBytes);
            // SKIP FileSystem rewriter to test if it's undoing scope changes
            rewrittenBytes = refRewritten;
            Console.WriteLine($"[+] Final rewritten (no FS rewriter): {rewrittenBytes.Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Rewriter threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            rewrittenBytes = sdvBytes;
        }

        // 4. Load SDV + dependencies into default ALC.
        Assembly sdvAsm;
        try
        {
            sdvAsm = await SdvLoader.LoadSdvWithDependenciesAsync(_http!, HostEnv.BaseAddress, rewrittenBytes);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] SDV load threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            return null;
        }

        // 5. Pre-set Program._sdk = new NullSDKHelper() via reflection.
        //    Without this, Program.get_sdk() tries new SteamHelper() which fails
        //    (Steamworks.NET native deps unavailable in WASM).
        try
        {
            PreSetNullSdk(sdvAsm);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Could not pre-set Program._sdk: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            // Continue — get_sdk() will throw later if SteamHelper ctor fails
        }

        // 6. Find GameRunner type (Program.Main creates this — NOT Game1).
        //    Use GetType() instead of GetTypes() — GetTypes() iterates ALL types
        //    and fails on the first one that can't be resolved (e.g., types that
        //    reference GalaxyCSharp). GetType() only loads the requested type.
        Type? gameRunnerType = null;
        try
        {
            gameRunnerType = sdvAsm.GetType("StardewValley.GameRunner");
            Console.WriteLine($"[+] Found GameRunner via GetType(): {gameRunnerType?.FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] sdvAsm.GetType(\"StardewValley.GameRunner\") threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
        }
        if (gameRunnerType == null)
        {
            Console.WriteLine("[FAIL] StardewValley.GameRunner type not found via GetType — trying GetTypes() (will fail on first unresolvable type)");
            try
            {
                var allTypes = sdvAsm.GetTypes();
                Console.WriteLine($"[+] GetTypes() succeeded — {allTypes.Length} types");
                gameRunnerType = allTypes.FirstOrDefault(t => t.FullName == "StardewValley.GameRunner");
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"[FAIL] sdvAsm.GetTypes() also threw: {ex2.GetType().Name}: {ex2.Message}");
            }
        }
        if (gameRunnerType == null)
        {
            Console.WriteLine("[FAIL] StardewValley.GameRunner type not found");
            return null;
        }
        Console.WriteLine($"[+] Found GameRunner: {gameRunnerType.FullName}");
        Console.WriteLine($"[+] GameRunner base: {gameRunnerType.BaseType?.FullName} (asm: {gameRunnerType.BaseType?.Assembly.GetName().Name})");

        // 7. Instantiate GameRunner (calls GameRunner..ctor() — the heavy one).
        object? gameRunnerInstance;
        try
        {
            Console.WriteLine("[+] Instantiating GameRunner (ctor does GraphicsDeviceManager + LocalMultiplayer.Initialize + ItemRegistry.RegisterItemTypes + Window.AllowUserResizing)...");
            gameRunnerInstance = Activator.CreateInstance(gameRunnerType);
            Console.WriteLine($"[+] GameRunner instantiated: {gameRunnerInstance?.GetType().FullName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] GameRunner instantiation threw: {ex.GetType().Name}: {ex.Message}");
            var inner = ex.InnerException ?? ex;
            Console.WriteLine($"    Inner: {inner.GetType().Name}: {inner.Message}");
            Console.WriteLine($"    Stack: {inner.StackTrace}");
            return null;
        }

        // 8. Set GameRunner.instance static field (Program.Main does this).
        try
        {
            var instanceField = gameRunnerType.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceField != null)
            {
                instanceField.SetValue(null, gameRunnerInstance);
                Console.WriteLine("[+] GameRunner.instance set");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not set GameRunner.instance: {ex.Message}");
        }

        // 9. Initialize critical Game1 static fields (since .cctor is patched to no-op).
        //    Game1.log is accessed by Instance_Initialize — without it, NRE.
        try
        {
            InitializeGame1Statics(sdvAsm);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Could not init Game1 statics: {ex.Message}");
        }

        return (Game?)gameRunnerInstance;
    }

    /// <summary>
    /// Initialize critical Game1 static fields that the .cctor would have set.
    /// We only set fields that are accessed before LoadContent — others will
    /// be set by the game's own init code.
    /// </summary>
    private static void InitializeGame1Statics(Assembly sdvAsm)
    {
        var game1Type = sdvAsm.GetType("StardewValley.Game1");
        if (game1Type == null) return;

        // Set Game1.log to DefaultLogger (implements IGameLogger)
        var logField = game1Type.GetField("log", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (logField != null)
        {
            var logType = logField.FieldType;
            if (logType.IsInterface)
            {
                // Find a concrete implementor in the SDV assembly
                var defaultLoggerType = sdvAsm.GetType("StardewValley.Logging.DefaultLogger");
                if (defaultLoggerType != null)
                {
                    try
                    {
                        // DefaultLogger(bool, bool) — pass false, false
                        var logger = Activator.CreateInstance(defaultLoggerType, false, false);
                        logField.SetValue(null, logger);
                        Console.WriteLine("[+] Game1.log set to DefaultLogger(false,false)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[WARN] Could not create DefaultLogger: " + ex.Message);
                    }
                }
            }
            else
            {
                try
                {
                    var logger = Activator.CreateInstance(logType);
                    logField.SetValue(null, logger);
                    Console.WriteLine("[+] Game1.log set to stub instance");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[WARN] Could not create Game1.log stub: " + ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// Read bisect mode from URL query param (?bisect=N) via JS interop.
    /// Used for debugging the GameRunner..ctor() Mono assertion. Returns 0 if not set.
    /// (Can't use NavigationManager.Uri because System.Uri.get_Query was trimmed.)
    /// </summary>
    private async Task<int> ReadBisectModeFromUrlAsync()
    {
        try
        {
            var bisectStr = await JsRuntime.InvokeAsync<string>("eval", "new URLSearchParams(location.search).get('bisect') || '0'");
            if (int.TryParse(bisectStr, out var mode) && mode >= 0 && mode <= 9)
                return mode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] ReadBisectModeFromUrl failed: {ex.Message}");
        }
        return 0;
    }

    /// <summary>
    /// Pre-set Program._sdk = new NullSDKHelper() to bypass Steam/Galaxy SDK init.
    /// Without this, Program.get_sdk() tries new SteamHelper() first, which fails
    /// because Steamworks.NET has native deps unavailable in WASM.
    /// </summary>
    private static void PreSetNullSdk(Assembly sdvAsm)
    {
        var programType = sdvAsm.GetType("StardewValley.Program");
        if (programType == null)
        {
            Console.WriteLine("[WARN] StardewValley.Program type not found");
            return;
        }

        var sdkField = programType.GetField("_sdk", BindingFlags.NonPublic | BindingFlags.Static);
        if (sdkField == null)
        {
            Console.WriteLine("[WARN] Program._sdk field not found");
            return;
        }

        var nullSdkType = sdvAsm.GetType("StardewValley.SDKs.NullSDKHelper");
        if (nullSdkType == null)
        {
            Console.WriteLine("[WARN] StardewValley.SDKs.NullSDKHelper type not found");
            return;
        }

        var nullSdk = Activator.CreateInstance(nullSdkType);
        sdkField.SetValue(null, nullSdk);
        Console.WriteLine($"[+] Program._sdk = NullSDKHelper (bypass Steam/Galaxy SDK)");
    }
}
