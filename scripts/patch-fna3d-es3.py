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

    # Patch 1: forceES3 = 1
    old1 = 'forceES3 = SDL_GetHintBoolean("FNA3D_OPENGL_FORCE_ES3", 0);'
    new1 = 'forceES3 = 1; /* SDV-WASM: hardcoded ES3 */'
    if old1 in content:
        content = content.replace(old1, new1)
        print('[OK] Patch 1: forceES3 = 1')
    else:
        print('[SKIP] Patch 1: forceES3 pattern not found (already patched?)')

    # Patch 2: useES3 = 1
    old2 = 'renderer->useES3 = (flags & SDL_GL_CONTEXT_PROFILE_ES) != 0;'
    new2 = 'renderer->useES3 = 1; /* SDV-WASM: force ES3, SDL_GL_GetAttribute is broken in emscripten */'
    if old2 in content:
        content = content.replace(old2, new2)
        print('[OK] Patch 2: useES3 = 1')
    else:
        print('[SKIP] Patch 2: useES3 pattern not found (already patched?)')

    # Patch 3: skip DoublePrecisionDepth check when useES3 is true
    # Original: if (\t!renderer->supports_DoublePrecisionDepth &&\n\t\t!renderer->supports_OES_single_precision\t)
    # Note: 'if (' has a space, then tab after the paren. Use regex with \s to match any whitespace.
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
    # WebGL 2.0 may not expose all BaseGL functions (e.g., glClearDepth with GLdouble).
    # When useES3 is true, skip the BaseGL check — ES3 mandates these functions.
    pattern4 = re.compile(
        r'(if\s*\(\s*!renderer->supports_BaseGL\s*\)\s*\{)'
    )
    new4 = 'if (!renderer->useES3 && !renderer->supports_BaseGL) {'
    content, n4 = pattern4.subn(new4, content)
    if n4 > 0:
        print(f'[OK] Patch 4: skip BaseGL check when useES3 ({n4} replacement(s))')
    else:
        print('[SKIP] Patch 4: BaseGL check pattern not found (already patched?)')

    # Patch 5: skip NonES3 check when useES3 is true (the else branch).
    # The else branch checks !supports_3DTexture || !supports_ARB_occlusion_query || !supports_NonES3
    # but useES3 takes the if branch, so this should already be skipped.
    # However, if there's any other check that uses baseErrorString, skip it too.
    # Replace all remaining 'FNA3D_LogError("%s\\n%s", baseErrorString, driverInfo); return;'
    # with a warning instead of fatal error.
    # IMPORTANT: the \\n in the C string literal must be preserved as backslash-n.
    # In Python, '\\\\n' produces the two characters \ and n in the output file.
    pattern5 = re.compile(
        r'FNA3D_LogError\(\s*"%s\\n%s",\s*baseErrorString,\s*driverInfo\s*\);\s*return;'
    )
    new5 = 'FNA3D_LogWarn("SDV-WASM: skipping GL capability check. %s", driverInfo);'
    content, n5 = pattern5.subn(new5, content)
    if n5 > 0:
        print(f'[OK] Patch 5: downgrade remaining baseErrorString errors to warnings ({n5} replacement(s))')
    else:
        print('[SKIP] Patch 5: no remaining baseErrorString errors found')

    path.write_text(content)
    print(f'[+] Patched file written to {path}')


if __name__ == '__main__':
    main()
