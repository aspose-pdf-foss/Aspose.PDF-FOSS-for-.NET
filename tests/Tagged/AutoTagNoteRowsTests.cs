using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of footnotes set one to a line at a page's foot, each a raised number then an address: lines
/// opening with a raised note number are no table's rows, whatever columns their numbers and texts line up in.</summary>
public class AutoTagNoteRowsTests
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

    private static string Line(double x, double y, string text, double size = 12)
        => System.FormattableString.Invariant($"BT /F1 {size} Tf {x} {y} Td ({text}) Tj ET\n");

    /// <summary>Lines of 12 pt prose from (72, top) down, 14 pt apart.</summary>
    private static string Prose(double top, int lines)
        => string.Concat(Enumerable.Range(0, lines).Select(i =>
            Line(72, top - 14 * i, $"The survey of the ridge went on through the summer and the autumn months {i}")));

    // Body text, then three footnotes at the foot, as a word processor sets them: each its number raised and set small
    // (8 pt, then 6 pt), a blank, then its address in 9 pt blue, a rule in the same blue drawn just under its baseline.
    private static readonly string Page =
        Prose(720, 6)
        + "0 g\n" + Line(72, 121.4, "5", 8) + Line(76, 116.7, " ", 9)
        + "0 0 1 rg\n" + Line(79, 116.7, "https://blog.example.com/inside-the-infamous-botnet-a-retrospective-analysis/", 9)
        + "79 116.2 322.9 0.4 re f\n"
        + "0 g\n" + Line(72, 109.1, "6", 6) + Line(75, 105.9, " ", 9)
        + "0 0 1 rg\n" + Line(77.3, 105.9, "https://aws.example.com/message/41926/", 9)
        + "77.3 105.2 147.5 0.4 re f\n"
        + "0 g\n" + Line(72, 98.8, "7", 6) + Line(75, 95.5, " ", 9)
        + "0 0 1 rg\n" + Line(78, 95.5, "https://www.example.co.nz/business/news/article.cfm?c_id=3&objectid=12286870", 9)
        + "78 94.9 300.6 0.4 re f\n";

    private static Document Tag(byte[] pdf)
    {
        var doc = new Document(new MemoryStream(pdf));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None)
        {
            AutoTaggingSettings = new AutoTaggingSettings { EnableAutoTagging = true },
        });
        return doc;
    }

    private static string TagOf(StructureElement el) => el.StructureType?.Tag ?? el.S.Name;
    private static string TextOf(StructureElement el) => string.Concat(el.GetMarkedContent(true).Select(i => i.Text)).Trim();

    [Fact]
    public void FootnotesOneToALineAreNoTable()
    {
        using var doc = Tag(Build(Page));
        var elements = doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).ToList();
        Assert.DoesNotContain(elements, e => TagOf(e) == "Table");
        // Each note is a block of its own, its number opening it.
        var notes = elements.Where(e => TagOf(e) is "Note" or "P" && TextOf(e).Contains("https://", System.StringComparison.Ordinal)).ToList();
        Assert.Equal(3, notes.Count);
        Assert.StartsWith("5", TextOf(notes[0]), System.StringComparison.Ordinal);
    }
}
