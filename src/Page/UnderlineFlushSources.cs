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
    /// <summary>A fragment carrying captured underline sources flushes each source's own rule (its extent, offset and thickness) and is then done; true when the fragment was handled that way.</summary>
    private bool FlushCapturedUnderlineSources(UnderlineFlushState ul, Text.TextFragment frag)
    {
        if (frag.CapturedUnderlineSources is { Count: > 0 })
        {
            var afg = frag.TextState.ForegroundColor;
            // A face with no descent metric keeps the typical 0.216 the formula was
            // probed against, so the band never collapses onto YIndent itself.
            var ulBottom = ul.fragPos.YIndent + (ul.ulDescent > 0 ? ul.ulDescent : 0.216) / 10 * ul.fs;
            // A source rule normally runs past the matched phrase in BOTH directions -
            // one rule under "www.oliver.com" covers three runs. Splicing it out and
            // redrawing only the match leaves the runs on either side bare, so the rule
            // is re-laid piece by piece: the HEAD (whatever the rule covered left of the
            // match), the match itself at its new advance, and the TAIL. Each piece is
            // written inline, before the run it dresses, in the library's own band -
            // the same treatment the whole line gets.
            double headW = 0, headX = ul.fragPos.XIndent;
            if (frag.CapturedUnderlinePageRect is { } srcRule && srcRule.Llx < ul.fragPos.XIndent - 0.5)
            {
                headX = srcRule.Llx;
                headW = ul.fragPos.XIndent - srcRule.Llx;
            }
            double tailW = 0, tailX = ul.fragPos.XIndent + ul.w;
            double tailBottom = ulBottom, tailThick = ul.ulThick;
            if (frag.SourceUnderlineTrailingText is { Length: > 0 } tail)
            {
                // The tail is the rest of the match's OWN run, so it follows the
                // replacement at whatever advance that came to, in the same band.
                try { tailW = frag.TextState.Font?.MeasureString(tail, ul.fs) ?? 0; }
                catch { tailW = 0; }
            }
            else if (frag.CapturedUnderlinePageRect is { } tailRule
                && frag.SourceUnderlineRunEndX > tailRule.Llx
                && tailRule.Urx > frag.SourceUnderlineRunEndX + 0.5)
            {
                // ...otherwise the rule runs on past the match's run and dresses a
                // SEPARATE one the replacement never moved. That piece is not ours to
                // re-lay: it keeps the span AND THE BAND the source gave it, because the
                // run under it still sits where it always did. Redrawing it in the
                // library's band moves a rule whose text did not move.
                tailX = frag.SourceUnderlineRunEndX;
                tailW = tailRule.Urx - frag.SourceUnderlineRunEndX;
                tailBottom = tailRule.Lly;
                tailThick = tailRule.Ury - tailRule.Lly;
            }
            var placedInline = InsertBeforeTextObjectAt(
                DecorationBlock(afg, ul.fragPos.XIndent, ulBottom, ul.w, ul.ulThick),
                ul.fragPos.XIndent, ul.fragPos.YIndent);
            if (placedInline)
            {
                if (headW > 0.5)
                    InsertBeforeTextObjectAt(
                        DecorationBlock(afg, headX, ulBottom, headW, ul.ulThick),
                        headX, ul.fragPos.YIndent);
                if (tailW > 0)
                    InsertBeforeTextObjectAt(
                        DecorationBlock(afg, tailX, tailBottom, tailW, tailThick),
                        tailX, ul.fragPos.YIndent);
                foreach (var comp in frag.CompanionRules ?? Enumerable.Empty<(double X, double W, Aspose.Pdf.Color Colour)>())
                    InsertBeforeTextObjectAt(
                        DecorationBlock(comp.Colour, comp.X, ulBottom, comp.W, ul.ulThick),
                        comp.X, ul.fragPos.YIndent);
                return true;
            }
            ul.builder.SaveState();
            ul.builder.SetFillColor(afg?.R / 255.0 ?? 0, afg?.G / 255.0 ?? 0, afg?.B / 255.0 ?? 0);
            if (headW > 0.5) { ul.builder.Rectangle(headX, ulBottom, headW, ul.ulThick); ul.builder.Fill(); }
            ul.builder.Rectangle(ul.fragPos.XIndent, ulBottom, ul.w, ul.ulThick);
            ul.builder.Fill();
            if (tailW > 0)
            {
                ul.builder.Rectangle(tailX, tailBottom, tailW, tailThick);
                ul.builder.Fill();
            }
            ul.builder.RestoreState();
            return true;
        }
        return false;
    }
}
