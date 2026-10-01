using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
// The stages of the styled-segment append: the marker emit and one segment at a time.
    private void EmitMarker(StyledSegmentsState ss)
    {
        ss.builder.BeginText();
        ss.builder.SetFont(ss.fragResName, ss.fontSize > 0 ? ss.fontSize : 12.0);
        ss.builder.MoveTextPosition(ss.curX, ss.curY - ss.fragDescentComp);
        ss.builder.ShowText(string.Empty);
        ss.builder.EndText();
    }

    /// <summary>The stages of the styled-segment append: the marker emit and one segment at a time.</summary>
    private void AppendStyledSegment(StyledSegmentsState ss, TextSegment seg)
    {
        var segState = seg.TextState;
        var segText = seg.Text ?? string.Empty;
        if (segText.Length == 0)
        {
            // The parameterless-ctor empty segment surfaces as its own empty run.
            ss.builder.BeginText();
            ss.builder.SetFont(ss.fragResName, ss.fontSize > 0 ? ss.fontSize : 12.0);
            ss.builder.MoveTextPosition(ss.curX, ss.curY - ss.fragDescentComp);
            ss.builder.ShowText(string.Empty);
            ss.builder.EndText();
            return;
        }

        var segBase = segState is { FontSizeTouched: true, FontSize: > 0 }
            ? segState.FontSize
            : (ss.fontSize > 0 ? ss.fontSize : 12.0);
        var scriptState = segState ?? ss.fragment.TextState;
        var segFs = ScriptSize(scriptState, segBase);
        var segFont = segState?.Font is { } sfnt && !ReferenceEquals(sfnt, FontInfo.DefaultHelvetica)
            ? sfnt
            : ss.fragment.TextState.Font;
        var segData = segState?.FontData ?? segFont?.SourceFontData;

        // Styled-face upgrade (Bold/Italic selects the styled family member).
        if (segState is not null && (segState.IsBold || segState.IsItalic))
        {
            var family = segData?.FontName ?? segFont?.FontName ?? segState.FontName;
            if (!string.IsNullOrEmpty(family) && !Standard14Fonts.IsCoreName(family)
                && !family.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                && !family.Contains("Italic", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = (segState.IsBold ? " Bold" : string.Empty)
                    + (segState.IsItalic ? " Italic" : string.Empty);
                var spaced = System.Text.RegularExpressions.Regex.Replace(family, "(?<=[a-z])(?=[A-Z])", " ");
                var styled = FontRepository.FindFontData(family + suffix)
                    ?? (spaced != family ? FontRepository.FindFontData(spaced + suffix) : null);
                var wantTag = segState.IsBold ? "Bold" : "Italic";
                if (styled?.TtfData is not null
                    && styled.FontName?.Contains(wantTag, StringComparison.OrdinalIgnoreCase) == true)
                    segData = styled;
            }
        }

        var segFg = segState?.ForegroundColor ?? ss.fragment.TextState.ForegroundColor;
        var lines = segText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var li = 0; li < lines.Length; li++)
        {
            if (li > 0)
            {
                ss.curY -= ss.lineH;
                ss.curX = ss.x;
                ss.lineStarted = false;
            }
            if (!ss.lineStarted)
            {
                EmitMarker(ss);
                ss.lineStarted = true;
            }
            var piece = lines[li];
            if (piece.Length == 0) continue;

            double segComp = 0;
            string resName;
            byte[]? hexIds = null;
            if (segData?.TtfData is not null)
            {
                (resName, hexIds) = EnsureEmbeddedCIDFont(segData, piece);
                var (_, d, _, _) = FontRepository.ReadTtfMetrics(segData.TtfData);
                if (d != 0) segComp = d * segFs / 1000.0;
            }
            else
            {
                var segMapState = segState ?? ss.fragment.TextState;
                resName = EnsureFontResource(MapToStandard14(segMapState));
            }

            if (segFg is not null)
                ss.builder.SetFillColor(segFg.R / 255.0, segFg.G / 255.0, segFg.B / 255.0);
            ss.builder.BeginText();
            ss.builder.SetFont(resName, segFs);
            if (ScriptRise(scriptState, segBase) is not 0 and var rise) ss.builder.SetTextRise(rise);
            ss.builder.MoveTextPosition(ss.curX, ss.curY - segComp);
            if (hexIds is not null) ss.builder.ShowTextHex(hexIds);
            else ss.builder.ShowText(piece);
            ss.builder.EndText();

            // Advance the cursor by the piece's measured width in ITS face.
            double w;
            try
            {
                if (segData is not null)
                    w = FontInfo.FromFontData(segData).MeasureString(piece, segFs);
                else if (segFont is not null)
                    w = segFont.MeasureString(piece, segFs);
                else
                    w = piece.Length * segFs * 0.5;
            }
            catch { w = piece.Length * segFs * 0.5; }
            ss.curX += w;
        }
    }
}
