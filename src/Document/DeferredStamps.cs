namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary>Set while a page holds stamps waiting for the paragraph layout.</summary>
    private bool _stampsDeferred;

    /// <summary>The page the last look found with paragraphs waiting, looked at first next time.</summary>
    private Page? _pendingLayoutPage;

    /// <summary>The paragraph count (<see cref="Paragraphs.Additions"/>) at which the last look found no page
    /// waiting: until a paragraph is added anywhere, none is - a page laid out only loses its paragraphs.</summary>
    private long _noPendingLayoutAt = -1;

    /// <summary>True when a page of this document, <paramref name="stamped"/> first, still has paragraphs or a table of
    /// contents to lay out: what a stamp added now would be drawn under, and pages it would not count. Stamping every
    /// page looks over the document once, not once per page.</summary>
    internal bool HasPendingParagraphLayout(Page stamped)
    {
        static bool Pending(Page page) => page.Paragraphs.Count > 0 || (page.TocInfo is not null && !page.LayoutApplied);
        if (Pending(stamped)) return true;
        if (_pendingLayoutPage is { } last && Pending(last)) return true;
        var additions = Paragraphs.Additions;
        if (_pages is null || additions == _noPendingLayoutAt) return false;
        foreach (var page in _pages)
        {
            if (!Pending(page)) continue;
            _pendingLayoutPage = page;
            return true;
        }
        _noPendingLayoutAt = additions;
        return false;
    }

    internal void NoteDeferredStamps() => _stampsDeferred = true;

    /// <summary>Draw every stamp that waited for the layout, on the page it was added to and the pages that page's
    /// content ran on to.</summary>
    private void ApplyDeferredStamps()
    {
        if (!_stampsDeferred) return;
        _stampsDeferred = false;
        foreach (var page in Pages) page.ApplyDeferredStamps();
    }
}
