using Aspose.Pdf.Core;

namespace Aspose.Pdf;

/// <summary>
/// Schema of a PDF Portfolio collection (PDF spec §7.11.5 Table 74).
/// Each entry maps a logical field key to a <see cref="CollectionField"/>.
/// </summary>
public class CollectionSchema
{
    private readonly PdfDictionary _dict;
    private Dictionary<string, CollectionField>? _fields;

    internal CollectionSchema(PdfDictionary dict) { _dict = dict; }

    private Dictionary<string, CollectionField> Fields
    {
        get
        {
            if (_fields is not null) return _fields;
            _fields = new Dictionary<string, CollectionField>(StringComparer.Ordinal);
            foreach (var key in _dict.Keys)
            {
                if (_dict.Get(key) is PdfDictionary fieldDict)
                    _fields[key] = new CollectionField(fieldDict);
            }
            return _fields;
        }
    }

    /// <summary>Whether the schema has a field with the given key.</summary>
    public bool HasName(string name) => Fields.ContainsKey(name);

    /// <summary>Lookup the field by key. Returns null if absent.</summary>
    public CollectionField? GetCollectionField(string name)
        => Fields.TryGetValue(name, out var f) ? f : null;

    /// <summary>All field objects.</summary>
    public ICollection<CollectionField> AllFields => Fields.Values;

    /// <summary>All field keys.</summary>
    public ICollection<string> AllNames => Fields.Keys;
}
