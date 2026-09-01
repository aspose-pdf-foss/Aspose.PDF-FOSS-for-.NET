using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.LogicalStructure;

/// <summary>Text-state snapshot used to format inline structure-element
/// runs. The FOSS structure-tree builder doesn't currently render
/// content through this state — values are stored only.</summary>
public sealed class StructureTextState
{
    /// <summary>Font name applied to the run.</summary>
    public string? FontName { get; set; }
    /// <summary>Font size in points.</summary>
    public float FontSize { get; set; } = 12f;
    /// <summary>Foreground fill colour.</summary>
    public Aspose.Pdf.Color? ForegroundColor { get; set; }
    /// <summary>Background fill colour.</summary>
    public Aspose.Pdf.Color? BackgroundColor { get; set; }
    /// <summary>Font style (bold / italic) applied to the run.</summary>
    public Aspose.Pdf.Text.FontStyles FontStyle { get; set; }
    /// <summary>Font applied to the run.</summary>
    public Aspose.Pdf.Text.Font? Font { get; set; }
    /// <summary>Whether the run is underlined.</summary>
    public bool Underline { get; set; }
    /// <summary>Whether the run is struck through.</summary>
    public bool StrikeOut { get; set; }
    /// <summary>Whether the run is rendered as subscript.</summary>
    public bool Subscript { get; set; }
    /// <summary>Whether the run is rendered as superscript.</summary>
    public bool Superscript { get; set; }
    /// <summary>Horizontal glyph scaling (percent).</summary>
    public float HorizontalScaling { get; set; } = 100f;
    /// <summary>Leading between lines, in points.</summary>
    public float LineSpacing { get; set; }
    /// <summary>Extra spacing between characters, in points.</summary>
    public float CharacterSpacing { get; set; }
    /// <summary>Extra spacing between words, in points.</summary>
    public float WordSpacing { get; set; }

    /// <summary>Layout margin for the element the state is applied to. An
    /// alternative to <see cref="Aspose.Pdf.Tagged.StructureElement.AdjustPosition"/>
    /// for positioning an authored block; consumed by the tagged-content renderer.</summary>
    public Aspose.Pdf.MarginInfo? MarginInfo { get; set; }
}
