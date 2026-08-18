using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The till-slip invoice: a body that declares itself a percent-wide table box
// and holds nothing but full-width tables, a floated QR code and a closing
// rule. Every table fills that box, and its columns take their max-content
// share of it — the rule the probe derived for auto columns
// (see the html-table-columns spec), with a spanning cell pushing the columns
// it covers up to its own width first.
internal static partial class HtmlToPdfConverter
{
    private const double SlipSpacingPt = 1.5;    // the 2px default border-spacing
    private const double SlipPadPt = 0.75;       // the 1px default cellpadding
    private const double SlipTableGapPt = 1.5;   // the gap a following table opens
    private const double SlipBorderPt = 0.5;     // the dashed row rules
    // The closing <hr> sits this far under the last row's bottom (measured:
    // the last row closes at 296.64 and the rule draws at 323.61).
    private const double SlipHrGapPt = 26.97;

    /// <summary>A declared CSS length on an element's inline style, in points.</summary>
    private static double DeclaredLen(string attrs, string prop, double fallback)
    {
        var m = Regex.Match(attrs, prop + @"\s*:\s*([\d.]+\s*\w+)", RegexOptions.IgnoreCase);
        return m.Success && TryParseLength(m.Groups[1].Value.Replace(" ", ""), out var v) && v > 0
            ? v : fallback;
    }

    /// <summary>Render the percent-width till-slip invoice, or null.</summary>
    private static Document? TryRenderSlipInvoice(string html, HtmlLoadOptions? options,
        IReadOnlyDictionary<string, Dictionary<string, string>> css,
        double pageWidth, double pageHeight)
    {
        // the body declares a PERCENT width and lays out as a table box
        if (!css.TryGetValue("body", out var bodyRule)
            || !bodyRule.TryGetValue("display", out var disp)
            || !disp.Contains("table", StringComparison.OrdinalIgnoreCase)
            || !bodyRule.TryGetValue("width", out var bw) || !bw.Trim().EndsWith('%'))
            return null;
        if (!double.TryParse(bw.Trim().TrimEnd('%'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var bodyPct)
            || bodyPct is <= 0 or > 100) return null;
        // …in a face the sheet names and a size it pins
        var face = "Calibri";
        if (bodyRule.TryGetValue("font-family", out var famv))
            foreach (var cand in famv.Split(','))
            {
                var nm = cand.Trim().Trim('"', '\'');
                if (nm.Length > 0 && !nm.StartsWith('-') && WinMetricsFor(nm) is not null)
                { face = nm; break; }
            }
        if (WinMetricsFor(face) is not { } fm) return null;
        var fs = 9.0;
        if (bodyRule.TryGetValue("font-size", out var fsv)
            && TryParseLength(fsv.Trim(), out var fsPt) && fsPt > 0) fs = fsPt;

        var bodyM = Regex.Match(html, @"<body\b[^>]*>([\s\S]*)</body\s*>", RegexOptions.IgnoreCase);
        var body = bodyM.Success ? bodyM.Groups[1].Value : html;
        if (!Regex.IsMatch(body, @"<table\b", RegexOptions.IgnoreCase)) return null;

        var lineH = MetricLineHeight(fs, HheaLineSumFor(face) ?? fm.sum);
        var drop = MetricBaselineDrop(fs, lineH, fm);
        var boldFace = face + "-Bold";
        var italFace = face + "-Italic";

        // the body box: its percent of the sheet, opened at the UA body margin
        var boxLeft = UaBodyMarginPt;
        var boxW = pageWidth * bodyPct / 100.0;
        var padTop = 0.0;
        if (bodyRule.TryGetValue("padding-top", out var ptv) && TryParseLength(ptv.Trim(), out var ptPt))
            padTop = ptPt;

        var doc = new Document();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);
        const string res = "F8", resB = "F9", resI = "F10";
        EnsureFont(page, face, res);
        EnsureFont(page, face + "-Bold", resB);
        EnsureFont(page, face + "-Italic", resI);
        var invc = System.Globalization.CultureInfo.InvariantCulture;

        // The first baseline sits under the body's padding AND its UA margin,
        // then the table's own spacing and the cell's padding (measured:
        // 5.669 + 6 + 1.5 + 0.75 + the 8.7 drop = 22.62 exactly).
        var rowAdvance = lineH + 2 * SlipPadPt + SlipSpacingPt;
        var y = padTop + UaBodyMarginPt + SlipSpacingPt + SlipPadPt;
        var drewAny = false;
        foreach (Match tm in Regex.Matches(body, @"<table\b[^>]*>([\s\S]*?)</table\s*>",
                     RegexOptions.IgnoreCase))
        {
            // ── the rows ──────────────────────────────────────────────────
            var rows = new List<List<(string Text, int Span, bool Head, bool Ital, bool Right, bool Rule)>>();
            var rowImgs = new List<string?>();
            var rowDivs = new List<string?>();
            foreach (Match rm in Regex.Matches(tm.Groups[1].Value,
                         @"<tr\b([^>]*)>([\s\S]*?)</tr\s*>", RegexOptions.IgnoreCase))
            {
                var ruleRow = Regex.IsMatch(rm.Groups[1].Value, @"border-bottom", RegexOptions.IgnoreCase);
                var cells = new List<(string, int, bool, bool, bool, bool)>();
                string? rowImg = null, rowDiv = null;
                foreach (Match cm in Regex.Matches(rm.Groups[2].Value,
                             @"<(t[dh])\b([^>]*)>([\s\S]*?)</t[dh]\s*>", RegexOptions.IgnoreCase))
                {
                    var raw = cm.Groups[3].Value;
                    var txt = CollapseWs(DecodeEntities(Regex.Replace(raw, @"<[^>]+>", " "))).Trim();
                    var span = 1;
                    var sm = Regex.Match(cm.Groups[2].Value, @"colspan\s*=\s*[""']?(\d+)",
                        RegexOptions.IgnoreCase);
                    if (sm.Success && int.TryParse(sm.Groups[1].Value, out var sv) && sv > 1) span = sv;
                    // a float-right div's declared-size image rides this cell
                    var imgM = Regex.Match(raw,
                        @"<div\b([^>]*float\s*:\s*right[^>]*)>[\s\S]*?<img\b([^>]*)>",
                        RegexOptions.IgnoreCase);
                    if (imgM.Success)
                    { rowImg = imgM.Groups[2].Value; rowDiv = imgM.Groups[1].Value; }
                    var isTh = cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                    var cls = cm.Groups[2].Value;
                    cells.Add((txt, span, isTh,
                        Regex.IsMatch(cls, @"\bitalic\b", RegexOptions.IgnoreCase),
                        // a th is right-aligned unless its class says otherwise
                        Regex.IsMatch(cls, @"align-right", RegexOptions.IgnoreCase)
                            || (isTh && !Regex.IsMatch(cls, @"align-left", RegexOptions.IgnoreCase)),
                        ruleRow));
                }
                if (cells.Count > 0) { rows.Add(cells); rowImgs.Add(rowImg); rowDivs.Add(rowDiv); }
            }
            if (rows.Count == 0) continue;

            var nCols = 0;
            foreach (var r in rows)
            {
                var t = 0;
                foreach (var c in r) t += c.Span;
                nCols = Math.Max(nCols, t);
            }
            if (nCols == 0) continue;

            // ── the columns: max-content boxes, spanning cells pushing the
            // columns they cover, then a proportional share of the box ──────
            var inset = 2 * SlipPadPt;
            var box = new double[nCols];
            foreach (var r in rows)
            {
                var ci = 0;
                foreach (var (txt, span, head, ital, _, _) in r)
                {
                    if (span == 1 && ci < nCols)
                        box[ci] = Math.Max(box[ci],
                            MeasureFaceText(head ? boldFace : ital ? italFace : face, txt, fs) + inset);
                    ci += span;
                }
            }
            for (var i = 0; i < nCols; i++) if (box[i] <= 0) box[i] = inset;
            foreach (var r in rows)
            {
                var ci = 0;
                foreach (var (txt, span, head, ital, _, _) in r)
                {
                    if (span > 1 && ci + span <= nCols)
                    {
                        var need = MeasureFaceText(head ? boldFace : ital ? italFace : face, txt, fs) + inset;
                        double have = (span - 1) * SlipSpacingPt;
                        for (var k = 0; k < span; k++) have += box[ci + k];
                        if (need > have)
                        {
                            double baseSum = 0;
                            for (var k = 0; k < span; k++) baseSum += box[ci + k];
                            if (baseSum > 0)
                                for (var k = 0; k < span; k++)
                                    box[ci + k] += (need - have) * box[ci + k] / baseSum;
                        }
                    }
                    ci += span;
                }
            }
            double sum = 0;
            foreach (var w in box) sum += w;
            var avail = boxW - (nCols + 1) * SlipSpacingPt;
            if (sum > 0 && avail > 0)
                for (var i = 0; i < nCols; i++) box[i] *= avail / sum;

            // ── draw ──────────────────────────────────────────────────────
            for (var ri = 0; ri < rows.Count; ri++)
            {
                var r = rows[ri];
                var ci = 0;
                var cx = boxLeft + SlipSpacingPt;
                var any = false;
                foreach (var (txt, span, head, ital, right, ruleRow) in r)
                {
                    double w = (span - 1) * SlipSpacingPt;
                    for (var k = 0; k < span && ci + k < nCols; k++) w += box[ci + k];
                    if (txt.Length > 0)
                    {
                        var mFace = head ? boldFace : ital ? italFace : face;
                        var tw = MeasureFaceText(mFace, txt, fs);
                        var tx = right ? cx + w - SlipPadPt - tw : cx + SlipPadPt;
                        EmitPositionedRun(page, head ? resB : ital ? resI : res, fs,
                            tx, pageHeight - (y + drop), txt);
                        any = true;
                        drewAny = true;
                    }
                    if (ruleRow)
                        page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                            $"q 0 0 0 RG {SlipBorderPt:0.##} w [1 1] 0 d " +
                            $"{cx:F2} {pageHeight - (y + rowAdvance - SlipSpacingPt):F2} m {cx + w:F2} {pageHeight - (y + rowAdvance - SlipSpacingPt):F2} l S Q\n")));
                    cx += w + SlipSpacingPt;
                    ci += span;
                }
                // A float-right div hangs its image from the row's TOP with
                // the div's own right edge inside the box's spacing and
                // padding, and the image at the div's LEFT (measured: a 2cm
                // div closing at 803.65 puts its 1.7cm image at 746.96).
                if (ri < rowImgs.Count && rowImgs[ri] is { } imgAttrs
                    && Regex.Match(imgAttrs, @"src\s*=\s*[""'](data:image/[a-z]+;base64,[^""']+)[""']",
                        RegexOptions.IgnoreCase) is { Success: true } srcM)
                {
                    var divW = ri < rowDivs.Count && rowDivs[ri] is { } dv
                        ? DeclaredLen(dv, "width", 0) : 0;
                    var wM = Regex.Match(imgAttrs, @"width\s*:\s*([\d.]+\s*\w+)", RegexOptions.IgnoreCase);
                    var hM = Regex.Match(imgAttrs, @"height\s*:\s*([\d.]+\s*\w+)", RegexOptions.IgnoreCase);
                    if (wM.Success && hM.Success
                        && TryParseLength(wM.Groups[1].Value.Replace(" ", ""), out var iw)
                        && TryParseLength(hM.Groups[1].Value.Replace(" ", ""), out var ih)
                        && iw > 0 && ih > 0)
                    {
                        byte[]? bytes = null;
                        var b64 = srcM.Groups[1].Value;
                        var comma = b64.IndexOf(',');
                        try { bytes = System.Convert.FromBase64String(b64[(comma + 1)..]); }
                        catch { }
                        if (bytes is not null)
                        {
                            var divRight = boxLeft + boxW - SlipSpacingPt - SlipPadPt;
                            var ix = divRight - (divW > 0 ? divW : iw);
                            try
                            {
                                page.AddImage(bytes, new Rectangle(
                                    ix, pageHeight - y - ih, ix + iw, pageHeight - y));
                            }
                            catch { }
                        }
                    }
                }
                y += rowAdvance + (r[0].Rule ? SlipBorderPt : 0);
                _ = any;
            }
            y += SlipTableGapPt;   // the spacing a following table opens with
        }
        // the sheet's closing <hr>, drawn across the body box
        if (drewAny && Regex.IsMatch(body, @"<hr\b", RegexOptions.IgnoreCase))
        {
            var hy = y - SlipTableGapPt + SlipHrGapPt;
            page.AddContentStream(Encoding.ASCII.GetBytes(string.Create(invc,
                $"q 0 0 0 RG {SlipBorderPt:0.##} w [1 1] 0 d " +
                $"{boxLeft:F2} {pageHeight - hy:F2} m {boxLeft + boxW:F2} {pageHeight - hy:F2} l S Q\n")));
        }
        return drewAny ? doc : null;
    }
}
