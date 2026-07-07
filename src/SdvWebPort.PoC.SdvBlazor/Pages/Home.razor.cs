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
    private int _tickErrorCount;
    private int _tickSuccessCount;

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
            try
            {
                _game.Tick();
                if (_tickErrorCount == 0 && _tickSuccessCount < 5)
                {
                    Console.WriteLine("[Tick] Tick() succeeded (" + _tickSuccessCount + ")");
                    _tickSuccessCount++;
                }
            }
            catch (System.TypeLoadException ex)
            {
                if (_tickErrorCount % 60 == 0)
                    Console.WriteLine("[Tick] TypeLoadException (suppressed): " + ex.Message + "\n  Stack: " + ex.StackTrace);
                _tickErrorCount++;
            }
            catch (Exception ex)
            {
                if (_tickErrorCount < 3 || _tickErrorCount % 60 == 0)
                {
                    Console.WriteLine("[Tick] Error (suppressed): " + ex.GetType().Name + ": " + ex.Message);
                    Console.WriteLine("  Stack: " + ex.StackTrace);
                }
                _tickErrorCount++;
            }
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
            Console.WriteLine($"[+] Running Cecil FileSystem rewriter (File/Directory → SdvFileShim)...");
            rewrittenBytes = SdvWebPort.Rewriter.SdvFileSystemRewriter.Rewrite(refRewritten);
            Console.WriteLine($"[+] Final rewritten: {rewrittenBytes.Length:N0} bytes");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] Rewriter threw: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"    Stack: {ex.StackTrace}");
            // Rewriter failed during Write (Cecil can't resolve nested types).
            // Retry with CustomMetadataResolver's dummy type fallback.
            try
            {
                Console.WriteLine("[+] Retrying rewriter (with dummy type fallback)...");
                SdvWebPort.Rewriter.SdvAssemblyRefRewriter.SkipMethodSignatureRewrite = false;
                rewrittenBytes = SdvWebPort.Rewriter.SdvAssemblyRefRewriter.Rewrite(sdvBytes);
                Console.WriteLine($"[+] Retry succeeded: {rewrittenBytes.Length:N0} bytes");
            }
            catch (Exception ex2)
            {
                Console.WriteLine($"[FAIL] Retry also failed: {ex2.Message}");
                // Last resort: skip method signature rewrite
                try
                {
                    Console.WriteLine("[+] Last resort: skip method signature rewrite...");
                    SdvWebPort.Rewriter.SdvAssemblyRefRewriter.SkipMethodSignatureRewrite = true;
                    rewrittenBytes = SdvWebPort.Rewriter.SdvAssemblyRefRewriter.Rewrite(sdvBytes);
                    Console.WriteLine($"[+] Last resort succeeded: {rewrittenBytes.Length:N0} bytes");
                }
                catch (Exception ex3)
                {
                    Console.WriteLine($"[FAIL] Last resort also failed: {ex3.Message}");
                    rewrittenBytes = sdvBytes;
                }
            }
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
            // Log ALL nested inner exceptions
            var inner = ex;
            int depth = 0;
            while (inner.InnerException != null && depth < 5)
            {
                inner = inner.InnerException;
                depth++;
                Console.WriteLine($"    Inner[{depth}]: {inner.GetType().Name}: {inner.Message}");
                Console.WriteLine($"    Inner[{depth}] Stack: {inner.StackTrace}");
            }
            // Also check TypeInitializationException's special _typeName field
            if (ex is System.TypeInitializationException tie)
            {
                Console.WriteLine($"    TypeInit Stack: {tie.StackTrace}");
            }
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
            var defaultLoggerType = sdvAsm.GetType("StardewValley.Logging.DefaultLogger");
            if (defaultLoggerType != null)
            {
                try
                {
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

        // Set Game1.random = new Random()
        var randomField = game1Type.GetField("random", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (randomField != null && randomField.GetValue(null) == null)
        {
            try
            {
                randomField.SetValue(null, new Random());
                Console.WriteLine("[+] Game1.random set to new Random()");
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1.random: " + ex.Message); }
        }

        // Set Game1.onScreenMenus = new List<IClickableMenu>()
        var osmField = game1Type.GetField("onScreenMenus", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (osmField != null && osmField.GetValue(null) == null)
        {
            try
            {
                var listType = typeof(System.Collections.Generic.List<>);
                var elementType = sdvAsm.GetType("StardewValley.Menus.IClickableMenu");
                if (elementType != null)
                {
                    var concreteListType = listType.MakeGenericType(elementType);
                    var list = Activator.CreateInstance(concreteListType);
                    osmField.SetValue(null, list);
                    Console.WriteLine("[+] Game1.onScreenMenus set to new List<IClickableMenu>()");
                }
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1.onScreenMenus: " + ex.Message); }
        }

        // Set Game1._shortDayDisplayName = new string[7]
        var sdnField = game1Type.GetField("_shortDayDisplayName", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (sdnField != null && sdnField.GetValue(null) == null)
        {
            try
            {
                sdnField.SetValue(null, new string[7]);
                Console.WriteLine("[+] Game1._shortDayDisplayName set to new string[7]");
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1._shortDayDisplayName: " + ex.Message); }
        }

        // Set Game1.rainDrops = new RainDrop[...]
        var rdField = game1Type.GetField("rainDrops", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (rdField != null && rdField.GetValue(null) == null)
        {
            try
            {
                var rainDropType = sdvAsm.GetType("StardewValley.RainDrop");
                if (rainDropType != null)
                {
                    var array = Array.CreateInstance(rainDropType, 100);
                    for (int i = 0; i < 100; i++)
                        array.SetValue(Activator.CreateInstance(rainDropType), i);
                    rdField.SetValue(null, array);
                    Console.WriteLine("[+] Game1.rainDrops set to new RainDrop[100]");
                }
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1.rainDrops: " + ex.Message); }
        }

        // Set Game1.dynamicPixelRects = new Texture2D[0]
        var dprField = game1Type.GetField("dynamicPixelRects", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (dprField != null && dprField.GetValue(null) == null)
        {
            try
            {
                var textureType = typeof(Microsoft.Xna.Framework.Graphics.Texture2D);
                dprField.SetValue(null, Array.CreateInstance(textureType, 0));
                Console.WriteLine("[+] Game1.dynamicPixelRects set to empty array");
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1.dynamicPixelRects: " + ex.Message); }
        }

        // Set Game1.content = new LocalizedContentManager(serviceProvider, "Content")
        var contentField = game1Type.GetField("content", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (contentField != null && contentField.GetValue(null) == null)
        {
            try
            {
                var lcmType = sdvAsm.GetType("StardewValley.LocalizedContentManager");
                if (lcmType != null)
                {
                    var ctor = lcmType.GetConstructor(new[] { typeof(System.IServiceProvider), typeof(string), typeof(System.Globalization.CultureInfo) });
                    if (ctor != null)
                    {
                        var sp = new SimpleServiceProvider();
                        var lcm = ctor.Invoke(new object?[] { sp, "Content", System.Globalization.CultureInfo.InvariantCulture });
                        contentField.SetValue(null, lcm);
                        Console.WriteLine("[+] Game1.content set to new LocalizedContentManager(sp, Content, InvariantCulture)");
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1.content: " + ex.Message); }
        }

        // Initialize audio-related fields (updateMusic NRE)
        // Game1.musicCategory, ambientCategory — these are IAudioCategory interfaces
        // Game1.soundBank — ISoundBank interface
        // For now, just set them to null-safe stubs or skip
        var musicCatField = game1Type.GetField("musicCategory", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (musicCatField != null && musicCatField.GetValue(null) == null)
        {
            Console.WriteLine("[+] Game1.musicCategory is null — updateMusic will NRE (expected)");
        }

        // Set Game1.currentGameTime = new GameTime()
        var cgtField = game1Type.GetField("currentGameTime", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (cgtField != null && cgtField.GetValue(null) == null)
        {
            try
            {
                var gtType = typeof(Microsoft.Xna.Framework.GameTime);
                var gt = Activator.CreateInstance(gtType);
                cgtField.SetValue(null, gt);
                Console.WriteLine("[+] Game1.currentGameTime set to new GameTime()");
            }
            catch (Exception ex) { Console.WriteLine("[WARN] Game1.currentGameTime: " + ex.Message); }
        }
    }

    /// <summary>
    /// Minimal IServiceProvider that returns null for all service requests.
    /// Used to construct LocalizedContentManager.
    /// </summary>
    private class SimpleServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
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
