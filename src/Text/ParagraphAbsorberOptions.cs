using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Options for <see cref="ParagraphAbsorber"/> controlling section detection thresholds.
/// </summary>
public sealed class ParagraphAbsorberOptions
{
    private double _vOverride = double.NaN;
    private double _hOverride = double.NaN;

    /// <summary>
    /// Vertical distance override (as fraction of font size) below which lines are
    /// considered part of the same section. NaN means "use the default heuristic".
    /// </summary>
    public double SectionUnbreakingVerticalOverride
    {
        get => _vOverride;
        set => _vOverride = value;
    }

    /// <summary>
    /// Horizontal distance override (as fraction of page width) below which fragments
    /// are considered part of the same section. NaN means "use the default heuristic".
    /// </summary>
    public double SectionUnbreakingHorizontalOverride
    {
        get => _hOverride;
        set => _hOverride = value;
    }

    internal bool HasVerticalOverride => !double.IsNaN(_vOverride);
    internal bool HasHorizontalOverride => !double.IsNaN(_hOverride);

    /// <summary>Optional rectangle restricting the absorber to a page region. Stored only.</summary>
    public Aspose.Pdf.Rectangle? SearchRectangle { get; set; }
}
