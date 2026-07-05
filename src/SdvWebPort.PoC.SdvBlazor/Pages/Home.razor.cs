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
        Console.WriteLine("[+] Pre-fetching SDV dependencies + KNI assemblies...");
        var deps = new Dictionary<string, byte[]>();
        // SDV-specific dependencies that need explicit loading (not in Blazor .wasm bundle
        // or have version mismatches). Pre-fetch their .dll bytes.
        // Note: HashFunction uses v1.8.2.2 (matching SDV's AssemblyRef), NOT v2.0.0.
        foreach (var depName in new[] { "xTile", "StardewValley.GameData",
            "System.Data.HashFunction.xxHash", "System.Data.HashFunction.Interfaces",
            "System.Data.HashFunction.Core" })
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
        // KNI assemblies — loaded into the SAME ALC as SDV so TypeForwardedTo resolution works.
        // Raw .dll files are copied to wwwroot root during publish (NOT _framework/ —
        // Blazor converts _framework/*.dll to .wasm format which can't be loaded via ALC).
        foreach (var kniName in new[] {
            "Xna.Framework", "Xna.Framework.Game", "Xna.Framework.Graphics",
            "Xna.Framework.Content", "Xna.Framework.Input",
            "Xna.Framework.Audio", "Xna.Framework.Media",
            "Xna.Framework.Devices", "Xna.Framework.Storage",
        })
        {
            try
            {
                var baseUri = new Uri(HostEnv.BaseAddress);
                var kniUri = new Uri(baseUri, $"{kniName}.dll");
                var kniBytes = await _http!.GetByteArrayAsync(kniUri);
                Console.WriteLine($"[+] Fetched KNI {kniName}.dll: {kniBytes.Length:N0} bytes");
                deps[kniName] = kniBytes;
            }
            catch (Exception ex) { Console.WriteLine($"[!] Could not fetch KNI {kniName}.dll: {ex.Message}"); }
        }

        // 6. Load SDV deps (xTile, GameData) into default ALC, then load SDV.
        //    KNI assemblies already loaded in step 2.
        Assembly sdvAsm;
        try
        {
            // Load ALL pre-fetched deps (xTile, GameData, HashFunction, KNI) into default ALC
            foreach (var (depName, depBytes) in deps)
            {
                Console.WriteLine($"[+] Loading {depName} into default ALC...");
                try { AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(depBytes)); }
                catch (Exception ex) { Console.WriteLine($"[!] Failed loading {depName}: {ex.Message}"); }
            }

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
        // Debug: list all loaded assemblies containing "Xna" or "MonoGame"
        Console.WriteLine("[+] === Loaded Xna/MonoGame assemblies ===");
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name?.Contains("Xna") == true || a.GetName().Name?.Contains("MonoGame") == true)
            .OrderBy(a => a.GetName().Name))
        {
            Console.WriteLine($"    {a.FullName} (ALC: {AssemblyLoadContext.GetLoadContext(a)?.Name ?? "default"})");
        }
        Console.WriteLine("[+] === End assembly list ===");

        // Debug: check if WaveBank exists in loaded Xna.Framework.Audio
        var audioAsm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Xna.Framework.Audio");
        if (audioAsm != null)
        {
            Console.WriteLine($"[+] Xna.Framework.Audio loaded: {audioAsm.FullName}");
            var waveBankType = audioAsm.GetType("Microsoft.Xna.Framework.Audio.WaveBank");
            Console.WriteLine($"[+] WaveBank type found: {waveBankType != null}");
            if (waveBankType == null)
            {
                // List all types in the assembly
                Console.WriteLine("[+] Types in Xna.Framework.Audio:");
                Type[] audioTypes;
                try { audioTypes = audioAsm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { audioTypes = ex.Types.Where(t => t != null).ToArray()!; }
                foreach (var t in audioTypes.Where(t => t.Name.Contains("Wave") || t.Name.Contains("Sound") || t.Name.Contains("Audio")).Take(20))
                    Console.WriteLine($"    - {t.FullName}");
                Console.WriteLine($"    (total {audioTypes.Length} types)");
            }
        }
        else
        {
            Console.WriteLine("[!] Xna.Framework.Audio NOT loaded!");
        }

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
            // Drill deeper for TypeInitializationException
            var deepInner = inner.InnerException;
            int depth = 0;
            while (deepInner != null && depth < 5)
            {
                depth++;
                Console.WriteLine($"    Deep[{depth}]: {deepInner.GetType().Name}: {deepInner.Message}");
                Console.WriteLine($"    Deep[{depth}] Stack: {deepInner.StackTrace?.Split('\n')[0]}");
                deepInner = deepInner.InnerException;
            }
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

        // Framework assemblies: redirect facades to CoreLib, return runtime versions for others
        if (name.StartsWith("System.") || name == "System" || name == "mscorlib" || name == "netstandard")
        {
            // First try CoreLib (for facade types like System.Guid, System.IComparable)
            var coreLib = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "System.Private.CoreLib");
            // For facade assemblies (System.Runtime etc.), return CoreLib directly
            var facadeSet = new HashSet<string> {
                "System.Runtime", "System.Runtime.Extensions", "System.Runtime.InteropServices",
                "System.Runtime.Loader", "System.Diagnostics.Debug", "System.Diagnostics.StackTrace",
                "System.Diagnostics.Process", "System.Globalization", "System.Resources.ResourceManager",
                "System.Reflection", "System.Reflection.Primitives", "System.Reflection.Emit",
                "System.Reflection.Emit.Lightweight", "System.Reflection.Emit.ILGeneration",
                "System.Text.Encoding", "System.Text.Encoding.Extensions", "System.Threading.ThreadPool",
                // Phase 2.8: these are also facades that forward to System.Private.Xml / System.Private.CoreLib
                "System.Xml.ReaderWriter", "System.Xml.XmlSerializer", "System.Threading",
                "System.Threading.Thread", "System.Threading.Tasks", "System.Linq",
                "System.Linq.Expressions", "System.Net.Primitives", "System.Net.NameResolution",
                "System.Console", "System.ComponentModel", "System.ObjectModel",
                "System.Collections.Concurrent",
            };
            if (facadeSet.Contains(name) && coreLib != null)
                return coreLib;

            // For non-facade System.* (System.Collections, System.Xml.*, System.Linq, etc.)
            // return the runtime's already-loaded version (matching by name, ignoring version)
            var runtimeAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);
            if (runtimeAsm != null)
            {
                Console.WriteLine($"[SdvLoadContext] {name} → runtime {runtimeAsm.GetName().Version}");
                return runtimeAsm;
            }
            // Not found in runtime — try fuzzy match (e.g., System.Xml.ReaderWriter → System.Xml)
            // Blazor WASM bundles some assemblies under different names
            var fuzzyMatch = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name.StartsWith(name) || name.StartsWith(a.GetName().Name));
            if (fuzzyMatch != null)
            {
                Console.WriteLine($"[SdvLoadContext] {name} → fuzzy match {fuzzyMatch.GetName().Name}");
                return fuzzyMatch;
            }
            Console.WriteLine($"[SdvLoadContext] WARN: {name} not found in runtime — trying CoreLib");
            // If not found by exact name, try CoreLib as fallback
            if (coreLib != null) return coreLib;
        }

        // MonoGame.Framework → facade
        if (name == "MonoGame.Framework")
        {
            var facade = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "MonoGame.Framework");
            if (facade != null) return facade;
        }

        // KNI assemblies (Xna.Framework.*) — load from pre-fetched bytes in THIS ALC
        // (not from the default ALC's runtime versions — TypeForwardedTo requires same ALC)
        if (name.StartsWith("Xna.Framework"))
        {
            if (_loaded.TryGetValue(name, out var cachedKni))
                return cachedKni;
            if (_preloadedDeps.TryGetValue(name, out var kniBytes))
            {
                Console.WriteLine($"[SdvLoadContext] Loading KNI from pre-fetched: {name}");
                var asm = LoadFromStream(new MemoryStream(kniBytes));
                _loaded[name] = asm;
                return asm;
            }
            // Fallback: try runtime's version
            var kniAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);
            if (kniAsm != null)
            {
                Console.WriteLine($"[SdvLoadContext] KNI fallback (runtime): {name}");
                return kniAsm;
            }
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
