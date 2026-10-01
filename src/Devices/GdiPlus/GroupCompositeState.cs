using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class GroupCompositeState
{
    public int w;
    public int h;
    // Am I an element of an enclosing knockout group? If so, my Do-time blend mode must
    // act against that group's initial backdrop (already applied as the layer content
    // there), not against my siblings — so composite me plainly (Normal). Captured before
    // rendering my own content flips the flag for my interior.
    public bool parentKnockout;
    // Render the group's contents onto a TRANSPARENT backdrop, capturing the group's own
    // colour+coverage, then composite that with the Do-time alpha / blend mode. This is
    // correct for isolated groups (which shield the backdrop) and — because a group's
    // Normal-content result composited at its ca reaches the backdrop identically whether
    // rendered in isolation or onto a backdrop copy — for the common non-isolated case
    // too. It also composes cleanly against the coverage-alpha page (the backdrop-copy
    // path double-counts a partially-covered paper backdrop).
    // The exception is a non-isolated group whose INTERIOR uses a blend mode against its
    // backdrop (hasInternalBlend): that interior blend must see the real backdrop, so such
    // a group renders onto a copy of it. If it composites back Normal the untouched pixels
    // stay equal to the backdrop; if it ALSO carries an outer non-Normal Do-blend, that
    // blend then applies to the backdrop-copy result at the pixels the group actually
    // painted (so a nested Difference and the outer Difference both act on the real
    // backdrop — the blend applies twice).
    public bool doBlendNonNormal;
    // Q_OBM=raw: EVERY non-isolated group with an outer non-Normal Do-blend renders on
    // a backdrop copy (not only those with interior blends), and the outer blend is
    // applied to the layer result AS-IS (no backdrop removal), weighted by coverage —
    // the blend hits the backdrop-mixed result a second time, so nested Difference
    // stacks double-apply.
    public bool rawOuterBlend;
    public bool backdropCopy;
    public bool useTransparentLayer;
    // A backdrop-copy group with an outer non-Normal Do-blend re-applies that blend to the
    // painted result; the plain backdrop-copy case (Normal Do) just lerps back.
    public bool backdropCopyOuterBlend;
    // The group only paints within its /BBox; compositing (and the non-isolated
    // backdrop copy) outside that rect is wasted work on large pages.
    public System.Drawing.Rectangle compRect;
    // Capture the inherited clip in device space so the group respects any clip
    // active at the Do (e.g. a page-level `re W n`). GDI+ stores the clip in device
    // coordinates; read it with an identity transform to get device-space geometry.
    public System.Drawing.Drawing2D.Matrix savedT = null!;
    public System.Drawing.Region deviceClip = null!;
    // Q_OBM=alias: the outer blend applies to the raw backdrop-copy layer at full
    // strength under a binary aliased content mask (stair-step tails one px past the
    // AA ink — where the layer still equals the backdrop, Difference gives |B−B|=0
    // exactly, yielding solid-black stroke tails).
    public bool aliasMask;
    public float[]? aliasCov;
    // bin2: collect which pre-pass pixels come from nested-group stamps (they take
    // replace-semantics in the composite). Save/restore around the recursive call.
    public byte[]? savedStampMask;
    public byte[]? coverage;
    public byte[]? stampMask;
    public System.Drawing.Bitmap groupBmp = null!;
    public System.Drawing.Graphics savedG = null!;
    public System.Drawing.Bitmap savedBmp = null!;
    public System.Drawing.Bitmap? savedScratch;
    public System.Drawing.Bitmap? savedKoBackdrop;
    public bool savedKoReplace;
    public System.Drawing.Graphics gg = null!;
    // Transparent-backdrop layers carry the Do-time blend mode (they composite onto the
    // backdrop like any source). Backdrop-copy layers already blended internally against
    // the backdrop, so they composite back with a plain Normal lerp (untouched pixels
    // equal the backdrop and stay unchanged). Inside a knockout parent, blend is forced
    // Normal so this element does not blend with earlier siblings.
    public string compositeBlend = null!;
    public byte[] content = default!;
    public double[] effectiveCtm = default!;
    public GraphicsPath? bboxClip = null;
    public GraphicsState state = default!;
    public bool isolated = false;
    public bool isKnockout = false;
    public bool hasInternalBlend = false;
}
}
