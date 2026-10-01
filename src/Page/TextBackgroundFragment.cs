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

        // A fragment whose SOURCE decorations were captured had them spliced out, so its
        // highlight is a REPLACEMENT for the rect that stood there and belongs where that
        // rect stood - inline, immediately before the run it backs. Prepended to the page
        // it lands under everything the source draws, including the very highlight it
        // replaces, and is simply not seen.
        if (SkipsFormBackground(fb, frag)) return true;

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

        // Fragment-level BackgroundColor: emit ONE rectangle.
        // Use fragment rectangle for X/width but compute height from font metrics
        // using the MAXIMUM rawFs across all segments (the tallest glyph determines height).
        if (TryEmitBackgroundBand(fb, frag)) return true;

        fb.segList = new List<Text.TextSegment>();
        foreach (var seg in frag.Segments)
        {
            if (seg.TextState.BackgroundColor is { IsEmpty: false } && seg.Position is not null)
                fb.segList.Add(seg);
        }

        fb.si = 0;
        EmitStrikeOuts(fb, frag);
        return true;
    }
}
