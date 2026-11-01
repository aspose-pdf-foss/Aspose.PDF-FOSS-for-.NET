using System.Collections.Generic;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private Dictionary<Core.PdfStream, (Core.PdfDictionary Page, Core.PdfObject? Matrix)>? _turnedAppearances;

    /// <summary>Each annotation appearance a turn baked into a page has composed its matrix into: the
    /// page whose annotations it was turned for, and the /Matrix entry it had before. A bake of another
    /// page whose annotations draw it gives them a copy carrying that matrix to compose instead, so one
    /// page's turn never reaches another page's appearance.</summary>
    internal Dictionary<Core.PdfStream, (Core.PdfDictionary Page, Core.PdfObject? Matrix)> TurnedAppearances =>
        _turnedAppearances ??= new Dictionary<Core.PdfStream, (Core.PdfDictionary Page, Core.PdfObject? Matrix)>(
            ReferenceEqualityComparer.Instance);
}
