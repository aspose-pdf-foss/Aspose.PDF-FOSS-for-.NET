using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>Show text: a span carrying /ActualText contributes that text and its advance.</summary>
    private void ShowActualText(ExtractState xs, PdfString tjStr)
    {
        if (!xs.actualTextUsed)
        {
            AppendShowText(xs.actualText!);
            xs.actualTextUsed = true;
            // The replaced glyphs' advance differs from the ActualText's,
            // so the gap chain restarts at the next regular run.
            xs.lastRunEndX = double.NaN; xs.lastRunEndDevX = double.NaN; xs.lastRunEndPageX = double.NaN;
        }
        // The span's glyphs still advance the pen even though their
        // decode is replaced — with a stale tx the NEXT run's Td
        // reads as a huge phantom word gap ("Is," grew a space).
        var atAdvW = ((xs.currentMetrics?.MeasureString(tjStr.Value, xs.fontSize)
               ?? xs.fontSize * 0.5 * tjStr.Value.Length) + SpacingAdvance(xs, tjStr.Value)) * xs.horizScale;
        if (Type3SpanActive(xs))
        {
            var t3Adv = Type3Advance(tjStr.Value, xs.currentFontDict!, xs.reader, xs.fontSize);
            if (t3Adv >= 0) atAdvW = t3Adv * xs.horizScale;
            CollectType3SpanRun(xs, 
                DecodeString(tjStr.Value, xs.currentToUnicode, xs.currentFontDict, xs.reader, xs.useFontEngine).Length,
                xs.tmOriginX + (xs.tx - xs.tmOriginX) * xs.tmA + xs.localCmTx, xs.tmY + xs.localCmTy,
                xs.fontSize * Math.Abs(xs.tmA), atAdvW * Math.Abs(xs.tmA));
        }
        xs.tx += atAdvW;
    }
}
