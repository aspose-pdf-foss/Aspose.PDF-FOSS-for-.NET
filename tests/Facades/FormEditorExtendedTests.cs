using Aspose.Pdf.Facades;
using Aspose.Pdf.Forms;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

public sealed class FormEditorExtendedTests
{
    [Fact]
    public void GetFieldNames_ReturnsAllFields()
    {
        var pdf = Helpers.PdfBuilder.BuildWithMultipleFields();
        var editor = new FormEditor();
        var names = editor.GetFieldNames(pdf);
        Assert.Contains("name", names);
        Assert.Contains("agree", names);
        Assert.Contains("color", names);
    }

    [Fact]
    public void GetFieldValue_ReturnsExistingValue()
    {
        var pdf = Helpers.PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        Assert.Equal("John", editor.GetFieldValue(pdf, "Name"));
    }

    [Fact]
    public void GetFieldValue_UnknownField_ReturnsNull()
    {
        var pdf = Helpers.PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        Assert.Null(editor.GetFieldValue(pdf, "NonExistent"));
    }

    [Fact]
    public void GetFieldType_ReturnsCorrectType()
    {
        var pdf = Helpers.PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        Assert.Equal(Aspose.Pdf.Forms.FieldType.Text, editor.GetFieldType(pdf, "Name"));
    }

    [Fact]
    public void HasForm_TrueForFormDoc()
    {
        var pdf = Helpers.PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        Assert.True(editor.HasForm(pdf));
    }

    [Fact]
    public void HasForm_FalseForMinimalPdf()
    {
        var pdf = Helpers.PdfBuilder.BuildMinimal();
        var editor = new FormEditor();
        Assert.False(editor.HasForm(pdf));
    }

    [Fact]
    public void GetFieldCount()
    {
        var pdf = Helpers.PdfBuilder.BuildWithMultipleFields();
        var editor = new FormEditor();
        Assert.Equal(3, editor.GetFieldCount(pdf));
    }

    [Fact]
    public void FillField_Single()
    {
        var pdf = Helpers.PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();
        var result = editor.FillField(pdf, "Name", "Jane");
        Assert.Equal("Jane", editor.GetFieldValue(result, "Name"));
    }

    [Fact]
    public void ExportFormData_ReturnsAllValues()
    {
        var pdf = Helpers.PdfBuilder.BuildWithMultipleFields();
        var editor = new FormEditor();
        var data = editor.ExportFormData(pdf);
        Assert.Equal("Alice", data["name"]);
        Assert.Equal("Green", data["color"]);
    }

    [Fact]
    public void ImportFormData_RoundTrip()
    {
        var pdf = Helpers.PdfBuilder.BuildWithFormField();
        var editor = new FormEditor();

        var data = new Dictionary<string, string> { ["Name"] = "Bob" };
        var result = editor.ImportFormData(pdf, data);
        Assert.Equal("Bob", editor.GetFieldValue(result, "Name"));
    }
}
