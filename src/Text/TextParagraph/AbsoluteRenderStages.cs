using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    /// <summary>The stages of the absolute paragraph render: one visual line.</summary>
    private bool RenderAbsoluteLine(AbsoluteRenderState ra, int li)
    {
        ra.line = ra.visualLines[li];
        ra.lineFs = LineFontSize(ra.line);
        ra.textY -= LineAdvance(ra.visualLines, li);

        // A bounds-limited paragraph stops at the first line whose baseline
        // falls below the content bottom; that line and the rest are handed
        // back as RemainingLines for the caller to continue elsewhere.
        if (LimitWithBounds && ra.textY < ra.minY)
        {
            for (int ri = li; ri < ra.visualLines.Count; ri++)
                _remainingLines.Add(RemainingLineFragment(ra.visualLines[ri]));
            return false;
        }

        ra.lineStartX = ra.startX + (li == 0 ? FirstLineIndent : SubsequentLinesIndent);

        ra.lineWidth = 0;
        foreach (var (text, ts) in ra.line) ra.lineWidth += MeasureLineWidth(text, ts);

        ra.firstTs = ra.line.Count > 0 ? ra.line[0].ts : _lines[0].TextState;
        ra.firstFd = ra.firstTs.FontData ?? ra.firstTs.Font?.SourceFontData;
        ra.firstLifted = ra.ensureCidFont is not null && ra.firstFd is { TtfData: not null };
        ra.bgRectY = Rectangle is null && Position is not null
            ? ra.textY - (ra.firstLifted ? 0 : GetDescentCompensation(ra.firstTs, ra.lineFs))
            : ra.textY + ra.lineFs;

        ra.bg = ra.firstTs.BackgroundColor;
        if (ra.bg is not null)
        {
            double bgH = ra.lineFs * 1.1;
            double bgW = ra.anyBg ? ra.maxLineWidth : ra.lineWidth;
            ra.builder.SaveState();
            ra.builder.SetFillColor(ra.bg.R / 255.0, ra.bg.G / 255.0, ra.bg.B / 255.0);
            ra.builder.Raw($"{F2T(ra.lineStartX)} {F2T(ra.bgRectY)} {F2T(bgW)} {F2T(bgH)} re");
            ra.builder.Fill();
            ra.builder.RestoreState();
        }

        if (ra.firstTs.IsUnderline)
        {
            double descentComp = GetDescentCompensation(ra.firstTs, ra.lineFs);
            double ulY = ra.bgRectY + descentComp * 0.1;
            double ulH = GetUnderlineThickness(ra.firstTs, ra.lineFs);
            double ulW = ra.lineWidth;
            var fg = ra.firstTs.ForegroundColor;
            double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b2 = fg?.B / 255.0 ?? 0;
            ra.builder.SaveState();
            ra.builder.SetFillColor(r, g, b2);
            ra.builder.SetMatrix(1, 0, 0, 1, ra.lineStartX, ulY);
            ra.builder.Rectangle(0, 0, ulW, ulH);
            ra.builder.FillEvenOdd();
            ra.builder.RestoreState();
        }

        ra.penX = ra.lineStartX;
        foreach (var (rawText, ts) in ra.line)
        {
            if (!RenderAbsoluteRun(ra, rawText, ts)) break;
        }
        return true;
    }

    /// <summary></summary>
    private bool RenderAbsoluteRun(AbsoluteRenderState ra, string rawText, TextState ts)
    {
        // Shape Arabic to connected presentation forms in visual order so the
        // embedded-font path emits the cursive glyphs; a no-op for non-Arabic.
        var text = ArabicShaper.ShapeForDisplay(rawText);
        var fontSize = ts.FontSize;
        var fd = ts.FontData ?? ts.Font?.SourceFontData;
        // A Bold/Italic style on a repository face selects the styled family
        // member when the family has one (Arial Bold); a family without it
        // keeps its regular face and synthesises the bold weight below.
        var styled = TextBuilder.ResolveStyledFace(ts, fd);
        var syntheticBold = ts.IsBold && styled is null && fd is { TtfData: not null };
        if (styled is not null) fd = styled;
        // A fragment that explicitly carries a real font program embeds it
        // (with its descriptor) rather than downgrading to a bare
        // Standard-14 alias dict — an explicitly set FontRepository font
        // is embedded even for pure-Latin text, and the absorber
        // needs the descriptor descent to seat the read-back rectangle.
        var needsCid = ra.ensureCidFont is not null &&
                       fd is { TtfData: not null };
        string fontResName;
        byte[]? hexGlyphs = null;
        if (needsCid)
            (fontResName, hexGlyphs) = ra.ensureCidFont!(fd!, text);
        else
            fontResName = ra.ensureFont(TextBuilder.MapToStandard14Public(ts));

        var fgColor = ts.ForegroundColor;
        var alphaGsName = fgColor is not null ? EnsureFillAlphaExtGState(ra.page, fgColor.AByte) : null;
        if (alphaGsName is not null)
            ra.builder.SetExtGState(alphaGsName);
        ra.builder.BeginText();
        if (fgColor is not null)
            ra.builder.SetFillColor(fgColor.R / 255.0, fgColor.G / 255.0, fgColor.B / 255.0);
        ra.builder.SetFont(fontResName, fontSize);
        // Emit Tc/Tw so the line's character/word spacing is applied on render and
        // re-parse; guarded so default (zero) spacing keeps byte-identical output.
        if (ts.CharacterSpacing != 0)
            ra.builder.SetCharSpacing(ts.CharacterSpacing);
        if (ts.WordSpacing != 0)
            ra.builder.SetWordSpacing(ts.WordSpacing);
        // Synthesised bold: fill AND stroke the outlines with a pen
        // proportional to the size, reset to the defaults after the run so
        // the following regular lines read back a 1-pt pen.
        if (syntheticBold)
        {
            ra.builder.SetLineWidth(fontSize * SyntheticBoldPenFactor);
            ra.builder.SetTextRenderingMode((int)TextRenderingMode.FillThenStrokeText);
        }
        // The run is written one descriptor descent above its layout
        // baseline when its face carries one (see WrittenDescentLift).
        ra.builder.MoveTextPosition(ra.penX, ra.textY + (needsCid ? WrittenDescentLift(fd, fontSize) : 0));
        if (hexGlyphs is not null)
            ra.builder.ShowTextHex(hexGlyphs);
        else
            ra.builder.ShowText(text);
        if (syntheticBold)
        {
            ra.builder.SetLineWidth(1);
            ra.builder.SetTextRenderingMode((int)TextRenderingMode.FillText);
        }
        ra.builder.EndText();

        ra.penX += MeasureLineWidth(text, ts);
        return true;
    }
}
