using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

/// <summary>What one content-stream operation does, as far as tagging is concerned.</summary>
internal enum ContentOpKind
{
    /// <summary>State, text-positioning and other operators that paint nothing.</summary>
    Other,
    /// <summary>Tj, TJ, ' or ": paints text.</summary>
    TextShow,
    /// <summary>Do of an image XObject.</summary>
    Image,
    /// <summary>Do of a form XObject (or of an XObject that cannot be resolved).</summary>
    Form,
    /// <summary>An inline image (BI … EI).</summary>
    InlineImage,
    /// <summary>Path construction: m, l, c, v, y, h, re.</summary>
    PathBuild,
    /// <summary>Path painting: S, s, f, F, f*, B, B*, b, b*.</summary>
    PathPaint,
    /// <summary>n: ends a path without painting it (a clip or a discarded path).</summary>
    PathEnd,
    /// <summary>W or W*: marks the current path as a clip.</summary>
    Clip,
    /// <summary>sh: paints a shading.</summary>
    Shading,
    BeginText,
    EndText,
    /// <summary>BMC or BDC already in the source.</summary>
    BeginMark,
    /// <summary>EMC already in the source.</summary>
    EndMark,
}

/// <summary>One operation of a page's content stream: its byte span, what it does and,
/// for painting operations, where on the page it paints (on the page as it is shown, see <see cref="PageContentScan.ShownMatrix"/>).</summary>
internal sealed class ContentOp
{
    public int Start;
    public int End;
    public string Name = string.Empty;
    public ContentOpKind Kind;
    /// <summary>Inside a BT … ET text object (BT itself is outside, ET inside).</summary>
    public bool InText;

    // Text shows: the baseline start and end, the effective size and the decoded text.
    public double X0, X1, Y, Size;
    public string Text = string.Empty;

    // Images and forms: the unit square mapped through the CTM.
    public double Llx, Lly, Urx, Ury;

    /// <summary>For BeginMark: the tag, and whether its properties carry an MCID (a mark of
    /// an existing structure tree, which retagging replaces).</summary>
    public string? MarkTag;
    public bool MarkHasMcid;
    /// <summary>For BeginMark: the MCID it carries (-1: none) and its property list.</summary>
    public int MarkMcid = -1;
    public PdfDictionary? MarkProps;
    /// <summary>For BeginMark: the mark (and its EMC) is dropped - a producer's artifact holding a
    /// picture the tagger made a Figure.</summary>
    public bool MarkDropped;

    /// <summary>The content stream the operation is in: 0 for the page's own, else the index
    /// (from 1) of a form XObject looked into, in the order the page's work lists them.</summary>
    public int Stream;
    /// <summary>For Do of a form XObject the page names by reference: the form, the reference,
    /// and the matrix from the form's space to the shown page at the Do (its own /Matrix not
    /// yet applied).</summary>
    public PdfStream? Form;
    public PdfObject? FormRef;
    public double[]? FormCtm;
    /// <summary>For Do of a form XObject: its content is marked inside the form, so the Do is not.</summary>
    public bool Expanded;
    /// <summary>For Do of a form XObject: the form is a figure drawn whole (a vector picture with its labels).</summary>
    public bool AsFigure;
    /// <summary>The drawing (a cluster of painted paths read as one figure) the operation belongs to; -1: none.</summary>
    public int Drawing = -1;
    /// <summary>For a path paint: the curves among the segments of the path painted (a shape
    /// outlined - a logo, an emblem; a rule, a frame or a box has none).</summary>
    public int Curves;
    /// <summary>For a path paint that fills: the colour it fills with (red, green, blue, 0-1); null for a stroke alone.</summary>
    public double[]? Fill;
    /// <summary>For a path paint: whether it paints nothing a reader sees - white on the white page, with nothing painted
    /// under it (a text box's white ground and outline).</summary>
    public bool Unseen;
}

/// <summary>Reads a page's content into <see cref="ContentOp"/>s over one byte buffer (the
/// page's content streams joined), so the tagger can place each painting operation into a
/// block and then rewrite the buffer with marked content around it.</summary>
internal static class PageContentScan
{
    private static readonly HashSet<string> PathBuildOps = ["m", "l", "c", "v", "y", "h", "re"];
    private static readonly HashSet<string> PathPaintOps = ["S", "s", "f", "F", "f*", "B", "B*", "b", "b*"];
    private static readonly HashSet<string> TextShowOps = ["Tj", "TJ", "'", "\""];

    /// <summary>A straight axis-aligned stroke on the page as it is shown: a horizontal one at
    /// <c>At</c> = y from <c>From</c> to <c>To</c> in x, a vertical one at x the other way. An edge a
    /// table's text shows, where nothing is drawn, is a rule that is not <c>Drawn</c>. <c>Width</c> is how thick
    /// the stroke is drawn (points); 0 when unread.</summary>
    public readonly record struct Rule(bool Horizontal, double At, double From, double To, bool Drawn = true, double Width = 0);

    /// <summary>The matrix from a page's user space to the page as it is shown, turned by its
    /// /Rotate — the space text extraction places lines in (and so the space the tagger lays
    /// the page out in); null for a page that is not turned.</summary>
    internal static double[]? ShownMatrix(IO.PdfReader reader, PdfDictionary page)
    {
        PdfObject? rotate = null, mediaBox = null;
        for (var node = page; node is not null; node = reader.ResolveDict(node.Get("Parent")))
        {
            rotate ??= reader.Resolve(node.Get("Rotate"));
            mediaBox ??= reader.Resolve(node.Get("MediaBox"));
        }
        var degrees = rotate switch { PdfInteger i => (int)i.Value, PdfReal r => (int)r.Value, _ => 0 };
        degrees = ((degrees % 360) + 360) % 360;
        if (degrees == 0) return null;
        double N(PdfArray box, int i) => reader.Resolve(box[i]) switch { PdfInteger n => n.Value, PdfReal d => d.Value, _ => 0 };
        double w = 612, h = 792;
        if (mediaBox is PdfArray { Count: >= 4 } mb)
        {
            w = Math.Abs(N(mb, 2) - N(mb, 0));
            h = Math.Abs(N(mb, 3) - N(mb, 1));
        }
        return degrees switch
        {
            90 => [0, -1, 1, 0, 0, w],
            180 => [-1, 0, 0, -1, w, h],
            270 => [0, 1, -1, 0, h, 0],
            _ => null,
        };
    }

    /// <summary><paramref name="m"/> followed by the turn to the shown page, when there is one.</summary>
    internal static double[] Shown(double[] m, double[]? shown)
        => shown is null ? m : GraphicsState.MultiplyMatrices(m, shown);

    /// <summary>A user-space rectangle as it stands on the shown page.</summary>
    internal static Rectangle Shown(Rectangle r, double[]? shown)
    {
        if (shown is null) return r;
        (double X, double Y) T(double x, double y) => (shown[0] * x + shown[2] * y + shown[4], shown[1] * x + shown[3] * y + shown[5]);
        var a = T(r.LLX, r.LLY);
        var b = T(r.URX, r.URY);
        return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));
    }

    /// <summary>A rectangle on the shown page as it stands in the page's own space: through
    /// the inverse of <paramref name="shown"/> (a quarter turn, or none).</summary>
    internal static Rectangle Unshown(Rectangle r, double[]? shown)
    {
        if (shown is null) return r;
        var det = shown[0] * shown[3] - shown[1] * shown[2];
        double[] inverse =
        [
            shown[3] / det, -shown[1] / det, -shown[2] / det, shown[0] / det,
            (shown[2] * shown[5] - shown[3] * shown[4]) / det, (shown[1] * shown[4] - shown[0] * shown[5]) / det,
        ];
        return Shown(r, inverse);
    }

    // A stroke thinner than this (points) across is a rule; a filled rectangle this thin is one too.
    private const double RuleThickness = 3.0;
    // A stroke at least this many times as thick as it is long paints a bar across its own direction.
    private const double CrossStrokeShare = 4.0;

    public static (byte[] Bytes, List<ContentOp> Ops) Scan(Page page) => Scan(page, null);

    /// <summary>Scan the page's operations and, when <paramref name="rules"/> is given, collect
    /// its ruling lines: the straight segments of stroked paths, the edges of stroked
    /// rectangles and filled rectangles thin enough to be lines.</summary>
    public static (byte[] Bytes, List<ContentOp> Ops) Scan(Page page, List<Rule>? rules)
    {
        var reader = page.Reader;
        var bytes = page.GetContentStreamBytes() ?? [];
        if (bytes.Length == 0) return (bytes, new List<ContentOp>());
        return (bytes, ScanStream(reader, bytes, InheritedResources(reader, page.Dict), null, ShownMatrix(reader, page.Dict), rules));
    }

    // Forms nest: a form drawing a form drawing an image is looked into this deep.
    internal const int MaxFormDepth = 3;

    /// <summary>Scan a form XObject's content as the page shows it: <paramref name="ctm"/> maps
    /// the form's space to the shown page (its /Matrix followed by the matrix at its Do), so the
    /// operations stand where the page shows them. A form without resources of its own draws
    /// with the page's. The bytes are the form's decoded content; the operations' spans index them.</summary>
    public static (byte[] Bytes, List<ContentOp> Ops) ScanForm(IO.PdfReader reader, PdfStream form, double[] ctm,
        PdfDictionary? pageResources, List<Rule>? rules)
    {
        byte[] bytes;
        try { bytes = reader.DecodeStream(form); }
        catch { return ([], new List<ContentOp>()); }
        var resources = reader.ResolveDict(form.Dict.Get("Resources")) ?? pageResources;
        return (bytes, ScanStream(reader, bytes, resources, ctm, null, rules));
    }

    /// <summary>A form XObject's /Matrix (the identity when it states none).</summary>
    internal static double[] FormMatrix(IO.PdfReader reader, PdfStream form)
    {
        double[] identity = [1, 0, 0, 1, 0, 0];
        if (reader.Resolve(form.Dict.Get("Matrix")) is not PdfArray { Count: 6 } m) return identity;
        var result = new double[6];
        for (var i = 0; i < 6; i++)
            result[i] = reader.Resolve(m[i]) switch { PdfInteger n => n.Value, PdfReal r => r.Value, _ => identity[i] };
        return result;
    }

    /// <summary>/Resources of the page or, when absent, of the nearest ancestor in the page
    /// tree (PDF 32000-1 §7.7.3.4).</summary>
    internal static PdfDictionary? InheritedResources(IO.PdfReader reader, PdfDictionary page)
    {
        PdfDictionary? resources = null;
        for (var node = page; node is not null && resources is null; node = reader.ResolveDict(node.Get("Parent")))
            resources = reader.ResolveDict(node.Get("Resources"));
        return resources;
    }

    private static List<ContentOp> ScanStream(IO.PdfReader reader, byte[] bytes, PdfDictionary? resources,
        double[]? initialCtm, double[]? shown, List<Rule>? rules)
    {
        var ops = new List<ContentOp>();
        var fonts = new Dictionary<string, PdfDictionary>();
        if (resources is not null && reader.ResolveDict(resources.Get("Font")) is { } fontDict)
            foreach (var key in fontDict.Keys)
                if (reader.ResolveDict(fontDict.Get(key)) is { } fd)
                    fonts[key] = fd;
        var xobjects = resources is not null ? reader.ResolveDict(resources.Get("XObject")) : null;
        var properties = resources is not null ? reader.ResolveDict(resources.Get("Properties")) : null;
        // The colour spaces paint is named in: an ink's tint (a /Separation's "1 SCN") is read as the colour it prints.
        var colorSpaces = resources is not null ? reader.ResolveDict(resources.Get("ColorSpace")) : null;

        var parser = new ContentStreamParser(reader);
        if (initialCtm is not null) parser.State.Ctm = initialCtm;
        var prevEnd = 0;
        var inText = false;
        ContentOp? current = null;
        // The areas filled so far in a colour that shows: the ground white paint shows on.
        var grounds = new List<Rectangle>();

        ContentOp Current()
        {
            // Events of one operator fire before its OnOperator; the first one opens its record.
            return current ??= new ContentOp { Start = prevEnd };
        }

        parser.OnTextShown += (text, _, state) =>
        {
            var op = Current();
            var rm = Shown(GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm), shown);
            var size = state.FontSize * Math.Sqrt(rm[2] * rm[2] + rm[3] * rm[3]);
            if (op.Kind != ContentOpKind.TextShow)
            {
                op.Kind = ContentOpKind.TextShow;
                op.X0 = rm[4];
                op.Y = rm[5];
                op.Size = size;
            }
            op.Text += text;
        };
        parser.OnImageDrawn += (name, state) =>
        {
            var op = Current();
            var entry = xobjects?.Get(name);
            var stream = entry is not null ? reader.ResolveStream(entry) : null;
            var sub = stream?.Dict.GetName("Subtype");
            op.Kind = sub == "Image" ? ContentOpKind.Image : ContentOpKind.Form;
            var ctm = Shown(state.Ctm, shown);
            if (sub == "Form" && stream is not null)
            {
                // A form the page names by reference can be marked inside and referred to by /Stm.
                if (entry is PdfIndirectRef)
                {
                    op.Form = stream;
                    op.FormRef = entry;
                    op.FormCtm = ctm;
                }
                SetFormBox(op, reader, stream, ctm);
            }
            else SetUnitSquare(op, ctm);
        };
        parser.OnMarkedContentBegin += (tag, props) =>
        {
            var op = Current();
            op.Kind = ContentOpKind.BeginMark;
            op.MarkTag = tag;
            op.MarkHasMcid = props?.Get("MCID") is PdfInteger;
            op.MarkMcid = props?.Get("MCID") is PdfInteger mcid ? (int)mcid.Value : -1;
            op.MarkProps = props;
        };
        parser.OnPathPainted += (op, state, cmds) =>
        {
            var ctm = Shown(state.Ctm, shown);
            var painted = PathBox(ctm, cmds);
            var fillShows = Shows(state.FillR, state.FillG, state.FillB);
            // White paint shows where it lies over paint (a white box on a grey band), not on the white page.
            var overGround = painted is { } p && grounds.Any(g => Within(g, (p.LLX + p.URX) / 2, (p.LLY + p.URY) / 2));
            var strokeSeen = overGround || Shows(state.StrokeR, state.StrokeG, state.StrokeB);
            if (rules is not null) CollectRules(op, ctm, cmds, rules, state.LineWidth, strokeSeen, overGround || fillShows);
            if (painted is { } ground && fillShows && op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*") grounds.Add(ground);
            // The painted path's box: what a cluster of paths covers as a drawing.
            if (painted is { } box)
            {
                var record = Current();
                record.Llx = box.LLX; record.Lly = box.LLY; record.Urx = box.URX; record.Ury = box.URY;
                record.Curves = cmds.Count(c => c.Op is PathOp.CurveTo or PathOp.CurveToV or PathOp.CurveToY);
                if (op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*") record.Fill = [state.FillR, state.FillG, state.FillB];
                record.Unseen = !(op is "S" or "s" or "B" or "B*" or "b" or "b*" && strokeSeen)
                                && !(op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" && (overGround || fillShows));
            }
        };
        parser.OnInlineImageEnd += () =>
        {
            var op = Current();
            op.Kind = ContentOpKind.InlineImage;
            op.Name = "BI";
            op.InText = inText;
            SetUnitSquare(op, Shown(parser.State.Ctm, shown));
            Close();
        };
        parser.OnOperator += (name, _, state) =>
        {
            var op = Current();
            op.Name = name;
            if (op.Kind == ContentOpKind.TextShow)
            {
                // The state has advanced past the shown text: the pen is at its end.
                var rm = Shown(GraphicsState.MultiplyMatrices(state.TextMatrix, state.Ctm), shown);
                op.X1 = rm[4];
            }
            else if (op.Kind is ContentOpKind.Other)
            {
                op.Kind = name switch
                {
                    "BT" => ContentOpKind.BeginText,
                    "ET" => ContentOpKind.EndText,
                    "EMC" => ContentOpKind.EndMark,
                    "BMC" or "BDC" => ContentOpKind.BeginMark,
                    "n" => ContentOpKind.PathEnd,
                    "W" or "W*" => ContentOpKind.Clip,
                    "sh" => ContentOpKind.Shading,
                    "Do" => ContentOpKind.Form,
                    _ when PathBuildOps.Contains(name) => ContentOpKind.PathBuild,
                    _ when PathPaintOps.Contains(name) => ContentOpKind.PathPaint,
                    // A show whose font could not decode anything still paints.
                    _ when TextShowOps.Contains(name) => ContentOpKind.TextShow,
                    _ => ContentOpKind.Other,
                };
            }
            if (name == "BT") inText = true;
            op.InText = inText && name != "BT";
            if (name == "ET") inText = false;
            Close();
        };

        void Close()
        {
            var op = current!;
            op.End = (int)Math.Min(parser.OperatorEnd, bytes.Length);
            ops.Add(op);
            prevEnd = op.End;
            current = null;
        }

        try
        {
            parser.Parse(bytes, fonts, colorSpaces: colorSpaces, properties: properties);
        }
        catch
        {
            // A page whose content cannot be read to the end is left as it is: marking only
            // part of it could leave marked content unbalanced.
            ops.Clear();
        }
        return ops;
    }

    // A colour this close to white in every channel paints nothing a reader sees on a white page.
    private const double WhiteLevel = 0.95;

    /// <summary>Whether paint in this colour shows on a white page: white lines (a text box's
    /// outline drawn in white, say) rule nothing.</summary>
    private static bool Shows(double r, double g, double b) => r < WhiteLevel || g < WhiteLevel || b < WhiteLevel;

    private static bool Within(Rectangle area, double x, double y) => x >= area.LLX && x <= area.URX && y >= area.LLY && y <= area.URY;

    private static void CollectRules(string op, double[] ctm, IReadOnlyList<PathCommand> cmds, List<Rule> rules, double lineWidth,
        bool strokeShows, bool fillShows)
    {
        // A stroke is as thick as its line width, scaled as the page scales it.
        var width = lineWidth * Math.Sqrt(Math.Abs(ctm[0] * ctm[3] - ctm[1] * ctm[2]));
        var stroked = strokeShows && op is "S" or "s" or "B" or "B*" or "b" or "b*";
        var filled = fillShows && op is "f" or "F" or "f*" or "B" or "B*" or "b" or "b*";
        (double X, double Y) T(double x, double y) => (ctm[0] * x + ctm[2] * y + ctm[4], ctm[1] * x + ctm[3] * y + ctm[5]);
        void Segment((double X, double Y) a, (double X, double Y) b)
        {
            // A stroke thicker than it is long paints a bar across it: a short flat stroke drawn wide is an
            // upright rule, as thick as the stroke is long.
            var length = Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            if (length > 0 && length < RuleThickness && width > CrossStrokeShare * length && width >= RuleThickness)
            {
                if (Math.Abs(a.Y - b.Y) < 0.5)
                    rules.Add(new Rule(false, (a.X + b.X) / 2, (a.Y + b.Y) / 2 - width / 2, (a.Y + b.Y) / 2 + width / 2, Width: length));
                else if (Math.Abs(a.X - b.X) < 0.5)
                    rules.Add(new Rule(true, (a.Y + b.Y) / 2, (a.X + b.X) / 2 - width / 2, (a.X + b.X) / 2 + width / 2, Width: length));
                return;
            }
            if (Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(a.X - b.X) > 0)
                rules.Add(new Rule(true, (a.Y + b.Y) / 2, Math.Min(a.X, b.X), Math.Max(a.X, b.X), Width: width));
            else if (Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) > 0)
                rules.Add(new Rule(false, (a.X + b.X) / 2, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y), Width: width));
        }

        (double X, double Y)? start = null, pen = null;
        foreach (var c in cmds)
        {
            switch (c.Op)
            {
                case PathOp.MoveTo:
                    start = pen = T(c.X1, c.Y1);
                    break;
                case PathOp.LineTo:
                    var to = T(c.X1, c.Y1);
                    if (stroked && pen is { } from) Segment(from, to);
                    pen = to;
                    break;
                case PathOp.Close:
                    if (stroked && pen is { } p && start is { } s) Segment(p, s);
                    pen = start;
                    break;
                case PathOp.Rect:
                    var a = T(c.X1, c.Y1);
                    var b = T(c.X1 + c.X2, c.Y1 + c.Y2);
                    double l = Math.Min(a.X, b.X), r = Math.Max(a.X, b.X), lo = Math.Min(a.Y, b.Y), hi = Math.Max(a.Y, b.Y);
                    if (filled && hi - lo < RuleThickness && r - l >= RuleThickness)
                        rules.Add(new Rule(true, (lo + hi) / 2, l, r, Width: hi - lo));
                    else if (filled && r - l < RuleThickness && hi - lo >= RuleThickness)
                        rules.Add(new Rule(false, (l + r) / 2, lo, hi, Width: r - l));
                    else if (stroked)
                    {
                        Segment((l, lo), (r, lo)); Segment((l, hi), (r, hi));
                        Segment((l, lo), (l, hi)); Segment((r, lo), (r, hi));
                    }
                    start = pen = null;
                    break;
                default:
                    pen = null; // curves are not rules
                    break;
            }
        }
    }

    /// <summary>A form paints inside its /BBox: the box through the form's /Matrix and the
    /// matrix at its Do is where it stands on the page (the unit square, without a box).</summary>
    private static void SetFormBox(ContentOp op, IO.PdfReader reader, PdfStream form, double[] ctm)
    {
        if (FormBox(reader, form, ctm) is not { } box)
        {
            SetUnitSquare(op, ctm);
            return;
        }
        op.Llx = box.LLX; op.Lly = box.LLY; op.Urx = box.URX; op.Ury = box.URY;
    }

    /// <summary>Where a form XObject stands when drawn with <paramref name="ctm"/> current: its
    /// /BBox through its /Matrix and the matrix; null for a form stating no box.</summary>
    internal static Rectangle? FormBox(IO.PdfReader reader, PdfStream form, double[] ctm)
    {
        if (reader.Resolve(form.Dict.Get("BBox")) is not PdfArray { Count: 4 } box) return null;
        double N(int i) => reader.Resolve(box[i]) switch { PdfInteger n => n.Value, PdfReal r => r.Value, _ => 0 };
        var m = GraphicsState.MultiplyMatrices(FormMatrix(reader, form), ctm);
        return Bounds(m, [(N(0), N(1)), (N(2), N(1)), (N(0), N(3)), (N(2), N(3))]);
    }

    /// <summary>The box a path's points cover through <paramref name="ctm"/> (a curve's control
    /// points bound it); null for a path with no point.</summary>
    internal static Rectangle? PathBox(double[] ctm, IReadOnlyList<PathCommand> cmds)
    {
        var points = new List<(double X, double Y)>();
        foreach (var c in cmds)
        {
            switch (c.Op)
            {
                case PathOp.MoveTo or PathOp.LineTo:
                    points.Add((c.X1, c.Y1));
                    break;
                case PathOp.CurveTo:
                    points.Add((c.X1, c.Y1)); points.Add((c.X2, c.Y2)); points.Add((c.X3, c.Y3));
                    break;
                case PathOp.CurveToV or PathOp.CurveToY:
                    points.Add((c.X1, c.Y1)); points.Add((c.X2, c.Y2));
                    break;
                case PathOp.Rect:
                    points.Add((c.X1, c.Y1)); points.Add((c.X1 + c.X2, c.Y1 + c.Y2));
                    break;
            }
        }
        return points.Count == 0 ? null : Bounds(ctm, points);
    }

    private static Rectangle Bounds(double[] m, IEnumerable<(double X, double Y)> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (u, v) in points)
        {
            var x = m[0] * u + m[2] * v + m[4];
            var y = m[1] * u + m[3] * v + m[5];
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        return new Rectangle(minX, minY, maxX, maxY);
    }

    private static void SetUnitSquare(ContentOp op, double[] ctm)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (u, v) in new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) })
        {
            var x = ctm[0] * u + ctm[2] * v + ctm[4];
            var y = ctm[1] * u + ctm[3] * v + ctm[5];
            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
        }
        op.Llx = minX; op.Lly = minY; op.Urx = maxX; op.Ury = maxY;
    }
}
