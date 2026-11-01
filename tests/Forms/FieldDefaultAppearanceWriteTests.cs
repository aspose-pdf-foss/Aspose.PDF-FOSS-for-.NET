using System.IO;
using System.Text;
using Aspose.Pdf;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Forms;

public class FieldDefaultAppearanceWriteTests
{
    private static Document Saved(Document doc)
    {
        var saved = new MemoryStream();
        doc.Save(saved);
        saved.Position = 0;
        return new Document(saved);
    }

    /// <summary>The content of a field's normal appearance.</summary>
    private static string NormalAppearance(Field field)
    {
        var ap = field.Reader.ResolveDict(field.Dict.Get("AP"));
        var normal = field.Reader.ResolveStream(ap?.Get("N"));
        return normal is null ? "" : Encoding.ASCII.GetString(field.Reader.DecodeStream(normal));
    }

    private static PdfDictionary? AppearanceFont(Field field, string name)
    {
        var normal = field.Reader.ResolveStream(field.Reader.ResolveDict(field.Dict.Get("AP"))?.Get("N"));
        var fonts = normal is null ? null : field.Reader.ResolveDict(field.Reader.ResolveDict(normal.Dict.Get("Resources"))?.Get("Font"));
        return fonts is null ? null : field.Reader.ResolveDict(fonts.Get(name));
    }

    private static Document FormWithTextField(out TextBoxField field)
    {
        var doc = new Document();
        doc.Pages.Add();
        field = new TextBoxField(doc.Pages[1], new Rectangle(100, 700, 300, 720)) { PartialName = "name", Value = "Hello" };
        doc.Form.Add(field, 1);
        return doc;
    }

    [Fact]
    public void AnAppearanceAssignedThroughTheWidgetTheFormHandsOutRedrawsTheField()
    {
        using var doc = FormWithTextField(out _);
        WidgetAnnotation widget = doc.Form["name"];
        widget.DefaultAppearance = new DefaultAppearance("Courier", 10, System.Drawing.Color.Black);
        using var back = Saved(doc);

        var field = (Field)back.Form["name"];
        Assert.Contains("/Courier 10 Tf", NormalAppearance(field));
    }

    [Fact]
    public void AColourSetOnTheAppearanceOfAFieldMadeInCodeIsWritten_BeforeItIsAddedOrAfter()
    {
        var doc = new Document();
        doc.Pages.Add();
        var before = new TextBoxField(doc.Pages[1], new Rectangle(100, 700, 300, 720)) { PartialName = "before", Value = "red" };
        before.DefaultAppearance.TextColor = System.Drawing.Color.Red;
        doc.Form.Add(before, 1);
        var after = new TextBoxField(doc.Pages[1], new Rectangle(100, 600, 300, 620)) { PartialName = "after", Value = "blue" };
        doc.Form.Add(after, 1);
        after.DefaultAppearance.TextColor = System.Drawing.Color.Blue;
        after.DefaultAppearance.FontSize = 14;
        using var back = Saved(doc);

        var red = (Field)back.Form["before"];
        var blue = (Field)back.Form["after"];
        Assert.Equal(System.Drawing.Color.Red.ToArgb(), red.DefaultAppearance.TextColor.ToArgb());
        Assert.Contains("1 0 0 rg", NormalAppearance(red).Replace(".000", ""));
        Assert.Equal(System.Drawing.Color.Blue.ToArgb(), blue.DefaultAppearance.TextColor.ToArgb());
        Assert.Equal(14, blue.DefaultAppearance.FontSize);
        Assert.Contains("14 Tf", NormalAppearance(blue));
        doc.Dispose();
    }

    [Fact]
    public void AFieldGivenAnEmbeddedFaceIsDrawnInThatFace()
    {
        var doc = new Document();
        doc.Pages.Add();
        var face = FontRepository.OpenFont(new MemoryStream(PdfBuilder.BuildMinimalTrueTypeFont()), FontTypes.TTF);
        var field = new TextBoxField(doc.Pages[1], new Rectangle(100, 700, 300, 720)) { PartialName = "face" };
        field.DefaultAppearance = new DefaultAppearance(face, 10, System.Drawing.Color.Black);
        field.Value = "ABC";
        doc.Form.Add(field, 1);
        using var back = Saved(doc);

        var saved = (Field)back.Form["face"];
        var resource = saved.DefaultAppearance.FontName;
        Assert.Equal("Type0", AppearanceFont(saved, resource)?.GetName("Subtype"));
        doc.Dispose();
    }

    [Fact]
    public void ARichFieldHeldInItsRichValueAloneIsDrawnByFlatten()
    {
        var doc = new Document();
        doc.Pages.Add();
        var field = new RichTextBoxField(doc.Pages[1], new Rectangle(100, 600, 400, 700)) { PartialName = "notes" };
        doc.Form.Add(field, 1);
        field.Dict.Set("RV", new PdfString(Encoding.ASCII.GetBytes("<body xmlns=\"http://www.w3.org/1999/xhtml\"><p>Instructions for use</p></body>")));
        field.Dict.Remove("V");
        field.Dict.Remove("AP");
        using var reopened = Saved(doc);
        reopened.Flatten();
        using var flat = Saved(reopened);

        var absorber = new TextAbsorber();
        flat.Pages[1].Accept(absorber);
        Assert.Contains("Instructions for use", absorber.Text);
        doc.Dispose();
    }
}
