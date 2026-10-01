using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

public class AutoTagRetagTests
{
    /// <summary>A tagged page: a Span whose replacement text is "fi", a French paragraph and a
    /// described figure, each over its own marked content.</summary>
    private static byte[] BuildAuthorTagged(string producer = "Some other producer")
    {
        const string content =
            "/Span <</MCID 0>> BDC BT /F1 12 Tf 72 700 Td (\\001) Tj ET EMC\n" +
            "/P <</MCID 1>> BDC BT /F1 12 Tf 72 670 Td (Bonjour tout le monde) Tj ET EMC\n" +
            "/Figure <</MCID 2>> BDC q 100 0 0 100 72 450 cm /Im1 Do Q EMC\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /MarkInfo << /Marked true >> /StructTreeRoot 4 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /StructParents 0 /Contents 10 0 R " +
                "/Resources << /Font << /F1 11 0 R >> /XObject << /Im1 12 0 R >> >> >>",
            "<< /Type /StructTreeRoot /K 5 0 R /ParentTree 9 0 R >>",
            "<< /Type /StructElem /S /Document /P 4 0 R /K [6 0 R 7 0 R 8 0 R] >>",
            "<< /Type /StructElem /S /Span /P 5 0 R /Pg 3 0 R /K 0 /ActualText (fi) >>",
            "<< /Type /StructElem /S /P /P 5 0 R /Pg 3 0 R /K 1 /Lang (fr-FR) >>",
            "<< /Type /StructElem /S /Figure /P 5 0 R /Pg 3 0 R /K 2 /Alt (A red square) >>",
            "<< /Nums [0 [6 0 R 7 0 R 8 0 R]] >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB " +
                "/BitsPerComponent 8 /Length 3 >>\nstream\n\xFF\x00\x00\nendstream",
            $"<< /Producer ({producer}) >>",
        };
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
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R /Info {objects.Length} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static byte[] Retag(byte[] source)
    {
        using var doc = new Document(new MemoryStream(source));
        var options = new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = AutoTaggingSettings.Default,
        };
        doc.Convert(options);
        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    [Fact]
    public void Retagging_KeepsTheAuthorsReplacementTextAndLanguageInTheContent()
    {
        using var doc = new Document(new MemoryStream(Retag(BuildAuthorTagged())));
        var content = Compat.Latin1.GetString(doc.Pages[1].GetContentStreamBytes()!);

        // "fi" and "fr-FR" as hex strings in marks of their own around the same content.
        Assert.Contains("/Span << /ActualText <6669> >> BDC", content);
        Assert.Contains("/Lang <66722D4652>", content);
    }

    [Fact]
    public void Retagging_KeepsTheAuthorsFigureDescription()
    {
        using var doc = new Document(new MemoryStream(Retag(BuildAuthorTagged())));
        var figures = doc.TaggedContent.RootElement.FindElements<FigureElement>(true);

        Assert.Contains(figures, f => f.AlternativeText == "A red square");
    }

    private static Document ConvertToLevelA(byte[] source)
    {
        var doc = new Document(new MemoryStream(source));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_A_2A, ConvertErrorAction.None));
        using var output = new MemoryStream();
        doc.Save(output);
        doc.Dispose();
        return new Document(new MemoryStream(output.ToArray()));
    }

    private static bool HasTheSourcesSpan(Document doc) =>
        doc.TaggedContent.RootElement.FindElements<SpanElement>(true).Any(s => s.ActualText == "fi");

    [Fact]
    public void LevelAConversion_KeepsACompleteTreeThisLibraryProduced()
    {
        using var doc = ConvertToLevelA(BuildAuthorTagged("Aspose.PDF.FOSS for .NET 1.0.0"));
        Assert.True(HasTheSourcesSpan(doc));
    }

    [Fact]
    public void LevelAConversion_RetagsATreeWhoseDocumentNamesNoProducer()
    {
        using var doc = ConvertToLevelA(BuildAuthorTagged(""));
        Assert.False(HasTheSourcesSpan(doc));
    }

    [Fact]
    public void LevelAConversion_RetagsATreeAnotherProducerWrote()
    {
        using var doc = ConvertToLevelA(BuildAuthorTagged("Some other producer"));
        Assert.False(HasTheSourcesSpan(doc));
    }
}
