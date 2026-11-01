using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of footnotes whose numbers hang raised a tab before their text, out in the margin of the notes
/// and marked by the producer as layout artifacts: each number is its note's text, read before it.</summary>
public class AutoTagNoteLabelTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Times-Roman.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [4 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.3 841.9] /Contents 5 0 R /Resources << /Font << /F1 3 0 R >> >> >>",
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

    /// <summary>A string as the producer writes it: marked content of its own, a text object scaled to a thirtieth of a
    /// point, its size given in those units.</summary>
    private static string Show(string mark, double size, double x, double y, string text)
        => System.FormattableString.Invariant($"{mark} BDC BT /F1 {size} Tf 0.03 0 0 0.03 {x} {y} Tm [({text})] TJ ET EMC\n");

    // A note's number: 7.1 pt, raised 3.5 pt over its note's baseline at the notes' left edge, a layout artifact.
    private static string Number(double y, string number)
        => Show("/Artifact <</Type /Layout /BBox [85.05 0 89 10]>>", 236.11, 85.05, y, number);

    [Fact]
    public void ARaisedNumberHangingATabBeforeItsNoteIsTheNotesText()
    {
        var mcid = 0;
        string Content(double size, double x, double y, string text) => Show($"/P <</MCID {mcid++}>>", size, x, y, text);
        var body = new[]
        {
            "Die Bundesversammlung der Eidgenossenschaft, gestützt auf Artikel 123 Absatz 1 der Verfassung,",
            "nach Einsicht in die Botschaft des Bundesrates vom 21. Dezember 2005, beschliesst:",
            "Dieses Gesetz regelt die Verfolgung und Beurteilung der Straftaten nach Bundesrecht durch die",
            "Strafbehörden des Bundes und der Kantone. Die Verfahrensvorschriften anderer Bundesgesetze",
            "bleiben vorbehalten, und die Strafrechtspflege steht einzig den Behörden zu, die das Gesetz bestimmt.",
        };
        var content = string.Concat(body.Select((line, k) => Content(420, 85.05, 700 - 14.4 * k, line)))
                      + Content(366.67, 85.05, 84.6, "AS ") + Content(366.67, 101.85, 84.6, "2010") + Content(366.67, 123.84, 84.6, " 1881")
                      + Number(74.52, "1") + Content(366.67, 106.32, 71.01, "SR ") + Content(366.67, 122.52, 71.01, "101")
                      + Number(60.93, "2") + Content(366.67, 106.32, 57.42, "BBl ") + Content(366.67, 126.78, 57.42, "2006")
                      + Content(366.67, 148.77, 57.42, " 1085");
        using var doc = Tag(Build(content));
        var text = Text(doc.TaggedContent.StructTreeRootElement);

        Assert.Contains("1 SR 101", text.Replace("  ", " "));
        Assert.Contains("2 BBl 2006 1085", text.Replace("  ", " "));
    }
}
