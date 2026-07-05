// Blazor WebAssembly bootstrap for SdvLoad PoC (Phase 2.5)
import { dotnet } from './_framework/dotnet.js'

const logEl = document.getElementById('log');
const statusEl = document.getElementById('status');

// Expose a function for the C# side to get the current URL (for HTTP fetch).
globalThis.getCurrentBaseUrl = function() {
    return window.location.href;
};

// SHIM: KNI's nkast.Wasm.* JS layer (JSObject.10.0.0.js, CanvasGLContext.10.0.0.js, etc.)
// references `Blazor.platform.getArrayEntryPtr(arr, ...)` and `Blazor.runtime.Module`
// unconditionally. In .NET 10's Microsoft.NET.Sdk.WebAssembly, the `Blazor` global
// does NOT exist — only the `dotnet` runtime API does. We provide a minimal shim
// that routes these calls to the .NET 10 runtime equivalents.
//
// Blazor.platform.getArrayEntryPtr(arr, startIndex, elementSize):
//   In old Blazor WASM, `arr` was a JS wrapper around a .NET array, and this
//   returned the raw WASM memory pointer to the array's data. In .NET 10,
//   the KNI JS code passes `arr` as an integer pointer (read from HEAP32),
//   so we just return it as-is. The caller then uses
//   `new Uint8Array(module.HEAPU8.buffer, arrPtr + offset, length)` to read.
//
// Blazor.runtime.Module: the WASM Module object (has HEAP8/HEAP16/HEAP32/etc.).
//   In .NET 10 this is `runtime.Module` from `dotnet.create()`.
globalThis.Blazor = {
    platform: {
        getArrayEntryPtr: function(arr, startIndex, elementSize) {
            // `arr` is already a raw pointer (integer) in KNI's usage.
            // Just return it — the caller indexes into HEAPU8 with it.
            return arr;
        }
    },
    // Will be set after dotnet.create() resolves.
    runtime: { Module: null }
};

// Expose a function to read canvas pixels (for headless verification).
// Returns a compact summary: total pixels + non-black pixel count + sample color.
globalThis.readCanvasPixels = function() {
    const canvas = document.getElementById('theCanvas');
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
    const { runMain, getAssemblyExports, getConfig } = runtime;

    // KNI's Blazor.GL platform (via nkast.Wasm.JSInterop) expects either
    // globalThis.Module or Blazor.runtime.Module to access WASM memory (HEAP32, HEAPU16, etc).
    // The new .NET 10 Microsoft.NET.Sdk.WebAssembly exposes Module via the runtime API
    // (api.Module is assigned in setRuntimeGlobals via Object.assign(e.api, {Module: e.module, ...e.module}))
    // but not on globalThis. Bridge it here so KNI's JSObject.js can find it.
    // (Same fix as PoC.Render — proven working.)
    if (typeof globalThis.Module === 'undefined') {
        if (runtime.Module) {
            globalThis.Module = runtime.Module;
            console.log('[boot] globalThis.Module set from runtime.Module: ' + (globalThis.Module.HEAPU16 ? 'has HEAPU16' : 'no HEAPU16'));
        } else {
            console.log('[boot] WARN: runtime.Module not found. KNI JSObject.js will fail.');
            console.log('[boot] runtime keys: ' + Object.keys(runtime).join(', '));
        }
    }

    // Also populate the Blazor.runtime.Module shim (for KNI JSObject.js line 85 etc.
    // that use `Blazor.runtime.Module` instead of `globalThis.Module`).
    if (runtime.Module) {
        globalThis.Blazor.runtime.Module = runtime.Module;
        console.log('[boot] Blazor.runtime.Module shim set');
    }

    // Get the .NET assembly exports so we can call [JSExport] methods from JS.
    // The DotNet shim below routes KNI's `DotNet.invokeMethod(asm, method, ...args)`
    // calls to our DotNetInvoker.[JSExport] methods, which use reflection to
    // invoke the actual static method on the named assembly.
    const config = getConfig();
    const exports = await getAssemblyExports(config.mainAssemblyName);
    console.log('[boot] Assembly exports keys: ' + Object.keys(exports).join(', '));
    // The exports object has nested structure: exports.SdvWebPort.PoC.SdvLoad.DotNetInvoker.InvokeStaticMethod
    // Flatten it for easier access.
    let dotnetInvoker = null;
    try {
        // Walk the exports tree to find DotNetInvoker
        function findInvoker(obj, path) {
            for (const key of Object.keys(obj)) {
                if (key === 'DotNetInvoker') return obj[key];
                if (typeof obj[key] === 'object' && obj[key] !== null) {
                    const found = findInvoker(obj[key], path + '.' + key);
                    if (found) return found;
                }
            }
            return null;
        }
        dotnetInvoker = findInvoker(exports, '');
        console.log('[boot] DotNetInvoker found: ' + (dotnetInvoker ? 'yes' : 'no'));
        if (dotnetInvoker) {
            console.log('[boot] DotNetInvoker methods: ' + Object.keys(dotnetInvoker).join(', '));
        }
    } catch (e) {
        console.log('[boot] WARN: Could not find DotNetInvoker in exports: ' + e.message);
    }

    // SHIM: KNI's nkast.Wasm.* JS layer (Window.10.0.0.js, JSObject.10.0.0.js, etc.)
    // calls `DotNet.invokeMethod(assemblyName, methodName, ...args)` to invoke
    // static methods on .NET assemblies (e.g. for requestAnimationFrame callbacks).
    // In .NET 10's Microsoft.NET.Sdk.WebAssembly, `DotNet` is NOT a global.
    // We provide a shim that routes these calls to our [JSExport] DotNetInvoker
    // methods, which use reflection to find and invoke the static method.
    globalThis.DotNet = {
        invokeMethod: function(assemblyName, methodName, ...args) {
            if (!dotnetInvoker) {
                console.error('[DotNet shim] DotNetInvoker not available — cannot invoke ' + assemblyName + '.' + methodName);
                return null;
            }
            // Route based on arg count. KNI's calls use 1, 2, or 3 args (all ints/doubles).
            try {
                if (args.length === 3) {
                    // (int uid, int ci, double time) — requestAnimationFrame callback
                    return dotnetInvoker.InvokeStaticMethod(assemblyName, methodName, args[0]|0, args[1]|0, +args[2]);
                } else if (args.length === 2) {
                    return dotnetInvoker.InvokeStaticMethodIntInt(assemblyName, methodName, args[0]|0, args[1]|0);
                } else if (args.length === 1) {
                    return dotnetInvoker.InvokeStaticMethodInt(assemblyName, methodName, args[0]|0);
                } else {
                    console.warn('[DotNet shim] Unsupported arg count ' + args.length + ' for ' + assemblyName + '.' + methodName);
                    return null;
                }
            } catch (e) {
                console.error('[DotNet shim] Error invoking ' + assemblyName + '.' + methodName + ': ' + e.message);
                return null;
            }
        }
    };
    console.log('[boot] DotNet shim installed');

    statusEl.textContent = 'Runtime ready. Invoking Main...';
    logEl.textContent = '';
    // runMain runs Main and returns. Main calls game.Run() which blocks — but KNI's Blazor.GL
    // platform uses requestAnimationFrame so the loop continues even after
    // Main returns. We use runMain so Main can keep alive via Task.Delay(-1).
    const exitCode = await runMain('SdvWebPort.PoC.SdvLoad.dll', []);
    statusEl.textContent = `Main returned (exit ${exitCode}) — game loop should be running`;
    if (exitCode === 0) statusEl.classList.add('pass');
    else statusEl.classList.add('fail');
} catch (err) {
    statusEl.textContent = `ERROR: ${err.message}`;
    statusEl.classList.add('fail');
    console.error(err);
}
