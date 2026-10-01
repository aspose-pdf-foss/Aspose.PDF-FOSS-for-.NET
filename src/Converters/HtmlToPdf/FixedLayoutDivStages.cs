using System.Text;
using System.Text.RegularExpressions;
namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Every absolutely positioned div on the fixed sheet is measured into the model, and the furthest right edge any of them reaches comes back.</summary>
    private static double MeasureFixedSheetDivs(List<FixedPageDiv> divs, FixedSheetModel model, bool emGridMarkup)
    {
        double maxRight = 0;
        foreach (var div in divs)
        {
            if (div.HasObjectGraphic) maxRight = Math.Max(maxRight, div.ObjectInkRight ?? div.SrcW);
            // A full-bleed raster page contributes its whole BOX, like a sidecar page graphic:
            // it IS the page, so the sheet grows to hold it rather than to its inked columns.
            if (div.BackgroundW > 0) maxRight = Math.Max(maxRight, div.BackgroundW);

            // Selection-layer spans (transparent text over a full-page raster) can carry
            // synthetic padding and unshaped RTL runs whose naive advance far exceeds
            // what any layout engine would produce for the visible line. The raster IS
            // the visible content, so its rightmost inked column (plus up to an em of
            // trailing whitespace) caps their contribution.
            double? bgInkRight = null;
            if (div.Background is not null)
                try
                {
                    var (px, pw, ph, hasAlpha) = Facades.PdfFileMend.DecodePng(div.Background);
                    var bpp = hasAlpha ? 4 : 3;
                    var right = -1;
                    for (var yy = 0; yy < ph; yy++)
                    {
                        var row = yy * pw;
                        for (var xx = pw - 1; xx > right; xx--)
                        {
                            var o = (row + xx) * bpp;
                            if (px[o] < 220 || px[o + 1] < 220 || px[o + 2] < 220) { right = xx; break; }
                        }
                    }
                    if (right >= 0 && pw > 0) bgInkRight = (right + 1) / (double)pw * div.SrcW;
                }
                catch { /* not a PNG / undecodable — no cap */ }

            // A bidi page's selection layer measures unreliably as a whole: RTL runs
            // are stored unshaped (ligatures collapse in any real layout) and even its
            // LTR fragments carry mirrored ordering with positioning spaces. Cap ALL
            // of that div's transparent lines at the raster's ink edge plus up to an
            // em of trailing whitespace; pure-LTR pages measure reliably and stay as-is.
            var divHasRtl = false;
            foreach (var s in div.Spans)
                if (HasRtlChar(s.Text)) { divHasRtl = true; break; }

            foreach (var s in div.Spans)
                foreach (var line in s.Lines)
                {
                    var r = s.Left + MeasureSpanLine(s, line);
                    if (Environment.GetEnvironmentVariable("STL_DEBUG_WIDTH2") is not null && r > 400)
                    {
                        var trimmed = line.TrimEnd();
                        var spacesN = 0; foreach (var chW in line) if (chW == ' ') spacesN++;
                        var trailAdvDbg = MeasureSpanLine(s, line[trimmed.Length..]);
                        Console.Error.WriteLine(Compat.Format(System.Globalization.CultureInfo.InvariantCulture,
                            $"WLINE r={r:F2} left={s.Left:F2} fs={s.FontSize:F4} ls={s.LetterSpacing:F4} ws={s.WordSpacing:F4} nsp={spacesN} nch={line.Length} trail={trailAdvDbg:F2} txt='{(trimmed.Length > 34 ? trimmed[^34..] : trimmed)}'"));
                    }
                    if (s.Color is null && bgInkRight is not null && !emGridMarkup)
                    {
                        var trailingWs = line[line.TrimEnd().Length..];
                        var trailAdv = MeasureSpanLine(s, trailingWs);
                        // RTL layers measure so unreliably that even their trailing-space
                        // credit is clamped; LTR layers keep the real trailing advance
                        // with a floor of a few ems (selection spans
                        // run a little past the ink even on lines showing none).
                        r = Math.Min(r, bgInkRight.Value
                            + (divHasRtl ? Math.Min(trailAdv, s.FontSize)
                                         : Math.Max(trailAdv, 2.5 * s.FontSize)));
                    }
                    // Text overflowing the fixed page container never widens the sheet:
                    // the container's box is the layout surface (a 493 pt cover title
                    // whose naive advance runs metres past an A4 box still yields
                    // the 96+box+89.76 sheet).
                    // This dialect's sheet is measured to the INK, so the last glyph's right
                    // side bearing comes off the run's advance before it sizes the page.
                    if (model.SheetOnInkExtent) r -= LastGlyphRightBearingPt(s, line);
                    maxRight = Math.Max(maxRight, Math.Min(r, div.SrcW));
                }
        }
        return maxRight;
    }
}
