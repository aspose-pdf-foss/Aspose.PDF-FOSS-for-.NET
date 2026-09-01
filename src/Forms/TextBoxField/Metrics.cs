using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class TextBoxField
{
    /// <summary>Sum of per-glyph advances of <paramref name="run"/> as a fraction of the em.</summary>
    private double MeasureRunEm(string run, string fontName)
    {
        double em = 0;
        foreach (char c in run) em += GetGlyphWidthEm(c, fontName);
        return em;
    }

    /// <summary>Does the run carry a character that may be broken after without a space -
    /// CJK ideographs, kana, and the full-width punctuation that travels with them?</summary>
    private static bool ContainsIdeograph(string run)
    {
        foreach (var c in run)
            if (c >= '⺀' && c <= '鿿' || c >= '豈' && c <= '﫿'
                || c >= '＀' && c <= '￯')
                return true;
        return false;
    }

    /// <summary>Greedy word-wrap of a multiline field value into lines no wider than
    /// <paramref name="availWidth"/> at <paramref name="fontSize"/>. Explicit newlines always
    /// break; a single word wider than the box is left on its own (overflowing) line.</summary>
    private System.Collections.Generic.List<string> WrapMultilineText(
        string text, string fontName, double fontSize, double availWidth)
    {
        var lines = new System.Collections.Generic.List<string>();
        double spaceW = GetGlyphWidthEm(' ', fontName) * fontSize;
        // Normalise every hard line break to '\n' first: a field value may separate lines
        // with '\r' or '\r\n' (the PDF form convention, e.g. FillField) as well as '\n'.
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var para in normalized.Split('\n'))
        {
            // A paragraph that already fits is kept verbatim — preserving its exact spacing
            // (leading/inner spaces) — so only genuine overflow is re-flowed by word-wrap.
            if (MeasureRunEm(para, fontName) * fontSize <= availWidth) { lines.Add(para); continue; }

            // Fill the line CHARACTER by character. A space is a break opportunity but not
            // the only one: CJK writes whole sentences without a space, and the line
            // fills to the box and breaks between two ideographs rather than
            // pushing the sentence whole onto the next line. So the break falls at the
            // last space only while the run still under way is Latin - a word is never
            // split in two - and between characters otherwise.
            var cur = new System.Text.StringBuilder();
            double curW = 0;
            var lastSpace = -1;
            foreach (var ch in para)
            {
                var chW = GetGlyphWidthEm(ch, fontName) * fontSize;
                if (cur.Length > 0 && curW + chW > availWidth)
                {
                    var tail = lastSpace >= 0 ? cur.ToString(lastSpace + 1, cur.Length - lastSpace - 1) : "";
                    if (lastSpace >= 0 && !ContainsIdeograph(tail))
                    {
                        lines.Add(cur.ToString(0, lastSpace));
                        cur.Remove(0, lastSpace + 1);
                        curW = MeasureRunEm(cur.ToString(), fontName) * fontSize;
                    }
                    else
                    {
                        lines.Add(cur.ToString());
                        cur.Clear();
                        curW = 0;
                    }
                    lastSpace = -1;
                }
                if (ch == ' ') lastSpace = cur.Length;
                cur.Append(ch);
                curW += chW;
            }
            lines.Add(cur.ToString());
        }
        return lines;
    }

    /// <summary>Auto-size font for a multiline text field:
    /// <c>Tf = min(cap, (h - inset) / (N · 1.14 · L))</c> where <c>N</c> is the number of
    /// display lines after word-wrap, <c>L</c> is the font's line-height factor (the
    /// FontBBox height ÷ 1000 read from the embedded AcroForm /DR descriptor when the font
    /// is embedded, else a 1.20 default), and <c>cap</c> is 12 for a value with ≥ 2 hard
    /// lines or the single-line ceiling 1525/128 (≈ 11.914) for a single hard line. Width
    /// never caps the size directly — it only matters by forcing wraps that raise N.</summary>
    private double AutoFitMultilineSize(string text, double w, double h, string fontName)
    {
        double availW = w - 4;
        const double inset = 2;
        double L = GetFontLineHeightEm(fontName);
        double perLine = 1.14 * L;

        // Hard-line count picks the cap; a single hard line uses the 1525/128 ceiling even
        // when it soft-wraps onto several display lines.
        int hardLines = 1;
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        foreach (var ch in normalized) if (ch == '\n') hardLines++;
        double cap = hardLines >= 2 ? 12.0 : 1525.0 / 128.0;

        // The display-line count N(s) grows as s grows (more soft-wraps), so N(s)·perLine·s
        // is monotonic in s: search downward from the cap for the largest size whose wrapped
        // lines still fit the inner height. For a value that never wraps (N = hard lines) this
        // yields the closed form (h − inset)/(N·perLine); for a wrapping value it stops at the
        // largest self-consistent size (a fixed-point iteration can converge to a non-maximal
        // size, so a monotone search is used instead).
        double budget = h - inset;
        for (double s = cap; s >= 4; s -= 0.02)
        {
            int n = System.Math.Max(1, WrapMultilineText(text, fontName, s, availW).Count);
            if (n * perLine * s <= budget)
                return s;
        }
        return 4;
    }

    /// <summary>The font's line-height factor <c>L</c> (rendered line pitch ÷ font size): the
    /// FontBBox height ÷ 1000 from the embedded /DR font descriptor when available, else a
    /// 1.20 default (the value used for a freshly-named/system font).</summary>
    private double GetFontLineHeightEm(string fontName)
    {
        if (GetFontBBoxHeightEmFromDR(fontName) is { } bbox && bbox > 0) return bbox;
        return 1.20;
    }

    /// <summary>FontBBox height (yMax − yMin) ÷ 1000 read from the AcroForm
    /// /DR/Font/&lt;name&gt;/FontDescriptor/FontBBox, or null when the named font has no
    /// embedded descriptor (e.g. a fresh standard font not yet embedded).</summary>
    private double? GetFontBBoxHeightEmFromDR(string fontName)
    {
        PdfDictionary? acro;
        try { acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm")); }
        catch { return null; }
        var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
        var fonts = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
        var font = fonts is null ? null : Reader.ResolveDict(fonts.Get(fontName));
        var fd = font is null ? null : Reader.ResolveDict(font.Get("FontDescriptor"));
        if (fd is null) return null;
        if (Reader.Resolve(fd.Get("FontBBox")) is not PdfArray bbox || bbox.Count < 4) return null;
        double y0 = ArrayNum(bbox[1]), y1 = ArrayNum(bbox[3]);
        double hgt = (y1 - y0) / 1000.0;
        return hgt > 0 ? hgt : null;
    }

    private static double ArrayNum(PdfObject o) => o switch
    {
        PdfReal r => r.Value,
        PdfInteger n => n.Value,
        _ => 0.0,
    };

    /// <summary>Advance width of a character as a fraction of the em, read from the field's
    /// /DA font: first the PDF-level /Widths on the AcroForm /DR font dict (the authoritative
    /// widths for the named comb font), then the embedded /DA font (hmtx), then the Core-14
    /// AFM widths for a standard font name, then the named system face, else ~0.5.</summary>
    /// <summary>AFM FontBBox heights (per-mille) for the Standard-14 faces — the
    /// appearance line pitch for a base-14 /DA font is size × height / 1000.</summary>
    private static double? Std14BboxHeight(string baseName) => baseName switch
    {
        "Helvetica" or "Helvetica-Oblique" => 1156,
        "Helvetica-Bold" or "Helvetica-BoldOblique" => 1190,
        "Times-Roman" => 1116,
        "Times-Bold" => 1153,
        "Times-Italic" => 1100,
        "Times-BoldItalic" => 1139,
        "Courier" or "Courier-Oblique" => 1055,
        "Courier-Bold" or "Courier-BoldOblique" => 1051,
        "Symbol" => 1303,
        "ZapfDingbats" => 963,
        _ => null,
    };

    /// <summary>AFM descender depth (per-mille, positive) for the Standard-14 faces.</summary>
    private static double Std14Descent(string baseName) => baseName switch
    {
        "Times-Roman" or "Times-Bold" or "Times-Italic" or "Times-BoldItalic" => 217,
        "Courier" or "Courier-Bold" or "Courier-Oblique" or "Courier-BoldOblique" => 194,
        _ => 207,
    };

    /// <summary>Head-table bounding-box height of a TrueType program as an em
    /// fraction ((yMax − yMin) / unitsPerEm), or null when unparsable.</summary>
    private static double? HeadBboxHeightEm(byte[] ttf)
    {
        try
        {
            var p = new Aspose.Pdf.Text.TrueTypeParser(ttf);
            p.Parse();
            var h = p.BBox[3] - p.BBox[1];
            if (h <= 0 || p.UnitsPerEm <= 0) return null;
            return (double)h / p.UnitsPerEm;
        }
        catch { return null; }
    }

    /// <summary>The appearance line pitch for the field's /DA font: size × the head
    /// bounding-box HEIGHT of the actual font PROGRAM — the embedded /DR FontFile2
    /// when present, an AFM FontBBox for a Standard-14 base font, else the system
    /// face resolved by name, else Times New Roman.</summary>
    /// <summary>Auto-size font size of a text box whose value is empty (nothing
    /// to fit): the generator's default point size.</summary>
    private const double EmptyValueAutoSize = 10;

    /// <summary>Inset the auto-size fit leaves on each axis: one unit plus the
    /// border width on both sides. The border width is /BS /W; without a /BS a
    /// loaded widget carries the PDF default of 1 (inset 3 — a 76.6 pt field
    /// fits "AgreementNumber" at 8.457 pt), while a widget the generator itself
    /// placed has no border at all (inset 1 — probed 2026-08-23: 15.4252 pt in a
    /// 20 pt box). The two widgets are dictionary-identical; only their
    /// provenance tells them apart.</summary>
    private double AutoSizeInset()
    {
        double bw = _generatorPlaced ? 0 : 1;
        if (Reader.ResolveDict(Dict.Get("BS")) is { } bs)
            bw = bs.ContainsKey("W") ? Aspose.Pdf.Functions.PdfArrayHelper.GetDoubleFromDict(bs, "W", 1) : 1;
        return 1 + 2 * bw;
    }

    /// <summary>Line pitch, in per-mille of the font size, that a face NAMED by a
    /// <c>Style</c> string paces a multiline appearance by. Measured over 53 cases and
    /// 31 faces; the value is exact, not an approximation (a 7 pt Tahoma field steps
    /// 7.196 = 7 x 1.028, so the factor carries three decimals and no device rounding
    /// is involved).
    ///
    /// The rule has two parts. The base law is the face's own hhea ascender and descender,
    /// each quantised to PDF 1000-unit space ON ITS OWN and FLOORED. Flooring the SUM
    /// instead is wrong by a count that shows: Verdana is 1005 + 209 = 1214, while
    /// 2489 * 1000 / 2048 floors to 1215. The hhea LINE GAP takes no part.
    ///
    /// On top of that a hardcoded table covers the classic core faces,
    /// keyed on the family NAME rather than on anything in the font file. Proved by cloning
    /// each face with only its name changed - every metric byte identical - at which point
    /// the pitch falls straight onto the base law: a renamed Arial paces 11.16 where
    /// Arial paces 11.49, a renamed Tahoma 12.06 against 10.28, a renamed Courier New 11.32
    /// against 10.00. The table is therefore reproduced as measured; it cannot be derived
    /// from the metrics.</summary>
    private static double? NamedFacePitchPerMille(string family, bool bold) => family switch
    {
        "Arial" or "Helvetica" or "ZapfDingbats" or "Times New Roman" => 1149,
        "Arial Narrow" => 1131,
        "Tahoma" => 1028,
        "Courier New" => 1000,
        // The Standard-14 Courier is the only face measured that paces differently
        // bold, and neither value is its AFM box (1055/1051) - another table entry.
        "Courier" => bold ? 1199 : 1203,
        "Times" => 1116,
        "Symbol" => 1303,
        _ => null,
    };

    /// <summary>Height of the box a SINGLE-line style-named appearance centres in, as a
    /// fraction of the font size. It is normally just the line pitch - measured on
    /// Helvetica, Times, Symbol, ZapfDingbats, Tahoma, Verdana, Calibri, Georgia, Impact,
    /// Comic Sans and Segoe UI, every one of which seats at exactly (h - pitch*fs)/2 - but
    /// five of the classic families centre on a SMALLER box, and this is the second
    /// name-keyed table.
    ///
    /// Name-keyed, proved the same way as the pitch table: a clone of Arial carrying only a
    /// different family name centres on its ordinary pitch (baseline 30.42 at 10 pt) where
    /// Arial itself centres on 0.6734 em (32.633), and a renamed Courier New gives 30.34
    /// against Courier New's 33.585. Nothing in the font file distinguishes them, so the
    /// values are reproduced as measured. Each was read at two or more sizes and is exact:
    /// Arial at 5 / 10 / 20 / 30 pt seats at 34.3165 / 32.633 / 29.266 / 25.899, i.e.
    /// h/2 - 0.3367*fs to four decimals.</summary>
    private static double? NamedFaceSingleLineBoxPerMille(string family, bool bold) => family switch
    {
        "Arial" => 673.4,
        "Arial Narrow" => 665,
        "Times New Roman" => 638.4,
        "Courier New" => 483,
        "Courier" => 596.5,
        _ => null,
    };

    /// <summary>The box a single-line style-named appearance centres in: the table above
    /// when the name is in it, otherwise the face's own line pitch.</summary>
    private double StyleFaceSingleLineBoxEm(string fontName, double fontSize)
    {
        var family = (DefaultAppearance?.FontName ?? fontName) ?? string.Empty;
        if (NamedFaceSingleLineBoxPerMille(family, StyleFaceBold) is { } pinned) return pinned / 1000.0;
        return StyleFacePitchEm(fontName, fontSize);
    }

    /// <summary>The line-pitch factor (pitch / font size) for a face named by a Style
    /// string: the override table, else the base law over the resolved system face, else
    /// the Standard-14 AFM box, else the existing bbox model.</summary>
    private double StyleFacePitchEm(string fontName, double fontSize)
    {
        var family = (DefaultAppearance?.FontName ?? fontName) ?? string.Empty;
        if (NamedFacePitchPerMille(family, StyleFaceBold) is { } pinned) return pinned / 1000.0;

        var ttf = Aspose.Pdf.Text.SystemFontResolver.Resolve(family);
        if (ttf is not null)
        {
            try
            {
                var p = new Aspose.Pdf.Text.TrueTypeParser(ttf);
                p.Parse();
                if (p.UnitsPerEm > 0 && p.Ascent > 0)
                {
                    // Per-component floor into 1000-unit space - see the note above.
                    var asc = System.Math.Floor(p.Ascent * 1000.0 / p.UnitsPerEm);
                    var desc = System.Math.Floor(System.Math.Abs(p.Descent) * 1000.0 / p.UnitsPerEm);
                    if (asc + desc > 0) return (asc + desc) / 1000.0;
                }
            }
            catch { }
        }
        var baseName = NormalizeStdFontName(family);
        if (Std14BboxHeight(baseName) is { } bb) return bb / 1000.0;
        return ApLineHeight(family, fontSize) / fontSize;
    }

    private protected double ApLineHeight(string fontName, double fontSize)
    {
        var dr = ResolveDrFontDict(fontName);
        if (dr is not null)
        {
            try
            {
                var fd = Reader.ResolveDict(dr.Get("FontDescriptor"));
                var ff2 = fd is null ? null : Reader.ResolveStream(fd.Get("FontFile2"));
                if (ff2 is not null && HeadBboxHeightEm(Reader.DecodeStream(ff2)) is { } em)
                    return fontSize * em;
            }
            catch { }
        }
        var baseName = NormalizeStdFontName(fontName);
        if ((dr is null || dr.GetName("Subtype") == "Type1") && Std14BboxHeight(baseName) is { } bb)
            return fontSize * bb / 1000.0;
        var sys = Aspose.Pdf.Text.SystemFontResolver.Resolve(
            dr?.GetName("BaseFont") is { Length: > 0 } bf
                ? Aspose.Pdf.Text.SystemFontResolver.NormalizeBaseFontName(bf) : baseName);
        if (sys is not null && HeadBboxHeightEm(sys) is { } em2) return fontSize * em2;
        if (Std14BboxHeight(baseName) is { } bb2) return fontSize * bb2 / 1000.0;
        var tnr = Aspose.Pdf.Text.SystemFontResolver.Resolve("Times New Roman");
        if (tnr is not null && HeadBboxHeightEm(tnr) is { } em3) return fontSize * em3;
        return fontSize * 1.15;
    }

    /// <summary>The /DA font's descender depth as a positive per-mille value, from
    /// the /DR FontDescriptor /Descent when present, else the AFM table.</summary>
    private protected double ApDescent(string fontName)
    {
        try
        {
            var dr = ResolveDrFontDict(fontName);
            var fd = dr is null ? null : Reader.ResolveDict(dr.Get("FontDescriptor"));
            if (fd is not null && fd.ContainsKey("Descent"))
            {
                var d = Aspose.Pdf.Functions.PdfArrayHelper.GetDoubleFromDict(fd, "Descent", 0);
                if (d != 0) return System.Math.Abs(d);
            }
        }
        catch { }
        return Std14Descent(NormalizeStdFontName(fontName));
    }

    /// <summary>True when <paramref name="ttf"/> has a real glyph for every
    /// beyond-WinAnsi char of <paramref name="text"/> (sampled up to 64).</summary>
    private static bool CoversBeyondAnsi(byte[] ttf, string text)
    {
        try
        {
            var parser = _uniWidthParsers.GetValue(ttf, static t => new Aspose.Pdf.Text.GlyphOutlineParser(t));
            var checked_ = 0;
            for (var i = 0; i < text.Length && checked_ < 64; i++)
            {
                int cp = text[i];
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    cp = char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                if (cp <= 0xFF) continue;
                checked_++;
                if (!parser.CMap.TryGetValue(cp, out var gid) || gid == 0) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private double GetGlyphWidthEm(char c, string fontName)
    {
        // The composite appearance draws the WHOLE value through this one face as CID
        // hex - ASCII included - so measure every character with it. Measuring the Latin
        // half against another face leaves the line a few characters short of where the
        // reference breaks it.
        if (_uniAppearanceTtf is { } uttf)
        {
            try
            {
                var parser = _uniWidthParsers.GetValue(uttf, static t => new Aspose.Pdf.Text.GlyphOutlineParser(t));
                if (parser.CMap.TryGetValue(c, out var gid) && gid != 0)
                {
                    var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
                    return System.Math.Round(parser.GetAdvanceWidth(gid) * 1000.0 / upm) / 1000.0;
                }
            }
            catch { /* fall through to the default estimate */ }
        }
        if (GetGlyphWidthFromDR(fontName, c) is { } drw) return drw;
        var ttf = DefaultAppearance?.EmbeddedFont?.SourceFontData?.TtfData;
        if (ttf is { Length: > 0 })
        {
            var (_, _, _, widths) = Aspose.Pdf.Text.FontRepository.ReadTtfMetrics(ttf);
            if (c < 256 && widths[c] > 0) return widths[c] / 1000.0;
        }
        // Core-14 AFM widths (Helv/Helvetica/Arial, TiRo/Times, Cour/Courier, …) — the
        // authoritative metrics for a non-embedded standard font named in /DA.
        var std = Aspose.Pdf.Text.Standard14Fonts.GetWidth(NormalizeStdFontName(fontName), c);
        if (std > 0) return std / 1000.0;
        var resolved = Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName);
        if (resolved is { Length: > 0 })
        {
            var (_, _, _, widths) = Aspose.Pdf.Text.FontRepository.ReadTtfMetrics(resolved);
            if (c < 256 && widths[c] > 0) return widths[c] / 1000.0;
        }
        return 0.5;
    }

    /// <summary>Map the abbreviated AcroForm /DA font names to their Core-14 base names so
    /// the AFM width tables resolve (Helv→Helvetica, TiRo→Times-Roman, Cour→Courier, …).</summary>
    private static string NormalizeStdFontName(string fontName) => fontName switch
    {
        "Helv" => "Helvetica",
        "HeBO" => "Helvetica-BoldOblique",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "TiBI" => "Times-BoldItalic",
        "Cour" => "Courier",
        _ => fontName,
    };

    /// <summary>Read a simple font's PDF /Widths entry for a character from the AcroForm
    /// /DR /Font dictionary (the default-resources font named by /DA). Returns the advance
    /// as an em fraction, or null when the font is absent / composite / has no usable entry.</summary>
    private double? GetGlyphWidthFromDR(string fontName, char c)
    {
        PdfDictionary? acro;
        try { acro = Reader.ResolveDict(Reader.Catalog.Get("AcroForm")); }
        catch { return null; }
        var dr = acro is null ? null : Reader.ResolveDict(acro.Get("DR"));
        var fonts = dr is null ? null : Reader.ResolveDict(dr.Get("Font"));
        var font = fonts is null ? null : Reader.ResolveDict(fonts.Get(fontName));
        if (font is null) return null;
        if (font.GetName("Subtype") == "Type0") return null; // composite widths not handled here
        int first = (int)font.GetInt("FirstChar");
        if (Reader.Resolve(font.Get("Widths")) is not PdfArray warr) return null;
        int idx = c - first;
        if (idx < 0 || idx >= warr.Count) return null;
        double wv = warr[idx] switch { PdfReal r => r.Value, PdfInteger n => n.Value, _ => 0.0 };
        return wv > 0 ? wv / 1000.0 : null;
    }

    /// <summary>The line pitch, in ems, a multiline appearance paces itself by when
    /// its /DA font is a face the caller opened: a flat 1.2 regardless of the face
    /// (measured over four sizes and four box heights).</summary>
    private const double AuthoredFacePitchEm = 1.2;

    private double ReadTypoDescentEm(string fontName)
    {
        var ttf = DefaultAppearance?.EmbeddedFont?.SourceFontData?.TtfData;
        if (ttf is { Length: > 0 })
        {
            var d = Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(ttf);
            if (d != 0) return d;
        }
        var resolved = Aspose.Pdf.Text.SystemFontResolver.Resolve(fontName);
        if (resolved is not null)
        {
            var d = Aspose.Pdf.Text.FontRepository.ReadTtfTypoDescentEm(resolved);
            if (d != 0) return d;
        }
        return -0.207; // Helvetica-class default
    }
}
