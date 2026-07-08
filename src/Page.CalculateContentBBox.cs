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
    public Rectangle CalculateContentBBox()
    {
        var acc = new BBoxAccumulator();

        // Text bboxes: reuse TextFragmentAbsorber, which already produces
        // page-space rectangles via full CTM/text-matrix application.
        var tfa = new TextFragmentAbsorber();
        tfa.Visit(this);
        foreach (var frag in tfa.TextFragments)
        {
            acc.Include(frag.Rectangle);
            ExtendFlippedTextLineBox(frag, acc);
        }

        // Vector paths, images, inline images, Form-XObject recursion.
        var contents = ResolveContentStreams(_dict, _reader);
        foreach (var stream in contents)
            WalkContentForBBox(stream, _dict, _reader, Cm.Identity, acc, depth: 0);

        return acc.HasAny ? acc.ToRectangle() : MediaBox;
    }

    /// <summary>
    /// Extend the content bbox below a flipped-text-matrix fragment (Tm.d &lt; 0).
    /// An absorbed fragment's Rectangle.LLY sits at its baseline; for flipped text
    /// the reference content box drops a line-box below that baseline. The drop is
    /// an internal line-box heuristic proportional to the effective (page-space)
    /// font size, NOT any font descent metric — a black-box probe of the reference
    /// implementation showed the value maps to no hhea/descriptor/FontBBox descent.
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

        // Effective page-space font size = raw size × |Tm y-scale| × |CTM y-scale|.
        double ctmScale = Math.Sqrt(ctm.C * ctm.C + ctm.D * ctm.D);
        double effFs = frag.TextState.FontSize * Math.Abs(tmD) * ctmScale;
        if (effFs <= 0)
            return;

        // Line-box factor matching the reference content-box drop below a flipped
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

        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        var ctm = inheritedCtm;
        var ctmStack = new Stack<Cm>();

        // Active clip in page space — the bbox of the union of clip paths
        // accumulated by W/W* operators. Painted content is intersected with
        // this on emit. Saved/restored by q/Q (graphics state).
        double clipMinX = double.NegativeInfinity, clipMinY = double.NegativeInfinity;
        double clipMaxX = double.PositiveInfinity, clipMaxY = double.PositiveInfinity;
        var clipStack = new Stack<(double, double, double, double)>();

        // Current path bbox (user-space pre-CTM, so we can apply the active CTM
        // at paint time). Reset on n/S/s/f/F/f*/B/B*/b/b*.
        bool pathStarted = false;
        double pminX = 0, pminY = 0, pmaxX = 0, pmaxY = 0;
        double curX = 0, curY = 0;
        bool inText = false;
        bool clipPending = false; // W/W* seen — intersect path bbox with active clip on next path-end op

        void ResetPath()
        {
            pathStarted = false;
            pminX = pminY = pmaxX = pmaxY = 0;
        }

        void IncludePathPoint(double x, double y)
        {
            if (!pathStarted)
            {
                pminX = pmaxX = x;
                pminY = pmaxY = y;
                pathStarted = true;
                return;
            }
            if (x < pminX) pminX = x;
            if (y < pminY) pminY = y;
            if (x > pmaxX) pmaxX = x;
            if (y > pmaxY) pmaxY = y;
        }

        // Project the user-space path bbox to page space and return axis-aligned
        // page-space bounds.
        (double minX, double minY, double maxX, double maxY) ProjectPathBBox()
        {
            var (x1, y1) = ctm.Apply(pminX, pminY);
            var (x2, y2) = ctm.Apply(pmaxX, pminY);
            var (x3, y3) = ctm.Apply(pmaxX, pmaxY);
            var (x4, y4) = ctm.Apply(pminX, pmaxY);
            return (Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
                    Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
                    Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
                    Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
        }

        void EmitPathBBox()
        {
            if (!pathStarted) return;
            var (mnx, mny, mxx, mxy) = ProjectPathBBox();
            // Intersect with active clip — content outside the clip is not painted.
            mnx = Math.Max(mnx, clipMinX);
            mny = Math.Max(mny, clipMinY);
            mxx = Math.Min(mxx, clipMaxX);
            mxy = Math.Min(mxy, clipMaxY);
            if (mnx <= mxx && mny <= mxy)
            {
                acc.IncludePoint(mnx, mny);
                acc.IncludePoint(mxx, mxy);
            }
            ResetPath();
            clipPending = false;
        }

        // Tighten the active clip with the current path bbox (page space).
        void ApplyPendingClip()
        {
            if (!clipPending || !pathStarted) { clipPending = false; return; }
            var (mnx, mny, mxx, mxy) = ProjectPathBBox();
            clipMinX = Math.Max(clipMinX, mnx);
            clipMinY = Math.Max(clipMinY, mny);
            clipMaxX = Math.Min(clipMaxX, mxx);
            clipMaxY = Math.Min(clipMaxY, mxy);
            clipPending = false;
        }

        while (true)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) break;

            switch (t.Kind)
            {
                case TokenKind.Integer: operands.Add(new PdfInteger(t.IntValue)); break;
                case TokenKind.Real: operands.Add(new PdfReal(t.RealValue)); break;
                case TokenKind.LiteralString: operands.Add(new PdfString(t.BytesValue!)); break;
                case TokenKind.HexString: operands.Add(new PdfString(t.BytesValue!, isHex: true)); break;
                case TokenKind.Name: operands.Add(new PdfName(t.StringValue!)); break;
                case TokenKind.ArrayStart: operands.Add(ParseArrayForBBox(lexer)); break;
                case TokenKind.Keyword:
                {
                    var op = t.StringValue!;
                    switch (op)
                    {
                        case "q":
                            ctmStack.Push(ctm);
                            clipStack.Push((clipMinX, clipMinY, clipMaxX, clipMaxY));
                            break;
                        case "Q":
                            if (ctmStack.Count > 0) ctm = ctmStack.Pop();
                            if (clipStack.Count > 0)
                                (clipMinX, clipMinY, clipMaxX, clipMaxY) = clipStack.Pop();
                            break;
                        case "W" or "W*":
                            clipPending = true;
                            break;
                        case "cm" when operands.Count >= 6:
                            ctm = new Cm(
                                Num(operands[0]), Num(operands[1]),
                                Num(operands[2]), Num(operands[3]),
                                Num(operands[4]), Num(operands[5])).Multiply(ctm);
                            break;

                        case "BT": inText = true; ResetPath(); break;
                        case "ET": inText = false; break;

                        // Path-construction operators (skip while inside BT/ET — those
                        // operands are text positioning, not path geometry).
                        case "m" when !inText && operands.Count >= 2:
                            curX = Num(operands[0]); curY = Num(operands[1]);
                            IncludePathPoint(curX, curY);
                            break;
                        case "l" when !inText && operands.Count >= 2:
                            curX = Num(operands[0]); curY = Num(operands[1]);
                            IncludePathPoint(curX, curY);
                            break;
                        case "c" when !inText && operands.Count >= 6:
                        {
                            // Cubic Bézier from current point through (x1,y1) and
                            // (x2,y2) to (x3,y3). Use exact extrema rather than the
                            // convex hull — control points can be placed far outside
                            // the actual curve (common for thin-stroke shapes), and
                            // including them would inflate the bbox dramatically.
                            var x1 = Num(operands[0]); var y1 = Num(operands[1]);
                            var x2 = Num(operands[2]); var y2 = Num(operands[3]);
                            var x3 = Num(operands[4]); var y3 = Num(operands[5]);
                            CubicExtremaInclude(IncludePathPoint, curX, curY, x1, y1, x2, y2, x3, y3);
                            curX = x3; curY = y3;
                            break;
                        }
                        case "v" when !inText && operands.Count >= 4:
                        {
                            // First control = current point.
                            var x2 = Num(operands[0]); var y2 = Num(operands[1]);
                            var x3 = Num(operands[2]); var y3 = Num(operands[3]);
                            CubicExtremaInclude(IncludePathPoint, curX, curY, curX, curY, x2, y2, x3, y3);
                            curX = x3; curY = y3;
                            break;
                        }
                        case "y" when !inText && operands.Count >= 4:
                        {
                            // Second control = endpoint.
                            var x1 = Num(operands[0]); var y1 = Num(operands[1]);
                            var x3 = Num(operands[2]); var y3 = Num(operands[3]);
                            CubicExtremaInclude(IncludePathPoint, curX, curY, x1, y1, x3, y3, x3, y3);
                            curX = x3; curY = y3;
                            break;
                        }
                        case "re" when !inText && operands.Count >= 4:
                        {
                            var x = Num(operands[0]);
                            var y = Num(operands[1]);
                            var w = Num(operands[2]);
                            var h = Num(operands[3]);
                            IncludePathPoint(x, y);
                            IncludePathPoint(x + w, y + h);
                            curX = x; curY = y;
                            break;
                        }
                        case "h": /* close: no new point */ break;

                        // Painting operators — apply pending clip THEN emit bbox.
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            ApplyPendingClip();
                            EmitPathBBox();
                            break;
                        case "n":
                            // No-op end-of-path. Apply pending clip first, then drop the path.
                            ApplyPendingClip();
                            ResetPath();
                            break;

                        // External XObject reference.
                        case "Do" when operands.Count >= 1 && operands[0] is PdfName doName:
                        {
                            var xobjs = TextAbsorber.ResolveXObjects(ownerDict, reader);
                            if (xobjs is not null)
                            {
                                var xstr = reader.ResolveStream(xobjs.Get(doName.Value));
                                if (xstr is not null)
                                {
                                    var subtype = xstr.Dict.GetName("Subtype");
                                    if (subtype == "Image")
                                    {
                                        // Images are painted into the unit square (0,0)-(1,1)
                                        // pre-CTM; transform that square's corners.
                                        var (ix1, iy1) = ctm.Apply(0, 0);
                                        var (ix2, iy2) = ctm.Apply(1, 0);
                                        var (ix3, iy3) = ctm.Apply(1, 1);
                                        var (ix4, iy4) = ctm.Apply(0, 1);
                                        acc.IncludePoint(Math.Min(Math.Min(ix1, ix2), Math.Min(ix3, ix4)),
                                                         Math.Min(Math.Min(iy1, iy2), Math.Min(iy3, iy4)));
                                        acc.IncludePoint(Math.Max(Math.Max(ix1, ix2), Math.Max(ix3, ix4)),
                                                         Math.Max(Math.Max(iy1, iy2), Math.Max(iy3, iy4)));
                                    }
                                    else if (subtype == "Form")
                                    {
                                        // Form XObjects carry their own /Matrix and /BBox.
                                        // Compose Matrix into the CTM, recurse with the form's content,
                                        // then clip the form's contribution to its /BBox in form-space
                                        // (the spec requires content outside /BBox to be clipped).
                                        var formCtm = ctm;
                                        if (xstr.Dict.Get("Matrix") is PdfArray mArr && mArr.Count >= 6)
                                        {
                                            formCtm = new Cm(
                                                NumOf(mArr[0]), NumOf(mArr[1]),
                                                NumOf(mArr[2]), NumOf(mArr[3]),
                                                NumOf(mArr[4]), NumOf(mArr[5])).Multiply(ctm);
                                        }
                                        var xbytes = reader.DecodeStream(xstr);

                                        // Build a per-form accumulator so we can clip its
                                        // contribution to /BBox before merging into the outer acc.
                                        var formAcc = new BBoxAccumulator();
                                        WalkContentForBBox(xbytes, xstr.Dict, reader, formCtm, formAcc, depth + 1);
                                        if (formAcc.HasAny)
                                        {
                                            // Translate /BBox to page-space using formCtm and intersect.
                                            var bboxArr = xstr.Dict.Get("BBox") as PdfArray;
                                            if (bboxArr is not null && bboxArr.Count >= 4)
                                            {
                                                var bx1 = NumOf(bboxArr[0]); var by1 = NumOf(bboxArr[1]);
                                                var bx2 = NumOf(bboxArr[2]); var by2 = NumOf(bboxArr[3]);
                                                var (px1, py1) = formCtm.Apply(bx1, by1);
                                                var (px2, py2) = formCtm.Apply(bx2, by1);
                                                var (px3, py3) = formCtm.Apply(bx2, by2);
                                                var (px4, py4) = formCtm.Apply(bx1, by2);
                                                var bboxMinX = Math.Min(Math.Min(px1, px2), Math.Min(px3, px4));
                                                var bboxMaxX = Math.Max(Math.Max(px1, px2), Math.Max(px3, px4));
                                                var bboxMinY = Math.Min(Math.Min(py1, py2), Math.Min(py3, py4));
                                                var bboxMaxY = Math.Max(Math.Max(py1, py2), Math.Max(py3, py4));
                                                var ix1 = Math.Max(formAcc.MinX, bboxMinX);
                                                var iy1 = Math.Max(formAcc.MinY, bboxMinY);
                                                var ix2 = Math.Min(formAcc.MaxX, bboxMaxX);
                                                var iy2 = Math.Min(formAcc.MaxY, bboxMaxY);
                                                if (ix1 <= ix2 && iy1 <= iy2)
                                                {
                                                    acc.IncludePoint(ix1, iy1);
                                                    acc.IncludePoint(ix2, iy2);
                                                }
                                            }
                                            else
                                            {
                                                acc.IncludePoint(formAcc.MinX, formAcc.MinY);
                                                acc.IncludePoint(formAcc.MaxX, formAcc.MaxY);
                                            }
                                        }
                                    }
                                }
                            }
                            break;
                        }

                        // Inline images — BI..ID..EI. The image is painted into the
                        // unit square pre-CTM, same as Do/Image.
                        case "BI":
                        {
                            SkipInlineImageBody(lexer);
                            var (ix1, iy1) = ctm.Apply(0, 0);
                            var (ix2, iy2) = ctm.Apply(1, 0);
                            var (ix3, iy3) = ctm.Apply(1, 1);
                            var (ix4, iy4) = ctm.Apply(0, 1);
                            acc.IncludePoint(Math.Min(Math.Min(ix1, ix2), Math.Min(ix3, ix4)),
                                             Math.Min(Math.Min(iy1, iy2), Math.Min(iy3, iy4)));
                            acc.IncludePoint(Math.Max(Math.Max(ix1, ix2), Math.Max(ix3, ix4)),
                                             Math.Max(Math.Max(iy1, iy2), Math.Max(iy3, iy4)));
                            break;
                        }
                    }
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
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
