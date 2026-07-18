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
            // SW should be registered by inline script in index.html.
            // If crossOriginIsolated is still false, the SW is still activating
            // — wait briefly and reload.
            console.log("[SDV] crossOriginIsolated=false, waiting for SW...");
            if (navigator.serviceWorker.controller) {
                // SW is controlling but COOP/COEP not set — reload to re-fetch through SW
                window.location.reload();
                return;
            }
            // Wait for SW to be ready, then reload
            navigator.serviceWorker.ready.then(() => {
                window.location.reload();
            });
            return;
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

        console.log("[SDV] Loading .NET runtime...");
        try {
            const { dotnet } = await import('./_framework/dotnet.js');
            console.log("[SDV] dotnet.js loaded, creating instance...");

            // With -sOFFSCREENCANVAS_SUPPORT in EmccExtraLDFlags (csproj),
            // emscripten's SDL3 video driver automatically transfers the canvas
            // to the deputy worker. No manual canvas transfer hack needed.
            //
            // With -sMIN_WEBGL_VERSION=2, emscripten creates WebGL 2.0 context
            // (= OpenGL ES 3.0), which FNA3D's ES3 auto-detection requires.
            //
            // Reference: celeste-wasm pattern (MercuryWorkshop/celeste-wasm)
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

    // Synchronous HTTP fetch for Content files.
    // Used by HttpTitleContainer.OpenStream via JS interop.
    // XMLHttpRequest in synchronous mode is blocking but doesn't deadlock
    // the WASM single thread (unlike HttpClient.GetAsync().GetAwaiter().GetResult()).
    fetchSync(url) {
        try {
            const xhr = new XMLHttpRequest();
            xhr.open('GET', url, false); // false = synchronous
            xhr.responseType = 'arraybuffer';
            xhr.send();
            if (xhr.status === 200) {
                return new Uint8Array(xhr.response);
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
