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
/// equals the template's — mirroring the public API's expectation that a caller
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
        if (_template is not null && !Matches(op, _template)) return;
        Selected.Add(op);
    }

    // Template matching is wider than type equality for the text families:
    //  * any text-STATE operator template (Tc/Tw/Tz/TL/Tf/Tr/Ts) admits every
    //    text-state operator, and any text-PLACE template (Td/TD/Tm/T*) admits
    //    every text-place operator;
    //  * T* additionally answers to EVERY text-operator template (show, state,
    //    place or BT/ET) — but never to a non-text template;
    //  * a plain TextShowOperator template admits every text-show operator;
    //  * everything else (the concrete text-show ops, BT/ET, path/paint/state ops) matches
    //    on exact runtime type only.
    private static bool Matches(Operator op, Operator template)
    {
        if (op.GetType() == template.GetType()) return true;
        if (op is Operators.TextStateOperator && template is Operators.TextStateOperator) return true;
        if (op is Operators.TextPlaceOperator && template is Operators.TextPlaceOperator) return true;
        if (op is Operators.MoveToNextLine && template is Operators.TextOperator) return true;
        // A plain TextShowOperator template stands for the whole text-show family: Tj, TJ, ' and ".
        if (template.GetType() == typeof(Operators.TextShowOperator) && op is Operators.TextShowOperator) return true;
        return false;
    }

    /// <summary>Visits a <c>BDC</c> (begin marked content with properties) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(BDC BDC) => Match(BDC);
    /// <summary>Visits a <c>BI</c> (begin inline image) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(BI BI) => Match(BI);
    /// <summary>Visits a <c>BMC</c> (begin marked content) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(BMC BMC) => Match(BMC);
    /// <summary>Visits a <c>BT</c> (begin text object) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(BT BT) => Match(BT);
    /// <summary>Visits a <c>BX</c> (begin compatibility section) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(BX BX) => Match(BX);
    /// <summary>Visits a <c>W</c> (nonzero clip) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(Clip W) => Match(W);
    /// <summary>Visits an <c>h</c> (close subpath) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ClosePath h) => Match(h);
    /// <summary>Visits a <c>b*</c> (close, even-odd fill and stroke) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ClosePathEOFillStroke b_) => Match(b_);
    /// <summary>Visits a <c>b</c> (close, fill and stroke) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ClosePathFillStroke b) => Match(b);
    /// <summary>Visits an <c>s</c> (close and stroke) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ClosePathStroke s) => Match(s);
    /// <summary>Visits a <c>cm</c> (concatenate matrix) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ConcatenateMatrix cm) => Match(cm);
    /// <summary>Visits a <c>c</c> (Bezier curve) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(CurveTo c) => Match(c);
    /// <summary>Visits a <c>v</c> (Bezier curve, first control point at the current point) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(CurveTo1 v) => Match(v);
    /// <summary>Visits a <c>y</c> (Bezier curve, second control point at the end point) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(CurveTo2 y) => Match(y);
    /// <summary>Visits a <c>DP</c> (marked-content point with properties) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(DP DP) => Match(DP);
    /// <summary>Visits a <c>Do</c> (paint XObject) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(Do Do) => Match(Do);
    /// <summary>Visits an <c>EI</c> (end inline image) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EI EI) => Match(EI);
    /// <summary>Visits an <c>EMC</c> (end marked content) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EMC EMC) => Match(EMC);
    /// <summary>Visits a <c>W*</c> (even-odd clip) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EOClip W_) => Match(W_);
    /// <summary>Visits an <c>f*</c> (even-odd fill) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EOFill f_) => Match(f_);
    /// <summary>Visits a <c>B*</c> (even-odd fill and stroke) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EOFillStroke B_) => Match(B_);
    /// <summary>Visits an <c>ET</c> (end text object) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ET ET) => Match(ET);
    /// <summary>Visits an <c>EX</c> (end compatibility section) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EX EX) => Match(EX);
    /// <summary>Visits an <c>n</c> (end path without painting) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(EndPath n) => Match(n);
    /// <summary>Visits an <c>f</c> (nonzero fill) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(Fill f) => Match(f);
    /// <summary>Visits a <c>B</c> (nonzero fill and stroke) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(FillStroke B) => Match(B);
    /// <summary>Visits a <c>Q</c> (restore graphics state) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(GRestore Q) => Match(Q);
    /// <summary>Visits a <c>gs</c> (set extended graphics state) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(GS gs) => Match(gs);
    /// <summary>Visits a <c>q</c> (save graphics state) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(GSave q) => Match(q);
    /// <summary>Visits an <c>ID</c> (inline image data) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ID ID) => Match(ID);
    /// <summary>Visits an <c>l</c> (line to) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(LineTo l) => Match(l);
    /// <summary>Visits an <c>MP</c> (marked-content point) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(MP MP) => Match(MP);
    /// <summary>Visits a <c>Td</c> (move text position) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(MoveTextPosition Td) => Match(Td);
    /// <summary>Visits a <c>TD</c> (move text position and set leading) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(MoveTextPositionSetLeading TD) => Match(TD);
    /// <summary>Visits an <c>m</c> (move to) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(MoveTo m) => Match(m);
    /// <summary>Visits a <c>T*</c> (move to next line) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(MoveToNextLine T_) => Match(T_);
    /// <summary>Visits a <c>'</c> (move to next line and show text) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(MoveToNextLineShowText _) => Match(_);
    /// <summary>Visits an <c>F</c> (obsolete nonzero fill) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ObsoleteFill F) => Match(F);
    /// <summary>Visits a <c>re</c> (append rectangle) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(Re re) => Match(re);
    /// <summary>Visits a <c>Tf</c> (select font and size) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SelectFont Tf) => Match(Tf);
    /// <summary>Visits an <c>scn</c> (set fill colour, extended) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetAdvancedColor scn) => Match(scn);
    /// <summary>Visits an <c>SCN</c> (set stroke colour, extended) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetAdvancedColorStroke SCN) => Match(SCN);
    /// <summary>Visits a <c>k</c> (set CMYK fill colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetCMYKColor k) => Match(k);
    /// <summary>Visits a <c>K</c> (set CMYK stroke colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetCMYKColorStroke K) => Match(K);
    /// <summary>Visits a <c>d0</c> (Type 3 glyph width) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetCharWidth d0) => Match(d0);
    /// <summary>Visits a <c>d1</c> (Type 3 glyph width and bounding box) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetCharWidthBoundingBox d1) => Match(d1);
    /// <summary>Visits a <c>Tc</c> (set character spacing) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetCharacterSpacing Tc) => Match(Tc);
    /// <summary>Visits an <c>sc</c> (set fill colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetColor sc) => Match(sc);
    /// <summary>Visits an <c>ri</c> (set rendering intent) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetColorRenderingIntent ri) => Match(ri);
    /// <summary>Visits a <c>cs</c> (set fill colour space) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetColorSpace cs) => Match(cs);
    /// <summary>Visits a <c>CS</c> (set stroke colour space) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetColorSpaceStroke CS) => Match(CS);
    /// <summary>Visits an <c>SC</c> (set stroke colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetColorStroke SC) => Match(SC);
    /// <summary>Visits a <c>d</c> (set dash pattern) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetDash d) => Match(d);
    /// <summary>Visits an <c>i</c> (set flatness tolerance) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetFlat i) => Match(i);
    /// <summary>Visits a <c>TJ</c> (show text with individual glyph positioning) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetGlyphsPositionShowText TJ) => Match(TJ);
    /// <summary>Visits a <c>g</c> (set gray fill colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetGray g) => Match(g);
    /// <summary>Visits a <c>G</c> (set gray stroke colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetGrayStroke G) => Match(G);
    /// <summary>Visits a <c>Tz</c> (set horizontal text scaling) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetHorizontalTextScaling Tz) => Match(Tz);
    /// <summary>Visits a <c>J</c> (set line cap) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetLineCap J) => Match(J);
    /// <summary>Visits a <c>j</c> (set line join) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetLineJoin j) => Match(j);
    /// <summary>Visits a <c>w</c> (set line width) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetLineWidth w) => Match(w);
    /// <summary>Visits an <c>M</c> (set miter limit) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetMiterLimit M) => Match(M);
    /// <summary>Visits an <c>rg</c> (set RGB fill colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetRGBColor rg) => Match(rg);
    /// <summary>Visits an <c>RG</c> (set RGB stroke colour) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetRGBColorStroke RG) => Match(RG);
    /// <summary>Visits a <c>"</c> (set spacing, move to next line and show text) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetSpacingMoveToNextLineShowText __) => Match(__);
    /// <summary>Visits a <c>TL</c> (set text leading) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetTextLeading TL) => Match(TL);
    /// <summary>Visits a <c>Tm</c> (set text matrix) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetTextMatrix Tm) => Match(Tm);
    /// <summary>Visits a <c>Tr</c> (set text rendering mode) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetTextRenderingMode Tr) => Match(Tr);
    /// <summary>Visits a <c>Ts</c> (set text rise) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetTextRise Ts) => Match(Ts);
    /// <summary>Visits a <c>Tw</c> (set word spacing) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(SetWordSpacing Tw) => Match(Tw);
    /// <summary>Visits an <c>sh</c> (paint shading) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ShFill sh) => Match(sh);
    /// <summary>Visits a <c>Tj</c> (show text) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(ShowText Tj) => Match(Tj);
    /// <summary>Visits an <c>S</c> (stroke path) operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(Stroke S) => Match(S);
    /// <summary>Visits any other text operator; adds it to <c>Selected</c> unless the selector template excludes it.</summary>
    public virtual void Visit(TextOperator textOperator) => Match(textOperator);
}
