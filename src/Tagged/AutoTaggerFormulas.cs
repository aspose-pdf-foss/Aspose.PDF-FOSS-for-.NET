using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // The faces mathematics is set in: TeX's symbol, math-italic and extension fonts and their
    // Latin Modern, TX, MnSymbol and AMS relatives, and the Symbol face.
    private static readonly Regex MathFace = new(
        @"CMSY|CMMI|CMEX|CMBSY|MSAM|MSBM|rsfs|eufm|wasy|stmary|LMMath|txsy|txmi|txex|MnSymbol|MathJax|Symbol|Math",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // A display formula's line sets at least this share of its characters in a math face, or
    // carries at least this many raised or lowered fragments (limits, indices); it runs to at
    // most this share of its column, and a formula stands over at most this many lines.
    private const double FormulaMathShare = 0.3;
    private const int FormulaRaisedFragments = 2;
    private const double FormulaRaisedSize = 0.8;
    private const double FormulaMaxWidthShare = 0.85;
    private const int FormulaMaxLines = 4;

    /// <summary>Display formulas: a short paragraph, set apart from the margin, whose lines are
    /// mathematics - characters in a math face, indices and limits raised and lowered - is a
    /// Formula element stating its box, which a reader can render as a picture.</summary>
    private static void MarkFormulas(PageAnalysisState pa)
    {
        for (var i = 0; i < pa.result.Count; i++)
        {
            var b = pa.result[i];
            if (b.Kind != BlockKind.Paragraph || b.Note || b.Lines is not { Count: > 0 and <= FormulaMaxLines } lines) continue;
            if (b.Links is { Count: > 0 } || b.InlineFigs is { Count: > 0 }) continue;
            if (!lines.All(l => IsMathLine(pa, l))) continue;
            b.Formula = true;
            pa.result[i] = b;
        }
    }

    private static bool IsMathLine(PageAnalysisState pa, Line line)
    {
        if (line.Frags.Count == 0) return false;
        var (left, right) = ColumnBounds(pa, line);
        if (right - left > 0 && Width(line) >= FormulaMaxWidthShare * (right - left)) return false;
        var chars = line.Frags.Sum(f => f.Text.Count(c => !char.IsWhiteSpace(c)));
        if (chars == 0) return false;
        var math = line.Frags.Where(f => f.Font is not null && MathFace.IsMatch(f.Font)).Sum(f => f.Text.Count(c => !char.IsWhiteSpace(c)));
        if (math >= FormulaMathShare * chars) return true;
        var raised = line.Frags.Count(f => f.Size > 0 && f.Size <= FormulaRaisedSize * line.Size && !string.IsNullOrWhiteSpace(f.Text));
        return raised >= FormulaRaisedFragments;
    }

    // A sum's sign before its operand: times, plus, minus, divided by (the space between them may be no character).
    private static readonly Regex SumSign = new(@"^\s*[x\u00D7+\-\u2212\u2013\u00F7]\s*[\d.,$(]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // A sum's lines are at most this share of the text round them wide, and stand at most this many of their size apart;
    // the rule under its operand stands at most this share of the size under that line's baseline.
    private const double SumLineShare = 0.5;
    private const double SumPitchEms = 1.8;
    private const double SumRuleEms = 0.6;

    /// <summary>A sum set out in lines - a quantity, under it a sign and an operand with a rule under them, and
    /// the result under the rule - is one Formula stating its box, whatever paragraphs its lines had made: short
    /// lines set in from their column's edge, one under another.</summary>
    private static void MarkSums(PageAnalysisState pa, PageWork pw)
    {
        foreach (var rule in pw.Rules.Where(r => r.Horizontal && r.Drawn))
        {
            // The operand's line: the text standing on the rule, across it, opening with a sign (the sign may
            // stand apart from the operand, a piece of its own).
            var on = BlocksOf(pa, l => l.Frags.Count > 0 && rule.At <= l.Baseline + 0.5 && l.Baseline - rule.At <= SumRuleEms * l.Size
                                       && l.MinX < rule.To && l.Frags[^1].R > rule.From).OrderBy(s => s.Line.MinX).ToList();
            if (on.Count == 0 || !SumSign.IsMatch(string.Join(" ", on.Select(s => string.Concat(s.Line.Frags.Select(f => f.Text)))))) continue;
            var operand = on[^1];
            var sum = new List<(int Block, Line Line)>(on);
            // The lines over it and under it, as near as a sum's lines stand, each short and set in.
            foreach (var up in new[] { true, false })
            {
                var edge = operand.Line;
                while (BlockOf(pa, l => l.Frags.Count > 0 && !sum.Any(s => ReferenceEquals(s.Line, l)) && Math.Abs(l.Size - edge.Size) <= 0.5
                                        && (up ? l.Y > edge.Y : l.Y < edge.Y) && Math.Abs(l.Y - edge.Y) <= SumPitchEms * edge.Size
                                        && l.MinX < edge.Frags[^1].R + edge.Size * 8 && l.Frags[^1].R > edge.MinX - edge.Size * 8 && SumLine(pa, l)) is { } next)
                {
                    sum.Add(next);
                    edge = next.Line;
                    if (!up) break; // the result, one line under the rule
                }
            }
            // (a sign standing apart is a piece of the operand's line: the operand shows how the line is set)
            if (sum.Count < on.Count + 2 || !sum.Skip(on.Count - 1).All(s => SumLine(pa, s.Line))) continue;
            MakeFormula(pa, sum.Select(s => s.Line).ToList());
        }
    }

    // A fraction's numerator stands at most this many of its size over its bar, its denominator at most this many under
    // it; a fraction's lines, and the terms beside it, run to at most this share of the text round them.
    private const double FractionOverEms = 1.0;
    private const double FractionUnderEms = 1.6;
    private const double FractionLineShare = 0.85;
    // A term beside a fraction ends at most this many of its size before the fraction's bar.
    private const double FractionReachEms = 6.0;

    /// <summary>A fraction set out in lines - a short rule, text stacked over it and under it within its span - is one
    /// Formula with the terms standing level with it beside it (a multiplier, its sign), whatever paragraph its lines
    /// had joined.</summary>
    private static void MarkFractions(PageAnalysisState pa, PageWork pw)
    {
        foreach (var rule in pw.Rules.Where(r => r.Horizontal && r.Drawn))
        {
            var length = rule.To - rule.From;
            // (a footnote's number raised at the margin stacks nothing: its line's text runs past the rule under the note above)
            bool Within(Line l) => l.Frags.Any(f => !string.IsNullOrWhiteSpace(f.Text) && f.X >= rule.From - l.Size && f.R <= rule.To + l.Size
                                                   && !(f.Size <= l.Size - NoteMarkSizeStep && f.Base >= l.Baseline + NoteMarkRiseEms * l.Size));
            bool Short(Line l) => pa.lines.Any(o => o.Running == 0 && o.Frags.Count > 0 && Width(o) > 0 && Width(l) <= FractionLineShare * Width(o));
            var over = BlockOf(pa, l => Within(l) && l.Baseline > rule.At && l.Baseline - rule.At <= FractionOverEms * l.Size);
            var under = BlockOf(pa, l => Within(l) && l.Baseline < rule.At && rule.At - l.Baseline <= FractionUnderEms * l.Size);
            var widest = pa.lines.Where(l => l.Running == 0 && l.Frags.Count > 0).Select(Width).DefaultIfEmpty(0).Max();
            // (its lines may stand in paragraphs of their own - short lines in a justified column end theirs; the blocks
            // they stand in hold nothing else, or no formula is made)
            if (over is not { } top || under is not { } bottom || length > FractionLineShare * widest) continue;
            // The stacked lines over and under the bar, each within its span, a line's pitch apart.
            var stack = new List<Line> { top.Line, bottom.Line };
            foreach (var up in new[] { true, false })
            {
                var edge = up ? top.Line : bottom.Line;
                while (BlockOf(pa, l => Within(l) && !stack.Contains(l) && Math.Abs(l.Size - edge.Size) <= 0.5
                                        && (up ? l.Y > edge.Y : l.Y < edge.Y) && Math.Abs(l.Y - edge.Y) <= SumPitchEms * edge.Size) is { } next)
                {
                    stack.Add(next.Line);
                    edge = next.Line;
                }
            }
            // The terms level with the fraction: the lines standing between its top and its foot, over the bar or ending a
            // little before it (a multiplier and its sign; the text of a column beside stands further off).
            var (low, high) = (stack.Min(l => l.Y), stack.Max(l => l.Y + l.Size));
            var lines = BlocksOf(pa, l => l.Y >= low - 1 && l.Y + l.Size <= high + 1
                                          && (Within(l) || l.Frags[^1].R <= rule.From + l.Size && rule.From - l.Frags[^1].R <= FractionReachEms * l.Size))
                .Select(s => s.Line).Union(stack).ToList();
            if (!lines.All(Short)) continue;
            MakeFormula(pa, lines);
        }
    }

    // A delimiter drawn in pieces: its top pieces, and the bottom piece of each, in turn (a brace, a parenthesis, a bracket,
    // each opening and closing); it spans at most this many of its size.
    private const string DelimiterTops = "⎧⎫⎛⎞⎡⎤";
    private const string DelimiterBottoms = "⎩⎭⎝⎠⎣⎦";
    private const double DelimiterSpanEms = 4.0;
    // The pieces of one delimiter stand this near across (points).
    private const double PieceAlign = 1.0;

    /// <summary>Whether two pieces of a delimiter stand one over the other: their left edges, or their right ones, level
    /// (a piece's string may open or close with a space).</summary>
    private static bool SameColumn((double X, double R, string Text, double Y, double Base, double Size, string? Font) a,
        (double X, double R, string Text, double Y, double Base, double Size, string? Font) b)
        => Math.Abs(a.X - b.X) <= PieceAlign || Math.Abs(a.R - b.R) <= PieceAlign;

    /// <summary>A delimiter drawn in pieces - a brace's top over its middle over its bottom, one under another - holds
    /// the lines it spans together: one Formula of them, with the lines between beside it (the case a brace opens, and
    /// what it is defined as, set level with its middle), whatever paragraphs they had made.</summary>
    private static void MarkPiecedDelimiters(PageAnalysisState pa)
    {
        static bool Piece(string text, string pieces) => text.Trim() is { Length: 1 } t && pieces.Contains(t[0]);
        foreach (var (_, topLine) in BlocksOf(pa, l => l.Frags.Any(f => Piece(f.Text, DelimiterTops))).ToList())
        {
            foreach (var top in topLine.Frags.Where(f => Piece(f.Text, DelimiterTops)).ToList())
            {
                var bottomPiece = DelimiterBottoms[DelimiterTops.IndexOf(top.Text.Trim()[0])];
                var span = DelimiterSpanEms * topLine.Size;
                if (BlockOf(pa, l => l.Baseline < topLine.Baseline && topLine.Baseline - l.Baseline <= span
                                     && l.Frags.Any(f => f.Text.Trim() == bottomPiece.ToString() && SameColumn(f, top))) is not { } bottom)
                    continue;
                // The lines from the top piece's to the bottom piece's, beside the delimiter or right of it.
                var lines = BlocksOf(pa, l => l.Baseline <= topLine.Baseline + PieceAlign && l.Baseline >= bottom.Line.Baseline - PieceAlign
                                              && l.Frags.Count > 0 && l.Frags[^1].R > top.X - FractionReachEms * topLine.Size)
                    .Select(s => s.Line).Distinct().ToList();
                if (lines.Count >= 2) MakeFormula(pa, lines);
            }
        }
    }

    /// <summary>Makes one Formula of lines set out as a sum or a fraction, cutting them from the paragraphs they joined;
    /// nothing when they share a block with other lines they cannot be cut from.</summary>
    private static void MakeFormula(PageAnalysisState pa, List<Line> sum)
    {
        CutRound(pa, sum);
        var blocks = Enumerable.Range(0, pa.result.Count)
            .Where(i => pa.result[i].Lines is { } held && held.Any(l => sum.Any(s => ReferenceEquals(s, l)))).ToList();
        // Its lines are all of the blocks they stand in: a paragraph's other lines are no part of a sum.
        if (blocks.Count == 0 || blocks.Any(i => pa.result[i].Kind != BlockKind.Paragraph || pa.result[i].Links is { Count: > 0 }
                            || pa.result[i].Lines!.Any(l => !sum.Any(s => ReferenceEquals(s, l))))) return;
        var lines = sum.OrderByDescending(l => Math.Round(l.Baseline)).ThenBy(l => l.MinX).ToList();
        var first = pa.result[blocks[0]];
        foreach (var l in lines) l.BlockId = first.Id;
        first.Lines = lines;
        first.Formula = true;
        first.Y = lines[^1].Y;
        first.SortTop = lines[0].Y + lines[0].Size;
        first.Layout = SumLayout(pa, lines) with { SpaceBefore = first.Layout?.SpaceBefore ?? 0 };
        pa.result[blocks[0]] = first;
        pa.blockLines[blocks[0]] = lines;
        foreach (var i in blocks.Skip(1).Reverse())
        {
            pa.result.RemoveAt(i);
            pa.blockLines.RemoveAt(i);
        }
    }

    /// <summary>A paragraph running on into a sum's lines (the text before a column's break going on in lower
    /// case), or a sum's lines running on into a paragraph, is cut where the sum starts or ends.</summary>
    private static void CutRound(PageAnalysisState pa, List<Line> sum)
    {
        for (var i = pa.result.Count - 1; i >= 0; i--)
        {
            var b = pa.result[i];
            if (b.Kind != BlockKind.Paragraph || b.Links is { Count: > 0 } || b.Lines is not { } lines) continue;
            var held = lines.Count(l => sum.Any(s => ReferenceEquals(s, l)));
            if (held == 0 || held == lines.Count) continue;
            var tail = lines.Skip(lines.Count - held).All(l => sum.Any(s => ReferenceEquals(s, l)));
            if (!tail && !lines.Take(held).All(l => sum.Any(s => ReferenceEquals(s, l)))) continue;
            var cut = tail ? lines.Count - held : held;
            var (first, second) = (lines.GetRange(0, cut), lines.GetRange(cut, lines.Count - cut));
            b.Lines = first;
            b.Y = first[^1].Y;
            pa.result[i] = b;
            pa.blockLines[i] = first;
            var id = pa.run.NextBlockId++;
            foreach (var l in second) l.BlockId = id;
            pa.result.Insert(i + 1, new Block { Id = id, Kind = BlockKind.Paragraph, Y = second[^1].Y, SortTop = second[0].Y + second[0].Size,
                Lines = second, Layout = LayoutOf(pa, second) });
            pa.blockLines.Insert(i + 1, second);
        }
    }

    // A sum standing within this share of its size of the middle of the text round it is centred.
    private const double SumCentreEms = 0.5;

    /// <summary>How a sum stands across the text round it, as a whole: centred when its box stands midway across the
    /// widest line round it, else as far in from that line's start as its box starts.</summary>
    private static ParagraphLayout SumLayout(PageAnalysisState pa, List<Line> lines)
    {
        double left = lines.Min(l => l.MinX), right = lines.Max(l => l.Frags[^1].R), size = lines.Max(l => l.Size);
        var round = pa.lines.Where(l => l.Running == 0 && l.Frags.Count > 0 && !lines.Contains(l) && l.MinX <= left - size && l.Frags[^1].R >= right)
            .OrderByDescending(Width).FirstOrDefault();
        if (round is null) return new ParagraphLayout(null, 0, 0);
        var centred = Math.Abs((left + right) / 2 - (round.MinX + round.Frags[^1].R) / 2) <= Math.Max(SumCentreEms * size, EdgeToleranceFloor);
        return centred ? new ParagraphLayout(LS.AttributeName.TextAlign_Center, 0, 0) : new ParagraphLayout(null, Math.Round(left - round.MinX, 1), 0);
    }

    /// <summary>The first line the blocks hold that <paramref name="fits"/>, with its block's place; null for none.</summary>
    private static (int Block, Line Line)? BlockOf(PageAnalysisState pa, Func<Line, bool> fits)
        => BlocksOf(pa, fits).Cast<(int Block, Line Line)?>().FirstOrDefault();

    /// <summary>The lines the paragraphs hold that <paramref name="fit"/>, each with its block's place.</summary>
    private static IEnumerable<(int Block, Line Line)> BlocksOf(PageAnalysisState pa, Func<Line, bool> fit)
    {
        for (var i = 0; i < pa.result.Count; i++)
            if (pa.result[i].Kind == BlockKind.Paragraph && pa.result[i].Lines is { } lines)
                foreach (var l in lines)
                    if (fit(l)) yield return (i, l);
    }

    /// <summary>Whether a line is as short as a sum's and set in: the page's text runs wider round it - a line
    /// starting further left and ending no sooner, the sum's line at most this share of it wide.</summary>
    private static bool SumLine(PageAnalysisState pa, Line line)
        => line.Frags.Count > 0 && pa.lines.Any(l => l.Running == 0 && l.Frags.Count > 0 && l.MinX <= line.MinX - line.Size
                                                     && l.Frags[^1].R >= line.Frags[^1].R && Width(line) <= SumLineShare * Width(l));

    /// <summary>The box a block's lines cover: left of its leftmost fragment to right of its
    /// rightmost, bottom of its lowest line to top of its highest.</summary>
    private static double[] LinesBox(List<Line> lines)
        => [Math.Round(lines.Min(l => l.MinX), 2), Math.Round(lines.Min(l => l.Y), 2),
            Math.Round(lines.Max(l => l.Frags.Count > 0 ? l.Frags[^1].R : l.MinX), 2), Math.Round(lines.Max(l => l.Y + l.Size), 2)];
}
