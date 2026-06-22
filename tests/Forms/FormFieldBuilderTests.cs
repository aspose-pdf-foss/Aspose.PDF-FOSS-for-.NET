using Aspose.Pdf;
using Aspose.Pdf.Forms;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class FormFieldBuilderTests
{
    [Fact]
    public void AddTextField_CreatesFieldOnPage()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page, "name", new Rectangle(72, 700, 200, 720), "John");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        Assert.Equal("name", doc2.Form!.Fields[0].PartialName);
        Assert.Equal("John", doc2.Form!.Fields[0].Value);
        Assert.Equal(FieldType.Text, doc2.Form!.Fields[0].Type);
    }

    [Fact]
    public void AddTextField_WithoutValue_CreatesEmptyField()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page, "email", new Rectangle(72, 660, 200, 680));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        Assert.Equal("email", doc2.Form!.Fields[0].PartialName);
        Assert.Null(doc2.Form!.Fields[0].Value);
    }

    [Fact]
    public void AddCheckBox_CreatesCheckBoxField()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddCheckBox(page, "agree", new Rectangle(72, 620, 92, 640), true);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        Assert.Equal("agree", doc2.Form!.Fields[0].PartialName);
        Assert.Equal(FieldType.CheckBox, doc2.Form!.Fields[0].Type);
    }

    [Fact]
    public void AddCheckBox_Unchecked()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddCheckBox(page, "opt", new Rectangle(72, 580, 92, 600), false);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal("Off", doc2.Form!.Fields[0].Value);
    }

    [Fact]
    public void AddComboBox_CreatesChoiceField()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddComboBox(page, "country",
            new Rectangle(72, 540, 200, 560),
            ["USA", "Canada", "UK"],
            "Canada");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        Assert.Equal("country", doc2.Form!.Fields[0].PartialName);
        Assert.Equal(FieldType.ComboBox, doc2.Form!.Fields[0].Type);
        Assert.Equal("Canada", doc2.Form!.Fields[0].Value);
    }

    [Fact]
    public void MultipleFields_OnSamePage()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page, "first_name", new Rectangle(72, 700, 200, 720));
        builder.AddTextField(page, "last_name", new Rectangle(72, 660, 200, 680));
        builder.AddCheckBox(page, "agree", new Rectangle(72, 620, 92, 640));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(3, doc2.Form!.Count);
    }

    [Fact]
    public void AddTextField_FindByName()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page, "username", new Rectangle(72, 700, 200, 720), "admin");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var field = doc2.Form!.FindByName("username");
        Assert.NotNull(field);
        Assert.Equal("admin", field!.Value);
    }

    [Fact]
    public void AddFields_OnDifferentPages()
    {
        using var doc = Document.Create();
        var page1 = doc.Pages.Add();
        var page2 = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page1, "page1_field", new Rectangle(72, 700, 200, 720));
        builder.AddTextField(page2, "page2_field", new Rectangle(72, 700, 200, 720));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(2, doc2.Form!.Count);
    }

    [Fact]
    public void FieldHasRect()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page, "test", new Rectangle(72, 700, 200, 720));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var rect = doc2.Form!.Fields[0].Rect;
        Assert.NotNull(rect);
        Assert.Equal(72, rect!.LLX, 1);
        Assert.Equal(700, rect.LLY, 1);
    }

    [Fact]
    public void AddRadioButton_CreatesRadioButtonField()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        var rects = new[]
        {
            new Rectangle(72, 700, 92, 720),
            new Rectangle(72, 670, 92, 690),
            new Rectangle(72, 640, 92, 660),
        };
        var values = new[] { "Option1", "Option2", "Option3" };

        builder.AddRadioButton(page, "radio_group", rects, values, 1);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        var field = doc2.Form!.Fields[0];
        Assert.Equal("radio_group", field.PartialName);
        Assert.Equal(FieldType.RadioButton, field.Type);
        Assert.Equal("Option2", field.Value);
    }

    [Fact]
    public void AddRadioButton_DefaultSelectsFirstOption()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        var rects = new[]
        {
            new Rectangle(72, 700, 92, 720),
            new Rectangle(72, 670, 92, 690),
        };
        var values = new[] { "Yes", "No" };

        builder.AddRadioButton(page, "choice", rects, values);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal("Yes", doc2.Form!.Fields[0].Value);
    }

    [Fact]
    public void AddRadioButton_FindByName()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        var rects = new[]
        {
            new Rectangle(72, 700, 92, 720),
            new Rectangle(72, 670, 92, 690),
        };
        var values = new[] { "A", "B" };

        builder.AddRadioButton(page, "myRadio", rects, values, 0);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var field = doc2.Form!.FindByName("myRadio");
        Assert.NotNull(field);
        Assert.Equal(FieldType.RadioButton, field!.Type);
    }

    [Fact]
    public void AddListBox_CreatesChoiceFieldWithoutCombo()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddListBox(page, "colors",
            new Rectangle(72, 500, 200, 600),
            ["Red", "Green", "Blue"],
            "Green");

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        var field = doc2.Form!.Fields[0];
        Assert.Equal("colors", field.PartialName);
        Assert.Equal(FieldType.ListBox, field.Type);
        Assert.Equal("Green", field.Value);

        // Verify it's a list box (no Combo flag)
        var choiceField = field as ChoiceField;
        Assert.NotNull(choiceField);
        Assert.False(choiceField!.IsCombo);
    }

    [Fact]
    public void AddListBox_HasOptions()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddListBox(page, "items",
            new Rectangle(72, 400, 200, 500),
            ["Apple", "Banana", "Cherry"]);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var field = doc2.Form!.Fields[0] as ChoiceField;
        Assert.NotNull(field);
        Assert.Equal(3, field!.Options.Count);
        Assert.Equal("Apple", field.Options[1].ExportValue);
        Assert.Equal("Banana", field.Options[2].ExportValue);
        Assert.Equal("Cherry", field.Options[3].ExportValue);
    }

    [Fact]
    public void AddSignatureField_CreatesSignatureField()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddSignatureField(page, "sig", new Rectangle(72, 300, 272, 370));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.True(doc2.HasForm);
        Assert.Single(doc2.Form!);
        var field = doc2.Form!.Fields[0];
        Assert.Equal("sig", field.PartialName);
        Assert.Equal(FieldType.Signature, field.Type);
        // No value initially â€” it's a placeholder
        Assert.Null(field.Value);
    }

    [Fact]
    public void AddSignatureField_HasRect()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddSignatureField(page, "signature", new Rectangle(100, 200, 300, 270));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        var rect = doc2.Form!.Fields[0].Rect;
        Assert.NotNull(rect);
        Assert.Equal(100, rect!.LLX, 1);
        Assert.Equal(200, rect.LLY, 1);
        Assert.Equal(300, rect.URX, 1);
        Assert.Equal(270, rect.URY, 1);
    }

    [Fact]
    public void AllFieldTypes_OnSamePage()
    {
        using var doc = Document.Create();
        var page = doc.Pages.Add();
        var builder = new FormFieldBuilder(doc);

        builder.AddTextField(page, "name", new Rectangle(72, 700, 200, 720), "Test");
        builder.AddCheckBox(page, "agree", new Rectangle(72, 660, 92, 680));
        builder.AddRadioButton(page, "radio",
            [new Rectangle(72, 620, 92, 640), new Rectangle(72, 590, 92, 610)],
            ["A", "B"]);
        builder.AddListBox(page, "list", new Rectangle(72, 500, 200, 580),
            ["X", "Y", "Z"]);
        builder.AddSignatureField(page, "sig", new Rectangle(72, 400, 200, 470));

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);

        Assert.Equal(5, doc2.Form!.Count);
        Assert.Equal(FieldType.Text, doc2.Form.Fields[0].Type);
        Assert.Equal(FieldType.CheckBox, doc2.Form.Fields[1].Type);
        Assert.Equal(FieldType.RadioButton, doc2.Form.Fields[2].Type);
        Assert.Equal(FieldType.ListBox, doc2.Form.Fields[3].Type);
        Assert.Equal(FieldType.Signature, doc2.Form.Fields[4].Type);
    }
}
