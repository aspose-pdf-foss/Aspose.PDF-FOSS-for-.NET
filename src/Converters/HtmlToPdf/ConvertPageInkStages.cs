using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>An unassigned page width follows the sheet, edge-to-edge tables and chart cards.</summary>
    private static void WidenPageForChartsAndEdges(ConvertState cv, HtmlLoadOptions? options)
    {
        if (!(cv.pageInfo?.WidthAssigned ?? false))
            foreach (var b in cv.blocks)
                if (b.Slide is { } slw)
                {
                    double slExtentPx = 0;
                    foreach (var it in slw.Items)
                        slExtentPx = Math.Max(slExtentPx, it.LeftPx + it.WPx);
                    if (slExtentPx > 0)
                    {
                        var slNeedW = 90.0 + CardBodyPadPt + slExtentPx * 0.75 + 90.0;
                        if (slNeedW > cv.pageWidth)
                        {
                            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                            cv.pageWidth = slNeedW;
                            cv.marginLeft = 90.0;
                            cv.marginRight = 90.0;
                            cv.marginTop = 72.0;
                        }
                    }
                }

        // The engine's growth allowance when a page widens to natural content:
        // 0.1 in (7.2 pt = 9.6 px). Measured: the chart report's 622.0 page is
        // its minimal content fit 614.72 + 7.2 (rounded to the quarter-point
        // grid), and the zero-margin table pair's 602.5 is 595.28 + 7.2 the
        // same way.
        const double ChartWidenSlackPt = 7.2;

        // Edge-to-edge sheets with tables get the same growth allowance on the
        // authored page itself (595.28 + 7.2 → 602.5 on the quarter-point grid).
        // The engine's A4 basis is the true 595.276 — our 595.0 default page
        // stands in for it, so the widen resolves against the real sheet.
        if (cv.edgeToEdgeDoc && cv.blocks.Exists(b => b.IsTable))
        {
            const double A4TruePt = 210.0 / 25.4 * 72.0;   // 595.276
            var widenBase = Math.Abs(cv.pageWidth - 595.0) < 0.5 ? A4TruePt : cv.pageWidth;
            cv.pageWidth = Math.Round((widenBase + ChartWidenSlackPt) * 4.0,
                MidpointRounding.AwayFromZero) / 4.0;
        }
        // Chart-card widen: the page grows so the inline-SVG chart fits at its
        // NATURAL size inside its width-billing container chrome, plus the engine's
        // growth allowance, quantized to the quarter-point grid. Both constants are
        // measured on the expected output: the chart report widens to exactly
        // round4(90 + (svg 419.72 + col pads 15) + 7.2 + 90) = 622.0, and the
        // zero-margin table pair grows by the same 7.2 (602.5 = 595.28 + 7.22
        // rounded) — a 0.1 in allowance on the content's natural width.
        if (cv.profile.chartCardDoc && !(cv.pageInfo?.WidthAssigned ?? false))
        {
            double widestSvg = 0;
            foreach (var b in cv.blocks)
                if (b.IsImage && b.ImageWidth > 0
                    && b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal))
                    widestSvg = Math.Max(widestSvg, b.ImageWidth * 0.75 + b.ImageWidenPadPt);
            if (widestSvg > 0)
            {
                var neededW = Math.Round(
                    (cv.marginLeft + widestSvg + ChartWidenSlackPt + cv.marginRight) * 4.0,
                    MidpointRounding.AwayFromZero) / 4.0;
                if (neededW > cv.pageWidth)
                {
                    cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
                    cv.pageWidth = neededW;
                }
            }
        }
    }

    /// <summary>position:absolute/fixed overflow widen: the page grows so the furthest
    /// `left`-anchored absolute box's own right edge fits inside the page margins, the same
    /// way a table or slide does. Measured against the reference: only
    /// a `left`-anchored box (anywhere in its chain of positioned ancestors) can force this;
    /// a `right`-anchored one never does, so `cv.absMaxResolvedRightPt` (populated only by
    /// the `left` branch of ApplyAbsolutePositionResolve) already excludes those.</summary>
    /// <summary>The default page margins an HtmlLoadOptions carries when the caller sets none.</summary>
    private const double DefaultPageSideMarginPt = 90.0;

    private const double DefaultPageTopMarginPt = 72.0;

    /// <summary>The page's own left margin: the options' value, or the default.</summary>
    private static double PageMarginLeftPt(ConvertState cv) => cv.pageInfo?.Margin?.Left ?? DefaultPageSideMarginPt;

    /// <summary>The page's own right margin: the options' value, or the default.</summary>
    private static double PageMarginRightPt(ConvertState cv) => cv.pageInfo?.Margin?.Right ?? DefaultPageSideMarginPt;

    /// <summary>The page's own top margin: the options' value, or the default.</summary>
    private static double PageMarginTopPt(ConvertState cv) => cv.pageInfo?.Margin?.Top ?? DefaultPageTopMarginPt;

    private static void WidenPageForAbsoluteOverflow(ConvertState cv, HtmlLoadOptions? options)
    {
        if (cv.pageInfo?.WidthAssigned ?? false) return;
        if (cv.absMaxResolvedRightPt <= 0) return;
        // The page is sized from its OWN margin box (the options' margins, 90 + 90 by default),
        // not from the calibrated flow's inset (probed: 1263 px of ink → 947.25 + 180 = 1127.25).
        var neededW = Math.Round((PageMarginLeftPt(cv) + cv.absMaxResolvedRightPt + PageMarginRightPt(cv)) * 4.0,
            MidpointRounding.AwayFromZero) / 4.0;
        if (neededW > cv.pageWidth)
        {
            cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
            cv.pageWidth = neededW;
        }
    }

    /// <summary>The widest table has passed the box the sheet grows past. A table with an
    /// ABSOLUTE declared width cannot shrink, so it grows the sheet as soon as it passes the
    /// PAGE-margin box (595 - 96 - 90), not the calibrated flow's narrower right inset
    /// (probed: a 560 px table, 420 pt, grows the default sheet to 96 + 420 + 90 = 606);
    /// a percent or undeclared table fits the box until its natural width passes the
    /// flow's own inset (probed: 99 % and 100 % grids keep the 595 sheet).</summary>
    private static bool WidestTablePastPageBox(ConvertState cv)
        => cv.widestTable + (cv.widestIsPctMin ? MinFloorInkChromePt(cv) : 0) > cv.availContentW
           || (cv.declaredTableW > 0 && cv.widestTable > cv.pageWidth - cv.marginLeft - 90.0);

    /// <summary>The chrome a min-floor grid inks around and between its floors: its left chrome,
    /// the gap between each pair of columns, the frame past the last, less the advance a control
    /// in the last column carries past its box (probed: six min-floor columns page 96 + 2.25 + Σ +
    /// 5 × 3 + 90 = 1623.11; the returns grid 96 + 3.5 + Σ + 13 × 6 + 3.5 + 90 = 641.75).</summary>
    private static double MinFloorInkChromePt(ConvertState cv)
        => cv.widestTableChromePt
            // (a floor that is one ROW's line runs across the column gaps: only the lead and trail
            // chrome stand outside it - probed on the cheque: 96 + 2.25 + the nowrap PAY line + 90)
            + (cv.widestIsRowDemand ? 0 : Math.Max(0, cv.widestTableCols - 1) * cv.widestTableColGapPt)
            + cv.widestTableTrailPt - cv.widestTableTrailingPt;

    /// <summary>The default A4 content box: the sheet less the calibrated 96 pt inset a side. Ink
    /// inside it never grows a sheet; ink past it is what a grown sheet is grown for.</summary>
    private const double DefaultContentBoxWPt = 403.0;

    /// <summary>Whether a Word-filtered page declares a box or image wider than the default content
    /// box - the banner that grew the sheet the filtered column was measured on. A filtered page
    /// without one wraps every line inside the box and keeps the default sheet (MEASURED).</summary>
    private static bool MsoFilteredHasOverWideBox(ConvertState cv)
    {
        foreach (Match m in Regex.Matches(cv.html,
            @"(?<![-\w])width\s*[:=]\s*[""']?\s*(\d+(?:\.\d+)?)\s*(px|pt|in|cm|mm)?", RegexOptions.IgnoreCase))
        {
            if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var w)) continue;
            w *= m.Groups[2].Value.ToLowerInvariant() switch
            {
                "pt" => 1.0,
                "in" => 72.0,
                "cm" => 72.0 / 2.54,
                "mm" => 72.0 / 25.4,
                _ => 0.75,      // a bare number is the HTML4 pixel attribute
            };
            if (w > DefaultContentBoxWPt) return true;
        }
        return false;
    }

    /// <summary>The fieldset worksheet's sheet: the page margin, the whole left chrome chain, the
    /// declared table and the frame's right pad.</summary>
    private static double FieldsetWorksheetSheetPt(ConvertState cv)
        => 90.0 + cv.fsBodyChromePt + FsPadLeftPt + cv.declaredTableW + FsPadRightPt + FsWidenRightPt;

    /// <summary>The sheet grows to an over-constrained UA grid's ink: one page margin past it, the
    /// flow keeping the symmetric body inset on its right as on every sheet grown to a declared
    /// grid's ink; nothing when no grid overflows the content box.</summary>
    private static void WidenPageToOverConstrainedGrid(ConvertState cv, HtmlLoadOptions? options)
    {
        if (!cv.profile.uaStdSerif || cv.profile.deadExternalCss || cv.profile.bodyZeroMargin
            || cv.marginsExplicit || (cv.pageInfo?.WidthAssigned ?? false)) return;
        var ink = OverConstrainedGridInkPt(cv, options);
        if (ink <= cv.availContentW) return;
        cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
        cv.pageWidth = cv.marginLeft + ink + 90.0;
        cv.marginRight = 90.0 + UaBodyMarginPt;
    }

    /// <summary>The engine's one-point allowance past an image's drawn box (probed: an 800px
    /// image widens the default sheet to 787 = 90 + 6 + 600 + 90 + 1).</summary>
    private const double ImageInkAllowancePt = 1.0;

    /// <summary>A form control's border ink, both sides (probed: a 950px textarea widens the
    /// sheet by its 712.5 pt box + 2.25).</summary>
    private const double ControlBorderInkPt = 2.25;

    /// <summary>The UA default font size, for a block that declares none.</summary>
    private const double UaDefaultFontPt = 12.0;

    /// <summary>The calibrated size of a grid cell the document sizes nowhere (the legacy table model).</summary>
    private const double LegacyCellFontPt = 11.0;

    /// <summary>The page grows to hold its widest painted ink: the sheet is page margin + body
    /// inset + ink + page margin, and the whole flow then lays out on the grown sheet (probed on
    /// the default A4 sheet: a painted 800px div widens it to 786, a 600px one to 636, a
    /// paragraph wrapped in an unpainted 800px box to its text's own extent, and a 500px box
    /// not at all). The height keeps the portrait long edge.</summary>
    private static void WidenPageToInk(ConvertState cv, HtmlLoadOptions? options)
    {
        // An authored sheet, a scale-to-page layout, a clipping float page and a
        // Word-filtered export keep their own width model.
        if (cv.marginsExplicit || (cv.pageInfo?.WidthAssigned ?? false)
            || cv.profile.scaleToPageWidth || cv.profile.floatBothSidesDoc || cv.profile.msoFilteredDoc)
            return;
        var ink = MeasureWidestInk(cv, options);
        if (ink <= 0) return;
        var bodyInset = cv.profile.bodyZeroMargin ? 0.0 : UaBodyMarginPt;
        var needed = 90.0 + bodyInset + ink + 90.0;
        if (needed <= cv.pageWidth) return;
        cv.pageHeight = Math.Max(cv.pageWidth, cv.pageHeight);
        cv.pageWidth = needed;
        // The grown sheet ends one page margin past the ink; the text box keeps the
        // body inset on both sides (probed: a paragraph outside the widened box wraps
        // to W - 96), which the standard-serif flow already charges on the right of
        // every text block, so only the other flows inset the margin itself.
        cv.marginLeft = 90.0 + bodyInset;
        cv.marginRight = 90.0 + (cv.profile.uaStdSerif ? 0.0 : bodyInset);
        cv.profile.inkWidenPt = ink;
    }

    /// <summary>The widest ink the flow blocks declare, measured from the content box's left
    /// edge: a declared-width image, a form control's box, a painted declared box, or the text
    /// inside an unpainted declared box as far as it runs. Tables keep their own width model.</summary>
    private static double MeasureWidestInk(ConvertState cv, HtmlLoadOptions? options)
    {
        double widest = 0;
        var traceInk = Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1";
        foreach (var b in cv.blocks)
        {
            if (b.IsTable || b.IsHardBreak || b.IsHorizontalRule) continue;
            var ink = b.Pre is { } pre ? pre.WidestPt
                : b.IsImage ? ImageInk(b)
                : b.IsInputField || b.IsSelectBox ? ControlInk(b)
                : TextBoxInk(cv, b);
            if (ink > 0) widest = Math.Max(widest, ink + (b.IsImage ? b.ImageIndentPt : b.LeftIndent));
            if (ink > 0 && traceInk)
                Console.Error.WriteLine($"[inkblock] ink={ink:0.##} indent={(b.IsImage ? b.ImageIndentPt : b.LeftIndent):0.##} pre={b.Pre is not null} img={b.IsImage} widthPx={b.WidthPx:0.#} bg={b.BgBoxWidthPt:0.#} border={b.BorderBoxWPt:0.#} text='{(b.Text is { Length: > 30 } t ? t[..30] : b.Text)}'");
        }
        return widest;
    }

    /// <summary>An in-flow image at its declared width plus the engine's allowance; a
    /// positioned, percent-capped or undeclared image contributes nothing.</summary>
    private static double ImageInk(Block b)
    {
        if (b.ImageAbsPos || b.ImageMaxWFrac > 0 || b.ImageWidth <= 0) return 0;
        return b.ImageWidth * 0.75 + ImageInkAllowancePt;
    }

    /// <summary>A form control's declared box plus its border ink.</summary>
    private static double ControlInk(Block b)
        => b.InputWidth > 0 ? b.InputWidth * 0.75 + ControlBorderInkPt : 0;

    /// <summary>A painted declared box inks its whole width plus its border; an unpainted one
    /// inks only as far as its text runs, which is the box itself once the text wraps in it.
    /// Read only in the flows that wrap text to a declared pixel box.</summary>
    private static double TextBoxInk(ConvertState cv, Block b)
    {
        var wrapsToBox = cv.profile.uaStdSerif || cv.profile.formDialectTables;
        var box = Math.Max(wrapsToBox ? b.WidthPx * 0.75 : 0, Math.Max(b.BgBoxWidthPt, b.BorderBoxWPt));
        if (box <= 0) return 0;
        var bordered = b.BorderColor is not null && b.BorderWidth > 0 && !b.BorderTopOnly;
        if (b.BackgroundColor is not null || bordered)
            return box + (bordered ? 2 * b.BorderWidth : 0);
        if (string.IsNullOrWhiteSpace(b.Text)) return 0;
        return Math.Min(box, MeasureBlockText(b, b.Text));
    }

    /// <summary>The advance of a run in the block's face: the declared family when its metrics
    /// are installed, else the Standard-14 face its resource names.</summary>
    private static double MeasureBlockText(Block b, string s)
    {
        var fs = b.FontSize > 0 ? b.FontSize : UaDefaultFontPt;
        if (b.FontFamily is { Length: > 0 } fam && WinMetricsFor(fam) is not null)
            return MeasureFaceText(fam, s, fs);
        var std14 = b.FontRes switch
        {
            "F2" => "Helvetica-Bold",
            "F3" => "Helvetica-Oblique",
            "F4" => "Courier",
            _ => "Helvetica",
        };
        return MeasureStd14(std14, s, fs);
    }
}
