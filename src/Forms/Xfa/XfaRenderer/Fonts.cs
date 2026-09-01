using System.Globalization;
using System.Text;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    // Standard Helvetica / Helvetica-Bold AFM advance widths (per-1000 em) for ASCII 32..126, so
    // line-break decisions match the layout engine's wrapping.
    private static readonly int[] HelvW =
    {
        278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,
        556,556,278,278,584,584,584,556,1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,333,556,556,500,556,556,278,556,
        556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584,
    };

    private static readonly int[] HelvBoldW =
    {
        278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,
        556,556,333,333,584,584,584,611,975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,
        667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,333,556,611,556,611,556,333,611,
        611,278,278,556,278,889,611,611,611,611,389,556,333,611,556,778,556,556,500,389,280,389,584,
    };

    // Standard Times-Roman / Times-Bold AFM advance widths (per-1000 em) for ASCII 32..126.
    private static readonly int[] TimesW =
    {
        250,333,408,500,500,833,778,180,333,333,500,564,250,333,250,278,500,500,500,500,500,500,500,500,
        500,500,278,278,564,564,564,444,921,722,667,667,722,611,556,722,722,333,389,722,611,889,722,722,
        556,722,667,556,611,722,722,944,722,722,611,333,278,333,469,500,333,444,500,444,500,444,333,500,
        500,278,278,500,278,778,500,500,500,500,333,389,278,500,500,722,500,500,444,480,200,480,541,
    };

    private static readonly int[] TimesBoldW =
    {
        250,333,555,500,500,1000,833,278,333,333,500,570,250,333,250,278,500,500,500,500,500,500,500,500,
        500,500,333,333,570,570,570,500,930,722,667,722,722,667,611,778,778,389,500,778,667,944,722,778,
        611,778,722,556,667,722,722,1000,722,722,667,333,278,333,581,500,333,500,556,444,556,444,333,500,
        556,278,333,556,278,833,556,500,556,556,444,389,333,556,500,722,500,500,444,394,220,394,520,
    };

    private static double TextWidth(string s, double fs, bool bold)
    {
        var t = bold ? HelvBoldW : HelvW;
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : 556;
        return w * fs / 1000.0;
    }

    private static string RtFontName(bool serif, bool bold, bool italic)
        => (serif ? "TimesNewRoman" : "Arial") + (bold && italic ? "-BoldItalic" : bold ? "-Bold" : italic ? "-Italic" : "");

    /// <summary>Advance width of a styled rich-text run, including per-glyph letter
    /// spacing — real TrueType advances when the system font resolves, else the AFM
    /// tables (Times or Helvetica family).</summary>
    private static double TextWidthF(string s, RtRun r)
    {
        if (r.Family is { } fam)
            return FamTextWidth(s, r.Size, fam, r.Bold, r.Italic) + r.LetterSpacing * s.Length;
        var font = RtFont(r.Serif, r.Bold, r.Italic);
        if (font is { } f)
        {
            double adv = 0;
            var upm = (double)f.parser.UnitsPerEm;
            foreach (var ch in s)
                adv += f.parser.CMap.TryGetValue(ch, out var gid)
                    ? Math.Round(f.parser.GetAdvanceWidth(gid) * 1000.0 / upm)
                    : 500;
            return adv * r.Size / 1000.0 + r.LetterSpacing * s.Length;
        }
        var t = r.Serif ? (r.Bold ? TimesBoldW : TimesW) : (r.Bold ? HelvBoldW : HelvW);
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : 500;
        return w * r.Size / 1000.0 + r.LetterSpacing * s.Length;
    }

    private static byte[] Emit(List<Item> items, Page page)
    {
        var b = new Content.ContentStreamBuilder();
        foreach (var it in items)
        {
            if (it.Kind == "fill")
            {
                b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
                b.Rectangle(it.X, it.Y, it.W, it.H).Fill();
                b.RestoreState();
            }
            else if (it.Kind == "line")
            {
                b.SaveState().SetStrokeColor(0, 0, 0).SetLineWidth(0.5);
                b.MoveTo(it.X, it.Y).LineTo(it.X + it.W, it.Y).Stroke();
                b.RestoreState();
            }
            else if (it.Kind == "box")
            {
                b.SaveState().SetStrokeColor(0, 0, 0).SetLineWidth(0.6);
                b.Rectangle(it.X, it.Y, it.W, it.H).Stroke();
                b.RestoreState();
            }
            else if (it.Kind == "diag")
            {
                // Diagonal rule: slope "/" (Stretch=true) runs bottom-left to
                // top-right; the default runs top-left to bottom-right.
                b.SaveState().SetStrokeColor(it.Color[0], it.Color[1], it.Color[2])
                 .SetLineWidth(it.FontSize > 0 ? it.FontSize : 1);
                if (it.Stretch)
                    b.MoveTo(it.X, it.Y).LineTo(it.X + it.W, it.Y + it.H);
                else
                    b.MoveTo(it.X, it.Y + it.H).LineTo(it.X + it.W, it.Y);
                b.Stroke();
                b.RestoreState();
            }
            else if (it.Kind is "circle" or "dot")
            {
                // Circle via four Bézier quadrants; "dot" is filled (radio selection).
                double r = it.W / 2, cx = it.X + r, cy = it.Y + r, k = r * 0.5523;
                b.SaveState();
                if (it.Kind == "dot") b.SetFillColor(0, 0, 0); else b.SetStrokeColor(0, 0, 0).SetLineWidth(0.6);
                b.MoveTo(cx + r, cy)
                 .CurveTo(cx + r, cy + k, cx + k, cy + r, cx, cy + r)
                 .CurveTo(cx - k, cy + r, cx - r, cy + k, cx - r, cy)
                 .CurveTo(cx - r, cy - k, cx - k, cy - r, cx, cy - r)
                 .CurveTo(cx + k, cy - r, cx + r, cy - k, cx + r, cy);
                if (it.Kind == "dot") b.Fill(); else b.Stroke();
                b.RestoreState();
            }
            else if (it.Kind == "image" && it.ImageData is not null)
            {
                try
                {
                    page.Resources.Images.Add(new System.IO.MemoryStream(it.ImageData));
                    var name = page.Resources.Images[page.Resources.Images.Count].Name;
                    if (name is not null)
                    {
                        // Default XFA aspect ("fit"): preserve the image's natural
                        // ratio inside the box, anchored at the box's top-left.
                        double dw = it.W, dh = it.H, dy = it.Y;
                        if (!it.Stretch
                            && Document.TryGetImageNaturalSizePt(it.ImageData, out var nw, out var nh)
                            && nw > 0 && nh > 0)
                        {
                            var s = Math.Min(it.W / nw, it.H / nh);
                            dw = nw * s; dh = nh * s;
                            dy = it.Y + it.H - dh;
                        }
                        b.SaveState().SetMatrix(dw, 0, 0, dh, it.X, dy);
                        b.DrawXObject(name);
                        b.RestoreState();
                    }
                }
                catch { /* undecodable image — skip */ }
            }
            else if (it.Kind == "text")
            {
                b.SaveState().SetFillColor(it.Color[0], it.Color[1], it.Color[2]);
                b.BeginText();
                var unicodeShown = false;
                if (NeedsUnicodeFont(it.Text))
                {
                    // Text WinAnsi can't encode (Hebrew, Cyrillic, CJK, …) is shown with
                    // an embedded Identity-H Type0 font instead of collapsing to '?'.
                    var ttf = Text.SystemFontResolver.Resolve(it.Bold ? "Arial,Bold" : "Arial")
                              ?? Text.SystemFontResolver.Resolve("Arial");
                    var fontRes = PageFontDict(page);
                    if (ttf is not null && fontRes is not null)
                    {
                        var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(
                            fontRes, ttf, it.Bold ? "Arial-Bold" : "Arial", OrderForExtraction(it.Text));
                        b.SetFont(resName, it.FontSize);
                        b.MoveTextPosition(it.X, it.Y);
                        b.ShowTextHex(hexGlyphs);
                        unicodeShown = true;
                    }
                }
                // A resolvable non-default template face (Verdana, Tahoma …) is
                // embedded so painted advances equal the widths the wrap measured.
                if (!unicodeShown && it.Family is { } fam
                    && FamilyFont(fam, it.Bold, it.Italic) is { } famf
                    && PageFontDict(page) is { } famFd)
                {
                    var famName = fam.Replace(" ", "")
                        + (it.Bold && it.Italic ? "-BoldItalic" : it.Bold ? "-Bold" : it.Italic ? "-Italic" : "");
                    var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(famFd, famf.ttf, famName, it.Text);
                    b.SetFont(resName, it.FontSize);
                    if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
                    b.MoveTextPosition(it.X, it.Y);
                    b.ShowTextHex(hexGlyphs);
                    unicodeShown = true;
                }
                // A rich-text run renders with the real (embedded) system font so drawn
                // glyph shapes and advances match the widths the wrap was measured with.
                if (!unicodeShown && it.Rich && RtFont(it.Serif, it.Bold, it.Italic) is { } rtf
                    && PageFontDict(page) is { } fd)
                {
                    var (resName, hexGlyphs) = Text.Type0FontEmbedder.Embed(
                        fd, rtf.ttf, RtFontName(it.Serif, it.Bold, it.Italic), it.Text);
                    b.SetFont(resName, it.FontSize);
                    if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
                    b.MoveTextPosition(it.X, it.Y);
                    b.ShowTextHex(hexGlyphs);
                    unicodeShown = true;
                }
                if (!unicodeShown)
                {
                    b.SetFont(StandardFontRes(page, it.Serif, it.Bold, it.Italic), it.FontSize);
                    if (it.CharSpacing != 0) b.SetCharSpacing(it.CharSpacing);
                    if (it.HScale != 1.0) b.SetHorizontalScaling(it.HScale * 100.0);
                    b.MoveTextPosition(it.X, it.Y);
                    b.ShowText(ToWinAnsi(it.Text));
                }
                b.EndText();
                b.RestoreState();
            }
        }
        return b.Build();
    }

    /// <summary>True when the string contains characters ToWinAnsi would collapse to '?'
    /// (anything above 0xFF that isn't one of its special punctuation mappings).</summary>
    private static bool NeedsUnicodeFont(string s)
    {
        foreach (var ch in s)
            if (ch > (char)0xFF && ch is not ('‘' or '’' or '“' or '”' or '•' or '–' or '—' or '…' or '™'))
                return true;
        return false;
    }

    /// <summary>Order a line for extraction round-trip: a PURE-RTL(+neutral) line is stored
    /// reversed (visual order) because the text extractor reverses exactly such runs back to
    /// logical order; mixed lines (digits/Latin embedded in RTL) are stored logically — the
    /// extractor leaves them unchanged. Mirrors TextAbsorber.ApplyRtlIfPureRtl.</summary>
    private static string OrderForExtraction(string s)
    {
        bool hasRtl = false;
        foreach (var c in s)
        {
            if (Text.BidiReorderer.IsRtlChar(c))
                hasRtl = true;
            else if (c == ' ' || c == '\t'
                     || (c >= '!' && c <= '/') || (c >= ':' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
                { /* neutral */ }
            else
                return s; // LTR character — logical order round-trips as-is
        }
        if (!hasRtl) return s;
        var arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>The page's /Resources /Font dictionary (created by EnsureFonts).</summary>
    private static Core.PdfDictionary? PageFontDict(Page page)
        => (page.Dict.Get("Resources") as Core.PdfDictionary)?.Get("Font") as Core.PdfDictionary;

    /// <summary>Map a Unicode string to a WinAnsi-encoded byte string (returned as a char string whose
    /// code units are the encoded bytes). ShowText handles the ()\ escaping itself.</summary>
    private static string ToWinAnsi(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            int c = ch switch
            {
                '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94,
                '•' => 0x95, '–' => 0x96, '—' => 0x97, '…' => 0x85,
                ' ' => 0x20, '™' => 0x99, '®' => 0xAE, '©' => 0xA9,
                _ => ch <= 0xFF ? ch : '?',
            };
            sb.Append((char)c);
        }
        return sb.ToString();
    }

    private static void EnsureFonts(Page page)
    {
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
    }

    /// <summary>Resource name of the standard-14 font for a run style, registered on the
    /// page on first use (F1/F2 = the legacy Helvetica pair; the rest F3..F8).</summary>
    private static string StandardFontRes(Page page, bool serif, bool bold, bool italic)
    {
        var (baseFont, res) = (serif, bold, italic) switch
        {
            (false, false, false) => ("Helvetica", "F1"),
            (false, true, false) => ("Helvetica-Bold", "F2"),
            (false, false, true) => ("Helvetica-Oblique", "F3"),
            (false, true, true) => ("Helvetica-BoldOblique", "F4"),
            (true, false, false) => ("Times-Roman", "F5"),
            (true, true, false) => ("Times-Bold", "F6"),
            (true, false, true) => ("Times-Italic", "F7"),
            (true, true, true) => ("Times-BoldItalic", "F8"),
        };
        EnsureFont(page, baseFont, res);
        return res;
    }

    private static void EnsureFont(Page page, string baseFont, string res)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null) { resources = new Core.PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fonts = resources.Get("Font") as Core.PdfDictionary;
        if (fonts is null) { fonts = new Core.PdfDictionary(); resources.Set("Font", fonts); }
        if (fonts.ContainsKey(res)) return;
        var f = new Core.PdfDictionary();
        f.Set("Type", new Core.PdfName("Font"));
        f.Set("Subtype", new Core.PdfName("Type1"));
        f.Set("BaseFont", new Core.PdfName(baseFont));
        f.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
        fonts.Set(res, f);
    }

    private static double FontSize(XmlElement e)
    {
        // 10pt is the XFA default type size (a Designer <font> without size means 10).
        var f = FirstChild(e, "font");
        return f is null ? 10 : Len(f.GetAttribute("size"), 10);
    }

    /// <summary>The element's explicit font size, or null when the font carries none.</summary>
    private static double? FontSizeN(XmlElement e)
    {
        var f = FirstChild(e, "font");
        return f is null ? null : LenN(f.GetAttribute("size"));
    }

    private static bool FontBold(XmlElement e) =>
        FirstChild(e, "font") is { } f
        && (f.GetAttribute("weight") == "bold"
            // "Black"-class faces (Arial Black) are inherently heavy — no weight attr.
            || f.GetAttribute("typeface").Contains("Black", StringComparison.OrdinalIgnoreCase));

    /// <summary>Advance-width factor for faces wider than the Helvetica model:
    /// Arial Black runs ~15% wider than Helvetica-Bold. The emitter mirrors the
    /// factor as Tz so painted ink matches the measured span.</summary>
    private static double FontWideFactor(XmlElement e) =>
        FirstChild(e, "font")?.GetAttribute("typeface")
            .Contains("Black", StringComparison.OrdinalIgnoreCase) == true ? 1.15 : 1.0;

    /// <summary>The element's font typeface when it is a NON-default family the
    /// system resolves (Verdana, Tahoma, Calibri, …). Null keeps the calibrated
    /// Helvetica/Times model: default faces, unresolvable Designer faces (Myriad),
    /// and Arial Black (which keeps its Tz wide-factor emulation).</summary>
    private static string? ResolvedFamily(XmlElement e, bool bold)
    {
        var tf = FirstChild(e, "font")?.GetAttribute("typeface") ?? "";
        if (tf.Length == 0) return null;
        if (tf.Contains("Arial", StringComparison.OrdinalIgnoreCase)
            || tf.Contains("Helvetica", StringComparison.OrdinalIgnoreCase)
            || tf.Contains("Times", StringComparison.OrdinalIgnoreCase)
            || tf.Contains("Black", StringComparison.OrdinalIgnoreCase))
            return null;
        return FamilyFont(tf, bold, false) is not null ? tf : null;
    }

    /// <summary>Advance width of <paramref name="s"/> in the resolved family face;
    /// falls back to the Helvetica model when the face is unavailable.</summary>
    private static double FamTextWidth(string s, double fs, string family, bool bold, bool italic = false)
    {
        if (FamilyFont(family, bold, italic) is not { } f) return TextWidth(s, fs, bold);
        double adv = 0;
        var upm = (double)f.parser.UnitsPerEm;
        foreach (var ch in s)
            adv += f.parser.CMap.TryGetValue(ch, out var gid)
                ? Math.Round(f.parser.GetAdvanceWidth(gid) * 1000.0 / upm)
                : 500;
        return adv * fs / 1000.0;
    }

    private static double[] FontColor(XmlElement e)
    {
        var f = FirstChild(e, "font");
        var col = f is null ? null : FirstChild(f, "fill");
        return ColorOf(col) ?? new double[] { 0, 0, 0 };
    }

    private static double[]? FillColor(XmlElement e)
    {
        // A box's background fill is either a direct <fill> or the <border>'s <fill>.
        var direct = ColorOf(FirstChild(e, "fill"));
        if (direct is not null) return direct;
        var border = FirstChild(e, "border");
        return border is null ? null : ColorOf(FirstChild(border, "fill"));
    }

    private static double[]? ColorOf(XmlElement? fill)
    {
        if (fill is null) return null;
        if (fill.GetAttribute("presence") == "hidden") return null;
        var color = FirstChild(fill, "color");
        if (color is null) return null;                 // <fill> with no <color> defaults to white — skip
        var v = color.GetAttribute("value");
        var parts = v.Split(',');
        if (parts.Length != 3) return null;
        return new[]
        {
            int.TryParse(parts[0], out var r) ? r / 255.0 : 0,
            int.TryParse(parts[1], out var g) ? g / 255.0 : 0,
            int.TryParse(parts[2], out var bl) ? bl / 255.0 : 0,
        };
    }
}
