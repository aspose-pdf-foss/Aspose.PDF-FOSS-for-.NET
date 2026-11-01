using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Options for <see cref="ParagraphAbsorber"/> controlling section detection thresholds.
/// </summary>
public sealed class ParagraphAbsorberOptions
{
    /// <summary>Creates options with no vertical or horizontal override (both <c>NaN</c>, so the default heuristics apply).</summary>
    public ParagraphAbsorberOptions() { }

    /// <summary>
    /// Vertical distance override (as fraction of font size) below which lines are
    /// considered part of the same section. NaN means "use the default heuristic".
    /// </summary>
    public double SectionUnbreakingVerticalOverride { get; set; } = double.NaN;

    /// <summary>
    /// Horizontal distance override (as fraction of page width) below which fragments
    /// are considered part of the same section. NaN means "use the default heuristic".
    /// </summary>
    public double SectionUnbreakingHorizontalOverride { get; set; } = double.NaN;

    internal bool HasVerticalOverride => !double.IsNaN(SectionUnbreakingVerticalOverride);
    internal bool HasHorizontalOverride => !double.IsNaN(SectionUnbreakingHorizontalOverride);

    /// <summary>Optional rectangle restricting the absorber to a page region. Stored only.</summary>
    public Aspose.Pdf.Rectangle? SearchRectangle { get; set; }
}
