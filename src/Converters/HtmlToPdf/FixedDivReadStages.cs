using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Read one page_N div: its container box, background, the svg object or the class-styled spans. False when the box is not in the stylesheet (the caller reflows).</summary>
    private static bool ReadStlFixedPageDiv(FixedDivReadState fx, int p)
    {
        var segStart = fx.pageDivs[p].Index;
        var segEnd = p + 1 < fx.pageDivs.Count ? fx.pageDivs[p + 1].Index : fx.html.Length;
        var seg = fx.html[segStart..segEnd];
        var div = new FixedPageDiv { SrcW = 612.0, SrcH = 842.0 };

        // Page box from the container's stylesheet class (width/height em).
        var clsAttr = Regex.Match(fx.pageDivs[p].Value, @"class=""(?<c>[^""]+)""");
        var boxResolved = false;
        if (clsAttr.Success)
        {
            foreach (var cls in clsAttr.Groups["c"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var box = Regex.Match(fx.css,
                    @"\." + Regex.Escape(cls) + @"\s*\{[^}]*width:\s*(?<w>[\d.]+)em[^}]*height:\s*(?<h>[\d.]+)em",
                    RegexOptions.Singleline);
                if (box.Success)
                {
                    div.SrcW = double.Parse(box.Groups["w"].Value, System.Globalization.CultureInfo.InvariantCulture) * StlEmPt;
                    div.SrcH = double.Parse(box.Groups["h"].Value, System.Globalization.CultureInfo.InvariantCulture) * StlEmPt;
                    boxResolved = true;
                    break;
                }
            }
        }
        if (!boxResolved) return false;   // container box not in the stylesheet → reflow

        var bg = Regex.Match(seg, @"<img\s+(?=[^>]*class=""stl_04"")[^>]*src=""(?<src>[^""]*)""|<img\s+(?=[^>]*src=""(?<src2>[^""]*)"")[^>]*class=""stl_04""");
        if (bg.Success)
        {
            var src = bg.Groups["src"].Success ? bg.Groups["src"].Value : bg.Groups["src2"].Value;
            div.Background = LoadConverterImage(DecodeEntities(src), fx.options);
        }
        ReadStlFixedDivObject(seg, div);

        ReadStlFixedDivSpans(fx, seg, div);
        fx.divs.Add(div);
        return true;
    }

    /// <summary>Read the page's positioned span divs into the page div: class fonts, word spacing and the running x advance.</summary>
    private static void ReadStlFixedDivSpans(FixedDivReadState fx, string seg, FixedPageDiv div)
    {
        foreach (Match dm in Regex.Matches(seg,
            @"<div class=""[^""]*"" style=""left:(?<l>-?[\d.]+)em;\s*top:(?<t>-?[\d.]+)em;?[^""]*"">(?<body>.*?)</div>",
            RegexOptions.Singleline))
        {
            double Num(string s) => double.Parse(s,
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            // A line div holds ONE OR MORE spans (a pinned line is split into
            // a span per word-anchored segment, each with its own spacing
            // classes and optionally its own <a> wrapper). Segments flow one
            // after another from the div's left edge: each becomes its own
            // FixedSpan at the accumulated x, advanced by the segment's
            // measured width with its pinned letter/word-spacing applied.
            var x = Num(dm.Groups["l"].Value) * StlEmPt;
            var top = Num(dm.Groups["t"].Value) * StlEmPt;
            foreach (Match m in Regex.Matches(dm.Groups["body"].Value,
                @"<span class=""(?<cls>[^""]*)""(?:\s+style=""(?<sst>[^""]*)"")?[^>]*>(?<stext>.*?)</span>",
                RegexOptions.Singleline))
            {
                var text = DecodeEntities(Regex.Replace(m.Groups["stext"].Value, "<[^>]+>", ""));
                if (text.Length == 0) continue;
                double fsEm = 1.0, lsEm = 0.0;
                string fam = "sans-serif";
                string? col = null;
                foreach (var cls in m.Groups["cls"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!fx.clsFont.TryGetValue(cls, out var e)) continue;
                    if (e.fs is not null && e.fam is not null) { fsEm = e.fs.Value; fam = e.fam; col ??= e.col; }
                    else if (e.fs is not null) fsEm = e.fs.Value;
                    if (e.ls is not null) lsEm = e.ls.Value;
                    if (e.col is not null && e.fs is not null) col = e.col;
                }
                var wsM = Regex.Match(m.Groups["sst"].Value ?? "", @"word-spacing:\s*(-?[\d.]+)em");
                var wsEm = wsM.Success ? Num(wsM.Groups[1].Value) : 0.0;
                var famKey = fam.Split(',')[0].Trim().Trim('"', '\'');
                var span = new FixedSpan
                {
                    Left = x,
                    Top = top,
                    FontSize = fsEm * StlEmPt,
                    LetterSpacing = lsEm * StlEmPt,
                    WordSpacing = wsEm * fsEm * StlEmPt,
                    Text = text,
                    Face = ResolveFixedFace(fam),
                    Own = fx.ownFaces.TryGetValue(famKey, out var ofl) ? ofl : null,
                    AllOwn = fx.ownFaces.Count > 0 ? fx.ownFaces : null,
                    Color = ParseCssColorRgb(col),
                };
                div.Spans.Add(span);
                // The next segment starts where this one's TEXT ends: a span's
                // FINAL space carries no word-spacing. The exporter solved each
                // segment's spacing to place its own glyphs, so charging the
                // trailing space again here would push every following segment
                // out by one word-spacing (a 0.2514em span left a 14 px word gap
                // where 7 px is correct).
                x += MeasureSpanLine(span, text)
                     - (span.WordSpacing != 0 && text.EndsWith(' ') ? span.WordSpacing : 0);
            }
        }
    }

    /// <summary>The page's svg object, else the ruled backdrop the stylesheet draws.</summary>
    private static void ReadStlFixedDivObject(string seg, FixedPageDiv div)
    {
        var objM = Regex.Match(seg, @"<object\s[^>]*data=""(?<u>[^""]*)""");
        if (objM.Success)
        {
            div.HasObjectGraphic = true;
            div.ObjectUrl = DecodeEntities(objM.Groups["u"].Value);
            // A page SVG referenced as a SIDECAR contributes its BOX to the
            // sheet width, not its drawn ink: a page whose
            // only vector is a header rule ending mid-page widens all the way to
            // the page box. (On a rule ending at 546 pt of a 612 pt
            // box the correct sheet is 798 pt, which is the box;
            // 736.91 pt, which is the ink, renders 127 px too
            // narrow.) Only the INLINE dialect below, where the
            // markup we emit IS the page's whole vector art, measures its ink.
        }
        else
        {
            // The self-contained dialect carries the page SVG as INLINE
            // markup instead of an <object> sidecar reference; the markup
            // itself is the replay source (its rasters are data: URIs).
            var inlineSvgM = Regex.Match(seg, @"<svg\b[\s\S]*?</svg\s*>");
            if (inlineSvgM.Success)
            {
                div.HasObjectGraphic = true;
                div.InlineSvgText = inlineSvgM.Value;
                try
                {
                    var frac = TrySvgInkRightFraction(div.InlineSvgText);
                    if (frac is { } fr) div.ObjectInkRight = Math.Min(1.0, Math.Max(0, fr)) * div.SrcW;
                }
                catch { /* unscannable SVG: box fallback */ }
            }
        }
    }
}
