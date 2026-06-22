namespace Aspose.Pdf.Text;

/// <summary>
/// PDF text rendering mode (Tr operator). Controls whether glyph outlines
/// are filled, stroked, used as a clipping boundary, or some combination.
/// PDF 32000-1 §9.3.6.
/// </summary>
public enum TextRenderingMode
{
    /// <summary>0 — Fill text.</summary>
    FillText = 0,
    /// <summary>1 — Stroke text.</summary>
    StrokeText = 1,
    /// <summary>2 — Fill, then stroke text.</summary>
    FillThenStrokeText = 2,
    /// <summary>3 — Neither fill nor stroke text (invisible).</summary>
    Invisible = 3,
    /// <summary>4 — Fill text and add to path for clipping.</summary>
    FillTextAndAddPathToClipping = 4,
    /// <summary>5 — Stroke text and add to path for clipping.</summary>
    StrokeTextAndAddPathToClipping = 5,
    /// <summary>6 — Fill, then stroke text and add to path for clipping.</summary>
    FillThenStrokeTextAndAddPathToClipping = 6,
    /// <summary>7 — Add text to path for clipping.</summary>
    AddPathToClipping = 7,
}
