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
        /// <summary>Top/bottom margins of overflow slot <paramref name="slot"/>:
        /// the page-break hook's answer (asked once per slot — a table that
        /// pre-builds the slot's slice and the flow that later continues on it
        /// see the same prepared page), else the current margins.</summary>
        internal (double top, double bottom) MarginsForSlot(int slot)
        {
            if (_slotMargins.TryGetValue(slot, out var m)) return m;
            m = OnPageBreak is { } onBreak ? onBreak(slot) : (_marginTop, _marginBottom);
            _slotMargins[slot] = m;
            return m;
        }

        /// <summary>Where a paragraph started rendering in this flow (slot −1 =
        /// the start page, else the flow-relative overflow slot). Used by the
        /// deferred TOC emit to print the page a heading FINALLY landed on.</summary>
        public bool TryGetParagraphPosition(BaseParagraph p, out (int slot, double yTop) pos)
            => _paragraphPositions.TryGetValue(p, out pos);

        /// <summary>One styled run inside a flow paragraph: a piece of text
        /// with its own font/size/colour/underline, an optional superscript
        /// flag (footnote reference marks and band labels), the owning
        /// paragraph's extra left margin (heading Margin.Left — applies to the
        /// lines this run starts), and an optional fixed tab stop the NEXT
        /// run jumps to (heading label → text gap).</summary>
        internal sealed class StyledRun
        {
            public string Text = string.Empty;
            public double Size = 10;
            public Text.TextState State = new();
            public bool Sup;
            // A note reference mark: half the parent size, top-aligned at the
            // line's text top, linked to its band.
            public bool NoteMark;
            public Note? Note;
            // A mark whose paragraph is an inline-joined fragment with no text
            // of its own: the paragraph joins the previous line at that line's
            // box top and the flow continues this far below the box top (the
            // paragraph's own size + leading), not below the line.
            public double JoinHeight;
            // The first text run of an inline-joined fragment: its first word
            // cannot leave the line it joins (see LayoutStyledLines).
            public bool InlineStart;
            public double OwnerLeft;
            public double TabAfter;
            public Hyperlink? Link;
            // A picture laid into the line. It is drawn from the line's TOP
            // downward at its own size and advances the pen by its width, but it
            // gives the line NO height while the line carries text (probed: a
            // 168 pt picture on a 9 pt inline line still advances the flow the
            // text's 22 pt and simply overhangs). A line holding only pictures is
            // as tall as its tallest one, and charges no leading.
            public byte[]? ImageData;
            public double ImageW, ImageH;
            // A whitespace-only segment: it CLOSES the line it sits on and stands one
            // builder-default line of its own (10 pt, in the default face) — NOT its own
            // declared size, and charging no leading of its own. The line after it
            // charges its leading normally. Probed 2026-08-26: a 12 pt fragment with a
            // 15 pt leading that OPENS with a newline segment drops 10 pt to that empty
            // line and only then steps its own 27 pt pitch.
            public bool HardBreak;
        }

        /// <summary>The footer's text lines: one line per TextFragment paragraph,
        /// an inline paragraph sharing the line before it when that line has text
        /// (a page number after an empty paragraph takes its own line). Returns the
        /// sum of the lines' text sizes (each line as tall as its tallest member)
        /// and the sum of their leadings (each line's tallest member leading).</summary>
        private static (double textHeight, double leading) FooterLines(HeaderFooter footer)
        {
            double textTotal = 0, leadTotal = 0, lineFs = 0, lineLs = 0;
            var lineHasText = false;
            var any = false;
            foreach (var p in footer.Paragraphs)
            {
                if (p is not Text.TextFragment tf) continue;
                var st = tf.TextState;
                double fs = st.FontSizeTouched ? st.FontSize : 0, ls = st.LineSpacing;
                foreach (var seg in tf.Segments)
                {
                    if (fs <= 0 && seg.TextState.FontSizeTouched) fs = seg.TextState.FontSize;
                    if (ls <= 0 && seg.TextState.LineSpacing > 0) ls = seg.TextState.LineSpacing;
                }
                if (fs <= 0) fs = 10;
                var hasText = !string.IsNullOrEmpty(tf.Text);
                if (any && tf.IsInLineParagraph && lineHasText)
                {
                    lineFs = Math.Max(lineFs, fs);
                    lineLs = Math.Max(lineLs, ls);
                }
                else
                {
                    textTotal += lineFs; leadTotal += lineLs;
                    lineFs = fs; lineLs = ls;
                    lineHasText = false;
                }
                lineHasText |= hasText;
                any = true;
            }
            textTotal += lineFs; leadTotal += lineLs;
            if (!any) textTotal = footer.TextState.FontSize > 0 ? footer.TextState.FontSize : 10;
            return (textTotal, leadTotal);
        }

        /// <summary>The x of every cell of a line: justified lines spread their
        /// slack over the interior spaces; centred / right lines shift whole.</summary>
        private static double[] CellXs(BandLine line, double width)
            => AlignedCellXs(line.Cells.ConvertAll(c => (c.x, c.text, c.run, c.size)),
                line.NaturalWidth, width, line.Align, line.LastOfParagraph);

        private static double[] AlignedCellXs(List<(double x, string text, StyledRun run, double size)> cells,
            double naturalWidth, double width, HorizontalAlignment align, bool lastLine)
        {
            var xs = new double[cells.Count];
            for (var i = 0; i < cells.Count; i++) xs[i] = cells[i].x;
            var slack = width - naturalWidth;
            if (slack <= 0) return xs;
            if (align == HorizontalAlignment.Justify && !lastLine)
            {
                // Interior spaces: every space cell followed by ink.
                var lastInk = -1;
                for (var i = 0; i < cells.Count; i++) if (cells[i].text.Length > 0 && cells[i].text != " ") lastInk = i;
                var spaces = 0;
                for (var i = 0; i < lastInk; i++) if (cells[i].text == " ") spaces++;
                if (spaces == 0) return xs;
                var extra = slack / spaces;
                var before = 0;
                for (var i = 0; i < cells.Count; i++)
                {
                    xs[i] = cells[i].x + before * extra;
                    if (cells[i].text == " " && i < lastInk) before++;
                }
                return xs;
            }
            var shift = align == HorizontalAlignment.Center ? slack / 2
                : align == HorizontalAlignment.Right ? slack : 0;
            if (shift > 0) for (var i = 0; i < xs.Length; i++) xs[i] += shift;
            return xs;
        }

        /// <summary>Reserve a paragraph's top margin: the margin and the first
        /// line are one unit, so when they do not fit together the page breaks
        /// first and the margin opens the next page (a headnote heading with a
        /// 6 pt top margin stands 6 pt below the content top of the page it
        /// moves to, not flush against it).</summary>
        public void ReserveTopMargin(double margin, Text.TextFragment tf)
        {
            if (margin <= 0) return;
            double fs = tf.TextState.FontSize > 0 ? tf.TextState.FontSize : 0;
            double ls = tf.TextState.LineSpacing;
            foreach (var seg in tf.Segments)
            {
                fs = Math.Max(fs, seg.TextState.FontSize);
                ls = Math.Max(ls, seg.TextState.LineSpacing);
            }
            if (fs <= 0) fs = XmlDefaultFontSize;
            EnsureRoom(margin + fs + ls);
            AdvanceY(margin);
        }

        /// <summary>First baseline of a Standard-14 flow paragraph in the generator
        /// line model: the line box hangs the pitch (the bare font size without a
        /// caller leading) below the band top and the baseline sits the face's AFM
        /// descent above the box bottom — a 10 pt Helvetica line under a 770 band
        /// top seats on 762.07, Times on 762.17, 12 pt Courier on 759.88. A link-box
        /// seated state (HTML dialect) keeps its cap-height placement.</summary>
        private double PlainFirstBaseline(Text.TextState state, string baseFont, double fontSize,
            double lineHeight)
        {
            if (state.LineBoxSeat)
            {
                var cap = Text.Standard14Fonts.GetCapHeight(baseFont);
                return _curY - (cap > 0 ? cap / 1000.0 * fontSize : fontSize * 0.7);
            }
            return FirstBaselineSeat(state, fontSize, lineHeight) + DescentNorm(baseFont) * fontSize;
        }

        /// <summary>Queue the fill of a block's CSS background box: a band across the
        /// write region's full width from <paramref name="top"/> on
        /// <paramref name="startSlot"/> down to <paramref name="bottom"/> on the slot the
        /// flow is on now. A box taller than the page continues at the next page's top
        /// margin and ends at its bottom one, the way a browser prints it.</summary>
        public void QueueBandFill(int startSlot, double top, double bottom, Color color)
        {
            var left = AnchorLeft ?? _marginLeft;
            var right = _startPage.Width - _marginRight;
            if (right - left <= 0) return;
            var pageTop = _startPageHeight - _marginTop;
            for (var slot = startSlot; slot <= _currentSlot; slot++)
            {
                var t = slot == startSlot ? top : pageTop;
                var b = slot == _currentSlot ? bottom : _marginBottom;
                if (t - b <= 0) continue;
                _pendingBackFills.Add((slot, new Rectangle(left, b, right, t), color));
            }
        }

        /// <summary>Queue the fill of the document body's own background: the whole
        /// content box of every page the flow has run over since
        /// <paramref name="startSlot"/>. The body box IS the printed page's content
        /// area, so the fill does not stop where the last block ends.</summary>
        public void QueueBodyBackground(int startSlot, Color color)
        {
            var left = AnchorLeft ?? _marginLeft;
            var right = _startPage.Width - _marginRight;
            if (right - left <= 0) return;
            var pageTop = _startPageHeight - _marginTop;
            // Under everything else queued: the body box is the bottom of the paint
            // order, and the block bands queued while it flowed sit on top of it.
            for (var slot = _currentSlot; slot >= startSlot; slot--)
                _pendingBackFills.Insert(0,
                    (slot, new Rectangle(left, _marginBottom, right, pageTop), color));
        }

        /// <summary>Put vector content BEFORE everything already on the page a given slot
        /// will become — the mirror of <see cref="AddContentToSlot"/>, for paint that
        /// belongs under the content rather than over it.</summary>
        private void PrependContentToSlot(int slot, byte[] content)
        {
            if (content is null || content.Length == 0) return;
            if (_overflowBuffer is not null && slot == _currentSlot)
            { _overflowBuffer.Insert(0, content); return; }
            if (slot < 0) { _startPage.PrependContentStream(content); return; }
            if (slot >= _overflowPages.Count) return;
            var (existing, w, h) = _overflowPages[slot];
            var merged = new byte[existing.Length + content.Length];
            Buffer.BlockCopy(content, 0, merged, 0, content.Length);
            Buffer.BlockCopy(existing, 0, merged, content.Length, existing.Length);
            _overflowPages[slot] = (merged, w, h);
        }

        /// <summary>Room the next block needs before it may start on this page: its whole
        /// height when it is shorter than the orphan floor, else that floor.</summary>
        private double OrphanRoom(double lineHeight, int lineCount)
            => lineHeight * Math.Max(1, Math.Min(Math.Max(MinLinesPerPage, 1), lineCount));

        /// <summary>Left edge (in points) of the region the next line writes
        /// into — the current column when in column mode, else the page's left
        /// content margin, plus any block indent.</summary>
        private double CurLeft => (_colLefts is not null ? _colLefts[_curCol] : AnchorLeft ?? _marginLeft) + LeftIndent;

        /// <summary>Usable width (in points) of the current write region — the
        /// current column's width in column mode, else the full content width,
        /// reduced by any block indent.</summary>
        internal double CurWidth => (_colWidths is not null
            ? _colWidths[_curCol]
            : _startPage.Width - (AnchorLeft ?? _marginLeft) - _marginRight) - LeftIndent;

        /// <summary>Absolute left edge the full-width flow writes from once a
        /// positioned paragraph (a Graph with an assigned Left) re-anchored it:
        /// everything that follows starts at that edge and keeps the page's right
        /// content margin. Null = the page's left content margin.</summary>
        public double? AnchorLeft { get; set; }

        private void FlowToNextRegion()
        {
            // A fresh column / page restarts body line placement from the top, so
            // the next line drops by its font size rather than chaining onto the
            // previous region's last baseline.
            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            if (_colLefts is not null && _curCol < _colLefts.Length - 1)
            {
                _curCol++;
                _curY = _colBandTop;
            }
            else
            {
                // A paragraph leaving this page mid-way has laid lines on it: the
                // page carries body down to the cursor (a note band on such a page
                // hangs under those lines; the paragraph's end records only its
                // last page).
                if (_curY < ContentTop - 0.5)
                    RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
                StartNewPage();
            }
        }

        /// <summary>Resume the flow on a spill page whose content is already built — e.g.
        /// the final page of a multi-page table. The supplied content seeds the next
        /// overflow slot's buffer and the cursor resumes at <paramref name="resumeY"/>, so
        /// following paragraphs append below it on the same page instead of opening a fresh
        /// one. Returns the slot index that page will occupy in the overflow queue.</summary>
        public int ContinueOnPrebuiltSpill(byte[] content, double resumeY)
        {
            // Flush any buffer in flight so it keeps its own slot before we seed a new one.
            if (_overflowBuffer is { Count: > 0 })
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
            _overflowBuffer = new List<byte[]> { content };
            _currentSlot = _overflowPages.Count; // slot this buffer will flush into
            (_marginTop, _marginBottom) = MarginsForSlot(_currentSlot);
            _curY = resumeY;
            if (_colLefts is not null)
            {
                _curCol = 0;
                _colBandTop = _curY;
                _colDeepestY = _curY;
            }
            return _currentSlot;
        }

        /// <summary>Break one wrapped line into word / single-space tokens and
        /// give each its x offset from the line start, full-justified to
        /// <paramref name="targetWidth"/>: the slack beyond the line's natural
        /// width is split equally over the interior spaces, and every token is
        /// shifted right by (spaces before it) x that share — so each stretch
        /// gap opens between a space glyph and the following word, space glyphs
        /// keep their natural width, and the last word's right edge lands on
        /// the target exactly. The line-break trailing space is dropped, and a
        /// line without interior spaces stays at its natural width.</summary>
        private static IEnumerable<(string token, double xOffset)> JustifyLineTokens(
            string line, Func<string, double> measure, double targetWidth)
        {
            var content = line.TrimEnd(' ');
            if (content.Length == 0) yield break;

            var tokens = new List<string>();
            var start = 0;
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i] != ' ') continue;
                if (i > start) tokens.Add(content.Substring(start, i - start));
                tokens.Add(" ");
                start = i + 1;
            }
            if (start < content.Length) tokens.Add(content.Substring(start));

            var widths = new double[tokens.Count];
            double naturalWidth = 0;
            var interiorSpaces = 0;
            for (var t = 0; t < tokens.Count; t++)
            {
                widths[t] = measure(tokens[t]);
                naturalWidth += widths[t];
                if (tokens[t] == " ") interiorSpaces++;
            }
            var extra = interiorSpaces > 0 && targetWidth > naturalWidth
                ? (targetWidth - naturalWidth) / interiorSpaces
                : 0.0;

            double cum = 0;
            var spacesBefore = 0;
            for (var t = 0; t < tokens.Count; t++)
            {
                yield return (tokens[t], cum + spacesBefore * extra);
                if (tokens[t] == " ") spacesBefore++;
                cum += widths[t];
            }
        }

        /// <summary>
        /// Write a TextFragment as wrapped, flowed text starting at the current Y
        /// cursor. Spans multiple pages via the overflow queue when needed. Returns
        /// false if the fragment is too complex for flow layout (embedded font or
        /// multi-segment) — caller should fall back to legacy fixed-position writer.
        /// </summary>
        /// <summary>True when every NON-EMPTY segment of <paramref name="tf"/> carries
        /// the same explicitly-touched font size — the shape whose segment size may be
        /// promoted to the fragment level.</summary>
        private static bool FragmentSegmentsShareOneTouchedSize(Text.TextFragment tf)
        {
            double size = -1;
            foreach (var s in tf.Segments)
            {
                if (string.IsNullOrEmpty(s.Text)) continue;
                if (!s.TextState.FontSizeTouched) return false;
                if (size < 0) size = s.TextState.FontSize;
                else if (Math.Abs(size - s.TextState.FontSize) > 0.01) return false;
            }
            return size > 0;
        }

        /// <summary>Vertical extent of a link annotation over one line of text:
        /// the drawing face's own ascent above the baseline and its descent below
        /// it (Arial at 12 pt: 10.86 above, 2.54 below). NOT the line box — the
        /// leading a caller asks for opens the pitch BETWEEN links and must not
        /// grow the clickable box, which is why a 1.5 pt leading leaves a 0.1 pt
        /// gap between the boxes of two consecutive lines.</summary>
        private static (double Above, double Below) LinkBoxExtent(Text.TextState? state,
            double fontSize)
        {
            var ttf = state?.FontData?.TtfData ?? state?.Font?.SourceFontData?.TtfData;
            if (ttf is { Length: > 12 } && Text.FontRepository.ReadTtfHheaExtent(ttf)
                    is { ascent: > 0 } hh)
                return (hh.ascent / 1000.0 * fontSize, hh.descent / 1000.0 * fontSize);
            if (state is not null)
            {
                var std = Text.TextBuilder.MapToStandard14Public(state);
                var sa = Text.Standard14Fonts.GetWrittenFaceAscent(std);
                var sd = Text.Standard14Fonts.GetWrittenFaceDescent(std);
                if (sa > 0) return (sa / 1000.0 * fontSize, -sd / 1000.0 * fontSize);
            }
            // Neither an embedded face nor a named Standard-14 one: the generic font
            // descriptor's own extents, the pair the TrueType reader itself falls
            // back to when a face's tables cannot be read.
            return (GenericAscentEm * fontSize, GenericDescentEm * fontSize);
        }

        private void StartNewPage(bool flushEmpty = false)
        {
            _lastBodyBaseline = null;
            // Flush the previous overflow page (if any) so each overflow-queue entry
            // corresponds to exactly one new Page — otherwise all overflow content
            // for this flow would collapse onto a single page. flushEmpty additionally
            // commits an empty buffer as a blank page (an explicit page break past a
            // paragraph that rendered nothing still occupies its own page); a null
            // buffer means we're still on the start page, so there's nothing to flush.
            if (_overflowBuffer is not null && (flushEmpty || _overflowBuffer.Count > 0))
            {
                _overflowPages.Add((ConcatBlocks(_overflowBuffer), _startPage.Width, _startPage.Height));
            }
            _overflowBuffer = new List<byte[]>();
            // The new buffer will flush into the next available slot index in
            // _overflowPages -- record now so pending links / paragraph positions
            // logged before the flush still resolve to the right Page later.
            _currentSlot = _overflowPages.Count;
            (_marginTop, _marginBottom) = MarginsForSlot(_currentSlot);
            _curY = _startPageHeight - _marginTop;
            // A fresh page restarts the column band at column 0 from the top.
            if (_colLefts is not null)
            {
                _curCol = 0;
                _colBandTop = _curY;
                _colDeepestY = _curY;
            }
        }

        /// <summary>Break a run list into lines of (x, token, run) cells at
        /// <paramref name="width"/>: words and single spaces are the tokens, a
        /// newline forces a break, a space that would overflow is dropped with the
        /// break, and a single token wider than the line is cut character by
        /// character.</summary>
        internal static List<List<(double x, string text, InlineRun run)>> LayoutInlineLines(
            List<InlineRun> runs, double width)
        {
            var lines = new List<List<(double, string, InlineRun)>>();
            var cells = new List<(double, string, InlineRun)>();
            double x = 0;
            void Break()
            {
                lines.Add(cells);
                cells = new List<(double, string, InlineRun)>();
                x = 0;
            }
            foreach (var r in runs)
            {
                if (r.ImageData is not null)
                {
                    if (cells.Count > 0 && x + r.ImageW > width) Break();
                    cells.Add((x, string.Empty, r));
                    x += r.ImageW;
                    continue;
                }
                var t = r.Text ?? string.Empty;
                var i = 0;
                while (i < t.Length)
                {
                    if (t[i] == '\r') { i++; continue; }
                    if (t[i] == '\n') { Break(); i++; continue; }
                    string tok;
                    if (t[i] == ' ') { tok = " "; i++; }
                    else
                    {
                        var j = i;
                        while (j < t.Length && t[j] != ' ' && t[j] != '\n' && t[j] != '\r') j++;
                        tok = t.Substring(i, j - i);
                        i = j;
                    }
                    var w = InlineMeasure(tok, r);
                    if (tok == " ")
                    {
                        if (cells.Count > 0 && x + w > width) { Break(); continue; }
                        cells.Add((x, tok, r));
                        x += w;
                        continue;
                    }
                    if (cells.Count > 0 && x + w > width) Break();
                    if (w > width)
                    {
                        var rest = tok;
                        while (rest.Length > 0)
                        {
                            var take = 0;
                            double pw = 0;
                            while (take < rest.Length)
                            {
                                var len = char.IsHighSurrogate(rest[take]) && take + 1 < rest.Length
                                          && char.IsLowSurrogate(rest[take + 1]) ? 2 : 1;
                                var cw = InlineMeasure(rest.Substring(take, len), r);
                                if (take > 0 && x + pw + cw > width) break;
                                pw += cw;
                                take += len;
                            }
                            cells.Add((x, rest.Substring(0, take), r));
                            x += pw;
                            rest = rest.Substring(take);
                            if (rest.Length > 0) Break();
                        }
                        continue;
                    }
                    cells.Add((x, tok, r));
                    x += w;
                }
            }
            if (cells.Count > 0 || lines.Count == 0) lines.Add(cells);
            return lines;
        }

    }
}
