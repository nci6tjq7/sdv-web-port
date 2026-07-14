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

        // Set up input handlers
        SDV.setupInput();

        // Load .NET WASM runtime
        console.log("[SDV] Loading .NET runtime...");
        const { dotnet } = await import('./_framework/dotnet.js');
        dotnetInstance = await dotnet.create();
        console.log("[SDV] .NET runtime loaded");

        // Call managed Main
        try {
            const main = dotnetInstance.getAssemblyExports("SdvWebPort.FnaRuntime").SdvWebPort.FnaRuntime.Program.Main;
            console.log("[SDV] Calling Program.Main...");
            // Run in background - Main blocks (Thread.Sleep(Infinite))
            main([]);
        } catch (e) {
            console.error("[SDV] Error calling Main:", e);
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
        // Touch (mobile)
        canvas.addEventListener('touchstart', (e) => {
            const t = e.touches[0];
            const rect = canvas.getBoundingClientRect();
            SDV.mouseX = Math.round((t.clientX - rect.left) * (canvas.width / rect.width));
            SDV.mouseY = Math.round((t.clientY - rect.top) * (canvas.height / rect.height));
            SDV.input['mouse0'] = true;
            e.preventDefault();
        });
        canvas.addEventListener('touchmove', (e) => {
            const t = e.touches[0];
            const rect = canvas.getBoundingClientRect();
            SDV.mouseX = Math.round((t.clientX - rect.left) * (canvas.width / rect.width));
            SDV.mouseY = Math.round((t.clientY - rect.top) * (canvas.height / rect.height));
            e.preventDefault();
        });
        canvas.addEventListener('touchend', (e) => {
            SDV.input['mouse0'] = false;
            e.preventDefault();
        });
        console.log("[SDV] Input handlers attached");
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
