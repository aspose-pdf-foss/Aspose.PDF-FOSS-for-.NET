using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FormXObjectDrawState
{
    public byte[] formContent = null!;
    // Resolve Form XObject's own resources
    public Aspose.Pdf.Core.PdfDictionary? formResources;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary> formFontDicts = null!;
    public Dictionary<string, Aspose.Pdf.Core.PdfDictionary>? formExtGStates;
    public Dictionary<string, Aspose.Pdf.Core.PdfStream> formXObjects = null!;
    // PDF 32000 §8.10: `Do` on a Form XObject concatenates the form's /Matrix to
    // the caller's CTM, clips to the form's /BBox, and is bracketed by an implicit
    // q…Q. Propagating the caller CTM × form.Matrix places the form at the
    // caller's user-space position; the BBox clip keeps strokes inside a form
    // from leaking onto surrounding page content.
    public double[]? formMatrix;
    public double[] effectiveCtm = null!;
    public byte[]? formClipMask;
    // PDF 32000 §11.6.6 Transparency Group: when the form has /Group /S /Transparency,
    // its contents render onto a transparent backdrop in a separate buffer; the buffer
    // is then composited back to the parent using the BlendMode / fill-alpha that were
    // active at the `Do` call. Without this, blend modes like Multiply applied AROUND
    // a form Do (via gs) get reset by the form's own internal `/GS0 gs` (BM=Normal) on
    // each path, producing flat overlays instead of multiplied overlap colours
    // (e.g. blue-on-yellow should compose to green under Multiply).
    public Aspose.Pdf.Core.PdfDictionary? groupDict;
    public bool isTransparencyGroup;
    public RenderContext ctx = default!;
    public PdfStream formStream = default!;
    public GraphicsState state = default!;
    public Dictionary<string, PdfDictionary>? extGStates = null;
}
}
