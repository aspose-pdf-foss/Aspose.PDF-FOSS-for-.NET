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
    /// <summary>Write the decorations registered on the page's text - highlights, underlines,
    /// strike-outs and the source rules they replace - into its content. Run at save: each is
    /// placed from where its text was read, through the redraws of the content since, out of the
    /// matrix the stream it goes into is drawn under.</summary>
    internal void FlushTextDecorations()
    {
        // The content's end matrix is read again for this flush, by the first decoration appended.
        _appendedBaseRead = false;
        FlushBgColorRectangles();
        FlushUnderlineRectangles();
        // Removal LAST: its own write-back is what folds a prepended or appended
        // decoration stream into the page's single operator list, which is the list a
        // caller reads after save.
        FlushUnderlineRemovals();
        FlushBgColorOverlay();
        FlushStrikeOutRectangles();
        _appendedBase = null;
        _appendedBaseRead = false;
    }

    /// <summary>
    /// Emit thin filled rectangles below text for every registered underline fragment.
    /// Called during save after content stream operators are written.
    /// </summary>
    internal void FlushUnderlineRectangles()
    {
        if (_underlineFragments is null || _underlineFragments.Count == 0) return;
        // Start from what the page ACTUALLY holds. An earlier save-time pass may have
        // appended or prepended a stream, and the cached operator view is a snapshot from
        // before it existed - flushing that view back would restore the page as it was and
        // drop the append. Only these passes need it: resetting inside the append itself
        // pulls the view out from under a caller that is still building with it.
        ResetContentsCache();
        _underlineRedrawn = new HashSet<Text.TextFragment>(_underlineFragments);
        var ul = new UnderlineFlushState();
        ul.builder = new Content.ContentStreamBuilder();
        ul.appended = true;
#pragma warning disable S3267 // a step loop: each call does the work and returns false to stop - as a LINQ predicate the side effects would hide in a query
        foreach (var frag in _underlineFragments)
#pragma warning restore S3267
        {
            if (!FlushUnderlineFragment(ul, frag)) break;
        }
        ul.bytes = ul.builder.Build();
        if (ul.bytes.Length > 0)
            AddContentStream(ul.bytes);
        _underlineFragments.Clear();
    }

    /// <summary>The rule of one registered underline fragment, built now and taken OUT of
    /// the save-time flush, so a flow paragraph can paint its underline before its text the
    /// way the reference generator orders them (the rule under the glyphs, not over their
    /// descenders). Null when the fragment is not registered.</summary>
    internal byte[]? TakeUnderlineBytes(Text.TextFragment fragment)
    {
        if (_underlineFragments is null || !_underlineFragments.Remove(fragment)) return null;
        var ul = new UnderlineFlushState();
        ul.builder = new Content.ContentStreamBuilder();
        FlushUnderlineFragment(ul, fragment);
        var bytes = ul.builder.Build();
        return bytes.Length > 0 ? bytes : null;
    }
}
