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
    /// precision than geometry — the reference writes e.g. 119/255 as "0.4666666667"
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
    void Visit(Aspose.Pdf.Operators.BDC BDC);
    void Visit(Aspose.Pdf.Operators.BI BI);
    void Visit(Aspose.Pdf.Operators.BMC BMC);
    void Visit(Aspose.Pdf.Operators.BT BT);
    void Visit(Aspose.Pdf.Operators.BX BX);
    void Visit(Aspose.Pdf.Operators.Clip W);
    void Visit(Aspose.Pdf.Operators.ClosePath h);
    void Visit(Aspose.Pdf.Operators.ClosePathEOFillStroke b_);
    void Visit(Aspose.Pdf.Operators.ClosePathFillStroke b);
    void Visit(Aspose.Pdf.Operators.ClosePathStroke s);
    void Visit(Aspose.Pdf.Operators.ConcatenateMatrix cm);
    void Visit(Aspose.Pdf.Operators.CurveTo c);
    void Visit(Aspose.Pdf.Operators.CurveTo1 v);
    void Visit(Aspose.Pdf.Operators.CurveTo2 y);
    void Visit(Aspose.Pdf.Operators.DP DP);
    void Visit(Aspose.Pdf.Operators.Do Do);
    void Visit(Aspose.Pdf.Operators.EI EI);
    void Visit(Aspose.Pdf.Operators.EMC EMC);
    void Visit(Aspose.Pdf.Operators.EOClip W_);
    void Visit(Aspose.Pdf.Operators.EOFill f_);
    void Visit(Aspose.Pdf.Operators.EOFillStroke B_);
    void Visit(Aspose.Pdf.Operators.ET ET);
    void Visit(Aspose.Pdf.Operators.EX EX);
    void Visit(Aspose.Pdf.Operators.EndPath n);
    void Visit(Aspose.Pdf.Operators.Fill f);
    void Visit(Aspose.Pdf.Operators.FillStroke B);
    void Visit(Aspose.Pdf.Operators.GRestore Q);
    void Visit(Aspose.Pdf.Operators.GS gs);
    void Visit(Aspose.Pdf.Operators.GSave q);
    void Visit(Aspose.Pdf.Operators.ID ID);
    void Visit(Aspose.Pdf.Operators.LineTo l);
    void Visit(Aspose.Pdf.Operators.MP MP);
    void Visit(Aspose.Pdf.Operators.MoveTextPosition Td);
    void Visit(Aspose.Pdf.Operators.MoveTextPositionSetLeading TD);
    void Visit(Aspose.Pdf.Operators.MoveTo m);
    void Visit(Aspose.Pdf.Operators.MoveToNextLine T_);
    void Visit(Aspose.Pdf.Operators.MoveToNextLineShowText _);
    void Visit(Aspose.Pdf.Operators.ObsoleteFill F);
    void Visit(Aspose.Pdf.Operators.Re re);
    void Visit(Aspose.Pdf.Operators.SelectFont Tf);
    void Visit(Aspose.Pdf.Operators.SetAdvancedColor scn);
    void Visit(Aspose.Pdf.Operators.SetAdvancedColorStroke SCN);
    void Visit(Aspose.Pdf.Operators.SetCMYKColor k);
    void Visit(Aspose.Pdf.Operators.SetCMYKColorStroke K);
    void Visit(Aspose.Pdf.Operators.SetCharWidth d0);
    void Visit(Aspose.Pdf.Operators.SetCharWidthBoundingBox d1);
    void Visit(Aspose.Pdf.Operators.SetCharacterSpacing Tc);
    void Visit(Aspose.Pdf.Operators.SetColor sc);
    void Visit(Aspose.Pdf.Operators.SetColorRenderingIntent ri);
    void Visit(Aspose.Pdf.Operators.SetColorSpace cs);
    void Visit(Aspose.Pdf.Operators.SetColorSpaceStroke CS);
    void Visit(Aspose.Pdf.Operators.SetColorStroke SC);
    void Visit(Aspose.Pdf.Operators.SetDash d);
    void Visit(Aspose.Pdf.Operators.SetFlat i);
    void Visit(Aspose.Pdf.Operators.SetGlyphsPositionShowText TJ);
    void Visit(Aspose.Pdf.Operators.SetGray g);
    void Visit(Aspose.Pdf.Operators.SetGrayStroke G);
    void Visit(Aspose.Pdf.Operators.SetHorizontalTextScaling Tz);
    void Visit(Aspose.Pdf.Operators.SetLineCap J);
    void Visit(Aspose.Pdf.Operators.SetLineJoin j);
    void Visit(Aspose.Pdf.Operators.SetLineWidth w);
    void Visit(Aspose.Pdf.Operators.SetMiterLimit M);
    void Visit(Aspose.Pdf.Operators.SetRGBColor rg);
    void Visit(Aspose.Pdf.Operators.SetRGBColorStroke RG);
    void Visit(Aspose.Pdf.Operators.SetSpacingMoveToNextLineShowText __);
    void Visit(Aspose.Pdf.Operators.SetTextLeading TL);
    void Visit(Aspose.Pdf.Operators.SetTextMatrix Tm);
    void Visit(Aspose.Pdf.Operators.SetTextRenderingMode Tr);
    void Visit(Aspose.Pdf.Operators.SetTextRise Ts);
    void Visit(Aspose.Pdf.Operators.SetWordSpacing Tw);
    void Visit(Aspose.Pdf.Operators.ShFill sh);
    void Visit(Aspose.Pdf.Operators.ShowText Tj);
    void Visit(Aspose.Pdf.Operators.Stroke S);
    void Visit(Aspose.Pdf.Operators.TextOperator textOperator);
}
