using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A resolvable font family declared by a rule that is neither the body's own nor scoped to the blocks it styles - the sheet then states no single flow face.</summary>
    private static bool RealFamilyDeclaredOutsideBody(ConvertState cv, out string? bodyClassFace,
        out Dictionary<string, string>? bodyClassRule)
    {
        // The body's OWN class rule is the body rule (`<body class="ev-print">` under `.ev-print
        // { Arial; 9.5pt; line-height: 1.25em }`): its face and size seed the flow, and a rule
        // elsewhere naming the SAME face states nothing new.
        bodyClassRule = Regex.Match(cv.html, @"<body\b[^>]*\bclass\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase) is { Success: true } bodyClsM
            && cv.css.TryGetValue("." + bodyClsM.Groups[1].Value, out var bcr) ? bcr : null;
        bodyClassFace = bodyClassRule is not null && bodyClassRule.TryGetValue("font-family", out var bcFam)
            && FirstFontFamily(bcFam) is { } bcName && !bcName.Contains(',') && WinMetricsFor(bcName) is not null ? bcName : null;
        var realFamilyOutsideBody = false;
        foreach (var kv in cv.css)
            if (!kv.Key.TrimStart().StartsWith('@') && SelectorUsed(cv.html, kv.Key)
                && !(bodyClassFace is not null && kv.Value.TryGetValue("font-family", out var sameFam)
                     && FirstFontFamily(sameFam) is { } sameName && sameName.Equals(bodyClassFace, StringComparison.OrdinalIgnoreCase))
                && !(bodyClassRule is not null && ReferenceEquals(kv.Value, bodyClassRule))
                && !TableScopedSelector(cv.html, cv, cv.bodyAllTables, cv.edgeToEdgePre, kv.Key, kv.Value)
                && !kv.Key.Trim().Equals("body", StringComparison.OrdinalIgnoreCase)
                // The UNIVERSAL rule is the root's own inherited typography - the same
                // level a body rule states - not a family declared outside the flow.
                && !IsRootFamilySelector(kv.Key)
                // a bare element-TAG rule's resolvable face rides its
                // blocks (h6 { font-family: Verdana } styles the h6s, not
                // the flow) — it does not disqualify the UA structure
                && !(Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]+[1-6]?$")
                     && kv.Value.TryGetValue("font-family", out var tagFamDecl)
                     && FirstFontFamily(tagFamDecl) is { } tagFamName
                     && WinMetricsFor(tagFamName) is not null)
                // …and a class-scoped rule rides its classed blocks the
                // same way (see cssRealFamily above).
                && !Regex.IsMatch(kv.Key.Trim(), @"^[a-zA-Z]*[1-6]?\.[\w-]+$")
                && !IsRunScopedFamilySelector(kv.Key)
                && kv.Value.TryGetValue("font-family", out var nbDecl)
                && FirstFontFamily(nbDecl) is { } nbName && !nbName.Contains(',')
                && WinMetricsFor(nbName) is not null)
            { realFamilyOutsideBody = true; break; }
        return realFamilyOutsideBody;
    }
}
