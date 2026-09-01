namespace Aspose.Pdf;

using Aspose.Pdf.Core;

/// <summary>Per-page additional actions (PDF 32000 §12.6.3 /AA entries), backed by
/// the page dictionary so assigned actions survive save → reload.</summary>
public class PageActionCollection
{
    private readonly Page _page;

    internal PageActionCollection(Page page) { _page = page; }

    /// <summary>Action fired when the page is opened in the viewer (/AA /O).</summary>
    public Aspose.Pdf.Annotations.PdfAction? OnOpen
    {
        get => GetAction("O");
        set => SetAction("O", value);
    }

    /// <summary>Action fired when the page is closed in the viewer (/AA /C).</summary>
    public Aspose.Pdf.Annotations.PdfAction? OnClose
    {
        get => GetAction("C");
        set => SetAction("C", value);
    }

    /// <summary>Remove all page-level additional actions (clears /AA open and close).</summary>
    public void RemoveActions()
    {
        OnOpen = null;
        OnClose = null;
    }

    private PdfDictionary? AaDict(bool create)
    {
        var reader = _page.Reader;
        if ((reader is null ? _page.Dict.Get("AA") : reader.Resolve(_page.Dict.Get("AA"))) is PdfDictionary aa)
            return aa;
        if (!create) return null;
        var fresh = new PdfDictionary();
        _page.Dict.Set("AA", fresh);
        return fresh;
    }

    private Aspose.Pdf.Annotations.PdfAction? GetAction(string key)
    {
        var aa = AaDict(create: false);
        var reader = _page.Reader;
        var obj = aa?.Get(key);
        if (reader is not null) obj = reader.Resolve(obj);
        return obj is PdfDictionary actionDict
            ? Aspose.Pdf.Annotations.PdfAction.Create(actionDict, reader!)
            : null;
    }

    private void SetAction(string key, Aspose.Pdf.Annotations.PdfAction? action)
    {
        if (action is null)
        {
            var aa = AaDict(create: false);
            if (aa is null) return;
            aa.Remove(key);
            if (aa.Count == 0) _page.Dict.Remove("AA");
            return;
        }
        AaDict(create: true)!.Set(key, action.Dict);
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
    public Watermark(System.Drawing.Image image) { _image = image; }

    public Watermark(System.Drawing.Image image, Rectangle rect)
    {
        _image = image;
        Position = rect;
    }

    /// <summary>Creates an unavailable watermark (no image). Returned by
    /// <see cref="Page.Watermark"/> when the page carries no watermark, so callers
    /// can test <see cref="Available"/> without a null check.</summary>
    internal Watermark() { }

    /// <summary>The page HAS a watermark, but its image is a
    /// <see cref="System.Drawing.Image"/> and this host has no GDI+ to build one.
    /// Reporting "no watermark" there would be a wrong answer to a question that has
    /// nothing to do with the platform - the artifact is in the file either way - so the
    /// presence is reported truthfully and only the IMAGE says it cannot be had.</summary>
    internal Watermark(bool imageNeedsPlatform) { _imageNeedsPlatform = imageNeedsPlatform; }

    private readonly bool _imageNeedsPlatform;
    private readonly System.Drawing.Image? _image;

    /// <summary>The watermark image (null when the watermark is unavailable). Throws
    /// <see cref="PlatformNotSupportedException"/> when the page carries a watermark whose
    /// image this host cannot decode.</summary>
    public System.Drawing.Image? Image => _imageNeedsPlatform
        ? throw new PlatformNotSupportedException(
            "The watermark's image is a System.Drawing.Image, which needs GDI+; this host has none.")
        : _image;

    /// <summary>Position rectangle on the page (null = whole page).</summary>
    public Rectangle? Position { get; }

    /// <summary>Whether the watermark is currently available for rendering.</summary>
    public bool Available => _image is not null || _imageNeedsPlatform;
}
