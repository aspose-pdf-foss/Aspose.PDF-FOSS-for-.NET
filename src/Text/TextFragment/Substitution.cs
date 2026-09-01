
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>True when the caller explicitly opted into
    /// <see cref="TextEditOptions.NoCharacterAction.ReplaceFonts"/> — the generator
    /// then substitutes a glyph-covering face at layout time. Reads the backing
    /// field so the check never instantiates default options.</summary>
    internal bool HasExplicitReplaceFonts =>
        _textEditOptions is { NoCharacterBehaviorExplicit: true,
            NoCharacterBehavior: TextEditOptions.NoCharacterAction.ReplaceFonts };

    /// <summary>The explicit-ReplaceFonts font assignment on an absorbed fragment:
    /// when the caller-assigned face cannot cover the fragment's text, the run
    /// re-dresses through the substitution scan (caller sources → the named Han
    /// face → per-user) — NOT the source family. Measured directly:
    /// with the source family SimHei installed per-user, an assignment of
    /// Times New Roman still reads back as SimSun, while the assignment-free
    /// replace of the same run family-preserves (its corpus test expects SimHei
    /// exactly when FindFont resolves it).</summary>
    internal void RedressAfterExplicitFontAssignment()
    {
        if (SourcePage is null || AttachedSegment is not null || !HasExplicitReplaceFonts) return;
        if (string.IsNullOrEmpty(_text) || _pendingCidRedressFamily is not null) return;
        var assigned = TextState.Font;
        if (assigned is null) return;
        FontData? sub;
        try { sub = FontRepository.SubstituteForMissingGlyphs(_text, assigned); } catch { return; }
        if (sub?.FontName is not { Length: > 0 } family) return;
        var t = _text;
        // Defeat the setter's same-text no-op; the segments still carry the drawn
        // text the page rewrite searches for.
        _text = string.Empty;
        _pendingCidRedressFamily = family;
        try { Text = t; }
        finally { _pendingCidRedressFamily = null; }
    }

    /// <summary>Regular Arial-family name (subset prefixes stripped): the reflow
    /// re-fonts such runs with the system Arial face and its own metrics.</summary>
    private static bool IsArialFamily(string? fontName)
    {
        if (string.IsNullOrEmpty(fontName)) return false;
        var stem = fontName!.ToLowerInvariant().Replace(" ", "");
        var plus = stem.IndexOf('+');
        if (plus >= 0 && plus + 1 < stem.Length) stem = stem[(plus + 1)..];
        return stem.StartsWith("arial", StringComparison.Ordinal)
            && !stem.Contains("bold") && !stem.Contains("italic");
    }

    /// <summary>Delete the pre-reflow paragraph text. Removes each current
    /// segment's line at its own baseline Y (falls back to a page-wide delete of
    /// the joined text when segment positions are unavailable).</summary>
    /// <summary>The Standard-14 face a replacement must be written in when the
    /// source font cannot encode it — a subset carrying only its own glyphs, or a
    /// face whose width table answers every character with the same default. The
    /// serif default stands in for a family the system cannot resolve; a font that
    /// really does carry the glyphs returns null and keeps its own face.</summary>
    private string? ResolveSubstituteFace(FontInfo font, string newText)
    {
        var mapped = TextBuilder.MapToStandard14Public(TextState);
        var rawName = (TextState.FontName ?? "").Replace(" ", "");
        var familyKnown = !mapped.StartsWith("Helvetica", StringComparison.Ordinal)
            || rawName.StartsWith("Arial", StringComparison.OrdinalIgnoreCase)
            || rawName.StartsWith("Helvetica", StringComparison.OrdinalIgnoreCase);
        var probe = newText.Trim();
        if (familyKnown || probe.Length == 0) return null;
        // The trigger is real glyph coverage: one character the source font cannot
        // represent re-dresses the whole run. A width table that answers every
        // character with the same default is the same story told in widths.
        // An Identity-H CID subset addresses glyphs by the subset's own ids: text it
        // never carried has no id to write, so such a run always re-dresses.
        var lacksGlyph = font.Subtype == "Type0";
        try
        {
            if (!lacksGlyph)
                foreach (var ch in probe)
                    if (!char.IsWhiteSpace(ch) && !font.CanRepresent(ch)) { lacksGlyph = true; break; }
        }
        catch { return null; }
        if (!lacksGlyph)
        {
            double own = 0, wi = 0, wM = 0, wW = 0;
            try
            {
                own = font.MeasureString(probe, 1);
                wi = font.MeasureString("i", 1);
                wM = font.MeasureString("M", 1);
                wW = font.MeasureString("W", 1);
            }
            catch { return null; }
            var uniformWidths = wi > 0 && Math.Abs(wi - wM) < 1e-9 && Math.Abs(wM - wW) < 1e-9;
            if (!uniformWidths && own / probe.Length < 0.9) return null;
        }
        // Standing in for a COMPOSITE font, the stand-in is the default serif face
        // itself — that choice does not follow the source's weight or slope, so a
        // bold CID run is re-dressed in the regular face.
        if (font.Subtype == "Type0") return "Times-Roman";
        return TextState.IsBold
            ? (TextState.IsItalic ? "Times-BoldItalic" : "Times-Bold")
            : (TextState.IsItalic ? "Times-Italic" : "Times-Roman");
    }

    /// <summary>The face a stand-in hands off to when it has no glyph for some character
    /// of the replacement. One name covers the cases that arise — dingbats, circled
    /// numerals, kana and han — because the covering face is a full CJK font.</summary>
    private const string CjkStandInFace = "MS-Gothic";

    /// <summary>
    /// The face that must stand in for <paramref name="face"/> because
    /// <paramref name="text"/> contains a character it cannot show, or null when the
    /// stand-in already covers every character. Coverage is asked of the INSTALLED
    /// face's own cmap — the core width tables answer for characters the real font has
    /// no outline for, so they cannot be used to decide this.
    /// </summary>
    private static string? CoveringSubstituteFace(string face, string text)
    {
        var glyphs = SystemFaceGlyphs(face);
        if (glyphs is null) return null;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) continue;
            if (!glyphs.CMap.ContainsKey(ch)) return CjkStandInFace;
        }
        return null;
    }

    /// <summary>The installed face behind a stand-in name, parsed once. Null when the
    /// face is not on this machine.</summary>
    private static GlyphOutlineParser? SystemFaceGlyphs(string face)
    {
        lock (_systemFaceGlyphs)
        {
            if (_systemFaceGlyphs.TryGetValue(face, out var cached)) return cached;
            GlyphOutlineParser? parser = null;
            try
            {
                var path = SystemFaceFilePath(face);
                if (path is not null)
                    parser = new GlyphOutlineParser(System.IO.File.ReadAllBytes(path));
            }
            catch { parser = null; }
            _systemFaceGlyphs[face] = parser;
            return parser;
        }
    }

    private static string? SystemFaceFile(string face) => face switch
    {
        "Times-Roman" => "times.ttf",
        "Times-Bold" => "timesbd.ttf",
        "Times-Italic" => "timesi.ttf",
        "Times-BoldItalic" => "timesbi.ttf",
        "Arial" => "arial.ttf",
        _ => null,
    };

    /// <summary>Path of the installed file behind a stand-in face. The system fonts
    /// folder answers first (the Windows install), then the shared resolver's
    /// directory walk — the folder enum is empty off Windows, where the same file
    /// lives in a configured font directory under whatever letter case.</summary>
    private static string? SystemFaceFilePath(string face)
    {
        var file = SystemFaceFile(face);
        if (file is null) return null;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (dir.Length != 0)
        {
            var p = System.IO.Path.Combine(dir, file);
            if (System.IO.File.Exists(p)) return p;
        }
        return SystemFontResolver.FindFontFile(file);
    }

    /// <summary>Advance widths of the INSTALLED face behind a stand-in, in 1000ths of
    /// an em and left unrounded — the finer table a substituted replacement is
    /// measured through. Indexed by character code (the run is written WinAnsi, and
    /// the codes a substitution carries are Latin). Null when the face is not on this
    /// machine, which falls the caller back to the core width table.</summary>
    private static double[]? SystemFaceWidths(string face)
    {
        lock (_systemFaceWidths)
        {
            if (_systemFaceWidths.TryGetValue(face, out var cached)) return cached;
            double[]? table = null;
            try
            {
                var path = SystemFaceFilePath(face);
                if (path is not null)
                {
                    var gp = new GlyphOutlineParser(System.IO.File.ReadAllBytes(path));
                    var upm = gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000;
                    var t = new double[256];
                    for (var c = 0; c < 256; c++)
                    {
                        var w = gp.CMap.TryGetValue((char)c, out var gid)
                            ? gp.GetAdvanceWidth(gid) * 1000.0 / upm
                            : -1;
                        // A code the face does not carry keeps the core table's answer,
                        // so a partial cmap cannot silently zero a glyph's advance.
                        if (w < 0) { var cw = Standard14Fonts.GetWidth(face, (char)c); w = cw >= 0 ? cw : 500; }
                        t[c] = w;
                    }
                    table = t;
                }
            }
            catch { table = null; }
            _systemFaceWidths[face] = table;
            return table;
        }
    }

    /// <summary>Measure through the stand-in's own width table — the same table the
    /// face is WRITTEN with, so what the flow measures is what the page reports back.</summary>
    private static Func<string, double, double> Standard14Measurer(string face) =>
        (str, size) =>
        {
            double w = 0;
            foreach (var ch in str)
            {
                var cw = Standard14Fonts.GetWidth(face, ch < 256 ? ch : '?');
                w += (cw >= 0 ? cw : 500) * size / 1000.0;
            }
            return w;
        };
}
