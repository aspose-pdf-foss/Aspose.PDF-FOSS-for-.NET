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
            ResolveEmphasisFaces(tf, out var boldData, out var boldFont, out var italicData, out var italicFont);
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

            WriteEmphasisLines(tf, lines, fontSize, lineHeight);
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
            var fragFs = tf.TextState.FontSizeTouched && tf.TextState.FontSize > 0
                ? (double)tf.TextState.FontSize : XmlDefaultFontSize;
            // An authored-empty <TextFragment /> (no segment at all) takes no room;
            // a fragment whose segment is empty still stands one line tall.
            if (tf.XmlEmptyShell && tf.FootNote is null && tf.EndNote is null)
                return true;

            // Margin.Top/Bottom are consumed by the paragraph dispatcher around
            // this call; only the horizontal margins are applied here.
            var ln = new XmlModelLine
            {
                FragLeft = CurLeft + (tf.Margin?.Left ?? 0),
                RightEdge = _startPage.Width - _marginRight - (tf.Margin?.Right ?? 0),
                Leading = XmlModelLeading(tf),
                Align = tf.HorizontalAlignment,
            };
            ln.Pen = ln.FragLeft;

            foreach (var seg in tf.Segments)
                WriteXmlModelSegment(ln, tf, seg, fragFs);
            FlushXmlModelLine(ln);

            _lastBodyBaseline = null;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
        }

        /// <summary>The leading: the fragment's own, else the tallest its segments declare (a
        /// &lt;TextSegment&gt;&lt;TextState LineSpacing="3"/&gt; pitches the line at fs + 3).</summary>
        private static double XmlModelLeading(Text.TextFragment tf)
        {
            if (tf.TextState.LineSpacing > 0) return tf.TextState.LineSpacing;
            double leading = 0;
            if (tf.Segments is { Count: > 0 })
                foreach (var seg in tf.Segments)
                    if (seg.TextState.LineSpacing > leading) leading = seg.TextState.LineSpacing;
            return leading;
        }

        /// <summary>The line an XML-model fragment is building: the styled runs placed on it (x
        /// is absolute), the pen, the tallest size on the line, how many of the fragment's tab
        /// stops it has consumed, and the fragment geometry every line of it shares.</summary>
        private sealed class XmlModelLine
        {
            public readonly List<(double x, string text, Text.TextState st, string face, double fs)> Runs = new();
            public double Pen;
            public double MaxFs;
            public int TabIndex;
            public double FragLeft;
            public double RightEdge;
            public double Leading;
            public HorizontalAlignment Align;
        }

        /// <summary>Queues the line's runs and opens the next one. A line with NO glyph runs at
        /// all (a bare newline) is one schema-default line tall regardless of the fragment's
        /// size — the 12 pt headings' leading blank lines and the 20 pt title's are all exactly
        /// 10 pt (whitespace-BEARING lines instead take their runs' size, like any other
        /// line).</summary>
        private void FlushXmlModelLine(XmlModelLine ln)
        {
            var lineH = (ln.MaxFs > 0 ? ln.MaxFs : XmlDefaultFontSize) + ln.Leading;
            EnsureRoom(lineH);
            double shift = 0;
            if (ln.Runs.Count > 0
                && ln.Align is HorizontalAlignment.Center or HorizontalAlignment.Right)
            {
                var slack = ln.RightEdge - ln.Pen;
                if (slack > 0)
                    shift = ln.Align == HorizontalAlignment.Center ? slack / 2 : slack;
            }
            foreach (var r in ln.Runs)
            {
                var descent = Text.Standard14Fonts.GetDescent(r.face); // negative
                var seat = (1000 + descent) / 1000.0 * r.fs + ln.Leading;
                _pendingEmbeddedRenders.Add((_currentSlot, r.x + shift, _curY,
                    r.text, r.st, r.fs, _curY - seat));
                if (_overflowBuffer is not null)
                    _overflowBuffer.Add(Array.Empty<byte>());
            }
            _curY -= lineH;
            ln.Runs.Clear();
            ln.Pen = ln.FragLeft;
            ln.MaxFs = 0;
            ln.TabIndex = 0;
        }

        /// <summary>Two segment states draw identically when every property the run emission
        /// reads agrees — the heading prefix and its text, cloned from the same style, must merge
        /// into ONE show (the absorber reads a run's interior and trailing spaces verbatim, while
        /// a run break re-synthesises them from geometry and drops the trailing one).</summary>
        private static bool SameXmlRunStyle(Text.TextState a, Text.TextState b)
        {
            var ca = a.ForegroundColor;
            var cb = b.ForegroundColor;
            var colorsEqual = ca is null ? cb is null
                : cb is not null && ca.R == cb.R && ca.G == cb.G && ca.B == cb.B;
            return colorsEqual && a.Underline == b.Underline && a.IsStrikeOut == b.IsStrikeOut;
        }

        /// <summary>Places one run at the pen, coalescing contiguous same-style words into one
        /// show — the writer emits one run per styled piece per line.</summary>
        private static void AppendXmlModelRun(XmlModelLine ln, string text, Text.TextState st,
            string face, double fs)
        {
            if (text.Length == 0) return;
            if (ln.Runs.Count > 0)
            {
                var last = ln.Runs[^1];
                if (last.face == face && last.fs == fs && SameXmlRunStyle(last.st, st)
                    && Math.Abs(last.x + MeasureLineWidth(last.text, face, fs) - ln.Pen) < 0.005)
                {
                    ln.Runs[^1] = (last.x, last.text + text, st, face, fs);
                    ln.Pen += MeasureLineWidth(text, face, fs);
                    if (fs > ln.MaxFs) ln.MaxFs = fs;
                    return;
                }
            }
            ln.Runs.Add((ln.Pen, text, st, face, fs));
            ln.Pen += MeasureLineWidth(text, face, fs);
            if (fs > ln.MaxFs) ln.MaxFs = fs;
        }

        /// <summary>#$TAB: the pen to the next stop, measured from the fragment left — the
        /// fragment's own explicit stops first, then the default stops at multiples of
        /// <see cref="XmlDefaultTabStopSpaces"/> space-widths.</summary>
        private static void AdvanceXmlModelTab(XmlModelLine ln, Text.TextFragment tf,
            string face, double fs)
        {
            var rel = ln.Pen - ln.FragLeft;
            double target;
            if (tf.TabStops is { Count: > 0 } stops && ln.TabIndex < stops.Count)
                target = stops[ln.TabIndex++].Position;
            else
            {
                var interval = XmlDefaultTabStopSpaces
                    * Text.Standard14Fonts.GetWidth(face, ' ') / 1000.0 * fs;
                target = (Math.Floor(rel / interval + 1e-6) + 1) * interval;
            }
            if (target > rel) ln.Pen = ln.FragLeft + target;
            if (fs > ln.MaxFs) ln.MaxFs = fs;
        }

        /// <summary>Lays one segment out: its tab-separated pieces, their newline-separated
        /// lines, and each line wrapped word by word against the fragment's right edge.</summary>
        private void WriteXmlModelSegment(XmlModelLine ln, Text.TextFragment tf,
            Text.TextSegment seg, double fragFs)
        {
            var st = seg.TextState ?? tf.TextState;
            var fs = st.FontSizeTouched && st.FontSize > 0 ? (double)st.FontSize : fragFs;
            var face = Text.TextBuilder.MapToStandard14Public(st);
            var text = (seg.Text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
            if (text.Length == 0) return;

            var pieces = text.Split(new[] { Text.TextBuilder.TabMarker }, StringSplitOptions.None);
            for (var pi = 0; pi < pieces.Length; pi++)
            {
                if (pi > 0) AdvanceXmlModelTab(ln, tf, face, fs);
                var nlSplit = pieces[pi].Split('\n');
                for (var li = 0; li < nlSplit.Length; li++)
                {
                    if (li > 0) FlushXmlModelLine(ln);
                    // Wrap the part word-by-word; a word keeps its trailing
                    // spaces (they render, and the pen advances past them —
                    // the fit test ignores them).
                    foreach (var word in SplitXmlWords(nlSplit[li]))
                    {
                        if (ln.Runs.Count > 0 || ln.Pen > ln.FragLeft + 0.01)
                        {
                            var bareW = MeasureLineWidth(word.TrimEnd(' '), face, fs);
                            if (ln.Pen + bareW > ln.RightEdge + 0.01) FlushXmlModelLine(ln);
                        }
                        AppendXmlModelRun(ln, word, st, face, fs);
                    }
                }
            }
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
    }
}
