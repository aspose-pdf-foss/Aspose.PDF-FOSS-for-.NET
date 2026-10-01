using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Every positioned div of the source, in the stl_ class dialect or the inline-style one.</summary>
    private static (List<FixedPageDiv> divs, bool emGridMarkup, FixedSheetModel model)? ReadFixedLayoutDivs(string html, HtmlLoadOptions? options)
    {
        List<FixedPageDiv>? divs = default;
        bool emGridMarkup = default;
        var model = new FixedSheetModel();
        divs = new List<FixedPageDiv>();
        // The em-compensation grid dialect (every letter-spacing on the 0.01 em
        // grid): its width budget is exact and its raster background carries
        // IMAGES ONLY, so the raster ink edge must not cap the text width.
        emGridMarkup = false;
        var stl = IsStlPositionedHtml(html);
        if (stl)
        {
            if (ReadStlFixedDivs(html, options, divs) is not { } emGrid) return null;
            emGridMarkup = emGrid;
        }
        else if (!ReadInlineStyleFixedDivs(html, options, divs))
        {
            // The em-unit class-positioned export: no page_N container, no inline
            // geometry — every box and every seat comes out of the stylesheet.
            // The inline-style INCH layout: a page container sized in inches holding absolute
            // boxes in inches, no stylesheet at all (see InchStyleFixed.cs). It shares the
            // em-class dialect band geometry - the same 78 / 698 / 90 the reference uses for
            // every one of these fixed-layout imports.
            if (ReadInchStyleFixedDivs(html, divs))
            {
                model = new FixedSheetModel
                {
                    ContentTopPt = StlEmContentTopPt,
                    ContentBottomPt = 0,
                    RightPadPt = StlEmRightPadPt,
                    BandPitchPt = StlEmBandPitchPt,
                    FitBottomPt = StlEmFitBottomPt,
                    WidowTopPt = StlEmWidowTopPt,
                    SheetOnInkExtent = true,
                };
                return (divs, emGridMarkup, model);
            }
            if (!ReadStlEmClassFixedDivs(html, options, divs)) return null;
            model = new FixedSheetModel
            {
                ContentTopPt = StlEmContentTopPt,
                ContentBottomPt = 0,   // solved from the pitch once the sheet height is known
                RightPadPt = StlEmRightPadPt,
                BandPitchPt = StlEmBandPitchPt,
                FitBottomPt = StlEmFitBottomPt,
                WidowTopPt = StlEmWidowTopPt,
            };
        }
        return (divs, emGridMarkup, model);
    }

    /// <summary>The inline-style dialect: each pdf-page div's own left/top/width geometry.</summary>
    private static bool ReadInlineStyleFixedDivs(string html, HtmlLoadOptions? options, List<FixedPageDiv> divs)
    {
        // pdf-page dialect: self-contained (inline pt styles, data-URI background).
        var pageDivs = Regex.Matches(html, @"<div class=""pdf-page""[^>]*style=""(?<st>[^""]*)""[^>]*>");
        if (pageDivs.Count == 0) return false;
        for (var p = 0; p < pageDivs.Count; p++)
        {
            var segStart = pageDivs[p].Index;
            var segEnd = p + 1 < pageDivs.Count ? pageDivs[p + 1].Index : html.Length;
            var seg = html[segStart..segEnd];
            var div = new FixedPageDiv
            {
                SrcW = StylePt(pageDivs[p].Groups["st"].Value, "width") ?? 612.0,
                SrcH = StylePt(pageDivs[p].Groups["st"].Value, "height") ?? 792.0,
            };
            var bg = Regex.Match(seg, @"<img\s+(?=[^>]*class=""pdf-page-bg"")[^>]*src=""(?<src>[^""]*)""");
            if (bg.Success)
                div.Background = LoadConverterImage(bg.Groups["src"].Value, options);

            foreach (Match m in Regex.Matches(seg,
                @"<span class=""pdf-text"" style=""(?<st>[^""]*)"">(?<body>.*?)</span>",
                RegexOptions.Singleline))
            {
                var st = m.Groups["st"].Value;
                var text = DecodeEntities(Regex.Replace(m.Groups["body"].Value, "<[^>]+>", ""));
                if (text.Length == 0) continue;
                var famM = Regex.Match(st, @"font-family:\s*(?<v>[^;""]+)");
                var colM = Regex.Match(st, @"(?<!-)color:\s*(?<v>[^;]+)");
                div.Spans.Add(new FixedSpan
                {
                    Left = StylePt(st, "left") ?? 0,
                    Top = StylePt(st, "top") ?? 0,
                    FontSize = StylePt(st, "font-size") ?? 12,
                    Text = text,
                    Face = ResolveFixedFace(famM.Success ? famM.Groups["v"].Value : "serif"),
                    Color = ParseCssColorRgb(colM.Success ? colM.Groups["v"].Value : null),
                });
            }
            divs.Add(div);
        }
        return true;
    }

    /// <summary>The stl_ class dialect: the stylesheet box, the page containers and the spans they hold.</summary>
    private static bool? ReadStlFixedDivs(string html, HtmlLoadOptions? options, List<FixedPageDiv> divs)
    {
        bool emGridMarkup = false;
        var fx = new FixedDivReadState();
        fx.html = html;
        fx.options = options;
        fx.divs = divs;
        fx.css = GatherStlCss(fx.html, fx.options?.BasePathAutoDerived == true ? null : fx.options);
        if (string.IsNullOrWhiteSpace(fx.css)) return null;
        emGridMarkup = StlLetterSpacingOnEmGrid(fx.css);

        fx.clsFont = new Dictionary<string, (double? fs, string? fam, string? col, double? ls)>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(fx.css, @"\.(?<cls>[\w-]+)\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline))
        {
            var body = m.Groups["body"].Value;
            double? fs = null, ls = null;
            var fm = Regex.Match(body, @"font-size:\s*(?<v>[\d.]+)em");
            if (fm.Success) fs = double.Parse(fm.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var lm = Regex.Match(body, @"letter-spacing:\s*(?<v>-?[\d.]+)em");
            if (lm.Success) ls = double.Parse(lm.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var am = Regex.Match(body, @"font-family:\s*""?(?<v>[^;""}]+)");
            var cm = Regex.Match(body, @"(?<!-)color:\s*(?<v>[^;}]+)");
            var key = m.Groups["cls"].Value;
            // Later rules override earlier ones per property, like a cascade.
            fx.clsFont.TryGetValue(key, out var prev);
            fx.clsFont[key] = (fs ?? prev.fs,
                am.Success ? am.Groups["v"].Value.Trim() : prev.fam,
                cm.Success ? cm.Groups["v"].Value.Trim() : prev.col,
                ls ?? prev.ls);
        }

        fx.ownFaces = ParseFontFaces(fx.css, fx.html, fx.options);
        fx.pageDivs = Regex.Matches(fx.html, @"<div id=""page_\d+""[^>]*>");
        if (fx.pageDivs.Count == 0) return false;
        for (var p = 0; p < fx.pageDivs.Count; p++)
        {
            if (!ReadStlFixedPageDiv(fx, p)) return false;
        }
        return true;
    }

    /// <summary>True when the stylesheet declares em letter-spacing and every value sits on the
    /// hundredth-em grid: the markup was authored on the em grid.</summary>
    private static bool StlLetterSpacingOnEmGrid(string css)
    {
        var sawLs = false; var onGrid = true;
        foreach (Match lm in Regex.Matches(css, @"letter-spacing:\s*(-?[\d.]+)em"))
        {
            var le = double.Parse(lm.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            if (le == 0) continue;
            sawLs = true;
            var cents = le * 100.0;
            if (System.Math.Abs(cents - System.Math.Round(cents)) > 1e-6) { onGrid = false; break; }
        }
        return sawLs && onGrid;
    }
}
