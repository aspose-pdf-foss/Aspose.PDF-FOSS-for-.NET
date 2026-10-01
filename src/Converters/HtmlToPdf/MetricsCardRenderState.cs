using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MetricsCardRenderState
{
    public string? face;
    public Dictionary<string, string>? headCls;
    public bool hasCollapse;
    public bool hasInherit;
    public double bodyFs;
    // Heading text = the first heading-class cell; rows = the nested table.
    public System.Text.RegularExpressions.Match headTdM = null!;
    public string headText = null!;
    public System.Text.RegularExpressions.Match innerTblM = null!;
    public List<(string label, string valueMain, string valueSup)> rows = null!;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public Page page = null!;
    public string faceRes = null!;
    public System.Text.StringBuilder sb = null!;
    public System.Globalization.CultureInfo inv = null!;
    // Heading band fill, then the collapsed frame: the band box and the body
    // cell box share their middle edge (stroke centres at the measured lines).
    public double fillL;
    public double fillR;
    // Label/value rows: labels wrap at the 75% column of the inherited-33%
    // nested box (= value pen − label pen); wrapped lines pitch at the
    // in-cell line, rows at the row pitch.
    public double labelBoxW;
    public double yBase;
    public string html = default!;
    public IReadOnlyDictionary<string, Dictionary<string, string>> css = default!;
    public double pageHeight = 0;
    public double headFs;
    public Color headBg = null!;
}
}
