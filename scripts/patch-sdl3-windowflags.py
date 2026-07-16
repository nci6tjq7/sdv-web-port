#!/usr/bin/env python3
"""
Patch SDL3-CS to work around dotnet/runtime#112262.

The bug: .NET WASM PInvokeTableGenerator's SignatureMapper.cs incorrectly
maps `enum:long` / `enum:ulong` types in DllImport signatures. It generates
'i32' in the WASM indirect call table instead of 'i64', causing
"function signature mismatch" trap when the C function (which expects
Uint64=i64) is called.

SDL3-CS declares `enum SDL_WindowFlags : ulong` and uses it in 4 DllImports:
  1. INTERNAL_SDL_CreateWindow(byte*, int, int, SDL_WindowFlags)
  2. SDL_CreatePopupWindow(IntPtr, int, int, int, int, SDL_WindowFlags)
  3. SDL_GetWindowFlags(IntPtr) -> SDL_WindowFlags
  4. INTERNAL_SDL_CreateWindowAndRenderer(byte*, int, int, SDL_WindowFlags, ...)

Maintainer's suggested workaround (dotnet/runtime#112262 comment):
  "cast C# enum:ulong/enum:long to ulong/long directly in DllImport signatures"

This script:
  - Changes SDL_WindowFlags → ulong in all 4 DllImport signatures
  - Casts at the call site for the 2 private DllImports
  - Adds public wrappers for the 2 public DllImports (SDL_CreatePopupWindow,
    SDL_GetWindowFlags) so the FNA-side callers using SDL_WindowFlags still work

Run after cloning SDL3-CS, before building FNA.
Usage: patch-sdl3-windowflags.py /path/to/SDL3-CS/SDL3/SDL3.Legacy.cs
"""
import sys
import re
from pathlib import Path


def patch_file(path: Path) -> int:
    """Apply all 4 patches to SDL3.Legacy.cs. Returns number of patches applied."""
    text = path.read_text(encoding="utf-8")
    original = text
    patches_applied = 0

    # ---- Patch 1: INTERNAL_SDL_CreateWindow (private DllImport) ----
    # Change SDL_WindowFlags → ulong in the DllImport signature.
    # Cast at the call site (just below, in the public wrapper).
    old1 = (
        '[DllImport(nativeLibName, EntryPoint = "SDL_CreateWindow", CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tprivate static extern IntPtr INTERNAL_SDL_CreateWindow(byte* title, int w, int h, SDL_WindowFlags flags);\n'
        '\t\tpublic static IntPtr SDL_CreateWindow(string title, int w, int h, SDL_WindowFlags flags)\n'
        '\t\t{\n'
        '\t\t\tvar titleUTF8 = EncodeAsUTF8(title);\n'
        '\t\t\tvar result = INTERNAL_SDL_CreateWindow(titleUTF8, w, h, flags);\n'
    )
    new1 = (
        '[DllImport(nativeLibName, EntryPoint = "SDL_CreateWindow", CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tprivate static extern IntPtr INTERNAL_SDL_CreateWindow(byte* title, int w, int h, ulong flags);\n'
        '\t\tpublic static IntPtr SDL_CreateWindow(string title, int w, int h, SDL_WindowFlags flags)\n'
        '\t\t{\n'
        '\t\t\tvar titleUTF8 = EncodeAsUTF8(title);\n'
        '\t\t\tvar result = INTERNAL_SDL_CreateWindow(titleUTF8, w, h, (ulong)flags);\n'
    )
    if old1 in text:
        text = text.replace(old1, new1)
        patches_applied += 1
        print(f"  [OK] Patch 1: INTERNAL_SDL_CreateWindow (ulong flags) + cast at call site")
    else:
        print(f"  [WARN] Patch 1: pattern not found (maybe already applied?)")

    # ---- Patch 2: SDL_CreatePopupWindow (public DllImport) ----
    # Rename DllImport to INTERNAL_SDL_CreatePopupWindow with ulong,
    # add public wrapper that takes SDL_WindowFlags.
    old2 = (
        '[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tpublic static extern IntPtr SDL_CreatePopupWindow(IntPtr parent, int offset_x, int offset_y, int w, int h, SDL_WindowFlags flags);\n'
    )
    new2 = (
        '[DllImport(nativeLibName, EntryPoint = "SDL_CreatePopupWindow", CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tprivate static extern IntPtr INTERNAL_SDL_CreatePopupWindow(IntPtr parent, int offset_x, int offset_y, int w, int h, ulong flags);\n'
        '\t\tpublic static IntPtr SDL_CreatePopupWindow(IntPtr parent, int offset_x, int offset_y, int w, int h, SDL_WindowFlags flags)\n'
        '\t\t{\n'
        '\t\t\treturn INTERNAL_SDL_CreatePopupWindow(parent, offset_x, offset_y, w, h, (ulong)flags);\n'
        '\t\t}\n'
    )
    if old2 in text:
        text = text.replace(old2, new2)
        patches_applied += 1
        print(f"  [OK] Patch 2: SDL_CreatePopupWindow (renamed to INTERNAL_*, ulong, public wrapper)")
    else:
        print(f"  [WARN] Patch 2: pattern not found (maybe already applied?)")

    # ---- Patch 3: SDL_GetWindowFlags (public DllImport returning SDL_WindowFlags) ----
    # Rename DllImport to INTERNAL_SDL_GetWindowFlags returning ulong,
    # add public wrapper that casts back to SDL_WindowFlags.
    old3 = (
        '[DllImport(nativeLibName, CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tpublic static extern SDL_WindowFlags SDL_GetWindowFlags(IntPtr window);\n'
    )
    new3 = (
        '[DllImport(nativeLibName, EntryPoint = "SDL_GetWindowFlags", CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tprivate static extern ulong INTERNAL_SDL_GetWindowFlags(IntPtr window);\n'
        '\t\tpublic static SDL_WindowFlags SDL_GetWindowFlags(IntPtr window)\n'
        '\t\t{\n'
        '\t\t\treturn (SDL_WindowFlags)INTERNAL_SDL_GetWindowFlags(window);\n'
        '\t\t}\n'
    )
    if old3 in text:
        text = text.replace(old3, new3)
        patches_applied += 1
        print(f"  [OK] Patch 3: SDL_GetWindowFlags (renamed to INTERNAL_*, ulong return, public wrapper)")
    else:
        print(f"  [WARN] Patch 3: pattern not found (maybe already applied?)")

    # ---- Patch 4: INTERNAL_SDL_CreateWindowAndRenderer (private DllImport) ----
    # Change SDL_WindowFlags → ulong in the DllImport signature.
    # Cast at the call site (just below, in the public wrapper).
    old4 = (
        '[DllImport(nativeLibName, EntryPoint = "SDL_CreateWindowAndRenderer", CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tprivate static extern SDLBool INTERNAL_SDL_CreateWindowAndRenderer(byte* title, int width, int height, SDL_WindowFlags window_flags, out IntPtr window, out IntPtr renderer);\n'
        '\t\tpublic static SDLBool SDL_CreateWindowAndRenderer(string title, int width, int height, SDL_WindowFlags window_flags, out IntPtr window, out IntPtr renderer)\n'
        '\t\t{\n'
        '\t\t\tvar titleUTF8 = EncodeAsUTF8(title);\n'
        '\t\t\tvar result = INTERNAL_SDL_CreateWindowAndRenderer(titleUTF8, width, height, window_flags, out window, out renderer);\n'
    )
    new4 = (
        '[DllImport(nativeLibName, EntryPoint = "SDL_CreateWindowAndRenderer", CallingConvention = CallingConvention.Cdecl)]\n'
        '\t\tprivate static extern SDLBool INTERNAL_SDL_CreateWindowAndRenderer(byte* title, int width, int height, ulong window_flags, out IntPtr window, out IntPtr renderer);\n'
        '\t\tpublic static SDLBool SDL_CreateWindowAndRenderer(string title, int width, int height, SDL_WindowFlags window_flags, out IntPtr window, out IntPtr renderer)\n'
        '\t\t{\n'
        '\t\t\tvar titleUTF8 = EncodeAsUTF8(title);\n'
        '\t\t\tvar result = INTERNAL_SDL_CreateWindowAndRenderer(titleUTF8, width, height, (ulong)window_flags, out window, out renderer);\n'
    )
    if old4 in text:
        text = text.replace(old4, new4)
        patches_applied += 1
        print(f"  [OK] Patch 4: INTERNAL_SDL_CreateWindowAndRenderer (ulong window_flags) + cast at call site")
    else:
        print(f"  [WARN] Patch 4: pattern not found (maybe already applied?)")

    if patches_applied > 0:
        path.write_text(text, encoding="utf-8")
        print(f"\nPatched {patches_applied}/4 patterns in {path}")
    else:
        print(f"\nNo patches applied (file may already be patched or pattern doesn't match)")
    return patches_applied


def verify_no_remaining_enum_in_dllimports(path: Path) -> int:
    """Verify no DllImport in the file still uses SDL_WindowFlags. Returns count of remaining."""
    text = path.read_text(encoding="utf-8")
    # Find all DllImport method declarations
    # Pattern: [DllImport(...)] ... extern ... SDL_WindowFlags ...
    pattern = r'\[DllImport[^\]]+\][^\[]*?extern[^;]+SDL_WindowFlags[^;]*;'
    matches = re.findall(pattern, text, re.DOTALL)
    if matches:
        print(f"\n[FAIL] {len(matches)} DllImport(s) still use SDL_WindowFlags:")
        for m in matches[:5]:
            preview = re.sub(r'\s+', ' ', m)[:200]
            print(f"  - {preview}")
    else:
        print(f"\n[OK] No DllImports use SDL_WindowFlags anymore (in {path.name})")
    return len(matches)


def main():
    if len(sys.argv) < 2:
        # Default: search common locations
        candidates = [
            Path("/tmp/FNA/lib/SDL3-CS/SDL3/SDL3.Legacy.cs"),
            Path("/tmp/FNA/lib/SDL3-CS/SDL3/SDL3.Core.cs"),
        ]
        # Find all .cs files in /tmp/FNA/lib/SDL3-CS
        sdl3cs_dir = Path("/tmp/FNA/lib/SDL3-CS")
        if sdl3cs_dir.exists():
            candidates = list(sdl3cs_dir.rglob("*.cs"))
    else:
        candidates = [Path(p) for p in sys.argv[1:]]

    total_patches = 0
    total_remaining = 0
    for path in candidates:
        if not path.exists():
            continue
        # Only process files that contain SDL_WindowFlags
        text = path.read_text(encoding="utf-8")
        if "SDL_WindowFlags" not in text:
            continue
        # Only process files that have DllImport
        if "DllImport" not in text:
            continue
        print(f"\n=== Processing {path} ===")
        total_patches += patch_file(path)
        total_remaining += verify_no_remaining_enum_in_dllimports(path)

    print(f"\n=== Summary ===")
    print(f"Total patches applied: {total_patches}")
    print(f"Total remaining SDL_WindowFlags DllImports: {total_remaining}")
    if total_remaining > 0:
        sys.exit(1)


if __name__ == "__main__":
    main()
