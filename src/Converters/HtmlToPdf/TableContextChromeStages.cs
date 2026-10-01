using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The chain sheet's cell spacing and the table's own classes settle the row bands, the cell padding and the border the grid draws.</summary>
    private static void ApplyChainSpacingAndClassChrome(TableStyleConfig cfg, TableColumnModel colModel, Table table)
    {
        // Lifted nested tables render as real grids in place (measured into the row
        // plan, drawn by the slice pass); recursion levels inherit through the flag.
        table.NestedTableRender = cfg.liftNestedTables;
        // The declared cellspacing separates the rows VERTICALLY too: half a
        // spacing above and below each cell — the measured
        // row bands (row 1 = 69, flags = 44.3) hold once the reserve rows keep
        // their padding.
        // …and the side inset horizontally: the pills and grids keep a small white
        // gap off the row borders instead of touching them. The spacing also
        // separates each cell's BORDER BOX from the row band (HtmlRowSpacingPt).
        // Vertical decomposition of the row bands: the
        // border box insets HALF a spacing from the row band (the other half is
        // the visible white gap to the neighbouring row's border) and the content
        // keeps the UA pad inside the border — a section bar sits ~1 pt below its
        // border. Row heights follow from the tallest CONTENT
        // (row 1 = its Managers grid + these pads exactly).
        if (cfg.chainBase is not null && colModel.tblCellSpacingPt > 0 && table.DefaultCellPadding is null)
        {
            var vPad = colModel.tblCellSpacingPt / 2 + UaCellPadPt;
            table.DefaultCellPadding = new MarginInfo(ChainCellSideInsetPt, vPad,
                ChainCellSideInsetPt, vPad);
            table.HtmlRowSpacingPt = colModel.tblCellSpacingPt;
        }
        cfg.chainBorderSeparate = cfg.chainBase is not null && !cfg.tblCellSpacingDeclared && !((cfg.tblChainDecls is not null && cfg.tblChainDecls.TryGetValue("border-collapse", out var bcColl) && bcColl.Contains("collapse", StringComparison.OrdinalIgnoreCase)) || (cfg.tblStyle.TryGetValue("border-collapse", out var bcColl2) && bcColl2.Contains("collapse", StringComparison.OrdinalIgnoreCase)));
        cfg.chainSpacingPt = 0.0;
        if (cfg.chainBorderSeparate)
        {
            string? bsDecl = null;
            if (cfg.tblChainDecls is not null) cfg.tblChainDecls.TryGetValue("border-spacing", out bsDecl);
            if (bsDecl is null) cfg.tblStyle.TryGetValue("border-spacing", out bsDecl);
            if (bsDecl is not null)
            {
                var bsFirst = bsDecl.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (bsFirst.Length > 0 && ChainLenPt(bsFirst[0], cfg.cellFontSize) is > 0 and var bsPt)
                    cfg.chainSpacingPt = bsPt;
            }
        }
        if (cfg.chainSpacingPt > 0 && table.DefaultCellPadding is null)
        {
            table.DefaultCellPadding = new MarginInfo(cfg.chainSpacingPt / 2, cfg.chainSpacingPt / 2,
                cfg.chainSpacingPt / 2, cfg.chainSpacingPt / 2);
            table.HtmlCellSpacingBandPt = cfg.chainSpacingPt;
            // …and the draw insets each cell's border box by the same half band, so
            // the gap is real white space between the boxes and not thicker chrome.
            table.HtmlRowSpacingPt = cfg.chainSpacingPt;
        }
        else if (cfg.chainBorderSeparate && table.DefaultCellPadding is null)
            table.DefaultCellPadding = new MarginInfo(0, SeparateBorderSpacingPt / 2,
                0, SeparateBorderSpacingPt / 2);
        // a class rule can box the cells even when the table itself declares none
        foreach (var tcls in cfg.tblClasses)
        {
            Dictionary<string, string>? clsCellRule = null;
            if (!cfg.css.TryGetValue("." + tcls + " td", out clsCellRule))
                cfg.docCss?.TryGetValue("." + tcls + " td", out clsCellRule);
            if (clsCellRule is null || !clsCellRule.TryGetValue("border", out var clsBorder)) continue;
            var bm2 = Regex.Match(clsBorder, @"([\d.]+)\s*px");
            if (bm2.Success && double.TryParse(bm2.Groups[1].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var clsBwv) && clsBwv > 0)
            { table.HtmlCellBorderPt = clsBwv * PxToPt; table.HtmlCellBorderShared = true; break; }
        }
        if (cfg.tblBorderAttr is not null
            && double.TryParse(Regex.Match(cfg.tblBorderAttr, @"[\d.]+").Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var tblBw) && tblBw > 0)
        { table.HtmlCellBorderPt = tblBw * PxToPt; table.HtmlCellBorderShared = false; }
        table.HtmlAutoWidth = !cfg.tblTag.Success
            || !Regex.IsMatch(cfg.tblTag.Value, @"width\s*=\s*['""]?\s*[\d.]", RegexOptions.IgnoreCase);
    }

    /// <summary>The dialect flags the built table carries: its cell borders and padding, the sheet's paragraph margins and the UA cell boxes.</summary>
    private static void ApplyTableChromeFlags(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, Table table)
    {
        // collapsed borders share one stroke between neighbours: the row bills half its top + bottom pair
        // A UA-boxed grid's cell lines keep their own left margins (a list item's hanging indent).
        if (cfg.uaCellBoxes) table.HtmlWrapInsetsCellMargins = true;
        if (cfg.uaCellBoxes && cfg.breakAnywhereDoc) table.HtmlBreakAnywhere = true;
        if (cfg.collapsedGrid && cfg.makeCheckbox is not null)
        {
            table.HtmlWrapInsetsCellMargins = true;
            table.RowSpacingPt = 0;   // collapsed borders: no border spacing between the rows
        }
        if (cfg.hasBorder) table.DefaultCellBorder = new BorderInfo(BorderSide.Box, cfg.borderWidth, cfg.borderColor);
        // The border attribute frames the TABLE too (separate-borders model: the
        // outer 1px frame sits outside the cells' own borders, insetting the first
        // row by its width). The inline-face grid honours it; legacy corpora are
        // calibrated without the frame.
        if (cfg.hasBorder && cfg.inlineFaceRatio > 0 && cfg.outerBorder is null)
            table.Border = new BorderInfo(BorderSide.Box, cfg.borderWidth, cfg.borderColor);
        else if (cfg.cellSideBorder is not null) table.DefaultCellBorder = cfg.cellSideBorder;
        if (cfg.outerBorder is not null) table.Border = cfg.outerBorder;
        if (!(cfg.collapsedGrid && cfg.makeCheckbox is not null)) ApplyTableFrameStyle(table, cfg, cfg.tblBorderAttr);
        // The chain rule's `border` on the table is the grid's outer frame (CSS boxes the table, not its
        // cells), and its background fills the whole grid box behind the cells' own fills (measured:
        // `#right_column TABLE { BACKGROUND: #c8c8c8; BORDER: 1px solid #304742 }` frames the 560 px grid
        // and shows the grey through its 1 px cell spacing).
        if (cfg.tblChainDecls is not null && _ancestorGridSheet && cfg.cssAncestors is { Count: > 0 })
        {
            if (table.Border is null && cfg.tblChainDecls.TryGetValue("border", out var chBorder)
                && TryParseBorderShorthand("border:" + chBorder, "border") is (var chW, var chCol) && chW > 0
                && BorderStyleKeywordOf(chBorder) is not (null or "none" or "hidden"))
                table.Border = new BorderInfo(BorderSide.Box, chW, chCol ?? Color.Black);
            if (table.BackgroundColor is null
                && (cfg.tblChainDecls.TryGetValue("background-color", out var chBg) || cfg.tblChainDecls.TryGetValue("background", out chBg))
                && ParseCssColor(chBg) is { } chBgCol)
                table.BackgroundColor = chBgCol;
        }
        // A grid's OWN background paints its whole box, declared in its style attribute as readily as
        // in a rule: the header band of a cell-box sheet is the colour its table declares, and without
        // it the band is white paper.
        if (ps.sheetTdBoxRule && table.BackgroundColor is null
            && (cfg.tblStyle.TryGetValue("background-color", out var tblBg)
                || cfg.tblStyle.TryGetValue("background", out tblBg))
            && ParseCssColor(tblBg) is { } tblBgCol)
            table.BackgroundColor = tblBgCol;
        table.HtmlSheetParagraphMargins = ps.resetSheetGrid && ps.sheetPMarginTopPt > 0;
        if (cfg.pad > 0) table.DefaultCellPadding = new MarginInfo(cfg.padSide, cfg.padBottom, cfg.padSide, cfg.pad);
        // The UA stylesheet's own `td, th { padding: 1px }` — 0.75 pt above and below
        // every cell's content box. Only the vertical pair is taken: the horizontal
        // grid is already calibrated off the measured column footprints.
        // …and a grid laid out on the browser's box model — its face and line box taken
        // from the document's own CSS — carries that UA padding too, since the same
        // stylesheet supplies both.
        // (…unless the grid DECLARES its cellpadding - a cellpadding=0 grid pads nothing)
        // (…nor when the document sheet zeroes the cell padding - `td { padding: 0 }` beats the UA rule)
        else if ((cfg.uaCellBoxes || cfg.uaDocGrid) && !(cfg.uaCellBoxes && (cfg.tblCellPadAttr is not null || ps.sheetTdPadZero)))
            table.DefaultCellPadding = cfg.uaCellBoxes
                // (the browser's `td { padding: 1px }` pads every side of a UA-boxed cell)
                ? new MarginInfo(UaCellPadPt, UaCellPadPt, UaCellPadPt, UaCellPadPt)
                : new MarginInfo(0, UaCellPadPt, 0, UaCellPadPt);
        // ...and the declared cellspacing is real space round every cell of a UA-boxed grid: half
        // of it on each side (measured: `cellspacing="1" cellpadding="1"` rows pitch 9.75 =
        // the 7.5 line + 1.5 pads + 0.75 spacing).
        if (cfg.uaCellBoxes && colModel.tblCellSpacingPt <= 0 && !cfg.tblCellSpacingDeclared
            && !UaBorderCollapse(cfg))
            colModel.tblCellSpacingPt = UaCellSpacingPt;
        // (…and a border-spacing the grid's style or the sheet's table rule DECLARES beats the cellspacing
        //  attribute: the mailing's `table { border-spacing: 0 }` pitches its `cellspacing="2"` itemized grid
        //  on its lines alone, while the resume's collapsed grids keep their attributes)
        if (cfg.uaCellBoxes && UaSheetBorderSpacingPt(cfg) is { } sheetBsPt) colModel.tblCellSpacingPt = sheetBsPt;
        if (cfg.uaCellBoxes && colModel.tblCellSpacingPt > 0)
        {
            var half = colModel.tblCellSpacingPt / 2;
            var dcp = table.DefaultCellPadding;
            table.DefaultCellPadding = dcp is null
                ? new MarginInfo(half, half, half, half)
                : new MarginInfo(dcp.Left + half, dcp.Bottom + half, dcp.Right + half, dcp.Top + half);
        }
    }

    /// <summary>The empty Table the parse loop fills, with the borders, padding and dialect switches the sheet and the tag settled.</summary>
    private static Table NewContextTable(TableStyleConfig cfg, TableParseState ps, TableColumnModel colModel, string? cssRunFace)
    {
        return new Table
        {
            IsBordersIncluded = cfg.hasBorder || cfg.outerBorder is not null || cfg.cellSideBorder is not null,
            // Mixed run sizes: each line takes its own size's line box (see CssRunBoxes).
            // …and the reset-sheet grid stacks its lines on their CSS boxes (each line its own box, a
            // paragraph margin a spacer box), the way the browser lays its cells out.
            // …and a sheet that states its cells' box states their line boxes too: the grid pitches on
            // the CSS boxes its lines carry, not on the generator's own leading model.
            CssRunBoxes = cssRunFace is not null || ps.resetSheetGrid || (ps.sheetTdBoxRule),
            // A grid whose base face came from the document's own `body { font-family }`
            // draws its cells in that face through the Type0 path, with the face's real
            // kerned advances. Without this the cell writer falls back to the Standard-14
            // pair and silently retypesets the table in Helvetica while the prose around
            // it sets in the declared face.
            HonorCellTtfFaces = cfg.defaultCellFace is { Length: > 0 } || cfg.inlineFaceRatio > 0 || cfg.uaCellBoxes,
            InlineFaceGridRatio = cfg.inlineFaceRatio,
            // A grid pitching on the document face's own line box seats its baselines in
            // that box the browser's way too.
            UaSeatMetrics = cfg.uaDocGrid && cfg.defaultCellFace is { Length: > 0 } usf
                && WinMetricsFor(usf) is { } usm ? (usm.asc, usm.sum) : default,
            DwFormCells = cfg.dwFormCells,
            // …and takes the UA's own separate-borders `border-spacing: 2px` unless the
            // table declares a cellspacing of its own.
            // The over-declared grid dialect carries REAL border spacing too: the
            // reference pitches every row a full cellspacing lower (probed: pitch
            // 20.25 at cellspacing=1, 21.0 at 2, 19.5 at 0; leading gap included),
            // and the shipped-era template needs the accumulation the modern
            // engine also has.
            RowSpacingPt = cfg.dwFormCells ? colModel.cellSpacingPt
                : cfg.uaDocGrid ? (colModel.cellSpacingPt > 0 ? colModel.cellSpacingPt : UaCellSpacingPt)
                : cfg.overDeclaredDraw ? (colModel.cellSpacingPt > 0 ? colModel.cellSpacingPt
                    : cfg.tblCellSpacingDeclared ? 0 : UaCellSpacingPt)
                : 0,
            HtmlOverDeclaredDraw = cfg.overDeclaredDraw,
            // The markup's cell rule is sized into the column by HtmlCellBorderPt.
            CellBorderInPitch = false,
        };
    }
}

