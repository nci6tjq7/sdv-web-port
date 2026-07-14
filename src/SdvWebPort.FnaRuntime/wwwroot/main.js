// SDV WASM Runtime - main.js
// Uses Microsoft.NET.Sdk.WebAssembly pattern
// The SDK auto-invokes Program.Main on dotnet.create()

let canvas = null;
let dotnetInstance = null;

// Register COOP/COEP service worker for SharedArrayBuffer support
async function registerSW() {
    if (!('serviceWorker' in navigator)) return false;
    try {
        const reg = await navigator.serviceWorker.register('./coop-coep-sw.js');
        console.log("[SDV] COOP/COEP service worker registered");
        // Wait for the service worker to be active
        if (!navigator.serviceWorker.controller) {
            console.log("[SDV] Waiting for service worker to activate...");
            // Force activation
            await navigator.serviceWorker.ready;
            console.log("[SDV] Service worker ready, reloading...");
            window.location.reload();
            return false; // Will reload
        }
        return true;
    } catch(e) {
        console.error("[SDV] Service worker registration failed:", e);
        return false;
    }
}

const SDV = {
    canvas: null,
    input: {},

    async init() {
        console.log("[SDV] Initializing runtime...");
        
        // Check if cross-origin isolated (needed for SharedArrayBuffer)
        if (!window.crossOriginIsolated) {
            console.log("[SDV] Not cross-origin isolated, registering service worker...");
            const ok = await registerSW();
            if (!ok) return;
            if (!window.crossOriginIsolated) {
                console.log("[SDV] Still not isolated after SW, reloading...");
                window.location.reload();
                return;
            }
        }
        console.log("[SDV] Cross-origin isolated:", window.crossOriginIsolated);
        
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

        // Load .NET WASM runtime via dotnet.js
        // The SDK auto-invokes Program.Main on create()
        console.log("[SDV] Loading .NET runtime...");
        try {
            const { dotnet } = await import('./_framework/dotnet.js');
            console.log("[SDV] dotnet.js loaded, creating instance...");
            // dotnet.create() starts the runtime and calls Program.Main automatically
            dotnetInstance = await dotnet.create();
            console.log("[SDV] .NET runtime loaded and Main invoked");
            // Program.Main runs SDV and calls OnReady() via JSImport
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
