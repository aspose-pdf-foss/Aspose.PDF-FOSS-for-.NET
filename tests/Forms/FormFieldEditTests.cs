using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class FormFieldEditTests
{
    [Fact]
    public void SetTextFieldValue()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        var field = doc.Form!.FindByName("Name");
        Assert.NotNull(field);
        Assert.Equal("John", field!.Value);

        field.Value = "Jane";
        Assert.Equal("Jane", field.Value);
    }

    [Fact]
    public void SetTextFieldValue_Null()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        var field = doc.Form!.FindByName("Name")!;

        field.Value = null;
        Assert.Null(field.Value);
    }

    [Fact]
    public void SetCheckboxField_Check()
    {
        var pdf = PdfBuilder.BuildWithCheckboxField(isChecked: false);
        using var doc = Document.Open(pdf);
        var cb = doc.Form!.Fields[0] as CheckboxField;
        Assert.NotNull(cb);
        Assert.False(cb!.IsChecked);

        cb.IsChecked = true;
        Assert.True(cb.IsChecked);
        Assert.Equal("Yes", cb.Value);
    }

    [Fact]
    public void SetCheckboxField_Uncheck()
    {
        var pdf = PdfBuilder.BuildWithCheckboxField(isChecked: true);
        using var doc = Document.Open(pdf);
        var cb = doc.Form!.Fields[0] as CheckboxField;
        Assert.NotNull(cb);
        Assert.True(cb!.IsChecked);

        cb.IsChecked = false;
        Assert.False(cb.IsChecked);
        Assert.Equal("Off", cb.Value);
    }

    [Fact]
    public void SetChoiceFieldValue()
    {
        var pdf = PdfBuilder.BuildWithChoiceField(["Red", "Green", "Blue"], selected: "Green");
        using var doc = Document.Open(pdf);
        var field = doc.Form!.Fields[0];
        Assert.Equal("Green", field.Value);

        field.Value = "Blue";
        Assert.Equal("Blue", field.Value);
    }

    [Fact]
    public void SetMultipleFieldValues()
    {
        var pdf = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(pdf);
        var form = doc.Form!;

        var nameField = form.FindByName("name")!;
        var agreeField = form.FindByName("agree") as CheckboxField;
        var colorField = form.FindByName("color")!;

        nameField.Value = "Bob";
        agreeField!.IsChecked = false;
        colorField.Value = "Red";

        Assert.Equal("Bob", nameField.Value);
        Assert.False(agreeField.IsChecked);
        Assert.Equal("Red", colorField.Value);
    }

    [Fact]
    public void SetFieldValue_PersistsAfterSave()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        doc.Form!.FindByName("Name")!.Value = "Saved";

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal("Saved", doc2.Form!.FindByName("Name")!.Value);
    }

    [Fact]
    public void SetCheckbox_PersistsAfterSave()
    {
        var pdf = PdfBuilder.BuildWithCheckboxField(isChecked: false);
        using var doc = Document.Open(pdf);
        (doc.Form!.Fields[0] as CheckboxField)!.IsChecked = true;

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        var cb = doc2.Form!.Fields[0] as CheckboxField;
        Assert.True(cb!.IsChecked);
    }
}
