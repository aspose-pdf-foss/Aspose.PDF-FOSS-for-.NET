using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests;

public class FormFieldTypesTests
{
    [Fact]
    public void CheckboxField_Checked()
    {
        var data = PdfBuilder.BuildWithCheckboxField(isChecked: true);
        using var doc = Document.Open(data);
        var field = doc.Form!.Fields[0];
        Assert.Equal(FieldType.CheckBox, field.Type);
        Assert.IsType<CheckboxField>(field);
        var cb = (CheckboxField)field;
        Assert.True(cb.IsChecked);
    }

    [Fact]
    public void CheckboxField_Unchecked()
    {
        var data = PdfBuilder.BuildWithCheckboxField(isChecked: false);
        using var doc = Document.Open(data);
        var cb = (CheckboxField)doc.Form!.Fields[0];
        Assert.False(cb.IsChecked);
    }

    [Fact]
    public void CheckboxField_PartialName()
    {
        var data = PdfBuilder.BuildWithCheckboxField();
        using var doc = Document.Open(data);
        Assert.Equal("agree", doc.Form!.Fields[0].PartialName);
    }

    [Fact]
    public void ChoiceField_IsCombo()
    {
        var data = PdfBuilder.BuildWithChoiceField(["Red", "Green", "Blue"], isCombo: true);
        using var doc = Document.Open(data);
        var field = doc.Form!.Fields[0];
        Assert.Equal(FieldType.ComboBox, field.Type);
        Assert.IsType<ComboBoxField>(field);
        var ch = (ComboBoxField)field;
        Assert.True(ch.IsCombo);
    }

    [Fact]
    public void ChoiceField_ListBox()
    {
        var data = PdfBuilder.BuildWithChoiceField(["A", "B", "C"], isCombo: false);
        using var doc = Document.Open(data);
        var ch = (ChoiceField)doc.Form!.Fields[0];
        Assert.False(ch.IsCombo);
    }

    [Fact]
    public void ChoiceField_SelectedValue()
    {
        var data = PdfBuilder.BuildWithChoiceField(["Red", "Green", "Blue"], selected: "Green");
        using var doc = Document.Open(data);
        Assert.Equal("Green", doc.Form!.Fields[0].Value);
    }

    [Fact]
    public void MultipleFields_AllFieldTypes()
    {
        var data = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(data);
        Assert.Equal(3, doc.Form!.Count);

        // Text field
        var text = doc.Form.Fields[0];
        Assert.Equal(FieldType.Text, text.Type);
        Assert.Equal("name", text.PartialName);
        Assert.Equal("Alice", text.Value);
        Assert.Equal("Enter your name", text.AlternateName);

        // Checkbox
        var cb = doc.Form.Fields[1];
        Assert.Equal(FieldType.CheckBox, cb.Type);
        Assert.Equal("agree", cb.PartialName);

        // Choice — /Ff bit 18 set in the helper PDF so the field is a combo.
        var ch = doc.Form.Fields[2];
        Assert.Equal(FieldType.ComboBox, ch.Type);
        Assert.Equal("color", ch.PartialName);
        Assert.Equal("Green", ch.Value);
    }

    [Fact]
    public void FindByName_TextBoxField()
    {
        var data = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(data);
        var field = doc.Form!.FindByName("name");
        Assert.NotNull(field);
        Assert.IsType<TextBoxField>(field);
        Assert.Equal("Alice", field!.Value);
    }

    [Fact]
    public void FindByName_CheckboxField()
    {
        var data = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(data);
        var field = doc.Form!.FindByName("agree");
        Assert.NotNull(field);
        Assert.IsType<CheckboxField>(field);
    }

    [Fact]
    public void FindByName_ChoiceField()
    {
        var data = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(data);
        var field = doc.Form!.FindByName("color");
        Assert.NotNull(field);
        Assert.IsType<ComboBoxField>(field);
    }

    [Fact]
    public void Field_IsReadOnly_Default_False()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        Assert.False(doc.Form!.Fields[0].IsReadOnly);
    }

    [Fact]
    public void Field_IsRequired_Default_False()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        Assert.False(doc.Form!.Fields[0].IsRequired);
    }

    [Fact]
    public void Field_Rect_HasValue()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var rect = doc.Form!.Fields[0].Rect;
        Assert.NotNull(rect);
        Assert.Equal(100, rect!.LLX);
        Assert.Equal(700, rect!.LLY);
    }

    [Fact]
    public void TextBoxField_Properties()
    {
        var data = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(data);
        var tb = (TextBoxField)doc.Form!.Fields[0];
        Assert.Equal(0, tb.MaxLen); // not set
        Assert.False(tb.IsMultiline);
        Assert.False(tb.IsPassword);
    }

    [Fact]
    public void Form_Enumeration()
    {
        var data = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(data);
        var count = 0;
        foreach (var field in doc.Form!.Fields)
        {
            Assert.NotNull(field.PartialName);
            count++;
        }
        Assert.Equal(3, count);
    }
}
