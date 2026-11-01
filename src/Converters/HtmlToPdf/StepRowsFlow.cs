using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── the step-row walker's page fit, pictures and captions: a row is measured before it draws and moves
    // whole to a fresh page when it does not fit the space left; a bare-text row's pictures draw at their
    // declared box between its text runs; the caption over a step's table draws above it, centred where its
    // markup centres it.

    /// <summary>The worksheet's 12 pt Arial body: the bullets, step text, captions and attribute cells.</summary>
    private const double SrBodyFsPt = 12.0;
    /// <summary>A row this close under the content top already opens its page.</summary>
    private const double SrPageTopTolerancePt = 0.1;

    /// <summary>One piece of a bare-text step row in source order: wrapped text, or a picture's box.</summary>
    private sealed class StepFlowItem
    {
        public string[] Lines = Array.Empty<string>();
        public byte[]? Picture;
        public double WidthPt;
        public double HeightPt;
    }

    private static readonly Regex StepImgRx = new("<img\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);
    private static readonly Regex StepCaptionRx = new(
        "<div\\b[^>]*class\\s*=\\s*['\"]swdt-caption['\"][^>]*>(?<t>[\\s\\S]*?)</div>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);
    private static readonly Regex StepCentredRx = new("text-align\\s*:\\s*center|<center\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);
    private static readonly Regex StepClassRx = new("\\bclass\\s*=\\s*['\"](?<c>[^'\"]*)['\"]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    /// <summary>A row that does not fit the space left moves to a fresh page, unless it already opens one.</summary>
    private static void FitStepRowOnPage(StepRowsState sw, double heightPt)
    {
        if (heightPt <= 0 || sw.rowTop + heightPt <= sw.limit || sw.rowTop <= sw.contentTop + SrPageTopTolerancePt) return;
        NewPage(sw);
        sw.rowTop = sw.yTd = sw.contentTop;
    }

    /// <summary>The bare-text row's text runs and pictures in source order; null when none of its pictures
    /// loads (the row then lays out as one paragraph, the way it always has).</summary>
    private static List<StepFlowItem>? StepFlowItems(StepRowsState sw)
    {
        var items = new List<StepFlowItem>();
        var anyPicture = false;
        var pos = 0;
        foreach (Match m in SafeRegex.Matches(StepImgRx, sw.content))
        {
            AddStepFlowText(items, sw.content[pos..m.Index]);
            pos = m.Index + m.Length;
            if (StepPicture(sw, m.Value) is { } pic) { items.Add(pic); anyPicture = true; }
        }
        AddStepFlowText(items, sw.content[pos..]);
        return anyPicture ? items : null;
    }

    private static void AddStepFlowText(List<StepFlowItem> items, string html)
    {
        var txt = Flat(html);
        if (txt.Length == 0) return;
        var lines = MeasuredWordWrap(txt, SrContentWPt, "Arial", SrBodyFsPt);
        items.Add(new StepFlowItem { Lines = lines, HeightPt = lines.Length * SrLinePt });
    }

    /// <summary>A step picture that loads: the file its src attribute names (quoted or not; a data-src or a src
    /// written in its alt text names none), at its declared px box - its style's, else its width and height
    /// attributes - (else its own pixel size), scaled into the content column when wider; null when its
    /// file cannot be read.</summary>
    private static StepFlowItem? StepPicture(StepRowsState sw, string imgTag)
    {
        var attrs = ParseAttributes(imgTag);
        if (attrs is null || !attrs.TryGetValue("src", out var src) || src.Length == 0
            || LoadConverterImage(DecodeEntities(src), sw.options) is not { Length: > 0 } bytes) return null;
        var wPt = StepPictureSidePx(attrs, "width") * PxToPt;
        var hPt = StepPictureSidePx(attrs, "height") * PxToPt;
        if ((wPt <= 0 || hPt <= 0) && TryReadImagePixelSize(bytes) is (var pw, var ph) && pw > 0 && ph > 0)
        {
            if (wPt <= 0 && hPt <= 0) { wPt = pw * PxToPt; hPt = ph * PxToPt; }
            else if (wPt <= 0) wPt = hPt * pw / ph;
            else hPt = wPt * ph / pw;
        }
        if (wPt <= 0 || hPt <= 0) return null;
        if (wPt > SrContentWPt) { hPt *= SrContentWPt / wPt; wPt = SrContentWPt; }
        // (a vector picture, which the page cannot take, draws as a raster of its box with the drawing letterboxed
        //  in it, as an image paragraph draws one)
        if (IsSvgBytes(bytes)) bytes = ImageRasterizer.RasterizeSvgOnCanvas(bytes, wPt, hPt) ?? bytes;
        return new StepFlowItem { Picture = bytes, WidthPt = wPt, HeightPt = hPt };
    }

    /// <summary>A picture's declared side in px: its style's px length - a style that sets the side in another
    /// form (auto, a percent) overrides the attribute too and declares none - else the attribute's number.
    /// Only those attributes are read: a side named in the alt text or title is prose.</summary>
    private static double StepPictureSidePx(Dictionary<string, string>? attrs, string side)
    {
        if (attrs is null) return 0;
        var m = attrs.TryGetValue("style", out var style)
            ? SafeRegex.Match(style, "(?<![-\\w])" + side + "\\s*:\\s*(?:([\\d.]+)\\s*px)?", RegexOptions.IgnoreCase) : Match.Empty;
        if (!m.Success && attrs.TryGetValue(side, out var attr))
            m = SafeRegex.Match(attr, "^\\s*([\\d.]+)(?:px)?(?=[\\s/]|$)", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static double StepFlowHeightPt(List<StepFlowItem> items)
    {
        double h = 0;
        foreach (var it in items) h += it.HeightPt;
        return Math.Max(h, SrLinePt);
    }

    /// <summary>Draws the row's pieces down the content column; a piece that runs past the page foot opens
    /// the next page (a row taller than a page breaks between its pieces).</summary>
    private static void RenderStepFlow(StepRowsState sw, List<StepFlowItem> items)
    {
        var y = sw.rowTop;
        foreach (var it in items)
        {
            if (y + it.HeightPt > sw.limit && y > sw.contentTop + SrPageTopTolerancePt)
            {
                NewPage(sw);
                y = sw.contentTop;
            }
            if (it.Picture is { } pic)
            {
                FlushOps(sw);
                // (a picture the page cannot decode - a WebP, corrupt data, an SVG the rasteriser could not draw -
                //  keeps its box, empty; any other SVG arrives here as the raster StepPicture drew of it)
                try { sw.page.AddImage(pic, new Rectangle(sw.contentX, sw.pageHeight - y - it.HeightPt, sw.contentX + it.WidthPt, sw.pageHeight - y)); }
                catch { /* an image the page cannot take is left out - the layout carries on */ }
                y += it.HeightPt;
                continue;
            }
            foreach (var ln in it.Lines)
            {
                Run(sw, "FA", SrBodyFsPt, sw.contentX, y, ln);
                y += SrLinePt;
            }
        }
        sw.yTd = Math.Max(y, sw.rowTop + SrLinePt);
    }

    /// <summary>The bare-text row's height as it lays out today: its wrapped lines, one at least.</summary>
    private static double StepBareTextHeightPt(StepRowsState sw)
    {
        var txt = Flat(sw.content);
        return txt.Length > 0 ? Math.Max(1, MeasuredWordWrap(txt, SrContentWPt, "Arial", SrBodyFsPt).Length) * SrLinePt : SrLinePt;
    }

    /// <summary>The markup written before the step's attribute table that is not its consumed heading: the
    /// table's caption ("Table Caption" in a centred div), or an instruction over it.</summary>
    private static string StepTableCaptionHtml(StepRowsState sw)
    {
        var before = sw.content[..sw.atM.Index];
        if (sw.consumedHead && sw.headM.Success && sw.headM.Index < sw.atM.Index)
        {
            // (a heading that holds the table, or is left open past it, leaves no text after itself)
            var headEnd = Math.Min(sw.headM.Index + sw.headM.Length, before.Length);
            before = before[..sw.headM.Index] + before[headEnd..];
        }
        return before;
    }

    /// <summary>A caption's lines over the content column from <paramref name="top"/>: centred where its markup
    /// centres them, else from the column's left edge. Returns the y under them (<paramref name="top"/> for
    /// markup without text).</summary>
    private static double RenderStepCaption(StepRowsState sw, string captionHtml, double top, bool draw = true)
    {
        var caption = Flat(captionHtml);
        if (caption.Length == 0) return top;
        var centred = StepCaptionCentred(sw, captionHtml);
        foreach (var ln in MeasuredWordWrap(caption, SrContentWPt, "Arial", SrBodyFsPt))
        {
            var x = centred ? sw.contentX + (SrContentWPt - MeasureFaceText("Arial", ln, SrBodyFsPt)) / 2 : sw.contentX;
            if (draw) Run(sw, "FA", SrBodyFsPt, x, top, ln);
            top += SrLinePt;
        }
        return top;
    }

    /// <summary>The detable's caption - the widget's own (`swdt-caption`), else the caption of the bordered
    /// table the detable stands in - drawn from the row's top; the label and the table then open under it.
    /// Returns the caption's height, 0 without one.</summary>
    private static double RenderDetableCaption(StepRowsState sw)
    {
        var cap = SafeRegex.Match(StepCaptionRx, sw.content) is { Success: true } cm && Flat(cm.Groups["t"].Value).Length > 0 ? cm.Value : "";
        if (cap.Length == 0 && sw.atM.Success && sw.atM.Index < sw.detM.Index) cap = StepTableCaptionHtml(sw);
        return RenderStepCaption(sw, cap, sw.rowTop) - sw.rowTop;
    }

    /// <summary>Whether a caption's markup centres it: inline (`text-align: center`, a `center` element), or
    /// through a sheet rule for one of its classes that centres its text or a sized box (the warning
    /// heading's 88 px box at `margin: auto`).</summary>
    private static bool StepCaptionCentred(StepRowsState sw, string captionHtml)
        => SafeRegex.IsMatch(StepCentredRx, captionHtml)
            || SafeRegex.Matches(StepClassRx, captionHtml)
                .SelectMany(m => m.Groups["c"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                .Any(cls => sw.css.Any(rule => rule.Key.EndsWith("." + cls, StringComparison.OrdinalIgnoreCase) && RuleCentres(rule.Value)));

    private static bool RuleCentres(Dictionary<string, string> decls)
        => (decls.TryGetValue("text-align", out var align) && align.TrimStart().StartsWith("center", StringComparison.OrdinalIgnoreCase))
            || (decls.ContainsKey("width") && decls.TryGetValue("margin", out var margin)
                && StripImportant(margin).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } sides
                && sides[sides.Length == 1 ? 0 : 1].Equals("auto", StringComparison.OrdinalIgnoreCase));
}
