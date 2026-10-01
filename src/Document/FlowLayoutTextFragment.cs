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

        /// <summary>The block box a background fills, when the caller asked for a
        /// block background rather than a per-line highlight: the content width,
        /// and the line box's own split about the baseline so the box is exactly
        /// its line boxes tall. Null leaves the highlight alone.</summary>
        private static (double Width, double Above, double Below)? BlockBackgroundBox(
            Text.TextState state, double fontSize, double lineHeight, double width)
        {
            if (state.FormattingOptions is not { BlockBackground: true }) return null;
            var (above, below) = LinkBoxExtent(state, fontSize);
            var aboveBaseline = (lineHeight - (above + below)) / 2 + above;
            return (width, aboveBaseline, lineHeight - aboveBaseline);
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
            if (state.LineBoxSeat || DeclaresLineBox(state))
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

        /// <summary>The bullet's code in the Standard-14 metrics (WinAnsi).</summary>
        private const int WinAnsiBullet = 0x95;

        /// <summary>Draw a list marker with its right edge at <paramref name="rightX"/> on the
        /// baseline the item's first body line opens on; the cursor does not move, the item's
        /// own text follows on that line.</summary>
        public void WriteListMarker(string marker, Text.TextState itemState, double fontSize, double rightX)
        {
            var lineHeight = fontSize + itemState.LineSpacing;
            var baseline = _lastBodyBaseline.HasValue
                ? _lastBodyBaseline.Value - lineHeight
                : FirstBaselineSeat(itemState, fontSize, lineHeight);
            var fontName = itemState.Font?.FontName ?? "Helvetica";
            var w = 0.0;
            foreach (var c in marker)
            {
                // the bullet is U+2022 in the text, the WinAnsi bullet in the metrics
                var code = c == '\u2022' ? WinAnsiBullet : c < 256 ? c : '?';
                var cw = Text.Standard14Fonts.GetWidth(fontName, code);
                w += (cw < 0 ? 500 : cw) * fontSize / 1000.0;
            }
            var tf = new Text.TextFragment(marker) { Position = new Text.Position(rightX - w, baseline) };
            tf.TextState.FontSize = (float)fontSize;
            if (itemState.Font is not null) tf.TextState.Font = itemState.Font;
            new Text.TextBuilder(_startPage).AppendTextInline(tf);
        }

        public bool WriteTextFragment(Text.TextFragment tf)
        {
            // A block box -- margins, border, padding, width, height -- is laid around
            // the fragment's lines (see FlowBlockBox).
            if (tf.HasBlockBox && !tf.HasExplicitPosition) return WriteBlockBoxFragment(tf);
            return WriteTextFragmentCore(tf);
        }

        private bool WriteTextFragmentCore(Text.TextFragment tf)
        {
            // BindXml-built fragments carry the classic XML-generator line model.
            if (tf.XmlGeneratorModel) return WriteXmlModelFragment(tf);
            if (TryWriteSelfPlacedFragment(tf) is { } placed) return placed;
            PromoteSegmentStylesToFragment(tf);
            SubstituteFaceForUncoveredText(tf);
            // Invisible and clipping text rendering modes need the legacy writer;
            // the flow's own writer strokes (modes 1 and 2) itself.
            if ((int)tf.TextState.RenderingMode >= 3) return false;
            if (FragmentSegmentsRefuseFlow(tf) is { } refused) return refused;

            var wtf = new FlowTextFragmentState();
            wtf.useEmbeddedFont = HasFaceProgram(tf.TextState);
            wtf.unclippedLines = tf.TextState.FormattingOptions?.UnclippedLines ?? false;
            if (!InitFragmentWrap(wtf, tf)) return false;
            InitFragmentLineHeight(wtf, tf);
            InitFragmentLinksAndAlignment(wtf, tf);

            if (!WriteFragmentLines(wtf, tf)) return false;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            // Record how far down the body reached on this slot. In column mode the
            // footnote sits below the deepest column, so use _colDeepestY (the bottom
            // of the fullest column), not _curY (which may be near the top of a later,
            // shorter column).
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);

            WriteFragmentNotesAndLinks(wtf, tf);
            return true;
        }

        /// <summary>Writes a fragment that places itself — one the caller positioned, or one
        /// whose tab stops lay its own line out — and reports what it returned. Null means the
        /// fragment places nothing of its own and flows through the paginator below.</summary>
        private bool? TryWriteSelfPlacedFragment(Text.TextFragment tf)
        {
            // Caller-specified Position overrides flow layout. Use HasExplicitPosition,
            // not "Position != null": the getter now auto-materialises a (0,0) Position,
            // so a fragment the caller never positioned must still flow here.
            if (tf.HasExplicitPosition)
            {
                // The caller placed it; the flow only decides WHICH page. While the
                // flow is still on its start page the dispatcher writes it there, as
                // before. Once the flow has moved on, that page is a slot that does
                // not exist yet -- writing to the start page would strand the
                // fragment on page one while the text around it had moved. Queue it
                // for the slot instead, and do NOT advance the cursor: caller-placed
                // content takes no flow space.
                if (_overflowBuffer is null) return false;
                // Only when the caller said this belongs with the flow; a fragment
                // that named its own page keeps it.
                if (tf.TextState.FormattingOptions is not { SeatOnFlowPage: true }) return false;
                // Resources resolve against the start page, whose fonts are merged
                // into every overflow page this flow produces.
                var seated = new Text.TextBuilder(_startPage) { ContentSink = WriteContent };
                seated.AppendTextInline(tf);
                return true;
            }
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
            return null;
        }

        /// <summary>Lifts a font, size or leading a caller attached to a SEGMENT up onto the
        /// fragment when the fragment itself never set one.</summary>
        private static void PromoteSegmentStylesToFragment(Text.TextFragment tf)
        {
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
        }

        /// <summary>Trades a face that cannot show part of the paragraph for one that covers
        /// more of it — once, for the whole paragraph, so every page's chunk draws the same.</summary>
        private static void SubstituteFaceForUncoveredText(Text.TextFragment tf)
        {
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
        }

        /// <summary>Whether the fragment's segments refuse to flow as one wrapped paragraph, and
        /// with what answer: false when the legacy fixed-position writer must take it, the inline
        /// styled-line writer's own result when that writer took it. Null means the paragraph
        /// flows normally.</summary>
        private bool? FragmentSegmentsRefuseFlow(Text.TextFragment tf)
        {
            // Per-segment explicit Position means the caller wants precise control;
            // otherwise tf.Text (concatenated from all segments via RefreshTextFromSegments)
            // is the paragraph's logical content and flow-wraps correctly even when the
            // fragment was constructed via `new TextFragment()` + `.Segments.Add(seg)`
            // (which produces Segments.Count == 2: a default empty segment + caller's).
            if (tf.Segments is { Count: > 1 })
            {
                foreach (var s in tf.Segments)
                    if (s.Position is not null) return false;
                // A paragraph whose segments were asked to flow as runs wraps them
                // together, each drawn in its own style (see FlowSegmentedRuns).
                if (tf.TextState.FormattingOptions is { SegmentsFlowAsRuns: true }
                    && TryWriteSegmentedRuns(tf) is { } wroteRuns)
                    return wroteRuns;
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
            return null;
        }

        /// <summary>Resolves the paragraph's face, size and write width, and wraps its text to
        /// that width. False means the region has no width to write in.</summary>
        private bool InitFragmentWrap(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            wtf.baseFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            wtf.fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            wtf.contentWidth = CurWidth;
            if (wtf.contentWidth <= 0) return false;

            wtf.noWrap = tf.TextState.FormattingOptions?.WrapMode
                         == Text.TextFormattingOptions.WordWrapMode.NoWrap;
            wtf.firstLineIndent = (double)(tf.TextState.FormattingOptions?.FirstLineIndent ?? 0f);
            wtf.subsequentLinesIndent = (double)(tf.TextState.FormattingOptions?.SubsequentLinesIndent ?? 0f);
            wtf.rawText = tf.Text ?? string.Empty;
            wtf.charSpacing = tf.TextState.CharacterSpacing;
            wtf.allLines = wtf.noWrap
                ? new List<string>(wtf.rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
                : Text.TextPaginator.WrapToWidth(wtf.rawText, wtf.baseFont, wtf.fontSize,
                    (wtf.contentWidth - OccupiedExtra(tf.TextState.FormattingOptions, wtf.fontSize)) / ScaleOf(tf.TextState),
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, wtf.firstLineIndent, wtf.charSpacing,
                    tf.TextState.FormattingOptions?.HangingBreakSpace ?? false, tf.TextState.WordSpacing);
            // WrapLinesCount caps the wrapped paragraph at its first N lines; the
            // rest of the text is dropped (a one-line cell shows only its first line).
            if (tf.WrapLinesCount > 0 && wtf.allLines.Count > tf.WrapLinesCount)
                wtf.allLines.RemoveRange(tf.WrapLinesCount, wtf.allLines.Count - tf.WrapLinesCount);
            wtf.lineTrace = _logNotifications && !wtf.noWrap
                ? Text.TextPaginator.TraceLines(wtf.rawText, wtf.baseFont, wtf.fontSize, wtf.contentWidth,
                    tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData, wtf.firstLineIndent)
                : null;
            return true;
        }

        /// <summary>Resolves the line pitch, seeds the per-line fallback maps, and reserves the
        /// paragraph's opening room.</summary>
        private void InitFragmentLineHeight(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            wtf.fontTtf = tf.TextState.FontData?.TtfData
                          ?? tf.TextState.Font?.SourceFontData?.TtfData;
            wtf.fullSize = tf.TextState.FormattingOptions?.LineSpacing
                           == Text.TextFormattingOptions.LineSpacingMode.FullSize;
            if (tf.TextState.LineSpacing > 0)
                // An explicit LineSpacing is extra leading added on
                // top of the glyph height: the line pitch is fontSize + LineSpacing
                // (a 10pt font with LineSpacing 13
                // lays out on a 23pt pitch, not 13). LineSpacing == 0 degenerates to the
                // default fontSize pitch below, so the rule is uniform.
                wtf.lineHeight = wtf.fontSize + tf.TextState.LineSpacing;
            else if (wtf.fullSize && wtf.fontTtf is { Length: > 12 })
                wtf.lineHeight = ComputeFullSizeLineHeight(wtf.fontTtf, wtf.fontSize);
            else if (wtf.fullSize && Standard14ExtentEm(wtf.baseFont) > 0)
                // A Standard-14 face has no TTF to measure; FullSize takes its AFM
                // extent (Helvetica 0.925 em: a 10 pt line pitches 9.25 and seats on
                // 762.82 under a 770 band top, measured on the reference).
                wtf.lineHeight = Standard14ExtentEm(wtf.baseFont) * wtf.fontSize;
            else
                // Default LineSpacingMode is FontSize: the line
                // advance equals the font size, not an inflated 1.2x leading.
                wtf.lineHeight = wtf.fontSize;
            wtf.variableLineHeights = wtf.fullSize && wtf.fontTtf is { Length: > 12 };
            wtf.fallbackFace = new Dictionary<int, Text.Font?>();
            wtf.shapingLines = new HashSet<int>();
            wtf.coveringFace = new Dictionary<int, Text.Font?>();
            wtf.lineFallbackActive = wtf.fontTtf is { Length: > 12 };
            EnsureRoom(OrphanRoom(wtf.lineHeight, wtf.allLines.Count));
            _lastTextLinePitch = wtf.lineHeight;
        }

        /// <summary>Captures the link anchors the write loop will need once the cursor has
        /// moved, and resolves how each line is justified and aligned.</summary>
        private void InitFragmentLinksAndAlignment(FlowTextFragmentState wtf, Text.TextFragment tf)
        {
            wtf.fragHyperlink = tf.HyperlinkValue;
            wtf.fragSlot = _currentSlot;
            wtf.fragTop = _curY;

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
                wtf.segHyperlinks = collected;
            }

            wtf.fullJustify = !wtf.noWrap
                && (tf.HorizontalAlignment == HorizontalAlignment.FullJustify
                    || tf.TextState.HorizontalAlignment == HorizontalAlignment.FullJustify);
            wtf.spacingJustify = !wtf.noWrap && tf.TextState.FormattingOptions?.JustifySpacingRatio is not null
                && (tf.HorizontalAlignment == HorizontalAlignment.Justify
                    || tf.TextState.HorizontalAlignment == HorizontalAlignment.Justify);
            wtf.justify = !wtf.noWrap && !wtf.spacingJustify
                && (tf.HorizontalAlignment == HorizontalAlignment.Justify
                    || tf.TextState.HorizontalAlignment == HorizontalAlignment.Justify);

            wtf.alignMode = tf.HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right
                ? tf.HorizontalAlignment
                : tf.TextState.HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right
                    ? tf.TextState.HorizontalAlignment : HorizontalAlignment.Left;
            wtf.alignsLines = !wtf.noWrap && wtf.alignMode != HorizontalAlignment.Left;
        }

        /// <summary>The paragraph clip reaches this many line boxes above the bottom of the
        /// last one (the same 1.16 the table cell clip and the TextParagraph clip use).</summary>
        private const double ParagraphClipLineBoxEm = 1.16;

        /// <summary>The AFM extent (ascender + descender) of a Standard-14 face in em, 0 for
        /// any other name.</summary>
        private static double Standard14ExtentEm(string baseFont)
        {
            var ascent = Text.Standard14Fonts.GetAscent(baseFont);
            if (ascent <= 0) return 0;
            return (ascent + Math.Abs(Text.Standard14Fonts.GetDescent(baseFont))) / 1000.0;
        }

        /// <summary>The descriptor extent (Ascent + |Descent|, in em) of the face a state
        /// draws in: the hhea values a TrueType program's descriptor is written from,
        /// truncated to thousandths as the descriptor is, else the Standard-14 AFM.</summary>
        private static double DescriptorExtentEm(Text.TextState state, string baseFont)
        {
            var ttf = state.FontData?.TtfData ?? state.Font?.SourceFontData?.TtfData;
            if (ttf is { Length: > 12 } && Text.FontRepository.ReadTtfHheaExtent(ttf) is { } extent)
                return (extent.ascent + extent.descent) / 1000.0;
            return Standard14ExtentEm(baseFont);
        }

        /// <summary>The content bytes wrapped in a saved state that clips to
        /// <paramref name="clip"/>.</summary>
        private static byte[] WrapInClip(byte[] content, Rectangle clip)
        {
            var head = new Content.ContentStreamBuilder();
            head.SaveState();
            head.Rectangle(clip.LLX, clip.LLY, clip.Width, clip.Height).Clip();
            var tail = new Content.ContentStreamBuilder();
            tail.RestoreState();
            var h = head.Build();
            var t = tail.Build();
            var all = new byte[h.Length + content.Length + t.Length];
            Buffer.BlockCopy(h, 0, all, 0, h.Length);
            Buffer.BlockCopy(content, 0, all, h.Length, content.Length);
            Buffer.BlockCopy(t, 0, all, h.Length + content.Length, t.Length);
            return all;
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
