using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    private const double HtmlCellFontSize = 12.0;

    private static readonly Regex BoldOnlyHtmlRegex = new(
        @"^\s*<(b|strong)\b[^>]*>(?<t>[^<>]+)</\1\s*>\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string? TryBoldOnlyHtml(string? html)
    {
        string text = "";
        if (string.IsNullOrEmpty(html)) return null;
        var m = BoldOnlyHtmlRegex.Match(html);
        if (!m.Success) return null;
        text = HtmlFragment.StripHtmlTags(m.Groups["t"].Value);
        return (text.Length > 0) ? text : null;
    }

    private const double HtmlSmallFontSize = 10.0;

    /// <summary>One styled run on an HTML-engine cell line: text at an x-offset from the
    /// cell content-left, regular or bold serif, at its own size.</summary>
    private sealed class HtmlRun
    {
        public string Text = "";
        public double X;
        public double Size;
        public bool Bold;
        /// The href of the enclosing anchor, when this run sits inside one.
        public string? Url;
        /// A span-styled run's own colour (null = the line's colour).
        public Color? Color;
        /// text-decoration: underline on the enclosing span.
        public bool Underline;
        /// The face a family, slant or heading gave the run (null = the serif pair).
        public UaFace? Face;
    }

    private static readonly Regex HtmlEngineTagRegex = new(
        @"<(/?)(b|strong|small|div|br|p|a|ul|ol|li|span|em|i|u|h[1-6]|table|tbody|thead|tfoot|tr|td|th|img)\b[^>]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A list item's text starts this far inside its list's content edge —
    /// the UA `padding-inline-start: 40px`.</summary>
    private const double UaListIndentPt = 40 * 0.75;

    /// <summary>…and the item's marker ends this far before that text (0.375 em, the
    /// UA marker gap): a bullet or an ordinal is laid RIGHT-aligned against it, so
    /// "4." and "10." end on the same x.</summary>
    private const double UaListMarkerGapEm = 0.375;

    private static readonly Regex HrefRegex = new(
        @"\bhref\s*=\s*(?:'(?<u>[^']*)'|""(?<u>[^""]*)""|(?<u>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>Decode the common HTML entities in tag-free text WITHOUT trimming or
    /// tag-stripping (unlike <see cref="HtmlFragment.StripHtmlTags"/>), so inter-word
    /// spaces at run boundaries survive.</summary>
    private static string DecodeHtmlEntities(string s) =>
        s.IndexOf('&') < 0 ? s : System.Net.WebUtility.HtmlDecode(s).Replace(' ', ' ');

    /// <summary>Draw an HtmlFragment's HTML-engine lines with the fragment's content box
    /// top-left at (<paramref name="x"/>, <paramref name="topY"/>) — the same serif line
    /// model a table cell uses, so a fragment hosted outside a cell (a FloatingBox child)
    /// sets in the identical face, size and rhythm. Returns the height consumed, or null
    /// when the markup falls outside the engine family (the caller keeps its own path).</summary>
    internal static double? DrawHtmlEngineFragment(ContentStreamBuilder b, Page page,
        string? html, double x, double topY, double availWidth)
    {
        if (page is null) return null;
        if (ParseHtmlEngineCell(html, availWidth) is not { Count: > 0 } lines) return null;
        var fontDict = ResolvePageFontDict(page);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineBase = topY - _serifBaseDrop - i * _serifRootBox;
            if (lines[i].Runs is not { Count: > 0 } runs) continue;
            foreach (var run in runs)
            {
                if (run.Text.Length == 0) continue;
                var ttf = run.Bold ? _serifBoldTtf : _serifTtf;
                if (ttf is null) continue;
                var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, ttf, run.Bold ? "Times New Roman Bold" : "Times New Roman",
                    run.Text, stripSpacesInBaseFont: true);
                b.BeginText();
                b.SetFont(resName, run.Size);
                b.MoveTextPosition(x + run.X, lineBase);
                if (KernAdjustments(run.Text, ttf) is { } kern) b.ShowTextHexKerned(hex, kern);
                else b.ShowTextHex(hex);
                b.EndText();
            }
        }
        // The last line ends at its own baseline + descent, not at a full box.
        return (lines.Count - 1) * _serifRootBox + lines[^1].BoxH;
    }

    private static readonly Regex EscNlDoctypeRegex = new(@"<!DOCTYPE[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlStyleRegex = new(@"<style[^>]*>[\s\S]*?</style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlCellRegex = new(
        @"<(t[dh])\b[^>]*>(?<c>[\s\S]*?)</t[dh]\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlRowRegex = new(@"<tr\b[^>]*>(?<r>[\s\S]*?)</tr\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlBrRegex = new(@"<br\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>HTML default table chrome, in points: cellspacing 2px, cellpadding 1px.</summary>
    private const double EscNlCellSpacing = 2 * 0.75;

    private const double EscNlCellPadding = 1 * 0.75;

    /// <summary>A shrink-wrapped centred table centres over the band shrunk by this
    /// side margin each side (measured at two left margins: table left
    /// 23.34 on the margin-0 A4 band, 38.34 at margin 30 — both exactly
    /// bandLeft + (bandW − 2·60 − tableW)/2).</summary>
    private const double EscNlCenterSideMargin = 60;

    /// <summary>True when every paragraph of the cell is a bold-only HtmlFragment (and the
    /// serif face resolves), i.e. the cell lays out on HTML-engine metrics
    /// with zero autofit padding.</summary>
    /// <summary>True when any of the cell's paragraphs is an <see cref="HtmlFragment"/>.
    /// Such a cell measures MIN-content under AutoFitToContent: the HTML shrink-to-fit
    /// measure reports the widest unbreakable word, so its column wraps.</summary>
    private static bool HasHtmlContent(Cell cell)
    {
        foreach (var p in cell.Paragraphs)
            if (p is HtmlFragment) return true;
        return false;
    }

    /// <summary>True when the cell holds an HtmlFragment of BLOCK-structured markup
    /// (paragraphs, lists, headings). Such a fragment is a block box, and its column
    /// FILLS the width the other columns leave: probed on a three-column auto-fit
    /// table, the list column took every point of the content box the two text columns
    /// did not, and moving the list to the middle column moved the fill with it. An
    /// INLINE fragment keeps the shrink-to-fit min-content measure.</summary>
    private static bool HasFillHtmlContent(Cell cell)
    {
        foreach (var p in cell.Paragraphs)
            if (p is HtmlFragment h
                && Converters.HtmlToPdfConverter.HasBlockStructure(h.HtmlContent ?? ""))
                return true;
        return false;
    }

    private static bool AllBoldSerifHtml(Cell cell)
    {
        if (cell.Paragraphs.Count == 0 || BoldSerifTtf() is null) return false;
        foreach (var p in cell.Paragraphs)
            if (p is not HtmlFragment h || TryBoldOnlyHtml(h.HtmlContent) is null) return false;
        return true;
    }
}
