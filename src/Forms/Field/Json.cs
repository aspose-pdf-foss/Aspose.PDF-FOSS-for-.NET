using System.Collections;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public partial class Field
{
    /// <summary>Serialise this field to JSON (a single <see cref="FieldExportingData"/>
    /// object) via the supplied stream.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(Stream stream)
        => ExportToJson(stream, null);

    /// <summary>Serialise this field to a JSON file.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(string fileName)
        => ExportToJson(fileName, null);

    /// <summary>Serialise this field to JSON via the supplied stream.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(Stream stream, ExportFieldsToJsonOptions? options)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        var data = FieldJsonExporter.BuildField(this);
        FieldJsonExporter.Write(stream, data, options?.WriteIndented ?? false);
        return new[]
        {
            new FieldSerializationResult
            {
                FieldFullName = FullName ?? PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            },
        };
    }

    /// <summary>Serialise this field to a JSON file.</summary>
    public new IEnumerable<FieldSerializationResult> ExportToJson(string fileName, ExportFieldsToJsonOptions? options)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        return ExportToJson(fs, options);
    }

    /// <summary>Write the field's name, flags and value to a JSON stream. A
    /// dotted field name (an XFA path such as <c>form1[0].P1[0].Field[0]</c>)
    /// is emitted as nested <c>{ "Name", "ChildFields" }</c> objects, one per
    /// path segment, with the leaf carrying <c>Flags</c> and <c>Value</c>.</summary>
    public void ExportValueToJson(Stream outputJsonStream, bool indented)
    {
        if (outputJsonStream is null) throw new ArgumentNullException(nameof(outputJsonStream));
        var fullName = FullName ?? PartialName ?? string.Empty;
        var form = OwnerDocument?.Form;
        var value = (form is { IsXfa: true } ? form.GetXfaFieldValue(fullName) : Value) ?? Value ?? string.Empty;

        var segments = fullName.Length == 0 ? new[] { string.Empty } : fullName.Split('.');
        var node = new FieldValueNode { Name = segments[segments.Length - 1], Flags = (int)Flags, Value = value };
        for (var i = segments.Length - 2; i >= 0; i--)
            node = new FieldValueNode { Name = segments[i], ChildFields = new List<FieldValueNode> { node } };

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = indented,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node, options);
        outputJsonStream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Nested field node used when (de)serialising a field-value path
    /// to/from JSON. Property order matters: <c>Name, Flags, Value, ChildFields</c>.</summary>
    private sealed class FieldValueNode
    {
        public string? Name { get; set; }
        public int? Flags { get; set; }
        public string? Value { get; set; }
        public List<FieldValueNode>? ChildFields { get; set; }
    }

    /// <summary>Read this field's value from a JSON stream. Returns true on success.</summary>
    public bool ImportValueFromJson(Stream inputJsonStream)
        => ImportValueFromJson(inputJsonStream, FullName ?? PartialName ?? string.Empty);

    /// <summary>Read a named field's value from a JSON stream — a single field
    /// object (possibly nested via <c>ChildFields</c>) or an array of them — and
    /// apply the value of the entry whose dotted path matches
    /// <paramref name="fieldFullNameInJSON"/>. For an XFA-backed form the value
    /// is written into the XFA datasets as well as the AcroForm field. Returns
    /// true on success.</summary>
    public bool ImportValueFromJson(Stream inputJsonStream, string fieldFullNameInJSON)
    {
        if (inputJsonStream is null) return false;
        try
        {
            using var reader = new StreamReader(inputJsonStream, System.Text.Encoding.UTF8, leaveOpen: true);
            using var doc = System.Text.Json.JsonDocument.Parse(reader.ReadToEnd());
            var root = doc.RootElement;
            string? value = null;
            if (root.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var element in root.EnumerateArray())
                    if (TryFindValueByPath(element, string.Empty, fieldFullNameInJSON, out value))
                        break;
            }
            else
            {
                TryFindValueByPath(root, string.Empty, fieldFullNameInJSON, out value);
            }
            if (value is null) return false;
            ApplyImportedValue(value);
            return true;
        }
        catch
        {
            // parse failure → false
        }
        return false;
    }

    /// <summary>Search a field node (recursing through <c>ChildFields</c>) for a
    /// leaf whose accumulated dotted path equals <paramref name="target"/>.</summary>
    private static bool TryFindValueByPath(
        System.Text.Json.JsonElement element, string prefix, string target, out string? value)
    {
        value = null;
        if (element.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
        var name = element.TryGetProperty("Name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var full = prefix.Length == 0 ? name : prefix + "." + name;
        // Children are nested under "ChildFields" (field-level export) or "Fields"
        // (form-level export); recurse whichever is present.
        if ((element.TryGetProperty("ChildFields", out var kids) ||
             element.TryGetProperty("Fields", out kids)) &&
            kids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var kid in kids.EnumerateArray())
                if (TryFindValueByPath(kid, full, target, out value))
                    return true;
            return false;
        }
        if (full == target &&
            element.TryGetProperty("Value", out var v) &&
            v.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            value = v.GetString();
            return true;
        }
        return false;
    }

    private void ApplyImportedValue(string value)
    {
        Value = value;
        var form = OwnerDocument?.Form;
        if (form is { IsXfa: true })
            form.SetXfaFieldValue(FullName ?? string.Empty, value);
    }
}
