using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>The highest <see cref="BaseParagraph.ZIndex"/> among the paragraphs the last layout pass put on
    /// this page, or null when it laid out none.</summary>
    internal int? LaidParagraphsTopZIndex { get; set; }

    /// <summary>Put the text a <see cref="Text.TextBuilder"/> appended to this page in ZIndex order, as page
    /// paragraphs are: the appended runs are written in ascending ZIndex (appended order among equals), and a run
    /// whose index is above every paragraph laid out on the page is drawn after them. A run is written the moment
    /// it is appended, while paragraphs are laid out at save, so without this an appended run lay under every
    /// paragraph whatever its index. Runs and paragraphs where no index was set keep their order.</summary>
    internal void OrderAppendedTextByZIndex()
    {
        if (_attachedFragments is null && _attachedParagraphs is null) return;
        // Operators held unwritten go back by stream position.
        if (_contents is { IsMaterialized: true }) return;
        if (_reader.Resolve(_dict.Get("Contents")) is not PdfArray arr || arr.Count < 2) return;
        var zOf = AppendedSegmentZIndices();
        if (zOf is null) return;

        var entries = arr.ToList();
        var top = LaidParagraphsTopZIndex;
        // Each appended segment's index, null for content of any other kind, which stays where it is.
        var zAt = entries.Select(e => _reader.Resolve(e) is PdfStream s && zOf.TryGetValue(s, out var z) ? z : (int?)null).ToList();
        bool Lifted(int i) => top is { } t && zAt[i] > t;
        var keptSlots = Enumerable.Range(0, entries.Count).Where(i => zAt[i] is not null && !Lifted(i)).ToList();
        // Stable sorts: equal indices keep their appended order.
        var keptInOrder = keptSlots.OrderBy(i => zAt[i]).ToList();
        var liftedInOrder = Enumerable.Range(0, entries.Count).Where(Lifted).OrderBy(i => zAt[i]);
        var order = new List<int>(entries.Count);
        var next = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (zAt[i] is null) order.Add(i);
            else if (!Lifted(i)) order.Add(keptInOrder[next++]);
        }
        order.AddRange(liftedInOrder);
        if (order.Select((i, at) => i == at).All(same => same)) return;
        var reordered = new PdfArray();
        foreach (var i in order) reordered.Add(entries[i]);
        _dict.Set("Contents", reordered);
        _trailingMarkedContent = null;
        _contents?.InvalidateCache();
    }

    /// <summary>The ZIndex of each content segment an appended fragment or paragraph owns, or null when the
    /// order they were written in is already their ZIndex order on this page.</summary>
    private Dictionary<PdfStream, int>? AppendedSegmentZIndices()
    {
        // Most pages set no index at all: tell that without building the map.
        int? first = null;
        var uniform = true;
        void Note(int z)
        {
            first ??= z;
            if (z != first) uniform = false;
        }
        if (_attachedFragments is not null)
            foreach (var f in _attachedFragments)
                if (f.AttachedSegment is not null) Note(f.ZIndex);
        if (_attachedParagraphs is not null)
            foreach (var p in _attachedParagraphs)
                if (p.AttachedSegment is not null) Note(p.ZIndex);
        if (first is not { } z0 || (uniform && (LaidParagraphsTopZIndex is not { } top || z0 <= top))) return null;

        var zOf = new Dictionary<PdfStream, int>(ReferenceEqualityComparer.Instance);
        if (_attachedFragments is not null)
            foreach (var f in _attachedFragments)
                if (f.AttachedSegment is { } s) zOf[s] = f.ZIndex;
        if (_attachedParagraphs is not null)
            foreach (var p in _attachedParagraphs)
                if (p.AttachedSegment is { } s) zOf[s] = p.ZIndex;
        return zOf;
    }
}
