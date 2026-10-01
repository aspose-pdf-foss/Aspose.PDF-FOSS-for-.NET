using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ClinicalFormState
{
    public byte[]? segoe;
    // ── parse ────────────────────────────────────────────────────────────
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string body = null!;
    // header: broken-logo alt text, the h5 title, four <strong>-labelled cells,
    // and the centred maintext.
    public string logoAlt = null!;
    public System.Text.RegularExpressions.Match titleM = null!;
    public string title = null!;
    public System.Text.RegularExpressions.Match headTableM = null!;
    public List<string> headCells = null!;
    public System.Text.RegularExpressions.Match mainTextM = null!;
    public string mainText = null!;
    // sections: h3 title + the <ol>'s <li>s parsed into inline/block atoms
    public List<(string Title, List<List<Aspose.Pdf.Converters.HtmlToPdfConverter.CfAtom>> Items)> sections = null!;
    // ── layout ───────────────────────────────────────────────────────────
    public System.Globalization.CultureInfo inv = null!;
    public Aspose.Pdf.Core.PdfDictionary measureDict = null!;
    // A page holds raw op strings and deferred text runs (embedded at paint).
    public List<System.Text.StringBuilder> pageShapes = null!;
    public List<List<(int Style, double Fs, double X, double BaseTd, string Text, string Col)>> pageRuns = null!;
    public List<double> pageFlowBot = null!;
    public double bot;
    public bool prevWasBox;
    public double bandX;
    public double bandW;
    // the 2×2 cell grid: bold label + a trailing space where the source span
    // was empty; columns split the band on max content + an equal remainder
    public string[] boldParts = null!;
    public double c1;
    public double c2;
    public double slack;
    public double col1W;
    public double rowY;
    // the centred notice, wrapped on the inner column
    public double noticeTop;
    public List<string> noticeLines = null!;
    // the form-header's 2px black rule sits under the notice's line box
    public double ruleCenter;
    // ── paint ────────────────────────────────────────────────────────────
    public Document doc = null!;
    public byte[]? segoeBold;
    public byte[]? segoeItalic;
    public string[] restParts = null!;
    public bool markerPending;
    public List<(Aspose.Pdf.Converters.HtmlToPdfConverter.CfAtom Atom, double X)> lineAtoms = null!;
    public double pen;
    public bool lineHasInput;
    public bool pendingSpace;
}
}
