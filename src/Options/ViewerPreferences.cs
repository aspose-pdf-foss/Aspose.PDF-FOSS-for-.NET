using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Page layout mode for the document (PDF32000 §12.2, Table 28).
/// </summary>
public enum PageLayoutMode
{
    /// <summary>Display one page at a time.</summary>
    SinglePage,
    /// <summary>Display pages in one continuous column.</summary>
    OneColumn,
    /// <summary>Display pages in two columns, odd pages on the left.</summary>
    TwoColumnLeft,
    /// <summary>Display pages in two columns, odd pages on the right.</summary>
    TwoColumnRight,
    /// <summary>Display pages two at a time, odd pages on the left.</summary>
    TwoPageLeft,
    /// <summary>Display pages two at a time, odd pages on the right.</summary>
    TwoPageRight,
}

/// <summary>
/// Page mode specifying how the document should be displayed on opening (PDF32000 §12.2, Table 28).
/// </summary>
public enum PageModeValue
{
    /// <summary>Neither outlines nor thumbnails visible.</summary>
    UseNone,
    /// <summary>Outline panel visible.</summary>
    UseOutlines,
    /// <summary>Thumbnail panel visible.</summary>
    UseThumbs,
    /// <summary>Full-screen mode.</summary>
    FullScreen,
    /// <summary>Optional content group panel visible.</summary>
    UseOC,
    /// <summary>Attachments panel visible.</summary>
    UseAttachments,
}

/// <summary>
/// Represents the document's viewer preferences (PDF32000 §12.2).
/// Controls how the document is displayed when opened in a viewer.
/// </summary>
public sealed class ViewerPreferences
{
    private readonly PdfDictionary _dict;
    private readonly PdfDictionary? _catalog;

    internal ViewerPreferences(PdfDictionary dict, PdfDictionary? catalog = null)
    {
        _dict = dict;
        _catalog = catalog;
    }

    /// <summary>Create default viewer preferences.</summary>
    public ViewerPreferences() : this(new PdfDictionary()) { }

    /// <summary>Hide the menu bar.</summary>
    public bool HideMenubar
    {
        get => GetBool("HideMenubar");
        set => SetBool("HideMenubar", value);
    }

    /// <summary>Hide the toolbar.</summary>
    public bool HideToolbar
    {
        get => GetBool("HideToolbar");
        set => SetBool("HideToolbar", value);
    }

    /// <summary>Hide the window UI elements.</summary>
    public bool HideWindowUI
    {
        get => GetBool("HideWindowUI");
        set => SetBool("HideWindowUI", value);
    }

    /// <summary>Resize the document window to fit the first displayed page.</summary>
    public bool FitWindow
    {
        get => GetBool("FitWindow");
        set => SetBool("FitWindow", value);
    }

    /// <summary>Position the window in the center of the screen.</summary>
    public bool CenterWindow
    {
        get => GetBool("CenterWindow");
        set => SetBool("CenterWindow", value);
    }

    /// <summary>Display the document title in the title bar.</summary>
    public bool DisplayDocTitle
    {
        get => GetBool("DisplayDocTitle");
        set => SetBool("DisplayDocTitle", value);
    }

    /// <summary>
    /// Reading direction: "L2R" (left-to-right) or "R2L" (right-to-left).
    /// </summary>
    public string Direction
    {
        get => _dict.GetName("Direction") ?? "L2R";
        set => _dict.Set("Direction", new PdfName(value));
    }

    /// <summary>
    /// Page scaling for printing: "None" or "AppDefault".
    /// </summary>
    public string? PrintScaling
    {
        get => _dict.GetName("PrintScaling");
        set
        {
            if (value is null)
                _dict.Remove("PrintScaling");
            else
                _dict.Set("PrintScaling", new PdfName(value));
        }
    }

    /// <summary>
    /// Duplex printing mode: "Simplex", "DuplexFlipShortEdge", "DuplexFlipLongEdge".
    /// </summary>
    public string? Duplex
    {
        get => _dict.GetName("Duplex");
        set
        {
            if (value is null)
                _dict.Remove("Duplex");
            else
                _dict.Set("Duplex", new PdfName(value));
        }
    }

    /// <summary>
    /// Page layout mode. Reads/writes the /PageLayout entry in the catalog dictionary.
    /// Returns null if no catalog reference is available or the key is not set.
    /// </summary>
    public PageLayoutMode? PageLayout
    {
        get
        {
            var name = _catalog?.GetName("PageLayout");
            return name switch
            {
                "SinglePage" => PageLayoutMode.SinglePage,
                "OneColumn" => PageLayoutMode.OneColumn,
                "TwoColumnLeft" => PageLayoutMode.TwoColumnLeft,
                "TwoColumnRight" => PageLayoutMode.TwoColumnRight,
                "TwoPageLeft" => PageLayoutMode.TwoPageLeft,
                "TwoPageRight" => PageLayoutMode.TwoPageRight,
                _ => null,
            };
        }
        set
        {
            if (_catalog is null) return;
            if (value is null)
            {
                _catalog.Remove("PageLayout");
                return;
            }
            var name = value switch
            {
                PageLayoutMode.SinglePage => "SinglePage",
                PageLayoutMode.OneColumn => "OneColumn",
                PageLayoutMode.TwoColumnLeft => "TwoColumnLeft",
                PageLayoutMode.TwoColumnRight => "TwoColumnRight",
                PageLayoutMode.TwoPageLeft => "TwoPageLeft",
                PageLayoutMode.TwoPageRight => "TwoPageRight",
                _ => "SinglePage",
            };
            _catalog.Set("PageLayout", new PdfName(name));
        }
    }

    /// <summary>
    /// Page mode specifying how the document is displayed on opening.
    /// Reads/writes the /PageMode entry in the catalog dictionary.
    /// Returns null if no catalog reference is available or the key is not set.
    /// </summary>
    public PageModeValue? PageMode
    {
        get
        {
            var name = _catalog?.GetName("PageMode");
            return name switch
            {
                "UseNone" => PageModeValue.UseNone,
                "UseOutlines" => PageModeValue.UseOutlines,
                "UseThumbs" => PageModeValue.UseThumbs,
                "FullScreen" => PageModeValue.FullScreen,
                "UseOC" => PageModeValue.UseOC,
                "UseAttachments" => PageModeValue.UseAttachments,
                _ => null,
            };
        }
        set
        {
            if (_catalog is null) return;
            if (value is null)
            {
                _catalog.Remove("PageMode");
                return;
            }
            var name = value switch
            {
                PageModeValue.UseNone => "UseNone",
                PageModeValue.UseOutlines => "UseOutlines",
                PageModeValue.UseThumbs => "UseThumbs",
                PageModeValue.FullScreen => "FullScreen",
                PageModeValue.UseOC => "UseOC",
                PageModeValue.UseAttachments => "UseAttachments",
                _ => "UseNone",
            };
            _catalog.Set("PageMode", new PdfName(name));
        }
    }

    /// <summary>The underlying dictionary.</summary>
    internal PdfDictionary Dict => _dict;

    private bool GetBool(string key)
    {
        var obj = _dict.Get(key);
        return obj is PdfBoolean b && b.Value;
    }

    private void SetBool(string key, bool value)
    {
        if (value)
            _dict.Set(key, PdfBoolean.True);
        else
            _dict.Remove(key);
    }
}
