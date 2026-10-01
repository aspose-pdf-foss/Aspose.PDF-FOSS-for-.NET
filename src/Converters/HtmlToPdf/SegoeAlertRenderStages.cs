using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The stages of the Segoe alert render: the measure, run, line, fill and flat helpers, one panel, and the grid body.
    private static double SegoeMeasure(SegoeAlertRenderState sg, byte[] ttf, string face, string s, double fs)
    {
        if (sg.page.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return s.Length * fs * 0.5;
        return Text.Type0FontEmbedder.MeasureText(fd, ttf, face, s, fs,
            stripSpacesInBaseFont: true);
    }

    private static void SegoeRun(SegoeAlertRenderState sg, bool bold, double fs, double x, double baselineTd, string text, Color col)
    {
        if (text.Length == 0) return;
        if (sg.page.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fd) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(fd,
            bold ? sg.segoeBold! : sg.segoe!, bold ? "SegoeUIBold" : "SegoeUI", text,
            stripSpacesInBaseFont: true);
        sg.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(sg.invc,
            $"BT {col.R / 255.0:0.###} {col.G / 255.0:0.###} {col.B / 255.0:0.###} rg " +
            $"/{rn} {fs:0.##} Tf 1 0 0 1 {x:0.##} {sg.pageHeight - baselineTd:0.##} Tm ")
            + "<" + Compat.ToHexString(hex) + "> Tj ET\n"));
    }

    private static void SegoeLine(SegoeAlertRenderState sg, double x0, double x1, double yTd, double w, Color c)
        => sg.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(sg.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} RG {w:0.##} w " +
            $"{x0:0.##} {sg.pageHeight - yTd:0.##} m {x1:0.##} {sg.pageHeight - yTd:0.##} l S Q\n")));

    private static void SegoeFill(SegoeAlertRenderState sg, Color c, double x, double yTd, double w, double h)
        => sg.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(sg.invc,
            $"q {c.R / 255.0:0.###} {c.G / 255.0:0.###} {c.B / 255.0:0.###} rg " +
            $"{x:0.##} {sg.pageHeight - yTd - h:0.##} {w:0.##} {h:0.##} re f Q\n")));

    private static string SegoeFlat(SegoeAlertRenderState sg, string frag)
        => Regex.Replace(DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    // ── the two label/value panels ──
    private static void SegoePanel(SegoeAlertRenderState sg, string cls, double labelRight, double valueX)
    {
        var pm = Regex.Match(sg.html,
            @"<table\b[^>]*class\s*=\s*[""']" + cls + @"[""'][^>]*>(?<b>[\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (!pm.Success) return;
        var baseline = SaPanelBase0;
        foreach (Match trM in Regex.Matches(pm.Groups["b"].Value,
            @"<tr\b[^>]*>(?<r>[\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var lblM = Regex.Match(trM.Groups["r"].Value,
                @"class\s*=\s*[""']top_label[""'][^>]*>(?<t>[\s\S]*?)</span>", RegexOptions.IgnoreCase);
            var valM = Regex.Match(trM.Groups["r"].Value,
                @"class\s*=\s*[""']top_value[""'][^>]*>(?<t>[\s\S]*?)</span>", RegexOptions.IgnoreCase);
            var lbl = lblM.Success ? SegoeFlat(sg, lblM.Groups["t"].Value).ToUpperInvariant() : "";
            var val = valM.Success ? SegoeFlat(sg, valM.Groups["t"].Value) : "";
            var valLines = MeasuredWordWrap(val, SaValueWrapW, "Segoe UI", 10.5);
            // the label centres on the value block's baselines
            var lblBase = baseline + (valLines.Length - 1) * SaValuePitch / 2;
            if (lbl.Length > 0)
                SegoeRun(sg, false, 9, labelRight - SegoeMeasure(sg, sg.segoe!, "SegoeUI", lbl, 9), lblBase, lbl, sg.grey);
            for (var li = 0; li < valLines.Length; li++)
                SegoeRun(sg, true, 10.5, valueX, baseline + li * SaValuePitch, valLines[li], sg.black);
            baseline += Math.Max(SaPanelPitch,
                (valLines.Length - 1) * SaValuePitch + SaPanelPitch);
        }
    }

    /// <summary></summary>
    private static void RenderSegoeAlertGrid(SegoeAlertRenderState sg)
    {
        var gbody = sg.gm.Groups["b"].Value;
        // headers: centred grey per column over the per-column underline
        var heads = new List<string>();
        foreach (Match thM in Regex.Matches(gbody,
            @"class\s*=\s*[""']grid_header[""'][\s\S]*?<span>(?<t>[^<]*)</span>",
            RegexOptions.IgnoreCase))
            heads.Add(SegoeFlat(sg, thM.Groups["t"].Value));
        for (var ci = 0; ci < heads.Count && ci + 1 < SaColEdges.Length; ci++)
        {
            if (heads[ci].Length == 0) continue;
            var cw = SegoeMeasure(sg, sg.segoe!, "SegoeUI", heads[ci], 9);
            var cx = (SaColEdges[ci] + SaColEdges[ci + 1] - cw) / 2;
            SegoeRun(sg, false, 9, cx, SaGridHeadTop + 9 * SegoeAscEm, heads[ci], sg.grey);
        }
        var ruleInk = Color.FromRgbBytes(0x89, 0x89, 0x89);
        for (var ci = 0; ci + 1 < SaColEdges.Length; ci++)
            SegoeLine(sg, SaColEdges[ci] - (ci > 0 ? 0.7 : 0), SaColEdges[ci + 1] + 1.4,
                SaGridRuleY, 1.5, ruleInk);

        // data rows: [id, computed?, temp?, target?, sensor, days?] — a
        // colspan row carries only the id and its red sensor note
        var rowTop = SaGridRow0Top;
        var redTop = SaRedTop0;
        foreach (Match tbM in Regex.Matches(gbody,
            @"<tbody\b[^>]*>(?<r>[\s\S]*?)</tbody>", RegexOptions.IgnoreCase))
        {
            var r = tbM.Groups["r"].Value;
            if (Regex.IsMatch(r, @"grid_header", RegexOptions.IgnoreCase)) continue;
            var cells = new List<(string text, bool highlight, bool redText)>();
            foreach (Match tdM in Regex.Matches(r,
                @"<td\b(?<a>[^>]*)>(?<c>[\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var c = tdM.Groups["c"].Value;
                var hl = Regex.IsMatch(c, @"grid_highlight_value[^>]*background",
                    RegexOptions.IgnoreCase);
                var rt = Regex.IsMatch(c, @"color:\s*#CF3135", RegexOptions.IgnoreCase);
                var span = Regex.Match(tdM.Groups["a"].Value, @"colspan\s*=\s*[""']?(\d+)");
                cells.Add((SegoeFlat(sg, c), hl, rt));
                if (span.Success)
                    for (var k = 1; k < int.Parse(span.Groups[1].Value); k++)
                        cells.Add(("", false, false));
            }
            if (cells.Count == 0) continue;
            var isFull = cells.Count >= 6 && cells[1].highlight;
            for (var ci = 0; ci < cells.Count && ci + 1 < SaColEdges.Length; ci++)
            {
                var (txt, hl, rt) = cells[ci];
                if (hl)
                    SegoeFill(sg, sg.red, SaColEdges[1] + 0.8, redTop, SaColEdges[2] - SaColEdges[1] - 0.8, SaRedH);
                if (txt.Length == 0) continue;
                var cw = SegoeMeasure(sg, sg.segoeBold!, "SegoeUIBold", txt, 9);
                var cx = (SaColEdges[ci] + SaColEdges[ci + 1] - cw) / 2;
                SegoeRun(sg, true, 9, cx, rowTop + 9 * SegoeAscEm, txt,
                    hl ? sg.white : rt ? sg.red : sg.black);
            }
            var pitch = isFull ? SaGridRowPitch1 : SaGridRowPitch2;
            rowTop += pitch;
            redTop += pitch;
        }
    }
}
