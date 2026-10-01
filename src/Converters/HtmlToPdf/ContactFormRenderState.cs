using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ContactFormRenderState
{
    public Dictionary<string, Dictionary<string, string>> css = null!;
    public System.Globalization.CultureInfo inv = null!;
    // Per-field-class widths/heights, straight from the sheet.
    public Dictionary<string, (double W, double H)> clsW = null!;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string body2 = null!;
    // Blocks in document order: section headings and rows of fields.
    public List<(string Kind, string Text, List<Aspose.Pdf.Converters.HtmlToPdfConverter.CfField> Fields)> blocks = null!;
    public System.Text.RegularExpressions.Regex tokRx = null!;
    public System.Text.RegularExpressions.Regex divRx = null!;
    public int scanPos;
    // Column: min(1025px, content) centred in the page's content box.
    public double contentPt;
    public double colPt;
    public double colX;
    public Document doc = null!;
    public Page page = null!;
    public Dictionary<string, string> resByFace = null!;
    public System.Text.StringBuilder sb = null!;
    // The first heading's glyph top: the page margin plus the h2's own
    // margin-top (20 px) and its ascent within the 1.1 line box.
    public double y;
    public bool pendingHeading;
    public string html = default!;
    public HtmlLoadOptions? options = null;
    public double pageWidth = 0;
    public double pageHeight = 0;
    public double marginLeft = 0;
    public double marginRight = 0;
    public double marginTop = 0;
}
}
