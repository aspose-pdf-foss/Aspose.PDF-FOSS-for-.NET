using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
    /// <summary>Shows the fragment in the font its state names when that font has no host program to
    /// embed: a font taken from a run of a document - its program embedded there, or a face the
    /// standard 14 do not stand for - through its own dictionary and codes, or a predefined CJK face,
    /// declared in its predefined CMap. Both were written in the Standard-14 face their name maps to:
    /// another face and width, the baseline one descent low, CJK text as '?'. False, nothing set, when
    /// the font is neither, or the run is one this route cannot show as one string of codes (several
    /// segments, line breaks, word spacing, a character the font has no code for); the Standard-14
    /// face takes it then.</summary>
    private bool TryShowInFontWithoutProgram(FragmentWriteState fw, TextFragment fragment)
    {
        var state = fragment.TextState;
        // A stand-in the caller chose (a replacement reflowed in the face it measured with) is kept.
        if (state.Std14FaceOverride is not null || fragment.Segments.Count > 1 || state.WordSpacing != 0
            || fw.text.IndexOfAny(LineBreakChars) >= 0)
            return false;
        if (state.FontData?.PredefinedCid is { } cid)
        {
            fw.fontResName = FontCollection.DeclarePredefinedCid(GetOrCreateOwnFontDict(), cid);
            fw.hexGlyphIds = cid.Encode(fw.text);
            fw.hostlessDescent = cid.Descent;
            return true;
        }
        if (state.FontData is not null || state.Font is not { } font || !IsShowableDocumentFont(font)
            || _page.Reader is null || font.TryEncode(fw.text) is not { } codes)
            return false;
        fw.fontResName = EnsureDocumentFontResource(FontDictForThisDocument(font));
        fw.hexGlyphIds = codes;
        fw.hostlessDescent = font.GetMetrics()?.Descent ?? 0;
        return true;
    }

    /// <summary>A font read out of a document whose dictionary can carry new text: one that embeds its
    /// program, or one naming a face the standard 14 do not stand for (a standard face is written as
    /// one already).</summary>
    private static bool IsShowableDocumentFont(FontInfo font) =>
        font.IsDocumentEmbeddedFont
        || (font.IsDocumentFont && !Standard14Fonts.IsStandard14(font.FontDict.GetName("BaseFont") ?? font.FontName));

    private static readonly char[] LineBreakChars = ['\n', '\r'];

    /// <summary>The dictionary of a document font as this page's document holds it: the font itself in
    /// its own document, else its copy - imported once per target document, with the program and the
    /// /ToUnicode it references - since its references number objects of the document it came from.</summary>
    internal PdfDictionary FontDictForThisDocument(FontInfo font)
    {
        var source = font.SourceReader;
        if (source is null || ReferenceEquals(source, _page.Reader) || _page.OwnerDocument is not { } document)
            return font.FontDict;
        var imported = ImportedFonts.GetValue(font.FontDict, _ => new ImportedFont());
        lock (imported)
        {
            if (!ReferenceEquals(imported.Target, document))
            {
                imported.Dict = document.ImportDict(font.FontDict, source, document.GetSharedImportCloneMap(source));
                imported.Target = document;
            }
            return imported.Dict!;
        }
    }

    // A document font's copy in the document it was last shown in, kept with the font's dictionary.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, ImportedFont> ImportedFonts = new();

    private sealed class ImportedFont
    {
        public Document? Target;
        public PdfDictionary? Dict;
    }
}
