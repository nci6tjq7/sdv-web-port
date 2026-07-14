// SDV WASM Runtime - main.js
// Provides canvas/input for FNA and hooks into Blazor boot

let canvas = null;
let ctx = null;

const SDV = {
    canvas: null,
    ctx: null,
    input: {},

    init() {
        console.log("[SDV] Initializing UI...");
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
        // Touch (mobile) - passive to avoid violation warnings
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
        const loading = document.getElementById('loading');
        if (loading) loading.style.display = 'none';
        const status = document.getElementById('status');
        if (status) status.textContent = 'Game running';
    },

    // Log from C# (for debugging)
    log(msg) {
        console.log("[C#]", msg);
    },

    // Error from C#
    error(msg) {
        console.error("[C#]", msg);
        const el = document.getElementById('error-log');
        if (el) {
            el.textContent += msg + '\n';
            el.style.display = 'block';
        }
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
