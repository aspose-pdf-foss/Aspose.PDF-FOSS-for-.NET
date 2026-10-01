using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Optimization;

internal static partial class FontSubsetter
{
    /// <summary>The stages of the embedded-font subset: collecting one font's used codes, and subsetting one font program.</summary>
    private static bool SubsetFontProgram(FontSubsetState su, Aspose.Pdf.Core.PdfStream fontFileStream, (HashSet<int> subsetCodes, List<(Aspose.Pdf.Core.PdfDictionary fontDict, Aspose.Pdf.Core.PdfDictionary descriptor, string baseFont, HashSet<int> ownCodes)> fonts, bool isPending) entry)
    {
        // Decode the font stream
        byte[] fontData;
        try
        {
            fontData = su.reader.DecodeStream(fontFileStream);
        }
        catch
        {
            return true; // Skip fonts that fail to decode
        }

        if (fontData.Length < 12) return true; // Too small to be a valid TrueType font

        // Parse the font with TrueTypeParser
        TrueTypeParser parser;
        try
        {
            parser = new TrueTypeParser(fontData);
            parser.Parse();
        }
        catch
        {
            return true; // Skip fonts that fail to parse
        }

        // Only the programs THIS conversion just embedded are re-subset when
        // the caller asks for the conservative mode: they come from
        // cmap-complete system faces the subsetter's code model matches. A
        // source's own embedded (usually already-subset) program uses
        // producer-specific encodings — re-subsetting one has produced both
        // tofu (unresolved codes) and mismapped glyphs (rebuilt-cmap key
        // clashes) on Word-produced files.
        if (su.newlyEmbeddedOnly && !entry.isPending) return true;

        // Perform subsetting once for the shared program
        byte[] subsetData;
        try
        {
            var subsetter = new TrueTypeSubsetter(fontData, parser);
            Dictionary<int, int> glyphMap;
            (subsetData, glyphMap) = subsetter.Subset(entry.subsetCodes);
            // Safety valve: codes were used but NONE resolved through the
            // program's cmap — keep the full program.
            if (glyphMap.Count <= 1 && entry.subsetCodes.Count > 0)
                return true;
        }
        catch
        {
            return true; // Skip fonts that fail to subset
        }

        // Only replace if the subset is actually smaller
        if (subsetData.Length >= fontData.Length) return true;

        // Replace the font stream data
        fontFileStream.ReplaceData(subsetData);
        // Remove filter since we're writing raw data
        fontFileStream.Dict.Remove("Filter");
        fontFileStream.Dict.Remove("DecodeParms");
        fontFileStream.Dict.Set("Length", new PdfInteger(subsetData.Length));

        foreach (var (fontDict, descriptor, baseFont, ownCodes) in entry.fonts)
        {
            // Update Length1 in the font descriptor
            descriptor.Set("Length1", new PdfInteger(subsetData.Length));

            // Update Widths array based on used character range
            UpdateWidths(fontDict, ownCodes, parser, su.reader);

            // Add subset prefix to BaseFont name
            AddSubsetPrefix(fontDict, descriptor, baseFont);
        }
        return true;
    }

    /// <summary></summary>
    private static bool CollectFontSubsetCodes(FontSubsetState su, int fontObjNum, HashSet<int> charCodes)
    {
        var fontObj = su.reader.Resolve(new PdfIndirectRef(fontObjNum, 0));
        if (fontObj is not PdfDictionary fontDict) return true;

        var baseFont = fontDict.GetName("BaseFont");
        if (baseFont is null) return true;

        if (fontDict.GetName("Subtype") == "Type0") return true; // handled above

        // Skip Standard 14 fonts
        if (IsStandard14(baseFont)) return true;

        // Get font descriptor
        var descriptorObj = fontDict.Get("FontDescriptor");
        if (descriptorObj is null) return true;
        var descriptor = su.reader.ResolveDict(descriptorObj);
        if (descriptor is null) return true;

        // Only process TrueType fonts with FontFile2
        var fontFileRef = descriptor.Get("FontFile2");
        if (fontFileRef is null) return true;

        // The program stream may be an original file object (resolvable through the
        // reader) or one that a preceding pass (e.g. PDF/A font embedding) allocated but
        // has not yet serialised — those live in the document's pending-object list and
        // are only reachable through the supplied resolver.
        var fontFileStream = su.reader.ResolveStream(fontFileRef);
        var isPendingProgram = fontFileStream is null;
        if (fontFileStream is null && fontFileRef is PdfIndirectRef nref)
            fontFileStream = su.resolveNewStream?.Invoke(nref.ObjectNumber);
        if (fontFileStream is null) return true;


        // The content stream records single-byte character codes; the glyph cmap may be
        // keyed by those raw codes (symbol/Mac cmaps common in Word subset fonts) or by
        // Unicode (a (3,1) cmap, used by the system faces the PDF/A embedder substitutes
        // in). Offer both the raw code and its WinAnsi→Unicode mapping so the subsetter
        // keeps the right glyph whichever cmap the program carries — extra non-matching
        // codes resolve to gid 0 and are ignored, so this never drops a used glyph.
        var subsetCodes = new HashSet<int>(charCodes);
        foreach (var code in charCodes)
            if (code is >= 0 and <= 255)
            {
                subsetCodes.Add(Cp1252.GetString(new[] { (byte)code })[0]);
                // Symbolic (3,0) cmaps — ubiquitous in Word-produced subset
                // fonts — key their glyphs at 0xF000+code.
                subsetCodes.Add(0xF000 | code);
            }

        if (!su.byProgram.TryGetValue(fontFileStream, out var entry))
        {
            entry = (new HashSet<int>(), new List<(PdfDictionary, PdfDictionary, string, HashSet<int>)>(), isPendingProgram);
            su.byProgram[fontFileStream] = entry;
        }
        entry.subsetCodes.UnionWith(subsetCodes);
        entry.fonts.Add((fontDict, descriptor, baseFont, charCodes));
        return true;
    }
}
