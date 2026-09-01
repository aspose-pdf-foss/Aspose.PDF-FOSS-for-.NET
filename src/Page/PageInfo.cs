namespace Aspose.Pdf;

/// <summary>
/// Container for page dimension and margin properties. Used both as a live
/// view on a <see cref="Page"/> (when accessed via <c>page.PageInfo</c>) and as
/// a free-standing descriptor held by <c>Document.PageInfo</c> / load options.
/// </summary>
public sealed class PageInfo
{
    private readonly Page? _page;
    // Default to A4 (595x842), i.e. `PageSize.A4 = new PageSize(595f, 842f)`,
    // assigned by the parameterless ctor. The documented default is A4,
    // not US Letter — every A4-based layout depends on it.
    private double _width = 595;
    private double _height = 842;

    /// <summary>Create a free-standing PageInfo (not bound to any page).</summary>
    public PageInfo() { }

    /// <summary>Bound constructor used by <c>Page.PageInfo</c>. A page's margins default
    /// to the same values the layout engine falls back to (90 pt left/right, 72 pt
    /// top/bottom) so reading <c>page.PageInfo.Margin</c> reports the effective margins
    /// rather than bare zeros.</summary>
    internal PageInfo(Page page)
    {
        _page = page;
        // Effective defaults, NOT marked user-set: layout must still see this
        // page's margins as untouched so a document-level margin (set via
        // Document.PageInfo even after the page was added) can take effect.
        // Field assignment: the property setter would flag MarginAssigned.
        _margin = MarginInfo.Defaults(90, 72, 90, 72);
    }

    /// <summary>Page width in points. A size-INHERITED bound page (no-size
    /// Pages.Insert) reports the free-standing A4 default until the caller
    /// explicitly sizes it — PageInfo is a descriptor with A4
    /// defaults, not a view on the inherited media box (TOC layout computes
    /// its column widths from 595 even inside a US-Letter document).</summary>
    public double Width
    {
        get => _authoredWidth ?? (_page is not null ? (_page.SizeInherited ? _width : _page.MediaBox.Width) : _width);
        set
        {
            LandscapeSwapApplied = false;
            _authoredWidth = _authoredHeight = null;
            WidthAssigned = true;
            if (_page is not null)
            {
                var h = _page.MediaBox.Height;
                _page.MediaBox = new Rectangle(0, 0, value, h);
                _page.SizeInherited = false;
            }
            else
            {
                _width = value;
            }
        }
    }

    /// <summary>The caller AUTHORED a page width (as distinct from the A4 default).
    /// An HTML import that would otherwise grow the sheet to fit a wide table keeps
    /// the authored width instead and lets the content overflow, the way a browser
    /// printing to a fixed paper size does.</summary>
    internal bool WidthAssigned { get; private set; }

    /// <summary>Page height in points. See <see cref="Width"/> for the
    /// size-inherited rule.</summary>
    public double Height
    {
        get => _authoredHeight ?? (_page is not null ? (_page.SizeInherited ? _height : _page.MediaBox.Height) : _height);
        set
        {
            LandscapeSwapApplied = false;
            _authoredWidth = _authoredHeight = null;
            if (_page is not null)
            {
                var w = _page.MediaBox.Width;
                _page.MediaBox = new Rectangle(0, 0, w, value);
                _page.SizeInherited = false;
            }
            else
            {
                _height = value;
            }
        }
    }

    /// <summary>Landscape explicitly requested via the setter — remembered so layout
    /// can re-apply the swap after later explicit Width/Height assignments
    /// (orientation is resolved when the page is laid out, not at set time).</summary>
    internal bool LandscapeRequested { get; private set; }

    /// <summary>True when the <see cref="IsLandscape"/> SETTER swapped the
    /// dimensions (the caller gave portrait Width/Height and asked for
    /// landscape). Consumers that ignore the landscape flag undo exactly this
    /// swap — dimensions the caller authored landscape stand.</summary>
    internal bool LandscapeSwapApplied { get; private set; }

    /// <summary>Re-apply a requested landscape orientation: if landscape was set but a
    /// subsequent Width/Height assignment left the page portrait, swap the dimensions.</summary>
    internal void ApplyRequestedOrientation()
    {
        // Layout has consumed the geometry: the authored numbers stop shadowing the
        // media box FIRST, so the test below reads the box itself and a page the setter
        // already turned is not turned back.
        _authoredWidth = _authoredHeight = null;
        if (LandscapeRequested && Height > Width)
            SwapDimensions();
    }

    /// <summary>The Width/Height the CALLER assigned, kept while a landscape request
    /// has already turned the media box sideways. These are not swapped at set
    /// time — after `Width = 595; Height = 842; IsLandscape = true` it still
    /// reports 595 x 842 (with IsLandscape true) and only the saved page is 842 x 595 —
    /// and callers size content from that read (a caller derives its column widths from
    /// `PageInfo.Height` AFTER the flag and expects 842). FOSS turns the MEDIA BOX at
    /// once because its layout, header and background passes measure the page through
    /// it; these shadow the reads so both sides see what the generator gives them.</summary>
    private double? _authoredWidth;
    private double? _authoredHeight;

    /// <summary>Swap width and height for an orientation change. Goes through the
    /// public setters (they carry the bound-page media-box update) but preserves
    /// <see cref="WidthAssigned"/>: turning a page landscape is not the caller
    /// AUTHORING a width, and must not pin the sheet against the HTML auto-widen.</summary>
    private void SwapDimensions()
    {
        var authored = WidthAssigned;
        (Width, Height) = (Height, Width);
        WidthAssigned = authored;
    }

    /// <summary>Whether the page is in landscape orientation. On a bound page a
    /// requested landscape counts immediately (IsLandscape reads
    /// true right after the set) even though the dimension swap is
    /// deferred to layout.</summary>
    public bool IsLandscape
    {
        get => _page is not null ? (LandscapeRequested || Width > Height) : Width > Height;
        set
        {
            if (value) LandscapeRequested = true;
            else LandscapeRequested = false;
            // A SIZE-INHERITED bound page (no-size Pages.Insert) defers the
            // swap to layout (such a page is resolved from its
            // PageInfo A4 defaults then, and until layout PageInfo.Width
            // keeps reporting the portrait default — TOC-layout callers
            // size their columns from the pre-swap Width).
            if (_page is { SizeInherited: true }) return;
            // Other bound pages keep the immediate swap, but only ESTABLISH
            // landscape; never revert an already-landscape page to portrait
            // (an HTML conversion's auto-sized wide media box must not be
            // rotated back under its laid-out content).
            if (_page is not null && !value) return;
            var isCurrentlyLandscape = Width > Height;
            if (isCurrentlyLandscape == value) return;
            var authoredW = Width;
            var authoredH = Height;
            SwapDimensions();
            if (value)
            {
                LandscapeSwapApplied = true;
                if (_page is not null) { _authoredWidth = authoredW; _authoredHeight = authoredH; }
            }
        }
    }

    /// <summary>Page margins.</summary>
    public MarginInfo Margin
    {
        get => _margin;
        set { _margin = value; MarginAssigned = true; }
    }
    private MarginInfo _margin = new MarginInfo();

    /// <summary>True when the caller ASSIGNED a MarginInfo (even an untouched
    /// all-zero one) — an HTML load treats that as explicit zero margins, distinct
    /// from the never-touched default that gets the renderer's fallback margins.</summary>
    internal bool MarginAssigned { get; private set; }

    /// <summary>The margin for the pages an HTML flow generates after the first —
    /// <see cref="Margin"/> covers the first page alone once this one is assigned.
    /// Until then it reads through to <see cref="Margin"/>, so a caller that only
    /// ever sets <c>Margin</c> keeps one margin for every page.</summary>
    public MarginInfo AnyMargin
    {
        get => _anyMargin ?? Margin;
        set { _anyMargin = value; AnyMarginAssigned = true; }
    }
    private MarginInfo? _anyMargin;

    /// <summary>True when the caller assigned <see cref="AnyMargin"/> in its own
    /// right, i.e. asked for continuation pages to differ from the first.</summary>
    internal bool AnyMarginAssigned { get; private set; }

    private Aspose.Pdf.Text.TextState? _defaultTextState;

    /// <summary>Default text state applied to content laid out via this PageInfo. A live
    /// default: every PageInfo carries one, so a caller sets its face or size directly
    /// (<c>doc.PageInfo.DefaultTextState.Font = …</c>) without creating it first.</summary>
    public Aspose.Pdf.Text.TextState? DefaultTextState
    {
        get => _defaultTextState ??= new Aspose.Pdf.Text.TextState();
        set => _defaultTextState = value;
    }

    /// <summary>The default text state only when the caller touched it — the
    /// layout asks this so an untouched default never shadows a page's own.</summary>
    internal Aspose.Pdf.Text.TextState? AssignedDefaultTextState => _defaultTextState;

    /// <summary>Page height minus top+bottom margins.</summary>
    public double PureHeight => System.Math.Max(0, Height - Margin.Top - Margin.Bottom);

    /// <summary>Take every authored property of <paramref name="other"/> — a
    /// free-standing descriptor assigned to <c>page.PageInfo</c> — onto this bound
    /// instance, so the page itself is resized and re-margined. Assigning the
    /// descriptor object wholesale must do what setting its properties one by one
    /// does; a caller that then goes on to set <c>page.PageInfo.Height</c> keeps
    /// talking to the page. A landscape REQUEST carries over unresolved: like the
    /// property path, the swap is settled when layout consumes the geometry.</summary>
    internal void CopyFrom(PageInfo other)
    {
        Width = other.Width;
        Height = other.Height;
        if (other.LandscapeRequested) IsLandscape = true;
        // A caller that MUTATES the descriptor's margins — `info.Margin.Left = 20` —
        // has authored them just as surely as one that assigns a whole MarginInfo, and
        // that is how the API reads in the wild (callers build a PageInfo side by
        // side). Taking only the assigned case left such a page on the layout's own
        // fallback margins, 90/72 instead of the authored 20/5.
        if (other.MarginAssigned || other._margin.IsTouched) Margin = other._margin.CloneWithFlags();
        if (other.AnyMarginAssigned) AnyMargin = other.AnyMargin;
        if (other._defaultTextState is not null) DefaultTextState = other._defaultTextState;
    }

    /// <summary>Copy the authored margins (per-side flags kept, the MarginInfo
    /// cloned so the pages stay independent) and default text state of a
    /// source page's descriptor onto a page the layout generated from it.</summary>
    internal void InheritFrom(PageInfo other)
    {
        if (other.MarginAssigned || other._margin.IsTouched)
            Margin = other._margin.CloneWithFlags();
        if (other._defaultTextState is not null) _defaultTextState = other._defaultTextState;
    }

    /// <summary>Shallow clone — Margin is shared by reference.</summary>
    public object Clone() => new PageInfo
    {
        Width = Width,
        Height = Height,
        _margin = _margin,           // field: cloning must not flag MarginAssigned
        MarginAssigned = MarginAssigned,
        // …but the Width/Height setters above DID run, so carry the authored flag
        // truthfully rather than letting the clone look caller-sized.
        WidthAssigned = WidthAssigned,
        _defaultTextState = _defaultTextState,
    };
}
