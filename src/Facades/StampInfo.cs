namespace Aspose.Pdf.Facades;

/// <summary>
/// Contains information about a stamp on a page.
/// Stamps are content-stream blocks wrapped in q/Q save/restore pairs
/// that were added by stamp operations (AddStamp, PdfFileStamp, etc.).
/// Lives in <c>Aspose.Pdf.Facades</c> to match the Aspose.PDF for .NET surface.
/// </summary>
public sealed class StampInfo
{
    /// <summary>
    /// The stamp identifier. This is the /StampId value if one was stored
    /// in the content stream metadata, or -1 if none.
    /// </summary>
    public int StampId { get; internal set; } = -1;

    /// <summary>
    /// The 0-based index of this stamp on the page (order of q/Q blocks).
    /// </summary>
    public int IndexOnPage { get; internal set; }

    /// <summary>The type of stamp.</summary>
    public StampType StampType { get; internal set; }

    /// <summary>Whether the stamp is visible.</summary>
    public bool Visible { get; internal set; } = true;

    /// <summary>Text content, if this is a text stamp.</summary>
    public string? Text { get; internal set; }

    /// <summary>Image bytes, if this is an image stamp.</summary>
    public byte[]? ImageBytes { get; internal set; }

    /// <summary>Image content (System.Drawing.Image) for an image stamp.</summary>
    public System.Drawing.Image? Image
    {
        get
        {
            if (ImageBytes is null || ImageBytes.Length == 0) return null;
#pragma warning disable CA1416 // Validate platform compatibility
            try { return System.Drawing.Image.FromStream(new System.IO.MemoryStream(ImageBytes)); }
            catch { return null; }
#pragma warning restore CA1416
        }
    }

    /// <summary>The Form XObject for this stamp (form stamps only). Stored only.</summary>
    public XForm? Form { get; internal set; }

    /// <summary>The rectangle area of this stamp (if determinable).</summary>
    public Rectangle? Rect { get; internal set; }

    /// <summary>Alias for <see cref="Rect"/> matching the Aspose.PDF for .NET property name.</summary>
    public Rectangle? Rectangle => Rect;
}

/// <summary>Stamp kind exposed by <see cref="StampInfo"/>. Lives in Facades to match the Aspose.PDF for .NET reflection shape.</summary>
public enum StampType
{
    Form = 0,
    Image = 1,
    /// <summary>FOSS-only convenience value used by the stamp parser when it
    /// recognises a text-only block. Aspose.PDF for .NET omits this value.</summary>
    Text = 2,
}
