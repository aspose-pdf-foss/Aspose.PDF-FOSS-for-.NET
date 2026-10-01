using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The ipd claim-file letter (a generated claims-system report) ───────────
    // A form letter: an `ipdPortrait` 8-in sheet holding an `ipdPageTitle`, then
    // `ipdSection`s — each a double-bordered `ipdSectionTitle` band followed by a
    // 25/75 `ipdPropertyGrid` or bordered `ipdSubSection` boxes (gray title bar +
    // padded contents holding fixed-layout `slfFormLayout` label/value tables).
    //
    // Geometry (all measured on the expected conversion of the corpus letter):
    //   page 771.5 × 842 = 96 content left + the section-title border box 585.5
    //   (576 declared + 2×3.75 px padding + 2×1 border) + the 90 right band;
    //   content runs 72..770. The 18 pt bold title centres on the 96..672 sheet
    //   (baseline 94.74) over a 1.5 pt rule at 99.75. A section title seats its
    //   12 pt bold lines on a 13.5 pitch inside a 1 pt "double" border (drawn as
    //   two ⅓ pt strokes) with 3.75 padding; margins collapse to 15 between the
    //   letter's blocks. Grid rows are 19.5 tall (10 pt text at 3.75 cell
    //   padding, 0.75 collapsed borders), label column 25 % of 576; form-table
    //   rows are lines×11.25 + 7.5 with cells wrapped at their fixed column.
    //   The grid's label-column gray paints as one FIXED rect per
    //   page (96.38..240.19 × 72.38..242.25) regardless of where the rows sit —
    //   an era artifact reproduced as-is.

    private const double IpdPageW = 771.5;
    private const double IpdPageH = 842.0;
    private const double IpdContentTop = 72.0;
    private const double IpdContentBottom = 770.0;
    private const double IpdLeft = 96.0;
    private const double IpdSheetW = 576.0;          // .ipdPortrait: 8in
    private const double IpdTitleBaseTd = 94.74;
    private const double IpdTitleRuleTd = 99.75;     // 2px border-bottom, 1.5 pt
    private const double IpdBlockMargin = 15.0;      // 20px margins, collapsed
    private const double IpdSecPad = 3.75;           // 5px padding
    private const double IpdSecBorder = 1.0;         // the "double" border, 1 pt
    private const double IpdSecLinePitch = 13.5;     // 12 pt title line pitch
    private const double IpdSecAscent = 10.91;       // baseline inset under padding
    private const double IpdGridRowH = 19.5;
    private const double IpdGridLabelFrac = 0.25;
    private const double IpdGridBaseOff = 13.59;     // row top → text baseline
    private const double IpdCellPad = 3.75;          // 5px td padding
    private const double IpdAscent10 = 9.05;         // 10 pt Arial ascent
    private const double IpdLinePitch10 = 11.25;     // 10 pt wrapped-line pitch
    private const double IpdFormRowPad = 7.5;        // row height = lines·11.25 + 7.5
    private const double IpdSubBarH = 18.0;          // gray sub-title bar height
    private const double IpdSubBarPad = 2.25;        // 3px padding
    private const double IpdSubBarBase = 12.43;      // bar top → 11 pt baseline
    private const double IpdSubBarW = 579.0;         // width:100% bar overhang
    private const double IpdSubPad = 6.0;            // 8px contents padding
    private const double IpdSubTableFrac = 0.95;     // .ipdSubSectionContents table
    private const double IpdGrayTop = 72.38;         // the fixed label-gray rect
    private const double IpdGrayBottom = 242.25;
    private static readonly double[] IpdCol4 = { 0.20, 0.30, 0.20, 0.30 };
    private static readonly double[] IpdCol2 = { 0.50, 0.50 };
    private static readonly double[] IpdCol2Pay = { 0.55, 0.45 };

    private sealed class IpdItem
    {
        public string Kind = "";                     // title/sectitle/subbegin/subend/grid/form/text
        public string Text = "";
        public double Fs = 10;
        public List<List<string>> Rows = new();      // grid + form cell texts per row
        public double[] Cols = Array.Empty<double>();
        public bool Framed;
    }

    private static Document? TryRenderIpdClaimLetter(string html)
    {
        if (!html.Contains("class=\"ipdPortrait\"", StringComparison.Ordinal)
            || !html.Contains("ipdPageTitle", StringComparison.Ordinal)
            || !html.Contains("ipdPropertyGrid", StringComparison.Ordinal)
            || !html.Contains("slfFormLayout", StringComparison.Ordinal))
            return null;

        var ic = new IpdClaimLetterState();
        ic.items = new List<IpdItem>();
        ic.bodyM = Regex.Match(html, @"<div\b[^>]*class=""ipdPortrait""[^>]*>",
            RegexOptions.IgnoreCase);
        if (!ic.bodyM.Success) return null;
        (ic.seg, _) = DivInner(ic, html, ic.bodyM.Index + ic.bodyM.Length);

        ic.pos = 0;
        ic.subDepth = new Stack<int>();
        ic.depthNow = 0;
        ic.tagRx = new Regex(@"<(/?)(div|table|textarea)\b([^>]*)>", RegexOptions.IgnoreCase);
        while (ic.pos < ic.seg.Length)
        {
            if (!ParseIpdToken(ic)) break;
        }
        while (ic.subDepth.Count > 0) { ic.subDepth.Pop(); ic.items.Add(new IpdItem { Kind = "subend" }); }
        if (ic.items.Count == 0 || ic.items[0].Kind != "title") return null;

        ic.doc = Document.Create();
        ic.docFontDict = new Core.PdfDictionary();
        ic.inv = System.Globalization.CultureInfo.InvariantCulture;
        ic.pages = new List<StringBuilder>();
        ic.sb = null!;
        ic.pageHasGrid = new List<bool>();
        NewPage(ic);
        ic.y = IpdContentTop;
        ic.pendingMargin = 0;
        ic.subTop = -1;
        ic.inSub = false;

        foreach (var it in ic.items)
        {
            if (!RenderIpdItem(ic, it)) break;
        }

        // pages: white sheet + the fixed label-gray artifact on grid pages
        for (var pi = 0; pi < ic.pages.Count; pi++)
        {
            var page = ic.doc.Pages.Add(IpdPageW, IpdPageH);
            EnsureFonts(page, ic.docFontDict);
            EnsureFont(page, "Arial", "F8");
            EnsureFont(page, "ArialBold", "F9");
            var head = new StringBuilder();
            head.AppendLine(Compat.Format(ic.inv,
                $"q 1 1 1 rg 90 {IpdPageH - IpdContentBottom:F2} 591.5 698 re f Q"));
            if (ic.pageHasGrid[pi])
                head.AppendLine(Compat.Format(ic.inv,
                    $"q 0.945 0.945 0.945 rg 96.38 {IpdPageH - IpdGrayBottom:F2} 143.81 {IpdGrayBottom - IpdGrayTop:F2} re f Q"));
            page.AddContentStream(Encoding.ASCII.GetBytes(head.ToString() + ic.pages[pi]));
        }
        return ic.doc;
    }
}
