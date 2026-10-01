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
        /// <summary>The size a styled run draws at: a note mark at half its parent's
        /// size, a superscript at the superscript ratio, else its own.</summary>
        private static double StyledRunSize(StyledRun r) =>
            r.NoteMark ? r.Size * MarkerSizeRatio : r.Sup ? r.Size * 0.583 : r.Size;

        private static double MeasureStyled(string text, StyledRun r, double size)
        {
            var st = r.State;
            var fd = st.FontData ?? st.Font?.SourceFontData;
            var baseFont = Text.TextBuilder.MapToStandard14Public(st);
            return Text.TextPaginator.CreateMeasurer(baseFont, size, fd)(text);
        }

        /// <summary>Write a styled paragraph (heading with label / decorated
        /// segments, an inline-joined fragment chain, footnote reference
        /// marks) into the flow at the cursor. Line pitch is the line's
        /// dominant base size + <paramref name="lineSpacing"/>; the first
        /// line at a region top drops 0.8×size + spacing below the band top
        /// (the FloatingBox flow rule); later lines chain
        /// baselines. Emission goes through the deferred embedded-render
        /// queue so real fonts, colours, underline and superscript baselines
        /// all apply.</summary>
        public void WriteStyledParagraph(List<StyledRun> runs, double lineSpacing,
            Color? background = null, HorizontalAlignment align = HorizontalAlignment.Left)
        {
            var lines = LayoutStyledLines(runs, CurWidth);
            var hl = new StyledHighlight();
            // The leading sits above a line only when the line before it carried
            // text (and above the paragraph's first line); a line holding only a
            // note mark is as tall as the mark and charges no leading below it.
            var prevHadText = true;
            for (var lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var (left, cells) = lines[lineIdx];
                var m = MeasureStyledLine(cells, runs, lineIdx);
                // A picture-only line takes the picture's own height and no leading;
                // a line that also carries text keeps the text's pitch and lets the
                // picture overhang.
                var lh = m.HasText || (m.MaxImage <= 0 && m.BreakSize <= 0)
                    ? m.MaxBase + (prevHadText ? lineSpacing : 0)
                    : m.MaxBase;
                if (_curY - lh < EffectiveBottom)
                {
                    FlushStyledHighlight(hl, background);
                    FlowToNextRegion();
                    lh = m.MaxBase + lineSpacing;
                }
                // Line box = [cursor, cursor − pitch]; the queued Y is the box
                // bottom (descender line — the deferred TextBuilder write lifts
                // it by the font descent, landing the baseline exactly on
                // the line grid). Superscript runs raise the box; a note mark
                // hangs from the line's text top.
                var boxBottom = _curY - lh;
                var lineTop = boxBottom + lh;
                // A mark-only inline paragraph joined onto this line hangs its
                // mark from the line's box top and ends its own height below it.
                double joinH = 0;
                if (m.HasText)
                    foreach (var (_, _, r) in cells)
                        if (r.NoteMark && r.JoinHeight > 0) joinH = Math.Max(joinH, r.JoinHeight);
                var markTop = joinH > 0 ? lineTop : boxBottom + m.MaxBase;
                if (m.HasText) _lastTextLinePitch = m.MaxBase + lineSpacing;
                // Natural cell extents, then the line's alignment: justified
                // lines spread their slack over the interior spaces (not the
                // paragraph's last line), centred / right lines shift whole.
                var (sized, lineW) = SizeStyledCells(cells);
                var xs = AlignedCellXs(sized, lineW, CurWidth - left, align, lineIdx == lines.Count - 1);
                DrawStyledCells(sized, xs, left, lineTop, boxBottom, markTop, m.MaxBase);
                hl.Open(boxBottom, CurLeft + left, m.MaxBase);
                hl.Extend(boxBottom, lineW);
                // A break line carries no text, but the line AFTER it still charges its
                // own leading, so it counts as a text line for that purpose.
                prevHadText = m.HasText || m.BreakSize > 0;
                QueueStyledRunLinks(sized, xs, left, boxBottom, m.MaxBase);
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY = joinH > 0 ? lineTop - joinH : boxBottom;
            }
            FlushStyledHighlight(hl, background);
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>A styled line's vertical extents: the dominant base size the pitch is built
        /// on, the note mark and its parent text, the tallest picture and the tallest hard
        /// break.</summary>
        private readonly record struct StyledLineExtents(double MaxBase, double MaxImage,
            double BreakSize, bool HasText);

        /// <summary>Measures one laid-out line's extents. A paragraph that is only its note mark
        /// is as tall as the mark's parent text; a mark that wrapped onto its own line is as tall
        /// as the mark.</summary>
        private static StyledLineExtents MeasureStyledLine(
            List<(double x, string text, StyledRun run)> cells, List<StyledRun> runs, int lineIdx)
        {
            double maxBase = 0, markSize = 0, markParent = 0, maxImage = 0, breakSize = 0;
            foreach (var (_, _, r) in cells)
            {
                if (r.HardBreak) { breakSize = Math.Max(breakSize, r.Size); continue; }
                if (r.ImageData is not null) { maxImage = Math.Max(maxImage, r.ImageH); continue; }
                if (r.NoteMark)
                {
                    markSize = Math.Max(markSize, StyledRunSize(r));
                    markParent = Math.Max(markParent, r.Size);
                }
                else if (!r.Sup && r.Size > maxBase) maxBase = r.Size;
            }
            var hasText = maxBase > 0;
            if (!hasText)
                maxBase = maxImage > 0 ? maxImage
                    : breakSize > 0 ? breakSize
                    : lineIdx == 0 && markParent > 0 ? markParent
                    : markSize > 0 ? markSize : runs.Count > 0 ? runs[0].Size : 10;
            return new StyledLineExtents(maxBase, maxImage, breakSize, hasText);
        }

        /// <summary>Each cell's draw size and natural extent, and the line's own width.</summary>
        private static (List<(double x, string text, StyledRun run, double size)> sized, double lineW)
            SizeStyledCells(List<(double x, string text, StyledRun run)> cells)
        {
            var sized = new List<(double x, string text, StyledRun run, double size)>(cells.Count);
            double lineW = 0;
            foreach (var (xr, text, r) in cells)
            {
                var size = StyledRunSize(r);
                sized.Add((xr, text, r, size));
                if (r.ImageData is not null) lineW = Math.Max(lineW, xr + r.ImageW);
                else if (text.Length > 0) lineW = Math.Max(lineW, xr + MeasureStyled(text, r, size));
            }
            return (sized, lineW);
        }

        /// <summary>Queues one line's cells: pictures against the line top, text on the line grid
        /// (a superscript raised, a note mark hung from the line's mark top).</summary>
        private void DrawStyledCells(List<(double x, string text, StyledRun run, double size)> sized,
            double[] xs, double left, double lineTop, double boxBottom, double markTop, double maxBase)
        {
            for (var ci = 0; ci < sized.Count; ci++)
            {
                var (_, text, r, size) = sized[ci];
                if (r.ImageData is not null)
                {
                    var ix = CurLeft + left + xs[ci];
                    _pendingImages.Add((_currentSlot, r.ImageData,
                        new Rectangle(ix, lineTop - r.ImageH, ix + r.ImageW, lineTop), false));
                    continue;
                }
                if (text.Length == 0) continue;
                var cx = CurLeft + left + xs[ci];
                var y = (r.NoteMark ? markTop - size : boxBottom + (r.Sup ? 0.33 * maxBase : 0))
                        + Std14Seat(r.State, size);
                _pendingEmbeddedRenders.Add((_currentSlot, cx, _curY, text, r.State, size, y));
                if (r.NoteMark && r.Note is { } markNote)
                {
                    _noteMarkLine[markNote] = (_currentSlot, markTop);
                    QueueNoteLink(markNote, cx, markTop, MeasureStyled(text, r, size), size);
                }
            }
        }

        /// <summary>ONE Link annotation per hyperlinked run per line — consecutive word/space
        /// cells of the same run coalesce into a single rect.</summary>
        private void QueueStyledRunLinks(List<(double x, string text, StyledRun run, double size)> sized,
            double[] xs, double left, double boxBottom, double maxBase)
        {
            Hyperlink? runLink = null;
            Text.TextState? runLinkState = null;
            double runLinkSize = 0;
            double linkX0 = 0, linkX1 = 0;
            void FlushRunLink()
            {
                if (runLink is not null && linkX1 > linkX0)
                {
                    // The queued Y is the descender line, so the baseline sits
                    // one descent above it and the box closes at the ascent.
                    var (lkAbove, lkBelow) = LinkBoxExtent(runLinkState,
                        runLinkSize > 0 ? runLinkSize : maxBase);
                    _pendingLinks.Add((_currentSlot,
                        new Rectangle(linkX0, boxBottom, linkX1,
                            boxBottom + lkAbove + lkBelow), runLink));
                }
                runLink = null;
            }
            for (var ci = 0; ci < sized.Count; ci++)
            {
                var (_, text, r, size) = sized[ci];
                if (r.Link is null || text.Length == 0) { FlushRunLink(); continue; }
                var x0 = CurLeft + left + xs[ci];
                var x1 = x0 + MeasureStyled(text, r, size);
                if (!ReferenceEquals(runLink, r.Link))
                {
                    FlushRunLink();
                    runLink = r.Link; linkX0 = x0;
                    runLinkState = r.State; runLinkSize = r.Size;
                }
                linkX1 = x1;
            }
            FlushRunLink();
        }

        /// <summary>The background a styled paragraph fills behind its lines: one rectangle per
        /// write region, from the region's last line box bottom up to the first line's bottom
        /// plus the highlight box height, as wide as the region's widest line.</summary>
        private sealed class StyledHighlight
        {
            public bool IsOpen;
            public double FirstBottom;
            public double LastBottom;
            public double Left;
            public double Width;
            public double Size;

            /// <summary>Opens the region's rectangle on its first line; later lines are ignored.</summary>
            public void Open(double boxBottom, double left, double size)
            {
                if (IsOpen) return;
                IsOpen = true;
                FirstBottom = boxBottom;
                Left = left;
                Size = size;
                Width = 0;
            }

            public void Extend(double boxBottom, double lineW)
            {
                LastBottom = boxBottom;
                Width = Math.Max(Width, lineW);
            }
        }

        /// <summary>Fills the highlight accumulated over this write region and closes it.</summary>
        private void FlushStyledHighlight(StyledHighlight hl, Color? background)
        {
            if (!hl.IsOpen) return;
            hl.IsOpen = false;
            if (background is null || hl.Width <= 0) return;
            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            if (background.AByte < 255
                && Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, background.AByte) is { } bgGs)
                b.SetExtGState(bgGs);
            b.SetFillColor(background.R / 255.0, background.G / 255.0, background.B / 255.0);
            b.Rectangle(hl.Left, hl.LastBottom, hl.Width,
                hl.FirstBottom + HighlightBoxEm * hl.Size - hl.LastBottom);
            b.Fill();
            b.RestoreState();
            WriteContent(b.Build());
        }


        /// <summary>Advance of <paramref name="text"/> drawn in <paramref name="st"/> at <paramref name="size"/>.</summary>
        private static double MeasureStyledText(string text, Text.TextState st, double size)
        {
            var fd = st.FontData ?? st.Font?.SourceFontData;
            return Text.TextPaginator.CreateMeasurer(Text.TextBuilder.MapToStandard14Public(st), size, fd)(text);
        }





        /// <summary>Measure the rendered width of <paramref name="text"/> in points
        /// using the same Standard-14 metrics that <see cref="Text.TextPaginator"/>
        /// uses for line-break calculations -- keeping the two in sync means
        /// per-segment link rectangles align with the wrap breakpoints that
        /// produced the rendered line.</summary>
        /// <summary>Queue one absolutely-placed text run on the current region — the
        /// SVG <c>&lt;text&gt;</c> path, whose glyphs are positioned by the SVG's own
        /// transform rather than by the line flow.</summary>
        public void WriteAbsoluteText(double x, double baselineY, string text,
            double fontSize, Text.Font? font)
        {
            if (string.IsNullOrEmpty(text)) return;
            _pendingEmbeddedRenders.Add((_currentSlot, x, baselineY, text,
                new Text.TextState { Font = font }, fontSize, baselineY));
        }

        private static double MeasureText(string text, string fontName, double fontSize)
        {
            double w = 0;
            foreach (var c in text)
            {
                var glyph = c < 256 ? c : '?';
                var cw = Text.Standard14Fonts.GetWidth(fontName, glyph);
                if (cw < 0) cw = 500;
                w += cw * fontSize / 1000.0;
            }
            return w;
        }

        private void WriteContent(byte[] content)
        {
            if (_overflowBuffer is null)
                _startPage.AddContentStream(content);
            else
                _overflowBuffer.Add(content);
        }

        /// <summary>Write a fragment's content honouring its marked-content
        /// tagging: a tagged fragment routes through the page's BDC/EMC wrapper
        /// (which merges directly consecutive same-tag/-MCID runs into one
        /// block). Overflow-buffered content keeps the plain path — the buffer
        /// is raw byte concatenation with no merge point.</summary>
        private void WriteContent(byte[] content, Text.TextState state)
        {
            if (_overflowBuffer is null && state.MarkedContentTag is { } tag)
                _startPage.AddMarkedContentStream(content, tag, state.MarkedContentMcid);
            else
                WriteContent(content);
        }

        private static byte[] BuildWrappedTextStream(List<string> lines, string fontResName, double fontSize,
            double startX, double startY, double lineHeight, Color? foreground,
            bool strikeOut = false, bool underline = false, string? fontName = null,
            string? alphaGsName = null, double firstLineIndent = 0,
            double subsequentLinesIndent = 0, bool chunkStartsParagraph = true,
            Color? background = null, string? bgAlphaGsName = null,
            double rotation = 0, double? firstBaselineSeat = null,
            IReadOnlyList<double>? lineOffsets = null,
            double charSpacing = 0, double wordSpacing = 0,
            (double Width, double Above, double Below)? blockBackground = null,
            Text.TextState? decorated = null, (string? Underline, string? Strike) ruleAlpha = default,
            (double Tc, double Tw)[]? lineSpacings = null)
        {
            // The left indent of this chunk's first rendered line: the paragraph's
            // own first line uses FirstLineIndent; a chunk that continues the
            // paragraph onto a new page is all "subsequent" lines. Per-line
            // alignment offsets, when given, replace both.
            var firstIndent = chunkStartsParagraph ? firstLineIndent : subsequentLinesIndent;
            var indents = new double[Math.Max(1, lines.Count)];
            for (var i = 0; i < lines.Count; i++)
                indents[i] = lineOffsets is not null ? lineOffsets[i]
                    : i == 0 ? firstIndent : subsequentLinesIndent;

            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            if (alphaGsName is not null)
                b.SetExtGState(alphaGsName);
            if (foreground is not null)
                b.SetFillColor(foreground.R / 255.0, foreground.G / 255.0, foreground.B / 255.0);
            // startY is the top of the text band. Drop the first baseline by the
            // font ascent (cap height) so the glyph tops align with the top margin
            // — the standard first-line placement. Subsequent lines advance
            // by lineHeight, so the whole block shifts down uniformly.
            var capHeight = fontName is not null ? Text.Standard14Fonts.GetCapHeight(fontName) : 0;
            var ascent = capHeight > 0 ? capHeight / 1000.0 * fontSize : fontSize * 0.7;
            // A caller that knows the generator line model hands the seat in
            // (box bottom + face descent); the cap-height drop is the legacy
            // placement for everything else.
            var firstBaseline = firstBaselineSeat ?? startY - ascent;

            if (background is { } bgcol)
                AppendWrappedBackground(b, lines, indents, fontName, fontSize, startX, firstBaseline,
                    lineHeight, bgcol, bgAlphaGsName, blockBackground,
                    decorated?.FormattingOptions?.BlockBackgroundOutset);
            AppendWrappedShows(b, lines, indents, fontResName, fontSize, lineHeight, startX,
                firstBaseline, rotation, charSpacing, wordSpacing, decorated, lineSpacings);
            if ((strikeOut || underline) && fontName is not null)
                AppendWrappedDecorations(b, lines, indents, fontName, fontSize, startX, firstBaseline,
                    lineHeight, strikeOut, underline, decorated, ruleAlpha);

            b.RestoreState();
            return b.Build();
        }

        /// <summary>Background highlight: a filled rectangle behind each wrapped line, sized to
        /// the line's measured width and the font's em box (baseline + descent up by one font
        /// size). Drawn before the glyphs, in its own graphics state so the background's /ca alpha
        /// doesn't bleed into the foreground fill that follows.</summary>
        private static void AppendWrappedBackground(Content.ContentStreamBuilder b, List<string> lines,
            double[] indents, string? fontName, double fontSize, double startX, double firstBaseline,
            double lineHeight, Color bgcol, string? bgAlphaGsName,
            (double Width, double Above, double Below)? blockBackground, MarginInfo? outset = null)
        {
            var bgFontName = fontName ?? "Helvetica";
            var descentPt = Text.Standard14Fonts.GetDescent(bgFontName) / 1000.0 * fontSize; // negative
            b.SaveState();
            if (bgAlphaGsName is not null) b.SetExtGState(bgAlphaGsName);
            b.SetFillColor(bgcol.R / 255.0, bgcol.G / 255.0, bgcol.B / 255.0);
            if (blockBackground is { } block)
            {
                // The block's own box: from the top of the FIRST line box to
                // the bottom of the LAST, at the content width. That is
                // lines.Count line boxes tall by construction, so a declared
                // leading grows it, which is what tells it apart from the
                // per-line highlight below.
                var top = firstBaseline + block.Above + (outset?.Top ?? 0);
                var bottom = firstBaseline - (lines.Count - 1) * lineHeight - block.Below - (outset?.Bottom ?? 0);
                b.Rectangle(startX - (outset?.Left ?? 0), bottom,
                    block.Width + (outset?.Left ?? 0) + (outset?.Right ?? 0), top - bottom);
                b.Fill();
            }
            else
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    var lineW = MeasureLineWidth(lines[i], bgFontName, fontSize);
                    if (lineW <= 0) continue;
                    var lineY = firstBaseline - i * lineHeight;
                    b.Rectangle(startX + indents[i], lineY + descentPt, lineW, fontSize * HighlightBoxEm);
                    b.Fill();
                }
            }
            b.RestoreState();
        }

        /// <summary>The glyphs themselves: one show per wrapped line, each line reached by the
        /// change in indent (Td is relative to the current line start, so a plain T* would carry
        /// the first line's indent down to every line).</summary>
        private static void AppendWrappedShows(Content.ContentStreamBuilder b, List<string> lines,
            double[] indents, string fontResName, double fontSize, double lineHeight, double startX,
            double firstBaseline, double rotation, double charSpacing, double wordSpacing,
            Text.TextState? decorated = null, (double Tc, double Tw)[]? lineSpacings = null)
        {
            if (lineSpacings is not null)
            {
                AppendJustifiedShows(b, lines, indents, fontResName, fontSize, lineHeight, startX, firstBaseline,
                    decorated, lineSpacings);
                return;
            }
            b.BeginText();
            b.SetFont(fontResName, fontSize);
            b.SetLeading(lineHeight);
            // Tc and Tw are ordinary text state, and this writer simply never
            // emitted them: the wrap already measured with the character spacing
            // (so the break points were right) while the glyphs went down without
            // it, and the word spacing was not read at all. Only non-zero values
            // are written, so a fragment that sets neither emits neither.
            if (charSpacing != 0) b.SetCharSpacing(charSpacing);
            if (wordSpacing != 0) b.SetWordSpacing(wordSpacing);
            // TextState.Rotation rotates the whole block around its first baseline
            // origin via the text matrix (Td/T* then advance in rotated text space).
            if (rotation != 0)
            {
                var rad = rotation * Math.PI / 180.0;
                var cos = Math.Round(Math.Cos(rad), 10);
                var sin = Math.Round(Math.Sin(rad), 10);
                b.SetTextMatrix(cos, sin, -sin, cos, startX + indents[0], firstBaseline);
            }
            else if (ShearOf(decorated) is var (slope, skew) && (slope != 0 || skew != 0))
            {
                // A sheared upright face: c leans the verticals, b tilts the baseline.
                b.SetTextMatrix(1, slope, skew, 1, startX + indents[0], firstBaseline);
            }
            else
            {
                b.MoveTextPosition(SnapLinePosition(startX + indents[0], decorated),
                    SnapLinePosition(firstBaseline, decorated));
            }
            AppendStrokeState(b, decorated);
            AppendRiseAndScaling(b, decorated);
            var (lineSlope, lineSkew) = ShearOf(decorated);
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0 && (lineSlope != 0 || lineSkew != 0) && rotation == 0)
                {
                    // A relative move in sheared text space would lean the line's
                    // origin too; each line gets its own matrix at the margin.
                    b.SetTextMatrix(1, lineSlope, lineSkew, 1, startX + indents[i], firstBaseline - i * lineHeight);
                }
                else if (i > 0 && rotation == 0 && decorated?.FormattingOptions?.LinePositionDecimals is not null)
                {
                    // Each line lands on its own rounded origin, reached from the last one's.
                    b.MoveTextPosition(
                        SnapLinePosition(startX + indents[i], decorated) - SnapLinePosition(startX + indents[i - 1], decorated),
                        SnapLinePosition(firstBaseline - i * lineHeight, decorated)
                            - SnapLinePosition(firstBaseline - (i - 1) * lineHeight, decorated));
                }
                else if (i > 0)
                {
                    var delta = indents[i] - indents[i - 1];
                    if (delta != 0) b.MoveTextPosition(delta, -lineHeight);
                    else b.NextLine();
                }
                b.ShowText(lines[i]);
            }
            b.EndText();
        }

        /// <summary>A line origin's coordinate rounded to the decimals the caller's writer
        /// states positions in (<see cref="Text.TextFormattingOptions.LinePositionDecimals"/>),
        /// halves away from zero; as it is when none are given.</summary>
        private static double SnapLinePosition(double value, Text.TextState? state) =>
            state?.FormattingOptions?.LinePositionDecimals is { } decimals
                ? Math.Round(value, decimals, MidpointRounding.AwayFromZero)
                : value;

        /// <summary>Strikeout / underline rectangles, emitted after the text. One per wrapped
        /// line, sized to the line's measured width.</summary>
        private static void AppendWrappedDecorations(Content.ContentStreamBuilder b, List<string> lines,
            double[] indents, string fontName, double fontSize, double startX, double firstBaseline,
            double lineHeight, bool strikeOut, bool underline, Text.TextState? decorated = null,
            (string? Underline, string? Strike) ruleAlpha = default)
        {
            var options = decorated?.FormattingOptions;
            // A hanging break space renders past the margin where nothing shows;
            // the rule stops at the ink. What a synthetic weight or shear adds
            // to the run's box, the rule spans too.
            var trimHanging = options?.HangingBreakSpace ?? false;
            var ruleExtra = RuleExtra(options, fontSize);
            var thickness = fontSize * DecorationThicknessEm;
            // Both rules hang off the decoration origin below the baseline;
            // the strike-through rises a fixed share of the em above it.
            var origin = -DecorationOriginDescentShare * DescentNorm(fontName) * fontSize;
            var soOffset = origin + StrikeoutRiseEm * fontSize;
            // A caller's own rule geometry replaces those defaults: its thickness
            // and the height of its centre, from which the rectangle's bottom is
            // half a thickness down.
            var (underlineH, underlineY) = RuleGeometry(options?.UnderlineStyle, fontSize, thickness, origin);
            var (strikeH, strikeY) = RuleGeometry(options?.StrikeoutStyle, fontSize, thickness, soOffset);
            for (var i = 0; i < lines.Count; i++)
            {
                var lineW = MeasureLineWidth(trimHanging ? lines[i].TrimEnd(' ') : lines[i], fontName, fontSize) * ScaleOf(decorated) + ruleExtra;
                var lineY = firstBaseline - i * lineHeight;
                var lineX = startX + indents[i];
                if (strikeOut)
                {
                    AppendRuleState(b, options?.StrikeoutStyle, ruleAlpha.Strike);
                    b.Rectangle(lineX, lineY + strikeY, lineW, strikeH);
                    b.Fill();
                }
                if (underline)
                {
                    AppendRuleState(b, options?.UnderlineStyle, ruleAlpha.Underline);
                    b.Rectangle(lineX, lineY + underlineY, lineW, underlineH);
                    b.Fill();
                }
            }
        }

        /// <summary>The lines of a paragraph justified by spacing, each as its OWN text
        /// object with its own character and word spacing, the spaces it hangs past
        /// the measure shown after its text: what a reader of the page sees as a
        /// line's text is the line, not the line and its break.</summary>
        private static void AppendJustifiedShows(Content.ContentStreamBuilder b, List<string> lines,
            double[] indents, string fontResName, double fontSize, double lineHeight, double startX,
            double firstBaseline, Text.TextState? decorated, (double Tc, double Tw)[] lineSpacings)
        {
            // The spacing is graphics state and outlives a text object: each line
            // sets what differs from the line before, the last one back to none.
            var (curTc, curTw) = (0.0, 0.0);
            for (var i = 0; i < lines.Count; i++)
            {
                b.BeginText();
                b.SetFont(fontResName, fontSize);
                b.MoveTextPosition(startX + indents[i], firstBaseline - i * lineHeight);
                AppendStrokeState(b, decorated);
                AppendRiseAndScaling(b, decorated);
                var (tc, tw) = lineSpacings[i];
                if (Math.Abs(tc - curTc) > 1e-9) { b.SetCharSpacing(tc); curTc = tc; }
                if (Math.Abs(tw - curTw) > 1e-9) { b.SetWordSpacing(tw); curTw = tw; }
                var ink = lines[i].TrimEnd(' ');
                if (ink.Length > 0) b.ShowText(ink);
                if (ink.Length < lines[i].Length) b.ShowText(lines[i].Substring(ink.Length));
                b.EndText();
            }
        }

        /// <summary>The rise (<c>Ts</c>) and horizontal scaling (<c>Tz</c>) a state
        /// declares, in that order, only when they differ from the defaults.</summary>
        private static void AppendRiseAndScaling(Content.ContentStreamBuilder b, Text.TextState? state)
        {
            if (state is null) return;
            if (state.TextRise != 0) b.SetTextRise(state.TextRise);
            if (Math.Abs(state.HorizontalScaling - 100) > 1e-9) b.SetHorizontalScaling(state.HorizontalScaling);
        }

        /// <summary>A styled rule's own colour, opacity and cap, set before it is painted.</summary>
        private static void AppendRuleState(Content.ContentStreamBuilder b, Text.TextDecorationStyle? style, string? alphaGs)
        {
            if (style is null) return;
            if (style.Color is { } c) b.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
            if (alphaGs is not null) b.SetExtGState(alphaGs);
            if (style.LineCap != 0) b.SetLineCap(style.LineCap);
        }

        /// <summary>A rule's height and its bottom's height above the baseline: the
        /// style's own when there is one, else the flow's defaults.</summary>
        private static (double Height, double Bottom) RuleGeometry(Text.TextDecorationStyle? style,
            double fontSize, double defaultThickness, double defaultBottom)
        {
            if (style is null) return (defaultThickness, defaultBottom);
            var (thickness, centre) = style.At(fontSize);
            return (thickness, centre - thickness / 2);
        }

        /// <summary>The stroke a non-fill rendering mode paints with: the mode, the
        /// pen width and the stroking colour, inside the text object where the
        /// reference writers put them. Nothing for plain filled text.</summary>
        private static void AppendStrokeState(Content.ContentStreamBuilder b, Text.TextState? state)
        {
            if (state is null) return;
            if (state.RenderingMode != Text.TextRenderingMode.FillText)
            {
                b.SetTextRenderingMode((int)state.RenderingMode);
                if (state.LineWidth != 1.0) b.SetLineWidth(state.LineWidth);
            }
            else if (state.FormattingOptions?.SyntheticBoldPen is > 0 and var pen)
            {
                b.SetTextRenderingMode((int)Text.TextRenderingMode.FillThenStrokeText);
                b.SetLineWidth(pen);
            }
            else return;
            if (state.ExplicitStrokingColor is { } stroke) b.SetStrokeColor(stroke.R / 255.0, stroke.G / 255.0, stroke.B / 255.0);
        }


        private static double MeasureLineWidth(string line, string fontName, double fontSize)
        {
            if (Text.Standard14Fonts.IsStandard14(fontName))
            {
                double w = 0;
                foreach (var ch in line)
                {
                    var cw = Text.Standard14Fonts.GetWidth(fontName, ch < 256 ? ch : '?');
                    w += (cw >= 0 ? cw : 500) * fontSize / 1000.0;
                }
                return w;
            }
            return line.Length * fontSize * 0.5;
        }

        /// <summary>Write an inline-model paragraph (see the model above) at the
        /// cursor. Text goes through the deferred render queue (real faces,
        /// colours), images through the image queue, decorations and links are
        /// placed here from the same geometry.</summary>
        public void WriteInlineParagraph(List<InlineRun> runs, HorizontalAlignment align)
        {
            foreach (var rawCells in LayoutInlineLines(runs, CurWidth))
            {
                var cells = MergeInlineCells(rawCells);
                var m = MeasureInlineLine(cells);
                EnsureRoom(m.Advance);
                if (m.HasText) _lastTextLinePitch = m.Advance;
                var lineTop = _curY;
                var slack = align switch
                {
                    HorizontalAlignment.Right => CurWidth - m.LineWidth,
                    HorizontalAlignment.Center => (CurWidth - m.LineWidth) / 2,
                    _ => 0,
                };
                if (slack < 0) slack = 0;
                WriteInlineCells(cells, m.GroupPitch, lineTop, slack);
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY = lineTop - m.Advance;
            }
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>One show per run per line: consecutive cells of the same run merge (the
        /// absorber then reports one fragment per run and line, as the generator does), so word
        /// tokens never surface as fragments.</summary>
        private static List<(double x, string text, InlineRun run)> MergeInlineCells(
            List<(double x, string text, InlineRun run)> rawCells)
        {
            var cells = new List<(double x, string text, InlineRun run)>();
            foreach (var cell in rawCells)
            {
                if (cells.Count > 0 && ReferenceEquals(cells[^1].run, cell.run) && cell.run.ImageData is null)
                    cells[^1] = (cells[^1].x, cells[^1].text + cell.text, cell.run);
                else cells.Add(cell);
            }
            return cells;
        }

        /// <summary>An inline line's geometry: the tallest pitch in each of its groups, the
        /// advance the cursor takes from it, its width, and whether any text sits on it.</summary>
        private readonly record struct InlineLineMetrics(Dictionary<int, double> GroupPitch,
            double Advance, double LineWidth, bool HasText);

        /// <summary>Measures one inline line. A line holding no text and no picture — a newline
        /// segment's own line — is one builder-default line (10 pt) and charges no leading
        /// (probed: a 12 pt / 15 pt-leading fragment opening with a newline drops 10 to it, then
        /// 27 to its first text line).</summary>
        private static InlineLineMetrics MeasureInlineLine(
            List<(double x, string text, InlineRun run)> cells)
        {
            var groupPitch = new Dictionary<int, double>();
            double maxImageH = 0, lineWidth = 0;
            var lastTextGroup = -1;
            foreach (var (cx, text, r) in cells)
            {
                lineWidth = Math.Max(lineWidth, cx + InlineMeasure(text, r));
                if (r.ImageData is not null) { maxImageH = Math.Max(maxImageH, r.ImageH); continue; }
                if (r.NoteMarker) continue;
                groupPitch.TryGetValue(r.Group, out var gp);
                groupPitch[r.Group] = Math.Max(gp, r.Pitch);
                lastTextGroup = r.Group;
            }
            var advance = lastTextGroup >= 0 ? groupPitch[lastTextGroup] : maxImageH;
            if (advance <= 0) advance = BuilderDefaultLinePt;
            return new InlineLineMetrics(groupPitch, advance, lineWidth, lastTextGroup >= 0);
        }

        /// <summary>Queues one inline line's cells: pictures and inline graphics against the line
        /// top, text on its group's line grid, then the line's decoration overlay.</summary>
        private void WriteInlineCells(List<(double x, string text, InlineRun run)> cells,
            Dictionary<int, double> groupPitch, double lineTop, double slack)
        {
            var deco = new InlineDecorations();
            var link = new InlineLinkRun();
            foreach (var (cx, text, r) in cells)
            {
                var x0 = CurLeft + slack + cx;
                if (r.Graph is { } inlineGraph)
                {
                    FlushInlineLink(link);
                    WriteContent(inlineGraph.Build(CurrentPage, x0, lineTop - r.ImageH));
                    continue;
                }
                if (r.ImageData is not null)
                {
                    FlushInlineLink(link);
                    _pendingImages.Add((_currentSlot, r.ImageData,
                        new Rectangle(x0, lineTop - r.ImageH, x0 + r.ImageW, lineTop), false));
                    continue;
                }
                if (text.Length == 0) { FlushInlineLink(link); continue; }
                if (r.NoteMarker)
                {
                    FlushInlineLink(link);
                    WriteInlineNoteMarker(r, text, x0, lineTop, groupPitch);
                    continue;
                }
                var boxBottom = lineTop - groupPitch[r.Group];
                var descent = RunDescentEm(r.State) * r.Size;
                var baseline = boxBottom + descent;
                // The deferred writer lifts an embedded face by its own descent;
                // a Standard-14 run is seated on the baseline directly.
                _pendingEmbeddedRenders.Add((_currentSlot, x0, lineTop, text, r.State, r.Size,
                    RunIsEmbedded(r.State) ? boxBottom : baseline));
                var w = InlineMeasure(text, r);
                AppendInlineRunDecorations(deco, r, x0, w, lineTop, baseline, descent);
                if (r.Link is null) { FlushInlineLink(link); continue; }
                if (!ReferenceEquals(link.Link, r.Link))
                {
                    FlushInlineLink(link);
                    link.Link = r.Link;
                    link.X0 = x0;
                    link.State = r.State;
                    link.Size = r.Size;
                    link.Bottom = boxBottom;
                }
                link.X1 = x0 + w;
            }
            FlushInlineLink(link);
            if (deco.Close() is { } overlay) AddContentToSlot(_currentSlot, overlay);
        }

        /// <summary>A note marker hangs from the top of the line box of the group it refers to,
        /// at the marker's own size.</summary>
        private void WriteInlineNoteMarker(InlineRun r, string text, double x0, double lineTop,
            Dictionary<int, double> groupPitch)
        {
            groupPitch.TryGetValue(r.Group, out var parentPitch);
            if (parentPitch <= 0) parentPitch = r.Size / MarkerSizeRatio;
            var markBottom = lineTop - parentPitch + (parentPitch - r.Size);
            var markBaseline = markBottom + DescentNorm("Helvetica") * r.Size;
            _pendingEmbeddedRenders.Add((_currentSlot, x0, lineTop, text, r.State, r.Size, markBaseline));
            if (r.Note is { } inlineNote)
            {
                _noteMarkLine[inlineNote] = (_currentSlot, lineTop);
                QueueNoteLink(inlineNote, x0, lineTop, MeasureStyledText(text, r.State, r.Size), r.Size);
            }
        }

        /// <summary>One run's background box and its underline / strike-through. The HTML model
        /// hangs both rules off the baseline at its own em fractions; the legacy model hangs them
        /// off the decoration origin below it.</summary>
        private static void AppendInlineRunDecorations(InlineDecorations deco, InlineRun r,
            double x0, double w, double lineTop, double baseline, double descent)
        {
            if (r.Background is { } runBg)
            {
                var b = deco.Open();
                b.SetFillColor(runBg.R / 255.0, runBg.G / 255.0, runBg.B / 255.0);
                b.Rectangle(x0, lineTop - (HtmlRunBackgroundTopEm + HtmlRunBackgroundEm) * r.Size,
                    w, HtmlRunBackgroundEm * r.Size);
                b.Fill();
            }
            if (!r.Underline && !r.Strike) return;
            var thick = r.Size * (r.HtmlDeco ? HtmlDecoThicknessEm : DecorationThicknessEm);
            var deckBuilder = deco.Open();
            var fg = r.State.ForegroundColor;
            if (fg is not null) deckBuilder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
            else deckBuilder.SetFillColor(0, 0, 0);
            var underlineY = r.HtmlDeco
                ? baseline - HtmlUnderlineDropEm * r.Size - thick / 2
                : baseline - DecorationOriginDescentShare * descent;
            var strikeY = r.HtmlDeco
                ? baseline + HtmlStrikeRiseEm * r.Size - thick / 2
                : baseline - DecorationOriginDescentShare * descent + StrikeoutRiseEm * r.Size;
            if (r.Underline) { deckBuilder.Rectangle(x0, underlineY, w, thick); deckBuilder.Fill(); }
            if (r.Strike) { deckBuilder.Rectangle(x0, strikeY, w, thick); deckBuilder.Fill(); }
        }

        /// <summary>The decoration overlay one inline line accumulates — run backgrounds,
        /// underlines and strike-throughs — built only if some run asks for one, and written
        /// after the line's text.</summary>
        private sealed class InlineDecorations
        {
            private readonly Content.ContentStreamBuilder _builder = new();
            private bool _any;

            /// <summary>The overlay's builder, with its graphics state opened on first use.</summary>
            public Content.ContentStreamBuilder Open()
            {
                if (!_any) { _builder.SaveState(); _any = true; }
                return _builder;
            }

            /// <summary>The overlay's content stream, or null when no run decorated itself.</summary>
            public byte[]? Close()
            {
                if (!_any) return null;
                _builder.RestoreState();
                return _builder.Build();
            }
        }

        /// <summary>The link rect an inline line is accumulating: consecutive cells of the same
        /// run coalesce into one annotation.</summary>
        private sealed class InlineLinkRun
        {
            public Hyperlink? Link;
            public Text.TextState? State;
            public double Size;
            public double Bottom;
            public double X0;
            public double X1;
        }

        /// <summary>Emits the accumulated link rect, if it covers anything, and closes it.</summary>
        private void FlushInlineLink(InlineLinkRun link)
        {
            if (link.Link is not null && link.X1 > link.X0)
            {
                var (lkAbove, lkBelow) = LinkBoxExtent(link.State, link.Size);
                _pendingLinks.Add((_currentSlot,
                    new Rectangle(link.X0, link.Bottom, link.X1, link.Bottom + lkAbove + lkBelow),
                    link.Link));
            }
            link.Link = null;
        }


    }
}
