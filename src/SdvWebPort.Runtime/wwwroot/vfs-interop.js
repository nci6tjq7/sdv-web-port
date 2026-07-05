// vfs-interop.js — JS interop layer for File System Access API + OPFS
// Called from C# via [JSImport("globalThis.vfsXxx")].

let pickedDirectoryHandle = null;

// ── File System Access API (A2 path) ──────────────────────────────────────

globalThis.vfsPickDirectory = async function() {
  if (!('showDirectoryPicker' in window)) return false;
  try {
    pickedDirectoryHandle = await window.showDirectoryPicker({ mode: 'read' });
    return true;
  } catch (e) {
    console.log('[vfs] Directory picker cancelled or failed: ' + e.message);
    return false;
  }
};

globalThis.vfsFsaExists = async function(path) {
  if (!pickedDirectoryHandle) return false;
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = pickedDirectoryHandle;
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i]);
    }
    await handle.getFileHandle(parts[parts.length - 1]);
    return true;
  } catch (e) {
    return false;
  }
};

globalThis.vfsFsaReadFile = async function(path) {
  if (!pickedDirectoryHandle) throw new Error('No directory picked');
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = pickedDirectoryHandle;
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  const buffer = await file.arrayBuffer();
  return new Uint8Array(buffer);
};

globalThis.vfsFsaGetFileSize = async function(path) {
  if (!pickedDirectoryHandle) return -1;
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = pickedDirectoryHandle;
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  return file.size;
};

globalThis.vfsFsaEnumerateFiles = async function(dirPath, pattern) {
  if (!pickedDirectoryHandle) return [];
  const parts = dirPath.split('/').filter(p => p.length > 0);
  let handle = pickedDirectoryHandle;
  for (const p of parts) {
    handle = await handle.getDirectoryHandle(p);
  }
  const results = [];
  const regex = globToRegex(pattern);
  for await (const entry of handle.values()) {
    if (entry.kind === 'file' && regex.test(entry.name)) {
      results.push(dirPath.replace(/\/$/, '') + '/' + entry.name);
    }
  }
  return results;
};

// ── OPFS (A1 path) ────────────────────────────────────────────────────────

async function getOpfsRoot() {
  const root = await navigator.storage.getDirectory();
  return root;
}

globalThis.vfsOpfsWriteFile = async function(path, uint8array) {
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = await getOpfsRoot();
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i], { create: true });
    }
    const fileHandle = await handle.getFileHandle(parts[parts.length - 1], { create: true });
    const writable = await fileHandle.createWritable();
    await writable.write(uint8array);
    await writable.close();
    return true;
  } catch (e) {
    console.log('[vfs] OPFS write failed: ' + e.message);
    return false;
  }
};

globalThis.vfsOpfsExists = async function(path) {
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = await getOpfsRoot();
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i]);
    }
    await handle.getFileHandle(parts[parts.length - 1]);
    return true;
  } catch (e) {
    return false;
  }
};

globalThis.vfsOpfsReadFile = async function(path) {
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = await getOpfsRoot();
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  const buffer = await file.arrayBuffer();
  return new Uint8Array(buffer);
};

globalThis.vfsOpfsGetFileSize = async function(path) {
  const parts = path.split('/').filter(p => p.length > 0);
  let handle = await getOpfsRoot();
  for (let i = 0; i < parts.length - 1; i++) {
    handle = await handle.getDirectoryHandle(parts[i]);
  }
  const fileHandle = await handle.getFileHandle(parts[parts.length - 1]);
  const file = await fileHandle.getFile();
  return file.size;
};

globalThis.vfsOpfsEnumerateFiles = async function(dirPath, pattern) {
  const parts = dirPath.split('/').filter(p => p.length > 0);
  let handle = await getOpfsRoot();
  for (const p of parts) {
    handle = await handle.getDirectoryHandle(p);
  }
  const results = [];
  const regex = globToRegex(pattern);
  for await (const entry of handle.values()) {
    if (entry.kind === 'file' && regex.test(entry.name)) {
      results.push(dirPath.replace(/\/$/, '') + '/' + entry.name);
    }
  }
  return results;
};

globalThis.vfsOpfsDeleteFile = async function(path) {
  try {
    const parts = path.split('/').filter(p => p.length > 0);
    let handle = await getOpfsRoot();
    for (let i = 0; i < parts.length - 1; i++) {
      handle = await handle.getDirectoryHandle(parts[i]);
    }
    await handle.removeEntry(parts[parts.length - 1]);
    return true;
  } catch (e) {
    return false;
  }
};

// ── Capabilities detection ────────────────────────────────────────────────

globalThis.vfsGetCapabilities = function() {
  return JSON.stringify({
    fsa: 'showDirectoryPicker' in window,
    opfs: 'storage' in navigator && 'getDirectory' in navigator.storage
  });
};

// ── Helpers ───────────────────────────────────────────────────────────────

function globToRegex(pattern) {
  if (!pattern || pattern === '*' || pattern === '*.*') return /.*/;
  const escaped = pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*').replace(/\?/g, '.');
  return new RegExp('^' + escaped + '$');
}

// ── UI helpers (called from C# via [JSImport]) ────────────────────────────
globalThis.showElement = function(id) { document.getElementById(id)?.classList.remove('hidden'); };
globalThis.hideElement = function(id) { document.getElementById(id)?.classList.add('hidden'); };
globalThis.setStatJs = function(msg) { const s = document.getElementById('status'); if (s) s.innerText = msg; };
globalThis.clearCanvas = function(r, g, b) {
  const canvas = document.getElementById('game-canvas');
  if (!canvas) return;
  const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
  if (gl && gl.clearColor) { gl.clearColor(r/255, g/255, b/255, 1.0); gl.clear(gl.COLOR_BUFFER_BIT); }
  else { const ctx = canvas.getContext('2d'); ctx.fillStyle = `rgb(${r},${g},${b})`; ctx.fillRect(0, 0, canvas.width, canvas.height); }
};
