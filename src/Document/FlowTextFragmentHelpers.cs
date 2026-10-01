using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
    // The text-fragment writer's per-line helpers, lifted out of the writer so its line loop can move.
        private double EmptyHeight(FlowTextFragmentState wtf) => _overflowBuffer is null
                ? Text.Standard14Fonts.FullSizeEmptyLineEm * wtf.fontSize : wtf.fontSize;

        private Text.Font? LineFallback(FlowTextFragmentState wtf, int i)
            {
                if (!wtf.lineFallbackActive || i < 0 || i >= wtf.allLines.Count) return null;
                if (wtf.fallbackFace.TryGetValue(i, out var cached)) return cached;
                var f = Text.FontRepository.ResolveCoveringFont(wtf.fontTtf!, wtf.allLines[i]);
                // A line whose face covers it but cannot SHAPE its script is drawn in the
                // script's system face; unlike the covering hand-off it keeps the
                // paragraph's own pitch (HeightOfLine) - the reference seats such a line
                // the drawing face's descent above the paragraph's line box.
                var resolvedTtf = f?.SourceFontData?.TtfData ?? wtf.fontTtf!;
                if (Text.FontRepository.ResolveScriptShapingFont(resolvedTtf, wtf.allLines[i]) is { } shaping)
                {
                    wtf.shapingLines.Add(i);
                    wtf.coveringFace[i] = f;
                    f = shaping;
                }
                wtf.fallbackFace[i] = f;
                return f;
            }

        private double HeightOfLine(FlowTextFragmentState wtf, int i)
            {
                if (!wtf.variableLineHeights || i < 0 || i >= wtf.allLines.Count) return wtf.lineHeight;
                // (fullSize-only from here: fallback faces change the LINE EXTENT.)
                if (string.IsNullOrWhiteSpace(wtf.allLines[i])) return EmptyHeight(wtf);
                var fallback = LineFallback(wtf, i);
                if (wtf.shapingLines.Contains(i))
                    fallback = wtf.coveringFace.TryGetValue(i, out var covering) ? covering : null;
                if (fallback?.SourceFontData?.TtfData is { Length: > 12 } fb)
                    return Math.Max(Text.Standard14Fonts.FullSizeEmptyLineEm * wtf.fontSize,
                                    ComputeFullSizeLineHeight(fb, wtf.fontSize));
                return wtf.lineHeight;
            }

            // LAW (the paragraph clip): the reference clips every page paragraph to its
            // column, from the bottom of its last line box up by 1.16 line boxes; a line
            // handed to a script face for shaping clips that face's descriptor extent
            // (ascent + descent) instead, which is how a Telugu line drawn in Gautami inside
            // an Arial Unicode MS paragraph clips 1.735 em on a 1.3398 em pitch. Measured
            // 2026-09-07 on 25 face x spacing x line-count cases (Arial, Times, Arial
            // Unicode MS, Gautami, Mangal, Segoe UI Historic, Helvetica; 1-3 lines;
            // FullSize and default), the rectangle exact to the printed digits in every
            // one - and NOT a max of the two: a default-spaced Arial Unicode MS line clips
            // 1.16 em, under its 1.338 em descriptor extent. An unwrapped line's horizontal
            // overrun is cut by the same rectangle.
        private Rectangle? ParagraphClip(FlowTextFragmentState wtf, double bottom, double bandHeight, Text.TextState? shapingState)
            {
                if (CurWidth <= 0 || wtf.unclippedLines) return null;
                var h = shapingState is not null && DescriptorExtentEm(shapingState, wtf.baseFont) is > 0 and var extentEm
                    ? extentEm * wtf.fontSize
                    : ParagraphClipLineBoxEm * bandHeight;
                return new Rectangle(CurLeft, bottom, CurLeft + CurWidth, bottom + h);
            }

        /// <summary>What a run occupies past its glyphs' advances at <paramref name="fontSize"/>:
        /// a synthetic weight's pen and a shear's lean (see
        /// <see cref="Text.TextFormattingOptions.SyntheticBoldPen"/> and
        /// <see cref="Text.TextFormattingOptions.Skew"/>).</summary>
        private static double OccupiedExtra(Text.TextFormattingOptions? options, double fontSize) =>
            options is null ? 0 : options.SyntheticBoldPen + Math.Abs(options.SyntheticItalicLean) * fontSize;

        /// <summary>The names of the alpha states the fragment's rules paint under,
        /// ensured on the start page; null where a rule is opaque.</summary>
        private (string? Underline, string? Strike) RuleAlphaStates(Text.TextFormattingOptions? options) =>
            (options?.UnderlineStyle?.Opacity is { } u ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, u, strokeToo: false) : null,
             options?.StrikeoutStyle?.Opacity is { } s ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, s, strokeToo: false) : null);

        /// <summary>What a rule under the run spans past its glyphs: the whole pen,
        /// half the lean.</summary>
        private static double RuleExtra(Text.TextFormattingOptions? options, double fontSize) =>
            options is null ? 0 : options.SyntheticBoldPen + Math.Abs(options.SyntheticItalicLean) * fontSize / 2;

        /// <summary>The text matrix's shear components a state asks for: <c>b</c>
        /// from the slope, <c>c</c> from the skew and the synthetic slant.</summary>
        private static (double B, double C) ShearOf(Text.TextState? state) =>
            state is null ? (0, 0) : Text.TextBuilder.ShearOf(state);

        /// <summary>The factor a state's horizontal scaling (a percentage, 100 =
        /// none) applies to every advance.</summary>
        private static double ScaleOf(Text.TextState? state) =>
            state is null ? 1 : state.HorizontalScaling / 100.0;

        /// <summary>A block background's box for a chunk the paragraph continues past
        /// on the next region: closed at the descent the caller named
        /// (<see cref="Text.TextFormattingOptions.BreakBoxDescentEm"/>), else as it is.</summary>
        private static (double Width, double Above, double Below)? BoxAtBreak(
            (double Width, double Above, double Below)? box, bool continues,
            Text.TextFormattingOptions? options, double fontSize) =>
            box is { } b && continues && options?.BreakBoxDescentEm is > 0 and var descentEm
                ? (b.Width, b.Above, descentEm * fontSize)
                : box;

        /// <summary>The character and word spacing each line of the chunk is shown
        /// with when the paragraph is justified by spacing: the line's slack shared
        /// by the ratio the caller gave; the last line keeps the fragment's own
        /// spacing unless asked to stretch too.</summary>
        private (double Tc, double Tw)[] JustifyingSpacings(FlowTextFragmentState wtf, Text.TextFragment tf, List<string> chunk)
        {
            var options = tf.TextState.FormattingOptions!;
            var ratio = options.JustifySpacingRatio!.Value;
            var baseTc = (double)tf.TextState.CharacterSpacing;
            var baseTw = (double)tf.TextState.WordSpacing;
            wtf.alignMeasurer ??= Text.TextPaginator.CreateMeasurer(wtf.baseFont, wtf.fontSize,
                tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
            var result = new (double, double)[chunk.Count];
            for (var j = 0; j < chunk.Count; j++)
            {
                result[j] = (baseTc, baseTw);
                var last = wtf.idx + j == wtf.allLines.Count - 1;
                if (last && !options.JustifyLastLine) continue;
                var text = chunk[j].TrimEnd(' ');
                var glyphs = text.Length;
                if (glyphs < 2) continue;
                var spaces = 0;
                foreach (var c in text) if (c == ' ') spaces++;
                var indent = wtf.idx + j == 0 ? wtf.firstLineIndent : wtf.subsequentLinesIndent;
                var measure = CurWidth - indent - OccupiedExtra(options, wtf.fontSize);
                // The base spacing rides every glyph, the last one's included; the
                // justifying share is spread over the gaps between them.
                var natural = wtf.alignMeasurer(text) * ScaleOf(tf.TextState) + baseTc * glyphs + baseTw * spaces;
                var slack = measure - natural;
                var share = (1 - ratio) * (glyphs - 1) + ratio * spaces;
                if (slack <= 0 || share <= 0) continue;
                var unit = slack / share;
                result[j] = (baseTc + (1 - ratio) * unit, baseTw + ratio * unit);
            }
            return result;
        }

        /// <summary>The paragraph's clip, widened by the reach of a block background
        /// painted outside the box, so the outset is not clipped away.</summary>
        private static Rectangle GrownByOutset(Rectangle clip, MarginInfo? outset) =>
            outset is null ? clip
                : new Rectangle(clip.LLX - outset.Left, clip.LLY - outset.Bottom,
                    clip.URX + outset.Right, clip.URY + outset.Top);

        /// <summary>The part of an outset that reaches OUTSIDE the box: a side taken
        /// inward (a background kept off the padding) must not narrow the text's clip.</summary>
        private static MarginInfo? OutwardPart(MarginInfo? outset) =>
            outset is null ? null
                : new MarginInfo(Math.Max(0, outset.Left), Math.Max(0, outset.Bottom),
                    Math.Max(0, outset.Right), Math.Max(0, outset.Top));

        /// <summary>The paragraph's clip, raised (or lowered) by how far a sloped
        /// baseline climbs (or falls) across the band, so the far end of the line
        /// is not cut off.</summary>
        private static Rectangle GrownBySlope(Rectangle clip, double slope, double width) =>
            slope == 0 ? clip
                : slope > 0 ? new Rectangle(clip.LLX, clip.LLY, clip.URX, clip.URY + slope * width)
                : new Rectangle(clip.LLX, clip.LLY + slope * width, clip.URX, clip.URY);

        /// <summary>The paragraph's clip, reaching an em past the measure when break
        /// spaces hang there: they paint nothing, but a clip that cuts them makes a
        /// reader of the page see a line that ends without its space.</summary>
        private static Rectangle GrownByHangingSpace(Rectangle clip, Text.TextFormattingOptions? options, double fontSize) =>
            options is { HangingBreakSpace: true }
                ? new Rectangle(clip.LLX, clip.LLY, clip.URX + fontSize, clip.URY)
                : clip;

        /// <summary>The paragraph's clip, extended by a text rise: raised glyphs
        /// stand above the band, lowered ones hang below it.</summary>
        private static Rectangle GrownByRise(Rectangle clip, double rise) =>
            rise == 0 ? clip
                : rise > 0 ? new Rectangle(clip.LLX, clip.LLY, clip.URX, clip.URY + rise)
                : new Rectangle(clip.LLX, clip.LLY + rise, clip.URX, clip.URY);

        private double LineAlignOffset(FlowTextFragmentState wtf, Text.TextFragment tf, string line)
            {
                if (wtf.noWrap || wtf.alignMode == HorizontalAlignment.Left) return 0;
                wtf.alignMeasurer ??= Text.TextPaginator.CreateMeasurer(wtf.baseFont, wtf.fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var slack = CurWidth - wtf.alignMeasurer(line.TrimEnd(' ')) * ScaleOf(tf.TextState)
                            - OccupiedExtra(tf.TextState.FormattingOptions, wtf.fontSize);
                if (slack <= 0) return 0;
                return wtf.alignMode == HorizontalAlignment.Center ? slack / 2 : slack;
            }
    }
}
