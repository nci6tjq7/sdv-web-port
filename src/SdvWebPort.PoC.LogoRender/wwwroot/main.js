import { dotnet } from './_framework/dotnet.js'
const api = await dotnet.create();
if (typeof globalThis.Module === 'undefined' && api.Module) globalThis.Module = api.Module;
if (typeof globalThis.Blazor === 'undefined') {
    globalThis.Blazor = {
        runtime: { Module: globalThis.Module },
        platform: {
            getArrayEntryPtr: function(arr, index, elemSize) {
                // Return a pointer into the WASM heap for the array element
                // In the new SDK, we use the Module's HEAP buffers
                return arr.byteOffset + index * elemSize;
            }
        }
    };
}
console.log('[boot] Module + Blazor stub ready');
await api.runMain();
