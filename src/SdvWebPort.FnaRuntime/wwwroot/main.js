// SDV WASM Runtime - main.js
// Threaded mode with FULLY patched .NET 10 runtime + r58Playz fnalibs
// Requires COOP/COEP headers (service worker on GitHub Pages)
// SW registration is done inline in index.html (before this module loads)
// to avoid race conditions with ES module loading.

let canvas = null;
let dotnetInstance = null;

const SDV = {
    canvas: null,
    input: {},

    async init() {
        console.log("[SDV] Initializing runtime...");

        // CRITICAL: Wait for Service Worker to be active before proceeding.
        // The .NET WASM runtime (threaded mode) requires SharedArrayBuffer,
        // which requires COOP/COEP headers. On GitHub Pages, these headers
        // are injected by our Service Worker (coop-coep-sw.js).
        //
        // Race condition: main.js (ES module) can execute before the SW
        // is active. If we start the .NET runtime before COOP/COEP is set,
        // dotnet.create() will fail with "SharedArrayBuffer is not enabled".
        //
        // Fix: wait for navigator.serviceWorker.ready, then check
        // crossOriginIsolated. If still false after SW ready, reload the
        // page (the SW will intercept the reload and add COOP/COEP headers).
        if ('serviceWorker' in navigator) {
            console.log("[SDV] Waiting for Service Worker to be ready...");
            try {
                await navigator.serviceWorker.ready;
                console.log("[SDV] Service Worker is ready.");
            } catch (e) {
                console.warn("[SDV] SW ready failed:", e.message);
            }

            if (!window.crossOriginIsolated) {
                console.log("[SDV] crossOriginIsolated=false after SW ready. Reloading page to activate COOP/COEP...");
                // Give the SW time to claim the page
                await new Promise(r => setTimeout(r, 500));
                if (!navigator.serviceWorker.controller) {
                    // SW registered but not controlling this page yet.
                    // A reload will let it intercept the navigation request.
                    window.location.reload();
                    return; // Stop init — page will reload
                }
                // SW is controlling but crossOriginIsolated is still false.
                // This shouldn't happen — but if it does, continue anyway
                // (the runtime will fail, but at least we'll see the error).
                console.warn("[SDV] SW is controller but crossOriginIsolated still false. Continuing anyway...");
            } else {
                console.log("[SDV] crossOriginIsolated=true. COOP/COEP headers are active.");
            }
        } else {
            console.warn("[SDV] No Service Worker support. Threads will fail.");
        }

        // Compute base path for Content/ requests.
        // The site is hosted at a subpath (e.g. /sdv-web-port/), so absolute
        // paths like "/deps/..." would resolve to the domain root, returning 404.
        // We use document.baseURI which respects the <base> tag and works on
        // both root-hosted and subpath-hosted deployments.
        const baseURI = document.baseURI.replace(/[^/]*$/, '');
        SDV.depsBase = baseURI + 'deps/';
        console.log("[SDV] depsBase:", SDV.depsBase);

        canvas = document.getElementById('canvas');
        if (!canvas) {
            console.error("[SDV] Canvas element not found!");
            return;
        }
        canvas.width = 1280;
        canvas.height = 720;
        SDV.canvas = canvas;
        console.log("[SDV] Canvas ready:", canvas.width, "x", canvas.height);

        SDV.setupInput();

        // Preload essential Content files via async fetch (works with COEP).
        // Sync XMLHttpRequest can't read SW-wrapped responses in COEP pages.
        // Solution: async fetch all files BEFORE starting .NET runtime,
        // cache them in globalThis.__contentCache. fetchSync() reads from cache.
        console.log("[SDV] Preloading Content files...");
        globalThis.__contentCache = new Map();
        globalThis.__manifestJson = null;
        try {
            // Fetch the ContentHashes.json manifest and store as string.
            // SDV's ContentHashParser.ParseFromFile calls File.ReadAllText to load
            // this file, which fails in WASM. We preload it here and expose via
            // globalThis.SDV.getManifestJson() for the patched ParseFromFile to use.
            const manifestResp = await fetch(SDV.depsBase + 'Content/ContentHashes.json');
            if (manifestResp.ok) {
                globalThis.__manifestJson = await manifestResp.text();
                console.log("[SDV] Got Content manifest (" + globalThis.__manifestJson.length + " chars)");
            }
        } catch (e) {
            console.warn("[SDV] No Content manifest, will fetch on demand");
        }

        // Preload critical files for title screen.
        // NOTE: SDV/FNA expects lowercase filenames (e.g. smallFont.xnb), but
        // the actual game ships them with mixed case (SmallFont.xnb). On
        // case-sensitive filesystems (Linux/GitHub Pages), this matters.
        // We try lowercase first, then uppercase first letter as fallback.
        const criticalFiles = [
            'Content/XACT/FarmerSounds.xgs',
            'Content/XACT/FarmerSounds.xwb',
            'Content/Data/BigCraftables.xnb',
            'Content/Data/CraftingRecipes.xnb',
            'Content/Data/NPCDispositions.xnb',
            'Content/Data/ObjectInformation.xnb',
            'Content/Fonts/smallFont.xnb',
            'Content/LooseSprites/Cursors.xnb',
            'Content/LooseSprites/clouds.xnb',
            'Content/LooseSprites/titleButtons.xnb',
            'Content/Menus/TitleMenu.xnb',
        ];

        for (const file of criticalFiles) {
            try {
                // Try the requested path first
                let resp = await fetch(SDV.depsBase + file);
                // If 404, try case-insensitive variants
                if (!resp.ok) {
                    // Try with first letter of basename capitalized
                    const parts = file.split('/');
                    const last = parts.pop();
                    const cap = last.charAt(0).toUpperCase() + last.slice(1);
                    const variant = parts.join('/') + '/' + cap;
                    resp = await fetch(SDV.depsBase + variant);
                }
                if (resp.ok) {
                    const data = new Uint8Array(await resp.arrayBuffer());
                    // Cache under BOTH the requested path and the actual URL
                    // so fetchSync can find it regardless of which key it uses
                    globalThis.__contentCache.set(file, data);
                    globalThis.__contentCache.set(SDV.depsBase + file, data);
                    console.log(`[SDV] Preloaded: ${file} (${data.length} bytes)`);
                } else {
                    console.warn(`[SDV] Preload miss: ${file} (status=${resp.status})`);
                }
            } catch (e) {
                console.warn(`[SDV] Failed to preload: ${file}`, e.message);
            }
        }
        console.log(`[SDV] Preloaded ${globalThis.__contentCache.size} entries`);

        console.log("[SDV] Loading .NET runtime...");
        try {
            const { dotnet } = await import('./_framework/dotnet.js');
            console.log("[SDV] dotnet.js loaded, creating instance...");

            console.log("[SDV] Creating .NET runtime instance...");
            dotnetInstance = await dotnet.create();
            globalThis.__dotnetInstance = dotnetInstance;
            console.log("[SDV] .NET runtime loaded");

            // C# Program.Main will block in RunPlatformMainLoop which runs a
            // C#-driven loop: while(true) { RunOneFrame(); Thread.Sleep(0); }
            // No JS callback needed — the loop is entirely C#-driven.
            // The canvas was transferred to the worker via celeste-wasm sed
            // patch, so WebGL calls happen on the worker.

            console.log("[SDV] Invoking runMain...");
            const exitCode = await dotnetInstance.runMain("SdvWebPort.FnaRuntime", []);
            console.log("[SDV] runMain returned:", exitCode);
        } catch (e) {
            console.error("[SDV] Failed to load .NET runtime:", e);
            SDV.error("Failed to start: " + e.message + "\n" + e.stack);
        }
    },

    setupInput() {
        canvas.tabIndex = 0;
        canvas.focus();
        canvas.addEventListener('keydown', (e) => {
            SDV.input[e.code] = true;
            e.preventDefault();
        });
        canvas.addEventListener('keyup', (e) => {
            SDV.input[e.code] = false;
            e.preventDefault();
        });
        canvas.addEventListener('mousedown', (e) => {
            SDV.input['mouse' + e.button] = true;
            e.preventDefault();
        });
        canvas.addEventListener('mouseup', (e) => {
            SDV.input['mouse' + e.button] = false;
            e.preventDefault();
        });
        canvas.addEventListener('mousemove', (e) => {
            const rect = canvas.getBoundingClientRect();
            SDV.mouseX = Math.round((e.clientX - rect.left) * (canvas.width / rect.width));
            SDV.mouseY = Math.round((e.clientY - rect.top) * (canvas.height / rect.height));
        });
        canvas.addEventListener('touchstart', (e) => {
            const t = e.touches[0];
            const rect = canvas.getBoundingClientRect();
            SDV.mouseX = Math.round((t.clientX - rect.left) * (canvas.width / rect.width));
            SDV.mouseY = Math.round((t.clientY - rect.top) * (canvas.height / rect.height));
            SDV.input['mouse0'] = true;
        }, { passive: true });
        canvas.addEventListener('touchmove', (e) => {
            const t = e.touches[0];
            const rect = canvas.getBoundingClientRect();
            SDV.mouseX = Math.round((t.clientX - rect.left) * (canvas.width / rect.width));
            SDV.mouseY = Math.round((t.clientY - rect.top) * (canvas.height / rect.height));
        }, { passive: true });
        canvas.addEventListener('touchend', () => {
            SDV.input['mouse0'] = false;
        }, { passive: true });
        console.log("[SDV] Input handlers attached");
    },

    onReady() {
        console.log("[SDV] C# runtime signaled ready");
        const loading = document.getElementById('loading');
        if (loading) loading.style.display = 'none';
        const status = document.getElementById('status');
        if (status) status.textContent = 'Game running';
    },

    log(msg) {
        console.log("[C#]", msg);
    },

    // Returns the preloaded ContentHashes.json manifest as a string.
    // Called by the patched ContentHashParser.ParseFromFile via JS interop.
    // Returns null if the manifest wasn't preloaded.
    getManifestJson() {
        console.log('[SDV] getManifestJson called, manifest=' + (globalThis.__manifestJson ? globalThis.__manifestJson.length + ' chars' : 'null'));
        return globalThis.__manifestJson;
    },

    // Sets up the main game loop using requestAnimationFrame.
    // Called by C# (via JSImport) to register a RunOneFrame callback.
    // Replaces the original [DllImport("__Native")] emscripten_set_main_loop
    // which fails with DllNotFoundException in WASM.
    //
    // C# calls SDV.setMainLoopCallback(callback) which stores the callback.
    // JS then drives requestAnimationFrame, calling the callback each frame.
    setMainLoopCallback(cb) {
        console.log('[SDV] setMainLoopCallback called with:', typeof cb);
        globalThis.__sdvMainLoopCallback = cb;
        if (globalThis.__sdvMainLoopRunning) {
            console.log('[SDV] Main loop already running, just registered callback');
            return;
        }
        globalThis.__sdvMainLoopRunning = true;

        function frame() {
            try {
                if (globalThis.__sdvMainLoopCallback) {
                    globalThis.__sdvMainLoopCallback();
                }
            } catch (e) {
                console.error('[SDV] Main loop error:', e);
            }
            requestAnimationFrame(frame);
        }
        requestAnimationFrame(frame);
    },

    // Legacy setMainLoop (unused — kept for backwards compat).
    // The new approach uses setMainLoopCallback which is called from C#.
    setMainLoop() {
        console.log('[SDV] setMainLoop called (legacy, no-op — using setMainLoopCallback)');
    },

    // Synchronous fetch for Content files.
    // First checks the preload cache (populated via async fetch before runtime start).
    // If not in cache, falls back to sync XHR (may fail in COEP pages).
    fetchSync(url) {
        // Normalize URL: C# passes "/deps/..." (absolute), but the site is at
        // a subpath. Convert to the proper depsBase-relative URL.
        let normalized = url;
        if (url.startsWith('/deps/')) {
            normalized = SDV.depsBase + url.substring('/deps/'.length);
        } else if (url.startsWith('deps/')) {
            normalized = SDV.depsBase + url.substring('deps/'.length);
        } else if (url.startsWith('/')) {
            normalized = SDV.depsBase + url.substring(1);
        }
        // Also derive the path-only key for cache lookup
        const pathKey = url.startsWith('/deps/') ? url.substring('/deps/'.length)
                     : url.startsWith('deps/')  ? url.substring('deps/'.length)
                     : url;

        // Check cache by all possible keys
        if (globalThis.__contentCache) {
            if (globalThis.__contentCache.has(normalized)) {
                console.log('[SDV] fetchSync cache hit (normalized):', normalized);
                return globalThis.__contentCache.get(normalized);
            }
            if (globalThis.__contentCache.has(pathKey)) {
                console.log('[SDV] fetchSync cache hit (pathKey):', pathKey);
                return globalThis.__contentCache.get(pathKey);
            }
            if (globalThis.__contentCache.has(url)) {
                console.log('[SDV] fetchSync cache hit (raw):', url);
                return globalThis.__contentCache.get(url);
            }
        }
        // Fall back to sync XHR (may fail in COEP pages)
        try {
            const xhr = new XMLHttpRequest();
            xhr.open('GET', normalized, false);
            xhr.overrideMimeType('text/plain; charset=x-user-defined');
            xhr.send();
            if (xhr.status === 200) {
                const text = xhr.responseText;
                const bytes = new Uint8Array(text.length);
                for (let i = 0; i < text.length; i++) {
                    bytes[i] = text.charCodeAt(i) & 0xff;
                }
                // Cache for future use under all keys
                if (globalThis.__contentCache) {
                    globalThis.__contentCache.set(normalized, bytes);
                    globalThis.__contentCache.set(pathKey, bytes);
                    globalThis.__contentCache.set(url, bytes);
                }
                return bytes;
            }
            console.warn('[SDV] fetchSync failed:', normalized, 'status:', xhr.status);
            return null;
        } catch (e) {
            console.warn('[SDV] fetchSync error:', normalized, e.message);
            return null;
        }
    },

    error(msg) {
        console.error("[C#]", msg);
        const el = document.getElementById('error-log');
        if (el) {
            el.textContent += msg + '\n';
            el.style.display = 'block';
        }
    },

    getCanvas() {
        return canvas;
    }
};

globalThis.SDV = SDV;

if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => SDV.init());
} else {
    SDV.init();
}
