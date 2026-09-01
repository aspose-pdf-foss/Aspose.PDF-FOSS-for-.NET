using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// A Tx form field that carries the rich-text flag (bit 26 of /Ff). Stores rich-text
/// content via /RV and exposes <see cref="Value"/> as the plain-text representation
/// from /V. Mirrors the public type.
/// </summary>
public sealed class RichTextBoxField : TextBoxField
{
    internal RichTextBoxField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct a rich-text field on the given page rectangle.</summary>
    public RichTextBoxField(Page page, Rectangle rect) : base(page, rect)
    {
        // Set the rich-text bit (PDF table-228 bit 26).
        Dict.Set("Ff", new PdfInteger(FieldFlags | (1 << 25)));
    }

    /// <summary>The rich-text representation of the field value (PDF /RV entry). Setting it also
    /// derives the plain-text /V and regenerates the /AP: the XHTML runs (bold spans, <c>&lt;br/&gt;</c>
    /// line breaks) are laid out with a font per style (regular vs bold) so the appearance shows the
    /// styled text.</summary>
    public string? RichTextValue
    {
        get => Reader.Resolve(Dict.Get("RV")) is PdfString s ? s.ToText() : null;
        set
        {
            Dict.Set("RV", value is null ? null! : EncodePdfTextString(value));
            if (!string.IsNullOrEmpty(value)) ApplyRichText(value!);
            if (OwnerDocument is not null && ObjectNumber >= 0)
                OwnerDocument.MarkDirty(ObjectNumber, Dict);
        }
    }

    /// <summary>One styled run of the rich text: a piece of literal text (with its bold flag)
    /// or a hard line break.</summary>
    private sealed class RtRun { public string Text = ""; public bool Bold; public bool Break; }

    /// <summary>Parse the /RV XHTML, set the plain-text /V, and regenerate the /AP so the styled
    /// runs render with a regular / bold font and honour <c>&lt;br/&gt;</c> breaks.</summary>
    private void ApplyRichText(string rv)
    {
        var runs = new System.Collections.Generic.List<RtRun>();
        // Unstyled rich text lays out in the viewer default face — Helvetica; an
        // explicit font-family style (the styled-span producers write one) overrides.
        string family = "Helvetica";
        double size = 12;
        try
        {
            // Tolerate HTML-style void tags (<br>, <hr>) that aren't well-formed XML by
            // self-closing them before parsing — viewers accept them in /RV. A PAIRED
            // closer (<br></br>) must lose its closer first, or self-closing the opener
            // leaves an orphan </br> that breaks the whole parse.
            var xml = System.Text.RegularExpressions.Regex.Replace(
                rv, @"</\s*(br|hr)\s*>", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            xml = System.Text.RegularExpressions.Regex.Replace(
                xml, @"<(br|hr)(\s[^>/]*)?>", "<$1$2/>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var xdoc = System.Xml.Linq.XDocument.Parse(xml);
            System.Xml.Linq.XElement? p = null;
            foreach (var e in xdoc.Descendants())
                if (e.Name.LocalName == "p") { p = e; break; }
            p ??= xdoc.Root;
            if (p is not null)
            {
                var style = (string?)p.Attribute("style") ?? "";
                var fm = System.Text.RegularExpressions.Regex.Match(style, @"font-family:\s*'?([^;']+)'?");
                if (fm.Success) family = fm.Groups[1].Value.Trim();
                var sm = System.Text.RegularExpressions.Regex.Match(style, @"font-size:\s*([\d.]+)\s*pt");
                if (sm.Success) double.TryParse(sm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out size);
                WalkRichText(p, false, runs);
            }
        }
        catch { return; } // malformed /RV — leave the field value/appearance untouched

        var plain = new System.Text.StringBuilder();
        foreach (var r in runs) plain.Append(r.Break ? "\n" : r.Text);
        Dict.Set("V", EncodePdfTextString(plain.ToString()));

        RegenerateRichAppearance(runs, family, size <= 0 ? 12 : size);
    }

    /// <summary>Recursively collect styled text runs: a bold-weighted <c>&lt;span&gt;</c> marks its
    /// content bold; <c>&lt;br/&gt;</c> is a line break; other decorations (e.g. underline) don't
    /// change the font, so their text merges with the surrounding run.</summary>
    private static void WalkRichText(System.Xml.Linq.XElement el, bool bold, System.Collections.Generic.List<RtRun> runs)
    {
        foreach (var node in el.Nodes())
        {
            if (node is System.Xml.Linq.XText t)
                runs.Add(new RtRun { Text = t.Value, Bold = bold });
            else if (node is System.Xml.Linq.XElement ce)
            {
                if (ce.Name.LocalName == "br") { runs.Add(new RtRun { Break = true }); continue; }
                var childBold = bold
                    || ce.Name.LocalName == "b"
                    || ((string?)ce.Attribute("style") ?? "").Replace(" ", "").Contains("font-weight:bold");
                WalkRichText(ce, childBold, runs);
            }
        }
    }

    /// <summary>Build the /AP/N stream for a list of styled runs: a regular and a bold font resource
    /// (only those actually used), one <c>Tf</c> per font change (so consecutive same-font runs merge
    /// into a single fragment), and a negative-Y <c>Td</c> for each line break.</summary>
    private void RegenerateRichAppearance(System.Collections.Generic.List<RtRun> runs, string family, double size)
    {
        var rectArr = Reader.Resolve(Dict.Get("Rect")) as PdfArray;
        double w = 100, h = 20;
        if (rectArr is { Count: >= 4 })
        {
            var r = Rectangle.FromPdfArray(rectArr);
            w = r.URX - r.LLX; h = r.URY - r.LLY;
        }

        const string regRes = "FR", boldRes = "FB";
        var lineHeight = size * 1.15;
        var firstY = h - lineHeight + 0.207 * size;

        // The value paints in the field's /DA colour (a rich-text field's /DA carries
        // the authored colour, e.g. "0.25 0.333 1 rg"); black stays the fallback.
        var sb = new System.Text.StringBuilder();
        sb.Append("/Tx BMC\nq\nBT\n").Append(ExtractDaColor(ResolveInheritedDa() ?? "", "0 0 0 rg")).Append('\n');
        sb.Append($"2 {Format(firstY)} Td\n");
        string? curFont = null;
        bool usedReg = false, usedBold = false;
        foreach (var run in runs)
        {
            if (run.Break) { sb.Append($"0 {Format(-lineHeight)} Td\n"); continue; }
            if (run.Text.Length == 0) continue;
            var res = run.Bold ? boldRes : regRes;
            if (res != curFont) { sb.Append($"/{res} {Format(size)} Tf\n"); curFont = res; }
            if (run.Bold) usedBold = true; else usedReg = true;
            // One Tj per word (trailing whitespace kept with its word) — text
            // extractors then report the appearance as per-word fragments.
            foreach (System.Text.RegularExpressions.Match word in
                     System.Text.RegularExpressions.Regex.Matches(run.Text, @"\S+\s*|\s+"))
            {
                var esc = word.Value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
                sb.Append($"({esc}) Tj\n");
            }
        }
        sb.Append("ET\nQ\nEMC\n");

        var apStream = new PdfStream(new PdfDictionary(), Aspose.Pdf.Text.Cp1252.GetBytes(sb.ToString()));
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bbox);

        var fonts = new PdfDictionary();
        if (usedReg) fonts.Set(regRes, MakeRichFont(family));
        // The styled variant's name carries a style comma ("Arial,Bold"). A BARE
        // /BaseFont's comma is a separator that FontInfo.FontName normalises away
        // ("ArialBold"), while a subset-tagged one is genuine and kept verbatim —
        // readers must report the styled family exactly, so tag the styled variant
        // (a rich-text /AP reports "Arial,Bold"). The Standard-14 default family
        // instead takes its dash-styled sibling name verbatim ("Helvetica-Bold").
        if (usedBold) fonts.Set(boldRes, MakeRichFont(
            family == "Helvetica" ? "Helvetica-Bold"
                                  : RichSubsetTag() + "+" + family + ",Bold"));
        var res2 = new PdfDictionary();
        res2.Set("Font", fonts);
        apStream.Dict.Set("Resources", res2);

        var newAp = new PdfDictionary();
        newAp.Set("N", apStream);
        Dict.Set("AP", newAp);
    }

    /// <summary>6-uppercase-letter subset tag (PDF 32000 §9.6.4) for the styled rich-text
    /// appearance font.</summary>
    private static string RichSubsetTag()
    {
        var random = new System.Random();
        var chars = new char[6];
        for (int i = 0; i < 6; i++)
            chars[i] = (char)('A' + random.Next(26));
        return new string(chars);
    }

    /// <summary>A Type1 appearance font whose /BaseFont is the given name verbatim (e.g. "Arial"
    /// or "Arial,Bold") so a reader reports that exact family/style.</summary>
    private static PdfDictionary MakeRichFont(string baseFont)
    {
        var f = new PdfDictionary();
        f.Set("Type", new PdfName("Font"));
        f.Set("Subtype", new PdfName("Type1"));
        f.Set("BaseFont", new PdfName(baseFont));
        f.Set("Encoding", new PdfName("WinAnsiEncoding"));
        return f;
    }

    /// <summary>The formatted (rich-text) representation; alias for <see cref="RichTextValue"/>.</summary>
    public string? FormattedValue
    {
        get => RichTextValue;
        set => RichTextValue = value;
    }

    /// <summary>Plain-text value rendered by this field (/V entry). Assigning a rich-text
    /// body (an XHTML <c>&lt;body&gt;</c> fragment) routes through <see cref="RichTextValue"/>
    /// — /RV keeps the markup verbatim and /V gets the derived plain text. Assigning plain
    /// text keeps /RV in sync by wrapping the text in a default-styled body (the field's
    /// /DA font and size), so the formatted and plain representations never diverge.
    /// A real OVERRIDE (not a shadow): callers fill rich fields through base-typed
    /// <c>Field</c> references, and the rich sync must fire for them too.</summary>
    public override string? Value
    {
        get => Reader.Resolve(Dict.Get("V")) is PdfString s ? s.ToText() : null;
        set
        {
            if (!string.IsNullOrEmpty(value) && LooksLikeRichBody(value!))
            {
                RichTextValue = value;
                return;
            }
            // Plain text keeps /V text-exact (values are round-tripped verbatim by
            // export paths — no newline or escaping normalisation may leak in);
            // /RV and the styled appearance are derived as side-effects.
            // EncodePdfTextString, not raw UTF-8: BOM-less bytes read back as
            // PDFDocEncoding, so a non-Latin1 value (e.g. CJK) would corrupt on
            // the save→reopen round-trip.
            Dict.Set("V", EncodePdfTextString(value ?? string.Empty));
            if (!string.IsNullOrEmpty(value))
            {
                Dict.Set("RV", EncodePdfTextString(SynthesizeRichBody(value!)));
                var da = DefaultAppearance;
                var family = ResolveDaFamily(da.FontName);
                var size = da.FontSize > 0 ? da.FontSize : 12;
                var runs = new System.Collections.Generic.List<RtRun>();
                var first = true;
                foreach (var line in value!.Replace("\r\n", "\n").Split('\n', '\r'))
                {
                    if (!first) runs.Add(new RtRun { Break = true });
                    runs.Add(new RtRun { Text = line });
                    first = false;
                }
                RegenerateRichAppearance(runs, family, size);
            }
            if (OwnerDocument is not null && ObjectNumber >= 0)
                OwnerDocument.MarkDirty(ObjectNumber, Dict);
        }
    }

    /// <summary>A value that already is rich-text markup (an XHTML body or a full XML
    /// document) rather than literal text.</summary>
    private static bool LooksLikeRichBody(string value)
    {
        var t = value.TrimStart();
        return t.StartsWith("<body", System.StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("<?xml", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Wrap plain text in the /RV XHTML shape Acrobat writes, styled with the
    /// field's /DA font family and size so the appearance matches the plain rendering.</summary>
    private string SynthesizeRichBody(string plain)
    {
        var da = DefaultAppearance;
        var family = ResolveDaFamily(da.FontName);
        var size = da.FontSize > 0 ? da.FontSize : 12;
        var escaped = new System.Text.StringBuilder();
        foreach (var ch in plain.Replace("\r\n", "\n"))
            escaped.Append(ch switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\n' => "<br/>",
                _ => ch.ToString(),
            });
        return "<body xfa:APIVersion=\"Acroform:2.7.0.0\" xfa:spec=\"2.1\""
             + " xmlns=\"http://www.w3.org/1999/xhtml\""
             + " xmlns:xfa=\"http://www.xfa.org/schema/xfa-data/1.0/\">"
             + "<p dir=\"ltr\" style=\"margin-top:0pt;margin-bottom:0pt;font-family:" + family
             + ";font-size:" + size.ToString(System.Globalization.CultureInfo.InvariantCulture) + "pt\">"
             + escaped + "</p></body>";
    }

    /// <summary>Map the /DA resource alias to a face name via the form's /DR font
    /// dictionary (/BaseFont), falling back to the well-known Acrobat aliases.</summary>
    private string ResolveDaFamily(string daFontName)
    {
        if (string.IsNullOrEmpty(daFontName)) return "Helvetica";
        if (ResolveDrFontDict(daFontName) is { } fd && fd.GetName("BaseFont") is { } bf)
        {
            var name = bf;
            var plus = name.IndexOf('+');
            if (plus == 6) name = name.Substring(7);
            var comma = name.IndexOf(',');
            if (comma > 0) name = name.Substring(0, comma);
            return name;
        }
        return daFontName switch
        {
            "Helv" => "Helvetica",
            "TiRo" => "Times New Roman",
            "Cour" => "Courier",
            _ => daFontName,
        };
    }

    private string _style = string.Empty;

    /// <summary>CSS-style style string applied to the rich-text fragments — e.g.
    /// <c>font: 'Tahoma' bold 10pt</c>. Setting it updates the field's /DA (font family, size and
    /// bold/italic style) so the generated appearance uses the requested font instead of the
    /// default.</summary>
    public string Style
    {
        get => _style;
        set { _style = value ?? string.Empty; ApplyStyleToDefaultAppearance(_style); }
    }

    /// <summary>Parse the style string and write the matching /DA (font + size). Two syntaxes
    /// are accepted: the shorthand <c>font: 'Family' [bold] [italic] Npt</c> and longhand CSS
    /// declarations (<c>font-family: X; font-size: Npt; line-height: Npt</c>). The font family
    /// may contain spaces; it is resolved to a face name the appearance generator can both
    /// render and re-resolve. A <c>line-height</c> becomes the multiline appearance's line
    /// pitch (see the multiline appearance builder).</summary>
    private void ApplyStyleToDefaultAppearance(string style)
    {
        if (string.IsNullOrEmpty(style)) return;
        string? family = null;
        double size = 0;
        var m = System.Text.RegularExpressions.Regex.Match(style,
            @"font\s*:\s*'([^']*)'(.*?)([\d.]+)\s*pt",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
        {
            family = m.Groups[1].Value.Trim();
            double.TryParse(m.Groups[3].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out size);
        }
        else
        {
            var fm = System.Text.RegularExpressions.Regex.Match(style,
                @"font-family\s*:\s*'?([^;'}]+)'?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (fm.Success) family = fm.Groups[1].Value.Trim();
            var sm = System.Text.RegularExpressions.Regex.Match(style,
                @"font-size\s*:\s*([\d.]+)\s*pt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (sm.Success)
                double.TryParse(sm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out size);
        }
        var lm = System.Text.RegularExpressions.Regex.Match(style,
            @"line-height\s*:\s*([\d.]+)\s*pt", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (lm.Success && double.TryParse(lm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var lh) && lh > 0)
            StyleLineHeightPt = lh;
        if (family is null || size <= 0) return;
        StyleNamedFace = true;
        DaFontSizePinned = true;
        StyleFaceBold = m.Success
            && m.Groups[2].Value.IndexOf("bold", System.StringComparison.OrdinalIgnoreCase) >= 0;
        DefaultAppearance = new Aspose.Pdf.Annotations.DefaultAppearance(family, (float)size,
            System.Drawing.Color.Black);
    }

    /// <summary>Text justification.</summary>
    public Aspose.Pdf.Annotations.Justification Justify { get; set; } = Aspose.Pdf.Annotations.Justification.Left;
}
