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

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    /// <summary>The stages of the GDI+ form XObject draw: the transparency classification and the layer composite.</summary>
    private void CompositeFormLayer(GdiFormXObjectDrawState gf, bool hasInternalBlend)
    {
        // /I true = isolated group: contents blend against a transparent backdrop.
        // Default (/I false) = non-isolated: contents blend against the page backdrop,
        // so backdrop-dependent blend modes (e.g. Difference vs the white page) resolve
        // correctly only if the group renders onto a copy of that backdrop.
        // A forced (annotation /CA) composite has no group dict and is isolated.
        bool isolated = gf.groupDict is null
            || (gf.groupDict.Get("I") is PdfBoolean iso && iso.Value);
        // /K true = knockout group: each element composites against the group's
        // INITIAL backdrop, not the accumulated result — later opaque elements knock
        // out earlier ones (topmost wins) and a blend mode on a child sees only the
        // initial backdrop, so overlapping children do NOT blend with each other
        // (PDF 32000 §11.4.5, §7.3.4).
        bool isKnockout = gf.groupDict is not null
            && gf.groupDict.Get("K") is PdfBoolean kn && kn.Value;
        RenderGroupComposited(gf.content, gf.effectiveCtm, gf.bboxClip, gf.state, isolated, isKnockout, hasInternalBlend);
    }

    /// <summary></summary>
    private void ClassifyFormTransparency(GdiFormXObjectDrawState gf)
    {
        gf.groupDict = _reader.ResolveDict(gf.formStream.Dict.Get("Group"));
        gf.isTransparencyGroup = gf.groupDict is not null && gf.groupDict.GetName("S") == "Transparency";
        // An isolated group (/I true) establishes a transparent backdrop: its contents
        // blend only against each other, shielded from the page/parent backdrop
        // (PDF 32000 §11.4.5). Rendering it inline would let a child's blend mode reach
        // the real backdrop (e.g. a Multiply circle multiplying the page instead of only
        // its sibling), so isolated groups must always composite through their own layer —
        // even when invoked with a trivial Normal / ca=1 composite.
        gf.isIsolatedGroup = gf.isTransparencyGroup
            && gf.groupDict!.Get("I") is PdfBoolean iso0 && iso0.Value;
        // A knockout group (/K true) must composite through its own layer even when
        // invoked trivially: its elements replace each other and blend only against
        // the group's INITIAL backdrop, which rendering inline cannot express — an
        // interior Multiply child would blend with its sibling instead of knocking
        // it out.
        gf.isKnockoutGroup = gf.isTransparencyGroup
            && gf.groupDict!.Get("K") is PdfBoolean ko0 && ko0.Value;
        gf.needsComposite = (gf.isTransparencyGroup || gf.forceComposite) &&
            (gf.state.FillAlpha < 0.999
             || (!string.IsNullOrEmpty(gf.state.BlendMode) && gf.state.BlendMode != "Normal")
             || gf.state.SoftMask is not null
             || gf.isIsolatedGroup
             || gf.isKnockoutGroup);
    }
}
