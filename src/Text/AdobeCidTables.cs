// Adobe CID-to-Unicode tables for predefined CID collections.
// Ported from @aspose/pdf-foss TypeScript: src/fonts/adobe-cid-tables.ts
// Generated from Adobe-Japan1-UCS2, Adobe-CNS1-UCS2, Adobe-GB1-UCS2, Adobe-Korea1-UCS2 bcmaps.
// Each table is an embedded .bin resource (Text/Resources/AdobeCid.<Ordering>.bin) holding the pair array
// [cid0, unicode0, cid1, unicode1, ...] as uint16 little-endian.
// Lookup: binary search on CID in even positions, Unicode at odd position.

using System;

namespace Aspose.Pdf.Text;

/// <summary>
/// Adobe CID-to-Unicode lookup tables for predefined CID collections (Japan1, CNS1, GB1, Korea1).
/// </summary>
internal static class AdobeCidTables
{
    private static ushort[]? _japan1;
    private static ushort[]? _cns1;
    private static ushort[]? _gb1;
    private static ushort[]? _korea1;

    /// <summary>Looks up the Unicode codepoint for the given CID in the specified Adobe collection ordering.</summary>
    public static int? LookupCid(string ordering, int cid)
    {
        var table = GetTable(ordering);
        if (table is null) return null;
        return BinarySearch(table, cid);
    }

    private static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<int, int>> _inverse = new();

    /// <summary>
    /// Reverse lookup: the CID for a Unicode codepoint in the given ordering. Lets a
    /// non-embedded predefined-CMap font reach the PDF's CID-keyed /W widths after we
    /// decode its bytes straight to Unicode. Built (and cached) by inverting the
    /// CID→Unicode table; the first CID wins on the rare many-to-one collisions.
    /// </summary>
    public static int? UnicodeToCid(string ordering, int unicode)
    {
        System.Collections.Generic.Dictionary<int, int>? map;
        lock (_inverse)
        {
            if (!_inverse.TryGetValue(ordering, out map))
            {
                var table = GetTable(ordering);
                map = new System.Collections.Generic.Dictionary<int, int>(table is null ? 0 : table.Length / 2);
                if (table is not null)
                    for (int i = 0; i < table.Length; i += 2)
                        map.TryAdd(table[i + 1], table[i]);
                _inverse[ordering] = map;
            }
        }
        return map.TryGetValue(unicode, out var cid) ? cid : null;
    }

    /// <summary>Largest CID present in the ordering's table (0 when the
    /// ordering is unknown). Used to size a synthesised /CIDToGIDMap.</summary>
    public static int MaxCid(string ordering)
    {
        var table = GetTable(ordering);
        return table is { Length: >= 2 } ? table[^2] : 0;
    }

    private static ushort[]? GetTable(string ordering) => ordering switch
    {
        "Japan1"  => _japan1  ??= LoadTable("Japan1"),
        "CNS1"    => _cns1    ??= LoadTable("CNS1"),
        "GB1"     => _gb1     ??= LoadTable("GB1"),
        "Korea1"  => _korea1  ??= LoadTable("Korea1"),
        _         => null
    };

    /// <summary>Reads one ordering's CID table from its embedded resource: the little-endian
    /// UInt16 pairs the table has always been stored as, formerly inside a base64 string
    /// constant, now as the bytes themselves.</summary>
    private static ushort[] LoadTable(string ordering)
    {
        var asm = typeof(AdobeCidTables).Assembly;
        using var stream = asm.GetManifestResourceStream("Aspose.Pdf.Text.Resources.AdobeCid." + ordering + ".bin")
            ?? throw new InvalidOperationException("CID table resource missing: " + ordering);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ToU16(ms.ToArray());
    }

    private static int? BinarySearch(ushort[] table, int cid)
    {
        int lo = 0, hi = (table.Length >> 1) - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var c = table[mid * 2];
            if (c == cid) return table[mid * 2 + 1];
            if (c < cid) lo = mid + 1;
            else hi = mid - 1;
        }
        return null;
    }

    private static ushort[] ToU16(byte[] raw)
    {
        var result = new ushort[raw.Length / 2];
        for (int i = 0; i < result.Length; i++)
            result[i] = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << 8));
        return result;
    }
}
