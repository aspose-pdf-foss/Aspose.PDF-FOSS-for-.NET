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

    /// <summary>The resolved entry under <paramref name="key"/>, or null when absent.</summary>
    public FieldDictionaryEntry? this[string key]
    {
        get
        {
            var v = _reader.Resolve(_dict.Get(key));
            return v is null ? null : new FieldDictionaryEntry(v);
        }
    }
}

/// <summary>A single dictionary entry from <see cref="FieldDictionaryView"/>.</summary>
internal sealed class FieldDictionaryEntry
{
    private readonly PdfObject _obj;

    internal FieldDictionaryEntry(PdfObject obj) => _obj = obj;

    /// <summary>View the entry as a PDF string. Throws when the entry is not a string.</summary>
    public RawPdfStringView ToPdfString() =>
        new(_obj as PdfString
            ?? throw new InvalidOperationException($"Entry is {_obj.GetType().Name}, not a string"));
}

/// <summary>A PDF string entry surfaced byte-for-byte: <see cref="ToString"/> maps each
/// stored byte to one char (Latin-1), preserving any BOM prefix verbatim.</summary>
internal sealed class RawPdfStringView
{
    private readonly PdfString _s;

    internal RawPdfStringView(PdfString s) => _s = s;

    public override string ToString() => System.Text.Encoding.Latin1.GetString(_s.Value);

    /// <summary>The DECODED text of the string (BOM-aware), as opposed to the raw
    /// byte view above — the shape callers reading e.g. a rich-text /RV expect.</summary>
    public string ExtractedString => _s.ToText();
}
