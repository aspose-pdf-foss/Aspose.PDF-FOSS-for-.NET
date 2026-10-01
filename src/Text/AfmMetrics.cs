namespace Aspose.Pdf.Text;

/// <summary>
/// An Adobe Font Metrics file (AFM, Adobe technical note 5004) read into its parts: the global
/// keys (FontName, Weight, FontBBox, Ascender and so on) as the text after the key, the character
/// metrics (code, width, glyph name, box), and the kerning pairs by glyph name. Keys the file
/// repeats keep their last value; a character line naming no width has width 0.
/// </summary>
internal sealed class AfmMetrics
{
    /// <summary>One character's metrics: its code in the font's encoding (-1 for none), its
    /// advance width, its glyph name and its bounding box (null when the line gives none).</summary>
    internal sealed record CharMetric(int Code, int Width, string Name, int[]? Box);

    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
    public List<CharMetric> Characters { get; } = new();
    public List<(string First, string Second, int Amount)> KernPairs { get; } = new();

    private const int DefaultWidth = 0;
    private const string Header = "StartFontMetrics";

    /// <summary>The metrics the text holds; null when it does not open as an AFM file.</summary>
    public static AfmMetrics? Parse(byte[] bytes)
    {
        // Each byte is its own character (Latin-1), spelled out: net48 has no Latin1 encoding.
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) chars[i] = (char)bytes[i];
        var text = new string(chars);
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].TrimStart().StartsWith(Header, StringComparison.Ordinal)) return null;
        var metrics = new AfmMetrics();
        var section = "";
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("Comment", StringComparison.Ordinal)) continue;
            var (key, rest) = SplitKey(line);
            switch (key)
            {
                case "StartCharMetrics":
                case "StartKernPairs":
                case "StartKernPairs0":
                    section = key;
                    continue;
                case "EndCharMetrics":
                case "EndKernPairs":
                    section = "";
                    continue;
            }
            if (section == "StartCharMetrics") metrics.Characters.Add(ReadCharacter(line));
            else if (section.StartsWith("StartKernPairs", StringComparison.Ordinal))
            {
                if (key is "KPX" && rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is { Length: >= 3 } pair
                    && int.TryParse(pair[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var amount))
                    metrics.KernPairs.Add((pair[0], pair[1], amount));
            }
            else if (key.Length > 0) metrics.Keys[key] = rest;
        }
        return metrics;
    }

    /// <summary>A global key's integer value; null when absent or not a number.</summary>
    public int? Integer(string key) =>
        Keys.TryGetValue(key, out var value)
        && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? (int)number : null;

    /// <summary>A global key's number; null when absent or not a number.</summary>
    public double? Number(string key) =>
        Keys.TryGetValue(key, out var value)
        && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number : null;

    /// <summary>A global key's text; null when absent.</summary>
    public string? Text(string key) => Keys.TryGetValue(key, out var value) ? value : null;

    private static (string Key, string Value) SplitKey(string line)
    {
        var space = line.IndexOfAny(new[] { ' ', '\t' });
        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..].Trim());
    }

    /// <summary>A character metrics line: "C code ; WX width ; N name ; B llx lly urx ury ;".</summary>
    private static CharMetric ReadCharacter(string line)
    {
        int code = -1, width = DefaultWidth;
        string name = "";
        int[]? box = null;
        foreach (var part in line.Split(';'))
        {
            var fields = part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2) continue;
            switch (fields[0])
            {
                case "C": code = ParseInt(fields[1], -1); break;
                case "CH": code = Convert.ToInt32(fields[1].Trim('<', '>'), 16); break;
                case "WX" or "W0X": width = ParseInt(fields[1], DefaultWidth); break;
                case "N": name = fields[1]; break;
                case "B" when fields.Length >= 5:
                    box = new[] { ParseInt(fields[1], 0), ParseInt(fields[2], 0), ParseInt(fields[3], 0), ParseInt(fields[4], 0) };
                    break;
            }
        }
        return new CharMetric(code, width, name, box);
    }

    private static int ParseInt(string text, int fallback) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? (int)number : fallback;
}
