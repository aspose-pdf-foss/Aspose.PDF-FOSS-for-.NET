using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfDictionary : PdfObject
{
    private readonly Dictionary<string, PdfObject> _entries = new(StringComparer.Ordinal);

    public int Count => _entries.Count;
    public IEnumerable<string> Keys => _entries.Keys;

    public PdfObject? Get(string key) => _entries.GetValueOrDefault(key);
    public void Set(string key, PdfObject value) => _entries[key] = value;
    public bool Remove(string key) => _entries.Remove(key);
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
