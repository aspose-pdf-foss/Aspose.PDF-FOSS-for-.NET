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
    private const double CssMarkerGapEm = 0.375;

    private static bool EmitCssBoxMarker(BlockTextState bt, double itemIndent)
    {
        var marker = bt.block.Marker!.Trim();
        if (marker.Length == 0
            || bt.flow.page.Dict.Get("Resources") is not Core.PdfDictionary res
            || res.Get("Font") is not Core.PdfDictionary fontDict)
            return false;
        var face = FirstFontFamily(bt.block.FontFamily ?? "") ?? "Arial";
        var faceTtf = PosFace(face).ttf;
        var (ttf, name) = faceTtf is not null && PosFace(face).parser is { } fp && fp.CMap.ContainsKey(marker[0])
            ? (faceTtf, face)
            : (ResolveUnicodeFont(marker)?.SourceFontData?.TtfData, ResolveUnicodeFont(marker)?.FontName ?? "Unicode");
        if (ttf is null) return false;
        var adv = MeasureFaceText(name, marker, bt.metrics.blockFontSize);
        var x = bt.marginLeft + itemIndent - CssMarkerGapEm * bt.metrics.blockFontSize - adv;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, name, marker, stripSpacesInBaseFont: true);
        bt.flow.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
            $"BT /{rn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf 1 0 0 1 {x.ToString("F2", bt.invc)} {bt.flow.y.ToString("F2", bt.invc)} Tm <{Compat.ToHexString(hex)}> Tj ET\n"));
        return true;
    }

    private static void EmitMarkerHere(BlockTextState bt)
    {
        // The marker takes the colour a ::marker rule gives it, else its item's own.
        if ((bt.block.MarkerColor ?? bt.block.ForeColor) is not { } ink)
        {
            EmitMarkerGlyph(bt);
            return;
        }
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(bt.invc,
            $"q {ink.R / 255.0:0.###} {ink.G / 255.0:0.###} {ink.B / 255.0:0.###} rg\n")));
        EmitMarkerGlyph(bt);
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
    }

    private static void EmitMarkerGlyph(BlockTextState bt)
    {
        // (a marker keeps the item's own indent: the controls a control line seats before the text stand after it)
        var itemIndent = bt.block.LeftIndent - (bt.block.CtlSeat?.LeadPt ?? 0);
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
                bt.marginLeft + itemIndent - mAdv - mGap);
            // The marker inherits the item's weight (an h1-nested list
            // draws bold bullets in the bold serif resource).
            var mY = bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y;
            // A body-pinned face draws the marker too (probed: an Arial 8 body's "1." is Arial).
            if (bt.block.FontFamily is { Length: > 0 } mFam
                && PosFace(mFam + (bt.block.FontRes == "F2" ? " Bold" : "")).ttf is { } mTtf
                && bt.flow.page.Dict.Get("Resources") is Core.PdfDictionary mRes
                && mRes.Get("Font") is Core.PdfDictionary mDict)
            {
                var (mRn, mHex) = Text.Type0FontEmbedder.Embed(mDict, mTtf,
                    mFam.Replace(" ", "") + (bt.block.FontRes == "F2" ? "Bold" : ""), bt.block.Marker!, stripSpacesInBaseFont: true);
                bt.flow.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                    $"BT /{mRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf 1 0 0 1 {uaX.ToString("F2", bt.invc)} {mY.ToString("F2", bt.invc)} Tm {KernedTj(mTtf, mHex)} ET\n"));
                return;
            }
            EmitPositionedRun(bt.flow.page, bt.block.FontRes == "F2" ? "F6" : "F5",
                bt.metrics.blockFontSize, uaX, mY, bt.block.Marker!);
            return;
        }
        // The CSS-box flow hangs the marker 0.375 em before the item text on its own advance, in the
        // face that holds it (probed: a 10.5 pt disc ends 3.94 left of the text; the square falls back
        // to a symbol face).
        if (bt.profile.cssBoxBody && EmitCssBoxMarker(bt, itemIndent)) return;
        var markerW = bt.block.Marker!.Length * bt.metrics.blockFontSize * 0.52;
        var markerX = Math.Max(bt.marginLeft, bt.marginLeft + itemIndent - markerW - 4);
        // The marker sits on the SAME baseline as the item's first line —
        // in the styled-article flow that line drops half-leading + ascent
        // below the cursor, and the marker must drop with it. The OTHER
        // metric dialects are calibrated with the raw-cursor marker.
        EmitPositionedRun(bt.flow.page, bt.fontRes, bt.metrics.blockFontSize, markerX,
            bt.articleFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y, bt.block.Marker);
    }
}
