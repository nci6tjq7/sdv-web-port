// Blazor WebAssembly bootstrap for SdvLoad PoC
import { dotnet } from './_framework/dotnet.js'

const logEl = document.getElementById('log');
const statusEl = document.getElementById('status');

// Expose a function for the C# side to get the current URL (for HTTP fetch).
globalThis.getCurrentBaseUrl = function() {
    return window.location.href;
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
    const exitCode = await runtime.runMainAndExit('SdvWebPort.PoC.SdvLoad.dll', []);
    statusEl.textContent = `Main exited with code ${exitCode}`;
    if (exitCode === 0) statusEl.classList.add('pass');
    else statusEl.classList.add('fail');
} catch (err) {
    statusEl.textContent = `ERROR: ${err.message}`;
    statusEl.classList.add('fail');
    console.error(err);
}
