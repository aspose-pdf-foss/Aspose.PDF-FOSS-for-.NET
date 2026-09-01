using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a row in a table.
/// </summary>
/// <summary>An inline box drawn behind part of a cell line (the HTML inline-block
/// idiom: a title plate, a rounded status pill), optionally trailed by a filled
/// circle carrying a letter (traffic-light badges). Geometry is pre-measured by
/// the HTML converter with the same metrics that lay the line out; offsets are
/// relative to the line's text origin.</summary>
internal sealed class InlineBoxDecoration
{
    /// <summary>Right inset a packed plate keeps off its column edge (a
    /// title plate ends ≈2 pt short of its cell).</summary>
    internal const double PackEdgeInsetPt = 2.0;

    /// <summary>Box left edge relative to the line's text origin.</summary>
    public double XOff;
    /// <summary>Full box width (pads + text + optional circle).</summary>
    public double Width;
    public double PadTop;
    public double PadBottom;
    public double PadRight;
    public double Radius;
    /// <summary>Box fill; null draws no rectangle (a continuation line whose
    /// plate was already painted by its first line).</summary>
    public Color? Fill = Color.White;
    /// <summary>Explicit box height (a CSS-declared plate height + pads); the
    /// rectangle may span the following line(s). 0 = the line's own box.</summary>
    public double Height;
    /// <summary>Vertical white inset inside the line box (status pills keep a
    /// small gap above and below their rounded rectangle).</summary>
    public double InsetV;
    /// <summary>The text run drawn inside the box: the box model owns the pen, so
    /// the run's x is explicit and the line's flat text is not drawn.</summary>
    public string? Text;
    public double TextX;
    public double TextSize;
    public bool TextBold;
    /// <summary>CSS letter-spacing for the text run (Tc operator).</summary>
    public double TextLetterSpacing;
    /// <summary>Block-level box (a section heading bar): spans the cell's content
    /// width at draw time.</summary>
    public bool FullWidth;
    /// <summary>Centre the text run within the (possibly full-width) box.</summary>
    public bool TextCentered;
    /// <summary>The run's own colour (a heading bar's white); null = the line's.</summary>
    public Color? TextColor;
    /// <summary>Trailing circle fill; null = no circle.</summary>
    public Color? CircleFill;
    public string? CircleLetter;
    public Color? CircleLetterColor;
    /// <summary>Circle diameter in points.</summary>
    public double CircleD;
}
