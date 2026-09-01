using System.Text;
using System.Xml;
using Aspose.Pdf.Forms;

namespace Aspose.Pdf.Facades;

public sealed partial class Form
{
    /// <summary>
    /// Export form field values to an XML stream.
    /// </summary>
    public void ExportXml(Stream outputXmlStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");

        if (_doc.Form.IsXfa)
        {
            ExportXmlXfa(outputXmlStream);
        }
        else
        {
            ExportXmlAcroForm(outputXmlStream);
        }

        // Reset stream position to beginning for caller convenience (API compatibility)
        if (outputXmlStream.CanSeek)
            outputXmlStream.Position = 0;
    }

    /// <summary>
    /// Export form field values as FDF to a stream.
    /// </summary>
    public void ExportFdf(Stream outputFdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (_doc.Form.IsXfa)
        {
            ExportFdfXfa(outputFdfStream);
            if (outputFdfStream.CanSeek) outputFdfStream.Position = 0;
            return;
        }
        var bytes = _doc.Form.ExportFdf();
        outputFdfStream.Write(bytes, 0, bytes.Length);
        if (outputFdfStream.CanSeek)
            outputFdfStream.Position = 0;
    }

    /// <summary>
    /// Export form field values as XFDF to a stream.
    /// </summary>
    public void ExportXfdf(Stream outputXfdfStream)
    {
        if (_doc is null) throw new InvalidOperationException("No document bound.");
        if (_doc.Form.IsXfa)
        {
            ExportXfdfXfa(outputXfdfStream);
            if (outputXfdfStream.CanSeek) outputXfdfStream.Position = 0;
            return;
        }
        var xml = _doc.Form.ExportXfdf();
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        outputXfdfStream.Write(bytes, 0, bytes.Length);
        if (outputXfdfStream.CanSeek)
            outputXfdfStream.Position = 0;
    }

    /// <summary>
    /// Exports the contents of all fields in the document into a JSON stream.
    /// Button-field values are not exported.
    /// </summary>
    /// <param name="outputJsonStream">The output JSON stream.</param>
    /// <param name="indented">Whether the JSON output should be indented.</param>
    public void ExportJson(Stream outputJsonStream, bool indented = true)
    {
        if (outputJsonStream is null)
            throw new ArgumentNullException(nameof(outputJsonStream));
        if (_doc is null)
            throw new InvalidOperationException("No document bound.");

        var fields = FormJsonSerializer.BuildFieldData(_doc);
        var json = System.Text.Json.JsonSerializer.Serialize(fields, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        outputJsonStream.Write(bytes, 0, bytes.Length);
        if (outputJsonStream.CanSeek)
            outputJsonStream.Position = 0;
    }

    private void ExportXmlAcroForm(Stream output)
    {
        var form = _doc!.Form;
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
        };

        using var writer = XmlWriter.Create(output, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("fields");

        var fieldsArray = form.Fields;
        for (int i = 0; i < fieldsArray.Length; i++)
        {
            var field = fieldsArray[i];
            writer.WriteStartElement("field");
            writer.WriteAttributeString("name", field.FullName ?? $"field_{i + 1}");
            writer.WriteStartElement("value");
            writer.WriteString(field.Value ?? "");
            writer.WriteEndElement(); // </value>
            writer.WriteEndElement(); // </field>
        }

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }
}
