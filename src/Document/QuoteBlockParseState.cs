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
private sealed class QuoteBlockParseState
{
    public List<Aspose.Pdf.Document.QsBlock> blocks = null!;
    public int bodyAt;
    public string body = null!;
    public System.Text.RegularExpressions.Regex rx = null!;
    public string html = default!;
    public Dictionary<string, (double width, double fontPx, bool centre, bool underline)> css = default!;
    public HtmlLoadOptions? options = null;
}
}
