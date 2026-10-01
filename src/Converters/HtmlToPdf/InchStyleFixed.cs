using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The inline-style INCH fixed layout: a report generator's export where every source page is one
// `position: relative` container sized in inches, holding a few hundred `position: absolute` divs
// also sized in inches, each wrapping its text in a bare <span>. No stylesheet, no classes, no
// tables - all the geometry is in the tags' own style attributes. It is the inch-unit sibling of
// the pdf-page dialect (ReadInlineStyleFixedDivs), which spells the same shape in points.
internal static partial class HtmlToPdfConverter
{
    /// <summary>Points per CSS inch.</summary>
    private const double InchPt = 72.0;

    /// <summary>The padding this export puts on every positioned box, in points (2 CSS px).</summary>
    private const double InchBoxPaddingPt = 1.5;

    /// <summary>The face an export that declares no font-family draws in: the UA default serif at
    /// the initial size, which is what the reference resolves it to.</summary>
    private const double InchDefaultFontPt = 12.0;

    /// <summary>A page container of this dialect: sized in inches on both axes, positioned
    /// relative, and holding the absolute boxes.</summary>
    private static readonly Regex InchPageContainerRx = new(
        @"<div\s+style=""(?=[^""]*position:\s*relative)(?=[^""]*height:\s*[\d.]+in)(?=[^""]*width:\s*[\d.]+in)(?<st>[^""]*)""[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>One absolutely positioned text box: its inch geometry and whatever it holds. The
    /// export wraps most values in a bare span and writes the rest as the box's own text, so the
    /// content is taken as-is and its tags stripped rather than a span being required - a box
    /// holding only text is the same box.</summary>
    private static readonly Regex InchTextBoxRx = new(
        @"<div\s+style=""(?=[^""]*position:\s*absolute)(?<st>[^""]*)""[^>]*>(?<body>(?:(?!</?div\b).)*?)</div>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>A length declared in inches, in points; null when the property is absent or in
    /// any other unit.</summary>
    private static double? StyleInchPt(string style, string prop)
    {
        var m = Regex.Match(style, @"(?<![-\w])" + prop + @"\s*:\s*(-?[\d.]+)in", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v * InchPt : null;
    }

    /// <summary>Reads the inch-unit fixed layout: one FixedPageDiv per page container, each
    /// carrying the text of its absolute boxes at their own seats. False when the document is
    /// not this dialect.</summary>
    private static bool ReadInchStyleFixedDivs(string html, List<FixedPageDiv> divs)
    {
        var pages = InchPageContainerRx.Matches(html);
        if (pages.Count == 0) return false;
        for (var p = 0; p < pages.Count; p++)
        {
            var seg = html[pages[p].Index..(p + 1 < pages.Count ? pages[p + 1].Index : html.Length)];
            var st = pages[p].Groups["st"].Value;
            var div = new FixedPageDiv
            {
                SrcW = StyleInchPt(st, "width") ?? 0,
                SrcH = StyleInchPt(st, "height") ?? 0,
            };
            if (div.SrcW <= 0 || div.SrcH <= 0) return false;
            foreach (Match m in InchTextBoxRx.Matches(seg))
            {
                var bst = m.Groups["st"].Value;
                var text = DecodeEntities(Regex.Replace(m.Groups["body"].Value, "<[^>]+>", ""));
                if (text.Trim().Length == 0) continue;
                if (StyleInchPt(bst, "left") is not { } left || StyleInchPt(bst, "top") is not { } top) continue;
                // The box's own padding insets its text on both axes; the export writes the same
                // 2px on every box, so it is read from the box rather than assumed.
                var pad = StyleBoxPaddingPt(bst);
                div.Spans.Add(new FixedSpan
                {
                    Left = left + pad,
                    Top = top + pad,
                    FontSize = InchDefaultFontPt,
                    Text = text,
                    Face = ResolveFixedFace("serif"),
                    Color = (0, 0, 0),
                });
            }
            divs.Add(div);
        }
        return divs.Count > 0 && InchHasSpans(divs);
    }

    /// <summary>A box's uniform padding in points: the px shorthand this export writes, or the
    /// dialect's own 2px when it is spelled some other way.</summary>
    private static double StyleBoxPaddingPt(string style)
    {
        var m = Regex.Match(style, @"(?<![-\w])padding\s*:\s*([\d.]+)px", RegexOptions.IgnoreCase);
        return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v * 0.75 : InchBoxPaddingPt;
    }

    /// <summary>How far short of its ADVANCE a run's last glyph stops inking, in points.</summary>
    /// <remarks>
    /// A sheet of this dialect ends one page margin past the last INKED column, not past the last
    /// text advance, and a glyph's ink stops short of its advance by its right side bearing.
    /// Measured on the widest run of the settlement report: 20 digits of Times New Roman 12 advance
    /// 120.00 pt and ink 119.55, and the reference's sheet is the 119.55 one. The two coincide for
    /// a glyph that fills its box, which is why most runs need no correction at all.
    /// </remarks>
    private static double LastGlyphRightBearingPt(FixedSpan s, string text)
    {
        var t = text.TrimEnd();
        if (t.Length == 0) return 0;
        int cp = t[^1];
        if (char.IsLowSurrogate(t[^1]) && t.Length > 1 && char.IsHighSurrogate(t[^2]))
            cp = char.ConvertToUtf32(t[^2], t[^1]);
        var (own, sys) = s.FaceFor(cp);
        var parser = own?.Parser ?? PosFace(sys).parser;
        if (parser is null || !parser.CMap.TryGetValue(cp, out var gid) || gid == 0) return 0;
        var upm = parser.UnitsPerEm > 0 ? parser.UnitsPerEm : 1000;
        if (parser.GetOutline(gid) is not { } outline) return 0;
        var advance = Math.Round(parser.GetAdvanceWidth(gid) * 1000.0 / upm) * s.FontSize / 1000.0;
        var inkRight = outline.XMax * s.FontSize / upm;
        // a glyph whose ink runs past its advance (an italic overhang) never SHRINKS the sheet
        return Math.Max(0, advance - inkRight);
    }

    private static bool InchHasSpans(List<FixedPageDiv> divs)
    {
        foreach (var d in divs)
            if (d.Spans.Count > 0) return true;
        return false;
    }
}
