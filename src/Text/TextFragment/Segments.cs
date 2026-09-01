
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>The bounding box the absorber computed at match time, snapshotted
    /// by value. Callers routinely mutate the live <see cref="Rectangle"/> instance
    /// (shift it, then hand it back as a replace target) — this copy preserves the
    /// pre-mutation geometry the shift is measured against.</summary>
    internal Rectangle? AbsorbedRectangle;

    /// <summary>Recover the source block's TOP baseline from its own positioning
    /// operators (full precision, unlike the quantized segment positions):
    /// identity text matrices set inside the absorbed region, and the absolute
    /// Td straight after a BT.</summary>
    private static bool TryGetSourceTopBaseline(Page page, Rectangle region, out double top)
    {
        top = double.MinValue;
        var afterBt = false;
        // A bare positioning op is not a line: empty marker runs park the pen
        // above the block's first baseline (and repeat it after the last line).
        // Only a baseline that text is actually SHOWN at counts.
        double? pending = null;
        page.Contents.EnsureMaterialized();
        foreach (var op in page.Contents)
        {
            switch (op)
            {
                case Aspose.Pdf.Operators.BT:
                    afterBt = true;
                    pending = null;
                    break;
                case Aspose.Pdf.Operators.SetTextMatrix tm:
                    pending = Math.Abs(tm.A - 1) < 1e-6 && Math.Abs(tm.B) < 1e-6
                        && Math.Abs(tm.C) < 1e-6 && Math.Abs(tm.D - 1) < 1e-6
                        && tm.E >= region.LLX - 2 && tm.E <= region.URX + 2
                        && tm.F >= region.LLY - 2 && tm.F <= region.URY + 2
                        ? tm.F : null;
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.MoveTextPosition td:
                    // Absolute only straight after BT (line matrix = identity).
                    pending = afterBt
                        && td.X >= region.LLX - 2 && td.X <= region.URX + 2
                        && td.Y >= region.LLY - 2 && td.Y <= region.URY + 2
                        ? td.Y : null;
                    afterBt = false;
                    break;
                case Aspose.Pdf.Operators.TextShowOperator:
                    if (pending is { } y && y > top) top = y;
                    break;
                case Aspose.Pdf.Operators.TextPlaceOperator:
                    afterBt = false;
                    pending = null;
                    break;
            }
        }
        return top > double.MinValue;
    }

    /// <summary>The Y a re-flowed line is written on: the source line's TRUE BASELINE, never
    /// its rectangle bottom. The two differ by the descent the source run's font DESCRIPTOR
    /// declares, and a re-emitted line does not carry that descent back — it is written into a
    /// fresh font resource whose descriptor is the face's own — so seating it on the old
    /// rectangle bottom drops the whole block by one descent. The first re-flowed line stays
    /// on the ORIGINAL baseline exactly (one page: baseline 725.880 before and
    /// after; only the rect bottom moves, 721.584 → 722.370, as the descriptor descent goes
    /// -306 → -250).</summary>
    private static double LineBaseline(
        (TextFragment f, double y, double lx, double rx) line)
        => (line.f.BaselinePosition ?? line.f.PositionOrNull) is { } bp ? bp.YIndent : line.y;

    /// <summary>The content-stream segment a TextBuilder append wrote this fragment
    /// into. While it is set the fragment is ATTACHED: text, segment and state edits
    /// made after the append do not touch the page directly - the segment is written
    /// again from the fragment's current state at save time.</summary>
    internal Core.PdfStream? AttachedSegment { get; set; }

    /// <summary>
    /// Isolate the character range <c>[startIndex, startIndex+length)</c>
    /// (<paramref name="startIndex"/> is a 0-based character offset into the
    /// fragment's text) into its own <see cref="TextSegment"/>(s): the
    /// covering segment is split into up to three pieces (before / isolated /
    /// after), each inheriting the original segment's <see cref="TextState"/>,
    /// and the fragment's <see cref="Segments"/> collection is rebuilt to
    /// reflect the split. Returns the isolated (middle) segments so a caller
    /// can restyle just that range, e.g. recolour "95" inside "Windows 95 ".
    /// </summary>
    public TextSegmentCollection IsolateTextSegments(int startIndex, int length)
    {
        var result = new TextSegmentCollection();
        if (length <= 0 || startIndex < 0) return result;

        var rangeStart = startIndex;
        var rangeEnd = startIndex + length;
        var rebuilt = new List<TextSegment>();
        var cursor = 0;
        foreach (var seg in _segments)
        {
            var text = seg.Text ?? string.Empty;
            var segStart = cursor;
            var segEnd = cursor + text.Length;
            cursor = segEnd;

            // No overlap with the isolation range — keep the segment intact.
            if (segEnd <= rangeStart || segStart >= rangeEnd)
            {
                rebuilt.Add(seg);
                continue;
            }

            // Overlap, expressed in this segment's local coordinates.
            var localStart = Math.Max(rangeStart, segStart) - segStart;
            var localEnd = Math.Min(rangeEnd, segEnd) - segStart;

            if (localStart > 0)
                rebuilt.Add(CloneSegmentText(seg, text.Substring(0, localStart)));

            var isolated = CloneSegmentText(seg, text.Substring(localStart, localEnd - localStart));
            rebuilt.Add(isolated);
            result.Add(isolated);

            if (localEnd < text.Length)
                rebuilt.Add(CloneSegmentText(seg, text.Substring(localEnd)));
        }

        _segments.Clear();
        foreach (var s in rebuilt) _segments.Add(s);
        RefreshTextFromSegments();
        return result;
    }

    /// <summary>New <see cref="TextSegment"/> carrying <paramref name="text"/>
    /// with a copy of <paramref name="src"/>'s text state.</summary>
    private static TextSegment CloneSegmentText(TextSegment src, string text)
    {
        var s = new TextSegment(text);
        s.TextState.ApplyChangesFrom(src.TextState);
        return s;
    }

    /// <summary>Clone the fragment AND its segments. The cloned fragment
    /// has fresh segment instances that mirror the source's text+state.</summary>
    public object CloneWithSegments()
    {
        var copy = (TextFragment)Clone();
        copy._segments.Clear();
        foreach (var s in _segments)
        {
            var fresh = new TextSegment(s.Text);
            fresh.TextState.ApplyChangesFrom(s.TextState);
            copy._segments.Add(fresh);
        }
        copy.RefreshTextFromSegments();
        return copy;
    }

    internal void RefreshTextFromSegments()
    {
        // The plain join, empty result included — removing every segment empties the
        // text. (DrawnOrderText keeps the last text in that case: it answers "what does
        // the content stream hold", which an emptied segment list cannot say.)
        var sb = new System.Text.StringBuilder();
        foreach (var seg in _segments)
            sb.Append(seg.Text);
        _text = sb.ToString();
    }
}
