namespace Aspose.Pdf.Tagged;

internal static partial class TaggedContentRenderer
{
    private sealed partial class Engine
    {
        /// <summary>Alignments that place a box of a known width: centred in its column, or
        /// against the column's right edge.</summary>
        private static bool PlacesBox(HorizontalAlignment align)
            => align is HorizontalAlignment.Center or HorizontalAlignment.Right;

        /// <summary>The left edge of a box <paramref name="width"/> wide in the column
        /// [<paramref name="start"/>, <paramref name="end"/>]: centred, against the right edge,
        /// or at the start for every other alignment.</summary>
        private static double AlignedX(HorizontalAlignment align, double start, double end, double width)
            => align switch
            {
                HorizontalAlignment.Center => (start + end - width) / 2,
                HorizontalAlignment.Right => end - width,
                _ => start,
            };

        /// <summary>A span's text in the column [<paramref name="start"/>, <paramref name="end"/>]:
        /// one piece placed as a box, or, fully justified, a piece per word and space spread over
        /// the column. Justify leaves a span's single line as it is.</summary>
        private List<Chunk> PlaceSpan(HorizontalAlignment align, Run run, double start, double end, double baseline)
        {
            var whole = new Chunk(0, start, baseline, run.Text, 0);
            if (align == HorizontalAlignment.FullJustify
                && SpreadLine(new List<Run> { run }, new List<Chunk> { whole }, start, end) is { } spread)
                return spread;
            if (PlacesBox(align))
                whole = whole with { X = AlignedX(align, start, end, Measure(run.Font, run.Size, run.Text)) };
            return new List<Chunk> { whole };
        }

        /// <summary>A wrapped paragraph's lines placed by its alignment, with the left edge of
        /// its clip box (the first line's start). A centred line is centred with the spaces it
        /// closes with; a right-aligned line puts its last glyph on the right edge, the clip box
        /// starting where those spaces would; a justified line is spread over the column except
        /// the paragraph's last line, which FullJustify spreads as well.</summary>
        private (List<Chunk> Chunks, double ClipLeft) AlignParagraphLines(List<Run> runs, WrappedParagraph wrapped,
            double left, double right, HorizontalAlignment align)
        {
            if (align is not (HorizontalAlignment.Center or HorizontalAlignment.Right
                or HorizontalAlignment.Justify or HorizontalAlignment.FullJustify))
                return (wrapped.Chunks, left);

            var placed = new List<Chunk>();
            double clipLeft = left;
            foreach (var line in wrapped.Chunks.GroupBy(chunk => chunk.Line))
            {
                var chunks = line.ToList();
                var end = wrapped.Ends[line.Key];
                bool spreads = align == HorizontalAlignment.FullJustify
                    || (align == HorizontalAlignment.Justify && line.Key < wrapped.LineCount - 1);
                double dx = align switch
                {
                    HorizontalAlignment.Center => (right - end.Pen) / 2,
                    HorizontalAlignment.Right => right - (end.Pen - end.Trailing),
                    _ => 0,
                };
                if (line.Key == 0 && dx != 0)
                    clipLeft = left + dx - (align == HorizontalAlignment.Right ? end.Trailing : 0);
                if (spreads && SpreadLine(runs, chunks, left, right) is { } spread)
                    placed.AddRange(spread);
                else
                    placed.AddRange(chunks.Select(chunk => chunk with { X = chunk.X + dx }));
            }
            return (placed, clipLeft);
        }

        /// <summary>One line's words spread over [<paramref name="left"/>, <paramref name="right"/>]:
        /// every word and every space becomes a chunk of its own, the spaces between words share
        /// the room left over, and the spaces closing the line are dropped. Null when the line has
        /// no space to widen.</summary>
        private List<Chunk>? SpreadLine(List<Run> runs, List<Chunk> line, double left, double right)
        {
            var tokens = new List<(int Run, string Text, double Width, bool Space)>();
            foreach (var chunk in line)
            {
                var run = runs[chunk.RunIdx];
                foreach (var token in SplitWordsAndSpaces(chunk.Text))
                    tokens.Add((chunk.RunIdx, token, Measure(run.Font, run.Size, token), token == " "));
            }
            while (tokens.Count > 0 && tokens[^1].Space) tokens.RemoveAt(tokens.Count - 1);
            int gaps = tokens.Count(token => token.Space);
            if (gaps == 0) return null;

            double extra = (right - left - tokens.Sum(token => token.Width)) / gaps;
            double pen = left;
            var spread = new List<Chunk>(tokens.Count);
            foreach (var token in tokens)
            {
                spread.Add(new Chunk(token.Run, pen, line[0].Baseline, token.Text, line[0].Line));
                pen += token.Width + (token.Space ? extra : 0);
            }
            return spread;
        }

        /// <summary>Text cut into words and single spaces, in order.</summary>
        private static IEnumerable<string> SplitWordsAndSpaces(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                if (text[i] == ' ') { yield return " "; i++; continue; }
                int end = text.IndexOf(' ', i);
                if (end < 0) end = text.Length;
                yield return text.Substring(i, end - i);
                i = end;
            }
        }
    }
}
