using Aspose.Pdf.Core;

namespace Aspose.Pdf.Content;

/// <summary>
/// Snapshot of an /SMask soft-mask dictionary captured at <c>gs</c>-time
/// (PDF 32000 §11.6.5.4). The renderer uses this to render the mask group on
/// first paint and derive a per-pixel alpha mask multiplied into every fragment
/// that follows. Strict spec semantics: the mask group is rendered using the
/// CTM that was active when <c>gs</c> ran, NOT the CTM at paint-time — so we
/// snapshot it here.
/// </summary>
internal sealed class SoftMaskInfo
{
    /// <summary>The full soft-mask dictionary — has /Type /Mask, /S, /G, /BC?, /TR?.</summary>
    public PdfDictionary Dict { get; init; } = null!;

    /// <summary>Subtype: "Alpha" or "Luminosity". Default per spec is "Luminosity".</summary>
    public string Subtype { get; init; } = "Luminosity";

    /// <summary>CTM at gs-time — used to render the mask group.</summary>
    public double[] Ctm { get; init; } = { 1, 0, 0, 1, 0, 0 };
}
