namespace Aspose.Pdf.Facades;

/// <summary>
/// A single bookmark extracted from a document's outline tree by
/// <see cref="PdfBookmarkEditor.ExtractBookmarks()"/>. Mirrors the
/// shape exposed by the public Aspose.PDF for .NET facade.
/// </summary>
public sealed class Bookmark
{
    /// <summary>The bookmark title.</summary>
    public string? Title { get; set; }

    /// <summary>1-based page number the bookmark targets, or 0 if the
    /// destination cannot be resolved.</summary>
    public int PageNumber { get; set; }

    /// <summary>The destination as a string. For named destinations this
    /// is the name; for explicit destinations the resolved page index
    /// (1-based) as a string. Empty when the bookmark has no destination.</summary>
    public string? Destination { get; set; }

    /// <summary>The action type identifier ("GoTo", "URI", "Launch", etc.)
    /// when the outline item carries an /A action entry. Empty string when
    /// the bookmark uses /Dest (no action).</summary>
    public string? Action { get; set; }

    /// <summary>True when the bookmark title is rendered in bold.</summary>
    public bool BoldFlag { get; set; }

    /// <summary>True when the bookmark title is rendered in italic.</summary>
    public bool ItalicFlag { get; set; }

    /// <summary>Nesting level: 1 for top-level, 2 for first-level children, …</summary>
    public int Level { get; set; }

    /// <summary>The /F (Fit) display mode string from the destination, or null.</summary>
    public string? PageDisplay { get; set; }

    /// <summary>The remote file path for GoToR-style bookmarks, or null.</summary>
    public string? RemoteFile { get; set; }

    /// <summary>Title rendering color (defaults to black when /C is absent).</summary>
    public System.Drawing.Color TitleColor { get; set; } = System.Drawing.Color.Black;

    /// <summary>Child bookmarks in the original outline tree.</summary>
    public Bookmarks ChildItems { get; set; } = new Bookmarks();

    /// <summary>Singular alias for <see cref="ChildItems"/>. Some Aspose
    /// .NET callers refer to the child collection as ChildItem (no s).</summary>
    public Bookmarks ChildItem
    {
        get => ChildItems;
        set => ChildItems = value;
    }

    /// <summary>True when the bookmark renders its child outline items expanded.</summary>
    public bool Open { get; set; }

    /// <summary>Explicit-destination /XYZ left coordinate. Stored only.</summary>
    public int PageDisplay_Left { get; set; }

    /// <summary>Explicit-destination /XYZ top coordinate. Stored only.</summary>
    public int PageDisplay_Top { get; set; }

    /// <summary>Explicit-destination /FitR right coordinate. Stored only.</summary>
    public int PageDisplay_Right { get; set; }

    /// <summary>Explicit-destination /FitR bottom coordinate. Stored only.</summary>
    public int PageDisplay_Bottom { get; set; }

    /// <summary>Explicit-destination /XYZ zoom factor (0 = inherit). Stored only.</summary>
    public int PageDisplay_Zoom { get; set; }

    /// <summary>Custom Acrobat viewer menu actions associated with the bookmark.
    /// Stored only; the FOSS facade does not emit a /Named action group.
    /// (Property name preserves the public-API typo "Acorbat".)</summary>
    public System.Enum[]? CustomAcorbatViewerMenuActionName { get; set; }
}

/// <summary>
/// An ordered, indexable collection of <see cref="Bookmark"/> entries
/// returned by <see cref="PdfBookmarkEditor.ExtractBookmarks()"/>.
/// </summary>
public sealed class Bookmarks : List<Bookmark>
{
}
