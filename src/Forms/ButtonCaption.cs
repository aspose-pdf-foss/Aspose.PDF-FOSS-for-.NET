using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public partial class ButtonField
{
    // The Type0 font a caption beyond ASCII is drawn in, set while the face is built.
    private PdfDictionary? _captionFonts;

    private const string CaptionFontFallback = "Arial";

    /// <summary>A caption with a character beyond ASCII shown through a face that has it, embedded
    /// in the appearance: the font of the default appearance's host face, else a face covering the
    /// caption's script. Returns the font's resource name, the show operand and the caption's
    /// width; null for an ASCII caption, or when no face resolves. The caption was written as
    /// Latin-1 bytes under a standard font with no encoding - 'á' drawn as 'Æ' - and anything past
    /// Latin-1 folded or lost.</summary>
    private (string font, string shown, double width)? TryShowCaptionInCoveringFace(string caption, string fontName, double fontSize)
    {
        if (caption.All(c => c <= AsciiMax)) return null;
        var family = StandardBaseFont(string.IsNullOrEmpty(fontName) ? "Helv" : fontName);
        var ttf = Text.SystemFontResolver.Resolve(family) ?? Text.SystemFontResolver.Resolve(CaptionFontFallback);
        if (!Text.FontRepository.CoversText(ttf, caption) && TextStamp.TryResolveCjkTtf(caption) is { } cjk)
            (ttf, family) = (cjk.ttf, cjk.name);
        if (ttf is not { Length: > 0 }) return null;
        _captionFonts = new PdfDictionary();
        var (resName, glyphs) = Text.Type0FontEmbedder.Embed(_captionFonts, ttf, family, caption);
        var width = Text.Type0FontEmbedder.MeasureText(_captionFonts, ttf, family, caption, fontSize);
        var hex = new System.Text.StringBuilder(glyphs.Length * 2 + 2).Append('<');
        foreach (var b in glyphs) hex.Append(b.ToString("X2"));
        return (resName, hex.Append('>').ToString(), width);
    }

    private const char AsciiMax = '\u007F';

    /// <summary>The appearance's font resources: the covering face a caption beyond ASCII took, else the
    /// standard font the default appearance names.</summary>
    private PdfDictionary CaptionFontResources(string fontName)
    {
        if (_captionFonts is null) return MakeStandardFontResources(fontName);
        var resources = new PdfDictionary();
        resources.Set("Font", _captionFonts);
        return resources;
    }
}
