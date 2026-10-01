using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>UA serif table cells: one cell parsed, measured and drawn.</summary>
    private void DrawUaCell(UaSerifTableState ua, UaRowState ur, int ci)
    {
        var cell = ur.cells[ci];
        var colW = ua.colWs[Math.Min(ci, ua.colWs.Count - 1)];
        var cellR = ur.cellL + colW;
        if (cell.Bg is { } bgc)
            ua.tb.SetFillColor(bgc.R / 255.0, bgc.G / 255.0, bgc.B / 255.0)
              .Rectangle(ur.cellL, ua.page.Height - (ua.topD + ur.rowH), colW, ur.rowH)
              .Fill();
        if (cell.Solid[0])
        {
            var ec = cell.EdgeColor ?? Color.Black;
            ua.tb.SetStrokeColor(ec.R / 255.0, ec.G / 255.0, ec.B / 255.0)
              .SetLineWidth(UaTableEdgePt)
              .MoveTo(ur.cellL, ua.page.Height - (ua.topD + UaTableEdgePt / 2))
              .LineTo(cellR, ua.page.Height - (ua.topD + UaTableEdgePt / 2))
              .Stroke();
        }
        if (cell.Solid[2])
            ua.tb.SetStrokeColor(0, 0, 0).SetLineWidth(UaTableEdgePt)
              .MoveTo(ur.cellL, ua.page.Height - (ua.topD + ur.rowH - UaTableEdgePt / 2))
              .LineTo(cellR, ua.page.Height - (ua.topD + ur.rowH - UaTableEdgePt / 2))
              .Stroke();
        if (cell.Solid[3])
            ua.tb.SetStrokeColor(0, 0, 0).SetLineWidth(UaTableEdgePt)
              .MoveTo(ur.cellL + UaTableEdgePt / 2, ua.page.Height - ua.topD)
              .LineTo(ur.cellL + UaTableEdgePt / 2, ua.page.Height - (ua.topD + ur.rowH))
              .Stroke();

        DrawUaCellLines(ua, ur, ci, colW, cellR);
        ur.cellL = cellR;
    }

    /// <summary></summary>
    private void MeasureUaCell(UaSerifTableState ua, UaRowState ur, int ci)
    {
        var cell = ur.cells[ci];
        var colW = ua.colWs[Math.Min(ci, ua.colWs.Count - 1)];
        var leftInset = cell.Solid[3] ? UaTableEdgePt : 0.0;
        var availW = colW - leftInset - 2 * ua.pad;
        var styled = cell.Family is not null && Face(ua, cell.Family) is not null;
        var fb = Face(ua, UaFallbackFamily);
        // lines break by the real face's
        // metrics even where we draw the base-14 serif —
        // measure with the system TTF when it resolves
        var measureFace = styled
            ? Face(ua, cell.Family!)
            : Face(ua, "Times New Roman");
        double WordW(string w)
        {
            if (measureFace is { } mf)
                return StyledWidth(ua, w, mf.Item3, fb?.Item3, cell.Fs);
            double mw = 0;
            foreach (System.Text.RegularExpressions.Match sm in
                System.Text.RegularExpressions.Regex.Matches(w,
                    $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                mw += sm.Value[0] == UaFallbackChar && fb is not null
                    ? StyledWidth(ua, sm.Value, fb.Value.Item3, null, cell.Fs)
                    : TimesWidth(ua, sm.Value);
            return mw;
        }
        var cur = "";
        foreach (var w in cell.Text.Split(' ',
            StringSplitOptions.RemoveEmptyEntries))
        {
            var probe = cur.Length == 0 ? w : cur + " " + w;
            if (cur.Length > 0 && WordW(probe) > availW)
            { cell.Lines.Add(cur); cur = w; }
            else cur = probe;
        }
        if (cur.Length > 0) cell.Lines.Add(cur);
        foreach (var ln in cell.Lines)
        {
            double box;
            if (styled)
            {
                var f = Face(ua, cell.Family!)!.Value;
                box = CssBox(ua, f.Item4, cell.Fs);
                if (ln.IndexOf(UaFallbackChar) >= 0 && fb is not null)
                    box = Math.Max(box, CssBox(ua, fb.Value.Item4, cell.Fs));
            }
            else box = UaSerifPitchPt;
            cell.Boxes.Add(box);
        }
        var sum = 0.0;
        foreach (var b in cell.Boxes) sum += b;
        ur.rowContentH = Math.Max(ur.rowContentH, sum);
        ur.cells[ci] = cell;
    }

    /// <summary></summary>
    private void ParseUaCell(UaSerifTableState ua, UaRowState ur, System.Text.RegularExpressions.Match dm)
    {
        var attrs = dm.Groups["a"].Value;
        var text = System.Text.RegularExpressions.Regex.Replace(
            HtmlFragment.StripHtmlTags(dm.Groups["in"].Value),
            @"\s+", " ").Trim();
        var fs = UaSerifPt;
        var fsm = System.Text.RegularExpressions.Regex.Match(attrs,
            @"font-size\s*:\s*([\d.]+)pt", UaRx);
        if (fsm.Success)
            fs = double.Parse(fsm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
        string? family = null;
        var ffm = System.Text.RegularExpressions.Regex.Match(attrs,
            @"font-family\s*:\s*([^;""']+)", UaRx);
        if (ffm.Success) family = ffm.Groups[1].Value.Trim();
        Color? bg = null;
        var bgm = System.Text.RegularExpressions.Regex.Match(attrs,
            @"background[^;]*?(#[0-9a-f]{6})", UaRx);
        if (bgm.Success)
            bg = Converters.HtmlToPdfConverter.ParseCssColor(bgm.Groups[1].Value);
        // border-style lists top right bottom left
        var solid = new bool[4];
        var bsm = System.Text.RegularExpressions.Regex.Match(attrs,
            @"border-style\s*:\s*([^;]+)", UaRx);
        if (bsm.Success)
        {
            var toks = bsm.Groups[1].Value.Trim().Split(' ',
                StringSplitOptions.RemoveEmptyEntries);
            for (var e = 0; e < 4; e++)
                solid[e] = string.Equals(
                    toks[Math.Min(e, toks.Length - 1)], "solid",
                    StringComparison.OrdinalIgnoreCase);
            if (toks.Length == 2) { solid[2] = solid[0]; solid[3] = solid[1]; }
        }
        Color? edgeColor = null;
        var bcm = System.Text.RegularExpressions.Regex.Match(attrs,
            @"border-color\s*:\s*(#[0-9a-f]{6})", UaRx);
        if (bcm.Success)
            edgeColor = Converters.HtmlToPdfConverter.ParseCssColor(bcm.Groups[1].Value);
        ur.cells.Add((text, fs, family, bg, edgeColor, solid,
            new List<string>(), new List<double>()));
    }

    /// <summary></summary>
    private void DrawUaCellLines(UaSerifTableState ua, UaRowState ur, int ci, double colW, double cellR)
    {
        var cell = ur.cells[ci];
        if (cell.Lines.Count > 0)
        {
            var styled = cell.Family is not null && Face(ua, cell.Family) is not null;
            var fb = Face(ua, UaFallbackFamily);
            // the fill colour above still governs — text is black
            ua.tb.SetFillColor(0, 0, 0);
            var sum = 0.0;
            foreach (var b in cell.Boxes) sum += b;
            var contentTop = ua.topD + (ur.rowH - sum) / 2;
            double pitch, seat1;
            if (styled)
            {
                var f = Face(ua, cell.Family!)!.Value;
                pitch = CssBox(ua, f.Item4, cell.Fs);
                seat1 = SeatIn(ua, f.Item4, cell.Fs, cell.Boxes[0]);
            }
            else
            {
                pitch = UaSerifPitchPt;
                seat1 = UaSerifSeatPt + (cell.Boxes[0] - UaSerifPitchPt) / 2;
            }
            var textX = ur.cellL + (cell.Solid[3] ? UaTableEdgePt : 0) + ua.pad;
            for (var li = 0; li < cell.Lines.Count; li++)
            {
                var baseD = contentTop + seat1 + li * pitch;
                var py = ua.page.Height - baseD;
                var ln = cell.Lines[li];
                if (styled)
                {
                    var f = Face(ua, cell.Family!)!.Value;
                    var x = textX;
                    // split at fallback-glyph boundaries
                    foreach (System.Text.RegularExpressions.Match sm in
                        System.Text.RegularExpressions.Regex.Matches(ln,
                            $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                    {
                        var seg = sm.Value;
                        var isFb = seg[0] == UaFallbackChar && fb is not null;
                        var (ttf, name, gp, _) = isFb ? fb!.Value : f;
                        var (res, hex) = Text.Type0FontEmbedder.Embed(
                            ua.fontDict, ttf, name, seg,
                            stripSpacesInBaseFont: true);
                        ua.tb.BeginText().SetFont(res, cell.Fs)
                          .MoveTextPosition(x, py);
                        if (StepKernAdjustments(seg, gp) is { } adj)
                            ua.tb.ShowTextHexKerned(hex, adj);
                        else ua.tb.ShowTextHex(hex);
                        ua.tb.EndText();
                        x += StyledWidth(ua, seg, gp,
                            isFb ? null : fb?.Item3, cell.Fs);
                    }
                }
                else
                {
                    // base-14 Times for the serif default; the
                    // fallback glyph alone goes through its
                    // embedded face
                    var x = textX;
                    foreach (System.Text.RegularExpressions.Match sm in
                        System.Text.RegularExpressions.Regex.Matches(ln,
                            $@"[^{UaFallbackChar}]+|{UaFallbackChar}+"))
                    {
                        var seg = sm.Value;
                        if (seg[0] == UaFallbackChar && fb is not null)
                        {
                            var (ttf, name, gp, _) = fb.Value;
                            var (res, hex) = Text.Type0FontEmbedder.Embed(
                                ua.fontDict, ttf, name, seg,
                                stripSpacesInBaseFont: true);
                            ua.tb.BeginText().SetFont(res, cell.Fs)
                              .MoveTextPosition(x, py)
                              .ShowTextHex(hex).EndText();
                            x += StyledWidth(ua, seg, gp, null, cell.Fs);
                        }
                        else
                        {
                            ua.tb.BeginText().SetFont(ua.uaTimes2, cell.Fs)
                              .MoveTextPosition(x, py)
                              .ShowText(seg).EndText();
                            x += TimesWidth(ua, seg);
                        }
                    }
                }
            }
        }
    }
}
