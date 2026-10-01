using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Read the em-unit class-positioned stl_ export into fixed page divs, or
    /// false when the markup is not it. The dialect needs a resolvable stylesheet (all of
    /// its geometry lives there), a repeated pt-sized container div per source page, and
    /// class-positioned line divs — flow markup of any kind rules it out.</summary>
    private static bool ReadStlEmClassFixedDivs(string html, HtmlLoadOptions? options, List<FixedPageDiv> divs)
    {
        if (!IsStlEmClassCandidate(html)) return false;
        var css = GatherStlCss(html, options?.BasePathAutoDerived == true ? null : options);
        if (string.IsNullOrWhiteSpace(css)) return false;

        var classes = ReadStlEmClasses(css);
        if (StlEmContainerClass(html, classes) is not { } container) return false;
        var box = classes[container];
        var emPt = box.FontSizePt!.Value;
        if (emPt <= 0) return false;

        if (StlEmPositionedLineCount(html, classes) < 3) return false;

        var ownFaces = ParseFontFaces(css, html, options);
        var pages = Regex.Matches(html, @"<div class=""" + Regex.Escape(container) + @"""[^>]*>");
        for (var p = 0; p < pages.Count; p++)
        {
            var segEnd = p + 1 < pages.Count ? pages[p + 1].Index : html.Length;
            var seg = html[pages[p].Index..segEnd];
            var div = new FixedPageDiv { SrcW = box.WidthPt!.Value, SrcH = box.HeightPt!.Value };
            ReadStlFixedDivObject(seg, div);
            if (div.Background is null && StlEmPageBackground(seg, classes, options) is { } pageBg)
            {
                div.Background = pageBg;
                if (TryReadImagePixelSize(pageBg) is { } bgPx && bgPx.w > 0 && bgPx.h > 0)
                {
                    div.BackgroundW = bgPx.w * PxPt;
                    div.BackgroundH = bgPx.h * PxPt;
                }
            }
            ReadStlEmClassSpans(seg, classes, ownFaces, emPt, div);
            div.InFlowContentHeightPt = StlEmInFlowContentHeightPt(seg, classes, emPt);
            divs.Add(div);
        }
        return divs.Count > 0 && StlEmHasSpans(divs);
    }

    /// <summary>The height of a container's IN-FLOW content: the explicit heights its class rules
    /// give the block divs inside it (the raster export's `.stl_05 { height: 6.6em }` layer), summed
    /// over siblings, an outermost explicit height standing for everything nested in it. A
    /// positioned div and everything under it counts nothing (the reference measures the flow the
    /// container's own box holds, not its absolute lines), and a div with no declared height is
    /// a wrapper of zero height on its own.</summary>
    private static double StlEmInFlowContentHeightPt(string seg, Dictionary<string, StlEmCls> classes, double emPt)
    {
        var total = 0.0;
        // (positioned, counted): one entry per open div; the container itself is the first.
        var open = new List<(bool positioned, bool counted)>();
        foreach (Match m in Regex.Matches(seg, @"<div(?<attrs>[^>]*)>|</div\s*>"))
        {
            if (m.Value[1] == '/')
            {
                if (open.Count > 0) open.RemoveAt(open.Count - 1);
                continue;
            }
            var attrs = m.Groups["attrs"].Value;
            var isContainer = open.Count == 0;
            var positioned = !isContainer && StlEmLineSeat(attrs, classes) is not null;
            var shielded = false;
            foreach (var (p, c) in open) if (p || c) { shielded = true; break; }
            var height = isContainer || positioned || shielded ? 0.0 : StlEmDeclaredHeightPt(attrs, classes, emPt);
            if (height > 0) total += height;
            open.Add((positioned, height > 0));
        }
        return total;
    }

    /// <summary>The explicit height a div's class rules declare, in pt (em against the dialect's em).</summary>
    private static double StlEmDeclaredHeightPt(string attrs, Dictionary<string, StlEmCls> classes, double emPt)
    {
        if (Regex.Match(attrs, @"class=""(?<v>[^""]*)""") is not { Success: true } cm) return 0;
        var height = 0.0;
        foreach (var cn in cm.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!classes.TryGetValue(cn, out var c)) continue;
            if (c.HeightPt is > 0) height = c.HeightPt.Value;
            else if (c.HeightEm is > 0) height = c.HeightEm.Value * emPt;
        }
        return height;
    }


    /// <summary>A line div's seat, in ems, or null when the div is not a positioned line. This
    /// export family spells the seat three ways and they all mean the same box: wholly in the
    /// stylesheet class, wholly in the inline style, or the class declaring `position: absolute`
    /// while the inline style carries the offsets. The inline value wins, as the cascade says.
    /// </summary>
    private static (double LeftEm, double TopEm)? StlEmLineSeat(string attrs, Dictionary<string, StlEmCls> classes)
    {
        var st = Regex.Match(attrs, @"style=""(?<v>[^""]*)""") is { Success: true } sm ? sm.Groups["v"].Value : "";
        var absolute = Regex.IsMatch(st, @"position:\s*absolute");
        double? leftEm = StlEmNum(st, "left"), topEm = StlEmNum(st, "top");
        if (Regex.Match(attrs, @"class=""(?<v>[^""]*)""") is { Success: true } cm)
            foreach (var cn in cm.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!classes.TryGetValue(cn, out var c)) continue;
                absolute |= c.Absolute;
                leftEm ??= c.LeftEm;
                topEm ??= c.TopEm;
            }
        return absolute && leftEm is not null && topEm is not null ? (leftEm.Value, topEm.Value) : null;
    }

    /// <summary>How many positioned lines the document seats, by the same cascade the reader
    /// uses. The dialect's cheap pre-filter counts these rather than the stylesheet's positioned
    /// CLASSES alone, because the raster flavour of this export seats every line inline.</summary>
    private static int StlEmPositionedLineCount(string html, Dictionary<string, StlEmCls> classes)
    {
        var n = 0;
        foreach (Match dm in Regex.Matches(html, @"<div(?<attrs>[^>]*)>"))
            if (StlEmLineSeat(dm.Groups["attrs"].Value, classes) is not null) n++;
        return n;
    }

    /// <summary>The page's full-bleed raster background: the image this source page's container
    /// seats at its own origin. The vector flavour of the export carries the page as an
    /// &lt;object&gt; sidecar; the raster flavour carries one PNG per source page instead.</summary>
    private static byte[]? StlEmPageBackground(string seg, Dictionary<string, StlEmCls> classes, HtmlLoadOptions? options)
    {
        foreach (Match im in Regex.Matches(seg, @"<img(?<attrs>[^>]*)>"))
        {
            var attrs = im.Groups["attrs"].Value;
            if (StlEmLineSeat(attrs, classes) is not var (l, t) || l != 0 || t != 0) continue;
            if (Regex.Match(attrs, @"src=""(?<v>[^""]*)""") is not { Success: true } sm) continue;
            if (LoadConverterImage(DecodeEntities(sm.Groups["v"].Value), options) is { Length: > 0 } bytes)
                return bytes;
        }
        return null;
    }

    private static bool StlEmHasSpans(List<FixedPageDiv> divs)
    {
        foreach (var d in divs)
            if (d.Spans.Count > 0) return true;
        return false;
    }

    /// <summary>Every class-positioned line div of one source page. A line div holds one or
    /// more inline spans that flow from its left edge, each at the accumulated advance.</summary>
    private static void ReadStlEmClassSpans(string seg, Dictionary<string, StlEmCls> classes,
        Dictionary<string, List<OwnFace>> ownFaces, double emPt, FixedPageDiv div)
    {
        foreach (Match dm in Regex.Matches(seg,
            @"<div(?<attrs>[^>]*)>(?<body>(?:(?!</?div\b).)*?)</div>",
            RegexOptions.Singleline))
        {
            if (StlEmLineSeat(dm.Groups["attrs"].Value, classes) is not var (leftEm, topEm)) continue;
            var x = leftEm * emPt;
            var top = topEm * emPt;
            foreach (Match sm in Regex.Matches(dm.Groups["body"].Value,
                @"<span class=""(?<cls>[^""]*)""[^>]*>(?<text>.*?)</span>", RegexOptions.Singleline))
            {
                var text = DecodeEntities(Regex.Replace(sm.Groups["text"].Value, "<[^>]+>", ""));
                if (text.Length == 0) continue;
                var span = StlEmClassSpan(sm.Groups["cls"].Value, classes, ownFaces, emPt, x, top, text);
                div.Spans.Add(span);
                x += MeasureSpanLine(span, text);
            }
        }
    }

    /// <summary>One inline span: its class-cascaded typography, the face the document's own
    /// programs give it, and the baseline its line box seats on.</summary>
    private static FixedSpan StlEmClassSpan(string clsList, Dictionary<string, StlEmCls> classes,
        Dictionary<string, List<OwnFace>> ownFaces, double emPt, double x, double top, string text)
    {
        double fsEm = 1.0, lsEm = 0, wsEm = 0, lhEm = 0;
        string fam = "serif";
        string? col = null;
        foreach (var cls in clsList.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!classes.TryGetValue(cls, out var c)) continue;
            if (c.FontSizeEm is not null) fsEm = c.FontSizeEm.Value;
            if (c.LineHeightEm is not null) lhEm = c.LineHeightEm.Value;
            if (c.LetterSpacingEm is not null) lsEm = c.LetterSpacingEm.Value;
            if (c.WordSpacingEm is not null) wsEm = c.WordSpacingEm.Value;
            if (c.Family is not null) fam = c.Family;
            if (c.Color is not null) col = c.Color;
        }
        var fs = fsEm * emPt;
        var famKey = fam.Split(',')[0].Trim().Trim('"', '\'');
        var span = new FixedSpan
        {
            Left = x,
            Top = top,
            FontSize = fs,
            LetterSpacing = lsEm * fs,
            WordSpacing = wsEm * fs,
            Text = text,
            Face = ResolveFixedFace(fam),
            Own = ownFaces.TryGetValue(famKey, out var ofl) ? ofl : null,
            AllOwn = ownFaces.Count > 0 ? ownFaces : null,
            Color = ParseCssColorRgb(col) ?? (0, 0, 0),
        };
        var (asc, desc) = StlEmFaceMetrics(span);
        // The exporter writes `line-height` as the face's own ascent+descent, so the CSS
        // half-leading is zero and the box top sits exactly on the glyph box. Keeping the
        // general form means a sheet that declares anything else still seats correctly.
        span.LineHeightPt = lhEm > 0 ? lhEm * fs : (asc + desc) * fs;
        span.BaselineDropPt = (span.LineHeightPt - (asc + desc) * fs) / 2 + asc * fs;
        return span;
    }

    /// <summary>Ascent and descent of the span's face as a fraction of the em — from the
    /// document's own program where it has one, else the resolved system face.</summary>
    private static (double asc, double desc) StlEmFaceMetrics(FixedSpan s)
    {
        var parser = s.Own is { Count: > 0 } ? s.Own[0].Parser : PosFace(s.Face).parser;
        if (parser is null || parser.UnitsPerEm <= 0 || parser.Ascender == 0) return (1.0, 0.2);
        return (parser.Ascender / (double)parser.UnitsPerEm,
                -parser.Descender / (double)parser.UnitsPerEm);
    }

    /// <summary>Sheet y of one line of <paramref name="s"/> on the band being drawn, or null
    /// when that line belongs to another sheet. A dialect with no widow rule keeps its flow
    /// seat and lets the band clip decide; one with a widow rule seats each LINE itself, so a
    /// box that would cross the band's bottom edge moves whole to the next sheet's top.</summary>
    private static double? FixedLineSeat(FixedSpan s, FixedSheetModel model, int li,
        double cum, double bandH, double band)
    {
        if (model.FitBottomPt <= 0)
            return model.ContentTopPt - (band * bandH - cum) + s.Top + li * 1.2 * s.FontSize;
        var lineH = s.LineHeightPt > 0 ? s.LineHeightPt : 1.2 * s.FontSize;
        var top = cum + s.Top + li * lineH;
        var seatBand = (int)Math.Floor(top / bandH);
        var y = model.ContentTopPt + top - seatBand * bandH;
        // A line whose box starts at or below the band's bottom edge CONTINUES on the next
        // sheet at its flow seat (probed: tops 770..785 land at 72..87); only a box that
        // straddles the edge moves to the top margin.
        if (y >= model.FitBottomPt)
        {
            seatBand++;
            y -= bandH;
        }
        else if (y + lineH > model.FitBottomPt)
        {
            seatBand++;
            y = model.WidowTopPt;
        }
        return seatBand == (int)band ? y : null;
    }
}
