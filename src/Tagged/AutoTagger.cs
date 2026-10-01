using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Generates a logical-structure tree (/StructTreeRoot) for an untagged document by
/// analysing each page's laid-out content: text is clustered into lines, lines are
/// classified as headings (by font size) or body, and consecutive body lines are grouped
/// into paragraphs; images become figures. The inferred tree is authored through
/// <see cref="ITaggedContent"/>, and the page content is then marked so every structure
/// element owns the content it stands for and everything else is an artifact.
/// Driven by <see cref="AutoTaggingSettings"/> during PDF/A / PDF/UA conversion.
/// </summary>
internal static partial class AutoTagger
{
    private sealed class Line
    {
        public double Y;        // bottom of the line's fragments
        public double Baseline; // where its text is shown
        // A table row whose label wraps over lines: where its first line and its last are shown (NaN: at Baseline).
        public double FirstBaseline = double.NaN, LastBaseline = double.NaN;
        public double Size;
        public double MinX;
        public int BlockId = -1;
        public int Item = -1;   // list item the line belongs to (List blocks)
        public int Running;     // a running header (ContentMarker.HeaderArtifact) or footer, else 0
        // The line's fragments left to right: start, end and text.
        public List<(double X, double R, string Text, double Y, double Base, double Size, string? Font)> Frags = [];
        public bool ColumnStart;  // the first line of a column after the first in a column band
        public double ColumnRight; // the right edge of the line's column (0: the page's text margin)
        public bool Toc;          // an entry of a table of contents: read whole, an entry of its own
    }

    /// <summary>One item of a list: its lines, where its label starts and where its body
    /// text starts (the label is everything left of that).</summary>
    private sealed class ListItem
    {
        public List<Line> Lines = [];
        public double LabelX;
        public double BodyX;
    }

    private enum BlockKind { Heading, Paragraph, Figure, Table, List }

    private struct Block
    {
        public int Id;
        public BlockKind Kind;
        public int Level;     // heading level (1..6) for Heading blocks
        public List<Aspose.Pdf.Core.PdfObject>? Links; // link-annotation refs inside a paragraph
        public int Rows, Cols; // grid dimensions for Table blocks
        public int TableIndex; // the page table a Table block stands for
        public double Y;      // bottom anchor (baseline of the block's last/only line)
        public double SortTop; // top edge, orders blocks inside a heading segment
        public double TextMinX; // heading text left edge (orders absorbed icons around the MCR)
        public List<(double X, int Fig)>? HeaderFigs; // figures absorbed into a Heading (inline icons)
        public List<int>? Figs;       // images in a grouped figure paragraph (Figure blocks)
        public List<int>? InlineFigs; // small in-line images inside a text paragraph
        public List<ListItem>? Items; // the items of a List block
        public List<Aspose.Pdf.Rectangle>? LinkRects; // the rectangles of Links, in the same order
        public List<int>? LinkGroups; // the Link element each of Links belongs to (a wrapped link has several)
        public List<Line>? Lines;     // a Paragraph block's lines
        public List<int>? Joined;     // paragraphs continuing this one across page breaks
        public ParagraphLayout? Layout; // how a paragraph or heading sits in its column
        public bool Note;     // a footnote: a Note element, read after the page's other blocks
        public bool Toc;      // an entry of a table of contents: a TOCI element, the entries in a row one TOC
        public bool Formula;  // a display formula: a Formula element stating its box
        public double? RuleGapBelow; // a rule standing alone closes the block, this far under its text
        public (double From, double To, double At, double Top) RuleBox; // the rule's span and the block's height over it
        public double RuleThickness;  // how thick that rule is drawn (0: unread)
        public double? RuleGapAbove;  // a rule standing alone opens the block, this far over its text
        public (double From, double To, double Thickness) RuleAbove; // that rule's span and how thick it is drawn
        public bool RuleDouble; // a rule opening or closing it is drawn twice, a thick line and a thin one a little apart
        public Frame? Frame;  // the box ruled round the block, with others (a Div states it)
    }

    /// <summary>One page's analysis, kept for the marking pass.</summary>
    private sealed class PageWork
    {
        public Page Page = null!;
        public byte[] Bytes = [];
        public List<ContentOp> Ops = [];
        public List<Line> Lines = [];
        public List<Block> Blocks = [];
        public List<(double y, double x, double w, double h, int op)> Figures = [];
        public List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> Tables = [];
        public List<(int Rows, bool Col)> TableHeaders = []; // per table: its header rows / a header column
        public List<(int Row, int Col)[,]> TableCells = []; // per table: the cell each grid cell belongs to (MergedCells)
        public List<Aspose.Pdf.Rectangle> RuledBoxes = []; // the boxes the page's rules draw, tables or not
        public List<PageContentScan.Rule> Rules = []; // the page's rules (a table cell's edges are drawn where one runs)
        public Dictionary<int, int> RepeatedHeaders = []; // table blocks going on with a table -> how many of its header rows they repeat
        public HashSet<int> SideBySide = []; // tables that are one half of a table set in two halves side by side
        public HashSet<int> Decorative = []; // image operations that are decoration: artifacts, no Figure
        public List<FormWork> Forms = []; // the form XObjects looked into: ContentOp.Stream - 1 indexes them
        public List<Drawing> Drawings = []; // clusters of painted paths read as figures: ContentOp.Drawing indexes them
        public List<Frame> Frames = []; // boxes ruled round content that is no table
    }

    /// <summary>The whole tagging run: every page's analysis and every content slot the
    /// tree holds.</summary>
    private sealed class TaggingRun
    {
        public int NextBlockId;
        public List<PageWork> Pages = [];
        public List<Slot> Slots = [];
        public List<LinkSlot> LinkSlots = [];
        public Dictionary<int, PageWork> PageOfBlock = [];
        public CarriedProperties Carried = new(); // what a replaced tree said about its content
        // Rows of the later parts of a joined table with the slots of their cells: a row whose cells
        // receive no content (a header the author marked as pagination) is dropped.
        public List<(LogicalStructure.StructureElement Row, List<Slot> Cells)> ContinuedRows = [];
    }

    public static void Apply(Document document, AutoTaggingSettings settings)
    {
        var tc = document.TaggedContent;
        // Regenerate from scratch: clear any pre-existing structure tree, then root the new
        // one in a Document element.
        var carried = CollectCarriedProperties(document);
        var structRoot = tc.StructTreeRootElement;
        structRoot.ClearChildren();
        var docRoot = new LogicalStructure.DocumentElement();
        structRoot.AppendChild(docRoot);
        (tc as TaggedContent)?.ResetRootElement(docRoot);

        var run = new TaggingRun { Carried = carried };
        var blocks = new List<Block>();
        var progress = new TaggingProgress(settings.CustomProgressHandler, document.Pages.Count);
        // Sizes are ranked over the whole document: the body size is the most common one, and a
        // heading level means the same on every page.
        var pageLines = new List<(Page Page, List<Line> Lines)>();
        foreach (var page in document.Pages)
        {
            var lines = CollectLines(page);
            if (SplitInterleavedShows(page, lines)) lines = CollectLines(page);
            pageLines.Add((page, lines));
            progress.Read(pageLines.Count);
        }
        MarkRunningLines(pageLines);
        var pages = new List<PageWork>();
        var forms = new FormUse(document);
        foreach (var (page, lines) in pageLines)
        {
            pages.Add(PreparePage(page, lines, pages.Count > 0 ? pages[^1] : null, forms));
            progress.Analysed(pages.Count);
        }
        MarkDecorativeImages(pages);
        var allLines = pages.SelectMany(p => p.Lines).Where(l => l.Running == 0).ToList();
        var bodySize = allLines.Count > 0
            ? allLines.GroupBy(l => Math.Round(l.Size, 1)).OrderByDescending(g => g.Count())
                   .ThenBy(g => g.Key).First().Key
            : 0;
        var headingSizes = allLines.Select(l => Math.Round(l.Size, 1))
            .Where(s => s > bodySize + 0.5)
            .Distinct().OrderByDescending(s => s).ToList();
        var headingRule = MakeHeadingRule(document, settings, bodySize, headingSizes);
        foreach (var pw in pages)
        {
            AnalyzePage(pw, bodySize, headingRule, run);
            run.Pages.Add(pw);
            foreach (var b in pw.Blocks) run.PageOfBlock[b.Id] = pw;
            blocks.AddRange(pw.Blocks);
        }

        progress.Total(TaggingProgress.BuildShare);
        JoinPageBreakTables(blocks, run);
        JoinPageBreakParagraphs(blocks, run);
        NormalizeHeadingLevels(blocks);
        BuildTree(tc, docRoot, blocks, run);
        TagAnnotations(tc, docRoot, run);
        progress.Total(TaggingProgress.MarkShare);
        MarkContent(document, docRoot, run);
        tc.Save();
        progress.Total(TaggingProgress.Whole);
    }

    /// <summary>What the tagger tells <see cref="AutoTaggingSettings.CustomProgressHandler"/>: each page read, each page's
    /// layout analysed, and the share of the tagging done. The shares follow where the time goes (measured on a 40-page
    /// publication: reading the pages' lines a quarter, analysing their layout three fifths, the tree and its marks the rest).</summary>
    private sealed class TaggingProgress(UnifiedSaveOptions.ConversionProgressEventHandler? handler, int pages)
    {
        private const int ReadShare = 25;
        public const int BuildShare = 85;
        public const int MarkShare = 90;
        public const int Whole = 100;

        public void Read(int page) => Total(ReadShare * page / Math.Max(1, pages));

        public void Analysed(int page)
        {
            Fire(ProgressEventType.SourcePageAnalysed, page, pages);
            Total(ReadShare + (BuildShare - ReadShare) * page / Math.Max(1, pages));
        }

        public void Total(int percent) => Fire(ProgressEventType.TotalProgress, percent, Whole);

        private void Fire(ProgressEventType type, int value, int max)
            => handler?.Invoke(new UnifiedSaveOptions.ProgressEventHandlerInfo { EventType = type, Value = value, MaxValue = max });
    }

    /// <summary>Reduce a page to an ordered list of structural blocks: headings (with a level
    /// inferred from font size), tables (from ruling lines or aligned columns), lists (runs of
    /// labelled items), body paragraphs (split at vertical gaps and first-line indents) and
    /// figures (one per image).</summary>
    private static void AnalyzePage(PageWork pw, double bodySize, HeadingRule headingRule, TaggingRun run)
    {
        var pa = new PageAnalysisState();
        pa.page = pw.Page;
        pa.run = run;
        pa.result = new List<Block>();
        pa.lines = pw.Lines;
        pa.bodySize = bodySize;
        // Table rows set no paragraph's pitch (their rows can stand between a text column's lines).
        pa.pitch = TypicalPitch(pa.lines.Where(l => !pw.Tables.Any(t => InRegion(l, t.region))).ToList(), pa.bodySize);
        pa.margin = pa.lines.Where(l => l.Frags.Count > 0).Select(l => l.Frags[^1].R).DefaultIfEmpty(0).Max();
        pa.tables = pw.Tables.Select(t => (t.region, t.rows, t.cols)).ToList();
        pa.tableEmitted = new bool[pa.tables.Count];
        pa.tableBottoms = pw.Tables.Select(t => Math.Min(t.region.LLY, t.rowY.Count > 0 ? t.rowY.Min() : t.region.LLY)).ToArray();
        pa.tableIds = new int[pa.tables.Count];
        pa.blockLines = new List<List<Line>?>();
        pa.frames = pw.Frames.Select(f => f.Box).ToList();

        foreach (var line in pa.lines)
            ClassifyLine(pa, line, headingRule);
        FlushParagraph(pa);
        FlushList(pa);

        // Figures: place each image at its rendered position so it lands in the correct
        // section. The section a figure belongs to is the heading band containing the
        // figure's BOTTOM Y (its baseline) — that anchor keeps per-section figure
        // grouping stable, whereas the image's top/centre Y crosses bands for
        // tall floated images. A figure sitting on a heading's line is absorbed into that
        // HeaderElement; otherwise it becomes an image-only paragraph interleaved by Y.
        MergeFigures(pa.result, pa.blockLines, pw.Figures.Where(f => !pw.Decorative.Contains(f.op)).ToList(), run,
            Math.Max(pa.pitch - pa.bodySize, 0));
        AttachLinks(pa, pw);
        MarkFormulas(pa);
        MarkSums(pa, pw);
        MarkFractions(pa, pw);
        MarkPiecedDelimiters(pa);
        MoveNotesLast(pa);
        MarkRuledBlocks(pw, pa.result, pa.blockLines, pa.bodySize);
        MarkFramedBlocks(pw, pa.result);

        pw.Blocks = pa.result;
    }

    /// <summary>Everything about a page that does not depend on the rest of the document: its
    /// content operations, images, tables (ruled or aligned), and its lines split into columns
    /// and put in reading order.</summary>
    private static PageWork PreparePage(Page page, List<Line> rows, PageWork? previous, FormUse forms)
    {
        var pw = new PageWork { Page = page };
        // Words a link annotation covers become shows of their own, for its Link to own.
        LinkTextSplit.Apply(page, GetLinkRefs(page).Select(r => LinkRect(page, r)).OfType<Rectangle>().ToList());
        var rules = new List<PageContentScan.Rule>();
        (pw.Bytes, pw.Ops) = PageContentScan.Scan(page, rules);
        ExpandForms(pw, forms, rules);
        pw.Rules = rules;
        // The lines inside a form drawn whole as a figure are its labels, not the page's text.
        foreach (var figure in pw.Ops.Where(o => o.AsFigure))
            rows.RemoveAll(l => InsideForm(l, figure));
        pw.Figures = CollectFigures(pw.Ops);
        var ruled = DetectTables(rules, ShadeEdges(pw, null, rows).ToList());
        pw.RuledBoxes = ruled.Select(t => t.region).ToList();
        // Rows inside a ruled table are the table's, even where they recur at the top of every page
        // (a long table's header rows): the header repeated after a page break is marked when the
        // table joins across it.
        foreach (var line in rows.Where(l => l.Running != 0 && ruled.Any(t => InRegion(l, t.region))))
            line.Running = 0;
        var content = rows.Where(l => l.Running == 0).ToList();
        pw.Tables = ruled.Where(t => content.Any(l => InRegion(l, t.region))).ToList();
        MarkTocLines(content.Where(l => !pw.Tables.Any(t => InRegion(l, t.region))).ToList());
        // A band at a table's end ruled into columns of its own, the table's text running across them, is no band of it.
        pw.Tables = pw.Tables.Select(t => WithoutForeignEndBands(t, rules, content)).ToList();
        pw.Tables = pw.Tables.Select(t => SplitDataRows(t, MergedCells(t, rules), content, rules))
            .Select(t => SplitDataColumns(t, MergedCells(t, rules), content, rules))
            // Columns found in the lines of a cell make rows of them.
            .Select(t => SplitDataRows(t, MergedCells(t, rules), content, rules)).ToList();
        var size = content.Count > 0
            ? content.GroupBy(l => Math.Round(l.Size, 1)).OrderByDescending(g => g.Count()).First().Key
            : 12;
        // The bands the rules enclose at a table's ends are no rows of it, and a grid whose
        // rows hold no cells side by side at the text's size is a framed cover block or a box, no table.
        pw.Tables = pw.Tables.Select(t => TrimEndBands(t, content, size)).Where(t => IsGrid(t, content, size)).ToList();
        // A grid of one row whose cells each hold running text is boxes set side by side, no table.
        var boxes = pw.Tables.Where(t => BoxesSideBySide(t, content)).ToList();
        pw.Tables = pw.Tables.Where(t => !boxes.Contains(t)).ToList();
        pw.Frames = FindFrames(pw, ruled, content);
        pw.Frames.AddRange(boxes.SelectMany(t => CellFrames(pw, t)));
        var halves = new List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)>();
        foreach (var parts in pw.Tables.Select(t => SplitSideBySide(t, content).ToList()))
        {
            if (parts.Count > 1) pw.SideBySide.UnionWith(Enumerable.Range(halves.Count, parts.Count));
            halves.AddRange(parts.Count > 1 ? parts : SplitAtInteriorHeaders(parts[0], content));
        }
        pw.Tables = halves;
        // A table ruled between its rows only: its rules are its rows' edges, nothing of it is split further.
        pw.Tables.AddRange(PieceRuledTables(rules, content, pw.Tables));
        // A shade's edges part a table's cells as rules do (a grey box beside a heading, no rule drawn round it).
        pw.TableCells = pw.Tables.Select(t => MergedCells(t, rules.Concat(ShadeEdges(pw, t.region, content)).ToList())).ToList();
        var ruledCount = pw.Tables.Count;
        if (ContinuedTextTable(content.Where(l => !l.Toc && !pw.Tables.Any(t => InRegion(l, t.region))).ToList(), previous) is { } continued)
        {
            pw.Tables.Add(continued);
            pw.TableCells.Add(SingleCells(continued.Item2, continued.Item3));
        }
        // The lines in a box ruled round them are the box's text, not a table of it.
        foreach (var (table, cells) in DetectTextTables(content.Where(l => !l.Toc && !pw.Tables.Any(t => InRegion(l, t.region))
                     && !InFrames(pw, l)).ToList(), pw.Figures))
        {
            pw.Tables.Add(table);
            pw.TableCells.Add(SpanByRules(table, cells ?? SingleCells(table.rows, table.cols), content, rules));
        }
        JoinStackedTextTables(pw, content, ruledCount);
        if (pw.SideBySide.Count == 0) JoinHeaderBands(pw.Tables, pw.TableCells, content);
        ruledCount = AdoptHeaderBlocks(pw, content, ruledCount);
        CloseTablesAtRules(pw, content);
        SnapColumnsToRules(pw, content, ruledCount);
        pw.TableHeaders = pw.Tables.Select(t => TableHeaders(t, content)).ToList();
        SplitShowsAtColumns(pw, forms);
        SplitShowsAtLabels(pw, rows);
        SplitShowsAtGaps(pw, rows);
        FindDrawings(pw, rows, size);
        pw.Lines = SplitColumns(rows, pw.Tables.Concat(FrameRegions(pw)).ToList(), pw.Figures, size, pw.Rules);
        return pw;
    }

    // A mark before a line's text - a check box, an icon - is at most this many of the line's size tall, stands at most
    // this many of it before the text, and shares this much of its own height with the line.
    private const double LineMarkEms = 1.5;
    private const double LineMarkGapEms = 1.0;
    private const double LineMarkShare = 0.5;
    // A mark stands apart from the text after it, and from any before it by a column's gutter: a path this few of the
    // line's size from the text after it, with text before it within the gap a mark keeps, is a glyph of the line drawn
    // as a path (a formula's slash), no mark.
    private const double LineMarkApartEms = 0.25;

    /// <summary>Whether a figure is a mark standing before a line's text, on the line: a little taller than the text
    /// it may be, as a box to tick is. A line not yet parted into its columns holds text before the mark too; the mark
    /// stands before the first of its pieces past the mark, with none reaching into it.</summary>
    private static bool MarksLine((double y, double x, double w, double h, int op) fig, Line line)
    {
        if (line.Frags.Count == 0 || fig.h > LineMarkEms * line.Size) return false;
        var after = line.Frags.FindIndex(f => f.X >= fig.x + fig.w - 1);
        if (after < 0 || line.Frags.Take(after).Any(f => f.R > fig.x + 1)) return false;
        var gap = line.Frags[after].X - (fig.x + fig.w);
        if (gap < -1 || gap > LineMarkGapEms * line.Size) return false;
        if (after > 0 && gap < LineMarkApartEms * line.Size && fig.x - line.Frags.Take(after).Max(f => f.R) < LineMarkGapEms * line.Size) return false;
        var shared = Math.Min(fig.y + fig.h, line.Y + line.Size) - Math.Max(fig.y, line.Y);
        return shared >= LineMarkShare * fig.h;
    }

    /// <summary>The page's image placements — one per image-painting operation, so a reused
    /// image counts once per appearance — as (bottomY, x, width, height, operation index),
    /// ordered top-to-bottom.</summary>
    private static List<(double y, double x, double w, double h, int op)> CollectFigures(List<ContentOp> ops)
    {
        var figs = new List<(double, double, double, double, int)>();
        for (var i = 0; i < ops.Count; i++)
            if (ops[i].Kind is ContentOpKind.Image or ContentOpKind.InlineImage || ops[i].AsFigure)
                figs.Add((ops[i].Lly, ops[i].Llx, ops[i].Urx - ops[i].Llx, ops[i].Ury - ops[i].Lly, i));
        return figs.OrderByDescending(f => f.Item1).ToList();
    }

    /// <summary>Place the page's images into the block stream (measured 2026-08-28 on
    /// the expected tagging of a markdown-rendered page):
    ///  - an image sitting on a heading's line is absorbed into that Heading (an inline
    ///    heading icon), keeping its x so it can stand before or after the heading text;
    ///  - an image no taller than a body line that overlaps one of a paragraph's lines is
    ///    an IN-LINE figure of that paragraph (the paragraph's content splits around it);
    ///  - everything else forms figure-only paragraphs: images grouped while every pair
    ///    overlaps vertically (one rendered row of images = one paragraph, even when the
    ///    row's members are floated to different heights), each group anchored in its
    ///    heading segment by the figure bottoms and ordered among the segment's blocks
    ///    by top edge.</summary>
    // Text set beside a picture starts at most this far (points) from it; a column's gutter is wider.
    private const double PictureGap = 10.0;

    private static void MergeFigures(List<Block> blocks, List<List<Line>?> blockLines,
        List<(double y, double x, double w, double h, int op)> figures, TaggingRun run, double usualGap)
    {
        var loose = new List<(double y, double x, double w, double h, int op)>();
        foreach (var fig in figures)
        {
            // A mark standing before a paragraph line's text is that line's, however near a heading it stands.
            var marked = Enumerable.Range(0, blocks.Count).FirstOrDefault(i => blocks[i].Kind == BlockKind.Paragraph && blockLines[i] is { } ls && ls.Any(l => MarksLine(fig, l)), -1);
            // Heading icon: a small figure whose baseline lies within a line-height of a
            // heading's baseline, standing beside its text (not off in another column),
            // belongs to that (closest) heading, not a separate paragraph.
            var headerIdx = -1;
            var best = double.MaxValue;
            if (fig.h <= 50 && marked < 0)
                for (var i = 0; i < blocks.Count; i++)
                    if (blocks[i].Kind == BlockKind.Heading && BesideText(blockLines[i], fig.x, fig.x + fig.w))
                    {
                        var dy = Math.Abs(fig.y - blocks[i].Y);
                        if (dy <= 14 && dy < best) { best = dy; headerIdx = i; }
                    }
            if (headerIdx >= 0)
            {
                var h = blocks[headerIdx];
                (h.HeaderFigs ??= new List<(double, int)>()).Add((fig.x, fig.op));
                blocks[headerIdx] = h;
                continue;
            }

            // In-line figure: no taller than the line it sits on, overlapping that line's band.
            var inlineIdx = marked;
            for (var i = 0; i < blocks.Count && inlineIdx < 0; i++)
            {
                if (blocks[i].Kind != BlockKind.Paragraph || blockLines[i] is not { } lines) continue;
                foreach (var line in lines)
                    if (fig.h <= line.Size + 0.5 && fig.y + fig.h > line.Y && fig.y < line.Y + line.Size)
                    { inlineIdx = i; break; }
            }
            if (inlineIdx >= 0)
            {
                var b = blocks[inlineIdx];
                (b.InlineFigs ??= new List<int>()).Add(fig.op);
                blocks[inlineIdx] = b;
                continue;
            }

            loose.Add(fig);
        }

        // Group the loose figures into rows: walk top-to-bottom, a figure joins the open
        // group while it vertically overlaps EVERY member (pairwise, not chained — a tall
        // figure hanging into the next row must not weld the rows together).
        foreach (var group in loose
            .GroupBy(f => SegmentOf(blocks, f.y))
            .SelectMany(seg => GroupRows(seg.OrderByDescending(f => f.y + f.h).ToList())))
        {
            var bottom = group.Min(f => f.y);
            var top = group.Max(f => f.y + f.h);
            // Insert into the block list inside its segment - under the last heading of its column above it - ordered by
            // top edge, among the blocks of its column (text standing over or under it, or beside it within a picture's
            // gap): before the first standing no higher than it, or after the last. A figure no text stands over, under or
            // beside (an emblem out beside a title) is placed among all the blocks of its segment.
            var (figLeft, figRight) = (group.Min(f => f.x), group.Max(f => f.x + f.w));
            bool Near(int i) => blockLines[i] is { Count: > 0 } ls
                                && Math.Min(figRight, ls.Where(l => l.Frags.Count > 0).Select(l => l.Frags[^1].R).DefaultIfEmpty(figRight).Max())
                                   - Math.Max(figLeft, ls.Min(l => l.MinX)) >= -PictureGap;
            // (a figure placed before this one stands in the column by its own extent: it has no lines)
            bool NearFigure(int i) => blocks[i].Kind == BlockKind.Figure && blocks[i].Figs is { } placed
                                      && placed.Select(op => figures.First(f => f.op == op))
                                          .Any(f => Math.Min(figRight, f.x + f.w) - Math.Max(figLeft, f.x) >= -PictureGap);
            var alone = !Enumerable.Range(0, blocks.Count).Any(Near);
            bool InColumn(int i) => alone || Near(i) || NearFigure(i);
            // A heading heads the figure's column when the column it stands in holds the figure - its lines' column, from
            // where the lines of that column start to its right edge (the page's text, for a column of the whole page) -
            // whether or not its own words reach the figure: a short heading at the left heads a figure out at the right,
            // and a centred title over the whole page heads none of the figures under the headings below it.
            var middle = (figLeft + figRight) / 2;
            bool HeadsColumn(int i)
            {
                if (blockLines[i] is not { Count: > 0 } ls) return false;
                var right = ls[0].ColumnRight;
                var left = blockLines.Where(o => o is { Count: > 0 } && o[0].ColumnRight == right).SelectMany(o => o!).Min(l => l.MinX);
                return middle >= left - PictureGap && (right <= 0 || middle <= right + PictureGap);
            }
            var segIdx = Enumerable.Range(0, blocks.Count).Where(i => blocks[i].Kind == BlockKind.Heading && blocks[i].Y > bottom && (InColumn(i) || HeadsColumn(i)))
                .DefaultIfEmpty(-1).Last();
            // The segment ends at the next heading; one holding nothing of the figure's column (another column's heading read
            // first) runs on to the next heading of its column when text of that column stands level with the figure (an
            // icon at the start of a note beside a heading).
            var end = segIdx + 1;
            while (end < blocks.Count && blocks[end].Kind != BlockKind.Heading) end++;
            var column = Enumerable.Range(segIdx + 1, end - segIdx - 1).Where(InColumn).ToList();
            bool Level(int i) => blockLines[i] is { Count: > 0 } ls && ls.Any(l => l.Y < top && l.Y + l.Size > bottom);
            if (column.Count == 0 && Enumerable.Range(0, blocks.Count).Any(i => Near(i) && Level(i)))
            {
                end = segIdx + 1;
                while (end < blocks.Count && !(blocks[end].Kind == BlockKind.Heading && InColumn(end))) end++;
                column = Enumerable.Range(segIdx + 1, end - segIdx - 1).Where(InColumn).ToList();
            }
            var insert = column.Where(i => blocks[i].SortTop <= top).Select(i => (int?)i).FirstOrDefault()
                         ?? (column.Count > 0 ? column[^1] + 1 : end);
            var figure = new Block { Id = run.NextBlockId++, Kind = BlockKind.Figure, Y = bottom, SortTop = top, Figs = group.Select(f => f.op).ToList() };
            // The block before the figure's place ends above it: the figure's space is what lies
            // between them, and the block after it, which measured its own space to that block
            // across the figure, keeps only what lies below the figure.
            if (insert > 0 && blocks[insert - 1] is { Kind: BlockKind.Paragraph or BlockKind.Heading or BlockKind.List } before && before.Y > top)
            {
                var space = Math.Round(before.Y - top - usualGap, 1);
                if (space > EdgeToleranceFloor) figure.Layout = new ParagraphLayout(null, 0, 0) { SpaceBefore = space };
                if (insert < blocks.Count && blocks[insert].Layout is { SpaceBefore: > 0 } layout)
                {
                    var after = blocks[insert];
                    var left = Math.Round(layout.SpaceBefore - (before.Y - bottom), 1);
                    after.Layout = layout with { SpaceBefore = left > EdgeToleranceFloor ? left : 0 };
                    blocks[insert] = after;
                }
            }
            blocks.Insert(insert, figure);
            blockLines.Insert(insert, null);
        }
    }

    /// <summary>Index of the heading block whose section contains a figure whose BOTTOM is
    /// at <paramref name="y"/> (the last heading above that bottom), or -1 for the region
    /// before the first heading.</summary>
    // An icon stands within this many ems of a heading's text to be part of it.
    private const double HeadingIconReachEms = 2.0;

    /// <summary>Whether the span <paramref name="left"/>..<paramref name="right"/> lies beside
    /// the text of the block's lines, within a couple of ems of it.</summary>
    private static bool BesideText(List<Line>? lines, double left, double right)
    {
        if (lines is null || lines.Count == 0) return true;
        var textLeft = lines.Min(l => l.MinX);
        var textRight = lines.Where(l => l.Frags.Count > 0).Select(l => l.Frags[^1].R).DefaultIfEmpty(textLeft).Max();
        var reach = HeadingIconReachEms * lines[0].Size;
        return right >= textLeft - reach && left <= textRight + reach;
    }

    private static int SegmentOf(List<Block> blocks, double y)
    {
        var seg = -1;
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i].Kind == BlockKind.Heading && blocks[i].Y > y)
                seg = i;
        return seg;
    }

    private static List<List<(double y, double x, double w, double h, int op)>> GroupRows(
        List<(double y, double x, double w, double h, int op)> figs)
    {
        var groups = new List<List<(double y, double x, double w, double h, int op)>>();
        foreach (var f in figs)
        {
            var open = groups.Count > 0 ? groups[^1] : null;
            if (open is not null && open.All(m => f.y + f.h > m.y && f.y < m.y + m.h))
                open.Add(f);
            else
                groups.Add(new List<(double y, double x, double w, double h, int op)> { f });
        }
        return groups;
    }

    /// <summary>The indirect references of the page's link annotations that lead somewhere - by an action,
    /// or to a destination in the document - in /Annots order — used to attach OBJR object-references to
    /// Link structure elements.</summary>
    private static List<Aspose.Pdf.Core.PdfObject> GetLinkRefs(Page page)
    {
        var refs = new List<Aspose.Pdf.Core.PdfObject>();
        try
        {
            var reader = page.Reader;
            if (reader.Resolve(page.Dict.Get("Annots")) is Aspose.Pdf.Core.PdfArray annots)
            {
                foreach (var item in annots)
                {
                    var ad = reader.ResolveDict(item);
                    if (ad is not null && ad.GetName("Subtype") == "Link" && (ad.Get("A") is not null || ad.Get("Dest") is not null))
                        refs.Add(item);
                }
            }
        }
        catch { /* best-effort: a page with no resolvable /Annots simply has no links */ }
        return refs;
    }

    /// <summary>A link annotation's /Rect, normalised; null when it has none.</summary>
    private static Aspose.Pdf.Rectangle? LinkRect(Page page, Aspose.Pdf.Core.PdfObject linkRef)
    {
        var reader = page.Reader;
        if (reader.ResolveDict(linkRef) is not { } ad || reader.Resolve(ad.Get("Rect")) is not Aspose.Pdf.Core.PdfArray r || r.Count < 4)
            return null;
        double N(int i) => reader.Resolve(r[i]) switch
        {
            Aspose.Pdf.Core.PdfInteger n => n.Value,
            Aspose.Pdf.Core.PdfReal d => d.Value,
            _ => 0,
        };
        return new Aspose.Pdf.Rectangle(Math.Min(N(0), N(2)), Math.Min(N(1), N(3)), Math.Max(N(0), N(2)), Math.Max(N(1), N(3)));
    }

    // A stroke at least this long counts as a table rule (a cell of a separated-border table
    // draws its own short edges).
    private const double MinRuleLength = 8.0;
    // A grid line: the rules at one height (one x) together cover at least this share of the
    // table's width (height). A form's fill-in underline does not.
    private const double GridRuleShare = 0.5;
    // A band at an end of the ruled rows this many times taller than any other, which no column
    // rule reaches, lies outside the table.
    private const double ApartBandFactor = 2.0;

    /// <summary>Detect a table on a page from its ruling lines: the heights and x positions
    /// whose rules span most of the grid are its row and column edges (a separated-border table
    /// draws one short rule per cell, which add up). Needs at least two cells. Returns the
    /// table's region, its row/column count and its grid lines. The edges of the page's shades (<paramref name="shades"/>)
    /// bound rows as the rules of a box do.</summary>
    private static List<(Aspose.Pdf.Rectangle region, int rows, int cols, List<double> colX, List<double> rowY)> DetectTables(List<PageContentScan.Rule> rules,
        IReadOnlyList<PageContentScan.Rule>? shades = null)
    {
        var tables = new List<(Aspose.Pdf.Rectangle, int, int, List<double>, List<double>)>();
        var h = rules.Where(r => r.Horizontal && r.To - r.From >= MinRuleLength).ToList();
        var v = rules.Where(r => !r.Horizontal && r.To - r.From >= MinRuleLength).ToList();
        if (h.Count < 2 || v.Count < 2) return tables;

        var colCand = ClusterValues(v.Select(r => r.At), 3.0);
        var rowCand = ClusterValues(h.Select(r => r.At), 3.0);
        double Covered(List<PageContentScan.Rule> set, double at) =>
            Union(set.Where(r => Math.Abs(r.At - at) <= 3.0).Select(r => (r.From, r.To))).Sum(iv => iv.R - iv.L);

        // Rows over the columns' span.
        var width = colCand.Max() - colCand.Min();
        var rows = rowCand.Where(y => Covered(h, y) >= GridRuleShare * width).ToList();
        if (rows.Count < 2) return tables;

        // Two framed tables one above the other with a caption between them: the frame breaks
        // at the caption's band. A band no column rule crosses, between bands whose frame
        // stands at both ends of the row lines bounding it, separates two tables; each is a
        // table of its own, its columns found over its own height. (A row across an open-sided
        // table, or across a framed one whose frame runs on, keeps the table whole.)
        // A column rule crosses a band when its pieces (a rule drawn cell by cell) cover the band.
        bool Crossed(double bottom, double top, double? at = null) => (at is { } x ? [x] : colCand)
            .Any(c => Union(v.Where(r => Math.Abs(r.At - c) <= 3.0 && r.To > bottom && r.From < top)
                    .Select(r => (Math.Max(r.From, bottom), Math.Min(r.To, top)))).Sum(iv => iv.R - iv.L)
                >= top - bottom - 2 * 3.0);
        (double L, double R) Extent(double y)
        {
            var at = h.Where(r => Math.Abs(r.At - y) <= 3.0).ToList();
            return (at.Min(r => r.From), at.Max(r => r.To));
        }
        bool Framed(int band, (double L, double R) edges)
            => band >= 0 && band < rows.Count - 1
               && Crossed(rows[band], rows[band + 1], edges.L) && Crossed(rows[band], rows[band + 1], edges.R);
        var start = 0;
        for (var i = 1; i < rows.Count - 2; i++)
        {
            if (Crossed(rows[i], rows[i + 1]) || !Framed(i - 1, Extent(rows[i])) || !Framed(i + 1, Extent(rows[i + 1]))) continue;
            AddTable(rows.GetRange(start, i + 1 - start));
            start = i + 1;
        }
        AddTable(rows.GetRange(start, rows.Count - start));
        return tables;

        void AddTable(List<double> tableRows)
        {
            // A band at either end that no column rule reaches is no row of the table: the space up
            // to a rule elsewhere on the page (a running header's, a section's).
            // It stands out by its height too: taller than any row of the table several times over -
            // or by the frame: where rules down both sides close rows of the table in, a band they
            // do not reach lies outside it, whatever its height.
            tableRows = new List<double>(tableRows);
            bool Apart(int band)
            {
                var height = tableRows[band + 1] - tableRows[band];
                var others = Enumerable.Range(0, tableRows.Count - 1).Where(b => b != band).Max(b => tableRows[b + 1] - tableRows[b]);
                var framed = Enumerable.Range(0, tableRows.Count - 1).Any(b =>
                    Crossed(tableRows[b], tableRows[b + 1], colCand.Min()) && Crossed(tableRows[b], tableRows[b + 1], colCand.Max()));
                return (height > ApartBandFactor * others || framed)
                       && !v.Any(r => r.From < tableRows[band + 1] - 3.0 && r.To > tableRows[band] + 3.0);
            }
            while (tableRows.Count > 2 && Apart(0)) tableRows.RemoveAt(0);
            while (tableRows.Count > 2 && Apart(tableRows.Count - 2)) tableRows.RemoveAt(tableRows.Count - 1);
            if (tableRows.Count < 2) return;
            var (bottom, top) = (tableRows.Min(), tableRows.Max());
            var within = v.Where(r => r.From < top - 3.0 && r.To > bottom + 3.0)
                .Select(r => r with { From = Math.Max(r.From, bottom), To = Math.Min(r.To, top) }).ToList();
            // A column edge rules most of the table's height, or the whole of one of its rows (a
            // table whose label rows run across it rules its columns only in its data rows);
            // cells with no rule between them merge again (MergedCells).
            bool RulesARow(double x) => Enumerable.Range(0, tableRows.Count - 1).Any(i =>
            {
                var (lo, hi) = (tableRows[i], tableRows[i + 1]);
                if (hi - lo < MinRuleLength) return false;
                var band = within.Where(r => r.From < hi && r.To > lo)
                    .Select(r => r with { From = Math.Max(r.From, lo), To = Math.Min(r.To, hi) }).ToList();
                return Covered(band, x) >= hi - lo - 2 * GridLineTolerance;
            });
            var cols = colCand.Where(x => Covered(within, x) >= GridRuleShare * (top - bottom) || RulesARow(x)).ToList();
            // Rows ruled at one side only: a rule inside the table from its outer edge across the cells there (a
            // worksheet's boxes for each entry's number and amount) bounds a row across the table; the columns its
            // cells' sides rule are then found over those rows.
            if (cols.Count >= 2)
            {
                var partial = rowCand.Where(y => y > bottom + GridLineTolerance && y < top - GridLineTolerance
                                                 && !tableRows.Any(r => Math.Abs(r - y) <= GridLineTolerance)
                                                 && Union(h.Where(r => Math.Abs(r.At - y) <= GridLineTolerance).Select(r => (r.From, r.To)))
                                                     .Any(iv => iv.R - iv.L >= MinRuleLength
                                                                && (Math.Abs(iv.R - cols.Max()) <= GridLineTolerance || Math.Abs(iv.L - cols.Min()) <= GridLineTolerance)))
                    .ToList();
                if (partial.Count > 0)
                {
                    // Each such row edge parts the row's cells across the table, drawn or not (the text column is ruled nowhere).
                    rules.AddRange(partial.Select(y => new PageContentScan.Rule(true, y, cols.Min(), cols.Max(), Drawn: false)));
                    tableRows = tableRows.Concat(partial).OrderBy(y => y).ToList();
                    cols = colCand.Where(x => Covered(within, x) >= GridRuleShare * (top - bottom) || RulesARow(x)).ToList();
                }
                // So does a shade's edge from the table's outer edge across whole cells there (a grey box beside a subheading,
                // under a white gap): it parts the cells it runs along (MergedCells, by ShadeEdges); the cells beside it span it.
                bool OnColumn(double x) => cols.Any(c => Math.Abs(c - x) <= GridLineTolerance);
                var shaded = (shades ?? []).Where(r => r.Horizontal && r.At > bottom + GridLineTolerance && r.At < top - GridLineTolerance
                                                        && !tableRows.Any(y => Math.Abs(y - r.At) <= GridLineTolerance)
                                                        && OnColumn(r.From) && OnColumn(r.To)
                                                        && (Math.Abs(r.To - cols.Max()) <= GridLineTolerance || Math.Abs(r.From - cols.Min()) <= GridLineTolerance))
                    .Select(r => r.At).ToList();
                if (shaded.Count > 0) tableRows = tableRows.Concat(ClusterValues(shaded, GridLineTolerance)).OrderBy(y => y).ToList();
            }
            // A table ruled cell by cell with no column rule drawn (a statistical release, each cell's top and bottom a
            // stroke of its own): its row rules break where the cells' sides stand. An x where they break on most of
            // the rows is a column edge, and the cells' sides there are ruled in the rows whose rules break at it, so
            // the cells stay apart (MergedCells) while a cell drawn across the break spans it.
            if (cols.Count >= 1) cols = cols.Concat(BrokenColumns(h, tableRows, cols, rules)).OrderBy(x => x).ToList();
            // A table with no frame at its sides: its row rules reach past the outer column rules,
            // and the columns out there end where the rules do.
            if (cols.Count >= 1)
            {
                // Each row rule's stretch joined to the grid (a rule over another text column at the
                // same height is not the table's), and the ends most rows reach.
                var mid = (cols.Min() + cols.Max()) / 2;
                var reach = tableRows.Select(y => Union(h.Where(r => Math.Abs(r.At - y) <= GridLineTolerance).Select(r => (r.From, r.To)))
                        .FirstOrDefault(iv => iv.L <= mid && iv.R >= mid))
                    .Where(iv => iv.R > iv.L).ToList();
                if (reach.Count == 0) reach.Add((cols.Min(), cols.Max()));
                var left = reach.Select(iv => iv.L).OrderBy(x => x).ElementAt(reach.Count / 2);
                var right = reach.Select(iv => iv.R).OrderBy(x => x).ElementAt((reach.Count - 1) / 2);
                if (cols.Min() - left >= MinRuleLength) cols.Insert(0, left);
                if (right - cols.Max() >= MinRuleLength) cols.Add(right);
            }
            // One cell is a table only when rules close it on all four sides: a box drawn round a block (a notice set on a
            // shade), which the frames take (FindFrames).
            var boxed = cols.Count == 2 && tableRows.Count == 2 && Crossed(bottom, top, cols[0]) && Crossed(bottom, top, cols[1]);
            if (cols.Count < 2 || (tableRows.Count - 1) * (cols.Count - 1) < 2 && !boxed) return;
            var region = new Aspose.Pdf.Rectangle(cols.Min(), bottom, cols.Max(), top);
            tables.Add((region, tableRows.Count - 1, cols.Count - 1, cols, tableRows));
        }
    }

    // A row rule's break (an end of one of its strokes) within this many points of an x breaks it there.
    private const double BreakTolerance = 1.5;

    /// <summary>The column edges of a table ruled cell by cell: the x positions, other than those in <paramref name="cols"/>,
    /// where the strokes of the table's row rules (<paramref name="h"/>, at the heights <paramref name="tableRows"/>) end
    /// on at least <see cref="GridRuleShare"/> of the rows. Each is ruled, undrawn, down every row whose bounding rule
    /// breaks at it (added to <paramref name="rules"/>).</summary>
    private static List<double> BrokenColumns(List<PageContentScan.Rule> h, List<double> tableRows, List<double> cols,
        List<PageContentScan.Rule> rules)
    {
        var rowRules = h.Where(r => tableRows.Any(y => Math.Abs(r.At - y) <= GridLineTolerance)).ToList();
        bool BreaksAt(double x, double y) => rowRules.Any(r => Math.Abs(r.At - y) <= GridLineTolerance
                                                                && (Math.Abs(r.From - x) <= BreakTolerance || Math.Abs(r.To - x) <= BreakTolerance));
        var found = new List<double>();
        foreach (var x in ClusterValues(rowRules.SelectMany(r => new[] { r.From, r.To }), BreakTolerance))
        {
            if (cols.Any(c => Math.Abs(c - x) <= GridLineTolerance) || found.Any(c => Math.Abs(c - x) <= GridLineTolerance)) continue;
            if (tableRows.Count(y => BreaksAt(x, y)) < GridRuleShare * tableRows.Count) continue;
            found.Add(x);
            for (var i = 0; i + 1 < tableRows.Count; i++)
                if (BreaksAt(x, tableRows[i]) || BreaksAt(x, tableRows[i + 1]))
                    rules.Add(new PageContentScan.Rule(false, x, tableRows[i], tableRows[i + 1], Drawn: false));
        }
        return found;
    }

    /// <summary>Collapse near-equal values into one representative each (the lowest), returning
    /// the distinct cluster values. A value within <paramref name="tol"/> of the one before joins
    /// its cluster while the cluster stays within twice that: an edge drawn as two or three
    /// hairlines a point or two apart is one grid line, not a sliver of an empty row.</summary>
    private static List<double> ClusterValues(IEnumerable<double> values, double tol)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var result = new List<double>();
        var previous = double.NegativeInfinity;
        foreach (var v in sorted)
        {
            if (result.Count == 0 || v - previous > tol || v - result[^1] > 2 * tol)
                result.Add(v);
            previous = v;
        }
        return result;
    }

    /// <summary>Author the block list into the structure tree. A heading-bearing document is
    /// wrapped in a Part whose heading hierarchy nests sections (each heading below the top
    /// level opens a Sect under its parent section); a flat document adds its paragraphs and
    /// figures directly under the root.</summary>
    private static void BuildTree(ITaggedContent tc, LogicalStructure.StructureElement root, List<Block> blocks, TaggingRun run)
    {
        var tg = new TagTreeBuildState();
        tg.tc = tc;
        tg.root = root;
        tg.blocks = blocks;
        tg.run = run;
        tg.headingCount = tg.blocks.Count(b => b.Kind == BlockKind.Heading);
        var minHeadingLevel = tg.blocks.Where(b => b.Kind == BlockKind.Heading)
            .Select(b => b.Level).DefaultIfEmpty(0).Min();

        // A heading hierarchy (two or more headings) is wrapped in a Part with nested sections;
        // a document with at most one heading stays flat, its content (including any lone
        // heading) directly under the Document root.
        // The blocks of one frame, one after another, are grouped under its Div.
        (LogicalStructure.StructureElement Div, Frame Frame, LogicalStructure.StructureElement Parent)? framed = null;
        // The entries of a table of contents one after another are the TOCIs of one TOC.
        (LogicalStructure.StructureElement Toc, LogicalStructure.StructureElement Parent)? toc = null;
        void AddContent(LogicalStructure.StructureElement container, Block b)
        {
            if (b.Toc && b.Frame is null)
            {
                framed = null;
                if (toc is not { } list || !ReferenceEquals(list.Parent, container))
                {
                    toc = (tg.tc.CreateTOCElement(), container);
                    // (where the entries stand, in their section: the authoring API's rule keeps a TOC under the root)
                    container.AppendChild(toc.Value.Toc, validate: false);
                }
                toc.Value.Toc.AppendChild(MakeContent(tg, b));
                return;
            }
            toc = null;
            if (b.Frame is not { } frame)
            {
                framed = null;
                container.AppendChild(MakeContent(tg, b));
                return;
            }
            if (framed is not { } open || !ReferenceEquals(open.Frame, frame) || !ReferenceEquals(open.Parent, container))
            {
                framed = (FrameDiv(tg.tc, frame), frame, container);
                container.AppendChild(framed.Value.Div);
            }
            framed.Value.Div.AppendChild(MakeContent(tg, b));
        }
        if (tg.headingCount < 2)
        {
            foreach (var b in tg.blocks)
                AddContent(tg.root, b);
            return;
        }

        tg.part = tg.tc.CreatePartElement();
        tg.root.AppendChild(tg.part);
        tg.stack = new List<(int level, LogicalStructure.StructureElement container)>
        {
            (minHeadingLevel, tg.part),
        };

        foreach (var b in tg.blocks)
        {
            // A heading inside a box heads the box's text: it opens no section of the document's.
            if (b.Kind == BlockKind.Heading && b.Frame is null)
            {
                framed = null;
                while (tg.stack.Count > 1 && tg.stack[^1].level >= b.Level)
                    tg.stack.RemoveAt(tg.stack.Count - 1);
                var parent = tg.stack[^1].container;
                if (b.Level <= minHeadingLevel)
                {
                    // The top-level heading sits directly in the Part (its implicit section).
                    parent.AppendChild(MakeHeaderEl(tg, b));
                }
                else
                {
                    var sect = tg.tc.CreateSectElement();
                    parent.AppendChild(sect);
                    sect.AppendChild(MakeHeaderEl(tg, b));
                    tg.stack.Add((b.Level, sect));
                }
            }
            else
            {
                AddContent(tg.stack[^1].container, b);
            }
        }
    }

    /// <summary>Cluster a page's text fragments into lines (by baseline Y), ordered
    /// top-to-bottom. Each line records its dominant font size.</summary>
    // Glyphs whose bottoms are within this many points of a line's are on that line.
    private const double LineTolerance = 3.0;
    // Text drawn less than this many points tall cannot be seen: a producer's hidden copy of words it shows another way
    // (an accessible label under the label it draws as an artifact) is no text of the page's.
    private const double MinVisibleTextHeight = 1.0;

    private static List<Line> CollectLines(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        try { page.Accept(absorber); }
        catch { return new List<Line>(); }

        // Snapshot each fragment's geometry once, skipping any with no resolved rectangle.
        var glyphs = new List<Glyph>();
        var sideways = new List<(Glyph Glyph, int Turn)>();
        foreach (TextFragment f in absorber.TextFragments)
        {
            if (string.IsNullOrWhiteSpace(f.Text)) continue;
            var rect = f.Rectangle;
            if (rect is null) continue;
            var size = f.TextState?.FontSize ?? 0;
            var glyph = new Glyph(rect.LLY, rect.LLX, size, f.BaselinePosition?.YIndent ?? rect.LLY, rect.URX, rect.URY, f.Text, f.TextState?.Font?.FontName);
            if (Turn(f.TextState?.Rotation ?? 0) is { } turn) sideways.Add((glyph, turn));
            else
                foreach (var (x, r, text) in WidePieces(f, rect, size))
                    glyphs.Add(glyph with { x = x, r = r, text = text });
        }
        var stamps = MarginStamps(glyphs, sideways);

        // Group glyphs whose bottom Y is within a small tolerance into one line: of the line's
        // first glyph or of its lowest so far (a bullet set a little below its text, beside a
        // neighbouring column's line a little above it, still joins its text).
        var buckets = new List<List<Glyph>>();
        var bucketY = new List<double>();
        var bucketLow = new List<double>();
        foreach (var g in glyphs.OrderByDescending(g => g.y))
        {
            var idx = -1;
            for (var i = 0; i < bucketY.Count; i++)
                if (Math.Abs(bucketY[i] - g.y) <= LineTolerance || Math.Abs(bucketLow[i] - g.y) <= LineTolerance) { idx = i; break; }
            if (idx < 0)
            {
                buckets.Add(new List<Glyph>());
                bucketY.Add(g.y);
                bucketLow.Add(g.y);
                idx = buckets.Count - 1;
            }
            buckets[idx].Add(g);
            bucketLow[idx] = Math.Min(bucketLow[idx], g.y);
        }

        var lines = new List<Line>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var items = buckets[i];
            var size = DominantSize(items.Select(it => (it.size, it.text)));
            lines.Add(new Line
            {
                Y = bucketY[i], Size = size, MinX = items.Min(it => it.x),
                Baseline = TextBaseline(items.Select(it => (it.size, it.baseline, it.text)), size),
                Frags = items.OrderBy(it => it.x).Select(it => (it.x, it.r, it.text, it.y, it.baseline, it.size, it.font)).ToList(),
            });
        }
        return MergeRaisedFragments(lines.OrderByDescending(l => l.Y).ToList()).Concat(stamps).ToList();
    }

    /// <summary>One text fragment's box and text: bottom, left, size, baseline, right, top.</summary>
    private readonly record struct Glyph(double y, double x, double size, double baseline, double r, double top, string text, string? font);

    // Text turned a quarter either way, within this many degrees, runs up or down the page.
    private const double SidewaysTolerance = 15;
    private const int SidewaysUp = 90;
    private const int SidewaysDown = 270;

    /// <summary>Whether text at <paramref name="rotation"/> degrees runs up the page
    /// (<see cref="SidewaysUp"/>), down it (<see cref="SidewaysDown"/>), or along it (null).</summary>
    private static int? Turn(double rotation)
    {
        var r = ((rotation % 360) + 360) % 360;
        if (Math.Abs(r - SidewaysUp) <= SidewaysTolerance) return SidewaysUp;
        if (Math.Abs(r - SidewaysDown) <= SidewaysTolerance) return SidewaysDown;
        return null;
    }

    /// <summary>Text set on its side in the margin beside the body - a stamp along the page's
    /// edge - is no line of the text: each such fragment is a line marked as an artifact. Text
    /// set on its side among the body joins <paramref name="glyphs"/> and lines up as any other.</summary>
    private static List<Line> MarginStamps(List<Glyph> glyphs, List<(Glyph Glyph, int Turn)> sideways)
    {
        var stamps = new List<Line>();
        var left = glyphs.Count > 0 ? glyphs.Min(g => g.x) : 0;
        var right = glyphs.Count > 0 ? glyphs.Max(g => g.r) : 0;
        foreach (var (g, turn) in sideways)
        {
            if (glyphs.Count == 0 || (g.r >= left && g.x <= right))
            {
                glyphs.Add(g);
                continue;
            }
            // Text running up starts its baseline at its box's bottom, text running down at its top.
            var start = turn == SidewaysUp ? g.y : g.top;
            stamps.Add(new Line
            {
                Y = g.y, Baseline = start, Size = g.size, MinX = g.x, Running = ContentMarker.Artifact,
                Frags = [(g.x, g.r, g.text, g.y, start, g.size, g.font)],
            });
        }
        return stamps;
    }

    /// <summary>A line's baseline: its text's, the middle one of the baselines of its fragments in
    /// its own size - a note call or a mark raised over it sits above it.</summary>
    private static double TextBaseline(IEnumerable<(double Size, double Base, string Text)> frags, double size)
    {
        var all = frags.ToList();
        var own = all.Where(f => Math.Abs(f.Size - size) < 0.5 && f.Text.Trim().Length > MaxMarkChars).Select(f => f.Base).OrderBy(b => b).ToList();
        if (own.Count == 0) own = all.Where(f => Math.Abs(f.Size - size) < 0.5).Select(f => f.Base).OrderBy(b => b).ToList();
        return own.Count > 0 ? own[own.Count / 2] : all[0].Base;
    }

    /// <summary>The size most of a line's characters are set in: a bullet or a note call drawn
    /// in another size does not set the line's.</summary>
    private static double DominantSize(IEnumerable<(double Size, string Text)> frags)
        => frags.GroupBy(f => Math.Round(f.Size, 1))
            .OrderByDescending(g => g.Sum(f => f.Text.Count(c => !char.IsWhiteSpace(c))))
            .ThenByDescending(g => g.Count()).First().Key;

    // A superscript or subscript is smaller than its line (at most this share of its size) and
    // sits within this share of the line's size above or below it.
    private const double RaisedSizeFactor = 0.9;
    private const double RaisedRiseFactor = 0.6;
    // Marks set in the line's own size (a revision "r" after a column heading) sit within this
    // share of the size above or below it, each touching the end of one of its words, at most
    // this many characters long.
    private const double AttachedRiseFactor = 0.8;
    private const int MaxMarkChars = 2;
    private const double TouchTolerance = 1.0;

    /// <summary>Whether every fragment of <paramref name="marks"/> is a short mark starting where
    /// a fragment of <paramref name="host"/> ends: a superscript set in full size.</summary>
    private static bool MarksAttached(Line marks, Line host)
        => marks.Frags.Count > 0 && marks.Frags.All(f => f.Text.Trim().Length is > 0 and <= MaxMarkChars
                                                         && host.Frags.Any(h => Math.Abs(f.X - h.R) <= TouchTolerance));

    // A mark opening a line ends at most this share of the line's size before the line's text starts.
    private const double OpeningMarkGap = 0.5;

    /// <summary>Whether <paramref name="marks"/> are short marks, the first ending where <paramref name="host"/>'s text
    /// starts: a footnote's number raised at the start of its first line (and marks raised further along it).</summary>
    private static bool OpensLine(Line marks, Line host)
        => marks.Frags.Count > 0 && marks.Frags.All(f => f.Text.Trim().Length is > 0 and <= MaxMarkChars)
           && marks.Frags[0].R <= host.MinX + TouchTolerance && marks.Frags[0].R >= host.MinX - OpeningMarkGap * host.Size;

    /// <summary>Fold a line of smaller text raised or lowered a little beside a line (a
    /// superscript note call, a subscript) into that line: it is part of the line's text, not
    /// a line of its own.</summary>
    private static List<Line> MergeRaisedFragments(List<Line> lines)
    {
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            var small = lines[i];
            var host = lines.Where(l => l != small
                    && ((small.Size <= RaisedSizeFactor * l.Size && Math.Abs(small.Y - l.Y) <= RaisedRiseFactor * l.Size)
                        || (Math.Abs(small.Y - l.Y) <= AttachedRiseFactor * l.Size && MarksAttached(small, l)))
                    && l.Frags.Count > 0 && (small.MinX >= l.MinX - 1 || OpensLine(small, l))
                    && small.MinX <= l.Frags[^1].R + l.Size)
                .OrderBy(l => Math.Abs(small.Y - l.Y)).FirstOrDefault();
            if (host is null) continue;
            host.Frags = host.Frags.Concat(small.Frags).OrderBy(f => f.X).ToList();
            lines.RemoveAt(i);
        }
        return lines;
    }
}
