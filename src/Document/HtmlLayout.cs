using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The CSS "normal" line height (pt) for an embedded face at a
    /// point size: the OS/2 win line-box height quantized to whole CSS pixels, recovering
    /// half the leading when the pixel round truncated down (exact across
    /// 6-22pt for Verdana/Calibri). Zero when the metrics are unreadable.</summary>
    private static double HtmlNormalLineHeightPt(byte[]? ttf, double sizePt)
    {
        if (ttf is null || ttf.Length < 12) return 0;
        try
        {
            var tp = new Text.TrueTypeParser(ttf);
            tp.Parse();
            if (tp.UsWinAscent <= 0 || tp.UnitsPerEm <= 0) return 0;
            double upm = tp.UnitsPerEm, winSum = tp.UsWinAscent + tp.UsWinDescent;
            var px = sizePt * 96.0 / 72.0;
            var rawPx = winSum * px / upm;
            var rpx = Math.Round(rawPx, MidpointRounding.AwayFromZero);
            var pitchPx = rpx + Math.Max(0, rawPx - rpx) / 2;
            return pitchPx * 0.75;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Shape a generator <see cref="Text.TextFragment"/> that carries Arabic/RTL text: replace
    /// each segment's base letters with their contextual presentation forms in visual
    /// right-to-left order, and route the fragment through an Arabic-capable embedded font
    /// (Arial covers Arabic Presentation Forms-B). Without this the default Standard-14 font has
    /// no Arabic glyphs and the renderer applies no OpenType shaping, so Arabic rendered as
    /// disconnected isolated letters in left-to-right order (or missing glyphs).
    /// </summary>
    private static void ShapeArabicForGenerator(Text.TextFragment tf)
    {
        if (!Text.ArabicTextShaper.ContainsArabic(tf.Text)) return;
        // The fragment keeps its own embedded face when that face carries the Arabic
        // glyphs (a caller's Arial Unicode MS paragraph must not be re-dressed in
        // Arial and lose its ideographs); only a face without them — the Standard-14
        // default above all — routes through the Arabic-capable host Arial.
        var own = tf.TextState.Font;
        var arabicChars = new System.Text.StringBuilder();
        foreach (var c in tf.Text)
            if (Text.ArabicTextShaper.ContainsArabic(c.ToString())) arabicChars.Append(c);
        // A face that cannot show the Arabic is traded for the one the
        // repository's substitution picks for it (a Standard-14 fragment moves
        // to the host serif; a face short of a few letters to the face that
        // covers them), Arial being the last resort.
        Text.Font? font;
        if (own?.SourceFontData?.TtfData is { Length: > 0 } ownTtf
            && Text.FontRepository.CoversText(ownTtf, arabicChars.ToString()))
            font = own;
        else
        {
            var sub = Text.FontRepository.SubstituteForMissingGlyphs(tf.Text, own);
            font = sub?.TtfData is { Length: > 0 }
                ? (Text.Font?)sub
                : Text.FontRepository.TryFindFont("Arial");
        }
        if (font?.SourceFontData?.TtfData is null) return;

        // Collect each segment's display text (shape Arabic segments to visual order; keep
        // non-Arabic segments as-is) and the effective size. Segment-level bidi: a fragment
        // whose first content segment is Arabic lays out right-to-left, so the segments are
        // emitted in reverse order (each segment is treated as a directional unit,
        // which keeps e.g. a leading "." attached to its Latin segment rather than migrating
        // to the adjacent Arabic run as a per-character bidi pass would).
        var size = tf.TextState.FontSize;
        var displays = new List<string>();
        var firstArabic = (bool?)null;
        if (tf.Segments is { Count: > 0 })
        {
            foreach (var s in tf.Segments)
            {
                if (string.IsNullOrEmpty(s.Text)) continue;
                var arabic = Text.ArabicTextShaper.ContainsArabic(s.Text);
                firstArabic ??= arabic;
                if (s.TextState.FontSize > 0 && size <= 0) size = s.TextState.FontSize;
                displays.Add(arabic ? Text.ArabicTextShaper.Shape(s.Text) : s.Text);
            }
        }
        if (displays.Count == 0) displays.Add(Text.ArabicTextShaper.Shape(tf.Text));
        if (firstArabic == true) displays.Reverse();

        tf.TextState.Font = font;
        if (size > 0) tf.TextState.FontSize = size;
        tf.Text = string.Concat(displays);
    }
}
