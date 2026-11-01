using System.Collections.Generic;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private Dictionary<string, Core.PdfDictionary>? _sharedNamedFaces;

    /// <summary>The font dictionary of each embedded named face a conversion registered, by its
    /// base font name: every page that uses the face refers to the one dictionary, so the face's
    /// program travels once per document rather than once per page.</summary>
    internal Dictionary<string, Core.PdfDictionary> SharedNamedFaces =>
        _sharedNamedFaces ??= new Dictionary<string, Core.PdfDictionary>(System.StringComparer.Ordinal);
}
