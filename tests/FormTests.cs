using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class FormTests
{
    [Fact]
    public void Document_WithNoForm_ReturnsNull()
    {
        var data = PdfBuilder.BuildMinimal();
        using var doc = Document.Open(data);
        Assert.False(doc.HasForm);
        Assert.Empty(doc.Form);
    }

    [Fact]
    public void Document_WithForm_ReturnsForm()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        Assert.True(doc.HasForm);
        Assert.NotNull(doc.Form);
        Assert.Single(doc.Form!);
    }

    [Fact]
    public void Field_Type_IsText()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var field = doc.Form!.Fields[0];
        Assert.Equal(FieldType.Text, field.Type);
    }

    [Fact]
    public void Field_PartialName()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var field = doc.Form!.Fields[0];
        Assert.Equal("Name", field.PartialName);
    }

    [Fact]
    public void Field_Value()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var field = doc.Form!.Fields[0];
        Assert.Equal("John", field.Value);
    }

    [Fact]
    public void TextBoxField_Type()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var field = doc.Form!.Fields[0];
        Assert.IsType<TextBoxField>(field);
    }

    [Fact]
    public void FindByName_ExistingField()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var field = doc.Form!.FindByName("Name");
        Assert.NotNull(field);
        Assert.Equal("John", field!.Value);
    }

    [Fact]
    public void FindByName_NonExistent_ReturnsNull()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        Assert.Null(doc.Form!.FindByName("NonExistent"));
    }
}
