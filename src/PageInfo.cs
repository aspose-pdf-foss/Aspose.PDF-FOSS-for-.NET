namespace Aspose.Pdf;

/// <summary>
/// Container for page dimension and margin properties. Used both as a live
/// view on a <see cref="Page"/> (when accessed via <c>page.PageInfo</c>) and as
/// a free-standing descriptor held by <c>Document.PageInfo</c> / load options.
/// </summary>
public sealed class PageInfo
{
    private readonly Page? _page;
    // Default to A4 (595x842), matching Aspose.Pdf's PageInfo ctor
    // (`PageSize.A4 = new PageSize(595f, 842f)` and assignment from
    // the parameterless ctor). FOSS previously defaulted to US Letter; that
    // caused pixel-diff against A4-rendered templates.
    private double _width = 595;
    private double _height = 842;

    /// <summary>Create a free-standing PageInfo (not bound to any page).</summary>
    public PageInfo() { }

    /// <summary>Bound constructor used by <c>Page.PageInfo</c>. A page's margins default
    /// to the same values the layout engine falls back to (90 pt left/right, 72 pt
    /// top/bottom) so reading <c>page.PageInfo.Margin</c> reports the effective margins
    /// rather than bare zeros — matching Aspose.Pdf.</summary>
    internal PageInfo(Page page)
    {
        _page = page;
        Margin = new MarginInfo(90, 72, 90, 72);
    }

    /// <summary>Page width in points.</summary>
    public double Width
    {
        get => _page is not null ? _page.MediaBox.Width : _width;
        set
        {
            if (_page is not null)
            {
                var h = _page.MediaBox.Height;
                _page.MediaBox = new Rectangle(0, 0, value, h);
            }
            else
            {
                _width = value;
            }
        }
    }

    /// <summary>Page height in points.</summary>
    public double Height
    {
        get => _page is not null ? _page.MediaBox.Height : _height;
        set
        {
            if (_page is not null)
            {
                var w = _page.MediaBox.Width;
                _page.MediaBox = new Rectangle(0, 0, w, value);
            }
            else
            {
                _height = value;
            }
        }
    }

    /// <summary>Whether the page is in landscape orientation.</summary>
    public bool IsLandscape
    {
        get => Width > Height;
        set
        {
            var isCurrentlyLandscape = Width > Height;
            if (isCurrentlyLandscape == value) return;
            // On a page already bound to a live Page, only ESTABLISH landscape; do not revert an
            // already-landscape page to portrait, where setting
            // IsLandscape=false on a page created landscape (e.g. an HTML conversion done with
            // PageInfo.IsLandscape=true, whose media box was auto-sized to wide content) leaves
            // that box in place rather than rotating the laid-out content back to portrait.
            // Free-standing PageInfo keeps the symmetric swap so sizing via `new PageInfo` is unchanged.
            if (_page is not null && !value) return;
            (Width, Height) = (Height, Width);
        }
    }

    /// <summary>Page margins.</summary>
    public MarginInfo Margin { get; set; } = new MarginInfo();

    /// <summary>Whichever margin is currently active for layout passes; Aspose.Pdf alias for <see cref="Margin"/>.</summary>
    public MarginInfo AnyMargin
    {
        get => Margin;
        set => Margin = value;
    }

    /// <summary>Default text state applied to content laid out via this PageInfo. Stored only.</summary>
    public Aspose.Pdf.Text.TextState? DefaultTextState { get; set; }

    /// <summary>Page height minus top+bottom margins.</summary>
    public double PureHeight => System.Math.Max(0, Height - Margin.Top - Margin.Bottom);

    /// <summary>Shallow clone — Margin is shared by reference.</summary>
    public object Clone() => new PageInfo
    {
        Width = Width,
        Height = Height,
        Margin = Margin,
        DefaultTextState = DefaultTextState,
    };
}
