using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Converters;

public sealed partial class PdfToHtmlConverter
{
    /// <summary>One font resource: its program, its encoding and widths, the face it maps to and the record the HTML draws with.</summary>
    private static bool ResolveFontResource(string key, PdfDictionary fontDict, PdfReader reader, bool preferFontCmap, Dictionary<int, LigatureSubstitutor>? substitutors, string? defaultFontName, bool friendlyFamilies, Dictionary<string, HtmlFontRecord> result)
    {
        var fr = new FontRecordState();
        fr.fontRef = fontDict.Get(key);
        if (reader.ResolveDict(fr.fontRef) is not { } fontDictOfKey) return true;
        fr.font = fontDictOfKey;

        fr.baseFont = fr.font.GetName("BaseFont") ?? "sans-serif";
        var (family, weight, style) = MapFont(fr.baseFont);
        // HtmlSaveOptions.DefaultFontName substitutes the requested face for every
        // source font — embedded ones included: the emitted font classes carry it
        // as the family, so the text both displays and round-trips in that face.
        if (!string.IsNullOrEmpty(defaultFontName)) family = defaultFontName;

        // Otherwise a font that ships as an @font-face program is named by its
        // FULL BaseFont there — subset prefix kept, separator-normalized
        // ("ACMJVR+Arial,Bold" → "ACMJVR+Arial Bold"); the class must reference
        // the SAME family, or the consumer substitutes a host face whose cmap
        // disagrees with a custom-encoded subset (an invoice re-imported through
        // such classes painted every run with a sibling font's garble), and a
        // bold sibling must get its own class rather than folding into the
        // regular face's. With FontSavingMode.DontSave nothing is embedded, so
        // there is no @font-face to match and the class instead keeps the font's
        // FRIENDLY family name (CssFamily below) — the raw BaseFont's style tail
        // ("Calibri-Bold") would otherwise leak into the CSS, which should
        // show the plain family ("Calibri").
        else if (!friendlyFamilies
                 && (HasEmbeddedProgram(fr.font, reader) || SystemResolvable(fr.baseFont)))
        {
            // System-resolvable fonts count too: the sidecar emitter embeds the
            // resolved face as an @font-face under the same BaseFont-derived name.
            var famTag = CssFaceFamily(fr.baseFont);
            if (famTag.Length > 0) family = famTag;
        }
        else if (!friendlyFamilies && CjkSubstituteFamily(fr.font, reader) is { } subFam)
        {
            // A substituted CJK font's class must reference the shipped
            // subset's @font-face name, exactly like an embedded program's.
            family = SubstituteTag(fr.baseFont) + "+" + subFam;
        }

        ReadFontEncoding(fr, reader, preferFontCmap);
        ReadFontCmaps(fr, reader, defaultFontName);

        BuildFontDecoders(fr, substitutors);

        ReadFontMetrics(fr, reader, friendlyFamilies);

        FinishFontRecord(fr, key, reader, family, weight, style, result);
        return true;
    }
}
