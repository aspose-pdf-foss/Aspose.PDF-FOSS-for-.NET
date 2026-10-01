using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

internal static partial class LayerHelper
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PageLayersState
{
    public List<Aspose.Pdf.OptionalContentGroup> result = null!;
    // Get document-level OCG properties so layers can persist state changes.
    // Build a lookup from OCG dict → existing group instance so that changes
    // to a page layer's DefaultState propagate to the document-level group.
    public Aspose.Pdf.Core.PdfDictionary? ocPropsDict;
    public OptionalContentProperties? ocProps;
    public Dictionary<Aspose.Pdf.Core.PdfDictionary, Aspose.Pdf.OptionalContentGroup> ocgLookup = null!;
    public HashSet<Aspose.Pdf.Core.PdfDictionary> seen = null!;
    public Aspose.Pdf.Core.PdfDictionary? resources;
    public Page page = default!;
    public PdfReader reader = default!;
}
}
