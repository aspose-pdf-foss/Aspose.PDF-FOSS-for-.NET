using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public partial class ImageStamp
{
    /// <summary>The image XObject one stamp (and every copy made of it by <see cref="ShareCopy"/>)
    /// built for one document: a stamp placed again - on another page, or on the same page - draws
    /// the same object instead of embedding the picture once more. The document is held weakly, so
    /// a stamp kept by the caller does not keep a finished document alive.</summary>
    private sealed class XObjectSlot
    {
        internal System.WeakReference<object>? Owner;
        internal int Quality;
        internal int StampId;
        internal bool StampIdMarked;
        internal PdfStream? Stream;
    }

    private XObjectSlot? _xobjectSlot;

    /// <summary>The image XObject of this stamp for the document behind <paramref name="owner"/>:
    /// the one built before when it was built for that document with the same quality and id,
    /// else a new one, which later placements share.</summary>
    private PdfStream SharedImageXObject(object owner)
    {
        var slot = _xobjectSlot ??= new XObjectSlot();
        var marked = StampId != 0 || ForceStampIdComment;
        lock (slot)
        {
            if (slot.Stream is not null && slot.Owner is not null && slot.Owner.TryGetTarget(out var held)
                && ReferenceEquals(held, owner) && slot.Quality == Quality
                && slot.StampId == StampId && slot.StampIdMarked == marked)
                return slot.Stream;
            var stream = BuildImageXObject();
            // Managed stamps record their id on the XObject dictionary so the stamp
            // facade can rediscover them after a save. The %StampId content-stream
            // comment alone does not survive re-serialisation (comments are dropped),
            // but a /StampId dict entry does — and PdfContentEditor.GetStamps reads it.
            if (marked)
                stream.Dict.Set("StampId", new PdfInteger(StampId));
            slot.Owner = new System.WeakReference<object>(owner);
            slot.Quality = Quality;
            slot.StampId = StampId;
            slot.StampIdMarked = marked;
            slot.Stream = stream;
            return stream;
        }
    }

    /// <summary>A copy of this stamp to place on its own: its placement settings are its own, its
    /// decoded picture and the image XObject built from it are shared with this stamp.</summary>
    internal ImageStamp ShareCopy()
    {
        _xobjectSlot ??= new XObjectSlot();
        return (ImageStamp)MemberwiseClone();
    }
}
