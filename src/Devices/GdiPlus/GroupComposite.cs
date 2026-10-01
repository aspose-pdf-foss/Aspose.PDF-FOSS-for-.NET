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
    private void RenderGroupComposited(byte[] content, double[] effectiveCtm, GraphicsPath? bboxClip, GraphicsState state, bool isolated, bool isKnockout = false, bool hasInternalBlend = false)
    {
        var gc = new GroupCompositeState();
        gc.content = content;
        gc.effectiveCtm = effectiveCtm;
        gc.bboxClip = bboxClip;
        gc.state = state;
        gc.isolated = isolated;
        gc.isKnockout = isKnockout;
        gc.hasInternalBlend = hasInternalBlend;
        gc.w = _bitmap.Width;
        gc.h = _bitmap.Height;

        if (!PrepareGroupComposite(gc)) return;

        if (CompositeSupersampledOuterBlend(gc)) return;

        gc.aliasMask = gc.backdropCopyOuterBlend && ObMode == "alias";
        gc.aliasCov = null;
        if (gc.aliasMask)
        {
            var mb = CaptureGroupCoverage(gc.content, gc.effectiveCtm, gc.bboxClip, gc.deviceClip, gc.compRect, gc.isKnockout, aliased: true);
            gc.aliasCov = RentCovFloat(gc.w * gc.h);
            for (int i = 0; i < mb.Length; i++) if (mb[i] > 0) gc.aliasCov[i] = 1f;
            _covBytePool.Push(mb);
        }

        gc.savedStampMask = _stampMask;
        _stampMask = gc.backdropCopyOuterBlend && !gc.aliasMask && ObMode == "bin2" ? RentCovByte(gc.w * gc.h) : null;
        gc.coverage = gc.backdropCopyOuterBlend && !gc.aliasMask
            ? CaptureGroupCoverage(gc.content, gc.effectiveCtm, gc.bboxClip, gc.deviceClip, gc.compRect, gc.isKnockout)
            : null;
        gc.stampMask = _stampMask;
        _stampMask = gc.savedStampMask;

        gc.groupBmp = RentLayer(gc.w, gc.h);
        gc.savedG = _g;
        gc.savedBmp = _bitmap;
        gc.savedScratch = _blendScratch;
        gc.savedKoBackdrop = _koBackdrop;
        gc.savedKoReplace = _koReplace;
        gc.gg = Graphics.FromImage(gc.groupBmp);
        try
        {
            RenderGroupInterior(gc);
        }
        finally
        {
            RestoreGroupDevice(gc);
        }

        CompositeGroupResult(gc);
    }
}
