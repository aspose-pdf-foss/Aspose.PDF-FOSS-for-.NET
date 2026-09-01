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

    /// <summary>Painted vector drawings of the page as pseudo image placements (one per
    /// spatial cluster), each carrying serialized SVG markup. Walks the content operators
    /// with the same q/Q/cm tracking as the grid detector, but keeps every PAINTED path's
    /// bounding box (fills and strokes alike); the page background (a paint covering most
    /// of the page) and anything inside a detected table region are dropped, and nearby
    /// paths merge into one drawing.</summary>
    private static List<ImgPlace> CollectVectorGraphics(Page page, List<Rectangle> tableRegions)
    {
        var result = new List<ImgPlace>();
        List<(Rectangle box, string elem)> paints;
        try
        {
            paints = CollectPaintedPaths(page);
        }
        catch
        {
            return result;
        }

        var pageRect = page.GetPageRect(true);
        var pageArea = Math.Max(1.0, pageRect.Width * pageRect.Height);
        paints = paints.Where(p =>
                p.box.Width * p.box.Height < pageArea * 0.6
                && !InAnyRegion(p.box, tableRegions))
            .ToList();
        if (paints.Count == 0) return result;

        // Cluster paints whose boxes (with a small slack) touch.
        var parent = new int[paints.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        for (var i = 0; i < paints.Count; i++)
            for (var j = i + 1; j < paints.Count; j++)
            {
                var a = paints[i].box; var b = paints[j].box;
                if (a.LLX - SegGroupSlack <= b.URX && b.LLX - SegGroupSlack <= a.URX
                    && a.LLY - SegGroupSlack <= b.URY && b.LLY - SegGroupSlack <= a.URY)
                { var ra = Find(i); var rb = Find(j); if (ra != rb) parent[ra] = rb; }
            }

        foreach (var group in Enumerable.Range(0, paints.Count).GroupBy(Find))
        {
            var members = group.Select(i => paints[i]).ToList();
            var box = new Rectangle(
                members.Min(m => m.box.LLX), members.Min(m => m.box.LLY),
                members.Max(m => m.box.URX), members.Max(m => m.box.URY));
            if (box.Width < 1 && box.Height < 1) continue;
            var w = F(box.Width); var h = F(box.Height);
            var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\""
                + $" width=\"{w}\" height=\"{h}\" viewBox=\"0 0 {w} {h}\">"
                + string.Concat(members.Select(m => m.elem)) + "</svg>";
            result.Add(new ImgPlace(box, svg));
        }
        return result;
    }

    /// <summary>The text line whose band contains a vector drawing's centre, or null when
    /// the drawing floats between lines (then it stands alone as a block).</summary>
    private static Line VectorInlineHost(List<Line> textLines, Rectangle rect)
    {
        var cy = (rect.LLY + rect.URY) / 2;
        Line best = null;
        var bestDist = double.MaxValue;
        foreach (var l in textLines)
        {
            if (l.CharCount == 0) continue;
            if (cy < l.TopY || cy > l.TopY + l.FontSize) continue;
            var d = Math.Abs(l.TopY + l.FontSize / 2 - cy);
            if (d < bestDist) { bestDist = d; best = l; }
        }
        return best;
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

    private sealed class ImgPlace
    {
        public readonly Rectangle Rect;
        public readonly ImagePlacement Placement;   // null for a vector-graphics cluster
        public readonly string VectorSvg;            // svg markup for a vector cluster
        public string Token;                         // assigned in reading-order numbering
        public ImgPlace(ImagePlacement p) { Rect = p.Rectangle; Placement = p; }
        public ImgPlace(Rectangle rect, string svg) { Rect = rect; VectorSvg = svg; }
        public bool IsVector => Placement == null;
    }

    private sealed class ImageNumberer
    {
        private readonly string _outputDir;
        private readonly string _resourceDir;
        private readonly bool _htmlTags;
        private readonly Dictionary<string, int> _numbers = new(StringComparer.Ordinal);
        private int _next = 1;

        public ImageNumberer(string outputDir, string resourceDir, bool htmlTags)
        {
            _outputDir = outputDir;
            _resourceDir = string.IsNullOrEmpty(resourceDir) ? "resources" : resourceDir;
            _htmlTags = htmlTags;
        }

        public string Token(ImgPlace place)
        {
            if (place.IsVector)
            {
                // Every vector cluster is its own drawing — no cross-placement dedupe.
                var vnum = _next++;
                SaveVector(place.VectorSvg, vnum);
                return $"![]({_resourceDir}/image_{vnum}.svg)";
            }

            var placement = place.Placement;
            var img = placement.Image;
            // A markdown `![]()` token carries no size, so every distinct drawn size gets
            // its own numbered file; an HTML `<img>` tag states its own width/height, so
            // the number follows the underlying image XObject and sizes stay per-placement.
            var key = _htmlTags
                ? img?.Name ?? string.Empty
                : $"{img?.Name}|{Math.Round(placement.Rectangle.Width)}x{Math.Round(placement.Rectangle.Height)}";
            if (!_numbers.TryGetValue(key, out var num))
            {
                num = _next++;
                _numbers[key] = num;
                SaveImage(placement, num);
            }
            if (_htmlTags)
            {
                // The tag's width/height are CSS pixels (96 per inch), not points.
                const double CssPxPerPt = 96.0 / 72.0;
                return $"<img src=\"{_resourceDir}/image_{num}.png\""
                    + $" width=\"{Math.Floor(placement.Rectangle.Width * CssPxPerPt)}\""
                    + $" height=\"{Math.Floor(placement.Rectangle.Height * CssPxPerPt)}\""
                    + " style=\"margin-right: 5px;\"/>";
            }
            return $"![]({_resourceDir}/image_{num}.png)";
        }

        private void SaveVector(string svg, int num)
        {
            if (string.IsNullOrEmpty(_outputDir) || string.IsNullOrEmpty(svg))
                return;
            try
            {
                var dir = Path.Combine(_outputDir, _resourceDir);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"image_{num}.svg"), svg,
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
                // Best-effort: a save failure still leaves the reference in the markdown.
            }
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
        List<LinkInfo> links, Rectangle area, out List<Rectangle> regions)
    {
        regions = new List<Rectangle>();
        var result = new List<MdBlock>();

        List<GridInfo> grids;
        try
        {
            grids = DetectGrids(page);
        }
        catch
        {
            return result;
        }

        foreach (var grid in grids)
        {
            if (area != null && grid.Bounds.Intersect(area).IsEmpty)
                continue;
            var text = RenderGrid(grid, pageFrags, links);
            if (text == null)
                continue;
            regions.Add(grid.Bounds);
            result.Add(new MdBlock(text, true, grid.Bounds.URY));
        }
        return result;
    }

    private sealed class GridInfo
    {
        public List<double> Xs;      // vertical-rule positions, ascending
        public List<double> YsDesc;  // horizontal-rule positions, descending
        public Rectangle Bounds;
    }

    private readonly struct Seg
    {
        public readonly double X0, Y0, X1, Y1;
        public Seg(double x0, double y0, double x1, double y1)
        {
            X0 = Math.Min(x0, x1); Y0 = Math.Min(y0, y1);
            X1 = Math.Max(x0, x1); Y1 = Math.Max(y0, y1);
        }
        public bool Vertical => X1 - X0 < 0.7;
        public bool Horizontal => Y1 - Y0 < 0.7;
        public double Length => Math.Max(X1 - X0, Y1 - Y0);
    }

    // A rule must run most of the way across its cluster to be a grid line; an
    // underline under one link inside a cell falls far short of this.
    private const double RuleSpanRatio = 0.55;

    private const double RuleClusterTol = 1.5;   // rules this close merge into one position

    private const double SegGroupSlack = 4.0;    // bbox slack when clustering segments

    private const double MinRuleLength = 4.0;

    private static List<GridInfo> DetectGrids(Page page)
    {
        var segs = CollectStrokedSegments(page);
        var grids = new List<GridInfo>();
        if (segs.Count == 0) return grids;

        // Union-find on segment bboxes expanded by a small slack.
        var parent = new int[segs.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
        for (var i = 0; i < segs.Count; i++)
            for (var j = i + 1; j < segs.Count; j++)
            {
                var a = segs[i]; var b = segs[j];
                if (a.X0 - SegGroupSlack <= b.X1 && b.X0 - SegGroupSlack <= a.X1
                    && a.Y0 - SegGroupSlack <= b.Y1 && b.Y0 - SegGroupSlack <= a.Y1)
                    Union(i, j);
            }

        foreach (var group in Enumerable.Range(0, segs.Count).GroupBy(Find))
        {
            var members = group.Select(i => segs[i]).ToList();
            var minX = members.Min(s => s.X0);
            var maxX = members.Max(s => s.X1);
            var minY = members.Min(s => s.Y0);
            var maxY = members.Max(s => s.Y1);
            var w = maxX - minX;
            var h = maxY - minY;

            var xs = ClusterPositions(members
                .Where(s => s.Vertical && s.Length >= h * RuleSpanRatio)
                .Select(s => (s.X0 + s.X1) / 2));
            var ys = ClusterPositions(members
                .Where(s => s.Horizontal && s.Length >= w * RuleSpanRatio)
                .Select(s => (s.Y0 + s.Y1) / 2));
            if (xs.Count < 3 || ys.Count < 3)
                continue;

            grids.Add(new GridInfo
            {
                Xs = xs,
                YsDesc = ys.AsEnumerable().Reverse().ToList(),
                Bounds = new Rectangle(xs[0], ys[0], xs[xs.Count - 1], ys[ys.Count - 1]),
            });
        }
        return grids.OrderByDescending(g => g.Bounds.URY).ToList();
    }

    private static List<double> ClusterPositions(IEnumerable<double> values)
    {
        var result = new List<double>();
        foreach (var v in values.OrderBy(v => v))
        {
            if (result.Count > 0 && v - result[result.Count - 1] <= RuleClusterTol)
                result[result.Count - 1] = (result[result.Count - 1] + v) / 2;
            else
                result.Add(v);
        }
        return result;
    }

    /// <summary>Axis-aligned line segments the page actually STROKES, in page space:
    /// walks the content operators tracking q/Q/cm and the current path, keeps the
    /// m/l runs and rectangle edges painted by a stroking operator, and drops filled
    /// or clipped-away paths (a table frame is stroked; text highlights are fills).</summary>
    private static List<Seg> CollectStrokedSegments(Page page)
    {
        var segs = new List<Seg>();
        var ctm = new double[] { 1, 0, 0, 1, 0, 0 };
        var stack = new Stack<double[]>();
        var path = new List<Seg>();
        double curX = 0, curY = 0;
        var haveCur = false;

        (double x, double y) Apply(double x, double y)
            => (ctm[0] * x + ctm[2] * y + ctm[4], ctm[1] * x + ctm[3] * y + ctm[5]);

        foreach (var raw in page.Contents.PeekOps())
        {
            var s = raw.Trim();
            var sp = s.LastIndexOf(' ');
            var name = sp < 0 ? s : s.Substring(sp + 1);
            switch (name)
            {
                case "q":
                    stack.Push((double[])ctm.Clone());
                    break;
                case "Q":
                    if (stack.Count > 0) ctm = stack.Pop();
                    break;
                case "cm":
                {
                    var p = Operands(s, 6);
                    if (p == null) break;
                    ctm = new[]
                    {
                        p[0] * ctm[0] + p[1] * ctm[2],
                        p[0] * ctm[1] + p[1] * ctm[3],
                        p[2] * ctm[0] + p[3] * ctm[2],
                        p[2] * ctm[1] + p[3] * ctm[3],
                        p[4] * ctm[0] + p[5] * ctm[2] + ctm[4],
                        p[4] * ctm[1] + p[5] * ctm[3] + ctm[5],
                    };
                    break;
                }
                case "m":
                {
                    var p = Operands(s, 2);
                    if (p == null) break;
                    (curX, curY) = Apply(p[0], p[1]);
                    haveCur = true;
                    break;
                }
                case "l":
                {
                    var p = Operands(s, 2);
                    if (p == null) break;
                    var (nx, ny) = Apply(p[0], p[1]);
                    if (haveCur) path.Add(new Seg(curX, curY, nx, ny));
                    curX = nx; curY = ny; haveCur = true;
                    break;
                }
                case "re":
                {
                    var p = Operands(s, 4);
                    if (p == null) break;
                    var (x0, y0) = Apply(p[0], p[1]);
                    var (x1, y1) = Apply(p[0] + p[2], p[1] + p[3]);
                    path.Add(new Seg(x0, y0, x1, y0));
                    path.Add(new Seg(x0, y1, x1, y1));
                    path.Add(new Seg(x0, y0, x0, y1));
                    path.Add(new Seg(x1, y0, x1, y1));
                    haveCur = false;
                    break;
                }
                case "c": case "v": case "y":
                    // A curve breaks the straight run; its endpoint still moves the pen.
                    haveCur = false;
                    break;
                case "S": case "s": case "B": case "b": case "B*": case "b*":
                    foreach (var seg in path)
                        if ((seg.Vertical || seg.Horizontal) && seg.Length >= MinRuleLength)
                            segs.Add(seg);
                    path.Clear();
                    haveCur = false;
                    break;
                case "f": case "F": case "f*": case "n":
                    path.Clear();
                    haveCur = false;
                    break;
            }
        }
        return segs;
    }

    private static string F(double v)
        => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static double[] Operands(string op, int count)
    {
        var parts = op.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < count + 1) return null;
        var result = new double[count];
        for (var i = 0; i < count; i++)
        {
            if (!double.TryParse(parts[parts.Length - 1 - count + i],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out result[i]))
                return null;
        }
        return result;
    }

    private static string RenderGrid(GridInfo grid, List<TextFragment> pageFrags, List<LinkInfo> links)
    {
        var xs = grid.Xs;
        var ys = grid.YsDesc;
        var rows = ys.Count - 1;
        var cols = xs.Count - 1;

        var cellText = new string[rows][];
        var cellPlain = new string[rows][];
        var any = false;
        for (var r = 0; r < rows; r++)
        {
            cellText[r] = new string[cols];
            cellPlain[r] = new string[cols];
            for (var c = 0; c < cols; c++)
            {
                var frags = pageFrags.Where(f =>
                {
                    var cx = (f.Rectangle.LLX + f.Rectangle.URX) / 2;
                    var cy = (f.Rectangle.LLY + f.Rectangle.URY) / 2;
                    return cx >= xs[c] - 0.5 && cx <= xs[c + 1] + 0.5
                        && cy >= ys[r + 1] - 0.5 && cy <= ys[r] + 0.5;
                }).ToList();
                var lines = GroupLines(frags, links).Where(l => l.CharCount > 0).ToList();
                // Each VISUAL line of a cell keeps its own text; the joins become
                // explicit <br/> breaks inside the markdown cell.
                cellText[r][c] = string.Join(" <br/> ",
                    lines.Select(l => RenderParagraph(new List<Line> { l }).Trim()));
                cellPlain[r][c] = string.Join(" <br/> ", lines.Select(l => l.Text.Trim()));
                if (cellText[r][c].Length > 0) any = true;
            }
        }
        if (!any) return null;

        // Trailing empty cells are dropped per row (a row emits only up to its last
        // non-empty cell — a short final row shows fewer columns).
        int RowCols(int r)
        {
            var last = -1;
            for (var c = 0; c < cols; c++)
                if (cellText[r][c].Length > 0) last = c;
            return last + 1;
        }

        var sb = new StringBuilder();
        void Row(int r)
        {
            sb.Append('|');
            var n = RowCols(r);
            for (var c = 0; c < n; c++)
                sb.Append(' ').Append(cellText[r][c]).Append(" |");
            sb.Append(NewLine);
        }

        // The dash-separator run matches each header cell's PLAIN text width (emphasis
        // markers and link targets do not count).
        Row(0);
        sb.Append('|');
        var headerCols = RowCols(0);
        for (var c = 0; c < headerCols; c++)
            sb.Append(' ').Append(new string('-', Math.Max(cellPlain[0][c].Length, 3))).Append(" |");
        sb.Append(NewLine);
        for (var r = 1; r < rows; r++)
            Row(r);

        var text = sb.ToString();
        return text.EndsWith(NewLine) ? text.Substring(0, text.Length - NewLine.Length) : text;
    }
}
