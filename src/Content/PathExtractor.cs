using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

/// <summary>
/// Represents a type of path operation.
/// </summary>
public enum PathOperationType
{
    MoveTo,
    LineTo,
    CurveTo,
    ClosePath,
    Rectangle,
}

/// <summary>
/// Represents a single path painting operation.
/// </summary>
public enum PathPaintMode
{
    Stroke,
    Fill,
    FillAndStroke,
    None,
}

/// <summary>
/// Represents a segment of a path.
/// </summary>
public sealed class PathSegment
{
    public PathSegment(PathOperationType type, double[] points)
    {
        Type = type;
        Points = points;
    }

    /// <summary>The operation type.</summary>
    public PathOperationType Type { get; }

    /// <summary>Point coordinates (x,y pairs). Count depends on operation type.</summary>
    public double[] Points { get; }
}

/// <summary>
/// Represents a complete path with its painting mode and color.
/// </summary>
public sealed class ExtractedPath
{
    public ExtractedPath(IReadOnlyList<PathSegment> segments, PathPaintMode paintMode,
        double[] strokeColor, double[] fillColor, double lineWidth)
    {
        Segments = segments;
        PaintMode = paintMode;
        StrokeColor = strokeColor;
        FillColor = fillColor;
        LineWidth = lineWidth;
    }

    /// <summary>The path segments.</summary>
    public IReadOnlyList<PathSegment> Segments { get; }

    /// <summary>How this path is painted.</summary>
    public PathPaintMode PaintMode { get; }

    /// <summary>Stroke color (RGB, 3 values).</summary>
    public double[] StrokeColor { get; }

    /// <summary>Fill color (RGB, 3 values).</summary>
    public double[] FillColor { get; }

    /// <summary>Line width in points.</summary>
    public double LineWidth { get; }

    /// <summary>Bounding rectangle of the path.</summary>
    public Rectangle Bounds
    {
        get
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var seg in Segments)
            {
                for (var i = 0; i < seg.Points.Length; i += 2)
                {
                    var x = seg.Points[i];
                    var y = seg.Points[i + 1];
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (minX == double.MaxValue) return new Rectangle(0, 0, 0, 0);
            return new Rectangle(minX, minY, maxX, maxY);
        }
    }
}

/// <summary>
/// Extracts vector paths from PDF page content streams.
/// </summary>
public sealed class PathExtractor
{
    private readonly List<ExtractedPath> _paths = [];

    /// <summary>Extracted paths after calling Visit().</summary>
    public IReadOnlyList<ExtractedPath> Paths => _paths;

    /// <summary>Extract paths from a page.</summary>
    public void Visit(Page page)
    {
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page.Dict, reader);

        foreach (var streamBytes in contentStreams)
        {
            ExtractPaths(streamBytes);
        }
    }

    private void ExtractPaths(byte[] streamBytes)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        var segments = new List<PathSegment>();
        double sr = 0, sg = 0, sb = 0; // stroke color
        double fr = 0, fg = 0, fb = 0; // fill color
        double lineWidth = 1;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    switch (op)
                    {
                        case "w":
                            if (operands.Count >= 1) lineWidth = Num(operands[0]);
                            break;
                        case "RG":
                            if (operands.Count >= 3)
                            { sr = Num(operands[0]); sg = Num(operands[1]); sb = Num(operands[2]); }
                            break;
                        case "rg":
                            if (operands.Count >= 3)
                            { fr = Num(operands[0]); fg = Num(operands[1]); fb = Num(operands[2]); }
                            break;
                        case "m":
                            if (operands.Count >= 2)
                                segments.Add(new PathSegment(PathOperationType.MoveTo,
                                    [Num(operands[0]), Num(operands[1])]));
                            break;
                        case "l":
                            if (operands.Count >= 2)
                                segments.Add(new PathSegment(PathOperationType.LineTo,
                                    [Num(operands[0]), Num(operands[1])]));
                            break;
                        case "c":
                            if (operands.Count >= 6)
                                segments.Add(new PathSegment(PathOperationType.CurveTo,
                                    [Num(operands[0]), Num(operands[1]), Num(operands[2]),
                                     Num(operands[3]), Num(operands[4]), Num(operands[5])]));
                            break;
                        case "h":
                            segments.Add(new PathSegment(PathOperationType.ClosePath, []));
                            break;
                        case "re":
                            if (operands.Count >= 4)
                                segments.Add(new PathSegment(PathOperationType.Rectangle,
                                    [Num(operands[0]), Num(operands[1]), Num(operands[2]), Num(operands[3])]));
                            break;
                        case "S":
                            EmitPath(segments, PathPaintMode.Stroke, sr, sg, sb, fr, fg, fb, lineWidth);
                            break;
                        case "s":
                            segments.Add(new PathSegment(PathOperationType.ClosePath, []));
                            EmitPath(segments, PathPaintMode.Stroke, sr, sg, sb, fr, fg, fb, lineWidth);
                            break;
                        case "f" or "F" or "f*":
                            EmitPath(segments, PathPaintMode.Fill, sr, sg, sb, fr, fg, fb, lineWidth);
                            break;
                        case "B" or "B*" or "b" or "b*":
                            EmitPath(segments, PathPaintMode.FillAndStroke, sr, sg, sb, fr, fg, fb, lineWidth);
                            break;
                        case "n":
                            segments.Clear();
                            break;
                        case "BI":
                            SkipInlineImage(lexer);
                            operands.Clear();
                            continue;
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

    private static void SkipInlineImage(PdfLexer lexer)
    {
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

    private void EmitPath(List<PathSegment> segments, PathPaintMode mode,
        double sr, double sg, double sb, double fr, double fg, double fb, double lineWidth)
    {
        if (segments.Count == 0) return;
        _paths.Add(new ExtractedPath(
            segments.ToArray(), mode,
            [sr, sg, sb], [fr, fg, fb], lineWidth));
        segments.Clear();
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) result.Add(reader.DecodeStream(stream));
        else if (obj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null) result.Add(reader.DecodeStream(s));
            }
        }
        return result;
    }
}
