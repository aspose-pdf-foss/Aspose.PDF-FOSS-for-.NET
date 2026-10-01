using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The UA-serif flow: an inline-block that declares a percent width is a box that
    /// wide inside its container's content box, so everything it holds ends that far short of
    /// the container's right edge (probed on the reward letter: the 68 % statement column's
    /// rows measure 449.4 = 0.68 x 705 less the section's own insets).</summary>
    private static void ApplyUaPercentBoxWidth(ParseBlocksState pb, Token tok)
    {
        if (!pb.style.UaBoxes || pb.contentWidthPt <= 0 || tok.Attributes is null
            || !tok.Attributes.TryGetValue("style", out var style) || string.IsNullOrEmpty(style))
            return;
        if (!Regex.IsMatch(style, @"display\s*:\s*inline-block", RegexOptions.IgnoreCase)) return;
        var pct = StylePct(style, "width");
        if (pct <= 0 || pct >= 100) return;
        var avail = pb.contentWidthPt - UaBodyMarginPt - pb.style.LeftIndent - pb.style.RightInsetPt;
        if (avail <= 0) return;
        pb.style.RightInsetPt += avail * (1 - pct / 100.0);
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_SEAT") == "1")
            Console.WriteLine(FormattableString.Invariant($"[pctbox] pct={pct} avail={avail:0.##} ri={pb.style.RightInsetPt:0.##}"));
    }
}
