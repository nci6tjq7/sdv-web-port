import { dotnet } from './_framework/dotnet.js'

const api = await dotnet.create();

// KNI's Blazor.GL platform needs globalThis.Module to access WASM memory
// Some KNI code paths also reference Blazor.runtime.Module directly
if (typeof globalThis.Module === 'undefined' && api.Module) {
    globalThis.Module = api.Module;
    console.log('[boot] globalThis.Module set: ' + (globalThis.Module.HEAPU16 ? 'has HEAPU16' : 'no HEAPU16'));
}
// Stub Blazor.global so KNI's Blazor.runtime.Module references don't crash
if (typeof globalThis.Blazor === 'undefined') {
    globalThis.Blazor = { runtime: { Module: globalThis.Module } };
    console.log('[boot] globalThis.Blazor stub created');
}

await api.runMain();
