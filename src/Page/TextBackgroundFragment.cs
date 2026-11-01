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
    /// <summary>Text backgrounds: one fragment's background and strike-out ink.</summary>
    private bool FlushFragmentBackground(BgColorFlushState bf, Text.TextFragment frag)
    {
        var fb = new BgFragmentState();
        if (frag.SourceXObjStream is { } sourceForm)
        {
            if (!bf.formBuilders.TryGetValue(sourceForm, out fb.builder!))
                bf.formBuilders[sourceForm] = fb.builder = new Content.ContentStreamBuilder();
        }
        else
            fb.builder = bf.pageBuilder;

        fb.fragBg = frag.TextState.BackgroundColor;

        // Text that draws no ink of its own - the OCR layer over a scanned page - has
        // nothing a box prepended to the page could sit behind: the box lands under the
        // bitmap and is never seen, and a box before the run would cover the scanned word.
        // Its highlight goes OVER the page, multiplied, so the word shows through it; a
        // source box captured under the word is replaced by it.
        if (fb.fragBg is { IsEmpty: false } && frag.SourceXObjStream is null
            && frag.TextState.RenderingMode == TextRenderingMode.Invisible)
        {
            fb.builder = OverlayBuilder(bf);
            if (frag.CapturedBackgroundSources is { Count: > 0 }) RegisterUnderlineRemoval(frag);
        }
        // A fragment whose SOURCE decorations were captured had them spliced out, so its
        // highlight is a REPLACEMENT for the rect that stood there and belongs where that
        // rect stood - inline, immediately before the run it backs. Prepended to the page
        // it lands under everything the source draws, including the very highlight it
        // replaces, and is simply not seen.
        else if (SkipsFormBackground(fb, frag)) return true;
        if (frag.SourceXObjStream is null) PlaceOnPage(fb, frag, ReferenceEquals(fb.builder, bf.overlayBuilder));

        fb.ctm = frag.ExtractionCtm;
        fb.ctmScaleX = fb.ctm is not null ? Math.Sqrt(fb.ctm.A * fb.ctm.A + fb.ctm.B * fb.ctm.B) : 1.0;
        fb.hasCtm = fb.ctmScaleX > 1.5;
        fb.ctmNonIdentity = fb.ctm is not null
            && (Math.Abs(fb.ctm.A - 1) + Math.Abs(fb.ctm.B) + Math.Abs(fb.ctm.C)
                + Math.Abs(fb.ctm.D - 1) + Math.Abs(fb.ctm.E) + Math.Abs(fb.ctm.F)) > 1e-6;
        fb.frame = !fb.hasCtm && fb.ctmNonIdentity && fb.ctm is not null
            && Math.Abs(fb.ctm.B) < 1e-6 && Math.Abs(fb.ctm.C) < 1e-6
            && fb.ctm.A > 1e-6 && Math.Abs(fb.ctm.D) > 1e-6 ? fb.ctm : null;
        fb.quarterTurn = !fb.hasCtm && fb.ctm is not null
            && Math.Abs(fb.ctm.A) < 1e-6 && Math.Abs(fb.ctm.D) < 1e-6
            && Math.Abs(fb.ctm.B) > 1e-6 && Math.Abs(fb.ctm.C) > 1e-6;
        // A box placed at the view's numbers (not through the run's matrix) is laid back in the page's space.
        fb.unrotate = frag.SourceXObjStream is null && !fb.hasCtm ? frag.PositionFrame?.Unrotate : null;

        // Fragment-level BackgroundColor: emit ONE rectangle.
        // Use fragment rectangle for X/width but compute height from font metrics
        // using the MAXIMUM rawFs across all segments (the tallest glyph determines height).
        if (TryEmitBackgroundBand(fb, frag)) return true;

        fb.segList = new List<Text.TextSegment>();
        foreach (var seg in frag.Segments.Where(seg => seg.TextState.BackgroundColor is { IsEmpty: false } && seg.Position is not null))
        {
            fb.segList.Add(seg);
        }

        fb.si = 0;
        EmitStrikeOuts(fb, frag);
        return true;
    }

    /// <summary>The matrices a box on the page opens with, by the space it was measured in: the stream ahead of
    /// the content is drawn under the identity, the one over it (<paramref name="over"/>) is appended to the
    /// content (see AppendedDecorationMatrix).</summary>
    private void PlaceOnPage(BgFragmentState fb, Text.TextFragment frag, bool over)
    {
        var frame = frag.PositionFrame;
        Matrix? Placed(Matrix? desired, bool inRunSpace)
            => over ? AppendedDecorationMatrix(frame, desired, inRunSpace) : DecorationMatrix(frame, desired, null);
        fb.inPageSpace = Placed(TransformsSince(frame), inRunSpace: false);
        fb.inReadFrame = Placed(FromReadFrame(frame), inRunSpace: false);
        fb.inRunSpace = Placed(FromRunSpace(frag), inRunSpace: true);
    }

    /// <summary>The builder of the boxes drawn over the page content: one Multiply
    /// graphics state for the whole appended stream, opened on first use.</summary>
    private Content.ContentStreamBuilder OverlayBuilder(BgColorFlushState bf)
    {
        if (bf.overlayBuilder is null)
        {
            bf.overlayBuilder = new Content.ContentStreamBuilder();
            bf.overlayBuilder.SaveState();
            bf.overlayBuilder.SetGraphicsState(AddExtGState(new Content.ExtGState { BlendMode = "Multiply" }));
        }
        return bf.overlayBuilder;
    }
}
