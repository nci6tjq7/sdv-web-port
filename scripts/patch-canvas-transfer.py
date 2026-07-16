#!/usr/bin/env python3
"""Patch dotnet.native.*.js to transfer canvas elements to the WASM worker.

In .NET 10 threaded WASM, all C# runs in the deputy worker, but <canvas>
lives only on the DOM thread. SDL3's emscripten video driver hardcodes
'#canvas' as the selector, and emscripten_webgl_create_context tries
GL.offscreenCanvases["canvas"] (empty - no canvas was transferred) then
Module['canvas'] (unset) then document.querySelector (undefined in workers)
→ returns 0 silently. FNA3D then calls a non-null JS stub which hits
GLctx.getParameter with GLctx===undefined → crash.

Fix: append a self-executing function expression (IIFE) at the END of
dotnet.native.js. The function runs on the DOM thread (where document exists)
and transfers all elements with class='canvas' via transferControlToOffscreen()
into GL.offscreenCanvases.

Usage: patch-canvas-transfer.py /path/to/_framework/
"""
import glob
import os
import sys


def patch_file(path):
    with open(path, 'r') as f:
        content = f.read()

    if 'TRANSFERRED_CANVAS' in content:
        print(f'  [SKIP] {path}: already patched')
        return False

    # The IIFE runs on DOM thread, transfers all <canvas class="canvas"> elements
    # to GL.offscreenCanvases via transferControlToOffscreen().
    # Retries with setTimeout in case GL isn't defined yet at script load time.
    inject = r"""
;(function(){
  if(typeof globalThis==='undefined'||!globalThis.window)return;
  if(window.TRANSFERRED_CANVAS)return;
  window.TRANSFERRED_CANVAS=true;
  function transferCanvases(){
    if(typeof GL==='undefined'||!GL)return false;
    var canvases=document.getElementsByClassName('canvas');
    for(var i=0;i<canvases.length;i++){
      var c=canvases[i];
      try{
        var off=c.transferControlToOffscreen();
        GL.offscreenCanvases[c.id||'canvas']=off;
        console.log('[canvas-patch] Transferred canvas "'+(c.id||'canvas')+'" to offscreen');
      }catch(e){
        console.warn('[canvas-patch] Failed to transfer canvas "'+c.id+'":',e);
      }
    }
    return canvases.length>0;
  }
  if(!transferCanvases()){
    setTimeout(transferCanvases,0);
    setTimeout(transferCanvases,100);
    setTimeout(transferCanvases,500);
  }
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
