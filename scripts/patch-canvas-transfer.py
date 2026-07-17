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
# Also sets ENV.FNA3D_OPENGL_FORCE_ES3 to force ES3 profile recognition
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
  // Set FNA3D_OPENGL_FORCE_ES3=1 in emscripten's ENV object.
  // FNA3D reads this via SDL_GetHintBoolean('FNA3D_OPENGL_FORCE_ES3', 0) which
  // calls SDL_getenv which reads from ENV. This forces FNA3D to:
  //   1. Call SDL_GL_SetAttribute(CONTEXT_MAJOR_VERSION, 3) before context creation
  //   2. Call SDL_GL_SetAttribute(CONTEXT_PROFILE_MASK, ES) before context creation
  //   3. Set renderer->useES3 = true after context creation
  // Without this, FNA3D creates a WebGL 2.0 context but doesn't recognize it
  // as ES3, causing 'OpenGL ES 3.0 support is required!' error.
  // ENV is defined as 'var ENV = {};' earlier in this same module, so it's
  // accessible here. We set it directly (no typeof check needed).
  try {
    ENV.FNA3D_OPENGL_FORCE_ES3='1';
    if(typeof console!=='undefined')console.log('[sdv-canvas] Set ENV.FNA3D_OPENGL_FORCE_ES3=1 (ENV keys: '+Object.keys(ENV).length+')');
  } catch(e) {
    if(typeof console!=='undefined')console.warn('[sdv-canvas] Failed to set ENV:', e.message);
  }
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

    # Step 2: Force WebGL 2.0 context (OpenGL ES 3.0) by patching TWO places:
    #
    # A) _emscripten_webgl_do_create_context: override contextAttributes.majorVersion = 2
    #    (FNA3D doesn't set ES3 because SDL_GetPlatform() returns "Unknown" not "Emscripten")
    #
    # B) GL.createContext: use "webgl2" instead of "webgl" when majorVersion >= 2
    #    (emscripten's GL.createContext hardcodes "webgl", ignoring majorVersion)
    #
    # Both patches are needed — without (A), majorVersion defaults to 1.
    # Without (B), even majorVersion=2 still creates a WebGL 1.0 context.

    # Patch A: force majorVersion=2
    pattern_major = re.compile(
        r'(var\s+contextAttributes\s*=\s*\{[^}]*majorVersion:\s*GROWABLE_HEAP_I32\(\)\[a\s*\+\s*\(32\s*>>\s*2\)\][^}]*\};)'
    )
    replacement_major = r'\1 contextAttributes.majorVersion = Math.max(contextAttributes.majorVersion || 1, 2); /* SDV: force WebGL 2.0 = OpenGL ES 3.0 */'
    new_content, n_major = pattern_major.subn(replacement_major, new_content, count=1)
    if n_major > 0:
        print(f'  [OK] {path}: forced majorVersion=2 ({n_major} replacement(s))')
    else:
        print(f'  [WARN] {path}: could not patch majorVersion')

    # Patch B: use "webgl2" when majorVersion >= 2
    # Original: var ctx = (canvas.getContext("webgl", webGLContextAttributes));
    # Patched:  var ctx = (canvas.getContext(webGLContextAttributes.majorVersion >= 2 ? "webgl2" : "webgl", webGLContextAttributes));
    pattern_ctx = re.compile(
        r'(var\s+ctx\s*=\s*\(canvas\.getContext\()"webgl"(\s*,\s*webGLContextAttributes\)\))'
    )
    replacement_ctx = r'\1webGLContextAttributes.majorVersion >= 2 ? "webgl2" : "webgl"\2'
    new_content, n_ctx = pattern_ctx.subn(replacement_ctx, new_content, count=1)
    if n_ctx > 0:
        print(f'  [OK] {path}: patched GL.createContext to use webgl2 ({n_ctx} replacement(s))')
    else:
        print(f'  [WARN] {path}: could not patch GL.createContext getContext call')

    # Step 3: Append message listener IIFE
    new_content = new_content + MESSAGE_LISTENER_IIFE
    print(f'  [OK] {path}: appended message listener IIFE ({len(MESSAGE_LISTENER_IIFE)} bytes)')

    with open(path, 'w') as f:
        f.write(new_content)
    return True


def patch_worker_file(path):
    """Patch dotnet.native.worker.*.mjs to handle canvas transfer messages.

    The worker's handleMessage function processes messages with specific 'cmd'
    values. Our canvas transfer message has __type='sdv_canvas_transfer' (no cmd),
    so it's silently ignored.

    Fix: patch handleMessage to intercept our message type and store the canvas
    in globalThis.__sdvTransferredCanvases BEFORE the import('./dotnet.native.js')
    completes. This ensures the canvas is available when findEventTarget runs.
    """
    with open(path, 'r') as f:
        content = f.read()

    if PATCH_MARKER in content:
        print(f'  [SKIP] {path}: already patched')
        return False

    # Find the handleMessage function and inject our handler at the TOP,
    # before any other cmd checks. This ensures our message is processed
    # even before the module is loaded.
    #
    # The handleMessage function starts with:
    #   self.onmessage = handleMessage;
    # And the function itself starts with:
    #   var handleMessage = function(e) {
    #     ...
    #   };
    # Or it might be:
    #   function handleMessage(e) {
    #     ...
    #   }
    # Or:
    #   self.onmessage = (e) => { ... }

    # Look for the onmessage assignment and inject our handler BEFORE it
    # so our code runs first for every message.
    inject = r"""
// """ + PATCH_MARKER + r"""
// Canvas transfer handler — runs BEFORE handleMessage to avoid race condition
// where the message arrives before import('./dotnet.native.js') completes.
if(typeof globalThis==='undefined')globalThis=globalThis;
if(!globalThis.__sdvTransferredCanvases)globalThis.__sdvTransferredCanvases={};
var __sdvOrigOnmessage=null;
function __sdvCanvasMessageHandler(e){
  var d=e&&e.data;
  if(d&&d.__type==='sdv_canvas_transfer'&&d.canvas){
    var key=d.id||'canvas';
    globalThis.__sdvTransferredCanvases[key]=d.canvas;
    if(typeof console!=='undefined')console.log('[sdv-canvas] Worker received canvas "'+key+'" (pre-load)');
  }
}
// Install immediately — will be called for ALL messages before self.onmessage
self.addEventListener('message',__sdvCanvasMessageHandler);
// Set environment variables that C library reads via getenv().
// FNA3D_OPENGL_FORCE_ES3=1: forces OpenGL ES 3.0 context (WebGL 2.0 in emscripten).
// The .NET WASM SDK's withEnvironmentVariables may not propagate to the worker's
// emscripten ENV object, so we set it directly here.
// We can't access ENV yet (it's defined inside dotnet.native.js), but we CAN
// override getenv() to return our value. Emscripten's getenv reads from ENV
// object, so we patch ENV after module load via a getter.
// Simpler: set it on the Module object that emscripten reads.
if(typeof Module==='undefined')Module={};
Module.preRun=Module.preRun||[];
Module.preRun.push(function(){
  if(typeof ENV!=='undefined'){
    ENV.FNA3D_OPENGL_FORCE_ES3='1';
    if(typeof console!=='undefined')console.log('[sdv-canvas] Set ENV.FNA3D_OPENGL_FORCE_ES3=1');
  }
});
"""

    # Prepend the inject at the very top of the file (after the license comment)
    # Find a good insertion point — after 'use strict';
    insertion_point = content.find("'use strict';")
    if insertion_point == -1:
        insertion_point = content.find('"use strict";')
    if insertion_point == -1:
        # Just prepend
        new_content = inject + content
    else:
        # Insert after 'use strict';
        end_of_strict = insertion_point + len("'use strict';")
        new_content = content[:end_of_strict] + inject + content[end_of_strict:]

    with open(path, 'w') as f:
        f.write(new_content)
    print(f'  [OK] {path}: prepended canvas transfer handler')
    return True


def main():
    if len(sys.argv) < 2:
        print('Usage: patch-canvas-transfer.py /path/to/_framework/')
        sys.exit(1)

    framework_dir = sys.argv[1]

    # Patch dotnet.native.*.js (findEventTarget + message listener IIFE)
    js_files = sorted(glob.glob(os.path.join(framework_dir, 'dotnet.native.*.js')))
    if not js_files:
        print(f'[ERROR] No dotnet.native.*.js found in {framework_dir}')
        sys.exit(1)

    print(f'[+] Found {len(js_files)} dotnet.native.*.js file(s)')
    patched_count = 0
    for f in js_files:
        if patch_file(f):
            patched_count += 1
    print(f'[+] Patched {patched_count}/{len(js_files)} .js file(s)')

    # Patch dotnet.native.worker.*.mjs (early message handler)
    mjs_files = sorted(glob.glob(os.path.join(framework_dir, 'dotnet.native.worker.*.mjs')))
    if not mjs_files:
        print(f'[WARN] No dotnet.native.worker.*.mjs found — worker patch skipped')
    else:
        print(f'[+] Found {len(mjs_files)} dotnet.native.worker.*.mjs file(s)')
        mjs_patched = 0
        for f in mjs_files:
            if patch_worker_file(f):
                mjs_patched += 1
        print(f'[+] Patched {mjs_patched}/{len(mjs_files)} .mjs file(s)')


if __name__ == '__main__':
    main()
