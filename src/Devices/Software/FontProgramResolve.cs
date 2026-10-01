using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>The glyph-parser resolution of one font resource: the embedded program under the descriptor, then the host-font fallback.</summary>
    /// <returns>The glyph source resolved from the host, or the one passed in, and the horizontal scale the substitute wants.</returns>
    private static (IGlyphOutlineSource? parser, double hScale) ResolveHostFontFallback(PdfDictionary fontDict, PdfDictionary? descriptor, IO.PdfReader reader, IGlyphOutlineSource? parser, double hScale)
    {
        // Prefer the font dict's /BaseFont. Some non-embedded TrueType fonts
        // ship with no BaseFont — the host-font name (Arial / Arial,Bold) lives
        // only on FontDescriptor./FontName, and that's where Adobe Reader picks
        // it up too. Fall back to FontDescriptor./FontName, resolving the
        // indirect ref explicitly rather than relying on GetName.
        var baseFont = fontDict.GetName("BaseFont");
        if (baseFont is null && descriptor is not null
            && reader.Resolve(descriptor.Get("FontName")) is PdfName descFontName)
        {
            baseFont = descFontName.Value;
        }
        if (baseFont is not null)
        {
            var isClassicSubset = baseFont.Length > 7 && baseFont[6] == '+' &&
                baseFont[..6].All(c => c >= 'A' && c <= 'Z');
            // A Type0 font with an Adobe REGISTRY ordering (GB1, Japan1, ...) and no
            // embedded program must NOT borrow a system face here. Its show-string
            // carries Adobe CIDs, and the CID draw path would index them straight
            // into that face's own glyph order - a different ordering entirely - so
            // the page came out as real but WRONG Chinese. Leaving the parser null
            // routes it to the CID fallback below, which maps CID to Unicode and
            // then through the resolved face's cmap. An Identity ordering keeps the
            // system face: its codes carry no registry meaning, so reading them as
            // glyph ids is the only thing available.
            if (!isClassicSubset && RegistryOrderingOf(fontDict, reader) is null)
            {
                var systemTtf = SystemFontResolver.Resolve(baseFont);
                if (systemTtf is not null)
                    parser = new GlyphOutlineParser(systemTtf);
            }
        }

        // Last resort: the BaseFont name matched no installed family (an obfuscated
        // name like "HE108E", or a Multiple-Master Type1 with no embedded program).
        // Substitute a Standard-14 face chosen from the FontDescriptor's flags/style
        // so the text still renders — the simple font's /Encoding maps each code to a
        // glyph name, which the substitute's cmap resolves. CID (Type0) fonts have
        // their own CJK fallback, so this applies only to simple fonts.
        if (parser is null && descriptor is not null && fontDict.GetName("Subtype") != "Type0")
        {
            (var sub, hScale) = SystemFontResolver.ResolveDescriptorSubstitute(fontDict, descriptor, reader);
            if (sub is not null)
                parser = new GlyphOutlineParser(sub);
        }
        return (parser, hScale);
    }

    /// <summary></summary>
    /// <returns>The glyph source of the embedded program, or the one passed in when the descriptor embeds none.</returns>
    private static IGlyphOutlineSource? LoadEmbeddedFontProgram(IO.PdfReader reader, PdfDictionary descriptor, IGlyphOutlineSource? parser, bool convertFontsToUnicodeTtf)
    {
        // TrueType embedding: /FontFile2 (TrueType) — the common CIDFontType2 path.
        var fontFile2 = reader.ResolveStream(descriptor.Get("FontFile2"));
        if (fontFile2 is not null)
        {
            var ttfData = reader.DecodeStream(fontFile2);
            // Some generators park a BARE CFF program under /FontFile2 (a
            // CIDFontType2 whose "TrueType" bytes begin with the CFF header
            // 01 00 hdrSize offSize). A real sfnt starts 00 01 00 00 / 'true' /
            // 'ttcf', so the two are unambiguous — route the CFF to its parser
            // instead of reading garbage table offsets and painting nothing.
            // An 'OTTO' container under /FontFile2 has CFF outlines too (no
            // glyf table) — same rerouting.
            if (ttfData.Length > 4 && ttfData[0] == 0x01 && ttfData[1] == 0x00)
                parser = LoadCharstringFont(ttfData, convertFontsToUnicodeTtf);
            else if (LooksLikeSfnt(ttfData) is (true, true))
                parser = LoadCharstringFont(ttfData, convertFontsToUnicodeTtf);
            else if (ttfData.Length > 0)
                parser = new GlyphOutlineParser(ttfData);
        }

        // CFF embedding: /FontFile3 with /Subtype /Type1C, /CIDFontType0C, or
        // /OpenType. CffGlyphSource unwraps the OpenType SFNT container if
        // present before parsing the CFF structure.
        if (parser is null)
        {
            var fontFile3 = reader.ResolveStream(descriptor.Get("FontFile3"));
            if (fontFile3 is not null)
            {
                var cffData = reader.DecodeStream(fontFile3);
                if (cffData.Length > 0)
                    parser = LoadCharstringFont(cffData, convertFontsToUnicodeTtf);
            }
        }

        // PostScript Type 1 embedding: /FontFile (no number). The stream
        // dict carries /Length1 (ASCII header) and /Length2 (eexec
        // encrypted body) byte counts that Type1GlyphSource needs to
        // know where to split. Falls through to system-font lookup when
        // the stream is missing or unparseable.
        if (parser is null)
        {
            var fontFile1 = reader.ResolveStream(descriptor.Get("FontFile"));
            if (fontFile1 is not null)
            {
                var t1Data = reader.DecodeStream(fontFile1);
                if (LooksLikeSfnt(t1Data) is (true, var isOpenTypeCff))
                {
                    // Some PDFs embed a CIDFontType2 (TrueType) program under the
                    // /FontFile key instead of the standard /FontFile2.
                    // Parse the sfnt as TrueType/OpenType, not as Type1.
                    parser = isOpenTypeCff
                        ? LoadCharstringFont(t1Data, convertFontsToUnicodeTtf)
                        : new GlyphOutlineParser(t1Data);
                }
                else if (t1Data.Length > 0)
                {
                    var len1 = (int)fontFile1.Dict.GetInt("Length1");
                    var len2 = (int)fontFile1.Dict.GetInt("Length2");
                    var t1 = Type1GlyphSource.TryLoad(t1Data, len1, len2);
                    if (t1 is not null && convertFontsToUnicodeTtf)
                        t1.QuantizeToFontUnits = true;
                    parser = t1;
                }
            }
        }
        return parser;
    }
}
