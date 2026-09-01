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
        /// <summary>Baseline raise of a footnote marker over its parent line's
        /// baseline, in the row model where the marker's row bottom sits
        /// (rowPitch - markerSize) above the parent row bottom and each baseline
        /// sits descent(size) above its row bottom.</summary>
        private static double MarkerBaselineRise(string fontName, double parentSize,
            double rowPitch, double markerSize)
        {
            var d = DescentNorm(fontName);
            return (rowPitch - markerSize) + d * markerSize - d * parentSize;
        }

        /// <summary>The first baseline for a fragment opening at the cursor: a CSS
        /// line box seats it half the surplus leading plus the ascent below the box
        /// top; otherwise the legacy drop of one line height (caller leading) or one
        /// font size applies.</summary>
        private double FirstBaselineSeat(Text.TextState state, double fontSize,
            double lineHeight)
        {
            if (state.CssLineBoxSeat)
            {
                var (above, below) = LinkBoxExtent(state, fontSize);
                // Half the box's surplus leading, then the ascent, reaches the
                // baseline; the seat this method returns is the text rect's bottom,
                // the face's descent below it.
                return _curY - ((lineHeight - (above + below)) / 2 + above) - below;
            }
            if (state.LineBoxSeat)
            {
                var (above, below) = LinkBoxExtent(state, fontSize);
                return _curY - ((lineHeight - (above + below)) / 2 + above);
            }
            // The opening line hangs a whole LINE below the band top, not a whole font
            // size: the two are the same only while the line advances by the font size.
            // A caller leading and full-size spacing both make the line taller, and the
            // first baseline sits that much further down — otherwise the opening line
            // rides up into the margin while every line after it sits correctly.
            var fullSize = state.FormattingOptions is
                { LineSpacing: Text.TextFormattingOptions.LineSpacingMode.FullSize };
            return _curY - (fullSize || (state.LineSpacing > 0 && !state.LineSpacingSynthetic)
                ? lineHeight : fontSize);
        }

        public bool WriteTextFragment(Text.TextFragment tf)
        {
            // BindXml-built fragments carry the classic XML-generator line model.
            if (tf.XmlGeneratorModel) return WriteXmlModelFragment(tf);
            // Caller-specified Position overrides flow layout. Use HasExplicitPosition,
            // not "Position != null": the getter now auto-materialises a (0,0) Position,
            // so a fragment the caller never positioned must still flow here.
            if (tf.HasExplicitPosition) return false;
            // A fragment with tab stops lays its own line out — the marker runs
            // aligned to their stops with a leader drawn between them. Seat it on
            // this flow's next baseline and let the writer that knows how emit it.
            if (tf.TabStops is { Count: > 0 } && tf.Text.Contains("#$TAB", StringComparison.Ordinal))
            {
                // A tab-stopped line the caller never sized renders at the tabbed
                // default, not the fragment ctor's placeholder: the column pitch and
                // the run widths of a tabbed table are both calibrated to it.
                const double tabbedDefaultFs = 8;
                if (!tf.TextState.FontSizeTouched) tf.TextState.SetFontSizeQuiet(tabbedDefaultFs);
                var tabFs = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : tabbedDefaultFs;
                var tabLine = tabFs * 1.2;
                if (_curY - tabLine < _marginBottom) StartNewPage(flushEmpty: true);
                AdvanceY(tabLine);
                tf.Position = new Text.Position(_marginLeft, _curY);
                new Text.TextBuilder(_startPage).AppendTextInline(tf);
                return true;
            }
            // Promote a segment-level font/size up to the fragment when the
            // fragment itself didn't set one. Generator-style tests build the
            // fragment with `new TextFragment()` then attach a TextSegment that
            // carries TextState.Font = FontRepository.FindFont("Arial") and a
            // FontSize -- the fragment-level TextState stays at the default
            // Helvetica/12 placeholder (TextFragmentState seeds Font with
            // FontInfo.DefaultHelvetica, which has no SourceFontData), hiding
            // the embedded font from both the paginator and TextBuilder. Treat
            // the fragment's Font as "not set" when it carries no SourceFontData,
            // so any segment that brings one wins.
            if (tf.Segments is { Count: > 0 } promoteSegs)
            {
                foreach (var s in promoteSegs)
                {
                    var fragHasEmbedded = tf.TextState.Font?.SourceFontData is not null
                                          || tf.TextState.FontData is not null;
                    if (!fragHasEmbedded && s.TextState.Font?.SourceFontData is not null)
                        tf.TextState.Font = s.TextState.Font;
                    if (tf.TextState.FontData is null && s.TextState.FontData is not null)
                        tf.TextState.FontData = s.TextState.FontData;
                    // FontSize defaults to a 10 pt placeholder, so promotion must key
                    // off FontSizeTouched, not <= 0 (same rule as QueueFootnote) — a
                    // segment-level 13 pt must not lose to the untouched fragment 10.
                    // Only the empty-ctor + single-styled-segment shape promotes: a
                    // fragment with its own ctor text plus a differently-sized added
                    // segment ("Aspose" + 5 pt "TM") keeps the fragment default for
                    // its untouched segments.
                    if (!tf.TextState.FontSizeTouched && s.TextState.FontSizeTouched
                        && FragmentSegmentsShareOneTouchedSize(tf))
                        tf.TextState.FontSize = s.TextState.FontSize;
                    // Line spacing lives on the segment in generator-style fragments
                    // (`seg.TextState.LineSpacing = ...`); promote it so the paginator
                    // sees the caller's leading rather than the fragment default.
                    if (tf.TextState.LineSpacing <= 0 && s.TextState.LineSpacing > 0)
                        tf.TextState.LineSpacing = s.TextState.LineSpacing;
                    if ((tf.TextState.Font?.SourceFontData ?? tf.TextState.FontData) is not null
                        && tf.TextState.FontSize > 0 && tf.TextState.LineSpacing > 0) break;
                }
            }
            // Embedded/CID fonts (FontData set directly, or via FontRepository.FindFont
            // populating TextState.Font.SourceFontData) need TextBuilder for correct
            // glyph encoding -- but TextBuilder is page-bound, and overflow pages
            // don't exist until after the outer Document.Save loop drains them. The
            // paginator lays the fragment out in Standard-14 metric space (close
            // enough for line-break decisions) and queues each per-page chunk into
            // _pendingEmbeddedRenders; FinaliseEmbeddedRenders runs after the drain
            // and uses a fresh TextBuilder against each target Page.
            // A face that cannot show part of the paragraph is traded for the face
            // that covers more of it ONCE, for the whole paragraph: every page's
            // chunk then wraps and draws in the same face (a Standard-14 paragraph
            // with Arabic letters moves wholly to the host serif).
            if (tf.TextState.FontData is null && HasNonLatin1(tf.Text))
            {
                var wholeText = tf.Text ?? string.Empty;
                var curFd = tf.TextState.Font?.SourceFontData;
                if (curFd?.TtfData is null || !Text.FontRepository.CoversText(curFd.TtfData, wholeText))
                {
                    var sub = Text.FontRepository.SubstituteForMissingGlyphs(wholeText, tf.TextState.Font);
                    if (sub?.TtfData is not null
                        && (curFd?.TtfData is null
                            || Text.FontRepository.CoverCount(sub.TtfData, wholeText)
                               > Text.FontRepository.CoverCount(curFd.TtfData, wholeText)))
                        tf.TextState.FontData = sub;
                }
            }
            var useEmbeddedFont = HasFaceProgram(tf.TextState);
            // Invisible / clipping text rendering modes need the legacy writer, which
            // emits `Tr` operators; the paginator does not.
            if (tf.TextState.RenderingMode != 0) return false;
            // Per-segment explicit Position means the caller wants precise control;
            // otherwise tf.Text (concatenated from all segments via RefreshTextFromSegments)
            // is the paragraph's logical content and flow-wraps correctly even when the
            // fragment was constructed via `new TextFragment()` + `.Segments.Add(seg)`
            // (which produces Segments.Count == 2: a default empty segment + caller's).
            if (tf.Segments is { Count: > 1 })
            {
                foreach (var s in tf.Segments)
                    if (s.Position is not null) return false;
                // Segments carrying DIFFERING font/size/style (a bold 50pt word inside
                // a 30pt sentence): render them inline AT THE CURSOR as one chained
                // line when they fit — falling back to the legacy fixed-position
                // writer stamped every such fragment at the page top-left, so a
                // page full of "label: value" fragments collapsed into one
                // overlapping line at the top. Only genuinely complex shapes
                // (multi-line, wrapping, links, decorations) keep the fallback.
                if (Text.TextBuilder.SegmentStylesDiffer(tf, tf.TextState.FontSize))
                    return TryWriteStyledSegmentsLine(tf);
            }

            var baseFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            var fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            // In column mode this is the current column's width; otherwise the
            // full page content width. Wrapping uses the entry column's width;
            // the test columns are equal-width so a fragment that flows into the
            // next column keeps the same break points.
            var contentWidth = CurWidth;
            if (contentWidth <= 0) return false;

            // WordWrapMode.NoWrap → each \n-delimited input line becomes one
            // output line, regardless of width: a
            // long line stays on one rendered line that overflows the page
            // horizontally; only vertical pagination still applies. Default
            // (ByWords / Undefined / null) flows through the width-aware wrap.
            var noWrap = tf.TextState.FormattingOptions?.WrapMode
                         == Text.TextFormattingOptions.WordWrapMode.NoWrap;
            // First-line indent (paragraph indentation set via FormattingOptions):
            // the first wrapped line starts indented and is correspondingly narrower.
            var firstLineIndent = (double)(tf.TextState.FormattingOptions?.FirstLineIndent ?? 0f);
            // Subsequent-lines indent: every wrapped line after the paragraph's first
            // starts indented by this amount. Applies across page
            // breaks too — a chunk that does not start the paragraph indents all its
            // lines.
            var subsequentLinesIndent = (double)(tf.TextState.FormattingOptions?.SubsequentLinesIndent ?? 0f);
            var rawText = tf.Text ?? string.Empty;
            var charSpacing = tf.TextState.CharacterSpacing;
            var allLines = noWrap
                ? new List<string>(rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                : Text.TextPaginator.WrapToWidth(rawText, baseFont, fontSize, contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, firstLineIndent, charSpacing);
            // WrapLinesCount caps the wrapped paragraph at its first N lines; the
            // rest of the text is dropped (a one-line cell shows only its first line).
            if (tf.WrapLinesCount > 0 && allLines.Count > tf.WrapLinesCount)
                allLines.RemoveRange(tf.WrapLinesCount, allLines.Count - tf.WrapLinesCount);
            // When notification logging is on, trace each wrapped line's width and
            // break reason (aligned 1:1 with allLines) so the loop below can record
            // where every line finished.
            var lineTrace = _logNotifications && !noWrap
                ? Text.TextPaginator.TraceLines(rawText, baseFont, fontSize, contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, firstLineIndent)
                : null;
            // lineHeight resolution order:
            //   1. fragment.TextState.LineSpacing -- explicit float override
            //      in points, set by callers like
            //      `seg.TextState.LineSpacing = fontSize + 3f`.
            //   2. FormattingOptions.LineSpacing == FullSize -- use the font's
            //      full vertical extent from the TTF (ascent - descent).
            //   3. fontSize * 1.2 -- default for FontSize / Undefined modes.
            var fontTtf = tf.TextState.FontData?.TtfData
                          ?? tf.TextState.Font?.SourceFontData?.TtfData;
            var fullSize = tf.TextState.FormattingOptions?.LineSpacing
                           == Text.TextFormattingOptions.LineSpacingMode.FullSize;
            double lineHeight;
            if (tf.TextState.LineSpacing > 0)
                // An explicit LineSpacing is extra leading added on
                // top of the glyph height: the line pitch is fontSize + LineSpacing
                // (a 10pt font with LineSpacing 13
                // lays out on a 23pt pitch, not 13). LineSpacing == 0 degenerates to the
                // default fontSize pitch below, so the rule is uniform.
                lineHeight = fontSize + tf.TextState.LineSpacing;
            else if (fullSize && fontTtf is { Length: > 12 })
                lineHeight = ComputeFullSizeLineHeight(fontTtf, fontSize);
            else
                // Default LineSpacingMode is FontSize: the line
                // advance equals the font size, not an inflated 1.2x leading.
                lineHeight = fontSize;
            // FullSize takes a line's height from the FONT — so a line with no glyphs
            // has no font to measure and falls back to the DEFAULT face's own full
            // extent (Helvetica, 0.925 em), which is shorter than a text line in a
            // tall face. Measured on a multi-script text dump: the
            // blank-line gaps are exactly lineHeight + 0.925 em per empty line.
            // ⚠ and that default is only in effect on the START page: once the flow has
            // spilled, an empty line measures as one em (its bare font
            // size) instead — probed on a two-page blank-line ladder, where the same
            // double blank is 2 x 9.245 on page 1 and 2 x 10.0 on the overflow page.
            bool variableLineHeights = fullSize && fontTtf is { Length: > 12 };
            double EmptyHeight() => _overflowBuffer is null
                ? Text.Standard14Fonts.FullSizeEmptyLineEm * fontSize : fontSize;
            // A line the requested face cannot COVER is not drawn in it at all: the
            // reference hands that line to a face that has the glyphs, and the line then
            // takes THAT face's full extent. (Arial Unicode MS has no Romanian
            // comma-below letters, so its lines drop to Times New Roman's 1.1074 em.)
            var fallbackFace = new Dictionary<int, Text.Font?>();
            // The covering-face hand-off applies to EVERY embedded-face fragment, not
            // only FullSize ones - a default-spacing Arial Unicode MS line with
            // Romanian comma-below letters is still handed to Times New Roman by the
            // fallback (the line is drawn, not blanked). Only the LINE-HEIGHT
            // treatment stays FullSize-gated (HeightOfLine below).
            bool lineFallbackActive = fontTtf is { Length: > 12 };
            Text.Font? LineFallback(int i)
            {
                if (!lineFallbackActive || i < 0 || i >= allLines.Count) return null;
                if (fallbackFace.TryGetValue(i, out var cached)) return cached;
                var f = Text.FontRepository.ResolveCoveringFont(fontTtf!, allLines[i]);
                fallbackFace[i] = f;
                return f;
            }
            double HeightOfLine(int i)
            {
                if (!variableLineHeights || i < 0 || i >= allLines.Count) return lineHeight;
                // (fullSize-only from here: fallback faces change the LINE EXTENT.)
                if (string.IsNullOrWhiteSpace(allLines[i])) return EmptyHeight();
                if (LineFallback(i)?.SourceFontData?.TtfData is { Length: > 12 } fb)
                    return Math.Max(Text.Standard14Fonts.FullSizeEmptyLineEm * fontSize,
                                    ComputeFullSizeLineHeight(fb, fontSize));
                return lineHeight;
            }
            // The content rectangle an unwrapped line is clipped to. Full page height:
            // only the horizontal overrun is cut, vertical pagination is unchanged.
            Rectangle? NoWrapClip() =>
                CurWidth > 0 ? new Rectangle(CurLeft, 0, CurLeft + CurWidth, _startPageHeight) : null;
            EnsureRoom(OrphanRoom(lineHeight, allLines.Count));
            _lastTextLinePitch = lineHeight;

            // A fragment-level hyperlink applies to the fragment's first line.
            // Capture the slot + top-of-line before the write loop advances _curY.
            var fragHyperlink = tf.HyperlinkValue;
            var fragSlot = _currentSlot;
            var fragTop = _curY;
            // The baseline the first line actually landed on - a segment link is
            // boxed on the baselines, not on the line box (see LinkBoxExtent).
            double? fragFirstBaseline = null;

            // Per-segment hyperlinks: each TextSegment with a Hyperlink emits a
            // LinkAnnotation sized to the segment's run. Char offsets are into the
            // fragment's full text; the emission below maps them onto each wrapped
            // line (a hyperlink that wraps gets one rect per line it covers).
            var segHyperlinks = (List<(int charStart, int charEnd, Hyperlink hyperlink)>?)null;
            if (tf.Segments is { Count: > 0 } segs)
            {
                List<(int, int, Hyperlink)>? collected = null;
                var cursor = 0;
                foreach (var seg in segs)
                {
                    var len = seg.Text?.Length ?? 0;
                    if (len > 0 && seg.Hyperlink is { } h)
                        (collected ??= new()).Add((cursor, cursor + len, h));
                    cursor += len;
                }
                segHyperlinks = collected;
            }

            // FullJustify: each wrapped line — including the paragraph's last —
            // is stretched so its final word ends exactly at the region's right
            // edge. Every word AND every interior space goes out as
            // its own absolutely-positioned show (the line-break trailing space
            // is dropped), distributing the slack equally across the interior
            // spaces as gaps between a space glyph and the next word; space
            // glyphs keep their natural width. That show structure
            // matters beyond geometry: the absorber yields one fragment per
            // show, and justified-output tests index into that fragment list.
            var fullJustify = !noWrap
                && (tf.HorizontalAlignment == HorizontalAlignment.FullJustify
                    || tf.TextState.HorizontalAlignment == HorizontalAlignment.FullJustify);
            Func<string, double>? justifyMeasurer = null;

            // Center / Right alignment: every wrapped line is offset by its own
            // slack against the write region (half of it for Center), measured
            // on the line without its break space -- a right-aligned line ends
            // on the region's right edge and its trailing space hangs past it
            // (reference column output, probed 2026-08-23).
            var alignMode = tf.HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right
                ? tf.HorizontalAlignment
                : tf.TextState.HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right
                    ? tf.TextState.HorizontalAlignment : HorizontalAlignment.Left;
            Func<string, double>? alignMeasurer = null;
            double LineAlignOffset(string line)
            {
                if (noWrap || alignMode == HorizontalAlignment.Left) return 0;
                alignMeasurer ??= Text.TextPaginator.CreateMeasurer(baseFont, fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var slack = CurWidth - alignMeasurer(line.TrimEnd(' '));
                if (slack <= 0) return 0;
                return alignMode == HorizontalAlignment.Center ? slack / 2 : slack;
            }
            var alignsLines = !noWrap && alignMode != HorizontalAlignment.Left;

            var idx = 0;
            double? nonEmbeddedLastBaseline = null;
            while (idx < allLines.Count)
            {
                int chunkSize;
                if (variableLineHeights)
                {
                    // Fill the band line by line — the lines are not all the same height.
                    var room = _curY - EffectiveBottom;
                    chunkSize = 0;
                    double used = 0;
                    while (idx + chunkSize < allLines.Count
                           && used + HeightOfLine(idx + chunkSize) <= room)
                    {
                        used += HeightOfLine(idx + chunkSize);
                        chunkSize++;
                    }
                    if (chunkSize == 0) chunkSize = 1;
                }
                else
                {
                    var availableLines = Math.Max(1, (int)((_curY - EffectiveBottom) / lineHeight));
                    chunkSize = Math.Min(availableLines, allLines.Count - idx);
                }
                chunkSize = Math.Min(chunkSize, allLines.Count - idx);
                var chunk = allLines.GetRange(idx, chunkSize);
                // Cumulative drop from the chunk's first baseline to line j.
                double BaselineDrop(int j)
                {
                    double d = 0;
                    for (var k = 0; k < j; k++) d += HeightOfLine(idx + k);
                    return d;
                }
                double ChunkHeight()
                {
                    double d = 0;
                    for (var k = 0; k < chunkSize; k++) d += HeightOfLine(idx + k);
                    return d;
                }

                if (fullJustify)
                {
                    justifyMeasurer ??= Text.TextPaginator.CreateMeasurer(baseFont, fontSize,
                        tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                    // Baselines follow the deferred-render chain: first line at the
                    // top of a region drops by the font size, later lines sit one
                    // line height below the previous baseline (same rule as the
                    // embedded branch below).
                    // A CALLER-set LineSpacing adds its leading above the first
                    // line too (23 pt drop for 10 pt + 13);
                    // synthetic (layout-assigned) leading keeps the plain drop.
                    var firstLineBaseline = _lastBodyBaseline.HasValue
                        ? _lastBodyBaseline.Value - lineHeight
                        : _curY - (tf.TextState.LineSpacing > 0 && !tf.TextState.LineSpacingSynthetic
                            ? lineHeight : fontSize);
                    for (var j = 0; j < chunkSize; j++)
                    {
                        var lineBaseline = firstLineBaseline - j * lineHeight;
                        foreach (var (token, xOffset) in JustifyLineTokens(chunk[j], justifyMeasurer, CurWidth))
                            _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + xOffset, _curY,
                                token, tf.TextState, fontSize, lineBaseline));
                    }
                    _lastBodyBaseline = firstLineBaseline - (chunkSize - 1) * lineHeight;
                    // All later content in this flow must defer to keep the page's
                    // content-stream order equal to paragraph order (see field doc).
                    _forceDeferredWrites = true;
                    if (_overflowBuffer is not null)
                        _overflowBuffer.Add(Array.Empty<byte>());
                }
                else if (useEmbeddedFont || _forceDeferredWrites)
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
                        ? _lastBodyBaseline.Value - lineHeight
                        : FullSizeFirstBaseline(fullSize ? fontTtf : null, lineHeight)
                          ?? FirstBaselineSeat(tf.TextState, fontSize, lineHeight);
                    fragFirstBaseline ??= firstBaseline;
                    _lastBodyBaseline = firstBaseline - BaselineDrop(chunkSize - 1);
                    if (alignsLines || variableLineHeights)
                    {
                        // Each aligned line is its own deferred render at its own x —
                        // and so is each line of a variable-height block, whose lines no
                        // longer share one pitch.
                        for (var j = 0; j < chunk.Count; j++)
                        {
                            // A line the requested face cannot cover is drawn in the
                            // covering face — otherwise its missing glyphs come out as
                            // look-alikes or blanks.
                            Text.TextState lineState = tf.TextState;
                            if (LineFallback(idx + j) is { } fbFont)
                            {
                                lineState = new Text.TextState();
                                lineState.ApplyChangesFrom(tf.TextState);
                                lineState.FontData = fbFont.SourceFontData;
                                lineState.Font = fbFont;
                            }
                            _pendingEmbeddedRenders.Add((_currentSlot, CurLeft + LineAlignOffset(chunk[j]),
                                _curY - BaselineDrop(j), chunk[j], lineState, fontSize,
                                firstBaseline - BaselineDrop(j)));
                            _pendingRenderPitch[_pendingEmbeddedRenders.Count - 1] = HeightOfLine(idx + j);
                            // LAW: an unwrapped line does not run off the page —
                            // it clips at the content rectangle's right
                            // edge (its ink stops exactly on the right margin).
                            if (noWrap && NoWrapClip() is { } nwClip)
                                _pendingRenderClip[_pendingEmbeddedRenders.Count - 1] = nwClip;
                        }
                    }
                    else
                    {
                        _pendingEmbeddedRenders.Add((_currentSlot, CurLeft, _curY,
                            string.Join("\n", chunk), tf.TextState, fontSize, firstBaseline));
                        // The chunk's lines advance by the pitch the paginator reserved.
                        _pendingRenderPitch[_pendingEmbeddedRenders.Count - 1] = lineHeight;
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
                else
                {
                    // Register the fragment's MAPPED base font (Times/Courier/… — not
                    // unconditionally Helvetica) so a FontName the caller or the HTML
                    // UA-default flow set actually draws in that face. Overflow pages
                    // register their own F1 at commit and stay Helvetica.
                    var fontResName = _overflowBuffer is null
                        ? Table.RegisterFont(_startPage, baseFont)
                        : "F1";
                    var alphaGsName = tf.TextState.ForegroundColor is { } fg
                        ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, fg.AByte)
                        : null;
                    // TextState.BackgroundColor draws a filled highlight behind each
                    // wrapped line (its own /ca alpha, independent of the foreground's),
                    // emitted before the glyphs so the text sits on top.
                    var bgColor = tf.TextState.BackgroundColor;
                    var bgAlphaGsName = bgColor is { } bgc
                        ? Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, bgc.AByte)
                        : null;
                    var plainSeat = PlainFirstBaseline(tf.TextState, baseFont, fontSize, lineHeight);
                    double[]? lineOffsets = null;
                    if (alignsLines)
                    {
                        lineOffsets = new double[chunk.Count];
                        for (var j = 0; j < chunk.Count; j++) lineOffsets[j] = LineAlignOffset(chunk[j]);
                    }
                    var content = BuildWrappedTextStream(chunk, fontResName, fontSize,
                        CurLeft, _curY, lineHeight, tf.TextState.ForegroundColor,
                        tf.TextState.IsStrikeOut, tf.TextState.IsUnderline, baseFont, alphaGsName,
                        idx == 0 ? firstLineIndent : 0, subsequentLinesIndent, idx == 0,
                        bgColor, bgAlphaGsName, tf.TextState.Rotation, plainSeat, lineOffsets);
                    WriteContent(content, tf.TextState);
                    // The chunk's last baseline, so a note marker can attach to
                    // the end of its last line.
                    nonEmbeddedLastBaseline = plainSeat - (chunkSize - 1) * lineHeight;
                    // The non-embedded path positions baselines independently;
                    // don't let a following embedded paragraph chain onto a
                    // stale baseline from before it.
                    _lastBodyBaseline = null;
                }

                // Record where each line in this chunk finished. The line "slot"
                // baseline reported is one line-height below the
                // band top per line (curY is the band top for this chunk); the X is
                // the left margin plus the line's width including its trailing space.
                if (lineTrace is not null)
                {
                    for (var j = 0; j < chunkSize && idx + j < lineTrace.Count; j++)
                    {
                        var t = lineTrace[idx + j];
                        LogLine(_currentSlot, t.content, CurLeft + t.width,
                            _curY - lineHeight * (j + 1), t.reason);
                    }
                }

                _curY -= variableLineHeights ? ChunkHeight() : lineHeight * chunkSize;
                idx += chunkSize;
                if (idx < allLines.Count)
                {
                    FlowToNextRegion();
                    // The paragraph's remainder wraps to the width of the region it
                    // continues in (a narrower second column re-breaks its lines).
                    if (!noWrap && Math.Abs(CurWidth - contentWidth) > 0.01
                        && rawText.IndexOf((char)10) < 0
                        && RemainderFrom(rawText, allLines, idx) is { } rest)
                    {
                        var reLines = Text.TextPaginator.WrapToWidth(rest, baseFont, fontSize, CurWidth,
                            tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, 0, charSpacing);
                        allLines = allLines.GetRange(0, idx);
                        allLines.AddRange(reLines);
                        contentWidth = CurWidth;
                    }
                }
            }
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            // Record how far down the body reached on this slot. In column mode the
            // footnote sits below the deepest column, so use _colDeepestY (the bottom
            // of the fullest column), not _curY (which may be near the top of a later,
            // shorter column).
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);

            // A FootNote / EndNote on the fragment: emit its superscript reference
            // marker right after the last laid-out glyph and queue the note body
            // for the band on this slot (foot) or the flow's last page (end).
            foreach (var (note, isEndNote) in new[] { (tf.FootNote, false), (tf.EndNote, true) })
            {
                if (note is null) continue;
                var marker = NextFootnoteMarker(note);
                var lastLine = allLines.Count > 0 ? allLines[^1] : string.Empty;
                var markerMeasurer = Text.TextPaginator.CreateMeasurer(baseFont, fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var markerBaseline = _lastBodyBaseline ?? nonEmbeddedLastBaseline;
                if (markerBaseline.HasValue && marker.Length > 0)
                {
                    var markerSize = fontSize * MarkerSizeRatio;
                    var markerState = new Text.TextState
                    {
                        Font = tf.TextState.Font,
                        FontData = tf.TextState.FontData,
                        ForegroundColor = note.TextState?.ForegroundColor,
                    };
                    var markerX = CurLeft + markerMeasurer(lastLine);
                    _pendingEmbeddedRenders.Add((_currentSlot,
                        markerX, 0, marker, markerState, markerSize,
                        markerBaseline.Value
                        + MarkerBaselineRise(baseFont, fontSize, lineHeight, markerSize)));
                    // The line's text top is its box bottom plus the font size; the
                    // marker hangs from it.
                    var markerLineTop = _curY + fontSize;
                    var markerW = Text.TextPaginator.CreateMeasurer(baseFont, markerSize,
                        tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData)(marker);
                    _noteMarkLine[note] = (_currentSlot, markerLineTop);
                    QueueNoteLink(note, markerX, markerLineTop, markerW, markerSize);
                }
                if (isEndNote) QueueEndNote(note, marker, fontSize);
                else QueueMarkedFootnote(note, marker, fontSize);
            }

            if (fragHyperlink is not null && allLines.Count > 0)
            {
                // A paragraph-level Hyperlink's box hugs the TEXT it was set on, not the
                // content band: "some text" at Helvetica 10 sits under a
                // link 43.35 pt wide (its exact advance), where the band is 415 (probed
                // 2026-08-26 on a TextFragment and on an HtmlFragment alike).
                var linkMeasure = Text.TextPaginator.CreateMeasurer(baseFont, fontSize,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData);
                var linkW = 0.0;
                foreach (var line in allLines)
                {
                    var lw = linkMeasure(line);
                    if (lw > linkW) linkW = lw;
                }
                if (linkW <= 0 || linkW > contentWidth) linkW = contentWidth;
                _pendingLinks.Add((fragSlot,
                    new Rectangle(CurLeft, fragTop - lineHeight, CurLeft + linkW, fragTop),
                    fragHyperlink));
            }

            if (segHyperlinks is { Count: > 0 })
            {
                // Locate each wrapped line's character span within the fragment text so a
                // segment range [a,b) can be split across the lines it covers (wrapping
                // drops the break space, so lines are matched sequentially by content).
                var lineStart = new int[allLines.Count];
                var lineEnd = new int[allLines.Count];
                int scan = 0;
                for (int li = 0; li < allLines.Count; li++)
                {
                    var ln = allLines[li];
                    int at = ln.Length == 0 ? scan : rawText.IndexOf(ln, Math.Min(scan, rawText.Length), StringComparison.Ordinal);
                    if (at < 0) at = scan;
                    lineStart[li] = at;
                    lineEnd[li] = at + ln.Length;
                    scan = lineEnd[li];
                }
                foreach (var (a, b, h) in segHyperlinks)
                {
                    for (int li = 0; li < allLines.Count; li++)
                    {
                        var ln = allLines[li];
                        int ov0 = Math.Max(a, lineStart[li]);
                        int ov1 = Math.Min(b, lineEnd[li]);
                        if (ov1 <= ov0) continue;
                        var prefix = ln.Substring(0, ov0 - lineStart[li]);
                        var run = ln.Substring(ov0 - lineStart[li], ov1 - ov0);
                        var x0 = CurLeft + MeasureText(prefix, baseFont, fontSize);
                        var w = MeasureText(run, baseFont, fontSize);
                        var (sgAbove, sgBelow) = LinkBoxExtent(tf.TextState, fontSize);
                        var sgBase = (fragFirstBaseline ?? fragTop - fontSize) - lineHeight * li;
                        _pendingLinks.Add((fragSlot,
                            new Rectangle(x0, sgBase - sgBelow, x0 + w, sgBase + sgAbove), h));
                    }
                }
            }
            return true;
        }

        /// <summary>Compute the per-line vertical advance for
        /// <see cref="Text.TextFormattingOptions.LineSpacingMode.FullSize"/>.
        /// FullSize means the embedded font's full vertical extent (ascent
        /// minus descent, since descent is negative) scaled to the requested
        /// font size, so multi-script content with tall ascent glyphs (CJK
        /// fonts, Arial Unicode MS) advances by the right amount per line
        /// instead of the 1.2x-of-font-size default. Falls back to 1.2x if
        /// the TTF metrics can't be parsed.</summary>
        private static double ComputeFullSizeLineHeight(byte[] ttf, double fontSize)
        {
            try
            {
                // The full-size line pitch is the font's own vertical extent -- hhea
                // ascender plus descender, or the OS/2 win metrics -- not the
                // typographic ascent/descent used for the PDF font descriptor. For
                // fonts where the two differ (CJK faces whose typo metrics span only
                // 1 em but whose line box is taller) the descriptor values understate
                // the leading. The hhea LINE GAP is NOT part of it: probed against the
                // reference, Arial Unicode MS (gap 0) lays out at 1.3398 em and Times
                // New Roman (gap 87/2048) at 1.1074, its ascent+descent exactly.
                var lineEm = Text.FontRepository.ReadTtfFullExtentEm(ttf);
                if (lineEm > 0) return lineEm * fontSize;
                var (ascent, descent, _, _) = Text.FontRepository.ReadTtfMetrics(ttf);
                if (ascent <= 0) return fontSize * 1.2;
                // ascent is positive; descent is negative. Total vertical
                // extent in 1/1000 em -> scale to points.
                var height = (ascent - descent) / 1000.0 * fontSize;
                return height > 0 ? height : fontSize * 1.2;
            }
            catch
            {
                return fontSize * 1.2;
            }
        }

    }
}
