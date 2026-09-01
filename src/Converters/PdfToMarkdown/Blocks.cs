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
                       paragraph[paragraph.Count - 1].TopY - tls[i].RaisedTopY <= tls[i].FontSize * 1.6)
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

        // Raised-reference merge: a line holding only glyphs visibly SMALLER than a
        // neighbour's, floating above that neighbour's baseline by less than an ascender,
        // is that line's superscript reference (a citation like "[31]") — the extractor
        // carries no script flag for it, so the geometry decides. It merges in at its X
        // with the superscript style forced.
        for (var i = Math.Min(lines.Count, baselines.Count) - 1; i >= 0; i--)
        {
            var l = lines[i];
            if (l.Elems.Count == 0 || l.Elems.Any(e => e.IsImage)) continue;
            var lSize = l.Elems.Max(e => e.FontSize);
            var lBase = baselines[i];
            var host = -1;
            for (var k = 0; k < baselines.Count; k++)
            {
                if (k == i) continue;
                var hostElems = lines[k].Elems.Where(e => !e.IsImage).ToList();
                if (hostElems.Count == 0) continue;
                var hSize = hostElems.Max(e => e.FontSize);
                if (lSize > hSize - 1.5) continue;              // must be visibly smaller
                var rise = lBase - baselines[k];
                if (rise < 1.0 || rise > hSize * 0.8) continue; // raised by less than an ascender
                var left = hostElems.Min(e => e.LLX);
                var right = hostElems.Max(e => e.URX);
                var cx = (l.Elems.Min(e => e.LLX) + l.Elems.Max(e => e.URX)) / 2;
                if (cx < left - hSize || cx > right + 2 * hSize) continue;
                host = k;
                break;
            }
            if (host < 0) continue;
            foreach (var e in l.Elems)
                lines[host].Elems.Add(Elem.ForScript(e.Frag, 8));
            lines.RemoveAt(i);
            baselines.RemoveAt(i);
        }

        foreach (var l in lines)
        {
            l.Columnar = columnar;
            l.Finish(links);
        }

        return lines;
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
        // A heading is set uniformly; big text sharing its line with ordinary-size
        // glyphs (a "Control plane [edit]" wiki heading) reads as a bold paragraph.
        if (line.HasMixedTextSizes)
            return 0;
        var idx = headingSizes.FindIndex(s => Math.Abs(s - size) < 0.25);
        return idx < 0 ? 0 : idx + 1;
    }
}
