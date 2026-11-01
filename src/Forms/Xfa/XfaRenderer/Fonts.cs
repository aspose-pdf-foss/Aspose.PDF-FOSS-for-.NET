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

    private static double TextWidth(string s, double fs, bool bold, bool serif = false)
    {
        int[] t;
        if (serif) t = bold ? TimesBoldW : TimesW;
        else t = bold ? HelvBoldW : HelvW;
        int other = serif ? 500 : 556;
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : IsFullWidth(ch) ? FullWidthAdvance : other;
        return w * fs / 1000.0;
    }

    // The serif faces plain text is set in: such text is measured and drawn in the Times
    // model, as the rich text of the same faces is, not in the Helvetica one.
    private static readonly string[] SerifFaces = { "Times", "Minion", "Goudy", "Garamond", "Georgia", "Book Antiqua" };

    /// <summary>True when the element's font is a serif face.</summary>
    private static bool FontSerif(XmlElement e)
    {
        var typeface = FirstChild(e, "font")?.GetAttribute("typeface") ?? string.Empty;
        return SerifFaces.Any(f => typeface.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The element whose font a field's caption is set in: the caption when it
    /// names a typeface of its own, else the field.</summary>
    private static XmlElement CaptionFontOwner(XmlElement e) =>
        FirstChild(e, "caption") is { } caption && FirstChild(caption, "font")?.GetAttribute("typeface") is { Length: > 0 }
            ? caption
            : e;

    private static string RtFontName(bool serif, bool bold, bool italic)
        => (serif ? "TimesNewRoman" : "Arial") + ((bold, italic) switch { (true, true) => "-BoldItalic", (true, _) => "-Bold", (_, true) => "-Italic", _ => "" });

    /// <summary>Advance width of a styled rich-text run, including per-glyph letter
    /// spacing — real TrueType advances when the system font resolves, else the AFM
    /// tables (Times or Helvetica family) - at the horizontal scale the run is set at.</summary>
    private static double TextWidthF(string s, RtRun r) => RunNaturalWidth(s, r) * r.HScale;

    private static double RunNaturalWidth(string s, RtRun r)
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
                    : UnmappedAdvance(f.parser, ch);
            return adv * r.Size / 1000.0 + r.LetterSpacing * s.Length;
        }
        int[] t;
        if (r.Serif) t = r.Bold ? TimesBoldW : TimesW;
        else t = r.Bold ? HelvBoldW : HelvW;
        double w = 0;
        foreach (var ch in s) w += ch is >= (char)32 and <= (char)126 ? t[ch - 32] : 500;
        return w * r.Size / 1000.0 + r.LetterSpacing * s.Length;
    }

    private static byte[] Emit(List<Item> items, Page page)
    {
        var b = new Content.ContentStreamBuilder();
        // The value of a field that gets a widget is drawn into the widget's appearance, with the page's fonts.
        Dictionary<XfaFieldPlacement, Content.ContentStreamBuilder>? values = null;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var it in items)
#pragma warning restore S3267
        {
            var into = b;
            if (it.ValueOf is { ValueOnPage: false } field)
            {
                values ??= new Dictionary<XfaFieldPlacement, Content.ContentStreamBuilder>();
                if (!values.TryGetValue(field, out into)) values[field] = into = new Content.ContentStreamBuilder();
                if (it.Kind == "text") CoverValueLine(field, it);
            }
            if (!EmitXfaItem(into, it, page)) break;
        }
        if (values is not null)
            foreach (var (field, drawn) in values)
                (field.ValueAppearance, field.ValueFonts) = (drawn.Build(), PageFontDict(page));
        return b.Build();
    }

    /// <summary>Share of the type size a line's glyphs reach below its baseline, and above it.</summary>
    private const double ValueLineDescent = 0.3;
    private const double ValueLineAscent = 1.0;

    /// <summary>Grows the widget over a line of its value: what the page would have shown of a value that runs
    /// past the field's box - its descenders, a line that did not fit - stays inside the appearance's box.
    /// A single-line text field is not widened: its line is cut at its box, as a viewer cuts it.</summary>
    private static void CoverValueLine(XfaFieldPlacement p, Item line)
    {
        double bottom = Math.Max(0, Math.Min(p.Y, line.Y - line.FontSize * ValueLineDescent));
        double top = Math.Max(p.Y + p.H, line.Y + line.FontSize * ValueLineAscent);
        (p.Y, p.H) = (bottom, top - bottom);
        if (p.Ft == "Tx" && (p.Ff & XfaFieldPlacement.FlagMultiline) == 0) return;
        double width = TextWidth(line.Text, line.FontSize, line.Bold, line.Serif) * line.HScale + line.CharSpacing * line.Text.Length;
        double left = Math.Max(0, Math.Min(p.X, line.X));
        double right = Math.Max(p.X + p.W, line.X + width);
        (p.X, p.W) = (left, right - left);
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
            else if (!IsExtractionNeutral(c))
                return s; // LTR character — logical order round-trips as-is
        }
        if (!hasRtl) return s;
        var arr = s.ToCharArray();
        Array.Reverse(arr);
        return new string(arr);
    }

    /// <summary>The characters the extractor lets a right-to-left run hold (ASCII spacing and
    /// punctuation): any other character keeps a run from reading as right-to-left.</summary>
    private static bool IsExtractionNeutral(char c) =>
        c == ' ' || c == '\t' || (c >= '!' && c <= '/') || (c >= ':' && c <= '@')
        || (c >= '[' && c <= '`') || (c >= '{' && c <= '~');

    /// <summary>A line that mixes right-to-left letters with anything else (digits, a URL, a
    /// typographic quote), in the order it draws: the bidi algorithm's visual order, cut into
    /// runs of right-to-left letters and runs of the rest. A reader draws them one after the
    /// other, and the extractor turns each right-to-left run back to logical order as it does
    /// a word processor's output. The spaces and marks between two runs of different
    /// directions are a run of their own, belonging to neither. Null for a line of one
    /// direction.</summary>
    private static List<string>? DirectionalRuns(string s)
    {
        if (!Text.BidiReorderer.ContainsRtl(s) || OrderForExtraction(s) != s) return null;
        var visual = Text.BidiReorderer.ToVisualIfRtl(s);
        var runs = new List<string>();
        var start = 0;
        var afterStrong = 0;
        int? direction = null;
        for (var i = 0; i < visual.Length; i++)
        {
            int? strong = Text.BidiReorderer.IsRtlChar(visual[i]) ? 1 : IsExtractionNeutral(visual[i]) ? null : 0;
            if (strong is null) continue;
            if (direction is not null && strong != direction)
            {
                runs.Add(visual[start..afterStrong]);
                if (afterStrong < i) runs.Add(visual[afterStrong..i]);
                start = i;
            }
            direction = strong;
            afterStrong = i + 1;
        }
        runs.Add(visual[start..]);
        return runs;
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
        return EnsureFont(page, baseFont, res);
    }

    /// <summary>The name the standard face goes by on the page: its usual one, or a further one when a face
    /// embedded for another run took that name first - text shown through the other face would be lost.</summary>
    private static string EnsureFont(Page page, string baseFont, string res)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null) { resources = new Core.PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fonts = resources.Get("Font") as Core.PdfDictionary;
        if (fonts is null) { fonts = new Core.PdfDictionary(); resources.Set("Font", fonts); }
        var name = res;
        for (int n = 1; fonts.Get(name) is { } taken; n++)
        {
            if (taken is Core.PdfDictionary existing && existing.GetName("Subtype") == "Type1" && existing.GetName("BaseFont") == baseFont)
                return name;
            name = res + "_" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var f = new Core.PdfDictionary();
        f.Set("Type", new Core.PdfName("Font"));
        f.Set("Subtype", new Core.PdfName("Type1"));
        f.Set("BaseFont", new Core.PdfName(baseFont));
        f.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
        fonts.Set(name, f);
        return name;
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
    /// factor as Tz so painted ink matches the measured span. A font set condensed or widened
    /// (fontHorizontalScale) scales it by that share.</summary>
    private static double FontWideFactor(XmlElement e) => FaceWideFactor(e) * FontHorizontalScale(e);

    /// <summary>The advance-width factor of the face alone, before any horizontal scale the font sets.</summary>
    private static double FaceWideFactor(XmlElement e)
    {
        var typeface = FirstChild(e, "font")?.GetAttribute("typeface") ?? string.Empty;
        if (typeface.Contains("Black", StringComparison.OrdinalIgnoreCase)) return 1.15;
        return IsNarrowDesignerFace(typeface) ? NarrowFaceFactor : 1.0;
    }

    /// <summary>The share of its natural width a font element sets its text at (fontHorizontalScale, a
    /// percentage): 1 when it declares none.</summary>
    private static double FontHorizontalScale(XmlElement e) =>
        HorizontalScale(FirstChild(e, "font")?.GetAttribute("fontHorizontalScale"));

    /// <summary>A horizontal scale written as a percentage ("91%"), as a share; 1 for none or one unreadable.</summary>
    private static double HorizontalScale(string? percent)
    {
        var v = percent?.Trim().TrimEnd('%');
        return !string.IsNullOrEmpty(v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p > 0
            ? p / PercentBase
            : 1.0;
    }

    private const double PercentBase = 100.0;

    // The face form designers fall back to by default is narrower than the Helvetica model
    // that stands in for it where it is not installed: its text takes this share of the
    // model's width, in the measure and in the paint alike, so a caption or a line the
    // designer fitted into its box is not wrapped or run over its neighbour. The share is
    // the mean of nine measured strings (0.906 to 0.944 each, glyph-dependent); a per-glyph
    // table of the face's advances would be exact, but no licence-free source of one exists.
    private const double NarrowFaceFactor = 0.93;
    private const string NarrowDesignerFace = "Myriad";

    // A line the designer fitted into its box must not wrap here: the Helvetica model over-estimates
    // most faces by up to a tenth (captions are given that much room), and the narrow face's factor
    // is a mean whose measured strings spread down to this share of the model - a line of that face
    // is kept whole when it fits at the narrowest share, and painted at the mean.
    private const double CaptionSlack = 1.10;
    private const double NarrowFaceNarrowest = 0.886;

    /// <summary>How much wider than its reserve a caption may measure and still stay on one line,
    /// for text in the given font owner's face.</summary>
    private static double CaptionWrapSlack(XmlElement fontOwner) =>
        FaceWideFactor(fontOwner) < 1 ? NarrowFaceFactor / NarrowFaceNarrowest : CaptionSlack;

    /// <summary>The width factor a wrap decision measures the element's text with: the narrowest
    /// measured share for the narrow face, else the factor it is painted with.</summary>
    private static double WrapWideFactor(XmlElement e)
    {
        var face = FaceWideFactor(e);
        return (face < 1 ? NarrowFaceNarrowest : face) * FontHorizontalScale(e);
    }

    private static bool IsNarrowDesignerFace(string typeface) =>
        typeface.StartsWith(NarrowDesignerFace, StringComparison.OrdinalIgnoreCase)
        && FamilyFont(typeface, false, false) is null;

    /// <summary>The width factor of a field's caption: that of the caption's own font
    /// when it declares one, else the field's.</summary>
    private static double CaptionWideFactor(XmlElement e) => FontWideFactor(CaptionFontOwner(e));

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
                : UnmappedAdvance(f.parser, ch);
        return adv * fs / 1000.0;
    }

    /// <summary>The advance (per mille of the size) of a character the face does not map: a full-width one keeps
    /// the ideographic advance, any other is painted as the face's .notdef glyph and measured as it is painted,
    /// so a tab in a caption does not make its line wider than it was wrapped.</summary>
    private static double UnmappedAdvance(Text.GlyphOutlineParser parser, char ch) =>
        IsFullWidth(ch) ? FullWidthAdvance : Math.Round(parser.GetAdvanceWidth(0) * 1000.0 / parser.UnitsPerEm);

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
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r / 255.0 : 0,
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? g / 255.0 : 0,
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bl) ? bl / 255.0 : 0,
        };
    }
}
