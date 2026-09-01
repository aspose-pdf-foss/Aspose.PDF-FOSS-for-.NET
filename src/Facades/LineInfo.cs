using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Line drawing parameters for PdfContentEditor.DrawCurve.
/// </summary>
public sealed class LineInfo
{
    /// <summary>Vertex coordinates as flat array [x1,y1, x2,y2, .].</summary>
    public float[]? VerticeCoordinate { get; set; }

    /// <summary>Whether the line is visible.</summary>
    public bool Visibility { get; set; } = true;

    /// <summary>Line color — red component (0-255).</summary>
    public byte LineColorR { get; set; }

    /// <summary>Line color — green component (0-255).</summary>
    public byte LineColorG { get; set; }

    /// <summary>Line color — blue component (0-255).</summary>
    public byte LineColorB { get; set; }

    /// <summary>Line colour (System.Drawing compatible).</summary>
    public System.Drawing.Color LineColor
    {
        get => System.Drawing.Color.FromArgb(LineColorR, LineColorG, LineColorB);
        set { LineColorR = value.R; LineColorG = value.G; LineColorB = value.B; }
    }

    /// <summary>Line width in points.</summary>
    public int LineWidth { get; set; } = 1;

    /// <summary>Border style indicator (PDF table-228 /BS /S code: 0=Solid, 1=Dashed, 2=Beveled, 3=Inset, 4=Underline).</summary>
    public int BorderStyle { get; set; }

    /// <summary>Dash on/off lengths in points.</summary>
    public int[]? LineDashPattern { get; set; }
}
