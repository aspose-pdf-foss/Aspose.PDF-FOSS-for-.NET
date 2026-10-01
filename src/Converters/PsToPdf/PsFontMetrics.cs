using System;
using System;
using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Glyph advances for one face, keyed by glyph name. The library's standard-14
/// tables are indexed by character code, so this adapter maps a name back to the
/// code the standard encoding gives it rather than carrying a second width table.
/// </summary>
internal sealed class PsFontMetrics
{
    private static readonly Dictionary<string, PsFontMetrics> Cache =
        new(StringComparer.Ordinal);

    private static readonly Dictionary<string, int> StandardCodes = BuildCodeIndex();

    private readonly string _baseFont;
    private readonly int _defaultWidth;
    private readonly bool _oneWidth;

    private PsFontMetrics(string baseFont)
    {
        _baseFont = baseFont;
        _defaultWidth = Standard14Fonts.GetDefaultWidth(baseFont);
        _oneWidth = baseFont.StartsWith(FixedPitchFamily, StringComparison.Ordinal);
    }

    /// <summary>The family whose every glyph is the same width.</summary>
    private const string FixedPitchFamily = "Courier";

    /// <summary>The metrics of a standard-14 face.</summary>
    public static PsFontMetrics For(string baseFont)
    {
        var key = baseFont ?? string.Empty;
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var metrics))
            {
                metrics = new PsFontMetrics(key);
                Cache[key] = metrics;
            }

            return metrics;
        }
    }

    /// <summary>The advance of one glyph, in units of one thousandth of an em.</summary>
    public double Width(string glyphName)
    {
        if (glyphName is null || glyphName == PsEncodings.NotDefined) return 0;
        if (StandardCodes.TryGetValue(glyphName, out var code))
        {
            var width = Standard14Fonts.GetWidth(_baseFont, code);
            if (width >= 0) return width;
        }

        // A face of one width answers with that width for a glyph the table cannot be
        // asked for by code; a proportional one has an advance of its own.
        if (_oneWidth) return _defaultWidth;
        return StandardOnlyWidths.TryGetValue(glyphName, out var only) ? only : _defaultWidth;
    }

    /// <summary>The advances the proportional standard faces give the glyphs the code
    /// table cannot be asked for. The table is indexed by the WINDOWS code page, and
    /// these sit only in the standard vector. The fraction bar's advance is measured
    /// from the etalon of the recipe that stacks a numerator over a denominator: the
    /// bar carries the denominator a sixth of an em along, not half of one.</summary>
    private static readonly Dictionary<string, int> StandardOnlyWidths =
        new(StringComparer.Ordinal) { ["fraction"] = 167 };

    /// <summary>Glyph name to the code the width table is indexed by. That table
    /// follows the WINDOWS code page above the plain ASCII range, so a name is looked
    /// for there first and in the standard vector only where the two agree.</summary>
    private static Dictionary<string, int> BuildCodeIndex()
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var code = 0; code < AsciiLimit; code++)
        {
            var name = Type1StandardEncoding.GetName(code);
            if (name != null && !index.ContainsKey(name)) index[name] = code;
        }

        for (var code = AsciiLimit; code < PsEncodings.CodeCount; code++)
        {
            var name = PdfEncodings.WinAnsiName(code);
            if (name != null && !index.ContainsKey(name)) index[name] = code;
        }

        return index;
    }

    /// <summary>The first code above the plain ASCII range, where the standard vector
    /// and the Windows code page part company.</summary>
    private const int AsciiLimit = 0x80;
}
