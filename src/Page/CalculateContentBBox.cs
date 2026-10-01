using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>
    /// Compute the smallest axis-aligned rectangle in page space enclosing all
    /// painted content on this page (text, vector paths, images, inline images,
    /// recursively through Form XObjects).
    /// Returns the MediaBox if the page is blank.
    /// </summary>
    public Rectangle CalculateContentBBox() => GeometricContentBBox();

    /// <summary>The ascent and descent, in em, of the box the reference gives every text
    /// run whatever its face (probed across five documents: an embedded Corbel run's box
    /// is 0.905 em above and 0.212 em below its baseline, the same as an Arial run's).</summary>
    private const double TextBoxAscentEm = 0.905;
    private const double TextBoxDescentEm = 0.212;

    /// <summary>The geometric extent of everything the page shows: the glyph-bearing
    /// text runs in the reference's constant-metric box, the drawn annotations' rects,
    /// paths, images and form XObjects, whether or not they end up visible. A run's box
    /// stops at its last glyph-bearing piece (a whitespace-only string element of a TJ
    /// array does not widen it, a space inside a piece does) and a run of nothing but
    /// spaces adds nothing.</summary>
    private Rectangle GeometricContentBBox()
    {
        var acc = new BBoxAccumulator();

        // Text bboxes: reuse TextFragmentAbsorber, which already produces
        // page-space rectangles via full CTM/text-matrix application.
        var tfa = new TextFragmentAbsorber();
        tfa.Visit(this);
        foreach (var frag in tfa.TextFragments)
        {
            foreach (var seg in frag.Segments) IncludeTextSegmentBox(seg, acc);
            ExtendFlippedTextLineBox(frag, acc);
        }
        foreach (var annotation in Annotations)
            if (IsDrawnAnnotation(annotation)) acc.Include(annotation.Rect);

        // Vector paths, images, inline images, Form-XObject recursion. A page's
        // content array is ONE content stream split at token boundaries: a q or a cm
        // in one part governs the paths of the next, so the parts walk as a whole
        // (walked apart, a logo drawn under a scaling cm in a later part landed at
        // its raw thousand-unit coordinates and put the content box off the page).
        var contents = ResolveContentStreams(_dict, _reader);
        WalkContentForBBox(ConcatenateContent(contents), _dict, _reader, Cm.Identity, acc, depth: 0);

        return acc.HasAny ? acc.ToRectangle() : MediaBox;
    }

    /// <summary>The reference's box for one text segment: the segment's glyph-bearing
    /// advance box for its x-extent and the constant ascent/descent band about its
    /// baseline for its y-extent. A segment without glyphs adds nothing; a run the
    /// absorber could not seat, or a rotated one, keeps its plain box.</summary>
    private static void IncludeTextSegmentBox(Text.TextSegment seg, BBoxAccumulator acc)
    {
        var ink = seg.InkRectangle;
        if (ink is null) return;
        var fs = seg.TextState?.FontSize ?? 0;
        var upright = seg.BaselineY is not null && fs > 0;
        if (!upright) { acc.Include(ink); return; }
        var baseline = seg.BaselineY!.Value;
        acc.IncludePoint(ink.LLX, baseline - TextBoxDescentEm * fs);
        acc.IncludePoint(ink.URX, baseline + TextBoxAscentEm * fs);
    }

    /// <summary>An annotation the page shows: not hidden, and not one of the kinds that
    /// draw nothing of their own (a link's hot area, a pop-up window).</summary>
    private static bool IsDrawnAnnotation(Aspose.Pdf.Annotations.Annotation annotation)
    {
        if (annotation is Aspose.Pdf.Annotations.LinkAnnotation or Aspose.Pdf.Annotations.PopupAnnotation) return false;
        if ((annotation.Flags & (Aspose.Pdf.Annotations.AnnotationFlags.Hidden | Aspose.Pdf.Annotations.AnnotationFlags.NoView)) != 0) return false;
        return annotation.Rect is { IsEmpty: false };
    }

    /// <summary>
    /// Extend the content bbox below a flipped-text-matrix fragment (Tm.d &lt; 0).
    /// An absorbed fragment's Rectangle.LLY sits at its baseline; for flipped text
    /// the content box drops a line-box below that baseline. The drop is
    /// an internal line-box heuristic proportional to the effective (page-space)
    /// font size, NOT any font descent metric — the value maps to no
    /// hhea/descriptor/FontBBox descent.
    /// Gated to flipped text so upright text (the common case, and the other
    /// CalculateContentBBox callers) is left exactly as the fragment rectangle.
    /// </summary>
    private static void ExtendFlippedTextLineBox(Text.TextFragment frag, BBoxAccumulator acc)
    {
        var rect = frag.Rectangle;
        if (rect is null || frag.ExtractionCtm is null || frag.Segments.Count == 0)
            return;

        Text.TextSegment? seg0 = null;
        foreach (var s in frag.Segments) { seg0 = s; break; }
        if (seg0 is null)
            return;

        // Only flipped text: the net vertical direction (text-matrix d × CTM d) is
        // negative, i.e. the glyph baseline is drawn under an inverted Y axis. The
        // double flip in this file is folded so the text-matrix reports d=+1 and the
        // inversion lives in the CTM, so test the product.
        var ctm = frag.ExtractionCtm;
        double tmD = seg0.TextState.TmD;
        if (tmD * ctm.D >= 0)
            return;

        // The fragment's FontSize is already the effective page-space size (the
        // absorber composes the Tm up-axis with the CTM) — applying |Tm d| and
        // the CTM scale again would square the scale on flipped-CTM documents.
        double effFs = frag.TextState.FontSize;
        if (effFs <= 0)
            return;

        // Line-box factor for the content-box drop below a flipped
        // baseline (empirical constant × effective font size).
        const double flippedLineBoxFactor = 0.60;
        acc.IncludePoint(rect.LLX, rect.LLY - flippedLineBoxFactor * effFs);
    }

    private readonly record struct Cm(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Cm Identity = new(1, 0, 0, 1, 0, 0);

        public Cm Multiply(Cm other) => new(
            A * other.A + B * other.C,
            A * other.B + B * other.D,
            C * other.A + D * other.C,
            C * other.B + D * other.D,
            E * other.A + F * other.C + other.E,
            E * other.B + F * other.D + other.F);

        public (double x, double y) Apply(double x, double y) =>
            (A * x + C * y + E, B * x + D * y + F);
    }

    private sealed class BBoxAccumulator
    {
        private double _minX = double.PositiveInfinity;
        private double _minY = double.PositiveInfinity;
        private double _maxX = double.NegativeInfinity;
        private double _maxY = double.NegativeInfinity;

        public bool HasAny => _minX <= _maxX;
        public double MinX => _minX;
        public double MinY => _minY;
        public double MaxX => _maxX;
        public double MaxY => _maxY;

        public void IncludePoint(double x, double y)
        {
            if (x < _minX) _minX = x;
            if (y < _minY) _minY = y;
            if (x > _maxX) _maxX = x;
            if (y > _maxY) _maxY = y;
        }

        public void Include(Rectangle? r)
        {
            if (r is null) return;
            // Skip degenerate (zero-area) rects so empty TextFragment entries
            // don't pull the bbox to (0,0).
            if (r.URX <= r.LLX || r.URY <= r.LLY) return;
            IncludePoint(r.LLX, r.LLY);
            IncludePoint(r.URX, r.URY);
        }

        public Rectangle ToRectangle() => new(_minX, _minY, _maxX, _maxY);
    }

    /// <summary>The parts of a page's content array joined with a newline between them.</summary>
    private static byte[] ConcatenateContent(List<byte[]> parts)
    {
        if (parts.Count == 1) return parts[0];
        var total = 0;
        foreach (var part in parts) total += part.Length + 1;
        var joined = new byte[total];
        var at = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, joined, at, part.Length);
            at += part.Length;
            joined[at++] = (byte)'\n';
        }
        return joined;
    }

    private static List<byte[]> ResolveContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream s)
            result.Add(reader.DecodeStream(s));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var st = reader.ResolveStream(item);
                if (st is not null) result.Add(reader.DecodeStream(st));
            }
        }
        return result;
    }

    private static void WalkContentForBBox(byte[] streamBytes, PdfDictionary ownerDict,
        PdfReader reader, Cm inheritedCtm, BBoxAccumulator acc, int depth)
    {
        if (depth > 6) return; // guard against pathological Form-XObject recursion

        var bw = new BBoxWalkState();
        bw.lexer = new PdfLexer(streamBytes);
        bw.operands = new List<PdfObject>();
        bw.ctm = inheritedCtm;
        bw.ctmStack = new Stack<Cm>();

        bw.clipMinX = double.NegativeInfinity;
        bw.clipMinY = double.NegativeInfinity;
        bw.clipMaxX = double.PositiveInfinity;
        bw.clipMaxY = double.PositiveInfinity;
        bw.clipStack = new Stack<(double, double, double, double)>();

        bw.pathStarted = false;
        bw.pminX = 0;
        bw.pminY = 0;
        bw.pmaxX = 0;
        bw.pmaxY = 0;
        bw.curX = 0;
        bw.curY = 0;
        bw.inText = false;
        bw.clipPending = false;

        while (true)
        {
            var t = bw.lexer.NextToken();
            if (t.Kind == TokenKind.Eof) break;

            switch (t.Kind)
            {
                case TokenKind.Integer: bw.operands.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: bw.operands.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: bw.operands.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: bw.operands.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: bw.operands.Add(new PdfName(t.StringValue!)); break;
                case TokenKind.ArrayStart: bw.operands.Add(ParseArrayForBBox(bw.lexer)); break;
                case TokenKind.Keyword:
                {
                    var op = t.StringValue!;
                    switch (op)
                    {
                        case "q": case "Q": case "W": case "W*": case "cm": case "BT": case "ET":
                            if (WalkStateOperator(bw, op)) return;
                            break;
                        case "m": case "l": case "c": case "v": case "y": case "re": case "h":
                            if (WalkPathOperator(bw, op)) return;
                            break;
                        case "S": case "s": case "f": case "F": case "f*": case "B": case "B*": case "b": case "b*": case "n":
                            if (WalkPaintOperator(bw, acc, op)) return;
                            break;
                        case "Do": case "BI":
                            if (WalkXObjectOperator(bw, acc, ownerDict, reader, depth, op)) return;
                            break;
                    }
                    bw.operands.Clear();
                    break;
                }
                default:
                    bw.operands.Clear();
                    break;
            }
        }
    }

    private static PdfArray ParseArrayForBBox(PdfLexer lexer)
    {
        var arr = new PdfArray();
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.ArrayEnd || t.Kind == TokenKind.Eof) break;
            switch (t.Kind)
            {
                case TokenKind.Integer: arr.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: arr.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: arr.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: arr.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static void SkipInlineImageBody(PdfLexer lexer)
    {
        // Walk to the ID keyword; then scan bytes until we find
        // \s EI \s — the inline image data is opaque between ID and EI.
        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
        }

        var pos = lexer.Position + 1;
        var len = lexer.Length;
        while (pos < len - 2)
        {
            var b = lexer.ByteAt(pos);
            if (b is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20 &&
                lexer.ByteAt(pos + 1) == (byte)'E' &&
                lexer.ByteAt(pos + 2) == (byte)'I')
            {
                var after = pos + 3;
                if (after >= len || lexer.ByteAt(after) is 0x00 or 0x09 or 0x0A or 0x0C or 0x0D or 0x20)
                {
                    lexer.Position = after;
                    return;
                }
            }
            pos++;
        }
        lexer.Position = len;
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static double NumOf(PdfObject obj) => Num(obj);

    /// <summary>
    /// Include the actual extrema of a cubic Bézier curve (not the convex hull
    /// of its control points). For each axis, B(t) = (1-t)^3 P0 + 3(1-t)^2 t P1
    /// + 3(1-t) t^2 P2 + t^3 P3, so B'(t) = 3 [(1-t)^2 (P1-P0) + 2(1-t) t (P2-P1)
    /// + t^2 (P3-P2)] which is a quadratic in t. Roots in (0,1) plus the
    /// endpoints give the extrema.
    /// </summary>
    private static void CubicExtremaInclude(Action<double, double> includePoint,
        double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        includePoint(x0, y0);
        includePoint(x3, y3);

        // Solve 3 [(P1-P0) + 2 (P2 - 2P1 + P0) t + (P3 - 3P2 + 3P1 - P0) t^2] = 0
        // for each axis. Add the curve point at each in-range root.
        IncludeAxisRoots(t => CubicValue(x0, x1, x2, x3, t),
                         t => CubicValue(y0, y1, y2, y3, t),
                         x0, x1, x2, x3, includePoint, isX: true);
        IncludeAxisRoots(t => CubicValue(x0, x1, x2, x3, t),
                         t => CubicValue(y0, y1, y2, y3, t),
                         y0, y1, y2, y3, includePoint, isX: false);
    }

    private static double CubicValue(double p0, double p1, double p2, double p3, double t)
    {
        var u = 1 - t;
        return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
    }

    private static void IncludeAxisRoots(Func<double, double> xAt, Func<double, double> yAt,
        double p0, double p1, double p2, double p3,
        Action<double, double> includePoint, bool isX)
    {
        // a t^2 + b t + c = 0
        var a = -p0 + 3 * p1 - 3 * p2 + p3;
        var b = 2 * (p0 - 2 * p1 + p2);
        var c = p1 - p0;

        Span<double> roots = stackalloc double[2];
        var nRoots = 0;
        if (Math.Abs(a) < 1e-9)
        {
            if (Math.Abs(b) > 1e-9)
                roots[nRoots++] = -c / b;
        }
        else
        {
            var disc = b * b - 4 * a * c;
            if (disc >= 0)
            {
                var sq = Math.Sqrt(disc);
                roots[nRoots++] = (-b + sq) / (2 * a);
                roots[nRoots++] = (-b - sq) / (2 * a);
            }
        }

        for (int i = 0; i < nRoots; i++)
        {
            var t = roots[i];
            if (t > 0 && t < 1)
                includePoint(xAt(t), yAt(t));
        }
    }
}
