using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public class FormEditorTests
{
    [Fact]
    public void FillFields_TextFieldRoundTrip()
    {
        var input = PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        var result = editor.FillFields(input, new Dictionary<string, string>
        {
            ["Name"] = "Jane"
        });

        using var doc = Document.Open(result);
        var field = doc.Form!.FindByName("Name");
        Assert.NotNull(field);
        Assert.Equal("Jane", field!.Value);
    }

    [Fact]
    public void FlattenForm_RemovesAcroForm()
    {
        var input = PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        var result = editor.FlattenForm(input);

        using var doc = Document.Open(result);
        Assert.False(doc.HasForm);
    }

    [Fact]
    public void FillFields_MultipleFields()
    {
        var input = PdfBuilder.BuildWithMultipleFields();
        var editor = new FormEditor();
        var result = editor.FillFields(input, new Dictionary<string, string>
        {
            ["name"] = "Bob",
            ["color"] = "Blue",
        });

        using var doc = Document.Open(result);
        Assert.Equal("Bob", doc.Form!.FindByName("name")?.Value);
        Assert.Equal("Blue", doc.Form!.FindByName("color")?.Value);
    }

    [Fact]
    public void FlattenForm_NoFormReturnsInput()
    {
        var input = PdfBuilder.BuildMinimal();
        var editor = new FormEditor();
        var result = editor.FlattenForm(input);

        // Should return the same bytes (no form to flatten)
        Assert.Equal(input.Length, result.Length);
    }
}
