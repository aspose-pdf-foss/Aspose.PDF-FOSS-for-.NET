using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenHeadingRun(TableStyleConfig cfg, TableParseState ps, Token tok, string tag)
    {
        // Word mail: a heading in a cell takes the sheet's h1/h2 rule (size, face,
        // alignment) in bold on its own line, paced by its own line-height.
        if (cfg.wordMailCells && ps.cell is not null)
        {
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            ps.styleStack.Add((tag, ps.curFontPt, ps.curFamily, true, ps.curColor, false, false));
            ps.boldDepth++;
            ps.curLineHeightPct = WordMailLineHeightPct(tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var hSt) ? hSt : null);
            if ((cfg.docCss ?? cfg.css) is { } wmCss && wmCss.TryGetValue(tag, out var hRule))
            {
                if (hRule.TryGetValue("font-size", out var hfs) && TryParseLength(hfs) is { } hPt && hPt > 0) ps.curFontPt = hPt;
                if (hRule.TryGetValue("font-family", out var hff) && FirstFontFamily(hff) is { Length: > 0 } hFam) ps.curFamily = hFam;
                if (hRule.TryGetValue("text-align", out var hta) && ParseAlignAttr(hta) is { } hAl) { ps.alignSet = true; ps.cellAlign = hAl; }
            }
            return;
        }
        // A UA-boxed cell's heading is a block: its own line in the browser's heading size
        // and weight (or the sheet's h1/h2 rule, a percent of the running size), aligned as
        // the rule says, its 0.67 / 0.83 em margins collapsing with its neighbours' (measured:
        // `h1 { font-size: 140%; text-align: center }` at Verdana 6 -> 8.4 bold, centred).
        if (ps.uaCellBoxes && ps.cell is not null)
        {
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            ps.styleStack.Add((tag, ps.curFontPt, ps.curFamily, true, ps.curColor, false, false));
            var uaBase = ps.curFontPt > 0 ? ps.curFontPt : ps.uaBaseFontPt;
            ps.curFontPt = uaBase * UaHeadingFontEm(tag);
            ps.boldDepth++;
            if ((cfg.docCss ?? cfg.css) is { } uaCss && uaCss.TryGetValue(tag, out var uaRule))
            {
                if (uaRule.TryGetValue("font-size", out var ufs))
                {
                    var upm = Regex.Match(ufs.Trim(), @"^([\d.]+)\s*%$");
                    if (upm.Success && double.TryParse(upm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var uPct) && uPct > 0)
                        ps.curFontPt = uaBase * uPct / 100.0;
                    else if (TryParseLength(ufs.Trim()) is { } uAbs && uAbs > 0) ps.curFontPt = uAbs;
                }
                if (uaRule.TryGetValue("text-align", out var uta) && ParseAlignAttr(uta) is { } uAl) ps.uaLineAlign = uAl;
                // (…and the rule's line-height paces the heading's line: `h2 { line-height: 1.2em }` stands 23.4 px)
                if (uaRule.TryGetValue("line-height", out var ulh) && UaLineHeightPct(ulh.Trim(), ps.curFontPt) is { } ulhPct && ulhPct > 0)
                    ps.curLineHeightPct = ulhPct;
            }
            // (…and the margin the sheet's heading rule or the heading's own style declares replaces the UA em:
            //  the mailing's `h2 { margin: 0 0 .83em 0 }` opens flush under the logo row)
            UaLeaveHeadingMargin(ps, tag, tok, top: true);
            return;
        }
        // DataWorks form grid: a UA heading inside the title cell —
        // its own line at 2 em bold serif (the generic close arm
        // restores the pushed style).
        if (cfg.dwFormCells && ps.cell is not null && cfg.chainBase is null)
        {
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            ps.styleStack.Add((tag, ps.curFontPt, ps.curFamily, true, ps.curColor, false, false));
            ps.curFontPt = tag == "h1" ? DwH1FontPt : DwH1FontPt * 0.75;
            ps.boldDepth++;
            return;
        }
        // Chain-styled section heading: a BLOCK box spanning the cell
        // (the report's red bars) — own line, background, centred text
        // in its own colour, sized by the heading rule's percent font.
        OpenChainHeadingCell(cfg, ps, tok, tag);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenAnchorRun(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        // Open an inline anchor: remember where its text starts on the
        // current line and the target URL.
        if (ps.cell is not null)
        {
            // The anchor's colour — its inline style, else the sheet's
            // `a { color: … }` rule — rides the style stack for the
            // anchor's extent, exactly like a coloured <span>.
            Color? aCol = null;
            if (tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var aSt)
                && Regex.Match(aSt, @"(?<![-\w])color\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } aCm)
                aCol = ParseCssColor(aCm.Groups[1].Value.Trim());
            aCol ??= cfg.docAnchorColor;
            if (aCol is not null)
            {
                ps.styleStack.Add(("a", ps.curFontPt, ps.curFamily, false, ps.curColor, false, false));
                ps.curColor = aCol;
            }
        }
        if (ps.cell is not null && cfg.docAnchorBold)
        {
            ps.boldDepth++;
            ps.anchorBoldDepth++;
        }
        if (ps.cell is not null && tok.Attributes is not null
            && tok.Attributes.TryGetValue("href", out var aHref)
            && !string.IsNullOrEmpty(aHref))
            ps.openAnchor = (ps.line.Length, aHref);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenBoldRun(TableStyleConfig cfg, TableParseState ps)
    {
        if (ps.cell is not null)
        {
            // Form-grid: a bold run OPENING mid-line marks a style-run
            // boundary (the segment so far keeps the regular face).
            var uaRuns = ps.uaCellBoxes;
            if (cfg.formGridDialect || cfg.wordMailCells || uaRuns)
            {
                ps.lineRunMarks ??= new();
                if (ps.lineRunMarks.Count == 0)
                    ps.lineRunMarks.Add((0, ps.boldDepth > 0));
            }
            ps.boldDepth++;
            if (cfg.formGridDialect || cfg.wordMailCells || uaRuns) ps.lineRunMarks!.Add((ps.line.Length, true));
            // Probe: the min-content measure applies real bold metrics per
            // RUN (a bold word followed by a regular superscript measures
            // each piece with its own face), marked by sentinels.
            if (cfg.widenProbe) ps.line.Append('\uE000');
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenPreformattedBlock(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Token tok)
    {
        if (ps.cell is not null && !tok.IsSelfClosing)
        {
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            if (ps.preDepth++ == 0)
            {
                // the pre's own inline font styles bind its lines (the
                // case-comment box declares Arial at 1.0em)
                var prevPrePt = ps.curFontPt; var prevPreFam = ps.curFamily;
                if (tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var preSt) && preSt is not null)
                {
                    var preFf = Regex.Match(preSt, @"font-family\s*:\s*([^;]+)",
                        RegexOptions.IgnoreCase);
                    if (preFf.Success
                        && FirstFontFamily(preFf.Groups[1].Value) is { Length: > 0 } preFam)
                        ps.curFamily = preFam;
                    var preFs = Regex.Match(preSt, @"font-size\s*:\s*([\d.]+)\s*em",
                        RegexOptions.IgnoreCase);
                    if (preFs.Success && double.TryParse(preFs.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var preEm) && preEm > 0)
                        ps.curFontPt = preEm * (prevPrePt > 0 ? prevPrePt : cfg.cellFontSize);
                }
                ps.styleStack.Add(("pre", prevPrePt, prevPreFam, false, ps.curColor, false, false));
                if (ps.row is not null
                    && ((colModel.preCells ??= new()).Count == 0 || colModel.preCells[^1].Cell != ps.cell))
                    colModel.preCells.Add((ps.row, ps.cell));
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenLineBreak(TableStyleConfig cfg, TableParseState ps, Table table)
    {
        if (ps.cell is not null)
        {
            // An explicit <br> on an empty line is a deliberate blank line: it
            // keeps its line box (at the active style's size) as vertical space.
            // A LONE br on an empty line (not preceded by another br — e.g.
            // right after a block boundary or table close) is tagged: the
            // lifted-unstyled dialect drops it, keeping only the N−1 blanks
            // of an N-br run (the <BR><BR> rhythm); styled dialects keep
            // every one — they were calibrated that way.
            var loneBrBlank = ps.line.Length == 0 && !ps.cellPendingBrBlank;
            if (!ps.lineStyleSet) { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; }
            if (ps.lineColor is null) ps.lineColor = ps.curColor;
            PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
            if (loneBrBlank) (ps.loneBrBlankLines ??= new HashSet<int>()).Add(ps.lines.Count - 1);
            ps.cellPendingBrBlank = true;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenListBlock(TableStyleConfig cfg, TableParseState ps, Token tok, string tag)
    {
        if (ps.cell is not null && ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        // A list opening inside a paragraph closes it (the HTML parser's own rule): the list's
        // items stand in the cell's base typography, not the paragraph's runs.
        if (ps.uaCellBoxes && ps.cell is not null && !tok.IsSelfClosing) UaCloseParagraphForBlock(cfg, ps);
        // (the list's own typography first: its margins resolve in ITS size)
        if (ps.uaCellBoxes && !tok.IsSelfClosing) UaOpenListTypography(ps, tag, tok);
        // UA margin-block-start on a TOP-LEVEL list opening mid-cell:
        // one line box above the first item. A nested list carries none
        // (`ul ul { margin-block-start: 0 }` in every UA sheet).
        if (ps.uaCellBoxes && ps.cell is not null && !tok.IsSelfClosing && ps.listNesting.Count == 0)
            UaOpenBlock(ps, tag, tok);      // the sheet's margin, else the browser's own 1.12em, collapsing
        else if (cfg.liftNestedTables && ps.cell is not null && !tok.IsSelfClosing
            && ps.listNesting.Count == 0 && ps.lines.Count > 0)
        {
            if (!ps.lineStyleSet) { ps.lineFontPt = ps.curFontPt; ps.lineFamily = ps.curFamily; }
            PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: true);
        }
        if (!tok.IsSelfClosing) ps.listNesting.Add((tag == "ol", 0));
        // Content of the list — including bare text before its first
        // <li> — seats on the list's padding-inline-start indent.
        ps.liStandingIndentPt = ListItemIndentPt * ps.listNesting.Count;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenListItem(TableStyleConfig cfg, TableParseState ps)
    {
        if (ps.cell is not null)
        {
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            if (ps.listNesting.Count > 0)
            {
                var (liOrd, liCnt) = ps.listNesting[^1];
                ps.listNesting[^1] = (liOrd, liCnt + 1);
                var liMarker = liOrd
                    ? (liCnt + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                    : "•";
                ps.liStandingIndentPt = ListItemIndentPt * ps.listNesting.Count;
                // Hanging marker: the item's text seats ON the list indent,
                // the marker rides just left of it ("1." draws
                // as its own run ending one gap before the text).
                var liFs = ps.curFontPt > 0 ? ps.curFontPt : cfg.cellFontSize;
                // UA-boxed cell: the marker is its own run in the item's font, hanging before the
                // text, which seats exactly on the list indent.
                if (ps.uaCellBoxes)
                {
                    var liFam = ps.curFamily ?? ps.cellFamily;
                    ps.lineMarker = (liMarker, liFs, liFam, liFs * ((liFam is { } lf ? WinMetricsFor(lf)?.sum : null) ?? UaSerifLineRatio));
                    ps.lineMarginLeft = ps.liStandingIndentPt;
                    ps.lineHadText = true;
                    return;
                }
                ps.lineMarginLeft = Math.Max(0,
                    ps.liStandingIndentPt - MeasureLine(ps, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.widenProbe, liMarker + " ", false, liFs));
                // No implicit gap above an item: the question rhythm
                // (2 line boxes between items) is the
                // markup's own explicit <BR><BR>, which survives as a
                // kept blank line — a plain <ul> stacks its items at
                // bare line pitch.
                ps.line.Append(liMarker).Append(' ');
                ps.lineHadText = true;
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenSelectOption(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        if (cfg.dwFormCells && ps.dwSelectDepth > 0 && !tok.IsClose)
        {
            ps.dwOptSelected = tok.Attributes?.ContainsKey("selected") == true && !ps.dwSawSelected;
            if (ps.dwOptSelected) ps.dwSawSelected = true;
            ps.dwOptBuf.Clear();
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenTextArea(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        if (cfg.dwFormCells && ps.cell is not null && !tok.IsClose)
        {
            ps.dwTextareaOpen = true; ps.dwTaBuf.Clear();
            var (dtW, dtH) = ParseInputSize(tok.Attributes is not null
                && tok.Attributes.TryGetValue("style", out var dtSt) ? dtSt : null);
            ps.dwTaW = dtW > 0 ? dtW * 0.75 : DwTextareaWPt;
            ps.dwTaH = dtH > 0 ? dtH * 0.75 : DwTextareaHPt;
        }
    }

    /// <summary>A UA grid's checkbox: a form checkbox the cell carries as a paragraph, measured as its 13px margin box.</summary>
    private static void OpenUaGridCheckbox(TableStyleConfig cfg, TableParseState ps, Token tok)
    {
        (ps.cellCheckboxes ??= new List<Aspose.Pdf.Forms.CheckboxField>()).Add(cfg.makeCheckbox!(tok.Attributes!.ContainsKey("checked")));
        ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, Table.UaCheckboxMarginBoxPt);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenInputControl(TableStyleConfig cfg, TableParseState ps, Table table, Token tok)
    {
        // A form control INSIDE a grid cell occupies its line inline (it
        // must not flush the cell's text flow). A checkbox/radio paints
        // as a near-invisible white box, so only its advance matters —
        // and that is within the wrap tolerance; a text-like input
        // contributes its VALUE as cell text (the visible part of the
        // filled-in control).
        if (ps.cell is not null && tok.Attributes is not null)
        {
            OpenInputControlInCell(cfg, ps, table, tok);
        }
    }
}
