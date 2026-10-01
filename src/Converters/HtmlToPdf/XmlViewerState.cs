using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class XmlViewerState
{
    public System.Text.RegularExpressions.Match styleM = null!;
    public string sheet = null!;
    // Root class: `font: <keyword> '<family>'` (quoted or bare family).
    public System.Text.RegularExpressions.Match rootM = null!;
    public double fs;   // the root font size, the em of every CSS length
    public string rootCls = null!;
    public string face = null!;
    // The `.root *` block rule with its em padding.
    public System.Text.RegularExpressions.Match starM = null!;
    public double starPad;
    // The root DIV in the document; everything renders from its subtree.
    public System.Text.RegularExpressions.Match rootDivM = null!;
    public string? rootInner;
    // Root box: class padding-left (its own longhand beats the shorthand),
    // inline style padding-top overrides the class shorthand.
    public System.Text.RegularExpressions.Match rootRule = null!;
    public string rootDecl = null!;
    public string rootStyle = null!;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public string faceRes = null!;
    public double lineBox;
    public double drop;
    public double y;
    public System.Text.StringBuilder sb = null!;
    public System.Globalization.CultureInfo inv = null!;
    public string html = default!;
    public double pageWidth = 0;
    public double pageHeight = 0;
}
}
