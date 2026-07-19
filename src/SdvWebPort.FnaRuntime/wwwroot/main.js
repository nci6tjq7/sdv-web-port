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
        if (!window.crossOriginIsolated) {
            console.log("[SDV] crossOriginIsolated=false (expected without COOP/COEP SW). Continuing anyway...");
        }

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
        try {
            // Fetch the file manifest
            const manifestResp = await fetch('/deps/Content/ContentHashes.json');
            if (manifestResp.ok) {
                const manifest = await manifestResp.json();
                console.log("[SDV] Got Content manifest");
            }
        } catch (e) {
            console.warn("[SDV] No Content manifest, will fetch on demand");
        }

        // Preload critical files for title screen
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
                const resp = await fetch('/deps/' + file);
                if (resp.ok) {
                    const data = new Uint8Array(await resp.arrayBuffer());
                    globalThis.__contentCache.set('/deps/' + file, data);
                    console.log(`[SDV] Preloaded: ${file} (${data.length} bytes)`);
                }
            } catch (e) {
                console.warn(`[SDV] Failed to preload: ${file}`);
            }
        }
        console.log(`[SDV] Preloaded ${globalThis.__contentCache.size} files`);

        console.log("[SDV] Loading .NET runtime...");
        try {
            const { dotnet } = await import('./_framework/dotnet.js');
            console.log("[SDV] dotnet.js loaded, creating instance...");

            console.log("[SDV] Creating .NET runtime instance...");
            dotnetInstance = await dotnet.create();
            console.log("[SDV] .NET runtime loaded");
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

    // Synchronous fetch for Content files.
    // First checks the preload cache (populated via async fetch before runtime start).
    // If not in cache, falls back to sync XHR (may fail in COEP pages).
    fetchSync(url) {
        // Check cache first
        if (globalThis.__contentCache && globalThis.__contentCache.has(url)) {
            console.log('[SDV] fetchSync cache hit:', url);
            return globalThis.__contentCache.get(url);
        }
        // Fall back to sync XHR (may fail in COEP pages)
        try {
            const xhr = new XMLHttpRequest();
            xhr.open('GET', url, false);
            xhr.overrideMimeType('text/plain; charset=x-user-defined');
            xhr.send();
            if (xhr.status === 200) {
                const text = xhr.responseText;
                const bytes = new Uint8Array(text.length);
                for (let i = 0; i < text.length; i++) {
                    bytes[i] = text.charCodeAt(i) & 0xff;
                }
                // Cache for future use
                if (globalThis.__contentCache) {
                    globalThis.__contentCache.set(url, bytes);
                }
                return bytes;
            }
            console.warn('[SDV] fetchSync failed:', url, 'status:', xhr.status);
            return null;
        } catch (e) {
            console.warn('[SDV] fetchSync error:', url, e.message);
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
