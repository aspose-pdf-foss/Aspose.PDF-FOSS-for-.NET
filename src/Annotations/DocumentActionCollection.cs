namespace Aspose.Pdf.Annotations;

/// <summary>Document-level /AA additional-action dictionary (PDF 32000-1
/// §12.6.3 Table 195). Slots are written to the catalog /AA dict during
/// <see cref="Document.ToArray()"/>.</summary>
public sealed class DocumentActionCollection
{
    private readonly Document _document;

    public DocumentActionCollection(Document document)
    {
        _document = document ?? throw new System.ArgumentNullException(nameof(document));
    }

    /// <summary>Triggered before the document is closed (/AA /WC).</summary>
    public PdfAction? BeforeClosing { get; set; }

    /// <summary>Triggered before the document is saved (/AA /WS).</summary>
    public PdfAction? BeforeSaving { get; set; }

    /// <summary>Triggered after the document is saved (/AA /DS).</summary>
    public PdfAction? AfterSaving { get; set; }

    /// <summary>Triggered before the document is printed (/AA /WP).</summary>
    public PdfAction? BeforePrinting { get; set; }

    /// <summary>Triggered after the document is printed (/AA /DP).</summary>
    public PdfAction? AfterPrinting { get; set; }

    /// <summary>Emit configured slots to the catalog /AA dictionary.
    /// Called by Document.Save before serialisation.</summary>
    internal void WriteToCatalog()
    {
        var actions = new System.Collections.Generic.Dictionary<string, PdfAction?>
        {
            { "WC", BeforeClosing },
            { "WS", BeforeSaving },
            { "DS", AfterSaving },
            { "WP", BeforePrinting },
            { "DP", AfterPrinting },
        };
        var nonNullCount = 0;
        foreach (var v in actions.Values) if (v is not null) nonNullCount++;
        if (nonNullCount == 0)
        {
            // Strip any pre-existing /AA when all slots are clear so the
            // round-trip doesn't carry stale entries.
            _document.Reader.Catalog.Remove("AA");
            return;
        }
        var aa = new Aspose.Pdf.Core.PdfDictionary();
        foreach (var kv in actions)
        {
            if (kv.Value is null) continue;
            aa.Set(kv.Key, kv.Value.Dict);
        }
        _document.Reader.Catalog.Set("AA", aa);
    }
}
