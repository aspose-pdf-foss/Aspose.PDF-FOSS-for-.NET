using System.Collections.Concurrent;
using System.IO;

namespace Aspose.Pdf.Text;

/// <summary>
/// A predefined CMap (ISO 32000-1 9.7.5.2, Table 118) a non-embedded CID font is declared
/// with: the codes its text is written in and the CID each one selects. The Unicode CMaps
/// (UniJIS-UCS2-H and its Chinese and Korean twins) take every character as its two-byte
/// UCS-2 code; 90ms-RKSJ-H takes Shift-JIS and GBK-EUC-H takes GBK, a byte for ASCII (and
/// half-width katakana) and two for the rest. The tables are Adobe's (see
/// Text/Resources/CMap.NOTICE.txt), written by build/cmap-tables.py: the codespace ranges as
/// (first code, last code, bytes per code), then sorted (first code, last code, first CID) ranges.
/// </summary>
internal sealed class PredefinedCMap
{
    /// <summary>The Shift-JIS CMap, the one legacy encoding a face can be declared in.</summary>
    public const string ShiftJis = "90ms-RKSJ-H";

    private const int RangeFields = 3;
    private const int ByteBits = 8;
    private const int ByteMask = 0xFF;
    private const string ResourcePrefix = "Aspose.Pdf.Text.Resources.CMap.";
    private const string ResourceSuffix = ".bin";

    /// <summary>What a character the encoding cannot write is written as.</summary>
    private const char Unwritable = '?';

    private static readonly ConcurrentDictionary<string, PredefinedCMap> Loaded = new();

    /// <summary>(first code, last code, bytes per code) per codespace range.</summary>
    private readonly ushort[] _codespace;
    private readonly ushort[] _ranges;

    public string Name { get; }

    private bool IsShiftJis => Name == ShiftJis;

    private PredefinedCMap(string name, ushort[] codespace, ushort[] ranges)
    {
        Name = name;
        _codespace = codespace;
        _ranges = ranges;
    }

    public static PredefinedCMap Get(string name) => Loaded.GetOrAdd(name, Load);

    /// <summary>The CMap <paramref name="name"/> names, or null when the library carries no table for it.</summary>
    public static PredefinedCMap? TryGet(string name) =>
        typeof(PredefinedCMap).Assembly.GetManifestResourceInfo(ResourcePrefix + name + ResourceSuffix) is null ? null : Get(name);

    private static PredefinedCMap Load(string name)
    {
        var asm = typeof(PredefinedCMap).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + name + ResourceSuffix)
            ?? throw new InvalidOperationException("CMap table resource missing: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        var raw = bytes.ToArray();
        var fields = new ushort[raw.Length / 2];
        for (var i = 0; i < fields.Length; i++)
            fields[i] = (ushort)(raw[i * 2] | (raw[i * 2 + 1] << ByteBits));
        // The first triple counts the codespace ranges that follow it; the rest are CID ranges.
        var codespaceFields = fields[0] * RangeFields;
        var codespace = new ushort[codespaceFields];
        Array.Copy(fields, RangeFields, codespace, 0, codespaceFields);
        var ranges = new ushort[fields.Length - RangeFields - codespaceFields];
        Array.Copy(fields, RangeFields + codespaceFields, ranges, 0, ranges.Length);
        return new PredefinedCMap(name, codespace, ranges);
    }

    /// <summary>The codes <paramref name="text"/> is written in, one per character.</summary>
    public int[] Codes(string text)
    {
        var codes = new int[text.Length];
        for (var i = 0; i < text.Length; i++) codes[i] = Code(text[i]);
        return codes;
    }

    private int Code(char c)
    {
        if (!IsShiftJis) return c;
        return SjisTable.FromUnicode(c) ?? Unwritable;
    }

    /// <summary>The codes a show string's <paramref name="bytes"/> hold, each as long as the
    /// codespace range its first byte starts (9.7.6.2); a trailing partial code is dropped.</summary>
    public int[] Codes(byte[] bytes)
    {
        var codes = new System.Collections.Generic.List<int>(bytes.Length);
        var i = 0;
        while (i < bytes.Length)
        {
            var width = CodeWidth(bytes[i]);
            if (i + width > bytes.Length) break;
            var code = 0;
            for (var k = 0; k < width; k++) code = (code << ByteBits) | bytes[i + k];
            codes.Add(code);
            i += width;
        }
        return codes.ToArray();
    }

    /// <summary>The bytes of <paramref name="codes"/> in a content stream: as many per code
    /// as the codespace range holding it declares.</summary>
    public byte[] Bytes(int[] codes)
    {
        var bytes = new System.Collections.Generic.List<byte>(codes.Length * 2);
        foreach (var code in codes)
        {
            for (var k = CodeWidth(code) - 1; k >= 0; k--)
                bytes.Add((byte)((code >> (k * ByteBits)) & ByteMask));
        }
        return bytes.ToArray();
    }

    /// <summary>The CID <paramref name="code"/> selects: 0, the missing glyph, when the
    /// CMap does not map it (9.7.6.3).</summary>
    public int Cid(int code)
    {
        int lo = 0, hi = _ranges.Length / RangeFields - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var at = mid * RangeFields;
            if (code < _ranges[at]) hi = mid - 1;
            else if (code > _ranges[at + 1]) lo = mid + 1;
            else return _ranges[at + 2] + code - _ranges[at];
        }
        return 0;
    }

    /// <summary>Bytes per code of the codespace range holding <paramref name="code"/> — a
    /// whole code, or its first byte alone, which decides the width while reading (9.7.6.2):
    /// a range's first bytes are those of its first and last code. A code outside every range
    /// takes the shortest width the CMap declares.</summary>
    private int CodeWidth(int code)
    {
        var shortest = int.MaxValue;
        for (var at = 0; at < _codespace.Length; at += RangeFields)
        {
            int first = _codespace[at], last = _codespace[at + 1], width = _codespace[at + 2];
            if (width < shortest) shortest = width;
            var leadShift = code <= ByteMask && width > 1 ? (width - 1) * ByteBits : 0;
            if (code >= first >> leadShift && code <= last >> leadShift) return width;
        }
        return shortest;
    }
}
