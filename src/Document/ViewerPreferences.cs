using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>
    /// Get or create viewer preferences for this document.
    /// </summary>
    public ViewerPreferences GetOrCreateViewerPreferences()
    {
        var existing = ViewerPreferences;
        if (existing is not null) return existing;

        var dict = new PdfDictionary();
        _reader.Catalog.Set("ViewerPreferences", dict);
        return new ViewerPreferences(dict, _reader.Catalog);
    }

    private bool GetViewerPrefBool(string key) =>
        _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"))?.Get(key) is PdfBoolean b && b.Value;

    private void SetViewerPrefBool(string key, bool value)
    {
        var prefs = _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"));
        if (prefs is null)
        {
            prefs = new PdfDictionary();
            _reader.Catalog.Set("ViewerPreferences", prefs);
        }
        prefs.Set(key, value ? PdfBoolean.True : PdfBoolean.False);
    }

    private void WriteViewerPrefName(string key, string? value)
    {
        var dict = _reader.ResolveDict(_reader.Catalog.Get("ViewerPreferences"));
        if (dict is null)
        {
            if (value is null) return;
            dict = new PdfDictionary();
            _reader.Catalog.Set("ViewerPreferences", dict);
        }
        if (value is null) dict.Remove(key);
        else dict.Set(key, new PdfName(value));
    }
}
