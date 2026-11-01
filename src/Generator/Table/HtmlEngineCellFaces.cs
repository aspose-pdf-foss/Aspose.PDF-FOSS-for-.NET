using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The face an open element of an HTML-engine cell imposes on the text inside
    /// it: a family and a size from its style (0 = inherit), a weight and a slant, and for
    /// a block the UA margin it opens under.</summary>
    private sealed class EngineFaceStyle
    {
        public string? Family;
        public double Size;
        public bool Bold;
        public bool Italic;
        public bool Block;
        public double MarginTop;
    }

    /// <summary>UA heading sizes and margins in em, index 1..6 (probed: h6 sets at 0.75 em).</summary>
    private static readonly double[] EngineHeadingSizeEm = { 0, 2.0, 1.5, 1.17, 1.0, 0.83, 0.75 };
    private static readonly double[] EngineHeadingMarginEm = { 0, 0.67, 0.75, 0.83, 1.12, 1.5, 1.67 };

    /// <summary>The style an element of the engine cell opens: its tag's UA weight/size (a
    /// heading is bold at its em size and opens under its UA margin) and its style
    /// attribute's font-family, font-size, font-weight and font-style.</summary>
    private static EngineFaceStyle ParseEngineFaceStyle(string tag, string tagName, double baseSize)
    {
        var st = new EngineFaceStyle();
        if (tagName is "em" or "i") st.Italic = true;
        if (tagName.Length == 2 && tagName[0] == 'h' && char.IsDigit(tagName[1]))
        {
            var level = tagName[1] - '0';
            st.Block = true;
            st.Bold = true;
            st.Size = baseSize * EngineHeadingSizeEm[level];
            st.MarginTop = EngineHeadingMarginEm[level] * st.Size;
        }
        else if (tagName is "p" or "div") st.Block = true;
        var css = EngineStyleValue(tag);
        if (css is null) return st;
        var fm = SafeRegex.Match(css, @"(?<![-\w])font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            var first = fm.Groups[1].Value.Split(',')[0].Trim().Trim('\'', '"');
            if (first.Length > 0) st.Family = first;
        }
        var zm = SafeRegex.Match(css, @"(?<![-\w])font-size\s*:\s*([\d.]+)\s*(px|pt)?", RegexOptions.IgnoreCase);
        if (zm.Success && double.TryParse(zm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var fz) && fz > 0)
        {
            st.Size = zm.Groups[2].Value.Equals("px", StringComparison.OrdinalIgnoreCase) ? fz * UaFace.CssPxToPt : fz;
            if (st.MarginTop > 0) st.MarginTop = EngineHeadingMarginEm[tagName[1] - '0'] * st.Size;
        }
        if (SafeRegex.IsMatch(css, @"font-weight\s*:\s*(bold|[6-9]00)", RegexOptions.IgnoreCase)) st.Bold = true;
        if (SafeRegex.IsMatch(css, @"font-style\s*:\s*(italic|oblique)", RegexOptions.IgnoreCase)) st.Italic = true;
        return st;
    }

    /// <summary>The face the open elements resolve for the text in progress: the innermost
    /// declared family (none = the UA serif) at the accumulated weight and slant. Null
    /// keeps the engine's serif TrueType pair (regular / bold).</summary>
    /// <summary>The CSS of a tag's style attribute: a quoted value, or an UNQUOTED one running to
    /// the next whitespace (probed: a backslash-escaped fixture's <c>style=\"color:#b10026;
    /// font-family:georgia;font-size:24px;\"</c> is one value whose first declaration carries the
    /// stray quote in its property name and is dropped, so the span sets Georgia 18 in black;
    /// a value with a space ends at the space and styles nothing). Null without a style.</summary>
    private static string? EngineStyleValue(string tag)
    {
        var m = SafeRegex.Match(tag, @"\bstyle\s*=\s*(?:'(?<v>[^']*)'|""(?<v>[^""]*)""|(?<v>[^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var kept = new List<string>();
        foreach (var decl in m.Groups["v"].Value.Split(';').Where(decl => SafeRegex.IsMatch(decl, @"^\s*[A-Za-z-]+\s*:")))
            kept.Add(decl);
        return string.Join(";", kept);
    }

    private static UaFace? ResolveEngineFace(HtmlEngineCellState hc, out bool bold, out bool italic)
    {
        string? family = null;
        bold = hc.boldDepth > 0;
        italic = false;
        foreach (var fs in hc.faceStack)
        {
            if (fs.Family is { Length: > 0 }) family = fs.Family;
            bold |= fs.Bold;
            italic |= fs.Italic;
        }
        if (family is null && hc.rootFont is { } rootFont) return ResolveEngineRootFace(hc, rootFont, bold, italic);
        if (family is null && !italic) return null;
        var key = (family ?? "").ToLowerInvariant() + (bold ? "|b" : "") + (italic ? "|i" : "");
        if (hc.faceCache.TryGetValue(key, out var cached)) return cached;
        var fam = family ?? UaSerifFamilyName;
        var face = UaFace.TryLoad(fam, bold, italic, EngineFaceResourcePrefix + (++hc.faceIndex))
                   ?? UaFace.TryLoad(fam, false, false, EngineFaceResourcePrefix + (++hc.faceIndex));
        hc.faceCache[key] = face;
        return face;
    }

    /// <summary>The face the fragment's text state names, at the weight and slant the open
    /// elements ask for: its family's styled member when one is installed, else the face
    /// itself (its own program, or the installed face standing in for a standard font).
    /// Null keeps the engine's serif pair.</summary>
    private static UaFace? ResolveEngineRootFace(HtmlEngineCellState hc, Font rootFont, bool bold, bool italic)
    {
        var key = EngineRootFaceKey + (bold ? "|b" : "") + (italic ? "|i" : "");
        if (hc.faceCache.TryGetValue(key, out var cached)) return cached;
        var family = SystemFontResolver.SubstituteFaceName(rootFont.BaseFont ?? rootFont.FontName).Family;
        var face = (bold || italic ? UaFace.TryLoad(family, bold, italic, EngineFaceResourcePrefix + (++hc.faceIndex)) : null)
                   ?? UaFace.FromFont(rootFont, EngineFaceResourcePrefix + (++hc.faceIndex))
                   ?? UaFace.TryLoad(family, false, false, EngineFaceResourcePrefix + (++hc.faceIndex));
        hc.faceCache[key] = face;
        return face;
    }

    /// <summary>The face an HTML fragment's own text state names for its cell text, as a CSS
    /// family: a font the caller set whose name is a family (a face named with a weight or a
    /// slant, "Arial Bold", is none, and the serif the engine sets a family-less run in stays,
    /// as it does for a state naming that serif itself). Null keeps the UA serif.</summary>
    private static Font? EngineRootFont(HtmlFragment html)
    {
        if (html.TextState?.ExplicitFont is not { } font) return null;
        var (family, bold, italic) = SystemFontResolver.SubstituteFaceName(font.BaseFont ?? font.FontName);
        if (bold || italic || string.Equals(family, UaSerifFamilyName, StringComparison.OrdinalIgnoreCase)) return null;
        return font;
    }

    /// <summary>Face-cache key of the text state's own face (no CSS family spells it).</summary>
    private const string EngineRootFaceKey = "|root";

    /// <summary>The size the open elements give the text in progress: the innermost declared
    /// size, else the small size inside <c>&lt;small&gt;</c>, else the base size.</summary>
    private static double ResolveEngineSize(HtmlEngineCellState hc)
    {
        var size = hc.smallDepth > 0 ? hc.smallSize : hc.baseSize;
        foreach (var fsSize in hc.faceStack.Select(fs => fs.Size).Where(fsSize => fsSize > 0)) size = fsSize;
        return size;
    }

    /// <summary>The UA serif family the engine's default pair stands for.</summary>
    private const string UaSerifFamilyName = "Times New Roman";
    /// <summary>Resource-name prefix of the faces an engine cell embeds beyond its serif pair.</summary>
    private const string EngineFaceResourcePrefix = "FUaCell";

    /// <summary>A run's line box: its own face's CSS box at its size, else the serif box.</summary>
    /// <summary>The strut a line starts from: the innermost open block's own size box when
    /// the block set a size, else the cell's root box.</summary>
    private static (double Box, double Drop) EngineBlockStrut(HtmlEngineCellState hc)
    {
        for (var i = hc.faceStack.Count - 1; i >= 0; i--)
            if (hc.faceStack[i].Block && hc.faceStack[i].Size > 0) return SerifLineBox(hc.faceStack[i].Size);
        return (hc.rootBox, hc.baseDrop);
    }

    private static (double Box, double Drop) EngineRunLineBox(HtmlRun run) =>
        run.Face is { } f ? (f.LineHeight(run.Size), f.Above(run.Size)) : SerifLineBox(run.Size);

    private static void PopEngineFace(HtmlEngineCellState hc)
    {
        if (hc.faceStack.Count > 0) hc.faceStack.RemoveAt(hc.faceStack.Count - 1);
    }

    /// <summary>The TrueType data a run is measured and drawn with.</summary>
    private static byte[] EngineRunTtf(HtmlRun run) => run.Face?.Ttf ?? (run.Bold ? _serifBoldTtf! : _serifTtf!);

    /// <summary>The name the run's face embeds under.</summary>
    private static string EngineRunFontName(HtmlRun run) => run.Face?.Name ?? (run.Bold ? "Times New Roman Bold" : "Times New Roman");

    /// <summary>A top-level list's UA block margin in em of the base size (above it the engine
    /// opens an empty line box, below it the next line takes this margin).</summary>
    private const double EngineListMarginEm = 1.12;
    /// <summary>The marker of a list nested in another (the UA circle).</summary>
    private const string EngineNestedBullet = "\u25E6";

    /// <summary>A block tag (p / div / heading) is a line boundary on open and close; opening
    /// pushes its style and owes its UA margin to its first line, closing pops it.</summary>
    private static void AbsorbEngineBlockTag(HtmlEngineCellState hc, string tag, string tagName, bool closing)
    {
        FlushLine(hc, force: false);
        if (closing)
        {
            PopEngineFace(hc);
            if (hc.blockDepth > 0) hc.blockDepth--;
            return;
        }
        var bst = ParseEngineFaceStyle(tag, tagName, hc.baseSize);
        hc.faceStack.Add(bst);
        hc.blockDepth++;
        hc.pendingBlockMargin = Math.Max(hc.pendingBlockMargin, bst.MarginTop);
    }

    /// <summary>The engine's wrap tokens: a space ends a token (and stays on it), and so does
    /// a hyphen followed by a letter or digit - the UA breaks a hyphenated word after its
    /// hyphen (probed: "well-known" wraps as "well-" / "known", "0123456789-0123456789" as
    /// "0123456789-" / "0123456789" once the word does not fit).</summary>
    private static IEnumerable<string> SplitEngineTokens(string s)
    {
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var breakHere = s[i] == ' ' || (s[i] == '-' && i + 1 < s.Length && char.IsLetterOrDigit(s[i + 1]) && i > start);
            if (breakHere) { yield return s.Substring(start, i - start + 1); start = i + 1; }
        }
        if (start < s.Length) yield return s.Substring(start);
    }
}
