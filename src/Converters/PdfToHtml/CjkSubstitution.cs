using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>CSS family name for an embedded font's class and @font-face: the FULL
    /// BaseFont with the subset prefix kept and name/style separators normalized to
    /// spaces ("ACMJVR+Arial,Bold" → "ACMJVR+Arial Bold").</summary>
    internal static string CssFaceFamily(string baseFont)
    {
        var s = baseFont.Replace(',', ' ').Replace('-', ' ');
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }

    /// <summary>Whether the system resolver can supply a program for this BaseFont —
    /// the sidecar emitter will then embed it as an @font-face, so the class family
    /// must use the same BaseFont-derived name. Cached per thread.</summary>
    private static bool SystemResolvable(string baseFont)
    {
        var cache = _sysResolvable ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        if (cache.TryGetValue(baseFont, out var known)) return known;
        bool ok;
        try { ok = Text.SystemFontResolver.Resolve(baseFont) is not null; }
        catch { ok = false; }
        return cache[baseFont] = ok;
    }

    /// <summary>The substitute face family served for a CJK font that neither
    /// embeds a program nor resolves to an installed face by name —
    /// SimSun subsets are shipped for such fonts (emitted as
    /// "TAG+SimSun" @font-face programs). Null when the font is
    /// not such a case.</summary>
    private static string? CjkSubstituteFamily(PdfDictionary font, PdfReader reader)
    {
        if (HasEmbeddedProgram(font, reader)) return null;
        var baseFont = font.GetName("BaseFont") ?? "";
        if (baseFont.Length == 0 || SystemResolvable(baseFont)) return null;
        // A registry-decoded CID font declares its script outright.
        if (CidOrderingOf(font, reader) == "GB1") return "SimSun";
        Dictionary<int, string>? toUni = null;
        try { toUni = Text.TextAbsorber.ParseToUnicodeFromDict(font, reader); }
        catch { }
        if (toUni is null) return null;
        foreach (var dst in toUni.Values)
            if (dst.Length > 0 && HtmlToPdfConverter.StlIdeograph(dst[0]))
                return "SimSun";
        return null;
    }

    /// <summary>CIDSystemInfo /Ordering of a Type0 font's descendant ("GB1",
    /// "Japan1", …), null for simple fonts or absent info.</summary>
    private static string? CidOrderingOf(PdfDictionary font, PdfReader reader)
    {
        try
        {
            if (font.GetName("Subtype") != "Type0") return null;
            if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArray da || da.Count == 0) return null;
            var cidFont = reader.ResolveDict(da[0]);
            if (cidFont is null) return null;
            var csi = reader.ResolveDict(cidFont.Get("CIDSystemInfo"));
            if (csi is null) return null;
            var registry = reader.Resolve(csi.Get("Registry")) is PdfString rs ? rs.ToText() : null;
            if (registry != "Adobe") return null;
            return reader.Resolve(csi.Get("Ordering")) is PdfString os ? os.ToText() : null;
        }
        catch { return null; }
    }

    /// <summary>Code → Unicode for a substituted font: the single-char ToUnicode
    /// when the font carries one, else the registry decode over the /W-declared
    /// CIDs (an Identity-H newspaper font names its used set there).</summary>
    private static Dictionary<int, int>? SubstituteCodeToUnicode(PdfDictionary font, PdfReader reader)
    {
        if (SingleCharToUnicode(font, reader) is { } tu) return tu;
        var ordering = CidOrderingOf(font, reader);
        if (ordering is null) return null;
        var map = new Dictionary<int, int>();
        foreach (var cid in DeclaredWidthCids(font, reader))
            if (Text.AdobeCidTables.LookupCid(ordering, cid) is { } uni and > 0)
                map.TryAdd(cid, uni);
        return map.Count > 0 ? map : null;
    }

    /// <summary>The CIDs a Type0 font's /W array declares widths for — the
    /// producer's own used-glyph set.</summary>
    private static IEnumerable<int> DeclaredWidthCids(PdfDictionary font, PdfReader reader)
    {
        if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArray da || da.Count == 0) yield break;
        if (reader.Resolve(reader.ResolveDict(da[0])?.Get("W")) is not PdfArray w) yield break;
        for (var i = 0; i < w.Count - 1;)
        {
            var start = reader.Resolve(w[i]) switch { PdfInteger n => n.Value, PdfReal r => (long)r.Value, _ => -1L };
            if (start < 0) yield break;
            if (reader.Resolve(w[i + 1]) is PdfArray arr)
            {
                for (var k = 0; k < arr.Count; k++) yield return (int)start + k;
                i += 2;
            }
            else
            {
                if (i + 2 >= w.Count) yield break;
                var end = reader.Resolve(w[i + 1]) switch { PdfInteger n => n.Value, PdfReal r => (long)r.Value, _ => start - 1 };
                for (var cid = start; cid <= end && cid - start < 65536; cid++) yield return (int)cid;
                i += 3;
            }
        }
    }

    /// <summary>Deterministic six-letter subset tag for a substituted font,
    /// derived from the BaseFont name — the same "ABCDEF+" shape an embedder's
    /// subset carries, but stable across runs so the emitted markup is
    /// reproducible.</summary>
    private static string SubstituteTag(string baseFont)
    {
        var h = 5381u;
        foreach (var c in baseFont) h = unchecked(h * 33 + c);
        var tag = new char[6];
        for (var i = 0; i < 6; i++) { tag[i] = (char)('A' + (int)(h % 26)); h /= 26; }
        return new string(tag);
    }

    /// <summary>The installed substitute face's outline parser, cached per
    /// family (the TTC face extraction and glyf parse are paid once).</summary>
    private static Text.GlyphOutlineParser? ResolveSubstituteParser(string family)
    {
        var cache = _substituteParsers ??= new Dictionary<string, Text.GlyphOutlineParser?>(StringComparer.Ordinal);
        if (cache.TryGetValue(family, out var p)) return p;
        Text.GlyphOutlineParser? parser = null;
        try
        {
            var bytes = Text.SystemFontResolver.Resolve(family);
            if (bytes is not null) parser = new Text.GlyphOutlineParser(bytes);
        }
        catch { }
        return cache[family] = parser;
    }

    /// <summary>Builds the shipped subset program for a substituted CJK font:
    /// the substitute face's glyphs behind the font's ToUnicode destinations
    /// (multi-char destinations contribute each component). Null when the font
    /// is not a substitute case or nothing maps.</summary>
    private static byte[]? BuildCjkSubstituteSubset(PdfDictionary font, PdfReader reader, out string? family)
    {
        family = null;
        var subName = CjkSubstituteFamily(font, reader);
        if (subName is null) return null;
        var parser = ResolveSubstituteParser(subName);
        if (parser is null) return null;
        var codeToUni = SubstituteCodeToUnicode(font, reader);
        if (codeToUni is null) return null;
        var uniToGid = new Dictionary<int, int>();
        foreach (var uni in codeToUni.Values)
            if (uni <= 0xFFFF && !uniToGid.ContainsKey(uni)
                && parser.CMap.TryGetValue(uni, out var g) && g > 0)
                uniToGid[uni] = g;
        if (parser.CMap.TryGetValue(' ', out var gSp) && gSp > 0) uniToGid.TryAdd(' ', gSp);
        var ttf = Text.CffToTrueType.BuildSubset(parser, uniToGid);
        if (ttf is null) return null;
        family = SubstituteTag(font.GetName("BaseFont") ?? "font") + "+" + subName;
        return ttf;
    }

    /// <summary>Any right-to-left-script codepoint (Hebrew/Arabic blocks and their
    /// presentation forms).</summary>
    private static bool HasRtlCodepoint(string s)
    {
        foreach (var c in s)
            if (c is (>= (char)0x0590 and <= (char)0x08FF)
                or (>= (char)0xFB1D and <= (char)0xFDFF)
                or (>= (char)0xFE70 and <= (char)0xFEFF))
                return true;
        return false;
    }

    /// <summary>The single character standing in for a multi-char ligature sequence.</summary>
    private static char StandardLigatureChar(string seq) => seq switch
    {
        "ff" => '\uFB00',
        "fi" => '\uFB01',
        "fl" => '\uFB02',
        "ffi" => '\uFB03',
        "ffl" => '\uFB04',
        "ft" => '\uFB05',
        "st" => '\uFB06',
        "ue" => '\u1D6B',
        "Th" => '\uE000',
        _ => seq[0],
    };

    /// <summary>A ToUnicode dst of tab+CR+space+nbsp marks the inter-word space glyph.</summary>
    private const string SpaceLigature = "\u0009\u000D\u0020\u00A0";

    private static int CodePointCount(string s)
    {
        var n = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            n++;
        }
        return n;
    }

    private static bool AllInCmap(string s, HashSet<int> cmapChars)
    {
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i];
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i++;
            }
            if (!cmapChars.Contains(cp)) return false;
        }
        return true;
    }
}
