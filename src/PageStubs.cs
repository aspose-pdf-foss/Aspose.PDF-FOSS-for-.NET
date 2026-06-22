namespace Aspose.Pdf;

/// <summary>Per-page additional actions (PDF 32000 §12.6.3 /AA entries).</summary>
public class PageActionCollection
{
    /// <summary>Action fired when the page is opened in the viewer.</summary>
    public Aspose.Pdf.Annotations.PdfAction? OnOpen { get; set; }

    /// <summary>Action fired when the page is closed in the viewer.</summary>
    public Aspose.Pdf.Annotations.PdfAction? OnClose { get; set; }

    /// <summary>Remove all page-level additional actions (clears /AA open and close).</summary>
    public void RemoveActions()
    {
        OnOpen = null;
        OnClose = null;
    }
}

/// <summary>Group / blending colour space dictionary (PDF 32000 §11.6.5).</summary>
public class Group
{
    public Group(Page page) { _page = page; }
    private readonly Page _page;

    /// <summary>Colour space for the page-group's blending.</summary>
    public ColorSpace ColorSpace { get; set; } = ColorSpace.DeviceRGB;
}

/// <summary>Tab order applied to widget annotations on a page (PDF 32000 /Tabs entry).</summary>
public enum TabOrder
{
    None = 0,
    Default = 1,
    Row = 2,
    Column = 3,
    Manual = 4,
}

/// <summary>Watermark applied to a page.</summary>
public class Watermark
{
    public Watermark(System.Drawing.Image image) { Image = image; }

    public Watermark(System.Drawing.Image image, Rectangle rect)
    {
        Image = image;
        Position = rect;
    }

    /// <summary>The watermark image.</summary>
    public System.Drawing.Image Image { get; }

    /// <summary>Position rectangle on the page (null = whole page).</summary>
    public Rectangle? Position { get; }

    /// <summary>Whether the watermark is currently available for rendering.</summary>
    public bool Available => Image is not null;
}
