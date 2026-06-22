namespace Aspose.Pdf.Text;

/// <summary>
/// Options for text replacement operations.
/// </summary>
public sealed class TextReplaceOptions
{
    /// <summary>Default-constructed options (ReplaceAdjustmentAction = None).</summary>
    public TextReplaceOptions() { }

    /// <summary>Construct with a specific adjustment action.</summary>
    public TextReplaceOptions(ReplaceAdjustment adjustment)
    {
        ReplaceAdjustmentAction = adjustment;
    }

    /// <summary>Construct with a replace scope (REPLACE_FIRST or REPLACE_ALL).</summary>
    public TextReplaceOptions(Scope scope)
    {
        ReplaceScope = scope;
    }

    /// <summary>
    /// The adjustment action to apply when replacing text.
    /// </summary>
    public ReplaceAdjustment ReplaceAdjustmentAction { get; set; } = ReplaceAdjustment.None;

    /// <summary>How many matches to replace; REPLACE_FIRST by default (callers opt
    /// into replacing every match via REPLACE_ALL).</summary>
    public Scope ReplaceScope { get; set; } = Scope.REPLACE_FIRST;

    /// <summary>Font-size strategy applied when replacement text doesn't fit. Stored only.</summary>
    public FontSizeAdjustment FontSizeAdjustmentAction { get; set; } = FontSizeAdjustment.None;

    /// <summary>Whether paragraph boundaries are ignored when matching. Stored only.</summary>
    public bool IgnoreParagraphs { get; set; }

    /// <summary>Optional rectangle constraining where replacement applies. Stored only.</summary>
    public Rectangle? Rectangle { get; set; }

    /// <summary>Horizontal point adjustment applied at the left of replaced runs. Stored only.</summary>
    public double LeftAdjustment { get; set; }

    /// <summary>Horizontal point adjustment applied at the right of replaced runs. Stored only.</summary>
    public double RightAdjustment { get; set; }

    /// <summary>Vertical adjustment (extra leading) when a replacement causes a new line. Stored only.</summary>
    public double AdjustmentNewLineSpacing { get; set; }

    /// <summary>Replace-scope selector (canonical screaming-snake naming).</summary>
    public enum Scope
    {
        REPLACE_FIRST = 0,
        REPLACE_ALL = 1,
    }

    /// <summary>
    /// Font size adjustment strategy when replaced text has a different size.
    /// </summary>
    public enum FontSizeAdjustment
    {
        /// <summary>No adjustment.</summary>
        None,
        /// <summary>Decrease font size to fit.</summary>
        Decrease,
        /// <summary>Shrink to fit.</summary>
        ShrinkToFit,
        /// <summary>Increase font size to fill.</summary>
        Increase,
        /// <summary>Scale the font size so the replaced run exactly fills the original glyph box.</summary>
        ScaleToFill,
    }

    /// <summary>
    /// Specifies how text replacement adjustments are handled.
    /// </summary>
    public enum ReplaceAdjustment
    {
        /// <summary>No adjustment.</summary>
        None,
        /// <summary>Adjust spacing between words only.</summary>
        AdjustSpaceWidth,
        /// <summary>Whole words hyphenation — split long replacement text across lines.</summary>
        WholeWordsHyphenation,
        /// <summary>Shift following rows (the rest of the page).</summary>
        ShiftRestOfContents,
        /// <summary>Shift the rest of the same line only — narrower than
        /// <see cref="ShiftRestOfContents"/>; value-distinct so callers can
        /// branch on it later.</summary>
        ShiftRestOfLine,
        /// <summary>Form-filling mode — replacement respects the surrounding field constraints.</summary>
        IsFormFillingMode,
    }
}
