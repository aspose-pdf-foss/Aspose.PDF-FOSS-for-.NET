using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class BgColorFlushState
{
    public Content.ContentStreamBuilder pageBuilder = null!;
    // A fragment extracted from a Form XObject gets its highlight drawn INTO
    // that form's stream (before its text), not onto the page: the rectangle
    // must live where the text lives, both for the paint order under nested
    // content and for consumers reading the form's own operator list.
    public Dictionary<Aspose.Pdf.Core.PdfStream, Aspose.Pdf.Content.ContentStreamBuilder> formBuilders = null!;
    public byte[] bytes = null!;
}
}
