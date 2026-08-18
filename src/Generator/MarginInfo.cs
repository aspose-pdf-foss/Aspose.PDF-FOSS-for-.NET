namespace Aspose.Pdf;

/// <summary>
/// Represents margin information for page elements.
/// </summary>
public sealed class MarginInfo
{
    private double _top, _bottom, _left, _right;

    /// <summary>Top margin in PDF points.</summary>
    public double Top
    {
        get => _top;
        set { _top = value; TopTouched = true; }
    }

    /// <summary>Bottom margin in PDF points.</summary>
    public double Bottom
    {
        get => _bottom;
        set { _bottom = value; BottomTouched = true; }
    }

    /// <summary>Left margin in PDF points.</summary>
    public double Left
    {
        get => _left;
        set { _left = value; LeftTouched = true; }
    }

    /// <summary>Right margin in PDF points.</summary>
    public double Right
    {
        get => _right;
        set { _right = value; RightTouched = true; }
    }

    /// <summary>True once any margin setter has fired. Distinguishes a default-constructed
    /// MarginInfo (all zeros, never touched) from a user-set zero that should be respected.</summary>
    internal bool IsTouched => TopTouched || BottomTouched || LeftTouched || RightTouched;

    // Per-side touched flags. A caller that sets only Left/Right (the common
    // multi-column case) leaves Top/Bottom untouched, so layout falls back to the
    // default for those sides instead of laying out with zero T/B margin.
    internal bool TopTouched { get; private set; }
    internal bool BottomTouched { get; private set; }
    internal bool LeftTouched { get; private set; }
    internal bool RightTouched { get; private set; }

    /// <summary>Set only on the MarginInfo the default <c>HtmlLoadOptions.PageInfo</c>
    /// is created with. Sides mutated on THIS instance resolve per side (the untouched
    /// sides keep the HTML renderer defaults), while a caller-replaced PageInfo or
    /// MarginInfo is authored as a whole — its untouched sides are deliberate zeros.</summary>
    internal bool HtmlPerSideDefaults { get; set; }

    public MarginInfo() { }

    public MarginInfo(double left, double bottom, double right, double top)
    {
        _left = left; _bottom = bottom; _right = right; _top = top;
        TopTouched = BottomTouched = LeftTouched = RightTouched = true;
    }

    /// <summary>Defaults carrier: the values are visible to readers (a page's
    /// PageInfo reports the EFFECTIVE 90/72 margins) but no side is marked as
    /// user-set, so the layout engine still falls through to the
    /// document-level <c>Document.PageInfo.Margin</c> (so
    /// <c>doc.PageInfo.Margin.Left = 40</c> set after the pages were added
    /// still takes effect).</summary>
    internal static MarginInfo Defaults(double left, double bottom, double right, double top)
        => new() { _left = left, _bottom = bottom, _right = right, _top = top };

    /// <summary>Shallow clone.</summary>
    public object Clone() => new MarginInfo(_left, _bottom, _right, _top);
}
