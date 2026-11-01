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
    {
        if (state.Superscript) return size * SuperscriptRiseEm;
        if (state.Subscript) return -size * SubscriptDropEm;
        return 0;
    }

    /// <summary>The state a fragment drawn as one run takes its script from: its own when it states one, else
    /// the script its segments state when every segment with text states the same one - a script set on the
    /// segment of a one-segment fragment is the whole run's.</summary>
    private static TextState ScriptStateOf(TextFragment fragment)
    {
        var own = fragment.TextState;
        if (own.Superscript || own.Subscript) return own;
        TextState? shared = null;
        foreach (var seg in fragment.Segments)
        {
            if (string.IsNullOrEmpty(seg.Text) || seg.TextState is not { } st) continue;
            if (shared is null) shared = st;
            else if (st.Superscript != shared.Superscript || st.Subscript != shared.Subscript) return own;
        }
        return shared is { } s && (s.Superscript || s.Subscript) ? s : own;
    }

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
    // A run appended to a page viewed turned is seated in the view: this carries the view to the page's own space.
    public Matrix? viewTurn;
    // Compute descent compensation for embedded fonts.
    // The absorber's Position.Y = Td.Y + descent*fs/1000 (descent is negative),
    // so we must write Td.Y = user_y - descent*fs/1000 to round-trip correctly.
    public double descentComp;
    // The descent (thousandths of the size) of a font shown without a host program - a document
    // font or a predefined CJK face - whose descriptor the absorber reads back; null otherwise.
    public double? hostlessDescent;
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
