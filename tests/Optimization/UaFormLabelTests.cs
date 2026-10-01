using System.IO;
using System.Linq;
using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Xunit;

namespace Aspose.Pdf.Tests.Optimization;

public class UaFormLabelTests
{
    /// <summary>A form line "Name: [text field]", and a line of two check boxes each followed by
    /// its option label: "[x] Yes [x] No". The first field already names itself.</summary>
    private static byte[] BuildForm()
    {
        const string content =
            "BT /F1 12 Tf 72 700 Td (Name:) Tj ET\n" +
            "BT /F1 12 Tf 72 700 Td ( ) Tj ET\n" +
            "BT /F1 12 Tf 90 650 Td (Yes) Tj ET\n" +
            "BT /F1 12 Tf 150 650 Td (No) Tj ET\n" +
            "BT /F1 12 Tf 72 600 Td (Phone:) Tj ET\n";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [6 0 R 7 0 R 8 0 R 9 0 R] >> >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Annots [6 0 R 7 0 R 8 0 R 9 0 R] /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (f1) /Rect [115 696 300 714] /P 3 0 R >>",
            "<< /Type /Annot /Subtype /Widget /FT /Btn /T (c1) /Rect [74 648 86 660] /P 3 0 R >>",
            "<< /Type /Annot /Subtype /Widget /FT /Btn /T (c2) /Rect [134 648 146 660] /P 3 0 R >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (f2) /TU (Daytime phone) /Rect [115 596 300 614] /P 3 0 R >>",
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
        Write($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static Document Converted()
    {
        var doc = new Document(new MemoryStream(BuildForm()));
        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None));
        return doc;
    }

    private static string? Tooltip(Document doc, string name) =>
        doc.Form.Fields.Cast<Field>().Single(f => f.PartialName == name).AlternateName;

    [Fact]
    public void TextField_TakesTheLabelBeforeIt()
    {
        using var doc = Converted();
        Assert.Equal("Name", Tooltip(doc, "f1"));
    }

    [Fact]
    public void CheckBoxes_TakeTheLabelAfterThem_NotTheNextOnes()
    {
        using var doc = Converted();
        Assert.Equal("Yes", Tooltip(doc, "c1"));
        Assert.Equal("No", Tooltip(doc, "c2"));
    }

    [Fact]
    public void AnExistingTooltip_IsKept()
    {
        using var doc = Converted();
        Assert.Equal("Daytime phone", Tooltip(doc, "f2"));
    }
}
