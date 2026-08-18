using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// PDF Portfolio collection. Wraps the catalog's /Collection dictionary and,
/// inheriting from <see cref="EmbeddedFileCollection"/>, exposes the
/// portfolio's attached files for both authoring and reading.
/// </summary>
public class Collection : EmbeddedFileCollection
{
    private readonly PdfDictionary _dict;
    private CollectionSchema? _schema;

    /// <summary>
    /// Construct an empty portfolio collection. Assign to
    /// <see cref="Document.Collection"/> to materialise a /Collection
    /// dictionary in the document catalog.
    /// </summary>
    public Collection() : base()
    {
        _dict = new PdfDictionary();
    }

    /// <summary>The catalog's default-entry name for this portfolio (/D entry), or empty.</summary>
    public string DefaultEntry
    {
        get
        {
            var d = _dict?.Get("D");
            return d is PdfString s ? s.ToText() : d is PdfName n ? n.Value : string.Empty;
        }
    }

    internal Collection(PdfDictionary collDict, PdfDictionary? namesDict, PdfReader reader)
        : base(namesDict, reader)
    {
        _dict = collDict;
    }

    /// <summary>The backing /Collection dictionary.</summary>
    internal PdfDictionary Dict => _dict;

    /// <summary>
    /// Collection schema (PDF spec §7.11.5). Null when the /Collection
    /// dictionary has no /Schema entry.
    /// </summary>
    public CollectionSchema? Schema
    {
        get
        {
            if (_schema is not null) return _schema;
            var reader = OwnerDocument?.Reader;
            var schemaDict = reader is not null
                ? reader.ResolveDict(_dict.Get("Schema"))
                : _dict.Get("Schema") as PdfDictionary;
            if (schemaDict is null) return null;
            _schema = new CollectionSchema(schemaDict);
            return _schema;
        }
    }

    /// <summary>
    /// Returns the file specifications ordered by the /Collection /Sort dict
    /// (PDF spec §7.11.4 Table 73). When /Sort is absent the natural
    /// iteration order is preserved.
    /// </summary>
    public IList<FileSpecification> GetSortedCollection()
    {
        var files = new List<FileSpecification>();
        foreach (var f in this) files.Add(f);

        var reader = OwnerDocument?.Reader;
        var sortDict = reader is not null
            ? reader.ResolveDict(_dict.Get("Sort"))
            : _dict.Get("Sort") as PdfDictionary;
        if (sortDict is null || Schema is null || reader is null) return files;

        var (key, ascending) = ReadPrimarySortKey(sortDict);
        if (key is null) return files;
        var field = Schema.GetCollectionField(key);
        if (field is null) return files;

        var subtype = field.Subtype;
        files.Sort((a, b) =>
        {
            var cmp = CompareBySubtype(a, b, key, subtype, reader);
            return ascending ? cmp : -cmp;
        });
        return files;
    }

    private static (string? key, bool ascending) ReadPrimarySortKey(PdfDictionary sortDict)
    {
        var s = sortDict.Get("S");
        string? key = s switch
        {
            PdfName n => n.Value,
            PdfArray a when a.Count > 0 && a[0] is PdfName n0 => n0.Value,
            _ => null,
        };
        var asc = sortDict.Get("A") switch
        {
            PdfBoolean b => b.Value,
            PdfArray a when a.Count > 0 && a[0] is PdfBoolean b0 => b0.Value,
            _ => true,
        };
        return (key, asc);
    }

    private static int CompareBySubtype(FileSpecification a, FileSpecification b,
        string schemaKey, CollectionFieldSubtype subtype, PdfReader reader)
    {
        switch (subtype)
        {
            case CollectionFieldSubtype.F:
                return string.CompareOrdinal(a.Name, b.Name);
            case CollectionFieldSubtype.Desc:
                return string.CompareOrdinal(a.Description ?? string.Empty, b.Description ?? string.Empty);
            case CollectionFieldSubtype.Size:
                return Compare(a.Size ?? 0, b.Size ?? 0);
            case CollectionFieldSubtype.CompressedSize:
                return Compare(CompressedSizeOf(a, reader), CompressedSizeOf(b, reader));
            case CollectionFieldSubtype.ModDate:
                return string.CompareOrdinal(ParamsString(a, reader, "ModDate"), ParamsString(b, reader, "ModDate"));
            case CollectionFieldSubtype.CreationDate:
                return string.CompareOrdinal(ParamsString(a, reader, "CreationDate"), ParamsString(b, reader, "CreationDate"));
            case CollectionFieldSubtype.S:
                return string.CompareOrdinal(CIString(a, reader, schemaKey), CIString(b, reader, schemaKey));
            case CollectionFieldSubtype.D:
                return string.CompareOrdinal(CIString(a, reader, schemaKey), CIString(b, reader, schemaKey));
            case CollectionFieldSubtype.N:
                return Compare(CINumber(a, reader, schemaKey), CINumber(b, reader, schemaKey));
            default:
                return 0;
        }
    }

    private static int Compare(long x, long y) => x < y ? -1 : x > y ? 1 : 0;
    private static int Compare(double x, double y) => x < y ? -1 : x > y ? 1 : 0;

    private static long CompressedSizeOf(FileSpecification fs, PdfReader reader)
    {
        if (fs.PendingData is { } pending) return pending.Length;
        var ef = reader.ResolveDict(fs.Dict.Get("EF"));
        if (ef is null) return 0;
        var stream = reader.ResolveStream(ef.Get("F"));
        return stream?.RawData.Length ?? 0;
    }

    private static string ParamsString(FileSpecification fs, PdfReader reader, string key)
    {
        var ef = reader.ResolveDict(fs.Dict.Get("EF"));
        if (ef is null) return string.Empty;
        var stream = reader.ResolveStream(ef.Get("F"));
        if (stream is null) return string.Empty;
        var parms = reader.ResolveDict(stream.Dict.Get("Params"));
        return (parms?.Get(key) as PdfString)?.ToText() ?? string.Empty;
    }

    private static string CIString(FileSpecification fs, PdfReader reader, string schemaKey)
    {
        var ci = reader.ResolveDict(fs.Dict.Get("CI"));
        if (ci is null) return string.Empty;
        var entry = reader.Resolve(ci.Get(schemaKey));
        return entry switch
        {
            PdfString s => s.ToText(),
            PdfDictionary d => (reader.Resolve(d.Get("D")) as PdfString)?.ToText() ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static double CINumber(FileSpecification fs, PdfReader reader, string schemaKey)
    {
        var ci = reader.ResolveDict(fs.Dict.Get("CI"));
        if (ci is null) return 0;
        var entry = reader.Resolve(ci.Get(schemaKey));
        return entry switch
        {
            PdfInteger i => i.Value,
            PdfReal r => r.Value,
            PdfDictionary d => reader.Resolve(d.Get("D")) switch
            {
                PdfInteger i => i.Value,
                PdfReal r => r.Value,
                _ => 0,
            },
            _ => 0,
        };
    }
}
