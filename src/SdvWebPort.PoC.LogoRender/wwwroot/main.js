import { dotnet } from './_framework/dotnet.js'
const api = await dotnet.create();
if (typeof globalThis.Module === 'undefined' && api.Module) globalThis.Module = api.Module;
if (typeof globalThis.Blazor === 'undefined') {
    globalThis.Blazor = {
        runtime: { Module: globalThis.Module },
        platform: { getArrayEntryPtr: function(arr, i, s) { return arr.byteOffset + i * s; } }
    };
}
console.log('[boot] Ready');
await api.runMain();
