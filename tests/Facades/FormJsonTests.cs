using System.IO;
using System.Text;
using Aspose.Pdf.Forms;
using Xunit;

namespace Aspose.Pdf.Tests.Facades;

/// <summary>
/// Form JSON goes through a generated serializer context (reflection is off in the trimmed WebAssembly build); it
/// must write exactly what the reflection serializer wrote and read it back.
/// </summary>
public class FormJsonTests
{
    private static Document FormDocument()
    {
        var document = new Document();
        var page = document.Pages.Add();
        var name = new TextBoxField(page, new Rectangle(100, 700, 300, 720)) { PartialName = "Name", Value = "Ada" };
        var city = new TextBoxField(page, new Rectangle(100, 650, 300, 670)) { PartialName = "City", Value = "London" };
        document.Form.Add(name);
        document.Form.Add(city);
        return document;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExportJson_WritesWhatTheReflectionSerializerWrote(bool indented)
    {
        var document = FormDocument();
        var form = new Aspose.Pdf.Facades.Form(document);
        using var output = new MemoryStream();
        form.ExportJson(output, indented);

        var expected = System.Text.Json.JsonSerializer.Serialize(
            Aspose.Pdf.Facades.FormJsonSerializer.BuildFieldData(document),
            new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            });
        Assert.Equal(expected, Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public void ImportJson_ReadsWhatExportJsonWrote()
    {
        var source = FormDocument();
        using var json = new MemoryStream();
        new Aspose.Pdf.Facades.Form(source).ExportJson(json);

        var target = FormDocument();
        ((TextBoxField)target.Form["Name"]).Value = "";
        ((TextBoxField)target.Form["City"]).Value = "";
        json.Position = 0;
        new Aspose.Pdf.Facades.Form(target).ImportJson(json);

        Assert.Equal("Ada", ((TextBoxField)target.Form["Name"]).Value);
        Assert.Equal("London", ((TextBoxField)target.Form["City"]).Value);
    }
}
