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

        ra.lineWidth = 0;
        foreach (var (text, ts) in ra.line) ra.lineWidth += MeasureLineWidth(text, ts);

        // The paragraph's own alignment: a centred or right line is offset within the band
        // it is laid in - the rectangle's content width, else the block's widest line. It
        // used to start at the anchor like a left one. A justified paragraph keeps its
        // left seat whatever its alignment says (as a word processor does), and spreads
        // each line a wrap ended over the band.
        var band = LineBand(ra);
        var slack = Math.Max(0, band - ra.lineWidth);
        var alignOffset = Justify ? 0 : HorizontalAlignment switch
        {
            HorizontalAlignment.Center => slack / 2,
            HorizontalAlignment.Right => slack,
            _ => 0,
        };
        var indent = li == 0 ? FirstLineIndent : SubsequentLinesIndent;
        ra.lineStartX = ra.startX + indent + alignOffset;
        ra.wordStretch = JustifiedWordStretch(ra.line, li, band - indent);

        ra.firstTs = ra.line.Count > 0 ? ra.line[0].ts : _lines[0].TextState;
        ra.firstFd = ra.firstTs.FontData ?? ra.firstTs.Font?.SourceFontData;
        ra.firstLifted = ra.ensureCidFont is not null && ra.firstFd is { TtfData: not null };
        // Each line's background is painted on its own glyph box, bottom at its baseline less the descent, and its
        // underline just under that baseline, however the paragraph is placed: seated a font size above the
        // baseline, a Rectangle-placed line's box covered the line before it and its underline crossed its glyphs.
        ra.bgRectY = ra.textY - (ra.firstLifted ? 0 : GetDescentCompensation(ra.firstTs, ra.lineFs));

        ra.bg = ra.firstTs.BackgroundColor;
        if (ra.bg is not null)
        {
            double bgH = ra.lineFs * 1.1;
            double bgW = ra.maxLaidOutWidth;
            ra.builder.SaveState();
            ra.builder.SetFillColor(ra.bg.R / 255.0, ra.bg.G / 255.0, ra.bg.B / 255.0);
            ra.builder.Raw($"{F2T(ra.lineStartX)} {F2T(ra.bgRectY)} {F2T(bgW)} {F2T(bgH)} re");
            ra.builder.Fill();
            ra.builder.RestoreState();
        }

        // Each underlined run draws its own rule (see UnderlineRun): a line of a plain run and an underlined
        // one is not underlined from end to end in the first run's state.
        ra.penX = ra.lineStartX;
        foreach (var (rawText, ts) in ra.line)
        {
            var runStart = ra.penX;
            if (!RenderAbsoluteRun(ra, rawText, ts)) break;
            // A spread line's rule ends at the band it fills, where its last word does, not where the pen's
            // stretch overshoots.
            var runEnd = ra.wordStretch > 0 ? Math.Min(ra.penX, ra.lineStartX + band - indent) : ra.penX;
            if (ts.IsUnderline) UnderlineRun(ra, ts, runStart, runEnd);
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
        // A font taken from a run of the document shows the text through its own dictionary.
        var inDocumentFont = TextBuilder.TryShowInDocumentFont(ra.page, ts, text, fontSize);
        var needsCid = inDocumentFont is null && ra.ensureCidFont is not null &&
                       fd is { TtfData: not null };
        string fontResName;
        byte[]? hexGlyphs = null;
        if (inDocumentFont is { } shown)
            (fontResName, hexGlyphs, _) = shown;
        else if (needsCid)
#pragma warning disable S8969 // the compiler reports a possible null without it (CS8602/CS8603)
            (fontResName, hexGlyphs) = ra.ensureCidFont!(fd!, text);
#pragma warning restore S8969
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
        // A justified line's gaps widen through Tw where a space is the one-byte code 32, and
        // through a move after each space glyph of a two-byte run, which Tw does not reach.
        var stretchedByTw = ra.wordStretch != 0 && hexGlyphs is null;
        if (ts.WordSpacing != 0 || stretchedByTw)
            ra.builder.SetWordSpacing(ts.WordSpacing + (stretchedByTw ? ra.wordStretch : 0));
        var stretchedGlyphs = ra.wordStretch != 0 && hexGlyphs is not null
            ? TextBuilder.CompositeWordSpacing(text, hexGlyphs, ra.wordStretch, fontSize) : null;
        // Synthesised bold: fill AND stroke the outlines with a pen
        // proportional to the size, reset to the defaults after the run so
        // the following regular lines read back a 1-pt pen.
        // The line's own rendering mode, Tr: an invisible or a stroked run is written the way its
        // state says, and the fill mode comes back after it, as the pen does after a synthesised bold.
        var mode = ts.Invisible ? TextRenderingMode.Invisible : ts.RenderingMode;
        if (syntheticBold)
        {
            ra.builder.SetLineWidth(fontSize * SyntheticBoldPenFactor);
            ra.builder.SetTextRenderingMode((int)TextRenderingMode.FillThenStrokeText);
        }
        else if (mode != TextRenderingMode.FillText)
        {
            ra.builder.SetTextRenderingMode((int)mode);
        }
        // The run is written one descriptor descent above its layout
        // baseline when its face carries one (see WrittenDescentLift).
        ra.builder.MoveTextPosition(ra.penX, ra.textY + (needsCid ? WrittenDescentLift(fd, fontSize) : 0));
        if (stretchedGlyphs is not null)
            ra.builder.ShowTextHexKerned(hexGlyphs!, stretchedGlyphs);
        else if (hexGlyphs is not null)
            ra.builder.ShowTextHex(hexGlyphs);
        else
            ra.builder.ShowText(text);
        // The spacing and the spread are the run's own: Tc and Tw outlive ET, so the runs and
        // lines after it would draw with them; they start again from none.
        if (ts.CharacterSpacing != 0)
            ra.builder.SetCharSpacing(0);
        if (ts.WordSpacing != 0 || stretchedByTw)
            ra.builder.SetWordSpacing(0);
        if (syntheticBold)
        {
            ra.builder.SetLineWidth(1);
            ra.builder.SetTextRenderingMode((int)TextRenderingMode.FillText);
        }
        else if (mode != TextRenderingMode.FillText)
        {
            ra.builder.SetTextRenderingMode((int)TextRenderingMode.FillText);
        }
        ra.builder.EndText();

        var stretch = stretchedByTw || stretchedGlyphs is not null ? ra.wordStretch * text.Count(c => c == ' ') : 0;
        ra.penX += (inDocumentFont?.width ?? MeasureLineWidth(text, ts)) + stretch;
        return true;
    }

    /// <summary>The rule under one underlined run, from where it started to where its pen stands, in the run's
    /// colour and thickness; a justified line's spread is in the pen's advance.</summary>
    private void UnderlineRun(AbsoluteRenderState ra, TextState ts, double fromX, double toX)
    {
        if (toX <= fromX) return;
        var ulY = ra.bgRectY + GetDescentCompensation(ts, ra.lineFs) * 0.1;
        var ulH = GetUnderlineThickness(ts, ra.lineFs);
        var fg = ts.ForegroundColor;
        ra.builder.SaveState();
        ra.builder.SetFillColor(fg?.R / 255.0 ?? 0, fg?.G / 255.0 ?? 0, fg?.B / 255.0 ?? 0);
        ra.builder.SetMatrix(1, 0, 0, 1, fromX, ulY);
        ra.builder.Rectangle(0, 0, toX - fromX, ulH);
        ra.builder.FillEvenOdd();
        ra.builder.RestoreState();
    }

    /// <summary>The band a line is laid in: the rectangle's content width, else the block's widest line.</summary>
    private double LineBand(AbsoluteRenderState ra)
        => Box is { } box ? box.Width - Margin.Left - Margin.Right : ra.maxLineWidth;

    /// <summary>The widest line as laid out, which a paragraph's background spans: a line a justification spreads
    /// fills its band (as the reference paints it), any other keeps its own width.</summary>
    private double WidestLaidOutLine(AbsoluteRenderState ra)
    {
        var band = LineBand(ra);
        var widest = ra.maxLineWidth;
        for (var li = 0; li < ra.visualLines.Count; li++)
        {
            var room = band - (li == 0 ? FirstLineIndent : SubsequentLinesIndent);
            if (JustifiedWordStretch(ra.visualLines[li], li, room) > 0) widest = Math.Max(widest, room);
        }
        return widest;
    }

    /// <summary>What each word gap of visual line <paramref name="li"/> gains so the line fills
    /// <paramref name="room"/>: a justified paragraph spreads every line a wrap ended, a fully justified one
    /// its last lines too. The spaces a wrap leaves at the line's end are no gaps and take no room. Zero for
    /// any other line, and for a line with no gap or none to fill.</summary>
    private double JustifiedWordStretch(List<(string text, TextState ts)> line, int li, double room)
    {
        var full = HorizontalAlignment == HorizontalAlignment.FullJustify;
        if (!Justify && HorizontalAlignment != HorizontalAlignment.Justify && !full) return 0;
        if (!full && (li >= _visualLineWrapped.Count || !_visualLineWrapped[li])) return 0;
        double width = 0, trailingWidth = 0;
        int spaces = 0, trailingSpaces = 0;
        foreach (var (text, ts) in line)
        {
            width += MeasureLineWidth(text, ts);
            spaces += text.Count(c => c == ' ');
            var tail = text.Length - text.TrimEnd(' ').Length;
            var tailWidth = tail > 0 ? MeasureLineWidth(text.Substring(text.Length - tail), ts) : 0;
            // A run with text past its spaces ends the trailing ones before it.
            if (tail < text.Length) { trailingSpaces = 0; trailingWidth = 0; }
            trailingSpaces += tail;
            trailingWidth += tailWidth;
        }
        var gaps = spaces - trailingSpaces;
        var fill = room - (width - trailingWidth);
        return gaps > 0 && fill > 0 ? fill / gaps : 0;
    }
}
