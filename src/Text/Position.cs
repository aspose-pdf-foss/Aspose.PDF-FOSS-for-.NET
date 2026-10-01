namespace Aspose.Pdf.Text;

/// <summary>A point on a page given as horizontal and vertical offsets in page coordinates, in points (1/72 inch), with y growing upward.</summary>
public sealed class Position
{
    /// <summary>Creates a position at the given horizontal and vertical offsets, in points.</summary>
    public Position(double xIndent, double yIndent)
    {
        // Assign the backing fields directly so construction does not set Touched —
        // only a later property write counts as the caller "setting" the position.
        _xIndent = xIndent;
        _yIndent = yIndent;
    }

    private double _xIndent;
    private double _yIndent;

    /// <summary>True once <see cref="XIndent"/>/<see cref="YIndent"/> has been
    /// written through a property setter (not via the constructor). Lets the owning
    /// <see cref="TextFragment"/> distinguish a position the caller explicitly set —
    /// e.g. <c>fragment.Position.XIndent = …</c> on a fresh fragment — from one that
    /// was merely auto-created when the (never-null) Position getter was read.</summary>
    internal bool Touched { get; private set; }

    /// <summary>Gets or sets the horizontal offset, in points.</summary>
    public double XIndent { get => _xIndent; set { _xIndent = value; Touched = true; } }
    /// <summary>Gets or sets the vertical offset, in points; PDF y grows upward.</summary>
    public double YIndent { get => _yIndent; set { _yIndent = value; Touched = true; } }

    public override bool Equals(object? obj)
        => obj is Position other
           && Math.Abs(XIndent - other.XIndent) < 0.001
           && Math.Abs(YIndent - other.YIndent) < 0.001;

    public override int GetHashCode()
        => HashCode.Combine(Math.Round(XIndent, 2), Math.Round(YIndent, 2));

    // Format: "( x, y )" with shortest-round-trip doubles ("( 25.92,
    // 661.138439991951 )") — tests log Position values and compare log LENGTHS.
    public override string ToString() => string.Format(
        System.Globalization.CultureInfo.InvariantCulture, "( {0}, {1} )", XIndent, YIndent);
}
