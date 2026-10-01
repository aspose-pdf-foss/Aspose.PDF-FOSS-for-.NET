using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf.DataEditor;

/// <summary>Edits the dictionary behind a page, a document's catalog or a resources
/// dictionary as a map of names to <see cref="ICosPdfPrimitive"/> values. Reads resolve
/// references through the document; writes go straight into the dictionary and are
/// saved with it.</summary>
public class DictionaryEditor : IDictionary<string, ICosPdfPrimitive>
{
    private readonly CosPdfDictionary _target;

    /// <summary>Edit the page's dictionary.</summary>
    public DictionaryEditor(Page page) : this(new CosPdfDictionary(page.Dict, page.Reader)) { }

    /// <summary>Edit the document's catalog.</summary>
    public DictionaryEditor(Document document) : this(new CosPdfDictionary(document.Catalog, document.Reader)) { }

    /// <summary>Edit a resources dictionary.</summary>
    public DictionaryEditor(Resources resources) : this(new CosPdfDictionary(resources)) { }

    private DictionaryEditor(CosPdfDictionary target) => _target = target;

    /// <summary>Every key, including those whose value the editor cannot show.</summary>
    public ICollection<string> AllKeys => _target.AllKeys;

    /// <summary>The keys whose value the editor shows.</summary>
    public ICollection<string> Keys => _target.Keys;

    /// <summary>The values the editor shows, with references resolved; entries it cannot show are left out.</summary>
    public ICollection<ICosPdfPrimitive> Values => _target.Values;

    /// <summary>The number of entries the editor shows (the size of <c>Keys</c>).</summary>
    public int Count => _target.Count;

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    /// <summary>Gets or sets the value stored under a key. Getting a missing or unshowable key throws
    /// <c>KeyNotFoundException</c>; setting writes the value straight into the dictionary.</summary>
    public ICosPdfPrimitive this[string key]
    {
        get => _target[key];
        set => _target[key] = value;
    }

    /// <summary>Returns true when the key holds a value the editor can show.</summary>
    public bool ContainsKey(string key) => _target.ContainsKey(key);

    /// <summary>Removes the entry with the given key; returns true when it was present.</summary>
    public bool Remove(string key) => _target.Remove(key);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out ICosPdfPrimitive value) => _target.TryGetValue(key, out value);

    /// <summary>Writes a value under a key, replacing any existing entry. Only values created by the data editor
    /// can be written; any other value throws <c>ArgumentException</c>.</summary>
    public void Add(string key, ICosPdfPrimitive value) => _target.Add(key, value);

    /// <inheritdoc/>
    public void Add(KeyValuePair<string, ICosPdfPrimitive> item) => _target.Add(item);

    /// <summary>Removes every entry, including those the editor cannot show.</summary>
    public void Clear() => _target.Clear();

    /// <inheritdoc/>
    public bool Contains(KeyValuePair<string, ICosPdfPrimitive> item) => _target.Contains(item);

    /// <inheritdoc/>
    public void CopyTo(KeyValuePair<string, ICosPdfPrimitive>[] array, int arrayIndex) => _target.CopyTo(array, arrayIndex);

    /// <inheritdoc/>
    public bool Remove(KeyValuePair<string, ICosPdfPrimitive> item) => _target.Remove(item);

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, ICosPdfPrimitive>> GetEnumerator() => _target.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
