using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class StyledGroupState
{
    // stl_ shape: a positioned stl_01 div wrapping the group's text.
    // Both stl_ dialects emit one span per word-anchored SEGMENT,
    // each letter/word-spacing-pinned so the measured boxes reach
    // their device anchors (external SVG-text saves
    // pin exactly like the PNG-background overlay); a group whose
    // face cannot resolve keeps the single whole-line span with the
    // plain Tc/TJ letter-spacing and the face's natural metric flow.
    // Channel bytes TRUNCATE (0.994118 -> 253 #FD) when forming
    // the emitted class colors.
    public string color = null!;
    public int fontNum;
    // Line-height is the font's hhea (asc+|desc|)/upm when a program
    // is available (1.117188 for Arial), the
    // generic 1.2 fallback otherwise.
    public int lhNum;
    public double fs;
    public string textAll = null!;
    public string? face;
    // Fixed-layout geometry: x is measured from the MediaBox left
    // edge, the page top reference is LLY + floor(height), and the
    // run's visual top sits ascent×size above the baseline (the
    // font's usWinAscent fraction, not a full em).
    public double yTop;
    public double left;
    public double top;
    // A rotated run carries a document-wide rotation class next to
    // stl_01 (vendor-prefixed transform block in the stylesheet).
    public string divCls = null!;
    public string zStyle = null!;
    // A text run inside a link annotation's rect renders as an anchor
    // wrapping the span(s) (div > a > span), carrying the link with
    // the text itself rather than only as an invisible overlay.
    // Containment is judged by OVERLAP, not the group origin: a rect
    // is fitted to the link's visible text with a little padding, so
    // the NEXT run's leading space can start inside the rect's right
    // padding without being the link's text.
    public Converters.PdfToHtmlConverter.LinkTarget? link;
    public List<(string Label, string Href)>? popupItems;
    public string? linkOpen;
    // A page-menu widget wraps the caption span in a relative
    // hover box; its drop-up list class allocates after the
    // caption's own classes.
    public int popupBoxNum;
}
}
