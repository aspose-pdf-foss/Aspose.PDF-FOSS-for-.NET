using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>Barcode symbology carried by a barcode form field's /PMD
/// (PaperMetaData) dictionary.</summary>
public enum Symbology
{
    PDF417,
    QRCode,
    DataMatrix,
}

/// <summary>
/// A Tx form field whose widget carries a /PMD (PaperMetaData) dictionary —
/// an Acrobat paper-barcode field. The barcode parameters (symbology, module
/// metrics, error correction) live in the /PMD entries.
/// </summary>
public sealed class BarcodeField : TextBoxField
{
    internal BarcodeField(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct a barcode field on the given page rectangle.</summary>
    public BarcodeField(Page page, Rectangle rect) : base(page, rect) { }

    /// <summary>Construct a barcode field bound to the document (no page widget yet).</summary>
    public BarcodeField(Document doc, Rectangle rect) : base(doc, rect) { }

    /// <summary>The field's /PMD dictionary, looked up on the widget itself or,
    /// for a split field/widget tree, on the first kid that carries one.</summary>
    private PdfDictionary? Pmd
    {
        get
        {
            if (Reader.ResolveDict(Dict.Get("PMD")) is { } own) return own;
            if (Reader.Resolve(Dict.Get("Kids")) is PdfArray kids)
                foreach (var k in kids)
                    if (Reader.ResolveDict(k) is { } kd
                        && Reader.ResolveDict(kd.Get("PMD")) is { } kidPmd)
                        return kidPmd;
            return null;
        }
    }

    /// <summary>Whether the dictionary marks a barcode field (used by the field factory).</summary>
    internal static bool IsBarcode(PdfDictionary dict, PdfReader reader)
    {
        if (reader.ResolveDict(dict.Get("PMD")) is not null) return true;
        if (reader.Resolve(dict.Get("Kids")) is PdfArray kids)
            foreach (var k in kids)
                if (reader.ResolveDict(k) is { } kd && reader.ResolveDict(kd.Get("PMD")) is not null)
                    return true;
        return false;
    }

    /// <summary>The barcode symbology (/PMD /Symbology). Unrecognised or missing
    /// names report PDF417.</summary>
    public Symbology Symbology => Pmd?.GetName("Symbology") switch
    {
        "QRCode" => Symbology.QRCode,
        "DataMatrix" => Symbology.DataMatrix,
        _ => Symbology.PDF417,
    };

    /// <summary>Rendering resolution in DPI (/PMD /Resolution).</summary>
    public int Resolution => (int)GetPmdDouble("Resolution");

    /// <summary>The barcode caption (/PMD /Caption).</summary>
    public string Caption => Pmd?.Get("Caption") is PdfString s ? s.ToText() : string.Empty;

    /// <summary>Horizontal module size in device units (/PMD /XSymWidth).</summary>
    public int XSymWidth => (int)GetPmdDouble("XSymWidth");

    /// <summary>Vertical module size in device units (/PMD /XSymHeight).</summary>
    public int XSymHeight => (int)GetPmdDouble("XSymHeight");

    /// <summary>Error-correction level (/PMD /ECC).</summary>
    public int ECC => (int)GetPmdDouble("ECC");

    private double GetPmdDouble(string key) =>
        Reader is { } r && Pmd is { } pmd ? r.Resolve(pmd.Get(key)) switch
        {
            PdfInteger i => i.Value,
            PdfReal d => d.Value,
            _ => 0,
        } : 0;
}
