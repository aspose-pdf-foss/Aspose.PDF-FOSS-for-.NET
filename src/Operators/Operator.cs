namespace Aspose.Pdf;

/// <summary>Top-level base for all PDF content-stream operators. The
/// concrete operator subclasses live in <see cref="Aspose.Pdf.Operators"/>
/// (BT, ET, GSave, GRestore, SelectFont, SetRGBColor, MoveTo, LineTo, …);
/// this base sits at the public-API namespace so callers and facade
/// signatures can pass <c>Aspose.Pdf.Operator</c> through (matches the
/// public reflection surface).</summary>
public abstract class Operator
{
    /// <summary>Serialize this operator to PDF syntax.</summary>
    public abstract string ToPdf();

    /// <summary>The PDF command name (last token of the serialised form,
    /// e.g. <c>"q"</c>, <c>"BT"</c>, <c>"Tf"</c>, <c>"rg"</c>). Typed
    /// subclasses can override for cheaper access; the default extracts it
    /// from <see cref="ToPdf"/>.</summary>
    public virtual string CommandName
    {
        get
        {
            var s = ToPdf().TrimEnd();
            var sp = s.LastIndexOf(' ');
            return sp >= 0 ? s[(sp + 1)..] : s;
        }
    }

    /// <summary>Default string form is the PDF serialisation, so callers
    /// (test helpers, debug logs) get the same content-stream text whether
    /// they hold an unparsed RawOperator or a typed subclass.</summary>
    public override string ToString() => ToPdf();

    /// <summary>Format a double in the canonical PDF-content-stream form
    /// (invariant culture, up to 6 fractional digits, no trailing zeros).</summary>
    protected static string Fmt(double v)
        => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Format a colour component (rg/RG/g/G/k/K operand) with more
    /// precision than geometry — e.g. 119/255 is written as "0.4666666667"
    /// (10 fractional digits), and round-tripping such an operator must preserve it.
    /// 10 fractional digits, no exponent, trailing zeros trimmed.</summary>
    protected static string FmtColor(double v)
        => v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>1-based position of this operator within its containing
    /// <see cref="OperatorCollection"/>. Set by the collection when the
    /// operator is added or moved; 0 means "not in a collection".</summary>
    public int Index { get; set; }

    /// <summary>Dispatch this operator to <paramref name="visitor"/>. Concrete
    /// subclasses override to land on the correct <c>Visit(SubType)</c>
    /// overload; the base implementation is a no-op so callers operating on a
    /// raw <see cref="Operator"/> reference can still invoke
    /// <c>Accept</c>.</summary>
    public virtual void Accept(IOperatorSelector visitor) { _ = visitor; }

    /// <summary>True when <paramref name="op"/> is a text-showing operator
    /// (Tj / TJ / ' / " — concrete subclasses of
    /// <see cref="Aspose.Pdf.Operators.TextShowOperator"/>).</summary>
    public static bool IsTextShowOperator(Operator op)
        => op is Aspose.Pdf.Operators.TextShowOperator;

    /// <summary>True when this operator's serialised PDF form equals
    /// <paramref name="op"/>'s. Compares the canonical <see cref="ToPdf"/>
    /// output rather than reference identity.</summary>
    public bool ValueEquals(Operator op)
        => op is not null && string.Equals(ToPdf(), op.ToPdf(), StringComparison.Ordinal);
}

/// <summary>Visitor interface for <see cref="OperatorCollection.Accept(IOperatorSelector)"/>.
/// Implementors override the relevant <c>Visit(SubType)</c> overloads to
/// filter operators by concrete type. Visit dispatch itself is not currently
/// invoked by the FOSS OperatorCollection — the methods are declared so
/// callers (and reflection) match the public signature.</summary>
public interface IOperatorSelector
{
    /// <summary>Called for a <c>BDC</c> (begin marked content with properties) operator.</summary>
    void Visit(Aspose.Pdf.Operators.BDC BDC);
    /// <summary>Called for a <c>BI</c> (begin inline image) operator.</summary>
    void Visit(Aspose.Pdf.Operators.BI BI);
    /// <summary>Called for a <c>BMC</c> (begin marked content) operator.</summary>
    void Visit(Aspose.Pdf.Operators.BMC BMC);
    /// <summary>Called for a <c>BT</c> (begin text object) operator.</summary>
    void Visit(Aspose.Pdf.Operators.BT BT);
    /// <summary>Called for a <c>BX</c> (begin compatibility section) operator.</summary>
    void Visit(Aspose.Pdf.Operators.BX BX);
    /// <summary>Called for a <c>W</c> (nonzero clip) operator.</summary>
    void Visit(Aspose.Pdf.Operators.Clip W);
    /// <summary>Called for an <c>h</c> (close subpath) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ClosePath h);
    /// <summary>Called for a <c>b*</c> (close, even-odd fill and stroke) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ClosePathEOFillStroke b_);
    /// <summary>Called for a <c>b</c> (close, fill and stroke) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ClosePathFillStroke b);
    /// <summary>Called for an <c>s</c> (close and stroke) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ClosePathStroke s);
    /// <summary>Called for a <c>cm</c> (concatenate matrix) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ConcatenateMatrix cm);
    /// <summary>Called for a <c>c</c> (Bezier curve) operator.</summary>
    void Visit(Aspose.Pdf.Operators.CurveTo c);
    /// <summary>Called for a <c>v</c> (Bezier curve, first control point at the current point) operator.</summary>
    void Visit(Aspose.Pdf.Operators.CurveTo1 v);
    /// <summary>Called for a <c>y</c> (Bezier curve, second control point at the end point) operator.</summary>
    void Visit(Aspose.Pdf.Operators.CurveTo2 y);
    /// <summary>Called for a <c>DP</c> (marked-content point with properties) operator.</summary>
    void Visit(Aspose.Pdf.Operators.DP DP);
    /// <summary>Called for a <c>Do</c> (paint XObject) operator.</summary>
    void Visit(Aspose.Pdf.Operators.Do Do);
    /// <summary>Called for an <c>EI</c> (end inline image) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EI EI);
    /// <summary>Called for an <c>EMC</c> (end marked content) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EMC EMC);
    /// <summary>Called for a <c>W*</c> (even-odd clip) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EOClip W_);
    /// <summary>Called for an <c>f*</c> (even-odd fill) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EOFill f_);
    /// <summary>Called for a <c>B*</c> (even-odd fill and stroke) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EOFillStroke B_);
    /// <summary>Called for an <c>ET</c> (end text object) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ET ET);
    /// <summary>Called for an <c>EX</c> (end compatibility section) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EX EX);
    /// <summary>Called for an <c>n</c> (end path without painting) operator.</summary>
    void Visit(Aspose.Pdf.Operators.EndPath n);
    /// <summary>Called for an <c>f</c> (nonzero fill) operator.</summary>
    void Visit(Aspose.Pdf.Operators.Fill f);
    /// <summary>Called for a <c>B</c> (nonzero fill and stroke) operator.</summary>
    void Visit(Aspose.Pdf.Operators.FillStroke B);
    /// <summary>Called for a <c>Q</c> (restore graphics state) operator.</summary>
    void Visit(Aspose.Pdf.Operators.GRestore Q);
    /// <summary>Called for a <c>gs</c> (set extended graphics state) operator.</summary>
    void Visit(Aspose.Pdf.Operators.GS gs);
    /// <summary>Called for a <c>q</c> (save graphics state) operator.</summary>
    void Visit(Aspose.Pdf.Operators.GSave q);
    /// <summary>Called for an <c>ID</c> (inline image data) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ID ID);
    /// <summary>Called for an <c>l</c> (line to) operator.</summary>
    void Visit(Aspose.Pdf.Operators.LineTo l);
    /// <summary>Called for an <c>MP</c> (marked-content point) operator.</summary>
    void Visit(Aspose.Pdf.Operators.MP MP);
    /// <summary>Called for a <c>Td</c> (move text position) operator.</summary>
    void Visit(Aspose.Pdf.Operators.MoveTextPosition Td);
    /// <summary>Called for a <c>TD</c> (move text position and set leading) operator.</summary>
    void Visit(Aspose.Pdf.Operators.MoveTextPositionSetLeading TD);
    /// <summary>Called for an <c>m</c> (move to) operator.</summary>
    void Visit(Aspose.Pdf.Operators.MoveTo m);
    /// <summary>Called for a <c>T*</c> (move to next line) operator.</summary>
    void Visit(Aspose.Pdf.Operators.MoveToNextLine T_);
    /// <summary>Called for a <c>'</c> (move to next line and show text) operator.</summary>
    void Visit(Aspose.Pdf.Operators.MoveToNextLineShowText _);
    /// <summary>Called for an <c>F</c> (obsolete nonzero fill) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ObsoleteFill F);
    /// <summary>Called for a <c>re</c> (append rectangle) operator.</summary>
    void Visit(Aspose.Pdf.Operators.Re re);
    /// <summary>Called for a <c>Tf</c> (select font and size) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SelectFont Tf);
    /// <summary>Called for an <c>scn</c> (set fill colour, extended) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetAdvancedColor scn);
    /// <summary>Called for an <c>SCN</c> (set stroke colour, extended) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetAdvancedColorStroke SCN);
    /// <summary>Called for a <c>k</c> (set CMYK fill colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetCMYKColor k);
    /// <summary>Called for a <c>K</c> (set CMYK stroke colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetCMYKColorStroke K);
    /// <summary>Called for a <c>d0</c> (Type 3 glyph width) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetCharWidth d0);
    /// <summary>Called for a <c>d1</c> (Type 3 glyph width and bounding box) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetCharWidthBoundingBox d1);
    /// <summary>Called for a <c>Tc</c> (set character spacing) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetCharacterSpacing Tc);
    /// <summary>Called for an <c>sc</c> (set fill colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetColor sc);
    /// <summary>Called for an <c>ri</c> (set rendering intent) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetColorRenderingIntent ri);
    /// <summary>Called for a <c>cs</c> (set fill colour space) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetColorSpace cs);
    /// <summary>Called for a <c>CS</c> (set stroke colour space) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetColorSpaceStroke CS);
    /// <summary>Called for an <c>SC</c> (set stroke colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetColorStroke SC);
    /// <summary>Called for a <c>d</c> (set dash pattern) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetDash d);
    /// <summary>Called for an <c>i</c> (set flatness tolerance) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetFlat i);
    /// <summary>Called for a <c>TJ</c> (show text with individual glyph positioning) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetGlyphsPositionShowText TJ);
    /// <summary>Called for a <c>g</c> (set gray fill colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetGray g);
    /// <summary>Called for a <c>G</c> (set gray stroke colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetGrayStroke G);
    /// <summary>Called for a <c>Tz</c> (set horizontal text scaling) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetHorizontalTextScaling Tz);
    /// <summary>Called for a <c>J</c> (set line cap) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetLineCap J);
    /// <summary>Called for a <c>j</c> (set line join) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetLineJoin j);
    /// <summary>Called for a <c>w</c> (set line width) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetLineWidth w);
    /// <summary>Called for an <c>M</c> (set miter limit) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetMiterLimit M);
    /// <summary>Called for an <c>rg</c> (set RGB fill colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetRGBColor rg);
    /// <summary>Called for an <c>RG</c> (set RGB stroke colour) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetRGBColorStroke RG);
    /// <summary>Called for a <c>"</c> (set spacing, move to next line and show text) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetSpacingMoveToNextLineShowText __);
    /// <summary>Called for a <c>TL</c> (set text leading) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetTextLeading TL);
    /// <summary>Called for a <c>Tm</c> (set text matrix) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetTextMatrix Tm);
    /// <summary>Called for a <c>Tr</c> (set text rendering mode) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetTextRenderingMode Tr);
    /// <summary>Called for a <c>Ts</c> (set text rise) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetTextRise Ts);
    /// <summary>Called for a <c>Tw</c> (set word spacing) operator.</summary>
    void Visit(Aspose.Pdf.Operators.SetWordSpacing Tw);
    /// <summary>Called for an <c>sh</c> (paint shading) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ShFill sh);
    /// <summary>Called for a <c>Tj</c> (show text) operator.</summary>
    void Visit(Aspose.Pdf.Operators.ShowText Tj);
    /// <summary>Called for an <c>S</c> (stroke path) operator.</summary>
    void Visit(Aspose.Pdf.Operators.Stroke S);
    /// <summary>Called for a text operator that has no more specific <c>Visit</c> overload.</summary>
    void Visit(Aspose.Pdf.Operators.TextOperator textOperator);
}
