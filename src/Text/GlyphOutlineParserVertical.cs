using Aspose.Pdf.Text.OpenType;

namespace Aspose.Pdf.Text;

internal sealed partial class GlyphOutlineParser
{
    /// <summary>The scripts under which a face lists its vertical-writing substitutions.</summary>
    private static readonly string[] VerticalScripts = ["kana", "hani", "DFLT", "latn"];

    private const string VerticalFeature = "vert";

    private Dictionary<int, int>? _verticalForms;
    private List<int>? _verticalLookups;
    private GsubEngine? _verticalEngine;
    private readonly object _verticalLock = new();

    /// <summary>The glyph the face draws in place of <paramref name="glyphId"/> in vertical
    /// writing - a parenthesis or bracket lying across the column, a long vowel mark standing
    /// up - through the face's own "vert" substitution; the glyph itself when the face has
    /// none for it. Looked up once per glyph.</summary>
    public int VerticalForm(int glyphId)
    {
        lock (_verticalLock)
        {
            _verticalForms ??= new Dictionary<int, int>();
            if (_verticalForms.TryGetValue(glyphId, out var known)) return known;
            var form = glyphId;
            try
            {
                if (_verticalLookups is null) OpenVerticalLookups();
                if (_verticalEngine is not null && glyphId is > 0 and <= ushort.MaxValue)
                {
                    var buffer = new List<ShapedGlyph> { new((ushort)glyphId, 0) };
                    foreach (var lookup in _verticalLookups!) _verticalEngine.ApplyLookup(buffer, lookup, uint.MaxValue);
                    if (buffer.Count == 1 && buffer[0].Glyph < _numGlyphs) form = buffer[0].Glyph;
                }
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidOperationException)
            {
                form = glyphId;
            }
            return _verticalForms[glyphId] = form;
        }
    }

    private void OpenVerticalLookups()
    {
        _verticalLookups = new List<int>();
        if (!_tables.ContainsKey("GSUB") || OtfLayout.Open(_data) is not { } layout) return;
        foreach (var script in VerticalScripts)
        {
            _verticalLookups = layout.LookupsForFeature("GSUB", script, VerticalFeature);
            if (_verticalLookups.Count > 0) break;
        }
        if (_verticalLookups.Count > 0) _verticalEngine = new GsubEngine(layout);
    }
}
