#!/usr/bin/env python3
"""Patch dotnet.native.*.js to transfer canvas elements to the WASM worker.

In .NET 10 threaded WASM, all C# runs in the deputy worker, but <canvas>
lives only on the DOM thread. SDL3's emscripten video driver calls
emscripten_webgl_create_context("#canvas", ...) which calls
findCanvasEventTarget("#canvas") which calls document.querySelector("#canvas")
— but `document` doesn't exist in workers, so it returns undefined, and
GL.createContext is never called. FNA3D then calls a non-null JS stub
which hits GLctx.getParameter with GLctx===undefined → crash.

Fix: monkey-patch `findCanvasEventTarget` in dotnet.native.*.js to look up
transferred offscreen canvases via OffscreenCanvas's name registry.

We do this by appending an IIFE that:
1. Listens for 'message' events from the main thread
2. When the main thread posts a transferred OffscreenCanvas, registers it
3. Overrides findCanvasEventTarget to look up by CSS selector
"""
import glob
import os
import sys


PATCH_MARKER = '__SDV_CANVAS_PATCH_v2__'


def patch_file(path):
    with open(path, 'r') as f:
        content = f.read()

    if PATCH_MARKER in content:
        print(f'  [SKIP] {path}: already patched')
        return False

    # Inject our canvas lookup override.
    # The IIFE wraps the original findCanvasEventTarget, intercepting "#canvas"
    # and "canvas" selectors to return a transferred OffscreenCanvas.
    #
    # The main thread (main.js) is responsible for posting a message with the
    # OffscreenCanvas to the worker before SDV starts.
    inject = r"""
;""" + "// " + PATCH_MARKER + r"""
;(function(){
  if(typeof globalThis==='undefined')return;
  // Only patch on worker threads (where 'document' is undefined)
  if(typeof document!=='undefined')return;

  // Storage for transferred canvases (keyed by id, defaults to 'canvas')
  if(!globalThis.__sdvTransferredCanvases)globalThis.__sdvTransferredCanvases={};

  // Listen for transferred OffscreenCanvas messages from main thread
  globalThis.addEventListener('message',function(e){
    var d=e&&e.data;
    if(!d)return;
    if(d.__type==='sdv_canvas_transfer'&&d.canvas){
      var key=d.id||'canvas';
      globalThis.__sdvTransferredCanvases[key]=d.canvas;
      console.log('[sdv-canvas] Worker received canvas "'+key+'"');
    }
  });

  // Wait for Module to be defined, then patch findCanvasEventTarget
  function patchFindCanvasEventTarget(){
    if(typeof findCanvasEventTarget==='undefined'){
      setTimeout(patchFindCanvasEventTarget,0);
      return;
    }
    var orig=findCanvasEventTarget;
    // findCanvasEventTarget is a let/var assignment, can't be reassigned in module scope.
    // Instead, hook into GL.createContext to translate '#canvas' → transferred canvas.
    if(typeof GL!=='undefined'&&GL&&GL.createContext){
      var origCreateContext=GL.createContext;
      GL.createContext=function(canvas,attrs){
        // If canvas is a string (CSS selector), look up transferred canvas
        if(typeof canvas==='string'){
          var selector=canvas.replace(/^#/,'');
          var transferred=globalThis.__sdvTransferredCanvases[selector];
          if(transferred){
            console.log('[sdv-canvas] GL.createContext using transferred canvas for "'+selector+'"');
            return origCreateContext.call(GL,transferred,attrs);
          }
        }
        return origCreateContext.apply(GL,arguments);
      };
      console.log('[sdv-canvas] Patched GL.createContext to intercept canvas selectors');
    } else {
      // GL not ready yet — retry
      setTimeout(patchFindCanvasEventTarget,0);
    }
  }
  patchFindCanvasEventTarget();
})();
"""
    content = content + inject
    with open(path, 'w') as f:
        f.write(content)
    print(f'  [OK] {path}: appended canvas transfer patch ({len(inject)} bytes)')
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
