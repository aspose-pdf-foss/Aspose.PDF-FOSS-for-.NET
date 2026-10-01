using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FormFieldState
{
    public double fx;
    public double fy;
    public double fw;
    public double fh;
    public string fBody = null!;
    // the content div: border colour/width, optional background
    public System.Text.RegularExpressions.Match cdM = null!;
    public string st = null!;
    public Color bCol = null!;
    public double bw;
    public Color? bgCol;
    // question text + width
    public System.Text.RegularExpressions.Match qM = null!;
    public System.Text.RegularExpressions.Match plainM = null!;
    public double qW;
    public string qText = null!;
    public double contentX0;
    public double contentY0;
    public double qRight;
    // responses: underline divs (border-bottom w×h), radio/checkbox
    // tables, in source order after the question
    public System.Text.RegularExpressions.Match respM = null!;
    public string resp = null!;
    // question wider than the row pushes the response BELOW it
    public bool respBelow;
    public double rx;
    public double ry;
    public double uy;
    // radio / checkbox option tables: one row per <tr>, control glyph
    // + label cell at 0.5em paddings
    public double pad;
    public double rowY;
    public PositionedFormState pf = default!;
    public FormSectionState sn = default!;
    public Match fb = default!;
}
}
