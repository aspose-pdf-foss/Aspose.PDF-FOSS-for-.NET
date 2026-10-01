using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CollectMetricText(MetricTableState mt, Token tok)
    {
        if (mt.mps.absCapture is { } abs) { abs.Text.Append(DecodeEntities(tok.Value)); return; }
        if (mt.mps.legendCapture is { } legend) { legend.Append(DecodeEntities(tok.Value)); return; }
        if (mt.mps.captionCapture is { } caption) { caption.Append(DecodeEntities(tok.Value)); return; }
        if (mt.mps.cell is not null && mt.mps.hiddenDepth == 0)
        {
            var ttext = DecodeEntities(tok.Value);
            if (mt.mps.curSeg is not null && mt.mps.whiteDepth == 0)
            {
                var segInk = ttext.AsSpan().Trim().Length;
                // Capture the segment's typography at its FIRST ink, whatever the
                // dialect: an inline <font face>/<span> inside the div has opened by
                // now but its close tag restores the cell face before the div closes,
                // so reading the face at CloseSeg would see the flow default. (A
                // `<div><span><font face="Calibri">` cell drew Times without this.)
                if (segInk > 0 && !mt.mps.segInkSeen)
                {
                    mt.mps.segInkSeen = true;
                    mt.mps.segFs = mt.mps.cell.FontSize; mt.mps.segFace = mt.mps.cell.Face;
                    mt.mps.segFore = mt.mps.cell.Fore; mt.mps.segItalic = mt.mps.cell.Italic;
                }
                if (segInk > 0 && !mt.mps.segLineInkSeen)
                {
                    mt.mps.segLineInkSeen = true;
                    mt.mps.segLineFs = mt.mps.cell.FontSize;
                }
                if (mt.reportCells && segInk > 0)
                {
                    if (mt.mps.boldDepth > 0 || mt.mps.cell.Bold) mt.mps.segBoldChars += segInk;
                    else mt.mps.segPlainChars += segInk;
                }
                // (a UA cell's ink is marked with the style it draws in - see the run model)
                if (mt.stdSerif && !mt.reportCells) MarkMetricRun(mt);
                mt.mps.divText.Append(ttext);
                return;
            }
            if (mt.mps.whiteDepth > 0)
                // white-on-white ink keeps its advance: an ideograph
                // becomes an ideographic space, latin a plain space
                foreach (var ch in ttext)
                    mt.text.Append(char.IsWhiteSpace(ch) ? ch : ch >= '⺀' ? '　' : ' ');
            else
            {
                var ink = ttext.AsSpan().Trim().Length;
                if (ink > 0) { if (mt.floatDepth > 0) mt.mps.cell.InkInFloat = true; else mt.mps.cell.InkOutsideFloat = true; }
                if (ink > 0 && mt.stdSerif && !mt.mps.firstInkSeen)
                {
                    mt.mps.firstInkSeen = true;
                    mt.mps.firstInkFs = mt.mps.cell.FontSize; mt.mps.firstInkFace = mt.mps.cell.Face; mt.mps.firstInkFore = mt.mps.cell.Fore;
                }
                if (mt.reportCells && ink > 0)
                {
                    mt.mps.cell.AltTextOnly = false;   // real ink joined the alt
                    if (!mt.mps.leadSeen)
                    {
                        mt.mps.leadSeen = true;
                        mt.mps.leadFs = mt.mps.cell.FontSize; mt.mps.leadFace = mt.mps.cell.Face;
                        mt.mps.leadFore = mt.mps.cell.Fore;
                        mt.mps.leadBold = mt.mps.boldDepth > 0 || mt.mps.cell.Bold;
                    }
                    if (mt.mps.boldDepth > 0 || mt.mps.cell.Bold) mt.mps.cellBoldChars += ink;
                    else mt.mps.cellPlainChars += ink;
                }
                if (mt.stdSerif && !mt.reportCells) MarkMetricRun(mt);
                mt.text.Append(ttext);
                if (mt.mps.sizedSegs.Count == 0 || mt.mps.sizedSegs[^1].Fs != mt.mps.cell.FontSize)
                    mt.mps.sizedSegs.Add((new StringBuilder(), mt.mps.cell.FontSize));
                mt.mps.sizedSegs[^1].Sb.Append(ttext);
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CloseMetricTag(MetricTableState mt, string tag)
    {
        if (tag is "caption")
        {
            if (mt.mps.captionCapture is { } caption) mt.captionText = CollapseWs(caption.ToString()).Trim();
            mt.mps.captionCapture = null;
        }
        else if (tag is "legend")
        {
            if (mt.mps.legendCapture is { } legend && mt.mps.cell is not null)
                mt.mps.cell.LegendText = CollapseWs(legend.ToString()).Trim();
            mt.mps.legendCapture = null;
        }
        else if (tag is "td" or "th") CloseCell(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
        else if (tag is "tr") { if (mt.mps.nestDepth == 0) CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif); }
        else if (tag is "table") { if (mt.mps.nestDepth > 0) mt.mps.nestDepth--; }
        else if (tag is "font")
        {
            // (a UA cell: the closing font tag restores the typography it replaced - the text after
            // it sets in the cell's own, and the cell keeps what its first ink saw)
            if (mt.stdSerif && mt.mps.cell is not null && mt.mps.fontStack.Count > 0)
            {
                var (fs, face, fore) = mt.mps.fontStack.Pop();
                mt.mps.cell.FontSize = fs; mt.mps.cell.Face = face; mt.mps.cell.Fore = fore;
            }
        }
        else if (tag is "b" or "strong")
        {
            mt.mps.boldDepth = Math.Max(0, mt.mps.boldDepth - 1);
            if (mt.stdSerif && mt.mps.cell is not null && mt.mps.strongSaves.Count > 0) mt.mps.cell.Fore = mt.mps.strongSaves.Pop();
            if (mt.mps.cell is not null) mt.mps.cellBoldMarks.Add((mt.text.Length - mt.mps.textMarkCount, mt.mps.boldDepth > 0));
        }
        else if (tag is "i" or "em") CloseMetricEmphasis(mt);
        else if (tag is "div") CloseMetricDiv(mt);
        else if (tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" && mt.mps.uaHeadingBands > 0)
        {
            mt.mps.uaHeadingBands--;
            CloseMetricDiv(mt);
        }
        else CloseMetricParagraphAndSpanTag(mt, tag);
    }

    /// <summary>A UA cell's paragraph that closed with nothing in it - the Words export's
    /// `<p style="margin:0pt"></p>` after every nested grid - holds no line box (probed on the
    /// Words letter: the block under a grid stands one nbsp row under it, not three lines).</summary>
    private static bool EmptyUaParagraph(MetricTableState mt)
        => mt.stdSerif && mt.mps.paraOpenTextLen == mt.text.Length
            // (an empty paragraph with its UA margins still spaces its neighbours - the calibrated gap)
            && (mt.mps.paraZeroMargin || (mt.text.Length > 0 && mt.text[^1] == '\u0003'));

    /// <summary>A UA cell's emphasis ends where its tag closes; the other flows keep the whole-cell flag.</summary>
    private static void CloseMetricEmphasis(MetricTableState mt)
    {
        mt.mps.italicDepth = Math.Max(0, mt.mps.italicDepth - 1);
        if (mt.stdSerif && mt.mps.cell is not null && mt.mps.italicDepth == 0) mt.mps.cell.Italic = false;
    }

    /// <summary>A div closes: its segment, its own box, and the block style frame it opened - text
    /// that follows it stays in the enclosing block's style.</summary>
    private static void CloseMetricDiv(MetricTableState mt)
    {
        if (mt.mps.absCapture is not null) { CloseMetricAbsText(mt); return; }
        CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif); mt.mps.pendingAbsLeftFrac = -1.0;
        // (the div's own box closes with it)
        if (mt.wrapperStacks && (!mt.mps.collapsedGrid || mt.elemCollapseGrid) && mt.mps.cell is not null
            && mt.mps.divBoxStack.Count > 0)
        {
            var hadBox = mt.mps.divBoxStack[^1];
            mt.mps.divBoxStack.RemoveAt(mt.mps.divBoxStack.Count - 1);
            if (hadBox) (mt.mps.cell.DivSegs ??= new List<MetricDivSeg>()).Add(new MetricDivSeg { BoxClose = true, EmptyBlock = true });
        }
        if (mt.stdSerif && mt.mps.divStyleStack.Count > 0)
        {
            mt.mps.divStyleStack.RemoveAt(mt.mps.divStyleStack.Count - 1);
            if (mt.mps.divStyleStack.Count > 0 && mt.mps.cell is not null)
                mt.mps.curSeg = InheritedUaBlockSeg(mt.mps.divStyleStack[^1]);
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricTable(MetricTableState mt)
    {
        // a table nested inside an open cell merges its cells into
        // the OUTER row (the letter's item list sits beside its
        // label); an empty container cell is discarded, but its
        // COLSPAN carries over to the first merged cell so the
        // columns stay aligned under the outer grid.
        if (mt.mps.cell is not null && IsAllWhitespace(mt.text))
        {
            if (mt.mps.cell.ColSpan > 1) mt.mps.pendingNestSpan = mt.mps.cell.ColSpan;
            mt.mps.cell = null; mt.text.Clear(); mt.mps.cellBoldMarks.Clear(); mt.mps.sizedSegs.Clear();
            mt.mps.runStyles.Clear(); mt.mps.pendingRunPadLeft = 0; mt.mps.textMarkCount = 0;
        }
        else CloseCell(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
        mt.mps.nestDepth++;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricRow(MetricTableState mt, Token tok)
    {
        if (mt.mps.nestDepth > 0) return;         // nested rows merge into the outer row
        CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif);
        mt.mps.row = new List<MetricCell>();
        if (tok.Attributes is { } trba && trba.TryGetValue("bgcolor", out var trbg)
            && AttrColor(trbg) is { } trbgc)
            mt.mps.rowBg = trbgc;
        // a row's align= attribute aligns every cell of the row, and its
        // height= attribute (pixels) paces the row like a cell's would
        if (tok.Attributes is { } trAttr)
        {
            if (trAttr.TryGetValue("align", out var trAl))
                mt.mps.rowAlign = trAl.Trim().ToLowerInvariant() switch
                {
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
            // …and its valign= attribute seats them in the row band the way a class
            // vertical-align does (measured: the report's `valign="top"` rows hold their
            // nested grids at the band top, where centring dropped the titles 11.7).
            if (mt.stdSerif && trAttr.TryGetValue("valign", out var trVa))
            {
                if (trVa.Trim().Equals("top", StringComparison.OrdinalIgnoreCase)) mt.mps.rowVTop = true;
                else if (trVa.Trim().Equals("bottom", StringComparison.OrdinalIgnoreCase)) mt.mps.rowVBottom = true;
            }
            if (trAttr.TryGetValue("height", out var trH)
                && double.TryParse(trH.Trim().TrimEnd('p', 'x'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var trHpx)
                && trHpx * PxPt > mt.mps.pendingRowH)
            {
                mt.mps.pendingRowH = trHpx * PxPt;
                mt.mps.pendingRowHAttr = true;
            }
        }
        // tr class skins: inheritable typography becomes the row
        // default, height paces the row, and `.cls td` descendant
        // bags queue for every cell of the row.
        if (mt.wrapperStacks && tok.Attributes is { } trka
            && trka.TryGetValue("class", out var trkc))
            foreach (var tc in trkc.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                (mt.mps.rowClasses ??= new List<string>()).Add(tc);
                if (mt.css.TryGetValue("." + tc, out var trBag))
                {
                    var probe = new MetricCell();
                    ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, probe, trBag);
                    if (probe.FontSize is { } pf) { mt.mps.rowFs = pf; mt.mps.rowFsFromClass = true; }
                    if (probe.Face is { } pfa) mt.mps.rowFace = pfa;
                    if (probe.Bold) mt.mps.rowBold = true;
                    if (probe.Fore is { } pfo) mt.mps.rowFore = pfo;
                    // a row class's background tints the row like a
                    // bgcolor attribute (`tr.head { background-color }`)
                    if (probe.Bg is { } pbg) mt.mps.rowBg = pbg;
                    if (probe.VAlignTop) mt.mps.rowVTop = true;
                    if (probe.VAlignBottom) mt.mps.rowVBottom = true;
                    if (trBag.ContainsKey("text-align")) mt.mps.rowAlign = probe.Align;
                    if (trBag.TryGetValue("height", out var trkh))
                    {
                        var hm3 = Regex.Match(trkh, @"([\d.]+)\s*px");
                        if (hm3.Success)
                        {
                            mt.mps.pendingRowH = DtpNum(hm3.Groups[1].Value) * PxPt;
                            mt.mps.pendingRowHExact = true;
                        }
                    }
                }
                if (mt.css.TryGetValue("." + tc + " td", out var tdBag))
                    (mt.mps.rowTdBags ??= new List<Dictionary<string, string>>()).Add(tdBag);
            }
        ApplyMetricRowStyleAttribute(mt, tok);
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricFont(MetricTableState mt, Token tok, string tag)
    {
        // A <font> tag styles the rest of its cell — face, color, and the
        // legacy 1..7 size ladder (the expected render applies the tag's
        // attributes to contained children, self-closing form included).
        if (mt.mps.cell is not null && tok.Attributes is { } fa)
        {
            // (a UA cell's font tag styles what it CONTAINS: its close restores what it replaced)
            if (mt.stdSerif && !tok.IsSelfClosing)
                mt.mps.fontStack.Push((mt.mps.cell.FontSize, mt.mps.cell.Face, mt.mps.cell.Fore));
            // (the pt form's `<font class>` wears its class rule - the title band's
            //  `.Form-table-title-text { Tahoma; 8pt; bold }` - probed: the band's text is 8 pt bold)
            if (mt.stdSerif && mt.mps.ptFormCells && fa.TryGetValue("class", out var fClsV) && fClsV is not null)
                foreach (var fc0 in fClsV.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    if (mt.css.TryGetValue("." + fc0, out var fRule0) || mt.css.TryGetValue("font." + fc0, out fRule0))
                        ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, fRule0, inlineBag: true);
            // A face the HTML engine does not resolve keeps the flow
            // default (David and friends draw the UA serif there).
            if (fa.TryGetValue("face", out var ffv)
                && FirstFontFamily(ffv) is { Length: > 0 } ffam
                && (!mt.stdSerif || SourceEngineFaces.Contains(ffam)))
            {
                mt.mps.cell.Face = ffam;
                // (a segment the line break opened but no text has reached yet takes the face too:
                // the safety data sheet's `<br></font><font face="Arial Narrow">` title line)
                if (mt.stdSerif && mt.mps.curSeg is { } faceSeg && faceSeg.Text.Length == 0 && mt.mps.divText.Length == 0)
                    faceSeg.Face = ffam;
            }
            if (fa.TryGetValue("color", out var fcv)
                && ParseCssColor(fcv.Trim()) is { } fcol)
                mt.mps.cell.Fore = fcol;
            if (fa.TryGetValue("size", out var fsv)
                && TryParseHtmlFontSize(fsv) is { } fszPt)
            {
                mt.mps.cell.FontSize = fszPt;
                mt.mps.cell.FontTagSized = true;
            }
            // an inline style on the font tag sizes the cell in points
            // (`<font style="FONT-SIZE: 14pt">` — the RTL grid's dates)
            if (fa.TryGetValue("style", out var fstv)
                && Regex.Match(fstv, @"font-size\s*:\s*([\d.]+)\s*pt",
                    RegexOptions.IgnoreCase) is { Success: true } fptM)
            {
                mt.mps.cell.FontSize = double.Parse(fptM.Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                mt.mps.cell.FontTagSized = true;
            }
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricAnchor(MetricTableState mt, Token tok)
    {
        // The anchor's colour — its inline style, else the sheet's `a`
        // rule — inks its text; like <font color> it styles the rest
        // of its cell (cells wrap their whole content in one <a>).
        if (mt.mps.cell is not null)
        {
            Color? aFore = null;
            if (tok.Attributes is { } aatt && aatt.TryGetValue("style", out var ast)
                && Regex.Match(ast, @"(?<![-\w])color\s*:\s*([^;]+)",
                    RegexOptions.IgnoreCase) is { Success: true } astm)
                aFore = ParseCssColor(astm.Groups[1].Value.Trim());
            aFore ??= mt.rmtAnchorColor;
            if (aFore is not null) mt.mps.cell.Fore = aFore;
            if (tok.Attributes is { } aatt2
                && aatt2.TryGetValue("href", out var ahref)
                && !string.IsNullOrEmpty(ahref))
                mt.mps.cell.LinkUrl = ahref;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricSpan(MetricTableState mt, Token tok)
    {
    // The sheet's class rules style the span's cell (the
    // .firm { font-size: 400% } masthead on the 12 pt base).
    // …and a span the report export lays out as the cell itself (`display: table-cell`)
    // gives the cell its BOX too: paddings, side borders, fill and alignment (measured on
    // the report: the 2 pt padded paragraph cells, the AliceBlue footer cells, the
    // 1 pt rules over and under a section label).
    var spStV = tok.Attributes is { } spSt0 && spSt0.TryGetValue("style", out var spSt1) ? spSt1 : null;
    var spanIsCellBox = spStV is not null && Regex.IsMatch(spStV, @"display\s*:\s*table-cell", RegexOptions.IgnoreCase);
    var spanFsBefore = mt.mps.cell?.FontSize;
    var spanInkBefore = mt.mps.firstInkSeen;
    // (the typography the span's close restores is the cell's BEFORE the span's class dressed
    // it - measured on the lab report: a 14 pt `span.header` before a grid left the grid at 14;
    // a span that IS the cell box keeps its class on the cell, as before)
    var spanRestore = mt.mps.cell is null ? default : (mt.mps.cell.FontSize, mt.mps.cell.Face, mt.mps.cell.Bold, mt.mps.cell.Fore);
    if (mt.stdSerif && mt.mps.cell is not null && tok.Attributes is { } spCls0
        && spCls0.TryGetValue("class", out var spClsV) && spClsV is not null)
        foreach (var sc0 in spClsV.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            // (…keyed `.cls` or `span.cls` - the lab report's `span.header { bold; 14pt }`)
            if (mt.css.TryGetValue("." + sc0, out var spRule0) || mt.css.TryGetValue("span." + sc0, out spRule0))
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, spRule0, inlineBag: !spanIsCellBox);
    if ((mt.reportCells || mt.stdSerif) && !tok.IsSelfClosing && mt.mps.cell is not null)
        mt.spanSaves.Push(spanIsCellBox ? (mt.mps.cell.FontSize, mt.mps.cell.Face, mt.mps.cell.Bold, mt.mps.cell.Fore) : spanRestore);
    // …and its inline ABSOLUTE width when that is the LAST width declared, as the
    // cascade reads it (the report footer's `width:100%; … width: 74.46mm` cells
    // right-align their text in that box, not in their text's own extent; a bare
    // `width:100%` table-cell span just fills its cell and sizes nothing).
    if (mt.stdSerif && spanIsCellBox && mt.mps.cell is not null
        && Regex.Matches(spStV!, @"(?<![-\w])width\s*:\s*[^;]+", RegexOptions.IgnoreCase) is { Count: > 0 } spWidths
        && Regex.IsMatch(spWidths[^1].Value, @"[\d.]\s*(mm|cm|in|pt|px)\s*$", RegexOptions.IgnoreCase))
        ApplyMetricCellStyleSize(mt.mps.cell, spWidths[^1].Value);
    // The pt form's INLINE-BLOCK span with an absolute width holds its cell open at that width
    // plus the span class's own padding (probed: a 65 px span in a padding-left 5px class floors
    // its column at 48.75 + 3.75 before the cell's own pads).
    if (mt.stdSerif && mt.mps.ptFormCells && mt.mps.cell is not null && spStV is not null
        && Regex.IsMatch(spStV, @"display\s*:\s*inline-block", RegexOptions.IgnoreCase)
        && Regex.Match(spStV, @"(?<![-\w])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase) is { Success: true } ibW
        && double.TryParse(ibW.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ibWpx))
    {
        var spanPad = 0.0;
        if (tok.Attributes is { } ibAttrs && ibAttrs.TryGetValue("class", out var ibCls) && ibCls is not null)
            foreach (var cn in ibCls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (mt.css.TryGetValue("." + cn, out var ibRule))
                {
                    if (ibRule.TryGetValue("padding-left", out var pl) && TryParseLength(pl.Trim()) is { } plPt) spanPad += plPt;
                    if (ibRule.TryGetValue("padding-right", out var pr) && TryParseLength(pr.Trim()) is { } prPt) spanPad += prPt;
                }
        mt.mps.cell.MinWidthPt = Math.Max(mt.mps.cell.MinWidthPt, ibWpx * PxPt + spanPad);
    }
    var sWhite = false;
    if (tok.Attributes is { } sa0 && sa0.TryGetValue("style", out var sst0)
        && Regex.IsMatch(sst0, @"color\s*:\s*(white|#fff(?:fff)?)\b", RegexOptions.IgnoreCase))
    {
        // White ink over an UNFILLED cell is invisible — it keeps its
        // advance as spaces (the official-letter dialect). Over a
        // bgcolor-filled cell/row/table it is REAL ink and draws white.
        if (mt.mps.cell is not null
            && (mt.mps.cell.Bg is not null || mt.mps.rowBg is not null || mt.mps.tableBg is not null))
            mt.mps.cell.Fore = Color.FromArgb(255, 255, 255);
        else
        {
            sWhite = true;
            mt.mps.whiteDepth++;
        }
    }
    if (!tok.IsSelfClosing) mt.whiteSpans.Push(sWhite);
    ApplyMetricSpanStyleAttribute(mt, tok);
    // (a span sizing the cell before its first ink stands round the cell's text like a sized
    // font tag: the quirks line-height quirk pitches the row on that size alone - measured on
    // the enterprise summary: `<td><span class=label>` Arial 9 rows pitch 10.5, no 13.5 strut)
    if (mt.stdSerif && mt.mps.cell is not null && !spanInkBefore && mt.mps.cell.FontSize != spanFsBefore)
        mt.mps.cell.InlineSizedLead = true;
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricDiv(MetricTableState mt, Token tok)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1")
            Console.Error.WriteLine($"[divopen] cell={(mt.mps.cell is not null)} curSeg={(mt.mps.curSeg is not null)} stacks={mt.wrapperStacks} collapsed={mt.mps.collapsedGrid} abs={(mt.mps.absCapture is not null)} style='{(tok.Attributes is { } da0 && da0.TryGetValue("style", out var ds0) ? ds0 : "")}'");
        if (mt.mps.absCapture is not null) { mt.mps.absDepth++; return; }
        if (mt.mps.uaFormCells && mt.mps.cell is not null && TryOpenMetricAbsText(mt, tok)) return;
        // Div-stacked cell content (the .t/.c ladders): each div is
        // one styled line; its classes resolve directly and through
        // the row's descendant rules ('.rc6 .t', '.rc6 div'). The
        // collapsed CLASS grid keeps its calibrated concatenation;
        // the element-rule collapse grid needs its div bands (the
        // green bar + abs image).
        if (mt.wrapperStacks && (!mt.mps.collapsedGrid || mt.elemCollapseGrid) && mt.mps.cell is not null)
        {
            if (tok.IsClose) { CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif); return; }
            // (a UA cell's text before its first block is a band of its own, in source order)
            if (mt.stdSerif && !mt.mps.uaBlockCells && mt.mps.curSeg is null) FlushUaCellText(mt.mps, mt.text, mt.reportCells);
            CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
            // (a nested block inherits the typography, alignment and side insets of the block it stands in)
            var seg = mt.stdSerif && mt.mps.divStyleStack.Count > 0 ? InheritedUaBlockSeg(mt.mps.divStyleStack[^1]) : new MetricDivSeg();
            var segProbe = new MetricCell();
            if (mt.mps.rowClasses is not null)
                foreach (var rcn in mt.mps.rowClasses)
                    if (mt.css.TryGetValue("." + rcn + " div", out var rdivBag))
                        ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, segProbe, rdivBag);
            if (tok.Attributes is { } da && da.TryGetValue("class", out var dcls))
            {
                seg.Classes = dcls.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var dcn in seg.Classes)
                {
                    if (mt.css.TryGetValue("." + dcn, out var dBag))
                        ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, segProbe, dBag);
                    if (mt.mps.rowClasses is not null)
                        foreach (var rcn in mt.mps.rowClasses)
                            if (mt.css.TryGetValue("." + rcn + " ." + dcn, out var rdBag))
                                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, segProbe, rdBag);
                    if (mt.css.TryGetValue("." + dcn, out var dbb)
                        && dbb.ContainsKey("border-bottom"))
                        seg.BorderBottom = true;
                }
            }
            seg.FontSize = segProbe.FontSize ?? seg.FontSize;
            seg.Face = segProbe.Face ?? seg.Face;
            seg.Bold |= segProbe.Bold;
            seg.Fore = segProbe.Fore ?? seg.Fore;
            seg.LineBoxPt = segProbe.HeightPt;
            seg.BorderBottomPt = segProbe.BorderBottomW;
            seg.PadLeft += segProbe.PadLeft > 0 ? segProbe.PadLeft : 0;
            seg.Bg = segProbe.Bg;
            // The div's own inline box (a UA block cell): its width plus horizontal paddings is
            // the box, its vertical paddings are spent round the blocks it holds, its background
            // fills it, its margins are a block's; it closes with the div.
            var boxStated = false;
            if (mt.mps.uaBlockCells && tok.Attributes is { } bxA && bxA.TryGetValue("style", out var bxSt) && bxSt is not null
                && Regex.Match(bxSt, @"(?<![-\w])width\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } bxW
                && TryParseLength(bxW.Groups[1].Value.Trim()) is { } bxWPt && bxWPt > 0)
            {
                var bxEm = mt.mps.cell.FontSize ?? mt.mps.fontSize;
                var (bpT, bpR, bpB, bpL) = CssPaddingSidesPt(bxSt, bxEm);
                seg.BoxOpen = true;
                seg.BoxWidthPt = bxWPt + bpL + bpR;
                seg.BoxPadTopPt = bpT; seg.BoxPadRightPt = bpR; seg.BoxPadBottomPt = bpB; seg.BoxPadLeftPt = bpL;
                if (Regex.Match(bxSt, @"(?<![-\w])background(?:-color)?\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } bxBg
                    && ParseCssColor(bxBg.Groups[1].Value.Trim()) is { } bxBgC)
                    seg.Bg = bxBgC;
                ReadUaBlockStyle(seg, tok, bxEm);
                seg.MarginTopPt = seg.MarginTopStatedPt ?? 0;
                seg.MarginBottomPt = seg.MarginBottomStatedPt ?? 0;
                seg.MarginsExplicit = true;
                boxStated = true;
            }
            // A UA cell's block carries its own inline style - size, weight, style, alignment, side
            // insets and margins (probed on the cheque: a `font-size: 11px` div sizes the divs it
            // holds, `padding-left: 100px` insets them, `text-align: right; margin-right: 55px` seats
            // them 41.25 in from the cell's right edge, `margin-top: -43px` pulls the block up).
            if (mt.stdSerif && !boxStated) ReadUaCellBlockStyle(seg, tok, mt.mps.cell.FontSize ?? mt.mps.fontSize);
            if (mt.stdSerif) mt.mps.divStyleStack.Add(seg);
            mt.mps.divBoxStack.Add(boxStated);
            // An absolutely positioned div (left:N%) is OUT of the
            // band flow — its image draws at the offset instead.
            if (tok.Attributes is { } absDa
                && absDa.TryGetValue("style", out var absSt)
                && Regex.IsMatch(absSt, @"position\s*:\s*absolute",
                    RegexOptions.IgnoreCase)
                && Regex.Match(absSt, @"left\s*:\s*([\d.]+)\s*%",
                    RegexOptions.IgnoreCase) is { Success: true } absLm
                && double.TryParse(absLm.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var absLv))
                mt.mps.pendingAbsLeftFrac = absLv / 100.0;
            mt.mps.curSeg = seg;
        }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricHeading(MetricTableState mt, Token tok, string tag)
    {
        // UA block cells: the heading is a block segment of its own - bold, the UA size of its
        // level unless its style states one, closed like a paragraph.
        if (mt.mps.uaBlockCells && mt.reportCells && !mt.mps.collapsedGrid && mt.mps.cell is not null && !tok.IsClose)
        {
            CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
            FlushUaBlockGridMarkers(mt.mps, mt.text);
            mt.mps.curSeg = new MetricDivSeg
            {
                Bold = true,
                FontSize = UaHeadingEm(tag).sizeEm * (mt.mps.cell.FontSize ?? mt.mps.fontSize),
            };
            ReadUaBlockStyle(mt.mps.curSeg, tok, mt.mps.cell.FontSize ?? mt.mps.fontSize);
            return;
        }
        // A heading in a plain UA cell is a block of its own: a band at the UA size and weight of
        // its level inside its UA margins, its inline style read as a div's (measured on the lab
        // report: `<td><h1>` draws 24 pt bold with the first text 128.7 down; the case report's
        // `<td><h2>` 18 pt).
        // (a cell sizing its own text keeps the calibrated heading model - bold in the cell's size:
        // the financial statement's `<td 11pt><h2><span 11pt>` heads draw 11 bold in their cell
        // lines; the UA scale is the UNSIZED cell's)
        // (…and a heading the SHEET sizes keeps the calibrated model below: the order ticket's
        // `h1 { font-size: 120% }` draws 14.4 on the cell's own line)
        if (mt.stdSerif && !mt.reportCells && !mt.mps.uaBlockCells && mt.wrapperStacks && !mt.mps.collapsedGrid
            && mt.mps.cell is { FontSize: null } && !tok.IsClose && mt.mps.absCapture is null
            && !(mt.css.TryGetValue(tag, out var sheetHead) && (sheetHead.ContainsKey("font-size") || sheetHead.ContainsKey("font"))))
        {
            OpenUaCellHeadingBand(mt, tok, tag);
            return;
        }
        // A heading inside a cell styles the rest of the cell: UA
        // bold plus the sheet's own element rule (the order ticket's
        // h1 { font-size: 120% } on the 12 pt base).
        if (mt.stdSerif && mt.mps.cell is not null)
        {
            mt.mps.cell.Bold = true;
            if (mt.css.TryGetValue(tag, out var cellHeadRule))
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, cellHeadRule);
            // (...and the sheet's descendant rules through the blocks open round the grid: the
            //  e-mail cards' `.header_sub h2 { padding: 20px; font-size: 18px; color }` size, colour
            //  and pad the collapsed grid's heading cells)
            if (!tok.IsClose && AncestorClassRuleDecls(mt.css, mt.mps.divStyleStack, tag, mt.mps.hostBlockClasses) is { } cellAncDecls)
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, cellAncDecls);
        }
    }
}
