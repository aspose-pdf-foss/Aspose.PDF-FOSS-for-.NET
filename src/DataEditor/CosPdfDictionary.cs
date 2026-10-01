using System.Collections;
using System.Collections.Generic;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.DataEditor;

/// <summary>A dictionary value, editable in place: reads resolve references through the
/// owning document, writes go straight into the dictionary.</summary>
public class CosPdfDictionary : CosPdfPrimitive, IDictionary<string, ICosPdfPrimitive>
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader? _reader;

    internal CosPdfDictionary(PdfDictionary dict, PdfReader? reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>A new, empty dictionary that can be written into the page's dictionary.</summary>
    public static CosPdfDictionary CreateEmptyDictionary(Page page) => new(new PdfDictionary(), page?.Reader);

    /// <summary>A new, empty dictionary that can be written into the document's catalog.</summary>
    public static CosPdfDictionary CreateEmptyDictionary(Document document) => new(new PdfDictionary(), document?.Reader);

    /// <summary>The resources dictionary of <paramref name="resources"/>.</summary>
    public CosPdfDictionary(Resources resources) : this(resources.ResourceDict, resources.ResourceReader) { }

    internal PdfDictionary Dictionary => _dict;

    /// <summary>Every key, including those whose value the editor cannot show.</summary>
    public ICollection<string> AllKeys => new List<string>(_dict.Keys);

    /// <summary>The keys whose value the editor shows.</summary>
    public ICollection<string> Keys
    {
        get
        {
            var keys = new List<string>();
            foreach (var key in _dict.Keys)
                if (Read(key) is not null) keys.Add(key);
            return keys;
        }
    }

    /// <summary>The values the editor shows, with references resolved; entries it cannot show are left out.</summary>
    public ICollection<ICosPdfPrimitive> Values
    {
        get
        {
            var values = new List<ICosPdfPrimitive>();
            foreach (var key in _dict.Keys)
                if (Read(key) is { } v) values.Add(v);
            return values;
        }
    }

    /// <summary>The number of entries the editor shows (the size of <c>Keys</c>).</summary>
    public int Count => Keys.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <summary>Gets or sets the value stored under a key. Getting a missing or unshowable key throws
    /// <c>KeyNotFoundException</c>; setting writes the value straight into the dictionary.</summary>
    public ICosPdfPrimitive this[string key]
    {
        get => Read(key) ?? throw new KeyNotFoundException(key);
        set => Write(key, value);
    }

    /// <inheritdoc/>
    public override CosPdfDictionary ToCosPdfDictionary() => this;

    /// <inheritdoc/>
    public override string ToString() => "<< " + string.Join(" ", Entries()) + " >>";

    /// <summary>Returns true when the key holds a value the editor can show.</summary>
    public bool ContainsKey(string key) => Read(key) is not null;

    /// <summary>Removes the entry with the given key; returns true when it was present.</summary>
    public bool Remove(string key) => _dict.Remove(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out ICosPdfPrimitive value)
    {
        value = Read(key)!;
        return value is not null;
    }

    /// <summary>Writes a value under a key, replacing any existing entry. Only values created by the data editor
    /// can be written; any other value throws <c>ArgumentException</c>.</summary>
    public void Add(string key, ICosPdfPrimitive value) => Write(key, value);

    /// <inheritdoc/>
    public void Add(KeyValuePair<string, ICosPdfPrimitive> item) => Write(item.Key, item.Value);

    /// <summary>Removes every entry, including those the editor cannot show.</summary>
    public void Clear()
    {
        foreach (var key in new List<string>(_dict.Keys)) _dict.Remove(key);
    }

    /// <inheritdoc/>
    public bool Contains(KeyValuePair<string, ICosPdfPrimitive> item)
        => Read(item.Key) is { } v && v.Equals(item.Value);

    /// <inheritdoc/>
    public void CopyTo(KeyValuePair<string, ICosPdfPrimitive>[] array, int arrayIndex)
    {
        foreach (var pair in this) array[arrayIndex++] = pair;
    }

    /// <inheritdoc/>
    public bool Remove(KeyValuePair<string, ICosPdfPrimitive> item) => Contains(item) && _dict.Remove(item.Key);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, ICosPdfPrimitive>> GetEnumerator()
    {
        foreach (var key in _dict.Keys)
            if (Read(key) is { } v) yield return new KeyValuePair<string, ICosPdfPrimitive>(key, v);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal override PdfObject ToPdfObject() => _dict;

    private CosPdfPrimitive? Read(string key)
    {
        var raw = _dict.Get(key);
        var value = _reader is null ? raw : _reader.Resolve(raw);
        return value is PdfDictionary d ? new CosPdfDictionary(d, _reader) : From(value);
    }

    private void Write(string key, ICosPdfPrimitive value)
    {
        if (value is not CosPdfPrimitive primitive)
            throw new System.ArgumentException("Only values created by the data editor can be written.", nameof(value));
        _dict.Set(key, primitive.ToPdfObject());
    }

    private IEnumerable<string> Entries()
    {
        foreach (var pair in this) yield return "/" + pair.Key + " " + pair.Value;
    }
}
