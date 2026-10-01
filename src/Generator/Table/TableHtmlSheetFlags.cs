namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>HTML reset-sheet grid: a cell paragraph's top margin books a spacer line above it, as the nested render does.</summary>
    internal bool HtmlSheetParagraphMargins;
    /// <summary>HTML UA-flow checkbox grid: a checkbox is a 13px inline box on the baseline (margins 3 / 2.25 pt) whose 7.75 pt widget sits one point inside it; the line box is the em plus a 0.225 em strut descent, and an aligned cell centres the box.</summary>
    internal bool HtmlUaControlGrid;
    /// <summary>...and the grid's text size, which prices a checkbox line (its cells carry no text state).</summary>
    internal double HtmlUaControlFontPt;
    internal const double UaCheckboxMarginBoxPt = 15.0;
    internal const double UaCheckboxLeadPt = 3.0;
    internal const double UaCheckboxWidgetPt = 7.75;
    internal const double UaCheckboxWidgetInsetPt = 1.0;
    /// <summary>The checkbox's own box: 13 px square, its bottom edge on the baseline.</summary>
    internal const double UaCheckboxBoxPt = 9.75;
    /// <summary>Its block margins: 3 px above the box and 3 px below the baseline.</summary>
    internal const double UaCheckboxMarginBlockPt = 2.25;
}
