using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The block writer's list-marker emitter.
    // List marker: a separate text run in the item's left indent on the first line,
    // so it surfaces as its own TextFragment. A numeric/bullet marker is emitted BEFORE
    // the content (earlier fragment); a CSS ::before marker (MarkerAfter) is emitted
    // AFTER the content so, on an RTL line, the item text is the earlier fragment.
    // The marker itself may be Arabic/Hebrew/CJK, so it uses the same RTL/Type0 path
    // as body text.
    private static void EmitMarkerHere(BlockTextState bt)
    {
        // UA-default serif flow: the marker draws in the same Standard-14
        // serif as its item text, hangs one marker advance + gap left of
        // the item indent, and seats on the item's first baseline.
        if (bt.profile.uaStdSerif)
        {
            var mAdv = MeasureFaceText(bt.metrics.metricMeasureFace, bt.block.Marker!, bt.metrics.blockFontSize);
            // DataWorks: markers hang a bare 0.8 pt left of the item
            // (reference bullets sit at indent − marker − 0.8).
            var mGap = bt.profile.dwFormDoc ? DwMarkerGapPt : UaMarkerGapEm * bt.metrics.blockFontSize;
            var uaX = Math.Max(bt.marginLeft,
                bt.marginLeft + bt.block.LeftIndent - mAdv - mGap);
            // The marker inherits the item's weight (an h1-nested list
            // draws bold bullets in the bold serif resource).
            EmitPositionedRun(bt.flow.page, bt.block.FontRes == "F2" ? "F6" : "F5",
                bt.metrics.blockFontSize, uaX,
                bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y, bt.block.Marker!);
            return;
        }
        var markerW = bt.block.Marker!.Length * bt.metrics.blockFontSize * 0.52;
        var markerX = Math.Max(bt.marginLeft, bt.marginLeft + bt.block.LeftIndent - markerW - 4);
        // The marker sits on the SAME baseline as the item's first line —
        // in the styled-article flow that line drops half-leading + ascent
        // below the cursor, and the marker must drop with it. The OTHER
        // metric dialects are calibrated with the raw-cursor marker.
        EmitPositionedRun(bt.flow.page, bt.fontRes, bt.metrics.blockFontSize, markerX,
            bt.articleFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y, bt.block.Marker!);
    }
}
