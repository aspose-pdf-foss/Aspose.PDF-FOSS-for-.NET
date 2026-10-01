using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One column of a metric row: the cell's box, background, borders, text lines, images and controls at the row's seat.</summary>
    private static void RenderMetricRowColumn(MetricRowsState mr, int c)
    {
        var boxW = mr.colW[c] + 2 * mr.p;
        if (mr.flatSkip > 0)
        {
            // a phantom slot under a spanning cell: nothing of its own,
            // no advance — the spanning cell already covered it.
            mr.flatSkip--;
            return;
        }
        if (c < mr.r.Count)
            for (var k = 1; k < mr.r[c].ColSpan && c + k < mr.nCols; k++)
            {
                boxW += mr.s + mr.colW[c + k] + 2 * mr.p;
                mr.flatSkip++;
            }
        // bgcolor cell fill: the whole cell box, behind the text - inset
        // inside the collapsed grid so the shared border strokes stay.
        if (c < mr.r.Count && mr.r[c].Bg is { } cbg)
        {
            var fIn = mr.mps.collapsedGrid ? 0.75 : 0;
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"q {cbg.R / 255.0:0.###} {cbg.G / 255.0:0.###} {cbg.B / 255.0:0.###} rg " +
                $"{mr.colX + fIn:F2} {mr.cursor.y - mr.s - mr.rowBoxH + fIn:F2} {boxW - 2 * fIn:F2} {mr.rowBoxH - 2 * fIn:F2} re f Q\n")));
        }
        // <fieldset> in a cell: the UA frames it with a 0.75 pt #808080 box around the cell,
        // with or without a legend (probed: a legend-less fieldset and a legend-bearing one
        // emit the identical four strokes). The metric grid drew no frame at all, so the
        // complaint report's boxed groups were bare text.
        // (a UA form cell's fieldset frames the grids it holds after they draw, page by page)
        if (c < mr.r.Count && mr.r[c].HasFieldset && !(mr.mps.uaFormCells && mr.r[c].SubTables is { Count: > 0 }))
        {
            // …around the cell's CONTENT box, which is the fieldset's own outer box: the
            // reference's frame stands one cell padding inside the row band on every side.
            var fsX0 = mr.colX + mr.p;
            var fsX1 = mr.colX + boxW - mr.p;
            // …at the height of THIS cell's own content, not the row's. A fieldset is a block
            // in the cell: it wraps what it holds and no more, and the cell's own
            // vertical-align then centres the short one in the row band - exactly as the cell's
            // TEXT already centres. Drawing it at the row height stretched every fieldset in a
            // row to its tallest sibling: measured on the complaint report, the two side-by-side
            // groups have 5 and 4 text rows and the reference frames them 104.7 and 91.2 tall on
            // a shared 104.7 band, sharing a centre. It is also the whole of what the block
            // comparator still sees on that page - 122 missing and 91 extra blocks, every one of
            // them an edge of this frame.
            var fsH = mr.r[c].ContentH > 0 ? mr.r[c].ContentH : mr.rowBoxH - 2 * mr.p;
            var fsTop = mr.cursor.y - mr.s - mr.p - Math.Max(0, (mr.rowContentH - fsH) / 2);
            var fsBot = fsTop - fsH;
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"q {MetricFieldsetFrameW:0.##} w {FsFrameGray:0.###} {FsFrameGray:0.###} {FsFrameGray:0.###} RG " +
                $"{fsX0:F2} {fsBot:F2} {fsX1 - fsX0:F2} {fsTop - fsBot:F2} re S Q\n")));
        }
        // <hr> in a cell: the browser's 3-D groove. A box HrGrooveH tall,
        // centred in the row band, spanning the cell's content box, with
        // its top and left edges black and its bottom and right in the
        // UA's grey — each stroke half a width inside its own edge.
        if (c < mr.r.Count && mr.r[c].HrRule)
        {
            var hrX0 = mr.colX + mr.p;
            var hrX1 = mr.colX + boxW - mr.p + mr.r[c].HrOutsetPt;
            var grooveH = mr.r[c].HrBoxPt > 0 ? mr.r[c].HrBoxPt : HrGrooveH;
            var hrTop = mr.cursor.y - mr.s - (mr.rowBoxH - grooveH) / 2;
            var hrBot = hrTop - grooveH;
            var half = HrGrooveW / 2;
            var hsb = new StringBuilder(Compat.Format(mr.invc, $"q {HrGrooveW:0.##} w "));
            hsb.Append(Compat.Format(mr.invc,
                $"0 0 0 RG {hrX0:F2} {hrTop - half:F2} m {hrX1:F2} {hrTop - half:F2} l S "));
            hsb.Append(Compat.Format(mr.invc,
                $"{hrX0 + half:F2} {hrTop:F2} m {hrX0 + half:F2} {hrBot:F2} l S "));
            hsb.Append(Compat.Format(mr.invc,
                $"{HrGrooveGrey:0.###} {HrGrooveGrey:0.###} {HrGrooveGrey:0.###} RG " +
                $"{hrX0:F2} {hrBot + half:F2} m {hrX1:F2} {hrBot + half:F2} l S "));
            hsb.Append(Compat.Format(mr.invc,
                $"{hrX1 - half:F2} {hrTop:F2} m {hrX1 - half:F2} {hrBot:F2} l S Q\n"));
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(hsb.ToString()));
        }
        // class-skin side borders (the boleto field grid): each declared
        // side strokes its own edge of the cell box in black; a dashed
        // top is the tear-off rule.
        RenderMetricCellBorders(mr, c, boxW);
        if (c < mr.r.Count && (mr.r[c].Lines.Length > 0 || mr.r[c].SubTables is { Count: > 0 }
            || mr.r[c].DivSegs is { Count: > 0 } || mr.r[c].ImgBytes is not null
            || mr.r[c].ImgPlaceholder
            || mr.r[c].LeadCheckboxName is not null
            || mr.r[c].InputBoxes is { Count: > 0 } || mr.r[c].AbsTexts is { Count: > 0 }))
        {
            RenderMetricCellContent(mr, c, boxW);
        }
        mr.colX += boxW + mr.s;
    }

    /// <summary>A cell with lines or sub-tables draws its content: the text lines with their faces and alignment, images, controls and the nested tables at the cell's seat.</summary>
    private static void RenderMetricCellContent(MetricRowsState mr, int c, double boxW)
    {
        mr.mc = mr.r[c];
        mr.cellFs = mr.mc.FontSize ?? mr.mps.fontSize;
        mr.cellLineH = CellLineOf(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, mr.mc, mr.cellFs);
        // A DECLARED row height bands the row taller than its content, and a middle-aligned
        // cell centres in that band rather than in the content (measured: a `height: 20px`
        // cell bands 15 pt and its 11.25 line seats 1.875 down, pitching the row 13.125).
        // NOTE: the reference centres a middle-aligned cell in a band its row DECLARES (probed on
        // foster02/pitch01/height01-04, all exact), but that term is NOT applied here: it shifts
        // a second document down 4.5 pt against its reference and no discriminator separates the two
        // documents - inline vs class height, explicit vs default valign, the height on the text
        // cell vs a sibling, and px vs mm were each probed and none of them splits it.
        var vBand = mr.rowContentH;
        // A UA form cell whose grids break to the next page opens at its band's top: the declared
        // height it cannot hold on this page seats nothing (measured on the test request: the
        // 615 px conditions row opens its legend at the content top and its frame closes on the
        // page after with no band left under it).
        if (mr.mps.uaFormCells && mr.mc.SubTables is { Count: > 0 }
            && mr.cursor.y - mr.s - mr.rowBoxH < mr.marginBottom)
            vBand = mr.mc.ContentH;
        // (a UA row that runs past the page bottom centres its cells in the part of its band that
        // stands on THIS page - probed on the Words letter: the address grid seats 15.4 under the
        // row top beside a block that paginates, half of what the page holds less the grid; a
        // shorter grid seats half its missing height lower)
        if (mr.stdSerif)
            vBand = Math.Min(vBand, Math.Max(mr.mc.ContentH, mr.cursor.y - mr.s - mr.marginBottom - 2 * mr.p));
        // (a row-spanning cell seats at the bottom of the band its rows make, not of its first row)
        // (a bottom-aligned UA cell seats at the bottom of the band its row DECLARES - probed on the
        // Words letter: the 112.5 pt row's bottom-aligned text stands on the band's last line)
        var bottomBand = mr.stdSerif ? Math.Max(mr.rowContentH, mr.rowBoxH - 2 * mr.p) : mr.rowContentH;
        mr.lineTop = mr.mc.VAlignBottom ? mr.contentTop - (Math.Max(bottomBand, mr.mc.RowSpan > 1 ? mr.mc.SpanBandPt : 0) - mr.mc.ContentH)
            // a rowspan cell hangs from its row top over the rows below
            // (a UA row-spanning cell that ASKS for middle centres in the band its rows make; one
            // that says nothing hangs from its row top - measured: the budget report's spanning cells)
            : mr.mc.RowSpan > 1 && mr.stdSerif && mr.mc.SpanBandPt > 0 && mr.mc.VAlignMiddle
                ? mr.contentTop - Math.Max(0, (mr.mc.SpanBandPt - mr.mc.ContentH) / 2)
            : mr.mc.VAlignTop || mr.mps.collapsedGrid || mr.mc.RowSpan > 1 ? mr.contentTop
            : mr.contentTop - (vBand - mr.mc.ContentH) / 2;
        mr.lineTop -= mr.mc.PadTopPt;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.WriteLine("[mrow] lines=" + mr.mc.Lines.Length + " subs=" + (mr.mc.SubTables?.Count ?? 0) + " cursorTop=" + (mr.pageHeight - mr.cursor.y).ToString("0.##") + " contentTop=" + (mr.pageHeight - mr.contentTop).ToString("0.##") + " lineTop=" + (mr.pageHeight - mr.lineTop).ToString("0.##") + " s=" + mr.s + " p=" + mr.p + " rowBoxH=" + mr.rowBoxH.ToString("0.##") + " rowContentH=" + mr.rowContentH.ToString("0.##") + " cellContentH=" + mr.mc.ContentH.ToString("0.##") + " cellLineH=" + mr.cellLineH.ToString("0.##") + " fs=" + mr.cellFs + " face=" + mr.mc.Face + " bold=" + mr.mc.Bold + " drop=" + CellDropOf(mr.mps, mr.stdSerif, mr.fm, mr.mc, mr.cellFs, mr.cellLineH).ToString("0.##") + " padT=" + mr.mc.PadTopPt + " padB=" + mr.mc.PadBottomPt + " '" + (mr.mc.Text.Length > 12 ? mr.mc.Text.Substring(0, 12) : mr.mc.Text) + "'");
        mr.mFace = CellFaceName(mr.face, mr.boldFace, mr.mc);
        // (an italic asked of a face without an italic file draws the upright face sheared)
        mr.cellShear = SyntheticItalicShearFor(mr.mc.Face, mr.mc.Bold, mr.mc.Italic);
        if (mr.cellShear > 0) { mr.mc.Italic = false; mr.mFace = CellFaceName(mr.face, mr.boldFace, mr.mc); }
        mr.fontRes = MetricRunRes(mr, mr.cursor.page, mr.mc);
        if (mr.mc.Fore is { } fc)
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"{fc.R / 255.0:0.###} {fc.G / 255.0:0.###} {fc.B / 255.0:0.###} rg")));
        // div-stacked cells draw band by band, each with its own
        // class typography; a .BB band strokes its bottom edge
        if (mr.mc.DivSegs is { Count: > 0 } dsegs2)
        {
            RenderMetricCellBands(mr, dsegs2, boxW, mr.cursor.page);
        }
        else if (mr.mc.Flow is null)
        {
        RenderMetricCellLines(mr, boxW, mr.cursor.page);
        }
        if (mr.mc.Fore is not null)
            mr.cursor.page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
        // (the cell's padding-bottom is box space under its last line, in the advance too)
        if (mr.mc.RowSpan <= 1 && mr.mc.SubTables is not { Count: > 0 })
            mr.rowRealBottom = Math.Min(mr.rowRealBottom, mr.lineTop - mr.mc.PadBottomPt);
        // Interleaved flow cells: text runs and nested grids draw in
        // SOURCE order — a <br> closes its line (an empty one is a
        // blank line box), runs carry their own bold, and a page
        // break resumes at the raw content top like any table row.
        if (mr.mc.Flow is { Count: > 0 } flowRuns)
        {
            RenderMetricCellFlow(mr, flowRuns, boxW);
        }
        // nested grids render inside the cell, stacked below its
        // own lines at the cell's content width
        else if (mr.mc.SubTables is { Count: > 0 })
        {
            RenderMetricCellSubTables(mr, boxW);
        }
    }

    /// <summary>A cell whose content is a nested table draws that grid at the cell's seat.</summary>
    private static void RenderMetricCellSubTables(MetricRowsState mr, double boxW)
    {
        // The nested grids flow on the cell's page from below its own lines, at a
        // position of their own: they share the row's page (a grid that paginates
        // moves the row on) but not its y.
        // A fieldset in the cell is a BOX the grid stands inside, not just a frame drawn
        // round it: its stroke and padding inset the content on every side.
        var fsIn = mr.mc.HasFieldset ? MetricFieldsetSideInsetPt(mr.mps, mr.cellFs) : 0;
        var cellLine = CellLineOf(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, mr.mc, mr.cellFs);
        var sub = new FlowPosition
        {
            page = mr.cursor.page,
            y = mr.lineTop - (mr.mc.HasFieldset ? MetricFieldsetTopInsetPt(mr.mps, mr.mc, mr.cellFs, cellLine) : 0),
        };
        var uaFrame = mr.mps.uaFormCells && mr.mc.HasFieldset;
        var frameTop = mr.lineTop;
        var framePage0 = mr.cursor.page;
        // (a nested grid's rows resume one host cellspacing under a continuation page's top)
        var subInset = mr.mps.uaFormCells ? mr.s : 0;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.WriteLine("[msub] lineTop=" + (mr.pageHeight - mr.lineTop).ToString("0.##") + " subTop=" + (mr.pageHeight - sub.y).ToString("0.##") + " lines=" + mr.mc.Lines.Length + " cellLine=" + CellLineOf(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, mr.mc, mr.cellFs).ToString("0.##"));
        // The nested grid inherits the CELL's typography - its face and size - the way any
        // descendant does (the letter's nested grids draw their class face at their class size).
        var subIndex = -1;
        foreach (var subTable in mr.mc.SubTables!)
        {
            subIndex++;
            var (subX, subW) = mr.stdSerif
                ? CentredSubTableBox(subTable, mr.css, mr.colX + mr.p + fsIn, boxW - 2 * mr.p - 2 * fsIn)
                : (mr.colX + mr.p + fsIn, boxW - 2 * mr.p - 2 * fsIn);
            RenderMetricTable(mr.doc, sub, subTable, mr.css,
                subX, subW, mr.pageWidth, mr.pageHeight,
                mr.marginTop, mr.marginBottom, GridTypoCell(mr.mc).Face ?? mr.face, CellFm(mr.fm, GridTypoCell(mr.mc)), mr.docFontDict,
                mr.stdSerif, GridTypoCell(mr.mc).FontSize ?? mr.baseFontSize,
                wrapperStacks: true, symInsetPt: 0,
                paragraphCells: mr.paragraphCells, serifReportCells: mr.serifReportCells,
                loadOptions: mr.loadOptions, siblingCellRules: mr.siblingCellRules, uaBlockCells: mr.mps.uaBlockCells,
                uaFormCells: mr.mps.uaFormCells, pageMargin: (mr.mps.pageMarginLeft, mr.mps.pageMarginTop),
                continuationInsetPt: subInset, hostCellPadPt: mr.p,
                hostBlockClasses: InheritedHostClasses(mr.mps.hostBlockClasses, mr.mc.SubTableHostClasses is { } shc && subIndex < shc.Count ? shc[subIndex] : null));
        }
        mr.cursor.page = sub.page;
        var frameBottom = sub.y - (mr.mc.HasFieldset ? MetricFieldsetBottomInsetPt(mr.cellFs) : 0);
        if (uaFrame)
            RenderUaFormFieldsetFrame(mr, boxW, framePage0, frameTop, sub.page, frameBottom, cellLine, subInset);
        // a ROWSPAN cell's nested grid overlays the rows below —
        // it must not carry its own row's bottom with it
        if (mr.mc.RowSpan <= 1)
            mr.rowSubBottom = Math.Min(mr.rowSubBottom,
                // …and the fieldset box closes UNDER the grid it holds, the cell's own
                // padding-bottom under that (measured: the letter's `padding-bottom: 17px`
                // grid rows keep their 12.75 under the nested grid)
                frameBottom - mr.mc.PadBottomPt - mr.mc.TrailingBreakLines * cellLine);
    }

    /// <summary>The classes of the blocks open round a nested grid, its host grid's own inherited ones
    /// first: the sheet's descendant rules reach a grid nested at any depth.</summary>
    private static string[]? InheritedHostClasses(string[]? inherited, string[]? own)
    {
        if (inherited is not { Length: > 0 }) return own;
        if (own is not { Length: > 0 }) return inherited;
        var all = new string[inherited.Length + own.Length];
        inherited.CopyTo(all, 0);
        own.CopyTo(all, inherited.Length);
        return all;
    }

    /// <summary>The box a nested grid draws in: an `align=center` grid of a declared width stands
    /// centred in its host cell's content box, as wide as the larger of its declared box and the
    /// intrinsic width of its own content (probed on the e-mail cards: the 755 px wrapper grows to
    /// its 767 px card and centres in the 775 px body; each 761 px card centres in that).</summary>
    private static (double x, double w) CentredSubTableBox(string subTable, IReadOnlyDictionary<string, Dictionary<string, string>> css, double hostX, double hostW)
    {
        var tag = Regex.Match(subTable, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tag.Success || !Regex.IsMatch(tag.Value, @"\balign\s*=\s*[""']?center", RegexOptions.IgnoreCase)) return (hostX, hostW);
        double declared = 0;
        var attr = Regex.Match(tag.Value, @"\bwidth\s*=\s*[""']?\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*[""'\s/>]", RegexOptions.IgnoreCase);
        if (attr.Success) declared = DtpNum(attr.Groups[1].Value) * PxPt;
        var styleW = Regex.Match(DivStyleOf(tag.Value), @"(?<![-\w])width\s*:\s*([\d.]+\s*(?:px|pt|in|cm|mm))", RegexOptions.IgnoreCase);
        if (styleW.Success && TryParseLength(styleW.Groups[1].Value.Replace(" ", "")) is { } stylePt) declared = stylePt;
        if (declared <= 0) return (hostX, hostW);
        var gridW = Math.Max(declared, InCellDeclaredTableIntrinsicPt(subTable, css, selfInCell: true));
        if (gridW >= hostW - 1e-6) return (hostX, hostW);
        return (hostX + (hostW - gridW) / 2, gridW);
    }

    /// <summary>A UA form cell's fieldset, drawn once its grids have: the legend on the frame's
    /// top line - centred when the sheet centres the fieldset, else at the UA content-left seat -
    /// the top line broken around it, the sides down every page the grids crossed (a
    /// continuation page's sides resume at the host row's restart and the page's sides end at
    /// the host cell's content bottom), and the bottom line under the last grid (measured on the
    /// test request: `Tester Information` opens at 661.28 on page 1, its sides run to 767.75,
    /// resume at 73.5 on page 2 and close at 133.5).</summary>
    private static void RenderUaFormFieldsetFrame(MetricRowsState mr, double boxW, Page page0, double frameTop,
        Page pageN, double frameBottom, double cellLine, double subInset)
    {
        var x0 = mr.colX + mr.p + UaFieldsetSideInsetPt;
        var x1 = mr.colX + boxW - mr.p - UaFieldsetSideInsetPt;
        var half = UaFieldsetStrokePt / 2;
        var legend = mr.mc.LegendText;
        var topLineY = legend is not null ? frameTop - cellLine / 2 : frameTop - half;
        var legendCentred = mr.css.TryGetValue("fieldset", out var fsRule)
            && fsRule.TryGetValue("text-align", out var fsAlign)
            && fsAlign.Trim().Equals("center", StringComparison.OrdinalIgnoreCase);
        double gap0 = 0, gap1 = 0;
        if (legend is not null && legend.Length > 0)
        {
            var legendW = MeasureFaceText(mr.mFace, legend, mr.cellFs);
            var legendX = legendCentred ? (x0 + x1 - legendW) / 2 : x0 + MetricFieldsetSideInsetPt(mr.cellFs) + UaFieldsetLegendPadPt;
            gap0 = legendX - UaFieldsetLegendPadPt;
            gap1 = legendX + legendW + UaFieldsetLegendPadPt;
            var drop = CellDropOf(mr.mps, mr.stdSerif, mr.fm, mr.mc, mr.cellFs, cellLine);
            EmitCellLineRuns(page0, mr.fontRes, mr.cellFs, legendX, frameTop - drop, legend, mr.mFace);
        }
        var pageTopY = mr.pageHeight - mr.marginTop - subInset;
        var pageBottomY = mr.marginBottom + mr.s + mr.p;
        for (var n = page0.Number; n <= pageN.Number; n++)
        {
            var page = mr.doc.Pages[n];
            var top = n == page0.Number ? topLineY : pageTopY;
            var bot = n == pageN.Number ? frameBottom : pageBottomY;
            var sb = new StringBuilder(Compat.Format(mr.invc,
                $"q {UaFieldsetStrokePt:0.##} w {FsFrameGray:0.###} {FsFrameGray:0.###} {FsFrameGray:0.###} RG "));
            if (n == page0.Number)
            {
                if (gap1 > gap0)
                    sb.Append(Compat.Format(mr.invc, $"{x0:F2} {top:F2} m {gap0:F2} {top:F2} l {gap1:F2} {top:F2} m {x1:F2} {top:F2} l S "));
                else sb.Append(Compat.Format(mr.invc, $"{x0:F2} {top:F2} m {x1:F2} {top:F2} l S "));
            }
            var sideTop = n == page0.Number ? top + half : top;
            sb.Append(Compat.Format(mr.invc, $"{x0 + half:F2} {sideTop:F2} m {x0 + half:F2} {bot:F2} l S "));
            sb.Append(Compat.Format(mr.invc, $"{x1 - half:F2} {sideTop:F2} m {x1 - half:F2} {bot:F2} l S "));
            if (n == pageN.Number)
                sb.Append(Compat.Format(mr.invc, $"{x0:F2} {bot + half:F2} m {x1:F2} {bot + half:F2} l S "));
            sb.Append("Q\n");
            page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        }
    }

    /// <summary>An interleaved flow cell draws its text runs and nested grids in source order.</summary>
    private static void RenderMetricCellFlow(MetricRowsState mr, List<(string? TableHtml, string Text, bool Bold)> flowRuns, double boxW)
    {
        // The cell's own flow position: it starts on the row's page at the cell's
        // seat, breaks pages as its lines and nested grids need, and hands the row
        // the page it ended on.
        var cf = new CellFlowLine
        {
            Flow = new FlowPosition { page = mr.cursor.page, y = mr.lineTop },
            EffW = boxW - 2 * mr.p,
        };
        foreach (var fi in flowRuns)
        {
            if (fi.TableHtml is { } subHtml)
            {
                if (cf.HasInk) FlushCellFlowLine(mr, cf);
                else cf.Pending.Clear();
                RenderNestedGridInCell(mr, cf, subHtml);
                continue;
            }
            var fParts = fi.Text.Split('\u0001');
            for (var fpi = 0; fpi < fParts.Length; fpi++)
            {
                if (fpi > 0) FlushCellFlowLine(mr, cf);
                var fpt = fParts[fpi];
                if (fpt.Length == 0) continue;
                if (cf.Pending.Count == 0 && MeasureFaceText(CellRunFace(mr, fi.Bold), fpt, mr.cellFs) > cf.EffW)
                {
                    var fWls = MeasuredWordWrap(fpt, cf.EffW, CellRunFace(mr, fi.Bold), mr.cellFs);
                    for (var wli = 0; wli < fWls.Length; wli++)
                    {
                        cf.Pending.Add((fWls[wli], fi.Bold));
                        if (wli < fWls.Length - 1) FlushCellFlowLine(mr, cf);
                    }
                }
                else cf.Pending.Add((fpt, fi.Bold));
            }
        }
        if (cf.HasInk) FlushCellFlowLine(mr, cf);
        mr.cursor.page = cf.Flow.page;
        mr.lineTop = cf.Flow.y;
        if (mr.mc.RowSpan <= 1)
            mr.rowSubBottom = Math.Min(mr.rowSubBottom, cf.Flow.y);
    }

    /// <summary>A flow cell's own cursor and the line it is filling.</summary>
    private sealed class CellFlowLine
    {
        public FlowPosition Flow = null!;
        public double EffW;
        public readonly List<(string T, bool B)> Pending = new();

        /// <summary>Whitespace between a grid and its line breaks is not a line of its own.</summary>
        public bool HasInk
        {
            get
            {
                foreach (var (pt, _) in Pending) if (pt.Trim().Length > 0) return true;
                return false;
            }
        }
    }

    /// <summary>The face a flow cell's runs measure and wrap on — which is the face they draw:
    /// a cell naming its own family measured on the table's face overflowed its box by the two
    /// faces' difference.</summary>
    private static string CellRunFace(MetricRowsState mr, bool fb)
        => CellFaceName(mr.face, mr.boldFace, new MetricCell { Face = mr.mc.Face, Bold = fb });

    /// <summary>The font resource the flow cell's runs draw through on the page it has reached.</summary>
    private static string CellFlowRes(MetricRowsState mr, CellFlowLine cf, bool fb)
        => mr.mc.Face is not null
            ? ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, cf.Flow.page, new MetricCell
                { Face = mr.mc.Face, Bold = fb, FontSize = mr.mc.FontSize })
            : (mr.tableRuleFace || (!mr.stdSerif && mr.wrapperStacks)
                || (mr.stdSerif && !mr.face.Equals("Times New Roman",
                    StringComparison.OrdinalIgnoreCase)))
               && PosFace(mr.face + (fb ? " Bold" : "")).ttf is not null
            ? ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, cf.Flow.page, new MetricCell
                { Face = mr.face, Bold = fb, FontSize = mr.mc.FontSize })
            : fb ? (mr.stdSerif ? "F6" : "F2") : (mr.stdSerif ? "F5" : "F1");

    /// <summary>Draws the runs waiting on the flow cell's line, breaking the page first when the
    /// line no longer fits, and drops the cursor one line.</summary>
    private static void FlushCellFlowLine(MetricRowsState mr, CellFlowLine cf)
    {
        if (cf.Flow.y - mr.cellLineH < mr.marginBottom)
        {
            cf.Flow.page = MetricNextPage(mr, cf.Flow.page);
            EnsureFonts(cf.Flow.page, mr.docFontDict);
            cf.Flow.y = mr.pageHeight - mr.marginTop;
        }
        var fDrop = CellDropOf(mr.mps, mr.stdSerif, mr.fm, mr.mc, mr.cellFs, mr.cellLineH);
        var fx = mr.colX + mr.mc.BorderLeftW + (mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p);
        foreach (var (rt, rb) in cf.Pending)
        {
            if (rt.Length == 0) continue;
            EmitCellLineRuns(cf.Flow.page, CellFlowRes(mr, cf, rb), mr.cellFs, fx,
                cf.Flow.y - fDrop, rt, CellRunFace(mr, rb));
            fx += MeasureFaceText(CellRunFace(mr, rb), rt, mr.cellFs);
        }
        cf.Pending.Clear();
        cf.Flow.y -= mr.cellLineH;
    }

    /// <summary>A nested grid in a flow cell. It moves to the next page WHOLE when it cannot
    /// fit — these tables are never split across the break — and inherits the CELL's face and
    /// size, as any descendant does.</summary>
    private static void RenderNestedGridInCell(MetricRowsState mr, CellFlowLine cf, string subHtml)
    {
        var subEst = NestedTableWrappedHeight(subHtml,
            mr.cellLineH, mr.face, mr.cellFs, cf.EffW);
        if (!mr.stdSerif && cf.Flow.y - subEst < mr.marginBottom
            && subEst <= mr.pageHeight - mr.marginTop - mr.marginBottom)
        {
            cf.Flow.page = MetricNextPage(mr, cf.Flow.page);
            EnsureFonts(cf.Flow.page, mr.docFontDict);
            cf.Flow.y = mr.pageHeight - mr.marginTop;
        }
        RenderMetricTable(mr.doc, cf.Flow, subHtml, mr.css,
            mr.colX + mr.p, cf.EffW, mr.pageWidth, mr.pageHeight,
            mr.marginTop, mr.marginBottom, GridTypoCell(mr.mc).Face ?? mr.face, CellFm(mr.fm, GridTypoCell(mr.mc)), mr.docFontDict,
            mr.stdSerif, GridTypoCell(mr.mc).FontSize ?? mr.baseFontSize,
            wrapperStacks: true, symInsetPt: 0,
            paragraphCells: mr.paragraphCells,
            serifReportCells: mr.serifReportCells,
            loadOptions: mr.loadOptions, siblingCellRules: mr.siblingCellRules, uaBlockCells: mr.mps.uaBlockCells,
            uaFormCells: mr.mps.uaFormCells, pageMargin: (mr.mps.pageMarginLeft, mr.mps.pageMarginTop),
            hostCellPadPt: mr.p, hostBlockClasses: mr.mps.hostBlockClasses);
    }


    /// <summary>A plain cell draws its image and its measured text lines at the cell's seat.</summary>
    private static void RenderMetricCellLines(MetricRowsState mr, double boxW, Page page)
    {
        RenderMetricCellAbsTexts(mr, page);
        var cellTop = mr.lineTop;
        // (a remote image's alt text stands one px inside its bordered box)
        if (mr.mc.AltBoxed) mr.lineTop -= UaAltBoxBorderPt;
        if (mr.mc.ImgPlaceholder && mr.mc.ImgWPt > 0)
        {
            // (a remote broken image's bevel stands one gutter in from the cell's edge and its top)
            var gutter = mr.mc.ImgRemoteBroken ? RemoteBrokenImageGutterPt : 0;
            var bx = mr.colX + mr.mc.BorderLeftW + (mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p) + gutter + (mr.mc.ImgRemoteBroken ? BrokenImageBevelPt : 0);
            var bTop = mr.lineTop - gutter - (mr.mc.ImgRemoteBroken ? BrokenImageBevelPt : 0); var bBot = bTop - mr.mc.ImgHPt; var bR = bx + mr.mc.ImgWPt;
            // (a sized broken image after the line's text stands on that line's baseline, past the text)
            var inline = mr.mc.ImgAfterText && mr.mc.Lines.Length > 0;
            if (inline)
            {
                var lastTop = mr.lineTop - (mr.mc.Lines.Length - 1) * mr.cellLineH;
                var drop = CellDropOf(mr.mps, mr.stdSerif, mr.fm, mr.mc, mr.cellFs, mr.cellLineH);
                bBot = lastTop - Math.Max(0, mr.mc.ImgHPt - CellFm(mr.fm, mr.mc).asc * mr.cellFs) - drop;
                bTop = bBot + mr.mc.ImgHPt;
                bx += MeasureFaceText(mr.mFace, mr.mc.Lines[^1], mr.cellFs);
                bR = bx + mr.mc.ImgWPt;
            }
            var h = BrokenImageBevelPt / 2;
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"q {BrokenImageBevelPt:0.##} w {BrokenImageDarkGray:0.###} G " +
                $"{bx:F2} {bTop - h:F2} m {bR:F2} {bTop - h:F2} l S {bx + h:F2} {bTop:F2} m {bx + h:F2} {bBot:F2} l S " +
                $"{BrokenImageLightGray:0.###} G {bx:F2} {bBot + h:F2} m {bR:F2} {bBot + h:F2} l S {bR - h:F2} {bTop:F2} m {bR - h:F2} {bBot:F2} l S Q\n")));
            if (!inline) mr.lineTop -= mr.mc.ImgHPt;
        }
        else if (mr.mc.ImgBytes is { } jpg3 && mr.mc.ImgWPt > 0)
        {
            var jx3 = mr.colX + mr.mc.BorderLeftW
                + (mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p);
            page.AddImage(jpg3, new Rectangle(
                jx3, mr.lineTop - mr.mc.ImgHPt, jx3 + mr.mc.ImgWPt, mr.lineTop));
            mr.lineTop -= mr.mc.ImgHPt;
        }
        // The cell's leading checkbox: its 13 px box sits on the FIRST line's baseline after a
        // 3 pt margin, its widget one point inside it, and nothing of it is drawn in the page
        // content. The line it takes is the em plus the strut descent under it.
        var leadBoxW = 0.0;
        var cbAbove = 0.0;
        if (mr.mc.LeadCheckboxName is not null)
        {
            var (above, below) = MetricCheckboxLine(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, mr.mc, mr.cellFs);
            cbAbove = above;
            var cbLineH = above + below;
            var cbBase = mr.lineTop - above;
            var cbX = mr.colX + mr.mc.BorderLeftW
                + (mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p)
                + (mr.mc.LeadCheckboxMarginLeftPt ?? Table.UaCheckboxLeadPt) + Table.UaCheckboxWidgetInsetPt;
            // the widget stands one point INSIDE the box, whose bottom sits on the baseline
            var cbY = cbBase + Table.UaCheckboxWidgetInsetPt;
            var cbf = new Aspose.Pdf.Forms.CheckboxField(page, new Rectangle(
                cbX, cbY, cbX + Table.UaCheckboxWidgetPt, cbY + Table.UaCheckboxWidgetPt))
            { Checked = mr.mc.LeadCheckboxChecked };
            if (mr.mc.LeadCheckboxName.Length > 0) cbf.PartialName = mr.mc.LeadCheckboxName;
            mr.doc.Form.Add(cbf, page.Number);
            if (mr.mc.LeadCheckboxOwnLine) mr.lineTop -= cbLineH;
            else leadBoxW = MetricCheckboxMarginBoxPt(mr.mc);
        }
        var lineOffs = mr.mc.Runs is not null ? LineStartOffsets(mr.mc.Text, mr.mc.Lines) : null;
        for (var li = 0; li < mr.mc.Lines.Length; li++)
        {
            var ln = mr.mc.Lines[li];
            var lead = li == 0 ? leadBoxW : 0;
            var boxH = (li == 0 && lead > 0
                    ? MetricCheckboxLineHeight(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, mr.mc, mr.cellFs)
                    : mr.cellLineH)
                + (mr.mc.HasSpan && li == 0 ? 3.0 : 0);
            var drop = lead > 0 ? cbAbove
                : CellDropOf(mr.mps, mr.stdSerif, mr.fm, mr.mc, mr.cellFs, boxH);
            // (the last line drops by what an inline broken image rises above its ascent)
            if (mr.mc.ImgAfterText && mr.mc.ImgPlaceholder && li == mr.mc.Lines.Length - 1)
                mr.lineTop -= Math.Max(0, mr.mc.ImgHPt - CellFm(mr.fm, mr.mc).asc * mr.cellFs);
            var lw = mr.mc.Runs is not null ? MeasureStyledLine(mr.face, mr.boldFace, mr.mc, mr.cellFs, ln, lineOffs![li], mr.mc.Runs)
                : MeasureFaceText(mr.mFace, ln, mr.cellFs);
            var lx = mr.mc.Align switch
            {
                HorizontalAlignment.Right => mr.colX + boxW - mr.p - lw,
                HorizontalAlignment.Center => mr.colX + (boxW - lw) / 2,
                // A span cell's content sits 1.5 pt further in; a
                // class border-left pushes the content past itself.
                _ => mr.colX + mr.mc.BorderLeftW + (mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p)
                     + (mr.mc.HasSpan ? 1.5 : 0) + lead,
            };
            if (ln.Length > 0)
            {
                // (a partly bold line draws run by run, each in its own weight)
                if (mr.mc.Runs is not null)
                    EmitStyledLine(mr, page, mr.mc, mr.cellFs, lx, mr.lineTop - drop, ln, lineOffs![li], mr.mc.Runs, mr.cellShear);
                else EmitCellLineRuns(page, mr.fontRes, mr.cellFs, lx, mr.lineTop - drop, ln, mr.mFace, mr.cellShear);
            }
            mr.lineTop -= boxH;
        }
        RenderMetricCellInputs(mr, boxW, page, cellTop);
    }

    /// <summary>The font resource a band's lines draw with on the given page: the band's own face,
    /// the flow's real face for newsletter segments and for a UA grid whose face is not the serif
    /// (the order ticket's Arial heading band, as MetricRunRes draws its plain lines), the standard
    /// resources otherwise. Resolved again on every page the band breaks to.</summary>
    private static string BandFontRes(MetricRowsState mr, MetricDivSeg sg, MetricCell sgProbe, Page on)
        => sgProbe.Face is not null ? ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, on, sgProbe)
            : (mr.paragraphCells || (mr.stdSerif && !mr.face.Equals("Times New Roman", StringComparison.OrdinalIgnoreCase)))
                && PosFace(mr.face + (sg.Bold ? " Bold" : "")).ttf is not null
            ? ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, on, new MetricCell
                { Face = mr.face, Bold = sg.Bold, FontSize = sg.FontSize })
            : sg.Bold ? (mr.stdSerif ? "F6" : "F2") : (mr.stdSerif ? "F5" : "F1");

    /// <summary>A band's lines continue on the next page: the row's cursor moves there and the
    /// band's own colour is set again on the new page's stream.</summary>
    private static Page BreakBandToNextPage(MetricRowsState mr, MetricDivSeg sg, Page page)
    {
        page = MetricNextPage(mr, page);
        mr.cursor.page = page;
        if (sg.Fore is { } bc)
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"{bc.R / 255.0:0.###} {bc.G / 255.0:0.###} {bc.B / 255.0:0.###} rg")));
        return page;
    }

    /// <summary>The index of the first line of a band that draws on the next page, -1 when the rest
    /// fits: the lines from <paramref name="from"/> the page holds, the break kept two lines from
    /// either edge (a band that cannot keep two on this page moves whole).</summary>
    private static int BandBreakLine(int lines, int from, double top, double lineH, double marginBottom)
    {
        if (lineH <= 0) return -1;
        var rest = lines - from;
        var fit = (int)Math.Floor((top - marginBottom) / lineH + 1e-6);
        if (fit >= rest) return -1;
        if (rest - fit < UaWidowLines) fit = rest - UaWidowLines;
        if (fit < UaOrphanLines) return from;
        return from + fit;
    }

    /// <summary>The lines a paragraph keeps at the bottom of a page when it breaks (the CSS default `orphans`).</summary>
    private const int UaOrphanLines = 2;
    /// <summary>The lines a paragraph carries to the next page when it breaks (the CSS default `widows`).</summary>
    private const int UaWidowLines = 2;

    /// <summary>A div-stacked cell draws band by band, each band with its own class typography.</summary>
    private static void RenderMetricCellBands(MetricRowsState mr, List<MetricDivSeg> dsegs2, double boxW, Page page)
    {
        mr.segTop = mr.lineTop;
        // An abs-positioned data-URI PNG draws over the cell at
        // its left:N% offset from the content box, natural size
        // (50px = 37.5pt), out of the flow.
        if (mr.mc.AbsPng is { } apng && apng.Length >= 24)
        {
            var apW = ((apng[16] << 24) | (apng[17] << 16)
                | (apng[18] << 8) | apng[19]) * PxPt;
            var apH = ((apng[20] << 24) | (apng[21] << 16)
                | (apng[22] << 8) | apng[23]) * PxPt;
            var apIn = mr.mps.collapsedGrid ? 0.75 : 0.0;
            if (apW > 0 && apH > 0)
            {
                var apx = mr.colX + apIn + mr.p
                    + mr.mc.AbsPngLeftFrac * (boxW - 2 * apIn - 2 * mr.p);
                page.AddImage(apng, new Rectangle(
                    apx, mr.segTop - apH, apx + apW, mr.segTop));
            }
        }
        // the intrinsic-aspect JPEG opens the cell — its
        // paragraphs stack below it
        if (mr.mc.ImgBytes is { } jpg2 && mr.mc.ImgWPt > 0)
        {
            var jx = mr.colX + mr.mc.BorderLeftW
                + (mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p);
            page.AddImage(jpg2, new Rectangle(
                jx, mr.segTop - mr.mc.ImgHPt, jx + mr.mc.ImgWPt, mr.segTop));
            mr.segTop -= mr.mc.ImgHPt;
        }
        mr.sgPrevMb = 0.0;
        mr.uaFirstBlock = true;
        mr.boxContentW = 0; mr.boxTextX = 0; mr.lastBandParagraph = false;
        mr.curBands = dsegs2;
        for (var si = 0; si < dsegs2.Count; si++)
        {
            mr.curBandIdx = si;
            RenderMetricCellBand(mr, dsegs2[si], boxW, mr.cursor.page);
        }
        mr.curBands = null;
        // (a cell whose bands broke to a later page ends the row there, under its last band)
        if (!ReferenceEquals(mr.cursor.page, page) && mr.mc.RowSpan <= 1)
            mr.rowSubBottom = Math.Min(mr.rowSubBottom, mr.segTop);
        // (a quirks cell drops the last paragraph's bottom margin, as it drops the first's top)
        if ((mr.mps.uaBlockCells || (mr.stdSerif && !QuirksDropsCellBlockMargins)) && !(QuirksDropsCellBlockMargins && mr.lastBandParagraph)) mr.segTop -= mr.sgPrevMb;
        mr.lineTop = mr.segTop;   // the cell's drawn bottom
    }

    /// <summary>The font resource a cell run draws with: its own named face; else the table's real
    /// face for a pt-report cell, a `table { font: … }` shorthand face or a UA-flow body face other
    /// than the serif; else the Standard-14 face of its weight and style.</summary>
    private static string MetricRunRes(MetricRowsState mr, Page page, MetricCell mc)
        => mc.Face is not null
            ? ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, page, mc)
            : (mr.tableRuleFace || (!mr.stdSerif && mr.wrapperStacks)
                || (mr.stdSerif && !mr.face.Equals("Times New Roman",
                    StringComparison.OrdinalIgnoreCase)))
                && PosFace(mr.face + (mc.Bold ? " Bold" : "")).ttf is not null
            ? ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, page, new MetricCell
                { Face = mr.face, Bold = mc.Bold, FontSize = mc.FontSize })
            : mc.Bold ? (mr.stdSerif ? "F6" : "F2")
            : mc.Italic ? (mr.stdSerif ? "F7" : "F3")
            : (mr.stdSerif ? "F5" : "F1");

    /// <summary>A UA block cell's band: every hard line of the block wraps and draws in its own
    /// bold and size, right-aligned when the block is; a right float draws at the band's top and
    /// takes no flow space; the block's margins collapse with its neighbours'.</summary>
    private static void RenderUaBlockBand(MetricRowsState mr, MetricDivSeg sg, double boxW, Page page, double wrapW)
    {
        if (sg.NestedTable >= 0)
        {
            RenderUaBlockNestedGrid(mr, sg, boxW);
            return;
        }
        if (sg.BoxOpen)
        {
            OpenUaBlockBox(mr, sg, boxW, page);
            if (sg.EmptyBlock) return;
        }
        else if (sg.BoxClose)
        {
            mr.segTop -= mr.sgPrevMb + mr.boxPadBottom;
            mr.sgPrevMb = mr.boxMarginBottom;
            mr.boxContentW = 0; mr.boxTextX = 0; mr.lastBandParagraph = false;
            return;
        }
        else if (sg.EmptyBlock)
        {
            var above = mr.uaFirstBlock && mr.p > 0 ? mr.sgPrevMb : Math.Max(mr.sgPrevMb, sg.MarginTopPt);
            mr.sgPrevMb = Math.Max(above, sg.MarginBottomPt);
            mr.uaFirstBlock = false;
            return;
        }
        mr.lastBandParagraph = (sg.IsParagraph || sg.IsHeading) && sg.DirectChild;
        // (inside an open div box the blocks wrap to the box and start at its text edge)
        if (mr.boxContentW > 0) wrapW = mr.boxContentW - sg.PadLeft;
        var floatTop = mr.segTop;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.Error.WriteLine($"[bandseat] first={mr.uaFirstBlock} p={mr.p} quirks={_quirksRowStrut} heading={sg.IsHeading} direct={sg.DirectChild} mt={sg.MarginTopPt:0.##} mb={sg.MarginBottomPt:0.##} prevMb={mr.sgPrevMb:0.##} segTop={mr.segTop:0.##} fs={sg.FontSize} '{(sg.Text.Length > 16 ? sg.Text[..16] : sg.Text)}'");
        if (!sg.FloatRight)
        {
            mr.segTop -= UaFirstBlockMarginDropped(mr.uaFirstBlock, mr.p, sg) ? 0 : Math.Max(sg.MarginTopPt, mr.sgPrevMb);
            mr.uaFirstBlock = false;
        }
        // (a float is the cell's first child too: the block after it keeps its margin)
        else mr.uaFirstBlock = false;
        var segLy = mr.segTop;
        if (sg.Fore is { } sgc)
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"{sgc.R / 255.0:0.###} {sgc.G / 255.0:0.###} {sgc.B / 255.0:0.###} rg")));
        var hardLines = sg.Text.Split('\u0001');
        for (var hi = 0; hi < hardLines.Length; hi++)
        {
            var (lineBold, lineFsOpt) = hi < sg.LineTypo.Count ? sg.LineTypo[hi] : (false, null);
            var probe = new MetricCell { Face = sg.Face, Bold = sg.Bold || lineBold, FontSize = lineFsOpt ?? sg.FontSize };
            var fs = probe.FontSize ?? mr.mps.fontSize;
            var faceName = CellFaceName(mr.face, mr.boldFace, probe);
            var res = ResOfFlatOn(mr.mps, mr.flatRes, mr.face, mr.boldFace, page, new MetricCell
                { Face = probe.Face ?? mr.face, Bold = probe.Bold, FontSize = probe.FontSize });
            var fmv = CellFm(mr.fm, probe);
            var lineH = CellLineOf(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, probe, fs);
            var text = hardLines[hi].Trim(' ');
            var lines = text.Length == 0 ? new[] { "" } : MeasuredWordWrap(text, wrapW, faceName, fs, dashBreaks: true);
            foreach (var ln in lines)
            {
                var drop = MetricBaselineDrop(fs, lineH, fmv);
                var lw = MeasureFaceText(faceName, ln, fs);
                var lx = mr.boxContentW > 0 ? mr.boxTextX + sg.PadLeft
                    : sg.AlignRight || (!sg.AlignCenter && mr.mc.Align == HorizontalAlignment.Right) ? mr.colX + boxW - mr.p - lw
                    : sg.AlignCenter || mr.mc.Align == HorizontalAlignment.Center ? mr.colX + (boxW - lw) / 2
                    : mr.colX + mr.mc.BorderLeftW + sg.PadLeft + mr.p;
                if (ln.Length > 0) EmitCellLineRuns(page, res, fs, lx, segLy - drop, ln, faceName);
                segLy -= lineH;
            }
        }
        if (sg.Fore is not null) page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
        if (sg.FloatRight) { mr.segTop = floatTop; return; }
        mr.sgPrevMb = sg.MarginBottomPt;
        mr.segTop = segLy;
    }

    /// <summary>A div box opens among a UA block cell's bands: it spends its margin like a block,
    /// stands at the cell's right edge when the cell is right-aligned, paints its background over
    /// the height its blocks will take, and spends its top padding; the blocks that follow, up to
    /// the closing band, wrap inside it at its text edge.</summary>
    /// <summary>A quirks cell drops the top margin of a paragraph or heading that is its first block
    /// (the UA block model's padded cells drop their first block's, as calibrated).</summary>
    private static bool UaFirstBlockMarginDropped(bool firstBlock, double cellPad, MetricDivSeg sg)
        => firstBlock && (cellPad > 0 || (_quirksRowStrut && sg.DirectChild && (sg.IsParagraph || sg.IsHeading)));

    private static void OpenUaBlockBox(MetricRowsState mr, MetricDivSeg sg, double boxW, Page page)
    {
        mr.segTop -= UaFirstBlockMarginDropped(mr.uaFirstBlock, mr.p, sg) ? 0 : Math.Max(sg.MarginTopPt, mr.sgPrevMb);
        mr.uaFirstBlock = false;
        var bx = mr.mc.Align == HorizontalAlignment.Right ? mr.colX + boxW - mr.p - sg.BoxWidthPt
            : mr.colX + mr.mc.BorderLeftW + mr.p;
        var bh = UaBlockBoxHeightPt(mr, sg);
        if (sg.Bg is { } bg && bh > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"q {bg.R / 255.0:0.###} {bg.G / 255.0:0.###} {bg.B / 255.0:0.###} rg " +
                $"{bx:F2} {mr.segTop - bh:F2} {sg.BoxWidthPt:F2} {bh:F2} re f Q\n")));
        mr.segTop -= sg.BoxPadTopPt;
        mr.boxTextX = bx + sg.BoxPadLeftPt;
        mr.boxContentW = sg.BoxWidthPt - sg.BoxPadLeftPt - sg.BoxPadRightPt;
        mr.boxPadBottom = sg.BoxPadBottomPt;
        mr.boxMarginBottom = sg.MarginBottomPt;
        mr.sgPrevMb = 0;
    }

    /// <summary>The height a div box takes: its paddings round the bands it holds, their margins
    /// collapsed between them, the last one's bottom margin spent inside the box.</summary>
    private static double UaBlockBoxHeightPt(MetricRowsState mr, MetricDivSeg open)
    {
        var h = open.BoxPadTopPt;
        var contentW = open.BoxWidthPt - open.BoxPadLeftPt - open.BoxPadRightPt;
        double prevMb = 0;
        if (mr.curBands is { } bands)
            for (var i = mr.curBandIdx + (open.EmptyBlock ? 1 : 0); i < bands.Count; i++)
            {
                var sg = bands[i];
                if (sg.BoxClose) break;
                if (sg.FloatRight || sg.NestedTable >= 0) continue;
                if (sg.EmptyBlock) { prevMb = Math.Max(Math.Max(prevMb, sg.MarginTopPt), sg.MarginBottomPt); continue; }
                h += Math.Max(sg.MarginTopPt, prevMb)
                    + UaBlockBandHeightPt(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.boldFace, mr.fm, sg, contentW - sg.PadLeft);
                prevMb = sg.MarginBottomPt;
            }
        return h + prevMb + open.BoxPadBottomPt;
    }

    /// <summary>A nested grid among a UA block cell's bands draws where it stands: under the
    /// block above (whose margin-bottom it keeps - a grid has none to collapse with) and at the
    /// cell's content box, in the cell's typography.</summary>
    private static void RenderUaBlockNestedGrid(MetricRowsState mr, MetricDivSeg sg, double boxW)
    {
        // (the grid's own inline margins are a block's: its top collapses with the block above,
        // its bottom stands under it - probed: a `margin-bottom: 10px` grid leaves 7.5)
        var (gridMt, gridMb) = NestedGridMarginsPt(mr.mps.nestedTables![sg.NestedTable]);
        mr.segTop -= Math.Max(mr.sgPrevMb, gridMt);
        var sub = new FlowPosition { page = mr.cursor.page, y = mr.segTop };
        RenderMetricTable(mr.doc, sub, mr.mps.nestedTables![sg.NestedTable], mr.css,
            mr.colX + mr.p, boxW - 2 * mr.p, mr.pageWidth, mr.pageHeight,
            mr.marginTop, mr.marginBottom, GridTypoCell(mr.mc).Face ?? mr.face, CellFm(mr.fm, GridTypoCell(mr.mc)), mr.docFontDict,
            mr.stdSerif, GridTypoCell(mr.mc).FontSize ?? mr.baseFontSize,
            wrapperStacks: true, symInsetPt: 0,
            paragraphCells: mr.paragraphCells, serifReportCells: mr.serifReportCells,
            loadOptions: mr.loadOptions, siblingCellRules: mr.siblingCellRules, uaBlockCells: true,
            uaFormCells: mr.mps.uaFormCells, pageMargin: (mr.mps.pageMarginLeft, mr.mps.pageMarginTop),
            hostCellPadPt: mr.p);
        mr.cursor.page = sub.page;
        mr.segTop = sub.y;
        mr.sgPrevMb = gridMb;
        mr.uaFirstBlock = false;
    }

    /// <summary>A nested grid's own inline margins (top, bottom) in points: the shorthand or the
    /// sides, px or pt; zero where none is stated.</summary>
    private static (double top, double bottom) NestedGridMarginsPt(string tableHtml)
    {
        var tag = Regex.Match(tableHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        if (!tag.Success) return (0, 0);
        var st = DivStyleOf(tag.Value);
        if (st.Length == 0) return (0, 0);
        double top = 0, bottom = 0;
        if (Regex.Match(st, @"(?<![-\w])margin\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } mM)
        {
            var parts = mM.Groups[1].Value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                top = StatedMarginPt(parts[0], UaDefaultFontPt) ?? 0;
                bottom = StatedMarginPt(parts[parts.Length >= 3 ? 2 : 0], UaDefaultFontPt) ?? 0;
            }
        }
        if (Regex.Match(st, @"(?<![-\w])margin-top\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } tM)
            top = StatedMarginPt(tM.Groups[1].Value, UaDefaultFontPt) ?? 0;
        if (Regex.Match(st, @"(?<![-\w])margin-bottom\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } bM)
            bottom = StatedMarginPt(bM.Groups[1].Value, UaDefaultFontPt) ?? 0;
        return (top, bottom);
    }

    /// <summary>One band of a div-stacked cell: its own typography, its lines and its bottom rule.</summary>
    private static void RenderMetricCellBand(MetricRowsState mr, MetricDivSeg sg, double boxW, Page page)
    {
        if (mr.mps.uaBlockCells)
        {
            RenderUaBlockBand(mr, sg, boxW, page, boxW - 2 * mr.p - sg.PadLeft - mr.mc.BorderLeftW + (mr.mc.PadLeft > 0 ? mr.p : 0));
            return;
        }
        SeatBandTop(mr, sg);
        var bt = MeasureBandTypography(mr, sg, boxW, page);
        var sgLines = sg.Text.Length == 0 ? System.Array.Empty<string>()
            : sg.Runs is not null ? MeasuredWordWrapStyled(sg.Text, bt.WrapW, mr.face, mr.boldFace, bt.Probe, bt.Fs, sg.Runs)
            : MeasuredWordWrap(sg.Text, bt.WrapW, bt.Face, bt.Fs);
        PaintBandBackground(mr, sg, boxW, page, sgLines.Length * bt.LineH);
        if (sg.Fore is { } sgc)
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"{sgc.R / 255.0:0.###} {sgc.G / 255.0:0.###} {sgc.B / 255.0:0.###} rg")));
        var lineOffs = sg.Runs is not null ? LineStartOffsets(sg.Text, sgLines) : null;
        var (endPage, segLy, broke) = DrawBandLines(mr, sg, bt, sgLines, lineOffs, page, boxW);
        page = endPage;
        if (sg.Fore is not null)
            page.AddContentStream(Encoding.ASCII.GetBytes("0 g"));
        var bandH = sg.LineBoxExact ? sg.LineBoxPt : Math.Max(sg.LineBoxPt, sgLines.Length * bt.LineH);
        if (sg.BorderBottom)
            page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
                $"q 0 0 0 RG 0.75 w {mr.colX + mr.mc.BorderLeftW:F2} {mr.segTop - bandH:F2} m {mr.colX + boxW:F2} {mr.segTop - bandH:F2} l S Q\n")));
        // the border sits OUTSIDE the content box: the band advances past it
        mr.segTop = broke ? segLy - sg.BorderBottomPt : mr.segTop - bandH - sg.BorderBottomPt;
    }

    /// <summary>Charges the band's top margin against the one the block above left open. A
    /// negative stated margin pulls the band up past the one above: CSS adds it to the collapsed
    /// positive margin rather than taking the larger; a quirks cell drops the top margin of the
    /// paragraph or heading that is its first block (measured on the case report: the cell's
    /// `&lt;h2&gt;` seats 14.5 under the cell top, its 0.75 em margin dropped).</summary>
    private static void SeatBandTop(MetricRowsState mr, MetricDivSeg sg)
    {
        var plainFirstDropped = mr.uaFirstBlock && QuirksDropsCellBlockMargins && sg.DirectChild && (sg.IsParagraph || sg.IsHeading);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_MROW") == "1")
            Console.Error.WriteLine($"[bandseat] first={mr.uaFirstBlock} dropped={plainFirstDropped} quirks={_quirksRowStrut} heading={sg.IsHeading} direct={sg.DirectChild} mt={sg.MarginTopPt:0.##} mb={sg.MarginBottomPt:0.##} prevMb={mr.sgPrevMb:0.##} segTop={mr.pageHeight - mr.segTop:0.##} fs={sg.FontSize} '{(sg.Text.Length > 16 ? sg.Text[..16] : sg.Text)}'");
        mr.segTop -= plainFirstDropped ? sg.PadTopPt /* a dropped first margin keeps the block's own top padding */ : sg.MarginTopPt < 0 ? sg.MarginTopPt + mr.sgPrevMb : Math.Max(sg.MarginTopPt, mr.sgPrevMb);
        mr.uaFirstBlock = false;
        mr.sgPrevMb = sg.MarginBottomPt;
    }

    /// <summary>The typography one band draws in: its probe cell, face, font resource and
    /// metrics, its size and line pitch, its synthetic-italic shear, and the width its lines wrap
    /// to inside the cell's own side padding.</summary>
    private readonly record struct BandTypography(MetricCell Probe, string Face, string Res,
        (double asc, double sum) Fm, double Fs, double LineH, double Shear,
        double WrapW, double CellPadL, double CellPadR);

    /// <summary>Resolves the band's typography. The overflowing image grew the content box, so
    /// the lines wrap at its width, exactly like the layout pass; the cell's own side padding
    /// insets its bands as it does its lines, and the block's own insets stand inside it.</summary>
    private static BandTypography MeasureBandTypography(MetricRowsState mr, MetricDivSeg sg,
        double boxW, Page page)
    {
        var sgFs = sg.FontSize ?? mr.mps.fontSize;
        var sgProbe = new MetricCell
        { Face = sg.Face, Bold = sg.Bold, FontSize = sg.FontSize, Italic = sg.Italic };
        var sgShear = SyntheticItalicShearFor(sgProbe.Face, sgProbe.Bold, sgProbe.Italic);
        if (sgShear > 0) sgProbe.Italic = false;
        var sgFmv = CellFm(mr.fm, sgProbe);
        var sgSum0 = sgFmv.sum <= 1.0 ? 1.2 : sgFmv.sum;
        var sgLineH = mr.paragraphCells
            ? CellLineOf(mr.mps, mr.stdSerif, mr.wrapperStacks, mr.hheaSum, mr.face, mr.fm, sgProbe, sgFs)
            : MetricLineHeight(sgFs, sgSum0);
        var cellPadL = mr.mc.PadLeft >= 0 ? mr.mc.PadLeft : mr.p;
        var cellPadR = mr.mc.PadRight >= 0 ? mr.mc.PadRight : mr.p;
        var sgWrapW = mr.mc.ImgBytes is not null
            && mr.mc.ImgWPt > boxW - 2 * mr.p
            ? mr.mc.ImgWPt
            : boxW - cellPadL - cellPadR - sg.PadLeft - sg.PadRight - mr.mc.BorderLeftW;
        return new BandTypography(sgProbe, CellFaceName(mr.face, mr.boldFace, sgProbe),
            BandFontRes(mr, sg, sgProbe, page), sgFmv, sgFs, sgLineH, sgShear,
            sgWrapW, cellPadL, cellPadR);
    }

    /// <summary>A class background fills the segment's band over the cell content width (the
    /// green bar: 97.5..497.5 × the class height, measured).</summary>
    private static void PaintBandBackground(MetricRowsState mr, MetricDivSeg sg, double boxW,
        Page page, double linesHeight)
    {
        if (sg.Bg is not { } sgBgC) return;
        var sgBandH = sg.LineBoxExact ? sg.LineBoxPt : Math.Max(sg.LineBoxPt, linesHeight);
        var sgIn = mr.mps.collapsedGrid ? 0.75 : 0.0;
        if (sgBandH <= 0) return;
        page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(mr.invc,
            $"q {sgBgC.R / 255.0:0.###} {sgBgC.G / 255.0:0.###} {sgBgC.B / 255.0:0.###} rg " +
            $"{mr.colX + sgIn:F2} {mr.segTop - sgBandH:F2} {boxW - 2 * sgIn:F2} {sgBandH:F2} re f Q\n")));
    }

    /// <summary>Draws the band's wrapped lines, breaking the page where the band runs out of
    /// room, and reports the page it ended on, the cursor it left and whether it broke. A break
    /// inside the band keeps two lines on either side of it — the orphans and widows of two the
    /// reference draws: a five-line paragraph with room for one line moves whole to the next
    /// page, the Words letter's 45-line paragraph leaves its last two lines to page 2.</summary>
    private static (Page page, double segLy, bool broke) DrawBandLines(MetricRowsState mr,
        MetricDivSeg sg, BandTypography bt, string[] sgLines, int[]? lineOffs, Page page, double boxW)
    {
        var segLy = mr.segTop;
        var sgRes = bt.Res;
        var broke = false;
        var paginates = BandCellPaginates(mr, mr.mc);
        var breakAt = paginates ? BandBreakLine(sgLines.Length, 0, segLy, bt.LineH, mr.marginBottom) : -1;
        for (var li = 0; li < sgLines.Length; li++)
        {
            var ln = sgLines[li];
            if (paginates && li == breakAt)
            {
                page = BreakBandToNextPage(mr, sg, page);
                sgRes = BandFontRes(mr, sg, bt.Probe, page);
                segLy = mr.pageHeight - mr.marginTop;
                broke = true;
                breakAt = BandBreakLine(sgLines.Length, li, segLy, bt.LineH, mr.marginBottom);
            }
            var sgDrop = MetricBaselineDrop(bt.Fs, bt.LineH, bt.Fm);
            var sgLw = (sg.Runs is not null ? MeasureStyledLine(mr.face, mr.boldFace, bt.Probe, bt.Fs, ln, lineOffs![li], sg.Runs)
                : MeasureFaceText(bt.Face, ln, bt.Fs)) + ShearOverhangPt(bt.Shear, bt.Fm, bt.Fs);
            var sgLx = sg.AlignRight || (!sg.AlignCenter && mr.mc.Align == HorizontalAlignment.Right)
                    ? mr.colX + boxW - bt.CellPadR - sg.PadRight - sgLw
                : sg.AlignCenter || mr.mc.Align == HorizontalAlignment.Center
                    ? mr.colX + bt.CellPadL + sg.PadLeft + (bt.WrapW - sgLw) / 2
                : mr.colX + mr.mc.BorderLeftW + bt.CellPadL + sg.PadLeft;
            if (ln.Length > 0)
            {
                if (sg.Runs is not null)
                    EmitStyledLine(mr, page, bt.Probe, bt.Fs, sgLx, segLy - sgDrop, ln, lineOffs![li], sg.Runs, bt.Shear);
                else EmitCellLineRuns(page, sgRes, bt.Fs, sgLx, segLy - sgDrop, ln, bt.Face, bt.Shear);
            }
            segLy -= bt.LineH;
        }
        return (page, segLy, broke);
    }
}
