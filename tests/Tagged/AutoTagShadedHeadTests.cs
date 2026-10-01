using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.LogicalStructure;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Tagged;

/// <summary>Auto-tagging of a table whose head row is shaded cell by cell - a black fill per cell, the fills meeting
/// where the body's column rules stand, no rule drawn between them - and whose body cells hold bulleted lines: the head
/// is a cell per fill, and a cell's bullets stand with their lines, no column of their own.</summary>
public class AutoTagShadedHeadTests
{
    /// <summary>A Letter page drawing <paramref name="content"/> with F1 Times-Roman, F2 Times-Bold and F3 Symbol.</summary>
    private static byte[] Build(string content)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [6 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Bold /Encoding /WinAnsiEncoding >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 7 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> >>",
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

    private static string Text(double x, double y, string text, string font = "F1")
        => System.FormattableString.Invariant($"BT /{font} 10 Tf {x} {y} Td ({text}) Tj ET\n");

    // A bullet of the Symbol face (its code 0267) at the line's start, the line's text an em and a half further on.
    private static string Bullet(double x, double y, string text)
        => System.FormattableString.Invariant($"BT /F3 10 Tf {x} {y} Td (\\267) Tj ET\n") + Text(x + 18, y, text);

    private static string Fill(double x, double y, double w, double h)
        => System.FormattableString.Invariant($"{x} {y} {w} {h} re f\n");

    // The table as a word processor draws it: the head's three cells filled black edge to edge (no rule between them),
    // its top rule drawn in a piece per cell, white bold text on them; the body's cells filled grey and ruled with thin dark fills - the column rules standing
    // where the head's fills meet - and two rows, each with a bulleted question or two beside its category.
    private static readonly string Table =
        "0 g\n" + Fill(72.48, 363.3, 94.02, 23.46) + Fill(166.5, 363.3, 198, 23.46) + Fill(364.5, 363.3, 157.02, 23.46)
        + Fill(72, 363.3, 0.48, 23.52) + Fill(521.52, 363.3, 0.48, 23.52)
        + Fill(72, 386.82, 94.5, 0.48) + Fill(166.5, 386.82, 198, 0.48) + Fill(364.5, 386.82, 157.5, 0.48)
        + "1 g\n" + Text(100, 369, "Category", "F2") + Text(224, 369, "Example Questions", "F2") + Text(410, 369, "Identified Gaps", "F2")
        + "0.8 g\n" + Fill(72.48, 274.32, 93.78, 88.44) + Fill(166.74, 274.32, 197.52, 88.44) + Fill(364.74, 274.32, 156.78, 88.44)
        + "0.4 g\n" + Fill(72, 362.82, 450, 0.48) + Fill(72, 300, 450, 0.48) + Fill(72, 274.32, 450, 0.48)
        + Fill(72, 274.32, 0.48, 88.5) + Fill(166.26, 274.32, 0.481, 88.5) + Fill(364.26, 274.32, 0.481, 88.5) + Fill(521.52, 274.32, 0.48, 88.5)
        + "0 g\n" + Text(78, 345, "Immediate", "F2") + Text(78, 334, "deployment", "F2")
        + Bullet(190, 345, "How should procurement") + Text(208, 333, "requirements be written?")
        + Bullet(190, 315, "How does a ZTA plan work with") + Text(208, 304, "TIC, FISMA, and other")
        + Bullet(388, 345, "Lack of a common") + Text(406, 333, "framework and vocabulary")
        + Bullet(388, 315, "Perception that ZTA") + Text(406, 304, "conflicts with existing")
        + Text(78, 285, "Systemic", "F2")
        + Bullet(190, 285, "How can vendor lock-in be")
        + Bullet(388, 285, "Too much reliance on");

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

    private static List<List<StructureElement>> Rows(Document doc) =>
        doc.TaggedContent.StructTreeRootElement.FindElements<StructureElement>(true).Where(e => TagOf(e) == "TR")
            .Select(tr => tr.ChildElements.OfType<StructureElement>().ToList()).ToList();

    [Fact]
    public void AHeadRowFilledCellByCellIsACellPerFill()
    {
        using var doc = Tag(Build(Table));
        var rows = Rows(doc);
        Assert.Equal(3, rows.Count);
        Assert.Equal(new[] { "Category", "Example Questions", "Identified Gaps" }, rows[0].Select(TextOf));
        Assert.All(rows[0], cell => Assert.Equal("TH", TagOf(cell)));
    }

    [Fact]
    public void ACellsBulletsStandWithTheirLinesNotInAColumnOfTheirOwn()
    {
        using var doc = Tag(Build(Table));
        var rows = Rows(doc);
        Assert.Equal(3, rows.Count);
        // Three cells a row: the category, the questions with their bullets, the gaps with theirs.
        Assert.All(rows.Skip(1), row => Assert.Equal(3, row.Count));
        Assert.DoesNotContain(rows.SelectMany(r => r), cell => TextOf(cell).Length <= 1);
        Assert.Contains("How should procurement", TextOf(rows[1][1]), System.StringComparison.Ordinal);
        Assert.Contains("Lack of a common", TextOf(rows[1][2]), System.StringComparison.Ordinal);
    }
}
