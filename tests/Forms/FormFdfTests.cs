using System.Text;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class FormFdfTests
{
    [Fact]
    public void ExportFdf_ProducesValidFdf()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        var fdfBytes = doc.Form!.ExportFdf();
        var fdf = Encoding.Latin1.GetString(fdfBytes);

        Assert.Contains("%FDF-1.2", fdf);
        Assert.Contains("/T (Name)", fdf);
        Assert.Contains("/V (John)", fdf);
        Assert.Contains("%%EOF", fdf);
    }

    [Fact]
    public void ImportFdf_SetsFieldValues()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        var fdf = "%FDF-1.2\n1 0 obj\n<< /FDF << /Fields [\n  << /T (Name) /V (Jane) >>\n] >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
        doc.Form!.ImportFdf(Encoding.Latin1.GetBytes(fdf));

        var field = doc.Form.FindByName("Name");
        Assert.NotNull(field);
        Assert.Equal("Jane", field!.Value);
    }

    [Fact]
    public void Fdf_RoundTrip()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        // Verify original value
        Assert.Equal("John", doc.Form!.FindByName("Name")!.Value);

        // Change value
        doc.Form.FindByName("Name")!.Value = "Updated";

        // Export
        var fdfBytes = doc.Form.ExportFdf();

        // Reset value
        doc.Form.FindByName("Name")!.Value = "Reset";
        Assert.Equal("Reset", doc.Form.FindByName("Name")!.Value);

        // Import back
        doc.Form.ImportFdf(fdfBytes);
        Assert.Equal("Updated", doc.Form.FindByName("Name")!.Value);
    }

    [Fact]
    public void ExportXfdf_ProducesValidXml()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        var xfdf = doc.Form!.ExportXfdf();

        Assert.Contains("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", xfdf);
        Assert.Contains("http://ns.adobe.com/xfdf/", xfdf);
        Assert.Contains("name=\"Name\"", xfdf);
        Assert.Contains(">John</", xfdf); // value element contains "John"
    }

    [Fact]
    public void ImportXfdf_SetsFieldValues()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        var xfdf = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                   "<xfdf xmlns=\"http://ns.adobe.com/xfdf/\">\n" +
                   "  <fields>\n" +
                   "    <field name=\"Name\"><value>FromXfdf</value></field>\n" +
                   "  </fields>\n" +
                   "</xfdf>";

        doc.Form!.ImportXfdf(xfdf);

        var field = doc.Form.FindByName("Name");
        Assert.NotNull(field);
        Assert.Equal("FromXfdf", field!.Value);
    }

    [Fact]
    public void Xfdf_RoundTrip()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        doc.Form!.FindByName("Name")!.Value = "XfdfTest";

        var xfdf = doc.Form.ExportXfdf();

        // Reset
        doc.Form.FindByName("Name")!.Value = "Reset";
        Assert.Equal("Reset", doc.Form.FindByName("Name")!.Value);

        // Import back
        doc.Form.ImportXfdf(xfdf);
        Assert.Equal("XfdfTest", doc.Form.FindByName("Name")!.Value);
    }

    [Fact]
    public void ImportFdf_IgnoresUnknownFields()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);

        var fdf = "%FDF-1.2\n1 0 obj\n<< /FDF << /Fields [\n  << /T (NonExistent) /V (value) >>\n] >> >>\nendobj\ntrailer\n<< /Root 1 0 R >>\n%%EOF\n";
        doc.Form!.ImportFdf(Encoding.Latin1.GetBytes(fdf));

        // Original value should be unchanged
        Assert.Equal("John", doc.Form.FindByName("Name")!.Value);
    }

    [Fact]
    public void ExportFdf_EscapesParentheses()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        doc.Form!.FindByName("Name")!.Value = "John (Jr)";

        var fdfBytes = doc.Form.ExportFdf();
        var fdf = Encoding.Latin1.GetString(fdfBytes);

        Assert.Contains("/V (John \\(Jr\\))", fdf);
    }
}
