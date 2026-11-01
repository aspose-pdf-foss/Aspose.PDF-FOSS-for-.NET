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

    [Fact]
    public void DeletingAFieldOfANewDocumentDropsItFromTheForm()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        var kept = new TextBoxField(page, new Rectangle(10, 10, 100, 30)) { PartialName = "Kept" };
        var dropped = new TextBoxField(page, new Rectangle(10, 40, 100, 60)) { PartialName = "Dropped" };
        doc.Form.Add(kept, 1);
        doc.Form.Add(dropped, 1);

        doc.Form.Delete(dropped);
        var left = doc.Form.Count;
        Assert.Equal(1, left);

        using var doc2 = Document.Open(doc.ToArray());
        var saved = doc2.Form!.Count;
        Assert.Equal(1, saved);
        Assert.NotNull(doc2.Form.FindByName("Kept"));
        Assert.Null(doc2.Form.FindByName("Dropped"));
    }

    [Fact]
    public void DeletingAFieldOfANewDocumentKeepsAFieldTheFormEditorAdded()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        var dropped = new TextBoxField(page, new Rectangle(10, 10, 100, 30)) { PartialName = "Dropped" };
        doc.Form.Add(dropped, 1);
        new Aspose.Pdf.Facades.FormEditor(doc).AddField(Aspose.Pdf.Facades.FieldType.Text, "Added", 1, 10, 40, 100, 60);

        doc.Form.Delete(dropped);
        var left = doc.Form.Count;
        Assert.Equal(1, left);

        using var doc2 = Document.Open(doc.ToArray());
        var saved = doc2.Form!.Count;
        Assert.Equal(1, saved);
        Assert.NotNull(doc2.Form.FindByName("Added"));
        Assert.Null(doc2.Form.FindByName("Dropped"));
    }

    [Fact]
    public void DeletingEveryRootFieldOfALoadedFormLeavesNoField()
    {
        // A radio group without a /Rect whose two option widgets point back to it, beside a text field.
        using var doc = Document.Open(BuildWithRadioGroupAndTextField());
        var roots = new List<Field>();
        foreach (Field field in doc.Form) roots.Add(field);
        Assert.Equal(2, roots.Count);

        // The radio group's options go with it.
        foreach (var field in roots) doc.Form.Delete(field);
        var left = doc.Form.Count;
        Assert.Equal(0, left);
        Assert.Empty(doc.Form.Fields);
    }

    private static byte[] BuildWithRadioGroupAndTextField()
    {
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R /AcroForm 5 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Annots [6 0 R 7 0 R 8 0 R] >>",
            "<< /FT /Btn /Ff 49152 /T (Choice) /Kids [6 0 R 7 0 R] >>",
            "<< /Fields [4 0 R 8 0 R] >>",
            "<< /Type /Annot /Subtype /Widget /Parent 4 0 R /Rect [10 10 30 30] /AS /Off >>",
            "<< /Type /Annot /Subtype /Widget /Parent 4 0 R /Rect [40 10 60 30] /AS /Off >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Name) /Rect [10 40 100 60] >>",
        ];
        var sb = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(sb.Length);
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = sb.Length;
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString());
    }
}
