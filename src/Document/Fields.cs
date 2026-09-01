using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// Remove a form field by name from all pages and the AcroForm.
    /// Returns true if the field was found and removed.
    /// </summary>
    public bool RemoveFormField(string fieldName)
    {
        if (!HasForm) return false;

        // Collect ALL fields with this fully-qualified name (the PDF spec
        // permits duplicate-named siblings — every match is removed).
        var targets = new List<Forms.Field>();
        foreach (var f in Form.Fields)
        {
            if (string.Equals(f.FullName, fieldName, StringComparison.Ordinal))
                targets.Add(f);
        }
        if (targets.Count == 0) return false;

        var targetDicts = new HashSet<PdfDictionary>(targets.Select(t => t.Dict));

        // Helper: a dict is a "target" if it IS one of the matched field dicts,
        // or its /T equals fieldName, or any of its /Parent ancestors are targets.
        bool IsTargetField(PdfDictionary? dict)
        {
            if (dict is null) return false;
            if (targetDicts.Contains(dict)) return true;
            if (MatchesFieldName(dict, fieldName)) return true;
            // Walk Parent chain
            var cur = dict;
            for (int hop = 0; hop < 16; hop++)
            {
                var parent = _reader.ResolveDict(cur.Get("Parent"));
                if (parent is null) return false;
                if (targetDicts.Contains(parent)) return true;
                if (MatchesFieldName(parent, fieldName)) return true;
                cur = parent;
            }
            return false;
        }

        // Remove widget annotation from all pages
        foreach (var page in Pages)
        {
            var annots = _reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
            if (annots is null) continue;

            var remaining = new PdfArray();
            foreach (var annotRef in annots)
            {
                var annotDict = _reader.ResolveDict(annotRef);
                if (IsTargetField(annotDict))
                    continue;
                remaining.Add(annotRef);
            }

            if (remaining.Count < annots.Count)
            {
                if (remaining.Count > 0)
                    page.Dict.Set("Annots", remaining);
                else
                    page.Dict.Remove("Annots");
            }
        }

        // Remove from AcroForm/Fields array
        var acroForm = _reader.ResolveDict(Catalog.Get("AcroForm"));
        if (acroForm is not null)
        {
            var fields = _reader.Resolve(acroForm.Get("Fields")) as PdfArray;
            if (fields is not null)
            {
                var newFields = new PdfArray();
                foreach (var fRef in fields)
                {
                    var fDict = _reader.ResolveDict(fRef);
                    if (IsTargetField(fDict))
                        continue;
                    newFields.Add(fRef);
                }
                acroForm.Set("Fields", newFields);
            }
        }

        // Reset cached form
        _form = null;
        return true;
    }

    private bool MatchesFieldName(PdfDictionary dict, string fieldName)
    {
        var t = dict.Get("T");
        if (t is PdfString s) return s.ToText() == fieldName;
        if (t is PdfName n) return n.Value == fieldName;
        return false;
    }

    /// <summary>Walk each page's generator paragraph tree (including table cells and
    /// floating boxes), find any form fields placed there, and register them in the
    /// document's AcroForm so they persist as interactive fields. Radio groups are
    /// reached through their option fields' back-reference.</summary>
    private void RegisterGeneratedFormFields()
    {
        if (_generatedFormFieldsRegistered) return;
        _generatedFormFieldsRegistered = true;

        var already = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (_reader.ResolveDict(_reader.Catalog.Get("AcroForm")) is { } af
            && _reader.Resolve(af.Get("Fields")) is PdfArray existing)
            foreach (var item in existing)
                if (_reader.ResolveDict(item) is { } d) already.Add(d);

        var seenRadios = new HashSet<Forms.RadioButtonField>();
        for (var pi = 0; pi < Pages.Count; pi++)
        {
            var fields = new List<Forms.Field>();
            var radios = new List<Forms.RadioButtonField>();
            CollectGeneratorFormFields(Pages[pi + 1].Paragraphs, fields, radios);
            foreach (var f in fields)
                if (!already.Contains(f.Dict)) { Form.Add(f, pi + 1); already.Add(f.Dict); }
            foreach (var rb in radios)
                if (seenRadios.Add(rb) && !already.Contains(rb.Dict)) { Form.Add(rb, pi + 1); already.Add(rb.Dict); }
        }
    }

    private static void CollectGeneratorFormFields(IEnumerable<BaseParagraph> paragraphs,
        List<Forms.Field> fields, List<Forms.RadioButtonField> radios)
    {
        foreach (var p in paragraphs)
        {
            switch (p)
            {
                // RadioButtonOptionField is now a RadioButtonField, so its cases MUST precede
                // both the RadioButtonField and the general Field case: an option registers
                // its OWNER radio group (not itself) in the AcroForm.
                case Forms.RadioButtonOptionField { OwnerRadio: { } owner }: radios.Add(owner); break;
                case Forms.RadioButtonOptionField: break;
                case Forms.RadioButtonField rb: radios.Add(rb); break;
                case Forms.Field f: fields.Add(f); break;
                case Table t:
                    foreach (var row in t.Rows)
                        foreach (var cell in row.Cells)
                            CollectGeneratorFormFields(cell.Paragraphs, fields, radios);
                    break;
                case FloatingBox fb:
                    CollectGeneratorFormFields(fb.Paragraphs, fields, radios);
                    break;
            }
        }
    }
}
