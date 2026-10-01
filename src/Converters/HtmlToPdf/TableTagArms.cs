using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleTextToken(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string? cssRunFace)
    {
        if (ps.uDepth > 0 && ps.cell is not null && ps.hiddenSubDepth == 0
            && !string.IsNullOrWhiteSpace(tok.Value))
            ps.lineHadU = true;
        if (ps.cell is not null && ps.hiddenSubDepth == 0
            && tok.Value.IndexOf(NestedMark, StringComparison.Ordinal) >= 0)
        {
            AbsorbNestedTableMark(cfg, ps, table, tok);
            return;
        }
        // A background-image badge's text is its letter — drawn inside the
        // badge circle by the render pass, never flowed into the line.
        if (ps.chainTrafficRun is not null && ps.cell is not null && ps.hiddenSubDepth == 0)
        {
            var badgeTxt = DecodeEntities(tok.Value).Trim();
            if (badgeTxt.Length > 0) ps.chainTrafficRun.CircleLetter += badgeTxt;
            return;
        }
        if (ps.cell is not null && ps.hiddenSubDepth == 0)
        {
            AppendTextTokenToLine(cfg, ps, colModel, tok, cssRunFace);
        }
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleCloseTag(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, string? cssRunFace)
    {
        // DataWorks control captures end here (the switch below sees only
        // opens): the select emits its chosen option's box, the textarea
        // its content box.
        CloseDataWorksFormTag(cfg, ps, tag);
        // Structure tags of a table NESTED inside a cell do not drive the
        // outer grid — the nested content flows as the host cell's text,
        // with a line break per nested CELL, so each nested cell keeps its
        // own text run the way it holds its own grid box.
        if (tag == "table")
        {
            ps.tableDepth--;
            if (ps.tableDepth <= 0) CloseRow(ps, colModel, table, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.widenProbe, cfg.uaSerifMin, cfg.ptCellWidths, cfg.redlineCells, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad);
            else if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        }
        else if (tag is "td" or "th")
        {
            if (ps.tableDepth <= 1) CloseCell(ps, colModel, table, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.widenProbe, cfg.uaSerifMin, cfg.ptCellWidths, cfg.redlineCells, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad);
            else if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        }
        else if (tag is "tbody" or "thead" or "tfoot")
        {
            if (ps.tableDepth <= 1) ps.sectionFontPt = 0;
        }
        else if (tag == "tr")
        {
            if (ps.tableDepth <= 1) CloseRow(ps, colModel, table, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.widenProbe, cfg.uaSerifMin, cfg.ptCellWidths, cfg.redlineCells, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad);
            else if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        }
        else if (tag == "a")
        {
            if (ps.anchorBoldDepth > 0) { ps.anchorBoldDepth--; if (ps.boldDepth > 0) ps.boldDepth--; }
            if (ps.cell is not null && ps.openAnchor is { } oaC)
            {
                var inner = CollapseWs(ps.line.ToString()[oaC.Start..]);
                if (inner.Length > 0) (ps.lineAnchors ??= new()).Add((inner, oaC.Url));
                ps.openAnchor = null;
            }
            // …and the anchor's ink ends with the anchor. OpenAnchorRun pushes a
            // style frame whenever the sheet's `a { color: … }` rule or an inline
            // style gives the link its own colour; without this restore the frame
            // outlives the element and the link ink runs on through the paragraphs
            // that follow it (probed: the reference paints only the link text).
            for (var k = ps.styleStack.Count - 1; k >= 0; k--)
                if (ps.styleStack[k].Tag == "a")
                {
                    ps.curFontPt = ps.styleStack[k].PrevPt;
                    ps.curFamily = ps.styleStack[k].PrevFamily;
                    ps.curColor = ps.styleStack[k].PrevColor;
                    ps.styleStack.RemoveAt(k);
                    break;
                }
        }
        else if (tag is "ol" or "ul")
        {
            if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            if (ps.listNesting.Count > 0) ps.listNesting.RemoveAt(ps.listNesting.Count - 1);
            ps.liStandingIndentPt = ListItemIndentPt * ps.listNesting.Count;
            // UA margin-block-end of a TOP-LEVEL list closing mid-cell: one
            // line box below the last item, the twin of the open-side margin.
            if (ps.uaCellBoxes && ps.cell is not null && ps.listNesting.Count == 0)
                UaCloseBlock(ps, tag);
            if (ps.uaCellBoxes) UaCloseListTypography(ps, tag);
            else if (cfg.liftNestedTables && ps.cell is not null && ps.listNesting.Count == 0
                && ps.lines.Count > 0)
            {
                if (!ps.lineStyleSet) { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; }
                PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
            }
        }
        else if (tag == "li")
        {
            if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        }
        else HandleCloseInlineAndBlockTag(cfg, ps, colModel, tag);
    }

    /// <summary>The font-size a row group's own style attribute declares, in points; 0 when it declares none.</summary>
    private static double RowGroupFontPt(Token tok)
    {
        if (tok.Attributes is null || !tok.Attributes.TryGetValue("style", out var st) || st is null) return 0;
        var m = Regex.Match(st, @"(?<![-\w])font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
        return m.Success && TryParseLength(m.Groups[1].Value.Trim()) is { } pt && pt > 0 ? pt : 0;
    }

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleRowOpen(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table, Token tok, string tag, string? cssRunFace)
    {
        if (ps.tableDepth > 1)
        {
            if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            return;
        }
        // A hidden row (`<tr style="display:none">` — the empty-state
        // tfoot band of a data grid) is out of the layout entirely: no
        // cells, no height, no column measures. The in-cell hidden check
        // above never sees it because no cell is open at a row boundary.
        if (IsHiddenElement(tag, tok.Attributes, cfg.css))
        {
            ps.hiddenSubTag = tag;
            ps.hiddenSubDepth = 1;
            return;
        }
        CloseRow(ps, colModel, table, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.widenProbe, cfg.uaSerifMin, cfg.ptCellWidths, cfg.redlineCells, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad); ps.row = new Row();
        ps.rowFontPt = ps.sectionFontPt; ps.rowLineHPt = 0; ps.rowMinHeightPt = 0; ps.rowMinHeightIsContent = false; ps.rowAlign = null;
        ps.rowVAlign = VerticalAlignment.None;
        if (cfg.liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("valign", out var trVaAttr))
            ps.rowVAlign = trVaAttr.Trim().ToLowerInvariant() switch
            {
                "top" => VerticalAlignment.Top,
                "middle" or "center" => VerticalAlignment.Center,
                "bottom" => VerticalAlignment.Bottom,
                _ => VerticalAlignment.None,
            };
        // A row's declared fill paints its whole band behind the cells.
        ReadRowAttributes(cfg, ps, tok);
        // ALIGN on the row is the default for every cell in it that
        // declares none of its own.
        if (cfg.liftNestedTables && tok.Attributes is not null
            && tok.Attributes.TryGetValue("align", out var trAl))
            ps.rowAlign = ParseAlignAttr(trAl);
        // A row's CSS height (a `tr {height:28px}` rule, a `.medium` class
        // variant, or an inline style) is a MINIMUM: content-driven rows
        // still grow past it, matching the browser's table model. The rule
        // usually lives in the document stylesheet, not the segment.
        if (TryGetCssLength(cfg.css, "tr", "height") is { } trh && trh > 0)
            ps.rowMinHeightPt = trh;
        else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "tr", "height") is { } dtrh && dtrh > 0)
            ps.rowMinHeightPt = dtrh;
        // An INLINE height on the row is the same minimum
        // (`<tr style="white-space:nowrap;height:30px">` — the rental
        // question row) — over-declared grid dialect only.
        ReadRowDeclaredWidths(cfg, ps, tok);
        // Separate element-rule borders ride ON the CSS row height
        // (see cfg.elemRuleBorder above).
        if (cfg.elemRuleBorder && ps.rowMinHeightPt > 0)
            ps.rowMinHeightPt += cfg.borderWidth;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var trCls))
            foreach (var cls in trCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryGetCssLength(cfg.css, "tr." + cls, "height") is { } trch && trch > 0)
                    ps.rowMinHeightPt = trch;
                else if (cfg.docCss is not null && TryGetCssLength(cfg.docCss, "tr." + cls, "height") is { } dtrch && dtrch > 0)
                    ps.rowMinHeightPt = dtrch;
            }
        ps.uaPrevRowRuleBelowPt = ps.rowRuleBelowPt;
        ps.rowRuleBelowPt = 0; ps.rowRuleColor = null;
        ps.uaRowBg = ps.uaCellBoxes ? UaRowBackground(cfg, ps, tok) : null;
        if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var trStyle))
        {
            if (ps.uaCellBoxes && UaRuleBelow(trStyle) is (var trRuleW, var trRuleC))
            { ps.rowRuleBelowPt = trRuleW; ps.rowRuleColor = trRuleC; }
            var trfs = Regex.Match(trStyle, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (trfs.Success && TryParseLength(trfs.Groups[1].Value.Trim()) is { } trfp) ps.rowFontPt = trfp;
            // (…and a UA-boxed row's own line-height paces its cells' lines: the remittance rows' 30 px)
            var trlh = Regex.Match(trStyle, @"(?<![-\w])line-height\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (ps.uaCellBoxes && trlh.Success
                && UaLineHeightPct(trlh.Groups[1].Value.Trim(), ps.rowFontPt > 0 ? ps.rowFontPt : cfg.cellFontSize) is { } trlhPct && trlhPct > 0)
                ps.rowLineHPt = (ps.rowFontPt > 0 ? ps.rowFontPt : cfg.cellFontSize) * trlhPct / WholeWidthPercent;
            var trhm = Regex.Match(trStyle, @"height\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (trhm.Success && TryParseLength(trhm.Groups[1].Value.Trim()) is { } trhp && trhp > 0)
                ps.rowMinHeightPt = trhp;
        }
    }

}
