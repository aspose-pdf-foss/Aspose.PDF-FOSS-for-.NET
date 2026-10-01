using System.Text;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private sealed partial class FlowLayout
    {
        /// <summary>One run of a segmented paragraph: a segment's text drawn in its own
        /// face, size and colour, and the line box that segment brings to any line it
        /// stands on.</summary>
        private readonly struct SegmentRun
        {
            public readonly string Text;
            public readonly string BaseFont;
            public readonly double FontSize;
            public readonly double LineBox;
            public readonly double AscentEm;
            public readonly double DescentEm;
            public readonly Color? Color;
            public readonly double CharSpacing;
            public readonly double WordSpacing;
            public readonly bool Underline;
            public readonly bool Strike;
            /// <summary>The run's own text state, for the stroke a rendering mode or
            /// synthetic weight paints with and the shear of a synthetic slant.</summary>
            public readonly Text.TextState State;
            /// <summary>What the run occupies past its glyphs, and what its rule spans.</summary>
            public readonly double Extra;
            public readonly double RuleExtra;
            /// <summary>The run's rise and the factor its horizontal scaling applies.</summary>
            public readonly double Rise;
            public readonly double Scale;
            /// <summary>A picture run: the picture's bytes and box. It stands on the
            /// baseline, as wide as its box, and its text is the mark that makes it a
            /// word of its own.</summary>
            public readonly byte[]? Picture;
            public readonly double PictureWidth;
            public readonly double PictureHeight;
            /// <summary>The segment's own text state, whatever the run paints with: what it
            /// declares for itself alone, such as a background behind its text.</summary>
            public readonly Text.TextState OwnState;
            /// <summary>The bands of the border round the run's box (left, bottom, right, top).</summary>
            public readonly (double Left, double Bottom, double Right, double Top) Bands;
            /// <summary>The run is drawn in a face program of its own (an embedded face):
            /// measured with that face and written by the deferred embedded writer.</summary>
            public readonly bool Embedded;
            /// <summary>A tab run (<see cref="Text.TextSegment.IsTab"/>): it draws nothing, takes
            /// the room its line gives it and draws its stop's leader across that room.</summary>
            public readonly bool Tab;

            public SegmentRun(string text, string baseFont, double fontSize, double lineBox,
                double ascentEm, double descentEm, Color? color, double charSpacing, double wordSpacing,
                bool underline, bool strike, Text.TextState state, double extra, double ruleExtra,
                double rise, double scale, byte[]? picture = null, double pictureWidth = 0, double pictureHeight = 0,
                Text.TextState? ownState = null, bool tab = false)
            {
                Tab = tab;
                OwnState = ownState ?? state;
                Bands = OwnState.FormattingOptions?.RunBorder is { } border ? BorderBands(border) : (0, 0, 0, 0);
                Embedded = picture is null && !tab && HasFaceProgram(OwnState);
                Extra = extra + Bands.Left + Bands.Right;
                Picture = picture;
                PictureWidth = pictureWidth;
                PictureHeight = pictureHeight;
                Text = text;
                BaseFont = baseFont;
                FontSize = fontSize;
                LineBox = lineBox;
                AscentEm = ascentEm;
                DescentEm = descentEm;
                Color = color;
                CharSpacing = charSpacing;
                WordSpacing = wordSpacing;
                Underline = underline;
                Strike = strike;
                State = state;
                RuleExtra = ruleExtra;
                Rise = rise;
                Scale = scale;
            }
        }

        /// <summary>Writes a paragraph whose segments FLOW AS RUNS
        /// (<see cref="Text.TextFormattingOptions.SegmentsFlowAsRuns"/>): the text
        /// wraps at spaces across the segments, a word split between two segments
        /// staying one word, and every line is drawn run by run, each run in its
        /// segment's own face, size and colour, advancing by its own measured width.
        ///
        /// A line is as tall as the TALLEST line box among the runs it holds, and
        /// seats its baseline the way a declared line box does -- half the surplus
        /// leading plus the ascent, both of the run with the largest size -- so a
        /// small run beside a large one shares the large one's baseline and the
        /// line pitches on the large one. That is how a CSS line box and every
        /// typesetter treats a line of mixed sizes; a fragment that declared no
        /// line box drops its baseline by the largest size instead.
        ///
        /// A segment carrying a picture (<see cref="Text.TextSegment.InlineImage"/>)
        /// is a word of its own, as wide as the picture's box; the picture stands ON
        /// the baseline and raises the line's ascent to its height when it is taller
        /// than the text's, the descent staying the text's.
        ///
        /// Faces are registered on the start page (Helvetica first, so its name is
        /// the one continuation pages pre-register) and keep their names on every
        /// page the paragraph runs onto. A run in an embedded face is measured with
        /// that face and written by the deferred embedded writer, so it draws plainly
        /// (<see cref="DrawnPlainly"/>). Null when the shape is not one this writer
        /// draws -- an embedded face asking for more, a newline, a decoration or a
        /// link -- and the fragment takes the flow's other paths.</summary>
        private bool? TryWriteSegmentedRuns(Text.TextFragment tf)
        {
            if (CollectSegmentRuns(tf) is not { Count: > 0 } runs) return null;
            var lines = WrapSegmentRuns(runs, CurWidth,
                tf.TextState.FormattingOptions?.FirstLineIndent ?? 0, tf.TextState.FormattingOptions);
            if (lines.Count == 0) return true;

            var resources = new Dictionary<string, string>(StringComparer.Ordinal);
            Table.RegisterFont(_startPage);
            var declaresBox = DeclaresLineBox(tf.TextState);
            // Embedded text is written after the flow drains; once any is, the plain text
            // after it waits too, so the stream keeps paragraph order.
            var embedded = runs.Exists(r => r.Embedded);
            var deferText = embedded || _forceDeferredWrites;
            var multiplier = tf.TextState.FormattingOptions?.RunLineBoxMultiplier;
            var seats = new (double Box, double Ascent)[lines.Count];
            for (var li = 0; li < lines.Count; li++) seats[li] = LineSeat(runs, lines[li], declaresBox, firstLine: li == 0, multiplier);
            var boxes = Array.ConvertAll(seats, seat => seat.Box);
            EnsureRoom(OrphanRoom(seats[0].Box, lines.Count) + (lines.Count == 1 ? BottomReserve : 0));
            var controlBreak = MoveWholeIfControlsAsk(tf, boxes);
            for (var li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                var (box, ascent) = seats[li];
                if (li > 0)
                {
                    var slotBefore = _currentSlot;
                    // The last line leaves room for the margin the paragraph keeps under it.
                    if (controlBreak == li) FlowToNextRegion();
                    else EnsureRoom(box + (li == lines.Count - 1 ? BottomReserve : 0));
                    if (_currentSlot != slotBefore)
                    {
                        if (tf.TextState.FormattingOptions is { TopMarginAfterBreak: true }) AdvanceY(tf.Margin?.Top ?? 0);
                        controlBreak = LineControlBreak(tf, boxes, li, mayMoveWhole: false);
                    }
                }
                var baseline = _curY - ascent;
                if (li == 0) CaptureFirstLine(baseline);
                var indent = li == 0 ? tf.TextState.FormattingOptions?.FirstLineIndent ?? 0 : 0;
                var x = CurLeft + indent + SegmentLineAlignOffset(tf, runs, line);
                var spacing = deferText ? default : RunJustifying(tf, runs, line, li == lines.Count - 1, indent);
                WriteSegmentLine(runs, line, x, baseline, resources, tf.TextState, deferText, spacing);
                _curY -= box;
                _lastTextLinePitch = box;
            }
            _lastBodyBaseline = null;
            if (embedded) _forceDeferredWrites = true;
            _colDeepestY = Math.Min(_colDeepestY, _curY);
            RecordSlotBottom(_colLefts is not null ? _colDeepestY : _curY);
            return true;
        }

        /// <summary>Moves the whole paragraph to the next region when its line controls ask it
        /// to (too few of its first lines would stand here); answers the line before which they
        /// break the region it then stands in, null for none.</summary>
        private int? MoveWholeIfControlsAsk(Text.TextFragment tf, double[] boxes)
        {
            var controlBreak = LineControlBreak(tf, boxes, 0, !NearRegionTop(tf));
            if (controlBreak != 0) return controlBreak;
            // Nothing of the paragraph stays behind: a box around it closes above its top margin.
            if (InsideBox) _partBottom = _curY + (tf.Margin?.Top ?? 0);
            FlowToNextRegion();
            if (tf.TextState.FormattingOptions is { TopMarginAfterBreak: true }) AdvanceY(tf.Margin?.Top ?? 0);
            return LineControlBreak(tf, boxes, 0, mayMoveWhole: false);
        }

        /// <summary>The runs a fragment's segments make, or null when one of them
        /// asks for something this writer does not draw.</summary>
        private static List<SegmentRun>? CollectSegmentRuns(Text.TextFragment tf)
        {
            var runs = new List<SegmentRun>();
            var fragState = tf.TextState;
            foreach (var seg in tf.Segments)
            {
                if (seg.IsTab)
                {
                    runs.Add(TabRun(fragState));
                    continue;
                }
                var text = seg.Text ?? string.Empty;
                var picture = seg.InlineImage;
                if (picture is null && text.Length == 0) continue;
                if (picture is null && (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)) return null;
                if (seg.Hyperlink is not null || seg.Position is not null) return null;
                byte[]? pictureBytes = null;
                if (picture is not null)
                {
                    if (picture.FixWidth <= 0 || picture.FixHeight <= 0) return null;
                    pictureBytes = InlinePictureBytes(picture);
                    if (pictureBytes is null) return null;
                    text = PictureMark;
                }
                var st = seg.TextState;
                if ((st.FontData is not null || st.Font?.SourceFontData is not null)
                    && (picture is not null || !HasFaceProgram(st) || !DrawnPlainly(st) || !DrawnPlainly(fragState)))
                    return null;
                var fs = st.FontSizeTouched && st.FontSize > 0 ? (double)st.FontSize
                    : fragState.FontSize > 0 ? fragState.FontSize : (double)st.FontSize;
                if (fs <= 0) fs = 12;
                var spacing = st.LineSpacing > 0 ? st.LineSpacing : fragState.LineSpacing;
                var (ascentEm, descentEm) = st is { LineBoxAscentEm: { } a, LineBoxDescentEm: { } d } ? (a, d)
                    : fragState is { LineBoxAscentEm: { } fa, LineBoxDescentEm: { } fd } ? (fa, fd)
                    : (GenericAscentEm, GenericDescentEm);
                // The run paints with its own stroke and shear when it declares any,
                // else with the paragraph's.
                var paintState = st.RenderingMode != 0 || st.ExplicitStrokingColor is not null
                    || st.FormattingOptions is { SyntheticBoldPen: > 0 } or { Skew: not 0 } or { Slope: not 0 } or { SyntheticItalicLean: not 0 }
                    ? st : fragState;
                var rise = st.TextRise != 0 ? st.TextRise : fragState.TextRise;
                var scale = ScaleOf(st.HorizontalScalingTouched ? st : fragState);
                runs.Add(new SegmentRun(text, Text.TextBuilder.MapToStandard14Public(st), fs,
                    fs + Math.Max(0, spacing), ascentEm, descentEm,
                    st.ForegroundColor ?? fragState.ForegroundColor,
                    st.CharacterSpacing != 0 ? st.CharacterSpacing : fragState.CharacterSpacing,
                    st.WordSpacing != 0 ? st.WordSpacing : fragState.WordSpacing,
                    st.IsUnderline || fragState.IsUnderline, st.IsStrikeOut || fragState.IsStrikeOut,
                    paintState, OccupiedExtra(paintState.FormattingOptions, fs), RuleExtra(paintState.FormattingOptions, fs),
                    rise, scale, pictureBytes, picture?.FixWidth ?? 0, picture?.FixHeight ?? 0, st));
            }
            return runs;
        }

        /// <summary>A tab run: the paragraph's own size and line box, which count on a line
        /// holding nothing but tabs.</summary>
        private static SegmentRun TabRun(Text.TextState fragState)
        {
            var fs = fragState.FontSize > 0 ? (double)fragState.FontSize : 12;
            var (ascentEm, descentEm) = fragState is { LineBoxAscentEm: { } a, LineBoxDescentEm: { } d } ? (a, d)
                : (GenericAscentEm, GenericDescentEm);
            return new SegmentRun(TabMark, Text.TextBuilder.MapToStandard14Public(fragState), fs,
                fs + Math.Max(0, fragState.LineSpacing), ascentEm, descentEm, null, 0, 0, false, false,
                fragState, 0, 0, 0, 1, tab: true);
        }

        /// <summary>The text a tab run stands in for.</summary>
        private const string TabMark = "\t";

        /// <summary>Whether a state draws its text as the deferred embedded writer carries a
        /// run: its face, size and colour, a stroke, a synthetic weight or a shear, but no
        /// spacing, rise, scaling or rule, which this writer measures and draws itself.</summary>
        private static bool DrawnPlainly(Text.TextState st) =>
            st.CharacterSpacing == 0 && st.WordSpacing == 0 && st.TextRise == 0
            && (!st.HorizontalScalingTouched || Math.Abs(ScaleOf(st) - 1) < 1e-9)
            && !st.IsUnderline && !st.IsStrikeOut;

        /// <summary>The text a picture run stands in for: one non-space character, so
        /// the wrapper treats the picture as a word of its own.</summary>
        private const string PictureMark = "\uFFFC";

        /// <summary>The bytes of an inline picture, read from its stream without
        /// disturbing the stream's position; null when it holds none.</summary>
        private static byte[]? InlinePictureBytes(Image picture)
        {
            if (picture.ImageStream is null) return null;
            var stream = picture.ImageStream;
            var pos = stream.CanSeek ? stream.Position : -1L;
            if (stream.CanSeek) stream.Position = 0;
            using var mem = new System.IO.MemoryStream();
            stream.CopyTo(mem);
            if (pos >= 0) stream.Position = pos;
            return mem.Length > 0 ? mem.ToArray() : null;
        }

        /// <summary>Draws an inline picture on the region the flow is in: on the start
        /// page directly, else queued against the current slot and bound when that
        /// slot becomes a page.</summary>
        private void PlaceInlinePicture(byte[] data, Rectangle rect)
        {
            if (_overflowBuffer is null) { _startPage.AddImage(data, rect); return; }
            _pendingImages.Add((_currentSlot, data, rect, false));
        }

        /// <summary>The runs wrapped by the shared word wrapper
        /// (<see cref="Text.RunWordWrap"/>): a word straddling a run boundary stays
        /// one word, and a run's extra is charged once per line it appears on.</summary>
        private static List<List<Text.RunPiece>> WrapSegmentRuns(List<SegmentRun> runs, double width, double firstLineIndent,
            Text.TextFormattingOptions? options)
        {
            var texts = new string[runs.Count];
            for (var i = 0; i < runs.Count; i++) texts[i] = runs[i].Text;
            var rules = runs.Exists(r => r.Tab)
                ? new Text.RunWrapRules
                {
                    TabRuns = ri => runs[ri].Tab,
                    TabStops = options?.RunTabStops,
                    TabInterval = options?.RunTabInterval ?? Text.RunWrapRules.DefaultTabInterval,
                }
                : null;
            var words = Text.RunWordWrap.SplitIntoWords(texts, (ri, chunk) => MeasureRunWidth(runs[ri], chunk), rules);
            return Text.RunWordWrap.Wrap(words, width, firstLineIndent, ri => runs[ri].Extra, rules,
                (Func<Text.RunPiece, double>)(piece => MeasureRunWidth(runs[piece.Run], piece.Text)));
        }

        /// <summary>The room a piece of a line takes: a tab's own, else its text's.</summary>
        private static double PieceWidth(SegmentRun run, Text.RunPiece piece, string text) =>
            piece.TabAdvance ?? MeasureRunWidth(run, text);

        /// <summary>The codes a run's text goes out as: a Latin face's text in its WinAnsi
        /// codes (a bullet is 0x95, measured as the bullet it draws rather than as an
        /// unknown character), a symbolic face's as it stands.</summary>
        private static string AsWrittenCodes(string text, string baseFont)
        {
            if (Table.IsSymbolicStandardFace(baseFont)) return text;
            var codes = text.ToCharArray();
            for (var i = 0; i < codes.Length; i++) codes[i] = Content.ContentStreamBuilder.ToWinAnsi(codes[i]);
            return new string(codes);
        }

        private static double MeasureRunWidth(SegmentRun run, string text)
        {
            if (run.Picture is not null) return text.Length == 0 ? 0 : run.PictureWidth;
            if (run.Embedded)
                return Text.TextPaginator.CreateMeasurer(run.BaseFont, run.FontSize,
                    run.OwnState.FontData ?? run.OwnState.Font?.SourceFontData)(text);
            var w = MeasureLineWidth(AsWrittenCodes(text, run.BaseFont), run.BaseFont, run.FontSize) + text.Length * run.CharSpacing;
            if (run.WordSpacing != 0)
                foreach (var c in text) if (c == ' ') w += run.WordSpacing;
            return w * run.Scale;
        }

        /// <summary>The line's box and how far below its top the baseline sits. The
        /// text runs give both: the tallest box among them, seated as a declared line
        /// box seats (half the surplus leading, then the top band and the ascent, of the
        /// run with the tallest line box - the larger text of two alike - its box grown by
        /// its border's bands) or dropped by that size when none was declared. A picture
        /// stands on the baseline and raises the ascent to its height when it is
        /// taller, the descent staying the text's; a line of pictures alone is
        /// exactly the tallest of them, with no descent under it. On every line
        /// after the paragraph's first a line holding a picture drops its whole box
        /// by the text's em descent, whatever the picture's height (probed at 8, 12,
        /// 18 and 24 pt: a 22.5 pt picture on a wrapped 12 pt line seats 22.5 + 2.98
        /// under the line above, on the first line 22.5). Given a
        /// <see cref="Text.TextFormattingOptions.RunLineBoxMultiplier"/>, a declared box
        /// spans the highest ascent and deepest descent among the text runs instead,
        /// that many times over.</summary>
        private static (double Box, double Ascent) LineSeat(List<SegmentRun> runs, List<Text.RunPiece> line, bool declaresBox,
            bool firstLine, double? multiplier = null)
        {
            var box = 0.0;
            var tallest = -1;
            var onlyTabs = line.TrueForAll(p => runs[p.Run].Tab || runs[p.Run].Picture is not null);
            foreach (var piece in line)
            {
                var run = runs[piece.Run];
                if (run.Picture is not null || (run.Tab && !onlyTabs)) continue;
                box = Math.Max(box, run.LineBox);
                if (tallest < 0 || SeatsOver(run, runs[tallest])) tallest = piece.Run;
            }
            var tallestPicture = 0.0;
            var textRun = -1;
            foreach (var piece in line)
            {
                var run = runs[piece.Run];
                if (run.Picture is not null) tallestPicture = Math.Max(tallestPicture, run.PictureHeight);
                else if (run.Tab && !onlyTabs) continue;
                else if (textRun < 0 || run.FontSize > runs[textRun].FontSize) textRun = piece.Run;
            }
            // (the drop of a later line's box under a picture: the text's em descent,
            // the paragraph's when the line holds no text)
            var drop = firstLine ? 0 : (textRun >= 0 ? runs[textRun] : runs[line[0].Run]) is { } dropRun && dropRun.Picture is null
                ? dropRun.DescentEm * dropRun.FontSize
                : runs[line[0].Run].DescentEm * runs[line[0].Run].FontSize;
            // A line of pictures alone: the tallest picture, standing on the line's bottom.
            if (tallest < 0) return (tallestPicture + drop, tallestPicture + drop);
            var seat = runs[tallest];
            if (declaresBox && multiplier is { } m)
            {
                var (above, below) = RunExtents(runs, line, onlyTabs);
                var spanned = m * (above + below);
                var spannedAscent = (spanned - above - below) / 2 + above;
                var spannedDescent = spanned - spannedAscent;
                if (tallestPicture > 0) spannedAscent = Math.Max(spannedAscent, tallestPicture) + drop;
                return (spannedAscent + spannedDescent, spannedAscent);
            }
            // A bordered run's box is grown by its bands: the leading is shared round the
            // grown box, and the text sits under the top band.
            var halfLeading = declaresBox
                ? (box - (seat.AscentEm + seat.DescentEm) * seat.FontSize - seat.Bands.Top - seat.Bands.Bottom) / 2
                : 0;
            var ascent = declaresBox ? halfLeading + seat.Bands.Top + seat.AscentEm * seat.FontSize : seat.FontSize;
            var descent = box - ascent;
            if (tallestPicture > 0) ascent = Math.Max(ascent, tallestPicture) + drop;
            return (ascent + descent, ascent);
        }

        /// <summary>The highest ascent and deepest descent among a line's text runs, each
        /// grown by its border's band on that side.</summary>
        private static (double Above, double Below) RunExtents(List<SegmentRun> runs, List<Text.RunPiece> line, bool onlyTabs)
        {
            double above = 0, below = 0;
            foreach (var piece in line)
            {
                var run = runs[piece.Run];
                if (run.Picture is not null || (run.Tab && !onlyTabs)) continue;
                above = Math.Max(above, run.AscentEm * run.FontSize + run.Bands.Top);
                below = Math.Max(below, run.DescentEm * run.FontSize + run.Bands.Bottom);
            }
            return (above, below);
        }

        /// <summary>Whether <paramref name="run"/> seats a line rather than <paramref name="seat"/>:
        /// the run with the taller line box seats it, and of two alike the larger text.</summary>
        private static bool SeatsOver(SegmentRun run, SegmentRun seat) =>
            run.LineBox > seat.LineBox + 1e-9
            || (Math.Abs(run.LineBox - seat.LineBox) <= 1e-9 && run.FontSize > seat.FontSize);

        private double SegmentLineAlignOffset(Text.TextFragment tf, List<SegmentRun> runs, List<Text.RunPiece> line)
        {
            var mode = LineAlignment(tf);
            if (mode is not (HorizontalAlignment.Center or HorizontalAlignment.Right)) return 0;
            var slack = CurWidth - RunLineWidth(runs, line);
            if (slack <= 0) return 0;
            return mode == HorizontalAlignment.Center ? slack / 2 : slack;
        }

        /// <summary>The alignment a paragraph's lines are laid at: the paragraph's own when it
        /// centres, right-aligns or justifies, else its text state's.</summary>
        private static HorizontalAlignment LineAlignment(Text.TextFragment tf) =>
            tf.HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right or HorizontalAlignment.Justify
                ? tf.HorizontalAlignment
                : tf.TextState.HorizontalAlignment;

        /// <summary>A line's width as laid: its pieces' advances (a tab's room, a picture's box),
        /// each run's extra once, the spaces after its last piece left out.</summary>
        private static double RunLineWidth(List<SegmentRun> runs, List<Text.RunPiece> line)
        {
            var width = 0.0;
            var seen = new HashSet<int>();
            for (var i = 0; i < line.Count; i++)
            {
                var text = i == line.Count - 1 ? line[i].Text.TrimEnd(' ') : line[i].Text;
                width += PieceWidth(runs[line[i].Run], line[i], text);
                if (seen.Add(line[i].Run)) width += runs[line[i].Run].Extra;
            }
            return width;
        }

        /// <summary>The character and word spacing a justified line of runs adds to every run's
        /// own: the line's slack shared by the fragment's spacing ratio
        /// (<see cref="Text.TextFormattingOptions.JustifySpacingRatio"/>) over the gaps between its
        /// glyphs and its spaces, the glyphs of every text run counted, the spaces after its last
        /// piece left out; a tab or a picture keeps its room and moves with the text before it.
        /// The last line keeps its spacing unless asked to stretch too
        /// (<see cref="Text.TextFormattingOptions.JustifyLastLine"/>).</summary>
        private (double Tc, double Tw) RunJustifying(Text.TextFragment tf, List<SegmentRun> runs, List<Text.RunPiece> line,
            bool lastLine, double indent)
        {
            var options = tf.TextState.FormattingOptions;
            if (options?.JustifySpacingRatio is not { } ratio || LineAlignment(tf) != HorizontalAlignment.Justify
                || (lastLine && !options.JustifyLastLine)) return default;
            int glyphs = 0, spaces = 0;
            for (var i = 0; i < line.Count; i++)
            {
                var run = runs[line[i].Run];
                if (run.Tab || run.Picture is not null) continue;
                var text = i == line.Count - 1 ? line[i].Text.TrimEnd(' ') : line[i].Text;
                glyphs += text.Length;
                foreach (var c in text) if (c == ' ') spaces++;
            }
            var slack = CurWidth - indent - RunLineWidth(runs, line);
            var share = (1 - ratio) * (glyphs - 1) + ratio * spaces;
            if (slack <= 0 || share <= 0) return default;
            var unit = slack / share;
            return ((1 - ratio) * unit, ratio * unit);
        }

        /// <summary>One line, run by run: consecutive pieces of one run go out as one
        /// show; each show sets its own face, size, colour and spacing (a justified line's
        /// added to it), and a decorated run's rule follows its show, as wide as the show.</summary>
        private void WriteSegmentLine(List<SegmentRun> runs, List<Text.RunPiece> line, double x, double baseline,
            Dictionary<string, string> resources, Text.TextState fragState, bool deferText, (double Tc, double Tw) spacing) =>
            WriteContent(BuildSegmentLine(runs, line, x, baseline, resources, fragState, null, deferText, spacing), fragState);

        /// <summary>The content of one line of runs, built rather than written: a picture among
        /// them is placed on the page <paramref name="pictureSlot"/> names, or on the page the
        /// flow is writing when that is null. A run in an embedded face, and a plain run
        /// when <paramref name="deferText"/> asks, is queued for the deferred writer
        /// instead (<see cref="DeferredRun"/>).</summary>
        private byte[] BuildSegmentLine(List<SegmentRun> runs, List<Text.RunPiece> line, double x, double baseline,
            Dictionary<string, string> resources, Text.TextState fragState, int? pictureSlot, bool deferText = false,
            (double Tc, double Tw) spacing = default)
        {
            var b = new Content.ContentStreamBuilder();
            b.SaveState();
            var options = fragState.FormattingOptions;
            var alphaGs = options?.Opacity is { } opacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, opacity, strokeToo: true)
                : options?.FillOpacity is { } fillOpacity
                ? Text.TextParagraph.EnsureAlphaExtGState(_startPage, fillOpacity, strokeToo: false)
                : null;
            if (alphaGs is not null) b.SetExtGState(alphaGs);
            var ruleAlpha = RuleAlphaStates(options);
            var i = 0;
            while (i < line.Count)
            {
                var runIndex = line[i].Run;
                var show = new StringBuilder(line[i].Text);
                var j = i + 1;
                while (j < line.Count && line[j].Run == runIndex) show.Append(line[j++].Text);
                var run = runs[runIndex];
                var text = show.ToString();
                if (run.Tab)
                {
                    var advance = line[i].TabAdvance ?? 0;
                    if (line[i].Stop?.LeaderRule is { } leader && advance > 0) AppendLeader(b, leader, x, baseline, advance);
                    x += advance;
                    i++;
                    continue;
                }
                if (run.Picture is not null)
                {
                    // The picture stands on the baseline, as wide and tall as its box.
                    var box = new Rectangle(x, baseline, x + run.PictureWidth, baseline + run.PictureHeight);
                    if (pictureSlot is { } slot) PlacePictureOnSlot(slot, run.Picture, box);
                    else PlaceInlinePicture(run.Picture, box);
                    x += run.PictureWidth + run.Extra;
                    i = j;
                    continue;
                }
                if (run.Embedded || (deferText && IsPlainRun(run)))
                {
                    var advance = MeasureRunWidth(run, text);
                    PaintRunBox(b, run, x, baseline, j == line.Count ? MeasureRunWidth(run, text.TrimEnd(' ')) : advance);
                    var (state, seat) = DeferredRun(run, baseline);
                    _pendingEmbeddedRenders.Add((pictureSlot ?? _currentSlot, x + run.Bands.Left, baseline, text,
                        state, run.FontSize, seat));
                    x += advance + run.Extra;
                    i = j;
                    continue;
                }
                if (!resources.TryGetValue(run.BaseFont, out var res))
                    resources[run.BaseFont] = res = Table.RegisterFont(_startPage, run.BaseFont);
                var width = MeasureRunWidth(run, text) + JustifiedExtra(text, spacing);
                // Each run in its own graphics state: a stroke mode or pen set for
                // one run outlives its text object and would paint the next run too.
                b.SaveState();
                // The box of the line's last piece stops at its last glyph: the space a
                // wrapped line keeps hangs past it.
                PaintRunBox(b, run, x, baseline, j == line.Count ? MeasureRunWidth(run, text.TrimEnd(' ')) : width);
                var textX = x + run.Bands.Left;
                b.BeginText().SetFont(res, run.FontSize);
                if (run.Color is { } c) b.SetFillColor(c.R / 255.0, c.G / 255.0, c.B / 255.0);
                else b.SetFillColor(0, 0, 0);
                if (run.CharSpacing + spacing.Tc != 0) b.SetCharSpacing(run.CharSpacing + spacing.Tc);
                if (run.WordSpacing + spacing.Tw != 0) b.SetWordSpacing(run.WordSpacing + spacing.Tw);
                if (ShearOf(run.State) is var (slope, skew) && (slope != 0 || skew != 0)) b.SetTextMatrix(1, slope, skew, 1, textX, baseline);
                else b.MoveTextPosition(textX, baseline);
                AppendStrokeState(b, run.State);
                if (run.Rise != 0) b.SetTextRise(run.Rise);
                if (Math.Abs(run.Scale - 1) > 1e-9) b.SetHorizontalScaling(run.Scale * 100);
                b.ShowText(text).EndText();
                if (run.Underline || run.Strike)
                    AppendRunDecorations(b, run, textX, baseline, width + run.RuleExtra, options, ruleAlpha);
                b.RestoreState();
                x += width + run.Extra;
                i = j;
            }
            b.RestoreState();
            return b.Build();
        }

        /// <summary>A tab's leader: its stroke along the baseline, its middle half its line width
        /// above it, across the tab's room.</summary>
        private void AppendLeader(Content.ContentStreamBuilder b, GraphInfo leader, double x, double baseline, double width)
        {
            b.SaveState();
            if (Text.TextParagraph.EnsureAlphaExtGState(_startPage, leader.StrokeOpacity, strokeToo: true) is { } gs)
                b.SetExtGState(gs);
            RulePainter.StrokeRule(b, leader, x, baseline + leader.LineWidth / 2, x + width);
            b.RestoreState();
        }

        /// <summary>What a justified line's added spacing lengthens a show by.</summary>
        private static double JustifiedExtra(string text, (double Tc, double Tw) spacing)
        {
            if (spacing == default) return 0;
            var extra = text.Length * spacing.Tc;
            foreach (var c in text) if (c == ' ') extra += spacing.Tw;
            return extra;
        }

        /// <summary>Whether a run draws with nothing the deferred writer does not carry.</summary>
        private static bool IsPlainRun(SegmentRun run) =>
            run.Picture is null && !run.Tab && run.CharSpacing == 0 && run.WordSpacing == 0 && run.Rise == 0
            && Math.Abs(run.Scale - 1) < 1e-9 && !run.Underline && !run.Strike && DrawnPlainly(run.State);

        /// <summary>The state the deferred writer draws a plain run in, and the position it
        /// is handed: it registers an embedded face's program on the page the slot becomes
        /// and lifts that run by the face's own descent, so such a run is handed its box
        /// bottom; a Standard-14 run is handed its baseline. The run paints as its own state
        /// paints: the stroke of a rendering mode, a synthetic weight's pen, a shear.</summary>
        private static (Text.TextState State, double Seat) DeferredRun(SegmentRun run, double baseline)
        {
            var state = run.Embedded
                ? new Text.TextState { Font = run.OwnState.Font, FontData = run.OwnState.FontData, ForegroundColor = run.Color }
                : new Text.TextState { FontName = run.BaseFont, ForegroundColor = run.Color };
            state.RenderingMode = run.State.RenderingMode;
            if (run.State.ExplicitStrokingColor is { } stroke) state.StrokingColor = stroke;
            state.LineWidth = run.State.LineWidth;
            state.FormattingOptions = run.State.FormattingOptions;
            return (state, run.Embedded ? baseline - RunDescentEm(run.OwnState) * run.FontSize : baseline);
        }

        /// <summary>The box behind a run's piece of one line, painted before its text: the
        /// piece's advance wide, from the run's line-box descent under the baseline to its
        /// ascent above it, grown by the bands of its border
        /// (<see cref="Text.TextFormattingOptions.RunBorder"/>); its background fills it and
        /// its border is painted inside it as a block's is, rounded when the run asks
        /// (<see cref="Text.TextFormattingOptions.BackgroundCornerRadii"/>).</summary>
        private static void PaintRunBox(Content.ContentStreamBuilder b, SegmentRun run, double x, double baseline, double width)
        {
            var options = run.OwnState.FormattingOptions;
            var fill = run.OwnState.BackgroundColor;
            var border = options?.RunBorder;
            if (fill is null && border is null) return;
            var bands = run.Bands;
            var bottom = baseline - run.DescentEm * run.FontSize - bands.Bottom;
            var height = (run.AscentEm + run.DescentEm) * run.FontSize + bands.Bottom + bands.Top;
            var boxWidth = width + bands.Left + bands.Right;
            if (boxWidth <= 0 || height <= 0) return;
            var box = new Rectangle(x, bottom, x + boxWidth, bottom + height);
            PaintBlock(b, border, box, fill,
                new BlockFill(box, null, options?.BackgroundCornerRadii?.Resolve(boxWidth, height)));
        }

        /// <summary>A run's underline and strike-through: the flow's default rules
        /// for its face and size, or the fragment's own decoration styles.</summary>
        private static void AppendRunDecorations(Content.ContentStreamBuilder b, SegmentRun run,
            double x, double baseline, double width, Text.TextFormattingOptions? options,
            (string? Underline, string? Strike) ruleAlpha)
        {
            var thickness = run.FontSize * DecorationThicknessEm;
            var origin = -DecorationOriginDescentShare * DescentNorm(run.BaseFont) * run.FontSize;
            if (run.Strike)
            {
                var (h, bottom) = RuleGeometry(options?.StrikeoutStyle, run.FontSize, thickness,
                    origin + StrikeoutRiseEm * run.FontSize);
                AppendRuleState(b, options?.StrikeoutStyle, ruleAlpha.Strike);
                b.Rectangle(x, baseline + bottom, width, h);
                b.Fill();
            }
            if (run.Underline)
            {
                var (h, bottom) = RuleGeometry(options?.UnderlineStyle, run.FontSize, thickness, origin);
                AppendRuleState(b, options?.UnderlineStyle, ruleAlpha.Underline);
                b.Rectangle(x, baseline + bottom, width, h);
                b.Fill();
            }
        }
    }
}
