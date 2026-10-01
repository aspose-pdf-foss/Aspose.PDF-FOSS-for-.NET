using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StepRowParseState
{
    public string? rowHtml;
    public Converters.HtmlToPdfConverter.StepRow row = null!;
    public System.Text.RegularExpressions.Match bm = null!;
    // the indent is declared on the row, or on the bullet column it pads out
    // (read the bullet's OPEN TAG, not its captured body — a slashed bullet's
    // body is a nested div the body regex cannot take)
    public System.Text.RegularExpressions.Match im = null!;
    public string? content;
    // The row is a flex line, so it is as tall as its tallest column - and the
    // acknowledge column is a column like any other. A bare `sr-ack` holder is
    // banked so far right that nothing it carries falls on the sheet, but it
    // still sets a floor under the row. The measured anchors: the
    // checkbox blank sits 13.87 below the widget's top, the signature's a
    // little lower, the boolean's pair of option boxes 3.00 below it and 13.50
    // tall, and each widget then stacks its own labels underneath.
    public string? ackBox;
    // The acknowledge column is drawn only when the row banks it to the sheet's
    // end: a bare `sr-ack` holder carries no widget the form puts on the page.
    public string? ack;
}
}
