
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>
    /// Check if this fragment uses a CID/Type0 font (Arabic, CJK, etc.).
    /// CID fonts store text in visual order with multi-byte character codes,
    /// requiring segment-by-segment replacement when the combined text isn't
    /// found in a single content stream operator.
    /// </summary>
    private bool IsCidFontFragment()
    {
        // Check font metadata first
        foreach (var seg in _segments)
        {
            if (seg.TextState?.Font?.IsCid == true) return true;
        }
        // Fallback: detect by Arabic/CJK presentation forms in the text
        foreach (var seg in _segments)
        {
            foreach (var ch in seg.Text)
            {
                if ((ch >= '\uFB50' && ch <= '\uFDFF') || (ch >= '\uFE70' && ch <= '\uFEFF') ||
                    (ch >= '\u3000' && ch <= '\u9FFF'))
                    return true;
            }
        }
        return false;
    }

    /// <summary>Tag this fragment's written content as marked content: the ops
    /// emitted for it are wrapped in <c>/name &lt;&lt;/MCID id&gt;&gt; BDC … EMC</c>.
    /// Consecutive fragments carrying the SAME tag and id share one block.</summary>
    internal void SetMarkedContentProperties(string name, int id)
    {
        TextState.MarkedContentTag = name;
        TextState.MarkedContentMcid = id;
    }

    /// <summary>
    /// Text direction in page space — the reading-direction unit vector
    /// transformed by both the text matrix and the CTM.
    /// For horizontal LTR text: (1, 0). For 90° rotated vertical text: (0, ±1).
    /// </summary>
    internal double TextDirX { get; set; } = 1;

    internal double TextDirY { get; set; }

    /// <summary>
    /// Trailing character spacing (Tc * HScaling * TmA) in page space, subtracted
    /// from Rectangle.Width to get the visual glyph-only width for bg rect rendering.
    /// </summary>
    internal double TrailingTcPageSpace { get; set; }

    /// <summary>
    /// The CTM that was active when this fragment was extracted.
    /// Used to transform page-space coordinates back to content-stream space
    /// when injecting background/underline rectangles.
    /// </summary>
    internal Matrix? ExtractionCtm { get; set; }

    /// <summary>The text-space Y of the fragment's first run (the line's Tm F
    /// plus any Td displacement) — with a FLIPPED text matrix (TmD &lt; 0) the
    /// background highlight replays the run's y-up frame, whose translation
    /// needs this value.</summary>
    internal double ExtractionTmTy { get; set; }

    /// <summary>Accumulated Position delta applied after absorption — geometry
    /// consumers (the background highlight) add it to the extraction rectangle.</summary>
    internal double PostAbsorbDx { get; set; }

    internal double PostAbsorbDy { get; set; }

    /// <summary>Set the text as absorbed from the page — a match can contain
    /// line-break sentinels that belong to no segment, so the segment join
    /// can't reproduce it. Leaves segments untouched.</summary>
    internal void SetAbsorbedText(string text) => _text = text;
}
