using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed partial class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;
    public IEnumerable<string> Keys => _entries.Keys;
    internal IEnumerable<PdfObject> Values => _entries.Values;

    /// <summary>True once an entry was set or removed after the dictionary was read: an incremental save
    /// writes the object that holds it. A dictionary filled by the reader (<see cref="SetRead"/>) is not edited.</summary>
    internal bool Edited { get; private set; }

    public PdfObject? Get(string key) => _entries.GetValueOrDefault(key);

    public void Set(string key, PdfObject value)
    {
        _entries[key] = value;
        Edited = true;
    }

    /// <summary>An entry as the file states it, set by the reader: no edit of the document.</summary>
    internal void SetRead(string key, PdfObject value) => _entries[key] = value;

    public bool Remove(string key)
    {
        if (!_entries.Remove(key)) return false;
        Edited = true;
        return true;
    }
    public bool ContainsKey(string key) => _entries.ContainsKey(key);

    public long GetInt(string key, long defaultValue = 0)
    {
        var obj = Get(key);
        return obj is PdfInteger i ? i.Value : defaultValue;
    }

    public bool GetBool(string key, bool defaultValue = false)
    {
        var obj = Get(key);
        return obj switch
        {
            PdfBoolean b => b.Value,
            PdfInteger i => i.Value != 0,
            _ => defaultValue,
        };
    }

    public string? GetName(string key)
    {
        var obj = Get(key);
        return obj is PdfName n ? n.Value : null;
    }

    public override string ToString() => $"<< {Count} entries >>";
}
