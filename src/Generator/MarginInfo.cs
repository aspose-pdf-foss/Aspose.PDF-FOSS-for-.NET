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

    public MarginInfo() { }

    public MarginInfo(double left, double bottom, double right, double top)
    {
        _left = left; _bottom = bottom; _right = right; _top = top;
        TopTouched = BottomTouched = LeftTouched = RightTouched = true;
    }

    /// <summary>Shallow clone.</summary>
    public object Clone() => new MarginInfo(_left, _bottom, _right, _top);
}
