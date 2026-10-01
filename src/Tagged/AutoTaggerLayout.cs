using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    // A paragraph ends where the gap to the next line exceeds this many usual line pitches.
    private const double ParagraphGapFactor = 1.4;
    // The usual pitch is never taken as more than this many body sizes (a page of one-line
    // paragraphs has only paragraph gaps to measure).
    private const double MaxPitchFactor = 1.3;
    // A line this much further right than the previous one opens a paragraph (first-line indent).
    private const double IndentStep = 8.0;
    // ... but at most this many ems further right, and at least this share of a pitch lower.
    private const double MaxIndentEms = 4.0;
    private const double MinLineStepFactor = 0.6;
    // Two list labels, or a continuation line and its item's body, align within this distance.
    private const double AlignTolerance = 3.0;
    // Fragments further apart than this fraction of the text size are separate table cells.
    private const double CellGapFactor = 0.2;
    // A line ending within this many ems of the page's text margin is a full line.
    private const double FullLineEms = 3.0;
    // The next line of a wrapped heading sits within this many heading sizes below it.
    private const double HeadingWrapFactor = 1.6;
    // A line ending this many ems before its column's right edge is short of it.
    private const double ShortLineEms = 1.0;
    // A column is justified when at least this share of its body lines (and at least this many)
    // run from its left edge to its right: there a line short of the edge is its paragraph's
    // last. A short ragged paragraph has two lines of one length by chance.
    private const double JustifiedColumnShare = 0.7;
    private const int JustifiedColumnMinLines = 4;
    // Two lines in a row each narrower than this share of their column are lines of their own
    // (names, an address, entries), not one paragraph's wrapped lines.
    private const double ShortLineShare = 0.6;
    // A heading line wraps to the next only when it runs at least this share of its column: a
    // list of short lines at heading size is a heading per line.
    private const double HeadingWrapShare = 0.4;
    // Prose columns fill their width; a table has short cells somewhere.
    private const double ProseFill = 0.7;
    // Columns each filled to this share of their width, each at least this many text sizes
    // wide, whose cells mostly end mid-sentence, are columns of running prose.
    private const double ProseColumnsFill = 0.9;
    private const double ProseColumnMinEms = 10;
    private const double ProseOpenEnds = 0.5;
    // Cells centred on their column within this share of the text size (their starts spread
    // more) are a grid of centred text, not a table.
    private const double CentreTolEms = 0.5;
    // ... and at least this share of its lines start in lower case (a sentence carried over).
    private const double ProseContinuation = 0.3;

    // A list label on its own: a bullet glyph, or a number / letter / roman numeral with . or ) - a number may
    // carry a letter, as a form's lines do (7a., 12b)) - or a number in brackets, as a list of references labels its entries.
    private static readonly Regex LabelOnly = new(
        @"^\s*([•◦▪▫‣⁃●○■□◆◇➢➤►▸–—\-\*·]|\(?(\d{1,3}[a-z]?|[a-zA-Z]|[ivxlcdmIVXLCDM]{1,6})[.)]|\[\d{1,3}\])\s*$", RegexOptions.Compiled);
    // A label that starts a fragment followed by the item's text.
    private static readonly Regex LabelLead = new(
        @"^\s*([•◦▪▫‣⁃●○■□◆◇➢➤►▸·]|\d{1,3}[a-z]?[.)]|\[\d{1,3}\])\s+\S", RegexOptions.Compiled);

    /// <summary>Whether a line at the body's size, all bold, is the bold lead of a paragraph running on into the next
    /// line: the next line of its column opens with bold words, then turns to the body's type - a run-in heading, no
    /// heading of its own.</summary>
    private static bool RunInLead(PageAnalysisState pa, Line line)
    {
        if (Math.Abs(line.Size - pa.bodySize) > 0.6) return false;
        var at = pa.lines.IndexOf(line);
        var next = pa.lines.Skip(at + 1).FirstOrDefault(l => l.Running == 0 && l.Frags.Count > 0 && l.ColumnRight == line.ColumnRight);
        if (next is null || line.Y - next.Y > MaxPitchFactor * pa.bodySize) return false;
        var words = next.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
        return words.Count >= 2 && IsBold(words[0].Font) && words.Skip(1).Any(f => !IsBold(f.Font));
    }

    private static void ClassifyLine(PageAnalysisState pa, Line line, HeadingRule headingRule)
    {
        if (line.Running != 0) return; // a running header or footer: an artifact, not content
        if (line.Toc)
        {
            // An entry of a table of contents is one of its own, whatever the lines around it.
            FlushParagraph(pa);
            FlushList(pa);
            line.BlockId = pa.run.NextBlockId++;
            Emit(pa, new Block { Id = line.BlockId, Kind = BlockKind.Paragraph, Toc = true, Y = line.Y, SortTop = line.Y + line.Size, Lines = [line] }, [line]);
            pa.prev = null;
            pa.heading = null;
            return;
        }
        var level = headingRule.LevelOf(line, pa.page.Number);
        if (level > 0 && RunInLead(pa, line)) level = 0;
        if (level == 0) pa.heading = null;
        if (line.ColumnStart)
        {
            // A new column: the lines above belong to another column, so gaps and indents
            // against them mean nothing; the paragraph goes on only if the text does, and never
            // into a table.
            if (pa.paraLines is null || pa.prev is null || pa.tables.Any(t => InRegion(line, t.region))
                || !ContinuesInNextColumn(pa.prev, line))
            {
                FlushParagraph(pa);
                FlushList(pa);
                pa.prev = null;
            }
            else
            {
                pa.paraLines.Add(line);
                pa.prev = line;
                return;
            }
        }

        var inTable = -1;
        for (var t = 0; t < pa.tables.Count; t++)
            if (InRegion(line, pa.tables[t].region)) { inTable = t; break; }

        if (inTable >= 0)
        {
            // A paragraph in a column beside the table goes on past the table's rows.
            var region = pa.tables[inTable].region;
            var beside = pa.paraLines is { Count: > 0 } pending
                         && pending.All(l => l.Frags.Count > 0 && (l.Frags[^1].R < region.LLX || l.MinX > region.URX));
            if (!beside)
            {
                FlushParagraph(pa);
                pa.prev = null;
            }
            FlushList(pa);
            if (!pa.tableEmitted[inTable])
            {
                pa.tableIds[inTable] = pa.run.NextBlockId++;
                Emit(pa, new Block { Id = pa.tableIds[inTable], Kind = BlockKind.Table, TableIndex = inTable, Rows = pa.tables[inTable].rows, Cols = pa.tables[inTable].cols, Y = line.Y, SortTop = line.Y + line.Size }, null);
                pa.tableEmitted[inTable] = true;
            }
            line.BlockId = pa.tableIds[inTable];
            return; // the table's cell text doesn't form paragraphs
        }

        var headingIdx = level - 1;
        if (headingIdx >= 0)
        {
            FlushParagraph(pa);
            FlushList(pa);
            // A heading wrapped over lines: the same size right below the heading line before.
            if (pa.heading is { } above && pa.result.Count > 0 && pa.result[^1].Kind == BlockKind.Heading
                && pa.result[^1].Level == Math.Min(level, 6)
                && Math.Abs(above.Size - line.Size) < 0.5 && above.Y - line.Y > 0 && above.Y - line.Y < HeadingWrapFactor * line.Size
                && RunsWide(pa, above))
            {
                var h = pa.result[^1];
                line.BlockId = h.Id;
                h.Y = line.Y;
                pa.result[^1] = h;
                pa.blockLines[^1]!.Add(line);
                pa.heading = line;
                return;
            }
            line.BlockId = pa.run.NextBlockId++;
            Emit(pa, new Block { Id = line.BlockId, Kind = BlockKind.Heading, Level = Math.Min(headingIdx + 1, 6), Y = line.Y, SortTop = line.Y + line.Size, TextMinX = line.MinX }, new List<Line> { line });
            pa.prev = null;
            pa.heading = line;
            return;
        }

        if (ListBodyX(line) is { } bodyX && !IsFootLine(pa, line) && !NumberRunningOn(pa, line))
        {
            FlushParagraph(pa);
            // A list's labels stand in one place and are of one kind: a number after bullets opens another.
            if (pa.listItems is not null && (Math.Abs(line.MinX - pa.listLabelX) > AlignTolerance
                                             || NumberLabelled(line) != NumberLabelled(pa.listItems[0].Lines[0]))) FlushList(pa);
            if (pa.listItems is null) { pa.listItems = []; pa.listLabelX = line.MinX; }
            pa.item = new ListItem { LabelX = line.MinX, BodyX = bodyX };
            pa.item.Lines.Add(line);
            pa.listItems.Add(pa.item);
            pa.prev = line;
            return;
        }
        if (pa.item is not null && !IsGap(pa, line)
            && (Math.Abs(line.MinX - pa.item.BodyX) <= AlignTolerance || HangsBackToLabel(pa, line)))
        {
            pa.item.Lines.Add(line);
            pa.prev = line;
            return;
        }
        FlushList(pa, goesOn: true);

        // Consecutive body lines form one paragraph until a gap, a first-line indent, an outdent
        // from a hanging indent, or a line that ends its paragraph.
        if (pa.paraLines is not null && (IsGap(pa, line) || IsIndent(pa, line) || IsOutdent(pa, line) || EndsParagraph(pa, line) || EndsNote(pa, line)))
            FlushParagraph(pa);
        (pa.paraLines ??= []).Add(line);
        pa.prev = line;
    }

    /// <summary>A number ending a sentence at the start of a line ("... a payment with Form" / "941. Mail your
    /// return ...") is the paragraph's text, no label: the line stands under the open paragraph's last line, at its
    /// pitch and where it starts - or back from it, that line the paragraph's first, set in - and that line's text
    /// goes on: it ends no sentence and leads in no list.</summary>
    private static bool NumberRunningOn(PageAnalysisState pa, Line line)
    {
        if (pa.item is not null || pa.paraLines is not { Count: > 0 } || pa.prev is not { Frags.Count: > 0 } prev || line.Frags.Count == 0) return false;
        if (!char.IsDigit(line.Frags[0].Text.TrimStart().FirstOrDefault())) return false;
        var setIn = pa.paraLines.Count == 1 && prev.MinX - line.MinX is var back && back > 0 && back < MaxIndentEms * line.Size;
        if (IsGap(pa, line) || (Math.Abs(line.MinX - prev.MinX) > AlignTolerance && !setIn) || Math.Abs(line.Size - prev.Size) > 0.5) return false;
        var text = string.Concat(prev.Frags.Select(f => f.Text)).TrimEnd();
        return text.Length > 0 && ".!?:;".IndexOf(text[^1]) < 0;
    }

    private static bool IsGap(PageAnalysisState pa, Line line) =>
        pa.prev is { } prev && prev.Y - line.Y > pa.pitch * ParagraphGapFactor;

    /// <summary>A first-line indent: a full line below the previous one, starting a little (at
    /// most a few ems) further right. A fragment raised or lowered part-way along a line forms
    /// a "line" of its own that starts far right and sits close by: that is not an indent. Nor
    /// is the second line of a hanging indent (a bibliography entry): the line before it is the
    /// paragraph's first, and its text goes on - it ends in a hyphen joining a word, or it ran
    /// to its block's edge without ending a sentence.</summary>
    private static bool IsIndent(PageAnalysisState pa, Line line)
    {
        if (pa.prev is not { } prev) return false;
        var step = line.MinX - prev.MinX;
        if (step <= IndentStep || step >= MaxIndentEms * line.Size || prev.Y - line.Y <= MinLineStepFactor * pa.pitch) return false;
        return !HangsFrom(pa, prev, line);
    }

    /// <summary>Whether <paramref name="line"/> is the second line of a paragraph hanging from
    /// <paramref name="first"/>: the first is alone in its paragraph and its text goes on.</summary>
    private static bool HangsFrom(PageAnalysisState pa, Line first, Line line)
    {
        if (pa.paraLines is not { Count: 1 } || first.Frags.Count == 0 || line.Frags.Count == 0) return false;
        var text = string.Concat(first.Frags.Select(f => f.Text)).TrimEnd();
        if (text.Length == 0) return false;
        if (text.Length >= 2 && text[^1] == '-' && char.IsLetter(text[^2])) return true;
        if (".!?:".IndexOf(text[^1]) >= 0) return false;
        var (_, right) = ColumnBounds(pa, first);
        var edge = Math.Max(right, line.Frags[^1].R);
        return edge - first.Frags[^1].R <= ShortLineEms * first.Size;
    }

    /// <summary>An outdent from a hanging indent: the open paragraph's lines after its first
    /// all start where the previous line does, and this line starts back where the first did
    /// (the next bibliography entry).</summary>
    private static bool IsOutdent(PageAnalysisState pa, Line line)
    {
        if (pa.prev is not { } prev || pa.paraLines is not { Count: >= 2 } lines) return false;
        if (prev.MinX - line.MinX <= IndentStep || Math.Abs(line.MinX - lines[0].MinX) > AlignTolerance) return false;
        return lines.Skip(1).All(l => Math.Abs(l.MinX - prev.MinX) <= AlignTolerance);
    }

    /// <summary>The column a line stands in: its left edge (where the page's lines of that
    /// column start furthest left) and its right edge (the column's, or the page's text margin).</summary>
    private static (double Left, double Right) ColumnBounds(PageAnalysisState pa, Line line)
    {
        var column = line.ColumnRight;
        var right = column > 0 ? column : pa.margin;
        var left = pa.lines.Where(l => l.Running == 0 && l.Frags.Count > 0 && l.ColumnRight == column)
            .Select(l => l.MinX).DefaultIfEmpty(line.MinX).Min();
        return (left, right);
    }

    private static double Width(Line line) => line.Frags.Count > 0 ? line.Frags[^1].R - line.MinX : 0;

    /// <summary>Whether a heading line runs wide enough of its column for the next line at its
    /// size to be its wrapped continuation rather than a heading of its own.</summary>
    private static bool RunsWide(PageAnalysisState pa, Line line)
    {
        var (left, right) = ColumnBounds(pa, line);
        return right - left <= 0 || Width(line) >= HeadingWrapShare * (right - left);
    }

    /// <summary>A list item's wrapped line set back under its label rather than its body (a
    /// list without a hanging indent): it goes on with the item when it carries the sentence on
    /// in lower case, or the item's line before it ran to the column's edge.</summary>
    private static bool HangsBackToLabel(PageAnalysisState pa, Line line)
    {
        if (pa.item is not { Lines.Count: > 0 } item || Math.Abs(line.MinX - item.LabelX) > AlignTolerance) return false;
        var text = string.Concat(line.Frags.Select(f => f.Text)).TrimStart();
        if (text.Length > 0 && char.IsLower(text[0])) return true;
        var last = item.Lines[^1];
        var (_, right) = ColumnBounds(pa, last);
        return last.Frags.Count > 0 && right - last.Frags[^1].R <= ShortLineEms * last.Size;
    }

    /// <summary>Whether the line before <paramref name="line"/> ends its paragraph: in a
    /// justified column a line short of its block's right edge is a paragraph's last - the
    /// block's edge being where its other lines and the next line end, so an abstract set
    /// narrower than its column keeps its lines; and two lines in a row set at the same left
    /// edge, each much narrower than the column and ending well short of its right edge, are
    /// lines of their own (an address, a glossary column), unless the second carries a
    /// sentence on in lower case.</summary>
    private static bool EndsParagraph(PageAnalysisState pa, Line line)
    {
        if (pa.prev is not { Frags.Count: > 0 } prev || line.Frags.Count == 0) return false;
        var (left, right) = ColumnBounds(pa, prev);
        var width = right - left;
        if (width <= 0) return false;
        bool ShortOfEdge(Line l) => right - l.Frags[^1].R > ShortLineEms * l.Size;
        var blockEdge = (pa.paraLines ?? []).Where(l => l != prev && l.Frags.Count > 0).Select(l => l.Frags[^1].R)
            .Append(line.Frags[^1].R).Max();
        var shortOfBlock = blockEdge - prev.Frags[^1].R > ShortLineEms * prev.Size;
        if (ShortOfEdge(prev) && shortOfBlock && IsJustifiedColumn(pa, prev)) return true;
        if (CentredAbove(pa, line, left, right)) return true;
        var next = string.Concat(line.Frags.Select(f => f.Text)).TrimStart();
        if (next.Length > 0 && char.IsLower(next[0])) return false;
        return Math.Abs(prev.MinX - line.MinX) <= AlignTolerance && ShortOfEdge(prev) && ShortOfEdge(line)
               && Width(prev) < ShortLineShare * width && Width(line) < ShortLineShare * width;
    }

    /// <summary>Whether the open paragraph's lines are set centred - two or more of different lengths, each well in from
    /// its column's edges, its middle on the column's - and <paramref name="line"/> starts at the column's left edge: a
    /// heading set centred over the text under it (a box's title over its entries).</summary>
    private static bool CentredAbove(PageAnalysisState pa, Line line, double left, double right)
    {
        if (pa.paraLines is not { Count: >= 2 } lines || lines.Any(l => l.Frags.Count == 0)) return false;
        var tol = Math.Max(EdgeToleranceFloor, EdgeToleranceEms * line.Size);
        var centre = (left + right) / 2;
        bool Centred(Line l) => Math.Abs((l.MinX + l.Frags[^1].R) / 2 - centre) <= tol
                                && l.MinX - left > InsetTolerances * tol && right - l.Frags[^1].R > InsetTolerances * tol;
        // Lines of different lengths: one line's middle falls on the column's by chance.
        return lines.Max(l => l.MinX) - lines.Min(l => l.MinX) > tol
               && lines.All(Centred) && line.MinX - left <= tol && !Centred(line);
    }

    /// <summary>Whether a paragraph's lines are lines of their own: a block set flush right or
    /// centred whose every line is much narrower than the column (a list of names, a title
    /// page's lines) rather than one paragraph wrapped.</summary>
    private static bool LinesOfTheirOwn(PageAnalysisState pa, List<Line> lines)
    {
        if (lines.Count < 2 || lines.Any(l => l.Frags.Count == 0)) return false;
        if (LayoutOf(pa, lines) is not { Align: { } align }
            || (align != LS.AttributeName.TextAlign_End && align != LS.AttributeName.TextAlign_Center)) return false;
        var (left, right) = ColumnBounds(pa, lines[0]);
        return right - left > 0 && lines.All(l => Width(l) < ShortLineShare * (right - left));
    }

    /// <summary>Whether the column a line stands in is justified: most of its body lines run
    /// from its left edge to its right. A line ending a sentence says nothing: a paragraph's last line ends where its
    /// text does in any column. Nor does a line set in from the column's edge (a list's item, an entry): it tells how
    /// the list is set, not the column's paragraphs.</summary>
    private static bool IsJustifiedColumn(PageAnalysisState pa, Line line)
    {
        if (pa.justifiedColumns.TryGetValue(line.ColumnRight, out var known)) return known;
        var (left, right) = ColumnBounds(pa, line);
        var tol = Math.Max(EdgeToleranceFloor, EdgeToleranceEms * pa.bodySize);
        var column = pa.lines.Where(l => l.Running == 0 && l.Frags.Count > 0 && l.ColumnRight == line.ColumnRight).OrderByDescending(l => l.Y).ToList();
        // A line ending a sentence under which a paragraph starts (a gap, an indent, or nothing) is a paragraph's last
        // line, set short however the column is set: no evidence. One the text goes on under at its pitch is a line like
        // any other - short of the edge, it tells the column is ragged.
        bool EndsItsParagraph(Line l)
        {
            var text = string.Concat(l.Frags.Select(f => f.Text)).TrimEnd();
            if (text.Length == 0 || ".!?:".IndexOf(text[^1]) < 0) return false;
            var under = column.FirstOrDefault(o => o.Y < l.Y);
            return under is null || l.Y - under.Y > pa.pitch * ParagraphGapFactor || under.MinX - left > IndentStep;
        }
        var body = column.Where(l => Math.Abs(l.Size - pa.bodySize) <= 0.5 && l.MinX - left <= tol
                                     && !pa.tables.Any(t => InRegion(l, t.region)) && !EndsItsParagraph(l)).ToList();
        var hang = Math.Max(tol, HangingEms * pa.bodySize);
        // A line running from the column's left edge to its right (its punctuation perhaps hanging past it) is the
        // evidence; a line set flush right on its own says nothing about the column.
        var full = body.Count(l => right - l.Frags[^1].R <= hang && l.MinX - left <= tol);
        var justified = full >= JustifiedColumnMinLines && full >= JustifiedColumnShare * body.Count;
        pa.justifiedColumns[line.ColumnRight] = justified;
        return justified;
    }

    /// <summary>An entry of a table of contents runs from where its number starts to the page it names, at the column's
    /// edge: it is set from its start, as far in as its level puts it - never centred or flush right, whatever its ends.</summary>
    private static ParagraphLayout? TocLayout(PageAnalysisState pa, Line line)
    {
        if (line.Frags.Count == 0) return null;
        var (left, _) = ColumnBounds(pa, line);
        var indent = line.Frags[0].X - left;
        return indent > Math.Max(EdgeToleranceFloor, EdgeToleranceEms * line.Size) ? new ParagraphLayout(null, indent, 0) : null;
    }

    private static void Emit(PageAnalysisState pa, Block b, List<Line>? lines)
    {
        double before = 0;
        if (lines is not null && b.Kind is BlockKind.Paragraph or BlockKind.Heading)
        {
            b.Layout = b.Toc ? TocLayout(pa, lines[0]) : LayoutOf(pa, lines);
            before = SpaceBefore(pa, lines);
        }
        else if (lines is not null && b.Kind == BlockKind.List)
        {
            before = SpaceBefore(pa, lines);
        }
        else if (b.Kind == BlockKind.Table)
        {
            var region = pa.tables[b.TableIndex].region;
            before = SpaceAbove(pa, region.URY, pa.bodySize,
                l => l.Frags.Count > 0 && l.MinX < region.URX && l.Frags[^1].R > region.LLX,
                r => r.URX > region.LLX && r.LLX < region.URX);
        }
        if (before > 0) b.Layout = (b.Layout ?? new ParagraphLayout(null, 0, 0)) with { SpaceBefore = before };
        pa.result.Add(b);
        pa.blockLines.Add(lines);
    }

    private static void FlushParagraph(PageAnalysisState pa)
    {
        if (pa.paraLines is not { Count: > 0 } lines) return;
        pa.paraLines = null;
        // Lines of their own keep the alignment the block showed: one name alone says nothing
        // about where the list is set.
        var own = LinesOfTheirOwn(pa, lines);
        var align = own ? LayoutOf(pa, lines)?.Align : null;
        foreach (var block in own ? lines.Select(l => new List<Line> { l }) : [lines])
        {
            var id = pa.run.NextBlockId++;
            foreach (var l in block) l.BlockId = id;
            Emit(pa, new Block { Id = id, Kind = BlockKind.Paragraph, Y = block[^1].Y, SortTop = block[0].Y + block[0].Size, Lines = block }, block);
            if (align is null) continue;
            var emitted = pa.result[^1];
            emitted.Layout = (emitted.Layout ?? new ParagraphLayout(null, 0, 0)) with { Align = align, StartIndent = 0, TextIndent = 0 };
            pa.result[^1] = emitted;
        }
    }

    /// <summary>Closes the open list. Numbered items whose later lines start where their numbers do are no list
    /// but paragraphs opening with a number (a form's line numbers before their run-in titles): each is a
    /// paragraph, the last the open one, which the text after it may go on (<paramref name="goesOn"/>).</summary>
    private static void FlushList(PageAnalysisState pa, bool goesOn = false)
    {
        if (pa.listItems is { Count: > 0 } numbered && pa.paraLines is null && numbered.Any(i => i.Lines.Count > 1)
            && numbered.All(i => NumberLabelled(i.Lines[0])) && numbered.Where(i => i.Lines.Count > 1).All(NumberedParagraph))
        {
            pa.listItems = null;
            pa.item = null;
            for (var i = 0; i < numbered.Count; i++)
            {
                pa.paraLines = numbered[i].Lines;
                if (!goesOn || i < numbered.Count - 1) FlushParagraph(pa);
            }
            return;
        }
        if (pa.listItems is not { Count: > 0 } items) { pa.listItems = null; pa.item = null; return; }
        var id = pa.run.NextBlockId++;
        for (var i = 0; i < items.Count; i++)
            foreach (var l in items[i].Lines) { l.BlockId = id; l.Item = i; }
        var first = items[0].Lines[0];
        var last = items[^1].Lines[^1];
        Emit(pa, new Block { Id = id, Kind = BlockKind.List, Items = items, Y = last.Y, SortTop = first.Y + first.Size },
            items.SelectMany(i => i.Lines).ToList());
        pa.listItems = null;
        pa.item = null;
    }

    /// <summary>Whether a line opening a list item is labelled with a number or a letter, not a bullet.</summary>
    private static bool NumberLabelled(Line line)
        => line.Frags.Count > 0 && char.IsLetterOrDigit(line.Frags[0].Text.TrimStart().FirstOrDefault());

    /// <summary>Whether a list's item is a paragraph opening with a number: its label is a number or a letter
    /// (no bullet), and its lines after the first start where the label does, not where its text does.</summary>
    private static bool NumberedParagraph(ListItem item)
    {
        if (!NumberLabelled(item.Lines[0])) return false;
        return item.BodyX - item.LabelX > AlignTolerance
               && item.Lines.Skip(1).All(l => Math.Abs(l.MinX - item.LabelX) <= AlignTolerance);
    }

    /// <summary>Where a list item's body text starts when the line opens a list item (its first
    /// fragment is a label, or begins with one); null for any other line.</summary>
    private static double? ListBodyX(Line line)
    {
        if (line.Frags.Count == 0) return null;
        var first = line.Frags[0];
        // A line holding its bullet again further along is entries set across it in columns, no list item (a number
        // again at a line's end is the entry's line number).
        if (LabelOnly.IsMatch(first.Text) && !first.Text.Any(char.IsLetterOrDigit)
            && line.Frags.Skip(1).Any(f => f.Text.Trim() == first.Text.Trim())) return null;
        if (LabelOnly.IsMatch(first.Text) && line.Frags.Count >= 2 && line.Frags[1].X >= first.R - 1)
            return line.Frags[1].X;
        var lead = LabelLead.Match(first.Text);
        if (lead.Success && first.Text.Length > 0)
        {
            // The label and its text share one fragment: estimate the body start by the share
            // of characters before the text.
            var labelChars = lead.Length - 1;
            return first.X + (first.R - first.X) * labelChars / first.Text.Length;
        }
        return null;
    }

    /// <summary>The usual distance between consecutive body-size lines (their median, capped
    /// so a page of one-line paragraphs still shows its gaps).</summary>
    private static double TypicalPitch(List<Line> lines, double bodySize)
    {
        if (bodySize <= 0) return 12;
        var diffs = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            if (Math.Abs(lines[i].Size - bodySize) > 0.5 || Math.Abs(lines[i - 1].Size - bodySize) > 0.5) continue;
            var d = lines[i - 1].Y - lines[i].Y;
            if (d > 0 && d < 2.5 * bodySize) diffs.Add(d);
        }
        var cap = MaxPitchFactor * bodySize;
        if (diffs.Count == 0) return cap;
        diffs.Sort();
        return Math.Min(diffs[diffs.Count / 2], cap);
    }

    /// <summary>A text table's row edges, bottom to top: below the last row, between rows, above the first. A row whose
    /// label wraps runs from its first line down to its last: the edges beside it stand midway to those, not to the
    /// middle of the row.</summary>
    private static List<double> RowEdges(List<Line> lines, int from, int to, double size)
    {
        static double First(Line l) => double.IsNaN(l.FirstBaseline) ? l.Baseline : l.FirstBaseline;
        static double Last(Line l) => double.IsNaN(l.LastBaseline) ? l.Baseline : l.LastBaseline;
        var rowY = new List<double> { Last(lines[to - 1]) - size };
        for (var r = to - 1; r > from; r--)
            rowY.Add((First(lines[r]) + Last(lines[r - 1])) / 2 + 0.3 * size);
        rowY.Add(TopEdge(lines, from, First(lines[from]) + size));
        return rowY;
    }

    private static bool InRegion(Line line, Aspose.Pdf.Rectangle region) =>
        line.Y >= region.LLY - 2 && line.Y <= region.URY + 2
        && line.MinX <= region.URX && (line.Frags.Count == 0 || line.Frags[^1].R >= region.LLX);

    // A page gutter: an x-stretch at most this share of the page's lines cross, with text left of it
    // on at least GutterBothSides of them and right of it on as many.
    private const double GutterCrossShare = 0.05;
    private const double GutterBothSides = 0.3;
    // ... and lines with text on one side of it outnumber those with text on both this many times.
    private const double OneSidedFactor = 2.0;
    // A gutter is shown by at least this many lines (a table's title and a page footer show none).
    private const int MinGutterLines = 8;
    // ... and at least this share of the lines beyond it start within this many points of its end.
    private const double AlignedStartShare = 0.6;
    private const double ColumnMarginTolerance = 2.5;
    // A column of cells starting within this many points of a gutter's end is the page's next column of text.
    private const double ColumnStartTolerance = 6.0;
    // The column before a gutter is filled: its typical line reaches this share of the way to the gutter
    // (ragged prose does; a glossary's terms, even long ones, fall short).
    private const double ColumnFill = 0.9;

    /// <summary>The page's gutters between columns of text: x-stretches at least
    /// <see cref="MinGutter"/> wide that almost no line crosses while many lines have text on either side
    /// of it. Two page columns side by side are no table, though their lines line up.</summary>
    private static List<(double L, double R)> PageGutters(List<Line> lines)
    {
        var rows = lines.Select(l => l.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).Select(f => (L: f.X, f.R)).ToList())
            .Where(r => r.Count > 0).ToList();
        var gutters = new List<(double L, double R)>();
        if (rows.Count < MinGutterLines) return gutters;
        var left = rows.Min(r => r.Min(f => f.L));
        var right = rows.Max(r => r.Max(f => f.R));
        double? start = null;
        for (var x = left; x <= right + 1; x += 1)
        {
            var crossed = rows.Count(r => r.Any(f => f.L < x && f.R > x));
            var before = rows.Count(r => r.Any(f => f.R <= x));
            var after = rows.Count(r => r.Any(f => f.L >= x));
            var both = rows.Count(r => r.Any(f => f.R <= x) && r.Any(f => f.L >= x));
            // Two page columns rarely share a baseline: most of their lines stand on one side only.
            // A table's rows have text on both sides of its gaps.
            var open = x <= right && crossed <= GutterCrossShare * rows.Count
                       && Math.Min(before, after) >= GutterBothSides * rows.Count
                       && before + after - 2 * both >= OneSidedFactor * both;
            if (open) start ??= x;
            else if (start is { } s)
            {
                // A column of text beyond it starts at one margin, where the gutter ends (right-aligned
                // figures of a table start anywhere).
                var starts = rows.Select(r => r.Where(f => f.L >= s).Select(f => f.L).DefaultIfEmpty(double.NaN).Min())
                    .Where(v => !double.IsNaN(v)).ToList();
                var aligned = starts.Count(v => Math.Abs(v - x) <= ColumnMarginTolerance);
                // ... and the column before it is filled: its lines typically run up to the gutter (a
                // glossary's terms leave most of their column empty beside their definitions).
                var ends = rows.Select(r => r.Where(f => f.R <= s).Select(f => f.R).DefaultIfEmpty(double.NaN).Max())
                    .Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
                var filled = ends.Count > 0 && (ends[ends.Count / 2] - left) >= ColumnFill * (s - left);
                if (x - s >= MinGutter && aligned >= AlignedStartShare * starts.Count && filled) gutters.Add((s, x));
                start = null;
            }
        }
        return gutters;
    }

    /// <summary>Tables without ruling lines: two or more consecutive lines that each split into
    /// the same number (two or more) of widely spaced cells, whose columns do not overlap. Over
    /// such rows, lines of fewer cells whose gaps fall on the table's column edges are its header
    /// rows with cells over several columns (each with its cells merged so); lines that had made
    /// a table of their own join the table under them.</summary>
    private static List<((Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) Table, (int Row, int Col)[,]? Cells)> DetectTextTables(
        List<Line> lines, List<(double y, double x, double w, double h, int op)> figures)
    {
        var tables = new List<((Aspose.Pdf.Rectangle, int, int, List<double>, List<double>), (int, int)[,]?)>();
        var span = new List<(int First, int Last)>(); // the lines each table takes
        lines = JoinWrappedLabels(lines);
        var cells = lines.Select(Cells).ToList();
        var i = 0;
        while (i < lines.Count)
        {
            // (a line opening with a raised note number - a footnote's - is a note's line, no row)
            var n = OpensWithRaisedMark(lines[i]) ? 1 : cells[i].Count;
            var j = i + 1;
            if (n >= 2 && ListBodyX(lines[i]) is null)
                while (j < lines.Count && cells[j].Count == n && ListBodyX(lines[j]) is null && !OpensWithRaisedMark(lines[j])
                       && lines[j - 1].Y - lines[j].Y < ParagraphPitchFactor * Math.Max(lines[j].Size, lines[j - 1].Size))
                    j++;
            if (n >= 2 && j - i >= 2 && !NumberedHeadings(lines, i, j) && TextTable(lines, cells, i, j, figures) is { } found)
            {
                (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) table = found;
                // Section labels over the rows and between their groups ("New car loans") are rows of the table.
                var (first, last) = LabelledRows(lines, cells, i, j, table.colX, span.Count > 0 ? span[^1].Last + 1 : 0);
                if (first < i || last > j)
                {
                    table = RowsOf(table, lines, cells, first, last);
                    (i, j) = (first, last);
                }
                var lead = LeadingSpanRows(lines, cells, i, table.colX);
                // The lines of the table before: all of them join, or none.
                if (lead > 0 && span.Count > 0 && i - lead <= span[^1].Last)
                {
                    if (i - lead <= span[^1].First) { tables.RemoveAt(tables.Count - 1); span.RemoveAt(span.Count - 1); }
                    else lead = i - span[^1].Last - 1;
                }
                if (lead > 0)
                {
                    table = RowsOver(table, lines, i - lead, i);
                    tables.Add((table, SpanCells(table, cells, i - lead, lead)));
                }
                else tables.Add((table, null));
                span.Add((i - lead, j - 1));
            }
            i = n >= 2 && j - i >= 2 ? j : i + 1;
        }
        return tables;
    }

    // A note's number opening its line is raised over the line's baseline by at least this share of the line's size, and
    // set smaller than the line's text by at least this much (points).
    private const double NoteMarkRiseEms = 0.2;
    private const double NoteMarkSizeStep = 0.5;

    /// <summary>Whether a line opens with a raised mark: its first text a number, set smaller than the line and raised
    /// over its baseline - a footnote's number before its text.</summary>
    private static bool OpensWithRaisedMark(Line line)
        => line.Frags.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Text)) is { } first && first.Text.Trim().All(char.IsDigit)
           && first.Size <= line.Size - NoteMarkSizeStep && first.Base >= line.Baseline + NoteMarkRiseEms * line.Size;

    // A section's number: "4", "4.1", "4.2.1.".
    private static readonly System.Text.RegularExpressions.Regex SectionNumber = new(@"^\d{1,2}(\.\d{1,2})*\.?$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Whether lines that split into cells alike are numbered headings, no table's rows: each set in bold or in
    /// italics throughout, opening with a section's number ("4  Definitions" over "4.1  Definitions and Abbreviations",
    /// "5.2.1  Authenticated Encryption Function" over "5.2.1.1  Input Data").</summary>
    private static bool NumberedHeadings(List<Line> lines, int from, int to)
        => Enumerable.Range(from, to - from).All(r => lines[r].Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList() is { Count: > 0 } frags
                                                      && (frags.All(f => IsBold(f.Font)) || frags.All(f => IsItalic(f.Font)))
                                                      && SectionNumber.IsMatch(frags[0].Text.Trim()));

    private static bool IsItalic(string? font) =>
        font is not null && (font.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0
                             || font.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>How many lines over <paramref name="from"/> are header rows of the table starting
    /// there: each within a line's pitch of the next, of two or more cells but fewer than the
    /// table's columns, within its width, every gap between its cells on one of its column edges,
    /// and reading as a header - set in bold, or text rather than figures (a row of figures with
    /// empty cells is a data row of the table before).</summary>
    private static int LeadingSpanRows(List<Line> lines, List<List<(double X, double R)>> cells, int from, List<double> colX)
    {
        var n = colX.Count - 1;
        var lead = 0;
        for (var r = from - 1; r >= 0 && lead < MaxHeaderRows; r--)
        {
            var c = cells[r];
            // A statistical table's headings stand a rule and a blank line over its first row:
            // up to this many pitches, where a paragraph's last line stands within two and a half.
            if (lines[r].Y - lines[r + 1].Y >= HeaderRowPitchFactor * Math.Max(lines[r].Size, lines[r + 1].Size)) break;
            if (c.Count < 2 || c.Count >= n || ListBodyX(lines[r]) is not null) break;
            // A title set larger than the table's text is no header row of it.
            if (lines[r].Size > HeaderSizeShare * lines[from].Size) break;
            if (c[0].X < colX[0] - AlignTolerance || c[^1].R > colX[^1] + AlignTolerance) break;
            // A heading set right over its figures may overhang its column's edge a little (tight columns).
            if (Enumerable.Range(0, c.Count - 1).Any(k => !colX.Skip(1).Take(n - 1).Any(x => x > c[k].R - AlignTolerance && x < c[k + 1].X + AlignTolerance))) break;
            var frags = lines[r].Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
            // A period - a year, a quarter, a month - heads a column as a word does.
            if (!frags.All(f => IsBold(f.Font))
                && frags.Count(f => NumericCell.IsMatch(f.Text) && !PeriodCell.IsMatch(f.Text.Trim())) >= HeaderNumericShare * frags.Count) break;
            lead++;
        }
        return lead;
    }

    // A table's heading rows stand at most this many line sizes over the row below them, set no
    // larger than this share of the table's text.
    private const double HeaderRowPitchFactor = 3.5;
    private const double HeaderSizeShare = 1.15;

    // A column heading naming a period: a year, a quarter, a half, a month, a fiscal year.
    private static readonly Regex PeriodCell = new(
        @"^(?:(?:19|20)\d\d[a-z]?|Q[1-4]|H[12]|FY\s?\d{2,4}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Sept|Oct|Nov|Dec)[a-z]*\.?(?:\s?(?:19|20)?\d\d)?)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>The table with the lines from <paramref name="newFrom"/> up to its first line added as rows over it.</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) RowsOver(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t, List<Line> lines, int newFrom, int oldFrom)
    {
        var size = lines[oldFrom].Size;
        var rowY = t.rowY.GetRange(0, t.rowY.Count - 1);
        for (var r = oldFrom; r > newFrom; r--)
            rowY.Add((lines[r].Baseline + lines[r - 1].Baseline) / 2 + 0.3 * size);
        rowY.Add(TopEdge(lines, newFrom, lines[newFrom].Baseline + size));
        var region = new Aspose.Pdf.Rectangle(t.region.LLX, t.region.LLY, t.region.URX, TopEdge(lines, newFrom, lines[newFrom].Y + size));
        return (region, t.rows + (oldFrom - newFrom), t.cols, t.colX, rowY);
    }

    /// <summary>A table's top edge: a line size over its first line, but no further than halfway
    /// to the line above it - a heading set one tight pitch over the table ("Assets" over
    /// "1 Bank credit ...") is no part of its first row.</summary>
    private static double TopEdge(List<Line> lines, int first, double wanted)
    {
        if (first == 0 || lines[first - 1].Y <= lines[first].Y) return wanted;
        return Math.Min(wanted, (lines[first - 1].Y + lines[first].Y) / 2);
    }

    /// <summary>The cells of a table whose first <paramref name="lead"/> rows have cells over
    /// several columns (<see cref="SpanRow"/>).</summary>
    private static (int Row, int Col)[,] SpanCells(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) t,
        List<List<(double X, double R)>> cells, int firstLine, int lead)
    {
        var map = SingleCells(t.rows, t.cols);
        for (var r = 0; r < lead; r++) SpanRow(map, t, r, cells[firstLine + r]);
        return map;
    }

    /// <summary>Whether a gap between a table's cells is a gutter the rows next to it keep clear: going out from
    /// the table's rows, row by row, at least <see cref="MinColumnRows"/> rows leave a channel through the gap at
    /// least <see cref="MinGutter"/> wide (narrowing as they come) with no text across it, their text beyond it a
    /// line of prose starting where the cells beyond the gap do - two columns of text side by side, not a table.</summary>
    // A gap in running text is at most this many of its size.
    private const double GutterWordGapEms = 1.0;
    // ... and a line of it runs at least this many of its size.
    private const double ProseRunEms = 8.0;
    // ... starting a paragraph's first line at most this many of its size further in.
    private const double GutterIndentEms = 2.0;

    private static bool GutterAround(List<Line> lines, int from, int to, double right, double left)
    {
        var near = Enumerable.Range(1, lines.Count).SelectMany(d => new[] { from - d, to - 1 + d })
            .Where(r => r >= 0 && r < lines.Count && (r < from || r >= to));
        var (channelL, channelR) = (right, left);
        var rows = 0;
        foreach (var r in near)
        {
            var frags = lines[r].Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
            if (frags.Count == 0) continue;
            // The row parts at its first gap a gutter wide past the table's cells before the gap: the column before may run
            // further right than those cells do (a heading of a few words beside the other column's prose).
            var ordered = frags.OrderBy(f => f.X).ToList();
            var at = Enumerable.Range(0, ordered.Count).FirstOrDefault(k => ordered[k].X > right
                && (k == 0 || (ordered[k - 1].R < left && ordered[k].X - ordered[k - 1].R >= MinGutter)), -1);
            // A row with nothing beyond the gap (a line of the column before, set on its own baseline) shows nothing.
            if (at < 0 && ordered[^1].R <= left) continue;
            if (at < 0) break;
            var run = ordered.Skip(at).ToList();
            var rest = ordered.Take(at).ToList();
            // The next column's text starts where the cells beyond the gap do, give or take an indent (a first line set in,
            // cells set in under a list's item), and is running text: no gap in it wider than a word's, as long as a line
            // of prose (a table's header row has its names apart, and a column's name is a word).
            var indent = ColumnStartTolerance + GutterIndentEms * lines[r].Size;
            if (Math.Abs(run[0].X - left) > indent) break;
            if (run.Zip(run.Skip(1), (a, b) => b.X - a.R).Any(gap => gap > GutterWordGapEms * lines[r].Size)
                || run[^1].R - run[0].X < ProseRunEms * lines[r].Size)
                break;
            var before = rest.Select(f => f.R).DefaultIfEmpty(double.MinValue).Max();
            (channelL, channelR) = (Math.Max(channelL, before), Math.Min(channelR, run[0].X));
            if (channelR - channelL < MinGutter) break;
            if (++rows >= MinColumnRows) return true;
        }
        return false;
    }

    private static (Aspose.Pdf.Rectangle, int, int, List<double>, List<double>)? TextTable(
        List<Line> lines, List<List<(double X, double R)>> cells, int from, int to,
        List<(double y, double x, double w, double h, int op)> figures)
    {
        var n = cells[from].Count;
        // The page's gutters, as the text around the rows shows them (a table's own gaps are none).
        var gutters = PageGutters(lines.Take(from).Concat(lines.Skip(to)).ToList());
        var colX = new List<double> { Enumerable.Range(from, to - from).Min(r => cells[r][0].X) - 1 };
        for (var c = 0; c < n - 1; c++)
        {
            var right = Enumerable.Range(from, to - from).Max(r => cells[r][c].R);
            var left = Enumerable.Range(from, to - from).Min(r => cells[r][c + 1].X);
            if (right >= left) return null; // columns overlap: not a table
            // The page's gutter between the cells, the next cells starting at its end where the
            // page's next column of text does: two columns of text, not a table (a table wider
            // than a column has a gap over the gutter too, its cells starting where they will).
            if (gutters.Any(g => (g.L + g.R) / 2 > right && (g.L + g.R) / 2 < left && Math.Abs(left - g.R) <= ColumnStartTolerance)) return null;
            // ... as the rows around the table show it where the page's columns share their baselines.
            if (GutterAround(lines, from, to, right, left)) return null;
            // An image in the gap: text flowing around a floated figure, not two columns.
            var top = lines[from].Y + lines[from].Size;
            var bottom = lines[to - 1].Y;
            if (figures.Any(f => f.x < left && f.x + f.w > right && f.y < top && f.y + f.h > bottom)) return null;
            colX.Add((right + left) / 2);
        }
        if (IsProse(lines, cells, from, to, colX) || CentredTextColumns(lines, cells, from, to)
            || LabelColumn(lines, cells, from, to, n) || LeaderColumn(lines, cells, from, to, n)) return null;
        colX.Add(Enumerable.Range(from, to - from).Max(r => cells[r][n - 1].R) + 1);

        // Row edges, bottom to top: below the last row, between rows, above the first.
        var size = lines[from].Size;
        var rowY = RowEdges(lines, from, to, size);
        var region = new Aspose.Pdf.Rectangle(colX[0], lines[to - 1].Y - 1, colX[^1], TopEdge(lines, from, lines[from].Y + size));
        return (region, to - from, n, colX, rowY);
    }

    // A section label stands at most this many rows over the rows it labels; rows of a table stand
    // within this many line sizes of each other (a paragraph's lines do too).
    private const int MaxLeadingLabels = 3;
    private const double ParagraphPitchFactor = 2.5;
    // A section label stands out at most this many of its size left of the first column.
    private const double LabelOutdentEms = 3.0;

    /// <summary>The lines a text table takes once its section labels are in: lines of one cell in its
    /// first column, over its rows (up to a few, each within a line's pitch of the next, none before
    /// <paramref name="floor"/>) and between groups of rows set in its columns (within a heading's pitch).
    /// Labels no rows follow close no group and stay out.</summary>
    private static (int First, int Last) LabelledRows(List<Line> lines, List<List<(double X, double R)>> cells,
        int from, int to, List<double> colX, int floor)
    {
        // A label may stand out left of the first column (the rows it labels are set in under it) and
        // reach as far as the second column's text.
        var secondLeft = Enumerable.Range(from, to - from).Min(r => cells[r][1].X);
        bool Label(int r) => cells[r].Count == 1 && ListBodyX(lines[r]) is null
                             && cells[r][0].X >= colX[0] - LabelOutdentEms * lines[r].Size && cells[r][0].R < secondLeft - AlignTolerance;
        bool Near(int above, int below, double pitches) =>
            lines[above].Y - lines[below].Y < pitches * Math.Max(lines[above].Size, lines[below].Size);
        bool InColumns(int r) => cells[r].Count == colX.Count - 1 && ListBodyX(lines[r]) is null
                                 && Enumerable.Range(0, cells[r].Count).All(c => cells[r][c].X >= colX[c] - (c == 0 ? LabelOutdentEms * lines[r].Size : AlignTolerance)
                                                                                 && cells[r][c].R <= (c == 0 ? secondLeft - AlignTolerance : colX[c + 1] + AlignTolerance));
        // Over the rows: labels (a few, each within a line's pitch of the next), and rows set in the table's
        // columns standing a group's gap over them (a total over its breakdown).
        var first = from;
        var labels = 0;
        while (first - 1 >= floor)
        {
            if (labels < MaxLeadingLabels && Label(first - 1) && Near(first - 1, first, ParagraphPitchFactor)) labels++;
            else if (!(InColumns(first - 1) && Near(first - 1, first, HeaderRowPitchFactor))) break;
            first--;
        }
        var last = to;
        while (true)
        {
            var k = last;
            while (k < lines.Count && Label(k) && Near(k - 1, k, HeaderRowPitchFactor)) k++;
            var m = k;
            while (m < lines.Count && InColumns(m) && Near(m - 1, m, HeaderRowPitchFactor)) m++;
            if (m == k) break;
            last = m;
        }
        return (first, last);
    }

    /// <summary>A text table laid over the lines <paramref name="from"/> to <paramref name="to"/>: a row per
    /// line, its columns kept but the first widened to hold every line's first cell (a label standing out).</summary>
    private static (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) RowsOf(
        (Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY) table,
        List<Line> lines, List<List<(double X, double R)>> cells, int from, int to)
    {
        var colX = new List<double>(table.colX);
        colX[0] = Math.Min(colX[0], Enumerable.Range(from, to - from).Min(r => cells[r][0].X) - 1);
        if (colX.Count > 2)
            colX[1] = Math.Max(colX[1], Enumerable.Range(from, to - from).Max(r => cells[r][0].R) + 1);
        table = (table.region, table.rows, table.cols, colX, table.rowY);
        var size = lines[from].Size;
        var rowY = RowEdges(lines, from, to, size);
        var region = new Aspose.Pdf.Rectangle(table.colX[0], lines[to - 1].Y - 1, table.colX[^1], TopEdge(lines, from, lines[from].Y + size));
        return (region, to - from, table.cols, table.colX, rowY);
    }

    // A table-of-contents line's leader: dots or dashes running from the title to the page number.
    private static readonly Regex LeaderOnly = new(@"^[\s.\u00B7\u2022\u2026_-]{3,}$", RegexOptions.Compiled);

    /// <summary>Whether one of the columns holds nothing but leaders (the dotted lines of a
    /// table of contents between the titles and their page numbers): contents lines, no table.</summary>
    private static bool LeaderColumn(List<Line> lines, List<List<(double X, double R)>> cells, int from, int to, int n)
        => Enumerable.Range(0, n).Any(c => Enumerable.Range(from, to - from).All(r =>
        {
            var (x, right) = cells[r][c];
            var text = string.Concat(lines[r].Frags.Where(f => (f.X + f.R) / 2 >= x - 1 && (f.X + f.R) / 2 <= right + 1).Select(f => f.Text));
            return LeaderOnly.IsMatch(text);
        }));

    /// <summary>Whether one of the columns holds nothing but list labels (a column of bullets
    /// between two columns of text): a list beside prose, no table.</summary>
    private static bool LabelColumn(List<Line> lines, List<List<(double X, double R)>> cells, int from, int to, int n)
    {
        for (var c = 0; c < n; c++)
        {
            var labels = true;
            for (var r = from; r < to && labels; r++)
            {
                var (x, right) = cells[r][c];
                var text = string.Concat(lines[r].Frags.Where(f => (f.X + f.R) / 2 >= x - 1 && (f.X + f.R) / 2 <= right + 1).Select(f => f.Text));
                labels = LabelOnly.IsMatch(text);
            }
            if (labels) return true;
        }
        return false;
    }

    /// <summary>The rows at the top of a page going on with the unruled table the page before
    /// ends with: its first lines while each splits into cells that stand in that table's
    /// columns (two or more of them). A table's last row alone on a page is found this way,
    /// where on its own it would read as a line of text.</summary>
    private static (Aspose.Pdf.Rectangle, int, int, List<double>, List<double>)? ContinuedTextTable(
        List<Line> lines, PageWork? previous)
    {
        if (previous is null || lines.Count == 0) return null;
        var content = previous.Lines.Where(l => l.Running == 0 && l.Frags.Count > 0).ToList();
        if (content.Count == 0) return null;
        // The table the page before ends with: the one its last line of content is in.
        var lastLine = content.OrderBy(l => l.Y).First();
        var ending = previous.Tables.Where(t => InRegion(lastLine, t.region)).ToList();
        if (ending.Count != 1) return null;
        var colX = ending[0].colX;
        var cols = ending[0].cols;

        bool InColumns(Line line)
        {
            var cells = Cells(line);
            if (cells.Count < 2) return false;
            var used = new HashSet<int>();
            foreach (var (x, r) in cells)
            {
                var c = colX.FindLastIndex(edge => edge <= x + AlignTolerance);
                if (c < 0 || c >= cols || r > colX[c + 1] + AlignTolerance || !used.Add(c)) return false;
            }
            return true;
        }

        var n = 0;
        while (n < lines.Count && InColumns(lines[n])
               && (n == 0 || lines[n - 1].Y - lines[n].Y < 2.5 * Math.Max(lines[n].Size, lines[n - 1].Size)))
            n++;
        if (n == 0) return null;

        var size = lines[0].Size;
        var rowY = new List<double> { lines[n - 1].Baseline - size };
        for (var r = n - 1; r > 0; r--)
            rowY.Add((lines[r].Baseline + lines[r - 1].Baseline) / 2 + 0.3 * size);
        rowY.Add(lines[0].Baseline + size);
        var region = new Aspose.Pdf.Rectangle(colX[0], lines[n - 1].Y - 1, colX[^1], lines[0].Y + size);
        return (region, n, cols, new List<double>(colX), rowY);
    }

    /// <summary>A grid of text centred on its columns - a paper's authors, their affiliations
    /// and addresses in columns - is no table: a table's cells start at their column's edge, or
    /// hold figures. Every column's cells share a centre, no two rows start at the same x in
    /// every column (a table's rows do, under a centred header), and no cell holds a digit.</summary>
    private static bool CentredTextColumns(List<Line> lines, List<List<(double X, double R)>> cells, int from, int to)
    {
        if (to - from < 2) return false;
        var tol = CentreTolEms * lines[from].Size;
        var n = cells[from].Count;
        for (var c = 0; c < n; c++)
        {
            var centres = Enumerable.Range(from, to - from).Select(r => (cells[r][c].X + cells[r][c].R) / 2).ToList();
            if (centres.Max() - centres.Min() > tol) return false;
        }
        for (var a = from; a < to; a++)
            for (var b = a + 1; b < to; b++)
                if (Enumerable.Range(0, n).All(c => Math.Abs(cells[a][c].X - cells[b][c].X) <= AlignTolerance)) return false;
        return !Enumerable.Range(from, to - from).Any(r => lines[r].Frags.Any(f => f.Text.Any(char.IsDigit)));
    }

    /// <summary>A line's cells: its fragments merged while the gap between them is small.</summary>
    private static List<(double X, double R)> Cells(Line line)
    {
        var cells = new List<(double X, double R)>();
        foreach (var f in line.Frags)
        {
            if (string.IsNullOrWhiteSpace(f.Text)) continue;
            if (cells.Count > 0 && f.X - cells[^1].R <= CellGapFactor * line.Size)
                cells[^1] = (cells[^1].X, Math.Max(cells[^1].R, f.R));
            else
                cells.Add((f.X, f.R));
        }
        return cells;
    }

    /// <summary>Attach each link annotation to the paragraph or list whose lines it overlaps: the
    /// paragraph's content (a list item's body) then splits around the link (Link → OBJR + content). A paragraph's
    /// links are read in the order they stand in - line by line, along each line - whatever
    /// order the page lists its annotations in.</summary>
    private static void AttachLinks(PageAnalysisState pa, PageWork pw)
    {
        var homes = new Dictionary<int, List<(Aspose.Pdf.Core.PdfObject Ref, Aspose.Pdf.Rectangle Rect)>>();
        foreach (var linkRef in GetLinkRefs(pa.page))
        {
            if (LinkRect(pa.page, linkRef) is not { } userRect) continue;
            var rect = PageContentScan.Shown(userRect, PageContentScan.ShownMatrix(pa.page.Reader, pa.page.Dict));
            // The paragraph whose lines the link sits on; failing that the nearest one, so every
            // link annotation has its Link element (PDF/UA-1 §7.18.5).
            var cy = (rect.LLY + rect.URY) / 2;
            var cx = (rect.LLX + rect.URX) / 2;
            var home = -1;
            var bestDistance = double.MaxValue;
            for (var i = 0; i < pa.result.Count; i++)
            {
                if (pa.result[i].Kind is not (BlockKind.Paragraph or BlockKind.List) || pa.blockLines[i] is not { } candidate) continue;
                var d = candidate.Min(l => LinkDistance(l, cx, cy));
                if (d < bestDistance) { bestDistance = d; home = i; }
            }
            if (home < 0) continue;
            if (!homes.TryGetValue(home, out var held)) homes[home] = held = [];
            held.Add((linkRef, rect));
        }
        foreach (var (home, held) in homes)
        {
            var b = pa.result[home];
            var lines = b.Lines ?? pa.blockLines[home]!;
            // The links standing on the paragraph's lines, in the order they stand in, split its text; one standing
            // on none of them (the nearest paragraph took it in) follows them, as the page lists it, splitting nothing.
            var ordered = held.Select((link, listed) => (link, listed, line: LinkLine(lines, link.Rect)))
                .OrderBy(l => l.line < 0 ? int.MaxValue : l.line).ThenBy(l => l.line < 0 ? l.listed : 0)
                .ThenBy(l => l.link.Rect.LLX).Select(l => l.link).ToList();
            foreach (var (linkRef, rect) in ordered)
            {
                // A link wrapped over two lines is two annotations with one target, nothing between them - the first ending
                // its line, the second starting the next: one Link. Two links to one place with text between are two.
                var target = LinkTarget(pa.page, linkRef);
                var group = b.Links is { Count: > 0 } prior && target is not null
                    && Equals(target, LinkTarget(pa.page, prior[^1])) && Wrapped(lines, b.LinkRects![^1], rect)
                    ? b.LinkGroups![^1] : (b.LinkGroups?.Count > 0 ? b.LinkGroups[^1] + 1 : 0);
                (b.Links ??= []).Add(linkRef);
                (b.LinkRects ??= []).Add(rect);
                (b.LinkGroups ??= []).Add(group);
            }
            pa.result[home] = b;
        }
    }

    /// <summary>The item of a list block a link stands in: the one whose lines the link's box is on, or the last item
    /// (a link on none of its lines follows the text).</summary>
    private static int ItemOfLink(Block b, int link)
    {
        var items = b.Items ?? [];
        for (var i = 0; i < items.Count; i++)
            if (LinkLine(items[i].Lines, b.LinkRects![link]) >= 0) return i;
        return items.Count - 1;
    }

    /// <summary>The links of a list block standing in its item <paramref name="item"/>, in reading order.</summary>
    private static IEnumerable<int> ItemLinks(Block b, int item)
        => Enumerable.Range(0, (b.LinkRects ?? []).Count).Where(k => ItemOfLink(b, k) == item);

    // A link's box ends its line, or starts one, within this many of the line's size of the line's text's end or start.
    private const double WrappedLinkEms = 1.0;

    /// <summary>Whether a link's box goes on in the next: the first stands at the end of a line of the paragraph, the
    /// second at the start of the line after it - or either stands on none of the paragraph's lines, which tells nothing.</summary>
    private static bool Wrapped(List<Line> lines, Aspose.Pdf.Rectangle first, Aspose.Pdf.Rectangle next)
    {
        var (a, b) = (LinkLine(lines, first), LinkLine(lines, next));
        if (a < 0 || b < 0) return true;
        if (b != a + 1) return false;
        var (end, start) = (lines[a], lines[b]);
        return end.Frags.Count > 0 && start.Frags.Count > 0
               && first.URX >= end.Frags[^1].R - WrappedLinkEms * end.Size && next.LLX <= start.MinX + WrappedLinkEms * start.Size;
    }

    /// <summary>How far a link's centre stands from a line: nothing when the line holds it, else
    /// how far it is past the line's box - beside it, in the other column of a page, as much as
    /// above or below it.</summary>
    private static double LinkDistance(Line l, double cx, double cy)
    {
        var dy = cy >= l.Y - 2 && cy <= l.Y + l.Size + 2 ? 0 : Math.Min(Math.Abs(cy - l.Y), Math.Abs(cy - l.Y - l.Size));
        var right = l.Frags.Count > 0 ? l.Frags[^1].R : l.MinX;
        var dx = cx < l.MinX - 2 ? l.MinX - cx : cx > right + 2 ? cx - right : 0;
        return dy + dx;
    }

    /// <summary>A paragraph cut by a page break is one paragraph: the last paragraph of a page
    /// and the first of the next join when the text visibly continues (see
    /// <see cref="ContinuesAcrossPages"/>). The later one's content becomes more content of the
    /// earlier one. A formula is set apart from the text: it joins nothing; nor does a paragraph whose first line
    /// is set in from its second, which opens it.</summary>
    private static void JoinPageBreakParagraphs(List<Block> blocks, TaggingRun run)
    {
        for (var i = blocks.Count - 1; i > 0; i--)
        {
            var b = blocks[i];
            if (b.Kind != BlockKind.Paragraph || b.Note || b.Formula) continue;
            // The notes at the foot of the page before stand between the paragraph's parts.
            var j = i - 1;
            while (j > 0 && blocks[j].Note && run.PageOfBlock[blocks[j].Id] != run.PageOfBlock[b.Id]) j--;
            var a = blocks[j];
            if (a.Kind != BlockKind.Paragraph || a.Note || a.Formula) continue;
            var pageA = run.PageOfBlock[a.Id];
            if (pageA == run.PageOfBlock[b.Id]) continue;
            if (b.Links is { Count: > 0 } || b.InlineFigs is { Count: > 0 }) continue;
            if (a.Lines is not { Count: > 0 } la || b.Lines is not { Count: > 0 } lb) continue;
            if (lb.Count > 1 && lb[0].MinX - lb[1].MinX > IndentEms * lb[0].Size) continue;
            if (!ContinuesAcrossPages(la[^1], lb[0], pageA)) continue;
            a.Joined = [b.Id, .. b.Joined ?? []];
            blocks[j] = a;
            blocks.RemoveAt(i);
        }
    }

    // Column edges of two parts of one table agree within this many points or this share of
    // the table's width, whichever is more.
    private const double ColumnEdgeTolerance = 3.0;
    private const double ColumnEdgeShare = 0.02;
    // At most this many notes of at most this many lines each may stand between two parts of a table.
    private const int MaxNotesBetweenParts = 3;
    private const int MaxNoteLines = 2;

    /// <summary>A table cut by a page break is one table: a table ending one page and one
    /// starting the next with the same columns (their edges where the first table's are) join,
    /// the later one's rows becoming more rows of the earlier one. A first row repeating the
    /// earlier table's header row is noted: it is pagination, not content.</summary>
    private static void JoinPageBreakTables(List<Block> blocks, TaggingRun run)
    {
        for (var i = blocks.Count - 1; i > 0; i--)
        {
            var b = blocks[i];
            if (b.Kind != BlockKind.Table) continue;
            // A few short notes may stand between the parts (a table's footnote and "Continued" at
            // the foot of the page, a "continued" title over the next part): they stay where they are, after the table.
            var j = i - 1;
            while (j >= 0 && i - 1 - j < MaxNotesBetweenParts
                   && (blocks[j].Kind == BlockKind.Heading || blocks[j] is { Kind: BlockKind.Paragraph, Lines.Count: > 0 and <= MaxNoteLines })) j--;
            if (j < 0 || blocks[j].Kind != BlockKind.Table) continue;
            var a = blocks[j];
            var pageA = run.PageOfBlock[a.Id];
            var pageB = run.PageOfBlock[b.Id];
            if (a.Cols != b.Cols) continue;
            var ta = pageA.Tables[a.TableIndex];
            var tb = pageB.Tables[b.TableIndex];
            var tolerance = Math.Max(ColumnEdgeTolerance, ColumnEdgeShare * (ta.colX[^1] - ta.colX[0]));
            bool Agree(double shift) => ta.colX.Count == tb.colX.Count
                && ta.colX.Zip(tb.colX, (x, y) => Math.Abs(x + shift - y)).All(d => d <= tolerance);
            // The next page, its columns where these are - or, for a table set in halves side by side
            // (SplitSideBySide), the half to the right on the same rows, or the next page's first half:
            // the same columns, standing elsewhere.
            var nextPage = pageB.Page.Number == pageA.Page.Number + 1;
            // Over notes, only the same table goes on: its halves, or a part repeating its first row.
            if (j < i - 1 && (!nextPage || !(pageA.SideBySide.Contains(a.TableIndex) || pageB.SideBySide.Contains(b.TableIndex)
                || (RowText(pageA, a.TableIndex, 0) is { Length: > 0 } firstRow && firstRow == RowText(pageB, b.TableIndex, 0))))) continue;
            var besideIt = ReferenceEquals(pageA, pageB) && tb.colX[0] >= ta.colX[^1] - tolerance
                           && ta.rowY.SequenceEqual(tb.rowY);
            var halves = pageA.SideBySide.Contains(a.TableIndex) || pageB.SideBySide.Contains(b.TableIndex);
            bool AlikeWidths() => ta.colX.Count == tb.colX.Count && Enumerable.Range(0, ta.colX.Count - 1).All(c =>
                Math.Abs(ta.colX[c + 1] - ta.colX[c] - (tb.colX[c + 1] - tb.colX[c]))
                <= Math.Max(HalfWidthTolerance, HalfWidthShare * (ta.colX[c + 1] - ta.colX[c])));
            if (!(nextPage && Agree(0)) && !(halves && (besideIt || nextPage) && AlikeWidths())) continue;
            // The header rows the part repeats: its leading rows reading as the first table's.
            var repeated = 0;
            while (repeated < pageA.TableHeaders[a.TableIndex].Rows && repeated < b.Rows - 1
                   && RowText(pageA, a.TableIndex, repeated) is { Length: > 0 } header && header == RowText(pageB, b.TableIndex, repeated))
                repeated++;
            if (repeated > 0) pageB.RepeatedHeaders[b.Id] = repeated;
            a.Joined = [b.Id, .. b.Joined ?? []];
            blocks[j] = a;
            blocks.RemoveAt(i);
        }
    }

    /// <summary>The text of a table's grid row (from the top), words joined by single spaces.</summary>
    private static string RowText(PageWork pw, int tableIndex, int row)
    {
        var t = pw.Tables[tableIndex];
        var top = t.rowY[t.rows - row];
        var bottom = t.rowY[t.rows - 1 - row];
        var words = pw.Lines
            .SelectMany(l => l.Frags)
            .Where(f => f.Base + 0.3 * f.Size is var cy && cy >= bottom && cy < top && (f.X + f.R) / 2 >= t.colX[0] && (f.X + f.R) / 2 <= t.colX[^1])
            .OrderBy(f => f.X)
            .SelectMany(f => f.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return string.Join(" ", words);
    }

    /// <summary>The next page's first line continues the last line of a page when the text
    /// visibly goes on (<see cref="ContinuesAfterBreak"/>), the page's text margin being the edge
    /// its lines run to.</summary>
    private static bool ContinuesAcrossPages(Line last, Line first, PageWork page)
    {
        var margin = page.Lines.Where(l => l.Frags.Count > 0).Select(l => l.Frags[^1].R).DefaultIfEmpty(0).Max();
        return ContinuesAfterBreak(last, first, margin);
    }

    // A space between words is about this many ems wide (whether a line had room for one more word).
    private const double SpaceEms = 0.25;
    // A line starting this many ems right of the line before it is indented: a paragraph's first line.
    private const double IndentEms = 1.0;

    /// <summary>Whether the first line after a page or column break goes on with the last line
    /// before it: it does when it starts in lower case; not when it is indented like a
    /// paragraph's first line; else when the last line ran full - to within a few ems of
    /// <paramref name="rightEdge"/> without ending a sentence, or so full that the next line's
    /// first word had no room on it (a break falls after a sentence as often as anywhere else,
    /// and a paragraph's own last line rarely fills its measure).</summary>
    private static bool ContinuesAfterBreak(Line last, Line first, double rightEdge)
    {
        var next = string.Concat(first.Frags.Select(f => f.Text)).TrimStart();
        if (next.Length > 0 && char.IsLower(next[0])) return true;
        var text = string.Concat(last.Frags.Select(f => f.Text)).TrimEnd();
        if (text.Length == 0 || last.Frags.Count == 0 || rightEdge <= 0) return false;
        if (first.MinX - last.MinX > IndentEms * first.Size) return false;
        var end = last.Frags[^1].R;
        if (".!?:;".IndexOf(text[^1]) < 0 && end >= rightEdge - FullLineEms * last.Size) return true;
        return end + SpaceEms * last.Size + FirstWordWidth(first) > rightEdge;
    }

    /// <summary>The width of a line's first word: its share of the characters of the fragment holding it.</summary>
    private static double FirstWordWidth(Line line)
    {
        var frag = line.Frags.FirstOrDefault(f => !string.IsNullOrWhiteSpace(f.Text));
        if (frag.Text is null) return 0;
        var word = frag.Text.TrimStart().Split(' ', 2)[0];
        return (frag.R - frag.X) * word.Length / Math.Max(1, frag.Text.Length);
    }

    /// <summary>What a link annotation leads to (its URI, or its destination), for telling
    /// the pieces of one wrapped link apart from two links; null when it cannot be read.</summary>
    private static object? LinkTarget(Page page, Aspose.Pdf.Core.PdfObject linkRef)
    {
        var reader = page.Reader;
        if (reader.ResolveDict(linkRef) is not { } annot) return null;
        if (reader.ResolveDict(annot.Get("A")) is { } action)
            return reader.Resolve(action.Get("URI")) is Aspose.Pdf.Core.PdfString uri ? uri.ToText()
                : reader.Resolve(action.Get("D"));
        // A destination compares by identity: the same destination object.
        return reader.Resolve(annot.Get("Dest"));
    }

    // Running headers and footers live in this share of the page height at its top / bottom.
    private const double RunningBand = 0.1;
    // Lines closer than this many times their size are one block (a title over its subtitle).
    private const double TuckedLeading = 1.8;
    // A page number on its own: "7", "- 7 -", "Page 7", "7 of 12".
    private static readonly Regex PageNumber = new(
        @"^\W*(page\s*)?\d{1,4}(\s*(of|/)\s*\d{1,4})?\W*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    // ... or a well-formed roman numeral in one case: "iv", "XII" (not a word like "did").
    private static readonly Regex RomanPageNumber = new(
        @"^\W*(?=[ivxlcdm]+\W*$|[IVXLCDM]+\W*$)(?i:m{0,3}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3}))\W*$",
        RegexOptions.Compiled);
    private static readonly Regex Digits = new(@"\d+", RegexOptions.Compiled);

    /// <summary>Mark running headers and footers: a line in the top or bottom band of the page
    /// whose text (digits aside) recurs on at least half the pages of a multi-page document - or
    /// on half of its odd pages, or of its even ones, standing on those only - or a lone page
    /// number in either band.</summary>
    private static void MarkRunningLines(List<(Page Page, List<Line> Lines)> pages)
    {
        var bands = new List<(Line Line, bool Top, string Key, int Page)>();
        for (var p = 0; p < pages.Count; p++)
        {
            var (page, lines) = pages[p];
            var rect = page.GetPageRect(true); // lines stand as the page is shown
            var band = rect.Height * RunningBand;
            foreach (var line in lines)
            {
                var top = line.Y > rect.URY - band;
                if (!top && line.Y + line.Size >= rect.LLY + band) continue;
                var text = string.Join(" ", line.Frags.Select(f => f.Text.Trim())).Trim();
                bands.Add((line, top, Digits.Replace(text, "#"), p));
            }
        }

        var needed = Math.Max(2, (pages.Count + 1) / 2);
        // The odd pages and the even ones may each have a line of their own (the title at the outer edge, the
        // page's number at the other): a line standing on odd pages only, or on even ones only, recurs when
        // half of those pages have it.
        int NeededOf(int parity) => Math.Max(2, ((pages.Count + 1 - parity) / 2 + 1) / 2);
        var recurring = bands.GroupBy(b => (b.Top, b.Key))
            .Where(g => pages.Count >= 2 && (g.Count() >= needed
                || (g.Select(b => b.Page % 2).Distinct().Count() == 1 && g.Count() >= NeededOf(g.First().Page % 2))))
            .Select(g => g.Key).ToHashSet();
        string TextOf(Line l) => string.Join(" ", l.Frags.Select(f => f.Text.Trim())).Trim();
        // A recurring line tucked under a line that does not recur (toward the page's edge, at a
        // line's leading) is part of that block: the second line of a title that differs by page.
        bool Tucked((Line Line, bool Top, string Key, int Page) b) => bands
            .Where(o => o.Page == b.Page && o.Top == b.Top && (b.Top ? o.Line.Y > b.Line.Y : o.Line.Y < b.Line.Y))
            .OrderBy(o => Math.Abs(o.Line.Y - b.Line.Y)).FirstOrDefault() is { Line: not null } edge
            && Math.Abs(edge.Line.Y - b.Line.Y) < TuckedLeading * Math.Max(edge.Line.Size, b.Line.Size)
            && !recurring.Contains((edge.Top, edge.Key)) && !PageNumber.IsMatch(TextOf(edge.Line));
        foreach (var b in bands)
        {
            var (line, top, key, _) = b;
            var text = TextOf(line);
            if ((recurring.Contains((top, key)) && !Tucked(b)) || PageNumber.IsMatch(text)
                || (pages.Count >= 2 && RomanPageNumber.IsMatch(text)))
                line.Running = top ? ContentMarker.HeaderArtifact : ContentMarker.FooterArtifact;
        }
    }

    /// <summary>Columns of running prose, not a table: in every column the typical cell fills
    /// most of the column's width (a table has columns of short cells), and the text runs on
    /// from line to line — many lines start in lower case, carrying a sentence over, where a
    /// table's cells each start afresh. A run under a bold header row is a table.</summary>
    private static readonly char[] SentenceEnd = ['.', '!', '?', ':', ';', ')', ']', '"', '\u201D'];

    private static bool IsProse(List<Line> lines, List<List<(double X, double R)>> cells, int from, int to, List<double> colX)
    {
        if (lines[from].Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).All(f => IsBold(f.Font))) return false;
        var full = true;
        for (var c = 0; c + 1 < colX.Count; c++)
        {
            var width = colX[c + 1] - colX[c];
            var fills = Enumerable.Range(from, to - from).Select(r => (cells[r][c].R - cells[r][c].X) / width).OrderBy(v => v).ToList();
            if (fills[fills.Count / 2] < ProseFill) return false;
            if (fills[fills.Count / 2] < ProseColumnsFill || width < ProseColumnMinEms * lines[from].Size) full = false;
        }
        var starts = new List<bool>();
        var open = 0;
        var texts = 0;
        for (var r = from; r < to; r++)
            foreach (var cell in cells[r])
            {
                var text = string.Concat(lines[r].Frags.Where(f => f.X >= cell.X - 0.5 && f.R <= cell.R + 0.5).Select(f => f.Text)).Trim();
                if (text.Length == 0) continue;
                texts++;
                if (char.IsLetter(text[0])) starts.Add(char.IsLower(text[0]));
                if (!SentenceEnd.Contains(text[^1])) open++;
            }
        // Wide columns each filled to their edges whose cells mostly end mid-sentence: two page
        // columns, or the abstract set narrower beside the body (their cells being lines cut
        // from their sentences; a table's cells each say their piece).
        if (full && texts > 0 && open >= ProseOpenEnds * texts) return true;
        return starts.Count > 0 && starts.Count(s => s) >= ProseContinuation * starts.Count;
    }
}
