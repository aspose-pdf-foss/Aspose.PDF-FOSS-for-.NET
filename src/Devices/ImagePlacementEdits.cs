using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>Hide and Replace of a placement the page's own operators do not reach: an image drawn inside a form
/// XObject or an annotation appearance, and an inline image (BI ... EI).</summary>
public sealed partial class ImagePlacement
{
    /// <summary>The form or appearance stream that draws this placement, when it is not the page's content.</summary>
    internal PdfStream? SourceStream;

    /// <summary>The resources the <see cref="SourceStream"/> draws with.</summary>
    internal PdfDictionary? SourceResources;

    /// <summary>The bytes of the drawing instruction in the decoded <see cref="SourceStream"/>: from the end of the
    /// instruction before it to the end of its own (its <c>Do</c>, or the <c>EI</c> of an inline image).</summary>
    internal int SpanStart = -1;
    internal int SpanEnd = -1;

    /// <summary>Ordinal of this inline image among the page-level inline images (0-based); -1 otherwise.</summary>
    internal int PageLevelInlineOrdinal = -1;

    // An inline image's three operators in the page's operator list: BI, ID, EI.
    private const int InlineImageOperators = 3;

    /// <summary>Blank the drawing instruction in its form or appearance stream. An appearance or a pattern is
    /// edited where it is; a form the page's content draws may be drawn elsewhere too, so the page gets a copy
    /// of it (<see cref="HideInOwnForm"/>). The span keeps its length, so the spans of the stream's other
    /// placements stay where they are.</summary>
    private bool HideInSourceStream() =>
        SourceStream is not null && (FormUse is null ? OverwriteSpan(string.Empty) : HideInOwnForm());

    private bool OverwriteSpan(string replacement)
    {
        if (SourceStream is null || SourceReader is null || SpanStart < 0) return false;
        byte[] content;
        try { content = SourceReader.DecodeStream(SourceStream); }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IndexOutOfRangeException)
        {
            return false;
        }
        if (SpanEnd > content.Length) return false;
        var text = Compat.Latin1.GetBytes(" " + replacement + " ");
        var length = SpanEnd - SpanStart;
        byte[] edited;
        if (text.Length <= length)
        {
            edited = (byte[])content.Clone();
            for (var i = SpanStart; i < SpanEnd; i++) edited[i] = (byte)' ';
            Buffer.BlockCopy(text, 0, edited, SpanStart, text.Length);
        }
        else
        {
            edited = new byte[content.Length - length + text.Length];
            Buffer.BlockCopy(content, 0, edited, 0, SpanStart);
            Buffer.BlockCopy(text, 0, edited, SpanStart, text.Length);
            Buffer.BlockCopy(content, SpanEnd, edited, SpanStart + text.Length, content.Length - SpanEnd);
        }
        SourceStream.ReplaceData(edited);
        SourceStream.Dict.Remove("Filter");
        SourceStream.Dict.Remove("DecodeParms");
        SourceStream.Dict.Set("Length", new PdfInteger(edited.Length));
        return true;
    }

    /// <summary>The 1-based index of this inline image's BI among the page's operators, or -1. The image is
    /// found by its ID, the one of its three operators always parsed to its own type; BI goes before it.</summary>
    private int PageInlineIndex()
    {
        var ops = Page.Contents;
        var seen = 0;
        for (var i = 2; i <= ops.Count; i++)
        {
            if (ops[i] is Aspose.Pdf.Operators.ID && seen++ == PageLevelInlineOrdinal)
                return i + 1 <= ops.Count ? i - 1 : -1;
        }
        return -1;
    }

    /// <summary>Remove an inline image from the page's content, or draw <paramref name="replacement"/> (a
    /// registered image XObject) where it was.</summary>
    private bool EditPageInlineImage(string? replacement)
    {
        if (PageLevelInlineOrdinal < 0) return false;
        var at = PageInlineIndex();
        if (at < 0) return false;
        var ops = Page.Contents;
        for (var i = 0; i < InlineImageOperators; i++) ops.Delete(at);
        if (replacement is not null) ops.Insert(at, new Aspose.Pdf.Operators.Do(replacement));
        return true;
    }

    /// <summary>Replace an inline image: the new picture becomes an image XObject of the resources the image was
    /// drawn with, and its <c>Do</c> takes the place of the BI ... EI (an inline image and an image XObject both
    /// fill the unit square of the current matrix).</summary>
    private void ReplaceInlineImage(byte[] imageFile)
    {
        var stamp = new ImageStamp(new MemoryStream(imageFile));
        if (SourceStream is null)
        {
            if (PageLevelInlineOrdinal >= 0) EditPageInlineImage(stamp.RegisterXObject(Page));
            return;
        }
        if (SourceResources is null) return;
        var xobjects = SourceReader?.ResolveDict(SourceResources.Get("XObject"));
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            SourceResources.Set("XObject", xobjects);
        }
        var n = 1;
        while (xobjects.ContainsKey("Im" + n)) n++;
        var name = "Im" + n;
        xobjects.Set(name, stamp.BuildImageXObject());
        OverwriteSpan($"/{name} Do");
    }
}
