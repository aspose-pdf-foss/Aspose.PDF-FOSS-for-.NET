using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class ChoiceFieldOptionsTests
{
    [Fact]
    public void Options_ParsesStringOptions()
    {
        var data = PdfBuilder.BuildWithChoiceField(["Red", "Green", "Blue"]);
        using var doc = Document.Open(data);
        var field = (ChoiceField)doc.Form!.Fields[0];

        var options = field.Options;
        Assert.Equal(3, options.Count);
        Assert.Equal("Red", options[1].ExportValue);
        Assert.Equal("Red", options[1].DisplayValue);
        Assert.Equal("Green", options[2].ExportValue);
        Assert.Equal("Blue", options[3].ExportValue);
    }

    [Fact]
    public void Options_EmptyList()
    {
        var data = PdfBuilder.BuildWithChoiceField([]);
        using var doc = Document.Open(data);
        var field = (ChoiceField)doc.Form!.Fields[0];

        Assert.Empty(field.Options);
    }

    [Fact]
    public void IsCombo_True()
    {
        var data = PdfBuilder.BuildWithChoiceField(["A", "B"], isCombo: true);
        using var doc = Document.Open(data);
        var field = (ChoiceField)doc.Form!.Fields[0];
        Assert.True(field.IsCombo);
    }

    [Fact]
    public void IsCombo_False_ListBox()
    {
        var data = PdfBuilder.BuildWithChoiceField(["A", "B"], isCombo: false);
        using var doc = Document.Open(data);
        var field = (ChoiceField)doc.Form!.Fields[0];
        Assert.False(field.IsCombo);
    }

    [Fact]
    public void IsSorted_Default_False()
    {
        var data = PdfBuilder.BuildWithChoiceField(["C", "A", "B"]);
        using var doc = Document.Open(data);
        var field = (ChoiceField)doc.Form!.Fields[0];
        Assert.False(field.IsSorted);
    }

    [Fact]
    public void SelectedValue_FromField()
    {
        var data = PdfBuilder.BuildWithChoiceField(["Red", "Green", "Blue"], selected: "Blue");
        using var doc = Document.Open(data);
        var field = (ChoiceField)doc.Form!.Fields[0];
        Assert.Equal("Blue", field.Value);
    }

    [Fact]
    public void MultipleFields_ChoiceOptions()
    {
        var data = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(data);
        var field = doc.Form!.FindByName("color");
        Assert.NotNull(field);
        Assert.IsType<ComboBoxField>(field);

        var choice = (ComboBoxField)field!;
        Assert.Equal(3, choice.Options.Count);
        Assert.Equal("Red", choice.Options[1].ExportValue);
        Assert.Equal("Green", choice.Options[2].ExportValue);
        Assert.Equal("Blue", choice.Options[3].ExportValue);
    }
}
