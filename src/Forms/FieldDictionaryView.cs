using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// Low-level read view over a form field's underlying PDF dictionary. Entries come
/// back as <see cref="FieldDictionaryEntry"/> wrappers that expose the raw stored
/// bytes, so callers can inspect exactly what the file carries (e.g. the /T name
/// string's encoding, BOM included) rather than a decoded convenience value.
/// </summary>
internal sealed class FieldDictionaryView
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;

    internal FieldDictionaryView(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    // One canonical view per underlying dictionary: the corpus compares
    // EngineDict instances across wrappers of the same annotation by
    // ReferenceEquals, so two Annotation objects over one dict must hand back
    // the same view instance.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfDictionary, FieldDictionaryView> Canonical = new();

    internal static FieldDictionaryView For(PdfDictionary dict, PdfReader reader) =>
        Canonical.GetValue(dict, d => new FieldDictionaryView(d, reader));

    /// <summary>The resolved entry under <paramref name="key"/>, or null when absent.</summary>
    public FieldDictionaryEntry? this[string key]
    {
        get
        {
            var v = _reader.Resolve(_dict.Get(key));
            return v is null ? null : new FieldDictionaryEntry(v, _reader);
        }
    }

    /// <summary>True when the dictionary carries an entry under <paramref name="key"/>.</summary>
    public bool HasKey(string key) => _dict.ContainsKey(key);
}

/// <summary>A single dictionary entry from <see cref="FieldDictionaryView"/>.</summary>
internal sealed class FieldDictionaryEntry
{
    private readonly PdfObject _obj;
    private readonly PdfReader _reader;

    internal FieldDictionaryEntry(PdfObject obj, PdfReader reader)
    {
        _obj = obj;
        _reader = reader;
    }

    /// <summary>View the entry as a PDF string. Throws when the entry is not a string.</summary>
    public RawPdfStringView ToPdfString() =>
        new(_obj as PdfString
            ?? throw new InvalidOperationException($"Entry is {_obj.GetType().Name}, not a string"));

    /// <summary>View the entry as a nested dictionary. Throws when the entry is not a dictionary.</summary>
    public FieldDictionaryView ToDictionary() =>
        new(_obj as PdfDictionary
            ?? throw new InvalidOperationException($"Entry is {_obj.GetType().Name}, not a dictionary"),
            _reader);

    /// <summary>View the entry as a number (the corpus reads
    /// <c>.ToNumber().ToNumber().ToDouble()</c> — the engine shape chains a
    /// number wrapper to itself before extracting the value).</summary>
    public PdfNumberView ToNumber() => new(_obj switch
    {
        PdfReal r => r.Value,
        PdfInteger i => i.Value,
        _ => throw new InvalidOperationException($"Entry is {_obj.GetType().Name}, not a number"),
    });

    /// <summary>The entry as a PDF name's text (the corpus chains
    /// <c>.ToName().ToString()</c> — a string serves both hops).</summary>
    public string ToName() =>
        (_obj as PdfName)?.Value
        ?? throw new InvalidOperationException($"Entry is {_obj.GetType().Name}, not a name");

    /// <summary>View the entry as an array of entries.</summary>
    public FieldDictionaryArrayView ToArray() =>
        new(_obj as PdfArray
            ?? throw new InvalidOperationException($"Entry is {_obj.GetType().Name}, not an array"),
            _reader);
}

/// <summary>An array entry surfaced through the corpus' engine-shaped chain.</summary>
internal sealed class FieldDictionaryArrayView
{
    private readonly PdfArray _arr;
    private readonly PdfReader _reader;

    internal FieldDictionaryArrayView(PdfArray arr, PdfReader reader)
    {
        _arr = arr;
        _reader = reader;
    }

    public int Count => _arr.Count;

    public FieldDictionaryEntry? this[int index]
    {
        get
        {
            var v = _reader.Resolve(_arr[index]);
            return v is null ? null : new FieldDictionaryEntry(v, _reader);
        }
    }
}

/// <summary>A numeric entry surfaced through the corpus' engine-shaped chain.</summary>
internal sealed class PdfNumberView
{
    private readonly double _value;
    internal PdfNumberView(double value) => _value = value;
    public PdfNumberView ToNumber() => this;
    public double ToDouble() => _value;
    public int ToInt() => (int)_value;
    public override string ToString() => _value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A PDF string entry surfaced byte-for-byte: <see cref="ToString"/> maps each
/// stored byte to one char (Latin-1), preserving any BOM prefix verbatim.</summary>
internal sealed class RawPdfStringView
{
    private readonly PdfString _s;

    internal RawPdfStringView(PdfString s) => _s = s;

    public override string ToString() => System.Text.Encoding.Latin1.GetString(_s.Value);

    /// <summary>The decoded text of the string (the shape the corpus reads as
    /// <c>.ToPdfString().String</c>).</summary>
    public string String => _s.ToText();

    /// <summary>The DECODED text of the string (BOM-aware), as opposed to the raw
    /// byte view above — the shape callers reading e.g. a rich-text /RV expect.</summary>
    public string ExtractedString => _s.ToText();
}
