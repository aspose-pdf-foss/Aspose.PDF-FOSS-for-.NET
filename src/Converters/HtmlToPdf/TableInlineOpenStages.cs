using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Inline-open stages: the chain selector, run face, inline style and redline attributes.</summary>
    private static void ApplyRedlineAttributes(InlineOpenState io)
    {
        if (io.redlineCells && io.ps.cell is not null && io.tok.Attributes is { } rdA)
        {
            void RdOpen(int kind, Color? c)
            {
                (io.ps.cellDecorActive ??= new()).Add((io.ps.styleStack.Count, kind, c));
                if (io.ps.lineDecorUnion is null || !io.ps.lineDecorUnion.Contains((kind, c)))
                    (io.ps.lineDecorUnion ??= new()).Add((kind, c));
            }
            if (rdA.TryGetValue("style", out var rdSt) && rdSt is not null
                && Regex.Match(rdSt, @"text-decoration\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } rdTd)
            {
                var rdv = rdTd.Groups[1].Value;
                if (rdv.Contains("underline", StringComparison.OrdinalIgnoreCase)) RdOpen(1, null);
                if (rdv.Contains("line-through", StringComparison.OrdinalIgnoreCase)) RdOpen(2, null);
            }
            if (rdA.TryGetValue("class", out var rdCls) && rdCls is not null)
                foreach (var rdSc in rdCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!io.css.TryGetValue("." + rdSc, out var rdr)
                        && !io.css.TryGetValue("span." + rdSc, out rdr)
                        && io.docCss is not null
                        && !io.docCss.TryGetValue("." + rdSc, out rdr))
                        io.docCss.TryGetValue("span." + rdSc, out rdr);
                    if (rdr is null) continue;
                    if (rdr.TryGetValue("text-decoration", out var rdd))
                    {
                        if (rdd.Contains("underline", StringComparison.OrdinalIgnoreCase)) RdOpen(1, null);
                        if (rdd.Contains("line-through", StringComparison.OrdinalIgnoreCase)) RdOpen(2, null);
                    }
                    if (rdr.TryGetValue("border-bottom", out var rdb)
                        && !rdb.Contains("none", StringComparison.OrdinalIgnoreCase))
                        RdOpen(rdb.Contains("dashed", StringComparison.OrdinalIgnoreCase)
                            || rdb.Contains("dotted", StringComparison.OrdinalIgnoreCase) ? 4 : 3,
                            ParseCssColor(rdb));
                }
        }
    }

    /// <summary></summary>
    private static void ApplyInlineStyle(InlineOpenState io)
    {
        if (io.tok.Attributes is not null && io.tok.Attributes.TryGetValue("style", out var inl))
        {
            if (io.ps.uaCellBoxes && UaUppercaseDeclared(inl)) io.ps.uaUpperDepths.Add(io.ps.styleStack.Count);
            // A UA-boxed paragraph's left margin and padding inset its lines (px, pt or em).
            if (io.ps.uaCellBoxes && io.tag == "p")
            {
                var uaFs = io.ps.curFontPt > 0 ? io.ps.curFontPt : io.ps.uaBaseFontPt;
                var uaInset = (CssBoxSidePt(inl, "margin", "margin-left", 3, uaFs) ?? 0)
                    + (CssBoxSidePt(inl, "padding", "padding-left", 3, uaFs) ?? 0);
                if (uaInset > 0) io.ps.lineMarginLeft = uaInset;
            }
            var fsm = Regex.Match(inl, @"font-size\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (fsm.Success && TryParseLength(fsm.Groups[1].Value.Trim()) is { } fsp) io.ps.curFontPt = fsp;
            // A UA-boxed cell's percent size is a percent of the running size (`font-size: 140%` at
            // Verdana 6 -> 8.4).
            else if (fsm.Success && io.ps.uaCellBoxes
                && Regex.Match(fsm.Groups[1].Value.Trim(), @"^([\d.]+)\s*%$") is { Success: true } fpm
                && double.TryParse(fpm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var fPct) && fPct > 0)
                io.ps.curFontPt = (io.ps.curFontPt > 0 ? io.ps.curFontPt : io.ps.uaBaseFontPt) * fPct / 100.0;
            var ffm = Regex.Match(inl, @"font-family\s*:\s*([^;""']+)", RegexOptions.IgnoreCase);
            if (ffm.Success && FirstFontFamily(ffm.Groups[1].Value) is { Length: > 0 } fam) io.ps.curFamily = fam;
            // The redline document QUOTES its families
            // (`font-family: 'Times New Roman'`) — read through
            // the quotes where the bare read came up empty.
            else if ((io.redlineCells || io.ps.wordMailCells)
                && Regex.Match(inl, @"font-family\s*:\s*['""]([^'"";]+)['""]",
                    RegexOptions.IgnoreCase) is { Success: true } ffq
                && FirstFontFamily(ffq.Groups[1].Value) is { Length: > 0 } famq)
                io.ps.curFamily = famq;
            // …and its own colour: `<p style="color:#004178">` paints THIS
            // paragraph, while its black sibling in the same cell stays black.
            var colm = Regex.Match(inl, @"(?<![-\w])color\s*:\s*([^;""']+)",
                RegexOptions.IgnoreCase);
            if (colm.Success && ParseCssColor(colm.Groups[1].Value.Trim()) is { } inlCol)
                io.ps.curColor = inlCol;
            // An inline font-weight (the expanded `font: bold …` shorthand)
            // opens a bold run like <b> does, restored at the closing tag.
            if (Regex.IsMatch(inl, @"font-weight\s*:\s*(bold|[7-9]00)", RegexOptions.IgnoreCase))
            {
                io.styleBold = true;
                UaMarkBoldRun(io.ps, io.ps.boldDepth > 0);
                io.ps.boldDepth++;
                UaMarkBoldRun(io.ps, true);
                if (io.widenProbe) io.ps.line.Append('');
            }
            // An inline font-style italic opens an italic run the same
            // way (the form-grid band titles), restored at the close.
            if ((io.formGridDialect || (io.ps.uaCellBoxes))
                && Regex.IsMatch(inl, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
            {
                io.styleItalic = true;
                io.ps.italicDepth++;
            }
            // Lifted dialect: a paragraph's vertical margins are real
            // space between the cell's paragraphs. The gap ABOVE this
            // one is the CSS-collapsed max of its own margin-top and
            // the previous paragraph's margin-bottom; an em resolves
            // against the paragraph's OWN font size (its inline
            // font-size when declared, since it may not have applied
            // to curFontPt yet).
            ApplyNestedParagraphMargins(io, inl);
            // Band dialect: a paragraph's explicit top margin survives as a
            // gap above its first line in the cell layout.
            // (The pt-styled fragment reads its margin shorthand
            // through the same parse.)
            ApplyParagraphMargins(io, inl);
        }
    }

    /// <summary></summary>
    private static void ApplyRunFace(InlineOpenState io)
    {
        if (io.ps.wordMailCells && io.tok.Attributes is not null
            && io.tok.Attributes.TryGetValue("class", out var wmCls))
            ApplyWordMailClassRule(io, wmCls);
        // …and a sheet that states its cells' box styles the runs inside them too: a span carrying a
        // class is that class's size and weight, not the cell's (
        // inside a 12 pt value cell is a 9.75 pt bold label, and measuring it at the cell's size wraps
        // a line the reference keeps whole).
        if ((io.cssRunFace is not null || io.ps.uaCellBoxes || io.ps.sheetTdBoxRule)
            && io.tok.Attributes is not null
            && io.tok.Attributes.TryGetValue("class", out var runCls))
            foreach (var rc in runCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var rk = "." + rc;
                if (!io.css.TryGetValue(rk, out var rcd)
                    && (io.docCss is null || !io.docCss.TryGetValue(rk, out rcd))) continue;
                if (rcd.TryGetValue("font-size", out var rfs)
                    && TryParseLength(rfs) is { } rfsp && rfsp > 0) io.ps.curFontPt = rfsp;
                if (rcd.TryGetValue("font-family", out var rff)
                    && FirstFontFamily(rff) is { Length: > 0 } rfam) io.ps.curFamily = rfam;
                if (io.ps.sheetTdBoxRule && !io.styleBold
                    && rcd.TryGetValue("font-weight", out var rfw)
                    && Regex.IsMatch(rfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                {
                    io.styleBold = true;
                    io.ps.boldDepth++;
                }
            }
        // Legacy `<font size="1".."7">` attribute in a grid cell —
        // browser-parsed (leading digits of junk like "7pt" count,
        // clamped to the 1..7 scale, 7 = 36pt). The form grid's
        // spacer row is sized from exactly this. Form-grid
        // dialect only — the calibrated grids ignore the attribute.
        if (io.formGridDialect && io.tag == "font" && io.tok.Attributes is not null
            && io.tok.Attributes.TryGetValue("size", out var fSizeAttr))
        {
            var fst = fSizeAttr.Trim();
            var fDigits = 0;
            while (fDigits < fst.Length && (char.IsDigit(fst[fDigits])
                   || (fDigits == 0 && fst[0] is '+' or '-'))) fDigits++;
            if (fDigits > 0 && int.TryParse(fst[..fDigits], out var fSz))
            {
                if (fst[0] is '+' or '-') fSz = 3 + fSz;
                io.ps.curFontPt = HtmlFontSizeToPt(Compat.Clamp(fSz, 1, 7));
            }
        }
    }

    /// <summary></summary>
    private static void ApplyChainSelector(InlineOpenState io)
    {
        if (io.chainBase is not null && io.tag != "p")
        {
            var chSpanElem = ChainTokElem(io.tag, io.tok.Attributes);
            io.ps.chainOpenElems!.Add(chSpanElem);
            if (io.ps.chainTdElem is not null
                && MatchChainDecls(io.chainRules, BuildOpenChain(io.ps, io.chainBase)) is { } srd)
            {
                if (srd.TryGetValue("display", out var sdisp))
                    chSpanElem.Display = sdisp.Trim().ToLowerInvariant();
                if (srd.TryGetValue("font-size", out var sfs))
                {
                    var sBase = io.ps.curFontPt > 0 ? io.ps.curFontPt
                        : io.ps.cellClassPt > 0 ? io.ps.cellClassPt : io.cellFontSize;
                    var spm = Regex.Match(sfs.Trim(), @"^([\d.]+)\s*%$");
                    if (spm.Success && double.TryParse(spm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var sPct)
                        && sPct > 0)
                        io.ps.curFontPt = sBase * sPct / 100.0;
                    else if (ChainLenPt(sfs, sBase) is > 0 and var sAbs)
                        io.ps.curFontPt = sAbs;
                }
                // font-weight:normal CANCELS an enclosing bold for
                // this run (`.SmallerTitle` under a bold plate).
                if (srd.TryGetValue("font-weight", out var sfwN)
                    && io.ps.boldDepth > 0
                    && Regex.IsMatch(sfwN, @"^\s*(normal|[1-5]00)", RegexOptions.IgnoreCase))
                {
                    io.chainUnbold.Add((io.tag, io.ps.boldDepth));
                    io.ps.boldDepth = 0;
                }
                ChainBoxOpenMaybe(io.ps, io.options, io.cellFontSize, chSpanElem, srd);
                if (!io.styleBold && srd.TryGetValue("font-weight", out var sfw)
                    && Regex.IsMatch(sfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                {
                    io.styleBold = true;
                    UaMarkBoldRun(io.ps, io.ps.boldDepth > 0);
                    io.ps.boldDepth++;
                    UaMarkBoldRun(io.ps, true);
                    if (io.widenProbe) io.ps.line.Append('');
                }
            }
        }
    }

    /// <summary></summary>
    private static void ApplyParagraphMargins(InlineOpenState io, string inl)
    {
        if ((io.bandDialect || io.ptCellWidths || io.redlineCells) && io.tag == "p")
        {
            var mtm = Regex.Match(inl, @"margin-top\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (mtm.Success && double.TryParse(mtm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pmt) && pmt > 0)
                io.ps.lineMarginTop = pmt;
            // margin-left in pt or em (em against the paragraph's own
            // resolved font size), netted against a NEGATIVE text-indent —
            // the "margin-left:2em; text-indent:-2em" hanging-indent idiom
            // leaves the first line at the content edge.
            var mlm = Regex.Match(inl, @"margin-left\s*:\s*([\d.]+)\s*(pt|em)", RegexOptions.IgnoreCase);
            if (mlm.Success && double.TryParse(mlm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var pml) && pml > 0)
            {
                var emBase = io.ps.curFontPt > 0 ? io.ps.curFontPt : 8;
                var ml = mlm.Groups[2].Value.Equals("em", StringComparison.OrdinalIgnoreCase)
                    ? pml * emBase : pml;
                var tim = Regex.Match(inl, @"text-indent\s*:\s*(-?[\d.]+)\s*(pt|em)", RegexOptions.IgnoreCase);
                if (tim.Success && double.TryParse(tim.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var ti) && ti < 0)
                    ml = Math.Max(0, ml + (tim.Groups[2].Value.Equals("em", StringComparison.OrdinalIgnoreCase)
                        ? ti * emBase : ti));
                io.ps.lineMarginLeft = ml;
            }
            // Redline cell paragraphs spell margin-top in a
            // 1-3 value shorthand (`margin: 4pt 0pt 0pt`).
            if (io.redlineCells && io.ps.cellFirstPMarginTopPt <= 0
                && Regex.Match(inl,
                    @"(?<![-\w])margin\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } rlcm
                && double.TryParse(rlcm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rlcmv)
                && rlcmv > 0)
                io.ps.cellFirstPMarginTopPt = rlcmv;
            if (io.redlineCells && io.ps.lineMarginTop <= 0
                && Regex.Match(inl,
                    @"(?<![-\w])margin\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } rlmt
                && double.TryParse(rlmt.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var rlmtv)
                && rlmtv > 0)
                io.ps.lineMarginTop = rlmtv;
            // pt-styled fragment: the margin SHORTHAND's
            // LEFT (4th) value indents the paragraph the
            // same way (`margin:0pt 6.4pt 0pt 1.7pt`), and
            // the RIGHT (2nd) narrows its wrap box.
            if (io.ptCellWidths
                && Regex.Match(inl,
                    @"(?<![-\w])margin\s*:\s*[\d.]+\w*\s+([\d.]+)\s*pt\s+[\d.]+\w*\s+([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } msh4)
            {
                if (io.ps.lineMarginLeft <= 0
                    && double.TryParse(msh4.Groups[2].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var msh4L)
                    && msh4L > 0)
                    io.ps.lineMarginLeft = msh4L;
                if (double.TryParse(msh4.Groups[1].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var msh4R)
                    && msh4R > 0)
                    io.ps.cellPMarginRightPt = Math.Max(io.ps.cellPMarginRightPt, msh4R);
            }
            // …and the paragraph's own text-align seats the
            // cell (the numeric columns' `text-align:right`
            // rides the <p>, not the <td>).
            if ((io.ptCellWidths || io.redlineCells)
                && Regex.Match(inl, @"text-align\s*:\s*(left|right|center)",
                    RegexOptions.IgnoreCase) is { Success: true } ptaM)
            {
                io.ps.alignSet = true;
                io.ps.cellAlign = ptaM.Groups[1].Value.ToLowerInvariant() switch
                {
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
            }
        }
    }

    /// <summary></summary>
    private static void ApplyNestedParagraphMargins(InlineOpenState io, string inl)
    {
        if (io.liftNestedTables && !io.bandDialect && io.tag == "p")
        {
            var pEmBase = Regex.Match(inl, @"(?<![-\w])font-size\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } pfm
                && TryParseLength(pfm.Groups[1].Value.Trim()) is { } pfsPt && pfsPt > 0
                ? pfsPt
                : io.ps.curFontPt > 0 ? io.ps.curFontPt
                : io.ps.cellClassPt > 0 ? io.ps.cellClassPt : io.cellFontSize;
            double pTop = 0, pBot = 0;
            if (Regex.Match(inl, @"(?<![-\w])margin\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } pShm)
            {
                var parts = pShm.Groups[1].Value
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                double PartPt(string v) =>
                    v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var em)
                        ? em * pEmBase
                        : TryParseLength(v) is { } abs ? abs : 0;
                if (parts.Length > 0) pTop = pBot = PartPt(parts[0]);
                if (parts.Length >= 3) pBot = PartPt(parts[2]);
            }
            if (Regex.Match(inl, @"(?<![-\w])margin-top\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } pTm)
            {
                var v = pTm.Groups[1].Value.Trim();
                pTop = v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var emT)
                    ? emT * pEmBase
                    : TryParseLength(v) is { } absT ? absT : pTop;
            }
            if (Regex.Match(inl, @"(?<![-\w])margin-bottom\s*:\s*([^;""']+)",
                    RegexOptions.IgnoreCase) is { Success: true } pBm)
            {
                var v = pBm.Groups[1].Value.Trim();
                pBot = v.EndsWith("em", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(v[..^2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var emB)
                    ? emB * pEmBase
                    : TryParseLength(v) is { } absB ? absB : pBot;
            }
            var pCollapsed = Math.Max(pTop, io.ps.cellPrevPBottomPt);
            if (pCollapsed > 0) io.ps.lineMarginTop = pCollapsed;
            io.ps.cellPrevPBottomPt = pBot;
        }
    }

    /// <summary>Word mail: a paragraph's stylesheet class (`p.MsoNormal { font-size:11pt; font-family:"Calibri" }`) sizes and faces the runs in its cell — the cell's own markup carries no inline family.</summary>
    private static void ApplyWordMailClassRule(InlineOpenState io, string classes)
    {
        foreach (var cls in classes.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            foreach (var key in new[] { io.tag + "." + cls, "." + cls })
            {
                if (!io.css.TryGetValue(key, out var decl)
                    && (io.docCss is null || !io.docCss.TryGetValue(key, out decl))) continue;
                if (decl.TryGetValue("font-size", out var fs) && TryParseLength(fs) is { } fsPt && fsPt > 0)
                    io.ps.curFontPt = fsPt;
                if (decl.TryGetValue("font-family", out var ff) && FirstFontFamily(ff) is { Length: > 0 } fam)
                    io.ps.curFamily = fam;
                break;
            }
    }

    /// <summary>Word mail: a paragraph's own text-align (or its legacy align attribute) seats its cell's text — the voucher's centred address column.</summary>
    private static void ApplyWordMailParagraphAlign(InlineOpenState io)
    {
        string? al = null;
        if (io.tok.Attributes!.TryGetValue("style", out var st)
            && Regex.Match(st, @"text-align\s*:\s*(left|right|center)", RegexOptions.IgnoreCase) is { Success: true } m)
            al = m.Groups[1].Value;
        else if (io.tok.Attributes.TryGetValue("align", out var at)) al = at;
        if (al is null || ParseAlignAttr(al) is not { } a) return;
        io.ps.alignSet = true; io.ps.cellAlign = a;
    }

    /// <summary>Word mail: a paragraph's `line-height: N%` paces every line it holds at N% of the line's own size (Word's multiple line spacing).</summary>
    private static void ReadWordMailLineHeight(InlineOpenState io)
        => io.ps.curLineHeightPct = WordMailLineHeightPct(io.tok.Attributes!.TryGetValue("style", out var st) ? st : null);

    /// <summary>The percent of a `line-height: N%` declaration, 0 when the style carries none.</summary>
    private static double WordMailLineHeightPct(string? style)
        => style is not null
            && Regex.Match(style, @"(?<![-\w])line-height\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase) is { Success: true } m
            && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 0
            ? pct : 0;
}
