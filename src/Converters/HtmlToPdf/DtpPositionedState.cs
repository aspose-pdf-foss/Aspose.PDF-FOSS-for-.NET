using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DtpPositionedState
{
    public double band;
    // ── stylesheet ─────────────────────────────────────────────────────
    public System.Text.StringBuilder styleText = null!;
    public string styles = null!;
    public Dictionary<string, Aspose.Pdf.Converters.HtmlToPdfConverter.DtpIdRule> idRules = null!;
    public int inputRuleCount;
    public Dictionary<string, Aspose.Pdf.Converters.HtmlToPdfConverter.DtpClassRule> classRules = null!;
    // ── body elements, document order ──────────────────────────────────
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string bodyHtml = null!;
    public System.Globalization.CultureInfo inv = null!;
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary fontDict = null!;
    // Per-page op lists: raw content-stream text/vector ops and image
    // stamps, interleaved in document order so later elements draw on top.
    public List<List<object>> pageOps = null!;
    public int pos;
    public System.Text.RegularExpressions.Regex tagRx = null!;
    public string html = null!;
    public double pageW = 0;
    public double pageH = 0;
}
}
