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
        /// <summary>The line loop: each wrapped line is seated, clipped, aligned and drawn, taking the page break and the fallback face its own text asks for. False means the caller writes nothing further.</summary>
        private bool WriteFragmentLines(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            while (wtf.idx < wtf.allLines.Count)
            {
                var fit = ChunkSizeForBand(wtf, tf);
                var chunkSize = Text.LineControls.Any(tf.TextState.FormattingOptions)
                    ? Text.LineControls.Kept(tf.TextState.FormattingOptions, fit, wtf.allLines.Count - wtf.idx, wtf.idx == 0,
                        !wtf.movedByLineControls && !NearRegionTop(tf))
                    : fit;
                if (chunkSize == 0)
                {
                    // The last line leaves no room for the margin under it (or the line controls
                    // move the paragraph on whole): it opens the next region, the way a continued
                    // paragraph does.
                    if (fit == 0) wtf.movedForBottomMargin = true;
                    else wtf.movedByLineControls = true;
                    // Nothing of the paragraph stays behind: a box around it closes
                    // where the paragraph began, above its top margin.
                    if (InsideBox) _partBottom = _curY + (wtf.idx == 0
                        || tf.TextState.FormattingOptions is { TopMarginAfterBreak: true } ? tf.Margin?.Top ?? 0 : 0);
                    FlowToNextRegion();
                    if (tf.TextState.FormattingOptions is { TopMarginAfterBreak: true }) AdvanceY(tf.Margin?.Top ?? 0);
                    RewrapRemainderForRegion(wtf, tf);
                    continue;
                }
                var chunk = wtf.allLines.GetRange(wtf.idx, chunkSize);
                if (wtf.fullJustify || wtf.justify) WriteJustifiedChunk(wtf, tf, chunk);
                else if (wtf.useEmbeddedFont || _forceDeferredWrites) QueueDeferredChunk(wtf, tf, chunk);
                else WritePlainChunk(wtf, tf, chunk);
                TraceChunkLines(wtf, chunk.Count);
                _curY -= wtf.variableLineHeights ? ChunkDrop(wtf, chunk.Count) : wtf.lineHeight * chunk.Count;
                wtf.idx += chunk.Count;
                if (wtf.idx >= wtf.allLines.Count) continue;
                if (InsideBox) _partBottom = PartBottomAtBreak(wtf, tf);
                FlowToNextRegion();
                if (tf.TextState.FormattingOptions is { TopMarginAfterBreak: true }) AdvanceY(tf.Margin?.Top ?? 0);
                RewrapRemainderForRegion(wtf, tf);
            }
            return true;
        }

        /// <summary>How many of the remaining lines fit in the band still open below the
        /// cursor - at least one, so a paragraph too tall for any band still advances.
        /// When the paragraph's last line would fit but leave no room for the bottom
        /// margin it keeps (<see cref="BottomReserve"/>), the band takes one line fewer;
        /// zero means the paragraph's only remaining line moves on - once, so a line
        /// that fits no region with its margin is still laid.</summary>
        private int ChunkSizeForBand(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            var remaining = wtf.allLines.Count - wtf.idx;
            if (tf.TextState.FormattingOptions is { LineFitsToDescent: true } && !wtf.variableLineHeights)
            {
                // Every line keeps its descent above the band less the room kept for the
                // margin: the half leading under it may run past, the margin may not.
                var fitting = LinesFittingBand(wtf,
                    _curY - EffectiveBottom - BottomReserve + LeadingBelowDescent(tf, wtf.fontSize, wtf.lineHeight));
                return fitting > 0 || wtf.movedForBottomMargin ? Math.Max(1, fitting) : 0;
            }
            var fit = LinesFittingBand(wtf, _curY - EffectiveBottom);
            if (BottomReserve <= 0 || fit < remaining || wtf.movedForBottomMargin
                || LinesFittingBand(wtf, _curY - EffectiveBottom - BottomReserve) >= remaining)
                return Math.Max(1, fit);
            return remaining - 1;
        }

        /// <summary>The half leading under a line's descent, which may fall past the band
        /// when the fragment asks for lines to fit down to their descent
        /// (<see cref="Text.TextFormattingOptions.LineFitsToDescent"/>); zero otherwise.</summary>
        private static double LeadingBelowDescent(Text.TextFragment tf, double fontSize, double lineHeight)
        {
            if (tf.TextState.FormattingOptions is not { LineFitsToDescent: true }) return 0;
            var (above, below) = LinkBoxExtent(tf.TextState, fontSize);
            return Math.Max(0, (lineHeight - (above + below)) / 2);
        }

        /// <summary>The room a fragment's first line needs to START in the band, as the
        /// line fitting prices it: its pitch, less the half leading under its descent and
        /// plus its bottom margin when its lines fit down to their descent.</summary>
        internal static double FirstLineNeedOf(Text.TextFragment tf)
        {
            var lineHeight = BlockLineHeight(tf);
            if (tf.TextState.FormattingOptions is not { LineFitsToDescent: true } options) return lineHeight;
            var fontSize = tf.TextState.FontSize > 0 ? (double)tf.TextState.FontSize : 12;
            return lineHeight - LeadingBelowDescent(tf, fontSize, lineHeight)
                + (options.BottomMarginInsideRegion ? tf.Margin?.Bottom ?? 0 : 0);
        }

        /// <summary>The room a fragment's lines need wrapped to the region, with its margins,
        /// as the line fitting prices them: a whole fragment kept together weighs this.</summary>
        internal double WholeNeedOf(Text.TextFragment tf)
        {
            var margins = (tf.Margin?.Top ?? 0) + (tf.Margin?.Bottom ?? 0);
            var lineHeight = BlockLineHeight(tf);
            var fontSize = tf.TextState.FontSize > 0 ? (double)tf.TextState.FontSize : 12;
            return LinesHeightOf(tf) + margins - LeadingBelowDescent(tf, fontSize, lineHeight);
        }

        /// <summary>How many of the remaining lines fit in <paramref name="room"/>.</summary>
        private int LinesFittingBand(FlowTextFragmentState wtf, double room)
        {
            var remaining = wtf.allLines.Count - wtf.idx;
            if (!wtf.variableLineHeights)
                return Math.Min(Math.Max(0, (int)(room / wtf.lineHeight)), remaining);
            // Fill the band line by line - the lines are not all the same height.
            var chunkSize = 0;
            double used = 0;
            while (chunkSize < remaining && used + HeightOfLine(wtf, wtf.idx + chunkSize) <= room)
            {
                used += HeightOfLine(wtf, wtf.idx + chunkSize);
                chunkSize++;
            }
            return chunkSize;
        }

        /// <summary>The cumulative height of the chunk's first <paramref name="lines"/> lines:
        /// the drop from the chunk's first baseline to line <paramref name="lines"/>, and, at
        /// the chunk's full count, the band the whole chunk fills.</summary>
        private double ChunkDrop(FlowTextFragmentState wtf, int lines)
        {
            double d = 0;
            for (var k = 0; k < lines; k++) d += HeightOfLine(wtf, wtf.idx + k);
            return d;
        }

        /// <summary>A fully justified chunk: every token of every line goes out as its own
        /// absolutely positioned show, so each line ends exactly on the region's right edge.</summary>
        private void WriteJustifiedChunk(FlowTextFragmentState wtf, Text.TextFragment tf, List<string> chunk)
        {
            wtf.justifyMeasurer ??= Text.TextPaginator.CreateMeasurer(wtf.baseFont, wtf.fontSize,
                tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
            // Baselines follow the deferred-render chain: first line at the
            // top of a region drops by the font size, later lines sit one
            // line height below the previous baseline (same rule as the
            // embedded branch below).
            // A CALLER-set LineSpacing adds its leading above the first
            // line too (23 pt drop for 10 pt + 13);
            // synthetic (layout-assigned) leading keeps the plain drop.
            var firstLineBaseline = _lastBodyBaseline.HasValue
                ? _lastBodyBaseline.Value - wtf.lineHeight
                : _curY - (tf.TextState.LineSpacing > 0 && !tf.TextState.LineSpacingSynthetic
                    ? wtf.lineHeight : wtf.fontSize);
            CaptureFirstLine(firstLineBaseline);
            for (var j = 0; j < chunk.Count; j++)
            {
                var lineBaseline = firstLineBaseline - j * wtf.lineHeight;
                // A justified (not full-justified) paragraph leaves its last line at its natural width.
                var stretchTo = wtf.justify && wtf.idx + j == wtf.allLines.Count - 1 ? 0 : CurWidth;
                foreach (var (token, xOffset) in JustifyLineTokens(chunk[j], wtf.justifyMeasurer, stretchTo))
                    _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + xOffset, _curY,
                        token, tf.TextState, wtf.fontSize, lineBaseline));
            }
            _lastBodyBaseline = firstLineBaseline - (chunk.Count - 1) * wtf.lineHeight;
            // All later content in this flow must defer to keep the page's
            // content-stream order equal to paragraph order (see field doc).
            _forceDeferredWrites = true;
            if (_overflowBuffer is not null)
                _overflowBuffer.Add(Array.Empty<byte>());
        }

        /// <summary>How far above the seat it is handed the deferred writer stands a run's
        /// glyphs: a face program's own descent (the writer lifts such a run by it), nothing for
        /// a face without one. A line captured for a list marker must be the glyphs' baseline,
        /// not the seat (probed: an item's marker stood one descent low under an embedded face
        /// while the item's text stood right).</summary>
        private static double DeferredWriterLift(Text.TextState state, double fontSize)
        {
            var ttf = (state.FontData ?? state.Font?.SourceFontData)?.TtfData;
            if (ttf is null) return 0;
            var (_, descent, _, _) = Text.FontRepository.ReadTtfMetrics(ttf);
            return descent == 0 ? 0 : -descent * fontSize / 1000.0;
        }

        /// <summary>The seat a deferred chunk's opening line is handed: the line box's BOTTOM
        /// for a run written through a face program, since the deferred writer lifts it by
        /// that face's own descent. A declared line box seats the BASELINE itself, so the
        /// face's descent comes off it -- else the line rides that descent high.</summary>
        private double DeferredFirstSeat(Text.TextState state, double fontSize, double lineHeight)
        {
            var seat = FirstBaselineSeat(state, fontSize, lineHeight);
            return DeclaresLineBox(state) && HasFaceProgram(state) ? seat - RunDescentEm(state) * fontSize : seat;
        }

        /// <summary>An embedded-face (or deferred) chunk: queued for rendering against the page
        /// the slot will become, since that page does not exist until the flow drains.</summary>
        private void QueueDeferredChunk(FlowTextFragmentState wtf, Text.TextFragment tf, List<string> chunk)
        {
            // Queue the per-page chunk for deferred rendering. TextBuilder
            // splits on \n internally and applies the leading set by
            // SetLeading(lineHeight), so joining chunk lines with \n gets
            // us multi-line rendering on the target page.
            // The first line at the top of a region drops by the font size
            // (the standard first-line placement); every following body line
            // sits one of its own line heights below the previous baseline,
            // so a size change between adjacent paragraphs is spaced by the
            // lower line's metrics. Same-size runs are unaffected.
            // Caller-set LineSpacing: leading above the first line too
            // (see the fullJustify branch above).
            var firstBaseline = _lastBodyBaseline.HasValue
                ? _lastBodyBaseline.Value - wtf.lineHeight
                : FullSizeFirstBaseline(wtf.fullSize ? wtf.fontTtf : null, wtf.lineHeight)
                  ?? DeferredFirstSeat(tf.TextState, wtf.fontSize, wtf.lineHeight);
            wtf.fragFirstBaseline ??= firstBaseline;
            CaptureFirstLine(firstBaseline + DeferredWriterLift(tf.TextState, wtf.fontSize));
            // What follows an embedded-face paragraph defers too, so the page's content stream
            // keeps paragraph order: a standard-face paragraph written at once would land before it.
            _forceDeferredWrites = true;
            _lastBodyBaseline = firstBaseline - ChunkDrop(wtf, chunk.Count - 1);
            // A spacing-justified chunk stretches each line by its own word and character
            // spacing, the immediate writer's numbers, so a justified paragraph deferred behind
            // an embedded face (or set in one) still ends on the right edge.
            var spacings = wtf.spacingJustify ? JustifyingSpacings(wtf, tf, chunk) : null;
            if (wtf.alignsLines || wtf.variableLineHeights || spacings is not null || ShearOf(tf.TextState) != (0, 0))
            {
                // Each aligned line is its own deferred render at its own x —
                // and so is each line of a variable-height block, whose lines no
                // longer share one pitch, each line stretched by its own spacing, and
                // each sheared line (a relative move in sheared text space would lean
                // the next line's origin; each line gets its own matrix).
                for (var j = 0; j < chunk.Count; j++)
                {
                    // A line the requested face cannot cover is drawn in the
                    // covering face — otherwise its missing glyphs come out as
                    // look-alikes or blanks.
                    Text.TextState lineState = tf.TextState;
                    if (LineFallback(wtf, wtf.idx + j) is { } fbFont)
                    {
                        lineState = new Text.TextState();
                        lineState.ApplyChangesFrom(tf.TextState);
                        lineState.FontData = fbFont.SourceFontData;
                        lineState.Font = fbFont;
                    }
                    if (spacings is not null) _pendingRenderSpacing[_pendingEmbeddedRenders.Count] = spacings[j];
                    _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + LineAlignOffset(wtf, tf, chunk[j]),
                        _curY - ChunkDrop(wtf, j), chunk[j], lineState, wtf.fontSize,
                        firstBaseline - ChunkDrop(wtf, j)));
                    _pendingRenderPitch[_pendingEmbeddedRenders.Count - 1] = HeightOfLine(wtf, wtf.idx + j);
                    // Every line of the chunk clips to the chunk's whole band (one
                    // rectangle per paragraph in the reference).
                    if (ParagraphClip(wtf, _curY - ChunkDrop(wtf, chunk.Count), ChunkDrop(wtf, chunk.Count),
                            wtf.shapingLines.Contains(wtf.idx + j) ? lineState : null) is { } lineClip)
                        _pendingRenderClip[_pendingEmbeddedRenders.Count - 1] =
                            GrownByHangingSpace(lineClip, tf.TextState.FormattingOptions, wtf.fontSize);
                }
            }
            else
            {
                _pendingEmbeddedRenders.Add((_currentSlot, CurLeft, _curY,
                    string.Join("\n", chunk), tf.TextState, wtf.fontSize, firstBaseline));
                // The chunk's lines advance by the pitch the paginator reserved.
                _pendingRenderPitch[_pendingEmbeddedRenders.Count - 1] = wtf.lineHeight;
                if (ParagraphClip(wtf, _curY - ChunkDrop(wtf, chunk.Count), ChunkDrop(wtf, chunk.Count), null) is { } chunkClip)
                    _pendingRenderClip[_pendingEmbeddedRenders.Count - 1] =
                        GrownByHangingSpace(chunkClip, tf.TextState.FormattingOptions, wtf.fontSize);
            }
            // Mark the overflow buffer non-empty so StartNewPage / Commit
            // flushes it -- otherwise an overflow-only embedded-render
            // slot would never produce a Page, the deferred render would
            // have no target, and the test would see Pages.Count
            // unchanged from the start-page count. The placeholder is an
            // empty byte array (concatenates to nothing in the final
            // content stream).
            if (_overflowBuffer is not null)
                _overflowBuffer.Add(Array.Empty<byte>());
        }

        /// <summary>A Standard-14 chunk, written straight into this page's content stream.</summary>
        private void WritePlainChunk(FlowTextFragmentState wtf, Text.TextFragment tf, List<string> chunk)
        {
            // Register the fragment's MAPPED base font (Times/Courier/… — not
            // unconditionally Helvetica) so a FontName the caller or the HTML
            // UA-default flow set actually draws in that face. An overflow page takes
            // the start page's font names before it registers any of its own, so the
            // name reaches the face there too (it used to be F1: Helvetica, whatever
            // the face).
            var fontResName = Table.RegisterFont(_startPage, wtf.baseFont);
            var options = tf.TextState.FormattingOptions;
            var alphaGsName = options?.Opacity is { } opacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, opacity, strokeToo: true)
                : options?.FillOpacity is { } fillOpacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, fillOpacity, strokeToo: false)
                : tf.TextState.ForegroundColor is { } fg
                ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, fg.AByte)
                : null;
            // TextState.BackgroundColor draws a filled highlight behind each
            // wrapped line (its own /ca alpha, independent of the foreground's),
            // emitted before the glyphs so the text sits on top.
            // (a bordered block's background is painted over its whole box, bands
            // included, by the bordered writer)
            var bgColor = _blockBackgroundTaken ? null : tf.TextState.BackgroundColor;
            var bgAlphaGsName = bgColor is null ? null
                : options?.BackgroundOpacity is { } bgOpacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, bgOpacity, strokeToo: false)
                : Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, bgColor.AByte);
            var plainSeat = PlainFirstBaseline(tf.TextState, wtf.baseFont, wtf.fontSize, wtf.lineHeight);
            CaptureFirstLine(plainSeat);
            double[]? lineOffsets = null;
            if (wtf.alignsLines)
            {
                lineOffsets = new double[chunk.Count];
                for (var j = 0; j < chunk.Count; j++) lineOffsets[j] = LineAlignOffset(wtf, tf, chunk[j]);
            }
            var lineSpacings = wtf.spacingJustify ? JustifyingSpacings(wtf, tf, chunk) : null;
            var content = BuildWrappedTextStream(chunk, fontResName, wtf.fontSize,
                CurLeft, _curY, wtf.lineHeight, tf.TextState.ForegroundColor,
                tf.TextState.IsStrikeOut, tf.TextState.IsUnderline, wtf.baseFont, alphaGsName,
                wtf.idx == 0 ? wtf.firstLineIndent : 0, wtf.subsequentLinesIndent, wtf.idx == 0,
                bgColor, bgAlphaGsName, tf.TextState.Rotation, plainSeat, lineOffsets,
                tf.TextState.CharacterSpacing, tf.TextState.WordSpacing,
                BoxAtBreak(BlockBackgroundBox(tf.TextState, wtf.fontSize, wtf.lineHeight, CurWidth),
                    wtf.idx + chunk.Count < wtf.allLines.Count, options, wtf.fontSize),
                tf.TextState, RuleAlphaStates(options), lineSpacings);
            if (ParagraphClip(wtf, _curY - chunk.Count * wtf.lineHeight, chunk.Count * wtf.lineHeight, null) is { } plainClip)
                content = WrapInClip(content, GrownByHangingSpace(GrownByRise(GrownBySlope(
                    GrownByOutset(plainClip, OutwardPart(options?.BlockBackgroundOutset)),
                    ShearOf(tf.TextState).B, CurWidth), tf.TextState.TextRise), options, wtf.fontSize));
            WriteContent(content, tf.TextState);
            // The chunk's last baseline, so a note marker can attach to
            // the end of its last line.
            wtf.nonEmbeddedLastBaseline = plainSeat - (chunk.Count - 1) * wtf.lineHeight;
            // The non-embedded path positions baselines independently;
            // don't let a following embedded paragraph chain onto a
            // stale baseline from before it.
            _lastBodyBaseline = null;
        }

        /// <summary>Record where each line in this chunk finished. The line "slot" baseline
        /// reported is one line-height below the band top per line (curY is the band top for
        /// this chunk); the X is the left margin plus the line's width including its trailing
        /// space.</summary>
        private void TraceChunkLines(FlowTextFragmentState wtf, int chunkSize)
        {
            if (wtf.lineTrace is null) return;
            for (var j = 0; j < chunkSize && wtf.idx + j < wtf.lineTrace.Count; j++)
            {
                var t = wtf.lineTrace[wtf.idx + j];
                LogLine(_currentSlot, t.content, CurLeft + t.width,
                    _curY - wtf.lineHeight * (j + 1), t.reason);
            }
        }

        /// <summary>The paragraph's remainder wraps to the width of the region it continues in
        /// (a narrower second column re-breaks its lines).</summary>
        private void RewrapRemainderForRegion(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            if (wtf.noWrap || Math.Abs(CurWidth - wtf.contentWidth) <= 0.01
                || wtf.rawText.IndexOf((char)10) >= 0
                || RemainderFrom(wtf.rawText, wtf.allLines, wtf.idx) is not { } rest) return;
            var reLines = Text.TextPaginator.WrapToWidth(rest, wtf.baseFont, wtf.fontSize,
                (CurWidth - OccupiedExtra(tf.TextState.FormattingOptions, wtf.fontSize)) / ScaleOf(tf.TextState),
                tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, 0, wtf.charSpacing,
                tf.TextState.FormattingOptions?.HangingBreakSpace ?? false, tf.TextState.WordSpacing);
            wtf.allLines = wtf.allLines.GetRange(0, wtf.idx);
            wtf.allLines.AddRange(reLines);
            wtf.contentWidth = CurWidth;
        }
    }
}
