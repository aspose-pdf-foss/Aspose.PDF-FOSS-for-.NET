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
            // A highlight is one rectangle per write region: from the region's
            // last line box bottom up to the first line's bottom plus the
            // highlight box height, as wide as the region's widest line.
            double hlFirstBottom = 0, hlLastBottom = 0, hlLeft = 0, hlWidth = 0, hlSize = 0;
            var hlOpen = false;
            void FlushHighlight()
            {
                if (!hlOpen) return;
                hlOpen = false;
                if (background is null || hlWidth <= 0) return;
                var b = new Content.ContentStreamBuilder();
                b.SaveState();
                if (background.AByte < 255
                    && Text.TextParagraph.EnsureFillAlphaExtGState(_startPage, background.AByte) is { } bgGs)
                    b.SetExtGState(bgGs);
                b.SetFillColor(background.R / 255.0, background.G / 255.0, background.B / 255.0);
                b.Rectangle(hlLeft, hlLastBottom, hlWidth,
                    hlFirstBottom + HighlightBoxEm * hlSize - hlLastBottom);
                b.Fill();
                b.RestoreState();
                WriteContent(b.Build());
            }
            // The leading sits above a line only when the line before it carried
            // text (and above the paragraph's first line); a line holding only a
            // note mark is as tall as the mark and charges no leading below it.
            var prevHadText = true;
            for (var lineIdx = 0; lineIdx < lines.Count; lineIdx++)
            {
                var (left, cells) = lines[lineIdx];
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
                // A paragraph that is only its note mark is as tall as the mark's
                // parent text; a mark that wrapped onto its own line is as tall as
                // the mark.
                if (!hasText)
                    maxBase = maxImage > 0 ? maxImage
                        : breakSize > 0 ? breakSize
                        : lineIdx == 0 && markParent > 0 ? markParent
                        : markSize > 0 ? markSize : runs.Count > 0 ? runs[0].Size : 10;
                // A picture-only line takes the picture's own height and no leading;
                // a line that also carries text keeps the text's pitch and lets the
                // picture overhang.
                var lh = hasText || (maxImage <= 0 && breakSize <= 0)
                    ? maxBase + (prevHadText ? lineSpacing : 0)
                    : maxBase;
                if (_curY - lh < EffectiveBottom)
                {
                    FlushHighlight();
                    FlowToNextRegion();
                    lh = maxBase + lineSpacing;
                }
                // Line box = [cursor, cursor − pitch]; the queued Y is the box
                // bottom (descender line — the deferred TextBuilder write lifts
                // it by the font descent, landing the baseline exactly on
                // the line grid). Superscript runs raise the box; a note mark
                // hangs from the line's text top.
                var boxBottom = _curY - lh;
                var textTop = boxBottom + maxBase;
                var lineTop = boxBottom + lh;
                // A mark-only inline paragraph joined onto this line hangs its
                // mark from the line's box top and ends its own height below it.
                double joinH = 0;
                if (hasText)
                    foreach (var (_, _, r) in cells)
                        if (r.NoteMark && r.JoinHeight > 0) joinH = Math.Max(joinH, r.JoinHeight);
                var markTop = joinH > 0 ? lineTop : textTop;
                if (hasText) _lastTextLinePitch = maxBase + lineSpacing;
                // Natural cell extents, then the line's alignment: justified
                // lines spread their slack over the interior spaces (not the
                // paragraph's last line), centred / right lines shift whole.
                var sized = new List<(double x, string text, StyledRun run, double size)>(cells.Count);
                double lineW = 0;
                foreach (var (xr, text, r) in cells)
                {
                    var size = StyledRunSize(r);
                    sized.Add((xr, text, r, size));
                    if (r.ImageData is not null) lineW = Math.Max(lineW, xr + r.ImageW);
                    else if (text.Length > 0) lineW = Math.Max(lineW, xr + MeasureStyled(text, r, size));
                }
                var xs = AlignedCellXs(sized, lineW, CurWidth - left, align, lineIdx == lines.Count - 1);
                for (var ci = 0; ci < sized.Count; ci++)
                {
                    var (_, text, r, size) = sized[ci];
                    if (r.ImageData is not null)
                    {
                        var ix = CurLeft + left + xs[ci];
                        _pendingImages.Add((_currentSlot, r.ImageData,
                            new Rectangle(ix, lineTop - r.ImageH, ix + r.ImageW, lineTop)));
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
                if (!hlOpen)
                {
                    hlOpen = true;
                    hlFirstBottom = boxBottom;
                    hlLeft = CurLeft + left;
                    hlSize = maxBase;
                    hlWidth = 0;
                }
                hlLastBottom = boxBottom;
                hlWidth = Math.Max(hlWidth, lineW);
                // A break line carries no text, but the line AFTER it still charges its
                // own leading, so it counts as a text line for that purpose.
                prevHadText = hasText || breakSize > 0;
                // ONE Link annotation per hyperlinked run per line — consecutive
                // word/space cells of the same run coalesce into a single rect.
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
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY = joinH > 0 ? lineTop - joinH : boxBottom;
            }
            FlushHighlight();
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

        /// <summary>Advance of <paramref name="text"/> drawn in <paramref name="st"/> at <paramref name="size"/>.</summary>
        private static double MeasureStyledText(string text, Text.TextState st, double size)
        {
            var fd = st.FontData ?? st.Font?.SourceFontData;
            return Text.TextPaginator.CreateMeasurer(Text.TextBuilder.MapToStandard14Public(st), size, fd)(text);
        }

        /// <summary>Render a fragment whose segments carry differing styles as ONE
        /// line at the flow cursor: each segment is its own show in its own
        /// Standard-14 base font/size, x chained by the segment's REAL measured
        /// width (a bold run advances by its bold width). Returns false — caller
        /// falls back to the legacy writer — for shapes this single-line writer
        /// doesn't model: explicit newlines, text wider than the band (needs
        /// wrapping), embedded fonts, decorations, hyperlinks, or an overflow
        /// buffer in flight (continuation pages only materialise Helvetica).</summary>
        public bool TryWriteStyledSegmentsLine(Text.TextFragment tf)
        {
            if (_overflowBuffer is not null) return false;
            var runs = new List<(string text, string baseFont, double fs, Color? color)>();
            foreach (var seg in tf.Segments)
            {
                var text = seg.Text ?? string.Empty;
                if (text.Length == 0) continue;
                if (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0) return false;
                if (seg.Hyperlink is not null) return false;
                var st = seg.TextState;
                if (st.IsUnderline || st.IsStrikeOut) return false;
                if (st.FontData is not null || st.Font?.SourceFontData is not null) return false;
                var fs = st.FontSizeTouched ? (double)st.FontSize
                    : tf.TextState.FontSize > 0 ? tf.TextState.FontSize : (double)st.FontSize;
                if (fs <= 0) fs = 12;
                runs.Add((text, Text.TextBuilder.MapToStandard14Public(st), fs,
                    st.ForegroundColor ?? tf.TextState.ForegroundColor));
            }
            if (runs.Count == 0) return true; // nothing visible — consume as an empty write

            double totalWidth = 0, maxFs = 0;
            foreach (var r in runs)
            {
                totalWidth += MeasureLineWidth(r.text, r.baseFont, r.fs);
                if (r.fs > maxFs) maxFs = r.fs;
            }
            if (totalWidth > CurWidth + 0.5) return false; // would wrap — legacy writer

            var lineHeight = tf.TextState.LineSpacing > 0 ? maxFs + tf.TextState.LineSpacing : maxFs;
            EnsureRoom(lineHeight);

            // First baseline drops by the cap-height ascent from the band top —
            // the same placement BuildWrappedTextStream uses for plain fragments,
            // so a styled line sits exactly where its unstyled twin would.
            var capHeight = Text.Standard14Fonts.GetCapHeight(runs[0].baseFont);
            var ascent = capHeight > 0 ? capHeight / 1000.0 * maxFs : maxFs * 0.7;
            var baseline = _curY - ascent;

            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            var x = CurLeft;
            foreach (var r in runs)
            {
                var res = Table.RegisterFont(_startPage, r.baseFont);
                b.BeginText().SetFont(res, r.fs);
                if (r.color is { } c) b.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
                else b.SetFillColor(0, 0, 0);
                b.MoveTextPosition(x, baseline).ShowText(r.text).EndText();
                x += MeasureLineWidth(r.text, r.baseFont, r.fs);
            }
            b.RestoreState();
            WriteContent(b.Build());

            _curY -= lineHeight;
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
        }

        /// <summary>Write a BindXml-built fragment with the classic XML-generator
        /// line model:
        ///   • every line advances the cursor by exactly its font size plus the
        ///     fragment leading (no 1.2× line box);
        ///   • a run's baseline seat below the line top is its own font's AFM
        ///     extent: (1000 − |descent|)/1000 × fs, plus the leading — two
        ///     same-line runs in different faces sit on slightly different
        ///     baselines;
        ///   • segment text is VERBATIM — newlines break lines (leading/trailing
        ///     newlines produce blank lines), spaces keep their width;
        ///   • #$TAB advances the pen to the fragment's explicit TabStop
        ///     (Position measured from the fragment's left edge), and past the
        ///     defined stops to the next default stop at multiples of
        ///     <see cref="XmlDefaultTabStopSpaces"/> space-widths;
        ///   • wrapped continuation lines restart at the fragment's left edge.
        /// Runs are queued as deferred renders so continuation pages register
        /// their own font resources.</summary>
        public bool WriteXmlModelFragment(Text.TextFragment tf)
        {
            var mLeft = tf.Margin?.Left ?? 0;
            var mRight = tf.Margin?.Right ?? 0;
            var fragFs = tf.TextState.FontSizeTouched && tf.TextState.FontSize > 0
                ? (double)tf.TextState.FontSize : XmlDefaultFontSize;
            // The leading: the fragment's own, else the tallest its segments declare
            // (a <TextSegment><TextState LineSpacing="3"/> pitches the line at fs + 3).
            var leading = tf.TextState.LineSpacing > 0 ? (double)tf.TextState.LineSpacing : 0;
            if (leading <= 0 && tf.Segments is { Count: > 0 })
                foreach (var seg in tf.Segments)
                    if (seg.TextState.LineSpacing > leading) leading = seg.TextState.LineSpacing;
            // An authored-empty <TextFragment /> (no segment at all) takes no room;
            // a fragment whose segment is empty still stands one line tall.
            if (tf.XmlEmptyShell && tf.FootNote is null && tf.EndNote is null)
                return true;

            // Margin.Top/Bottom are consumed by the paragraph dispatcher around
            // this call; only the horizontal margins are applied here.
            var fragLeft = CurLeft + mLeft;
            var rightEdge = _startPage.Width - _marginRight - mRight;

            // One styled run placed on the current line: x is absolute.
            var line = new List<(double x, string text, Text.TextState st, string face, double fs)>();
            double pen = fragLeft, lineMaxFs = 0;
            var tabIndex = 0;

            void FlushLine()
            {
                // A line with NO glyph runs at all (a bare newline) is one
                // schema-default line tall regardless of the fragment's size —
                // the 12 pt headings' leading blank lines and
                // the 20 pt title's are all exactly 10 pt (whitespace-BEARING
                // lines instead take their runs' size, like any other line).
                var lineH = (lineMaxFs > 0 ? lineMaxFs : XmlDefaultFontSize) + leading;
                EnsureRoom(lineH);
                double shift = 0;
                if (line.Count > 0
                    && tf.HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
                {
                    var slack = rightEdge - pen;
                    if (slack > 0)
                        shift = tf.HorizontalAlignment == HorizontalAlignment.Center ? slack / 2 : slack;
                }
                foreach (var r in line)
                {
                    var descent = Text.Standard14Fonts.GetDescent(r.face); // negative
                    var seat = (1000 + descent) / 1000.0 * r.fs + leading;
                    _pendingEmbeddedRenders.Add((_currentSlot, r.x + shift, _curY,
                        r.text, r.st, r.fs, _curY - seat));
                    if (_overflowBuffer is not null)
                        _overflowBuffer.Add(Array.Empty<byte>());
                }
                _curY -= lineH;
                line.Clear();
                pen = fragLeft;
                lineMaxFs = 0;
                tabIndex = 0;
            }

            // Two segment states draw identically when every property the run
            // emission reads agrees — the heading prefix and its text, cloned
            // from the same style, must merge into ONE show (the absorber reads
            // a run's interior and trailing spaces verbatim, while a run break
            // re-synthesises them from geometry and drops the trailing one).
            static bool SameRunStyle(Text.TextState a, Text.TextState b)
            {
                var ca = a.ForegroundColor;
                var cb = b.ForegroundColor;
                var colorsEqual = ca is null ? cb is null
                    : cb is not null && ca.R == cb.R && ca.G == cb.G && ca.B == cb.B;
                return colorsEqual && a.Underline == b.Underline && a.IsStrikeOut == b.IsStrikeOut;
            }

            void AppendRun(string text, Text.TextState st, string face, double fs)
            {
                if (text.Length == 0) return;
                // Coalesce contiguous same-style words into one show — the
                // writer emits one run per styled piece per line.
                if (line.Count > 0)
                {
                    var last = line[^1];
                    if (last.face == face && last.fs == fs && SameRunStyle(last.st, st)
                        && Math.Abs(last.x + MeasureLineWidth(last.text, face, fs) - pen) < 0.005)
                    {
                        line[^1] = (last.x, last.text + text, st, face, fs);
                        pen += MeasureLineWidth(text, face, fs);
                        if (fs > lineMaxFs) lineMaxFs = fs;
                        return;
                    }
                }
                line.Add((pen, text, st, face, fs));
                pen += MeasureLineWidth(text, face, fs);
                if (fs > lineMaxFs) lineMaxFs = fs;
            }

            foreach (var seg in tf.Segments)
            {
                var st = seg.TextState ?? tf.TextState;
                var fs = st.FontSizeTouched && st.FontSize > 0 ? (double)st.FontSize : fragFs;
                var face = Text.TextBuilder.MapToStandard14Public(st);
                var text = (seg.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
                if (text.Length == 0) continue;

                var pieces = text.Split(new[] { Text.TextBuilder.TabMarker }, StringSplitOptions.None);
                for (var pi = 0; pi < pieces.Length; pi++)
                {
                    if (pi > 0)
                    {
                        // #$TAB: pen to the next stop, measured from the fragment left.
                        var rel = pen - fragLeft;
                        double target;
                        if (tf.TabStops is { Count: > 0 } stops && tabIndex < stops.Count)
                            target = stops[tabIndex++].Position;
                        else
                        {
                            var interval = XmlDefaultTabStopSpaces
                                * Text.Standard14Fonts.GetWidth(face, ' ') / 1000.0 * fs;
                            target = (Math.Floor(rel / interval + 1e-6) + 1) * interval;
                        }
                        if (target > rel) pen = fragLeft + target;
                        if (fs > lineMaxFs) lineMaxFs = fs;
                    }
                    var piece = pieces[pi];
                    var nlSplit = piece.Split('\n');
                    for (var li = 0; li < nlSplit.Length; li++)
                    {
                        if (li > 0) FlushLine();
                        // Wrap the part word-by-word; a word keeps its trailing
                        // spaces (they render, and the pen advances past them —
                        // the fit test ignores them).
                        foreach (var word in SplitXmlWords(nlSplit[li]))
                        {
                            var bare = word.TrimEnd(' ');
                            if (line.Count > 0 || pen > fragLeft + 0.01)
                            {
                                var bareW = MeasureLineWidth(bare, face, fs);
                                if (pen + bareW > rightEdge + 0.01) FlushLine();
                            }
                            AppendRun(word, st, face, fs);
                        }
                    }
                }
            }
            FlushLine();

            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
        }

        /// <summary>Split a line into words, each carrying its trailing spaces
        /// ("in the" → ["in ", "the"]); leading spaces ride the first word.</summary>
        private static IEnumerable<string> SplitXmlWords(string text)
        {
            var start = 0;
            var i = 0;
            while (i < text.Length)
            {
                // consume a word (or leading spaces followed by a word), then its trailing spaces
                while (i < text.Length && text[i] == ' ') i++;
                while (i < text.Length && text[i] != ' ') i++;
                while (i < text.Length && text[i] == ' ') i++;
                yield return text[start..i];
                start = i;
            }
        }

        /// <summary>Write one HTML block whose inline <c>&lt;b&gt;</c>/<c>&lt;strong&gt;</c>
        /// and <c>&lt;u&gt;</c> runs each set in their OWN style, as a single wrapped
        /// paragraph: the emphasised words draw bold and/or underlined while the rest of
        /// the block stays regular. Every run is measured in the face it actually draws
        /// in — <see cref="Text.TextBuilder"/> resolves a bold flag on a repository face
        /// to that family's Bold member, so the wrap and the run x positions are taken
        /// from the same metrics the glyphs get — and each piece is queued as its own
        /// deferred render. Returns false when the flow cannot take the fragment.</summary>
        public bool WriteEmphasisRuns(Text.TextFragment tf,
            IReadOnlyList<(int Start, int Length, bool Bold, bool Italic, bool Underline,
                Hyperlink? Link)> runs)
        {
            if (tf.HasExplicitPosition) return false;
            var text = tf.Text ?? string.Empty;
            if (text.Length == 0 || runs.Count == 0) return false;
            var contentWidth = CurWidth;
            if (contentWidth <= 0) return false;

            var fontSize = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 12;
            var regFont = Text.TextBuilder.MapToStandard14Public(tf.TextState);
            var regData = tf.TextState.FontData ?? tf.TextState.Font?.SourceFontData;
            // The bold member of the block's own family, resolved exactly the way
            // TextBuilder resolves a bold flag on a repository-embedded face. Absent
            // (a core face, or no Bold file installed) the regular metrics stand in,
            // which is what the writer will draw with too.
            Text.FontData? boldData = null;
            var family = tf.TextState.FontData?.FontName ?? tf.TextState.Font?.FontName
                ?? tf.TextState.FontName;
            if (!string.IsNullOrEmpty(family) && !Text.Standard14Fonts.IsCoreName(family)
                && !family.Contains("Bold", StringComparison.OrdinalIgnoreCase))
            {
                var styled = Text.FontRepository.FindFontData(family + " Bold");
                if (styled?.TtfData is not null
                    && styled.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true)
                    boldData = styled;
            }
            // Standard-14 stand-in for the bold runs when no repository Bold member
            // resolved: the bold variant of whatever core family the block maps to.
            var boldProbe = new Text.TextState
            {
                Font = tf.TextState.Font,
                FontData = tf.TextState.FontData,
                FontName = tf.TextState.FontName,
                IsBold = true,
                IsItalic = tf.TextState.IsItalic,
            };
            var boldFont = Text.TextBuilder.MapToStandard14Public(boldProbe);
            // The italic member of the family, resolved the same way — an italic run is
            // measured in the face it will actually draw in.
            Text.FontData? italicData = null;
            if (!string.IsNullOrEmpty(family) && !Text.Standard14Fonts.IsCoreName(family)
                && !family.Contains("Italic", StringComparison.OrdinalIgnoreCase))
            {
                var styledItalic = Text.FontRepository.FindFontData(family + " Italic");
                if (styledItalic?.TtfData is not null
                    && styledItalic.FontName?.Contains("Italic", StringComparison.OrdinalIgnoreCase) == true)
                    italicData = styledItalic;
            }
            var italicProbe = new Text.TextState
            {
                Font = tf.TextState.Font,
                FontData = tf.TextState.FontData,
                FontName = tf.TextState.FontName,
                IsBold = tf.TextState.IsBold,
                IsItalic = true,
            };
            var italicFont = Text.TextBuilder.MapToStandard14Public(italicProbe);
            var measureReg = Text.TextPaginator.CreateMeasurer(regFont, fontSize, regData);
            var measureBold = Text.TextPaginator.CreateMeasurer(boldFont, fontSize,
                boldData ?? regData);
            var measureItalic = Text.TextPaginator.CreateMeasurer(italicFont, fontSize,
                italicData ?? regData);
            double Measure(string s, bool bold, bool italic)
                => bold ? measureBold(s) : italic ? measureItalic(s) : measureReg(s);

            // Word / whitespace tokens that never straddle a run boundary, so every
            // token has exactly one style and one measurable width.
            var tokens = new List<(string Text, bool Bold, bool Italic, bool Under,
                Hyperlink? Link, bool Space, double W)>();
            foreach (var r in runs)
            {
                if (r.Start < 0 || r.Length <= 0 || r.Start + r.Length > text.Length) continue;
                var seg = text.Substring(r.Start, r.Length);
                var i = 0;
                while (i < seg.Length)
                {
                    var isSpace = seg[i] == ' ';
                    var j = i;
                    while (j < seg.Length && (seg[j] == ' ') == isSpace) j++;
                    var piece = seg.Substring(i, j - i);
                    tokens.Add((piece, r.Bold, r.Italic, r.Underline, r.Link, isSpace,
                        Measure(piece, r.Bold, r.Italic)));
                    i = j;
                }
            }
            if (tokens.Count == 0) return false;

            // Greedy wrap: a word that would overrun the content box starts a new line
            // and the break's own space is swallowed, as the plain wrap does.
            var lines = new List<List<(string Text, bool Bold, bool Italic, bool Under,
                Hyperlink? Link, double W)>>();
            var cur = new List<(string Text, bool Bold, bool Italic, bool Under,
                Hyperlink? Link, double W)>();
            var curW = 0.0;
            foreach (var t in tokens)
            {
                if (!t.Space && cur.Count > 0 && curW + t.W > contentWidth + WrapWidthSlackPt)
                {
                    while (cur.Count > 0 && cur[^1].Text.Trim().Length == 0)
                        cur.RemoveAt(cur.Count - 1);
                    if (cur.Count > 0) lines.Add(cur);
                    cur = new List<(string, bool, bool, bool, Hyperlink?, double)>();
                    curW = 0;
                }
                if (t.Space && cur.Count == 0) continue;
                cur.Add((t.Text, t.Bold, t.Italic, t.Under, t.Link, t.W));
                curW += t.W;
            }
            if (cur.Count > 0) lines.Add(cur);
            if (lines.Count == 0) return false;

            var lineHeight = tf.TextState.LineSpacing > 0
                ? fontSize + tf.TextState.LineSpacing : fontSize;
            EnsureRoom(OrphanRoom(lineHeight, lines.Count));
            // Runs go out as deferred renders; everything after them in this flow must
            // defer too so the page's content order stays paragraph order.
            _forceDeferredWrites = true;

            var idx = 0;
            while (idx < lines.Count)
            {
                var availableLines = Math.Max(1, (int)((_curY - EffectiveBottom) / lineHeight));
                var chunkSize = Math.Min(availableLines, lines.Count - idx);
                // Same first-baseline rule as the plain embedded chunk: chain onto the
                // previous body baseline when there is one, else drop by the font size.
                var firstBaseline = _lastBodyBaseline.HasValue
                    ? _lastBodyBaseline.Value - lineHeight
                    : FirstBaselineSeat(tf.TextState, fontSize, lineHeight);
                for (var j = 0; j < chunkSize; j++)
                {
                    var baseline = firstBaseline - j * lineHeight;
                    var x = CurLeft;
                    var line = lines[idx + j];
                    var k = 0;
                    while (k < line.Count)
                    {
                        // Merge the neighbours that share a style into one show.
                        var m = k + 1;
                        while (m < line.Count && line[m].Bold == line[k].Bold
                               && line[m].Italic == line[k].Italic
                               && line[m].Under == line[k].Under
                               && ReferenceEquals(line[m].Link, line[k].Link)) m++;
                        var sb = new System.Text.StringBuilder();
                        var w = 0.0;
                        for (var p = k; p < m; p++) { sb.Append(line[p].Text); w += line[p].W; }
                        var runState = new Text.TextState
                        {
                            Font = tf.TextState.Font,
                            FontData = tf.TextState.FontData,
                            ForegroundColor = tf.TextState.ForegroundColor,
                            IsBold = line[k].Bold,
                            IsItalic = line[k].Italic || tf.TextState.IsItalic,
                            Underline = line[k].Under,
                        };
                        _pendingEmbeddedRenders.Add((_currentSlot, x, _curY,
                            sb.ToString(), runState, fontSize, baseline));
                        // An anchored run carries its own Link annotation, boxed on
                        // this line's baseline. Without this, a block that mixes a
                        // hyperlink with any bold or underlined run reaches the
                        // reader with no clickable link at all.
                        if (line[k].Link is { } emLink && w > 0
                            && sb.ToString().Trim().Length > 0)
                        {
                            var (emAbove, emBelow) = LinkBoxExtent(tf.TextState, fontSize);
                            _pendingLinks.Add((_currentSlot,
                                new Rectangle(x, baseline - emBelow, x + w,
                                    baseline + emAbove), emLink));
                        }
                        x += w;
                        k = m;
                    }
                }
                _lastBodyBaseline = firstBaseline - (chunkSize - 1) * lineHeight;
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY -= lineHeight * chunkSize;
                idx += chunkSize;
                if (idx < lines.Count) FlowToNextRegion();
            }
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
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
            IReadOnlyList<double>? lineOffsets = null)
        {
            // The left indent of this chunk's first rendered line: the paragraph's
            // own first line uses FirstLineIndent; a chunk that continues the
            // paragraph onto a new page is all "subsequent" lines. Per-line
            // alignment offsets, when given, replace both.
            var firstIndent = chunkStartsParagraph ? firstLineIndent : subsequentLinesIndent;
            double LineIndent(int i) => lineOffsets is not null ? lineOffsets[i]
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

            // Background highlight: a filled rectangle behind each wrapped line,
            // sized to the line's measured width and the font's em box (baseline +
            // descent up by one font size). Drawn before the glyphs, in its own
            // graphics state so the background's /ca alpha doesn't bleed into the
            // foreground fill that follows.
            if (background is { } bgcol)
            {
                var bgFontName = fontName ?? "Helvetica";
                var descentPt = Text.Standard14Fonts.GetDescent(bgFontName) / 1000.0 * fontSize; // negative
                b.SaveState();
                if (bgAlphaGsName is not null) b.SetExtGState(bgAlphaGsName);
                b.SetFillColor(bgcol.R / 255.0, bgcol.G / 255.0, bgcol.B / 255.0);
                for (var i = 0; i < lines.Count; i++)
                {
                    var lineW = MeasureLineWidth(lines[i], bgFontName, fontSize);
                    if (lineW <= 0) continue;
                    var lineY = firstBaseline - i * lineHeight;
                    var lineX = startX + LineIndent(i);
                    b.Rectangle(lineX, lineY + descentPt, lineW, fontSize * HighlightBoxEm);
                    b.Fill();
                }
                b.RestoreState();
            }

            b.BeginText();
            b.SetFont(fontResName, fontSize);
            b.SetLeading(lineHeight);
            // The first line starts indented by firstLineIndent; line 2 shifts back
            // to startX via a relative Td (Td is relative to the current line start,
            // so a plain T* would otherwise carry the indent down to every line).
            // TextState.Rotation rotates the whole block around its first baseline
            // origin via the text matrix (Td/T* then advance in rotated text space).
            if (rotation != 0)
            {
                var rad = rotation * Math.PI / 180.0;
                var cos = Math.Round(Math.Cos(rad), 10);
                var sin = Math.Round(Math.Sin(rad), 10);
                b.SetTextMatrix(cos, sin, -sin, cos, startX + LineIndent(0), firstBaseline);
            }
            else
            {
                b.MoveTextPosition(startX + LineIndent(0), firstBaseline);
            }
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    // Shift by the change in indent (Td is relative to the current
                    // line start), then drop a line; an unchanged indent is a plain
                    // NextLine (T*).
                    var delta = LineIndent(i) - LineIndent(i - 1);
                    if (delta != 0) b.MoveTextPosition(delta, -lineHeight);
                    else b.NextLine();
                }
                b.ShowText(lines[i]);
            }
            b.EndText();

            // Emit strikeout / underline rectangles after the text. One per
            // wrapped line, sized to the line's measured width.
            if ((strikeOut || underline) && (fontName is not null))
            {
                var thickness = fontSize * DecorationThicknessEm;
                // Both rules hang off the decoration origin below the baseline;
                // the strike-through rises a fixed share of the em above it.
                var origin = -DecorationOriginDescentShare * DescentNorm(fontName) * fontSize;
                var soOffset = origin + StrikeoutRiseEm * fontSize;
                for (var i = 0; i < lines.Count; i++)
                {
                    double lineW = MeasureLineWidth(lines[i], fontName, fontSize);
                    double lineY = firstBaseline - i * lineHeight;
                    double lineX = startX + LineIndent(i);
                    if (strikeOut)
                    {
                        b.Rectangle(lineX, lineY + soOffset, lineW, thickness);
                        b.Fill();
                    }
                    if (underline)
                    {
                        b.Rectangle(lineX, lineY + origin, lineW, thickness);
                        b.Fill();
                    }
                }
            }

            b.RestoreState();
            return b.Build();
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
            var lines = LayoutInlineLines(runs, CurWidth);
            foreach (var rawCells in lines)
            {
                // One show per run per line: consecutive cells of the same run merge
                // (the absorber then reports one fragment per run and line, as the
                // generator does), so word tokens never surface as fragments.
                var cells = new List<(double x, string text, InlineRun run)>();
                foreach (var cell in rawCells)
                {
                    if (cells.Count > 0 && ReferenceEquals(cells[^1].run, cell.run) && cell.run.ImageData is null)
                        cells[^1] = (cells[^1].x, cells[^1].text + cell.text, cell.run);
                    else cells.Add(cell);
                }
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
                if (advance <= 0) advance = runs.Count > 0 && runs[0].Pitch > 0 ? runs[0].Pitch : 10;
                EnsureRoom(advance);
                if (lastTextGroup >= 0) _lastTextLinePitch = advance;
                var lineTop = _curY;
                var slack = align switch
                {
                    HorizontalAlignment.Right => CurWidth - lineWidth,
                    HorizontalAlignment.Center => (CurWidth - lineWidth) / 2,
                    _ => 0,
                };
                if (slack < 0) slack = 0;

                var deco = new Content.ContentStreamBuilder();
                var anyDeco = false;
                Hyperlink? runLink = null;
                Text.TextState? runLinkState = null;
                double runLinkSize = 0, runLinkBottom = 0, linkX0 = 0, linkX1 = 0;
                void FlushRunLink()
                {
                    if (runLink is not null && linkX1 > linkX0)
                    {
                        var (lkAbove, lkBelow) = LinkBoxExtent(runLinkState, runLinkSize);
                        _pendingLinks.Add((_currentSlot,
                            new Rectangle(linkX0, runLinkBottom, linkX1, runLinkBottom + lkAbove + lkBelow),
                            runLink));
                    }
                    runLink = null;
                }
                foreach (var (cx, text, r) in cells)
                {
                    var x0 = CurLeft + slack + cx;
                    if (r.ImageData is not null)
                    {
                        FlushRunLink();
                        _pendingImages.Add((_currentSlot, r.ImageData,
                            new Rectangle(x0, lineTop - r.ImageH, x0 + r.ImageW, lineTop)));
                        continue;
                    }
                    if (text.Length == 0) { FlushRunLink(); continue; }
                    if (r.NoteMarker)
                    {
                        FlushRunLink();
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
                        continue;
                    }
                    var boxBottom = lineTop - groupPitch[r.Group];
                    var descent = RunDescentEm(r.State) * r.Size;
                    var baseline = boxBottom + descent;
                    // The deferred writer lifts an embedded face by its own descent;
                    // a Standard-14 run is seated on the baseline directly.
                    var y = RunIsEmbedded(r.State) ? boxBottom : baseline;
                    _pendingEmbeddedRenders.Add((_currentSlot, x0, lineTop, text, r.State, r.Size, y));
                    var w = InlineMeasure(text, r);
                    if (r.Underline || r.Strike)
                    {
                        var origin = baseline - DecorationOriginDescentShare * descent;
                        var thick = r.Size * DecorationThicknessEm;
                        if (!anyDeco) { deco.SaveState(); anyDeco = true; }
                        var fg = r.State.ForegroundColor;
                        if (fg is not null) deco.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);
                        else deco.SetFillColor(0, 0, 0);
                        if (r.Underline) { deco.Rectangle(x0, origin, w, thick); deco.Fill(); }
                        if (r.Strike) { deco.Rectangle(x0, origin + StrikeoutRiseEm * r.Size, w, thick); deco.Fill(); }
                    }
                    if (r.Link is null) { FlushRunLink(); continue; }
                    if (!ReferenceEquals(runLink, r.Link))
                    {
                        FlushRunLink();
                        runLink = r.Link;
                        linkX0 = x0;
                        runLinkState = r.State;
                        runLinkSize = r.Size;
                        runLinkBottom = boxBottom;
                    }
                    linkX1 = x0 + w;
                }
                FlushRunLink();
                if (anyDeco)
                {
                    deco.RestoreState();
                    AddContentToSlot(_currentSlot, deco.Build());
                }
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
                _curY = lineTop - advance;
            }
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
        }

    }
}
