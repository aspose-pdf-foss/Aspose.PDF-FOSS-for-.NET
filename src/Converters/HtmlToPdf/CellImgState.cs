using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class CellImgState
{
    // The extraction inputs, captured from the method parameters.
    public TableParseState ps = null!;
    /// <summary>The image resolved to the UA broken-image placeholder.</summary>
    public bool uaBrokenImage;
    public Table table = null!;
    public Token tok = null!;
    public HtmlLoadOptions? options;
    public double cellFontSize;
    public bool dwFormCells;
    public bool wordMailCells;
    public bool fullWidthCjkMin;
    public bool widenProbe;
    public bool redlineCells;
    public bool cellFontShorthand;
    public bool overDeclaredDraw;
    public double padSide;
    public bool liftNestedTables;
    public double pad;
    public List<byte[]>? inlineSvgs;
    public byte[]? cellImgBytes;
    public double ciw;
    public double cih;
    /// <summary>The image's CSS width as a fraction of its cell's text box (`width: 50%`); 0 = none.</summary>
    public double ciwFrac;
    /// <summary>The image's `max-width` as a fraction of its cell's text box (its own style, else the sheet's
    /// `img` rule): it then floors no column and draws no wider than that share; 0 = none.</summary>
    public double maxFrac;
    /// <summary>The image's `max-width` as a length in points (its own style, else the sheet's `img` rule): its box
    /// lays out no wider; 0 = none.</summary>
    public double maxWidthPt;
    /// <summary>The box the image loaded at, as LoadCellImage gives it (the sides its markup gives, and any side it
    /// completes from the file's size or aspect), as a max-width length's path found it, before completing or scaling
    /// it; null when no such path ran.</summary>
    public (double W, double H)? boxBeforeCap;
}
}
