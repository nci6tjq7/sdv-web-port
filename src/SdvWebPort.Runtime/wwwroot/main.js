// SdvWebPort.Runtime main.js — Blazor WebAssembly bootstrap
import { dotnet } from './_framework/dotnet.js'
import './vfs-interop.js'  // Registers globalThis.vfsXxx functions

// Define setDotnetExports BEFORE dotnet.create() so it's available
let dotnetExports = null;
window.setDotnetExports = function(exports) { dotnetExports = exports; };
window.getDotnetExports = async function() { return dotnetExports; };

const { runMain, getAssemblyExports, getConfig } = await dotnet.create();

// Expose .NET exports to JS for UI callbacks
const config = getConfig();
const exports = await getAssemblyExports(config.mainAssemblyName);
window.setDotnetExports(exports);

await runMain();
