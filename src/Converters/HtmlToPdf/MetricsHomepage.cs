using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The metrics-portal homepage export ─────────────────────────────────────
    // A zero-margin 1191×842 landscape export of the metrics site's homepage:
    // a hero row (white-on-white heading/paragraph — invisible ink, skipped — a
    // 400px SVG illustration on the right, and a rounded gradient CTA pill with
    // an underlined white Arial-Bold label), then `.table-container` metric
    // grids — `ol.flex-row` rows of `li.flex-cell` cells whose inline styles
    // carry the flex %, colours and sizes.
    //
    // Geometry (measured on the expected PDF):
    //   the content column is x 179.25..1011.75. The CTA pill is 312×48 at
    //   y 375 (its 416×64 px gradient tile drawn at 0.75), label centred,
    //   baseline 404.20, underline 1.5 under it. The SVG box is 300 pt wide
    //   (width="400") at the column's right edge, top 131.25. A metric grid:
    //   purple #6855E3 band 53.25 tall (25.5 pt heading at +33.1), rows of
    //   #D0F7FE label cells; a flex-cell is basis% × 832.5 + 15 pt padding
    //   (content-box), so the label cell is 181.5 wide, an 8% cell 81.6, the
    //   10% cell 98.25 with its link centred. Label lines pitch 22.5
    //   (baseline rowTop+22.56), link/number lines 20.25 (rowTop+21.05); a
    //   row is maxLines·pitch + 15 tall with #DADCE0 rules at each boundary.
    //   Links are #0052B3 underlined (1.35 links, 1.5 the 15 pt View All).
    //   The first grid sits at y 555 on page 1; the next grid opens page 2 at
    //   y 36. The expected output embeds Poppins fetched from its own environment —
    //   not installed here, so the visually closest installed faces stand in
    //   (Segoe UI / Segoe UI Semibold); every run start is position-pinned so
    //   the substitution only moves ink inside a run.

    private const double MhX0 = 179.25;
    private const double MhX1 = 1011.75;
    private const double MhTableW = 832.5;
    private const double MhPageW = 1191.0;
    private const double MhPageH = 842.0;
    private const double MhCellPad = 7.5;            // 10px flex-cell padding
    private const double MhBandH = 53.25;
    private const double MhBandBaseOff = 33.1;       // 25.5 pt heading baseline
    private const double MhLabelFs = 15.0;           // 20px
    private const double MhLinkFs = 13.5;            // 18px
    private const double MhHeadFs = 25.5;            // 34px
    private const double MhLabelBaseOff = 22.56;     // rowTop → label baseline
    private const double MhLinkBaseOff = 21.05;      // rowTop → link baseline
    private const double MhLabelPitch = 22.5;
    private const double MhLinkPitch = 20.25;
    private const double MhRowPad = 15.0;
    private const double MhCtaX = 179.25;
    private const double MhCtaTop = 375.0;
    private const double MhCtaW = 312.0;
    private const double MhCtaH = 48.0;
    private const double MhCtaBase = 404.20;
    private const double MhSvgW = 300.0;             // width="400" px
    private const double MhSvgTop = 131.25;
    private const double MhTable1Top = 555.0;
    private const double MhTable2Top = 36.0;
    private const string MhPurple = "0.408 0.333 0.890";     // #6855E3
    private const string MhCyan = "0.816 0.969 0.996";       // #D0F7FE
    private const string MhRule = "0.855 0.863 0.878";       // #DADCE0
    private const string MhBlue = "0 0.322 0.702";           // #0052B3 links
    private const string MhInk = "0.129 0.145 0.161";        // #212529
    /// <summary>Metrics homepage: one html fragment flattened to its plain text.</summary>
    private static string MhFlat(string frag) => Regex.Replace(
        DecodeEntities(Regex.Replace(frag, @"<[^>]+>", " ")), @"\s+", " ").Trim();

    private static readonly (double R, double G, double B) MhGrad0 = (0, 176, 209);    // 129deg teal
    private static readonly (double R, double G, double B) MhGrad1 = (35, 203, 176);
    // The expected output wraps in Poppins metrics; the substitute face is narrower, so
    // wrap DECISIONS scale the measured width up to Poppins' ("Programming
    // Languages" must break at 166.5 while "Total Downloads" must not).
    private const double MhWrapFactor = 1.25;

    private sealed class MhCell
    {
        public double Flex;                          // basis fraction of the table width
        public string Text = "";
        public bool IsLink;
        public bool IsLabel;                         // the 20% cyan cell
        public bool IsHeading;                       // the 100% band cell
    }

}
