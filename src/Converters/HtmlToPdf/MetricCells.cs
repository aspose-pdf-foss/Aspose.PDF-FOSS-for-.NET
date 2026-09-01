using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The table parser's working set, lifted out of BuildTableFromHtml: each
// method takes the parse state, the column model and the settled dialect
// scalars it reads. Bodies are verbatim.
    private static void CloseSeg(MetricParseState mps, StringBuilder text, bool reportCells, bool stdSerif)
    {
        mps.segBoldChars = 0; mps.segPlainChars = 0; mps.segInkSeen = false;
        mps.segFs = null; mps.segFace = null; mps.segFore = null;
        if (mps.curSeg is null || mps.cell is null) { mps.curSeg = null; mps.divText.Clear(); return; }
        // Sub-table markers belong to the CELL (CloseCell lifts them into
        // SubTables) — never to a segment's drawn text.
        var segRaw = mps.divText.ToString();
        if (segRaw.IndexOf('\u0002') >= 0)
        {
            var segMarkers = string.Concat(
                from Match sm in Regex.Matches(segRaw, "\u0002\\d+\u0003")
                select sm.Value);
            segRaw = Regex.Replace(segRaw, "\u0002\\d+\u0003", " ");
            text.Append(segMarkers);
        }
        mps.curSeg.Text = CollapseWs(segRaw).Trim(' ').Trim('\u0001').Trim(' ');
        (mps.cell.DivSegs ??= new List<MetricDivSeg>()).Add(mps.curSeg);
        mps.curSeg = null;
        mps.divText.Clear();
    }

    private static void CloseCell(MetricParseState mps, StringBuilder text, bool reportCells, bool stdSerif)
    {
        CloseSeg(mps, text, reportCells, stdSerif);
        // report cells: a p-less cell wholly wrapped in b/strong is bold;
        // mixed-run cells stay in the body face
        if (reportCells && mps.cell is not null && !mps.cell.Bold
            && mps.cellBoldChars > 0 && mps.cellPlainChars == 0)
            mps.cell.Bold = true;
        mps.cellBoldChars = 0; mps.cellPlainChars = 0;
        mps.leadSeen = false; mps.leadFs = null; mps.leadFace = null; mps.leadFore = null; mps.leadBold = false;
        if (mps.cell is null) return;
        mps.cell.Text = CollapseWs(text.ToString());
        // Interleaved cell flow: a nested grid that comes BEFORE text ink
        // keeps its source position — the cell draws text runs (bold per
        // run) and grids in order. Cells whose grids all trail the text
        // keep the calibrated stacked draw (text lines, then grids).
        if (mps.nestedTables is not null && !reportCells)
        {
            var raw = text.ToString();
            var mms = Regex.Matches(raw, "\u0002(\\d+)\u0003");
            var inkAfterMarker = false;
            if (mms.Count > 0)
            {
                var afterFirst = raw[(mms[0].Index + mms[0].Length)..];
                afterFirst = Regex.Replace(afterFirst, "\u0002\\d+\u0003", "");
                foreach (var ch in afterFirst)
                    if (!char.IsWhiteSpace(ch) && ch is not ('\u0001' or '\u00A0'))
                    { inkAfterMarker = true; break; }
            }
            if (inkAfterMarker)
            {
                bool BoldAt(int at)
                {
                    var on = false;
                    foreach (var (mp, mo) in mps.cellBoldMarks)
                    { if (mp > at) break; on = mo; }
                    return on;
                }
                var flow = new List<(string? TableHtml, string Text, bool Bold)>();
                void AddRuns(int from, int to)
                {
                    var runStart = from;
                    var runBold = BoldAt(from);
                    for (var ci = from + 1; ci <= to; ci++)
                    {
                        var b2 = ci < to ? BoldAt(ci) : !runBold;
                        if (b2 == runBold && ci < to) continue;
                        var chunk = CollapseWs(raw[runStart..ci]);
                        if (chunk.Length > 0) flow.Add((null, chunk, runBold));
                        runStart = ci; runBold = b2;
                    }
                }
                var fpos = 0;
                foreach (Match fmm in mms)
                {
                    if (fmm.Index > fpos) AddRuns(fpos, fmm.Index);
                    if (int.TryParse(fmm.Groups[1].Value, out var fti)
                        && fti < mps.nestedTables.Count)
                        flow.Add((mps.nestedTables[fti], "", false));
                    fpos = fmm.Index + fmm.Length;
                }
                if (fpos < raw.Length) AddRuns(fpos, raw.Length);
                mps.cell.Flow = flow;
                mps.cell.Bold = false;   // bold lives on the runs now
            }
        }
        // Nested-table markers lift out of the text into the cell's grids.
        if (mps.nestedTables is not null && mps.cell.Text.IndexOf('\u0002') >= 0)
        {
            foreach (Match nm in Regex.Matches(mps.cell.Text, "\u0002(\\d+)\u0003"))
                if (int.TryParse(nm.Groups[1].Value, out var nti) && nti < mps.nestedTables.Count)
                    (mps.cell.SubTables ??= new List<string>()).Add(mps.nestedTables[nti]);
            mps.cell.Text = Regex.Replace(mps.cell.Text, "\u0002\\d+\u0003", " ");
            mps.cell.Text = CollapseWs(mps.cell.Text);
        }
        // A container cell's whitespace-only paragraph segments (a
        // tellfriend <p> whose img is dead and whose text is one &nbsp;)
        // hold no band — dropping them keeps the nested grids at the top.
        if (reportCells && mps.cell.SubTables is { Count: > 0 }
            && mps.cell.DivSegs is { Count: > 0 })
        {
            mps.cell.DivSegs.RemoveAll(sg =>
            {
                foreach (var ch in sg.Text)
                    if (ch is not (' ' or '\u00A0' or '\u0001')) return false;
                return true;
            });
            if (mps.cell.DivSegs.Count == 0) mps.cell.DivSegs = null;
        }
        // A trailing <br> closes the cell's last line — it opens no new one
        // (mid-cell breaks keep their sentinel).
        mps.cell.Text = mps.cell.Text.TrimEnd('\u0001');
        // …but the two orphaned inlines around a block DO each keep a line box,
        // one on each side of the block's own line, so they are added after that
        // trim. Measured: the cell grows by two boxes and
        // its text still shares the row's baseline, because a middle-aligned
        // neighbour centres in exactly the pair this opens.
        if (mps.cell.OrphanInlineBoxes && mps.cell.Text.Length > 0)
            mps.cell.Text = '\u0001' + mps.cell.Text + '\u0001';
        // Two real sizes met on the cell line: keep the per-size segments
        // so the draw can honour them (a single size stays on the flat path).
        if (mps.sizedSegs.Count > 1 && mps.cell.SubTables is not { Count: > 0 })
        {
            var distinctFs = new HashSet<double>();
            foreach (var (sb2, fs2) in mps.sizedSegs)
                if (sb2.ToString().AsSpan().Trim().Length > 0)
                    distinctFs.Add(fs2 ?? 0);
            if (distinctFs.Count > 1)
            {
                mps.cell.SizedRuns = new List<(string, double)>();
                foreach (var (sb2, fs2) in mps.sizedSegs)
                {
                    var st2 = CollapseWs(sb2.ToString());
                    // a whitespace-only segment still advances the pen at
                    // ITS size (the 10 pt space between '23 May' and the
                    // 9 pt parenthetical)
                    if (st2.Length == 0 && sb2.Length > 0) st2 = " ";
                    if (st2.Length > 0)
                        mps.cell.SizedRuns.Add((st2, fs2 ?? 0));
                }
            }
        }
        mps.sizedSegs.Clear();
        text.Clear();
        mps.cellBoldMarks.Clear();
        mps.row!.Add(mps.cell);
        mps.cell = null;
    }

    private static void CloseRow(MetricParseState mps, List<List<MetricCell>> rows, StringBuilder text, bool reportCells, bool stdSerif)
    {
        CloseCell(mps, text, reportCells, stdSerif);
        if (mps.row is { Count: > 0 })
        {
            rows.Add(mps.row);
            mps.rowHeights.Add(mps.pendingRowH);
            mps.rowHeightExact.Add(mps.pendingRowHExact);
            mps.rowSections.Add(mps.curSection);
        }
        mps.row = null;
        mps.pendingRowH = 0;
        mps.pendingRowHExact = false;
        mps.rowFs = null;
        mps.rowAlign = null;
        mps.rowBg = null;
        mps.rowFace = null;
        mps.rowFsFromClass = false;
        mps.rowBold = false;
        mps.rowFore = null;
        mps.rowVTop = false;
        mps.rowVBottom = false;
        mps.rowTdBags = null;
        mps.rowClasses = null;
    }

    private static void ApplyCellClassBag(MetricParseState mps, IReadOnlyDictionary<string, Dictionary<string, string>> css, StringBuilder text, bool reportCells, bool stdSerif, MetricCell mc, IReadOnlyDictionary<string, string> bag)
    {
        foreach (var (bProp, bVal) in bag)
            switch (bProp.ToLowerInvariant())
            {
                case "font":
                {
                    var fsh = Regex.Match(bVal, @"([\d.]+)\s*px\s+(.+)$");
                    if (fsh.Success)
                    {
                        mc.FontSize = DtpNum(fsh.Groups[1].Value) * PxPt;
                        // a QUOTED family inside the shorthand is dropped with
                        // the shorthand's grammar (the sheet parser does the
                        // same) — the cell keeps the flow face
                        var shRaw = fsh.Groups[2].Value.TrimStart();
                        if (shRaw.Length > 0 && shRaw[0] != '"' && shRaw[0] != '\''
                            && FirstFontFamily(shRaw) is { Length: > 0 } shFam
                            && WinMetricsFor(shFam) is not null)
                            mc.Face = shFam;
                    }
                    if (bVal.Contains("bold", StringComparison.OrdinalIgnoreCase))
                        mc.Bold = true;
                    if (mc.FontSize is not null) mc.FontFromClass = true;
                    break;
                }
                case "font-size":
                    // a PERCENT size resolves against the cell's current size
                    // (th { font-size: 80% } = 9.6 on the 12 pt base; the
                    // .firm span's 400% = 48)
                    if (bVal.Trim().EndsWith("%", StringComparison.Ordinal)
                        && double.TryParse(bVal.Trim().TrimEnd('%'),
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out var bagPct) && bagPct > 0)
                    { mc.FontSize = (mc.FontSize ?? mps.fontSize) * bagPct / 100.0; mc.FontFromClass = true; }
                    else if (TryParseCssFontSize(bVal.Trim(), out var bagFs))
                    { mc.FontSize = bagFs; mc.FontFromClass = true; }
                    break;
                case "font-family":
                    // Under the UA flow only the faces the HTML engine
                    // resolves apply — 'Century Gothic' falls to the flow
                    // serif.
                    if (FirstFontFamily(bVal) is { Length: > 0 } bagFam
                        && WinMetricsFor(bagFam) is not null
                        && (!stdSerif || SourceEngineFaces.Contains(bagFam)))
                        mc.Face = bagFam;
                    break;
                case "font-weight":
                    if (bVal.Contains("bold", StringComparison.OrdinalIgnoreCase)
                        || (int.TryParse(bVal.Trim(), out var bagFw) && bagFw >= 600))
                        mc.Bold = true;
                    break;
                case "color":
                    if (ParseCssColor(bVal.Trim()) is { } bagFc
                        && (bagFc.R != 0 || bagFc.G != 0 || bagFc.B != 0)) mc.Fore = bagFc;
                    break;
                case "background-color":
                case "background":
                    if (ParseCssColor(bVal.Trim()) is { } bagBg) mc.Bg = bagBg;
                    break;
                case "text-align":
                    mc.Align = bVal.Trim().ToLowerInvariant() switch
                    {
                        "right" => HorizontalAlignment.Right,
                        "center" => HorizontalAlignment.Center,
                        _ => HorizontalAlignment.Left,
                    };
                    break;
                case "vertical-align":
                    // last declaration wins: a cell's own Ab overrides the
                    // row's At (and vice versa)
                    if (bVal.Contains("top", StringComparison.OrdinalIgnoreCase))
                    { mc.VAlignTop = true; mc.VAlignBottom = false; }
                    else if (bVal.Contains("bottom", StringComparison.OrdinalIgnoreCase))
                    { mc.VAlignBottom = true; mc.VAlignTop = false; }
                    break;
                case "width":
                {
                    var bwm2 = Regex.Match(bVal, @"([\d.]+)\s*px");
                    if (bwm2.Success) mc.WidthPx = DtpNum(bwm2.Groups[1].Value) * PxPt;
                    // a class PERCENT width pins its column only when the
                    // table is over-constrained (see the width solve)
                    else if (Regex.Match(bVal, @"([\d.]+)\s*%") is { Success: true } bwPct)
                        mc.ClassWidthPct = DtpNum(bwPct.Groups[1].Value);
                    break;
                }
                case "padding-top":
                {
                    // th { padding-top: 1em } grows the row above its text
                    // (em against the cell's resolved size).
                    var bpt = Regex.Match(bVal, @"([\d.]+)\s*(px|pt|em)");
                    if (bpt.Success)
                    {
                        var bptV = DtpNum(bpt.Groups[1].Value);
                        mc.PadTopPt = bpt.Groups[2].Value.ToLowerInvariant() switch
                        {
                            "px" => bptV * PxPt,
                            "em" => bptV * (mc.FontSize ?? mps.fontSize),
                            _ => bptV,
                        };
                    }
                    break;
                }
                case "padding-left":
                {
                    var bpl = Regex.Match(bVal, @"([\d.]+)\s*(px|pt|em)");
                    if (bpl.Success)
                        mc.PadLeft = bpl.Groups[2].Value.ToLowerInvariant() switch
                        {
                            "px" => DtpNum(bpl.Groups[1].Value) * PxPt,
                            "em" => DtpNum(bpl.Groups[1].Value) * (mc.FontSize ?? mps.fontSize),
                            _ => DtpNum(bpl.Groups[1].Value),
                        };
                    break;
                }
                case "height":
                {
                    var bph = Regex.Match(bVal, @"([\d.]+)\s*px");
                    if (bph.Success) mc.HeightPt = DtpNum(bph.Groups[1].Value) * PxPt;
                    break;
                }
                case "border-left":
                case "border-right":
                case "border-bottom":
                case "border-top":
                {
                    var bbw = Regex.Match(bVal, @"([\d.]+)\s*px");
                    var dashed = bVal.Contains("dashed", StringComparison.OrdinalIgnoreCase);
                    if (!bbw.Success
                        || !(dashed || bVal.Contains("solid", StringComparison.OrdinalIgnoreCase)))
                        break;
                    var sidePt = DtpNum(bbw.Groups[1].Value) * PxPt;
                    switch (bProp.ToLowerInvariant())
                    {
                        case "border-left": mc.BorderLeftW = sidePt; break;
                        case "border-right": mc.BorderRightW = sidePt; break;
                        case "border-bottom": mc.BorderBottomW = sidePt; break;
                        default:
                            mc.BorderTopW = sidePt;
                            mc.BorderTopDashed = dashed;
                            break;
                    }
                    break;
                }
            }
    }
}
