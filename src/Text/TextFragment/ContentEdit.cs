
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>
    /// Remove this fragment's text from the page content stream. Used when a
    /// fragment is removed from an absorber's result collection: the producing
    /// text-showing operator is dropped so the next save no longer renders it.
    /// Matches the operator whose entire shown text equals this fragment's text
    /// (scoped to the fragment's page-space Y) so deleting a short fragment such
    /// as "$" does not corrupt a longer one such as "$ 200.00" on the same row.
    /// </summary>
    internal void DeleteFromContent()
    {
        if (SourcePage is null || string.IsNullOrEmpty(_text))
            return;

        // The replacer scopes by the Tm-origin (baseline) Y; Position.YIndent is
        // often the rect bottom (a full descent below the baseline — exactly at
        // the scoping tolerance for a 24 pt font), so try the baseline-corrected
        // candidate first and the raw position second, same as the Text setter.
        var targetYs = new List<double?>();
        if (_position is { } pos)
        {
            var baseY = (BaselinePosition ?? pos).YIndent;
            targetYs.Add(baseY);
            if (Math.Abs(pos.YIndent - baseY) > 0.01)
                targetYs.Add(pos.YIndent);
        }
        else
            targetYs.Add(null);

        foreach (var targetY in targetYs)
        {
            var replacer = new TextReplacer { MatchWholeOperator = true, TargetY = targetY };
            // An invisible (Tr 3) fragment is typically the OCR/searchable twin of a
            // visible copy drawn at nearly the same spot (a scanned-invoice text layer).
            // Deleting it must not strip the visible copy, which Y/X scoping alone would
            // hit first — restrict the match to the invisible render mode.
            if (TextState is { RenderingMode: TextRenderingMode.Invisible })
                replacer.RequiredRenderMode = 3;
            replacer.Replace(SourcePage, _text, string.Empty);
            if (replacer.ReplacementCount > 0) break;

            // Fall back to substring replacement for fragments whose text spans only
            // part of an operator (or multiple operators) and so was not removed by
            // the exact whole-operator pass. The rest of the operator's text survives
            // the deletion, so it must keep its exact position: anchor the trailing
            // run at its original absolute Tm instead of letting it slide left into
            // the deleted span.
            var fallback = new TextReplacer { AnchorTrailingOnReplace = true, TargetY = targetY };
            if (TextState is { RenderingMode: TextRenderingMode.Invisible })
                fallback.RequiredRenderMode = 3;
            fallback.Replace(SourcePage, _text, string.Empty);
            if (fallback.ReplacementCount > 0) break;
        }

        _text = string.Empty;
        _segments.Clear();
    }

    /// <summary>
    /// Remove this fragment's text from the page for redaction: like
    /// <see cref="DeleteFromContent"/> but width-preserving — a fully-deleted show
    /// operator leaves a glyph-less advance instead of being dropped, so text after
    /// it on the same line keeps its position (no reflow). Scoped to the fragment's
    /// page-space Y so only this occurrence is removed.
    /// </summary>
    internal void RedactFromContent()
    {
        if (SourcePage is null || string.IsNullOrEmpty(_text))
            return;

        // Scope to this occurrence: Y picks the line, X picks the operator —
        // a short run like " e" can appear several times on one line, and
        // deleting the copies outside the redaction box would eat text the
        // caller never asked to remove.
        var replacer = new TextReplacer { MatchWholeOperator = true, PreserveAdvanceOnDelete = true };
        if (_position is { } pos)
        {
            replacer.TargetY = pos.YIndent;
            replacer.TargetX = pos.XIndent;
        }
        replacer.Replace(SourcePage, _text, string.Empty);

        if (replacer.ReplacementCount == 0)
        {
            var fallback = new TextReplacer { PreserveAdvanceOnDelete = true };
            if (_position is { } pos2)
            {
                fallback.TargetY = pos2.YIndent;
                fallback.TargetX = pos2.XIndent;
            }
            fallback.Replace(SourcePage, _text, string.Empty);
        }

        _text = string.Empty;
        _segments.Clear();
    }

    private int DeleteReflowSource(Page page, string oldText)
    {
        var deleted = 0;
        // Counted PER SEGMENT, not summed: one call can remove two runs when the segment's
        // text repeats on the line, and a sum then reads "all gone" while other segments
        // matched nothing at all. The caller only needs to know whether ANY segment was
        // left behind, so record the segments that resolved, not the runs removed.
        _unresolvedSegments = 0;
        foreach (var seg in _segments)
        {
            if (string.IsNullOrEmpty(seg.Text)) continue;
            var r = new TextReplacer();
            if (seg.Position is { } sp) r.TargetY = sp.YIndent;
            r.Replace(page, seg.Text, string.Empty);
            if (r.ReplacementCount == 0) _unresolvedSegments++;
            deleted += r.ReplacementCount;
        }
        if (deleted == 0 && !string.IsNullOrEmpty(oldText))
        {
            var r = new TextReplacer();
            if (_position is { } pos) r.TargetY = pos.YIndent;
            r.ReplaceWithCrossOperator(page, oldText, string.Empty);
            deleted += r.ReplacementCount;
        }
        return deleted;
    }

    /// <summary>Widen every clip rectangle (<c>re</c> followed by <c>W</c>/<c>W*</c>)
    /// that contains the point (x, y) but is too narrow to fit <paramref name="neededWidth"/>
    /// of text starting at x. Used by <see cref="Text"/> under
    /// <see cref="TextEditOptions.ClippingPathsProcessingMode.Expand"/>.</summary>
    private static void ExpandTightClipsAround(Page page, double x, double y, double neededWidth)
    {
        var ops = page.Contents;
        // Materialize first: a plain enumeration yields throw-away parses whose
        // mutations never reach the stream.
        ops.EnsureMaterialized();
        Aspose.Pdf.Operator? prev = null;
        var changed = false;
        foreach (var op in ops)
        {
            if (op is Aspose.Pdf.Operators.Clip or Aspose.Pdf.Operators.EOClip
                && prev is Aspose.Pdf.Operators.Re re
                && re.X <= x + 0.5 && re.X + re.Width >= x - 0.5
                && re.Y <= y + 0.5 && re.Y + re.Height >= y - 0.5
                && re.X + re.Width < x + neededWidth - 0.25)
            {
                re.Width = x + neededWidth + 0.75 - re.X;
                changed = true;
            }
            prev = op;
        }
        if (changed) ops.FlushToPage();
    }
}
