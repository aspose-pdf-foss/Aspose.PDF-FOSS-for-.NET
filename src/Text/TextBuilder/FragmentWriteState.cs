using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Text;

public sealed partial class TextBuilder
{
    // A sub- or superscript run draws at this share of its base size, its baseline raised (superscript)
    // or lowered (subscript) by this share of the base size: the generator's metrics.
    private const double ScriptScale = 0.583;
    private const double SuperscriptRiseEm = 0.421;
    private const double SubscriptDropEm = 0.245;

    /// <summary>The size a run draws at for <paramref name="state"/>: reduced for a sub- or superscript.</summary>
    private static double ScriptSize(TextState state, double size)
        => state.Superscript || state.Subscript ? size * ScriptScale : size;

    /// <summary>The text rise for <paramref name="state"/> at base size <paramref name="size"/>: up for a
    /// superscript, down for a subscript, none otherwise.</summary>
    private static double ScriptRise(TextState state, double size)
        => state.Superscript ? size * SuperscriptRiseEm : state.Subscript ? -size * SubscriptDropEm : 0;

/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class FragmentWriteState
{
    public string text = null!;
    // A face whose licence does not cover embedding is not written into the file at
    // all. Settle that before a writer is chosen: reporting is raised here, and a
    // caller that switched reporting off keeps its save, the run falling to the
    // by-name Standard-14 path with the reason left on the face.
    public Aspose.Pdf.Text.FontData? embeddableFontData;
    public bool needsCid;
    public string fontResName = null!;
    public byte[]? hexGlyphIds;
    public float fontSize;
    public double x;
    public double y;
    // Compute descent compensation for embedded fonts.
    // The absorber's Position.Y = Td.Y + descent*fs/1000 (descent is negative),
    // so we must write Td.Y = user_y - descent*fs/1000 to round-trip correctly.
    public double descentComp;
    public Content.ContentStreamBuilder builder = null!;
    public byte[] runBytes = null!;
    // Underline/strikeout are drawn as thin rectangles at save time. The
    // TextState flags are typically set before the fragment is attached to a
    // page, so the property setters' own registration (which needs a SourcePage)
    // is skipped — register here now that the fragment lives on this page. The
    // flag may sit on the fragment's TextState or on any of its segments.
    // Only a REQUESTED underline is drawn. One the absorber merely observed under the
    // source describes the page as it already is — re-emitting it for a replacement
    // lays a second copy over the original rule.
    public bool underline;
    public bool strikeOut;
}
}
