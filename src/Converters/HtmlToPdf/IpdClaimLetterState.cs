using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class IpdClaimLetterState
{
    public List<Aspose.Pdf.Converters.HtmlToPdfConverter.IpdItem> items = null!;
    public System.Text.RegularExpressions.Match bodyM = null!;
    public string seg = null!;
    public int pos;
    public Stack<int> subDepth = null!;
    public int depthNow;
    public System.Text.RegularExpressions.Regex tagRx = null!;
    // ── layout ──────────────────────────────────────────────────────────────
    public Document doc = null!;
    public Aspose.Pdf.Core.PdfDictionary docFontDict = null!;
    public System.Globalization.CultureInfo inv = null!;
    public List<System.Text.StringBuilder> pages = null!;
    public StringBuilder sb = null!;
    public List<bool> pageHasGrid = null!;
    public double y;
    public double pendingMargin;
    // open subsection boxes: (boxTopTd on this page) — sides drawn on close/break
    public double subTop;
    public bool inSub;
}
}
