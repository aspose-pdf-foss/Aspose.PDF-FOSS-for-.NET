using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>The inline style that keeps a span's text in the order it is written. A right-to-left
    /// run is written in the visual order the page draws it in; left to the browser's own bidi pass,
    /// every word of it is mirrored.</summary>
    private static string BidiOverrideCss(string text) =>
        HasRtlCodepoint(text) ? "unicode-bidi:bidi-override;direction:ltr;" : "";

    /// <summary>The css font size the em-compensation dialect emits for a drawn size: the em value rounded to
    /// the dialect's 0.01-em grid (a drawn 11 pt is 0.92em).</summary>
    private static double EmGridFontSizeEm(double fontSizePt) =>
        Math.Round(fontSizePt / 12.0, 2, MidpointRounding.AwayFromZero);

    /// <summary>The stages of the span spacing resolve: the slot-based spacing of the em-compensation dialect.</summary>
    private static void ResolveSlotSpacing(StlDivState sd, List<StlItem> its, int slots, bool lineFinal)
    {
        // THE EM-COMPENSATION SOLVE: the word-spacing that lands the
        // region's browser width on its pen width,
        //   ws = R2( ((ΣE + Σface) / R − Σface − n·lsFloor) / (slots·1000) )
        // · ΣE + Σface is the pen width of the region in milli-em of the
        //   DRAWN size; the browser draws the face at the EMITTED size,
        //   rounded to the 0.01-em grid (R = emitted/drawn), and its
        //   letter- and word-spacing are em of that size too.
        // · n counts the letter-spacings inside the region: one after
        //   every item (interior slots included; the trailing inter-span
        //   slot of a title span included — that slot extends the region
        //   to the next span's start), except the one after the line's
        //   last character, which lands beyond the ink.
        // · Every slot takes an equal share: css spreads the word-spacing
        //   over every space of the span, so a share charged to the
        //   kerned slots alone would be drawn several times over.
        var fsEm = sd.st.FontSize / 12.0;
        var cssEm = EmGridFontSizeEm(sd.st.FontSize);
        var ratio = cssEm > 0 ? cssEm / fsEm : 1.0;
        double sumE = 0, faceSum = 0;
        foreach (var x in its) { sumE += x.E; faceSum += x.FaceMilli; }
        var lsTerms = lineFinal && !its[^1].IsSlot ? its.Count - 1 : its.Count;
        sd.wsEm = Math.Round(
            ((sumE + faceSum) / ratio - faceSum - lsTerms * sd.lsMilli)
            / slots / 1000.0,
            2, MidpointRounding.AwayFromZero);
        var fitEnv = Environment.GetEnvironmentVariable("ASPOSE_PH2_FIT");
        if (fitEnv is "1" or "2")
        {
            var head = new StringBuilder();
            foreach (var x in its)
            {
                if (head.Length >= 28) break;
                head.Append(x.IsSlot ? ' ' : x.Ch);
            }
            Console.Error.WriteLine(
                $"[fit] n={lsTerms}/{its.Count} slots={slots} R={ratio:F5} " +
                $"sumE={sumE:F2} face={faceSum:F1} ls={sd.lsMilli:F1} " +
                $"ws={sd.wsEm:F2} |{head}|");
            if (fitEnv == "2")
                foreach (var x in its)
                    Console.Error.WriteLine(
                        $"  [it] {(x.IsSlot ? "SLOT" : (x.Text ?? x.Ch.ToString())),-4} " +
                        $"E={x.E:F2} face={x.FaceMilli:F1} x={x.StartX:F2}");
        }
    }
}
