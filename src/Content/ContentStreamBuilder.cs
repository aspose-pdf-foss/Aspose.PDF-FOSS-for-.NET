using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Content;

/// <summary>
/// Builds PDF content stream bytes using the operator syntax from PDF32000 §8-9.
/// </summary>
public sealed class ContentStreamBuilder
{
    private readonly StringBuilder _sb = new();

    // Graphics state
    public ContentStreamBuilder SaveState() { _sb.Append("q\n"); return this; }
    public ContentStreamBuilder RestoreState() { _sb.Append("Q\n"); return this; }

    public ContentStreamBuilder SetMatrix(double a, double b, double c, double d, double e, double f)
    {
        _sb.Append($"{F(a)} {F(b)} {F(c)} {F(d)} {F(e)} {F(f)} cm\n");
        return this;
    }

    // Color
    public ContentStreamBuilder SetFillColor(double r, double g, double b)
    {
        _sb.Append($"{Fc(r)} {Fc(g)} {Fc(b)} rg\n");
        return this;
    }

    // Color.R/.G/.B are bytes (0–255); PDF rg/RG operators expect 0–1.
    public ContentStreamBuilder SetFillColor(Color color)
        => SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    public ContentStreamBuilder SetStrokeColor(double r, double g, double b)
    {
        _sb.Append($"{Fc(r)} {Fc(g)} {Fc(b)} RG\n");
        return this;
    }

    public ContentStreamBuilder SetStrokeColor(Color color)
        => SetStrokeColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);

    public ContentStreamBuilder SetFillGray(double gray)
    {
        _sb.Append($"{Fc(gray)} g\n");
        return this;
    }

    public ContentStreamBuilder SetStrokeGray(double gray)
    {
        _sb.Append($"{Fc(gray)} G\n");
        return this;
    }

    // Line width
    public ContentStreamBuilder SetLineWidth(double width)
    {
        _sb.Append($"{F(width)} w\n");
        return this;
    }

    // Dash pattern
    public ContentStreamBuilder SetDashPattern(double[] pattern, double phase = 0)
    {
        _sb.Append('[');
        for (var i = 0; i < pattern.Length; i++)
        {
            if (i > 0) _sb.Append(' ');
            _sb.Append(F(pattern[i]));
        }
        _sb.Append($"] {F(phase)} d\n");
        return this;
    }

    // Line cap style (0=butt, 1=round, 2=square)
    public ContentStreamBuilder SetLineCap(int cap)
    {
        _sb.Append($"{cap} J\n");
        return this;
    }

    // Line join style (0=miter, 1=round, 2=bevel)
    public ContentStreamBuilder SetLineJoin(int join)
    {
        _sb.Append($"{join} j\n");
        return this;
    }

    // Path construction
    public ContentStreamBuilder MoveTo(double x, double y)
    {
        _sb.Append($"{F(x)} {F(y)} m\n");
        return this;
    }

    public ContentStreamBuilder LineTo(double x, double y)
    {
        _sb.Append($"{F(x)} {F(y)} l\n");
        return this;
    }

    public ContentStreamBuilder CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        _sb.Append($"{F(x1)} {F(y1)} {F(x2)} {F(y2)} {F(x3)} {F(y3)} c\n");
        return this;
    }

    public ContentStreamBuilder Rectangle(double x, double y, double width, double height)
    {
        _sb.Append($"{F(x)} {F(y)} {F(width)} {F(height)} re\n");
        return this;
    }

    public ContentStreamBuilder ClosePath() { _sb.Append("h\n"); return this; }

    // Path painting
    public ContentStreamBuilder Stroke() { _sb.Append("S\n"); return this; }
    public ContentStreamBuilder Fill() { _sb.Append("f\n"); return this; }
    public ContentStreamBuilder FillEvenOdd() { _sb.Append("f*\n"); return this; }
    public ContentStreamBuilder FillAndStroke() { _sb.Append("B\n"); return this; }
    public ContentStreamBuilder CloseAndStroke() { _sb.Append("s\n"); return this; }
    public ContentStreamBuilder EndPath() { _sb.Append("n\n"); return this; }

    // Clipping
    public ContentStreamBuilder Clip() { _sb.Append("W n\n"); return this; }
    public ContentStreamBuilder ClipEvenOdd() { _sb.Append("W* n\n"); return this; }

    // Text
    public ContentStreamBuilder BeginText() { _sb.Append("BT\n"); return this; }
    public ContentStreamBuilder EndText() { _sb.Append("ET\n"); return this; }

    public ContentStreamBuilder SetFont(string fontName, double size)
    {
        _sb.Append($"/{fontName} {F(size)} Tf\n");
        return this;
    }

    public ContentStreamBuilder MoveTextPosition(double tx, double ty)
    {
        _sb.Append($"{F(tx)} {F(ty)} Td\n");
        return this;
    }

    public ContentStreamBuilder SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
        _sb.Append($"{F(a)} {F(b)} {F(c)} {F(d)} {F(e)} {F(f)} Tm\n");
        return this;
    }

    public ContentStreamBuilder SetLeading(double leading)
    {
        _sb.Append($"{F(leading)} TL\n");
        return this;
    }

    public ContentStreamBuilder NextLine() { _sb.Append("T*\n"); return this; }

    public ContentStreamBuilder SetCharSpacing(double spacing)
    {
        _sb.Append($"{F(spacing)} Tc\n");
        return this;
    }

    public ContentStreamBuilder SetWordSpacing(double spacing)
    {
        _sb.Append($"{F(spacing)} Tw\n");
        return this;
    }

    public ContentStreamBuilder SetTextRenderingMode(int mode)
    {
        _sb.Append($"{mode} Tr\n");
        return this;
    }

    public ContentStreamBuilder ShowText(string text)
    {
        _sb.Append('(');
        foreach (var c in text)
        {
            if (c is '(' or ')' or '\\')
                _sb.Append('\\');
            _sb.Append(c);
        }
        _sb.Append(") Tj\n");
        return this;
    }

    /// <summary>
    /// Show pre-encoded single-byte text. Each byte is written verbatim into a
    /// PDF literal string `(...)` after escaping the three reserved chars
    /// (paren-open, paren-close, backslash). Callers that have already mapped
    /// their text through a font /Encoding (WinAnsi, /Differences, etc.) use
    /// this to keep raw byte values byte-perfect through Build()'s Latin-1
    /// round-trip — see <see cref="ShowText"/> when source is a managed
    /// string that should be written 1 char ⇒ 1 byte without re-encoding.
    /// </summary>
    public ContentStreamBuilder ShowTextBytes(byte[] bytes)
    {
        _sb.Append('(');
        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
                _sb.Append('\\');
            _sb.Append((char)b);
        }
        _sb.Append(") Tj\n");
        return this;
    }

    /// <summary>Show text using a hex-encoded string (for CIDFont glyph IDs).</summary>
    public ContentStreamBuilder ShowTextHex(byte[] glyphIds)
    {
        _sb.Append('<');
        foreach (var b in glyphIds)
            _sb.Append(b.ToString("X2"));
        _sb.Append("> Tj\n");
        return this;
    }

    // XObject (images, form XObjects)
    public ContentStreamBuilder DrawXObject(string name)
    {
        _sb.Append($"/{name} Do\n");
        return this;
    }

    // Extended graphics state
    public ContentStreamBuilder SetExtGState(string name)
    {
        _sb.Append($"/{name} gs\n");
        return this;
    }

    // Marked content (for tagged PDF / artifacts)
    public ContentStreamBuilder BeginMarkedContent(string tag)
    {
        _sb.Append($"/{tag} BMC\n");
        return this;
    }

    public ContentStreamBuilder BeginMarkedContent(string tag, int mcid)
    {
        _sb.Append($"/{tag} <</MCID {mcid}>> BDC\n");
        return this;
    }

    /// <summary>BDC with an inline properties dictionary (already-serialized PDF dict literal).</summary>
    public ContentStreamBuilder BeginMarkedContentWithProps(string tag, string propsDictInline)
    {
        _sb.Append($"/{tag} {propsDictInline} BDC\n");
        return this;
    }

    public ContentStreamBuilder EndMarkedContent()
    {
        _sb.Append("EMC\n");
        return this;
    }

    /// <summary>
    /// Begin a marked content sequence using a MarkedContentInfo from a StructureElementBuilder.
    /// </summary>
    public ContentStreamBuilder BeginMarkedContent(Tagged.MarkedContentInfo info)
    {
        _sb.Append(info.BeginMarkedContent());
        return this;
    }

    // Raw operator
    public ContentStreamBuilder Raw(string operatorText)
    {
        _sb.Append(operatorText);
        if (!operatorText.EndsWith('\n'))
            _sb.Append('\n');
        return this;
    }

    /// <summary>
    /// Build the content stream as a byte array.
    /// </summary>
    /// <remarks>
    /// Uses ISO-8859-1 (Latin-1) so chars 0x00-0xFF in the builder round-trip
    /// to bytes 0x00-0xFF unchanged. This is what PDF needs for content
    /// streams: operators (q/Q/BT/ET/Tj/...) are ASCII, and `(...)` literal
    /// strings carry raw bytes that the reader interprets through the active
    /// font /Encoding. ASCII encoding (the prior default) silently rewrote
    /// every char ≥ 0x80 to '?', which broke any caller using
    /// WinAnsi-extended (ó, é, ñ, …) or /Differences-mapped Polish glyphs.
    /// Chars > 0xFF (not representable in Latin-1) still fall back to '?',
    /// which is correct because no single-byte PDF encoding can carry them
    /// in a literal string — those callers must use ShowTextHex with a CID
    /// font, or pre-map via /Differences before reaching the builder.
    /// </remarks>
    public byte[] Build() => Encoding.Latin1.GetBytes(_sb.ToString());

    /// <summary>
    /// Build the content stream as a string.
    /// </summary>
    public override string ToString() => _sb.ToString();

    // PDF content streams accept only plain decimal real numbers; the "G" format
    // emits scientific notation (e.g. 6.1E-05) for very small or very large
    // magnitudes, which a conforming reader rejects and the operator is dropped.
    // Use a fixed-decimal format that never produces an exponent, trimming
    // trailing zeros so common integer-valued coordinates stay compact.
    private static string F(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
        var s = v.ToString("0.######", CultureInfo.InvariantCulture);
        // "-0" can result from a tiny negative rounding to zero; normalise it.
        return s == "-0" ? "0" : s;
    }

    // Colour components (rg/RG/g/G operands) keep more precision than geometry:
    // Aspose.PDF for .NET writes e.g. 119/255 as "0.4666666667" (10 fractional digits), and
    // an exact-string check on the parsed operator needs that form.
    // 10 digits, no exponent, trailing zeros trimmed.
    private static string Fc(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
        var s = v.ToString("0.##########", CultureInfo.InvariantCulture);
        return s == "-0" ? "0" : s;
    }
}
