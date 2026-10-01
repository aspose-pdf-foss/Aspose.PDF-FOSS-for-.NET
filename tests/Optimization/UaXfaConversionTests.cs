using System.IO;
using Aspose.Pdf;
using Aspose.Pdf.Core;
using Xunit;

namespace Aspose.Pdf.Tests.Optimization;

public class UaXfaConversionTests
{
    /// <summary>A one-page document whose form is dynamic XFA (/NeedsRendering), with or
    /// without an interactive text field beside it.</summary>
    private static byte[] BuildXfaForm(bool withField)
    {
        const string xfa = "<xdp:xdp xmlns:xdp=\"http://ns.adobe.com/xdp/\"><template/></xdp:xdp>";
        const string content = "BT /F1 12 Tf 72 700 Td (Please wait...) Tj ET";
        var fields = withField ? "[8 0 R]" : "[]";
        var annots = withField ? "/Annots [8 0 R]" : "";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R /NeedsRendering true /AcroForm 6 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R {annots} /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Fields {fields} /XFA 7 0 R >>",
            $"<< /Length {xfa.Length} >>\nstream\n{xfa}\nendstream",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /TU (Your name) /Rect [72 600 272 620] /P 3 0 R >>",
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

    private static bool HasXfa(Document doc) =>
        doc.Reader.ResolveDict(doc.Reader.Catalog.Get("AcroForm"))?.Get("XFA") is not null;

    [Fact]
    public void DynamicXfaAlone_IsRefused_AndTheDocumentIsLeftAsItWas()
    {
        using var doc = new Document(new MemoryStream(BuildXfaForm(withField: false)));
        var log = new MemoryStream();

        var converted = doc.Convert(new PdfFormatConversionOptions(log, PdfFormat.PDF_UA_1, ConvertErrorAction.Delete));

        Assert.False(converted);
        Assert.True(HasXfa(doc));
        Assert.Contains("dynamic XFA form", System.Text.Encoding.UTF8.GetString(log.ToArray()));
    }

    [Fact]
    public void DynamicXfaBesideFormFields_LosesTheXfa_AndKeepsTheFields()
    {
        using var doc = new Document(new MemoryStream(BuildXfaForm(withField: true)));

        var converted = doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.Delete));

        Assert.True(converted);
        Assert.False(HasXfa(doc));
        Assert.Equal("name", Assert.Single(doc.Form.Fields).PartialName);
    }

    [Fact]
    public void DynamicXfaBesideFormFields_IsKept_WhenTheConversionDeletesNothing()
    {
        using var doc = new Document(new MemoryStream(BuildXfaForm(withField: true)));

        doc.Convert(new PdfFormatConversionOptions(new MemoryStream(), PdfFormat.PDF_UA_1, ConvertErrorAction.None));

        Assert.True(HasXfa(doc));
    }
}
