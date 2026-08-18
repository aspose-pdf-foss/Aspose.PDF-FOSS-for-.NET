#nullable disable

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown
{
    /// <summary>
    /// Reflows the text of a PDF page into Markdown: heading detection (via the document
    /// outline when present, otherwise relative font size), paragraph reconstruction from
    /// wrapped visual lines, inline emphasis, links, ruled tables, and images (small images
    /// inline within a text line; larger ones as their own image blocks).
    /// </summary>
    internal static class MarkdownRenderer
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

            var images = new ImageNumberer(outputDir, options.ResourcesDirectoryName);

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
                    if (blocks[i].IsTable || blocks[i - 1].IsTable)
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

            var tableBlocks = CollectTables(page, fragments, out var tableRegions);
            if (tableRegions.Count > 0)
                fragments = fragments.Where(f => !InAnyRegion(f.Rectangle, tableRegions)).ToList();

            var links = CollectLinks(page);

            // Group text into lines first — these provide the bands used to decide whether an
            // image sits inline within a line or forms its own image block.
            var textLines = GroupLines(fragments, links);

            // Classify each image inline vs block, group block images into rows, then number
            // every image by first appearance in reading order (rows top-down, within-row LLX
            // ascending; inline images at their host line) and save the unique PNGs.
            var inlineImgs = new List<(ImgPlace img, Line host)>();
            var blockImgs = new List<ImgPlace>();
            foreach (var img in CollectPlacements(page, tableRegions))
            {
                var host = InlineHost(textLines, img.Rect);
                if (host != null) inlineImgs.Add((img, host));
                else blockImgs.Add(img);
            }

            var textBands = textLines.Where(l => l.CharCount > 0)
                .Select(l => (cy: l.TopY + l.FontSize / 2, lo: l.Left, hi: l.Right)).ToList();
            var rows = GroupBlockRows(blockImgs, textBands);

            var order = new List<(ImagePlacement pl, double repY, double llx)>();
            foreach (var (img, _) in inlineImgs)
                order.Add((img.Placement, img.Rect.URY, img.Rect.LLX));
            foreach (var row in rows)
            {
                var repY = row.Max(m => m.Rect.URY);
                foreach (var m in row) order.Add((m.Placement, repY, m.Rect.LLX));
            }
            var tokens = new Dictionary<ImagePlacement, string>();
            foreach (var it in order.OrderByDescending(x => x.repY).ThenBy(x => x.llx))
                tokens[it.pl] = images.Token(it.pl);

            foreach (var (img, host) in inlineImgs)
                host.AddImage(img.Rect, tokens[img.Placement], links);

            var imageLines = new List<Line>();
            foreach (var row in rows)
            {
                var l = new Line();
                foreach (var m in row.OrderBy(x => x.Rect.LLX))
                    l.Elems.Add(Elem.ForImage(m.Rect, tokens[m.Placement]));
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

        /// <summary>Group a top-to-bottom-ordered list of text lines into heading/paragraph
        /// blocks (shared by the single-column and per-column-group paths).</summary>
        private static List<MdBlock> BuildTextBlocks(List<Line> tls, int pageNumber, double bodySize,
            List<double> headingSizes, List<HeadingDest> outlineDests)
        {
            var result = new List<MdBlock>();
            var i = 0;
            while (i < tls.Count)
            {
                var top = tls[i].TopY;
                var level = HeadingLevel(tls[i], pageNumber, bodySize, headingSizes, outlineDests);
                if (level > 0)
                {
                    result.Add(new MdBlock(RenderHeading(tls[i], level, styled: outlineDests == null),
                        false, top));
                    i++;
                }
                else if (tls[i].FontSize > bodySize + 0.5)
                {
                    result.Add(new MdBlock(RenderParagraph(new List<Line> { tls[i] }), false, top));
                    i++;
                }
                else
                {
                    var paragraph = new List<Line> { tls[i] };
                    i++;
                    while (i < tls.Count &&
                           HeadingLevel(tls[i], pageNumber, bodySize, headingSizes, outlineDests) == 0 &&
                           tls[i].FontSize <= bodySize + 0.5 &&
                           paragraph[paragraph.Count - 1].TopY - tls[i].TopY <= tls[i].FontSize * 1.6)
                    {
                        paragraph.Add(tls[i]);
                        i++;
                    }
                    result.Add(new MdBlock(RenderParagraph(paragraph), false, top));
                }
            }
            return result;
        }

        /// <summary>True when the page has side-by-side text columns: two ParagraphAbsorber
        /// sections that overlap vertically but are horizontally disjoint, neither of which lies
        /// inside a detected table region. Such pages must be read column-by-column.</summary>
        private static bool IsMultiColumn(Page page, List<Rectangle> tableRegions)
        {
            List<MarkupSection> sections;
            try
            {
                var abs = new ParagraphAbsorber();
                abs.Visit(page);
                sections = abs.PageMarkups.SelectMany(m => m.Sections)
                    .Where(s => s.Rectangle != null).ToList();
            }
            catch
            {
                return false;
            }

            for (var a = 0; a < sections.Count; a++)
            for (var b = a + 1; b < sections.Count; b++)
            {
                var ra = sections[a].Rectangle;
                var rb = sections[b].Rectangle;
                var yOverlap = Math.Min(ra.URY, rb.URY) - Math.Max(ra.LLY, rb.LLY);
                var minHeight = Math.Min(ra.URY - ra.LLY, rb.URY - rb.LLY);
                var xDisjoint = ra.URX < rb.LLX - 2 || rb.URX < ra.LLX - 2;
                if (yOverlap > 0.3 * minHeight && xDisjoint
                    && !InAnyRegion(ra, tableRegions) && !InAnyRegion(rb, tableRegions))
                    return true;
            }
            return false;
        }

        /// <summary>Yield each page paragraph as an ordered list of <see cref="Line"/>s, using
        /// <see cref="ParagraphAbsorber"/> for the reading order (sections → paragraphs → lines),
        /// so multi-column regions read column-by-column. Paragraphs inside a detected table
        /// region are skipped (the table path renders them).</summary>
        private static IEnumerable<List<Line>> ExtractParagraphs(
            Page page, List<LinkInfo> links, List<Rectangle> tableRegions)
        {
            ParagraphAbsorber absorber;
            try
            {
                absorber = new ParagraphAbsorber();
                absorber.Visit(page);
            }
            catch
            {
                yield break;
            }

            var sections = absorber.PageMarkups.SelectMany(m => m.Sections)
                .Where(s => s.Rectangle != null).ToList();

            var groups = SectionColumnGroups(sections);
            var groupRects = groups.Select(GroupRect).ToList();

            for (var gi = 0; gi < groups.Count; gi++)
            {
                var sectionGroup = groups[gi];

                // A group is "columnar" when another group sits beside it (vertical overlap,
                // horizontally disjoint) — a genuine side-by-side column such as the skills grid.
                // There ParagraphAbsorber's own paragraph boundaries are meaningful (e.g. a label
                // above a list), so keep them (merging only its diagonally-split sub-columns).
                // An isolated full-width group (a résumé entry: title row, company line, item
                // list) is instead flattened to its rows and re-split from scratch, because
                // ParagraphAbsorber breaks such a block into several stacked paragraphs.
                var columnar = false;
                for (var gj = 0; gj < groups.Count; gj++)
                {
                    if (gj == gi) continue;
                    var a = groupRects[gi];
                    var b = groupRects[gj];
                    var yOverlap = Math.Min(a.URY, b.URY) - Math.Max(a.LLY, b.LLY);
                    if (yOverlap > 0 && (a.URX < b.LLX - 2 || b.URX < a.LLX - 2)) { columnar = true; break; }
                }

                var paras = sectionGroup.SelectMany(s => s.Paragraphs)
                    .OrderByDescending(p => VerticalSpan(p).hi).ToList();

                // Build the paragraph fragment-clusters. Full-width groups collapse to one
                // cluster (all fragments); columnar groups merge only vertically-overlapping
                // ParagraphAbsorber paragraphs (rejoining diagonally-split grid rows).
                var clusters = new List<List<TextFragment>>();
                if (!columnar)
                {
                    clusters.Add(sectionGroup.SelectMany(FragmentsOfSection).ToList());
                }
                else
                {
                    var g = 0;
                    while (g < paras.Count)
                    {
                        var frags = new List<TextFragment>(FragmentsOf(paras[g]));
                        var span = VerticalSpan(paras[g]);
                        var h = g + 1;
                        while (h < paras.Count)
                        {
                            var nextSpan = VerticalSpan(paras[h]);
                            if (nextSpan.hi < span.lo - 0.5) break; // clear gap → new paragraph
                            frags.AddRange(FragmentsOf(paras[h]));
                            span = (Math.Min(span.lo, nextSpan.lo), Math.Max(span.hi, nextSpan.hi));
                            h++;
                        }
                        g = h;
                        clusters.Add(frags);
                    }
                }

                foreach (var frags in clusters)
                {
                    var lines = GroupLines(
                            frags.Where(f => f?.Rectangle != null && !string.IsNullOrEmpty(f.Text)).ToList(),
                            links, columnar: true)
                        .Where(l => l.CharCount > 0)
                        .OrderByDescending(l => l.TopY)
                        .ToList();
                    if (lines.Count == 0) continue;

                    if (tableRegions.Count > 0)
                    {
                        var lo = lines.Min(l => l.Elems.Min(e => e.LLY));
                        var hi = lines.Max(l => l.Elems.Max(e => e.URY));
                        var left = lines.Min(l => l.Left);
                        var right = lines.Max(l => l.Right);
                        if (InAnyRegion(new Rectangle(left, lo, right, hi), tableRegions)) continue;
                    }

                    yield return lines;
                }
            }
        }

        private static IEnumerable<TextFragment> FragmentsOfSection(MarkupSection s)
        {
            foreach (var p in s.Paragraphs)
                foreach (var f in FragmentsOf(p))
                    yield return f;
        }

        private static Rectangle GroupRect(List<MarkupSection> group)
        {
            double llx = double.MaxValue, lly = double.MaxValue, urx = double.MinValue, ury = double.MinValue;
            foreach (var s in group)
            {
                var r = s.Rectangle;
                if (r == null) continue;
                llx = Math.Min(llx, r.LLX); lly = Math.Min(lly, r.LLY);
                urx = Math.Max(urx, r.URX); ury = Math.Max(ury, r.URY);
            }
            return new Rectangle(llx, lly, urx, ury);
        }

        /// <summary>Partition sections into visual columns, in reading order: sections that
        /// overlap vertically and sit within a small horizontal gap are one column (ParagraphAbsorber
        /// splits a column with narrow inner gutters into several sections); a wide gutter keeps
        /// true side-by-side columns separate.</summary>
        private static List<List<MarkupSection>> SectionColumnGroups(List<MarkupSection> secs)
        {
            var n = secs.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (var a = 0; a < n; a++)
            for (var b = a + 1; b < n; b++)
            {
                var ra = secs[a].Rectangle;
                var rb = secs[b].Rectangle;
                var yOverlap = Math.Min(ra.URY, rb.URY) - Math.Max(ra.LLY, rb.LLY);
                var minHeight = Math.Min(ra.URY - ra.LLY, rb.URY - rb.LLY);
                var gap = ra.URX < rb.LLX ? rb.LLX - ra.URX
                    : rb.URX < ra.LLX ? ra.LLX - rb.URX : -1;
                if (yOverlap > 0.3 * minHeight && gap >= 0 && gap < 25)
                    parent[Find(a)] = Find(b);
            }

            var byRoot = new Dictionary<int, (int firstIdx, List<MarkupSection> members)>();
            for (var k = 0; k < n; k++)
            {
                var r = Find(k);
                if (!byRoot.TryGetValue(r, out var grp))
                    grp = (k, new List<MarkupSection>());
                grp.members.Add(secs[k]);
                byRoot[r] = (Math.Min(grp.firstIdx, k), grp.members);
            }
            return byRoot.Values.OrderBy(grp => grp.firstIdx).Select(grp => grp.members).ToList();
        }

        /// <summary>Split a run of physical rows into paragraphs by applying these
        /// paragraph rules over the ink rows: a new paragraph starts at each bullet
        /// (•) line, at the first dash '-' list item after a non-item line, and where the font
        /// style changes (e.g. a bold job title above the plain company line). Wrapped
        /// continuation lines (same style, not a new marker) stay with their paragraph.</summary>
        private static List<List<Line>> SplitIntoParagraphs(List<Line> lines)
        {
            var result = new List<List<Line>>();
            List<Line> cur = null;
            for (var i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                var brk = cur == null;
                if (!brk)
                {
                    var prev = lines[i - 1];
                    if (StartsWithBullet(l.Text))
                        brk = true;                                   // each bullet is its own paragraph
                    else if (StartsWithDash(l.Text) && !cur.Any(x => StartsWithDash(x.Text))
                             && !EndsWithColonOrSemicolon(prev.Text))
                        brk = true;                                   // first dash item after a non-item block
                                                                      // (a label line ending ':' keeps its list)
                    else if (IsBoldLine(l) != IsBoldLine(prev))
                        brk = true;                                   // bold change (bold title → plain line)
                    else if (Math.Abs(l.Left - prev.Left) > Math.Max(l.FontSize * 0.5, 3.0))
                        brk = true;                                   // left-edge shift (indent/outdent)
                }
                if (brk) { cur = new List<Line>(); result.Add(cur); }
                cur.Add(l);
            }
            return result;
        }

        private static bool IsBoldLine(Line l) => l.BoldChars * 2 > l.CharCount;

        private static bool StartsWithBullet(string w)
        {
            foreach (var c in w)
            {
                if (char.IsWhiteSpace(c)) continue;
                return c is '•' or '·';
            }
            return false;
        }

        private static bool StartsWithDash(string w)
        {
            foreach (var c in w)
            {
                if (char.IsWhiteSpace(c)) continue;
                return c is '-' or '–' or '—';
            }
            return false;
        }

        private const double HeaderFontSizeRatio = 1.8;
        private const double FontSizeEqualityThreshold = 0.01;

        /// <summary>The document's most common font size (by total glyph count), mirroring
        /// HeuristicHeaderDetector's MostCommonFontSize.</summary>
        private static double MostCommonFontSize(Page page)
        {
            var weight = new Dictionary<double, long>();
            try
            {
                var abs = new TextFragmentAbsorber();
                abs.Visit(page);
                foreach (TextFragment f in abs.TextFragments)
                {
                    if (string.IsNullOrEmpty(f.Text)) continue;
                    var s = Math.Round(f.TextState.FontSize, 2);
                    weight.TryGetValue(s, out var w);
                    weight[s] = w + f.Text.Length;
                }
            }
            catch { return 0; }
            var best = 0.0; long bestW = -1;
            foreach (var kv in weight)
                if (kv.Value > bestW) { bestW = kv.Value; best = kv.Key; }
            return best;
        }

        /// <summary>True when every glyph of the paragraph shares one font size and style, the
        /// size is at least the most common size, and the run is either bold/italic or at least
        /// 1.8× the common size (HeuristicHeaderDetector.CheckTextFeatures).</summary>
        private static bool IsHeuristicHeader(List<Line> paragraph, double commonSize, out double fontSize)
        {
            fontSize = 0;
            double size = -1;
            var style = FontStyles.Regular;
            var have = false;
            foreach (var line in paragraph)
            foreach (var e in line.Elems)
            {
                if (e.IsImage) continue;
                var f = e.Frag;
                if (f == null || string.IsNullOrWhiteSpace(f.Text)) continue;
                var fStyle = f.TextState.FontStyle;
                var fSize = f.TextState.FontSize;
                if (!have) { size = fSize; style = fStyle; have = true; continue; }
                if (fStyle != style) return false;
                if (Math.Abs(size - fSize) > FontSizeEqualityThreshold) return false;
            }
            if (!have || size < 0) return false;
            if (size < commonSize) return false;
            if (style == FontStyles.Regular && size < HeaderFontSizeRatio * commonSize) return false;
            fontSize = size;
            return true;
        }

        private static void AddDistinct(List<double> sizes, double s)
        {
            foreach (var x in sizes)
                if (Math.Abs(x - s) <= FontSizeEqualityThreshold) return;
            sizes.Add(s);
        }

        private static int HeaderLevelOf(List<double> descendingSizes, double size)
        {
            for (var i = 0; i < descendingSizes.Count; i++)
                if (Math.Abs(descendingSizes[i] - size) <= FontSizeEqualityThreshold)
                    return i + 1;
            return descendingSizes.Count > 0 ? descendingSizes.Count : 1;
        }

        private static IEnumerable<TextFragment> FragmentsOf(MarkupParagraph para)
        {
            foreach (var line in para.Lines)
                foreach (var f in line)
                    yield return f;
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

        private static List<ImgPlace> CollectPlacements(Page page, List<Rectangle> tableRegions)
        {
            var result = new List<ImgPlace>();
            try
            {
                var absorber = new ImagePlacementAbsorber();
                absorber.Visit(page);
                foreach (ImagePlacement p in absorber.ImagePlacements)
                    if (p.Rectangle != null && p.Image != null && !InAnyRegion(p.Rectangle, tableRegions))
                        result.Add(new ImgPlace(p));
            }
            catch
            {
                // No placements on parse failure.
            }
            return result;
        }

        /// <summary>Decide whether an image sits inline within a text line. It does when the
        /// nearest text line at its vertical band is either much taller than the image (a small
        /// logo dropped into running text) or sits entirely beside the image (a figure with a
        /// caption/heading to one side). A tall image that a full-width text line runs across —
        /// or one with no text at its band — stands alone as its own block.</summary>
        private static Line InlineHost(List<Line> textLines, Rectangle rect)
        {
            // Text lines whose vertical band [baseline, baseline+em] overlaps the image.
            var overlapped = new List<Line>();
            foreach (var l in textLines)
            {
                if (l.CharCount == 0) continue;
                var lineTop = l.TopY + l.FontSize;
                if (Math.Min(rect.URY, lineTop) - Math.Max(rect.LLY, l.TopY) > 0)
                    overlapped.Add(l);
            }
            if (overlapped.Count == 0)
                return null; // sits in whitespace → its own block

            var cy = (rect.LLY + rect.URY) / 2;
            var closest = overlapped.OrderBy(l => Math.Abs(l.TopY - cy)).First();

            // Inline when the image is a small logo dropped into running text, or an isolated
            // figure that no text line runs horizontally across (only a caption/heading sits
            // beside it). A larger image the text column runs across stands alone as a block.
            var acrossByText = overlapped.Any(l => l.Left < rect.URX - 2 && l.Right > rect.LLX + 2);
            if (rect.Height <= closest.FontSize * 1.5 || !acrossByText)
                return closest;
            return null;
        }

        // Greedy row grouping for block images: process centre-Y
        // descending; an image joins the current row iff its Y-interval overlaps a row member
        // AND its X-interval collides with none. Otherwise it starts a new row.
        private static List<List<ImgPlace>> GroupBlockRows(List<ImgPlace> blockImages,
            List<(double cy, double lo, double hi)> textLines)
        {
            var rows = new List<List<ImgPlace>>();
            var ordered = blockImages.OrderByDescending(e => (e.Rect.LLY + e.Rect.URY) / 2).ToList();
            var rowMinCy = double.MaxValue;
            foreach (var e in ordered)
            {
                var cy = (e.Rect.LLY + e.Rect.URY) / 2;
                if (rows.Count > 0)
                {
                    var cur = rows[rows.Count - 1];
                    var yOverlap = cur.Any(m => Math.Min(m.Rect.URY, e.Rect.URY) - Math.Max(m.Rect.LLY, e.Rect.LLY) > 0);
                    var xConflict = cur.Any(m => Math.Min(m.Rect.URX, e.Rect.URX) - Math.Max(m.Rect.LLX, e.Rect.LLX) > 0);
                    // A SEPARATE paragraph between this image and the current row breaks the flow.
                    // Only text that sits beside the images (horizontally disjoint from every row
                    // member and from this image) counts — text the images are embedded in (a
                    // column the images sit within) does not separate them.
                    var minX = Math.Min(e.Rect.LLX, cur.Min(m => m.Rect.LLX));
                    var maxX = Math.Max(e.Rect.URX, cur.Max(m => m.Rect.URX));
                    var separated = textLines.Any(t => t.cy > cy && t.cy < rowMinCy &&
                        (t.hi <= minX + 2 || t.lo >= maxX - 2));
                    if (yOverlap && !xConflict && !separated)
                    {
                        cur.Add(e);
                        rowMinCy = Math.Min(rowMinCy, cy);
                        continue;
                    }
                }
                rows.Add(new List<ImgPlace> { e });
                rowMinCy = cy;
            }
            return rows;
        }

        private readonly struct ImgPlace
        {
            public readonly Rectangle Rect;
            public readonly ImagePlacement Placement;
            public ImgPlace(ImagePlacement p) { Rect = p.Rectangle; Placement = p; }
        }


        private sealed class ImageNumberer
        {
            private readonly string _outputDir;
            private readonly string _resourceDir;
            private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
            private int _next = 1;

            public ImageNumberer(string outputDir, string resourceDir)
            {
                _outputDir = outputDir;
                _resourceDir = string.IsNullOrEmpty(resourceDir) ? "resources" : resourceDir;
            }

            public string Token(ImagePlacement placement)
            {
                var img = placement.Image;
                var key = $"{img?.Name}|{Math.Round(placement.Rectangle.Width)}x{Math.Round(placement.Rectangle.Height)}";
                if (!_numbers.TryGetValue(key, out var num))
                {
                    num = _next++;
                    _numbers[key] = num;
                    SaveImage(placement, num);
                }
                return $"![]({_resourceDir}/image_{num}.png)";
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416",
                Justification = "ImageFormat.Png.Guid is read-only and works on all platforms.")]
            private void SaveImage(ImagePlacement placement, int num)
            {
                if (string.IsNullOrEmpty(_outputDir))
                    return;
                try
                {
                    var dir = Path.Combine(_outputDir, _resourceDir);
                    Directory.CreateDirectory(dir);
                    using var fs = File.Create(Path.Combine(dir, $"image_{num}.png"));
                    placement.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
                }
                catch
                {
                    // Best-effort: a save failure still leaves the reference in the markdown.
                }
            }
        }

        // ── Tables ──────────────────────────────────────────────────────────────────

        private static bool InAnyRegion(Rectangle r, List<Rectangle> regions)
        {
            var cx = (r.LLX + r.URX) / 2;
            var cy = (r.LLY + r.URY) / 2;
            foreach (var reg in regions)
                if (cx >= reg.LLX && cx <= reg.URX && cy >= reg.LLY && cy <= reg.URY)
                    return true;
            return false;
        }

        private static List<MdBlock> CollectTables(Page page, List<TextFragment> pageFrags,
            out List<Rectangle> regions)
        {
            regions = new List<Rectangle>();
            var result = new List<MdBlock>();

            TableAbsorber absorber;
            try
            {
                absorber = new TableAbsorber();
                absorber.Visit(page);
            }
            catch
            {
                return result;
            }

            foreach (var table in absorber.TableList)
            {
                if (table.Rect == null || table.RowList.Count == 0)
                    continue;
                regions.Add(table.Rect);
                result.Add(new MdBlock(RenderTable(table, pageFrags), true, table.Rect.URY));
            }
            return result;
        }

        private static string RenderTable(AbsorbedTable table, List<TextFragment> pageFrags)
        {
            var rows = table.RowList;
            // Render each cell once; trailing empty cells are dropped per row (a row emits
            // only up to its last non-empty cell — a short final row shows fewer columns).
            var cellText = rows
                .Select(r => r.CellList.Select(c => EscapeText(CellText(c, pageFrags))).ToList())
                .ToList();

            int RowCols(int r)
            {
                var cells = cellText[r];
                var last = -1;
                for (var c = 0; c < cells.Count; c++)
                    if (cells[c].Length > 0) last = c;
                return last + 1;
            }

            var headerCols = RowCols(0);
            var sb = new StringBuilder();

            void Row(int r)
            {
                sb.Append('|');
                var cols = RowCols(r);
                for (var c = 0; c < cols; c++)
                    sb.Append(' ').Append(cellText[r][c]).Append(" |");
                sb.Append(NewLine);
            }

            // The dash-separator run matches each header cell's rendered width.
            Row(0);
            sb.Append('|');
            for (var c = 0; c < headerCols; c++)
                sb.Append(' ').Append(new string('-', Math.Max(cellText[0][c].Length, 3))).Append(" |");
            sb.Append(NewLine);
            for (var r = 1; r < rows.Count; r++)
                Row(r);

            var s = sb.ToString();
            return s.EndsWith(NewLine) ? s.Substring(0, s.Length - NewLine.Length) : s;
        }

        // Reconstruct a cell's text with inter-word spaces. Cell fragments from the table
        // absorber are collapsed per cell (a single space-less run), so the spaces are recovered
        // from the page's per-glyph fragments that fall inside the cell rectangle — the same
        // horizontal-gap synthesis used for body text.
        private static string CellText(AbsorbedCell cell, List<TextFragment> pageFrags)
        {
            List<TextFragment> frags = null;
            var rect = cell.Rect;
            if (rect != null && pageFrags != null)
            {
                frags = pageFrags.Where(f =>
                {
                    var cx = (f.Rectangle.LLX + f.Rectangle.URX) / 2;
                    var cy = (f.Rectangle.LLY + f.Rectangle.URY) / 2;
                    return cx >= rect.LLX - 0.5 && cx <= rect.URX + 0.5
                        && cy >= rect.LLY - 0.5 && cy <= rect.URY + 0.5;
                }).OrderBy(f => f.Rectangle.LLX).ToList();
            }
            if (frags == null || frags.Count == 0)
                frags = cell.TextFragments.Cast<TextFragment>()
                    .Where(f => f.Rectangle != null && !string.IsNullOrEmpty(f.Text))
                    .OrderBy(f => f.Rectangle.LLX).ToList();

            var sb = new StringBuilder();
            TextFragment prev = null;
            foreach (var f in frags)
            {
                if (prev != null)
                {
                    var gap = f.Rectangle.LLX - prev.Rectangle.URX;
                    var em = Math.Max(Math.Max(prev.FontSize, f.FontSize), 12.0);
                    var lastCh = sb.Length > 0 ? sb[sb.Length - 1] : '\0';
                    if (gap > em * SpaceGapRatio && lastCh != ' ' && f.Text[0] != ' ')
                        sb.Append(' ');
                }
                sb.Append(f.Text);
                prev = f;
            }
            return sb.ToString().Trim();
        }

        // ── Line grouping ─────────────────────────────────────────────────────────────

        private static List<Line> GroupLines(List<TextFragment> fragments, List<LinkInfo> links,
            bool columnar = false)
        {
            // Superscript/subscript glyphs sit on shifted baselines; grouping by their own
            // baseline would split the line. Cluster the main-baseline text first, then attach
            // each script glyph to the nearest main line (so it lands inline at its X).
            bool IsScript(TextFragment f) => f.TextState.Superscript || f.TextState.Subscript;

            var main = fragments.Where(f => !IsScript(f))
                .OrderByDescending(f => f.Rectangle.LLY).ToList();
            var scripts = fragments.Where(IsScript).ToList();

            var lines = new List<Line>();
            var baselines = new List<double>();
            Line current = null;
            double anchorY = 0;
            foreach (var f in main)
            {
                var tol = Math.Max(f.FontSize * 0.5, 2.0);
                if (current == null || anchorY - f.Rectangle.LLY > tol)
                {
                    current = new Line();
                    lines.Add(current);
                    baselines.Add(f.Rectangle.LLY);
                    anchorY = f.Rectangle.LLY;
                }
                current.Elems.Add(Elem.ForText(f));
            }

            foreach (var s in scripts)
            {
                var best = -1;
                var bestDist = double.MaxValue;
                for (var k = 0; k < lines.Count; k++)
                {
                    var d = Math.Abs(baselines[k] - s.Rectangle.LLY);
                    if (d < bestDist) { bestDist = d; best = k; }
                }
                if (best >= 0)
                    lines[best].Elems.Add(Elem.ForText(s));
                else
                {
                    var l = new Line();
                    l.Elems.Add(Elem.ForText(s));
                    lines.Add(l);
                }
            }

            foreach (var l in lines)
            {
                l.Columnar = columnar;
                l.Finish(links);
            }

            return lines;
        }

        private static List<LinkInfo> CollectLinks(Page page)
        {
            var links = new List<LinkInfo>();
            foreach (var annotation in page.Annotations)
            {
                if (annotation is Aspose.Pdf.Annotations.LinkAnnotation link &&
                    !string.IsNullOrEmpty(link.Uri) && link.Rect != null)
                    links.Add(new LinkInfo(link.Rect, link.Uri));
            }
            return links;
        }

        private static string LinkFor(Elem e, List<LinkInfo> links)
        {
            if (links.Count == 0 || e.IsImage) return null;
            var cx = (e.LLX + e.URX) / 2;
            var cy = (e.LLY + e.URY) / 2;
            foreach (var link in links)
            {
                var lr = link.Rect;
                if (cx >= lr.LLX && cx <= lr.URX && cy >= lr.LLY && cy <= lr.URY)
                    return link.Uri;
            }
            return null;
        }

        private static double DominantSize(List<Line> lines)
        {
            var weight = new Dictionary<double, int>();
            foreach (var l in lines)
            {
                if (l.FontSize <= 0) continue;
                weight.TryGetValue(l.FontSize, out var w);
                weight[l.FontSize] = w + l.CharCount;
            }
            return weight.Count == 0 ? 0 : weight.OrderByDescending(kv => kv.Value).First().Key;
        }

        private static int HeadingLevel(Line line, int pageNumber, double bodySize,
            List<double> headingSizes, List<HeadingDest> outlineDests)
        {
            if (line.CharCount == 0)
                return 0;

            if (outlineDests != null)
            {
                // Outline case: a heading is a line an outline destination points at. TopY is the
                // baseline; TopY+FontSize is the glyph top, which is where a bookmark's top edge
                // lands (within a font-size tolerance). Misaligned bookmarks match nothing.
                var lineTop = line.TopY + line.FontSize;
                var tol = Math.Max(line.FontSize, 6.0);
                var best = 0;
                var bestDist = double.MaxValue;
                foreach (var d in outlineDests)
                {
                    if (d.Page != pageNumber) continue;
                    var dist = Math.Abs(d.Top - lineTop);
                    if (dist <= tol && dist < bestDist) { bestDist = dist; best = d.Level; }
                }
                return best;
            }

            var size = line.FontSize;
            if (size <= bodySize + 0.5)
                return 0;
            var idx = headingSizes.FindIndex(s => Math.Abs(s - size) < 0.25);
            return idx < 0 ? 0 : idx + 1;
        }

        // ── Rendering ─────────────────────────────────────────────────────────────────

        private static string RenderHeading(Line line, int level, bool styled)
        {
            var text = line.Text;
            var img = line.CharIsImage;
            var n = text.Length;

            var coreStart = 0;
            while (coreStart < n && (img[coreStart] || text[coreStart] == ' ')) coreStart++;
            var coreEnd = n;
            while (coreEnd > coreStart && (img[coreEnd - 1] || text[coreEnd - 1] == ' ')) coreEnd--;

            var prefix = text.Substring(0, coreStart);
            var suffix = text.Substring(coreEnd);
            // A heading collapses a run of trailing spaces to one (e.g. source "Education:  ").
            if (suffix.Length > 1 && suffix.All(c => c == ' ')) suffix = " ";
            var core = text.Substring(coreStart, coreEnd - coreStart);

            // Headings are already visually bold, so bold is never marked; only italic is.
            var inner = ApplyStyle(EscapeText(core), (byte)(styled && line.IsItalic ? 2 : 0));
            return new string('#', level) + " " + prefix + inner + suffix;
        }

        private static string RenderParagraph(List<Line> paragraph, bool columnar = false)
        {
            var logical = new List<(string text, List<string> uris, List<bool> img, List<byte> style)>();
            var buf = new StringBuilder();
            var bufUris = new List<string>();
            var bufImg = new List<bool>();
            var bufStyle = new List<byte>();
            for (var k = 0; k < paragraph.Count; k++)
            {
                AppendMerged(buf, bufUris, bufImg, bufStyle, paragraph[k]);
                var isLast = k == paragraph.Count - 1;
                // Soft break at a source-line boundary. The default (single-column) rule keeps a
                // line only when it ends a sentence ('.') and the next begins one. The columnar
                // rule uses a richer line-break heuristic (a new list item,
                // a colon/semicolon-terminated line, a numbered item, or a capitalised sentence
                // start after a period), which the tight column layouts depend on.
                var lineBreak = false;
                if (!isLast)
                {
                    if (columnar)
                    {
                        lineBreak = IsLineBreakRequired(paragraph, k);
                    }
                    else
                    {
                        var cur = paragraph[k].Text.TrimEnd();
                        var next = paragraph[k + 1].Text.TrimStart();
                        lineBreak = cur.EndsWith(".", StringComparison.Ordinal) && next.Length > 0
                            && (char.IsUpper(next[0]) || char.IsDigit(next[0]));
                    }
                }
                if (isLast || lineBreak)
                {
                    logical.Add((buf.ToString(), new List<string>(bufUris),
                        new List<bool>(bufImg), new List<byte>(bufStyle)));
                    buf.Clear();
                    bufUris.Clear();
                    bufImg.Clear();
                    bufStyle.Clear();
                }
            }

            var sb = new StringBuilder();
            for (var j = 0; j < logical.Count; j++)
            {
                var emitted = EmitRuns(logical[j].text, logical[j].uris, logical[j].img,
                    logical[j].style, 0, logical[j].text.Length);
                if (j < logical.Count - 1)
                    sb.Append(emitted.TrimEnd(' ')).Append(SoftBreak).Append(NewLine);
                else
                    sb.Append(emitted);
            }
            return sb.ToString();
        }

        // Line-break heuristic used for multi-column paragraphs. A source line keeps its own
        // output line when the NEXT line begins a list item ('-'), a number, or a capitalised
        // sentence following a period/TOC leader, or when THIS line ends with a colon/semicolon.
        private static bool IsLineBreakRequired(List<Line> lines, int lineIndex)
        {
            if (lineIndex >= lines.Count - 1) return false;
            var cur = lines[lineIndex];
            var next = lines[lineIndex + 1];

            // A hyperlink that continues across the break stays on one line.
            var u1 = LastCharUri(cur);
            var u2 = FirstCharUri(next);
            if (u1 != null && u2 != null && string.Equals(u1, u2, StringComparison.Ordinal))
                return false;

            return IsNeedNewLine(cur.Text, next.Text);
        }

        private static bool IsNeedNewLine(string last, string next)
        {
            if (StartsWithMinus(next)) return true;
            if (EndsWithColonOrSemicolon(last)) return true;
            if (StartsWithNumbering(next)) return true;
            var capital = StartsWithCapital(next);
            if (capital && EndsInADot(last)) return true;
            if (capital && TableOfContentsSecond(last)) return true;
            return false;
        }

        private static bool StartsWithMinus(string w)
        {
            foreach (var c in w)
            {
                if (char.IsWhiteSpace(c)) continue;
                return c is '-' or '•' or '·' or '–' or '—'; // -, •, ·, –, —
            }
            return false;
        }

        private static bool StartsWithCapital(string w)
        {
            foreach (var c in w)
            {
                if (char.IsWhiteSpace(c)) continue;
                return char.IsUpper(c);
            }
            return false;
        }

        private static bool EndsWithColonOrSemicolon(string w)
        {
            for (var i = w.Length - 1; i >= 0; i--)
            {
                if (char.IsWhiteSpace(w[i])) continue;
                return w[i] == ':' || w[i] == ';';
            }
            return false;
        }

        private static bool EndsInADot(string w)
        {
            for (var i = w.Length - 1; i >= 0; i--)
            {
                if (char.IsWhiteSpace(w[i])) continue;
                return w[i] == '.';
            }
            return false;
        }

        // Leading digits with optional dotted groups then whitespace (1 / 1. / 1.2 / 1.2.).
        private static bool StartsWithNumbering(string w)
        {
            bool lastNum = false, lastDot = false;
            foreach (var c in w)
            {
                if (char.IsDigit(c)) { lastDot = false; lastNum = true; }
                else if (c == '.')
                {
                    if (lastDot || !lastNum) return false;
                    lastDot = true; lastNum = false;
                }
                else if (char.IsWhiteSpace(c))
                    return lastNum || lastDot;
                else
                    return false;
            }
            return false;
        }

        // The line ends with a table-of-contents leader: three-or-more dot groups then a number.
        private static bool TableOfContentsSecond(string word)
        {
            if (string.IsNullOrEmpty(word)) return false;
            var index = word.Length - 1;
            while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
            if (index < 0) return false;
            var digitCount = 0;
            while (index >= 0 && char.IsDigit(word[index])) { index--; digitCount++; }
            if (digitCount == 0) return false;
            while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
            if (index < 0) return false;
            var dotGroups = 0;
            while (index >= 0)
            {
                while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
                if (index < 0 || word[index] != '.') break;
                index--;
                while (index >= 0 && char.IsWhiteSpace(word[index])) index--;
                dotGroups++;
            }
            return dotGroups >= 3;
        }

        private static string LastCharUri(Line line)
        {
            for (var i = line.CharUris.Count - 1; i >= 0; i--)
            {
                if (i < line.Text.Length && char.IsWhiteSpace(line.Text[i])) continue;
                if (line.CharUris[i] != null) return line.CharUris[i];
                break;
            }
            return null;
        }

        private static string FirstCharUri(Line line)
        {
            for (var i = 0; i < line.CharUris.Count; i++)
            {
                if (i < line.Text.Length && char.IsWhiteSpace(line.Text[i])) continue;
                return line.CharUris[i];
            }
            return null;
        }

        private static string EmitRuns(string raw, List<string> uris, List<bool> img,
            List<byte> style, int start, int end)
        {
            var sb = new StringBuilder();
            var i = start;
            while (i < end)
            {
                var uri = uris[i];
                var isImg = img[i];
                var st = style[i];
                var j = i;
                while (j < end && uris[j] == uri && img[j] == isImg && style[j] == st) j++;
                var seg = raw.Substring(i, j - i);
                if (isImg)
                {
                    sb.Append(seg);
                }
                else
                {
                    // Style markers wrap the trimmed run; surrounding whitespace stays outside.
                    var lead = seg.Substring(0, seg.Length - seg.TrimStart().Length);
                    var trail = seg.Substring(seg.TrimEnd().Length);
                    var coreText = ApplyStyle(EscapeText(seg.Trim()), st);
                    if (uri != null && coreText.Length > 0)
                        coreText = "[" + coreText + "](" + uri + ")";
                    sb.Append(lead).Append(coreText).Append(trail);
                }
                i = j;
            }
            return sb.ToString();
        }

        /// <summary>Wrap already-escaped text in emphasis markers. Superscript/subscript use
        /// <c>^…^</c>/<c>~…~</c>; otherwise strikethrough is outermost, then bold+italic.</summary>
        private static string ApplyStyle(string escaped, byte style)
        {
            if (escaped.Length == 0) return escaped;
            if ((style & 8) != 0) return "^" + escaped + "^";   // superscript
            if ((style & 16) != 0) return "~" + escaped + "~";  // subscript

            var bold = (style & 1) != 0;
            var italic = (style & 2) != 0;
            var emphasis = (bold, italic) switch
            {
                (true, true) => "***",
                (true, false) => "**",
                (false, true) => "*",
                _ => string.Empty,
            };
            var result = emphasis.Length == 0 ? escaped : emphasis + escaped + emphasis;
            if ((style & 4) != 0) result = "~~" + result + "~~";  // strikethrough, outermost
            return result;
        }

        private static void AppendMerged(StringBuilder buf, List<string> bufUris, List<bool> bufImg,
            List<byte> bufStyle, Line line)
        {
            if (buf.Length != 0)
            {
                var last = buf[buf.Length - 1];
                var first = line.Text.Length > 0 ? line.Text[0] : '\0';
                if (last != ' ' && first != ' ' && first != '\0')
                {
                    buf.Append(' ');
                    bufUris.Add(null);
                    bufImg.Add(false);
                    // Keep the joining space inside a same-style span across a wrapped line.
                    var lastStyle = bufStyle.Count > 0 ? bufStyle[bufStyle.Count - 1] : (byte)0;
                    var firstStyle = line.CharStyle.Count > 0 ? line.CharStyle[0] : (byte)0;
                    bufStyle.Add(lastStyle == firstStyle ? lastStyle : (byte)0);
                }
            }
            buf.Append(line.Text);
            bufUris.AddRange(line.CharUris);
            bufImg.AddRange(line.CharIsImage);
            bufStyle.AddRange(line.CharStyle);
        }


        private static string EscapeText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '-')
                {
                    // A hyphen is escaped except when it is a leading list-marker dash — the first
                    // character followed by whitespace ("- item"). A mid-line or joined hyphen
                    // ("July.09 - Present", "T-SQL", "-(X)") is escaped.
                    var next = i + 1 < text.Length ? text[i + 1] : ' ';
                    if (!(i == 0 && char.IsWhiteSpace(next))) sb.Append('\\');
                }
                else if (c is '_' or '#' or '[' or ']' or '*')
                    sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }

        // ── Model ─────────────────────────────────────────────────────────────────────

        // An outline destination reduced to what heading matching needs: the target page, the
        // top edge it points at, and the outline nesting depth (= heading level).
        private readonly record struct HeadingDest(int Page, double Top, int Level);

        private sealed record LinkInfo(Rectangle Rect, string Uri);

        private sealed record MdBlock(string Text, bool IsTable, double TopY);

        private readonly struct Elem
        {
            public readonly TextFragment Frag;
            public readonly Rectangle Rect;
            public readonly string ImageToken;

            private Elem(TextFragment frag, Rectangle rect, string token)
            {
                Frag = frag; Rect = rect; ImageToken = token;
            }

            public static Elem ForText(TextFragment f) => new(f, f.Rectangle, null);
            public static Elem ForImage(Rectangle r, string token) => new(null, r, token);

            public bool IsImage => ImageToken != null;
            public double LLX => Rect.LLX;
            public double URX => Rect.URX;
            public double LLY => Rect.LLY;
            public double URY => Rect.URY;
            public double FontSize => IsImage ? 0 : Frag.FontSize;
            public string Text => IsImage ? ImageToken : Frag.Text;

            // Inline style bits: 1=bold, 2=italic, 4=strikethrough, 8=superscript, 16=subscript.
            public byte Style
            {
                get
                {
                    if (IsImage) return 0;
                    var ts = Frag.TextState;
                    byte s = 0;
                    if ((ts.FontStyle & FontStyles.Bold) != 0) s |= 1;
                    if ((ts.FontStyle & FontStyles.Italic) != 0) s |= 2;
                    if (ts.StrikeOut) s |= 4;
                    if (ts.Superscript) s |= 8;
                    if (ts.Subscript) s |= 16;
                    return s;
                }
            }
        }

        private sealed class Line
        {
            public readonly List<Elem> Elems = new();
            // In multi-column paragraphs an inter-fragment space is inserted
            // by ink-gap even when the previous fragment already ends in a space, producing the
            // doubled spaces seen between column items.
            public bool Columnar;
            public string Text { get; private set; } = string.Empty;
            public List<string> CharUris { get; } = new();
            public List<bool> CharIsImage { get; } = new();
            public List<byte> CharStyle { get; } = new();
            public double FontSize { get; private set; }
            public double Right { get; private set; }
            public double Left { get; private set; }
            public double TopY { get; private set; }
            public bool IsItalic { get; private set; }
            public int CharCount { get; private set; }
            public int BoldChars { get; private set; }
            public int ItalicChars { get; private set; }
            public string CoreText { get; private set; } = string.Empty;

            public void Finish(List<LinkInfo> links)
            {
                var sizeWeight = new Dictionary<double, int>();
                foreach (var e in Elems)
                {
                    if (e.IsImage) continue;
                    var len = e.Frag.Text?.Length ?? 0;
                    var s = Math.Round(e.FontSize * 2) / 2.0;
                    sizeWeight.TryGetValue(s, out var w);
                    sizeWeight[s] = w + len;

                    CharCount += len;
                    if ((e.Frag.TextState.FontStyle & FontStyles.Bold) != 0) BoldChars += len;
                    if ((e.Frag.TextState.FontStyle & FontStyles.Italic) != 0) ItalicChars += len;
                }
                FontSize = sizeWeight.Count == 0
                    ? 0
                    : sizeWeight.OrderByDescending(kv => kv.Value).First().Key;
                IsItalic = CharCount > 0 && ItalicChars * 2 > CharCount;
                Reassemble(links);
            }

            /// <summary>Add an inline image to this line and re-lay it out.</summary>
            public void AddImage(Rectangle rect, string token, List<LinkInfo> links)
            {
                Elems.Add(Elem.ForImage(rect, token));
                Reassemble(links);
            }

            private void Reassemble(List<LinkInfo> links)
            {
                CharUris.Clear();
                CharIsImage.Clear();
                CharStyle.Clear();
                var ordered = Elems.OrderBy(e => e.LLX).ToList();
                AssembleText(ordered, links);
                Right = ordered.Count == 0 ? 0 : ordered.Max(e => e.URX);
                Left = ordered.Count == 0 ? 0 : ordered.Min(e => e.LLX);
                TopY = ordered.Count == 0 ? 0 : ordered.Max(e => e.LLY);
            }

            // Concatenate the line's elements left-to-right, reconstructing inter-word spaces
            // from horizontal gaps. Image tokens are space-delimited so they read as their own
            // word; a wide text→image gap contributes an extra (word-boundary) space.
            private void AssembleText(List<Elem> ordered, List<LinkInfo> links)
            {
                var sb = new StringBuilder();
                var core = new StringBuilder();
                Elem? prev = null;
                string prevUri = null;
                byte prevStyle = 0;

                void Emit(string s, string uri, bool isImage, byte style)
                {
                    foreach (var ch in s)
                    {
                        sb.Append(ch);
                        CharUris.Add(uri);
                        CharIsImage.Add(isImage);
                        CharStyle.Add(style);
                    }
                    if (!isImage) core.Append(s);
                }

                foreach (var e in ordered)
                {
                    var uri = LinkFor(e, links);
                    var style = e.Style;
                    if (prev != null)
                    {
                        var gap = e.LLX - prev.Value.URX;
                        var em = Math.Max(Math.Max(prev.Value.FontSize, e.FontSize), 12.0);
                        var lastCh = sb.Length > 0 ? sb[sb.Length - 1] : '\0';
                        var nextCh = e.IsImage ? ' ' : (e.Text.Length > 0 ? e.Text[0] : '\0');
                        // An image already carries its own delimiting spaces, so it only needs an
                        // extra word-boundary space when it sits in a wide gap (a word slot), not
                        // when it abuts the preceding text. Multi-column text uses the
                        // inter-fragment rule (gap > height·0.13636) so that a genuine
                        // column gap synthesizes a space even after a trailing space, while normal
                        // word spacing does not double.
                        var wide = e.IsImage
                            ? gap > em * 0.5
                            : Columnar
                                ? gap > prev.Value.Rect.Height * 0.13636
                                : gap > em * SpaceGapRatio;
                        // A capital's right side-bearing opens an ink-box gap that can be as wide
                        // as a real space (a bold Times 'H' before a lowercase letter clears ~0.2em),
                        // and the exact advance width is unavailable for this font. Suppress the
                        // synthesized space for an upper→lower transition under ~0.22em — a genuine
                        // inter-word gap in the same context runs wider.
                        if (wide && !e.IsImage && gap < em * 0.22
                            && char.IsUpper(lastCh) && char.IsLower(nextCh))
                            wide = false;
                        if (wide && Columnar && !e.IsImage)
                        {
                            if (lastCh == ' ' && gap <= em * 0.5)
                                wide = false;   // after a space, only a real column gap doubles it
                            else if (nextCh is ',' or '.' or ';' or ':')
                                wide = false;   // punctuation attaches to the preceding word
                            else if (char.IsUpper(nextCh) && !prev.Value.IsImage
                                     && prev.Value.Frag?.Text?.Trim() is { Length: 1 } pt
                                     && char.IsUpper(pt[0]))
                                wide = false;   // small-caps initial glues to its following run
                        }
                        if (wide && (Columnar || lastCh != ' ') && (e.IsImage || nextCh != ' '))
                        {
                            sb.Append(' ');
                            CharUris.Add(prevUri == uri && !e.IsImage ? uri : null);
                            CharIsImage.Add(false);
                            // A synthesized space between two runs of the same style stays inside
                            // that style span (e.g. "acinia interdum leo" under one ~~***…***~~).
                            CharStyle.Add(!e.IsImage && prevStyle == style ? style : (byte)0);
                        }
                    }

                    if (e.IsImage)
                    {
                        Emit(" ", null, false, 0);
                        Emit(e.ImageToken, null, true, 0);
                        Emit(" ", null, false, 0);
                    }
                    else
                    {
                        Emit(e.Text, uri, false, style);
                    }

                    prev = e;
                    prevUri = uri;
                    prevStyle = style;
                }

                Text = sb.ToString();
                CoreText = core.ToString();
            }
        }
    }
}
