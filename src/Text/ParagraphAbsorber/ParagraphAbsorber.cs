using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Absorbs text from PDF pages and organizes it into sections and paragraphs.
/// </summary>
public sealed partial class ParagraphAbsorber
{
    private static readonly bool GridDebug =
        Environment.GetEnvironmentVariable("ASPOSE_FOSS_GRIDDEBUG") == "1";

    private ParagraphAbsorberOptions _options;
    private readonly List<PageMarkup> _pageMarkups = [];

    /// <summary>Creates a paragraph absorber with default options.</summary>
    public ParagraphAbsorber() : this(new ParagraphAbsorberOptions()) { }

    /// <summary>Creates a paragraph absorber with the given options; <c>null</c> uses default options.</summary>
    public ParagraphAbsorber(ParagraphAbsorberOptions paragraphAbsorberOptions)
    {
        _options = paragraphAbsorberOptions ?? new ParagraphAbsorberOptions();
    }

    /// <summary>Creates a paragraph absorber with default options and the given <c>SectionsSearchDepth</c> (stored only).</summary>
    public ParagraphAbsorber(int sectionsSearchDepth)
        : this(new ParagraphAbsorberOptions())
    {
        SectionsSearchDepth = sectionsSearchDepth;
    }

    /// <summary>Creates a paragraph absorber with the given options (<c>null</c> uses defaults) and <c>SectionsSearchDepth</c> (stored only).</summary>
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
    /// Gets or sets whether paragraphs may continue across columns and pages. Applies
    /// to every page markup (present and future): each page joins its column
    /// continuations, and the last column of a page flows onto the first column of
    /// the page visited next.
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
                for (var pi = 0; pi + 1 < _pageMarkups.Count; pi++)
                    PageMarkup.JoinAcrossPages(_pageMarkups[pi], _pageMarkups[pi + 1]);
        }
    }
    private bool _isMulticolumn;

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
        if (_isMulticolumn)
        {
            markup.IsMulticolumnParagraphsAllowed = true;
            if (_pageMarkups.Count >= 2)
                PageMarkup.JoinAcrossPages(_pageMarkups[^2], markup);
        }
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

        var mbW = page.MediaBox?.Width ?? 612;
        var mbH = page.MediaBox?.Height ?? 792;
        var sections = FindSections(validFragments, mbW, mbH);

        // Re-assemble paragraph text with the standalone space glyphs folded back in.
        if (spaceFragments.Count > 0)
            foreach (var section in sections)
                foreach (var para in section.Paragraphs)
                    para.RefreshText(AssembleTextWithSpaces(para.Lines, spaceFragments));

        // A rotated page reports its markup in DISPLAY space: content (x, y)
        // maps to (W - y, x) for /Rotate 90 (W = the MediaBox width, not
        // the displayed height - a section at content y 387 lands at x 225 on a
        // 612-wide page), and by the same turn (y, H - x) for 270, (W - x, H - y)
        // for 180. Fragment rectangles stay in content space.
        var rotate = ((page.RotateDegrees % 360) + 360) % 360;
        var display = rotate switch
        {
            90 => new Frame(0, 1, -1, 0, mbW, 0),
            180 => new Frame(-1, 0, 0, -1, mbW, mbH),
            270 => new Frame(0, -1, 1, 0, 0, mbH),
            _ => Frame.Identity,
        };
        if (rotate != 0)
            foreach (var section in sections)
                MapSection(section, display);

        // Sections are ordered by their BOTTOM edge, top-to-bottom (LLY
        // descending), ties left-to-right - in the reported (display) space.
        sections.Sort((x, y) =>
        {
            var dy = y.Rectangle.LLY.CompareTo(x.Rectangle.LLY);
            return dy != 0 ? dy : x.Rectangle.LLX.CompareTo(y.Rectangle.LLX);
        });

        // A section rectangle is reported on the integer point grid: the union of
        // its line boxes rounded OUTWARD (floor of the lower-left, ceiling of the
        // upper-right) - lines spanning [29.75 1265.41 146.28 1277.02] are
        // reported as [29 1265 147 1278].
        foreach (var section in sections)
            section.Rectangle = RoundOutward(section.Rectangle);

        return new PageMarkup(page.Number, sections, _options);
    }

    // The upper edge is floor + 1, not ceiling: an edge that lands exactly on the
    // grid (a line ending at content y 249 on a rotated page, x' = 612 - 249 = 363)
    // is reported as 364.
    private static Rectangle RoundOutward(Rectangle r) =>
        new(Math.Floor(Snap(r.LLX)), Math.Floor(Snap(r.LLY)), Math.Floor(Snap(r.URX)) + 1, Math.Floor(Snap(r.URY)) + 1);

    /// <summary>Accumulated advances leave an edge a few 1e-7 off the grid value the
    /// reference lands on exactly (362.9999991 for its 363); snap before flooring.</summary>
    private static double Snap(double v) => Math.Round(v, 4);

    /// <summary>An affine map of page geometry: (x, y) -> (A x + C y + E, B x + D y + F).</summary>
    private readonly record struct Frame(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Frame Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>The frame in which text running along the unit direction
        /// (<paramref name="dx"/>, <paramref name="dy"/>) reads left to right with
        /// its ascent pointing up: X = d . p, Y = u . p with u = d turned a quarter
        /// turn counter-clockwise.</summary>
        public static Frame ForDirection(double dx, double dy) => new(dx, -dy, dy, dx, 0, 0);

        public Point Map(double x, double y) => new(A * x + C * y + E, B * x + D * y + F);

        public Rectangle MapRect(Rectangle r)
        {
            var p1 = Map(r.LLX, r.LLY);
            var p2 = Map(r.URX, r.LLY);
            var p3 = Map(r.URX, r.URY);
            var p4 = Map(r.LLX, r.URY);
            return new Rectangle(
                Math.Min(Math.Min(p1.X, p2.X), Math.Min(p3.X, p4.X)),
                Math.Min(Math.Min(p1.Y, p2.Y), Math.Min(p3.Y, p4.Y)),
                Math.Max(Math.Max(p1.X, p2.X), Math.Max(p3.X, p4.X)),
                Math.Max(Math.Max(p1.Y, p2.Y), Math.Max(p3.Y, p4.Y)));
        }

        public Frame Inverse()
        {
            var det = A * D - B * C;
            double ia = D / det, ib = -B / det, ic = -C / det, id = A / det;
            return new Frame(ia, ib, ic, id, -(ia * E + ic * F), -(ib * E + id * F));
        }
    }

    /// <summary>Map a section's rectangle and its paragraphs' polygons through
    /// <paramref name="frame"/>; the point ORDER is kept (a vertical text
    /// polygon starts at the content-space lower-right corner because that is
    /// where the text frame's lower-left corner lands).</summary>
    private static void MapSection(MarkupSection section, Frame frame)
    {
        section.Rectangle = frame.MapRect(section.Rectangle);
        foreach (var para in section.Paragraphs)
            for (var pi = 0; pi < para.Points.Length; pi++)
                para.Points[pi] = frame.Map(para.Points[pi].X, para.Points[pi].Y);
    }

    private List<MarkupSection> FindSections(List<TextFragment> fragments, double pageW, double pageH)
    {
        // Horizontal text runs the raster model as is. Vertical text runs the SAME
        // model in the frame X = y, Y = -x (text reading up the page, ascent toward
        // the left) - that one frame serves vertical text whichever way it
        // reads, which is why vertical paragraph polygons start at the
        // content lower-right corner - and maps the sections back afterwards. The
        // fragments' own geometry is swapped into the frame for the duration and
        // restored, so absorbed fragments keep their page-space rectangles.
        var horizontalFrags = fragments.Where(f => !IsVerticalFragment(f)).ToList();
        var verticalFrags = fragments.Where(IsVerticalFragment).ToList();

        var sections = new List<MarkupSection>();
        if (horizontalFrags.Count > 0)
            sections.AddRange(FindSectionsHorizontal(horizontalFrags, pageW, pageH));

        if (verticalFrags.Count > 0)
        {
            var frame = Frame.ForDirection(0, 1);
            var saved = new List<(TextFragment f, Rectangle? rect, Position? pos)>(verticalFrags.Count);
            try
            {
                foreach (var f in verticalFrags)
                {
                    var mapped = frame.MapRect(f.Rectangle!);
                    var (rect, pos) = f.SwapGeometry(mapped, new Position(mapped.LLX, mapped.LLY));
                    saved.Add((f, rect, pos));
                }
                var found = FindSectionsHorizontal(verticalFrags, pageH, pageW);
                var back = frame.Inverse();
                foreach (var section in found) MapSection(section, back);
                sections.AddRange(found);
            }
            finally
            {
                foreach (var (f, rect, pos) in saved) f.SwapGeometry(rect, pos);
            }
        }

        return sections;
    }

    private static bool IsVerticalFragment(TextFragment f) =>
        Math.Abs(f.TextDirY) > Math.Abs(f.TextDirX) + 0.01;

    private List<MarkupSection> FindSectionsHorizontal(List<TextFragment> fragments, double pageW, double pageH)
    {
        var sh = new SectionSplitState();
        sh.fragments = fragments;
        sh.pageW = pageW;
        sh.pageH = pageH;
        sh.vOv = _options.HasVerticalOverride ? _options.SectionUnbreakingVerticalOverride : 0.005;
        sh.hOv = _options.HasHorizontalOverride ? _options.SectionUnbreakingHorizontalOverride : 0.005;
        sh.vRun = (int)Math.Round(sh.pageH * sh.vOv, MidpointRounding.ToEven) + 2;

        sh.avgFontSize = sh.fragments.Average(f => f.FontSize > 0 ? f.FontSize : 12);
        sh.pageBodyRight = sh.fragments.Max(f => f.Rectangle?.URX ?? 0);

        sh.sections = new List<MarkupSection>();

        // Recursive raster splitter: rows first, then columns per band; a region
        // that split in either direction is re-examined (a 3-column page needs
        // per-column row splits that page-wide rows can't see — text in the other
        // columns masks the gaps).
        SplitRegion(sh, sh.fragments, byRows: true, 0);
        return sh.sections;
    }

    /// <summary>Reading order for lines: top to bottom, and among the halves of one
    /// physical line split at a column gap, left to right - a STABLE order (a plain
    /// MidY sort leaves equal-MidY halves in arbitrary order, which changes which
    /// paragraph an indented right-hand half opens).</summary>
    private static int TopToBottomThenLeft(TextLine a, TextLine b)
    {
        // Halves of one physical line can differ in MidY by floating-point noise:
        // compare on a half-point grid (a consistent key, unlike a tolerance).
        var dy = Math.Round(b.MidY * 2).CompareTo(Math.Round(a.MidY * 2));
        return dy != 0 ? dy : a.MinX.CompareTo(b.MinX);
    }

    private static MarkupSection BuildSection(List<TextLine> sectionLines, double pageBodyRight = double.NaN)
    {
        sectionLines.Sort(TopToBottomThenLeft); // top-to-bottom
        var llx = sectionLines.Min(l => l.MinX);
        var lly = sectionLines.Min(l => l.MinY);
        var urx = sectionLines.Max(l => l.MaxX);
        var ury = sectionLines.Max(l => l.MaxY);
        var rect = new Rectangle(llx, lly, urx, ury);
        var paragraphs = GroupIntoParagraphs(sectionLines, pageBodyRight);
        return new MarkupSection(rect, paragraphs);
    }

}
