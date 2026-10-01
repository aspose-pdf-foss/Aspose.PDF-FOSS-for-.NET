using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    // bfchar entries per block: the CMap syntax allows at most this many.
    private const int CMapBlockSize = 100;

    /// <summary>
    /// PDF/UA-1 clause 7.21.7: every character code a font shows maps to Unicode. A code its
    /// /ToUnicode leaves out, and its encoding does not name with a standard glyph name, takes
    /// the character the embedded TrueType program's own Unicode cmap gives its glyph (the
    /// glyph reached through /CIDToGIDMap, or through the program's own name for it). Nothing
    /// is guessed: a glyph the program gives no character stays unmapped, as does a
    /// private-use or control point. Every entry the font already had is kept as it is.
    /// </summary>
    private void FillUnicodeMappings()
    {
        foreach (var (_, font, _) in DocumentFonts())
        {
            try { FillUnicodeMapping(font); }
            catch { /* a font this library cannot read keeps its mapping as it is */ }
        }
    }

    private void FillUnicodeMapping(PdfDictionary font)
    {
        var subtype = font.GetName("Subtype");
        var composite = subtype == "Type0";
        if (subtype is "Type1" or "MMType1") { FillFromBaseEncoding(font); return; }
        if (!composite && subtype != "TrueType") return;

        // The program's glyphs and the characters its Unicode cmap gives them.
        var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
        PdfDictionary? cidFont = null;
        if (composite && _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } d)
        {
            cidFont = _reader.ResolveDict(d[0]);
            descriptor = _reader.ResolveDict(cidFont?.Get("FontDescriptor"));
        }
        if (SfntProgram(descriptor) is not { } stream) return;
        var program = _reader.DecodeStream(stream);
        var subtables = Text.TrueTypeAdvances.CmapSubtables(program);
        if (!subtables.Any(s => s.Platform == 0 || s.Platform == 3 && s.Encoding is 1 or 10)) return;
        var parser = new Text.TrueTypeParser(program);
        parser.Parse();
        var unicodeOfGlyph = new Dictionary<int, int>();
        foreach (var (codePoint, gid) in parser.CMap)
            if (!unicodeOfGlyph.TryGetValue(gid, out var have) || codePoint < have) unicodeOfGlyph[gid] = codePoint;

        var existing = ReadToUnicode(font);
        byte[]? cidToGid = composite && _reader.Resolve(cidFont?.Get("CIDToGIDMap")) is PdfStream mapStream
            ? _reader.DecodeStream(mapStream) : null;
        string?[]? names = composite ? null : EncodingGlyphNames(font);
        Dictionary<string, int>? glyphOfName = null;

        var added = new Dictionary<int, string>();
        foreach (var code in ShownCodes(font, composite))
        {
            // What the font already says about a code stands, whatever it says.
            if (existing.ContainsKey(code)) continue;
            int gid;
            if (composite)
                gid = cidToGid is null ? code : code * 2 + 1 < cidToGid.Length ? (cidToGid[code * 2] << 8) | cidToGid[code * 2 + 1] : 0;
            else
            {
                // A standard glyph name already says which character the code is; another name
                // is looked up among the program's own glyph names.
                if (names![code] is not { } name || Text.TextAbsorber.GlyphNameToUnicode.ContainsKey(name)) continue;
                glyphOfName ??= parser.GlyphNames.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Min(p => p.Key));
                if (!glyphOfName.TryGetValue(name, out gid))
                {
                    if (BaseEncodingText(font, code) is { } fromBase) added[code] = fromBase;
                    continue;
                }
            }
            if (gid <= 0 || !unicodeOfGlyph.TryGetValue(gid, out var unicode)) continue;
            var text = char.ConvertFromUtf32(unicode);
            if (IsRealText(text)) added[code] = text;
        }
        // A /ToUnicode whose code space does not span the font's codes (a composite font's
        // CMap declaring <0003> <005C>) leaves a reader unable to use any of it; the same
        // entries are written again over the whole space.
        var fixedWidthCodes = !composite || font.GetName("Encoding") is "Identity-H" or "Identity-V";
        if (added.Count == 0 && (existing.Count == 0 || !fixedWidthCodes || CodeSpaceSpansAll(font, composite))) return;

        foreach (var (code, text) in existing) added.TryAdd(code, text);
        WriteToUnicode(font, added, composite ? 2 : 1);
    }

    // The /ToUnicode maps this conversion has written: the streams are new objects a read
    // through the file does not reach yet.
    private readonly Dictionary<PdfDictionary, Dictionary<int, string>> _writtenToUnicode =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>The font's code-to-text map: the one this conversion wrote, else the file's.</summary>
    private Dictionary<int, string> ReadToUnicode(PdfDictionary font) =>
        _writtenToUnicode.TryGetValue(font, out var written) ? written
            : Text.TextAbsorber.ParseToUnicodeFromDict(font, _reader) ?? new Dictionary<int, string>();

    /// <summary>Give the font a /ToUnicode of <paramref name="byteWidth"/>-byte codes.</summary>
    private void WriteToUnicode(PdfDictionary font, Dictionary<int, string> map, int byteWidth)
    {
        var objectNumber = AllocateObjectNumber();
        AddNewObject(objectNumber, new PdfStream(new PdfDictionary(), ToUnicodeCMap(map, byteWidth)));
        font.Set("ToUnicode", new PdfIndirectRef(objectNumber, 0));
        _writtenToUnicode[font] = map;
    }

    /// <summary>The codes a font can show: a simple font's /FirstChar to /LastChar where a
    /// width is stated; a composite font's CIDs its /W lists or its CIDToGIDMap maps to a
    /// glyph (Identity encodings, where the code is the CID).</summary>
    private IEnumerable<int> ShownCodes(PdfDictionary font, bool composite)
    {
        if (!composite)
        {
            var first = (int)((_reader.Resolve(font.Get("FirstChar")) as PdfInteger)?.Value ?? 0);
            if (_reader.Resolve(font.Get("Widths")) is not PdfArray widths) yield break;
            for (var i = 0; i < widths.Count && first + i < 256; i++)
                if (StatedWidth(widths[i]) is not null) yield return first + i;
            yield break;
        }
        if (font.GetName("Encoding") is not ("Identity-H" or "Identity-V")) yield break;
        if (_reader.Resolve(font.Get("DescendantFonts")) is not PdfArray { Count: > 0 } descendants
            || _reader.ResolveDict(descendants[0]) is not { } cidFont) yield break;
        var codes = new SortedSet<int>(Text.TrueTypeAdvances.CidWidths(_reader.Resolve(cidFont.Get("W")) as PdfArray, _reader).Keys);
        if (_reader.Resolve(cidFont.Get("CIDToGIDMap")) is PdfStream mapStream)
        {
            var map = _reader.DecodeStream(mapStream);
            for (var cid = 1; cid * 2 + 1 < map.Length; cid++)
                if (map[cid * 2] != 0 || map[cid * 2 + 1] != 0) codes.Add(cid);
        }
        foreach (var code in codes)
            if (code is > 0 and <= 0xFFFF) yield return code;
    }

    /// <summary>A Type 1 font whose /Differences give codes names of the producer's own
    /// ("g17"), over an explicit /BaseEncoding: a code whose text, as this library reads it,
    /// is the very character the base encoding puts at that code is mapped to it - the font's
    /// encoding and its reading agree. A code where they part is left unmapped.</summary>
    private void FillFromBaseEncoding(PdfDictionary font)
    {
        var existing = ReadToUnicode(font);
        var names = EncodingGlyphNames(font);
        var added = new Dictionary<int, string>();
        foreach (var code in ShownCodes(font, composite: false))
        {
            if (existing.ContainsKey(code) || names[code] is not { } name
                || Text.TextAbsorber.GlyphNameToUnicode.ContainsKey(name)) continue;
            if (BaseEncodingText(font, code) is { } text) added[code] = text;
        }
        if (added.Count == 0) return;
        foreach (var (code, text) in existing) added.TryAdd(code, text);
        WriteToUnicode(font, added, 1);
    }

    /// <summary>The character an explicit /BaseEncoding puts at <paramref name="code"/>, when
    /// the text this library reads for the code is that very character; null otherwise.</summary>
    private string? BaseEncodingText(PdfDictionary font, int code)
    {
        if (_reader.Resolve(font.Get("Encoding")) is not PdfDictionary encoding) return null;
        var baseGlyph = encoding.GetName("BaseEncoding") switch
        {
            "WinAnsiEncoding" => Text.PdfEncodings.WinAnsiName(code),
            "MacRomanEncoding" => Text.PdfEncodings.MacRomanName(code),
            "StandardEncoding" => Text.Type1StandardEncoding.GetName(code),
            _ => null,
        };
        if (baseGlyph is null || !Text.TextAbsorber.GlyphNameToUnicode.TryGetValue(baseGlyph, out var expected)
            || !IsRealText(expected)) return null;
        var read = Text.TextAbsorber.DecodeStringPublic([(byte)code], null, font, _reader, foldNbsp: false);
        return read == expected ? expected : null;
    }

    /// <summary>True when the font's /ToUnicode declares the whole code space of its codes:
    /// &lt;00&gt; &lt;FF&gt; for a simple font, &lt;0000&gt; &lt;FFFF&gt; for a composite one.</summary>
    private bool CodeSpaceSpansAll(PdfDictionary font, bool composite)
    {
        if (_reader.ResolveStream(font.Get("ToUnicode")) is not { } stream) return true;
        string text;
        try { text = Encoding.ASCII.GetString(_reader.DecodeStream(stream)); }
        catch { return true; }
        var start = text.IndexOf("begincodespacerange", StringComparison.Ordinal);
        var end = text.IndexOf("endcodespacerange", StringComparison.Ordinal);
        if (start < 0 || end < start) return true;
        var ranges = System.Text.RegularExpressions.Regex.Matches(text.Substring(start, end - start), "<([0-9A-Fa-f]+)>\\s*<([0-9A-Fa-f]+)>");
        var whole = composite ? ("0000", "FFFF") : ("00", "FF");
        return ranges.Cast<System.Text.RegularExpressions.Match>().Any(m =>
            string.Equals(m.Groups[1].Value, whole.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Groups[2].Value, whole.Item2, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRealText(string? text) =>
        !string.IsNullOrEmpty(text)
        && text.All(c => c >= 0x20 && c != '�' && c != '￾' && c != '﻿' && !(c >= '' && c <= ''));

    /// <summary>A ToUnicode CMap of <paramref name="byteWidth"/>-byte codes.</summary>
    private static byte[] ToUnicodeCMap(Dictionary<int, string> map, int byteWidth)
    {
        var codeFormat = byteWidth == 1 ? "X2" : "X4";
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append(byteWidth == 1 ? "1 begincodespacerange\n<00> <FF>\nendcodespacerange\n"
            : "1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var entries = map.OrderBy(p => p.Key).ToList();
        for (var i = 0; i < entries.Count; i += CMapBlockSize)
        {
            var block = entries.Skip(i).Take(CMapBlockSize).ToList();
            sb.Append($"{block.Count} beginbfchar\n");
            foreach (var (code, text) in block)
                sb.Append('<').Append(code.ToString(codeFormat)).Append("> <")
                  .Append(string.Concat(text.Select(c => ((int)c).ToString("X4")))).Append(">\n");
            sb.Append("endbfchar\n");
        }
        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
