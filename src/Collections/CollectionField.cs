using Aspose.Pdf.Core;

namespace Aspose.Pdf;

/// <summary>
/// One column in a PDF Portfolio's collection schema (PDF spec §7.11.5
/// Table 75). Wraps the schema entry's dictionary read-only.
/// </summary>
public class CollectionField
{
    private readonly PdfDictionary _dict;

    internal CollectionField(PdfDictionary dict) { _dict = dict; }

    /// <summary>Logical value type derived from <see cref="Subtype"/>.</summary>
    public FieldValueType FiledType
    {
        get
        {
            return Subtype switch
            {
                CollectionFieldSubtype.S or CollectionFieldSubtype.F or CollectionFieldSubtype.Desc
                    => FieldValueType.Text,
                CollectionFieldSubtype.N or CollectionFieldSubtype.Size or CollectionFieldSubtype.CompressedSize
                    => FieldValueType.Number,
                CollectionFieldSubtype.D or CollectionFieldSubtype.ModDate or CollectionFieldSubtype.CreationDate
                    => FieldValueType.Date,
                _ => FieldValueType.None,
            };
        }
    }

    /// <summary>Predefined subtype (/Subtype) — see PDF spec §7.11.5 Table 75.</summary>
    public CollectionFieldSubtype Subtype => _dict.GetName("Subtype") switch
    {
        "S" => CollectionFieldSubtype.S,
        "D" => CollectionFieldSubtype.D,
        "N" => CollectionFieldSubtype.N,
        "F" => CollectionFieldSubtype.F,
        "Desc" => CollectionFieldSubtype.Desc,
        "ModDate" => CollectionFieldSubtype.ModDate,
        "CreationDate" => CollectionFieldSubtype.CreationDate,
        "Size" => CollectionFieldSubtype.Size,
        "CompressedSize" => CollectionFieldSubtype.CompressedSize,
        _ => CollectionFieldSubtype.None,
    };

    /// <summary>Display name (/N).</summary>
    public string N => (_dict.Get("N") as PdfString)?.ToText() ?? string.Empty;

    /// <summary>Display order (/O), if specified.</summary>
    public int? O
    {
        get
        {
            if (_dict.Get("O") is PdfInteger i) return (int)i.Value;
            return null;
        }
    }

    /// <summary>Whether the column is initially visible (/V), default false.</summary>
    public bool V => _dict.Get("V") is PdfBoolean b && b.Value;

    /// <summary>Whether the column is editable (/E), default false.</summary>
    public bool E => _dict.Get("E") is PdfBoolean b && b.Value;
}
