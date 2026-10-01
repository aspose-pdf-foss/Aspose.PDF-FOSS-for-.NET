using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A gap between two characters of one shown string this many ems wide is no word space
    // (word spaces, justified ones too, stay under an em): the string holds cells set apart -
    // a row of figures in a statistical table written as one string with its columns spaced out.
    private const double WideGapEms = 1.0;
    // No glyph is wider than this many ems: a character's box reaching further holds the gap a
    // TJ adjustment after it opened. Its ink ends at its own advance (the font's measure, with
    // this much slack for a synthetic or substituted face), within the box.
    private const double MaxGlyphEms = 1.0;
    private const double AdvanceSlack = 1.15;

    // A figure: a number (with its sign, thousands commas, decimals, parentheses or percent), or a
    // statistical table's "n.a." or "..." standing for one. Three or more of them, spaces between,
    // are a run of figures (two may be a date or a range in prose).
    private const string Figure = @"(\(?[-+−]?(\d[\d,]*(\.\d*)?|\.\d+)\)?%?|n\.a\.|\.\.\.|-)";
    private static readonly System.Text.RegularExpressions.Regex FigureRun = new(
        @"^\s*" + Figure + @"(\s+" + Figure + @"){2,}\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);
    // Two figures with their decimals, a space apart, are two cells as well: no date or range reads so.
    private const string DecimalFigure = @"\(?[-+−]?\d[\d,]*\.\d+\)?%?";
    private static readonly System.Text.RegularExpressions.Regex DecimalPair = new(
        @"^\s*" + DecimalFigure + @"\s+" + DecimalFigure + @"\s*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsFigureRun(string text) => FigureRun.IsMatch(text) || DecimalPair.IsMatch(text);

    /// <summary>The figures of a run of figures, each with the span its characters cover.</summary>
    private static List<(double X, double R, string Text)> FigureTokens(List<(char C, double L, double R)> chars)
    {
        var tokens = new List<(double X, double R, string Text)>();
        var start = -1;
        for (var i = 0; i <= chars.Count; i++)
        {
            var space = i == chars.Count || char.IsWhiteSpace(chars[i].C);
            if (!space && start < 0) start = i;
            if (space && start >= 0)
            {
                tokens.Add((chars[start].L, chars[i - 1].R, new string(chars.GetRange(start, i - start).ConvertAll(c => c.C).ToArray())));
                start = -1;
            }
        }
        return tokens;
    }

    /// <summary>The figures of a run of figures whose characters' boxes are not known: each placed where
    /// its font's measure of the text before it puts it, scaled to the span the fragment covers.</summary>
    private static List<(double X, double R, string Text)> MeasuredFigureTokens(TextFragment fragment, Rectangle rect)
    {
        var text = fragment.Text;
        var whole = fragment.TextState.MeasureString(text);
        if (whole <= 0) return new List<(double X, double R, string Text)> { (rect.LLX, rect.URX, text) };
        var scale = rect.Width / whole;
        var tokens = new List<(double X, double R, string Text)>();
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\S+"))
        {
            var x = rect.LLX + fragment.TextState.MeasureString(text.Substring(0, m.Index)) * scale;
            tokens.Add((x, x + fragment.TextState.MeasureString(m.Value) * scale, m.Value));
        }
        return tokens;
    }

    /// <summary>A fragment's text split where its characters stand a wide gap apart, each piece
    /// with the span its characters cover; the fragment whole when it has no such gap or its
    /// characters' boxes are not known.</summary>
    private static List<(double X, double R, string Text)> WidePieces(TextFragment fragment, Rectangle rect, double size)
    {
        var whole = new List<(double X, double R, string Text)> { (rect.LLX, rect.URX, fragment.Text) };
        var chars = new List<(char C, double L, double R)>();
        foreach (var segment in fragment.Segments)
        {
            var text = segment.Text ?? string.Empty;
            if (segment.Characters.Count != text.Length)
                return IsFigureRun(fragment.Text) ? MeasuredFigureTokens(fragment, rect) : whole;
            for (var i = 0; i < text.Length; i++)
            {
                var box = segment.Characters[i + 1].Rectangle;
                var advance = char.IsWhiteSpace(text[i]) ? 0 : segment.TextState.MeasureString(text[i].ToString());
                var ink = advance > 0 ? Math.Min(AdvanceSlack * advance, MaxGlyphEms * size) : MaxGlyphEms * size;
                chars.Add((text[i], box.LLX, Math.Min(box.URX, box.LLX + ink)));
            }
        }
        if (chars.Count == 0 || size <= 0) return whole;

        // A string of figures alone, a space between each - a statistical table's row set so tight its
        // columns stand but a space apart - is a figure per cell.
        if (IsFigureRun(fragment.Text))
            return FigureTokens(chars);

        var pieces = new List<(double X, double R, string Text)>();
        var start = -1;
        var last = -1; // the last character with ink
        void Close(int end)
        {
            if (start < 0) return;
            var text = new string(chars.GetRange(start, end - start + 1).ConvertAll(c => c.C).ToArray());
            // A piece set apart that is a run of figures (a row's figures after its label) is a figure per cell.
            if (IsFigureRun(text)) pieces.AddRange(FigureTokens(chars.GetRange(start, end - start + 1)));
            else pieces.Add((chars[start].L, chars[last].R, text.Trim()));
            start = -1;
        }
        for (var i = 0; i < chars.Count; i++)
        {
            if (char.IsWhiteSpace(chars[i].C)) continue;
            if (start >= 0 && chars[i].L - chars[last].R > WideGapEms * size) Close(last);
            if (start < 0) start = i;
            last = i;
        }
        Close(last);
        // One piece: the fragment, less the blanks stretched out before or after its text (a term's spaces run out to its
        // definition, drawn in the definition's string).
        // (spaces a word processor stretched - each wider than an em - not a run of plain ones, as a note hanging in spaces has)
        var (lead, trail) = (fragment.Text.Length - fragment.Text.TrimStart().Length, fragment.Text.Length - fragment.Text.TrimEnd().Length);
        if (pieces.Count == 1 && (lead > 0 && pieces[0].X - rect.LLX > WideGapEms * size * lead
                                  || trail > 0 && rect.URX - pieces[0].R > WideGapEms * size * trail))
            return [(pieces[0].X, pieces[0].R, fragment.Text.Trim())];
        return pieces.Count > 1 ? pieces : whole;
    }

    // A row's label wraps over at most this many lines above the line holding its figures.
    private const int MaxLabelLines = 3;
    // A row has at least this many cells: a label and figures.
    private const int MinRowCells = 3;
    // Wrapped lines stand at most this many line sizes apart.
    private const double WrapPitchFactor = 1.6;

    /// <summary>The lines with each row label wrapped over lines joined to the line that holds the
    /// row's figures ("16 Construction and land" over "development loans 460.0 453.8 ..."): the
    /// label lines stand in the label column only - left of the row's first figure - the first
    /// starts left of the row (it holds the row's number), and each is a line's pitch over the
    /// next. The joined line stands between them, so a row's bounds take all of its lines.</summary>
    private static List<Line> JoinWrappedLabels(List<Line> lines)
    {
        var result = new List<Line>();
        for (var i = 0; i < lines.Count; i++)
        {
            var k = i + 1;
            while (k < lines.Count && k - i <= MaxLabelLines && Close(lines[k - 1], lines[k]) && Cells(lines[k]).Count < MinRowCells) k++;
            if (k >= lines.Count || k - i > MaxLabelLines || !Close(lines[k - 1], lines[k]) || !Wraps(lines, i, k))
            {
                result.Add(lines[i]);
                continue;
            }
            var upper = lines[i];
            var row = lines[k];
            result.Add(new Line
            {
                Y = (upper.Y + row.Y) / 2, Baseline = (upper.Baseline + row.Baseline) / 2, Size = row.Size,
                FirstBaseline = upper.Baseline, LastBaseline = row.Baseline,
                MinX = Enumerable.Range(i, k - i + 1).Min(r => lines[r].MinX), ColumnRight = row.ColumnRight,
                Frags = Enumerable.Range(i, k - i + 1).SelectMany(r => lines[r].Frags).OrderBy(f => f.X).ToList(),
            });
            i = k;
        }
        return result;

        static bool Close(Line a, Line b) => a.Y - b.Y > 0 && a.Y - b.Y < WrapPitchFactor * Math.Max(a.Size, b.Size)
                                             && Math.Abs(a.Size - b.Size) < 0.5 && a.Running == 0 && b.Running == 0;
        static bool Wraps(List<Line> lines, int from, int row)
        {
            var cells = Cells(lines[row]);
            if (cells.Count < MinRowCells) return false;
            var labels = Enumerable.Range(from, row - from).Select(r => Cells(lines[r])).ToList();
            // The row's label goes on from the line above: it starts in lower case (a section
            // heading over the row - "Assets" over "1 Bank credit" - is a row of its own).
            var first = lines[row].Frags.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Text)).Text?.TrimStart();
            if (string.IsNullOrEmpty(first)) return false;
            // Or the label above starts with the row's number and the row starts with none of its own ("30 Total
            // federal funds sold and reverse" over "RPs").
            var numbered = LeadingNumber(lines[from]) && !LeadingNumber(lines[row]);
            if (!char.IsLower(first[0]) && !numbered) return false;
            return labels.All(c => c.Count > 0 && c.All(x => x.R < cells[1].X))
                   && labels[0][0].X < cells[0].X - AlignTolerance;
        }

        // A line starting with a number of a few digits alone - a table's line number.
        static bool LeadingNumber(Line line)
            => line.Frags.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Text)).Text?.Trim().Split(' ')[0] is { Length: > 0 and <= 3 } word
               && word.All(char.IsDigit);
    }

    /// <summary>A table row written as one string - its figures spaced out into the columns - is
    /// shown as one string per column, so each cell owns its own figures: the page's shows are
    /// cut between words at the tables' column edges (as for links, <see cref="LinkTextSplit"/>:
    /// every glyph lands where it did) and the page is read again, its forms looked into again
    /// (<see cref="RescanPage"/>).</summary>
    private static void SplitShowsAtColumns(PageWork pw, FormUse forms)
    {
        if (pw.Tables.Count == 0) return;
        var columns = new List<Rectangle>();
        foreach (var t in pw.Tables)
            for (var c = 0; c < t.cols; c++)
                columns.Add(new Rectangle(t.colX[c], t.region.LLY, t.colX[c + 1], t.region.URY));
        // The tables stand on the page as it is shown; its shows are cut in its own space.
        var shown = PageContentScan.ShownMatrix(pw.Page.Reader, pw.Page.Dict);
        if (!LinkTextSplit.ApplyToColumns(pw.Page, columns.Select(r => PageContentScan.Unshown(r, shown)).ToList())) return;
        RescanPage(pw, forms);
    }

    // Pieces of a line standing this many ems apart - a term and its definition, spaced out - are shown apart.
    private const double ApartGapEms = 1.5;
    // The next line stands within this many sizes under a line; lines this near (points) start together.
    private const double NextLinePitches = 2.5;
    private const double LinesStartTogether = 1.5;
    // A definition is prose: at least this many words (a column of figures, a sum set out in lines, is none).
    private const int MinDefinitionWords = 3;
    private static readonly System.Text.RegularExpressions.Regex ProseWords = new(@"\p{L}{2,}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The pieces of a line standing a wide gap apart, written in one string with the spaces between them
    /// stretched (") The output of..." after its term), are shown as strings of their own: each piece's text then stands
    /// where its glyphs do. Lines of tables and running lines keep their shows, and so does a page with forms looked into
    /// or drawn whole.</summary>
    private static void SplitShowsAtGaps(PageWork pw, List<Line> rows)
    {
        if (pw.Ops.Any(o => o.Expanded || o.AsFigure)) return;
        var pieces = new List<Rectangle>();
        foreach (var line in rows.Where(l => l.Running == 0 && l.Frags.Count > 1 && !pw.Tables.Any(t => InRegion(l, t.region))))
        {
            // (the text after the gap going on under itself on the next line: a definition beside its term)
            var apart = Enumerable.Range(1, line.Frags.Count - 1)
                .Any(k => line.Frags[k].X - line.Frags[k - 1].R > ApartGapEms * Math.Max(line.Size, 1)
                          && !string.IsNullOrWhiteSpace(line.Frags[k].Text) && !string.IsNullOrWhiteSpace(line.Frags[k - 1].Text)
                          && ProseWords.Matches(line.Frags[k].Text).Count >= MinDefinitionWords
                          && rows.Any(o => o.Running == 0 && o.Y < line.Y && line.Y - o.Y < NextLinePitches * Math.Max(line.Size, 1)
                                           && Math.Abs(o.MinX - line.Frags[k].X) <= LinesStartTogether));
            if (!apart) continue;
            // (the last piece runs on an em: the blank ending the line stays in its show)
            var inked = line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
            for (var k = 0; k < inked.Count; k++)
                pieces.Add(new Rectangle(inked[k].X - 0.5, line.Y, inked[k].R + (k + 1 < inked.Count ? 0.5 : Math.Max(line.Size, 1)),
                                         line.Y + line.Size));
        }
        if (pieces.Count == 0) return;
        var shown = PageContentScan.ShownMatrix(pw.Page.Reader, pw.Page.Dict);
        if (!LinkTextSplit.Apply(pw.Page, pieces.Select(r => PageContentScan.Unshown(r, shown)).ToList())) return;
        (pw.Bytes, pw.Ops) = PageContentScan.Scan(pw.Page);
        pw.Figures = CollectFigures(pw.Ops);
    }

    // A show starting this far (points) inside another on its line reaches into it.
    private const double InterleaveSlack = 1;
    // A blank a show ends with reaches this many ems: it ends no ink (a mark's trailing space reaching over the comma after it).
    private const double BlankEms = 0.3;

    /// <summary>A line whose shows reach into one another - a formula an equation editor set, its letters one string
    /// placed glyph by glyph (the pen stepping back between them), its operators another, its indices a third - has its
    /// shows cut into their words and the glyphs they place apart (every glyph lands where it did): the text of each
    /// then stands where its glyphs do, and the line reads left to right. True when the page's content changed.</summary>
    private static bool SplitInterleavedShows(Page page, List<Line> lines)
    {
        var apart = lines.Where(Interleaved)
            .Select(l => new Rectangle(l.MinX - InterleaveSlack, l.Y, l.Frags.Max(f => f.R) + InterleaveSlack, l.Y + l.Size)).ToList();
        if (apart.Count == 0) return false;
        var shown = PageContentScan.ShownMatrix(page.Reader, page.Dict);
        return LinkTextSplit.Apply(page, [], [], apart.Select(r => PageContentScan.Unshown(r, shown)).ToList());

        static bool Interleaved(Line line)
        {
            var inked = line.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
            return inked.Any(a => inked.Any(b => b.X > a.X + InterleaveSlack
                                                 && b.X < a.R - (a.Text.Length - a.Text.TrimEnd().Length) * BlankEms * a.Size - InterleaveSlack));
        }
    }

    /// <summary>A list item's label written in one string with its text ("1. Currency consists of...", or "[10] " stepped
    /// apart from "NIST Special..." in one show) is shown as a string of its own, so the item's Lbl owns it: the page's
    /// shows are cut after the labels opening the lines that open with one (every glyph lands where it did) and the page
    /// is read again. A page with forms looked into or drawn whole keeps its shows.</summary>
    private static void SplitShowsAtLabels(PageWork pw, List<Line> rows)
    {
        if (pw.Ops.Any(o => o.Expanded || o.AsFigure)) return;
        var leads = new List<(Rectangle At, string Label)>();
        foreach (var line in rows.Where(l => l.Running == 0 && l.Frags.Count > 0 && !pw.Tables.Any(t => InRegion(l, t.region))))
        {
            var first = line.Frags[0];
            // (a label read as a piece of its own, text after it on the line: its show may still hold that text)
            var label = LabelOnly.IsMatch(first.Text) ? line.Frags.Count > 1 ? first.Text.TrimStart() : null
                : LabelLead.Match(first.Text) is { Success: true } lead ? lead.Groups[1].Value : null;
            if (label is null) continue;
            leads.Add((new Rectangle(first.X - 1, line.Y, first.X + first.Size, line.Y + line.Size), label));
        }
        if (leads.Count == 0) return;
        var shown = PageContentScan.ShownMatrix(pw.Page.Reader, pw.Page.Dict);
        if (!LinkTextSplit.Apply(pw.Page, [], leads.Select(l => (PageContentScan.Unshown(l.At, shown), l.Label)).ToList())) return;
        (pw.Bytes, pw.Ops) = PageContentScan.Scan(pw.Page);
        pw.Figures = CollectFigures(pw.Ops);
    }
}
