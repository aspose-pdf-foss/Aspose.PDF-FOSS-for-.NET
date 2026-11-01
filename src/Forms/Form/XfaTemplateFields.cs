using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    // The fields a dynamic XFA form keeps in its template alone, built for the template and data texts they
    // were read from: both texts are kept while their streams are unchanged, so a form is resolved once however
    // often it is listed, and again after its data changes.
    private string? _xfaListedTemplate;
    private string? _xfaListedData;
    private IReadOnlyList<Field> _xfaListedFields = Array.Empty<Field>();

    /// <summary>The engine that resolves the template's fields, over the template as the value reads hold it:
    /// the walks only read it, so the template is parsed once for both. Null when there is no template or it
    /// does not parse.</summary>
    private Xfa.XfaFormEngine? XfaTemplateEngine()
    {
        try { return Xfa.XfaFormEngine.TryCreate(GetXfaTemplateDocumentResolved()); }
        catch (System.Xml.XmlException) { return null; }
    }

    /// <summary>The fields of an XFA form whose interactive form lists none: one per field the template shows,
    /// named by its path, of the type of its control and holding the value the data binds to it. They stand
    /// for the template's fields and belong to no page; the form's own field list is left as it is.</summary>
    private IReadOnlyList<Field> XfaTemplateFields()
    {
        var reader = ResolvedReader;
        if (reader is null || !IsXfa) return Array.Empty<Field>();
        var template = GetXfaTemplateXmlResolved();
        var data = GetXfaDatasetsXml();
        if (ReferenceEquals(template, _xfaListedTemplate) && ReferenceEquals(data, _xfaListedData)) return _xfaListedFields;
        var listed = new List<Field>();
        try
        {
            if (XfaTemplateEngine() is { } engine)
                foreach (var flat in engine.BuildRenderedFields(GetXfaFieldValue))
                    listed.Add(Field.Create(FlatXfaFieldDict(flat), reader));
        }
        catch
        {
            // A template the engine cannot resolve lists nothing, as before.
            listed.Clear();
        }
        (_xfaListedTemplate, _xfaListedData, _xfaListedFields) = (template, data, listed);
        return listed;
    }
}
