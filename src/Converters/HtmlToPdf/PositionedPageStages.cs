using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Graphical link overlays: each becomes a placeholder icon at its absolute seat with its own click surface.</summary>
    private static void PlacePositionedGraphicalLinks(PositionedSpansState pos, string seg, List<PosSpan> spans, List<PosLink> links, Page iconPage)
    {
        var graphical = new List<PosLink>();
        if (pos.stlDialect)
        {
            graphical.AddRange(links);
        }
        else
        {
            foreach (var l in links)
            {
                var contained = links.Exists(m => !ReferenceEquals(m, l)
                    && m.Left <= l.Left + 0.5 && m.Top <= l.Top + 0.5
                    && m.Left + m.Width >= l.Left + l.Width - 0.5
                    && m.Top + m.Height >= l.Top + l.Height - 0.5
                    && m.Width * m.Height > l.Width * l.Height + 1);
                if (contained) graphical.Add(l);
            }
            // "Covers text": the old dialect emits per-hotspot spans, so a span
            // STARTING inside the link is the tuned rule.
            var noTextSeen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in links)
            {
                if (graphical.Contains(l)) continue;
                var coversText = spans.Exists(s =>
                    s.Baseline >= l.Top - 1 && s.Baseline <= l.Top + l.Height + s.FontSize
                    && s.Left >= l.Left - 1.5 && s.Left <= l.Left + l.Width + 1.5
                    && s.Text.Trim().Length > 0);
                if (!coversText && noTextSeen.Add(l.Url)) graphical.Add(l);
            }
        }
        if (graphical.Count > 0)
        {
            (var iconName, pos.placeholderIconRef) = RegisterPlaceholderIcon(pos.doc, iconPage, pos.placeholderIconRef);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var isb = new StringBuilder();
            foreach (var g in graphical)
            {
                var ix = g.Left + IconOffsetX;
                var iyTop = g.Top + IconOffsetY;
                var iy = pos.pageH - iyTop - IconSizePt;
                isb.Append($"q {IconSizePt.ToString("F0", inv)} 0 0 {IconSizePt.ToString("F0", inv)} ");
                isb.AppendLine($"{ix.ToString("F2", inv)} {iy.ToString("F2", inv)} cm /{iconName} Do Q");
                // Each graphical link gets a line-box-shaped annot.
                pos.pendingLinks.Add((iconPage, new Aspose.Pdf.Rectangle(
                    ix, pos.pageH - iyTop - 13.29, ix + 34, pos.pageH - iyTop), g.Url));
            }
            iconPage.AddContentStream(Encoding.ASCII.GetBytes(isb.ToString()));
        }
    }

    /// <summary>The page's source lines, reflowed onto output pages as paragraphs with their link runs.</summary>
    private static Page EmitPositionedPageLines(PositionedSpansState pos, int p, List<List<PosSpan>> lines, List<PosLink> links)
    {
        Page? iconPage = default;
        // The reflow is CONTINUOUS across source page divs — a
        // multi-page document's text flows as one stream, filling each output page
        // before starting the next (page 1 can carry several source pages'
        // text). Only the first div opens a page; later divs keep the cursor.
        if (pos.page is null) StartPositionedPage(pos);
        if (pos.stlImgBg)
        {
            // One blank slot precedes every source page's text — empty source
            // pages included; a slot that crosses the bottom margin carries onto
            // the next output page.
            if (pos.baselineY > pos.pageH - BottomMarginPt) StartPositionedPage(pos);
            pos.baselineY += PitchPt;
        }
        iconPage = pos.page!;

        if (pos.stlImgBg)
        {
            // stl_: one paragraph per line div, in document order — same-baseline
            // divs never merge (the number column of a tabbed TOC stacks before
            // its titles, in the order the exporter wrote them).
            foreach (var para in pos.stlPages[p])
                EmitStlParagraph(pos.doc, para, pos.pendingLinks,
                    FontSizePt, StlSupFontSizePt, StlSupRisePt, StlSupLineExtraPt,
                    PitchPt, MarginSide, pos.contentW, pos.pageH, BottomMarginPt,
                    pos.docFontDict, pos);
        }
        else foreach (var line in lines)
        {
            line.Sort((a, b) => a.Left.CompareTo(b.Left));

            // Direct concatenation: PdfToHtml span texts carry their own spacing
            // (runs of whitespace collapse to a single space). Link coverage is
            // resolved per CHARACTER against the overlay rectangles, estimating
            // each character's source x from the span origin plus measured
            // advances, so an overlay covering part of a span (an inline "here"
            // link) doesn't paint the whole span blue.
            var sb = new StringBuilder();
            var urls = new List<string?>();
            foreach (var s in line)
            {
                var rowLinks = links.FindAll(l =>
                    s.Baseline >= l.Top && s.Baseline <= l.Top + l.Height + 2);
                var cx = s.Left;
                for (var ci = 0; ci < s.Text.Length; ci++)
                {
                    var ch = s.Text[ci];
                    var cpEnd = ci;
                    (var adv, cpEnd) = MeasureSerifChar(s.Text, cpEnd, s.FontSize);
                    var mid = cx + adv / 2;
                    cx += adv;
                    if (char.IsWhiteSpace(ch))
                    {
                        if (sb.Length > 0 && sb[^1] != ' ')   // collapse runs
                        {
                            sb.Append(' ');
                            urls.Add(null);
                        }
                        continue;
                    }
                    var covering = rowLinks.Find(l => mid >= l.Left - 0.5 && mid <= l.Left + l.Width + 0.5);
                    for (var u = ci; u <= cpEnd; u++)
                    {
                        sb.Append(s.Text[u]);
                        urls.Add(s.Urls is not null ? s.Urls[u] : covering?.Url);
                    }
                    ci = cpEnd;
                }
            }
            // Trim trailing whitespace (trailing space spans widen nothing).
            var text = sb.ToString();
            var end = text.Length;
            while (end > 0 && char.IsWhiteSpace(text[end - 1])) end--;
            if (end == 0) continue;   // whitespace-only source line
            text = text[..end];
            urls.RemoveRange(end, urls.Count - end);

            EmitReflowedLine(pos.doc, text, urls, pos.pendingLinks,
                FontSizePt, PitchPt, MarginSide, pos.contentW, pos.pageW, pos.pageH,
                BottomMarginPt, FirstBaselinePt, pos.docFontDict, pos);
        }
        return iconPage;
    }

    /// <summary>The page's link overlays and the grouping of its spans into source lines.</summary>
    private static (List<PosLink> links, List<List<PosSpan>> lines) ReadPositionedLinks(PositionedSpansState pos, string seg, List<PosSpan> spans)
    {
        List<PosLink>? links = default;
        List<List<PosSpan>>? lines = default;
        links = new List<PosLink>();
        foreach (Match m in Regex.Matches(seg,
            @"<a\s+(?=[^>]*class=""pdf-link"")(?=[^>]*href=""(?<href>[^""]*)"")(?=[^>]*style=""(?<st>[^""]*)"")[^>]*>",
            RegexOptions.Singleline))
        {
            var st = m.Groups["st"].Value;
            links.Add(new PosLink
            {
                Left = StylePt(st, "left") ?? 0,
                Top = StylePt(st, "top") ?? 0,
                Width = StylePt(st, "width") ?? 0,
                Height = StylePt(st, "height") ?? 0,
                Url = DecodeEntities(m.Groups["href"].Value),
            });
        }
        if (pos.stlDialect)
        {
            // The stl_ dialect's link-annotation rectangles are invisible
            // stl_grlink overlays (a positioned div > a > transparent img), in
            // em units. They feed the same graphical-link placeholder pass as
            // the old dialect's pdf-link overlays - broken-image icons are
            // drawn for hotspots whose raster fell out of the
            // reflow.
            double NumL(string s) => double.Parse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture);
            foreach (Match m in Regex.Matches(seg,
                @"<div style=""position:absolute;left:(?<l>-?[\d.]+)em;top:(?<t>-?[\d.]+)em;width:(?<w>[\d.]+)em;height:(?<h>[\d.]+)em;"">\s*<a\s+[^>]*href=""(?<href>[^""]*)""[^>]*>\s*<img[^>]*class=""stl_grlink""",
                RegexOptions.Singleline))
            {
                links.Add(new PosLink
                {
                    Left = NumL(m.Groups["l"].Value) * StlEmPt,
                    Top = NumL(m.Groups["t"].Value) * StlEmPt,
                    Width = NumL(m.Groups["w"].Value) * StlEmPt,
                    Height = NumL(m.Groups["h"].Value) * StlEmPt,
                    Url = DecodeEntities(m.Groups["href"].Value),
                });
            }
        }

        // ── Group spans into source lines by baseline (pdf-page dialect) ──
        lines = new List<List<PosSpan>>(spans.Count);
        if (!pos.stlImgBg)
        {
            spans.Sort((a, b) => a.Baseline != b.Baseline
                ? a.Baseline.CompareTo(b.Baseline) : a.Left.CompareTo(b.Left));
            foreach (var s in spans)
            {
                if (lines.Count > 0 && Math.Abs(lines[^1][0].Baseline - s.Baseline) <= 2.0)
                    lines[^1].Add(s);
                else
                    lines.Add(new List<PosSpan> { s });
            }
        }
        return (links, lines);
    }

    /// <summary>Every positioned run on the source page, with its seat, face and size.</summary>
    private static List<PosSpan> ReadPositionedSpans(PositionedSpansState pos, string seg, int p)
    {
        List<PosSpan>? spans = default;
        spans = new List<PosSpan>();
        foreach (Match m in Regex.Matches(seg,
            @"<span class=""pdf-text"" style=""(?<st>[^""]*)"">(?<body>.*?)</span>",
            RegexOptions.Singleline))
        {
            var st = m.Groups["st"].Value;
            var text = DecodeEntities(Regex.Replace(m.Groups["body"].Value, "<[^>]+>", ""));
            if (text.Length == 0) continue;
            spans.Add(new PosSpan
            {
                Left = StylePt(st, "left") ?? 0,
                Top = StylePt(st, "top") ?? 0,
                FontSize = StylePt(st, "font-size") ?? 12,
                Text = text,
            });
        }
        // Reflow stl_ shape (img background): line divs were parsed up front
        // into stlPages (document order, with per-character link, word-spacing
        // and sup data). Legacy stl_ shape (object background): parse into
        // baseline-merged PosSpans as before.
        if (pos.stlDialect && !pos.stlImgBg)
        {
            // stl_ text runs: <div class="stl_01" style="left:Xem;top:Yem;">
            //   <span class="stl_NN …">word </span><span …>word </span>…</div>
            // left/top are em (× 12 = pt). Each line div wraps one or more
            // word-anchored spans; the first span's font-size class fixes the
            // run's size.
            double Num(string s) => double.Parse(s,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture);
            foreach (Match dm in Regex.Matches(seg,
                @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?"">(?<body>.*?)</div>",
                RegexOptions.Singleline))
            {
                // A line div wraps ONE OR MORE word-anchored spans (a justified or
                // positioned line is split into a span per word); concatenate them
                // all \u2014 the earlier single-span parse dropped every span past the
                // first, so such a line re-imported as just its first word. The
                // first span's font-size class fixes the run's size.
                // Linked runs are spans wrapped in <a href="\u2026"> INSIDE the div
                // (there is no positioned overlay rectangle in this dialect);
                // the wrapped characters keep the URL so the reflow paints them
                // as links.
                var body = dm.Groups["body"].Value;
                var anchors = new List<(int Start, int End, string Url)>();
                foreach (Match am in Regex.Matches(body,
                    @"<a\s+[^>]*href=""(?<href>[^""]*)""[^>]*>(?<ab>.*?)</a>",
                    RegexOptions.Singleline))
                    anchors.Add((am.Index, am.Index + am.Length,
                        DecodeEntities(am.Groups["href"].Value)));
                var sbLine = new StringBuilder();
                var lineUrls = new List<string?>();
                double fsEm = 1.0; var fsSet = false;
                foreach (Match sm in Regex.Matches(body,
                    @"<span class=""(?<cls>[^""]*)""[^>]*>(?<stext>.*?)</span>",
                    RegexOptions.Singleline))
                {
                    string? url = null;
                    foreach (var a in anchors)
                        if (sm.Index >= a.Start && sm.Index < a.End) { url = a.Url; break; }
                    var stext = DecodeEntities(Regex.Replace(sm.Groups["stext"].Value, "<[^>]+>", ""));
                    sbLine.Append(stext);
                    for (var k = 0; k < stext.Length; k++) lineUrls.Add(url);
                    if (!fsSet)
                        foreach (var cls in sm.Groups["cls"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            if (pos.stlFontSizes!.TryGetValue(cls, out var v)) { fsEm = v; fsSet = true; break; }
                }
                // Each line div ends with a sentinel &nbsp; (occasionally " &nbsp;").
                // Keep it as a single trailing space: when two divs share a baseline
                // and merge into one reflow line the gap becomes a word space
                // ("\u2026to" + "meet" \u2192 "to meet"); a line-final div's trailing space is
                // trimmed downstream.
                var raw = sbLine.ToString();
                var keep = raw.Length;
                while (keep > 0 && (raw[keep - 1] == '\u00A0' || raw[keep - 1] == ' ')) keep--;
                if (keep == 0) continue;
                var text = raw[..keep] + " ";
                lineUrls.RemoveRange(keep, lineUrls.Count - keep);
                lineUrls.Add(null);
                var hasUrl = false;
                foreach (var u in lineUrls) if (u is not null) { hasUrl = true; break; }
                spans.Add(new PosSpan
                {
                    Left = Num(dm.Groups["l"].Value) * StlEmPt,
                    Top = Num(dm.Groups["t"].Value) * StlEmPt,
                    FontSize = fsEm * StlEmPt,
                    Text = text,
                    Urls = hasUrl ? lineUrls.ToArray() : null,
                });
            }
        }
        return spans;
    }
}
