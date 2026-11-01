using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Every resource dictionary a page draws through: its own, and those of the
    /// form XObjects and annotation appearances it reaches, however deeply nested. A font or
    /// an image inside a form is used by the page that invokes the form. With
    /// <paramref name="throughPatterns"/> the cells of the tiling patterns it paints with are
    /// followed too: text drawn inside a pattern cell shows through the pattern's fonts.</summary>
    internal IEnumerable<PdfDictionary> PageResourceDictionaries(Page page, bool throughPatterns = false)
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
            if (throughPatterns && _reader.ResolveDict(resources.Get("Pattern")) is { } patterns)
                foreach (var key in patterns.Keys)
                    if (_reader.ResolveStream(patterns.Get(key)) is not null) Push(patterns.Get(key));
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
        foreach (var resources in PageResourceDictionaries(page, throughPatterns: true))
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
    /// which is what a reader lays the existing content out with, and sets the advances of a
    /// TrueType program the source embeds to them - so the problem is
    /// convertable and does not fail the conversion. Reported once per font, against the
    /// font dictionary. The same walk over the fonts restates the encoding of a non-symbolic
    /// TrueType font that names no base PDF/A admits, and gives a composite font's TrueType
    /// CIDFont the CID-to-glyph map every part requires and a CIDSet that names its program's
    /// CIDs: part 1 requires the set of a subset, later parts only require a stated set to be
    /// right, so a wrong one is dropped there.</remarks>
    private void ApplyFontDictionaryRules(PdfAConvertState pa)
    {
        foreach (var (objectNumber, font, pageNumber) in DocumentFonts())
        {
            if (font.GetName("Subtype") == "Type0")
            {
                StateIdentityCidToGidMap(font);
                if (pa.part == "1") WriteSubsetCidSet(font, addMissing: true);
                else DropMisstatedCidSet(font);
                continue;
            }
            if (font.GetName("Subtype") is not ("Type1" or "TrueType")) continue;
            StateTrueTypeBaseEncoding(font);
            if (pa.part == "1") WriteType1SubsetCharSet(font);
            if (!_reconciledWidths.Contains(font) && !DeclaredWidthsDisagreeWithProgram(font)) continue;
            // A program the source embeds is made to agree with the widths the page was laid
            // out against. A face this conversion embedded is shared by every font that
            // stands in for its family; the subset that follows writes its advances instead.
            if (font.GetName("Subtype") == "TrueType"
                && _reader.ResolveStream(DescriptorOf(font)?.Get("FontFile2")) is not null)
                try { RepairSimpleTrueTypeWidths(font); }
                catch { /* an unreadable program is left as it is */ }
            pa.options.ConversionLog.Add(new PdfAViolation
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

    /// <summary>
    /// ISO 19005-1 clause 6.3.7 and 19005-2 clause 6.2.11.6: a non-symbolic TrueType font
    /// states WinAnsiEncoding or MacRomanEncoding, as its encoding or as the base under its
    /// /Differences. A font whose encoding dictionary names no base is restated over
    /// WinAnsiEncoding when that changes nothing a reader does: every code it shows is named
    /// by /Differences, so no code falls back on the base, and the program's Unicode cmap has
    /// a glyph for each of those names, which is how a reader finds the glyph of a named code.
    /// A font over StandardEncoding - the built-in encoding of a Type 1 font the conversion
    /// embedded a face for - names its codes through that table under its /Differences, and
    /// a code whose glyph the face lacks is left to the new base. A difference WinAnsiEncoding
    /// already states is left out, and a font left with none states the base alone - part 1
    /// admits no /Differences at all.
    /// </summary>
    private void StateTrueTypeBaseEncoding(PdfDictionary font)
    {
        if (font.GetName("Subtype") != "TrueType") return;
        var encoding = _reader.Resolve(font.Get("Encoding"));
        var standardBase = encoding is PdfName { Value: "StandardEncoding" }
            || (encoding as PdfDictionary)?.GetName("BaseEncoding") == "StandardEncoding";
        var differences = _reader.Resolve((encoding as PdfDictionary)?.Get("Differences")) as PdfArray;
        if (!standardBase && (encoding is not PdfDictionary dictionary || dictionary.Get("BaseEncoding") is not null
            || differences is null)) return;
        var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
        if (((_reader.Resolve(descriptor?.Get("Flags")) as PdfInteger)?.Value & SymbolicFlag) != 0) return;

        var names = new SortedDictionary<int, string>();
        if (standardBase)
            for (var code = 0; code < SingleByteCodeCount; code++)
                if (Text.Type1StandardEncoding.GetName(code) is { } standard) names[code] = standard;
        var next = 0;
        foreach (var item in differences ?? [])
            switch (_reader.Resolve(item))
            {
                case PdfInteger start: next = (int)start.Value; break;
                case PdfName glyph: names[next++] = glyph.Value; break;
            }
        var shown = ShownCodes(font, composite: false).Where(code => !standardBase || names.ContainsKey(code)).ToList();
        if (shown.Count == 0 || !shown.All(names.ContainsKey)) return;

        if (ResolveFontProgramStream(descriptor?.Get("FontFile2")) is not { } program) return;
        Text.TrueTypeParser face;
        try
        {
            var bytes = _reader.DecodeStream(program);
            if (!Text.TrueTypeAdvances.CmapSubtables(bytes).Contains(MicrosoftUnicodeCmap)) return;
            face = new Text.TrueTypeParser(bytes);
            face.Parse();
        }
        catch { return; }
        bool HasGlyph(int code) => Text.TextAbsorber.GlyphNameToUnicode.TryGetValue(names[code], out var text)
            && text.Length > 0 && face.CMap.TryGetValue(char.ConvertToUtf32(text, 0), out var glyph) && glyph > 0;
        if (standardBase) shown.RemoveAll(code => !HasGlyph(code));
        else if (!shown.All(HasGlyph)) return;

        var restated = new PdfArray();
        next = -1;
        foreach (var code in shown.Where(code => names[code] != Text.PdfEncodings.WinAnsiName(code)))
        {
            if (code != next) restated.Add(new PdfInteger(code));
            restated.Add(new PdfName(names[code]));
            next = code + 1;
        }
        if (restated.Count == 0)
        {
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
            return;
        }
        // A dictionary of the font's own: the one it had may be shared with a font that
        // shows other codes.
        var stated = new PdfDictionary();
        stated.Set("Type", new PdfName("Encoding"));
        stated.Set("BaseEncoding", new PdfName("WinAnsiEncoding"));
        stated.Set("Differences", restated);
        font.Set("Encoding", stated);
    }

    // The codes a simple font's encoding covers.
    private const int SingleByteCodeCount = 256;

    // The cmap subtable a reader maps a glyph name's character through: platform 3
    // (Microsoft), encoding 1 (Unicode).
    private static readonly (int Platform, int Encoding) MicrosoftUnicodeCmap = (3, 1);

    // A subset's /BaseFont opens with a tag of this many capital letters and a plus sign.
    private const int SubsetTagLength = 6;

    /// <summary>ISO 19005-1 clause 6.3.5: the descriptor of a Type 1 font subset lists, in
    /// /CharSet, the names of the glyphs the subset defines. A subset that states no list gets
    /// the one its program gives; a list the file already states is left as it is.</summary>
    private void WriteType1SubsetCharSet(PdfDictionary font)
    {
        if (font.GetName("Subtype") != "Type1" || font.GetName("BaseFont") is not { } name) return;
        if (name.Length <= SubsetTagLength || name[SubsetTagLength] != '+') return;
        var descriptor = _reader.ResolveDict(font.Get("FontDescriptor"));
        if (descriptor is null || descriptor.Get("CharSet") is not null) return;

        // The program is a Type 1 one in /FontFile, or its compact form in /FontFile3.
        Dictionary<string, int>? glyphs = null;
        try
        {
            if (_reader.ResolveStream(descriptor.Get("FontFile")) is { } program)
                glyphs = Text.Type1GlyphSource.TryLoad(_reader.DecodeStream(program),
                    (int)program.Dict.GetInt("Length1"), (int)program.Dict.GetInt("Length2"))?.NameToGid;
            else if (_reader.ResolveStream(descriptor.Get("FontFile3")) is { } compact
                && compact.Dict.GetName("Subtype") == "Type1C")
                glyphs = Text.CffGlyphSource.TryLoad(_reader.DecodeStream(compact))?.NameToGid;
        }
        catch { return; }
        if (glyphs is null) return;

        var listed = new System.Text.StringBuilder();
        foreach (var glyph in glyphs.OrderBy(pair => pair.Value).Select(pair => pair.Key))
            if (glyph != ".notdef") listed.Append('/').Append(glyph);
        if (listed.Length > 0)
            descriptor.Set("CharSet", new PdfString(Compat.Latin1.GetBytes(listed.ToString())));
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
