using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tagged;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a list of references: each entry's number in brackets opens its first line, stepped apart
/// from the entry's text in the same string, the entry's later lines standing where its text starts. Each entry is a
/// list item whose label owns the number and the blank after it, whatever the step - a wider number stands nearer its
/// text.</summary>
public class AutoTagBibliographyTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Times-Roman.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
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

    // Three entries set in one text object, 13.8 pt apart and a blank line between them: each number and its text one
    // string, the text stepped to 108 pt - "[1] " and "[9] " 22 pt before it, the wider "[10] " 16 pt (under 1.5 em).
    private const string References =
        "BT /F1 12 Tf 72 720 Td (A list of the works this recommendation refers to follows, each under its number.) Tj\n"
        + "0 -41.4 Td [([1] )-1583(Ferguson, N., Authentication Weaknesses in GCM, National Institute of)] TJ\n"
        + "36 -13.8 Td (Standards and Technology, comments on the drafts, May 20, 2005.) Tj\n"
        + "-36 -27.6 Td [([9] )-1583(National Institute of Standards and Technology, Implementation Guidance)] TJ\n"
        + "36 -13.8 Td (for the validation of cryptographic modules, and the program they serve.) Tj\n"
        + "-36 -27.6 Td [([10] )-1083(NIST Special Publication 800-38A, Recommendation for Block Cipher Modes)] TJ\n"
        + "36 -13.8 Td (of Operation, Methods and Techniques, December 2001.) Tj ET\n";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string TagOf(MarkedContentItem item) => item.Element?.StructureType?.Tag ?? item.Element?.S.Name ?? "";

    [Fact]
    public void AReferencesBracketedNumberIsItsLabelWithTheBlankAfterIt()
    {
        using var doc = Tag(Build(References));
        var items = doc.TaggedContent.StructTreeRootElement.GetMarkedContent(true).Where(i => i.Kind == MarkedContentKind.Text).ToList();

        var labels = items.Where(i => TagOf(i) == "Lbl").Select(i => i.Text.TrimStart()).ToList();
        Assert.Equal(new[] { "[1] ", "[9] ", "[10] " }, labels);
        // The text after the wider number starts where its glyphs do, as the others' does.
        var text = Assert.Single(items, i => i.Text.TrimStart().StartsWith("NIST Special", System.StringComparison.Ordinal));
        Assert.Equal("LBody", TagOf(text));
        Assert.InRange(text.Rectangle.LLX, 107, 109);
    }

    // References keyed by names, each key, the blank after it and the entry's text shown apart in one text object: the
    // text at 167.4 pt, where the entry's later lines start - 41 pt after a short key, 16 pt (1.36 em) after a long one.
    private const string KeyedReferences =
        "BT /F1 12 Tf 72 720 Td (A list of the works this recommendation refers to follows, each under its key.) Tj ET\n"
        + "BT /F1 12 Tf 77.4 692.4 Td ([M-19-17]) Tj ( ) Tj 90 0 Td (Office of Management and Budget \\(2019\\) Enabling Mission) Tj\n"
        + "0 -13.8 Td (Delivery through Improved Identity, Credential, and Access) Tj\n"
        + "0 -13.8 Td (Management, May 21, 2019.) Tj ET\n"
        + "BT /F1 12 Tf 77.4 637.2 Td ([NISTIR 7987]) Tj ( ) Tj 90 0 Td (Ferraiolo DF, Gavrila S, Jansen W \\(2015\\) Policy Machine:) Tj\n"
        + "0 -13.8 Td (Features, Architecture, and Specification. NIST Interagency) Tj\n"
        + "0 -13.8 Td (or Internal Report \\(IR\\) 7987, Rev. 1.) Tj ET\n";

    [Fact]
    public void AnEntrysTextNearALongKeyStandsApartFromItWhereItsLaterLinesStart()
    {
        using var doc = Tag(Build(KeyedReferences));
        var items = doc.TaggedContent.StructTreeRootElement.GetMarkedContent(true).Where(i => i.Kind == MarkedContentKind.Text).ToList();

        // The text after the long key is a run of its own, as the text after the short one is, starting where it stands.
        foreach (var start in new[] { "Office of", "Ferraiolo" })
        {
            var text = Assert.Single(items, i => i.Text.TrimStart().StartsWith(start, System.StringComparison.Ordinal));
            Assert.InRange(text.Rectangle.LLX, 166.4, 168.4);
        }
        Assert.Contains(items, i => i.Text.Trim() == "[NISTIR 7987]");
    }
}
