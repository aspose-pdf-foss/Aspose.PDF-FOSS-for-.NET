using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Slip invoice: one row of a slip table drawn.</summary>
    private static void DrawSlipRow(SlipInvoiceState si, int ri)
    {
        var r = si.rows[ri];
        var ci = 0;
        var cx = si.boxLeft + SlipSpacingPt;
        var any = false;
        foreach (var (txt, span, head, ital, right, ruleRow) in r)
        {
            double w = (span - 1) * SlipSpacingPt;
            for (var k = 0; k < span && ci + k < si.nCols; k++) w += si.box[ci + k];
            if (txt.Length > 0)
            {
                var mFace = head ? si.boldFace : ital ? si.italFace : si.face;
                var tw = MeasureFaceText(mFace, txt, si.fs);
                var tx = right ? cx + w - SlipPadPt - tw : cx + SlipPadPt;
                EmitPositionedRun(si.page, head ? si.resB : ital ? si.resI : si.res, si.fs,
                    tx, si.pageHeight - (si.y + si.drop), txt);
                any = true;
                si.drewAny = true;
            }
            if (ruleRow)
                si.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(si.invc,
                    $"q 0 0 0 RG {SlipBorderPt:0.##} w [1 1] 0 d " +
                    $"{cx:F2} {si.pageHeight - (si.y + si.rowAdvance - SlipSpacingPt):F2} m {cx + w:F2} {si.pageHeight - (si.y + si.rowAdvance - SlipSpacingPt):F2} l S Q\n")));
            cx += w + SlipSpacingPt;
            ci += span;
        }
        // A float-right div hangs its image from the row's TOP with
        // the div's own right edge inside the box's spacing and
        // padding, and the image at the div's LEFT (measured: a 2cm
        // div closing at 803.65 puts its 1.7cm image at 746.96).
        if (ri < si.rowImgs.Count && si.rowImgs[ri] is { } imgAttrs
            && Regex.Match(imgAttrs, @"src\s*=\s*[""'](data:image/[a-z]+;base64,[^""']+)[""']",
                RegexOptions.IgnoreCase) is { Success: true } srcM)
        {
            var divW = ri < si.rowDivs.Count && si.rowDivs[ri] is { } dv
                ? DeclaredLen(dv, "width", 0) : 0;
            var wM = Regex.Match(imgAttrs, @"width\s*:\s*([\d.]+\s*\w+)", RegexOptions.IgnoreCase);
            var hM = Regex.Match(imgAttrs, @"height\s*:\s*([\d.]+\s*\w+)", RegexOptions.IgnoreCase);
            if (wM.Success && hM.Success
                && TryParseLength(wM.Groups[1].Value.Replace(" ", "")) is { } iw
                && TryParseLength(hM.Groups[1].Value.Replace(" ", "")) is { } ih
                && iw > 0 && ih > 0)
            {
                byte[]? bytes = null;
                var b64 = srcM.Groups[1].Value;
                var comma = b64.IndexOf(',');
                try { bytes = System.Convert.FromBase64String(b64[(comma + 1)..]); }
                catch { }
                if (bytes is not null)
                {
                    var divRight = si.boxLeft + si.boxW - SlipSpacingPt - SlipPadPt;
                    var ix = divRight - (divW > 0 ? divW : iw);
                    try
                    {
                        si.page.AddImage(bytes, new Rectangle(
                            ix, si.pageHeight - si.y - ih, ix + iw, si.pageHeight - si.y));
                    }
                    catch { }
                }
            }
        }
        si.y += si.rowAdvance + (r[0].Rule ? SlipBorderPt : 0);
        _ = any;
    }

    /// <summary>Slip invoice: the table's column widths measured and scaled to the body box.</summary>
    private static void SolveSlipColumns(SlipInvoiceState si)
    {
        si.inset = 2 * SlipPadPt;
        si.box = new double[si.nCols];
        foreach (var r in si.rows)
        {
            var ci = 0;
            foreach (var (txt, span, head, ital, _, _) in r)
            {
                if (span == 1 && ci < si.nCols)
                    si.box[ci] = Math.Max(si.box[ci],
                        MeasureFaceText(head ? si.boldFace : ital ? si.italFace : si.face, txt, si.fs) + si.inset);
                ci += span;
            }
        }
        for (var i = 0; i < si.nCols; i++) if (si.box[i] <= 0) si.box[i] = si.inset;
        foreach (var r in si.rows)
        {
            var ci = 0;
            foreach (var (txt, span, head, ital, _, _) in r)
            {
                if (span > 1 && ci + span <= si.nCols)
                {
                    var need = MeasureFaceText(head ? si.boldFace : ital ? si.italFace : si.face, txt, si.fs) + si.inset;
                    double have = (span - 1) * SlipSpacingPt;
                    for (var k = 0; k < span; k++) have += si.box[ci + k];
                    if (need > have)
                    {
                        double baseSum = 0;
                        for (var k = 0; k < span; k++) baseSum += si.box[ci + k];
                        if (baseSum > 0)
                            for (var k = 0; k < span; k++)
                                si.box[ci + k] += (need - have) * si.box[ci + k] / baseSum;
                    }
                }
                ci += span;
            }
        }
        si.sum = 0;
        foreach (var w in si.box) si.sum += w;
        si.avail = si.boxW - (si.nCols + 1) * SlipSpacingPt;
        if (si.sum > 0 && si.avail > 0)
            for (var i = 0; i < si.nCols; i++) si.box[i] *= si.avail / si.sum;
    }

    /// <summary>Slip invoice: the table's rows and cells read off the markup.</summary>
    private static void ParseSlipRows(SlipInvoiceState si, Match tm)
    {
        si.rowDivs = new List<string?>();
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
            if (cells.Count > 0) { si.rows.Add(cells); si.rowImgs.Add(rowImg); si.rowDivs.Add(rowDiv); }
        }
    }
}
