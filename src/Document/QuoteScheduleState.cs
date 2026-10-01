using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class QuoteScheduleState
{
    // Gate: a stylesheet that sizes table columns by class, used by tables whose
    // cells carry those classes — the shape the column solver above describes.
    public Dictionary<string, (double width, double fontPx, bool centre, bool underline)> css = null!;
    public int colClasses;
    public double bodyPx;
    public double bodyW;
    public List<Aspose.Pdf.Document.QsBlock> blocks = null!;
    public Aspose.Pdf.Text.Font? regular;
    public Aspose.Pdf.Text.Font? bold;
    public Aspose.Pdf.Text.Font? italic;
    public System.Globalization.CultureInfo inv = null!;
    public double left;
    public double contentTop;
    public double y;
    public string html = default!;
    public FlowLayout flow = default!;
    public Page page = default!;
    public double marginLeft = 0;
    public double marginBottom = 0;
    public HtmlLoadOptions? options = null;
    public Aspose.Pdf.Text.Font? boldItalic = null;
}
}
