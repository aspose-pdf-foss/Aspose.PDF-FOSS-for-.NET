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
    /// <summary>
    /// Register a fragment whose TextState.Underline was set after extraction.
    /// Called from the Underline setter when the segment traces back to this page.
    /// </summary>
    internal void RegisterUnderlineFragment(Text.TextFragment fragment)
    {
        _underlineFragments ??= new();
        _underlineFragments.Add(fragment);
    }

    /// <summary>
    /// Register a fragment whose captured source underline should be removed from the
    /// content stream, because its TextState.Underline was toggled off after extraction
    /// (ToAttemptGetUnderlineFromSource). Called from the Underline setter.
    /// </summary>
    internal void RegisterUnderlineRemoval(Text.TextFragment fragment)
    {
        _underlineRemovalFragments ??= new();
        _underlineRemovalFragments.Add(fragment);
    }

    /// <summary>
    /// Splice out the source underline rectangles for every registered removal fragment by
    /// matching their captured raw <c>re</c> operands against the page content operators.
    /// Called during save, alongside <see cref="FlushUnderlineRectangles"/>.
    /// </summary>
    internal void FlushUnderlineRemovals()
    {
        if (_underlineRemovalFragments is null || _underlineRemovalFragments.Count == 0) return;
        // Start from what the page ACTUALLY holds. An earlier save-time pass may have
        // appended or prepended a stream, and the cached operator view is a snapshot from
        // before it existed - flushing that view back would restore the page as it was and
        // drop the append. Only these passes need it: resetting inside the append itself
        // pulls the view out from under a caller that is still building with it.
        ResetContentsCache();
        var targets = new List<(double X, double Y, double W, double H)>();
        // A source rule normally runs past the matched phrase — under "Test bold text 26"
        // the rule also covers the " 26". Switching THIS fragment's underline off must not
        // take the rest of the line's rule with it, so the part the fragment never covered
        // is re-emitted, keeping the source rule's own band (a stroked rule keeps its
        // stroke width). The REPLACEMENT case is different and is not handled here: the
        // fragment is registered for redraw too, and FlushUnderlineRectangles reseats both
        // the replacement's rule and the tail's in the library's own band.
        var survivors = new Content.ContentStreamBuilder();
        var anySurvivor = false;
        foreach (var frag in _underlineRemovalFragments)
        {
            if (frag.CapturedUnderlineSources is { } list) targets.AddRange(list);
            if (frag.CapturedBackgroundSources is { } bgList) targets.AddRange(bgList);
            if (frag.CompanionRuleSources is { } compList) targets.AddRange(compList);
            if (_underlineFragments?.Contains(frag) == true
                || _underlineRedrawn?.Contains(frag) == true) continue;
            if (frag.CapturedUnderlinePageRect is not { } pr) continue;
            var from = frag.SourceUnderlineRunEndX;
            if (from <= pr.Llx || pr.Urx - from <= 0.5) continue;
            var col = frag.TextState.ForegroundColor;
            survivors.SaveState();
            survivors.SetFillColor(col?.R / 255.0 ?? 0, col?.G / 255.0 ?? 0, col?.B / 255.0 ?? 0);
            survivors.Rectangle(from, pr.Lly, pr.Urx - from, pr.Ury - pr.Lly);
            survivors.Fill();
            survivors.RestoreState();
            anySurvivor = true;
        }
        _underlineRemovalFragments.Clear();
        _underlineRedrawn = null;
        if (targets.Count == 0) return;

        var ops = Contents;
        // A spliced `re` takes its PAINT operator with it. Removing the rectangle alone left
        // a bare `f*` behind, which fills whatever path happens to be current - an operator
        // list that reads as valid but is not what the page draws.
        var paintOfRe = new System.Collections.Generic.Dictionary<Operator, Operator>();
        {
            var scan = ops.ToList();
            for (var i = 0; i < scan.Count; i++)
            {
                if (scan[i] is not Aspose.Pdf.Operators.Re && scan[i].CommandName != "re") continue;
                for (var j = i + 1; j < scan.Count && j <= i + 2; j++)
                {
                    var cmd = scan[j].CommandName;
                    if (cmd is "f" or "F" or "f*" or "b" or "b*" or "B" or "B*" or "n" or "s" or "S")
                    { paintOfRe[scan[i]] = scan[j]; break; }
                    if (cmd is not ("W" or "W*")) break;
                }
            }
        }
        var doomedPaint = new System.Collections.Generic.HashSet<Operator>();
        var removed = ops.RemoveWhere(op => IsRemovedUnderlineOp(op, doomedPaint, targets, paintOfRe));
        // Persist the edited operator list back to the page content stream.
        if (removed > 0) ops.FlushToPage();
        if (anySurvivor)
        {
            var survivorBytes = survivors.Build();
            if (survivorBytes.Length > 0) AddContentStream(survivorBytes);
        }
    }

}
