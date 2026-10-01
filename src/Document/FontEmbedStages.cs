using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>A shallow copy of a font dictionary: the companion keeps every key of the original.</summary>
    private static PdfDictionary CopyFontDict(PdfDictionary src)
    {
        var copy = new PdfDictionary();
        foreach (var key in src.Keys)
            if (src.Get(key) is { } value) copy.Set(key, value);
        return copy;
    }

    /// <summary>The style part of an embedded substitute's name, in the form PDF gives a styled
    /// TrueType face (ISO 32000-1 §9.6.3: "TimesNewRoman,Bold"): a bold or italic face named
    /// by its family alone reads as the regular face to anything that looks at font names
    /// (text extraction, tagging, conversion to other formats).</summary>
    private static string StyleSuffix(string? subfamily)
    {
        if (string.IsNullOrWhiteSpace(subfamily)) return string.Empty;
        var bold = subfamily.IndexOf("Bold", StringComparison.OrdinalIgnoreCase) >= 0;
        var italic = subfamily.IndexOf("Italic", StringComparison.OrdinalIgnoreCase) >= 0
                     || subfamily.IndexOf("Oblique", StringComparison.OrdinalIgnoreCase) >= 0;
        return bold && italic ? ",BoldItalic" : bold ? ",Bold" : italic ? ",Italic" : string.Empty;
    }

    /// <summary>Embed one simple, glyph-bearing, non-embedded font dict in place, substituting a resolved system face (Helvetica→Arial, etc.) when the named font has none.</summary>
    private void EmbedOneFont(FontEmbedState ef, PdfDictionary fontDict)
    {
        if (!ef.done.Add(fontDict)) return;
        // Consume the transient "embed full, don't subset" marker (set by
        // Font.IsSubset = false). Removed here so it never reaches the output.
        var embedFull = fontDict.GetBool("AsposeEmbedFull");
        fontDict.Remove("AsposeEmbedFull");
        var subtype = fontDict.GetName("Subtype");
        if (subtype == "Type0")
        {
            EmbedNonEmbeddedCidFont(fontDict, ef.options, ef.reported, ef.fontFileCache);
            return;
        }
        if (subtype is not ("Type1" or "TrueType")) return;   // simple fonts only
        // A Type 1 program that carries an undefined charstring command is dropped and its
        // face substituted like an unembedded one (see Type1GlyphSource.HasUndefinedCommand).
        if (subtype == "Type1") DropInvalidType1Program(ef, fontDict);
        if (IsSimpleFontEmbedded(fontDict)) return;
        var baseFont = fontDict.GetName("BaseFont") ?? "";
        // A subset tag does NOT imply an embedded program: setting IsSubset on a
        // non-embedded font prefixes the name without adding a FontFile, and the
        // embed check above is the authority. Strip the tag so the bare family
        // resolves ("WRDIWR+Times-Roman" → "Times-Roman").
        if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];
        if (!ef.includeStandard14 &&
            new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
            return; // standard-14 stay as-is unless the caller opts in (Document.EmbedStandardFonts)

        // The source carries this glyph-bearing font without an embedded
        // program — log it (once per name) as a PDF/A violation before the
        // pass below embeds a resolved face.
        if (ef.options is not null && ef.reported.Add(baseFont))
            ef.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = $"Font '{baseFont}' is not embedded.",
            });

        var resolved = Text.SystemFontResolver.Resolve(baseFont);
        // No text face can stand in for the dingbats: they come from the library's own face.
        if (resolved is null && baseFont == "ZapfDingbats")
        {
            try
            {
                if (Text.FontEmbedder.EmbedDingbatsIntoFontDict(this, fontDict)) return;
            }
            catch { /* fall through to the text substitute below */ }
        }
        string newName;
        byte[]? ttf;
        if (resolved is not null) { ttf = resolved; newName = baseFont; }
        else { ttf = Text.SystemFontResolver.Resolve("Arial"); newName = "Arial"; }
        if (ttf is null || ttf.Length == 0) return;

        // A Standard-14 font carries no program of its own, so the resolver returns a
        // host substitute (Helvetica→Arial, Times→Times New Roman, …). Name the embedded
        // font after the face actually embedded — read from its name table — so the output
        // reflects what was embedded rather than the abstract standard name (matching
        // the public surface). Host-dependent by nature.
        if (new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont))
        {
            try
            {
                var ttp = new Text.TrueTypeParser(ttf);
                ttp.Parse();
                var fam = ttp.FamilyName;
                if (!string.IsNullOrWhiteSpace(fam) && fam != "Unknown")
                    newName = fam.Replace(" ", "") + StyleSuffix(ttp.SubfamilyName);
            }
            catch { /* keep the standard name if the face can't be parsed */ }
        }

        var isStandard14 = new HashSet<string>(Text.FontRepository.Standard14Names, StringComparer.Ordinal).Contains(baseFont);
        // A-level conversion only: snapshot the original before it is overwritten,
        // or plan the subset companion for a real face.
        if (ef.makeCompanions && isStandard14)
            ef.companions[fontDict] = CopyFontDict(fontDict);

        try
        {
            Text.FontEmbedder.EmbedIntoFontDict(this, ttf, fontDict, newName, ef.fontFileCache, subset: !embedFull);
            if (ef.makeCompanions && !isStandard14)
            {
                // Companion: the same face embedded AGAIN under its own entry,
                // beside the one the content references (the conversion leaves two
                // resource entries per fixed font). The companion embeds the
                // SUBSET program - a full-face companion ballooned every
                // conversion by megabytes per face and broke the corpus's
                // output-size budgets (<=500KB outputs
                // grew past 2MB), sizes the expected outputs stay under.
                var companion = CopyFontDict(fontDict);
                Text.FontEmbedder.EmbedIntoFontDict(this, ttf, companion, newName, ef.fontFileCache, subset: true);
                ef.companions[fontDict] = companion;
            }
            // The event reports the substitute by its user-facing family+style
            // name ("Courier-Bold" → "Courier New Bold"); the dictionary keeps
            // the PDF-safe space-free name written above. SynthesizedFontName
            // carries the display name past FontName's space-stripping.
            var reportedName = Text.FontInfo.SubstitutedFaceDisplayName(baseFont, ttf) ?? newName;
            RaiseFontSubstitution(new Text.Font(baseFont, "Type1"),
                new Text.Font(reportedName, "TrueType") { SynthesizedFontName = reportedName });
        }
        catch { /* best-effort: leave the font as-is if embedding fails */ }
    }

    /// <summary>Remove the /FontFile of a Type 1 font whose program carries an undefined
    /// charstring command, logging the violation once per name, so the embedding pass
    /// below substitutes a real face for it.</summary>
    private void DropInvalidType1Program(FontEmbedState ef, PdfDictionary fontDict)
    {
        PdfDictionary? descriptor;
        Core.PdfStream? program;
        try
        {
            descriptor = Reader.ResolveDict(fontDict.Get("FontDescriptor"));
            program = descriptor is null ? null : Reader.ResolveStream(descriptor.Get("FontFile"));
        }
        catch { return; }
        if (descriptor is null || program is null) return;
        Text.Type1GlyphSource? source;
        try
        {
            source = Text.Type1GlyphSource.TryLoad(Reader.DecodeStream(program),
                (int)program.Dict.GetInt("Length1"), (int)program.Dict.GetInt("Length2"));
        }
        catch { return; }
        if (source is null || !source.HasUndefinedCommand()) return;
        descriptor.Remove("FontFile");
        var baseFont = fontDict.GetName("BaseFont") ?? "";
        if (baseFont.Length > 7 && baseFont[6] == '+') baseFont = baseFont[7..];
        if (ef.options is not null && ef.reported.Add(baseFont))
            ef.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "FontEmbedding",
                Description = $"Font '{baseFont}' carries an invalid Type 1 program (an undefined charstring command); its text is re-mapped to a substitute.",
            });
    }

    /// <summary>Embed every non-embedded font reachable from the pages' resources and their annotation appearances.</summary>
    private void EmbedPageFonts(FontEmbedState ef)
    {
        foreach (var page in Pages)
        {
            PdfDictionary? resources;
            try { resources = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
            // Walk the page resources and any nested Form XObject resources — a font
            // used only inside a form/appearance stream (not the page's own /Font) must
            // be embedded too.
            if (resources is not null)
                foreach (var fontDict in CollectFontDictsRecursive(resources, ef.visitedRes))
                    EmbedOneFont(ef, fontDict);

            // Annotation appearance (/AP) streams are NOT reachable from the page
            // /Resources, so their fonts (e.g. a FreeText appearance regenerated with a
            // non-embedded standard /Helvetica) must be walked separately for PDF/A.
            foreach (var apRes in CollectAnnotationAppearanceResources(page))
                foreach (var fontDict in CollectFontDictsRecursive(apRes, ef.visitedRes))
                    EmbedOneFont(ef, fontDict);
        }
    }

    /// <summary>Register each companion font dictionary beside its original in every page's font resources.</summary>
    private void AttachCompanionFonts(FontEmbedState ef)
    {
        if (ef.companions.Count > 0)
            foreach (var page in Pages)
            {
                PdfDictionary? res;
                try { res = Reader.ResolveDict(page.Dict.Get("Resources")); } catch { continue; }
                var fonts = res is null ? null : Reader.ResolveDict(res.Get("Font"));
                if (fonts is null) continue;
                foreach (var key in fonts.Keys.ToArray())
                {
                    PdfDictionary? fd;
                    try { fd = Reader.ResolveDict(fonts.Get(key)); } catch { continue; }
                    if (fd is null || !ef.companions.TryGetValue(fd, out var companion)) continue;
                    var ck = key + "c";
                    while (fonts.ContainsKey(ck)) ck += "c";
                    fonts.Set(ck, companion);
                }
            }
    }
}
