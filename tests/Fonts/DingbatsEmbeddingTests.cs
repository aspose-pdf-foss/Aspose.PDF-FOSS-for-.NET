using System.IO;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Fonts;

public class DingbatsEmbeddingTests
{
    /// <summary>A page showing ZapfDingbats code "4" (a check mark), the font not embedded.</summary>
    private static byte[] BuildDingbatsPage() => BuildPage("ZapfDingbats", "4");

    /// <summary>A page showing <paramref name="codes"/> in the standard face <paramref name="baseFont"/>, the font not embedded.</summary>
    private static byte[] BuildPage(string baseFont, string codes) =>
        BuildPageInFont($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} >>", codes);

    /// <summary>A page showing <paramref name="codes"/> at 24 pt from x 72 in the font <paramref name="fontDict"/>,
    /// the objects <paramref name="more"/> numbered from 6 after it.</summary>
    private static byte[] BuildPageInFont(string fontDict, string codes, params string[] more)
    {
        var content = $"BT /F1 24 Tf 72 700 Td ({codes}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            fontDict,
            .. more,
        ];
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>The page converted to <paramref name="format"/> and saved.</summary>
    private static byte[] Converted(byte[] page, PdfFormat format = PdfFormat.PDF_UA_1)
    {
        using var doc = new Document(new MemoryStream(page));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), format, ConvertErrorAction.None));
        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    private static string TextOf(byte[] pdf)
    {
        using var doc = new Document(new MemoryStream(pdf));
        var absorber = new TextAbsorber();
        doc.Pages[1].Accept(absorber);
        return absorber.Text;
    }

    /// <summary>Where the page's text run lies, as the text reader places it.</summary>
    private static Rectangle RunOf(byte[] pdf)
    {
        using var doc = new Document(new MemoryStream(pdf));
        var absorber = new TextFragmentAbsorber();
        doc.Pages[1].Accept(absorber);
        Assert.Equal(1, absorber.TextFragments.Count);
        return absorber.TextFragments[1].Rectangle!;
    }

    /// <summary>The page saved after its font F1 took <paramref name="face"/>, a face built here, as the Symbol font
    /// - the embedding a conversion gives a host's Symbol face, on any host.</summary>
    private static byte[] WithSymbolFace(byte[] page, byte[] face) =>
        Embedded(page, (doc, font) => FontEmbedder.EmbedSymbolIntoFontDict(doc, font, face));

    /// <summary>The page saved after its font F1 took the library's dingbat face, as a conversion embeds ZapfDingbats.</summary>
    private static byte[] WithDingbatsFace(byte[] page) => Embedded(page, FontEmbedder.EmbedDingbatsIntoFontDict);

    private static byte[] Embedded(byte[] page, Func<Document, PdfDictionary, bool> embed)
    {
        using var doc = new Document(new MemoryStream(page));
        Assert.True(embed(doc, FontOf(doc)));
        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    /// <summary>How many of the file's objects are CMaps: the ToUnicode maps it carries.</summary>
    private static int CMapsIn(Document doc) =>
        doc.Reader.XRefTable.Entries.Values.Where(e => e.InUse && e.ObjectNumber > 0)
            .Select(e => doc.Reader.Resolve(new PdfIndirectRef(e.ObjectNumber, e.Generation))).OfType<PdfStream>()
            .Count(s => Encoding.ASCII.GetString(doc.Reader.DecodeStream(s)).Contains("begincmap"));

    private static PdfDictionary FontOf(Document doc) => doc.Reader.ResolveDict(FontsOf(doc).Get("F1"))!;

    /// <summary>The page's /Font resources.</summary>
    private static PdfDictionary FontsOf(Document doc)
    {
        var resources = doc.Reader.ResolveDict(doc.Pages[1].Dict.Get("Resources"))!;
        return doc.Reader.ResolveDict(resources.Get("Font"))!;
    }

    /// <summary>The advance, in thousandths of an em, the embedded program gives the glyph its symbol table keys
    /// <paramref name="code"/> at.</summary>
    private static int Advance(Document doc, int code)
    {
        var descriptor = doc.Reader.ResolveDict(FontOf(doc).Get("FontDescriptor"))!;
        var program = new TrueTypeParser(doc.Reader.DecodeStream(doc.Reader.ResolveStream(descriptor.Get("FontFile2"))!));
        program.PreferSubtable = (3, 0);
        program.Parse();
        Assert.True(program.CMap.TryGetValue(GlyphOutlineParser.SymbolCodeBase + code, out var glyph) && glyph > 0);
        return (int)Math.Round(program.GlyphWidths[glyph] * 1000.0 / program.UnitsPerEm);
    }

    [Fact]
    public void Conversion_EmbedsZapfDingbatsAsDingbats_NotAsAText()
    {
        var output = Converted(BuildDingbatsPage());
        var raw = Compat.Latin1.GetString(output);
        Assert.Contains("+ZapfDingbats", raw);
        Assert.DoesNotContain("Arial", raw);
        Assert.Contains("✔", TextOf(output)); // HEAVY CHECK MARK, not the digit 4
    }

    [Fact]
    public void Conversion_EmbedsSymbolAsTheSymbolFont_ItsCodesStillReadingAsGreek()
    {
        // The host's Symbol face is embedded as the symbol font it is: codes "abg" stay alpha, beta, gamma,
        // not the letters a text encoding would read them as.
        if (SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host to embed
        var output = Converted(BuildPage("Symbol", "abg"));
        Assert.Contains("+Symbol", Compat.Latin1.GetString(output));
        Assert.Contains("αβγ", TextOf(output));
    }

    [Theory]
    [InlineData("SymbolMT")]
    [InlineData("Symbol,Bold")]
    public void Conversion_EmbedsAFontNamedForTheSymbolFaceAsTheSymbolFont(string baseFont)
    {
        // A name the resolver reads as the Symbol face, as producers write it, takes that face as the symbol
        // font it is - not as a text face under a text encoding, which reads its codes as Latin letters.
        if (SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host to embed
        var output = Converted(BuildPage(baseFont, "abg"));
        var raw = Compat.Latin1.GetString(output);
        Assert.Contains("+Symbol", raw);
        Assert.DoesNotContain("WinAnsiEncoding", raw);
        Assert.Contains("αβγ", TextOf(output));
    }

    [Fact]
    public void Conversion_EmbedsAFontNamedForTheDingbatsFaceAsTheDingbats()
    {
        var output = Converted(BuildPage("ZapfDingbats,Bold", "4"));
        var raw = Compat.Latin1.GetString(output);
        Assert.Contains("+ZapfDingbats", raw);
        Assert.DoesNotContain("Arial", raw);
        Assert.Contains("✔", TextOf(output));
    }

    [Fact]
    public void Conversion_ReportsTheSymbolFaceItEmbeds_AndATaggedPdfAKeepsTheSourceEntryBesideIt()
    {
        // As every other standard face the conversion embeds a host face for: the substitution is reported,
        // and a level A conversion keeps the source's own entry beside the embedded one.
        if (SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host to embed
        var substitutions = new List<string>();
        using (var doc = new Document(new MemoryStream(BuildPage("Symbol", "abg"))))
        {
            doc.FontSubstitution += (oldFont, newFont) => substitutions.Add(oldFont.FontName + " -> " + newFont.FontName);
            doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_A_1B, ConvertErrorAction.None));
        }
        Assert.Equal(new[] { "Symbol -> Symbol" }, substitutions);

        var raw = Compat.Latin1.GetString(Converted(BuildPage("Symbol", "abg"), PdfFormat.PDF_A_1A));
        Assert.Contains("/F1c", raw);
        Assert.Matches(@"/BaseFont\s*/Symbol\b", raw);
        Assert.Contains("+Symbol", raw);
    }

    [Fact]
    public void Conversion_KeepsTheWidthsASymbolFontStates_ItsGlyphsStayingWhereTheSourceSetThem()
    {
        // The source states alpha 700, beta 650 and gamma 500 units, not the standard 631, 549 and 411: the
        // page was laid out against them, so the converted run spans the same 44.4 pt from x 72.
        if (SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host to embed
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /FirstChar 97 /LastChar 103 "
            + "/Widths [700 650 0 0 0 0 500] >>", "abg");
        var source = RunOf(page);
        var converted = RunOf(Converted(page));
        Assert.Equal(116.4, source.URX, 3);
        Assert.Equal(source.LLX, converted.LLX, 3);
        Assert.Equal(source.URX, converted.URX, 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ASymbolFace_TakesTheWidthsTheSourceStates_AndTheStandardOnesWhereItStatesNone(bool symbolTable)
    {
        // A face that keys its glyphs by Symbol's codes, in a Windows symbol table or a Macintosh one, and none by
        // character: each glyph is found by its code and advances by the width the source states for it - a
        // stated zero, or a code outside the stated range, by the standard width.
        var face = symbolTable ? TrueTypeFaceBuilder.WithSymbolCmapOnly() : TrueTypeFaceBuilder.WithMacintoshCmapOnly(new Dictionary<char, int>());
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /FirstChar 97 /LastChar 103 "
            + "/Widths [700 650 0 0 0 0 500] >>", "abg");
        var saved = WithSymbolFace(page, face);

        using var doc = new Document(new MemoryStream(saved));
        var font = FontOf(doc);
        Assert.Equal("TrueType", font.GetName("Subtype"));
        Assert.Null(font.Get("Encoding"));
        Assert.Equal(97, font.GetInt("FirstChar"));
        Assert.Equal(103, font.GetInt("LastChar"));
        Assert.Equal(new long[] { 700, 650, 0, 0, 0, 0, 500 },
            ((PdfArray)doc.Reader.Resolve(font.Get("Widths"))!).Select(w => ((PdfInteger)w).Value));
        Assert.Equal(700, Advance(doc, 'a'));
        Assert.Equal(650, Advance(doc, 'b'));
        Assert.Equal(549, Advance(doc, 'c')); // chi, stated as zero
        Assert.Equal(500, Advance(doc, 'g'));
        Assert.Equal(250, Advance(doc, ' ')); // outside the stated range
        Assert.Equal(RunOf(page).URX, RunOf(saved).URX, 3);
        Assert.Equal("αβγ", TextOf(saved).Trim()); // the source names no characters: the face's map does
    }

    /// <summary>A source ToUnicode map naming the codes <paramref name="entries"/> give, as the object after the font.</summary>
    private static string SourceMap(params (char Code, char Text)[] entries)
    {
        var chars = string.Concat(entries.Select(e => $"<{(int)e.Code:X2}> <{(int)e.Text:X4}> "));
        var map = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap "
            + $"1 begincodespacerange <00> <FF> endcodespacerange {entries.Length} beginbfchar {chars}endbfchar "
            + "endcmap CMapName currentdict /CMap defineresource pop end end";
        return $"<< /Length {map.Length} >>\nstream\n{map}\nendstream";
    }

    [Fact]
    public void ASymbolFace_StatesTheStandardWidthsWhereTheSourceStatesNone_AndLaysTheSourcesToUnicodeOverTheFacesMap()
    {
        // The source's own map says what the codes it names stand for - here the capital letters - and its entries
        // win; the face's map names the code it leaves out.
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /ToUnicode 6 0 R >>", "abg",
            SourceMap(('a', 'Α'), ('b', 'Β')));
        var saved = WithSymbolFace(page, TrueTypeFaceBuilder.WithSymbolCmapOnly());

        using var doc = new Document(new MemoryStream(saved));
        var font = FontOf(doc);
        Assert.Equal(32, font.GetInt("FirstChar"));
        Assert.Equal(254, font.GetInt("LastChar"));
        Assert.Equal(631, ((PdfInteger)((PdfArray)doc.Reader.Resolve(font.Get("Widths"))!)['a' - 32]).Value);
        Assert.Equal(631, Advance(doc, 'a'));
        Assert.Equal(RunOf(page).URX, RunOf(saved).URX, 3);
        Assert.Equal("ΑΒγ", TextOf(saved).Trim());
    }

    /// <summary>The ToUnicode map the page's font names, as the saved file writes it.</summary>
    private static string ToUnicodeText(Document doc) =>
        Encoding.ASCII.GetString(doc.Reader.DecodeStream(doc.Reader.ResolveStream(FontOf(doc).Get("ToUnicode"))!));

    [Theory]
    [InlineData("ZapfDingbats", '4', "✔", false, 202)]
    [InlineData("Symbol", 'a', "α", false, 189)]
    [InlineData("Symbol", 'a', "α", true, 189)]
    public void TheEmbeddingsToUnicode_LeavesOutTheCodesTheFacesEncodingLeavesUndefined(string baseFont, char code, string text,
        bool sourceMap, int defined)
    {
        // Of the codes 32 to 254, ZapfDingbats leaves 21 undefined and Symbol 34, 0x7F among them: they name no
        // character, so the map names none for them - not U+0000, which a PDF/UA or PDF/A-2u reader rejects.
        var font = $"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont}{(sourceMap ? " /ToUnicode 6 0 R" : "")} >>";
        var page = sourceMap ? BuildPageInFont(font, code.ToString(), SourceMap((code, text[0])))
            : BuildPageInFont(font, code.ToString());
        var saved = baseFont == "Symbol" ? WithSymbolFace(page, TrueTypeFaceBuilder.WithSymbolCmapOnly()) : Converted(page);

        using var doc = new Document(new MemoryStream(saved));
        Assert.DoesNotContain("<0000>", ToUnicodeText(doc));
        var map = TextAbsorber.ParseToUnicodeFromDict(FontOf(doc), doc.Reader)!;
        Assert.Equal(defined, map.Count);
        Assert.False(map.ContainsKey(0x7F));
        Assert.Equal(text, map[code]);
    }

    [Fact]
    public void UaConversion_MapsEveryCodeASymbolShows_WhereTheSourcesMapNamesSomeOfThem()
    {
        // The source's map names alpha alone: the face's map names beta and gamma beside it, so the converted
        // text reads as the Greek it draws and no code the page shows is logged as mapped nowhere.
        if (SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host to embed
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /ToUnicode 6 0 R >>", "abg",
            SourceMap(('a', 'α')));
        using var doc = new Document(new MemoryStream(page));
        var log = new MemoryStream();
        doc.Convert(new PdfFormatConversionOptions(log, PdfFormat.PDF_UA_1, ConvertErrorAction.None));
        var logText = Encoding.UTF8.GetString(log.ToArray());
        Assert.Contains("Font 'Symbol' is not embedded.", logText); // the font checks ran on it
        Assert.DoesNotContain("7.21.7", logText);
        using var output = new MemoryStream();
        doc.Save(output);
        Assert.Equal("αβγ", TextOf(output.ToArray()).Trim());
    }

    [Fact]
    public void Conversion_WritesTheDingbatsToUnicodeOverTheOneTheSourceGives()
    {
        // The dingbats' own map names each code's dingbat: a source map naming the code's digit is replaced.
        const string digitMap = "/CIDInit /ProcSet findresource begin 12 dict begin begincmap "
            + "1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <34> <0034> endbfchar "
            + "endcmap CMapName currentdict /CMap defineresource pop end end";
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /ZapfDingbats /ToUnicode 6 0 R >>", "4",
            $"<< /Length {digitMap.Length} >>\nstream\n{digitMap}\nendstream");
        Assert.Equal("4", TextOf(page).Trim());
        Assert.Contains("✔", TextOf(Converted(page)));
    }

    [Theory]
    [InlineData("ZapfDingbats", "4", "✔")]
    [InlineData("Symbol", "abg", "αβγ")]
    public void AutoTagging_ReadsASymbolicFontsCodesAsTheCharactersTheEmbeddingMapsThemTo(string baseFont, string codes, string text)
    {
        // The conversion reads the page's text after it embeds the font: the embedding's map already names the codes
        // then, so a check mark is tagged as the check mark and alpha, beta, gamma as Greek, as the saved file reads them.
        if (baseFont == "Symbol" && SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host
        using var doc = new Document(new MemoryStream(BuildPage(baseFont, codes)));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        var tagged = doc.TaggedContent.StructTreeRootElement.GetMarkedContent(true).Where(i => i.Kind == MarkedContentKind.Text);
        Assert.Equal(text, string.Concat(tagged.Select(i => i.Text)));
    }

    [Theory]
    [InlineData("ZapfDingbats", "4", "✔")]
    [InlineData("Symbol", "abg", "αβγ")]
    public void UaConversion_FindsTheToUnicodeMapTheSymbolicEmbeddingWrote(string baseFont, string codes, string text)
    {
        // The map the embedding writes names every code the page shows: the conversion reads it, rather than
        // logging the codes as mapped nowhere and the document as one it cannot convert.
        if (baseFont == "Symbol" && SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host
        using var doc = new Document(new MemoryStream(BuildPage(baseFont, codes)));
        var log = new MemoryStream();
        doc.Convert(new PdfFormatConversionOptions(log, PdfFormat.PDF_UA_1, ConvertErrorAction.None));
        var logText = Encoding.UTF8.GetString(log.ToArray());
        Assert.Contains($"Font '{baseFont}' is not embedded.", logText); // the font checks ran on it
        Assert.DoesNotContain("7.21.7", logText);

        using var output = new MemoryStream();
        doc.Save(output);
        using var saved = new Document(new MemoryStream(output.ToArray()));
        var map = TextAbsorber.ParseToUnicodeFromDict(FontOf(saved), saved.Reader);
        Assert.NotNull(map);
        Assert.Equal(text, string.Concat(codes.Select(code => map![code])));
    }

    [Theory]
    [InlineData("ZapfDingbats")]
    [InlineData("Symbol")]
    public void TheEmbedding_LeavesTheSourcesToUnicodeItReplaces_OutOfTheSavedFile(string baseFont)
    {
        // The font names the embedding's map now, and nothing else names the source's: the file carries the one map,
        // not the other as well with nothing reading it.
        var page = BuildPageInFont($"<< /Type /Font /Subtype /Type1 /BaseFont /{baseFont} /ToUnicode 6 0 R >>", "a",
            SourceMap(('a', 'α')));
        var saved = baseFont == "Symbol" ? WithSymbolFace(page, TrueTypeFaceBuilder.WithSymbolCmapOnly()) : WithDingbatsFace(page);

        using var doc = new Document(new MemoryStream(saved));
        Assert.Equal(1, CMapsIn(doc));
        Assert.Equal(baseFont == "Symbol" ? 189 : 202, TextAbsorber.ParseToUnicodeFromDict(FontOf(doc), doc.Reader)!.Count);
    }

    [Fact]
    public void TheEmbedding_KeepsTheSourcesToUnicode_WhereAnotherFontStillNamesIt()
    {
        // Only what nothing reaches is left out: a map a second font of the page shares stays, and reads as before.
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /ToUnicode 6 0 R >>", "a",
            SourceMap(('a', 'α')));
        byte[] saved;
        using (var source = new Document(new MemoryStream(page)))
        {
            var other = new PdfDictionary();
            other.Set("Type", new PdfName("Font"));
            other.Set("Subtype", new PdfName("Type1"));
            other.Set("BaseFont", new PdfName("Helvetica"));
            other.Set("ToUnicode", new PdfIndirectRef(6, 0));
            FontsOf(source).Set("F2", other);
            Assert.True(FontEmbedder.EmbedSymbolIntoFontDict(source, FontOf(source), TrueTypeFaceBuilder.WithSymbolCmapOnly()));
            using var output = new MemoryStream();
            source.Save(output);
            saved = output.ToArray();
        }

        using var doc = new Document(new MemoryStream(saved));
        Assert.Equal(2, CMapsIn(doc));
        var shared = doc.Reader.ResolveDict(FontsOf(doc).Get("F2"))!;
        Assert.Equal(new Dictionary<int, string> { ['a'] = "α" }, TextAbsorber.ParseToUnicodeFromDict(shared, doc.Reader));
    }

    [Fact]
    public void ALevelAConversion_KeepsTheSourcesToUnicode_ForTheCompanionEntryThatStillNamesIt()
    {
        // A level A conversion keeps the source's own entry beside the embedded one, and that entry names the source's
        // map: the map stays, and reads as the source wrote it, beside the embedding's.
        if (SystemFontResolver.Resolve("Symbol") is null) return; // no Symbol face on this host to embed
        var page = BuildPageInFont("<< /Type /Font /Subtype /Type1 /BaseFont /Symbol /ToUnicode 6 0 R >>", "abg",
            SourceMap(('a', 'α')));
        var converted = Converted(page, PdfFormat.PDF_A_2A);

        using var doc = new Document(new MemoryStream(converted));
        Assert.Equal(2, CMapsIn(doc));
        var companion = doc.Reader.ResolveDict(FontsOf(doc).Get("F1c"))!;
        Assert.Equal(new Dictionary<int, string> { ['a'] = "α" }, TextAbsorber.ParseToUnicodeFromDict(companion, doc.Reader));
        Assert.Equal("αβγ", TextOf(converted).Trim());
    }
}
