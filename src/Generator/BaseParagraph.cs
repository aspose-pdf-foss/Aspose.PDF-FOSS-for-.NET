namespace Aspose.Pdf;

/// <summary>
/// Base class for paragraph-level DOM content (text fragments, tables,
/// images, header/footer fragments, floating boxes).
/// </summary>
public class BaseParagraph
{
    /// <summary>Horizontal alignment applied to this paragraph.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; }

    /// <summary>Vertical alignment applied to this paragraph.</summary>
    public VerticalAlignment VerticalAlignment { get; set; }

    private MarginInfo? _margin;

    /// <summary>Outer-margin info applied to this paragraph. Auto-initialised on
    /// first access so callers can write <c>paragraph.Margin.Left = 10</c> on a
    /// fresh instance without a null check.</summary>
    public MarginInfo Margin
    {
        get => _margin ??= new MarginInfo();
        set => _margin = value;
    }

    /// <summary>Force the paragraph to start a new column.</summary>
    public bool IsFirstParagraphInColumn { get; set; }

    /// <summary>Keep this paragraph on the same page as the next one.</summary>
    public bool IsKeptWithNext { get; set; }

    /// <summary>Force the paragraph to start on a new page.</summary>
    public bool IsInNewPage { get; set; }

    /// <summary>Inline paragraph flag (does not start a new line).</summary>
    public bool IsInLineParagraph { get; set; }

    /// <summary>Legacy string-typed hyperlink target.</summary>
    public string? HyperlinkText { get; set; }

    /// <summary>Typed hyperlink decoration applied to the paragraph.</summary>
    public Hyperlink? Hyperlink { get; set; }

    /// <summary>Z-order index used by the DOM renderer.</summary>
    public int ZIndex { get; set; }

    /// <summary>Shallow clone — copies scalar/ref-shared state.</summary>
    public virtual object Clone() => MemberwiseClone();
}
