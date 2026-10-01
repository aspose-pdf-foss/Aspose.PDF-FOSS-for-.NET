using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Builds one page's content stream. The emission shape follows the conventions the
/// reference converter uses, as measured: the page opens with
/// a clip to its own media box, and every painted path is a self-contained block that
/// restates the CTM, the soft-mask state, both colours and the line parameters.
/// </summary>
internal sealed class PsPageWriter
{
    /// <summary>The name the single always-present graphics state is filed under.</summary>
    public const string AlphaStateName = "Alpha1";

    private readonly StringBuilder _content = new();
    private readonly Dictionary<string, PdfDictionary> _resources = new(StringComparer.Ordinal);
    private bool _pageOpened;

    /// <summary>Create a writer for a page of the given size in points.</summary>
    public PsPageWriter(double width, double height)
    {
        Width = width;
        Height = height;
    }

    /// <summary>Page width in points.</summary>
    public double Width { get; }

    /// <summary>Page height in points.</summary>
    public double Height { get; }

    /// <summary>Whether anything has been painted.</summary>
    public bool HasContent { get; private set; }

    /// <summary>The content stream built so far.</summary>
    public string Content => _content.ToString();

    /// <summary>Write the page prologue: the identity CTM and the clip to the media
    /// box that the reference converter opens every page with.</summary>
    public void OpenPage()
    {
        if (_pageOpened) return;
        _pageOpened = true;
        _content.Append("1 0 0 1 0 0 cm\n");
        _content.Append("0 0 m\n")
            .Append(N(Width)).Append(" 0 l\n")
            .Append(N(Width)).Append(' ').Append(N(Height)).Append(" l\n")
            .Append("0 ").Append(N(Height)).Append(" l\n")
            .Append("h\nW\nn\n");
        DeclareAlphaState();
    }

    /// <summary>Paint a path. The path arrives in device space, so the block states
    /// the identity CTM and lists the points as they are.</summary>
    public void PaintPath(PsPath path, PsGraphicsState state, PsPaintMode mode)
    {
        if (path is null || path.IsEmpty) return;
        OpenPage();
        _content.Append("q\n");
        WriteClip(state);
        // A stroke's pen is shaped by the transformation in force when the path is
        // painted, which a non-uniform scale turns into an ellipse. PDF can only
        // express that through the transformation itself, so the block states the
        // whole matrix and gives the path in the space that matrix applies to. A fill
        // has no pen, so its geometry stays in device space under the identity.
        var strokeSpace = mode == PsPaintMode.Stroke ? StrokeSpace(state) : (PsMatrix?)null;
        WriteStateBlock(state, mode, strokeSpace);
        WritePathGeometry(strokeSpace is null ? path : Mapped(path, strokeSpace.Value.Invert()));
        _content.Append(PaintOperator(mode)).Append("\nQ\n");
        HasContent = true;
    }

    /// <summary>The matrix a stroke is written under, or null when the state's own
    /// matrix cannot be inverted and the device-space fallback must be used.</summary>
    private static PsMatrix? StrokeSpace(PsGraphicsState state)
    {
        var ctm = state.Ctm;
        return Math.Abs(ctm.Determinant) < MinInvertibleArea ? null : ctm;
    }

    /// <summary>Below this a matrix has no area worth inverting.</summary>
    private const double MinInvertibleArea = 1e-9;

    /// <summary>A copy of a path with every point mapped through a matrix.</summary>
    private static PsPath Mapped(PsPath path, PsMatrix m)
    {
        var mapped = new PsPath();
        foreach (var s in path.Segments)
        {
            m.Transform(s.X, s.Y, out var x, out var y);
            switch (s.Kind)
            {
                case PsSegmentKind.Move:
                    mapped.MoveTo(x, y);
                    break;
                case PsSegmentKind.Line:
                    mapped.LineTo(x, y);
                    break;
                case PsSegmentKind.Curve:
                    m.Transform(s.X1, s.Y1, out var x1, out var y1);
                    m.Transform(s.X2, s.Y2, out var x2, out var y2);
                    mapped.CurveTo(x1, y1, x2, y2, x, y);
                    break;
                default:
                    mapped.ClosePath();
                    break;
            }
        }

        return mapped;
    }

    /// <summary>Show a string. Placement rides entirely on the CTM, so the text
    /// object itself always seats at the origin — the shape the reference converter
    /// emits for every show in the corpus.</summary>
    public void ShowText(PsMatrix textMatrix, string fontResource, double size,
        byte[] codes, PsGraphicsState state)
    {
        if (codes is null || codes.Length == 0) return;
        OpenPage();
        _content.Append("q\n");
        WriteClip(state);
        WriteMatrix(textMatrix);
        _content.Append('/').Append(AlphaStateName).Append(" gs\n");
        WriteColor(state.Color, state.PatternName, state.PatternComponents);
        _content.Append("BT\n0 0 Td\n0 Tr\n/").Append(fontResource).Append(' ')
            .Append(N(size)).Append(" Tf\n").Append(Literal(codes)).Append(" Tj\nET\nQ\n");
        HasContent = true;
    }

    /// <summary>Draw an image XObject under the given matrix.</summary>
    public void DrawXObject(PsMatrix placement, string name, PsGraphicsState state)
    {
        OpenPage();
        _content.Append("q\n");
        WriteClip(state);
        _content.Append('/').Append(AlphaStateName).Append(" gs\n");
        WriteMatrix(placement);
        _content.Append('/').Append(name).Append(" Do\nQ\n");
        HasContent = true;
    }

    /// <summary>State the CTM, the alpha state, both colours and the line parameters
    /// this paint needs. Fill-only paints still state the stroke colour, matching the
    /// reference converter's unconditional pair.</summary>
    private void WriteStateBlock(PsGraphicsState state, PsPaintMode mode, PsMatrix? strokeSpace)
    {
        WriteMatrix(strokeSpace ?? PsMatrix.Identity);
        _content.Append('/').Append(AlphaStateName).Append(" gs\n");
        WriteColor(state.Color, state.PatternName, state.PatternComponents);
        if (mode != PsPaintMode.Fill && mode != PsPaintMode.EoFill)
            WriteStrokeParameters(state, strokeSpace != null);
    }

    /// <summary>Both colour operators. A pattern is named in the pattern space, with
    /// the components in front of it for an uncoloured one, which takes its colour
    /// from the caller rather than from its own cell.</summary>
    private void WriteColor(PsColor color, string? patternName = null,
        double[]? patternComponents = null)
    {
        if (patternName != null)
        {
            var operands = new StringBuilder();
            foreach (var c in patternComponents ?? new double[0])
                operands.Append(N(c)).Append(' ');
            operands.Append('/').Append(patternName);
            _content.Append("/Pattern cs\n").Append(operands).Append(" scn\n")
                .Append("/Pattern CS\n").Append(operands).Append(" SCN\n");
            return;
        }

        var rgb = N(color.Red) + " " + N(color.Green) + " " + N(color.Blue);
        _content.Append(rgb).Append(" rg\n").Append(rgb).Append(" RG\n");
    }

    /// <summary>Width, cap, join, miter limit and dash, each only when it differs
    /// from the PDF default — except the cap, which is always stated.</summary>
    private void WriteStrokeParameters(PsGraphicsState state, bool inUserSpace)
    {
        // Under the state's own matrix the width is the width the program set; in the
        // device-space fallback it has to carry the matrix's scale itself.
        var width = inUserSpace ? state.LineWidth : state.LineWidth * state.Ctm.ApproximateScale;
        _content.Append(N(width)).Append(" w\n");
        _content.Append(state.LineCap.ToString(CultureInfo.InvariantCulture)).Append(" J\n");
        if (state.LineJoin != 0)
            _content.Append(state.LineJoin.ToString(CultureInfo.InvariantCulture)).Append(" j\n");
        if (state.MiterLimit != PsGraphicsState.DefaultMiterLimit)
            _content.Append(N(state.MiterLimit)).Append(" M\n");
        WriteDash(state, inUserSpace);
    }

    private void WriteDash(PsGraphicsState state, bool inUserSpace)
    {
        if (state.DashArray.Length == 0) return;
        var scale = inUserSpace ? 1 : state.Ctm.ApproximateScale;
        _content.Append('[');
        for (var i = 0; i < state.DashArray.Length; i++)
        {
            if (i > 0) _content.Append(' ');
            _content.Append(N(state.DashArray[i] * scale));
        }

        _content.Append("] ").Append(N(state.DashPhase * scale)).Append(" d\n");
    }

    /// <summary>Re-establish the clip inside the block, because each block is its own
    /// q/Q pair and a clip set by an earlier block would already have been undone.</summary>
    private void WriteClip(PsGraphicsState state)
    {
        var stated = false;
        foreach (var clip in state.Clips)
        {
            if (clip.Path.IsEmpty) continue;
            if (!stated)
            {
                _content.Append("1 0 0 1 0 0 cm\n");
                stated = true;
            }

            WritePathGeometry(clip.Path);
            _content.Append(clip.EvenOdd ? "W*\nn\n" : "W\nn\n");
        }
    }

    private void WritePathGeometry(PsPath path)
    {
        foreach (var s in path.Segments)
        {
            switch (s.Kind)
            {
                case PsSegmentKind.Move:
                    _content.Append(N(s.X)).Append(' ').Append(N(s.Y)).Append(" m\n");
                    break;
                case PsSegmentKind.Line:
                    _content.Append(N(s.X)).Append(' ').Append(N(s.Y)).Append(" l\n");
                    break;
                case PsSegmentKind.Curve:
                    _content.Append(N(s.X1)).Append(' ').Append(N(s.Y1)).Append(' ')
                        .Append(N(s.X2)).Append(' ').Append(N(s.Y2)).Append(' ')
                        .Append(N(s.X)).Append(' ').Append(N(s.Y)).Append(" c\n");
                    break;
                default:
                    _content.Append("h\n");
                    break;
            }
        }
    }

    private void WriteMatrix(PsMatrix m)
    {
        _content.Append(N(m.A)).Append(' ').Append(N(m.B)).Append(' ')
            .Append(N(m.C)).Append(' ').Append(N(m.D)).Append(' ')
            .Append(N(m.E)).Append(' ').Append(N(m.F)).Append(" cm\n");
    }

    private static string PaintOperator(PsPaintMode mode) => mode switch
    {
        PsPaintMode.Fill => "f",
        PsPaintMode.EoFill => "f*",
        PsPaintMode.Stroke => "S",
        _ => "n",
    };

    /// <summary>Register the one graphics state every block references.</summary>
    private void DeclareAlphaState()
    {
        var gs = new PdfDictionary();
        gs.Set("ca", new PdfInteger(1));
        gs.Set("CA", new PdfInteger(1));
        gs.Set("BM", new PdfName("Normal"));
        gs.Set("AIS", PdfBoolean.False);
        AddResource("ExtGState", AlphaStateName, gs);
    }

    /// <summary>File an object under a resource category.</summary>
    public void AddResource(string category, string name, PdfObject value)
    {
        if (!_resources.TryGetValue(category, out var dict))
        {
            dict = new PdfDictionary();
            _resources[category] = dict;
        }

        dict.Set(name, value);
    }

    /// <summary>A fresh name for the next image XObject.</summary>
    public string NextImageName() => "Img" + _imageCount++;

    private int _imageCount;

    /// <summary>Suppress the page prologue, for a writer that is building the inside
    /// of something — a pattern cell — rather than a page of its own.</summary>
    public void SuppressPagePrologue() => _pageOpened = true;

    /// <summary>The resources this writer accumulated, as a dictionary ready to be
    /// used as the <c>/Resources</c> of whatever the content belongs to.</summary>
    public PdfDictionary CollectedResources()
    {
        var resources = new PdfDictionary();
        resources.Set("ProcSet", new PdfArray(new List<PdfObject>
        {
            new PdfName("PDF"), new PdfName("Text"), new PdfName("ImageC"),
        }));
        foreach (var pair in _resources) resources.Set(pair.Key, pair.Value);
        return resources;
    }

    /// <summary>Whether a resource is already filed under a name.</summary>
    public bool HasResource(string category, string name) =>
        _resources.TryGetValue(category, out var dict) && dict.Get(name) is not null;

    /// <summary>Copy the accumulated resources onto a page, adding the procedure-set
    /// list the reference converter always writes.</summary>
    public void ApplyResources(Page page)
    {
        if (page.Dict.Get("Resources") is not PdfDictionary resources)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }

        resources.Set("ProcSet", new PdfArray(new List<PdfObject>
        {
            new PdfName("PDF"), new PdfName("Text"), new PdfName("ImageC"),
        }));
        foreach (var pair in _resources)
        {
            if (resources.Get(pair.Key) is not PdfDictionary target)
            {
                target = new PdfDictionary();
                resources.Set(pair.Key, target);
            }

            foreach (var key in pair.Value.Keys) target.Set(key, pair.Value.Get(key)!);
        }
    }

    /// <summary>Format a number the way a content stream wants it: no exponent, no
    /// trailing zeros, and a plain zero rather than a negative one.</summary>
    public static string N(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "0";
        var rounded = Math.Round(value, ContentDecimals);
        if (rounded == 0) return "0";
        return rounded.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    /// <summary>How many decimals a content-stream number keeps.</summary>
    private const int ContentDecimals = 5;

    /// <summary>Wrap bytes as a PDF literal string, escaping what must be escaped.</summary>
    public static string Literal(byte[] codes)
    {
        var sb = new StringBuilder(codes.Length + 2);
        sb.Append('(');
        foreach (var b in codes)
        {
            if (b == '(' || b == ')' || b == '\\') sb.Append('\\').Append((char)b);
            else if (b == '\r') sb.Append("\\r");
            else if (b == '\n') sb.Append("\\n");
            else sb.Append((char)b);
        }

        return sb.Append(')').ToString();
    }
}

/// <summary>How a path is painted.</summary>
internal enum PsPaintMode
{
    /// <summary>Fill with the non-zero winding rule.</summary>
    Fill,

    /// <summary>Fill with the even-odd rule.</summary>
    EoFill,

    /// <summary>Stroke.</summary>
    Stroke,

    /// <summary>Paint nothing; used when a path only establishes a clip.</summary>
    None,
}
