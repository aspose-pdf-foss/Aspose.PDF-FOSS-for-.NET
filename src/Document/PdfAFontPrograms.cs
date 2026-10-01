using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Every resource dictionary a page draws through: its own, and those of the
    /// form XObjects and annotation appearances it reaches, however deeply nested. A font or
    /// an image inside a form is used by the page that invokes the form.</summary>
    internal IEnumerable<PdfDictionary> PageResourceDictionaries(Page page)
    {
        var seen = new HashSet<PdfDictionary>();
        var pending = new Stack<PdfDictionary>();

        void Push(PdfObject? candidate)
        {
            var resources = _reader.ResolveStream(candidate)?.Dict is { } streamDict
                ? _reader.ResolveDict(streamDict.Get("Resources"))
                : _reader.ResolveDict(candidate);
            if (resources is not null && seen.Add(resources)) pending.Push(resources);
        }

        Push(page.Dict.Get("Resources"));
        if (_reader.Resolve(page.Dict.Get("Annots")) is PdfArray annots)
            foreach (var annotRef in annots)
            {
                var appearance = _reader.ResolveDict(_reader.ResolveDict(annotRef)?.Get("AP"));
                if (appearance is null) continue;
                foreach (var state in appearance.Keys) Push(appearance.Get(state));
            }

        while (pending.Count > 0)
        {
            var resources = pending.Pop();
            yield return resources;
            if (_reader.ResolveDict(resources.Get("XObject")) is not { } xObjects) continue;
            foreach (var key in xObjects.Keys)
            {
                var form = _reader.ResolveStream(xObjects.Get(key));
                if (form?.Dict.GetName("Subtype") == "Form") Push(xObjects.Get(key));
            }
        }
    }

    /// <summary>Every font a page draws with, paired with its object number, each font
    /// visited once.</summary>
    internal IEnumerable<(int ObjectNumber, PdfDictionary Font)> PageFonts(Page page,
        HashSet<PdfDictionary> visited)
    {
        foreach (var resources in PageResourceDictionaries(page))
        {
            if (_reader.ResolveDict(resources.Get("Font")) is not { } fonts) continue;
            foreach (var key in fonts.Keys)
            {
                var fontRef = fonts.Get(key);
                if (_reader.ResolveDict(fontRef) is not { } font || !visited.Add(font)) continue;
                yield return ((fontRef as PdfIndirectRef)?.ObjectNumber ?? 0, font);
            }
        }
    }

    /// <summary>Every font the document states, paired with its object number and the page
    /// it is drawn on. A font in the interactive form's default resources belongs to no page
    /// - a viewer reaches for it when it regenerates a field's appearance - so it is yielded
    /// with no page of its own.</summary>
    private IEnumerable<(int ObjectNumber, PdfDictionary Font, int? PageNumber)> DocumentFonts()
    {
        var visited = new HashSet<PdfDictionary>();
        foreach (var page in Pages)
            foreach (var (objectNumber, font) in PageFonts(page, visited))
                yield return (objectNumber, font, page.Number);

        var defaults = _reader.ResolveDict(
            _reader.ResolveDict(_reader.Catalog.Get("AcroForm"))?.Get("DR"));
        if (_reader.ResolveDict(defaults?.Get("Font")) is not { } formFonts) yield break;
        foreach (var key in formFonts.Keys)
        {
            var fontRef = formFonts.Get(key);
            if (_reader.ResolveDict(fontRef) is not { } font || !visited.Add(font)) continue;
            yield return ((fontRef as PdfIndirectRef)?.ObjectNumber ?? 0, font, null);
        }
    }

    /// <summary>
    /// ISO 19005-1 clause 6.3.2: PDF/A-1 admits only three shapes of embedded font program
    /// - a Type 1 program in /FontFile, a TrueType program in /FontFile2, and a /FontFile3
    /// whose subtype is Type1C or CIDFontType0C. A /FontFile3 carrying a whole OpenType file
    /// is none of those, so the file states a program the standard's readers are not
    /// required to understand.
    /// </summary>
    /// <remarks>Reported against the font dictionary, naming the font's own subtype - the
    /// vocabulary reads "invalid for Type0 font" or "invalid for Type1 font" - on the first
    /// page that uses it. Parts 2 and 3 admit OpenType programs and are not checked.</remarks>
    private void ReportInvalidFontProgramSubtypes(PdfFormatConversionOptions options)
    {
        foreach (var (objectNumber, font, pageNumber) in DocumentFonts())
        {
            var program = ResolveFontProgramStream(DescriptorOf(font)?.Get("FontFile3"));
            if (program?.Dict.GetName("Subtype") != "OpenType") continue;
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontProgramSubtype",
                Clause = "6.3.2",
                Description = $"Font program subtype is invalid for {font.GetName("Subtype")} font "
                    + $"'{font.GetName("BaseFont") ?? "Unknown"}'",
                PageNumber = pageNumber,
                ObjectId = objectNumber == 0 ? null : objectNumber.ToString(),
            });
        }
    }

    /// <summary>
    /// ISO 19005 clause 6.3.6: the /Widths a file declares for a font must agree with the
    /// advances of the program it embeds, because a reader is free to use either and the
    /// text would then set differently. The conversion embeds a face for every font the
    /// source left unembedded, and the /Widths it inherited were written for a face that is
    /// no longer the one travelling with the file.
    /// </summary>
    /// <remarks>The disagreement is repairable - the conversion keeps the declared widths,
    /// which is what a reader lays the existing content out with - so the problem is
    /// convertable and does not fail the conversion. Reported once per font, against the
    /// font dictionary.</remarks>
    private void ReportInconsistentGlyphWidths(PdfFormatConversionOptions options)
    {
        foreach (var (objectNumber, font, pageNumber) in DocumentFonts())
        {
            if (font.GetName("Subtype") is not ("Type1" or "TrueType")) continue;
            if (!_reconciledWidths.Contains(font) && !DeclaredWidthsDisagreeWithProgram(font)) continue;
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontWidths",
                Clause = "6.3.6",
                Description = "Width information for glyphs is inconsistent in embedded font "
                    + $"'{font.GetName("BaseFont") ?? "Unknown"}'",
                PageNumber = pageNumber,
                ObjectId = objectNumber == 0 ? null : objectNumber.ToString(),
            });
        }
    }

    /// <summary>While set, a face embedded for a font that states its widths is given those
    /// widths as its own advances (PDF/UA-1 clause 7.21.5 requires the two to agree). A
    /// conversion that sets it accepts one program per distinct set of widths where a
    /// shared face would otherwise travel once.</summary>
    internal bool ReconcileEmbeddedWidths { get; private set; }

    // Fonts whose embedded face had other advances than the widths the file states, made to
    // agree when the face was embedded: the disagreement was found (and repaired) all the same.
    private readonly HashSet<PdfDictionary> _reconciledWidths = new(ReferenceEqualityComparer.Instance);

    /// <summary>Record that <paramref name="font"/>'s embedded program was given the widths
    /// its dictionary states, differing from the face's own.</summary>
    internal void NoteReconciledWidths(PdfDictionary font) => _reconciledWidths.Add(font);

    /// <summary>True when any width the font declares differs from the advance the embedded
    /// TrueType program gives that character. A code the font declares no width for, or one
    /// the program has no glyph for, says nothing either way and is skipped.</summary>
    private bool DeclaredWidthsDisagreeWithProgram(PdfDictionary font)
    {
        var program = ResolveFontProgramStream(DescriptorOf(font)?.Get("FontFile2"));
        if (program is null) return false;
        if (_reader.Resolve(font.Get("Widths")) is not PdfArray widths || widths.Count == 0) return false;
        var firstChar = (int)font.GetInt("FirstChar", -1);
        if (firstChar < 0) return false;

        Text.TrueTypeParser face;
        try
        {
            face = new Text.TrueTypeParser(_reader.DecodeStream(program));
            face.Parse();
        }
        catch { return false; }
        if (face.UnitsPerEm <= 0 || face.GlyphWidths.Length == 0) return false;

        for (var i = 0; i < widths.Count; i++)
        {
            var declared = _reader.Resolve(widths[i]) switch
            {
                PdfInteger n => n.Value,
                PdfReal r => (long)Math.Round(r.Value),
                _ => 0,
            };
            // A zero width is how a font says "this code is unused"; it makes no claim
            // about the program's advance.
            if (declared == 0) continue;
            var code = firstChar + i;
            if (code is < 0 or > 255) continue;
            var text = Text.Cp1252.GetString([(byte)code]);
            if (text.Length == 0 || !face.CMap.TryGetValue(text[0], out var glyph)) continue;
            if (glyph <= 0 || glyph >= face.GlyphWidths.Length) continue;
            if (declared != (long)Math.Round(face.GlyphWidths[glyph] * 1000.0 / face.UnitsPerEm))
                return true;
        }
        return false;
    }

    /// <summary>Resolve a font program stream, reaching the objects a preceding conversion
    /// step allocated but has not yet serialised as well as the ones already in the file - a
    /// face the conversion has just embedded lives only in the pending set.</summary>
    private PdfStream? ResolveFontProgramStream(PdfObject? reference)
    {
        if (reference is null) return null;
        if (_reader.ResolveStream(reference) is { } stream) return stream;
        return reference is PdfIndirectRef indirect ? ResolvePendingStream(indirect.ObjectNumber) : null;
    }

    /// <summary>The font descriptor that carries a font's program: a composite font keeps it
    /// on the descendant CIDFont, a simple font on itself.</summary>
    internal PdfDictionary? DescriptorOf(PdfDictionary font)
    {
        var target = font;
        if (font.GetName("Subtype") == "Type0"
            && _reader.Resolve(font.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants)
            target = _reader.ResolveDict(descendants[0]) ?? font;
        return _reader.ResolveDict(target.Get("FontDescriptor"));
    }
}
