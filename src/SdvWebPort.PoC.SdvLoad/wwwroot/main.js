// Blazor WebAssembly bootstrap for SdvLoad PoC (Phase 2.5)
import { dotnet } from './_framework/dotnet.js'

const logEl = document.getElementById('log');
const statusEl = document.getElementById('status');

// Expose a function for the C# side to get the current URL (for HTTP fetch).
globalThis.getCurrentBaseUrl = function() {
    return window.location.href;
};

// Expose a function to read canvas pixels (for headless verification).
// Returns a compact summary: total pixels + non-black pixel count + sample color.
globalThis.readCanvasPixels = function() {
    const canvas = document.getElementById('game-canvas');
    if (!canvas) return JSON.stringify({ error: 'canvas not found' });
    try {
        const ctx = canvas.getContext('webgl2') || canvas.getContext('webgl') || canvas.getContext('2d');
        if (!ctx) return JSON.stringify({ error: 'no context' });

        // For WebGL2, we need to read the framebuffer
        if (ctx instanceof WebGL2RenderingContext || ctx instanceof WebGLRenderingContext) {
            const w = canvas.width;
            const h = canvas.height;
            const pixels = new Uint8Array(w * h * 4);
            ctx.readPixels(0,0, w, h, ctx.RGBA, ctx.UNSIGNED_BYTE, pixels);
            let nonBlack = 0;
            let sampleR = 0, sampleG = 0, sampleB = 0;
            // Sample every 1000th pixel for speed
            for (let i = 0; i < pixels.length; i += 4000) {
                const r = pixels[i], g = pixels[i+1], b = pixels[i+2];
                if (r > 0 || g > 0 || b > 0) nonBlack++;
                sampleR = r; sampleG = g; sampleB = b;
            }
            return JSON.stringify({
                width: w, height: h,
                nonBlackSamples: nonBlack,
                sampleColor: [sampleR, sampleG, sampleB]
            });
        }
        return JSON.stringify({ error: 'unsupported context type', type: ctx.constructor.name });
    } catch (e) {
        return JSON.stringify({ error: e.message });
    }
};

// Pipe console.log to the on-page <pre> for visibility.
const origLog = console.log;
const origError = console.error;
const origWarn = console.warn;

function appendLog(args, color) {
    const line = args.map(a => {
        if (typeof a === 'object') {
            try { return JSON.stringify(a); } catch { return String(a); }
        }
        return String(a);
    }).join(' ');
    const span = document.createElement('span');
    span.textContent = line + '\n';
    if (color) span.style.color = color;
    logEl.appendChild(span);
    logEl.scrollTop = logEl.scrollHeight;
}

console.log = function(...args) { origLog.apply(console, args); appendLog(args); };
console.error = function(...args) { origError.apply(console, args); appendLog(args, '#f44747'); };
console.warn = function(...args) { origWarn.apply(console, args); appendLog(args, '#dcdcaa'); };

statusEl.textContent = 'Loading WASM runtime...';

try {
    const runtime = await dotnet.create();
    statusEl.textContent = 'Runtime ready. Invoking Main...';
    logEl.textContent = '';
    // runMain runs Main and returns. Main calls game.Run() which blocks — but KNI's Blazor.GL
    // platform uses requestAnimationFrame so the loop continues even after
    // Main returns. We use runMain so Main can keep alive via Task.Delay(-1).
    const exitCode = await runtime.runMain('SdvWebPort.PoC.SdvLoad.dll', []);
    statusEl.textContent = `Main returned (exit ${exitCode}) — game loop should be running`;
    if (exitCode === 0) statusEl.classList.add('pass');
    else statusEl.classList.add('fail');
} catch (err) {
    statusEl.textContent = `ERROR: ${err.message}`;
    statusEl.classList.add('fail');
    console.error(err);
}
