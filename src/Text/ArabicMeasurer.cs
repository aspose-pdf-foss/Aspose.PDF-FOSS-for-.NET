namespace Aspose.Pdf.Text;

/// <summary>
/// Measures the width of Arabic text by shaping it to contextual presentation forms
/// (<see cref="ArabicShaper"/>) and summing the advance widths of the resulting glyphs in
/// an Arabic-capable face. A Latin base font (the layout default) carries no Arabic glyphs,
/// so its simple-font metrics would measure every Arabic codepoint as a missing glyph. The
/// fallback face is Times New Roman, matching the default Aspose.PDF uses for complex scripts.
/// </summary>
internal static class ArabicMeasurer
{
    private const string FallbackFamily = "Times New Roman";

    private static TrueTypeParser? _parser;
    private static bool _resolved;
    private static readonly object _lock = new();

    private static TrueTypeParser? Parser()
    {
        lock (_lock)
        {
            if (_resolved) return _parser;
            _resolved = true;
            try
            {
                var ttf = SystemFontResolver.Resolve(FallbackFamily);
                if (ttf is { Length: > 0 })
                {
                    var p = new TrueTypeParser(ttf);
                    p.Parse();
                    if (p.UnitsPerEm > 0 && p.GlyphWidths.Length > 0)
                        _parser = p;
                }
            }
            catch { _parser = null; }
            return _parser;
        }
    }

    /// <summary>Width of <paramref name="text"/> in points at <paramref name="fontSize"/>,
    /// or 0 when no Arabic-capable face is available (caller then falls back).</summary>
    public static double Measure(string text, double fontSize)
    {
        var parser = Parser();
        if (parser is null) return 0;

        var shaped = ArabicShaper.Shape(text);
        double units = 0;
        foreach (var ch in shaped)
        {
            if (parser.CMap.TryGetValue(ch, out var gid) && gid >= 0 && gid < parser.GlyphWidths.Length)
                units += parser.GlyphWidths[gid];
            else
                units += parser.UnitsPerEm * 0.5; // unmapped: half-em estimate
        }
        return units * fontSize / parser.UnitsPerEm;
    }
}
