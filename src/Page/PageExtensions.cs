using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Extension methods over <see cref="Page"/> that manipulate the page content stream.
/// </summary>
public static partial class PageExtensions
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

    private static void Collect(byte[] streamBytes, Rectangle region, double deltaX, double deltaY, StringBuilder dup)
    {
        var rc = new RegionCollectState();
        rc.streamBytes = streamBytes;
        rc.region = region;
        rc.deltaX = deltaX;
        rc.deltaY = deltaY;
        rc.dup = dup;
        rc.lexer = new PdfLexer(rc.streamBytes);
        rc.operands = new List<PdfObject>();
        rc.ctm = Mat.Identity;
        rc.ctmStack = new Stack<Mat>();
        rc.gs = new GState();
        rc.gsStack = new Stack<GState>();

        rc.path = new StringBuilder();           // raw construction ops of the current path
        rc.started = false;                      // any geometry recorded for the current path
        rc.minX = 0;
        rc.minY = 0;
        rc.maxX = 0;
        rc.maxY = 0;
        rc.curX = 0;
        rc.curY = 0;
        rc.inText = false;

        while (true)
        {
            if (!CollectRegionOperator(rc)) break;
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
