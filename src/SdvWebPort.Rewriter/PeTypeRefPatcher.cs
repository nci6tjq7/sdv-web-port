using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SdvWebPort.Rewriter;

/// <summary>
/// Directly patches PE metadata bytes to rewrite TypeRef ResolutionScope.
/// Modifies the raw PE bytes without going through Cecil, bypassing all caching.
/// </summary>
public static class PeTypeRefPatcher
{
    public static byte[] PatchTypeRefScopes(byte[] assemblyBytes,
        Dictionary<string, string> typeFullNameToKniAssembly,
        string mgAssemblyRefName)
    {
        Console.WriteLine("[PeTypeRefPatcher] Starting direct PE byte patching");

        using var peReader = new PEReader(new MemoryStream(assemblyBytes));
        var md = peReader.GetMetadataReader();

        // 1. Find AssemblyRef row numbers
        var asmRefRows = new Dictionary<int, string>(); // row → name
        int mgRow = -1;
        var kniRows = new Dictionary<string, int>(); // name → row

        foreach (var arHandle in md.AssemblyReferences)
        {
            var ar = md.GetAssemblyReference(arHandle);
            var name = md.GetString(ar.Name);
            var row = MetadataTokens.GetRowNumber(arHandle);
            asmRefRows[row] = name;
            if (name == mgAssemblyRefName) mgRow = row;
            if (name.StartsWith("Xna.Framework")) kniRows[name] = row;
        }

        if (mgRow < 0)
        {
            Console.WriteLine("[PeTypeRefPatcher] MonoGame.Framework not found — skipping");
            return assemblyBytes;
        }

        // 2. Collect typeref patches: (typeref row → new assembly ref row)
        var patches = new Dictionary<int, int>(); // typeref row → new asmref row

        foreach (var trHandle in md.TypeReferences)
        {
            var tr = md.GetTypeReference(trHandle);
            if (tr.ResolutionScope.Kind != HandleKind.AssemblyReference) continue;
            var scopeRow = MetadataTokens.GetRowNumber(tr.ResolutionScope);
            if (scopeRow != mgRow) continue;

            var ns = md.GetString(tr.Namespace);
            var name = md.GetString(tr.Name);
            var fullName = string.IsNullOrEmpty(ns) ? name : ns + "." + name;

            if (!typeFullNameToKniAssembly.TryGetValue(fullName, out var kniName))
            {
                // Try fuzzy: match by type name only
                foreach (var kv in typeFullNameToKniAssembly)
                {
                    if (kv.Key.EndsWith("." + name) || kv.Key == name)
                    {
                        kniName = kv.Value;
                        break;
                    }
                }
            }

            if (kniName == null || !kniRows.TryGetValue(kniName, out var kniRow)) continue;

            var trRow = MetadataTokens.GetRowNumber(trHandle);
            patches[trRow] = kniRow;
        }

        Console.WriteLine($"[PeTypeRefPatcher] {patches.Count} typeref scope patches");
        if (patches.Count == 0) return assemblyBytes;

        // 3. Get PE layout info
        var headers = peReader.PEHeaders;
        var metadataStart = headers.MetadataStartOffset;
        
        // Get the raw metadata bytes
        var result = (byte[])assemblyBytes.Clone();
        
        // 4. Find the TypeRef table offset in the metadata
        // We need to parse the metadata header to find table stream (#~)
        var metadataLength = headers.MetadataSize;
        
        // Parse metadata root header
        int pos = metadataStart;
        // Skip signature (4 bytes), major version (2), minor version (2), 
        // reserved (4), version string length (4), version string (padded),
        // flags (2), stream count (2)
        pos += 4 + 2 + 2 + 4; // sig + major + minor + reserved
        int versionLen = BitConverter.ToInt32(result, pos);
        pos += 4;
        pos += versionLen; // version string
        pos += 2; // flags
        int streamCount = BitConverter.ToInt16(result, pos);
        pos += 2;
        
        int tableStreamOffset = -1;
        int tableStreamSize = -1;
        
        for (int i = 0; i < streamCount; i++)
        {
            int offset = BitConverter.ToInt32(result, pos);
            int size = BitConverter.ToInt32(result, pos + 4);
            pos += 8;
            // Read stream name (null-terminated, 4-byte aligned)
            int nameStart = pos;
            while (result[pos] != 0) pos++;
            string streamName = System.Text.Encoding.ASCII.GetString(result, nameStart, pos - nameStart);
            pos++; // skip null terminator
            // Align to 4 bytes
            while (pos % 4 != 0) pos++;
            
            if (streamName == "#~" || streamName == "#-")
            {
                tableStreamOffset = metadataStart + offset;
                tableStreamSize = size;
            }
        }
        
        if (tableStreamOffset < 0)
        {
            Console.WriteLine("[PeTypeRefPatcher] Table stream not found!");
            return assemblyBytes;
        }

        // 5. Parse the table stream header
        // Reserved (4), MajorVersion (1), MinorVersion (1), HeapSizes (1), Reserved (1)
        // Valid (8), Sorted (8), Rows (4 per table, up to 64 tables)
        pos = tableStreamOffset;
        pos += 4 + 1 + 1 + 1 + 1; // reserved + major + minor + heapsizes + reserved
        ulong valid = BitConverter.ToUInt64(result, pos);
        pos += 8; // valid
        pos += 8; // sorted
        ulong sorted = 0; // not needed
        
        // Read row counts for all valid tables
        var rowCounts = new int[64];
        for (int i = 0; i < 64; i++)
        {
            if ((valid & (1UL << i)) != 0)
            {
                rowCounts[i] = BitConverter.ToInt32(result, pos);
                pos += 4;
            }
        }
        
        // 6. Calculate table offsets
        // Tables are stored in order: 0, 1, 2, ... 63
        // Each table's offset = previous table offset + previous table size
        // Table size = row count * row size
        
        // First, determine heap index sizes (from HeapSizes byte)
        byte heapSizes = result[tableStreamOffset + 6];
        int stringIdxSize = (heapSizes & 1) != 0 ? 4 : 2;
        int guidIdxSize = (heapSizes & 2) != 0 ? 4 : 2;
        int blobIdxSize = (heapSizes & 4) != 0 ? 4 : 2;
        
        // ResolutionScope coded index: tags 0=Module, 1=ModuleRef, 2=AssemblyRef, 3=TypeRef
        // 2 bits for tag. Max row count of Module, ModuleRef, AssemblyRef, TypeRef
        int resScopeMaxRows = Math.Max(
            Math.Max(rowCounts[(int)TableIndex.Module], rowCounts[(int)TableIndex.ModuleRef]),
            Math.Max(rowCounts[(int)TableIndex.AssemblyRef], rowCounts[(int)TableIndex.TypeRef]));
        int resScopeIdxSize = resScopeMaxRows < (1 << 14) ? 2 : 4;
        
        // TypeDefOrRef coded index: tags 0=TypeDef, 1=TypeRef, 2=TypeSpec
        int typeDefOrRefMax = Math.Max(
            Math.Max(rowCounts[(int)TableIndex.TypeDef], rowCounts[(int)TableIndex.TypeRef]),
            rowCounts[(int)TableIndex.TypeSpec]);
        int typeDefOrRefIdxSize = typeDefOrRefMax < (1 << 14) ? 2 : 4;
        
        // TypeRef row layout:
        // ResolutionScope (ResolutionScope coded index)
        // TypeName (string heap index)  
        // TypeNamespace (string heap index)
        int typeRefRowSize = resScopeIdxSize + stringIdxSize + stringIdxSize;
        int typeRefRowCount = rowCounts[(int)TableIndex.TypeRef];
        
        // Calculate TypeRef table offset
        // Tables before TypeRef (index 1): Module (0)
        // Table 0 = Module: rows=1, row size = Generation(2) + Name(string) + Mvid(guid) + EncId(guid) + EncBaseId(guid)
        int moduleRowSize = 2 + stringIdxSize + guidIdxSize + guidIdxSize + guidIdxSize;
        
        int tableDataStart = pos; // Start of actual table data
        int typeRefTableOffset = tableDataStart;
        
        // Skip Module table (table 0) if it exists
        if ((valid & 1) != 0)
        {
            typeRefTableOffset += rowCounts[(int)TableIndex.Module] * moduleRowSize;
        }
        
        Console.WriteLine($"[PeTypeRefPatcher] TypeRef table at offset {typeRefTableOffset}, row size={typeRefRowSize}, rows={typeRefRowCount}");
        Console.WriteLine($"[PeTypeRefPatcher] ResolutionScope index size={resScopeIdxSize}");
        
        // 7. Patch each typeref's ResolutionScope
        int patchedCount = 0;
        foreach (var patch in patches)
        {
            int trRow = patch.Key; // 1-based
            int newAsmRefRow = patch.Value; // 1-based
            
            // Calculate byte offset of this typeref's ResolutionScope field
            int rowOffset = typeRefTableOffset + (trRow - 1) * typeRefRowSize;
            
            // ResolutionScope is the first field in TypeRef (actually it's the last in the spec
            // but Cecil/Microsoft implementation puts it first... let me check)
            // ECMA-335 II.22.38: TypeRef = ResolutionScope + TypeName + TypeNamespace
            // Wait, actually the order in the spec is:
            // ResolutionScope (ResolutionScope coded index)
            // TypeName (string heap index)
            // TypeNamespace (string heap index)
            // But some implementations may differ. Let me check by reading a known value.
            
            // Actually, per ECMA-335, the column order is:
            // ResolutionScope, TypeName, TypeNamespace
            // The ResolutionScope for AssemblyRef is encoded as: (row << 2) | 2
            int resScopeOffset = rowOffset; // First column
            
            // Encode new ResolutionScope: AssemblyRef tag=2, so encoded = (row << 2) | 2
            int encoded = (newAsmRefRow << 2) | 2;
            
            if (resScopeIdxSize == 2)
            {
                BitConverter.GetBytes((ushort)encoded).CopyTo(result, resScopeOffset);
            }
            else
            {
                BitConverter.GetBytes(encoded).CopyTo(result, resScopeOffset);
            }
            
            patchedCount++;
            if (patchedCount <= 5)
                Console.WriteLine($"[PeTypeRefPatcher] Patched TypeRef row {trRow}: ResolutionScope → AssemblyRef row {newAsmRefRow}");
        }
        
        Console.WriteLine($"[PeTypeRefPatcher] Patched {patchedCount} typeref entries");
        return result;
    }
}
