using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>A free-text annotation: text shown directly on the page inside a box, styled by its default appearance.</summary>
public partial class FreeTextAnnotation : MarkupAnnotation
{
    internal FreeTextAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a new FreeTextAnnotation with a default appearance.</summary>
    public FreeTextAnnotation(Page page, Rectangle rect, DefaultAppearance appearance)
        : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("FreeText"));
        _defaultAppearance = appearance ?? DefaultFreeTextAppearance();
        Dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(_defaultAppearance.ToAppearanceString())));
    }

    /// <summary>Document-bound ctor for creating a FreeTextAnnotation that
    /// isn't yet attached to a specific page; the caller adds it via
    /// <c>page.Annotations.Add(annot)</c>.</summary>
    public FreeTextAnnotation(Document document, DefaultAppearance appearance)
        : base(document, rect: null!)
    {
        Dict.Set("Subtype", new PdfName("FreeText"));
        _defaultAppearance = appearance ?? DefaultFreeTextAppearance();
        Dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(_defaultAppearance.ToAppearanceString())));
    }

    /// <summary>Fallback /DA for a FreeText annotation created with no explicit
    /// appearance: Helvetica ("Helv") at 10pt black. A null appearance must still
    /// produce a valid, non-zero font size rather than an empty or zero-size /DA.</summary>
    private static DefaultAppearance DefaultFreeTextAppearance() => new("Helv", 10);

    private DefaultAppearance? _defaultAppearance;

    /// <summary>The /DA default-appearance string.</summary>
    public string? DefaultAppearance
    {
        get => (Dict.Get("DA") as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("DA");
            else Dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(value)));
        }
    }

    /// <summary>The strongly-typed default-appearance object backing
    /// <see cref="DefaultAppearance"/>. The construction-time appearance is stored;
    /// for an annotation read from a document (no stored object) the /DA string is
    /// parsed so the font, size and colour drive the generated appearance.</summary>
    public DefaultAppearance DefaultAppearanceObject =>
        _defaultAppearance ??= ParseDefaultAppearance(DefaultAppearance) ?? new DefaultAppearance();

    /// <summary>Parse a /DA appearance string (e.g. "/Helv 16 Tf 0 0 1 rg") into a typed
    /// <see cref="DefaultAppearance"/>. Handles the rg/g/k colour operators and the common
    /// Standard-14 resource abbreviations. Returns null when nothing recognisable is found.</summary>
    private static DefaultAppearance? ParseDefaultAppearance(string? da)
    {
        if (string.IsNullOrWhiteSpace(da)) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var t = da.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        double? TryD(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, ci, out var v) ? v : null;
        string fontName = "Helvetica"; double size = 12; var color = System.Drawing.Color.Black;
        bool got = false;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == "Tf" && i >= 2)
            {
                if (t[i - 2].StartsWith("/")) fontName = NormalizeDaFontName(t[i - 2].Substring(1));
                if (TryD(t[i - 1]) is { } s && s > 0) size = s;
                got = true;
            }
            else if (t[i] == "rg" && i >= 3 && TryD(t[i - 3]) is { } r && TryD(t[i - 2]) is { } g && TryD(t[i - 1]) is { } b)
            { color = System.Drawing.Color.FromArgb(C(r), C(g), C(b)); got = true; }
            else if (t[i] == "g" && i >= 1 && TryD(t[i - 1]) is { } gray)
            { color = System.Drawing.Color.FromArgb(C(gray), C(gray), C(gray)); got = true; }
            else if (t[i] == "k" && i >= 4 && TryD(t[i - 4]) is { } c && TryD(t[i - 3]) is { } m && TryD(t[i - 2]) is { } y && TryD(t[i - 1]) is { } k)
            { color = System.Drawing.Color.FromArgb(C((1 - c) * (1 - k)), C((1 - m) * (1 - k)), C((1 - y) * (1 - k))); got = true; }
        }
        return got ? new DefaultAppearance(fontName, size, color) : null;

        static int C(double v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255)));
    }

    private static string NormalizeDaFontName(string n) => n switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "Symb" => "Symbol",
        "ZaDb" => "ZapfDingbats",
        _ => n,
    };

    /// <summary>Inline default rich-text style carried in /DS.</summary>
    public string? DefaultStyle
    {
        get => (Dict.Get("DS") as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("DS");
            else Dict.Set("DS", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Rich text string (XHTML) for the annotation (/RC entry). Setting it parses the
    /// XHTML span styles (font-weight/font-style/text-decoration and base font/size/colour) into
    /// the plain text plus per-range style runs, and regenerates the styled appearance.</summary>
    public new string? RichText
    {
        get => GetString("RC");
        set
        {
            if (value is null) Dict.Remove("RC");
            else Dict.Set("RC", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
            // Capture the rich-text styling (plain text, per-range runs, base size/colour) so a
            // following SetTextStyle renders it, but do NOT take over appearance generation here:
            // rich text alone falls back to the normal save-time appearance (matching the
            // established output), and the styled path is driven by explicit SetTextStyle calls.
            ApplyRichTextStyles(value);
        }
    }

    /// <summary>Always <see cref="AnnotationType.FreeText"/>. Redeclared on
    /// the derived class so DeclaredOnly reflection sees it.</summary>
    public new AnnotationType AnnotationType => AnnotationType.FreeText;

    /// <summary>
    /// Build the /AP /N appearance stream from <see cref="Annotation.Contents"/>,
    /// laying the text out as word-wrapped lines inside the annotation rectangle using
    /// the /DA font, size and colour. No-op when an appearance already exists or there
    /// is no text. Invoked by the save pipeline so a freshly-created FreeText annotation
    /// renders (and exposes <see cref="Annotation.NormalAppearance"/>).
    /// </summary>
    /// <summary>Changing a FreeText annotation's /C background colour invalidates its
    /// stored /AP, which still paints the old background. Drop the existing appearance
    /// and rebuild it from /Contents + /DA so it reflects the new colour — the FreeText
    /// appearance regenerates on a colour change
    /// (dropping any previously-embedded font for the /DA's standard font). Only rebuilt
    /// when there is text to render; otherwise the existing appearance is left intact.</summary>
    private protected override void OnColorChanged()
    {
        var text = Contents;
        if (string.IsNullOrEmpty(text)) text = PlainTextFromRichText(RichText);
        if (string.IsNullOrEmpty(text)) return;
        Dict.Remove("AP");
        InvalidateAppearanceCache();
        GenerateAppearance();
    }

    /// <summary>Extract the plain text of a FreeText /RC rich-text packet: strip the
    /// XHTML/XFA markup (and the XML declaration) and decode the basic entities,
    /// leaving the concatenated character data. Returns the input unchanged when it
    /// carries no markup.</summary>
    private static string? PlainTextFromRichText(string? rc)
    {
        if (string.IsNullOrEmpty(rc) || rc.IndexOf('<') < 0) return rc;
        var sb = new System.Text.StringBuilder(rc.Length);
        var inTag = false;
        foreach (var c in rc)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString()
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&apos;", "'").Trim();
    }

    /// <summary>Greedy word-wrap: a word starts a new line when appending it would
    /// exceed <paramref name="avail"/>; a single word wider than the line still occupies
    /// its own line (no mid-word breaking).</summary>
    private static System.Collections.Generic.List<string> WrapText(
        string text, Aspose.Pdf.Text.FontMetrics metrics, double fontSize, double avail)
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var words = rawLine.Split(' ');
            var current = "";
            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    current = word;
                    continue;
                }
                var candidate = current + " " + word;
                if (metrics.MeasureString(candidate, fontSize) <= avail)
                    current = candidate;
                else
                {
                    result.Add(current);
                    current = word;
                }
            }
            result.Add(current);
        }
        return result;
    }

    /// <summary>Border width from /BS /W or the legacy /Border array (third element); defaults to 1.</summary>
    private double ReadBorderWidth()
    {
        var bs = InternalReader.ResolveDict(Dict.Get("BS"));
        if (bs is not null)
        {
            var wObj = bs.Get("W");
            if (wObj is PdfInteger bi) return bi.Value;
            if (wObj is PdfReal br) return br.Value;
        }
        if (InternalReader.Resolve(Dict.Get("Border")) is PdfArray arr && arr.Count >= 3)
        {
            if (arr[2] is PdfInteger ai) return ai.Value;
            if (arr[2] is PdfReal ar) return ar.Value;
        }
        return 1.0;
    }

    private static string ResName(string fontName) => fontName.Replace(" ", "");

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>A standard-14 Type1 font dictionary for the appearance, mapping common
    /// aliases (Arial→Helvetica) so metrics and rendering resolve.</summary>
    private static PdfDictionary MakeFreeTextFontDict(string fontName)
    {
        var n = fontName.Replace(" ", "");
        string baseFont = n switch
        {
            "Arial" or "Helv" or "Helvetica" => "Helvetica",
            "ArialBold" or "Arial-Bold" or "HelveticaBold" => "Helvetica-Bold",
            "TimesNewRoman" or "Times" or "TiRo" => "Times-Roman",
            "CourierNew" or "Cour" or "Courier" => "Courier",
            _ => Aspose.Pdf.Text.Standard14Fonts.IsStandard14(n) ? n : "Helvetica",
        };
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        return font;
    }

    /// <summary>Callout polyline (/CL entry, PDF 32000 §12.5.6.6 — Free
    /// text). Three points: leader-end, knee, baseline-anchor.</summary>
    public Point[]? Callout
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("CL")) as PdfArray;
            if (arr is null || arr.Count < 4) return null;
            var pts = new List<Point>(arr.Count / 2);
            for (var i = 0; i + 1 < arr.Count; i += 2)
            {
                double x = arr[i] is PdfReal rx ? rx.Value : arr[i] is PdfInteger ix ? ix.Value : 0;
                double y = arr[i + 1] is PdfReal ry ? ry.Value : arr[i + 1] is PdfInteger iy ? iy.Value : 0;
                pts.Add(new Point(x, y));
            }
            return pts.ToArray();
        }
        set
        {
            if (value is null) { Dict.Remove("CL"); return; }
            var arr = new PdfArray();
            foreach (var p in value)
            {
                arr.Add(new PdfReal(p.X));
                arr.Add(new PdfReal(p.Y));
            }
            Dict.Set("CL", arr);
        }
    }

    /// <summary>Justification of the rendered text (/Q: 0=Left, 1=Center, 2=Right).</summary>
    public Justification Justification
    {
        get
        {
            var q = (int)(Dict.Get("Q") is PdfInteger qi ? qi.Value : 0);
            return q switch
            {
                1 => Justification.Center,
                2 => Justification.Right,
                _ => Justification.Left,
            };
        }
        set => Dict.Set("Q", new PdfInteger(value switch
        {
            Justification.Center => 1,
            Justification.Right => 2,
            _ => 0,
        }));
    }

    /// <summary>Free-text intent (/IT — FreeTextCallout / FreeTextTypeWriter).</summary>
    public FreeTextIntent Intent
    {
        get => Dict.GetName("IT") switch
        {
            "FreeTextCallout" => FreeTextIntent.FreeTextCallout,
            "FreeTextTypeWriter" => FreeTextIntent.FreeTextTypeWriter,
            _ => FreeTextIntent.Undefined,
        };
        set
        {
            if (value == FreeTextIntent.Undefined) Dict.Remove("IT");
            else Dict.Set("IT", new PdfName(value.ToString()));
        }
    }

    /// <summary>Starting line-ending style (/LE first entry; callout intent only).</summary>
    public LineEnding StartingStyle
    {
        get => GetCalloutLineEnding(0);
        set => SetCalloutLineEnding(0, value);
    }

    /// <summary>Ending line-ending style (/LE second entry; callout intent only).</summary>
    public LineEnding EndingStyle
    {
        get => GetCalloutLineEnding(1);
        set => SetCalloutLineEnding(1, value);
    }

    // A FreeText callout has a single line ending — the arrowhead at the callout's
    // pointed end. It may be stored as a single /LE name (the PDF-spec form) or as a
    // two-element [head tail] array (the form Acrobat/XFDF round-trips produce, with the
    // ending carried in one slot). Report that single ending for both StartingStyle and
    // EndingStyle, which matches the callout model.
    private LineEnding GetCalloutLineEnding(int index)
    {
        var le = InternalReader.Resolve(Dict.Get("LE"));
        if (le is PdfName name)
            return LineAnnotation.ParseLineEnding(name.Value);
        if (le is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var v = (InternalReader.Resolve(arr[i]) as PdfName)?.Value;
                if (v is not null && v != "None")
                    return LineAnnotation.ParseLineEnding(v);
            }
        }
        return LineEnding.None;
    }

    private void SetCalloutLineEnding(int index, LineEnding value)
    {
        var arr = InternalReader.Resolve(Dict.Get("LE")) as PdfArray;
        var start = "None";
        var end = "None";
        if (arr is { Count: >= 1 } && InternalReader.Resolve(arr[0]) is PdfName s) start = s.Value;
        if (arr is { Count: >= 2 } && InternalReader.Resolve(arr[1]) is PdfName e) end = e.Value;
        else if (InternalReader.Resolve(Dict.Get("LE")) is PdfName single) { start = single.Value; end = single.Value; }
        if (index == 0) start = LineAnnotation.LineEndingToName(value);
        else end = LineAnnotation.LineEndingToName(value);
        var newArr = new PdfArray();
        newArr.Add(new PdfName(start));
        newArr.Add(new PdfName(end));
        Dict.Set("LE", newArr);
    }

    /// <summary>Page-rotation of the rendered text (/Rotate). Multiples of 90°.</summary>
    public Aspose.Pdf.Rotation Rotate
    {
        get
        {
            var r = (int)(Dict.Get("Rotate") is PdfInteger ri ? ri.Value : 0);
            return r switch
            {
                90 => Aspose.Pdf.Rotation.on90,
                180 => Aspose.Pdf.Rotation.on180,
                270 => Aspose.Pdf.Rotation.on270,
                _ => Aspose.Pdf.Rotation.None,
            };
        }
        set => Dict.Set("Rotate", new PdfInteger((int)value));
    }

    /// <summary>Inner text rectangle (/RD inset from /Rect).</summary>
    public Rectangle? TextRectangle
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("RD")) as PdfArray;
            if (arr is null || arr.Count < 4) return null;
            double L = arr[0] is PdfReal r0 ? r0.Value : 0;
            double B = arr[1] is PdfReal r1 ? r1.Value : 0;
            double R = arr[2] is PdfReal r2 ? r2.Value : 0;
            double T = arr[3] is PdfReal r3 ? r3.Value : 0;
            return new Rectangle(L, B, R, T);
        }
        set
        {
            if (value is null) { Dict.Remove("RD"); return; }
            var arr = new PdfArray();
            arr.Add(new PdfReal(value.LLX));
            arr.Add(new PdfReal(value.LLY));
            arr.Add(new PdfReal(value.URX));
            arr.Add(new PdfReal(value.URY));
            Dict.Set("RD", arr);
        }
    }

    /// <summary>Bundled text style (font / size / colour / alignment) applied when rendering the
    /// rich-text contents. A mutable stored object: <c>annot.TextStyle.FontSize = 18</c> persists
    /// and drives the generated appearance.</summary>
    public TextStyle TextStyle { get; set; } = new TextStyle();

    /// <summary>Apply style flags to the whole text run. The <paramref name="fontName"/>/
    /// <paramref name="fontSize"/>/<paramref name="fontColor"/> arguments mirror the caller's
    /// current <see cref="TextStyle"/>; they are recorded on <see cref="TextStyle"/> but the
    /// rendered base font is kept as the annotation's established /DA (so a bold base such as
    /// "Arial Bold" survives), with the size/colour applied.</summary>
    public void SetTextStyle(RichTextFontStyles textStyles, string fontName, double fontSize, System.Drawing.Color fontColor)
    {
        TextStyle = new TextStyle { FontName = fontName, FontSize = fontSize, Color = fontColor };
        var baseFont = DefaultAppearanceObject.FontName;
        DefaultAppearance = new DefaultAppearance(baseFont, fontSize, fontColor).ToAppearanceString();
        _defaultAppearance = new DefaultAppearance(baseFont, fontSize, fontColor);
        // The whole-run overload replaces any existing per-range styles with these flags
        // (a later whole-text SetTextStyle overrides earlier rich-text spans).
        var len = StyledSourceTextLength();
        _styleRuns.Clear();
        _styleRuns.Add((0, len, textStyles));
        RegenerateStyledAppearance();
    }

    /// <summary>Apply rich-text style flags to the substring [fromInd, toInd).
    /// <see cref="RichTextFontStyles.ClearExisting"/> (value 0) on its own clears the range;
    /// any other flags are OR-ed into the range. The annotation appearance is regenerated.</summary>
    public void SetTextStyle(int fromInd, int toInd, RichTextFontStyles textStyles)
    {
        _styleRuns.Add((fromInd, toInd, textStyles));
        RegenerateStyledAppearance();
    }

    // Ordered list of per-range style applications (from, to, styles). Replayed in order to
    // build a per-character style array. styles == 0 (ClearExisting alone) clears the range.
    private readonly System.Collections.Generic.List<(int from, int to, RichTextFontStyles styles)> _styleRuns = new();

    private int StyledSourceTextLength()
    {
        var t = Contents;
        if (string.IsNullOrEmpty(t)) t = PlainTextFromRichText(RichText);
        return t?.Length ?? 0;
    }

    private RichTextFontStyles[] ResolveCharStyles(int len)
    {
        var arr = new RichTextFontStyles[len];
        foreach (var (from, to, styles) in _styleRuns)
        {
            int a = System.Math.Max(0, System.Math.Min(from, to));
            int b = System.Math.Min(len, System.Math.Max(from, to));
            for (int i = a; i < b; i++)
                if (styles == 0) arr[i] = 0; else arr[i] |= styles; // 0 == ClearExisting (clear range)
        }
        return arr;
    }

    private static string VariantFontName(string baseFont, bool bold, bool italic)
    {
        var f = (baseFont ?? "").Replace(" ", "");
        // A bold/italic base font name (e.g. "Arial Bold") contributes the style itself.
        if (f.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0) bold = true;
        if (f.IndexOf("Italic", System.StringComparison.OrdinalIgnoreCase) >= 0
            || f.IndexOf("Oblique", System.StringComparison.OrdinalIgnoreCase) >= 0) italic = true;
        if (f.IndexOf("Times", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold" : italic ? "Times-Italic" : "Times-Roman";
        if (f.IndexOf("Courier", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold" : italic ? "Courier-Oblique" : "Courier";
        return bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica";
    }

    private bool BorderExplicitlyZero()
    {
        var bs = InternalReader.ResolveDict(Dict.Get("BS"));
        if (bs is not null)
        {
            if (bs.Get("W") is PdfInteger bi) return bi.Value == 0;
            if (bs.Get("W") is PdfReal br) return br.Value == 0;
        }
        if (InternalReader.Resolve(Dict.Get("Border")) is PdfArray arr && arr.Count >= 3)
        {
            if (arr[2] is PdfInteger ai) return ai.Value == 0;
            if (arr[2] is PdfReal ar) return ar.Value == 0;
        }
        return false;
    }

    /// <summary>Parse XHTML rich-text styling into the plain text, per-range style runs and a
    /// base font size/colour. Returns true when styled markup was found (so the caller should
    /// regenerate the styled appearance); false for plain text or no markup.</summary>
    private bool ApplyRichTextStyles(string? xhtml)
    {
        if (string.IsNullOrEmpty(xhtml) || xhtml!.IndexOf('<') < 0) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sbText = new System.Text.StringBuilder();
        var runs = new System.Collections.Generic.List<(int from, int to, RichTextFontStyles st)>();
        var stack = new System.Collections.Generic.Stack<(bool b, bool i, bool u)>();
        stack.Push((false, false, false));
        string? baseFont = null; double baseSize = 0; System.Drawing.Color? baseColor = null;
        int p = 0;
        while (p < xhtml.Length)
        {
            if (xhtml[p] == '<')
            {
                int gt = xhtml.IndexOf('>', p); if (gt < 0) break;
                string tag = xhtml.Substring(p + 1, gt - p - 1);
                if (tag.StartsWith("/")) { if (stack.Count > 1) stack.Pop(); }
                else if (!tag.StartsWith("?") && !tag.StartsWith("!"))
                {
                    var cur = stack.Peek();
                    bool b = cur.b, it = cur.i, u = cur.u;
                    var sm = System.Text.RegularExpressions.Regex.Match(tag, "style\\s*=\\s*\"([^\"]*)\"");
                    if (sm.Success)
                    {
                        var css = sm.Groups[1].Value;
                        if (css.Replace(" ", "").Contains("font-weight:bold")) b = true;
                        if (css.Replace(" ", "").Contains("font-style:italic")) it = true;
                        if (css.Contains("underline")) u = true;
                        var fs = System.Text.RegularExpressions.Regex.Match(css, "font-size:\\s*([0-9.]+)pt");
                        if (fs.Success && baseSize == 0) double.TryParse(fs.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out baseSize);
                        var ff = System.Text.RegularExpressions.Regex.Match(css, "font-family:\\s*([^;\"]+)");
                        if (ff.Success && baseFont == null) baseFont = ff.Groups[1].Value.Trim();
                        var cm = System.Text.RegularExpressions.Regex.Match(css, "color:\\s*#([0-9A-Fa-f]{6})");
                        if (cm.Success && baseColor == null)
                        {
                            var h = cm.Groups[1].Value;
                            baseColor = System.Drawing.Color.FromArgb(
                                System.Convert.ToInt32(h.Substring(0, 2), 16),
                                System.Convert.ToInt32(h.Substring(2, 2), 16),
                                System.Convert.ToInt32(h.Substring(4, 2), 16));
                        }
                    }
                    if (!tag.EndsWith("/")) stack.Push((b, it, u));
                }
                p = gt + 1;
            }
            else
            {
                int lt = xhtml.IndexOf('<', p); if (lt < 0) lt = xhtml.Length;
                var raw = xhtml.Substring(p, lt - p)
                    .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&#xD;", "").Replace("&#xA;", "");
                if (raw.Length > 0)
                {
                    var cur = stack.Peek();
                    RichTextFontStyles st = 0;
                    if (cur.b) st |= RichTextFontStyles.Bold;
                    if (cur.i) st |= RichTextFontStyles.Italic;
                    if (cur.u) st |= RichTextFontStyles.Underline;
                    int start = sbText.Length;
                    sbText.Append(raw);
                    if (st != 0) runs.Add((start, sbText.Length, st));
                }
                p = lt;
            }
        }
        var plain = sbText.ToString();
        if (string.IsNullOrEmpty(Contents)) Contents = plain;
        // Keep the construction-time base font (it may already encode weight, e.g. "Arial Bold");
        // only the size/colour are taken from the rich text.
        if (baseSize > 0 || baseColor != null)
        {
            var f = DefaultAppearanceObject.FontName;
            var s = baseSize > 0 ? baseSize : DefaultAppearanceObject.FontSize;
            var c = baseColor ?? DefaultAppearanceObject.TextColor;
            DefaultAppearance = new DefaultAppearance(f, s, c).ToAppearanceString();
            _defaultAppearance = new DefaultAppearance(f, s, c);
        }
        _styleRuns.AddRange(runs);
        return runs.Count > 0 || baseSize > 0 || baseColor != null;
    }

    /// <summary>Regenerate the /AP /N appearance honouring per-range rich-text styles
    /// (bold/italic/underline) set via <see cref="SetTextStyle(int,int,RichTextFontStyles)"/>,
    /// the /DA font/size/colour, and a default 1pt border (unless the border was set to 0).
    /// Word-wraps within the rectangle, measuring each run with its styled font variant.</summary>
    internal void RegenerateStyledAppearance()
    {
        var ra = new StyledAppearanceState();
        ra.rect = Rect;
        if (ra.rect is null || ra.rect.Width <= 0 || ra.rect.Height <= 0) return;
        ra.text = Contents;
        if (string.IsNullOrEmpty(ra.text)) ra.text = PlainTextFromRichText(RichText);
        if (string.IsNullOrEmpty(ra.text)) return;
        ra.text = ra.text!.Replace("\r\n", "\n").Replace("\r", "\n");

        ra.da = DefaultAppearanceObject;
        ra.baseFont = string.IsNullOrWhiteSpace(ra.da.FontName) ? "Helvetica" : ra.da.FontName!;
        ra.size = ra.da.FontSize > 0 ? ra.da.FontSize : 12.0;
        ra.color = ra.da.TextColor;

        ra.border = BorderExplicitlyZero() ? 0 : System.Math.Max(1.0, ReadBorderWidth());
        ra.inset = ra.border + 2.0;
        ra.w = ra.rect.Width;
        ra.h = ra.rect.Height;
        ra.avail = System.Math.Max(1.0, ra.w - 2 * ra.inset);
        ra.leading = ra.size * 1.15;

        ra.styles = ResolveCharStyles(ra.text.Length);

        ra.fontDicts = new System.Collections.Generic.Dictionary<string, PdfDictionary>();
        ra.metricsCache = new System.Collections.Generic.Dictionary<string, Aspose.Pdf.Text.FontMetrics>();
        ra.outLines = new System.Collections.Generic.List<System.Collections.Generic.List<(char ch, RichTextFontStyles st)>>();
        ra.line = new System.Collections.Generic.List<(char, RichTextFontStyles)>();
        ra.lineW = 0;
        ra.word = new System.Collections.Generic.List<(char ch, RichTextFontStyles st)>();
        ra.wordW = 0;
        for (int i = 0; i < ra.text.Length; i++)
        {
            char c = ra.text[i]; var s = ra.styles[i];
            if (c == '\n') { FtFlushWord(ra); ra.outLines.Add(ra.line); ra.line = new(); ra.lineW = 0; continue; }
            if (c == ' ')
            {
                FtFlushWord(ra);
                double sw = FtCharW(ra, ' ', s);
                if (ra.lineW > 0) { ra.line.Add((c, s)); ra.lineW += sw; } // skip leading spaces after a wrap
                continue;
            }
            ra.word.Add((c, s)); ra.wordW += FtCharW(ra, c, s);
        }
        FtFlushWord(ra);
        if (ra.line.Count > 0 || ra.outLines.Count == 0) ra.outLines.Add(ra.line);

        ra.ci = System.Globalization.CultureInfo.InvariantCulture;
        ra.sb = new System.Text.StringBuilder();

        // Border box (default 1pt unless set to 0), stroked in the text colour.
        if (ra.border > 0)
        {
            ra.sb.Append("q\n").Append(FtNum(ra, ra.color.R / 255.0)).Append(' ').Append(FtNum(ra, ra.color.G / 255.0)).Append(' ')
              .Append(FtNum(ra, ra.color.B / 255.0)).Append(" RG\n").Append(FtNum(ra, ra.border)).Append(" w\n")
              .Append(FtNum(ra, ra.border / 2)).Append(' ').Append(FtNum(ra, ra.border / 2)).Append(' ')
              .Append(FtNum(ra, ra.w - ra.border)).Append(' ').Append(FtNum(ra, ra.h - ra.border)).Append(" re\nS\nQ\n");
        }

        ra.underlines = new System.Collections.Generic.List<(double x, double y, double len)>();
        ra.sb.Append("/Tx BMC\nq\nBT\n");
        ra.sb.Append(FtNum(ra, ra.color.R / 255.0)).Append(' ').Append(FtNum(ra, ra.color.G / 255.0)).Append(' ').Append(FtNum(ra, ra.color.B / 255.0)).Append(" rg\n");
        ra.y0 = ra.h - ra.inset - ra.size;
        for (int li = 0; li < ra.outLines.Count; li++)
        {
            if (!EmitStyledAppearanceLine(ra, li)) break;
        }
        ra.sb.Append("ET\n");
        foreach (var (ux, uy, ulen) in ra.underlines)
            ra.sb.Append(FtNum(ra, ra.color.R / 255.0)).Append(' ').Append(FtNum(ra, ra.color.G / 255.0)).Append(' ').Append(FtNum(ra, ra.color.B / 255.0)).Append(" RG\n")
              .Append(FtNum(ra, System.Math.Max(0.5, ra.size * 0.06))).Append(" w\n")
              .Append(FtNum(ra, ux)).Append(' ').Append(FtNum(ra, uy)).Append(" m\n").Append(FtNum(ra, ux + ulen)).Append(' ').Append(FtNum(ra, uy)).Append(" l\nS\n");
        ra.sb.Append("Q\nEMC\n");

        ra.apStream = new PdfStream(new PdfDictionary(), Compat.Latin1.GetBytes(ra.sb.ToString()));
        ra.apStream.Dict.Set("Type", new PdfName("XObject"));
        ra.apStream.Dict.Set("Subtype", new PdfName("Form"));
        ra.bbox = new PdfArray();
        ra.bbox.Add(new PdfReal(0)); ra.bbox.Add(new PdfReal(0)); ra.bbox.Add(new PdfReal(ra.w)); ra.bbox.Add(new PdfReal(ra.h));
        ra.apStream.Dict.Set("BBox", ra.bbox);
        ra.fonts = new PdfDictionary();
        foreach (var kv in ra.fontDicts) ra.fonts.Set(ResName(kv.Key), kv.Value);
        ra.res = new PdfDictionary();
        ra.res.Set("Font", ra.fonts);
        ra.apStream.Dict.Set("Resources", ra.res);
        ra.ap = new PdfDictionary();
        ra.ap.Set("N", ra.apStream);
        Dict.Set("AP", ra.ap);
    }
}
