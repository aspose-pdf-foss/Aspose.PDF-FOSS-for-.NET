#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
    /// <summary>Place the page's images: inline ones onto their host line, block ones into rows, tokens numbered in reading order.</summary>
    private static void PlaceMarkdownImages(MarkdownPageState mp)
    {
        mp.inlineImgs = new List<(ImgPlace img, Line host)>();
        mp.blockImgs = new List<ImgPlace>();
        mp.placements = CollectPlacements(mp.page, mp.tableRegions);
        if (mp.options.ExtractVectorGraphics)
            mp.placements.AddRange(CollectVectorGraphics(mp.page, mp.tableRegions));
        foreach (var img in mp.placements)
        {
            // A vector drawing attaches inline only when its CENTER falls inside a text
            // line's band, and its token then opens the line (a decoration, not a glyph
            // in the running text); anything else stands alone as a block drawing.
            var host = img.IsVector ? VectorInlineHost(mp.textLines, img.Rect) : InlineHost(mp.textLines, img.Rect);
            if (host != null) mp.inlineImgs.Add((img, host));
            else mp.blockImgs.Add(img);
        }

        var textBands = mp.textLines.Where(l => l.CharCount > 0)
            .Select(l => (cy: l.TopY + l.FontSize / 2, lo: l.Left, hi: l.Right)).ToList();
        mp.rows = GroupBlockRows(mp.blockImgs, textBands);

        mp.order = new List<(ImgPlace ip, double repY, double llx)>();
        foreach (var (img, _) in mp.inlineImgs)
            mp.order.Add((img, img.Rect.URY, img.Rect.LLX));
        foreach (var row in mp.rows)
        {
            var repY = row.Max(m => m.Rect.URY);
            foreach (var m in row) mp.order.Add((m, repY, m.Rect.LLX));
        }
        foreach (var it in mp.order.OrderByDescending(x => x.repY).ThenBy(x => x.llx))
            it.ip.Token = mp.images.Token(it.ip);

        foreach (var (img, host) in mp.inlineImgs)
        {
            // A vector token opens its host line: its rect is re-seated left of the
            // line's first glyph so the left-to-right assembly places it first.
            var rect = img.IsVector
                ? new Rectangle(host.Left - img.Rect.Width - 1, img.Rect.LLY,
                    host.Left - 1, img.Rect.URY)
                : img.Rect;
            host.AddImage(rect, img.Token, mp.links);
        }

        mp.imageLines = new List<Line>();
        foreach (var row in mp.rows)
        {
            var l = new Line();
            foreach (var m in row.OrderBy(x => x.Rect.LLX))
                l.Elems.Add(Elem.ForImage(m.Rect, m.Token));
            l.Finish(mp.links);
            mp.imageLines.Add(l);
        }
    }

    /// <summary>Absorb the page's text, clip it to the extraction area, lift the tables out and group the rest into lines.</summary>
    private static void CollectMarkdownPageText(MarkdownPageState mp)
    {
        mp.absorber = new TextFragmentAbsorber();
        mp.absorber.Visit(mp.page);

        mp.fragments = mp.absorber.TextFragments
            .Cast<TextFragment>()
            .Where(f => !string.IsNullOrEmpty(f.Text) && f.Rectangle != null)
            .ToList();

        if (mp.options.AreaToExtract != null)
        {
            var area = mp.options.AreaToExtract;
            mp.fragments = mp.fragments.Where(f => !f.Rectangle.Intersect(area).IsEmpty).ToList();
        }

        mp.links = CollectLinks(mp.page);

        (mp.tableBlocks, var tableRegions) = CollectTables(mp.page, mp.fragments, mp.links, mp.options.AreaToExtract);
        mp.tableRegions = tableRegions;
        if (mp.tableRegions.Count > 0)
            mp.fragments = mp.fragments.Where(f => !InAnyRegion(f.Rectangle, mp.tableRegions)).ToList();

        mp.textLines = GroupLines(mp.fragments, mp.links);
    }

    /// <summary>A multi-column page: paragraphs extracted column-wise, headings by outline or heuristic, tables and image lines merged in by their top.</summary>
    private static void RenderMultiColumnPage(MarkdownPageState mp)
    {
        // Read paragraph-by-paragraph in ParagraphAbsorber's section→paragraph order,
        // which follows the columns; each MarkupParagraph is one heading/paragraph block.
        var paras = ExtractParagraphs(mp.page, mp.links, mp.tableRegions).ToList();

        // Heuristic header detection (mirrors HeuristicHeaderDetector): a paragraph is a
        // heading when all its glyphs share one font size ≥ the document's most common
        // size and are either bold/italic or ≥ 1.8× that size. Heading levels are the
        // distinct header sizes, largest = level 1. Used when the document has no outline
        // (the only multi-column corpus doc); outline documents keep the outline levels.
        var commonSize = MostCommonFontSize(mp.page);
        var headerSizes = new List<double>();
        foreach (var p in paras)
            if (mp.outlineDests == null && IsHeuristicHeader(p, commonSize) is { } hs)
                AddDistinct(headerSizes, hs);
        headerSizes.Sort((x, y) => y.CompareTo(x)); // descending → index 0 = level 1

        var textBlocks = new List<MdBlock>();
        foreach (var paraLines in paras)
        {
            var head = paraLines[0];
            var top = paraLines.Max(l => l.TopY);
            int level;
            if (mp.outlineDests != null)
                level = HeadingLevel(head, mp.pageNumber, mp.bodySize, mp.headingSizes, mp.outlineDests);
            else
                level = IsHeuristicHeader(paraLines, commonSize) is { } hsz
                    ? HeaderLevelOf(headerSizes, hsz) : 0;
            if (level > 0)
            {
                textBlocks.Add(new MdBlock(RenderHeading(head, level, styled: mp.outlineDests == null),
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
        var nonText = new List<MdBlock>(mp.tableBlocks);
        foreach (var l in mp.imageLines)
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
            mp.blocks.Add(b);
    }
}
