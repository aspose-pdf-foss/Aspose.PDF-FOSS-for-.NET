using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

/// <summary>
/// Embeds a TrueType font program as a composite Type0 / CIDFontType2 font with
/// Identity-H encoding and a /ToUnicode CMap, and encodes a string to the 2-byte
/// glyph-id codes that font expects. Shared by the generator (<see cref="TextBuilder"/>)
/// and the stamp pipeline so Unicode/CJK text can be written with an embedded font.
///
/// The font program is embedded ONCE per (resource dict, ttf) — repeated <see cref="Embed"/>
/// calls for the same font on the same page reuse that single Type0 resource and grow its
/// /W widths + /ToUnicode incrementally. Without this, a CJK-heavy document (one call per
/// line) would write the full multi-MB font program hundreds of times.
/// </summary>
internal static class Type0FontEmbedder
{
    // Per resource-dict (≈ per page) → per ttf-program → the one embedded Type0 font.
    private static readonly ConditionalWeakTable<PdfDictionary, PageFonts> _cache = new();

    private sealed class PageFonts
    {
        public readonly Dictionary<byte[], FontState> ByTtf =
            new(ReferenceEqualityComparer.Instance);
    }

    private sealed class FontState
    {
        public string ResName = "";
        public GlyphOutlineParser Parser = null!;
        public double Upm = 1000;
        public readonly Dictionary<int, int> UsedGlyphs = new(); // charCode → glyphId
        public PdfDictionary CidFont = null!;
        public PdfDictionary Type0Font = null!;
        public PdfStream FontFile = null!;   // the embedded /FontFile2 stream
        public byte[] Ttf = null!;           // the ORIGINAL font program (subset source)
        public bool SubsetDirty;             // new glyphs since the last sparse subset
    }

    // Live embedded fonts across all documents — walked at save time so the
    // multi-MB system font program shrinks to a GID-preserving sparse subset
    // of the glyphs actually shown (the full font program is never shipped).
    private static readonly List<WeakReference<FontState>> _liveFonts = new();

    /// <summary>
    /// Sparse-subset every embedded Type0 font program that gained glyphs since
    /// its last subset: unused glyphs lose their outlines while the glyph
    /// numbering stays intact (CIDToGIDMap=Identity content bytes remain valid).
    /// Called from the document save funnel; idempotent between growths, and
    /// safe to run repeatedly — the subset is always cut from the ORIGINAL
    /// program, so glyphs added after an earlier save reappear correctly.
    /// </summary>
    internal static void SparseSubsetEmbeddedFontsForSave()
    {
        lock (_liveFonts)
        {
            for (var i = _liveFonts.Count - 1; i >= 0; i--)
            {
                if (!_liveFonts[i].TryGetTarget(out var st)) { _liveFonts.RemoveAt(i); continue; }
                if (!st.SubsetDirty || st.UsedGlyphs.Count == 0) continue;
                try
                {
                    var gids = new HashSet<int> { 0 }; // keep .notdef
                    foreach (var (_, gid) in st.UsedGlyphs) gids.Add(gid);
                    var parser = new TrueTypeParser(st.Ttf);
                    parser.Parse();
                    var subset = new TrueTypeSubsetter(st.Ttf, parser).SubsetSparse(gids);
                    if (subset.Length > 0 && subset.Length < st.Ttf.Length)
                    {
                        st.FontFile.ReplaceData(subset);
                        st.FontFile.Dict.Set("Length1", new PdfInteger(subset.Length));
                    }
                    st.SubsetDirty = false;
                }
                catch
                {
                    // A malformed program keeps its full embed — correctness over size.
                }
            }
        }
    }

    /// <summary>
    /// Register (or reuse) a Type0 font built from <paramref name="ttfData"/> in
    /// <paramref name="fontDict"/> and return its resource name plus the hex-encoded
    /// (2-byte) glyph ids for <paramref name="text"/>. When <paramref name="stripSpacesInBaseFont"/>
    /// is true the /BaseFont name has its spaces removed (PDF base-font names are space-free,
    /// so the extracted Font.FontName reads back cleanly, e.g. "ArialUnicodeMS").
    /// </summary>
    public static (string resName, byte[] hexGlyphIds) Embed(
        PdfDictionary fontDict, byte[] ttfData, string fontName, string text,
        bool stripSpacesInBaseFont = false)
    {
        var pageFonts = _cache.GetValue(fontDict, static _ => new PageFonts());
        if (!pageFonts.ByTtf.TryGetValue(ttfData, out var st))
        {
            st = BuildFont(fontDict, ttfData, fontName, stripSpacesInBaseFont);
            pageFonts.ByTtf[ttfData] = st;
        }

        // Encode the text to 2-byte glyph ids (CID = GID under Identity), accumulating any
        // newly-seen glyphs so the shared font's /W + /ToUnicode grow to cover them.
        // Iterate by CODEPOINT: a surrogate pair (emoji, CJK Ext-B) is ONE glyph — walking
        // UTF-16 units would look each half up in the cmap and emit two .notdef glyphs.
        var hex = new System.Collections.Generic.List<byte>(text.Length * 2);
        var added = false;
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            var gid = st.Parser.GlyphIdOrLookAlike(cp);
            if (st.UsedGlyphs.TryAdd(cp, gid)) added = true;
            hex.Add((byte)(gid >> 8));
            hex.Add((byte)(gid & 0xFF));
        }
        if (added) { RefreshWidthsAndToUnicode(st); st.SubsetDirty = true; }
        return (st.ResName, hex.ToArray());
    }

    /// <summary>
    /// Measure <paramref name="text"/> at <paramref name="fontSize"/> using the same
    /// rounded 1000-unit advances the embedded font's /W array declares, so stamp
    /// layout agrees exactly with what extraction later measures from the file.
    /// </summary>
    public static double MeasureText(PdfDictionary fontDict, byte[] ttfData, string fontName,
        string text, double fontSize, bool stripSpacesInBaseFont = false)
    {
        var pageFonts = _cache.GetValue(fontDict, static _ => new PageFonts());
        if (!pageFonts.ByTtf.TryGetValue(ttfData, out var st))
        {
            st = BuildFont(fontDict, ttfData, fontName, stripSpacesInBaseFont);
            pageFonts.ByTtf[ttfData] = st;
        }
        double total = 0;
        // Same codepoint walk as Embed: a surrogate pair is ONE glyph.
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                cp = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }
            var gid = st.Parser.GlyphIdOrLookAlike(cp);
            total += Math.Round(st.Parser.GetAdvanceWidth(gid) * 1000.0 / st.Upm);
        }
        return total * fontSize / 1000.0;
    }

    // Build the Type0 font structure once (descriptor + FontFile2 + CIDFont + Type0), register
    // it under a free F-name, and return the mutable state used to grow /W + /ToUnicode later.
    private static FontState BuildFont(PdfDictionary fontDict, byte[] ttfData, string fontName,
        bool stripSpacesInBaseFont)
    {
        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var nameForBaseFont = stripSpacesInBaseFont ? fontName.Replace(" ", "") : fontName;
        var baseFontName = $"{GenerateSubsetTag()}+{nameForBaseFont}";

        var glyphParser = new GlyphOutlineParser(ttfData);
        var (ascent, descent, flags, _) = FontRepository.ReadTtfMetrics(ttfData);
        // Prefer the hhea ascender/descender (like the simple-TrueType
        // embedder): OS/2 typographic values under-report the descent for
        // common UI fonts, shifting absorbed line boxes read back from this embed.
        try
        {
            var hheaParser = new TrueTypeParser(ttfData);
            hheaParser.Parse();
            var hheaScale = 1000.0 / (hheaParser.UnitsPerEm > 0 ? hheaParser.UnitsPerEm : 1000);
            if (hheaParser.Ascent != 0) ascent = (int)(hheaParser.Ascent * hheaScale);
            if (hheaParser.Descent != 0) descent = (int)(hheaParser.Descent * hheaScale);
        }
        catch { }

        var descriptorDict = new PdfDictionary();
        descriptorDict.Set("Type", new PdfName("FontDescriptor"));
        descriptorDict.Set("FontName", new PdfName(baseFontName));
        descriptorDict.Set("Flags", new PdfInteger(flags | 4)); // Symbolic
        descriptorDict.Set("Ascent", new PdfInteger(ascent));
        descriptorDict.Set("Descent", new PdfInteger(descent));
        descriptorDict.Set("ItalicAngle", new PdfInteger(0));
        descriptorDict.Set("CapHeight", new PdfInteger((int)(ascent * 0.8)));
        descriptorDict.Set("StemV", new PdfInteger(80));
        var bboxArr = new PdfArray();
        bboxArr.Add(new PdfInteger(0)); bboxArr.Add(new PdfInteger(descent));
        bboxArr.Add(new PdfInteger(1000)); bboxArr.Add(new PdfInteger(ascent));
        descriptorDict.Set("FontBBox", bboxArr);

        var fontFileStream = new PdfStream(new PdfDictionary(), ttfData);
        fontFileStream.Dict.Set("Length1", new PdfInteger(ttfData.Length));
        descriptorDict.Set("FontFile2", fontFileStream);

        var cidFont = new PdfDictionary();
        cidFont.Set("Type", new PdfName("Font"));
        cidFont.Set("Subtype", new PdfName("CIDFontType2"));
        cidFont.Set("BaseFont", new PdfName(baseFontName));
        var cidSystemInfo = new PdfDictionary();
        cidSystemInfo.Set("Registry", new PdfString(System.Text.Encoding.ASCII.GetBytes("Adobe")));
        cidSystemInfo.Set("Ordering", new PdfString(System.Text.Encoding.ASCII.GetBytes("Identity")));
        cidSystemInfo.Set("Supplement", new PdfInteger(0));
        cidFont.Set("CIDSystemInfo", cidSystemInfo);
        cidFont.Set("FontDescriptor", descriptorDict);
        cidFont.Set("DW", new PdfInteger(500));
        cidFont.Set("CIDToGIDMap", new PdfName("Identity"));

        var type0Font = new PdfDictionary();
        type0Font.Set("Type", new PdfName("Font"));
        type0Font.Set("Subtype", new PdfName("Type0"));
        type0Font.Set("BaseFont", new PdfName(baseFontName));
        type0Font.Set("Encoding", new PdfName("Identity-H"));
        var descendantFonts = new PdfArray();
        descendantFonts.Add(cidFont);
        type0Font.Set("DescendantFonts", descendantFonts);

        fontDict.Set(name, type0Font);
        var state = new FontState
        {
            ResName = name,
            Parser = glyphParser,
            Upm = glyphParser.UnitsPerEm > 0 ? glyphParser.UnitsPerEm : 1000,
            CidFont = cidFont,
            Type0Font = type0Font,
            FontFile = fontFileStream,
            Ttf = ttfData,
        };
        lock (_liveFonts) _liveFonts.Add(new WeakReference<FontState>(state));
        return state;
    }

    // Rebuild /W (per-glyph advances) and /ToUnicode from the accumulated glyph set. Cheap
    // relative to the one-time font-program embed; only runs when a call introduced new glyphs.
    private static void RefreshWidthsAndToUnicode(FontState st)
    {
        var wArray = new PdfArray();
        foreach (var (_, gid) in st.UsedGlyphs)
        {
            var pdfWidth = (int)Math.Round(st.Parser.GetAdvanceWidth(gid) * 1000.0 / st.Upm);
            var widthArr = new PdfArray();
            widthArr.Add(new PdfInteger(pdfWidth));
            wArray.Add(new PdfInteger(gid));
            wArray.Add(widthArr);
        }
        if (wArray.Count > 0) st.CidFont.Set("W", wArray);

        var toUnicode = BuildToUnicodeCMap(st.UsedGlyphs);
        st.Type0Font.Set("ToUnicode", new PdfStream(new PdfDictionary(),
            System.Text.Encoding.ASCII.GetBytes(toUnicode)));
    }

    private static string BuildToUnicodeCMap(Dictionary<int, int> usedGlyphs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("/CIDInit /ProcSet findresource begin");
        sb.AppendLine("12 dict begin");
        sb.AppendLine("begincmap");
        sb.AppendLine("/CIDSystemInfo");
        sb.AppendLine("<< /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def");
        sb.AppendLine("/CMapName /Adobe-Identity-UCS def");
        sb.AppendLine("/CMapType 2 def");
        sb.AppendLine("1 begincodespacerange");
        sb.AppendLine("<0000> <FFFF>");
        sb.AppendLine("endcodespacerange");
        sb.AppendLine($"{usedGlyphs.Count} beginbfchar");
        foreach (var (charCode, gid) in usedGlyphs)
        {
            // Supplementary-plane codepoints are written as their UTF-16BE surrogate
            // pair (PDF 32000 §9.10.3) — "<gid> <D83DDC4D>" — not as 5-digit hex.
            if (charCode > 0xFFFF)
            {
                var s = char.ConvertFromUtf32(charCode);
                sb.AppendLine($"<{gid:X4}> <{(int)s[0]:X4}{(int)s[1]:X4}>");
            }
            else
            {
                sb.AppendLine($"<{gid:X4}> <{charCode:X4}>");
            }
        }
        sb.AppendLine("endbfchar");
        sb.AppendLine("endcmap");
        sb.AppendLine("CMapName currentdict /CMap defineresource pop");
        sb.AppendLine("end");
        sb.AppendLine("end");
        return sb.ToString();
    }

    private static string GenerateSubsetTag()
    {
        var random = new Random();
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }
}
