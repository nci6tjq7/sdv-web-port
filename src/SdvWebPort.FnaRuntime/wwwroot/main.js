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

            // Transfer the canvas to the WASM worker thread BEFORE creating the runtime.
            // .NET 10 threaded WASM runs all C# in the deputy worker, but <canvas> lives
            // on the DOM thread. SDL3's emscripten video driver calls
            // emscripten_webgl_create_context('#canvas', ...) which calls
            // findCanvasEventTarget('#canvas') → document.querySelector('#canvas')
            // — but `document` doesn't exist in workers, so it returns undefined.
            //
            // Fix: transfer the canvas via canvas.transferControlToOffscreen() and post
            // the resulting OffscreenCanvas to the worker via a 'message' event.
            // The patch-canvas-transfer.py-injected IIFE in dotnet.native.*.js listens
            // for this message and intercepts GL.createContext to substitute the
            // transferred OffscreenCanvas when the selector is '#canvas' or 'canvas'.
            //
            // We can't transfer the canvas BEFORE creating the runtime because we don't
            // have access to the worker. Instead, we register a callback to run as soon
            // as the worker is created. We do this by wrapping the Worker constructor.
            //
            // Reference: celeste-wasm pattern (adapted to .NET 10 SDK that doesn't
            // support transferredCanvasNames config option)
            let canvasTransferred = false;
            const origWorker = window.Worker;
            window.Worker = function(url, opts) {
                console.log('[SDV] Worker created:', url, opts);
                const worker = new origWorker(url, opts);
                // Transfer the canvas as soon as the worker is created
                if (!canvasTransferred) {
                    try {
                        const offscreen = canvas.transferControlToOffscreen();
                        // postMessage with transferable array
                        worker.postMessage(
                            { __type: 'sdv_canvas_transfer', id: 'canvas', canvas: offscreen },
                            [offscreen]
                        );
                        canvasTransferred = true;
                        console.log('[SDV] Canvas transferred to worker');
                    } catch (e) {
                        console.warn('[SDV] Canvas transfer failed:', e);
                    }
                }
                return worker;
            };
            // Copy prototype and static properties
            window.Worker.prototype = origWorker.prototype;
            window.Worker.PROTOTYPE = origWorker.prototype;

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
