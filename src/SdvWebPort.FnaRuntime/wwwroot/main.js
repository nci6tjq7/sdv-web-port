// SDV WASM Runtime - main.js
// Bootstraps the .NET WASM runtime and provides canvas/input to FNA

let dotnetInstance = null;
let canvas = null;
let ctx = null;

const SDV = {
    canvas: null,
    ctx: null,
    input: {},

    async init() {
        console.log("[SDV] Initializing runtime...");
        canvas = document.getElementById('canvas');
        if (!canvas) {
            console.error("[SDV] Canvas element not found!");
            return;
        }
        canvas.width = 1280;
        canvas.height = 720;
        ctx = canvas.getContext('webgl2') || canvas.getContext('webgl');
        if (!ctx) {
            console.error("[SDV] WebGL not supported!");
            return;
        }
        SDV.canvas = canvas;
        SDV.ctx = ctx;
        console.log("[SDV] Canvas ready:", canvas.width, "x", canvas.height);

        // Set up input handlers (passive where possible to avoid violations)
        SDV.setupInput();

        // Load .NET WASM runtime
        console.log("[SDV] Loading .NET runtime...");
        try {
            const { dotnet } = await import('./_framework/dotnet.js');
            console.log("[SDV] dotnet.js loaded, creating instance...");
            dotnetInstance = await dotnet.create();
            console.log("[SDV] .NET runtime loaded");
            console.log("[SDV] dotnetInstance keys:", Object.keys(dotnetInstance));
            if (dotnetInstance.getAssemblyExports) {
                console.log("[SDV] getAssemblyExports available");
            } else {
                console.log("[SDV] getAssemblyExports NOT available - using Blazor boot mode");
            }
        } catch (e) {
            console.error("[SDV] Failed to load .NET runtime:", e);
            return;
        }

        // Call managed Main - BlazorWebAssembly auto-boots via Program.Main
        // We don't need to call it manually; the runtime invokes it.
        // But we need to set up the JS interop first.
        try {
            // For BlazorWebAssembly, the runtime auto-invokes Program.Main
            // We just need to make sure our JS interop is ready
            console.log("[SDV] .NET runtime booted. Waiting for SDV to start...");
            // The runtime will call SDV.init() from C# via JSImport
        } catch (e) {
            console.error("[SDV] Error:", e);
        }
    },

    setupInput() {
        // Keyboard
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
        // Mouse
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
        // Touch (mobile) - use passive: true to avoid violation warnings
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

    // Called from C# via JSImport to signal that runtime is ready
    onReady() {
        console.log("[SDV] C# runtime signaled ready");
        document.getElementById('loading').style.display = 'none';
    },

    // Log from C# (for debugging)
    log(msg) {
        console.log("[C#]", msg);
    },

    // Error from C#
    error(msg) {
        console.error("[C#]", msg);
    },

    // Get canvas element for FNA
    getCanvas() {
        return canvas;
    }
};

// Expose to .NET
globalThis.SDV = SDV;

// Auto-init on page load
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => SDV.init());
} else {
    SDV.init();
}
