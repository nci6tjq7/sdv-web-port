#!/usr/bin/env python3
"""Patch FNA3D source to work in WebGL/WASM.

Applies 3 patches to FNA3D_Driver_OpenGL.c:
1. forceES3 = 1 (hardcode, skip SDL_GetHintBoolean)
2. useES3 = 1 (trust forceES3, skip SDL_GL_GetAttribute which is broken in emscripten)
3. Skip DoublePrecisionDepth check when useES3 is true
   (WebGL only has glClearDepthf/glDepthRangef, not the double versions)

Usage: patch-fna3d-es3.py <path/to/FNA3D/src/FNA3D_Driver_OpenGL.c>
"""
import re
import sys
from pathlib import Path


def main():
    if len(sys.argv) < 2:
        print('Usage: patch-fna3d-es3.py <path/to/FNA3D_Driver_OpenGL.c>')
        sys.exit(1)

    path = Path(sys.argv[1])
    content = path.read_text()

    # Patch 1: forceES3 = 1 (still useful as a hint, but not strictly needed
    # since we hardcode useES3 below)
    old1 = 'forceES3 = SDL_GetHintBoolean("FNA3D_OPENGL_FORCE_ES3", 0);'
    new1 = 'forceES3 = 1; /* SDV-WASM: hardcoded ES3 */'
    if old1 in content:
        content = content.replace(old1, new1)
        print('[OK] Patch 1: forceES3 = 1')
    else:
        print('[SKIP] Patch 1: forceES3 pattern not found (already patched?)')

    # Patch 2: useES3 = 1 (THE KEY FIX)
    # SDL_GL_GetAttribute(SDL_GL_CONTEXT_PROFILE_MASK) doesn't return the ES
    # profile flag in emscripten's SDL3 build, so useES3 stays false even
    # when we have a WebGL 2.0 context. Hardcode useES3 = 1.
    old2 = 'renderer->useES3 = (flags & SDL_GL_CONTEXT_PROFILE_ES) != 0;'
    new2 = 'renderer->useES3 = 1; /* SDV-WASM: force ES3, SDL_GL_GetAttribute is broken in emscripten */'
    if old2 in content:
        content = content.replace(old2, new2)
        print('[OK] Patch 2: useES3 = 1')
    else:
        print('[SKIP] Patch 2: useES3 pattern not found (already patched?)')

    # Patch 3: skip DoublePrecisionDepth check when useES3 is true
    # WebGL only has glClearDepthf/glDepthRangef (float), not glClearDepth/glDepthRange (double)
    pattern3 = re.compile(
        r'if\s*\(\s*!renderer->supports_DoublePrecisionDepth\s*&&\s*!renderer->supports_OES_single_precision\s*\)'
    )
    new3 = 'if (!renderer->useES3 && !renderer->supports_DoublePrecisionDepth && !renderer->supports_OES_single_precision)'
    content, n3 = pattern3.subn(new3, content)
    if n3 > 0:
        print(f'[OK] Patch 3: skip depth check when useES3 ({n3} replacement(s))')
    else:
        print('[SKIP] Patch 3: depth check pattern not found (already patched?)')

    # Patch 4: skip BaseGL check when useES3 is true.
    # BaseGL loads glActiveTexture, glBindBuffer, etc. via SDL_GL_GetProcAddress.
    # In WebGL 2.0, these functions exist but SDL_GL_GetProcAddress might not find them
    # (emscripten's GL proc lookup is finicky). When useES3 is true, skip this check.
    pattern4 = re.compile(
        r'(if\s*\(\s*!renderer->supports_BaseGL\s*\)\s*\{)'
    )
    new4 = 'if (!renderer->useES3 && !renderer->supports_BaseGL) {'
    content, n4 = pattern4.subn(new4, content)
    if n4 > 0:
        print(f'[OK] Patch 4: skip BaseGL check when useES3 ({n4} replacement(s))')
    else:
        print('[SKIP] Patch 4: BaseGL check pattern not found (already patched?)')

    path.write_text(content)
    print(f'[+] Patched file written to {path}')


if __name__ == '__main__':
    main()
