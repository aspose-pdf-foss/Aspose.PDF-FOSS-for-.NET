

namespace Aspose.Pdf.Text;

public partial class TextFragment
{
// Styled cascade helpers: font family, line segments, widths, descent, the styled split and the emitter.
    private static string Family(string? n)
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

    private static System.Collections.Generic.List<TextSegment> LineSegs(TextFragment f)
    {
        var list = new System.Collections.Generic.List<TextSegment>();
        foreach (var seg in f.Segments.Where(seg => seg.Position is not null && !string.IsNullOrEmpty(seg.Text)))
            list.Add(seg);
        list.Sort((a, b) => a.Position!.XIndent.CompareTo(b.Position!.XIndent));
        return list;
    }

    private static double W(Aspose.Pdf.Text.Font? f, double fs, string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (f is not null) { try { return f.MeasureString(s, fs); } catch { /* a face that cannot measure the text leaves the estimate in place */ } }
        return s.Length * fs * 0.5;
    }

    private static double DescentOf(Aspose.Pdf.Text.Font? f, double fs)
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
        catch { /* a font that cannot be found or read leaves the fallback in place */ }
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
            if (q < 0) { if (p < s.Length) { sc.runs.Add((s[p..], f, fs, fg)); } break; }
            if (q > p) sc.runs.Add((s[p..q], f, fs, fg));
            sc.runs.Add((sc.newText, sc.newFont, sc.newFs, sc.newFg));
            sc.newRuns.Add(sc.runs.Count - 1);
            p = q + sc.oldText.Length;
        }
    }

    /// <summary>The fragment's segments after the cascade: one per replacement piece, seated where the piece was
    /// written, so a colour or a decoration set on the fragment afterwards reaches the pieces, not the run the
    /// cascade deleted.</summary>
    private void RebuildSegmentsFromPieces(StyledCascadeState sc)
    {
        var pieces = sc.merged.Where(p => sc.newRuns.Contains(p.r)).ToList();
        if (pieces.Count == 0) return;
        _segments.Clear();
        Rectangle? box = null;
        foreach (var p in pieces)
        {
            var st = sc.runs[p.r];
            var seat = p.tmY - DescentOf(st.font, st.fs);
            var seg = new TextSegment(p.text);
            seg.TextState.FontSize = (float)st.fs;
            seg.TextState.Font = st.font;
            seg.Owner = this;
            seg.Position = new Position(p.x, seat);
            seg.BaselinePosition = new Position(p.x, p.tmY);
            seg.Rectangle = new Rectangle(p.x, seat, p.x + W(st.font, st.fs, p.text), p.tmY + st.fs);
            seg.TextState.OwnerSegment = seg;
            _segments.Add(seg);
            box = box is null ? seg.Rectangle : new Rectangle(System.Math.Min(box.LLX, seg.Rectangle.LLX), System.Math.Min(box.LLY, seg.Rectangle.LLY),
                System.Math.Max(box.URX, seg.Rectangle.URX), System.Math.Max(box.URY, seg.Rectangle.URY));
        }
        _position = _segments[1].Position;
        _rectangle = box;
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
