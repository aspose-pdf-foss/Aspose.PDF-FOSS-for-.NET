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
        var mp = new MarkdownPageState();
        mp.page = page;
        mp.pageNumber = pageNumber;
        mp.options = options;
        mp.outlineDests = outlineDests;
        mp.images = images;
        mp.blocks = blocks;
        CollectMarkdownPageText(mp);

        PlaceMarkdownImages(mp);

        mp.local = new List<MdBlock>(mp.tableBlocks);

        mp.textOnly = mp.textLines.Where(l => l.CharCount > 0).ToList();
        mp.bodySize = DominantSize(mp.textOnly);
        mp.headingSizes = mp.textOnly
            .Select(l => l.FontSize)
            .Where(s => s > mp.bodySize + 0.5)
            .Distinct()
            .OrderByDescending(s => s)
            .ToList();

        // A page with side-by-side text columns (a vertical gutter separating sections that
        // share the same vertical span, outside any table) cannot be read by a single
        // top-to-bottom sweep — the columns would interleave. Such pages read column-by-column
        // via ParagraphAbsorber's section→paragraph order; all other pages keep the plain
        // baseline-Y grouping below (unchanged).
        if (IsMultiColumn(mp.page, mp.tableRegions))
        {
            RenderMultiColumnPage(mp);
            return;
        }

        mp.tls = mp.textOnly.OrderByDescending(l => l.TopY).ToList();
        mp.local.AddRange(BuildTextBlocks(mp.tls, mp.pageNumber, mp.bodySize, mp.headingSizes, mp.outlineDests));

        // Image rows are anchored by their vertical centre, so a tall row that overlaps a
        // heading still sorts after it (heading baseline above the row centre).
        foreach (var l in mp.imageLines)
        {
            var center = (l.Elems.Min(e => e.LLY) + l.Elems.Max(e => e.URY)) / 2;
            mp.local.Add(new MdBlock(l.Text, false, center));
        }

        foreach (var b in mp.local.OrderByDescending(b => b.TopY))
            mp.blocks.Add(b);
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
        var pp = new PaintedPathsState();
        pp.page = page;
        pp.paints = new List<(Rectangle box, string elem)>();
        pp.ctm = new double[] { 1, 0, 0, 1, 0, 0 };
        pp.stack = new Stack<double[]>();
        pp.pts = new List<(double x, double y)>();
        pp.d = new StringBuilder();
        pp.fill = "#000";

        foreach (var raw in pp.page.Contents.PeekOps())
        {
            CollectPaintedPath(pp, raw);
        }
        return pp.paints;
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
