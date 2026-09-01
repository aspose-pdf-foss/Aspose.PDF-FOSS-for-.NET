using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void CollectMetricText(MetricTableState mt, Token tok)
    {
        if (mt.mps.cell is not null && mt.mps.hiddenDepth == 0)
        {
            var ttext = DecodeEntities(tok.Value);
            if (mt.mps.curSeg is not null && mt.mps.whiteDepth == 0)
            {
                var segInk = ttext.AsSpan().Trim().Length;
                if (mt.reportCells && segInk > 0)
                {
                    if (!mt.mps.segInkSeen)
                    {
                        mt.mps.segInkSeen = true;
                        mt.mps.segFs = mt.mps.cell.FontSize; mt.mps.segFace = mt.mps.cell.Face;
                        mt.mps.segFore = mt.mps.cell.Fore;
                    }
                    if (mt.mps.boldDepth > 0 || mt.mps.cell.Bold) mt.mps.segBoldChars += segInk;
                    else mt.mps.segPlainChars += segInk;
                }
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
        if (tag is "td" or "th") CloseCell(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
        else if (tag is "tr") { if (mt.mps.nestDepth == 0) CloseRow(mt.mps, mt.rows, mt.text, mt.reportCells, mt.stdSerif); }
        else if (tag is "table") { if (mt.mps.nestDepth > 0) mt.mps.nestDepth--; }
        else if (tag is "b" or "strong")
        {
            mt.mps.boldDepth = Math.Max(0, mt.mps.boldDepth - 1);
            if (mt.mps.cell is not null) mt.mps.cellBoldMarks.Add((mt.text.Length, mt.mps.boldDepth > 0));
        }
        else if (tag is "div") { CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif); mt.mps.pendingAbsLeftFrac = -1.0; }
        else if (tag is "p" && mt.wrapperStacks && !mt.mps.collapsedGrid && mt.mps.cell is not null)
        {
            // Report cells: the closing paragraph SEGMENT snapshots the
            // typography its spans left active, and carries the UA
            // 1.12 em block margins (collapsed between neighbours).
            if (mt.reportCells && mt.mps.curSeg is not null)
            {
                mt.mps.curSeg.FontSize = mt.mps.segInkSeen ? mt.mps.segFs : mt.mps.cell.FontSize;
                mt.mps.curSeg.Face = mt.mps.segInkSeen ? mt.mps.segFace : mt.mps.cell.Face;
                // bold by MAJORITY of the paragraph's ink (its strong
                // runs against its plain runs); style bold always wins
                mt.mps.curSeg.Bold = mt.mps.cell.Bold || mt.mps.segBoldChars > mt.mps.segPlainChars;
                mt.mps.curSeg.Fore = mt.mps.segInkSeen ? mt.mps.segFore : mt.mps.cell.Fore;
                var pFs = mt.mps.cell.FontSize ?? mt.mps.fontSize;
                if (!mt.mps.curSeg.MarginsExplicit)
                {
                    mt.mps.curSeg.MarginTopPt = UaBlockMarginEm * pFs;
                    mt.mps.curSeg.MarginBottomPt = UaBlockMarginEm * pFs;
                }
                var pMarkers = string.Concat(
                    from Match pm in Regex.Matches(mt.mps.divText.ToString(), "\u0002\\d+\u0003")
                    select pm.Value);
                if (pMarkers.Length > 0)
                {
                    var cleaned = Regex.Replace(mt.mps.divText.ToString(), "\u0002\\d+\u0003", " ");
                    mt.mps.divText.Clear();
                    mt.mps.divText.Append(cleaned);
                    mt.text.Append(pMarkers);
                }
                CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
            }
            // other wrapper flows keep the calibrated blank-line gap
            else if (mt.mps.curSeg is null && mt.text.Length > 0)
                mt.text.Append('\u0001').Append('\u0001');
        }
        else if (tag is "span" && mt.whiteSpans.Count > 0)
        {
            if (mt.whiteSpans.Pop()) mt.mps.whiteDepth = Math.Max(0, mt.mps.whiteDepth - 1);
            // report cells: the span's typography ends here
            if (mt.reportCells && mt.spanSaves.Count > 0 && mt.mps.cell is not null)
                (mt.mps.cell.FontSize, mt.mps.cell.Face, mt.mps.cell.Bold, mt.mps.cell.Fore) = mt.spanSaves.Pop();
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
        if (tok.Attributes is { } tra && tra.TryGetValue("style", out var trst))
        {
            // per-row inline styles (the official-letter dialect
            // sizes and paces every row this way)
            var fsm = Regex.Match(trst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
            if (fsm.Success && TryParseCssFontSize(fsm.Groups[1].Value.Trim(), out var trfs))
                mt.mps.rowFs = trfs;
            var ham = Regex.Match(trst, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (ham.Success)
                mt.mps.rowAlign = ham.Groups[1].Value.ToLowerInvariant() switch
                {
                    "right" => HorizontalAlignment.Right,
                    "center" => HorizontalAlignment.Center,
                    _ => HorizontalAlignment.Left,
                };
            var hm2 = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
            if (hm2.Success) mt.mps.pendingRowH = DtpNum(hm2.Groups[1].Value) * PxPt;
        }
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
            // A face the HTML engine does not resolve keeps the flow
            // default (David and friends draw the UA serif there).
            if (fa.TryGetValue("face", out var ffv)
                && FirstFontFamily(ffv) is { Length: > 0 } ffam
                && (!mt.stdSerif || SourceEngineFaces.Contains(ffam)))
                mt.mps.cell.Face = ffam;
            if (fa.TryGetValue("color", out var fcv)
                && ParseCssColor(fcv.Trim()) is { } fcol)
                mt.mps.cell.Fore = fcol;
            if (fa.TryGetValue("size", out var fsv)
                && TryParseHtmlFontSize(fsv, out var fszPt))
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
    if (mt.stdSerif && mt.mps.cell is not null && tok.Attributes is { } spCls0
        && spCls0.TryGetValue("class", out var spClsV) && spClsV is not null)
        foreach (var sc0 in spClsV.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (mt.css.TryGetValue("." + sc0, out var spRule0))
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, spRule0);
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
    if (mt.reportCells && !tok.IsSelfClosing && mt.mps.cell is not null)
        mt.spanSaves.Push((mt.mps.cell.FontSize, mt.mps.cell.Face, mt.mps.cell.Bold, mt.mps.cell.Fore));
    if (mt.mps.cell is not null && tok.Attributes is { } sa
        && sa.TryGetValue("style", out var sst))
    {
    // quote entities decode BEFORE the property scan — the ';'
    // inside &quot; would otherwise truncate a value mid-entity
    // (font-family: &quot;Arial&quot; parsed as the face '&quot')
    if (sst.IndexOf('&') >= 0)
        sst = sst.Replace("&quot;", "\"").Replace("&#34;", "\"")
                 .Replace("&apos;", "'").Replace("&#39;", "'");
    // WIDTH:Npx; DISPLAY:inline-table — the span fixes its column's
    // content width and grows the line box.
    var wm = Regex.Match(sst, @"width\s*:\s*(\d+(?:\.\d+)?)\s*px", RegexOptions.IgnoreCase);
    if (wm.Success && Regex.IsMatch(sst, @"display\s*:\s*inline-table", RegexOptions.IgnoreCase))
    {
        mt.mps.cell.HasSpan = true;
        mt.mps.cell.SpanW = Math.Max(mt.mps.cell.SpanW, double.Parse(wm.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * PxPt);
    }
    // Inline span typography styles the rest of its cell — the
    // legacy corpus wraps whole cell contents in one styled span.
    var sfm = Regex.Match(sst, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
    if (sfm.Success && FirstFontFamily(sfm.Groups[1].Value) is { Length: > 0 } sfam)
        mt.mps.cell.Face = sfam;
    var ssm = Regex.Match(sst, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
    // font-size: larger is RELATIVE — 1.2 x the cell's current
    // size (13px title → 15.6px = 11.7 pt, measured), so it must
    // beat the keyword table's fixed UA-base mapping.
    if (ssm.Success && ssm.Groups[1].Value.Trim()
            .Equals("larger", StringComparison.OrdinalIgnoreCase))
        mt.mps.cell.FontSize = HtmlLargerStepPt(mt.mps.cell.FontSize ?? mt.mps.fontSize);
    else if (ssm.Success && TryParseCssFontSize(ssm.Groups[1].Value.Trim(), out var sfs))
        mt.mps.cell.FontSize = sfs;
    if (Regex.IsMatch(sst, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase))
        mt.mps.cell.Italic = true;
    if (Regex.IsMatch(sst, @"font-weight\s*:\s*bold", RegexOptions.IgnoreCase))
        mt.mps.cell.Bold = true;
    var scm = Regex.Match(sst, @"(?<![-\w])color\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
    if (scm.Success && ParseCssColor(scm.Groups[1].Value.Trim()) is { } scol
        && (scol.R != 255 || scol.G != 255 || scol.B != 255))
        mt.mps.cell.Fore = scol;
    }
    }

    /// <summary>One arm of the enclosing token loop, verbatim; an
    /// arm-level continue/break became a return.</summary>
    private static void OpenMetricDiv(MetricTableState mt, Token tok)
    {
        // Div-stacked cell content (the .t/.c ladders): each div is
        // one styled line; its classes resolve directly and through
        // the row's descendant rules ('.rc6 .t', '.rc6 div'). The
        // collapsed CLASS grid keeps its calibrated concatenation;
        // the element-rule collapse grid needs its div bands (the
        // green bar + abs image).
        if (mt.wrapperStacks && (!mt.mps.collapsedGrid || mt.elemCollapseGrid) && mt.mps.cell is not null)
        {
            if (tok.IsClose) { CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif); return; }
            CloseSeg(mt.mps, mt.text, mt.reportCells, mt.stdSerif);
            var seg = new MetricDivSeg();
            var segProbe = new MetricCell();
            if (mt.mps.rowClasses is not null)
                foreach (var rcn in mt.mps.rowClasses)
                    if (mt.css.TryGetValue("." + rcn + " div", out var rdivBag))
                        ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, segProbe, rdivBag);
            if (tok.Attributes is { } da && da.TryGetValue("class", out var dcls))
                foreach (var dcn in dcls.Split(' ', StringSplitOptions.RemoveEmptyEntries))
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
            seg.FontSize = segProbe.FontSize;
            seg.Face = segProbe.Face;
            seg.Bold = segProbe.Bold;
            seg.Fore = segProbe.Fore;
            seg.LineBoxPt = segProbe.HeightPt;
            seg.PadLeft = segProbe.PadLeft > 0 ? segProbe.PadLeft : 0;
            seg.Bg = segProbe.Bg;
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
    private static void OpenMetricHeading(MetricTableState mt, string tag)
    {
        // A heading inside a cell styles the rest of the cell: UA
        // bold plus the sheet's own element rule (the order ticket's
        // h1 { font-size: 120% } on the 12 pt base).
        if (mt.stdSerif && mt.mps.cell is not null)
        {
            mt.mps.cell.Bold = true;
            if (mt.css.TryGetValue(tag, out var cellHeadRule))
                ApplyCellClassBag(mt.mps, mt.css, mt.text, mt.reportCells, mt.stdSerif, mt.mps.cell, cellHeadRule);
        }
    }
}
