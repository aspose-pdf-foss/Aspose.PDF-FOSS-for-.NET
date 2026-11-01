namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Stamps (a <see cref="Stamp"/> or an <see cref="ImageStamp"/>) added while the document still had
    /// paragraphs to lay out, in the order they were added.</summary>
    private List<object>? _deferredStamps;

    /// <summary>True when <paramref name="stamp"/> waits for the document's paragraph layout instead of being drawn
    /// now. Layout at save appends the paragraphs' content after anything drawn before it, and adds the pages a flow
    /// or a table of contents runs over: a stamp drawn at once lay under a box's background laid out later, and a page
    /// number read then counted none of those pages.</summary>
    private bool DefersStamp(object stamp)
    {
        if (_reader?.OwnerDocument is not { } doc || !doc.HasPendingParagraphLayout(this)) return false;
        (_deferredStamps ??= new()).Add(stamp);
        doc.NoteDeferredStamps();
        return true;
    }

    /// <summary>A page a flow added to carry on <paramref name="owner"/>'s content takes the stamps waiting for the
    /// owner, as it takes the owner's header and footer.</summary>
    internal void InheritDeferredStamps(Page owner)
    {
        if (owner._deferredStamps is { } stamps) _deferredStamps = new(stamps);
    }

    /// <summary>Draw the stamps that waited for the layout, now that the page's content and number are final.</summary>
    internal void ApplyDeferredStamps()
    {
        if (_deferredStamps is not { } stamps) return;
        _deferredStamps = null;
        foreach (var stamp in stamps)
        {
            if (stamp is ImageStamp image) AddStamp(image);
            else AddStamp((Stamp)stamp);
        }
    }

    /// <summary>True while stamps wait on this page for the layout.</summary>
    internal bool HasDeferredStamps => _deferredStamps is not null;
}
