using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>The forms one absorber run has already absorbed, and among them the
    /// ones a Do showed only in part, with the text each has shown so far.</summary>
    internal sealed class FormDedupSet : HashSet<object>
    {
        public FormDedupSet() : base(ReferenceEqualityComparer.Instance) { }

        /// <summary>Forms some of whose text an earlier Do's clip cut away, and the forms
        /// nested in them that hold such text: a later Do absorbs them again.</summary>
        public HashSet<object> ShownInPart { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Per form shown in part, the runs its Dos have shown: the nested
        /// form a run sits in, its place in that form and its text.</summary>
        public Dictionary<object, HashSet<(object?, double, double, string)>> Shown { get; } =
            new(ReferenceEqualityComparer.Instance);

        /// <summary>Per form shown in part, the placement of its first Do and the line boxes its
        /// runs had there - none when a run could stay whatever the clip.</summary>
        public Dictionary<object, (double A, double D, double E, double F, List<(double, double, double, double)> Boxes)?> FirstDo { get; } =
            new(ReferenceEqualityComparer.Instance);
    }

    /// <summary>After a form's runs were added from <paramref name="from"/> on: a first
    /// Do whose clip cuts some of the text away marks the form as shown in part, and a
    /// later Do of such a form keeps only the text its own clip lets through that no
    /// earlier Do has shown.</summary>
    private static void KeepWhatThisDoShows(List<RawTextRun> runs, int from, PdfStream form,
        FormDedupSet forms, bool seenBefore, Matrix ctm)
    {
        forms.Shown.TryGetValue(form, out var shown);
        // A nested form absorbed again inside its outer form: the outer Do sorts its runs.
        if (seenBefore && shown is null) return;
        var cutAway = new HashSet<int>();
        for (var i = runs.Count - 1; i >= from; i--)
        {
            var run = runs[i];
            if (run.ClipRect is not { } clip || run.Text.Length == 0 || run.Text == "\r\n") continue;
            var box = RunLineBox(run);
            if (IsHiddenByClip(run, run.Text, box.Llx, box.Lly, box.Urx, box.Ury, clip)) cutAway.Add(i);
        }
        if (shown is null)
        {
            if (cutAway.Count == 0) return;
            forms.Shown[form] = shown = new HashSet<(object?, double, double, string)>();
            forms.ShownInPart.Add(form);
            forms.FirstDo[form] = RecordFirstDo(runs, from, ctm);
            foreach (var i in cutAway)
                if (runs[i].SourceXObj is { } inner) forms.ShownInPart.Add(inner);
        }
        for (var i = runs.Count - 1; i >= from; i--)
        {
            var run = runs[i];
            if (run.Text.Length == 0 || run.Text == "\r\n") continue;
            var hidden = cutAway.Contains(i);
            // On the first Do everything stays as reported; later Dos add only new text.
            if (!hidden && !shown.Add((run.SourceXObj, run.X, run.Y, run.Text)) && seenBefore) hidden = true;
            if (hidden && seenBefore) runs.RemoveAt(i);
        }
    }

    /// <summary>How far past the clip a line box lies before the clip surely hides it: more
    /// than the clip test's slack on any side (<see cref="ClipTopExtraSlack"/> the largest).</summary>
    private const double ClipClearance = 1.0;

    /// <summary>The first Do's placement and its text runs' line boxes, or null when the boxes
    /// cannot settle a later Do: a placement that turns or shears, or a run of text the clip
    /// test keeps whatever its place (no height, width or tested letters). Line breaks and
    /// empty runs carry no text and are left out.</summary>
    private static (double, double, double, double, List<(double, double, double, double)>)? RecordFirstDo(
        List<RawTextRun> runs, int from, Matrix ctm)
    {
        if (ctm.B != 0 || ctm.C != 0 || ctm.A == 0 || ctm.D == 0) return null;
        var boxes = new List<(double, double, double, double)>(runs.Count - from);
        for (var i = from; i < runs.Count; i++)
        {
            var run = runs[i];
            if (run.Text.Length == 0 || run.Text == "\r\n") continue;
            if (TestedCharCount(run.Text).Count <= 0) return null;
            var box = RunLineBox(run);
            if (box.Urx - box.Llx <= 0 || box.Ury - box.Lly <= 0) return null;
            boxes.Add(box);
        }
        return (ctm.A, ctm.D, ctm.E, ctm.F, boxes);
    }

    /// <summary>Whether a later Do of a form shown in part can be passed over: placed without a
    /// turn, under a clip, every text run its first Do recorded lands clear of that clip, so the
    /// clip test would cut each one away and absorbing the form again would add no text.</summary>
    private static bool LaterDoShowsNothing(ExtractRunsState xr, FormDedupSet forms, PdfStream form, Matrix ctm)
    {
        if (xr.currentClip is not { } clip) return false;
        if (!forms.FirstDo.TryGetValue(form, out var first) || first is not { } at) return false;
        if (ctm.B != 0 || ctm.C != 0 || ctm.A == 0 || ctm.D == 0) return false;
        double sx = ctm.A / at.A, sy = ctm.D / at.D;
        foreach (var (llx, lly, urx, ury) in at.Boxes)
        {
            double x1 = (llx - at.E) * sx + ctm.E, x2 = (urx - at.E) * sx + ctm.E;
            double y1 = (lly - at.F) * sy + ctm.F, y2 = (ury - at.F) * sy + ctm.F;
            double l = Math.Min(x1, x2), r = Math.Max(x1, x2), b = Math.Min(y1, y2), t = Math.Max(y1, y2);
            var clear = r < clip.Llx - ClipClearance || l > clip.Urx + ClipClearance
                || t < clip.Lly - ClipClearance || b > clip.Ury + ClipClearance;
            if (!clear) return false;
        }
        return true;
    }

    /// <summary>A form box this far past the clip still counts as inside it.</summary>
    private const double ClipReachSlack = 1.0;

    /// <summary>Whether the form's box, as this Do places it, reaches past the clip in
    /// effect. With no clip, or the box inside it, the clip cuts none of the form's text.</summary>
    private static bool FormReachesPastClip(ExtractRunsState xr, PdfDictionary formDict, Matrix ctm)
    {
        if (xr.currentClip is not { } clip) return false;
        var box = xr.reader.ResolveArray(formDict.Get("BBox"));
        if (box is not { Count: >= 4 }) return true;
        double x1 = GetNum(box[0]), y1 = GetNum(box[1]), x2 = GetNum(box[2]), y2 = GetNum(box[3]);
        foreach (var (x, y) in new[] { (x1, y1), (x1, y2), (x2, y1), (x2, y2) })
        {
            var (dx, dy) = ApplyCtm(x, y, ctm);
            if (dx < clip.Llx - ClipReachSlack || dx > clip.Urx + ClipReachSlack
                || dy < clip.Lly - ClipReachSlack || dy > clip.Ury + ClipReachSlack)
                return true;
        }
        return false;
    }
}
