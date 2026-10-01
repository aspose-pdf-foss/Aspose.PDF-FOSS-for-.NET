using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The scanner stages of the row-block extractor: each pass walks the DOM for one recognised block shape and records its source span.
    private static bool Overlaps(RowBlocksState rb, int s, int e) => rb.extracts.Any(x => s < x.end && e > x.start);

    /// <summary>Centred div/p/span rows holding only inline children with a link become centred link-row blocks.</summary>
    private static void ScanCenteredLinkRows(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag is not ("div" or "p" or "span")) continue;
            if (Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
            var centered = DomDecl(el, "text-align", rb.css)?.Contains("center", StringComparison.OrdinalIgnoreCase) == true;
            if (!centered)
                for (var p = el.Parent; p is not null; p = p.Parent)
                    if (p.Tag == "center") { centered = true; break; }
            if (!centered) continue;
            var hasLink = false;
            var onlyInline = true;
            foreach (var c in el.Children)
            {
                if (c.Tag.Length == 0) continue;
                if (IsHiddenElement(c.Tag, c.Attrs, rb.css)) continue;
                if (c.Tag == "a") hasLink = true;
                else if (!InlineRowTags.Contains(c.Tag)) { onlyInline = false; break; }
            }
            if (!hasLink || !onlyInline) continue;
            var block = BuildCenteredLinkRow(el, rb.css);
            if (block is not null)
                rb.extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }
    }

    /// <summary>Under an RTL document, each table not yet claimed becomes an RTL SVG or topics table block.</summary>
    private static void ScanRtlTables(RowBlocksState rb)
    {
        if (Regex.IsMatch(rb.html, @"<(?:html|body)[^>]*\bdir\s*=\s*[""']?rtl", RegexOptions.IgnoreCase))
        {
            foreach (var el in rb.dom.Descendants())
            {
                if (el.Tag != "table" || Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
                var block = BuildRtlSvgTableBlock(el, rb.css) ?? BuildRtlTopicsTableBlock(el, rb.css);
                if (block is not null)
                    rb.extracts.Add((el.SrcIndex, el.SrcEnd, block));
            }
        }
    }

    /// <summary>A form under a centred ancestor becomes a search-form block.</summary>
    private static void ScanSearchForms(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "form" || Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
            var centered = false;
            for (var p = el.Parent; p is not null; p = p.Parent)
                if (p.Tag == "center"
                    || DomDecl(p, "text-align", rb.css)?.Contains("center", StringComparison.OrdinalIgnoreCase) == true)
                { centered = true; break; }
            if (!centered) continue;
            var block = BuildSearchFormBlock(el, rb.css);
            if (block is not null)
                rb.extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }
    }

    /// <summary>A relatively positioned div with a min-width and min-height becomes a positioned slide block.</summary>
    private static void ScanPositionedSlides(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "div" || Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
            if (DomDecl(el, "position", rb.css)?.Trim()
                    .Equals("relative", StringComparison.OrdinalIgnoreCase) != true) continue;
            var slMinW = ParsePxValue(DomDecl(el, "min-width", rb.css));
            var slMinH = ParsePxValue(DomDecl(el, "min-height", rb.css));
            if (slMinW <= 0 || slMinH <= 0) continue;
            var block = BuildPositionedSlideBlock(el, slMinW, slMinH, rb.css);
            if (block is not null)
                rb.extracts.Add((el.SrcIndex, el.SrcEnd, block));
        }
    }

    /// <summary>A solid-bordered full-width div with at least four flex rows becomes a flex-grid block; the page content width and height come from the nearest sized ancestors, and the claimed span widens to the outermost sole-child wrapper.</summary>
    private static void ScanFlexGrids(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "div" || Overlaps(rb, el.SrcIndex, el.SrcEnd)) continue;
            var fgBorder = DomDecl(el, "border", rb.css);
            if (fgBorder is null || !fgBorder.Contains("solid", StringComparison.OrdinalIgnoreCase)) continue;
            if (DomDecl(el, "width", rb.css)?.Trim() != "100%") continue;
            var flexRows = 0;
            foreach (var d in el.Descendants())
                if (d.Tag is "div" or "tr" && DomDecl(d, "display", rb.css)?.Trim()
                        .Equals("flex", StringComparison.OrdinalIgnoreCase) == true)
                    flexRows++;
            if (flexRows < 4) continue;
            var block = BuildFlexGridBlock(el, rb.css);
            if (block is not null)
            {
                // A positioned page wrapper above the grid may declare the sheet's
                // content width in physical units (width: 8in) — the widen reads it
                // off the block. The wrapper chain's HEIGHT (physical × any percent
                // factors on the way down, e.g. 10in × 107%) bounds the container
                // border, which overflows onto a continuation page.
                var hFactor = 1.0;
                for (var p = el.Parent; p is not null; p = p.Parent)
                {
                    if (p.Tag != "div") continue;
                    if (block.Flex!.PageContentPt <= 0 && DomDecl(p, "width", rb.css) is { } pw
                        && Regex.IsMatch(pw, @"[\d.]+\s*(in|cm|mm|pt)\b", RegexOptions.IgnoreCase)
                        && TryParseLength(pw.Trim()) is { } pwPt && pwPt > 0)
                        block.Flex!.PageContentPt = pwPt;
                    if (DomDecl(p, "height", rb.css) is { } ph)
                    {
                        var phv = ph.Trim();
                        if (Regex.Match(phv, @"^([\d.]+)\s*%$") is { Success: true } pctM
                            && double.TryParse(pctM.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var pctV)
                            && pctV > 0)
                            hFactor *= pctV / 100.0;
                        else if (Regex.IsMatch(phv, @"[\d.]+\s*(in|cm|mm|pt)\b", RegexOptions.IgnoreCase)
                                 && TryParseLength(phv) is { } phPt && phPt > 0)
                        {
                            block.Flex!.PageContentHPt = phPt * hFactor;
                            break;
                        }
                    }
                }
                // Swallow the positioned page-wrapper chain above the grid: each
                // wrapper is a div whose only content is the next one down, and
                // left behind it emits a page-filling height spacer ahead of the
                // grid (the blank-first-page failure).
                var fgTop = el;
                while (fgTop.Parent is { Tag: "div" } fgp)
                {
                    var others = 0;
                    foreach (var c in fgp.Children)
                        if ((c.Tag.Length > 0 && c != fgTop)
                            || (c.Tag.Length == 0 && c.Text.Trim().Length > 0)) others++;
                    if (others > 0) break;
                    fgTop = fgp;
                }
                rb.extracts.Add((fgTop.SrcIndex, fgTop.SrcEnd, block));
            }
        }
    }

    /// <summary>A relatively positioned div inside a div host becomes a positioned card block on the host.</summary>
    private static void ScanPositionedCards(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "div") continue;
            if (DomDecl(el, "position", rb.css)?.Trim()
                    .Equals("relative", StringComparison.OrdinalIgnoreCase) != true) continue;
            HtmlNode? cardHost = null;
            for (var p = el.Parent; p is not null && p.Tag.Length > 0; p = p.Parent)
                if (p.Tag == "div") { cardHost = p; break; }
            if (cardHost is null || Overlaps(rb, cardHost.SrcIndex, cardHost.SrcEnd)) continue;
            var block = BuildPositionedCardBlock(cardHost, el, rb.css);
            if (block is not null)
                rb.extracts.Add((cardHost.SrcIndex, cardHost.SrcEnd, block));
        }
    }

    /// <summary>An absolutely positioned full-width coloured bar inside a sized container becomes a nav-row block on the container.</summary>
    private static void ScanNavRows(RowBlocksState rb)
    {
        foreach (var el in rb.dom.Descendants())
        {
            if (el.Tag != "div") continue;
            if (DomDecl(el, "position", rb.css)?.Equals("absolute", StringComparison.OrdinalIgnoreCase) != true) continue;
            if (DomDecl(el, "width", rb.css)?.Trim() != "100%") continue;
            var barH = ParsePxValue(DomDecl(el, "height", rb.css));
            if (barH <= 0) continue;
            var barColor = ParseCssColor(DomDecl(el, "background-color", rb.css)
                                         ?? DomDecl(el, "background", rb.css) ?? "");
            if (barColor is null) continue;
            // container: nearest ancestor with an explicit height
            HtmlNode? container = null;
            for (var p = el.Parent; p is not null && p.Tag.Length > 0; p = p.Parent)
                if (ParsePxValue(DomDecl(p, "height", rb.css)) > 0) { container = p; break; }
            if (container is null || Overlaps(rb, container.SrcIndex, container.SrcEnd)) continue;
            var block = BuildNavRowBlock(container, el, barColor, barH, rb.css);
            if (block is not null)
                rb.extracts.Add((container.SrcIndex, container.SrcEnd, block));
        }
    }
}
