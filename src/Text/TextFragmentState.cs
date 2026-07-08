namespace Aspose.Pdf.Text;

/// <summary>
/// Fragment-level text formatting state. Wraps the underlying
/// <see cref="TextState"/> on a <see cref="TextFragment"/> and exposes
/// the additional Aspose.Pdf-shape members (Font typed as
/// <see cref="Aspose.Pdf.Text.Font"/>, DrawTextRectangleBorder,
/// TabStops, IsFitRectangle).
/// </summary>
public class TextFragmentState : TextState
{
    private readonly TextFragment _fragment;

    /// <summary>Wrap the fragment's underlying state so writes flow back
    /// to it.</summary>
    public TextFragmentState(TextFragment fragment)
    {
        _fragment = fragment ?? throw new ArgumentNullException(nameof(fragment));
        OwnerFragment = fragment;
    }

    // ── new shadows so DeclaredOnly reflection surfaces these on the
    //    derived type (Aspose.Pdf reports them as declared on
    //    TextFragmentState, not inherited). All forward to base.

    /// <summary>Background fill applied behind the fragment text.</summary>
    public new Color? BackgroundColor { get => base.BackgroundColor; set => base.BackgroundColor = value; }

    /// <summary>Character spacing (Tc) in text-space units.</summary>
    public new float CharacterSpacing { get => base.CharacterSpacing; set => base.CharacterSpacing = value; }

    /// <summary>Whether positioning treats Y as the baseline or the descender.</summary>
    public new CoordinateOrigin CoordinateOrigin { get => base.CoordinateOrigin; set => base.CoordinateOrigin = value; }

    /// <summary>Font size in points.</summary>
    public new float FontSize { get => base.FontSize; set => base.FontSize = value; }

    /// <summary>Font style flags (Bold / Italic).</summary>
    public new FontStyles FontStyle { get => base.FontStyle; set => base.FontStyle = value; }

    /// <summary>Fill (non-stroking) colour applied to the rendered glyphs.</summary>
    public new Color? ForegroundColor { get => base.ForegroundColor; set => base.ForegroundColor = value; }

    /// <summary>Text formatting options (wrap mode, line spacing mode, etc.).</summary>
    public new TextFormattingOptions FormattingOptions { get => base.FormattingOptions; set => base.FormattingOptions = value ?? new TextFormattingOptions(); }

    /// <summary>Horizontal alignment of the fragment's text.</summary>
    public new HorizontalAlignment HorizontalAlignment { get => base.HorizontalAlignment; set => base.HorizontalAlignment = value; }

    /// <summary>Horizontal scaling percentage (default 100).</summary>
    public new float HorizontalScaling { get => base.HorizontalScaling; set => base.HorizontalScaling = value; }

    /// <summary>Whether the text is rendered invisibly (rendering mode 3).</summary>
    public new bool Invisible { get => base.Invisible; set => base.Invisible = value; }

    /// <summary>Line spacing (leading) in text-space units.</summary>
    public new float LineSpacing { get => base.LineSpacing; set => base.LineSpacing = value; }

    /// <summary>Glyph rendering mode (fill / stroke / clip).</summary>
    public new TextRenderingMode RenderingMode { get => base.RenderingMode; set => base.RenderingMode = value; }

    /// <summary>Rotation angle in degrees.</summary>
    public new double Rotation { get => base.Rotation; set => base.Rotation = value; }

    /// <summary>Whether the text has strikethrough.</summary>
    public new bool StrikeOut { get => base.StrikeOut; set => base.StrikeOut = value; }

    /// <summary>Stroke (outline) colour.</summary>
    public new Color? StrokingColor { get => base.StrokingColor; set => base.StrokingColor = value; }

    /// <summary>Whether the text is subscript.</summary>
    public new bool Subscript { get => base.Subscript; set => base.Subscript = value; }

    /// <summary>Whether the text is superscript.</summary>
    public new bool Superscript { get => base.Superscript; set => base.Superscript = value; }

    /// <summary>Whether the text is underlined.</summary>
    public new bool Underline { get => base.Underline; set => base.Underline = value; }

    /// <summary>Word spacing (Tw) in text-space units.</summary>
    public new float WordSpacing { get => base.WordSpacing; set => base.WordSpacing = value; }

    /// <summary>
    /// The font used to render the fragment. Returns the base
    /// <see cref="TextState.Font"/> cast to <see cref="Font"/>; setters
    /// upcast through the inherited storage.
    /// </summary>
    public new Aspose.Pdf.Text.Font? Font
    {
        get => base.Font as Aspose.Pdf.Text.Font;
        set => base.Font = value;
    }

    /// <summary>When true, a rectangular border is drawn around the text
    /// bounding box. Stored only — the renderer does not currently emit
    /// the border stroke.</summary>
    public bool DrawTextRectangleBorder { get; set; }

    /// <summary>Tab-stop settings inherited from the owning fragment.</summary>
    public TabStops TabStops => _fragment.TabStops ?? new TabStops();

    /// <summary>Copy every public formatting property from
    /// <paramref name="textState"/> into this state.</summary>
    public new void ApplyChangesFrom(TextState textState) => base.ApplyChangesFrom(textState);

    /// <summary>Returns true when the rendered text fits within the
    /// supplied rectangle at the current font / size.</summary>
    public bool IsFitRectangle(string str, Rectangle rect)
    {
        if (rect is null || string.IsNullOrEmpty(str)) return true;
        var fontSize = FontSize;
        if (fontSize <= 0) return true;

        // Greedy word-wrap to the rectangle width at the current font/size, then
        // check the stacked line height fits the rectangle height. (Measuring the
        // whole string as one line would never fit a paragraph and would shrink the
        // font to nothing.)
        var words = str.Split(' ');
        var lines = 1;
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length == 0 || MeasureString(candidate) <= rect.Width)
                current = candidate;
            else { lines++; current = word; }
        }
        // Lines stack with ~1.2x leading (one line's glyph height plus interline
        // gap), matching the reference fit calculation.
        return lines * fontSize * 1.2 <= rect.Height;
    }

    /// <summary>Measure the height of a single character at the current
    /// font / size, in points.</summary>
    public new double MeasureHeight(char character) => base.MeasureHeight(character);

    /// <summary>Measure the rendered width of <paramref name="str"/> at
    /// the current font / size, in points.</summary>
    public new double MeasureString(string str) => base.MeasureString(str);
}
