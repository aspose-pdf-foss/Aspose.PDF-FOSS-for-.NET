#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;
/// <summary>
/// Reflows the text of a PDF page into Markdown: heading detection (via the document
/// outline when present, otherwise relative font size), paragraph reconstruction from
/// wrapped visual lines, inline emphasis, links, ruled tables, and images (small images
/// inline within a text line; larger ones as their own image blocks).
/// </summary>
internal static partial class MarkdownRenderer
{
    private const string NewLine = "\r\n";
    private const string SoftBreak = "   ";

    // Insert a synthesized inter-word space when the ink-box gap between two glyphs exceeds
    // this fraction of the em. Kept small because these fonts lay out per glyph with no space
    // glyphs; real word gaps are ≈0.23em while intra-word gaps are ≈0.
    private const double SpaceGapRatio = 0.05;

    public static string Render(Document doc, MarkdownSaveOptions options, string outputDir)
    {
        // Heading detection from the outline uses the bookmark DESTINATIONS, not their
        // titles: a line is a `#` heading only when an outline destination lands on it
        // (matching page + top edge). A document whose bookmarks are misaligned (point
        // past the page, or to the wrong page) yields no `#` headings — its big/bold
        // lines render as bold body instead.
        List<HeadingDest> outlineDests = null;
        if (doc.HasOutlines)
        {
            outlineDests = new List<HeadingDest>();
            CollectOutline(doc.Outlines, 1, outlineDests);
        }

        var images = new ImageNumberer(outputDir, options.ResourcesDirectoryName, options.UseImageHtmlTag);

        var blocks = new List<MdBlock>();
        for (var p = 1; p <= doc.Pages.Count; p++)
            RenderPage(doc.Pages[p], p, options, outlineDests, images, blocks);

        if (blocks.Count == 0)
            return string.Empty;

        var outLines = new List<string>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                outLines.Add(string.Empty);
                // An upcoming table opens after two blank lines; a table already closed
                // itself with its own two, so following text adds only the single break.
                if (blocks[i].IsTable)
                    outLines.Add(string.Empty);
            }
            outLines.AddRange(blocks[i].Text.Split(new[] { NewLine }, StringSplitOptions.None));
            if (blocks[i].IsTable)
            {
                outLines.Add(string.Empty);
                outLines.Add(string.Empty);
            }
        }
        return string.Join(NewLine, outLines) + NewLine;
    }

    private static void CollectOutline(IEnumerable items, int level, List<HeadingDest> dests)
    {
        foreach (OutlineItem item in items)
        {
            var dest = item.Destination as Aspose.Pdf.Annotations.ExplicitDestination
                ?? (item.Action as Aspose.Pdf.Annotations.GoToAction)?.Destination
                    as Aspose.Pdf.Annotations.ExplicitDestination;
            if (dest != null)
            {
                var top = DestTop(dest);
                if (!double.IsNaN(top))
                    dests.Add(new HeadingDest(dest.PageNumber, top, level));
            }
            CollectOutline((IEnumerable)item.Children, level + 1, dests);
        }
    }

    // The vertical anchor of an explicit destination, when it carries one (XYZ / FitH /
    // FitBH / FitR). Destinations without a top edge (Fit / FitB / FitV …) can't be matched
    // to a line, so they never mark a heading.
    private static double DestTop(Aspose.Pdf.Annotations.ExplicitDestination dest) => dest switch
    {
        Aspose.Pdf.Annotations.XYZExplicitDestination d => d.Top,
        Aspose.Pdf.Annotations.FitHExplicitDestination d => d.Top,
        Aspose.Pdf.Annotations.FitBHExplicitDestination d => d.Top,
        Aspose.Pdf.Annotations.FitRExplicitDestination d => d.Top,
        _ => double.NaN,
    };

    private static void RenderPage(Page page, int pageNumber, MarkdownSaveOptions options,
        List<HeadingDest> outlineDests, ImageNumberer images, List<MdBlock> blocks)
    {
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(page);

        var fragments = absorber.TextFragments
            .Cast<TextFragment>()
            .Where(f => !string.IsNullOrEmpty(f.Text) && f.Rectangle != null)
            .ToList();

        if (options.AreaToExtract != null)
        {
            var area = options.AreaToExtract;
            fragments = fragments.Where(f => !f.Rectangle.Intersect(area).IsEmpty).ToList();
        }

        var links = CollectLinks(page);

        var tableBlocks = CollectTables(page, fragments, links, options.AreaToExtract, out var tableRegions);
        if (tableRegions.Count > 0)
            fragments = fragments.Where(f => !InAnyRegion(f.Rectangle, tableRegions)).ToList();

        // Group text into lines first — these provide the bands used to decide whether an
        // image sits inline within a line or forms its own image block.
        var textLines = GroupLines(fragments, links);

        // Classify each image inline vs block, group block images into rows, then number
        // every image by first appearance in reading order (rows top-down, within-row LLX
        // ascending; inline images at their host line) and save the unique PNGs.
        var inlineImgs = new List<(ImgPlace img, Line host)>();
        var blockImgs = new List<ImgPlace>();
        var placements = CollectPlacements(page, tableRegions);
        if (options.ExtractVectorGraphics)
            placements.AddRange(CollectVectorGraphics(page, tableRegions));
        foreach (var img in placements)
        {
            // A vector drawing attaches inline only when its CENTER falls inside a text
            // line's band, and its token then opens the line (a decoration, not a glyph
            // in the running text); anything else stands alone as a block drawing.
            var host = img.IsVector ? VectorInlineHost(textLines, img.Rect) : InlineHost(textLines, img.Rect);
            if (host != null) inlineImgs.Add((img, host));
            else blockImgs.Add(img);
        }

        var textBands = textLines.Where(l => l.CharCount > 0)
            .Select(l => (cy: l.TopY + l.FontSize / 2, lo: l.Left, hi: l.Right)).ToList();
        var rows = GroupBlockRows(blockImgs, textBands);

        var order = new List<(ImgPlace ip, double repY, double llx)>();
        foreach (var (img, _) in inlineImgs)
            order.Add((img, img.Rect.URY, img.Rect.LLX));
        foreach (var row in rows)
        {
            var repY = row.Max(m => m.Rect.URY);
            foreach (var m in row) order.Add((m, repY, m.Rect.LLX));
        }
        foreach (var it in order.OrderByDescending(x => x.repY).ThenBy(x => x.llx))
            it.ip.Token = images.Token(it.ip);

        foreach (var (img, host) in inlineImgs)
        {
            // A vector token opens its host line: its rect is re-seated left of the
            // line's first glyph so the left-to-right assembly places it first.
            var rect = img.IsVector
                ? new Rectangle(host.Left - img.Rect.Width - 1, img.Rect.LLY,
                    host.Left - 1, img.Rect.URY)
                : img.Rect;
            host.AddImage(rect, img.Token, links);
        }

        var imageLines = new List<Line>();
        foreach (var row in rows)
        {
            var l = new Line();
            foreach (var m in row.OrderBy(x => x.Rect.LLX))
                l.Elems.Add(Elem.ForImage(m.Rect, m.Token));
            l.Finish(links);
            imageLines.Add(l);
        }

        var local = new List<MdBlock>(tableBlocks);

        var textOnly = textLines.Where(l => l.CharCount > 0).ToList();
        var bodySize = DominantSize(textOnly);
        var headingSizes = textOnly
            .Select(l => l.FontSize)
            .Where(s => s > bodySize + 0.5)
            .Distinct()
            .OrderByDescending(s => s)
            .ToList();

        // A page with side-by-side text columns (a vertical gutter separating sections that
        // share the same vertical span, outside any table) cannot be read by a single
        // top-to-bottom sweep — the columns would interleave. Such pages read column-by-column
        // via ParagraphAbsorber's section→paragraph order; all other pages keep the plain
        // baseline-Y grouping below (unchanged).
        if (IsMultiColumn(page, tableRegions))
        {
            // Read paragraph-by-paragraph in ParagraphAbsorber's section→paragraph order,
            // which follows the columns; each MarkupParagraph is one heading/paragraph block.
            var paras = ExtractParagraphs(page, links, tableRegions).ToList();

            // Heuristic header detection (mirrors HeuristicHeaderDetector): a paragraph is a
            // heading when all its glyphs share one font size ≥ the document's most common
            // size and are either bold/italic or ≥ 1.8× that size. Heading levels are the
            // distinct header sizes, largest = level 1. Used when the document has no outline
            // (the only multi-column corpus doc); outline documents keep the outline levels.
            var commonSize = MostCommonFontSize(page);
            var headerSizes = new List<double>();
            foreach (var p in paras)
                if (outlineDests == null && IsHeuristicHeader(p, commonSize, out var hs))
                    AddDistinct(headerSizes, hs);
            headerSizes.Sort((x, y) => y.CompareTo(x)); // descending → index 0 = level 1

            var textBlocks = new List<MdBlock>();
            foreach (var paraLines in paras)
            {
                var head = paraLines[0];
                var top = paraLines.Max(l => l.TopY);
                int level;
                if (outlineDests != null)
                    level = HeadingLevel(head, pageNumber, bodySize, headingSizes, outlineDests);
                else
                    level = IsHeuristicHeader(paraLines, commonSize, out var hsz)
                        ? HeaderLevelOf(headerSizes, hsz) : 0;
                if (level > 0)
                {
                    textBlocks.Add(new MdBlock(RenderHeading(head, level, styled: outlineDests == null),
                        false, top));
                    continue;
                }

                // Re-split the row block into paragraphs (bullets, first list item, style
                // changes) so a job title, its company line and its item list become
                // separate paragraphs while wrapped lines and dash items stay together.
                foreach (var sub in SplitIntoParagraphs(paraLines))
                    textBlocks.Add(new MdBlock(RenderParagraph(sub, columnar: true),
                        false, sub.Max(l => l.TopY)));
            }

            // Insert tables/block-images into the column-ordered text sequence just after
            // every text block at least as high, so they land at the right height without
            // re-sorting the columns.
            var nonText = new List<MdBlock>(tableBlocks);
            foreach (var l in imageLines)
            {
                var center = (l.Elems.Min(e => e.LLY) + l.Elems.Max(e => e.URY)) / 2;
                nonText.Add(new MdBlock(l.Text, false, center));
            }
            var merged = new List<MdBlock>(textBlocks);
            foreach (var nb in nonText.OrderBy(b => b.TopY))
            {
                var idx = 0;
                while (idx < merged.Count && merged[idx].TopY >= nb.TopY) idx++;
                merged.Insert(idx, nb);
            }
            foreach (var b in merged)
                blocks.Add(b);
            return;
        }

        // Build text blocks from text lines only (headings/paragraphs). A block image never
        // splits a paragraph, so image-row blocks are added separately and merged by top
        // edge; the block sort places a row before a paragraph when its top is higher.
        var tls = textOnly.OrderByDescending(l => l.TopY).ToList();
        local.AddRange(BuildTextBlocks(tls, pageNumber, bodySize, headingSizes, outlineDests));

        // Image rows are anchored by their vertical centre, so a tall row that overlaps a
        // heading still sorts after it (heading baseline above the row centre).
        foreach (var l in imageLines)
        {
            var center = (l.Elems.Min(e => e.LLY) + l.Elems.Max(e => e.URY)) / 2;
            local.Add(new MdBlock(l.Text, false, center));
        }

        foreach (var b in local.OrderByDescending(b => b.TopY))
            blocks.Add(b);
    }

    private static (double lo, double hi) VerticalSpan(MarkupParagraph para)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var f in FragmentsOf(para))
        {
            if (f?.Rectangle == null) continue;
            lo = Math.Min(lo, f.Rectangle.LLY);
            hi = Math.Max(hi, f.Rectangle.URY);
        }
        return (lo, hi);
    }

    // ── Images ────────────────────────────────────────────────────────────────────

    /// <summary>Every painted (stroked or filled) path of the page content, as its page-space
    /// bounding box plus a serialized SVG element (coordinates page-space; the cluster's
    /// bbox origin is subtracted and Y flipped when the final markup is assembled — for the
    /// bbox-relative viewBox the offset is folded into a transform attribute).</summary>
    private static List<(Rectangle box, string elem)> CollectPaintedPaths(Page page)
    {
        var paints = new List<(Rectangle box, string elem)>();
        var ctm = new double[] { 1, 0, 0, 1, 0, 0 };
        var stack = new Stack<double[]>();
        var pts = new List<(double x, double y)>();
        var d = new StringBuilder();
        var fill = "#000";

        (double x, double y) Apply(double x, double y)
            => (ctm[0] * x + ctm[2] * y + ctm[4], ctm[1] * x + ctm[3] * y + ctm[5]);

        foreach (var raw in page.Contents.PeekOps())
        {
            var s = raw.Trim();
            var sp = s.LastIndexOf(' ');
            var name = sp < 0 ? s : s.Substring(sp + 1);
            switch (name)
            {
                case "q": stack.Push((double[])ctm.Clone()); break;
                case "Q": if (stack.Count > 0) ctm = stack.Pop(); break;
                case "cm":
                {
                    var p = Operands(s, 6);
                    if (p == null) break;
                    ctm = new[]
                    {
                        p[0] * ctm[0] + p[1] * ctm[2], p[0] * ctm[1] + p[1] * ctm[3],
                        p[2] * ctm[0] + p[3] * ctm[2], p[2] * ctm[1] + p[3] * ctm[3],
                        p[4] * ctm[0] + p[5] * ctm[2] + ctm[4], p[4] * ctm[1] + p[5] * ctm[3] + ctm[5],
                    };
                    break;
                }
                case "rg":
                {
                    var p = Operands(s, 3);
                    if (p != null)
                        fill = $"#{(int)(p[0] * 255):x2}{(int)(p[1] * 255):x2}{(int)(p[2] * 255):x2}";
                    break;
                }
                case "g":
                {
                    var p = Operands(s, 1);
                    if (p != null)
                        fill = $"#{(int)(p[0] * 255):x2}{(int)(p[0] * 255):x2}{(int)(p[0] * 255):x2}";
                    break;
                }
                case "m":
                {
                    var p = Operands(s, 2);
                    if (p == null) break;
                    var (x, y) = Apply(p[0], p[1]);
                    pts.Add((x, y));
                    d.Append(d.Length > 0 ? " M " : "M ").Append(F(x)).Append(' ').Append(F(y));
                    break;
                }
                case "l":
                {
                    var p = Operands(s, 2);
                    if (p == null) break;
                    var (x, y) = Apply(p[0], p[1]);
                    pts.Add((x, y));
                    d.Append(" L ").Append(F(x)).Append(' ').Append(F(y));
                    break;
                }
                case "c":
                {
                    var p = Operands(s, 6);
                    if (p == null) break;
                    var (x1, y1) = Apply(p[0], p[1]);
                    var (x2, y2) = Apply(p[2], p[3]);
                    var (x3, y3) = Apply(p[4], p[5]);
                    pts.Add((x1, y1)); pts.Add((x2, y2)); pts.Add((x3, y3));
                    d.Append(" C ").Append(F(x1)).Append(' ').Append(F(y1)).Append(' ')
                        .Append(F(x2)).Append(' ').Append(F(y2)).Append(' ')
                        .Append(F(x3)).Append(' ').Append(F(y3));
                    break;
                }
                case "h": d.Append(" Z"); break;
                case "re":
                {
                    var p = Operands(s, 4);
                    if (p == null) break;
                    var (x0, y0) = Apply(p[0], p[1]);
                    var (x1, y1) = Apply(p[0] + p[2], p[1] + p[3]);
                    pts.Add((x0, y0)); pts.Add((x1, y1));
                    d.Append(d.Length > 0 ? " M " : "M ").Append(F(x0)).Append(' ').Append(F(y0))
                        .Append(" L ").Append(F(x1)).Append(' ').Append(F(y0))
                        .Append(" L ").Append(F(x1)).Append(' ').Append(F(y1))
                        .Append(" L ").Append(F(x0)).Append(' ').Append(F(y1)).Append(" Z");
                    break;
                }
                case "S": case "s": case "B": case "b": case "B*": case "b*":
                case "f": case "F": case "f*":
                {
                    if (pts.Count > 0)
                    {
                        var box = new Rectangle(pts.Min(p => p.x), pts.Min(p => p.y),
                            pts.Max(p => p.x), pts.Max(p => p.y));
                        var stroked = name is "S" or "s" or "B" or "b" or "B*" or "b*";
                        var filled = name is "f" or "F" or "f*" or "B" or "b" or "B*" or "b*";
                        var elem = $"<path d=\"{d}\""
                            + $" fill=\"{(filled ? fill : "none")}\""
                            + (stroked ? $" stroke=\"{fill}\"" : "")
                            + $" transform=\"translate({F(-box.LLX)} {F(box.URY)}) scale(1 -1)\"/>";
                        paints.Add((box, elem));
                    }
                    pts.Clear();
                    d.Clear();
                    break;
                }
                case "n":
                    pts.Clear();
                    d.Clear();
                    break;
            }
        }
        return paints;
    }


    // ── Tables ──────────────────────────────────────────────────────────────────

    // ── Ruled-grid detection ─────────────────────────────────────────────────────
    //
    // A markdown table comes from the page's DRAWN grid: the stroked horizontal and
    // vertical rules of the table frame. Underlines, strike-outs and highlight fills
    // are strokes too, so a rule only counts when it spans its cluster (see below),
    // and a cluster is a grid only with at least three rule positions on each axis
    // (two columns and two rows).

    // ── Grid rendering ───────────────────────────────────────────────────────────

    // ── Line grouping ─────────────────────────────────────────────────────────────

    // ── Rendering ─────────────────────────────────────────────────────────────────


    // ── Model ─────────────────────────────────────────────────────────────────────

}
