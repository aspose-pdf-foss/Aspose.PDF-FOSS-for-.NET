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
/// <see cref="ITaggedContent"/> so it serialises and round-trips like a hand-authored one.
/// Driven by <see cref="AutoTaggingSettings"/> during PDF/A / PDF/UA conversion.
/// </summary>
internal static class AutoTagger
{
    private sealed class Line
    {
        public double Y;
        public double Size;
        public double MinX;
    }

    private enum BlockKind { Heading, Paragraph, Figure, Table }

    private struct Block
    {
        public BlockKind Kind;
        public int Level;     // heading level (1..6) for Heading blocks
        public List<Aspose.Pdf.Core.PdfObject>? Links; // link-annotation refs inside a paragraph
        public int Rows, Cols; // grid dimensions for Table blocks
        public double Y;      // bottom anchor (baseline of the block's last/only line)
        public double SortTop; // top edge, orders blocks inside a heading segment
        public double TextMinX; // heading text left edge (orders absorbed icons around the MCR)
        public List<double>? HeaderFigXs; // x of figures absorbed into a Heading (inline icons)
        public int FigCount;  // images in a grouped figure paragraph (Figure blocks)
        public int InlineFigures; // small in-line images inside a text paragraph
    }

    public static void Apply(Document document, AutoTaggingSettings settings)
    {
        var tc = document.TaggedContent;
        // Regenerate from scratch: clear any pre-existing structure tree, then root the new
        // one in a Document element.
        var structRoot = tc.StructTreeRootElement;
        structRoot.ClearChildren();
        var docRoot = new LogicalStructure.DocumentElement();
        structRoot.AppendChild(docRoot);

        var blocks = new List<Block>();
        foreach (var page in document.Pages)
            blocks.AddRange(AnalyzePage(page));

        BuildTree(tc, docRoot, blocks);
        tc.Save();
    }

    /// <summary>Reduce a page to an ordered list of structural blocks: headings (with a level
    /// inferred from font size), body paragraphs (one per run of consecutive body lines) and
    /// figures (one per image).</summary>
    private static List<Block> AnalyzePage(Page page)
    {
        var result = new List<Block>();
        var lines = CollectLines(page);

        // The body text size is the most common line size; any distinct larger size is a
        // heading. Heading levels rank the distinct heading sizes largest→smallest.
        double bodySize = lines.Count > 0
            ? lines.GroupBy(l => Math.Round(l.Size, 1)).OrderByDescending(g => g.Count())
                   .ThenBy(g => g.Key).First().Key
            : 0;

        var headingSizes = lines.Select(l => Math.Round(l.Size, 1))
            .Where(s => s > bodySize + 0.5)
            .Distinct().OrderByDescending(s => s).ToList();

        // Tables are detected from ruling lines; their cell text is excluded from the
        // paragraph/heading flow and a single Table block is emitted at the table's position.
        var tables = DetectTables(page);
        var tableEmitted = new bool[tables.Count];

        var inParagraph = false;
        double paraY = 0;
        // Per-paragraph line list (parallel to result), used to spot in-line images.
        var blockLines = new List<List<Line>?>();
        List<Line>? paraLines = null;
        void Emit(Block b, List<Line>? bl)
        {
            result.Add(b);
            blockLines.Add(bl);
        }
        void FlushParagraph()
        {
            if (!inParagraph) return;
            var first = paraLines![0];
            Emit(new Block { Kind = BlockKind.Paragraph, Y = paraY, SortTop = first.Y + first.Size }, paraLines);
            inParagraph = false;
            paraLines = null;
        }

        foreach (var line in lines)
        {
            var inTable = -1;
            for (var t = 0; t < tables.Count; t++)
                if (line.Y >= tables[t].region.LLY - 2 && line.Y <= tables[t].region.URY + 2)
                { inTable = t; break; }

            if (inTable >= 0)
            {
                FlushParagraph();
                if (!tableEmitted[inTable])
                {
                    Emit(new Block { Kind = BlockKind.Table, Rows = tables[inTable].rows, Cols = tables[inTable].cols, Y = line.Y, SortTop = line.Y + line.Size }, null);
                    tableEmitted[inTable] = true;
                }
                continue; // the table's cell text doesn't form paragraphs
            }

            var sz = Math.Round(line.Size, 1);
            var headingIdx = headingSizes.IndexOf(sz);
            if (headingIdx >= 0)
            {
                FlushParagraph();
                Emit(new Block { Kind = BlockKind.Heading, Level = Math.Min(headingIdx + 1, 6), Y = line.Y, SortTop = line.Y + line.Size, TextMinX = line.MinX }, null);
            }
            else
            {
                // Accumulate consecutive body lines into one paragraph.
                if (!inParagraph) { paraY = line.Y; paraLines = new List<Line>(); }
                inParagraph = true;
                paraLines!.Add(line);
                paraY = line.Y;
            }
        }
        FlushParagraph();

        // Figures: place each image at its rendered position so it lands in the correct
        // section. The section a figure belongs to is the heading band containing the
        // figure's BOTTOM Y (its baseline) — that anchor keeps per-section figure
        // grouping stable, whereas the image's top/centre Y crosses bands for
        // tall floated images. A figure sitting on a heading's line is absorbed into that
        // HeaderElement; otherwise it becomes an image-only paragraph interleaved by Y.
        MergeFigures(result, blockLines, CollectFigures(page));

        // A page whose body is a single paragraph carries its in-text link annotations as
        // children of that paragraph (Link → OBJR + content).
        var links = GetLinkRefs(page);
        if (links.Count > 0 && result.Count(b => b.Kind == BlockKind.Paragraph) == 1
            && result.All(b => b.Kind != BlockKind.Heading && b.Kind != BlockKind.Figure))
        {
            for (var i = 0; i < result.Count; i++)
                if (result[i].Kind == BlockKind.Paragraph)
                {
                    var b = result[i];
                    b.Links = links;
                    result[i] = b;
                }
        }

        return result;
    }

    /// <summary>Collect the page's image placements (one per <c>Do</c>) as (bottomY, x, width,
    /// height), ordered top-to-bottom. Uses placements — not distinct XObjects — so a reused
    /// image counts once per appearance.</summary>
    private static List<(double y, double x, double w, double h)> CollectFigures(Page page)
    {
        var figs = new List<(double, double, double, double)>();
        try
        {
            var abs = new ImagePlacementAbsorber();
            abs.Visit(page);
            foreach (ImagePlacement p in abs.ImagePlacements)
            {
                var r = p.Rectangle;
                if (r is null) continue;
                figs.Add((r.LLY, r.LLX, r.Width, r.Height));
            }
        }
        catch { /* best-effort: a page whose images can't be absorbed simply has no figures */ }
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
    private static void MergeFigures(List<Block> blocks, List<List<Line>?> blockLines,
        List<(double y, double x, double w, double h)> figures)
    {
        var loose = new List<(double y, double x, double w, double h)>();
        foreach (var fig in figures)
        {
            // Heading icon: a small figure whose baseline lies within a line-height of a
            // heading's baseline belongs to that (closest) heading, not a separate paragraph.
            var headerIdx = -1;
            var best = double.MaxValue;
            if (fig.h <= 50)
                for (var i = 0; i < blocks.Count; i++)
                    if (blocks[i].Kind == BlockKind.Heading)
                    {
                        var dy = Math.Abs(fig.y - blocks[i].Y);
                        if (dy <= 14 && dy < best) { best = dy; headerIdx = i; }
                    }
            if (headerIdx >= 0)
            {
                var h = blocks[headerIdx];
                (h.HeaderFigXs ??= new List<double>()).Add(fig.x);
                blocks[headerIdx] = h;
                continue;
            }

            // In-line figure: no taller than the line it sits on, overlapping that line's band.
            var inlineIdx = -1;
            for (var i = 0; i < blocks.Count && inlineIdx < 0; i++)
            {
                if (blocks[i].Kind != BlockKind.Paragraph || blockLines[i] is not { } lines) continue;
                foreach (var line in lines)
                    if (fig.h <= line.Size + 0.5
                        && fig.y + fig.h > line.Y && fig.y < line.Y + line.Size)
                    { inlineIdx = i; break; }
            }
            if (inlineIdx >= 0)
            {
                var b = blocks[inlineIdx];
                b.InlineFigures++;
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
            var segIdx = SegmentOf(blocks, bottom);
            // Insert into the block list inside its segment ordered by top edge.
            var insert = segIdx + 1;
            while (insert < blocks.Count && blocks[insert].Kind != BlockKind.Heading
                   && blocks[insert].SortTop > top)
                insert++;
            blocks.Insert(insert, new Block { Kind = BlockKind.Figure, Y = bottom, SortTop = top, FigCount = group.Count });
            blockLines.Insert(insert, null);
        }
    }

    /// <summary>Index of the heading block whose section contains a figure whose BOTTOM is
    /// at <paramref name="y"/> (the last heading above that bottom), or -1 for the region
    /// before the first heading.</summary>
    private static int SegmentOf(List<Block> blocks, double y)
    {
        var seg = -1;
        for (var i = 0; i < blocks.Count; i++)
            if (blocks[i].Kind == BlockKind.Heading && blocks[i].Y > y)
                seg = i;
        return seg;
    }

    private static List<List<(double y, double x, double w, double h)>> GroupRows(
        List<(double y, double x, double w, double h)> figs)
    {
        var groups = new List<List<(double y, double x, double w, double h)>>();
        foreach (var f in figs)
        {
            var open = groups.Count > 0 ? groups[^1] : null;
            if (open is not null && open.All(m => f.y + f.h > m.y && f.y < m.y + m.h))
                open.Add(f);
            else
                groups.Add(new List<(double y, double x, double w, double h)> { f });
        }
        return groups;
    }

    /// <summary>The indirect references of the page's link annotations that carry an action,
    /// in /Annots order — used to attach OBJR object-references to Link structure elements.</summary>
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
                    if (ad is not null && ad.GetName("Subtype") == "Link" && ad.Get("A") is not null)
                        refs.Add(item);
                }
            }
        }
        catch { /* best-effort: a page with no resolvable /Annots simply has no links */ }
        return refs;
    }

    /// <summary>Detect tables on a page from its ruling lines: a grid of (near-)horizontal and
    /// (near-)vertical strokes whose distinct column / row positions give the cell counts. Returns
    /// each table's bounding region plus its row/column count.</summary>
    private static List<(Aspose.Pdf.Rectangle region, int rows, int cols)> DetectTables(Page page)
    {
        var tables = new List<(Aspose.Pdf.Rectangle, int, int)>();
        var abs = new Vector.GraphicsAbsorber();
        try { abs.Visit(page); } catch { return tables; }

        var colX = new List<double>();
        var rowY = new List<double>();
        foreach (var el in abs.Elements)
        {
            var r = el.Rectangle;
            if (r is null) continue;
            if (r.Width > 30 && r.Height < 3) rowY.Add(r.LLY);        // horizontal rule → row edge
            else if (r.Height > 30 && r.Width < 3) colX.Add(r.LLX);   // vertical rule → column edge
        }

        var cols = ClusterValues(colX, 3.0);
        var rows = ClusterValues(rowY, 3.0);
        if (cols.Count >= 2 && rows.Count >= 2)
        {
            var region = new Aspose.Pdf.Rectangle(cols.Min(), rows.Min(), cols.Max(), rows.Max());
            tables.Add((region, rows.Count - 1, cols.Count - 1));
        }
        return tables;
    }

    /// <summary>Collapse near-equal values (within <paramref name="tol"/>) into one representative
    /// each, returning the distinct cluster centres.</summary>
    private static List<double> ClusterValues(IEnumerable<double> values, double tol)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var result = new List<double>();
        foreach (var v in sorted)
            if (result.Count == 0 || v - result[^1] > tol)
                result.Add(v);
        return result;
    }

    /// <summary>Author the block list into the structure tree. A heading-bearing document is
    /// wrapped in a Part whose heading hierarchy nests sections (each heading below the top
    /// level opens a Sect under its parent section); a flat document adds its paragraphs and
    /// figures directly under the root.</summary>
    private static void BuildTree(ITaggedContent tc, LogicalStructure.StructureElement root,
        List<Block> blocks)
    {
        // Each leaf wraps a marked-content reference (the actual page content); a heading and a
        // body run are an H/P with one MCR, an image is a P that holds a Figure (the Figure
        // wraps the MCR).
        LogicalStructure.StructureElement WithMcr(LogicalStructure.StructureElement el)
        {
            el.AppendChild(new LogicalStructure.MCRElement());
            return el;
        }
        // A heading carries its text (MCR) plus any inline heading-icon figures absorbed
        // into it, in reading order: an icon left of the heading text stands before the
        // MCR, one to its right after it (probed: "## ![icon] *Title*" tags [Figure, MCR],
        // "## *Title* ![icon]" tags [MCR, Figure]).
        LogicalStructure.StructureElement MakeHeaderEl(Block b)
        {
            var h = tc.CreateHeaderElement(b.Level);
            var icons = b.HeaderFigXs ?? new List<double>();
            foreach (var x in icons.Where(x => x < b.TextMinX).OrderBy(x => x))
                h.AppendChild(WithMcr(tc.CreateFigureElement()));
            h.AppendChild(new LogicalStructure.MCRElement());
            foreach (var x in icons.Where(x => x >= b.TextMinX).OrderBy(x => x))
                h.AppendChild(WithMcr(tc.CreateFigureElement()));
            return h;
        }
        LogicalStructure.StructureElement MakeBody(Block b)
        {
            var p = tc.CreateParagraphElement();
            if (b.Links is { Count: > 0 } links)
            {
                // Interleave content runs with inline links: a leading MCR, then each link
                // (Link → OBJR(annotation) + MCR) followed by another content MCR.
                p.AppendChild(new LogicalStructure.MCRElement());
                foreach (var linkRef in links)
                {
                    var link = tc.CreateLinkElement();
                    var objr = new LogicalStructure.OBJRElement();
                    objr.SetObj(linkRef);
                    link.AppendChild(objr);
                    link.AppendChild(new LogicalStructure.MCRElement());
                    p.AppendChild(link);
                    p.AppendChild(new LogicalStructure.MCRElement());
                }
                return p;
            }
            // An in-line image splits the paragraph's content around itself:
            // [MCR, Figure, MCR] (probed: "text ![img] text" in one source paragraph).
            p.AppendChild(new LogicalStructure.MCRElement());
            for (var i = 0; i < b.InlineFigures; i++)
            {
                p.AppendChild(WithMcr(tc.CreateFigureElement()));
                p.AppendChild(new LogicalStructure.MCRElement());
            }
            return p;
        }
        LogicalStructure.StructureElement MakeFigure(Block b)
        {
            // One rendered row of images = one paragraph holding every figure of the row.
            var p = tc.CreateParagraphElement();
            for (var i = 0; i < Math.Max(1, b.FigCount); i++)
                p.AppendChild(WithMcr(tc.CreateFigureElement()));
            return p;
        }
        LogicalStructure.StructureElement MakeTable(Block b)
        {
            var table = tc.CreateTableElement();
            for (var r = 0; r < Math.Max(1, b.Rows); r++)
            {
                var tr = tc.CreateTableTRElement();
                for (var c = 0; c < Math.Max(1, b.Cols); c++)
                    tr.AppendChild(WithMcr(tc.CreateTableTDElement()));
                table.AppendChild(tr);
            }
            return table;
        }
        LogicalStructure.StructureElement MakeContent(Block b) => b.Kind switch
        {
            BlockKind.Figure => MakeFigure(b),
            BlockKind.Table => MakeTable(b),
            BlockKind.Heading => MakeHeaderEl(b),
            _ => MakeBody(b),
        };

        var headingCount = blocks.Count(b => b.Kind == BlockKind.Heading);
        var minHeadingLevel = blocks.Where(b => b.Kind == BlockKind.Heading)
            .Select(b => b.Level).DefaultIfEmpty(0).Min();

        // A heading hierarchy (two or more headings) is wrapped in a Part with nested sections;
        // a document with at most one heading stays flat, its content (including any lone
        // heading) directly under the Document root.
        if (headingCount < 2)
        {
            foreach (var b in blocks)
                root.AppendChild(MakeContent(b));
            return;
        }

        // Heading hierarchy: a Part holds the top-level heading and one Sect per lower-level
        // heading. The stack tracks the open section at each heading level.
        var part = tc.CreatePartElement();
        root.AppendChild(part);
        var stack = new List<(int level, LogicalStructure.StructureElement container)>
        {
            (minHeadingLevel, part),
        };

        foreach (var b in blocks)
        {
            if (b.Kind == BlockKind.Heading)
            {
                while (stack.Count > 1 && stack[^1].level >= b.Level)
                    stack.RemoveAt(stack.Count - 1);
                var parent = stack[^1].container;
                if (b.Level <= minHeadingLevel)
                {
                    // The top-level heading sits directly in the Part (its implicit section).
                    parent.AppendChild(MakeHeaderEl(b));
                }
                else
                {
                    var sect = tc.CreateSectElement();
                    parent.AppendChild(sect);
                    sect.AppendChild(MakeHeaderEl(b));
                    stack.Add((b.Level, sect));
                }
            }
            else
            {
                stack[^1].container.AppendChild(MakeContent(b));
            }
        }
    }

    /// <summary>Cluster a page's text fragments into lines (by baseline Y), ordered
    /// top-to-bottom. Each line records its dominant font size.</summary>
    private static List<Line> CollectLines(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        try { page.Accept(absorber); }
        catch { return new List<Line>(); }

        // Snapshot each fragment's geometry once, skipping any with no resolved rectangle.
        var glyphs = new List<(double y, double x, double size)>();
        foreach (TextFragment f in absorber.TextFragments)
        {
            if (string.IsNullOrWhiteSpace(f.Text)) continue;
            var rect = f.Rectangle;
            if (rect is null) continue;
            var size = f.TextState?.FontSize ?? 0;
            glyphs.Add((rect.LLY, rect.LLX, size));
        }

        // Group glyphs whose baseline Y is within a small tolerance into one line.
        var buckets = new List<List<(double y, double x, double size)>>();
        var bucketY = new List<double>();
        foreach (var g in glyphs.OrderByDescending(g => g.y))
        {
            var idx = -1;
            for (var i = 0; i < bucketY.Count; i++)
                if (Math.Abs(bucketY[i] - g.y) <= 3.0) { idx = i; break; }
            if (idx < 0)
            {
                buckets.Add(new List<(double, double, double)>());
                bucketY.Add(g.y);
                idx = buckets.Count - 1;
            }
            buckets[idx].Add(g);
        }

        var lines = new List<Line>();
        for (var i = 0; i < buckets.Count; i++)
        {
            var items = buckets[i];
            // The line's size is the most common glyph size on it.
            var size = items.GroupBy(it => Math.Round(it.size, 1))
                            .OrderByDescending(grp => grp.Count()).First().Key;
            lines.Add(new Line { Y = bucketY[i], Size = size, MinX = items.Min(it => it.x) });
        }
        return lines.OrderByDescending(l => l.Y).ToList();
    }
}
