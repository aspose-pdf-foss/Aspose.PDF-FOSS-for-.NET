namespace Aspose.Pdf.Facades;

/// <summary>
/// Bag of <c>const int</c> bit-flags describing viewer preferences
/// (page mode, page layout, fullscreen behaviour, duplex, print scaling,
/// reading direction). Combined with bitwise OR and tested with
/// bitwise AND; the underlying integer is what
/// <see cref="PdfContentEditor.ChangeViewerPreference(int)"/> consumes.
/// </summary>
public class ViewerPreference
{
    /// <summary>Default constructor matches the Aspose.PDF for .NET reflection
    /// signature; the class carries no instance state.</summary>
    public ViewerPreference() { }

    // ── PageMode values (mutually exclusive, bits 0-7) ──────────────────────
    /// <summary>Neither document outline nor thumbnail images visible.</summary>
    public const int PageModeUseNone = 0x0001;
    /// <summary>Document outline visible.</summary>
    public const int PageModeUseOutlines = 0x0002;
    /// <summary>Thumbnail images visible.</summary>
    public const int PageModeUseThumbs = 0x0004;
    /// <summary>Full-screen mode.</summary>
    public const int PageModeFullScreen = 0x0008;
    /// <summary>Optional content group panel visible.</summary>
    public const int PageModeUseOC = 0x0010;
    /// <summary>Attachments panel visible.</summary>
    public const int PageModeUseAttachment = 0x0020;

    // ── PageLayout values (bits 8-15) ───────────────────────────────────────
    /// <summary>Display one page at a time.</summary>
    public const int PageLayoutSinglePage = 0x0100;
    /// <summary>Display the pages in one column.</summary>
    public const int PageLayoutOneColumn = 0x0200;
    /// <summary>Display the pages in two columns, odd pages on the left.</summary>
    public const int PageLayoutTwoColumnLeft = 0x0400;
    /// <summary>Display the pages in two columns, odd pages on the right.</summary>
    public const int PageLayoutTwoColumnRight = 0x0800;
    /// <summary>Display the pages two at a time, odd pages on the left
    /// (FOSS extension; Aspose.PDF for .NET does not expose this flag).</summary>
    public const int PageLayoutTwoPageLeft = 0x1000;
    /// <summary>Display the pages two at a time, odd pages on the right
    /// (FOSS extension; Aspose.PDF for .NET does not expose this flag).</summary>
    public const int PageLayoutTwoPageRight = 0x2000;

    // ── ViewerPreferences dictionary flags (bits 16+) ───────────────────────
    /// <summary>Hide the menu bar.</summary>
    public const int HideMenubar = 0x010000;
    /// <summary>Hide the toolbar.</summary>
    public const int HideToolbar = 0x020000;
    /// <summary>Hide window UI elements.</summary>
    public const int HideWindowUI = 0x040000;
    /// <summary>Resize the document window to fit the first displayed page.</summary>
    public const int FitWindow = 0x080000;
    /// <summary>Position the document window in the center of the screen.</summary>
    public const int CenterWindow = 0x100000;
    /// <summary>Display the document title in the title bar.</summary>
    public const int DisplayDocTitle = 0x200000;

    // ── Non-full-screen page-mode behaviour (bits 24-27) ────────────────────
    /// <summary>In full-screen mode, neither outlines nor thumbnails are
    /// shown when the viewer exits to a non-full-screen state.</summary>
    public const int NonFullScreenPageModeUseNone = 0x01000000;
    /// <summary>In full-screen mode, show outlines after exit.</summary>
    public const int NonFullScreenPageModeUseOutlines = 0x02000000;
    /// <summary>In full-screen mode, show thumbnails after exit.</summary>
    public const int NonFullScreenPageModeUseThumbs = 0x04000000;
    /// <summary>In full-screen mode, show the optional-content group
    /// panel after exit.</summary>
    public const int NonFullScreenPageModeUseOC = 0x08000000;

    // ── Reading direction (bits 28-29) ──────────────────────────────────────
    /// <summary>Left-to-right reading order (default for Western scripts).</summary>
    public const int DirectionL2R = 0x10000000;
    /// <summary>Right-to-left reading order (Hebrew, Arabic, vertical CJK).</summary>
    public const int DirectionR2L = 0x20000000;

    // ── Print scaling (bits 30-31, in the lower 24-bit "print" group) ───────
    //
    // These overlap the upper bits of the int; the print preferences are
    // carried in a separate /PrintScaling entry of the viewer-preferences
    // dictionary and are not bit-combined with the page-mode flags.
    /// <summary>Page content is centred and scaled to fit the printer page.</summary>
    public const int PrintScalingAppDefault = 0x0040;
    /// <summary>Page content is printed at actual size, with no scaling.</summary>
    public const int PrintScalingNone = 0x0080;

    // ── Duplex (separate /Duplex entry; values stay small to avoid bit clashes) ─
    /// <summary>Print single-sided.</summary>
    public const int Simplex = 0x0001 << 4;
    /// <summary>Print duplex, flipping along the long edge of the page (book binding).</summary>
    public const int DuplexFlipLongEdge = 0x0002 << 4;
    /// <summary>Print duplex, flipping along the short edge of the page (calendar binding).</summary>
    public const int DuplexFlipShortEdge = 0x0004 << 4;

    /// <summary>When set, the viewer picks the printer paper tray that
    /// matches each page's PDF page size (auto tray selection).</summary>
    public const int PickTrayByPDFSize = 0x0008 << 4;
}
