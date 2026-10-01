using System.Collections.Generic;
using System.Globalization;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class PdfPageStamp
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class PageStampContentState
{
    public Page sourcePage = null!;
    public IO.PdfReader sourceReader = null!;
    // Get source page content
    public byte[] sourceContent = null!;
    public Rectangle mb = null!;
    public IO.PdfReader targetReader = null!;
    public Document? targetDoc;
    // Build (or reuse) the source-page Form XObject. When the same stamp is applied to
    // several pages of one target document the form is imported once and shared via a
    // single indirect reference; each page's /XObject entry points at it.
    public PdfObject formObject = null!;
    // Register the Form XObject in target page resources
    public Aspose.Pdf.Core.PdfDictionary targetResources = null!;
    public Aspose.Pdf.Core.PdfDictionary xobjectDict = null!;
    // Find unique name for the form XObject on this page
    public string xobjName = null!;
    public int counter;
    // Build content stream to draw the form XObject
    public double sx;
    public double sy;
    // Stamp placement: alignment (with margins) when set; Left/Bottom keep the
    // legacy XIndent/YIndent placement so indent-positioned stamps are unchanged.
    // Negative/explicit indents pass through untouched; margins only kick in
    // when the indent is exactly unset (0) under the default alignment.
    public double x;
    public double y;
    // Placement matrix. Normally axis-aligned (sx 0 0 sy X Y), but when the target
    // page is displayed rotated 90° the stamp must be rotated with it so it lands
    // upright in the displayed view — the /Rotate 90 is baked into
    // the matrix as (0 sx -sy 0  W-YIndent  XIndent), W being the page width.
    public string matrix = null!;
    public int rot;
    // Draw the stamp form inside an /Artifact marked-content block with a default
    // graphics state: the overlay is a pagination
    // artifact, not real page content, and the leading `gs` resets the graphics
    // state so the stamp is isolated from whatever state the page content left.
    public string gsName = null!;
    // %StampId identifies the block to GetStamps/DeleteStampById; the parser
    // expects the comment immediately before the q that opens the stamp block.
    public string idComment = null!;
    public string content = null!;
    public Page targetPage = default!;
}
}
