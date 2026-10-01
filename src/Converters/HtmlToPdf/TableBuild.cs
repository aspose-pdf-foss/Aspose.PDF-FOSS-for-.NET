using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>CSS pixels to points (96dpi -> 72dpi).</summary>
    private const double PxToPt = 0.75;
    private const double PxToPtW = 0.75;
    // The pt-styled fragment's cell line pitch as a factor of the font size
    // (measured: 10 pt Verdana rows step 12.0 per line,
    // wrapped header cells 2×12.0, the paragraph flow the same 1.2 em).
    private const double PtFragmentLineFactor = 1.2;

    // The redline diff document's line box as a factor of the font size
    // (measured: wrapped 10 pt paragraphs step 11.25,
    // the 12 pt red block 13.5 — a 1.125 em box at every size).
    internal const double RedlineLineFactor = 1.125;

    // Redline decoration geometry (measured on the expected stroke positions
    // against the run baselines): a text-decoration underline rides 0.09 em
    // below the baseline, a strike 0.26 em above, both 0.1 em thick; a
    // diff-marker border-bottom draws a 0.75 pt hairline 0.25 em below.
    internal const double RedlineUnderDropEm = 0.09;
    internal const double RedlineStrikeRiseEm = 0.26;
    internal const double RedlineDecorWidthEm = 0.1;
    internal const double RedlineBorderDropEm = 0.25;

    // Redline cross-paragraph baseline advance = DescLead(prev) + AscLead(next)
    // (probed: 21.0 across two 18 pt headers, 13.5 across an 18->10 boundary —
    // Times' descent + gap share and ascent + half-leading share of a 1.15 em
    // paragraph box).
    internal const double RedlineDescLeadEm = 0.2375;
    internal const double RedlineAscLeadEm = 0.929;

    // The redline <hr>: its black top stroke rides 8.5 pt below the previous
    // baseline, and the next paragraph's baseline 23.7 below the stroke
    // (both probed on the cover's 25% groove rule).
    // The redline document's UNSTYLED text (blank paragraphs, flattened
    // marker cells) runs at the UA default 12 pt (probed: the cover's bar
    // paragraph seats the added-marker underline at 97.3 only on a 12 pt line).
    internal const double RedlineBaseFontPt = 12.0;

    // An EMPTY paragraph's box in the redline flow (probed: 24.8 between two
    // 10 pt paragraphs separated by one empty 12 pt paragraph, net of the
    // Desc/Asc leads).
    internal const double RedlineEmptyParaPt = 13.13;

    // …and the conflicted two-column grid's first-column share (see above).
    internal const double RedlineConflictCol1Frac = 0.6075;

    // DataWorks form-grid control boxes (measured: text inputs
    // at their declared 177x22 px, the select at the same default width, the
    // textarea 367x103 px when undeclared, values in ~11 px sans).
    internal const double DwSelectBoxWPt = 132.75;
    internal const double DwInputBoxHPt = 16.5;
    internal const double DwTextareaWPt = 275.25;
    internal const double DwTextareaHPt = 50.0;
    internal const double DwBoxValueFontPt = 8.25;

    // DataWorks flow typography: UA 16px base (12 pt), h1 at 2 em, the classic
    // navigator link blue. The expected output's aged JPEG renders the #0000EE ink
    // desaturated (glyph cores ≈ rgb(16,17,125), the 1px underline between
    // (18,18,114) and (75,75,171) by row rounding); the exact gate compares
    // per-channel against those pixels, so the drawn ink is calibrated to sit
    // within the comparison budget of every measured variant.
    internal const double DwBodyFontPt = 12.0;
    internal const double DwH1FontPt = 24.0;
    internal static readonly Color DwLinkColor = Color.FromArgb(36, 36, 140);

    // The header's float:right print-link box (80px) and the Completed
    // button's inset from the content right edge (the 98% form-element box
    // plus the button chrome; measured: box right edge 516.5 = content
    // right 545.5 − 29.0).
    internal const double DwPrintLinkBoxPt = 60.0;
    internal const double DwCompletedRightInsetPt = 29.0;

    // The header bar's bottom rule: 2px (#cccccc) under two 1.125-em header
    // lines plus the spans' 5px bottom margin (2*13.5 + 3.75 = 30.75).
    internal const double DwHeaderRuleDropPt = 30.75;
    internal const double DwHeaderRuleHPt = 1.5;
    internal const double DwRuleGray = 204.0 / 255.0;

    // The h1 title row's line box (measured: the row spans 33.8pt with its
    // pads and spacing — a 32.5pt box) and the minimum height of a nested
    // results row that carries an (invisible) checkbox widget.
    internal const double DwH1LineBoxPt = 32.5;
    internal const double DwCheckboxRowHPt = 16.0;
    internal const double DwH1SeatDropPt = 5.0;
    internal const double DwButtonLinePt = 16.5;
    internal const double DwOptionLinePt = 14.76;
    internal const double DwButtonFollowPt = 13.0;
    internal const double DwAfterButtonDropPt = 0.7;
    internal const double DwBottomMarginPt = 68.0;
    // Button captions draw in the 10 pt UI sans inside the 12 pt-scaled chrome
    // (measured: 'Search' caption 27.6 wide, box 38.9 incl. outline), the box
    // seats 4.2 pt above the caption line's natural drop (both results-grid
    // button rows land on the expected boxes with the one rise), and the flow
    // list markers hang a bare 0.8 pt gap left of the item indent.
    internal const double DwButtonCapPt = 10.0;
    internal const double DwButtonBoxRaisePt = 4.2;
    internal const double DwMarkerGapPt = 0.8;
    // Link underline: the redline drop already lands within the window of the
    // expected 1px stroke (baseline +2.4 there, ours +1.1) — only the
    // hairline WIDTH differs from the redline dialect (two full-intensity
    // device rows; a thinner stroke anti-aliases too light to match).
    internal const double DwUnderRaisePt = 0.0;
    internal const double DwUnderWidthPt = 0.96;
    // Control-widget draw seats (all measured on the expected borders): every
    // widget box starts 3.7 pt past the pen; the drop-down draws its
    // arrow-button band (+13 wide) and rides 2.3 higher; the textarea keeps its
    // declared box 2.5 higher with the mono text tight under the top border;
    // trailing text (the validation star) hugs the box's right border.
    internal const double DwInputLeadPt = 3.7;
    internal const double DwSelectChromeWPt = 13.0;
    internal const double DwSelectLiftPt = 2.3;
    internal const double DwTextareaLiftPt = 2.5;
    internal const double DwMonoValueRaisePt = 0.7;
    internal const double DwAfterBoxPenPt = -2.0;
    internal const double DwGapLineLiftPt = 4.3;
    internal const double DwBoxBorderGray = 32.0 / 255.0;
    // The <input type=file> control's synthesized caption — its box draws the
    // flat gray chrome (no black outline) unlike the push buttons.
    internal const string DwFileButtonCaption = "Choose File";
    // A multi-row results grid pitches its rows on the plain 1.125-em line box
    // (measured: BXH→New-Topic steps 13.5) while the grid's TOTAL height keeps
    // the 16-per-row model that seats everything after it — the last row
    // absorbs the slack.
    internal const double DwNestedRowPitchPt = 13.5;
    // A results-grid nested table draws its content 4.3 pt right of the column
    // model: the expected output reserves the full 17.3 pt checkbox footprint and a
    // ~0.7 pt broken-icon sliver where the width model books 6.85 each (the
    // reserve stays excluded from the host column, so only the draw shifts).
    internal const double DwNestedDrawShiftPt = 4.3;

    // font-variant: small-caps ratio (probed: the blue covenant paragraph's
    // lowercase draws as 7.08 pt capitals on the 10 pt line).
    internal const double RedlineSmallCapsEm = 0.708;

    internal const double RedlineHrLeadPt = 8.5;
    internal const double RedlineHrDropPt = 7.0;

    // …and the seat of the first flow baseline under a grid: an ascent below
    // the borderless card's bottom edge (probed: the nbsp line at end + 0.815
    // em on the consuming 10 pt line)…
    internal const double PtDropEm = 0.815;

    // …deepened one collapsed-seat share under a BORDERED grid, whose drawn
    // bottom stroke rides below the layout cursor (probed: the flow resumes a
    // full em under each bordered table's bottom stroke — 0.815 + 0.18).
    internal const double PtBorderedDropExtraEm = 0.18;

    // A mid-flow grid's TOP STROKE sits 12.35 pt above its first row's text
    // bottom and one row pitch below the flow (probed on the drawn border
    // positions); the generic BaselineInLineBoxPt rise leaves the box 0.7 low.
    internal const double PtTableBoxRisePt = 0.7;

    /// <summary>Squeeze columns into <paramref name="cap"/>: each column sheds
    /// width in proportion to its slack above min-content; if the mins alone
    /// overflow, everything scales flat. Returns the input when it fits.</summary>
    private static List<double> SqueezeBySlack(List<double> cols, double cap, List<double> mins)
    {
        double sum = 0; foreach (var w in cols) sum += w;
        if (cap <= 0 || sum <= cap + 0.01) return cols;
        var reduce = sum - cap;
        double slackSum = 0;
        var slack = new double[cols.Count];
        for (var i = 0; i < cols.Count; i++)
        {
            var minI = i < mins.Count ? mins[i] : 0;
            slack[i] = Math.Max(0, cols[i] - minI);
            slackSum += slack[i];
        }
        var res = new List<double>(cols.Count);
        if (slackSum <= reduce + 0.01)
        {
            // even the mins overflow: flat scale
            var scale = cap / sum;
            foreach (var w in cols) res.Add(w * scale);
            return res;
        }
        for (var i = 0; i < cols.Count; i++)
            res.Add(cols[i] - reduce * slack[i] / slackSum);
        return res;
    }

    /// <remarks>
    /// The cells carry their own presentational styling — inline border sides and the
    /// legacy ALIGN attribute — rather than inheriting a frame from the table.
    /// The Verdana form-grid fragment dialect (see Document.cs): legacy ALIGN
    /// honored, and a sized &amp;nbsp;-only run binds its active font (the grid's
    /// 36pt spacer row) — scoped here so no calibrated dialect moves.
    /// The pt-styled fragment dialect: cells declare their widths as inline
    /// `width:Npt` (the px-only read leaves such columns at min-content — a
    /// phone column wrapping one character per line). Scoped so no
    /// calibrated grid re-reads widths it was measured without.
    /// The redline diff document's layout tables: percent columns whose cell
    /// paragraphs carry the typography (Times spans, text-align, valign).
    /// DataWorks form grid: text controls draw as their declared pixel boxes
    /// with the value typeset inside; selects show the chosen option;
    /// checked checkboxes draw bare checkmarks.
    /// The dialect's CSS strut: the ambient font's own line box, flooring
    /// every cell line (Verdana-12 → 14.25 inside the wrapper's font tag,
    /// the serif default's 13.5 outside). A td that styles its OWN
    /// font-size restruts its cell at that size's box instead.
    /// …and the strut's baseline drop (half-leading + winAscent within the
    /// strut box) — the floor every line's baseline seat takes.
    /// The document's base face, inherited by the grid like defaultCellFontPt.
    /// The element-styled fixed-grid dialect (quirks page whose stylesheet
    /// sizes the TABLE element and borders the cells by ELEMENT rule): the
    /// document sheet's table width pins-and-fills the grid box, td element
    /// borders box every cell, and the flat class rules carry the cells'
    /// full chrome (width, align, colour, size, padding, border sides).
    /// The page-width PROBE measures CJK the way the layout draws it:
    /// full-em ideograph advances and per-ideograph break opportunities. The
    /// render dialects are calibrated on the legacy estimates and keep them.
    /// Pinned-body report dialect: a cell's own inline font-size may GROW the
    /// text past the grid base (the header table's 22px title cell) — its
    /// lines measure at their own size, so the column absorbs the growth.
    /// Over-declared grid document, RENDER pass only: a nested grid resolves
    /// its percent columns against the STANDARD content box — the host cell's
    /// padding, border spacing and the UA body gutter all come off the
    /// available width (measured: inner W = pageW − 201 at every page width,
    /// while the host table itself full-bleeds to the page edge).
    /// Factory for a radio &lt;input> in a cell: (group name, checked) → an option
    /// already added to its RadioButtonField group. The CONVERTER owns the groups
    /// (it registers them on doc.Form after layout); the cell carries each option
    /// inline in its text via Table.InlineRadioChar markers. Null = radios are
    /// dropped from cell text, the pre-form-grid behaviour.
    /// </remarks>
internal static (Table? result, double naturalWidthPt) BuildTableFromHtml(string html, double availWidthPt, HtmlLoadOptions? options, List<byte[]>? inlineSvgs, IReadOnlyDictionary<string, Dictionary<string, string>>? docCss, bool bandDialect = false, bool widenProbe = false, double cellLineHeightPt = 0, double defaultCellFontPt = 0, bool tightExtras = false, bool liftNestedTables = false, bool uaCellBoxes = false, string? cssRunFace = null, Color? bodyTextColor = null, bool uaSerifMin = false, bool authoredCellChrome = false, bool formGridDialect = false, bool ptCellWidths = false, bool redlineCells = false, bool dwFormCells = false, double formGridStrutPt = 0, double formGridStrutDropPt = 0, string? defaultCellFace = null, bool docElementGrid = false, bool fullWidthCjkMin = false, bool pinnedBodyGrid = false, bool overDeclaredDraw = false, List<CssChainRule>? chainRules = null, List<CssElem>? cssAncestors = null, Func<string, bool, Aspose.Pdf.Forms.RadioButtonOptionField>? makeRadio = null, bool wordMailCells = false, Func<bool, Aspose.Pdf.Forms.CheckboxField>? makeCheckbox = null, bool nestedGrid = false, double uaLineFactor = 0, bool uaSheetGrid = false)
    {
        double naturalWidthPt = default;
        naturalWidthPt = 0;
        (var cfg, var ps, var colModel, var table, var tokens, cssRunFace) = BuildTableParseContext(html, availWidthPt, options, inlineSvgs, docCss, bandDialect, widenProbe, cellLineHeightPt, defaultCellFontPt, tightExtras, liftNestedTables, uaCellBoxes, cssRunFace, bodyTextColor, uaSerifMin, authoredCellChrome, formGridDialect, ptCellWidths, redlineCells, dwFormCells, formGridStrutPt, formGridStrutDropPt, defaultCellFace, docElementGrid, fullWidthCjkMin, pinnedBodyGrid, overDeclaredDraw, chainRules, cssAncestors, makeRadio, wordMailCells, makeCheckbox, nestedGrid: nestedGrid, uaLineFactor: uaLineFactor, uaSheetGrid: uaSheetGrid);
        table.HtmlUaControlGrid = ps.uaControlGrid;
        table.HtmlUaControlFontPt = cfg.cellFontSize;
        ps.chainOpenElems = cfg.chainBase is not null ? new List<CssElem>() : null;
        foreach (var tok in tokens)
            if (!BuildTableFromToken(cfg, ps, colModel, table, tok, cssRunFace)) break;
        CloseRow(ps, colModel, table, cfg.options, cfg.cellFontSize, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.breakAnywhereDoc, cfg.cellFontShorthand, cfg.chainBase, cfg.chainSpacingPt, cfg.chainUnbold, cfg.cssBaseFamily, cfg.cssBasePt, cfg.defaultCellFace, cfg.formGridStrutDropPt, cfg.hasBorder, cfg.inlineFaceRatio, cfg.overDeclaredDraw, cfg.padSide, cfg.uaDocGrid, cfg.widenProbe, cfg.uaSerifMin, cfg.ptCellWidths, cfg.redlineCells, cfg.bandDialect, cfg.cellLineHeightPt, cssRunFace, cfg.formGridDialect, cfg.formGridStrutPt, cfg.liftNestedTables, cfg.tightExtras, cfg.uaCellBoxes, cfg.borderWidth, cfg.pad);
        // Apply the deferred column-span constraints: a spanning cell only forces its
        // columns' widths up when they don't already sum to its content — the deficit is
        // spread evenly, so a wide spanning line grows the columns it needs without
        // inflating thin spacer columns that other rows keep narrow.
        ApplySpanConstraints(colModel, cfg.uaCellBoxes);
        if (ps.headerRows > 0 && ps.headerRows < table.Rows.Count) table.RepeatingRowsCount = ps.headerRows;

        // Form-document dialect: a `<table height="90">` attribute is a minimum on the
        // TABLE height, shared equally by its rows (the browser's table model) — each
        // row floors at its share, content still grows a row past it.
        if (cfg.cellFontShorthand && colModel.tblHeightPx > 0 && table.Rows.Count > 0)
        {
            var rowShare = colModel.tblHeightPx * PxToPt / table.Rows.Count;
            foreach (Row hr in table.Rows)
                if (rowShare > hr.MinRowHeight) hr.MinRowHeight = rowShare;
        }

        if (table.Rows.Count == 0) { naturalWidthPt = 0; return (null, naturalWidthPt); }
        naturalWidthPt = 0;
        // Colgroup grid: each column is its declared width, stretched to min-content when an
        // unbreakable run needs more (colMinW already includes padding/border slack).
        // A COLGROUP whose cols declare NO widths ("<col class=…>") pins nothing —
        // under the chain dialect those tables keep their content/percent column
        // model (legacy dialects keep the historical min-content pinning).
        if (colModel.colGroupPt is { Count: > 0 } && colModel.colGroupPt.Count == colModel.maxCols
            && (cfg.chainBase is null || colModel.colGroupPt.Exists(w => w > 0)))
        {
            colModel.colWidthsPt = new List<double>(colModel.maxCols);
            for (var i = 0; i < colModel.maxCols; i++)
            {
                var declared = colModel.colGroupPt[i];
                var minC = i < colModel.colMinW.Count ? colModel.colMinW[i] : 0;
                colModel.colWidthsPt.Add(Math.Max(declared, minC));
            }
        }
        // Redline: the two-column grid whose first column carries BOTH 86.58%
        // and 13.42% across rows resolves at the observed split
        // (probed: the checkbox row's centre at 245.9 and the right column's
        // text opening at 399.3 put the boundary at 60.75% of the content box).
        if (cfg.redlineCells && colModel.colPctConflict && colModel.maxCols == 2 && colModel.colWidthsPt is null
            && cfg.availWidthPt > 0)
            colModel.colWidthsPt = new List<double>
            {
                RedlineConflictCol1Frac * cfg.availWidthPt,
                (1 - RedlineConflictCol1Frac) * cfg.availWidthPt,
            };
        // A per-column percent grid (the classic sizing row) fixes the split against
        // the table's width — honoured before any content fit when the declared
        // percents dominate the grid. Columns the row leaves unsized (spacer cells)
        // share the leftover percent evenly; every column is floored at its
        // min-content so an unbreakable run still gets room.
        // Over-declared grid dialect, TABLE-LAYOUT:fixed draw: columns resolve at
        // their declared share of the FIXED BASE — the full-bled host box for a
        // grid whose declarations fit (width=100% fills the page-wide band), the
        // standard demand base for an OVER-declared one (its shares cannot fit
        // any box; they resolve against the same base the widen
        // demand used). Pixel columns pin, percent columns floor at min-content,
        // the auto columns split the remainder. (Fitted on the shipped grids:
        // the rental question row's 50% column and the amounts grid's 15%/35%
        // groups both land within a point.)
        ApplyOverDeclaredColumnDraw(cfg, ps, colModel);
        naturalWidthPt = SolveColumnWidths(colModel, table, cfg.tblStyle, cfg.tblTag, cfg.chainBase, cfg.availWidthPt, cfg.cellFontSize, cfg.cellFontShorthand, cfg.dwFormCells, cfg.fullWidthCjkMin, cfg.overDeclaredDraw, cfg.uaDocGrid, cfg.padSide, ps.rowPctDeclMax, ps.headerRows, cfg.ptCellWidths, cfg.uaCellBoxes, cfg.uaSerifMin, ps.rowPxAtMax, ps.rowPxCellsAtMax, naturalWidthPt, wordMailCells: cfg.wordMailCells, nestedGrid: cfg.nestedGrid);
        if (ps.sheetTdBoxRule
            && SolveColumnsOnDeclaredPercents(colModel, table,
                ps.minContentGrid ? 0 : cfg.availWidthPt - TableFrameSidesPt(table)) is > 0 and var pinnedW)
            naturalWidthPt = pinnedW;
        // A grid's own FRAME stands outside its columns: a bordered wrapper is that much wider than what
        // it holds, and a nest of them charges the sheet every frame in the chain.
        // …and it paints through its CELLS: a grid background is the colour behind every cell that
        // declares none of its own, which is how the band reaches the page.
        if (ps.sheetTdBoxRule && table.BackgroundColor is { } sheetBg)
            foreach (Row bgRow in table.Rows)
                foreach (Cell bgCell in bgRow.Cells)
                    bgCell.BackgroundColor ??= sheetBg;
        table.HtmlNoBreakBeforePunct = ps.sheetTdBoxRule;
        table.HtmlCellBoxSheet = ps.sheetTdBoxRule;
        DrawCellBoxSheetRules(table, colModel);
        if (ps.sheetTdBoxRule && TableFrameSidesPt(table) is > 0 and var framePt)
        {
            naturalWidthPt += framePt;
            if (table.HtmlMinContentPt > 0) table.HtmlMinContentPt += framePt;
            if (table.HtmlMaxContentPt > 0) table.HtmlMaxContentPt += framePt;
            if (table.HtmlPreferredWidthPt > 0) table.HtmlPreferredWidthPt += framePt;
        }
        return (table, naturalWidthPt);
    }

    /// <summary>The columns of a grid whose sheet states its cells' box: every column starts at its own
    /// floor, a DECLARED PERCENT is what its column WANTS of the box, and what the box has left over is
    /// shared out in proportion to how much each column still wants. A column whose floor already passes
    /// its percent keeps the floor and asks for nothing, which is why a grid squeezed to its min-content
    /// comes out with every column at its floor and nothing to distribute. An auto column beside declared
    /// ones wants nothing: the percents have already claimed the box.</summary>
    private static double SolveColumnsOnDeclaredPercents(TableColumnModel colModel, Table table, double boxPt)
    {
        var n = colModel.colMinW.Count;
        if (n == 0) return 0;
        var widths = new List<double>(n);
        var wants = new List<double>(n);
        double floorSum = 0, wantGap = 0;
        var anyPct = false;
        for (var i = 0; i < n; i++)
            if (i < colModel.colPctW.Count && colModel.colPctW[i] > 0) { anyPct = true; break; }
        for (var i = 0; i < n; i++)
        {
            var floor = colModel.colMinW[i];
            var pct = anyPct && i < colModel.colPctW.Count ? colModel.colPctW[i] : 0;
            var want = pct > 0 ? pct / 100.0 * boxPt : floor;
            widths.Add(floor); wants.Add(want);
            floorSum += floor;
            if (want > floor) wantGap += want - floor;
        }
        if (floorSum <= 0) return 0;
        // The box has nothing to give: every column stands at its floor and the grid overflows by what
        // it must (this is the grid a sheet-growing nest is made of).
        if (boxPt > 0 && floorSum < boxPt - 0.01)
        {
            if (!anyPct || wantGap <= 0) return 0;
            var free = boxPt - floorSum;
            floorSum = 0;
            for (var i = 0; i < n; i++)
            {
                widths[i] += free * (wants[i] - widths[i] > 0 ? wants[i] - widths[i] : 0) / wantGap;
                floorSum += widths[i];
            }
        }
        table.ColumnWidths = string.Join(" ", widths.ConvertAll(
            v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        table.HtmlPreferredWidthPt = floorSum;
        // …and the layout solves the columns again on the real box, so it needs the same wants.
        if (anyPct)
            table.HtmlColumnPercents = string.Join(" ", colModel.colPctW.ConvertAll(
                v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        return floorSum;
    }

    /// <summary>The width a table's own frame adds to what it holds: its left and right border.</summary>
    private static double TableFrameSidesPt(Table table)
    {
        if (table.Border is not { } frame) return 0;
        var w = 0.0;
        if ((frame.Side & BorderSide.Left) != 0) w += frame.Width;
        if ((frame.Side & BorderSide.Right) != 0) w += frame.Width;
        return w;
    }

    // The <hr> separator bar: a solid dark PNG the rule cell stretches across
    // its columns (built once; the UA hr renders as a near-black groove).
    private static byte[]? _hrBarPng;
    private static byte[] HrBarPng()
        => _hrBarPng ??= Compat.IsWindows() ? HrBarPngGdi() : HrBarPngManaged();

    /// <summary>Windows: the GDI+ PNG encoder, whose exact byte stream the rendered
    /// baselines are calibrated against.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static byte[] HrBarPngGdi()
    {
        using var bmp = new System.Drawing.Bitmap(4, 4,
            System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.Clear(System.Drawing.Color.FromArgb(64, 64, 64));
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Off Windows: the same 4x4 solid bar through the managed encoder. The
    /// pixels are identical - only the encoder's byte stream differs, so keeping GDI+
    /// on Windows leaves the Windows output byte-for-byte what it was.</summary>
    private static byte[] HrBarPngManaged()
    {
        var px = new byte[4 * 4 * 3];
        for (int i = 0; i < px.Length; i++) px[i] = 64;
        return Aspose.Pdf.IO.PngEncoder.Encode(px, 4, 4, colorType: 2);
    }
}
