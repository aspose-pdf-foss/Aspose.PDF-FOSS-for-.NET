using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricImgOpen(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool wrapperStacks, bool reportCells, List<List<MetricCell>> rows, string face, string boldFace, (double asc, double sum) fm, double indent, HtmlLoadOptions? loadOptions, double p, bool rtl, double s, string tableHtml, double tablePct, double tableWpt, string tag)
    {
        // an image reserves its DECLARED box even when the file is
        // unreadable — the row paces on it (the boleto's 40px logo)
        if (wrapperStacks && mps.cell is not null)
        {
            OpenMetricImgInCell(mps, tok, text, css, stdSerif, wrapperStacks, reportCells, rows, face, boldFace, fm, indent, loadOptions, p, rtl, s, tableHtml, tablePct, tableWpt, tag);
        }
    }

    /// <summary>A UA block's own inline typography: its font-size, its right alignment
    /// (`text-align: right`) and a right float (drawn right-aligned, out of the flow).</summary>
    private static void ReadUaBlockStyle(MetricDivSeg seg, Token tok, double cellEmPt = 0)
    {
        if (tok.Attributes is not { } a || !a.TryGetValue("style", out var st) || st is null) return;
        var fsM = Regex.Match(st, @"(?<![-\w])font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (fsM.Success && TryParseCssFontSize(fsM.Groups[1].Value.Trim()) is { } fs && fs > 0) seg.FontSize = fs;
        // (a margin in em is of the block's OWN size: its stated size, else the cell's)
        var emPt = seg.FontSize ?? (cellEmPt > 0 ? cellEmPt : UaDefaultFontPt);
        if (Regex.IsMatch(st, @"(?<![-\w])text-align\s*:\s*right", RegexOptions.IgnoreCase)) seg.AlignRight = true;
        if (Regex.IsMatch(st, @"(?<![-\w])float\s*:\s*right", RegexOptions.IgnoreCase)) { seg.FloatRight = true; seg.AlignRight = true; }
        // The block's own box: `margin-left` indents its lines, and a stated top or bottom
        // margin (or the shorthand) replaces the UA 1.12 em on that side (probed: margin-less
        // paragraphs 13.5 apart where the UA ones stand 13.44 further).
        if (Regex.Match(st, @"(?<![-\w])margin-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mlM
            && TryParseLength(mlM.Groups[1].Value.Trim()) is { } mlPt && mlPt > 0)
            seg.PadLeft = mlPt;
        if (Regex.Match(st, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mM)
        {
            var mParts = mM.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (mParts.Length > 0)
            {
                seg.MarginTopStatedPt = StatedMarginPt(mParts[0], emPt);
                seg.MarginBottomStatedPt = StatedMarginPt(mParts[mParts.Length >= 3 ? 2 : 0], emPt);
            }
        }
        if (Regex.Match(st, @"(?<![-\w])margin-top\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mtM)
            seg.MarginTopStatedPt = StatedMarginPt(mtM.Groups[1].Value, emPt);
        if (Regex.Match(st, @"(?<![-\w])margin-bottom\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mbM)
            seg.MarginBottomStatedPt = StatedMarginPt(mbM.Groups[1].Value, emPt);
    }

    /// <summary>A stated margin length in points: zero is zero, an em length is of the given size,
    /// another length its value, anything else (auto, a percentage) no statement.</summary>
    private static double? StatedMarginPt(string value, double emPt)
    {
        var v = value.Trim();
        if (IsZeroLength(v)) return 0.0;
        if (Regex.Match(v, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*em$", RegexOptions.IgnoreCase) is { Success: true } em)
            return double.Parse(em.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * emPt;
        // (a NEGATIVE margin is a statement too - the length parser hands back only positive
        // lengths, so the sign is read here: the cheque's `margin-top: -43px` block)
        if (v.StartsWith('-') && TryParseLength(v[1..].Trim()) is { } neg) return -neg;
        return TryParseLength(v);
    }

    /// <summary>The four padding sides an inline style states, in points (em of the given size), the
    /// shorthand first and a side's own property over it; zero where none is stated.</summary>
    private static (double top, double right, double bottom, double left) CssPaddingSidesPt(string style, double emPt)
    {
        double Len(string v) => StatedMarginPt(v, emPt) ?? 0;
        double t = 0, r = 0, b = 0, l = 0;
        if (Regex.Match(style, @"(?<![-\w])padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } pm)
        {
            var parts = pm.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) t = r = b = l = Len(parts[0]);
            else if (parts.Length == 2) { t = b = Len(parts[0]); r = l = Len(parts[1]); }
            else if (parts.Length == 3) { t = Len(parts[0]); r = l = Len(parts[1]); b = Len(parts[2]); }
            else if (parts.Length >= 4) { t = Len(parts[0]); r = Len(parts[1]); b = Len(parts[2]); l = Len(parts[3]); }
        }
        if (Regex.Match(style, @"(?<![-\w])padding-top\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } ptM) t = Len(ptM.Groups[1].Value);
        if (Regex.Match(style, @"(?<![-\w])padding-right\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } prM) r = Len(prM.Groups[1].Value);
        if (Regex.Match(style, @"(?<![-\w])padding-bottom\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } pbM) b = Len(pbM.Groups[1].Value);
        if (Regex.Match(style, @"(?<![-\w])padding-left\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } plM) l = Len(plM.Groups[1].Value);
        return (t, r, b, l);
    }

    /// <summary>A UA block's hard line closes: the line keeps the bold of its ink (by majority)
    /// and the size its first ink saw; the next line starts its own count.</summary>
    private static void CloseUaBlockLine(MetricParseState mps)
    {
        mps.curSeg!.LineTypo.Add((mps.segBoldChars > mps.segPlainChars, mps.segLineInkSeen ? mps.segLineFs : null));
        mps.segBoldChars = 0; mps.segPlainChars = 0;
        mps.segLineInkSeen = false; mps.segLineFs = null;
    }

    /// <summary>The UA heading sizes and block margins, in em of the cell's font: h1 2 / 0.67,
    /// h2 1.5 / 0.75 (the shipping era's h2 margin, see the heading-margin spec), h3 1.17 / 1,
    /// h4 1 / 1.33, h5 0.83 / 1.67, h6 0.67 / 2.33.</summary>
    private static (double sizeEm, double marginEm) UaHeadingEm(string tag) => tag switch
    {
        "h1" => (2.0, 0.67),
        "h2" => (1.5, 0.75),
        "h3" => (1.17, 1.0),
        "h4" => (1.0, 1.33),
        "h5" => (0.83, 1.67),
        _ => (0.67, 2.33),
    };

    /// <summary>One arm of BuildTableFromHtml's token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void HandleMetricParaOpen(MetricParseState mps, Token tok, StringBuilder text, IReadOnlyDictionary<string, Dictionary<string, string>> css, bool stdSerif, bool wrapperStacks, bool reportCells, List<List<MetricCell>> rows, string face, string boldFace, (double asc, double sum) fm, double indent, HtmlLoadOptions? loadOptions, double p, bool rtl, double s, string tableHtml, double tablePct, double tableWpt, string tag)
    {
        // `<b><p>…</p></b>` — a BLOCK opening inside an emphasis inline.
        // The HTML parser cannot nest them, so it closes the inline
        // before the block and reopens it after, leaving an empty
        // inline on each side (see CloseCell for the boxes they keep).
        if (mps.cell is not null && mps.boldDepth > 0
            && CollapseWs(text.ToString()).Trim(' ').Length == 0)
            mps.cell.OrphanInlineBoxes = true;
        // An in-cell paragraph's own margin-left indents the cell's
        // text (the financial statement's `margin: 0pt 0pt 0pt
        // 14.4pt` label ladder).
        if (stdSerif && mps.cell is not null && tok.Attributes is { } pmAttrs
            && pmAttrs.TryGetValue("style", out var pmSt) && pmSt is not null
            && Regex.Match(pmSt,
                @"margin\s*:\s*[\d.]+pt\s+[\d.]+pt\s+[\d.]+pt\s+([\d.]+)pt",
                RegexOptions.IgnoreCase) is { Success: true } pmM
            && double.TryParse(pmM.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pmL)
            && pmL > 0)
            mps.cell.PadLeft = Math.Max(mps.cell.PadLeft, pmL);
        // (a cell paragraph the sheet lays inline paces the cell's lines on the `p` rule's line-height)
        if (stdSerif && mps.pInlineCells && mps.cell is not null && mps.pInlineLineHeightEm > 0)
            mps.cell.LineHeightPt = mps.pInlineLineHeightEm * (mps.cell.FontSize ?? mps.fontSize);
        // (a plain UA cell's paragraph aligns the cell's lines by its own align attribute, whatever
        // the cell's - the safety data sheet's `<p align="center">` inside `<td align="left">`)
        if (stdSerif && !reportCells && mps.cell is not null && tok.Attributes is { } paA0
            && paA0.TryGetValue("align", out var paAlign0) && paAlign0 is not null)
        {
            var pal0 = paAlign0.Trim().ToLowerInvariant();
            if (pal0 == "center") mps.cell.Align = HorizontalAlignment.Center;
            else if (pal0 == "right") mps.cell.Align = HorizontalAlignment.Right;
            else if (pal0 == "left") mps.cell.Align = HorizontalAlignment.Left;
        }
        // The sheet's tag.class / class rules style the paragraph's
        // cell (`P.order { font-size: 120% }` on the 12 pt base).
        if (stdSerif && mps.cell is not null && tok.Attributes is { } pAttrs0
            && pAttrs0.TryGetValue("class", out var pCls0) && pCls0 is not null)
            foreach (var pc0 in pCls0.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (css.TryGetValue(tag + "." + pc0, out var pRule0)
                    || css.TryGetValue("." + pc0, out pRule0))
                    ApplyCellClassBag(mps, css, text, reportCells, stdSerif, mps.cell, pRule0);
        // Report cells: each paragraph is its own SEGMENT with the
        // typography its spans set. The lead text (a styled heading
        // span) becomes the first segment so a later span cannot
        // restyle it retroactively; sub-table markers stay in the
        // cell text for CloseCell's lift.
        OpenReportCellParagraph(mps, tok, text, css, stdSerif, reportCells);
    }
}
