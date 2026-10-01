using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using System.Globalization;

namespace Aspose.Pdf.Text;

public sealed partial class TextParagraph
{
    /// <summary>The stages of the local paragraph render: one line at a time.</summary>
    private void RenderLocalLine(LocalRenderState rl, int i)
    {
        var line = rl.visualLines[i];
        double lineFs = LineFontSize(line);
        var firstTs = line.Count > 0 ? line[0].ts : _lines[0].TextState;

        double localBgY = rl.localBaseY[i];
        double descentComp = GetDescentCompensation(firstTs, lineFs);
        double localTextY = localBgY + descentComp;
        double lineStartX = i == 0 ? FirstLineIndent : SubsequentLinesIndent;

        double lineWidth = 0;
        foreach (var (text, ts) in line) lineWidth += MeasureLineWidth(text, ts);

        // Emit background rectangle with cm translation. A rotated line folds
        // its rotation into the same cm — the box stays (0, 0, w, h) in the
        // line's local frame and turns with the text.
        var bg = firstTs.BackgroundColor;
        if (bg is not null)
        {
            double bgH = lineFs * 1.1;
            var bgRad = firstTs.Rotation * Math.PI / 180.0;
            double bgCos = Math.Cos(bgRad), bgSin = Math.Sin(bgRad);
            rl.builder.SaveState();
            rl.builder.SetFillColor(bg.R / 255.0, bg.G / 255.0, bg.B / 255.0);
            rl.builder.SetMatrix(bgCos, bgSin, -bgSin, bgCos, lineStartX, localBgY);
            rl.builder.Rectangle(0, 0, lineWidth, bgH);
            rl.builder.FillEvenOdd();
            rl.builder.RestoreState();
        }

        // Emit underline rectangle if this line is underlined.
        if (firstTs.IsUnderline)
        {
            double ulY = localBgY + descentComp * 0.1;
            double ulH = GetUnderlineThickness(firstTs, lineFs);
            var fg = firstTs.ForegroundColor;
            double r = fg?.R / 255.0 ?? 0, g = fg?.G / 255.0 ?? 0, b = fg?.B / 255.0 ?? 0;
            rl.builder.SaveState();
            rl.builder.SetFillColor(r, g, b);
            rl.builder.SetMatrix(1, 0, 0, 1, lineStartX, ulY);
            rl.builder.Rectangle(0, 0, lineWidth, ulH);
            rl.builder.FillEvenOdd();
            rl.builder.RestoreState();
        }

        // Emit the line's chunks left-to-right with Tm positioning.
        double penX = lineStartX;
        foreach (var (rawText, ts) in line)
        {
            // Shape Arabic to connected presentation forms in visual order so the
            // embedded-font path emits the cursive glyphs; a no-op for non-Arabic.
            var text = ArabicShaper.ShapeForDisplay(rawText);
            var fontSize = ts.FontSize;
            var fontName = ts.FontName ?? "Helvetica";
            var fd = ts.FontData ?? ts.Font?.SourceFontData;
            // A fragment that explicitly carries a real font program embeds it
            // (with its descriptor) rather than downgrading to a bare
            // Standard-14 alias dict — an explicitly set FontRepository font
            // is embedded even for pure-Latin text, and the absorber
            // needs the descriptor descent to seat the read-back rectangle.
            var needsCid = rl.ensureCidFont is not null &&
                           fd is { TtfData: not null };
            string fontResName;
            byte[]? hexGlyphs = null;
            if (needsCid)
                (fontResName, hexGlyphs) = rl.ensureCidFont!(fd!, text);
            else
                fontResName = rl.ensureFont(fontName);

            var fgColor = ts.ForegroundColor;
            var alphaGsName = fgColor is not null ? EnsureFillAlphaExtGState(rl.page, fgColor.AByte) : null;
            if (alphaGsName is not null)
                rl.builder.SetExtGState(alphaGsName);
            rl.builder.BeginText();
            if (fgColor is not null)
                rl.builder.SetFillColor(fgColor.R / 255.0, fgColor.G / 255.0, fgColor.B / 255.0);
            rl.builder.SetFont(fontResName, fontSize);
            if (ts.CharacterSpacing != 0)
                rl.builder.SetCharSpacing(ts.CharacterSpacing);
            if (ts.WordSpacing != 0)
                rl.builder.SetWordSpacing(ts.WordSpacing);
            rl.builder.SetTextMatrix(1, 0, 0, 1, penX, localTextY);
            if (hexGlyphs is not null)
                rl.builder.ShowTextHex(hexGlyphs);
            else
                rl.builder.ShowText(text);
            rl.builder.EndText();

            penX += MeasureLineWidth(text, ts);
        }
    }
}
