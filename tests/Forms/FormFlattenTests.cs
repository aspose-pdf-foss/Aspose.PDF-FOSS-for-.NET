using Aspose.Pdf.Tests.Helpers;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class FormFlattenTests
{
    [Fact]
    public void Flatten_RemovesAcroForm()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        Assert.True(doc.HasForm);

        doc.Form!.Flatten(doc);

        // After save/reload, form should be gone
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.False(doc2.HasForm);
    }

    [Fact]
    public void Flatten_RemovesWidgetAnnotations()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        Assert.True(doc.Pages[1].Annotations.Count > 0);

        doc.Form!.Flatten(doc);

        // Widget annotations should be removed
        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Empty(doc2.Pages[1].Annotations);
    }

    [Fact]
    public void Flatten_MultipleFields()
    {
        var pdf = PdfBuilder.BuildWithMultipleFields();
        using var doc = Document.Open(pdf);
        Assert.Equal(3, doc.Form!.Count);

        doc.Form!.Flatten(doc);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.False(doc2.HasForm);
        Assert.Empty(doc2.Pages[1].Annotations);
    }

    [Fact]
    public void Flatten_PreservesPageCount()
    {
        var pdf = PdfBuilder.BuildWithFormField();
        using var doc = Document.Open(pdf);
        doc.Form!.Flatten(doc);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.Equal(1, doc2.PageCount);
    }

    [Fact]
    public void Flatten_CheckboxField()
    {
        var pdf = PdfBuilder.BuildWithCheckboxField(isChecked: true);
        using var doc = Document.Open(pdf);
        doc.Form!.Flatten(doc);

        var saved = doc.ToArray();
        using var doc2 = Document.Open(saved);
        Assert.False(doc2.HasForm);
    }
}
