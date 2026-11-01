using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>What a page shown turned by its /Rotate held before the generator wrote to it: its /Contents and
    /// /Annots entries, an indirect entry by its object number and a direct one by identity (a stream read from the
    /// file resolves to a new object each time it is read, its reference does not change).</summary>
    internal sealed class ShownFrameMark
    {
        internal HashSet<object> Contents { get; } = new();
        internal HashSet<object> Annotations { get; } = new();
    }

    /// <summary>The q and Q streams the page put around its own content (<see cref="IsolateExistingContent"/>,
    /// <see cref="IsolateContentLeavingMatrix"/>, <see cref="WrapExistingContentInGraphicsState"/>): they belong
    /// to the page's content, however late they were added, so the generated content's turn leaves them out. Held
    /// by the stream, not the page: two Page objects can stand for the same page.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PdfStream, object> ContentBrackets = new();

    /// <summary>Records the bracket just put around the page's content: its first and its last stream.</summary>
    private void NoteContentBracket()
    {
        var streams = ContentStreamObjects();
        if (streams.Count == 0) return;
        foreach (var stream in new[] { streams[0], streams[streams.Count - 1] })
            ContentBrackets.GetValue(stream, _ => ContentBrackets);
    }

    /// <summary>Marks what the page holds before the generator lays paragraphs out on it, or null on an unturned
    /// page: the generator lays a turned page out in its shown frame (<see cref="LayoutFrameWidth"/> x
    /// <see cref="LayoutFrameHeight"/>), and what it then writes is turned into media space as one by
    /// <see cref="TurnGeneratedContent"/>.</summary>
    internal ShownFrameMark? MarkShownFrameContent()
    {
        if (RotationQuarter == 0) return null;
        // The turn is drawn from the page's own space: content that leaves another matrix in force (a scan's
        // top-level image scale outside any q/Q) is bracketed first, or the turn would compose with it.
        IsolateContentLeavingMatrix();
        var mark = new ShownFrameMark();
        KeepAsWritten(mark);
        return mark;
    }

    /// <summary>Counts what the page holds now as written in media space: content that turns itself (an
    /// artifact or stamp drawn through the page's turn) stays out of the generated content's turn.</summary>
    internal void KeepAsWritten(ShownFrameMark mark)
    {
        foreach (var entry in ContentEntries())
        {
            mark.Contents.Add(EntryKey(entry));
            // Bracketing the content later puts the stream itself where its reference stood.
            if (_reader.Resolve(entry) is PdfStream stream) mark.Contents.Add(stream);
        }
        if (_reader.Resolve(_dict.Get("Annots")) is not PdfArray annots) return;
        foreach (var entry in annots) mark.Annotations.Add(EntryKey(entry));
    }

    /// <summary>Turns what the generator wrote since <paramref name="mark"/> from the page as shown into its
    /// media space: each run of new content streams is drawn through <see cref="ShownToMediaMatrix"/> in a
    /// graphics state of its own, as stamping a turned page draws it, and each new annotation's rectangle,
    /// quad points and appearance take the same turn. The page's own content and the brackets around it stay as
    /// they are.</summary>
    internal void TurnGeneratedContent(ShownFrameMark mark)
    {
        if (ShownToMediaMatrix() is not { } m) return;
        var written = ContentEntries().Select(e => mark.Contents.Contains(EntryKey(e))
            || (_reader.Resolve(e) is PdfStream s && mark.Contents.Contains(s)) || IsContentBracket(e)).ToList();        // From the last run back, so the indexes of the runs before it stay put.
        var end = written.Count;
        while (end > 0)
        {
            while (end > 0 && written[end - 1]) end--;
            var start = end;
            while (start > 0 && !written[start - 1]) start--;
            if (start == end) break;
            var close = new Content.ContentStreamBuilder();
            close.RestoreState();
            InsertContentStreamAt(end, close.Build());
            var open = new Content.ContentStreamBuilder();
            open.SaveState();
            open.SetMatrix(m.A, m.B, m.C, m.D, m.E, m.F);
            InsertContentStreamAt(start, open.Build());
            end = start;
        }
        _contents?.InvalidateCache();
        TransformAnnotationGeometry(m.A, m.B, m.C, m.D, m.E, m.F, entry => !mark.Annotations.Contains(EntryKey(entry)));
    }

    private bool IsContentBracket(PdfObject entry) =>
        _reader.Resolve(entry) is PdfStream stream && ContentBrackets.TryGetValue(stream, out _);

    /// <summary>The page's /Contents entries in order, as <see cref="InsertContentStreamAt"/> indexes them.</summary>
    private List<PdfObject> ContentEntries()
    {
        var raw = _dict.Get("Contents");
        return _reader.Resolve(raw) switch
        {
            PdfArray arr => arr.ToList(),
            PdfStream => [raw!],
            _ => [],
        };
    }

    private static object EntryKey(PdfObject entry) =>
        entry is PdfIndirectRef r ? (r.ObjectNumber, r.Generation) : entry;
}
