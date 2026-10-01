using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class ImageStampApplyState
{
    public string imgName = null!;
    // Build content stream operators to place the image.
    public double w;
    public double h;
    // Anchor at XIndent/YIndent (the bottom-left placement) when set, else X/Y,
    // else derive it from Horizontal/VerticalAlignment against the page box
    // (a Right/Bottom-aligned image with no explicit
    // indent lands at pageWidth-imageWidth / 0).
    public Rectangle pageBox = null!;
    public double ax;
    public double ay;
    // Compose scale + rotation into the cm matrix, then translate so the
    // rotated image's bounding box bottom-left lands at the anchor.
    public double deg;
    public double rad;
    public double cos;
    public double sin;
    public double ma;
    public double mb;
    public double mc;
    public double md;
    public double minX;
    public double minY;
    public double me;
    public double mf;
    // Always emit a graphics-state operator (/GS gs) before placing the image so
    // the stamp composites against an explicit ExtGState rather than inheriting a
    // residual one from prior page content — otherwise a background image
    // watermark could hide the underlying content. The
    // ExtGState carries a non-default blend mode and/or partial opacity when
    // requested; otherwise it is empty (an /Type /ExtGState no-op).
    public bool wantBlend;
    public string gsName = null!;
    public string gsOp = null!;
    // A %StampId comment makes this stamp discoverable by PdfContentEditor.GetStamps
    // when an id was assigned via setStampId; the PdfFileStamp facade keeps its own
    // ImageStamp's StampId at 0 (it injects the id itself), so there is no double-mark.
    public string idComment = null!;
    public string rectComment = null!;
    // A foreground stamp is appended after the page's existing content, so it
    // inherits whatever CTM that content leaves active. Pages that were
    // flattened or are slightly malformed can leave a residual CTM — a scale
    // (e.g. a page authored in 1/600" units with a leading "0.12 0 0 -0.12 0
    // 792 cm") and/or an unbalanced q — that would silently transform the
    // stamp, placing it at the wrong position and size. Undo that residual by
    // prefixing the inverse of the active CTM, so the stamp's anchor
    // coordinates are interpreted against the page's base coordinate system.
    public string resetCm = null!;
    public string stampBody = null!;
    // A foreground image stamp is a pagination artifact: wrap it in an
    // /Artifact BDC … EMC marked-content block.
    // The BDC/EMC sit outside the q…Q draw block, so GetStamps still recognises
    // the clean q gs cm /Im Do Q shape inside. Background stamps stay bare.
    public string contentOps = null!;
    public byte[] contentBytes = null!;
    public Page page = default!;
}
}
