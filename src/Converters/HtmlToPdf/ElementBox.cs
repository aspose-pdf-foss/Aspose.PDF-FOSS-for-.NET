using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>An element's own box: its margin per side (the default given where the author states none)
/// and its padding, in points. The body's content opens inside the page margins by margin plus padding.</summary>
internal sealed class ElementBox
{
    /// <summary>Top, right, bottom, left margins in points.</summary>
    public readonly double[] Margin;
    /// <summary>Top, right, bottom, left paddings in points.</summary>
    public readonly double[] Padding = new double[4];
    /// <summary>The author stated a margin or a padding on the body (a sheet rule, a universal reset or the tag's style).</summary>
    public bool Authored;

    public ElementBox(double uaMarginPt) { Margin = new[] { uaMarginPt, uaMarginPt, uaMarginPt, uaMarginPt }; }

    public double Top => Margin[0] + Padding[0];
    public double Right => Margin[1] + Padding[1];
    public double Left => Margin[3] + Padding[3];
    public double MarginTop => Margin[0];
    public double PaddingTop => Padding[0];
}

internal static partial class HtmlToPdfConverter
{
    /// <summary>A margin or padding declaration's name and value.</summary>
    private static readonly Regex BodyBoxDeclRx = new(@"(?<![-\w])((?:margin|padding)(?:-(?:top|right|bottom|left))?)\s*:\s*([^;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>Resolves the body's box from the universal reset, the sheet's body rule and the body tag's own
    /// style, in cascade order; em lengths resolve against the body's font size. A value the box cannot
    /// take (auto, a percentage) leaves its declaration unread.</summary>
    private static ElementBox ResolveBodyBox(ConvertState cv)
    {
        var box = new ElementBox(UaBodyMarginPt);
        var bodyStyle = BodyTagStyleAttr(cv.html) ?? "";
        var emPt = DefaultBodyFontPt;
        var fontSize = SafeRegex.Match(bodyStyle, @"(?<![-\w])font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is { Success: true } inlineFs
            ? inlineFs.Groups[1].Value
            : cv.css.TryGetValue("body", out var bodyRule) && bodyRule.TryGetValue("font-size", out var sheetFs) ? sheetFs : null;
        if (fontSize is not null && (TryParseLength(fontSize.Trim()) ?? BodyPercentFontPt(fontSize.Trim())) is { } fsPt && fsPt > 0)
            emPt = fsPt;
        // (a body sized through the font shorthand resolves its em margins against that size)
        else if (fontSize is null && cv.css.TryGetValue("body", out var bodyShorthandRule)
            && bodyShorthandRule.TryGetValue("font", out var bodyShorthand)
            && FontShorthandParts(bodyShorthand) is { SizePt: > 0 } bodyShorthandParts)
            emPt = bodyShorthandParts.SizePt;
        foreach (var selector in new[] { "*", "body" })
            if (cv.css.TryGetValue(selector, out var rule))
                foreach (var decl in rule)
                    ApplyElementBoxDecl(box, decl.Key, decl.Value, emPt);
        foreach (Match m in SafeRegex.Matches(BodyBoxDeclRx, bodyStyle))
            ApplyElementBoxDecl(box, m.Groups[1].Value, m.Groups[2].Value, emPt);
        return box;
    }

    private static void ApplyElementBoxDecl(ElementBox box, string prop, string value, double emPt)
    {
        var name = prop.Trim().ToLowerInvariant();
        var sides = name.StartsWith("margin", StringComparison.Ordinal) ? box.Margin
            : name.StartsWith("padding", StringComparison.Ordinal) ? box.Padding : null;
        if (sides is null) return;
        var parts = value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var lens = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (ElementBoxLengthPt(parts[i], emPt) is not { } len) return;
            lens[i] = len;
        }
        var dash = name.IndexOf('-');
        if (dash > 0)
        {
            var side = Array.IndexOf(new[] { "top", "right", "bottom", "left" }, name[(dash + 1)..]);
            if (side < 0 || lens.Length != 1) return;
            sides[side] = lens[0];
        }
        else
        {
            switch (lens.Length)
            {
                case 1: sides[0] = sides[1] = sides[2] = sides[3] = lens[0]; break;
                case 2: sides[0] = sides[2] = lens[0]; sides[1] = sides[3] = lens[1]; break;
                case 3: sides[0] = lens[0]; sides[1] = sides[3] = lens[1]; sides[2] = lens[2]; break;
                case 4: sides[0] = lens[0]; sides[1] = lens[1]; sides[2] = lens[2]; sides[3] = lens[3]; break;
                default: return;
            }
        }
        box.Authored = true;
    }

    /// <summary>A length the body box takes, in points: zero, or a px/pt/em/rem/in/cm/mm length, a negative one
    /// clamped to zero; null for anything else (auto, a percentage, calc).</summary>
    private static double? ElementBoxLengthPt(string value, double emPt)
    {
        if (IsZeroLength(value)) return 0.0;
        var m = SafeRegex.Match(value, @"^(-?(?:\d+(?:\.\d+)?|\.\d+))\s*(px|pt|em|rem|in|cm|mm)$", RegexOptions.IgnoreCase);
        if (!m.Success) return SafeRegex.IsMatch(value, @"^-?(?:\d+(?:\.\d+)?|\.\d+)$") ? Math.Max(0, ChainLenPt(value, emPt)) : null;
        return Math.Max(0, m.Groups[2].Value.Equals("rem", StringComparison.OrdinalIgnoreCase)
            ? double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * DefaultBodyFontPt
            : ChainLenPt(value, emPt));
    }

    /// <summary>Whether the body's authored box governs the content inset: the author stated a margin or padding,
    /// and it is neither the all-zero margin the flows already model as the bare page margins nor the chrome of
    /// a dialect that lays its body box out itself (the fieldset worksheet; the custom-font report, whose
    /// measured render keeps the UA inset inside its explicit margins over its own zero margin).</summary>
    private static bool AuthoredBodyBox(ConvertState cv)
        => cv.bodyBox.Authored && !cv.profile.bodyZeroMargin && !cv.fieldsetDoc && !cv.customFontFaceDoc;

    /// <summary>The body's top inset under the page's top margin: none for a zero-margin body (the legacy
    /// sides-only form keeps the UA inset at the top), the authored margin plus padding, else the UA 6 pt.</summary>
    private static double BodyTopInsetPt(ConvertState cv)
    {
        if (cv.profile.bodyZeroMargin && !cv.profile.bodySideOnlyZero) return 0.0;
        return AuthoredBodyBox(cv) ? cv.bodyBox.Top : UaBodyMarginPt;
    }

    /// <summary>The body margin a first block's top margin collapses into at the top of the document: the UA
    /// 6 pt the flows model, or the authored margin-top - none when the body pads its top, which keeps the
    /// block's margin apart from the body's.</summary>
    private static double BodyTopCollapsePt(ConvertState cv)
    {
        if (!AuthoredBodyBox(cv)) return UaBodyMarginPt;
        return cv.bodyBox.PaddingTop > 0 ? 0.0 : cv.bodyBox.MarginTop;
    }
}
