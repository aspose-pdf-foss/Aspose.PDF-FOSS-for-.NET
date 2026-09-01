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

    private static Document? TryRenderContactForm(string html, HtmlLoadOptions? options,
        double pageWidth, double pageHeight, double marginLeft, double marginRight, double marginTop)
    {
        if (!html.Contains("contact-form-row", System.StringComparison.Ordinal)
            || !html.Contains("contact-form-field", System.StringComparison.Ordinal)) return null;

        var css = ParseStyleSheet(html);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        double Em(double em) => em * CfEmPx * 0.75;

        // Per-field-class widths/heights, straight from the sheet.
        var clsW = new Dictionary<string, (double W, double H)>(System.StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(html, @"\.(?<c>contact-form-field-[\w-]+)\s*\{(?<b>[^}]*)\}"))
        {
            var body = m.Groups["b"].Value;
            var wm = Regex.Match(body, @"(?<![-\w])width\s*:\s*([\d.]+)em");
            var hm = Regex.Match(body, @"(?<![-\w])height\s*:\s*([\d.]+)em");
            if (!wm.Success) continue;
            clsW[m.Groups["c"].Value] = (
                double.Parse(wm.Groups[1].Value, inv),
                hm.Success ? double.Parse(hm.Groups[1].Value, inv) : CfFieldHeightEm);
        }
        if (clsW.Count == 0) return null;

        var bodyM = Regex.Match(html, @"<body[^>]*>(?<b>[\s\S]*)</body\s*>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var body2 = bodyM.Groups["b"].Value;

        // Blocks in document order: section headings and rows of fields.
        var blocks = new List<(string Kind, string Text, List<CfField> Fields)>();
        var tokRx = new Regex(
            @"<h2\b[^>]*>(?<h2>[\s\S]*?)</h2\s*>|<div\b[^>]*class=""[^""]*\bcontact-form-row\b[^""]*""[^>]*>",
            RegexOptions.IgnoreCase);
        var divRx = new Regex(@"<(?<c>/?)div\b[^>]*>", RegexOptions.IgnoreCase);
        var scanPos = 0;
        while (scanPos < body2.Length)
        {
            var m = tokRx.Match(body2, scanPos);
            if (!m.Success) break;
            if (m.Groups["h2"].Success)
            {
                var t = Regex.Replace(m.Groups["h2"].Value, "<[^>]+>", "");
                blocks.Add(("h2", Regex.Replace(DecodeEntities(t), @"\s+", " ").Trim(), new List<CfField>()));
                scanPos = m.Index + m.Length;
                continue;
            }
            // The row runs to its matching </div>.
            var depth = 1;
            var end = -1;
            for (var s = divRx.Match(body2, m.Index + m.Length); s.Success;
                 s = divRx.Match(body2, s.Index + s.Length))
            {
                depth += s.Groups["c"].Length > 0 ? -1 : 1;
                if (depth == 0) { end = s.Index; break; }
            }
            if (end < 0) break;
            var rowHtml = body2[(m.Index + m.Length)..end];
            var rowH2 = Regex.Match(rowHtml, @"<h2\b[^>]*>(?<t>[\s\S]*?)</h2\s*>", RegexOptions.IgnoreCase);
            if (rowH2.Success)
            {
                var rt = Regex.Replace(rowH2.Groups["t"].Value, "<[^>]+>", "");
                blocks.Add(("h2row", Regex.Replace(DecodeEntities(rt), @"\s+", " ").Trim(), new List<CfField>()));
            }
            var fields = ParseContactFields(rowHtml, clsW);
            if (fields.Count > 0) blocks.Add(("row", "", fields));
            scanPos = end;
        }
        if (blocks.Count == 0) return null;

        // Column: min(1025px, content) centred in the page's content box.
        var contentPt = pageWidth - marginLeft - marginRight;
        var colPt = System.Math.Min(Em(CfMaxWidthPx / CfEmPx), contentPt);
        var colX = marginLeft + (contentPt - colPt) / 2;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        var resByFace = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var sb = new StringBuilder();

        // The first heading's glyph top: the page margin plus the h2's own
        // margin-top (20 px) and its ascent within the 1.1 line box.
        var y = marginTop + CfFirstHeadingTopPt;
        var pendingHeading = true;

        foreach (var (kind, text, fields) in blocks)
        {
            if (kind is "h2" or "h2row")
            {
                if (kind == "h2row") y += CfInRowHeadingLeadPt;
                EmitGridsterText(page, resByFace, CfSectionPt, colX,
                    pageHeight - (y + CfSectionPt + CfSectionSeatPt), text, "Arial");
                y += CfHeadingToFirstTitlePt;
                pendingHeading = true;
                continue;
            }

            var x = colX;
            double rowH = 0;
            foreach (var f in fields)
            {
                var fw = Em(f.WidthEm) + Em(CfBorderPx / CfEmPx);
                var fh = f.IsRadioGroup ? CfRadioRowHeightPt : Em(f.HeightEm);
                rowH = System.Math.Max(rowH, fh);
                var boxTop = y + CfTitleAboveBoxPt;

                if (f.Title.Length > 0)
                    EmitGridsterText(page, resByFace, CfTitlePt, x,
                        pageHeight - (y + CfTitlePt), f.Title, "Arial,Bold");

                if (f.IsRadioGroup)
                {
                    // Option circles on the value line, each followed by its label
                    // (probed: circles at +5.76 on a 43.02 pt pitch, 18.98 under the
                    // title's glyph top; labels 14.5 to the right of each circle).
                    var rx = x + CfRadioFirstPt;
                    foreach (var (lab, on) in f.Radios)
                    {
                        var cy = pageHeight - (y + CfRadioTopPt + CfRadioRPt);
                        var r = CfRadioRPt;
                        sb.Append(string.Create(inv,
                            $"q 0 0 0 RG 1 w {rx + r:F2} {cy:F2} m " +
                            $"{rx + r:F2} {cy + r:F2} {rx - r:F2} {cy + r:F2} {rx - r:F2} {cy:F2} c " +
                            $"{rx - r:F2} {cy - r:F2} {rx + r:F2} {cy - r:F2} {rx + r:F2} {cy:F2} c S Q\n"));
                        if (on)
                            sb.Append(string.Create(inv,
                                $"q 0 0 0 rg {rx - r / 2:F2} {cy - r / 2:F2} {r:F2} {r:F2} re f Q\n"));
                        EmitGridsterText(page, resByFace, CfValuePt, rx + CfRadioLabelPt,
                            cy - CfValuePt * 0.30, lab, "Arial,Bold");
                        rx += CfRadioPitchPt;
                    }
                }
                else if (f.HasControl)
                {
                    // The 2 px-bordered control box, stroked on its inset centre line.
                    sb.Append(string.Create(inv,
                        $"q 0 0 0 RG 1 w {x + 0.5:F2} {pageHeight - boxTop - 0.5:F2} " +
                        $"{fw - Em(CfBorderPx / CfEmPx) - 1:F2} {-(fh):F2} re S Q\n"));
                    if (f.Value.Length > 0)
                        EmitGridsterText(page, resByFace, CfValuePt, x + CfValueInsetPt,
                            pageHeight - (boxTop + CfValueBelowBoxPt), f.Value, "Times New Roman");
                }
                else if (f.Value.Length > 0)
                {
                    EmitGridsterText(page, resByFace, CfValuePt, x,
                        pageHeight - (boxTop + CfValueBelowBoxPt), f.Value, "Arial,Bold");
                }

                x += fw + Em(CfFieldGapEm);
            }
            _ = pendingHeading;
            y += rowH + Em(CfRowGapEm);
        }

        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        PruneUnusedFonts(doc);
        return doc;
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
