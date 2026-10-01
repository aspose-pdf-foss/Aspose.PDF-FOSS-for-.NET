using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Ishares fact sheet: one side-by-side column drawn.</summary>
    private static void RenderIfsColumn(IsharesFactSheetState ix, int c)
    {
        ix.col = ix.cols[c];
        ix.isAllo2 = ix.col.Subtype.Equals("TSR_Allo2", StringComparison.OrdinalIgnoreCase);
        ix.x0 = IfsColX[c];
        ix.tableW = c == 0 ? IfsColW : IfsCol2W;
        ix.x1 = ix.x0 + ix.tableW;

        MeasureIfsHangWidth(ix);
        ix.valueW = 0;
        foreach (var hl in ix.col.ValueHead)
            ix.valueW = Math.Max(ix.valueW, MW(ix, hl.Pre + hl.Ins, 8, "Arial Italic")
                + (hl.Sup.Length > 0 ? MW(ix, hl.Sup, IfsSupFs, "Arial Italic") : 0));
        if (ix.isAllo2) { ix.labelW = IfsAllo2LabelW; ix.valueW = IfsAllo2ValueW; ix.hangW = ix.tableW - ix.labelW - ix.valueW; }
        else ix.labelW = ix.tableW - ix.valueW - ix.hangW;
        ix.labelRight = ix.x0 + ix.labelW;
        ix.valueRight = ix.labelRight + ix.valueW;

        ix.sb.AppendLine(Compat.Format(ix.inv,
            $"q 0.463 0.737 0.129 rg {ix.x0:F2} {IfsPageH - IfsBandBottomTd:F2} {ix.tableW:F2} {IfsBandBottomTd - IfsBandTopTd:F2} re f Q"));
        Run(ix, "F9", 10, ix.x0, IfsTitleBaseTd, ix.col.Title);
        if (ix.col.TitleSup.Length > 0)
            Run(ix, "F9", 8.33, ix.x0 + MW(ix, ix.col.Title, 10, "Arial Bold"), IfsTitleBaseTd - 4.16, ix.col.TitleSup);
        HRule(ix, ix.x0, ix.x1, IfsBandBottomTd, 0.5);

        ix.headBase = IfsBandBottomTd + (ix.isAllo2 ? IfsAllo2HeadLine1Off : IfsHeadLine1Off);
        ix.headPitch = ix.isAllo2 ? IfsAllo2HeadPitch : IfsHeadPitch;
        ix.lastHeadBase = ix.headBase;
        for (var i = 0; i < ix.col.ValueHead.Count; i++)
        {
            var hl = ix.col.ValueHead[i];
            var ybNat = ix.headBase + i * ix.headPitch;
            ix.lastHeadBase = ybNat;
            var yb = hl.Ins.Length > 0 ? ybNat + IfsInsSeat : ybNat;
            var wPre = MW(ix, hl.Pre, 8, "Arial Italic");
            var wIns = MW(ix, hl.Ins, 8, "Arial Italic");
            var supW = hl.Sup.Length > 0 ? MW(ix, hl.Sup, IfsSupFs, "Arial Italic") : 0;
            var xh = ix.valueRight - wPre - wIns - supW;
            Run(ix, "F10", 8, xh, yb, hl.Pre + hl.Ins);
            if (hl.Delta) Delta(ix, xh + wPre, yb);
            if (hl.Ins.Length > 0)
                HRule(ix, xh + wPre, xh + wPre + wIns, yb + IfsInsRuleDrop, 0.8);
            if (hl.Sup.Length > 0)
                Run(ix, "F10", IfsSupFs, xh + wPre + wIns, yb - IfsSupRise + 0.5, hl.Sup);
        }
        Run(ix, "F10", 8, ix.x0, ix.lastHeadBase, ix.col.LabelHead);
        ix.ruleTd = ix.lastHeadBase + IfsHeadRuleOff;
        HRule(ix, ix.x0, ix.x1, ix.ruleTd, 0.5);
        HRule(ix, ix.x0, ix.labelRight, ix.ruleTd + 0.13, 0.25);

        ix.dotW = MW(ix, ".", 8.0);
        ix.yRow = ix.ruleTd + IfsRowStartOff;
        for (var ri = 0; ri < ix.col.Rows.Count; ri++)
        {
            RenderIfsRow(ix, ri);
        }
        ix.closeTd = ix.yRow - IfsRowPitch + IfsCloseRuleOff;
        HRule(ix, ix.x0, ix.x1, ix.closeTd, 0.5);
        HRule(ix, ix.x0, ix.labelRight, ix.closeTd - 0.13, 0.25);
    }

    /// <summary>Ishares fact sheet: the page opened, its faces registered and the blue top rule drawn.</summary>
    private static void OpenIfsPage(IsharesFactSheetState ix)
    {
        ix.doc = Document.Create();
        ix.docFontDict = new Core.PdfDictionary();
        ix.page = ix.doc.Pages.Add(IfsPageW, IfsPageH);
        EnsureFonts(ix.page, ix.docFontDict);
        EnsureFont(ix.page, "Arial", "F8");
        EnsureFont(ix.page, "ArialBold", "F9");
        EnsureFont(ix.page, "ArialItalic", "F10");

        ix.inv = System.Globalization.CultureInfo.InvariantCulture;
        ix.sb = new StringBuilder();
        // Blue top rule across both columns.
        ix.sb.AppendLine(Compat.Format(ix.inv,
            $"q 0 0.663 0.878 rg 126 {IfsPageH - IfsBlueTopTd - 4:F2} 540 4 re f Q"));
    }

    /// <summary>Ishares fact sheet: one column of the side-by-side layout read off the html.</summary>
    private static bool ParseIfsColumn(IsharesFactSheetState ix, Match colM)
    {
        var seg = colM.Groups[1].Value;
        var tblM = Regex.Match(seg,
            @"<table class=""Table col3""(?:[^>]*?\bsubtype\s*=\s*""([^""]*)"")?[^>]*>([\s\S]*?)</table>",
            RegexOptions.IgnoreCase);
        if (!tblM.Success) return true;
        var col = new IfsColumn { Subtype = tblM.Groups[1].Value };
        var headP = Regex.Match(seg, @"<p class=""centerhead""[^>]*>([\s\S]*?)</p>",
            RegexOptions.IgnoreCase);
        if (headP.Success)
        {
            var t = headP.Groups[1].Value;
            var supM = Regex.Match(t, @"<sup[^>]*>([\s\S]*?)</sup>", RegexOptions.IgnoreCase);
            if (supM.Success) { col.TitleSup = IfsFlat(supM.Groups[1].Value); t = t.Remove(supM.Index, supM.Length); }
            col.Title = IfsFlat(t);
        }
        var thead = Regex.Match(tblM.Groups[2].Value, @"<thead>([\s\S]*?)</thead>",
            RegexOptions.IgnoreCase);
        if (thead.Success)
            foreach (Match hm in Regex.Matches(thead.Groups[1].Value,
                @"<td\b([^>]*)>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var clsM = Regex.Match(hm.Groups[1].Value, @"class\s*=\s*""([^""]*)""",
                    RegexOptions.IgnoreCase);
                var cls = clsM.Success ? clsM.Groups[1].Value : "";
                var inner = hm.Groups[2].Value;
                if (cls.Contains("Heading1"))
                {
                    // the TSR_Allo2 title is a colspan thead row, not a centerhead p
                    var supM = Regex.Match(inner, @"<sup[^>]*>([\s\S]*?)</sup>",
                        RegexOptions.IgnoreCase);
                    if (supM.Success) { col.TitleSup = IfsFlat(supM.Groups[1].Value); inner = inner.Remove(supM.Index, supM.Length); }
                    col.Title = IfsFlat(inner);
                }
                else if (cls.Contains("hasHangColumn"))
                    ParseIfsHeadCell(StripMarkers(ix, inner), col.ValueHead, IfsFlat);
                else if (cls.Contains("hangColumn"))
                {
                    // the head hang cell carries only a superscript marker; it
                    // rides as the last value-head line's suffix
                    if (IfsFlat(inner) is { Length: > 0 } hsup && col.ValueHead.Count > 0)
                        col.ValueHead[^1].Sup = hsup;
                }
                else if (cls.StartsWith("Head", StringComparison.Ordinal))
                    col.LabelHead = IfsFlat(inner);
            }
        foreach (Match rm in Regex.Matches(tblM.Groups[2].Value,
            @"<tr class=""CalcSheetSectionItem""[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase))
        {
            var row = new IfsRow();
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                @"<td class=""([^""]*)""[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase))
            {
                var cls = cm.Groups[1].Value;
                var inner = cm.Groups[2].Value;
                if (cls.Contains("categoryhead")) row.Label = IfsFlat(inner);
                else if (cls.Contains("hasHangColumn"))
                {
                    row.ValueIns = Regex.IsMatch(inner, @"<ins\b", RegexOptions.IgnoreCase);
                    row.ValueDelta = inner.Contains("DeltaSymbol", StringComparison.Ordinal);
                    row.Value = IfsFlat(StripMarkers(ix, inner));
                }
                else if (cls.Contains("hangColumn"))
                    foreach (Match sm in Regex.Matches(inner,
                        @"<(span|sup)\b([^>]*)>([\s\S]*?)</\1>", RegexOptions.IgnoreCase))
                    {
                        var txt = IfsFlat(sm.Groups[3].Value);
                        if (txt.Length > 0)
                            row.Hang.Add((txt,
                                sm.Groups[1].Value.Equals("sup", StringComparison.OrdinalIgnoreCase),
                                sm.Groups[2].Value.Contains("number-suffix=\"percent\"",
                                    StringComparison.Ordinal)));
                    }
            }
            if (row.Label.Length > 0 || row.Value.Length > 0) col.Rows.Add(row);
        }
        if (col.Rows.Count > 0) ix.cols.Add(col);
        if (ix.cols.Count == 2) return false;
        return true;
    }

    /// <summary>Ishares fact sheet: the hang cell's width measured from the widest suffix run.</summary>
    private static void MeasureIfsHangWidth(IsharesFactSheetState ix)
    {
        ix.hangW = 0;
        for (var ri = 0; ri < ix.col.Rows.Count; ri++)
        {
            double w = 0;
            foreach (var (t, sup, pct) in ix.col.Rows[ri].Hang)
                w += pct && ri > 0 ? MW(ix, t, 8.0) - IfsHiddenPctMargin
                    : MW(ix, t, sup ? IfsSupFs : 8.0);
            ix.hangW = Math.Max(ix.hangW, w);
        }
    }
}
