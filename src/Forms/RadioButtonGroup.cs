using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// A single option within a radio button group.
/// </summary>
public sealed class RadioButtonOption
{
    /// <summary>Export value — the string written to /V when this option is selected.</summary>
    public string Value { get; }

    /// <summary>Whether this option is currently selected.</summary>
    public bool IsSelected { get; }

    internal RadioButtonOption(string value, bool isSelected)
    {
        Value = value;
        IsSelected = isSelected;
    }
}

/// <summary>
/// A logical radio-button group — a set of mutually exclusive options sharing
/// the same field name in the AcroForm hierarchy.
/// Obtain instances from <see cref="Form.RadioGroups"/>.
/// </summary>
public sealed class RadioButtonGroup
{
    /// <summary>
    /// Partial name of the group (the last component of the dot-separated name).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Fully-qualified dot-separated field name, or null when the field has no name.
    /// </summary>
    public string? FullName { get; }

    /// <summary>
    /// The underlying RadioButtonField objects that make up this group.
    /// </summary>
    public IReadOnlyList<RadioButtonField> Fields { get; }

    internal RadioButtonGroup(IReadOnlyList<RadioButtonField> fields)
    {
        Fields = fields;
        var first = fields[0];
        FullName = first.FullName;
        Name = first.PartialName ?? first.FullName ?? "";
    }

    /// <summary>
    /// Currently selected option value, or null when nothing is selected.
    /// </summary>
    public string? SelectedValue
    {
        get
        {
            foreach (var f in Fields)
            {
                var v = f.Value;
                if (v is not null && v != "Off") return v;
            }
            return null;
        }
    }

    /// <summary>
    /// Whether any option is selected.
    /// </summary>
    public bool HasSelection => SelectedValue is not null;

    /// <summary>
    /// All available options in the group.
    /// Options are collected from the AP/N appearance state dictionaries of each Kid widget.
    /// </summary>
    public IReadOnlyList<RadioButtonOption> Options
    {
        get
        {
            var selected = SelectedValue;
            var values = CollectOptionValues();
            return values.Select(v => new RadioButtonOption(v, v == selected)).ToList();
        }
    }

    private List<string> CollectOptionValues()
    {
        var seen = new HashSet<string>();
        var result = new List<string>();

        void Add(string? v)
        {
            if (v is not null && v != "" && v != "Off" && seen.Add(v))
                result.Add(v);
        }

        foreach (var field in Fields)
        {
            // Mode 1: single field whose dict has /Kids pointing to widget annotations
            var kidsObj = field.Reader.Resolve(field.Dict.Get("Kids"));
            if (kidsObj is PdfArray kids)
            {
                foreach (var kidRef in kids)
                {
                    var kidDict = field.Reader.ResolveDict(kidRef);
                    if (kidDict is null) continue;

                    var apDict = field.Reader.ResolveDict(kidDict.Get("AP"));
                    if (apDict is null) continue;

                    var nObj = field.Reader.Resolve(apDict.Get("N"));
                    if (nObj is PdfDictionary nDict)
                    {
                        foreach (var key in nDict.Keys)
                            Add(key);
                    }
                }
                if (result.Count > 0) continue;
            }

            // Mode 2 / fallback: use field's selected or appearance-based value
            var apObj = field.Reader.ResolveDict(field.Dict.Get("AP"));
            if (apObj is not null)
            {
                var nStateObj = field.Reader.Resolve(apObj.Get("N"));
                if (nStateObj is PdfDictionary stateDict)
                {
                    foreach (var key in stateDict.Keys)
                        Add(key);
                }
            }
        }

        return result;
    }
}
