using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class DtpTagState
{
    public System.Text.RegularExpressions.Match tm = null!;
    public int tagEnd;
    public string openTag = null!;
    public string tag = null!;
    public string? id = null!;
    public string? cls = null!;
    public Converters.HtmlToPdfConverter.DtpClassRule cr = null!;
    // div: capture the inner html up to the matching close.
    public int depth;
    public int scan;
    public int innerEnd;
    public System.Text.RegularExpressions.Regex divRx = null!;
    public string inner = null!;
    public string defaultAlign = null!;
    public List<DtpLogicalLine> lines = null!;
    // Line walker: wraps each logical line to the div width and lays
    // physical lines down the canvas, breaking to the next page's top
    // seat when a baseline would pass the band bottom.
    public int pageIdx;
    public double yIn;
    public double bottom;
    public bool first;
    public bool prevMso;
}
}
