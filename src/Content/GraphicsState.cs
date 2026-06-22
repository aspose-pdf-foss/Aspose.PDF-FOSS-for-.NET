namespace Aspose.Pdf.Content;

/// <summary>
/// Tracks the PDF graphics state during content stream parsing (PDF32000 §8.4).
/// Supports the graphics state stack (q/Q operators) and all state-modifying operators.
/// </summary>
public sealed class GraphicsState
{
    private readonly Stack<GraphicsStateSnapshot> _stack = new();

    /// <summary>Current transformation matrix (CTM). Initialized to identity.</summary>
    /// <remarks>
    /// Set access is internal so the renderer can pre-seed the CTM when starting to
    /// parse a child content stream (e.g. tiling pattern) that must inherit a parent
    /// transform before its own <c>cm</c> operators run. External callers should use
    /// <see cref="ConcatMatrix"/>.
    /// </remarks>
    public double[] Ctm { get; internal set; } = { 1, 0, 0, 1, 0, 0 };

    /// <summary>Fill color (RGB). Default: black.</summary>
    public double FillR { get; set; }
    public double FillG { get; set; }
    public double FillB { get; set; }

    /// <summary>Stroke color (RGB). Default: black.</summary>
    public double StrokeR { get; set; }
    public double StrokeG { get; set; }
    public double StrokeB { get; set; }

    /// <summary>Line width. Default: 1.0.</summary>
    public double LineWidth { get; set; } = 1.0;

    /// <summary>Line cap style (0=butt, 1=round, 2=square). Default: 0.</summary>
    public int LineCap { get; set; }

    /// <summary>Line join style (0=miter, 1=round, 2=bevel). Default: 0.</summary>
    public int LineJoin { get; set; }

    /// <summary>Miter limit. Default: 10.0.</summary>
    public double MiterLimit { get; set; } = 10.0;

    /// <summary>Flatness tolerance. Default: 0.</summary>
    public double Flatness { get; set; }

    /// <summary>Fill opacity (ca). Default: 1.0 (opaque).</summary>
    public double FillAlpha { get; set; } = 1.0;

    /// <summary>Stroke opacity (CA). Default: 1.0 (opaque).</summary>
    public double StrokeAlpha { get; set; } = 1.0;

    /// <summary>Blend mode (BM). Default: "Normal".</summary>
    public string BlendMode { get; set; } = "Normal";

    /// <summary>Overprint flag for stroking (OP). Default: false.</summary>
    public bool OverprintStroke { get; set; }

    /// <summary>Overprint flag for non-stroking (op). Default: false.</summary>
    public bool OverprintFill { get; set; }

    /// <summary>The name of the current ExtGState resource, or null.</summary>
    public string? ExtGStateName { get; set; }

    /// <summary>Current marked content tag (set by BDC/BMC, cleared by EMC).</summary>
    public string? MarkedContentTag { get; set; }

    /// <summary>ActualText from marked content properties (set by BDC with /ActualText).</summary>
    public string? ActualText { get; set; }

    // ── Dash pattern ────────────────────────────────────────────────

    /// <summary>Dash pattern array (d operator). Default: empty (solid line).</summary>
    public double[] DashArray { get; set; } = [];

    /// <summary>Dash phase (d operator). Default: 0.</summary>
    public double DashPhase { get; set; }

    // ── Color space ─────────────────────────────────────────────────

    /// <summary>Current fill color space name (cs operator). Default: "DeviceGray".</summary>
    public string FillColorSpace { get; set; } = "DeviceGray";

    /// <summary>Current stroke color space name (CS operator). Default: "DeviceGray".</summary>
    public string StrokeColorSpace { get; set; } = "DeviceGray";

    /// <summary>
    /// Pattern resource name set by <c>scn</c> when <see cref="FillColorSpace"/> is "Pattern".
    /// Null means solid fill — the normal RGB path. Non-null tells the renderer to fill the
    /// current path by executing the named pattern's content stream, clipped to the path.
    /// </summary>
    public string? FillPatternName { get; set; }

    /// <summary>Pattern resource name for stroking (SCN operator).</summary>
    public string? StrokePatternName { get; set; }

    // ── Text object tracking ────────────────────────────────────────

    /// <summary>Whether we are inside a BT/ET text object.</summary>
    public bool InTextObject { get; set; }

    // ── Text state ──────────────────────────────────────────────────

    /// <summary>Current font resource name (e.g., "F1").</summary>
    public string? FontName { get; set; }

    /// <summary>Current font size.</summary>
    public double FontSize { get; set; }

    /// <summary>Character spacing (Tc). Default: 0.</summary>
    public double CharSpacing { get; set; }

    /// <summary>Word spacing (Tw). Default: 0.</summary>
    public double WordSpacing { get; set; }

    /// <summary>Horizontal scaling (Tz). Default: 100.</summary>
    public double HorizontalScaling { get; set; } = 100;

    /// <summary>Text leading (TL). Default: 0.</summary>
    public double Leading { get; set; }

    /// <summary>Text rendering mode (Tr). Default: 0 (fill).</summary>
    public int RenderingMode { get; set; }

    /// <summary>Text rise (Ts). Default: 0.</summary>
    public double Rise { get; set; }

    /// <summary>Text matrix (Tm). Set by Tm operator, modified by Td/TD/T*.</summary>
    public double[] TextMatrix { get; private set; } = { 1, 0, 0, 1, 0, 0 };

    /// <summary>Text line matrix. Set by Tm, updated by Td/TD/T*.</summary>
    public double[] TextLineMatrix { get; private set; } = { 1, 0, 0, 1, 0, 0 };

    // ── Clipping path ──────────────────────────────────────────────

    /// <summary>
    /// Current clipping path as a binary stencil (<c>255</c> = pixel is inside clip,
    /// <c>0</c> = outside). Null means no clip (the full page is drawable).
    /// Updated by the <c>W</c> / <c>W*</c> operators via the renderer: each new
    /// clip path is intersected with the existing mask, matching PDF §8.5.4.1's
    /// "clipping path is the intersection of the current clip and the current path".
    /// Saved and restored with <c>q</c>/<c>Q</c> — the reference is snapshotted so
    /// the mask bytes don't need copying (the renderer always installs a fresh
    /// mask buffer when tightening the clip).
    /// </summary>
    public byte[]? ClipMask { get; set; }

    /// <summary>
    /// Active /SMask soft-mask info (PDF 32000 §11.6.5.4) installed via gs. Null
    /// means no soft mask. The renderer reads this at paint-time, renders the mask
    /// group lazily into a per-page alpha buffer, and multiplies fragment alpha
    /// against it. Snapshotted across q/Q like any other graphics-state field.
    /// </summary>
    internal SoftMaskInfo? SoftMask { get; set; }

    // ── State stack operations ──────────────────────────────────────

    /// <summary>Push the current state onto the stack (q operator).</summary>
    public void Save()
    {
        _stack.Push(new GraphicsStateSnapshot
        {
            Ctm = (double[])Ctm.Clone(),
            FillR = FillR, FillG = FillG, FillB = FillB,
            StrokeR = StrokeR, StrokeG = StrokeG, StrokeB = StrokeB,
            LineWidth = LineWidth, LineCap = LineCap, LineJoin = LineJoin,
            MiterLimit = MiterLimit, Flatness = Flatness,
            DashArray = (double[])DashArray.Clone(), DashPhase = DashPhase,
            FillAlpha = FillAlpha, StrokeAlpha = StrokeAlpha,
            BlendMode = BlendMode, OverprintStroke = OverprintStroke,
            OverprintFill = OverprintFill, ExtGStateName = ExtGStateName,
            FillColorSpace = FillColorSpace, StrokeColorSpace = StrokeColorSpace,
            FillPatternName = FillPatternName, StrokePatternName = StrokePatternName,
            FontName = FontName, FontSize = FontSize,
            CharSpacing = CharSpacing, WordSpacing = WordSpacing,
            HorizontalScaling = HorizontalScaling, Leading = Leading,
            RenderingMode = RenderingMode, Rise = Rise,
            ClipMask = ClipMask,
            SoftMask = SoftMask,
        });
    }

    /// <summary>Restore the most recently saved state (Q operator).</summary>
    public void Restore()
    {
        if (_stack.Count == 0) return;
        var s = _stack.Pop();
        Ctm = s.Ctm;
        FillR = s.FillR; FillG = s.FillG; FillB = s.FillB;
        StrokeR = s.StrokeR; StrokeG = s.StrokeG; StrokeB = s.StrokeB;
        LineWidth = s.LineWidth; LineCap = s.LineCap; LineJoin = s.LineJoin;
        MiterLimit = s.MiterLimit; Flatness = s.Flatness;
        DashArray = s.DashArray; DashPhase = s.DashPhase;
        FillAlpha = s.FillAlpha; StrokeAlpha = s.StrokeAlpha;
        BlendMode = s.BlendMode; OverprintStroke = s.OverprintStroke;
        OverprintFill = s.OverprintFill; ExtGStateName = s.ExtGStateName;
        FillColorSpace = s.FillColorSpace; StrokeColorSpace = s.StrokeColorSpace;
        FillPatternName = s.FillPatternName; StrokePatternName = s.StrokePatternName;
        FontName = s.FontName; FontSize = s.FontSize;
        CharSpacing = s.CharSpacing; WordSpacing = s.WordSpacing;
        HorizontalScaling = s.HorizontalScaling; Leading = s.Leading;
        RenderingMode = s.RenderingMode; Rise = s.Rise;
        ClipMask = s.ClipMask;
        SoftMask = s.SoftMask;
    }

    // ── Matrix operations ───────────────────────────────────────────

    /// <summary>Concatenate a matrix to the CTM (cm operator).</summary>
    public void ConcatMatrix(double a, double b, double c, double d, double e, double f)
    {
        Ctm = MultiplyMatrices(new[] { a, b, c, d, e, f }, Ctm);
    }

    /// <summary>Set the text matrix directly (Tm operator).</summary>
    public void SetTextMatrix(double a, double b, double c, double d, double e, double f)
    {
        TextMatrix = new[] { a, b, c, d, e, f };
        TextLineMatrix = new[] { a, b, c, d, e, f };
    }

    /// <summary>Move text position (Td operator).</summary>
    public void MoveTextPosition(double tx, double ty)
    {
        var translation = new double[] { 1, 0, 0, 1, tx, ty };
        TextLineMatrix = MultiplyMatrices(translation, TextLineMatrix);
        TextMatrix = (double[])TextLineMatrix.Clone();
    }

    /// <summary>Advance text position after showing a string (Tj/TJ). Only updates TextMatrix, not TextLineMatrix.</summary>
    public void AdvanceTextPosition(double tx, double ty)
    {
        var translation = new double[] { 1, 0, 0, 1, tx, ty };
        TextMatrix = MultiplyMatrices(translation, TextMatrix);
    }

    /// <summary>Move to start of next line (T* operator). Uses leading value.</summary>
    public void MoveToNextLine()
    {
        MoveTextPosition(0, -Leading);
    }

    /// <summary>
    /// Transform a point from user space to device space using the CTM.
    /// </summary>
    public (double x, double y) TransformPoint(double x, double y)
    {
        var tx = Ctm[0] * x + Ctm[2] * y + Ctm[4];
        var ty = Ctm[1] * x + Ctm[3] * y + Ctm[5];
        return (tx, ty);
    }

    /// <summary>
    /// Get the absolute position of text using both CTM and text matrix.
    /// </summary>
    public (double x, double y) GetTextPosition()
    {
        // Text rendering matrix = Tm × CTM
        var rm = MultiplyMatrices(TextMatrix, Ctm);
        return (rm[4], rm[5]);
    }

    /// <summary>
    /// Get the effective font size considering text matrix scaling and CTM.
    /// </summary>
    public double GetEffectiveFontSize()
    {
        // Scale factor from text matrix
        var sy = Math.Sqrt(TextMatrix[1] * TextMatrix[1] + TextMatrix[3] * TextMatrix[3]);
        if (sy == 0) sy = 1;
        return FontSize * sy;
    }

    // ── Matrix math ─────────────────────────────────────────────────

    /// <summary>Multiply two 3×3 affine matrices represented as [a b c d e f].</summary>
    internal static double[] MultiplyMatrices(double[] m1, double[] m2)
    {
        return new[]
        {
            m1[0] * m2[0] + m1[1] * m2[2],
            m1[0] * m2[1] + m1[1] * m2[3],
            m1[2] * m2[0] + m1[3] * m2[2],
            m1[2] * m2[1] + m1[3] * m2[3],
            m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
            m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
        };
    }

    private struct GraphicsStateSnapshot
    {
        public double[] Ctm;
        public double FillR, FillG, FillB;
        public double StrokeR, StrokeG, StrokeB;
        public double LineWidth;
        public int LineCap, LineJoin;
        public double MiterLimit, Flatness;
        public double[] DashArray;
        public double DashPhase;
        public double FillAlpha, StrokeAlpha;
        public string BlendMode;
        public bool OverprintStroke, OverprintFill;
        public string? ExtGStateName;
        public string FillColorSpace, StrokeColorSpace;
        public string? FillPatternName, StrokePatternName;
        public string? FontName;
        public double FontSize;
        public double CharSpacing, WordSpacing;
        public double HorizontalScaling, Leading;
        public int RenderingMode;
        public double Rise;
        // Clip mask is a reference snapshot — the renderer always installs a fresh
        // byte[] when tightening the clip, so restoring the reference after a Q
        // drops the tightened mask and reverts to the enclosing scope's clip.
        public byte[]? ClipMask;
        // Soft mask is a reference snapshot — installed fresh by gs, replaced by
        // subsequent gs (potentially with /SMask /None which clears it).
        public SoftMaskInfo? SoftMask;
    }
}
