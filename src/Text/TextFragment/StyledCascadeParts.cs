

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// Styled cascade helpers: font family, line segments, widths, descent, the styled split and the emitter.
    private static string Family(StyledCascadeState sc, string? n)
    {
        if (string.IsNullOrEmpty(n)) return string.Empty;
        var s = n;
        int plus = s.IndexOf('+');
        if (plus >= 0 && plus + 1 < s.Length) s = s[(plus + 1)..];
        int comma = s.IndexOf(',');
        if (comma > 0) s = s[..comma];
        int dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash];
        return s.Replace(" ", string.Empty);
    }

    private static System.Collections.Generic.List<TextSegment> LineSegs(StyledCascadeState sc, TextFragment f)
    {
        var list = new System.Collections.Generic.List<TextSegment>();
        foreach (var seg in f.Segments)
            if (seg.Position is not null && !string.IsNullOrEmpty(seg.Text)) list.Add(seg);
        list.Sort((a, b) => a.Position!.XIndent.CompareTo(b.Position!.XIndent));
        return list;
    }

    private static double W(StyledCascadeState sc, Aspose.Pdf.Text.Font? f, double fs, string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (f is not null) { try { return f.MeasureString(s, fs); } catch { } }
        return s.Length * fs * 0.5;
    }

    private static double DescentOf(StyledCascadeState sc, Aspose.Pdf.Text.Font? f, double fs)
    {
        double d = 0;
        try
        {
            if (f?.SourceFontData?.TtfData is { } ttf)
            {
                // hhea descender — the SAME value the Type0 embed's descriptor
                // carries, so emitted targets round-trip through the absorber.
                d = TextBuilder.HheaDescentPerMille(ttf);
                if (d == 0) (_, d, _, _) = FontRepository.ReadTtfMetrics(ttf);
            }
            if (d == 0) d = f?.GetMetrics()?.Descent ?? 0;
        }
        catch { }
        return d != 0 ? System.Math.Abs(d) / 1000.0 * fs : 0;
    }

    private static void AddStyledSplit(StyledCascadeState sc, string s, TextSegment src)
    {
        var f = src.TextState.Font ?? sc.headFont;
        double fs = src.TextState.FontSize > 0 ? src.TextState.FontSize : sc.headFs;
        var fg = src.TextState.ForegroundColor;
        int p = 0;
        while (true)
        {
            int q = s.IndexOf(sc.oldText, p, System.StringComparison.Ordinal);
            if (q < 0) { if (p < s.Length) sc.runs.Add((s[p..], f, fs, fg)); break; }
            if (q > p) sc.runs.Add((s[p..q], f, fs, fg));
            sc.runs.Add((sc.newText, sc.newFont, sc.newFs, sc.newFg));
            p = q + sc.oldText.Length;
        }
    }

    private static void Emit(StyledCascadeState sc, string text, Aspose.Pdf.Text.Font? f, double fs, Color? fg, double px, double py)
    {
        if (string.IsNullOrEmpty(text) || f is null) return;
        var frag = new TextFragment(text);
        frag.TextState.Font = f;
        frag.TextState.FontSize = (float)fs;
        if (fg is not null) frag.TextState.ForegroundColor = fg;
        frag.Position = new Position(px, py);
        sc.tb.AppendText(frag);
    }
}
