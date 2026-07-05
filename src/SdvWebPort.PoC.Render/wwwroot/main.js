// SdvWebPort.PoC.Render main.js — Blazor WebAssembly bootstrap for KNI render PoC
import { dotnet } from './_framework/dotnet.js'

const api = await dotnet.create();
const { runMain } = api;

// KNI's Blazor.GL platform (via nkast.Wasm.JSInterop) expects either
// globalThis.Module or Blazor.runtime.Module to access WASM memory (HEAP32, HEAPU16, etc).
// The new .NET 10 Microsoft.NET.Sdk.WebAssembly exposes Module via the runtime API
// (api.Module is assigned in setRuntimeGlobals via Object.assign(e.api, {Module: e.module, ...e.module}))
// but not on globalThis. Bridge it here so KNI's JSObject.js can find it.
if (typeof globalThis.Module === 'undefined') {
    if (api.Module) {
        globalThis.Module = api.Module;
        console.log('[boot] globalThis.Module set from api.Module: ' + (globalThis.Module.HEAPU16 ? 'has HEAPU16' : 'no HEAPU16'));
    } else {
        console.log('[boot] WARN: api.Module not found. KNI JSObject.js will fail.');
        console.log('[boot] api keys: ' + Object.keys(api).join(', '));
    }
}

// runMain executes the C# Program.Main() which starts the KNI game loop
await runMain();
