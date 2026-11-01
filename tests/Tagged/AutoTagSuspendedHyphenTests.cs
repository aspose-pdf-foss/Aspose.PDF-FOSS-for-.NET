using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a hyphen ending a line before a conjunction: a suspended hyphen - the head of a compound whose
/// tail the words after it give ("retro- and prospective") - stays, a space parting the words; a word broken over the line
/// still joins without its hyphen.</summary>
public class AutoTagSuspendedHyphenTests
{
    /// <summary>A page drawing <paramref name="content"/> with F1 Times-Roman, its space flipped as a word processor's
    /// (the y axis running down from the top).</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 453.5 680.3] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
        };
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Compat.Latin1.GetBytes(s));
        Write("%PDF-1.7\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string Text(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text));

    // A justified paragraph as a word processor writes it: under a flipped space, each line one TJ array whose words the
    // pen is moved apart between, 13 pt down the page from the one before.
    private const string Paragraph =
        "q 1 0 0 -1 0 680.314 cm q 1 0 0 1 56.692 62.362 cm 0 g BT /F1 10.5 Tf\n"
        + "1 0 0 -1 27 100 Tm [(the purpose of the review, the methods of the work and the ) -120 (correlation)] TJ\n"
        + "1 0 0 -1 27 113 Tm [(between the data and the purpose of ) -180 (retro-)] TJ\n"
        + "1 0 0 -1 27 126 Tm [(and prospective analyses, the integration of the models with the ) -60 (sys-)] TJ\n"
        + "1 0 0 -1 27 139 Tm [(tem analysis, and the selection of the experts or of the team.)] TJ\n"
        + "ET Q Q\n";

    // Words broken over the line whose rest a conjunction spells: as a word processor's export kerns them, each line one TJ
    // array of short strings - the rest "or" a string of its own (inter- or|ganizational), "nor" before a comma (do- nor,).
    private const string BrokenWords =
        "q 1 0 0 -1 0 680.314 cm q 1 0 0 1 56.692 62.362 cm 0 g BT /F1 12 Tf\n"
        + "1 0 0 -1 27 100 Tm [(is also more fle)-1 (x)1 (ible for the polic)5 (ies and in)-1 (ter-)] TJ\n"
        + "1 0 0 -1 27 114 Tm [(or)-2 (g)5 (an)-1 (iz)-2 (ational co)5 (llaboration pro)-1 (jects in the field.)] TJ\n"
        + "1 0 0 -1 27 128 Tm [(the manuscripts prepared by the do-)] TJ\n"
        + "1 0 0 -1 27 142 Tm [(nor)-1 (, and ca)5 (pital assets held by the fund.)] TJ\n"
        + "ET Q Q\n";

    // The paragraph as another writer shows it: each word of the line after the hyphen a string of its own, the pen moved
    // a word gap between them.
    private const string WordsApart =
        "q 1 0 0 -1 0 680.314 cm q 1 0 0 1 56.692 62.362 cm 0 g BT /F1 10.5 Tf\n"
        + "1 0 0 -1 27 100 Tm [(the purpose of the review, the methods of the work and the ) -120 (correlation)] TJ\n"
        + "1 0 0 -1 27 113 Tm [(between the data and the purpose of ) -180 (retro-)] TJ\n"
        + "1 0 0 -1 27 126 Tm [(and) -250 (prospective) -250 (analyses,) -250 (the) -250 (integration) -250 (of) -250 (the) -250 (models)] TJ\n"
        + "1 0 0 -1 27 139 Tm [(with the system analysis, and the selection of the experts.)] TJ\n"
        + "ET Q Q\n";

    [Fact]
    public void AHyphenEndingALineBeforeAConjunctionShownAsAStringOfItsOwnStays()
    {
        using var doc = Tag(Build(WordsApart));
        var text = Text(doc.TaggedContent.StructTreeRootElement);

        Assert.Contains("purpose of retro- and prospective analyses", text);
        Assert.DoesNotContain("retroand", text);
    }

    [Fact]
    public void AWordBrokenOverTheLineWhoseRestAConjunctionSpellsJoinsWithoutItsHyphen()
    {
        using var doc = Tag(Build(BrokenWords));
        var text = Text(doc.TaggedContent.StructTreeRootElement);

        Assert.Contains("interorganizational collaboration", text);
        Assert.Contains("prepared by the donor, and capital", text);
        Assert.DoesNotContain("- or", text);
        Assert.DoesNotContain("- nor", text);
    }

    [Fact]
    public void AHyphenEndingALineBeforeAConjunctionStaysAndASpacePartsTheWords()
    {
        using var doc = Tag(Build(Paragraph));
        var text = Text(doc.TaggedContent.StructTreeRootElement);

        Assert.Contains("purpose of retro- and prospective analyses", text);
        Assert.DoesNotContain("retroand", text);
        // A word broken over the line joins without its hyphen.
        Assert.Contains("with the system analysis", text);
    }
}
