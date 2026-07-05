# Real SDV.dll System.IO Coverage Analysis

**Date:** 2026-07-05
**Source:** Real GOG `Stardew Valley.dll` v1.6.15.24356 (6.2MB)
**Tool:** `/tmp/sdv-analyzer/` (Cecil-based static IL scanner)

## Summary

| Metric | Count |
|--------|-------|
| Total unique System.IO call patterns | 25 |
| Covered by _rewriteMap (Phase 2.75) | 5 (20%) |
| **Gaps needing coverage** | **13** |
| Path.* methods (no rewrite needed — pure string ops) | 7 |
| FileStream constructors | 1 |

## Coverage gaps (must add to _rewriteMap + SdvFileShim)

### File read operations
| Pattern | Call count | SdvFileShim method needed |
|---------|-----------|--------------------------|
| `File.Open(string, FileMode)` | 4 | `Open(string, FileMode)` → returns Stream |
| `File.Open(string, FileMode, FileAccess)` | 1 | `Open(string, FileMode, FileAccess)` → returns Stream |
| `File.ReadAllLines(string)` | 1 | `ReadAllLines(string)` → returns string[] |

### File write operations
| Pattern | Call count | SdvFileShim method needed |
|---------|-----------|--------------------------|
| `File.AppendAllText(string, string)` | 2 | `AppendAllText(string, string)` |
| `File.Create(string)` | 2 | `Create(string)` → returns Stream |
| `File.CreateText(string)` | 1 | `CreateText(string)` → returns StreamWriter |
| `File.Delete(string)` | 1 | `Delete(string)` |
| `File.Move(string, string, bool)` | 1 | `Move(string, string, bool)` |
| `File.WriteAllText(string, string)` | 3 | `WriteAllText(string, string)` |

### Directory operations
| Pattern | Call count | SdvFileShim method needed |
|---------|-----------|--------------------------|
| `Directory.CreateDirectory(string)` | 3 | `CreateDirectory(string)` |
| `Directory.Delete(string, bool)` | 1 | `DeleteDirectory(string, bool)` |
| `Directory.EnumerateDirectories(string)` | 1 | `EnumerateDirectories(string)` → returns string[] |

### FileStream constructors
| Pattern | Call count | SdvFileShim method needed |
|---------|-----------|--------------------------|
| `new FileStream(string, FileMode)` | 2 | Custom handling needed (constructor rewrite) |

## NOT needing rewrite (pure string manipulation)

These `Path.*` methods do NO file I/O — they just manipulate strings.
They work fine in WASM without rewriting:
- `Path.Combine(2 params)` — 21 calls
- `Path.Combine(3 params)` — 9 calls
- `Path.GetDirectoryName(1 params)` — 2 calls
- `Path.GetExtension(1 params)` — 1 call
- `Path.GetFileName(1 params)` — 2 calls
- `Path.GetFileNameWithoutExtension(1 params)` — 2 calls
- `Path.GetInvalidFileNameChars(0 params)` — 1 call

## Phase 2.8 plan

1. Add 13 methods to `SdvFileShim`
2. Add 13 entries to `_rewriteMap` (FileStream constructor needs special handling)
3. Add `IVirtualFileSystem` methods for write operations (WriteFileAsync exists; need CreateDirectory, DeleteFile, etc.)
4. Re-run analyzer to verify 100% coverage (excluding Path.*)
5. Try loading real SDV.dll in SdvBlazor
