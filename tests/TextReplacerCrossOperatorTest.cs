using Aspose.Pdf;
using Aspose.Pdf.Text;
using Aspose.Pdf.Facades;
using Xunit;
using System.Text;

namespace Aspose.Pdf.Tests;

public class TextReplacerCrossOperatorTest
{
    private static byte[] BuildPdfWithSplitText(string part1, string part2)
    {
        var contentText = $"BT /F1 12 Tf 72 720 Td ({part1}) Tj ({part2}) Tj ET";
        var contentBytes = Encoding.Latin1.GetByteCount(contentText);
        var pdf = $"%PDF-1.4\n" +
            $"1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
            $"2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
            $"3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> >>\nendobj\n" +
            $"4 0 obj\n<< /Length {contentBytes} >>\nstream\n{contentText}\nendstream\nendobj\n";
        // add minimal xref
        var bytes = Encoding.Latin1.GetBytes(pdf);
        var xrefOff = bytes.Length;
        var full = pdf + $"xref\n0 5\n0000000000 65535 f \n0000000009 00000 n \n0000000058 00000 n \n0000000115 00000 n \n0000000266 00000 n \ntrailer\n<< /Size 5 /Root 1 0 R >>\nstartxref\n{xrefOff}\n%%EOF";
        return Encoding.Latin1.GetBytes(full);
    }

    [Fact]
    public void CrossOperator_SingleTj_ContainsMelatonin()
    {
        // "melatonin" in a single Tj - should work
        var pdfBytes = BuildPdfWithSplitText("hello melatonin world", "");
        var doc = Document.Open(pdfBytes);
        var ta = new TextAbsorber();
        ta.Visit(doc);
        Assert.Contains("melatonin", ta.Text);
    }

    [Fact]
    public void CrossOperator_SplitTj_TextAbsorberSeesMelatonin()
    {
        // "melatonin" split: "melaton" + "in" - TextAbsorber should still extract it
        var pdfBytes = BuildPdfWithSplitText("melaton", "in");
        var doc = Document.Open(pdfBytes);
        var ta = new TextAbsorber();
        ta.Visit(doc);
        Assert.Contains("melatonin", ta.Text);
    }
}
