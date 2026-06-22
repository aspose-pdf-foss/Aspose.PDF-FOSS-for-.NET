namespace Aspose.Pdf;

using Aspose.Pdf.Operators;

/// <summary>
/// Visitor that filters content-stream operators by concrete type. Passed to
/// <see cref="OperatorCollection.Accept(IOperatorSelector)"/>, which walks the
/// collection and dispatches each operator to the matching <c>Visit</c>
/// overload; matched operators land in <see cref="Selected"/>.
///
/// When constructed with a template operator (e.g. <c>new OperatorSelector(new
/// Operators.Fill())</c>) the selector admits only operators whose runtime type
/// equals the template's — mirroring the Aspose.PDF for .NET expectation that a caller
/// asking for <c>Fill</c> gets back only <c>Fill</c> instances. The
/// parameterless constructor admits every visited operator.
/// </summary>
public class OperatorSelector : IOperatorSelector
{
    private readonly Operator? _template;

    /// <summary>Operators matched by the most recent Accept-walk.</summary>
    public System.Collections.Generic.IList<Operator> Selected { get; } = new System.Collections.Generic.List<Operator>();

    /// <summary>Create a selector that accepts every operator.</summary>
    public OperatorSelector() { }

    /// <summary>Create a selector that admits only operators whose runtime
    /// type matches <paramref name="op"/>.</summary>
    public OperatorSelector(Operator op) => _template = op;

    private void Match(Operator op)
    {
        if (op is null) return;
        if (_template is not null && op.GetType() != _template.GetType()) return;
        Selected.Add(op);
    }

    public virtual void Visit(BDC BDC) => Match(BDC);
    public virtual void Visit(BI BI) => Match(BI);
    public virtual void Visit(BMC BMC) => Match(BMC);
    public virtual void Visit(BT BT) => Match(BT);
    public virtual void Visit(BX BX) => Match(BX);
    public virtual void Visit(Clip W) => Match(W);
    public virtual void Visit(ClosePath h) => Match(h);
    public virtual void Visit(ClosePathEOFillStroke b_) => Match(b_);
    public virtual void Visit(ClosePathFillStroke b) => Match(b);
    public virtual void Visit(ClosePathStroke s) => Match(s);
    public virtual void Visit(ConcatenateMatrix cm) => Match(cm);
    public virtual void Visit(CurveTo c) => Match(c);
    public virtual void Visit(CurveTo1 v) => Match(v);
    public virtual void Visit(CurveTo2 y) => Match(y);
    public virtual void Visit(DP DP) => Match(DP);
    public virtual void Visit(Do Do) => Match(Do);
    public virtual void Visit(EI EI) => Match(EI);
    public virtual void Visit(EMC EMC) => Match(EMC);
    public virtual void Visit(EOClip W_) => Match(W_);
    public virtual void Visit(EOFill f_) => Match(f_);
    public virtual void Visit(EOFillStroke B_) => Match(B_);
    public virtual void Visit(ET ET) => Match(ET);
    public virtual void Visit(EX EX) => Match(EX);
    public virtual void Visit(EndPath n) => Match(n);
    public virtual void Visit(Fill f) => Match(f);
    public virtual void Visit(FillStroke B) => Match(B);
    public virtual void Visit(GRestore Q) => Match(Q);
    public virtual void Visit(GS gs) => Match(gs);
    public virtual void Visit(GSave q) => Match(q);
    public virtual void Visit(ID ID) => Match(ID);
    public virtual void Visit(LineTo l) => Match(l);
    public virtual void Visit(MP MP) => Match(MP);
    public virtual void Visit(MoveTextPosition Td) => Match(Td);
    public virtual void Visit(MoveTextPositionSetLeading TD) => Match(TD);
    public virtual void Visit(MoveTo m) => Match(m);
    public virtual void Visit(MoveToNextLine T_) => Match(T_);
    public virtual void Visit(MoveToNextLineShowText _) => Match(_);
    public virtual void Visit(ObsoleteFill F) => Match(F);
    public virtual void Visit(Re re) => Match(re);
    public virtual void Visit(SelectFont Tf) => Match(Tf);
    public virtual void Visit(SetAdvancedColor scn) => Match(scn);
    public virtual void Visit(SetAdvancedColorStroke SCN) => Match(SCN);
    public virtual void Visit(SetCMYKColor k) => Match(k);
    public virtual void Visit(SetCMYKColorStroke K) => Match(K);
    public virtual void Visit(SetCharWidth d0) => Match(d0);
    public virtual void Visit(SetCharWidthBoundingBox d1) => Match(d1);
    public virtual void Visit(SetCharacterSpacing Tc) => Match(Tc);
    public virtual void Visit(SetColor sc) => Match(sc);
    public virtual void Visit(SetColorRenderingIntent ri) => Match(ri);
    public virtual void Visit(SetColorSpace cs) => Match(cs);
    public virtual void Visit(SetColorSpaceStroke CS) => Match(CS);
    public virtual void Visit(SetColorStroke SC) => Match(SC);
    public virtual void Visit(SetDash d) => Match(d);
    public virtual void Visit(SetFlat i) => Match(i);
    public virtual void Visit(SetGlyphsPositionShowText TJ) => Match(TJ);
    public virtual void Visit(SetGray g) => Match(g);
    public virtual void Visit(SetGrayStroke G) => Match(G);
    public virtual void Visit(SetHorizontalTextScaling Tz) => Match(Tz);
    public virtual void Visit(SetLineCap J) => Match(J);
    public virtual void Visit(SetLineJoin j) => Match(j);
    public virtual void Visit(SetLineWidth w) => Match(w);
    public virtual void Visit(SetMiterLimit M) => Match(M);
    public virtual void Visit(SetRGBColor rg) => Match(rg);
    public virtual void Visit(SetRGBColorStroke RG) => Match(RG);
    public virtual void Visit(SetSpacingMoveToNextLineShowText __) => Match(__);
    public virtual void Visit(SetTextLeading TL) => Match(TL);
    public virtual void Visit(SetTextMatrix Tm) => Match(Tm);
    public virtual void Visit(SetTextRenderingMode Tr) => Match(Tr);
    public virtual void Visit(SetTextRise Ts) => Match(Ts);
    public virtual void Visit(SetWordSpacing Tw) => Match(Tw);
    public virtual void Visit(ShFill sh) => Match(sh);
    public virtual void Visit(ShowText Tj) => Match(Tj);
    public virtual void Visit(Stroke S) => Match(S);
    public virtual void Visit(TextOperator textOperator) => Match(textOperator);
}
