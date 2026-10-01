using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Core;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    private enum SlotKind { Text, Figure, Cell, Link, Label, Body }

    /// <summary>One marked-content reference of the tree and the page content it stands for:
    /// a text run of a block (a heading, or one segment of a paragraph split by in-line
    /// figures or links), a figure's image, a table cell or a link's text.</summary>
    private sealed class Slot
    {
        public SlotKind Kind;
        public int BlockId;
        public int Segment;
        public int Link = -1;
        public int Fig = -1;
        public int Row, Col;
        public string Tag = "P";
        public LS.MCRElement El = null!;
        public List<Mark> Marks = [];
    }

    /// <summary>One marked-content sequence a slot received: its MCID in the page's own content
    /// or, when the content is a form XObject's, in the form's (named by the reference the page
    /// draws it by, the /Stm of the marked-content reference).</summary>
    private readonly record struct Mark(Page Page, int Mcid, PdfStream? Form = null, PdfObject? FormRef = null);

    /// <summary>A Link element's object reference to its annotation.</summary>
    private sealed class LinkSlot
    {
        public LS.OBJRElement Objr = null!;
        public LS.StructureElement Link = null!; // the element owning the annotation (Link, Form or Annot)
        public Page? Page;                      // its page, when no block names it
        public PdfObject Annot = null!;
        public int BlockId;
    }

    /// <summary>Mark every page's content for the tree <see cref="BuildTree"/> authored, then
    /// turn the tree's placeholders into real references: MCRs naming their MCIDs and pages,
    /// OBJRs naming their annotations, and the /ParentTree that maps content back.</summary>
    private static void MarkContent(Document document, LS.StructureElement root, TaggingRun run)
    {
        var slotIndex = new Dictionary<(int, SlotKind, int, int, int), int>();
        for (var s = 0; s < run.Slots.Count; s++)
            slotIndex[Key(run.Slots[s])] = s;

        foreach (var pw in run.Pages)
        {
            if (pw.Ops.Count == 0) continue;
            var targets = AssignTargets(pw, slotIndex);
            CarryFigureAlternates(pw, targets, run, run.Carried);
            // Each content stream is rewritten on its own - the page's, then every form looked
            // into - each numbering its MCIDs from 0 under a parent-tree key of its own.
            for (var s = 0; s <= pw.Forms.Count; s++)
                MarkStream(pw, s, targets, run);
        }

        FinishReferences(document, run);
        WireParentTree(document, root, run);
    }

    private static void MarkStream(PageWork pw, int stream, int[] targets, TaggingRun run)
    {
        var indices = Enumerable.Range(0, pw.Ops.Count).Where(i => pw.Ops[i].Stream == stream).ToList();
        var ops = indices.Select(i => pw.Ops[i]).ToList();
        var own = indices.Select(i => targets[i]).ToArray();
        var form = stream == 0 ? null : pw.Forms[stream - 1];
        // What the old tree said is keyed by the page's MCIDs: a form's marks are its own.
        var opens = new Dictionary<int, int>();
        var ranks = ReadingRanks(pw, ops, own);
        var (content, mcids) = ContentMarker.Rewrite(form?.Bytes ?? pw.Bytes, ops, own, s => run.Slots[s].Tag,
            form is null ? op => CarriedFor(op, pw.Page.Dict, run.Carried) : null, opens, ranks);
        if (form is null) pw.Page.SetContentStream(content);
        else ReplaceFormContent(form.Stream, content);
        foreach (var (s, list) in mcids)
            foreach (var mcid in InReadingOrder(ops, list, opens, ranks))
                run.Slots[s].Marks.Add(new Mark(pw.Page, mcid, form?.Stream, form?.Ref));
    }

    /// <summary>A slot's sequences in the order their text is read: by the place of the text each opens with (see
    /// <see cref="ReadingRanks"/>) - a paragraph's text drawn out of that order (rows of links drawn column by column, a
    /// label drawn after its line's text) reads as it is set. Sequences of which one opens with no text of a line keep the
    /// order they are drawn in.</summary>
    private static List<int> InReadingOrder(List<ContentOp> ops, List<int> mcids, Dictionary<int, int> opens, Func<int, int?> ranks)
    {
        if (mcids.Count < 2) return mcids;
        var keys = new List<(int Mcid, int Read)>();
        foreach (var mcid in mcids)
        {
            // Its own first text: shown before the next sequence of the stream opens (a sequence of a drawing alone - an
            // entry blank - has none).
            var from = opens.TryGetValue(mcid, out var at) ? at : -1;
            var end = opens.Values.Where(o => o > from).DefaultIfEmpty(ops.Count).Min();
            var show = from < 0 ? -1 : ops.FindIndex(from, end - from, op => op.Kind == ContentOpKind.TextShow);
            if (show < 0 || ranks(show) is not { } read) return mcids;
            keys.Add((mcid, read));
        }
        return keys.OrderBy(k => k.Read).Select(k => k.Mcid).ToList();
    }

    /// <summary>Each text show's place in reading order among the shows of its target (null: on no line) - the marker keeps
    /// a sequence to text read one piece after another. By row (see <see cref="ReadAt"/>); within a row the lines in the
    /// order they are drawn, and along a line from its start (a label drawn after the text beside it).</summary>
    private static Func<int, int?> ReadingRanks(PageWork pw, List<ContentOp> ops, int[] targets)
    {
        var ranks = new Dictionary<int, int>();
        var placed = Enumerable.Range(0, ops.Count).Where(i => ops[i].Kind == ContentOpKind.TextShow)
            .Select(i => (Op: i, Row: ReadAt(pw, ops[i]), Line: LineOf(pw.Lines, ops[i])))
            .Where(x => x.Row is not null && x.Line is not null).ToList();
        foreach (var group in placed.GroupBy(x => targets[x.Op]))
        {
            // Where each line's text of the target is first drawn.
            var drawn = new Dictionary<Line, int>(ReferenceEqualityComparer.Instance);
            foreach (var x in group)
                if (!drawn.TryGetValue(x.Line!, out var first) || x.Op < first) drawn[x.Line!] = x.Op;
            // Along a line, the shows on one baseline from its start; shows of one line on baselines apart (a heading's
            // lines the page reads as one) as they are drawn.
            // A mark set smaller, raised or lowered off the text beside it (the 1 of a ½ built of glyphs), is on its baseline -
            // and on the baseline that text is on (an index lowered under a line a raised note number opens).
            bool OneBaseline(ContentOp a, ContentOp b)
            {
                var (small, large) = a.Size < b.Size ? (a, b) : (b, a);
                var off = Math.Abs(a.Y - b.Y);
                return off <= SameBaselineEms * large.Size
                       || (small.Size < MarkSizeShare * large.Size && off <= MarkOffsetEms * large.Size);
            }
            var baseline = new Dictionary<int, int>();
            foreach (var x in group.OrderBy(x => x.Op))
                baseline[x.Op] = group.Where(o => o.Op <= x.Op && ReferenceEquals(o.Line, x.Line) && OneBaseline(ops[o.Op], ops[x.Op]))
                    .Min(o => o.Op == x.Op ? x.Op : baseline[o.Op]);
            var rank = 0;
            foreach (var x in group.OrderBy(x => x.Row).ThenBy(x => drawn[x.Line!]).ThenBy(x => baseline[x.Op]).ThenBy(x => ops[x.Op].X0).ThenBy(x => x.Op))
                ranks[x.Op] = rank++;
        }
        return i => ranks.TryGetValue(i, out var r) ? r : null;
    }


    // Shows whose baselines stand within this share of their size of each other are on one baseline.
    private const double SameBaselineEms = 0.2;
    // A mark set at most this share of the text's size, at most this many of its size off the text's baseline, is on it.
    private const double MarkSizeShare = 0.9;
    private const double MarkOffsetEms = 0.5;

    // Lines next to each other in reading order sharing this share of the smaller one's size in height stand in one row.
    private const double RowShare = 0.25;

    /// <summary>Which row a text show is read in: the place of its line among the page's lines (in reading order), lines
    /// set level with the one before them (a mark raised over a line's end, a heading set midway beside two) in its row;
    /// null for any other operation, or text on no line. Within a row the text reads in the order it is drawn.</summary>
    private static int? ReadAt(PageWork pw, ContentOp op)
    {
        if (op.Kind != ContentOpKind.TextShow || LineOf(pw.Lines, op) is not { } line) return null;
        var row = pw.Lines.IndexOf(line);
        static bool Level(Line a, Line b)
            => Math.Min(a.Y + a.Size, b.Y + b.Size) - Math.Max(a.Y, b.Y) >= RowShare * Math.Min(a.Size, b.Size);
        while (row > 0 && Level(pw.Lines[row - 1], pw.Lines[row])) row--;
        return row;
    }

    private static (int, SlotKind, int, int, int) Key(Slot s) => s.Kind switch
    {
        SlotKind.Text => (s.BlockId, s.Kind, s.Segment, 0, 0),
        SlotKind.Link => (s.BlockId, s.Kind, s.Link, 0, 0),
        // An image is named by its operation on its page; the block (unique over the document)
        // tells two pages' images apart.
        SlotKind.Figure => (s.BlockId, s.Kind, s.Fig, 0, 0),
        SlotKind.Label => (s.BlockId, s.Kind, s.Row, 0, 0),
        // (an item's body is split around its links as a paragraph's text is)
        SlotKind.Body => (s.BlockId, s.Kind, s.Row, s.Segment, 0),
        _ => (s.BlockId, s.Kind, s.Row, s.Col, 0),
    };

    /// <summary>The slot each painting operation of the page belongs to; content no block
    /// claims stays <see cref="ContentMarker.Neutral"/> and is marked as an artifact.</summary>
    private static int[] AssignTargets(PageWork pw, Dictionary<(int, SlotKind, int, int, int), int> slots)
    {
        var targets = new int[pw.Ops.Count];
        for (var i = 0; i < targets.Length; i++) targets[i] = ContentMarker.Neutral;
        var blocks = pw.Blocks.ToDictionary(b => b.Id);
        // Which block holds each image operation of the page.
        var figureBlock = new Dictionary<int, int>();
        foreach (var b in pw.Blocks)
            foreach (var op in (b.Figs ?? []).Concat(b.InlineFigs ?? []).Concat((b.HeaderFigs ?? []).Select(h => h.Fig)))
                figureBlock[op] = b.Id;

        int Find((int, SlotKind, int, int, int) key) => slots.TryGetValue(key, out var s) ? s : ContentMarker.Neutral;

        for (var i = 0; i < pw.Ops.Count; i++)
        {
            var op = pw.Ops[i];
            if (op.Kind is ContentOpKind.Image or ContentOpKind.InlineImage || op.AsFigure)
            {
                targets[i] = figureBlock.TryGetValue(i, out var owner) ? Find((owner, SlotKind.Figure, i, 0, 0)) : ContentMarker.Neutral;
                continue;
            }
            // A drawing's paths, and the text standing inside its box, are the figure's.
            var drawing = op.Drawing >= 0 ? op.Drawing : op.Kind == ContentOpKind.TextShow ? DrawingAt(pw, op.X0, op.Y) : -1;
            if (drawing >= 0)
            {
                var rep = pw.Drawings[drawing].Rep;
                targets[i] = figureBlock.TryGetValue(rep, out var owner) ? Find((owner, SlotKind.Figure, rep, 0, 0)) : ContentMarker.Neutral;
                continue;
            }
            // The rule under a sum's operand is the formula's content.
            if (op.Kind == ContentOpKind.PathPaint && SumRuleBlock(pw, op) is { } sum)
            {
                targets[i] = Find((sum, SlotKind.Text, 0, 0, 0));
                continue;
            }
            // A rule under a heading's or a paragraph's text is its underline: the text's content.
            if (op.Kind == ContentOpKind.PathPaint && UnderlinedLine(pw, op) is { } under && blocks.TryGetValue(under.BlockId, out var underlined)
                && underlined.Kind is BlockKind.Heading or BlockKind.Paragraph && underlined.Links is not { Count: > 0 } && underlined.InlineFigs is not { Count: > 0 }
                // A rule the block is ruled below with is its rule, not an underline.
                && !(underlined.RuleBox.To > underlined.RuleBox.From && underlined.RuleBox.From < op.Urx && underlined.RuleBox.To > op.Llx
                     && Math.Abs(underlined.RuleBox.At - (op.Lly + op.Ury) / 2) <= MaxBlankThickness))
            {
                targets[i] = Find((underlined.Id, SlotKind.Text, 0, 0, 0));
                continue;
            }
            // A blank to write an entry on - a rule on a line, after its text - is the line's content.
            if (op.Kind == ContentOpKind.PathPaint && EntryBlankLine(pw, op) is { } entry && blocks.TryGetValue(entry.BlockId, out var holder))
            {
                targets[i] = holder.Kind switch
                {
                    BlockKind.List when entry.Item >= 0 => Find((holder.Id, SlotKind.Body, entry.Item, 0, 0)),
                    BlockKind.Paragraph when holder.Links is { Count: > 0 } => Find((holder.Id, SlotKind.Text, LinksUpTo(holder, entry), 0, 0)),
                    BlockKind.Paragraph when holder.InlineFigs is not { Count: > 0 } => Find((holder.Id, SlotKind.Text, 0, 0, 0)),
                    _ => ContentMarker.Neutral,
                };
                continue;
            }
            if (op.Kind != ContentOpKind.TextShow) continue;
            // Text drawn too small to be seen is no line's: it stays out of the tree (see MinVisibleTextHeight).
            if (op.Size < MinVisibleTextHeight) continue;
            var line = LineOf(pw.Lines, op);
            if (line is { Running: not 0 }) { targets[i] = line.Running; continue; }
            if (line is null || !blocks.TryGetValue(line.BlockId, out var b)) continue;
            targets[i] = b.Kind switch
            {
                BlockKind.Heading => Find((b.Id, SlotKind.Text, 0, 0, 0)),
                BlockKind.Table => CellTarget(pw, b, op, Find),
                // An item's label opens its first line: a word its later lines start with, flush with the label,
                // is its text.
                BlockKind.List when line.Item >= 0 && op.X0 < b.Items![line.Item].BodyX - 1 && Math.Max(op.X0, op.X1) <= b.Items[line.Item].BodyX + 1
                                                   && ReferenceEquals(line, b.Items[line.Item].Lines.FirstOrDefault())
                    => Find((b.Id, SlotKind.Label, line.Item, 0, 0)),
                BlockKind.List when line.Item >= 0 && b.Links is { Count: > 0 }
                    => LinkTarget(pw, b, b.Items![line.Item].Lines, ItemLinks(b, line.Item), op, before => Find((b.Id, SlotKind.Body, line.Item, before, 0)), Find),
                BlockKind.List when line.Item >= 0 => Find((b.Id, SlotKind.Body, line.Item, 0, 0)),
                BlockKind.Paragraph when b.Links is { Count: > 0 }
                    => LinkTarget(pw, b, b.Lines ?? [], Enumerable.Range(0, (b.LinkRects ?? []).Count), op, before => Find((b.Id, SlotKind.Text, before, 0, 0)), Find),
                BlockKind.Paragraph => Find((b.Id, SlotKind.Text,
                    (b.InlineFigs ?? []).Count(f => FigureBefore(pw, b, f, line, op)), 0, 0)),
                _ => ContentMarker.Neutral,
            };
        }
        ReleaseArtifactPictures(pw, targets);
        return targets;
    }

    /// <summary>The formula a painted path is a rule of: a flat rule under a line of a sum set out in lines (see
    /// <see cref="MarkSums"/>), across its text; null for any other path.</summary>
    private static int? SumRuleBlock(PageWork pw, ContentOp op)
    {
        if (op.Urx - op.Llx < MinBlankLength || op.Ury - op.Lly > MaxBlankThickness) return null;
        var at = (op.Lly + op.Ury) / 2;
        foreach (var b in pw.Blocks)
            if (b.Formula && b.Lines is { } lines && lines.Any(l => l.Frags.Count > 0 && at <= l.Baseline + 0.5
                    && l.Baseline - at <= SumRuleEms * l.Size && l.MinX < op.Urx && l.Frags[^1].R > op.Llx))
                return b.Id;
        return null;
    }

    // A picture covering at least this share of the page inside a producer's artifact is content.
    private const double ArtifactPictureShare = 0.1;

    /// <summary>A producer marks a page's pictures as artifacts by their wrapping, not by their
    /// meaning: a cover's art, a diagram set behind the text. One covering a good share of the
    /// page that the tagger made a Figure is content, and the artifact marks around it are dropped;
    /// so is a form's entry blank a line of text took (see <see cref="EntryBlankLine"/>).</summary>
    private static void ReleaseArtifactPictures(PageWork pw, int[] targets)
    {
        var rect = pw.Page.GetPageRect(true);
        var least = ArtifactPictureShare * rect.Width * rect.Height;
        // The lines holding text the producer marked as content: text it marked as an artifact on one of them - a list
        // entry's label drawn with its box - is that line's content.
        var shared = new HashSet<Line>(ReferenceEqualityComparer.Instance);
        var open = new Stack<int>();
        for (var i = 0; i < pw.Ops.Count; i++)
        {
            var op = pw.Ops[i];
            if (op.Kind == ContentOpKind.BeginMark) { open.Push(i); continue; }
            if (op.Kind == ContentOpKind.EndMark) { if (open.Count > 0) open.Pop(); continue; }
            if (op.Kind == ContentOpKind.TextShow && targets[i] >= 0 && !open.Any(m => pw.Ops[m].MarkTag == "Artifact")
                && LineOf(pw.Lines, op) is { } line)
                shared.Add(line);
        }
        open.Clear();
        for (var i = 0; i < pw.Ops.Count; i++)
        {
            var op = pw.Ops[i];
            if (op.Kind == ContentOpKind.BeginMark) { open.Push(i); continue; }
            if (op.Kind == ContentOpKind.EndMark) { if (open.Count > 0) open.Pop(); continue; }
            if (targets[i] < 0) continue;
            // An entry blank a line took, and a symbol a block took, are content whatever their size.
            var blank = (op.Kind == ContentOpKind.PathPaint && op.Drawing < 0) || (op.Kind == ContentOpKind.Form && op.AsFigure);
            var label = op.Kind == ContentOpKind.TextShow && !string.IsNullOrWhiteSpace(op.Text) && LineOf(pw.Lines, op) is { } on && shared.Contains(on);
            if (!blank && !label && (op.Kind != ContentOpKind.Image || (op.Urx - op.Llx) * (op.Ury - op.Lly) < least)) continue;
            foreach (var m in open)
                if (pw.Ops[m].MarkTag == "Artifact" && pw.Ops[m].Stream == op.Stream) pw.Ops[m].MarkDropped = true;
        }
    }

    /// <summary>The line a text show belongs to: the one whose baseline is nearest to the
    /// show's, within a fraction of the text size.</summary>
    private static Line? LineOf(List<Line> lines, ContentOp op)
    {
        // Lines of side-by-side columns share a baseline: the one the show sits in wins, and a
        // line the show does not sit in takes it only from its exact baseline.
        var tolerance = Math.Max(1.5, 0.35 * op.Size);
        Line? best = null;
        var bestD = double.MaxValue;
        var cx = (op.X0 + Math.Max(op.X0, op.X1)) / 2;
        foreach (var l in lines)
        {
            var d = Math.Abs(l.Baseline - op.Y);
            var inside = l.Frags.Count == 0 || (cx >= l.MinX - 2 && cx <= l.Frags[^1].R + 2);
            if (!inside) d += tolerance;
            if (d > tolerance || d >= bestD) continue;
            bestD = d;
            best = l;
        }
        // A mark raised over its line (a note call, a revision mark) is shown off the line's
        // baseline: the line holding the fragment the show drew takes it.
        return best ?? lines.FirstOrDefault(l => l.Frags.Any(f => Math.Abs(f.Base - op.Y) <= FragmentMatch && Math.Abs(f.X - op.X0) <= FragmentMatch));
    }

    // A show drew a fragment standing within this many points of its start and baseline.
    private const double FragmentMatch = 1.0;

    /// <summary>True when an in-line figure comes before a text show in reading order. The
    /// figure stands at the first of the paragraph's lines it reaches: text on lines below that
    /// one, or to the figure's right on it, comes after it — so text flowing around a floated
    /// figure falls into two runs, before and after.</summary>
    private static bool FigureBefore(PageWork pw, Block b, int figOp, Line line, ContentOp op)
    {
        var fig = pw.Figures.FirstOrDefault(f => f.op == figOp);
        var anchor = (b.Lines ?? []).Where(l => fig.y < l.Y + l.Size && fig.y + fig.h > l.Y)
            .OrderByDescending(l => l.Y).FirstOrDefault();
        if (anchor is null) return fig.y >= op.Y + op.Size * 0.8;
        return line.Y < anchor.Y - 0.5 || (line == anchor && op.X0 > fig.x);
    }

    // A link box on a show's line has its middle between this many ems under the show's
    // baseline (its descent) and this many over it (its ascent).
    private const double LinkBoxBelowBaselineEms = 0.3;
    private const double LinkBoxAboveBaselineEms = 1.0;

    /// <summary>The slot a show of a block with links goes to: a link's own when the show stands in the link's box,
    /// else the text segment after the links before it (<paramref name="text"/> names it by how many come before).
    /// <paramref name="lines"/> are the lines the show may be on - a paragraph's, or a list item's - and
    /// <paramref name="links"/> the block's links standing among them.</summary>
    private static int LinkTarget(PageWork pw, Block b, List<Line> lines, IEnumerable<int> links, ContentOp op, Func<int, int> text,
        Func<(int, SlotKind, int, int, int), int> find)
    {
        double cx = (op.X0 + Math.Max(op.X0, op.X1)) / 2, cy = op.Y + op.Size * 0.3;
        var before = new HashSet<int>();
        var rects = b.LinkRects ?? [];
        var groups = b.LinkGroups ?? Enumerable.Range(0, rects.Count).ToList();
        // The lines in reading order: a link on an earlier line comes before the show, whatever
        // their heights (a paragraph going on at the top of the next column stands higher than its link).
        var opLine = LineOf(pw.Lines, op) is { } found ? lines.IndexOf(found) : -1;
        foreach (var k in links)
        {
            var r = rects[k];
            if (cx >= r.LLX - 1 && cx <= r.URX + 1 && cy >= r.LLY - 1 && cy <= r.URY + 1)
                return find((b.Id, SlotKind.Link, groups[k], 0, 0));
            var linkLine = LinkLine(lines, r);
            // A link standing on none of the paragraph's lines follows its text: it splits nothing.
            if (linkLine < 0 && lines.Count > 0) continue;
            if (opLine >= 0 && linkLine >= 0)
            {
                if (linkLine < opLine || (linkLine == opLine && (r.LLX + r.URX) / 2 < cx)) before.Add(groups[k]);
                continue;
            }
            var above = r.LLY > op.Y + op.Size * 0.5;
            // By centres: a link's box often overhangs its text (a citation's box reaches into
            // the bracket after it), so its right edge can pass the start of the next show; and
            // a box one line tall standing on its baseline reaches the baseline of the line
            // above, so it is on the show's line when its middle is within the show's line box.
            var middle = (r.LLY + r.URY) / 2;
            var sameLine = middle >= op.Y - LinkBoxBelowBaselineEms * op.Size && middle <= op.Y + LinkBoxAboveBaselineEms * op.Size;
            var sameLineLeft = !above && sameLine && (r.LLX + r.URX) / 2 < cx;
            if (above || sameLineLeft) before.Add(groups[k]);
        }
        return text(before.Count);
    }

    // An entry blank is a rule at least this long (points) and at most this thick, standing within this share of
    // its line's size of the baseline, at most this many sizes after the line's text.
    private const double MinBlankLength = 12.0;
    private const double MaxBlankThickness = 4.0;
    private const double BlankBaselineShare = 0.4;
    // ... or under it by up to this share of the line's largest size: as deep as an underline goes, well above the next
    // line's baseline (a line's leader dots may be set smaller than its text).
    private const double BlankUnderShare = 0.5;
    private const double BlankReachEms = 4.0;
    // A rule this share of whose length has text over it, on the baseline it stands at, is that text's underline.
    private const double UnderlineCoverShare = 0.5;

    /// <summary>The line of text a painted path is an entry blank of: a flat rule standing on the line's
    /// baseline, after its text (an underline runs under the text - its own line's, or the text of the column
    /// beside the line it would follow - a table's or a paragraph's rule between the lines); null for any
    /// other path.</summary>
    private static Line? EntryBlankLine(PageWork pw, ContentOp op)
    {
        if (op.Urx - op.Llx < MinBlankLength || op.Ury - op.Lly > MaxBlankThickness) return null;
        if (pw.Tables.Any(t => op.Urx > t.region.LLX && op.Llx < t.region.URX && op.Ury >= t.region.LLY - 1 && op.Lly <= t.region.URY + 1)) return null;
        var at = (op.Lly + op.Ury) / 2;
        if (Underlines(pw, op, at)) return null;
        Line? best = null;
        var nearest = double.MaxValue;
        foreach (var l in pw.Lines)
        {
            if (l.Running != 0 || l.BlockId < 0) continue;
            var text = l.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text)).ToList();
            // The rule stands on the baseline of the text it follows (an entry's number may be set off its line's).
            if (text.Count == 0) continue;
            var under = text[^1].Base - at;
            if (under < -BlankBaselineShare * l.Size || under > Math.Max(BlankBaselineShare * l.Size, BlankUnderShare * text.Max(f => f.Size))) continue;
            var gap = op.Llx - text[^1].R;
            if (gap < -1 || gap > BlankReachEms * l.Size || gap >= nearest) continue;
            // ... in the line's column: a rule starting past the column's edge is the next column's.
            if (l.ColumnRight > 0 && op.Llx >= l.ColumnRight) continue;
            (best, nearest) = (l, gap);
        }
        return best;
    }

    /// <summary>The line a flat rule underlines: text of it stands over most of the rule's length, on the baseline the rule
    /// stands at; null for any other path (a table draws its own rules).</summary>
    private static Line? UnderlinedLine(PageWork pw, ContentOp op)
    {
        if (op.Urx - op.Llx < MinBlankLength || op.Ury - op.Lly > MaxBlankThickness) return null;
        if (pw.Tables.Any(t => op.Urx > t.region.LLX && op.Llx < t.region.URX && op.Ury >= t.region.LLY - 1 && op.Lly <= t.region.URY + 1)) return null;
        var at = (op.Lly + op.Ury) / 2;
        foreach (var l in pw.Lines.Where(l => l.Running == 0 && l.BlockId >= 0))
        {
            var over = l.Frags.Where(f => !string.IsNullOrWhiteSpace(f.Text) && Math.Abs(at - f.Base) <= BlankBaselineShare * l.Size
                                          && f.R > op.Llx && f.X < op.Urx).ToList();
            var covered = over.Sum(f => Math.Max(0, Math.Min(f.R, op.Urx) - Math.Max(f.X, op.Llx)));
            // ... and runs no further than its text, give or take a size (a rule across the column under a heading is its
            // rule, not an underline).
            if (covered > UnderlineCoverShare * (op.Urx - op.Llx)
                && op.Llx >= over.Min(f => f.X) - l.Size && op.Urx <= over.Max(f => f.R) + l.Size)
                return l;
        }
        return null;
    }

    /// <summary>True when text stands over most of a rule's length on the baseline the rule stands at.</summary>
    private static bool Underlines(PageWork pw, ContentOp op, double at)
    {
        var covered = 0.0;
        foreach (var l in pw.Lines)
            foreach (var f in l.Frags)
                if (!string.IsNullOrWhiteSpace(f.Text) && Math.Abs(at - f.Base) <= BlankBaselineShare * l.Size)
                    covered += Math.Max(0, Math.Min(f.R, op.Urx) - Math.Max(f.X, op.Llx));
        return covered > UnderlineCoverShare * (op.Urx - op.Llx);
    }

    /// <summary>How many of a paragraph's links stand on its lines up to <paramref name="line"/>: the text
    /// segment what ends that line is in.</summary>
    private static int LinksUpTo(Block b, Line line)
    {
        var lines = b.Lines ?? [];
        var at = lines.IndexOf(line);
        var rects = b.LinkRects ?? [];
        var groups = b.LinkGroups ?? Enumerable.Range(0, rects.Count).ToList();
        return Enumerable.Range(0, rects.Count).Where(k => LinkLine(lines, rects[k]) is >= 0 and var on && on <= at)
            .Select(k => groups[k]).Distinct().Count();
    }

    /// <summary>The index of the line of <paramref name="lines"/> a link's box stands on: its middle within the
    /// line's box, its box across the line's text; -1 for none.</summary>
    private static int LinkLine(List<Line> lines, Aspose.Pdf.Rectangle r)
    {
        var middle = (r.LLY + r.URY) / 2;
        // Of the lines a link's box stands on - a note's number raised after a line's last word reaches into the line over
        // it - the one it shares the most height with.
        var best = -1;
        var most = double.NegativeInfinity;
        for (var k = 0; k < lines.Count; k++)
        {
            var l = lines[k];
            if (l.Frags.Count == 0 || middle < l.Y - LinkBoxBelowBaselineEms * l.Size || middle > l.Y + LinkBoxAboveBaselineEms * l.Size
                || r.LLX >= l.Frags[^1].R + LinkAfterLineEms * l.Size || r.URX <= l.MinX) continue;
            var shared = Math.Min(r.URY, l.Y + l.Size) - Math.Max(r.LLY, l.Y);
            if (shared > most) (best, most) = (k, shared);
        }
        return best;
    }

    // A link's box starting within this many of a line's size after the line's last text (a note's number ending it) is on
    // that line.
    private const double LinkAfterLineEms = 1.0;

    private static int CellTarget(PageWork pw, Block b, ContentOp op, Func<(int, SlotKind, int, int, int), int> find)
    {
        var t = pw.Tables[b.TableIndex];
        // A show is placed where its text starts: its pen end runs on over an adjustment after it
        // (a figure of a row written as one string carries the gap to the next column).
        double cx = op.X0 + Math.Min((Math.Max(op.X0, op.X1) - op.X0) / 2, op.Size), cy = op.Y + op.Size * 0.3;
        var col = 0;
        while (col < t.cols - 1 && cx >= t.colX[col + 1]) col++;
        var fromBottom = 0;
        while (fromBottom < t.rows - 1 && cy >= t.rowY[fromBottom + 1]) fromBottom++;
        // A merged cell's content belongs to the cell, named by its top-left grid cell.
        var (row, start) = pw.TableCells[b.TableIndex][t.rows - 1 - fromBottom, col];
        // A header row repeated where the table goes on over a page break is pagination.
        if (pw.RepeatedHeaders.TryGetValue(b.Id, out var repeated) && row < repeated) return ContentMarker.Neutral;
        return find((b.Id, SlotKind.Cell, row, start, 0));
    }

    /// <summary>Turn each MCR placeholder into a marked-content reference dictionary for the first
    /// MCID its content received, or drop it when no content fell to it; the run's further MCIDs
    /// follow it in /K as bare integers when they are on the element's own page (/Pg), else as
    /// dictionaries of their own. Each OBJR becomes an object reference to its annotation.</summary>
    private static void FinishReferences(Document document, TaggingRun run)
    {
        var elementPage = new Dictionary<PdfDictionary, Page>(ReferenceEqualityComparer.Instance);
        foreach (var slot in run.Slots)
        {
            if (slot.Marks.Count == 0)
            {
                slot.El.Detach();
                continue;
            }
            var parent = slot.El.ParentElement;
            var first = slot.El._dict;
            MakeMcr(document, first, slot.Marks[0]);
            if (slot.Marks.Count > 1 && parent?._dict.Get("K") is PdfArray k)
            {
                // A bare MCID refers to the element's /Pg: the page of its first such run.
                if (!elementPage.TryGetValue(parent._dict, out var pg))
                {
                    elementPage[parent._dict] = pg = slot.Marks[0].Page;
                    document.PendingStructPgFixups.Add((parent._dict, pg));
                }
                var at = IndexOf(k, first);
                for (var m = 1; m < slot.Marks.Count; m++)
                {
                    PdfObject extra = new PdfInteger(slot.Marks[m].Mcid);
                    if (!ReferenceEquals(slot.Marks[m].Page, pg) || slot.Marks[m].Form is not null)
                    {
                        var dict = new PdfDictionary();
                        MakeMcr(document, dict, slot.Marks[m]);
                        extra = dict;
                    }
                    k.Insert(at + m, extra);
                }
            }
        }

        foreach (var (row, cells) in run.ContinuedRows)
            if (cells.All(s => s.Marks.Count == 0)) row.Detach();

        foreach (var ls in run.LinkSlots)
        {
            var d = ls.Objr._dict;
            foreach (var key in d.Keys.ToList()) d.Remove(key);
            d.Set("Type", new PdfName("OBJR"));
            d.Set("Obj", ls.Annot);
            if (ls.Page is { } own)
                document.PendingStructPgFixups.Add((d, own));
            else if (run.PageOfBlock.TryGetValue(ls.BlockId, out var pw))
                document.PendingStructPgFixups.Add((d, pw.Page));
        }
    }

    private static void MakeMcr(Document document, PdfDictionary d, Mark mark)
    {
        foreach (var key in d.Keys.ToList()) d.Remove(key);
        d.Set("Type", new PdfName("MCR"));
        d.Set("MCID", new PdfInteger(mark.Mcid));
        if (mark.FormRef is not null) d.Set("Stm", mark.FormRef);
        document.PendingStructPgFixups.Add((d, mark.Page));
    }

    private static int IndexOf(PdfArray arr, PdfDictionary d)
    {
        for (var i = 0; i < arr.Count; i++)
            if (ReferenceEquals(arr[i], d)) return i;
        return arr.Count - 1;
    }

    /// <summary>Number the tree, link parents, and build /ParentTree: one array per marked
    /// content stream (a page's own, or a form XObject's looked into) indexed by MCID (the element
    /// owning that content), then one entry per link annotation naming its Link element
    /// (PDF 32000-1 §14.7.4.4).</summary>
    private static void WireParentTree(Document document, LS.StructureElement root, TaggingRun run)
    {
        var structRootDict = StructureWiring.EnsureIndirectStructTreeRoot(document);
        if (structRootDict is null) return;
        var refs = StructureWiring.NumberAndLink(document, root, structRootDict);

        // Keys of a previous tree no longer mean anything.
        foreach (var pw in run.Pages)
        {
            pw.Page.Dict.Remove("StructParents");
            if (document.Reader.Resolve(pw.Page.Dict.Get("Annots")) is PdfArray annots)
            {
                foreach (var a in annots)
                    document.Reader.ResolveDict(a)?.Remove("StructParent");
                // Annotations are visited in structure order (PDF/UA-1 §7.18.3).
                if (annots.Count > 0) pw.Page.Dict.Set("Tabs", new PdfName("S"));
            }
            // A form XObject's key belonged to the replaced tree's /ParentTree.
            PdfDictionary? resources = null;
            for (var node = pw.Page.Dict; node is not null && resources is null; node = document.Reader.ResolveDict(node.Get("Parent")))
                resources = document.Reader.ResolveDict(node.Get("Resources"));
            if (resources is not null && document.Reader.ResolveDict(resources.Get("XObject")) is { } xobjects)
                foreach (var key in xobjects.Keys)
                    if (document.Reader.ResolveStream(xobjects.Get(key)) is { } form && form.Dict.GetName("Subtype") == "Form")
                        form.Dict.Remove("StructParents");
        }

        var nums = new PdfArray();
        var nextKey = 0;
        var streams = run.Slots.SelectMany(s => s.Marks.Select(m => (m.Page, m.Form, m.Mcid, Owner: s.El.ParentElement)))
            .GroupBy(m => (m.Page, m.Form));
        foreach (var group in streams)
        {
            var key = nextKey++;
            (group.Key.Form?.Dict ?? group.Key.Page.Dict).Set("StructParents", new PdfInteger(key));
            var arr = new PdfArray();
            var max = group.Max(m => m.Mcid);
            for (var i = 0; i <= max; i++) arr.Add(PdfNull.Instance);
            foreach (var m in group)
                if (m.Owner is not null && refs.TryGetValue(m.Owner._dict, out var r))
                    arr.ReplaceAt(m.Mcid, r);
            nums.Add(new PdfInteger(key));
            nums.Add(arr);
        }

        foreach (var ls in run.LinkSlots)
        {
            if (document.Reader.ResolveDict(ls.Annot) is not { } annot || !refs.TryGetValue(ls.Link._dict, out var linkRef))
                continue;
            var key = nextKey++;
            annot.Set("StructParent", new PdfInteger(key));
            nums.Add(new PdfInteger(key));
            nums.Add(linkRef);
        }

        var parentTree = new PdfDictionary();
        parentTree.Set("Nums", nums);
        structRootDict.Set("ParentTree", parentTree);
        structRootDict.Set("ParentTreeNextKey", new PdfInteger(nextKey));
    }
}
