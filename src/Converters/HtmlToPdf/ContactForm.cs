using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The em-grid contact form ────────────────────────────────────────────
    //
    // A hand-authored form page: a centred .max-width column of
    // .contact-form-row bands, each holding inline-block .contact-form-field
    // boxes whose width comes from a per-field class in em. Every field is a
    // title (h3.form-input-title) above a 2 px-bordered control carrying its
    // value; radio rows draw circles, and the tail rows are a textarea pair,
    // three date fields and a plain attachment list.
    //
    // Measured geometry — all in the document's own em
    // (body font-size 14 px, so 1 em = 10.5 pt):
    //  - the column is min(1025 px, content) CENTRED in the page's content box
    //    (measured: the first title opens at 36.12 on the 841 pt sheet with
    //    1 pt margins — (1118.7 − 1025)/2 px + the margin, to 0.02);
    //  - a field advances by its own width + its 2 px borders + the 1 em
    //    right margin (7em→123.04, 27em→419.96, 24em→301.54, 10em→419.96 …
    //    every measured field lands within 0.1 pt);
    //  - a row advances 6.28 em = 65.94 pt — the 3.28 em field height plus the
    //    3 em bottom margin (measured on five consecutive row pairs);
    //  - the control box is the field box inset half a stroke, stroked 1 pt;
    //    the title's glyph top sits 14.44 pt above the box top, and the value's
    //    11.65 pt below it.
    private const double CfEmPx = 14.0;                 // 1 em in css px
    private const double CfMaxWidthPx = 1025.0;
    private const double CfFieldHeightEm = 3.28;
    private const double CfRowGapEm = 3.0;
    private const double CfFieldGapEm = 1.0;
    /// <summary>Both 2 px borders — the field box is content-box, so a field
    /// advances by its em width plus 4 px plus the 1 em gap (probed: 7em → 86.92,
    /// 27em → 297.0, every measured field within 0.1 pt).</summary>
    private const double CfBorderPx = 4.0;
    private const double CfTitleAboveBoxPt = 14.44;
    /// <summary>Value baseline under the control-box top (probed: the value
    /// glyph top lands 11.65 pt below the box top, so its baseline sits here).</summary>
    private const double CfValueBelowBoxPt = 24.98;
    private const double CfTitlePt = 11.97;             // h3 1.14em
    private const double CfValuePt = 10.5;              // 1em
    private const double CfSectionPt = 22.5;            // h2 30px
    /// <summary>The h2 seats 0.82 pt below the title ladder it opens.</summary>
    private const double CfSectionSeatPt = 0.82;
    /// <summary>Title glyph top of the first row under a section heading, measured
    /// from the heading's own glyph top (31.56 → 68.06).</summary>
    private const double CfHeadingToFirstTitlePt = 36.5;
    private const double CfValueInsetPt = 2.0;          // border + the control's own pad
    private const double CfRadioFirstPt = 5.76;         // first circle, from the field left
    private const double CfRadioPitchPt = 43.02;        // option to option
    private const double CfRadioTopPt = 18.98;          // circle top under the title glyph top
    private const double CfRadioRPt = 4.37;             // circle radius
    private const double CfRadioLabelPt = 14.5;         // label left, from the circle left
    /// <summary>A radio row's band: no control box, so it is shorter than a field
    /// row (probed: the question row advances 45.25 pt to the heading below it).</summary>
    private const double CfRadioRowHeightPt = 13.75;
    /// <summary>An h2 that OPENS a row leads by this much (probed: the flow
    /// stands at 545.45 after the Zip row and the heading seats at 559.95).</summary>
    private const double CfInRowHeadingLeadPt = 14.5;
    /// <summary>Flow top of the first section heading, from the page margin
    /// (probed: glyph top 31.56 on the 1 pt-margin sheet).</summary>
    private const double CfFirstHeadingTopPt = 31.43;

    private sealed class CfField
    {
        public double WidthEm = 35;
        public double HeightEm = CfFieldHeightEm;
        public string Title = "";
        public string Value = "";
        public bool IsRadioGroup;
        public List<(string Label, bool Checked)> Radios = new();
        public bool HasControl = true;
    }

    private static Document? TryRenderContactForm(string html, HtmlLoadOptions? options, double pageWidth, double pageHeight, double marginLeft, double marginRight, double marginTop)
    {
        var cf = new ContactFormRenderState();
        cf.html = html;
        cf.options = options;
        cf.pageWidth = pageWidth;
        cf.pageHeight = pageHeight;
        cf.marginLeft = marginLeft;
        cf.marginRight = marginRight;
        cf.marginTop = marginTop;
        if (!cf.html.Contains("contact-form-row", System.StringComparison.Ordinal)
            || !cf.html.Contains("contact-form-field", System.StringComparison.Ordinal)) return null;

        cf.css = ParseStyleSheet(cf.html);
        cf.inv = System.Globalization.CultureInfo.InvariantCulture;
        cf.clsW = new Dictionary<string, (double W, double H)>(System.StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(cf.html, @"\.(?<c>contact-form-field-[\w-]+)\s*\{(?<b>[^}]*)\}"))
        {
            var body = m.Groups["b"].Value;
            var wm = Regex.Match(body, @"(?<![-\w])width\s*:\s*([\d.]+)em");
            var hm = Regex.Match(body, @"(?<![-\w])height\s*:\s*([\d.]+)em");
            if (!wm.Success) continue;
            cf.clsW[m.Groups["c"].Value] = (
                double.Parse(wm.Groups[1].Value, cf.inv),
                hm.Success ? double.Parse(hm.Groups[1].Value, cf.inv) : CfFieldHeightEm);
        }
        if (cf.clsW.Count == 0) return null;

        cf.bodyM = Regex.Match(cf.html, @"<body[^>]*>(?<b>[\s\S]*)</body\s*>", RegexOptions.IgnoreCase);
        if (!cf.bodyM.Success) return null;
        cf.body2 = cf.bodyM.Groups["b"].Value;

        cf.blocks = new List<(string Kind, string Text, List<CfField> Fields)>();
        cf.tokRx = new Regex(
            @"<h2\b[^>]*>(?<h2>[\s\S]*?)</h2\s*>|<div\b[^>]*class=""[^""]*\bcontact-form-row\b[^""]*""[^>]*>",
            RegexOptions.IgnoreCase);
        cf.divRx = new Regex(@"<(?<c>/?)div\b[^>]*>", RegexOptions.IgnoreCase);
        cf.scanPos = 0;
        while (cf.scanPos < cf.body2.Length)
        {
            if (!ScanContactFormToken(cf)) break;
        }
        if (cf.blocks.Count == 0) return null;

        cf.contentPt = cf.pageWidth - cf.marginLeft - cf.marginRight;
        cf.colPt = System.Math.Min(CfEm(CfMaxWidthPx / CfEmPx), cf.contentPt);
        cf.colX = cf.marginLeft + (cf.contentPt - cf.colPt) / 2;

        cf.doc = new Document();
        cf.page = cf.doc.Pages.Add(cf.pageWidth, cf.pageHeight);
        EnsureFonts(cf.page);
        cf.resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        cf.sb = new StringBuilder();

        cf.y = cf.marginTop + CfFirstHeadingTopPt;
        cf.pendingHeading = true;

        foreach (var (kind, text, fields) in cf.blocks)
        {
            if (!RenderContactFormBlock(cf, kind, text, fields)) break;
        }

        cf.page.AddContentStream(Encoding.ASCII.GetBytes(cf.sb.ToString()));
        PruneUnusedFonts(cf.doc);
        return cf.doc;
    }

    private static List<CfField> ParseContactFields(string rowHtml,
        Dictionary<string, (double W, double H)> clsW)
    {
        var fields = new List<CfField>();
        var openRx = new Regex(@"<div\b[^>]*class=""(?<cls>[^""]*\bcontact-form-field[\w-]*[^""]*)""[^>]*>", RegexOptions.IgnoreCase);
        var divRx = new Regex(@"<(?<c>/?)div\b[^>]*>", RegexOptions.IgnoreCase);
        var pos = 0;
        while (pos < rowHtml.Length)
        {
            var m = openRx.Match(rowHtml, pos);
            if (!m.Success) break;
            var depth = 1;
            var end = -1;
            for (var s = divRx.Match(rowHtml, m.Index + m.Length); s.Success;
                 s = divRx.Match(rowHtml, s.Index + s.Length))
            {
                depth += s.Groups["c"].Length > 0 ? -1 : 1;
                if (depth == 0) { end = s.Index; break; }
            }
            if (end < 0) break;
            var inner = rowHtml[(m.Index + m.Length)..end];
            var f = new CfField();
            foreach (var cls in m.Groups["cls"].Value.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                if (clsW.TryGetValue(cls, out var wh)) { f.WidthEm = wh.W; f.HeightEm = wh.H; }

            f.Title = FirstClassText(inner, "form-input-title");

            // The control: a text input's value attribute, a select's selected
            // option, a textarea's body, or a radio group.
            var radios = Regex.Matches(inner, @"<input\b[^>]*type=""radio""[^>]*>", RegexOptions.IgnoreCase);
            if (radios.Count > 0)
            {
                f.IsRadioGroup = true;
                foreach (Match rm in radios)
                {
                    var idm = Regex.Match(rm.Value, @"\bid=""(?<v>[^""]*)""");
                    var lab = idm.Success
                        ? Regex.Match(inner,
                            @"<label\b[^>]*for=""" + Regex.Escape(idm.Groups["v"].Value) + @"""[^>]*>(?<b>[\s\S]*?)</label\s*>",
                            RegexOptions.IgnoreCase) is { Success: true } lm
                            ? Regex.Replace(DecodeEntities(Regex.Replace(lm.Groups["b"].Value, "<[^>]+>", "")), @"\s+", " ").Trim()
                            : ""
                        : "";
                    f.Radios.Add((lab, rm.Value.Contains("checked", System.StringComparison.OrdinalIgnoreCase)));
                }
            }
            else if (Regex.Match(inner, @"<select\b[\s\S]*?</select\s*>", RegexOptions.IgnoreCase) is { Success: true } sel)
            {
                var opt = Regex.Match(sel.Value, @"<option\b[^>]*selected[^>]*>(?<b>[\s\S]*?)</option\s*>", RegexOptions.IgnoreCase);
                if (!opt.Success) opt = Regex.Match(sel.Value, @"<option\b[^>]*>(?<b>[\s\S]*?)</option\s*>", RegexOptions.IgnoreCase);
                if (opt.Success) f.Value = DecodeEntities(opt.Groups["b"].Value).Trim();
            }
            else if (Regex.Match(inner, @"<textarea\b[^>]*>(?<b>[\s\S]*?)</textarea\s*>", RegexOptions.IgnoreCase) is { Success: true } ta)
            {
                f.Value = DecodeEntities(Regex.Replace(ta.Groups["b"].Value, "<[^>]+>", "")).Trim();
            }
            else if (Regex.Match(inner, @"<input\b[^>]*>", RegexOptions.IgnoreCase) is { Success: true } inp)
            {
                var vm = Regex.Match(inp.Value, @"\bvalue=""(?<v>[^""]*)""");
                if (vm.Success) f.Value = DecodeEntities(vm.Groups["v"].Value).Trim();
            }
            else
            {
                // No control at all — an attachment list or a plain text field.
                f.HasControl = false;
                var text = Regex.Replace(inner, @"<h3\b[\s\S]*?</h3\s*>", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, "<[^>]+>", " ");
                f.Value = Regex.Replace(DecodeEntities(text), @"\s+", " ").Trim();
            }

            fields.Add(f);
            pos = end;
        }
        if (fields.Count == 0)
        {
            // The question row wraps each option group in its own <fieldset> (inside
            // one class-less div), so the fieldsets — not the divs — are the fields.
            foreach (Match fs in Regex.Matches(rowHtml, @"<fieldset\b[^>]*>(?<b>[\s\S]*?)</fieldset\s*>",
                         RegexOptions.IgnoreCase))
            {
                var bin = fs.Groups["b"].Value;
                var bf = new CfField { Title = FirstClassText(bin, "form-input-title") };
                foreach (Match rm in Regex.Matches(bin, @"<input\b[^>]*type=""radio""[^>]*>", RegexOptions.IgnoreCase))
                {
                    bf.IsRadioGroup = true;
                    var after = bin[(rm.Index + rm.Length)..];
                    var lab = Regex.Match(after, @"^[^<]{0,24}");
                    bf.Radios.Add((
                        Regex.Replace(DecodeEntities(lab.Value), @"\s+", " ").Trim(),
                        rm.Value.Contains("checked", System.StringComparison.OrdinalIgnoreCase)));
                }
                if (bf.IsRadioGroup) fields.Add(bf);
            }
        }
        return fields;
    }
}
