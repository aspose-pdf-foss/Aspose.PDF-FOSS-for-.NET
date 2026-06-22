using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Extension methods over <see cref="Page"/> that manipulate the page content stream.
/// </summary>
public static class PageExtensions
{
    /// <summary>
    /// Duplicate every vector path on the page whose painted geometry intersects
    /// <paramref name="region"/>, translating each copy by (<paramref name="deltaX"/>,
    /// <paramref name="deltaY"/>) in page coordinates. Useful for extending a drawn
    /// table with additional rows or columns that repeat the existing rule lines.
    /// The duplicated paths preserve their original transform, stroke/fill colour,
    /// line width and dash pattern. Text and images are not duplicated.
    /// </summary>
    /// <param name="page">The page to extend.</param>
    /// <param name="region">The page-space rectangle whose intersecting paths are copied.</param>
    /// <param name="deltaX">Horizontal shift of each copy, in points.</param>
    /// <param name="deltaY">Vertical shift of each copy, in points.</param>
    public static void DuplicateIntersectingGraphics(this Page page, Rectangle region,
        double deltaX, double deltaY)
    {
        if (page is null || region is null) return;
        var reader = page.Reader;

        // PDF concatenates a page's content streams into one logical stream, so the
        // graphics state and CTM carry across them — join before walking.
        var combined = new List<byte>();
        foreach (var bytes in ResolveContentStreams(page.Dict, reader))
        {
            combined.AddRange(bytes);
            combined.Add((byte)'\n');
        }
        if (combined.Count == 0) return;

        var dup = new StringBuilder();
        Collect(combined.ToArray(), region, deltaX, deltaY, dup);
        if (dup.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(dup.ToString()));
    }

    private static List<byte[]> ResolveContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream s)
            result.Add(reader.DecodeStream(s));
        else if (obj is PdfArray arr)
            foreach (var item in arr)
            {
                var st = reader.ResolveStream(item);
                if (st is not null) result.Add(reader.DecodeStream(st));
            }
        return result;
    }

    private readonly record struct Mat(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Mat Identity = new(1, 0, 0, 1, 0, 0);

        public Mat Multiply(Mat o) => new(
            A * o.A + B * o.C, A * o.B + B * o.D,
            C * o.A + D * o.C, C * o.B + D * o.D,
            E * o.A + F * o.C + o.E, E * o.B + F * o.D + o.F);

        public (double x, double y) Apply(double x, double y) => (A * x + C * y + E, B * x + D * y + F);
    }

    private sealed class GState
    {
        public string? Width, Cap, Join, Miter, Dash, StrokeColor, FillColor, StrokeCs, FillCs;
        public GState Clone() => (GState)MemberwiseClone();

        public void EmitInto(StringBuilder sb)
        {
            foreach (var op in new[] { StrokeCs, FillCs, Width, Cap, Join, Miter, Dash, StrokeColor, FillColor })
                if (!string.IsNullOrEmpty(op)) sb.Append(op).Append('\n');
        }
    }

    private static void Collect(byte[] streamBytes, Rectangle region,
        double deltaX, double deltaY, StringBuilder dup)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        var ctm = Mat.Identity;
        var ctmStack = new Stack<Mat>();
        var gs = new GState();
        var gsStack = new Stack<GState>();

        var path = new StringBuilder();           // raw construction ops of the current path
        bool started = false;                      // any geometry recorded for the current path
        double minX = 0, minY = 0, maxX = 0, maxY = 0, curX = 0, curY = 0;
        bool inText = false;

        void ResetPath() { path.Clear(); started = false; }

        void Pt(double x, double y)
        {
            if (!started) { minX = maxX = x; minY = maxY = y; started = true; return; }
            if (x < minX) minX = x; if (y < minY) minY = y;
            if (x > maxX) maxX = x; if (y > maxY) maxY = y;
        }

        void EmitIfIntersecting(string paintOp)
        {
            if (started)
            {
                // Project the user-space path bbox through the active CTM to page space.
                var (x1, y1) = ctm.Apply(minX, minY);
                var (x2, y2) = ctm.Apply(maxX, minY);
                var (x3, y3) = ctm.Apply(maxX, maxY);
                var (x4, y4) = ctm.Apply(minX, maxY);
                double pmnX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
                double pmnY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
                double pmxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
                double pmxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
                bool hit = pmnX <= region.URX && pmxX >= region.LLX
                        && pmnY <= region.URY && pmxY >= region.LLY;
                if (hit)
                {
                    // Reproduce the path translated by (deltaX, deltaY) in page space:
                    // page-point' = (p x CTM) x Translate. Emitting Translate then CTM
                    // composes the effective matrix CTM x Translate from the page base.
                    dup.Append("q\n");
                    dup.Append("1 0 0 1 ").Append(F(deltaX)).Append(' ').Append(F(deltaY)).Append(" cm\n");
                    dup.Append(F(ctm.A)).Append(' ').Append(F(ctm.B)).Append(' ').Append(F(ctm.C)).Append(' ')
                       .Append(F(ctm.D)).Append(' ').Append(F(ctm.E)).Append(' ').Append(F(ctm.F)).Append(" cm\n");
                    gs.EmitInto(dup);
                    dup.Append(path);
                    dup.Append(paintOp).Append("\nQ\n");
                }
            }
            ResetPath();
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
                case TokenKind.ArrayStart: operands.Add(ParseArray(lexer)); break;
                case TokenKind.Keyword:
                {
                    var op = t.StringValue!;
                    switch (op)
                    {
                        case "q":
                            ctmStack.Push(ctm); gsStack.Push(gs.Clone()); break;
                        case "Q":
                            if (ctmStack.Count > 0) ctm = ctmStack.Pop();
                            if (gsStack.Count > 0) gs = gsStack.Pop();
                            break;
                        case "cm" when operands.Count >= 6:
                            ctm = new Mat(Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                          Num(operands[3]), Num(operands[4]), Num(operands[5])).Multiply(ctm);
                            break;

                        case "BT": inText = true; ResetPath(); break;
                        case "ET": inText = false; break;

                        // Graphics-state setters — remember the latest of each kind.
                        case "w": gs.Width = OpLine(operands, op); break;
                        case "J": gs.Cap = OpLine(operands, op); break;
                        case "j": gs.Join = OpLine(operands, op); break;
                        case "M": gs.Miter = OpLine(operands, op); break;
                        case "d": gs.Dash = OpLine(operands, op); break;
                        case "CS": gs.StrokeCs = OpLine(operands, op); break;
                        case "cs": gs.FillCs = OpLine(operands, op); break;
                        case "G" or "RG" or "K" or "SC" or "SCN": gs.StrokeColor = OpLine(operands, op); break;
                        case "g" or "rg" or "k" or "sc" or "scn": gs.FillColor = OpLine(operands, op); break;

                        // Path construction (ignore inside text objects).
                        case "m" when !inText && operands.Count >= 2:
                            curX = Num(operands[0]); curY = Num(operands[1]); Pt(curX, curY);
                            path.Append(OpLine(operands, op)).Append('\n'); break;
                        case "l" when !inText && operands.Count >= 2:
                            curX = Num(operands[0]); curY = Num(operands[1]); Pt(curX, curY);
                            path.Append(OpLine(operands, op)).Append('\n'); break;
                        case "c" when !inText && operands.Count >= 6:
                            Pt(Num(operands[0]), Num(operands[1])); Pt(Num(operands[2]), Num(operands[3]));
                            curX = Num(operands[4]); curY = Num(operands[5]); Pt(curX, curY);
                            path.Append(OpLine(operands, op)).Append('\n'); break;
                        case "v" when !inText && operands.Count >= 4:
                            Pt(curX, curY); Pt(Num(operands[0]), Num(operands[1]));
                            curX = Num(operands[2]); curY = Num(operands[3]); Pt(curX, curY);
                            path.Append(OpLine(operands, op)).Append('\n'); break;
                        case "y" when !inText && operands.Count >= 4:
                            Pt(Num(operands[0]), Num(operands[1]));
                            curX = Num(operands[2]); curY = Num(operands[3]); Pt(curX, curY);
                            path.Append(OpLine(operands, op)).Append('\n'); break;
                        case "re" when !inText && operands.Count >= 4:
                        {
                            double x = Num(operands[0]), y = Num(operands[1]),
                                   w = Num(operands[2]), h = Num(operands[3]);
                            Pt(x, y); Pt(x + w, y + h); curX = x; curY = y;
                            path.Append(OpLine(operands, op)).Append('\n'); break;
                        }
                        case "h" when !inText:
                            path.Append("h\n"); break;
                        case "W" or "W*" when !inText:
                            // Keep clip operators inside the duplicated path so the copy
                            // clips itself the same way; never duplicate a clip-only path.
                            path.Append(op).Append('\n'); break;

                        // Painting operators end the path.
                        case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                            EmitIfIntersecting(op); break;
                        case "n":
                            ResetPath(); break;
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

    private static PdfArray ParseArray(PdfLexer lexer)
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
                case TokenKind.Name: arr.Add(new PdfName(t.StringValue!)); break;
            }
        }
        return arr;
    }

    private static string OpLine(List<PdfObject> operands, string keyword)
    {
        var sb = new StringBuilder();
        foreach (var o in operands) { sb.Append(OpText(o)); sb.Append(' '); }
        sb.Append(keyword);
        return sb.ToString();
    }

    private static string OpText(PdfObject o)
    {
        switch (o)
        {
            case PdfInteger i: return i.Value.ToString(CultureInfo.InvariantCulture);
            case PdfReal r: return F(r.Value);
            case PdfName n: return "/" + n.Value;
            case PdfArray a:
            {
                var sb = new StringBuilder("[");
                bool first = true;
                foreach (var e in a) { if (!first) sb.Append(' '); sb.Append(OpText(e)); first = false; }
                return sb.Append(']').ToString();
            }
            default: return "";
        }
    }

    private static double Num(PdfObject o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    // Format a real for a content stream: snap sub-epsilon values to 0 and avoid
    // exponent notation ("0.######" never emits E-notation), which is invalid in PDF.
    private static string F(double v)
    {
        if (Math.Abs(v) < 1e-6) v = 0;
        return v.ToString("0.######", CultureInfo.InvariantCulture);
    }
}
