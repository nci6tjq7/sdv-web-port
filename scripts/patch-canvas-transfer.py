#!/usr/bin/env python3
"""Patch dotnet.native.*.js to look up transferred offscreen canvases.

In .NET 10 threaded WASM, all C# runs in the deputy worker, but <canvas>
lives only on the DOM thread. SDL3's emscripten video driver calls
emscripten_webgl_create_context("#canvas", ...) which calls
findCanvasEventTarget("#canvas") which calls document.querySelector("#canvas")
— but `document` doesn't exist in workers, so it returns undefined, and
GL.createContext is never called. FNA3D then calls a non-null JS stub
which hits GLctx.getParameter with GLctx===undefined → crash.

Fix: patch the findEventTarget function definition to ALSO check
globalThis.__sdvTransferredCanvases (a dictionary populated by the main
thread posting messages with transferred OffscreenCanvas).

We also append a message listener at the end of the file to populate
globalThis.__sdvTransferredCanvases from main-thread messages.

Usage: patch-canvas-transfer.py /path/to/_framework/
"""
import glob
import os
import re
import sys


PATCH_MARKER = '__SDV_CANVAS_PATCH_v3__'

# The original findEventTarget function (emscripten-generated):
#   var findEventTarget = target => {
#    target = maybeCStringToJsString(target);
#    var domElement = specialHTMLTargets[target] || (typeof document != "undefined" ? document.querySelector(target) : undefined);
#    return domElement;
#   };
#
# We patch it to also check globalThis.__sdvTransferredCanvases:
#   var findEventTarget = target => {
#    target = maybeCStringToJsString(target);
#    var domElement = specialHTMLTargets[target]
#      || (typeof globalThis.__sdvTransferredCanvases!=='undefined' && globalThis.__sdvTransferredCanvases[target.replace(/^#/, '')])
#      || (typeof document != "undefined" ? document.querySelector(target) : undefined);
#    return domElement;
#   };

# Match the findEventTarget definition (single-line, emscripten output)
FIND_EVENT_TARGET_PATTERN = re.compile(
    r'(var\s+findEventTarget\s*=\s*target\s*=>\s*\{\s*'
    r'target\s*=\s*maybeCStringToJsString\(target\);\s*'
    r'var\s+domElement\s*=\s*specialHTMLTargets\[target\]\s*\|\|\s*'
    r'\(typeof\s+document\s*!=\s*"undefined"\s*\?\s*document\.querySelector\(target\)\s*:\s*undefined\);\s*'
    r'return\s+domElement;\s*'
    r'\})'
)

FIND_EVENT_TARGET_REPLACEMENT = (
    'var findEventTarget = target => { '
    'target = maybeCStringToJsString(target); '
    'var domElement = specialHTMLTargets[target] '
    '|| (typeof globalThis.__sdvTransferredCanvases!=="undefined" && globalThis.__sdvTransferredCanvases[target.replace(/^#/, "")]) '
    '|| (typeof document != "undefined" ? document.querySelector(target) : undefined); '
    'return domElement; '
    '}'
)

# Also patch findCanvasEventTarget = findEventTarget (no change needed, it's an alias)

# Message listener IIFE to populate globalThis.__sdvTransferredCanvases
# This runs in BOTH main thread and worker (no document check)
MESSAGE_LISTENER_IIFE = r"""
;""" + "// " + PATCH_MARKER + r"""
;(function(){
  if(typeof globalThis==='undefined')return;
  if(!globalThis.__sdvTransferredCanvases)globalThis.__sdvTransferredCanvases={};
  // Listen for transferred OffscreenCanvas messages from main thread
  // (addEventListener('message') works alongside self.onmessage)
  globalThis.addEventListener('message',function(e){
    var d=e&&e.data;
    if(!d)return;
    if(d.__type==='sdv_canvas_transfer'&&d.canvas){
      var key=d.id||'canvas';
      globalThis.__sdvTransferredCanvases[key]=d.canvas;
      if(typeof console!=='undefined')console.log('[sdv-canvas] Worker received canvas "'+key+'"');
    }
  });
  if(typeof console!=='undefined')console.log('[sdv-canvas] Message listener installed');
})();
"""


def patch_file(path):
    with open(path, 'r') as f:
        content = f.read()

    if PATCH_MARKER in content:
        print(f'  [SKIP] {path}: already patched')
        return False

    # Step 1: Patch findEventTarget function definition
    new_content, n = FIND_EVENT_TARGET_PATTERN.subn(FIND_EVENT_TARGET_REPLACEMENT, content)
    if n == 0:
        print(f'  [WARN] {path}: findEventTarget pattern not found')
        # Try a more flexible match
        # Look for the line with specialHTMLTargets[target] ||
        pattern2 = re.compile(
            r'(var\s+domElement\s*=\s*specialHTMLTargets\[target\]\s*\|\|\s*'
            r'\(typeof\s+document\s*!=\s*"undefined"\s*\?\s*document\.querySelector\(target\)\s*:\s*undefined\);)'
        )
        replacement2 = (
            'var domElement = specialHTMLTargets[target] '
            '|| (typeof globalThis.__sdvTransferredCanvases!=="undefined" && globalThis.__sdvTransferredCanvases[target.replace(/^#/, "")]) '
            '|| (typeof document != "undefined" ? document.querySelector(target) : undefined);'
        )
        new_content, n = pattern2.subn(replacement2, content)
        if n == 0:
            print(f'  [ERROR] {path}: could not patch findEventTarget')
            return False
    print(f'  [OK] {path}: patched findEventTarget ({n} replacement(s))')

    # Step 2: Append message listener IIFE
    new_content = new_content + MESSAGE_LISTENER_IIFE
    print(f'  [OK] {path}: appended message listener IIFE ({len(MESSAGE_LISTENER_IIFE)} bytes)')

    with open(path, 'w') as f:
        f.write(new_content)
    return True


def main():
    if len(sys.argv) < 2:
        print('Usage: patch-canvas-transfer.py /path/to/_framework/')
        sys.exit(1)

    framework_dir = sys.argv[1]
    files = sorted(glob.glob(os.path.join(framework_dir, 'dotnet.native.*.js')))
    if not files:
        print(f'[ERROR] No dotnet.native.*.js found in {framework_dir}')
        sys.exit(1)

    print(f'[+] Found {len(files)} dotnet.native.*.js file(s)')
    patched_count = 0
    for f in files:
        if patch_file(f):
            patched_count += 1

    print(f'[+] Patched {patched_count}/{len(files)} file(s)')


if __name__ == '__main__':
    main()
