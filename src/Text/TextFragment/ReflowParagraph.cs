
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>The /Descent (1/1000 units, negative) of the first page font
    /// resource matching this fragment's family — the same value the absorber
    /// used when it anchored the original segments' positions. Zero when no
    /// matching resource carries one.</summary>
    private double SourceDescentUnits(Page page)
    {
        var reader = page.Reader;
        var fonts = Aspose.Pdf.Text.TextAbsorber.ResolveFonts(page.Dict, reader);
        var family = TextState.FontName ?? "";
        // A page can carry several same-family faces with different descents
        // (a subset "…+ArialMT" title next to the paragraph's "Arial"): the
        // fragment's own face — exact family equality — wins over a substring
        // relative; the loose match is only the no-exact-hit fallback.
        double loose = 0;
        foreach (var (_, fd) in fonts)
        {
            var bf = fd.GetName("BaseFont") ?? "";
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus + 1 < bf.Length) bf = bf[(plus + 1)..];
            var bfFamily = bf.Split('-')[0].Split(',')[0];
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(bfFamily)) continue;
            var exact = string.Equals(bfFamily, family, StringComparison.OrdinalIgnoreCase);
            if (!exact && !bfFamily.Contains(family, StringComparison.OrdinalIgnoreCase)
                && !family.Contains(bfFamily, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var fm = FontMetrics.FromFontDict(fd, reader);
                if (fm is not null && fm.Descent != 0)
                {
                    if (exact) return fm.Descent;
                    if (loose == 0) loose = fm.Descent;
                }
            }
            catch { }
        }
        return loose;
    }

    /// <summary>Measure text with the SOURCE font resources' own width tables
    /// (the tables re-absorption measures with), instead of a host-face
    /// approximation. Returns null when the page has no font of this family;
    /// the returned function yields a negative value for unencodable text.</summary>
    private Func<string, double, double>? BuildSourceMeasurer(Page page)
    {
        var reader = page.Reader;
        var fonts = Aspose.Pdf.Text.TextAbsorber.ResolveFonts(page.Dict, reader);
        var family = TextState.FontName ?? "";
        var cands = new List<(FontMetrics M, bool Cid, Dictionary<char, int>? Rev)>();
        foreach (var (_, fd) in fonts)
        {
            var bf = fd.GetName("BaseFont") ?? "";
            var plus = bf.IndexOf('+');
            if (plus >= 0 && plus + 1 < bf.Length) bf = bf[(plus + 1)..];
            var bfFamily = bf.Split('-')[0].Split(',')[0];
            if (string.IsNullOrEmpty(family) || string.IsNullOrEmpty(bfFamily)
                || (!bfFamily.Contains(family, StringComparison.OrdinalIgnoreCase)
                    && !family.Contains(bfFamily, StringComparison.OrdinalIgnoreCase)))
                continue;
            FontMetrics? fm;
            try { fm = FontMetrics.FromFontDict(fd, reader); } catch { continue; }
            if (fm is null) continue;
            var cid = fd.GetName("Subtype") == "Type0";
            Dictionary<char, int>? rev = null;
            if (cid)
            {
                var tu = Aspose.Pdf.Text.TextAbsorber.ParseToUnicodeFromDict(fd, reader);
                if (tu is null) continue;
                rev = new Dictionary<char, int>();
                foreach (var (code, str) in tu)
                    if (str.Length == 1 && !rev.ContainsKey(str[0])) rev[str[0]] = code;
            }
            cands.Add((fm, cid, rev));
        }
        if (cands.Count == 0) return null;
        return (s, sz) =>
        {
            foreach (var (fm, cid, rev) in cands)
            {
                double w = 0;
                var ok = true;
                foreach (var c in s)
                {
                    var code = cid ? (rev!.TryGetValue(c, out var cc) ? cc : -1) : c;
                    if (code < 0) { ok = false; break; }
                    var gw = fm.GetWidth(code);
                    if (gw <= 0 && c != ' ') { ok = false; break; }
                    w += gw;
                }
                if (ok) return w * sz / 1000.0;
            }
            return -1;
        };
    }

    private bool TryReflowParagraph(Page page, string oldText, string newText)
    {
        if (_position is not { } myPos || TextState.Font is not { } myFont || TextState.FontSize <= 0)
            return false;
        var rp = new ReflowParagraphState();
        rp.fs = TextState.FontSize;

        if (!LocateMatchLine(page, rp, myPos, oldText)) return false;
        GrowParagraph(rp);
        if (!ReplacePerLine(page, rp, oldText, newText)) return false;
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine($"[reflow-para] lines0={rp.lines0.Count} bands={rp.bands.Count} bandPara={rp.bandPara.Count} bandRight={rp.bandColumnRight:F2} paraPitch={rp.paraPitch:F2} fs={rp.fs:F2} paraLines={rp.paraLines.Count} pageCol={rp.pageCol:F2} lonely={rp.lonelyOverflow} myLLX={rp.myLLX:F2} matchBase={LineBaseline(rp.paraLines[rp.myCol - rp.lo]):F2}");
        if (!rp.wholePara && !rp.ignorePara && !rp.lonelyOverflow
            && CascadeFromMatch(page, rp.paraLines, rp.myCol - rp.lo, rp.myLLX, oldText, newText,
                rp.pageCol, rp.bandPara) is { } cascadeBottom)
        {
            ExpandClipsToReflowBottom(page, rp.paraLines, cascadeBottom);
            return true;
        }

        if (!SolveWrapWidth(page, rp, myPos)) return false;

        if (!WrapInDominantFont(rp, myFont)) return false;

        WriteReflowedLines(page, rp, newText);
        return true;
    }
}
