using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Options for <see cref="ParagraphAbsorber"/> controlling section detection thresholds.
/// </summary>
public sealed class ParagraphAbsorberOptions
{
    private double _vOverride = double.NaN;
    private double _hOverride = double.NaN;

    /// <summary>
    /// Vertical distance override (as fraction of font size) below which lines are
    /// considered part of the same section. NaN means "use the default heuristic".
    /// </summary>
    public double SectionUnbreakingVerticalOverride
    {
        get => _vOverride;
        set => _vOverride = value;
    }

    /// <summary>
    /// Horizontal distance override (as fraction of page width) below which fragments
    /// are considered part of the same section. NaN means "use the default heuristic".
    /// </summary>
    public double SectionUnbreakingHorizontalOverride
    {
        get => _hOverride;
        set => _hOverride = value;
    }

    internal bool HasVerticalOverride => !double.IsNaN(_vOverride);
    internal bool HasHorizontalOverride => !double.IsNaN(_hOverride);

    /// <summary>Optional rectangle restricting the absorber to a page region. Stored only.</summary>
    public Aspose.Pdf.Rectangle? SearchRectangle { get; set; }
}

/// <summary>
/// Absorbs text from PDF pages and organizes it into sections and paragraphs.
/// </summary>
public sealed class ParagraphAbsorber
{
    private ParagraphAbsorberOptions _options;
    private readonly List<PageMarkup> _pageMarkups = [];

    public ParagraphAbsorber() : this(new ParagraphAbsorberOptions()) { }

    public ParagraphAbsorber(ParagraphAbsorberOptions paragraphAbsorberOptions)
    {
        _options = paragraphAbsorberOptions ?? new ParagraphAbsorberOptions();
    }

    public ParagraphAbsorber(int sectionsSearchDepth)
        : this(new ParagraphAbsorberOptions())
    {
        SectionsSearchDepth = sectionsSearchDepth;
    }

    public ParagraphAbsorber(int sectionsSearchDepth, ParagraphAbsorberOptions paragraphAbsorberOptions)
        : this(paragraphAbsorberOptions)
    {
        SectionsSearchDepth = sectionsSearchDepth;
    }

    /// <summary>
    /// The page markups produced by <see cref="Visit(Document)"/> or <see cref="Visit(Page)"/>.
    /// </summary>
    public List<PageMarkup> PageMarkups => _pageMarkups;

    /// <summary>Active options bag. Setting replaces; null is treated as defaults.</summary>
    public ParagraphAbsorberOptions ParagraphAbsorberOptions
    {
        get => _options;
        set => _options = value ?? new ParagraphAbsorberOptions();
    }

    /// <summary>How many nesting levels to descend when partitioning sections.
    /// Stored only — the FOSS absorber treats every page as a single section
    /// regardless of this value.</summary>
    public int SectionsSearchDepth { get; set; }

    /// <summary>Options that flow through to the inner <see cref="TextFragmentAbsorber"/>
    /// when re-emitting paragraphs. Stored only by the FOSS absorber.</summary>
    public TextReplaceOptions? TextReplaceOptions { get; set; }

    /// <summary>
    /// Gets or sets whether multicolumn paragraph merging is enabled.
    /// When set on the absorber level, it applies to all page markups and
    /// performs cross-page paragraph continuation assembly.
    /// </summary>
    public bool IsMulticolumnParagraphsAllowed
    {
        get => _isMulticolumn;
        set
        {
            _isMulticolumn = value;
            foreach (var m in _pageMarkups)
                m.IsMulticolumnParagraphsAllowed = value;

            if (_isMulticolumn)
                AssembleCrossPageParagraphs();
        }
    }
    private bool _isMulticolumn;

    /// <summary>
    /// After multicolumn merging, assemble cross-page paragraph continuations.
    /// The last paragraph on page N continues as the first paragraph on page N+1
    /// if the column positions match.
    /// </summary>
    private void AssembleCrossPageParagraphs()
    {
        for (var pi = 0; pi < _pageMarkups.Count - 1; pi++)
        {
            var currPage = _pageMarkups[pi];
            var nextPage = _pageMarkups[pi + 1];

            if (currPage.Sections.Count == 0 || nextPage.Sections.Count == 0) continue;

            // Get the last paragraph from the last section of current page
            var lastSection = currPage.Sections[^1];
            if (lastSection.Paragraphs.Count == 0) continue;
            var lastPara = lastSection.Paragraphs[^1];

            // Get the first paragraph from the first section of next page
            var firstSection = nextPage.Sections[0];
            if (firstSection.Paragraphs.Count == 0) continue;
            var firstPara = firstSection.Paragraphs[0];

            // Merge: concatenate lines from the continuation paragraph into the last paragraph.
            // With multicolumn, the right column's last paragraph flows into the next page,
            // so X positions may differ.
            var mergedLines = new List<List<TextFragment>>(lastPara.Lines);
            mergedLines.AddRange(firstPara.Lines);

            var mergedText = lastPara.Text + "\r\n" + firstPara.Text;
            var mergedPoints = PageMarkup.MergePoints(lastPara.Points, firstPara.Points);

            // Replace last paragraph with merged version
            lastSection.ReplaceParagraph(lastSection.Paragraphs.Count - 1,
                new MarkupParagraph(mergedText, mergedPoints, mergedLines));

            // Remove first paragraph from next page
            firstSection.RemoveParagraph(0);
        }
    }

    /// <summary>
    /// Visit an entire document — absorbs all pages.
    /// </summary>
    public void Visit(Document doc)
    {
        for (var i = 1; i <= doc.PageCount; i++)
            Visit(doc.Pages.At(i));
    }

    /// <summary>
    /// Visit a single page and absorb its text into sections/paragraphs.
    /// </summary>
    public void Visit(Page page)
    {
        var absorber = new TextFragmentAbsorber();
        absorber.Visit(page);

        var markup = BuildMarkup(page, absorber.TextFragments);
        _pageMarkups.Add(markup);
    }

    private PageMarkup BuildMarkup(Page page, TextFragmentCollection fragments)
    {
        if (fragments.Count == 0)
            return new PageMarkup(page.Number, [], _options);

        // Filter out fragments with no rectangle or whitespace-only text
        var validFragments = fragments
            .Where(f => f.Rectangle is not null && !string.IsNullOrWhiteSpace(f.Text))
            .ToList();

        // Whitespace-only fragments are excluded from section/line geometry but
        // kept aside: documents that draw each inter-word space as its own run
        // (word runs touching, space glyph overlapping) need them to re-insert
        // the spaces into paragraph text — the pure fragment-gap heuristic sees
        // a near-zero gap there and drops the space.
        var spaceFragments = fragments
            .Where(f => f.Rectangle is not null && f.Text is { Length: > 0 }
                && string.IsNullOrWhiteSpace(f.Text))
            .OrderBy(f => f.Rectangle!.LLX)
            .ToList();

        // Restrict to the requested page region, if any: keep a fragment when its
        // centre falls inside SearchRectangle. This drops out-of-region content
        // (e.g. headers/footers) before section/paragraph detection runs.
        if (_options.SearchRectangle is { } sr)
        {
            validFragments = validFragments
                .Where(f =>
                {
                    var r = f.Rectangle!;
                    // Vertically the centre must be inside the band (drops out-of-region
                    // headers/footers); horizontally any overlap with the region keeps the
                    // fragment, so glyphs starting at the left margin are not lost.
                    var cy = (r.LLY + r.URY) / 2;
                    return cy >= sr.LLY && cy <= sr.URY
                        && r.URX >= sr.LLX && r.LLX <= sr.URX;
                })
                .ToList();
        }

        if (validFragments.Count == 0)
            return new PageMarkup(page.Number, [], _options);

        // Use standard section detection
        var sections = FindSections(validFragments, page);

        // Post-process for multi-column layouts: split sections spanning multiple
        // detected columns, then merge same-column adjacent sections.
        var pageWidth = page.MediaBox?.Width ?? 612;
        var avgFs = validFragments.Average(f => f.FontSize > 0 ? f.FontSize : 12);
        var detectedColumns = DetectColumnsFromFragments(validFragments, avgFs, pageWidth);
        // ALWAYS run split-and-merge when columns detected
        if (detectedColumns is not null && detectedColumns.Count >= 2)
        {
            var before = sections.Count;
            sections = SplitAndMergeByColumns(sections, detectedColumns, avgFs);
            // Sanity: if split-merge made things worse, revert (safety net)
            // This shouldn't happen in production.
        }

        // Re-assemble paragraph text with the standalone space glyphs folded back in.
        if (spaceFragments.Count > 0)
            foreach (var section in sections)
                foreach (var para in section.Paragraphs)
                    para.RefreshText(AssembleTextWithSpaces(para.Lines, spaceFragments));

        // Undo page rotation on section/paragraph coordinates so they are in
        // the content stream's native coordinate system (matching the public API behaviour).
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        if (rotate != 0)
        {
            var mb = page.MediaBox;
            var mbW = mb?.Width ?? 612;
            var mbH = mb?.Height ?? 792;
            foreach (var section in sections)
            {
                section.Rectangle = UndoRotation(section.Rectangle, rotate, mbW, mbH);
                foreach (var para in section.Paragraphs)
                {
                    for (var pi = 0; pi < para.Points.Length; pi++)
                    {
                        var pt = para.Points[pi];
                        var ptRect = UndoRotation(new Rectangle(pt.X, pt.Y, pt.X, pt.Y), rotate, mbW, mbH);
                        para.Points[pi] = new Point(ptRect.LLX, ptRect.LLY);
                    }
                }
            }
        }

        return new PageMarkup(page.Number, sections, _options);
    }

    private static Rectangle UndoRotation(Rectangle rect, int rotate, double mbW, double mbH)
    {
        double llx, lly, urx, ury;
        switch (rotate)
        {
            case 90:
                llx = rect.LLY;
                lly = mbW - rect.URX;
                urx = rect.URY;
                ury = mbW - rect.LLX;
                break;
            case 180:
                llx = mbW - rect.URX;
                lly = mbH - rect.URY;
                urx = mbW - rect.LLX;
                ury = mbH - rect.LLY;
                break;
            case 270:
                llx = mbH - rect.URY;
                lly = rect.LLX;
                urx = mbH - rect.LLY;
                ury = rect.URX;
                break;
            default:
                return rect;
        }
        return new Rectangle(
            Math.Min(llx, urx), Math.Min(lly, ury),
            Math.Max(llx, urx), Math.Max(lly, ury));
    }

    /// <summary>
    /// Post-process sections for multi-column layouts.
    /// 1. Split any section spanning multiple detected columns into per-column sections.
    /// 2. Merge same-column sections that are vertically adjacent.
    /// </summary>
    private List<MarkupSection> SplitAndMergeByColumns(
        List<MarkupSection> sections, List<(double left, double right)> columns, double avgFontSize)
    {
        // Step 1: Split multi-column sections
        var result = new List<MarkupSection>();
        foreach (var section in sections)
        {
            // Count how many columns this section spans
            var spanCount = 0;
            for (var ci = 0; ci < columns.Count; ci++)
                if (section.Rectangle.URX > columns[ci].left && section.Rectangle.LLX < columns[ci].right)
                    spanCount++;

            if (spanCount <= 1)
            {
                result.Add(section);
                continue;
            }

            // Short multi-column sections: don't split (keep as-is) unless they have
            // large internal gaps indicating separate items (like page headers).
            var sectionHeight = section.Rectangle.URY - section.Rectangle.LLY;
            if (section.Paragraphs.Count <= 1 && sectionHeight < avgFontSize * 6)
            {
                result.Add(section);
                continue;
            }

            // Extract all fragments and assign each to a column by center X.
            // Then group into lines per column. This avoids multi-column lines.
            var colFrags = new Dictionary<int, List<TextFragment>>();
            foreach (var para in section.Paragraphs)
            {
                foreach (var line in para.Lines)
                {
                    foreach (var frag in line)
                    {
                        if (frag.Rectangle is null) continue;
                        var cx = (frag.Rectangle.LLX + frag.Rectangle.URX) / 2;
                        var col = -1;
                        for (var ci = 0; ci < columns.Count; ci++)
                        {
                            if (cx >= columns[ci].left && cx <= columns[ci].right)
                            { col = ci; break; }
                        }
                        if (col < 0) col = 0;
                        if (!colFrags.TryGetValue(col, out var list))
                        {
                            list = [];
                            colFrags[col] = list;
                        }
                        list.Add(frag);
                    }
                }
            }

            if (colFrags.Count == 0)
            {
                result.Add(section);
                continue;
            }

            // Group fragments per column into lines, then build sections
            var colLines = new Dictionary<int, List<TextLine>>();
            foreach (var kv in colFrags)
            {
                colLines[kv.Key] = GroupIntoLines(kv.Value);
            }

            // Build sections per column. Within a column, split at large Y gaps
            // to preserve section boundaries from the original layout.
            foreach (var kv in colLines.OrderBy(kv => kv.Key))
            {
                if (kv.Value.Count == 0) continue;
                var sorted = kv.Value;
                sorted.Sort((a, b) => b.MidY.CompareTo(a.MidY));

                if (sorted.Count <= 1)
                {
                    result.Add(BuildSection(sorted));
                    continue;
                }

                // Find the vertical threshold for section breaks within this column
                var colGaps = new List<double>();
                for (var li = 1; li < sorted.Count; li++)
                {
                    var gap = sorted[li - 1].MinY - sorted[li].MaxY;
                    if (gap > 0) colGaps.Add(gap);
                }

                double colVThreshold;
                if (colGaps.Count > 2)
                {
                    colGaps.Sort();
                    var medGap = colGaps[colGaps.Count / 2];
                    colVThreshold = Math.Max(medGap * 2.5, avgFontSize * 2.0);
                }
                else
                {
                    colVThreshold = avgFontSize * 2.0;
                }

                // Split into sub-sections at large gaps
                var currentGroup = new List<TextLine> { sorted[0] };
                for (var li = 1; li < sorted.Count; li++)
                {
                    var gap = currentGroup[^1].MinY - sorted[li].MaxY;
                    if (gap > colVThreshold)
                    {
                        result.Add(BuildSection(currentGroup));
                        currentGroup = [sorted[li]];
                    }
                    else
                    {
                        currentGroup.Add(sorted[li]);
                    }
                }
                if (currentGroup.Count > 0)
                    result.Add(BuildSection(currentGroup));
            }
        }

        // Step 2: Merge same-column vertically-adjacent sections.
        // Assign each section to a column (-1 if multi-column).
        // Use strict containment but with center-X fallback when section
        // overflows its column by < 1.3× column width.
        var colAssign = new int[result.Count];
        for (var si = 0; si < result.Count; si++)
        {
            var s = result[si];
            // First try strict containment
            var assigned = -1;
            var strictCount = 0;
            for (var ci = 0; ci < columns.Count; ci++)
            {
                if (s.Rectangle.URX > columns[ci].left && s.Rectangle.LLX < columns[ci].right)
                { strictCount++; assigned = ci; }
            }
            if (strictCount == 1)
            {
                colAssign[si] = assigned;
                continue;
            }
            // Multi-column by strict containment. Check if it's just slight overflow.
            var centerX = (s.Rectangle.LLX + s.Rectangle.URX) / 2;
            var sWidth = s.Rectangle.URX - s.Rectangle.LLX;
            assigned = -1;
            for (var ci = 0; ci < columns.Count; ci++)
            {
                if (centerX >= columns[ci].left && centerX <= columns[ci].right)
                { assigned = ci; break; }
            }
            if (assigned >= 0)
            {
                var colWidth = columns[assigned].right - columns[assigned].left;
                if (sWidth > colWidth * 1.5) assigned = -1;
            }
            colAssign[si] = assigned;
        }

        // Merge same-column sections that overlap in Y or have small gap.
        // Uses multiple passes until no more merges are possible.
        var mergeChanged = true;
        while (mergeChanged)
        {
            mergeChanged = false;
            for (var i = 0; i < result.Count; i++)
            {
                if (colAssign[i] < 0) continue;
                for (var j = i + 1; j < result.Count; j++)
                {
                    if (colAssign[j] != colAssign[i]) continue;
                    var a = result[i];
                    var b = result[j];

                    // Y overlap or small gap
                    var yGap = Math.Max(a.Rectangle.LLY, b.Rectangle.LLY) - Math.Min(a.Rectangle.URY, b.Rectangle.URY);
                    // yGap > 0 means gap, yGap <= 0 means overlap
                    if (yGap > avgFontSize * 1.8) continue;

                    // Left margin similarity
                    var leftDiff = Math.Abs(a.Rectangle.LLX - b.Rectangle.LLX);
                    var ci = colAssign[i];
                    var colWidth = columns[ci].right - columns[ci].left;
                    if (leftDiff > colWidth * 0.3) continue;

                    // Merge, sorting paragraphs by Y (top-to-bottom)
                    var allP = new List<MarkupParagraph>(a.Paragraphs);
                    allP.AddRange(b.Paragraphs);
                    allP.Sort((p1, p2) => p2.Points[0].Y.CompareTo(p1.Points[0].Y));
                    var newRect = new Rectangle(
                        Math.Min(a.Rectangle.LLX, b.Rectangle.LLX),
                        Math.Min(a.Rectangle.LLY, b.Rectangle.LLY),
                        Math.Max(a.Rectangle.URX, b.Rectangle.URX),
                        Math.Max(a.Rectangle.URY, b.Rectangle.URY));
                    result[i] = new MarkupSection(newRect, allP);
                    // Update column assignment
                    var sWidth = newRect.URX - newRect.LLX;
                    if (sWidth > colWidth * 1.3) colAssign[i] = -1;

                    result.RemoveAt(j);
                    var newAssign = new int[result.Count];
                    for (var k = 0; k < result.Count; k++)
                        newAssign[k] = k < j ? colAssign[k] : colAssign[k + 1];
                    colAssign = newAssign;

                    mergeChanged = true;
                    break;
                }
                if (mergeChanged) break;
            }
        }

        // Remove small multi-column orphan sections (cross-column artifacts).
        for (var si = result.Count - 1; si >= 0; si--)
        {
            if (colAssign[si] >= 0) continue; // single-column — keep
            var s = result[si];
            var sHeight = s.Rectangle.URY - s.Rectangle.LLY;
            // Keep large multi-column sections (they might be real content)
            if (s.Paragraphs.Count > 2 || sHeight > avgFontSize * 15) continue;
            // Absorb into nearest same-Y single-column section
            var bestIdx = -1;
            var bestDist = double.MaxValue;
            for (var ti = 0; ti < result.Count; ti++)
            {
                if (ti == si || colAssign[ti] < 0) continue;
                var t = result[ti];
                var yOv = Math.Min(s.Rectangle.URY, t.Rectangle.URY) - Math.Max(s.Rectangle.LLY, t.Rectangle.LLY);
                var yG = yOv < 0 ? -yOv : 0;
                if (yG > avgFontSize * 3) continue;
                var xOv = Math.Min(s.Rectangle.URX, t.Rectangle.URX) - Math.Max(s.Rectangle.LLX, t.Rectangle.LLX);
                if (xOv <= 0) continue; // must share X area
                if (yG < bestDist) { bestDist = yG; bestIdx = ti; }
            }
            if (bestIdx >= 0)
            {
                var t = result[bestIdx];
                var allP = new List<MarkupParagraph>(t.Paragraphs);
                allP.AddRange(s.Paragraphs);
                allP.Sort((p1, p2) => p2.Points[0].Y.CompareTo(p1.Points[0].Y));
                var newRect = new Rectangle(
                    Math.Min(s.Rectangle.LLX, t.Rectangle.LLX),
                    Math.Min(s.Rectangle.LLY, t.Rectangle.LLY),
                    Math.Max(s.Rectangle.URX, t.Rectangle.URX),
                    Math.Max(s.Rectangle.URY, t.Rectangle.URY));
                result[bestIdx] = new MarkupSection(newRect, allP);
            }
            result.RemoveAt(si);
            // Shift colAssign
            var na = new int[result.Count];
            for (var k = 0; k < result.Count; k++)
                na[k] = k < si ? colAssign[k] : colAssign[k + 1];
            colAssign = na;
        }

        // Sort: top-to-bottom by LLY (descending), then left-to-right for same LLY.
        result.Sort((a, b) =>
        {
            var dy = b.Rectangle.LLY.CompareTo(a.Rectangle.LLY);
            if (dy != 0) return dy;
            return a.Rectangle.LLX.CompareTo(b.Rectangle.LLX);
        });

        return result;
    }

    /// <summary>
    /// Finds sections by spatial clustering of text lines.
    /// Algorithm:
    /// 1. Group fragments into horizontal lines
    /// 2. Sort lines top-to-bottom
    /// 3. Use union-find to cluster lines that are vertically close AND horizontally aligned
    /// 4. Horizontal alignment is determined by checking if lines share the same column region
    ///    (overlap relative to the wider line, or close left-margins)
    /// 5. Each connected component becomes a section
    /// 6. Sort sections top-to-bottom, left-to-right
    /// </summary>
    private List<MarkupSection> FindSections(List<TextFragment> fragments, Page page,
        List<(double left, double right)>? columnHints = null)
    {
        // Partition into vertical and horizontal fragments
        var verticalFrags = fragments.Where(IsVerticalFragment).ToList();
        var horizontalFrags = fragments.Where(f => !IsVerticalFragment(f)).ToList();

        // Process horizontal fragments normally
        var sections = new List<MarkupSection>();
        if (horizontalFrags.Count > 0)
        {
            sections.AddRange(FindSectionsHorizontal(horizontalFrags, page, columnHints));
        }

        // Process vertical fragments separately
        if (verticalFrags.Count > 0)
        {
            sections.AddRange(FindSectionsVertical(verticalFrags, page));
        }

        // Sort sections: top-to-bottom, then left-to-right
        var avgFs = fragments.Average(f => f.FontSize > 0 ? f.FontSize : 12);
        sections.Sort((a, b) =>
        {
            var dy = b.Rectangle.URY.CompareTo(a.Rectangle.URY);
            if (Math.Abs(a.Rectangle.URY - b.Rectangle.URY) > avgFs)
                return dy;
            return a.Rectangle.LLX.CompareTo(b.Rectangle.LLX);
        });

        return sections;
    }

    private static bool IsVerticalFragment(TextFragment f) =>
        Math.Abs(f.TextDirY) > Math.Abs(f.TextDirX) + 0.01;

    private List<MarkupSection> FindSectionsHorizontal(List<TextFragment> fragments, Page page,
        List<(double left, double right)>? columnHints = null)
    {
        // Use effective font size based on actual rendered height of fragments.
        var avgRenderedHeight = fragments
            .Where(f => f.Rectangle is not null && f.Rectangle.Height > 0)
            .Select(f => f.Rectangle!.Height)
            .DefaultIfEmpty(12)
            .Average();
        var avgFontSize = Math.Max(avgRenderedHeight, fragments.Average(f => f.FontSize > 0 ? f.FontSize : 12));

        // Group fragments into lines, sort top-to-bottom
        var lines = GroupIntoLines(fragments);
        lines.Sort((a, b) => b.MidY.CompareTo(a.MidY));

        if (lines.Count == 0) return [];

        var pageWidth = page.MediaBox?.Width ?? 612;
        var columns = columnHints;

        // Compute vertical threshold for section breaks
        double vThreshold;
        if (_options.HasVerticalOverride)
        {
            // Override controls how aggressively lines are merged vertically.
            // Small values (e.g., 0.006) mean aggressive merging (large threshold).
            var pageH = page.MediaBox?.Height ?? 792;
            vThreshold = pageH * _options.SectionUnbreakingVerticalOverride * 5;
            vThreshold = Math.Max(vThreshold, avgRenderedHeight * 3);
        }
        else
        {
            var allGaps = new List<double>();
            for (var i = 1; i < lines.Count; i++)
            {
                var hOverlap = Math.Min(lines[i - 1].MaxX, lines[i].MaxX) - Math.Max(lines[i - 1].MinX, lines[i].MinX);
                if (hOverlap > 0)
                {
                    var gap = lines[i - 1].MinY - lines[i].MaxY;
                    if (gap > 0) allGaps.Add(gap);
                }
            }

            if (allGaps.Count > 2)
            {
                allGaps.Sort();
                var medianGap = allGaps[allGaps.Count / 2];
                vThreshold = Math.Max(medianGap * 2.5, avgFontSize * 2.0);
            }
            else
            {
                vThreshold = avgFontSize * 2.0;
            }
        }

        // Horizontal alignment threshold
        var hAlignThreshold = _options.HasHorizontalOverride
            ? pageWidth * _options.SectionUnbreakingHorizontalOverride
            : avgFontSize * 1.5;

        // Union-find for section clustering
        var parent = new int[lines.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        // Merge lines that are close along the primary axis and aligned along the secondary axis
        for (var i = 0; i < lines.Count; i++)
        {
            for (var j = i + 1; j < lines.Count; j++)
            {
                var lineI = lines[i];
                var lineJ = lines[j];

                // Vertical gap (i is above j since sorted top-to-bottom)
                var vGap = lineI.MinY - lineJ.MaxY;
                if (vGap > vThreshold) break;

                // Horizontal alignment check
                var hOverlap = Math.Min(lineI.MaxX, lineJ.MaxX) - Math.Max(lineI.MinX, lineJ.MinX);
                var widthI = lineI.MaxX - lineI.MinX;
                var widthJ = lineJ.MaxX - lineJ.MinX;
                var minWidth = Math.Min(widthI, widthJ);
                var maxWidth = Math.Max(widthI, widthJ);
                var widthRatio = minWidth > 0 ? maxWidth / minWidth : 1;
                var leftMarginClose = Math.Abs(lineI.MinX - lineJ.MinX) < hAlignThreshold;
                var rightMarginClose = Math.Abs(lineI.MaxX - lineJ.MaxX) < hAlignThreshold;

                var isAligned = false;

                if (hOverlap > 0)
                {
                    var overlapRatioNarrow = minWidth > 0 ? hOverlap / minWidth : 0;

                    if (overlapRatioNarrow > 0.5 && widthRatio < 2.5)
                        isAligned = true;
                    else if (leftMarginClose && overlapRatioNarrow > 0.3)
                        isAligned = true;
                    else if (rightMarginClose && overlapRatioNarrow > 0.3)
                        isAligned = true;
                    else if (leftMarginClose && rightMarginClose)
                        isAligned = true;
                }
                else
                {
                    if (Math.Abs(lineI.MinX - lineJ.MinX) < avgFontSize * 0.5)
                        isAligned = true;
                }

                if (isAligned)
                    Union(i, j);
            }
        }

        // Group lines by section
        var sectionMap = new Dictionary<int, List<TextLine>>();
        for (var i = 0; i < lines.Count; i++)
        {
            var root = Find(i);
            if (!sectionMap.TryGetValue(root, out var list))
            {
                list = [];
                sectionMap[root] = list;
            }
            list.Add(lines[i]);
        }

        // Convert grouped lines to MarkupSections
        var sections = new List<MarkupSection>();
        foreach (var kv in sectionMap)
        {
            var sectionLines = kv.Value;
            sectionLines.Sort((a, b) => b.MidY.CompareTo(a.MidY)); // top-to-bottom
            sections.Add(BuildSection(sectionLines));
        }

        // Sort sections: top-to-bottom, then left-to-right within same vertical band
        sections.Sort((a, b) =>
        {
            var dy = b.Rectangle.URY.CompareTo(a.Rectangle.URY);
            if (Math.Abs(a.Rectangle.URY - b.Rectangle.URY) > avgFontSize)
                return dy;
            return a.Rectangle.LLX.CompareTo(b.Rectangle.LLX);
        });

        // Single-pass merge of vertically-adjacent sections when one contains a very wide
        // line (indicating text replacement created an oversized fragment). Only consecutive
        // sections with matching left margins are merged. The page-width guard prevents
        // this from affecting normal (non-replacement) section detection.
        var llxTolerance = avgFontSize * 0.8;
        for (var si = 0; si < sections.Count - 1; si++)
        {
            var above = sections[si];
            var below = sections[si + 1];

            // Only trigger when at least one section has a very wide line (replacement artifact)
            var aboveWidth = above.Rectangle.URX - above.Rectangle.LLX;
            var belowWidth = below.Rectangle.URX - below.Rectangle.LLX;
            if (aboveWidth < pageWidth * 1.5 && belowWidth < pageWidth * 1.5) continue;

            var vGap = above.Rectangle.LLY - below.Rectangle.URY;
            if (vGap > vThreshold || vGap < -avgFontSize) continue;

            if (Math.Abs(above.Rectangle.LLX - below.Rectangle.LLX) > llxTolerance) continue;

            // Merge below into above
            var allP = new List<MarkupParagraph>(above.Paragraphs);
            allP.AddRange(below.Paragraphs);
            allP.Sort((p1, p2) => p2.Points[0].Y.CompareTo(p1.Points[0].Y));
            var newRect = new Rectangle(
                Math.Min(above.Rectangle.LLX, below.Rectangle.LLX),
                Math.Min(above.Rectangle.LLY, below.Rectangle.LLY),
                Math.Max(above.Rectangle.URX, below.Rectangle.URX),
                Math.Max(above.Rectangle.URY, below.Rectangle.URY));
            sections[si] = new MarkupSection(newRect, allP);
            sections.RemoveAt(si + 1);
            si--; // re-check the merged section against the next one
        }

        return sections;
    }

    /// <summary>
    /// Find sections from vertical text fragments.
    /// 1. Group fragments into vertical columns by X proximity
    /// 2. Split each column at Y gaps into segments
    /// 3. Cluster segments into sections by X proximity + Y overlap
    /// 4. Each column within a section becomes a paragraph
    /// </summary>
    private List<MarkupSection> FindSectionsVertical(List<TextFragment> fragments, Page page)
    {
        // For vertical text, "font size" corresponds to fragment width
        var avgCharWidth = fragments
            .Where(f => f.Rectangle is not null && f.Rectangle.Width > 0)
            .Select(f => f.Rectangle!.Width)
            .DefaultIfEmpty(12)
            .Average();

        // Step 1: Group fragments by X proximity into vertical columns
        var columns = GroupIntoVerticalColumns(fragments);
        if (columns.Count == 0) return [];

        // Step 2: Split each column at large Y gaps into segments
        var segments = new List<TextLine>();
        foreach (var col in columns)
        {
            // Sort fragments by Y descending (top to bottom)
            col.Fragments.Sort((a, b) => (b.Rectangle?.URY ?? 0).CompareTo(a.Rectangle?.URY ?? 0));

            if (col.Fragments.Count <= 1)
            {
                segments.Add(col);
                continue;
            }

            // Collect Y gaps within this column
            var yGaps = new List<double>();
            for (var i = 1; i < col.Fragments.Count; i++)
            {
                var prevMinY = col.Fragments[i - 1].Rectangle?.LLY ?? 0;
                var currMaxY = col.Fragments[i].Rectangle?.URY ?? 0;
                var gap = prevMinY - currMaxY;
                if (gap > 0) yGaps.Add(gap);
            }

            // Determine Y-gap threshold for splitting
            double yThreshold;
            if (yGaps.Count > 2)
            {
                yGaps.Sort();
                var median = yGaps[yGaps.Count / 2];
                yThreshold = Math.Max(median * 3, avgCharWidth * 3);
            }
            else
            {
                yThreshold = avgCharWidth * 3;
            }

            // Split at large Y gaps
            var current = new TextLine();
            current.Fragments.Add(col.Fragments[0]);
            for (var i = 1; i < col.Fragments.Count; i++)
            {
                var prevMinY = col.Fragments[i - 1].Rectangle?.LLY ?? 0;
                var currMaxY = col.Fragments[i].Rectangle?.URY ?? 0;
                var gap = prevMinY - currMaxY;

                if (gap > yThreshold)
                {
                    current.Recalc();
                    segments.Add(current);
                    current = new TextLine();
                }
                current.Fragments.Add(col.Fragments[i]);
            }
            current.Recalc();
            segments.Add(current);
        }

        if (segments.Count == 0) return [];

        // Step 3: Cluster segments into sections using union-find
        // Segments are in the same section if they are X-close and Y-overlapping
        var parent = new int[segments.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }
        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb) parent[ra] = rb;
        }

        for (var i = 0; i < segments.Count; i++)
        {
            for (var j = i + 1; j < segments.Count; j++)
            {
                var si = segments[i];
                var sj = segments[j];

                // X proximity: segments must be close in X (within ~1 character width)
                var xGap = Math.Max(si.MinX, sj.MinX) - Math.Min(si.MaxX, sj.MaxX);
                if (xGap > avgCharWidth * 1.1) continue;

                // For adjacent vertical columns (small X gap), use lenient Y check:
                // columns belong together if their Y ranges touch or nearly touch.
                var yGap = Math.Max(si.MinY, sj.MinY) - Math.Min(si.MaxY, sj.MaxY);
                // yGap > 0 means gap, <= 0 means overlap or touching
                if (yGap <= avgCharWidth * 3)
                    Union(i, j);
            }
        }

        // Group segments by section
        var sectionMap = new Dictionary<int, List<TextLine>>();
        for (var i = 0; i < segments.Count; i++)
        {
            var root = Find(i);
            if (!sectionMap.TryGetValue(root, out var list))
            {
                list = [];
                sectionMap[root] = list;
            }
            list.Add(segments[i]);
        }

        // Step 4: Convert to MarkupSections — each segment becomes a paragraph
        var sections = new List<MarkupSection>();
        foreach (var kv in sectionMap)
        {
            var sectionSegments = kv.Value;
            sectionSegments.Sort((a, b) =>
            {
                // Sort right-to-left by X, then top-to-bottom
                var dx = b.MidX.CompareTo(a.MidX);
                return dx != 0 ? dx : b.MidY.CompareTo(a.MidY);
            });
            sections.Add(BuildSectionVertical(sectionSegments));
        }

        return sections;
    }

    /// <summary>
    /// Groups vertical text fragments into columns by X-coordinate proximity.
    /// </summary>
    private static List<TextLine> GroupIntoVerticalColumns(List<TextFragment> fragments)
    {
        var columns = new List<TextLine>();
        var sorted = fragments
            .Where(f => f.Rectangle is not null)
            .OrderByDescending(f => f.Rectangle!.LLX)
            .ThenByDescending(f => f.Rectangle!.LLY)
            .ToList();

        foreach (var frag in sorted)
        {
            if (frag.Rectangle is null) continue;
            var fragMidX = (frag.Rectangle.LLX + frag.Rectangle.URX) / 2;
            var fragWidth = frag.Rectangle.Width;
            var tolerance = Math.Max(fragWidth * 0.6, 2.0);

            var found = false;
            for (var i = columns.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(columns[i].MidX - fragMidX) <= tolerance)
                {
                    columns[i].Fragments.Add(frag);
                    columns[i].Recalc();
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var col = new TextLine();
                col.Fragments.Add(frag);
                col.Recalc();
                columns.Add(col);
            }
        }

        return columns;
    }

    /// <summary>
    /// Build a section from vertical text segments.
    /// Each segment becomes a paragraph.
    /// </summary>
    private static MarkupSection BuildSectionVertical(List<TextLine> sectionSegments)
    {
        var llx = sectionSegments.Min(l => l.MinX);
        var lly = sectionSegments.Min(l => l.MinY);
        var urx = sectionSegments.Max(l => l.MaxX);
        var ury = sectionSegments.Max(l => l.MaxY);
        var rect = new Rectangle(llx, lly, urx, ury);

        var paragraphs = new List<MarkupParagraph>();
        foreach (var seg in sectionSegments)
        {
            var sb = new StringBuilder();
            var lineFragments = new List<List<TextFragment>> { new(seg.Fragments) };

            foreach (var frag in seg.Fragments)
                sb.Append(frag.Text);

            var text = sb.ToString();
            var points = new Point[]
            {
                new(seg.MinX, seg.MinY),
                new(seg.MaxX, seg.MinY),
                new(seg.MaxX, seg.MaxY),
                new(seg.MinX, seg.MaxY),
            };

            paragraphs.Add(new MarkupParagraph(text, points, lineFragments));
        }

        return new MarkupSection(rect, paragraphs);
    }

    /// <summary>Check if two lines belong to the same detected column.</summary>
    private static bool SameColumn(TextLine a, TextLine b, List<(double left, double right)> columns)
    {
        var colA = GetColumn(a, columns);
        var colB = GetColumn(b, columns);
        if (colA == colB) return true;

        var spanA = CountSpannedColumns(a, columns);
        var spanB = CountSpannedColumns(b, columns);

        if ((spanA >= 2 && spanB == 1) || (spanB >= 2 && spanA == 1))
            return false;

        if (spanA >= 2 && spanB >= 2)
        {
            var overlap = Math.Min(a.MaxX, b.MaxX) - Math.Max(a.MinX, b.MinX);
            var minWidth = Math.Min(a.MaxX - a.MinX, b.MaxX - b.MinX);
            return minWidth > 0 && overlap / minWidth > 0.5;
        }

        return false;
    }

    private static int CountSpannedColumns(TextLine line, List<(double left, double right)> columns)
    {
        var count = 0;
        for (var i = 0; i < columns.Count; i++)
            if (line.MaxX > columns[i].left && line.MinX < columns[i].right)
                count++;
        return Math.Max(count, 1);
    }

    private static int GetColumn(TextLine line, List<(double left, double right)> columns)
    {
        var centerX = (line.MinX + line.MaxX) / 2;
        for (var i = 0; i < columns.Count; i++)
            if (centerX >= columns[i].left && centerX <= columns[i].right)
                return i;
        var minDist = double.MaxValue;
        var best = 0;
        for (var i = 0; i < columns.Count; i++)
        {
            var mid = (columns[i].left + columns[i].right) / 2;
            var dist = Math.Abs(centerX - mid);
            if (dist < minDist) { minDist = dist; best = i; }
        }
        return best;
    }

    /// <summary>Detect column boundaries from raw text fragments.</summary>
    private static List<(double left, double right)>? DetectColumnsFromFragments(
        List<TextFragment> fragments, double avgFontSize, double pageWidth)
    {
        if (fragments.Count < 10) return null;

        var sorted = fragments
            .Where(f => f.Rectangle is not null && !string.IsNullOrWhiteSpace(f.Text))
            .OrderByDescending(f => f.Rectangle!.LLY)
            .ThenBy(f => f.Rectangle!.LLX)
            .ToList();

        if (sorted.Count < 10) return null;

        var gapPositions = new List<double>();
        var rowFrags = new List<TextFragment> { sorted[0] };

        for (var i = 1; i < sorted.Count; i++)
        {
            var prevY = rowFrags[^1].Rectangle!.LLY;
            var currY = sorted[i].Rectangle!.LLY;

            if (Math.Abs(currY - prevY) > avgFontSize * 0.5)
            {
                CollectColumnGaps(rowFrags, avgFontSize, gapPositions);
                rowFrags = [sorted[i]];
            }
            else
            {
                rowFrags.Add(sorted[i]);
            }
        }
        CollectColumnGaps(rowFrags, avgFontSize, gapPositions);

        if (gapPositions.Count < 3) return null;

        gapPositions.Sort();
        var gapClusters = new List<List<double>> { new() { gapPositions[0] } };
        for (var i = 1; i < gapPositions.Count; i++)
        {
            if (gapPositions[i] - gapClusters[^1].Average() > avgFontSize * 3)
                gapClusters.Add(new List<double> { gapPositions[i] });
            else
                gapClusters[^1].Add(gapPositions[i]);
        }

        var minRecurrence = Math.Max(5, sorted.Count / 20);
        var columnBoundaries = gapClusters
            .Where(c => c.Count >= minRecurrence)
            .Select(c => c.Average())
            .OrderBy(x => x)
            .ToList();

        if (columnBoundaries.Count < 1) return null;

        var cols = new List<(double left, double right)>();
        cols.Add((0, columnBoundaries[0]));
        for (var i = 0; i < columnBoundaries.Count - 1; i++)
            cols.Add((columnBoundaries[i], columnBoundaries[i + 1]));
        cols.Add((columnBoundaries[^1], pageWidth));

        var minColWidth = pageWidth * 0.08;
        cols = cols.Where(c => c.right - c.left >= minColWidth).ToList();

        if (cols.Count >= 2)
        {
            cols[0] = (0, cols[0].right);
            for (var i = 1; i < cols.Count; i++)
            {
                var prevRight = cols[i - 1].right;
                if (cols[i].left > prevRight)
                {
                    var mid = (prevRight + cols[i].left) / 2;
                    cols[i - 1] = (cols[i - 1].left, mid);
                    cols[i] = (mid, cols[i].right);
                }
            }
            cols[^1] = (cols[^1].left, pageWidth);
        }

        return cols.Count >= 2 ? cols : null;
    }

    private static void CollectColumnGaps(List<TextFragment> rowFrags, double avgFontSize, List<double> gapPositions)
    {
        if (rowFrags.Count < 2) return;
        rowFrags.Sort((a, b) => a.Rectangle!.LLX.CompareTo(b.Rectangle!.LLX));

        for (var i = 1; i < rowFrags.Count; i++)
        {
            var prevRight = rowFrags[i - 1].Rectangle!.URX;
            var currLeft = rowFrags[i].Rectangle!.LLX;
            var gap = currLeft - prevRight;
            // Column gaps are larger than word gaps; use a lower threshold
            // to catch narrow column separators (as low as ~1× font size).
            if (gap > avgFontSize * 1.0)
                gapPositions.Add((prevRight + currLeft) / 2);
        }
    }

    /// <summary>Detect column boundaries from line positions.</summary>
    private static List<(double left, double right)>? DetectColumns(
        List<TextLine> lines, double avgFontSize, double pageWidth)
    {
        if (lines.Count < 4) return null;

        var minLineWidth = avgFontSize * 3;
        var wideLines = lines.Where(l => (l.MaxX - l.MinX) > minLineWidth).ToList();
        if (wideLines.Count < 6) return null;

        var leftEdges = wideLines.Select(l => l.MinX).OrderBy(x => x).ToList();
        var clusters = new List<List<double>> { new() { leftEdges[0] } };
        var gapThreshold = Math.Max(avgFontSize * 3, pageWidth * 0.02);

        for (var i = 1; i < leftEdges.Count; i++)
        {
            if (leftEdges[i] - clusters[^1][^1] > gapThreshold)
                clusters.Add(new List<double> { leftEdges[i] });
            else
                clusters[^1].Add(leftEdges[i]);
        }

        var minPopulation = Math.Max(3, wideLines.Count / 6);
        var significantClusters = clusters.Where(c => c.Count >= minPopulation).ToList();
        if (significantClusters.Count < 2) return null;

        var columnStarts = significantClusters.Select(c => c.Average()).OrderBy(x => x).ToList();
        var cols = new List<(double left, double right)>();
        for (var i = 0; i < columnStarts.Count; i++)
        {
            var left = i == 0 ? 0 : (columnStarts[i - 1] + columnStarts[i]) / 2;
            var right = i == columnStarts.Count - 1 ? pageWidth : (columnStarts[i] + columnStarts[i + 1]) / 2;
            cols.Add((left, right));
        }

        return cols;
    }

    private static MarkupSection BuildSection(List<TextLine> sectionLines)
    {
        sectionLines.Sort((a, b) => b.MidY.CompareTo(a.MidY)); // top-to-bottom
        var llx = sectionLines.Min(l => l.MinX);
        var lly = sectionLines.Min(l => l.MinY);
        var urx = sectionLines.Max(l => l.MaxX);
        var ury = sectionLines.Max(l => l.MaxY);
        var rect = new Rectangle(llx, lly, urx, ury);
        var paragraphs = GroupIntoParagraphs(sectionLines);
        return new MarkupSection(rect, paragraphs);
    }

    /// <summary>
    /// Groups fragments into horizontal lines based on Y-coordinate proximity.
    /// </summary>
    private static List<TextLine> GroupIntoLines(List<TextFragment> fragments)
    {
        var lines = new List<TextLine>();
        var sorted = fragments.OrderByDescending(f => GetY(f)).ThenBy(f => GetX(f)).ToList();

        foreach (var frag in sorted)
        {
            if (frag.Rectangle is null) continue;
            var fragMidY = (frag.Rectangle.LLY + frag.Rectangle.URY) / 2;
            var fragHeight = frag.Rectangle.Height;
            var tolerance = Math.Max(fragHeight * 0.5, 1.0);

            var found = false;
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                if (Math.Abs(lines[i].MidY - fragMidY) <= tolerance)
                {
                    lines[i].Fragments.Add(frag);
                    lines[i].Recalc();
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                var line = new TextLine();
                line.Fragments.Add(frag);
                line.Recalc();
                lines.Add(line);
            }
        }

        // Sort fragments within each line left-to-right
        foreach (var line in lines)
            line.Fragments.Sort((a, b) => GetX(a).CompareTo(GetX(b)));

        // Split lines with large horizontal gaps into separate lines.
        // This handles multi-column layouts where fragments at the same Y are in different columns.
        var splitLines = new List<TextLine>();
        foreach (var line in lines)
        {
            if (line.Fragments.Count <= 1)
            {
                splitLines.Add(line);
                continue;
            }

            // Detect column gaps adaptively:
            // Collect all horizontal gaps within this line, then look for a natural break point.
            // If there's a clear gap between word-level spacing and column-level spacing, use it.
            var gaps = new List<double>();
            for (var i = 1; i < line.Fragments.Count; i++)
            {
                var prev = line.Fragments[i - 1];
                var curr = line.Fragments[i];
                var prevRight = prev.Rectangle?.URX ?? GetX(prev);
                var currLeft = curr.Rectangle?.LLX ?? GetX(curr);
                var gap = currLeft - prevRight;
                if (gap > 0) gaps.Add(gap);
            }

            double gapThreshold;
            if (gaps.Count > 2)
            {
                gaps.Sort();
                var medianGap = gaps[gaps.Count / 2];
                // Column gaps should be significantly larger than word gaps
                gapThreshold = Math.Max(medianGap * 3, line.AvgFontSize * 2);
            }
            else
            {
                gapThreshold = line.AvgFontSize * 3;
            }
            var currentSplit = new TextLine();
            currentSplit.Fragments.Add(line.Fragments[0]);

            for (var i = 1; i < line.Fragments.Count; i++)
            {
                var prev = line.Fragments[i - 1];
                var curr = line.Fragments[i];
                var prevRight = prev.Rectangle?.URX ?? GetX(prev);
                var currLeft = curr.Rectangle?.LLX ?? GetX(curr);
                var gap = currLeft - prevRight;

                if (gap > gapThreshold)
                {
                    currentSplit.Recalc();
                    splitLines.Add(currentSplit);
                    currentSplit = new TextLine();
                }
                currentSplit.Fragments.Add(curr);
            }

            currentSplit.Recalc();
            splitLines.Add(currentSplit);
        }

        return splitLines;
    }

    /// <summary>
    /// Groups lines into paragraphs within a section.
    /// Lines are already sorted top-to-bottom.
    /// </summary>
    private static List<MarkupParagraph> GroupIntoParagraphs(List<TextLine> lines)
    {
        if (lines.Count == 0) return [];

        var paragraphs = new List<MarkupParagraph>();
        var currentLines = new List<TextLine> { lines[0] };

        // Compute normal line spacing for this section (using consecutive line gaps)
        var lineSpacings = new List<double>();
        for (var i = 1; i < lines.Count; i++)
        {
            var gap = lines[i - 1].MidY - lines[i].MidY;
            if (gap > 0) lineSpacings.Add(gap);
        }

        var avgFontSize = lines.Average(l => l.AvgFontSize);
        var normalSpacing = lineSpacings.Count > 0 ? Median(lineSpacings) : avgFontSize * 1.2;

        // Right margin of this section's body: the furthest right any line reaches.
        // A line ending well short of it (with room for the next line's first word)
        // is a "ragged" paragraph end rather than a normal wrap.
        var bodyRight = lines.Max(l => l.MaxX);
        var bodyWidth = bodyRight - lines.Min(l => l.MinX);

        // Paragraph break threshold: use bimodal gap detection when available.
        // Find the largest jump in the sorted gap sequence — if significant,
        // the threshold sits between normal line spacing and paragraph breaks.
        double spacingThreshold;
        if (lineSpacings.Count > 20)
        {
            var sortedGaps = lineSpacings.OrderBy(g => g).ToList();
            // Use the lower quartile as "normal spacing" (robust against outliers)
            var q1 = sortedGaps[sortedGaps.Count / 4];
            // Find the largest gap jump
            var bestJumpIdx = -1;
            var bestJumpSize = 0.0;
            for (var gi = 1; gi < sortedGaps.Count; gi++)
            {
                var jump = sortedGaps[gi] - sortedGaps[gi - 1];
                if (jump > bestJumpSize) { bestJumpSize = jump; bestJumpIdx = gi; }
            }
            // The jump must be significant: the upper gap > 1.9× the lower gap,
            // and there must be enough normal-spaced lines below the jump
            if (bestJumpIdx >= sortedGaps.Count * 2 / 3 &&
                bestJumpSize > q1 * 0.5 &&
                sortedGaps[bestJumpIdx] > sortedGaps[bestJumpIdx - 1] * 1.7)
            {
                spacingThreshold = (sortedGaps[bestJumpIdx - 1] + sortedGaps[bestJumpIdx]) / 2;
            }
            else
            {
                spacingThreshold = normalSpacing * 2.0;
            }
        }
        else
        {
            spacingThreshold = normalSpacing * 2.0;
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var curr = lines[i];

            var vGap = prev.MidY - curr.MidY;

            // Detect paragraph break: spacing significantly larger than normal
            var isSpacingBreak = lineSpacings.Count > 0 && vGap > spacingThreshold;

            // Also detect paragraph break on significant indentation change
            // (e.g., left-aligned block followed by centered or right-aligned block)
            var indentDelta = Math.Abs(curr.MinX - prev.MinX);
            var rightDelta = Math.Abs(curr.MaxX - prev.MaxX);
            var prevWidth = prev.MaxX - prev.MinX;
            var currWidth = curr.MaxX - curr.MinX;

            // Font size change combined with extra spacing indicates a new paragraph
            // (e.g., heading vs body). Only trigger if there's also some extra vertical gap.
            var fontSizeRatio = prev.AvgFontSize > 0 ? curr.AvgFontSize / prev.AvgFontSize : 1;
            var hasFontSizeChange = fontSizeRatio < 0.85 || fontSizeRatio > 1.18;
            var isFontSizeBreak = hasFontSizeChange && lineSpacings.Count > 0 && vGap > normalSpacing * 0.9;

            // Bullet/marker character at the start of the current line triggers a new paragraph.
            var isIndentBreak = false;
            if (curr.Fragments.Count > 0 && curr.Fragments[0].Text.Length > 0)
            {
                var currChar = curr.Fragments[0].Text[0];
                if ("■●•▪▸►‣".Contains(currChar))
                    isIndentBreak = true;
            }

            // Ragged short-line break: the previous line ended a full sentence well
            // short of the body's right margin, and the current line's first word would
            // have fit in that gap — so the previous line deliberately ended the
            // paragraph instead of wrapping. Three guards keep this from firing on
            // ordinary justified/wrapped prose (which was the false-positive risk):
            //   1. the gap is substantial (> ~1.5 em), not a small ragged-right jitter;
            //   2. the previous line ends with sentence-terminal punctuation (. ! ?),
            //      so a line that wrapped mid-sentence on a long name/word never breaks;
            //   3. the current line's first word would have fit on the previous line.
            var isShortLineBreak = false;
            var prevGap = bodyRight - prev.MaxX;
            // Only a MODERATE short line (ends a bit early) is a within-block paragraph
            // break. A line ending very short (> a quarter of the body width) is a
            // section-level cue — a heading or standalone line — which section detection
            // owns; treating it as a within-section paragraph split miscounts.
            if (prevGap > prev.AvgFontSize * 1.5 && prevGap < bodyWidth * 0.25)
            {
                var prevText = string.Concat(prev.Fragments.Select(f => f.Text)).TrimEnd();
                var endsSentence = prevText.Length > 0
                    && (prevText[^1] == '.' || prevText[^1] == '!' || prevText[^1] == '?');
                if (endsSentence)
                {
                    var prevChars = prev.Fragments.Sum(f => f.Text?.Length ?? 0);
                    var avgCharW = prevChars > 0 ? (prev.MaxX - prev.MinX) / prevChars : prev.AvgFontSize * 0.5;
                    var currText = string.Concat(curr.Fragments.Select(f => f.Text)).TrimStart();
                    var sp = currText.IndexOf(' ');
                    var firstWordLen = sp < 0 ? currText.Length : sp;
                    if (prevGap > firstWordLen * avgCharW + avgCharW)
                        isShortLineBreak = true;
                }
            }

            if (isSpacingBreak || isFontSizeBreak || isIndentBreak || isShortLineBreak)
            {
                paragraphs.Add(BuildParagraph(currentLines));
                currentLines = [curr];
            }
            else
            {
                currentLines.Add(curr);
            }
        }

        if (currentLines.Count > 0)
            paragraphs.Add(BuildParagraph(currentLines));

        return paragraphs;
    }

    /// <summary>Re-join a paragraph's per-line fragments into text, consulting the page's
    /// standalone space glyphs: a space is inserted between two fragments when either the
    /// plain horizontal-gap heuristic fires or a space glyph sits between them (documents
    /// that draw every inter-word space as its own overlapping run leave a near-zero gap).</summary>
    private static string AssembleTextWithSpaces(
        List<List<TextFragment>> lines, List<TextFragment> spaces)
    {
        static bool SpaceGlyphAt(List<TextFragment> spaces, Rectangle anchor, double fromX, double toX)
        {
            foreach (var s in spaces)
            {
                var r = s.Rectangle!;
                var cx = (r.LLX + r.URX) / 2;
                if (cx < fromX - 1) continue;
                if (cx > toX + r.Width) break;              // spaces sorted by LLX
                if (r.LLY < anchor.URY && r.URY > anchor.LLY) return true;
            }
            return false;
        }

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            for (var fi = 0; fi < line.Count; fi++)
            {
                var f = line[fi];
                if (fi > 0 && f.Rectangle is not null)
                {
                    var prevFrag = line[fi - 1];
                    if (prevFrag.Rectangle is not null)
                    {
                        var gap = f.Rectangle.LLX - prevFrag.Rectangle.URX;
                        var spaceGlyph = gap <= f.FontSize * 0.15
                            && SpaceGlyphAt(spaces, f.Rectangle, prevFrag.Rectangle.URX, f.Rectangle.LLX);
                        if ((gap > f.FontSize * 0.15 || spaceGlyph)
                            && !prevFrag.Text.EndsWith(" ") && !f.Text.StartsWith(" "))
                            sb.Append(' ');
                    }
                }
                sb.Append(f.Text);
            }
            // Standalone space glyphs at the end of a line are dropped (the reference
            // keeps a trailing space only when it is part of the last word's own run).
            sb.Append("\r\n");
        }
        var text = sb.ToString();
        if (text.EndsWith("\r\n"))
            text = text[..^2];
        return text;
    }

    private static MarkupParagraph BuildParagraph(List<TextLine> lines)
    {
        var sb = new StringBuilder();
        var lineFragments = new List<List<TextFragment>>();

        foreach (var line in lines)
        {
            var frags = new List<TextFragment>(line.Fragments);
            lineFragments.Add(frags);

            for (var fi = 0; fi < line.Fragments.Count; fi++)
            {
                var f = line.Fragments[fi];
                // Insert space between fragments when there's a horizontal gap
                if (fi > 0 && f.Rectangle is not null)
                {
                    var prevFrag = line.Fragments[fi - 1];
                    if (prevFrag.Rectangle is not null)
                    {
                        var gap = f.Rectangle.LLX - prevFrag.Rectangle.URX;
                        if (gap > f.FontSize * 0.15 && !prevFrag.Text.EndsWith(" ") && !f.Text.StartsWith(" "))
                            sb.Append(' ');
                    }
                }
                sb.Append(f.Text);
            }

            sb.Append("\r\n");
        }

        // Compute bounding polygon points (4 corners: LL, LR, UR, UL)
        var llx = lines.Min(l => l.MinX);
        var lly = lines.Min(l => l.MinY);
        var urx = lines.Max(l => l.MaxX);
        var ury = lines.Max(l => l.MaxY);

        var points = new Point[]
        {
            new(llx, lly), // lower-left
            new(urx, lly), // lower-right
            new(urx, ury), // upper-right
            new(llx, ury), // upper-left
        };

        var text = sb.ToString();
        if (text.EndsWith("\r\n"))
            text = text[..^2];

        return new MarkupParagraph(text, points, lineFragments);
    }

    private static double GetX(TextFragment f) =>
        f.PositionOrNull?.XIndent ?? f.Rectangle?.LLX ?? 0;

    private static double GetY(TextFragment f) =>
        f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;

    private static double Median(List<double> list)
    {
        if (list.Count == 0) return 0;
        var sorted = list.OrderBy(x => x).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[mid - 1] + sorted[mid]) / 2
            : sorted[mid];
    }

    /// <summary>
    /// Represents a horizontal line of text.
    /// </summary>
    internal sealed class TextLine
    {
        public List<TextFragment> Fragments { get; } = [];
        public double MidY { get; private set; }
        public double MidX { get; private set; }
        public double MinY { get; private set; }
        public double MaxY { get; private set; }
        public double MinX { get; private set; }
        public double MaxX { get; private set; }
        public double AvgFontSize { get; private set; }

        public void Recalc()
        {
            MinX = Fragments.Min(f => f.Rectangle?.LLX ?? GetX(f));
            MaxX = Fragments.Max(f => f.Rectangle?.URX ?? GetX(f));
            MinY = Fragments.Min(f => f.Rectangle?.LLY ?? GetY(f));
            MaxY = Fragments.Max(f => f.Rectangle?.URY ?? (GetY(f) + f.FontSize));
            MidY = (MinY + MaxY) / 2;
            MidX = (MinX + MaxX) / 2;
            // Use actual rendered height for effective font size (handles scaled fonts with FontSize=1)
            var avgHeight = Fragments
                .Where(f => f.Rectangle is not null && f.Rectangle.Height > 0)
                .Select(f => f.Rectangle!.Height)
                .DefaultIfEmpty(12)
                .Average();
            var avgFontSz = Fragments.Average(f => f.FontSize > 0 ? f.FontSize : 12);
            AvgFontSize = Math.Max(avgHeight, avgFontSz);
        }

        private static double GetX(TextFragment f) =>
            f.PositionOrNull?.XIndent ?? f.Rectangle?.LLX ?? 0;
        private static double GetY(TextFragment f) =>
            f.PositionOrNull?.YIndent ?? f.Rectangle?.LLY ?? 0;
    }
}

/// <summary>
/// Represents the text markup of a single page, organized into sections.
/// </summary>
public sealed class PageMarkup
{
    private readonly List<MarkupSection> _sections;
    private readonly ParagraphAbsorberOptions _options;
    private bool _isMulticolumn;

    internal PageMarkup(int pageNumber, List<MarkupSection> sections, ParagraphAbsorberOptions options)
    {
        Number = pageNumber;
        _sections = sections;
        _options = options;
    }

    /// <summary>The 1-based page number.</summary>
    public int Number { get; }

    /// <summary>The sections detected on this page.</summary>
    public List<MarkupSection> Sections => _sections;

    /// <summary>Every <see cref="MarkupParagraph"/> on this page, flattened across sections.</summary>
    public List<MarkupParagraph> Paragraphs
    {
        get
        {
            var all = new List<MarkupParagraph>();
            foreach (var s in _sections)
                foreach (var p in s.Paragraphs) all.Add(p);
            return all;
        }
    }

    /// <summary>Every <see cref="TextFragment"/> on this page, flattened across paragraphs.</summary>
    public List<TextFragment> TextFragments
    {
        get
        {
            var all = new List<TextFragment>();
            foreach (var s in _sections)
                foreach (var p in s.Paragraphs)
                    foreach (var f in p.Fragments) all.Add(f);
            return all;
        }
    }

    /// <summary>Bounding rectangle that contains every section on this page.</summary>
    public Rectangle Rectangle
    {
        get
        {
            double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
            double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
            bool any = false;
            foreach (var s in _sections)
            {
                if (s.Rectangle is not { } r) continue;
                if (r.LLX < minX) minX = r.LLX;
                if (r.LLY < minY) minY = r.LLY;
                if (r.URX > maxX) maxX = r.URX;
                if (r.URY > maxY) maxY = r.URY;
                any = true;
            }
            return any ? new Rectangle(minX, minY, maxX, maxY) : new Rectangle(0, 0, 0, 0);
        }
    }

    /// <summary>
    /// Gets or sets whether multicolumn paragraph detection is enabled.
    /// Changing this reprocesses paragraphs within existing sections.
    /// </summary>
    public bool IsMulticolumnParagraphsAllowed
    {
        get => _isMulticolumn;
        set
        {
            if (_isMulticolumn == value) return;
            _isMulticolumn = value;

            if (_isMulticolumn)
            {
                // Merge column sections: find side-by-side sections with overlapping Y ranges
                // and combine them into a single section with interleaved paragraphs.
                MergeColumnSections();
            }
            else
            {
                // Restore original sections
                if (_originalSections is not null)
                {
                    _sections.Clear();
                    _sections.AddRange(_originalSections);
                    _originalSections = null;
                }
            }
        }
    }

    private List<MarkupSection>? _originalSections;

    internal static Point[] MergePoints(Point[] a, Point[] b) => new Point[]
    {
        new(Math.Min(a[0].X, b[0].X), Math.Min(a[0].Y, b[0].Y)),
        new(Math.Max(a[1].X, b[1].X), Math.Min(a[1].Y, b[1].Y)),
        new(Math.Max(a[2].X, b[2].X), Math.Max(a[2].Y, b[2].Y)),
        new(Math.Min(a[3].X, b[3].X), Math.Max(a[3].Y, b[3].Y)),
    };

    private void MergeColumnSections()
    {
        if (_sections.Count < 2) return;

        // Save original sections for restoring when multicolumn is disabled
        _originalSections = new List<MarkupSection>(_sections);

        // Find pairs of sections that are side-by-side (columns)
        // Columns have overlapping Y ranges and non-overlapping X ranges
        var merged = new bool[_sections.Count];
        var result = new List<MarkupSection>();

        for (var i = 0; i < _sections.Count; i++)
        {
            if (merged[i]) continue;

            var columnGroup = new List<int> { i };

            for (var j = i + 1; j < _sections.Count; j++)
            {
                if (merged[j]) continue;

                var si = _sections[i];
                var sj = _sections[j];

                // Check Y overlap — columns must share significant vertical range
                var yOverlap = Math.Min(si.Rectangle.URY, sj.Rectangle.URY) -
                               Math.Max(si.Rectangle.LLY, sj.Rectangle.LLY);
                var minHeight = Math.Min(
                    si.Rectangle.URY - si.Rectangle.LLY,
                    sj.Rectangle.URY - sj.Rectangle.LLY);
                if (minHeight <= 0 || yOverlap / minHeight < 0.5) continue;

                // Check X non-overlap — columns must be side-by-side
                var xOverlap = Math.Min(si.Rectangle.URX, sj.Rectangle.URX) -
                               Math.Max(si.Rectangle.LLX, sj.Rectangle.LLX);
                if (xOverlap > 0) continue; // overlapping X = not columns

                columnGroup.Add(j);
                merged[j] = true;
            }

            if (columnGroup.Count == 1)
            {
                result.Add(_sections[i]);
                continue;
            }

            // Sort column group by LLX (left to right)
            columnGroup.Sort((a, b) => _sections[a].Rectangle.LLX.CompareTo(_sections[b].Rectangle.LLX));

            // Merge paragraphs from column sections:
            // 1. Tag each paragraph with its bottom-Y (for reading-order sorting)
            // 2. Merge continuation paragraphs at column boundaries (last left + first right)
            // 3. Sort all paragraphs by bottom-Y descending (reading order)
            var taggedParas = new List<(MarkupParagraph para, double bottomY)>();
            foreach (var ci in columnGroup)
                foreach (var p in _sections[ci].Paragraphs)
                    taggedParas.Add((p, p.Points[0].Y)); // Points[0] = lower-left Y

            // Merge continuations: last para of column N + first para of column N+1
            var colStartIdx = 0;
            for (var colIdx = 0; colIdx < columnGroup.Count - 1; colIdx++)
            {
                var colCount = _sections[columnGroup[colIdx]].Paragraphs.Count;
                var lastIdx = colStartIdx + colCount - 1;
                var nextIdx = lastIdx + 1;
                if (lastIdx >= 0 && lastIdx < taggedParas.Count && nextIdx < taggedParas.Count)
                {
                    var lastPara = taggedParas[lastIdx].para;
                    var firstPara = taggedParas[nextIdx].para;
                    var mergedLines = new List<List<TextFragment>>(lastPara.Lines);
                    mergedLines.AddRange(firstPara.Lines);
                    var mergedText = lastPara.Text + "\r\n" + firstPara.Text;
                    var mergedPts = MergePoints(lastPara.Points, firstPara.Points);
                    var mergedPara = new MarkupParagraph(mergedText, mergedPts, mergedLines);
                    // Keep the sort key of the left column paragraph
                    taggedParas[lastIdx] = (mergedPara, taggedParas[lastIdx].bottomY);
                    taggedParas.RemoveAt(nextIdx);
                    // Don't advance colStartIdx past the removed item
                }
                colStartIdx += colCount;
            }

            // Sort by bottom-Y descending (reading order: top of page first)
            taggedParas.Sort((a, b) => b.bottomY.CompareTo(a.bottomY));
            var mergedParas = taggedParas.Select(x => x.para).ToList();

            // Compute merged bounding rectangle
            var llx = columnGroup.Min(ci => _sections[ci].Rectangle.LLX);
            var lly = columnGroup.Min(ci => _sections[ci].Rectangle.LLY);
            var urx = columnGroup.Max(ci => _sections[ci].Rectangle.URX);
            var ury = columnGroup.Max(ci => _sections[ci].Rectangle.URY);

            result.Add(new MarkupSection(new Rectangle(llx, lly, urx, ury), mergedParas));
            merged[i] = true;
        }

        _sections.Clear();
        _sections.AddRange(result);
    }
}

/// <summary>
/// A section of text on a page — a spatially coherent group of lines.
/// </summary>
public sealed class MarkupSection
{
    private List<MarkupParagraph> _paragraphs;
    private List<MarkupParagraph>? _commonParagraphs;
    private List<MarkupParagraph>? _multicolumnParagraphs;

    internal MarkupSection(Rectangle rectangle, List<MarkupParagraph> paragraphs)
    {
        Rectangle = rectangle;
        _paragraphs = paragraphs;
        _commonParagraphs = new List<MarkupParagraph>(paragraphs);
    }

    /// <summary>The bounding rectangle of this section.</summary>
    public Rectangle Rectangle { get; internal set; }

    /// <summary>The paragraphs within this section.</summary>
    public List<MarkupParagraph> Paragraphs => _paragraphs;

    /// <summary>All TextFragments composing this section, flattened across paragraphs.</summary>
    public List<TextFragment> Fragments
    {
        get
        {
            var all = new List<TextFragment>();
            foreach (var p in _paragraphs)
                foreach (var f in p.Fragments) all.Add(f);
            return all;
        }
    }

    internal void ReplaceParagraph(int index, MarkupParagraph replacement)
    {
        _paragraphs[index] = replacement;
    }

    internal void RemoveParagraph(int index)
    {
        _paragraphs.RemoveAt(index);
    }

    internal void ReprocessParagraphs(bool multicolumn)
    {
        if (multicolumn)
        {
            if (_multicolumnParagraphs is null)
                _multicolumnParagraphs = MergeMulticolumnParagraphs(_commonParagraphs!);
            _paragraphs = _multicolumnParagraphs;
        }
        else
        {
            _paragraphs = _commonParagraphs!;
        }
    }

    private static List<MarkupParagraph> MergeMulticolumnParagraphs(List<MarkupParagraph> paragraphs)
    {
        if (paragraphs.Count <= 1) return new List<MarkupParagraph>(paragraphs);

        var result = new List<MarkupParagraph>();
        var mergeGroup = new List<MarkupParagraph> { paragraphs[0] };

        for (var i = 1; i < paragraphs.Count; i++)
        {
            var prev = paragraphs[i - 1];
            var curr = paragraphs[i];

            var prevLLX = prev.Points[0].X;
            var prevURX = prev.Points[1].X;
            var currLLX = curr.Points[0].X;
            var currURX = curr.Points[1].X;

            var hOverlap = Math.Min(prevURX, currURX) - Math.Max(prevLLX, currLLX);
            var minWidth = Math.Min(prevURX - prevLLX, currURX - currLLX);

            var isColumnCandidate = minWidth > 0 && hOverlap / minWidth < 0.3;
            var sameIndent = Math.Abs(prevLLX - currLLX) < 5;

            if (isColumnCandidate && !sameIndent)
            {
                mergeGroup.Add(curr);
            }
            else
            {
                if (mergeGroup.Count > 1)
                    result.Add(MergeParagraphGroup(mergeGroup));
                else
                    result.AddRange(mergeGroup);
                mergeGroup = [curr];
            }
        }

        if (mergeGroup.Count > 1)
            result.Add(MergeParagraphGroup(mergeGroup));
        else
            result.AddRange(mergeGroup);

        return result;
    }

    private static MarkupParagraph MergeParagraphGroup(List<MarkupParagraph> group)
    {
        var sb = new StringBuilder();
        var allLines = new List<List<TextFragment>>();
        double llx = double.MaxValue, lly = double.MaxValue;
        double urx = double.MinValue, ury = double.MinValue;

        foreach (var p in group)
        {
            if (sb.Length > 0) sb.Append("\r\n");
            sb.Append(p.Text);
            allLines.AddRange(p.Lines);

            llx = Math.Min(llx, p.Points[0].X);
            lly = Math.Min(lly, p.Points[0].Y);
            urx = Math.Max(urx, p.Points[2].X);
            ury = Math.Max(ury, p.Points[2].Y);
        }

        var points = new Point[]
        {
            new(llx, lly),
            new(urx, lly),
            new(urx, ury),
            new(llx, ury),
        };

        return new MarkupParagraph(sb.ToString(), points, allLines);
    }
}

/// <summary>
/// A paragraph within a markup section.
/// </summary>
public sealed class MarkupParagraph
{
    private readonly List<List<TextFragment>> _lines;

    internal MarkupParagraph(string text, Point[] points, List<List<TextFragment>> lines)
    {
        _text = text; // set backing field directly to avoid triggering fragment replacement
        Points = points;
        _lines = lines;
    }

    /// <summary>Replace the cached text without touching the underlying fragments
    /// (used by the absorber's space-glyph reassembly pass).</summary>
    internal void RefreshText(string text) => _text = text;

    /// <summary>
    /// The text of this paragraph. Lines are separated by \r\n.
    /// Setting this replaces the text in the underlying fragments via TextReplacer.
    /// </summary>
    public string Text
    {
        get => _text;
        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
                return;

            // Replace text in the underlying fragments.
            // Strategy: put the full new text into the first fragment,
            // clear all subsequent fragments.
            var first = true;
            foreach (var line in _lines)
            {
                foreach (var fragment in line)
                {
                    if (first)
                    {
                        fragment.Text = value;
                        first = false;
                    }
                    else
                    {
                        fragment.Text = "";
                    }
                }
            }

            _text = value;
        }
    }
    private string _text;

    /// <summary>
    /// The bounding polygon points: [LL, LR, UR, UL].
    /// </summary>
    public Point[] Points { get; }

    /// <summary>
    /// The lines of text fragments that compose this paragraph.
    /// </summary>
    public List<List<TextFragment>> Lines => _lines;

    /// <summary>
    /// Secondary points for cross-page paragraph continuation.
    /// </summary>
    public List<Point[]> SecondaryPoints { get; internal set; } = new();

    /// <summary>
    /// Page numbers for cross-page paragraph continuation.
    /// </summary>
    public List<int> ContinuationPageNumbers { get; internal set; } = new();

    /// <summary>All TextFragments composing this paragraph, flattened across lines.</summary>
    public List<TextFragment> Fragments
    {
        get
        {
            var all = new List<TextFragment>();
            foreach (var line in _lines)
                foreach (var f in line) all.Add(f);
            return all;
        }
    }
}
