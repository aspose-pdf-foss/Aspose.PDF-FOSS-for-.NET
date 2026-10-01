using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend
{
    private const double TextInsetX = 2;
    private const double BarBottomRise = 11;
    private const double BarPadding = 4;
    private const double BaselineLift = 2;
    private const double LegacyBaselineRise = 13;
    private const double LegacyInsetFactor = 0.2;
    private const double LegacyBarHeightFactor = 1.325;

    private void AddTextToPage(Page page, FormattedText ft, float llx, float lly, float urx, float ury)
    {
        var tx = new MendTextState();
        tx.page = page;
        tx.ft = ft;
        tx.llx = llx;
        tx.lly = lly;
        tx.urx = urx;
        tx.ury = ury;
        tx.sb = new StringBuilder();
        tx.sb.Append("q\n");

        ResolveMendTextFont(tx);

        ResolveMendTextMetrics(tx);

        PlaceMendTextBar(tx);

        EmitMendBackgroundBars(tx);

        EmitMendText(tx);

        // The font is declared /WinAnsiEncoding (see AddOrGetFont), so the glyph
        // bytes must follow Windows-1252, not Latin-1. They differ in 0x80-0x9F,
        // where WinAnsi carries the "smart" punctuation/symbols (' " – — ™ …).
        // Encoding the stream with Latin-1 turned those into '?' and lost the text;
        // Cp1252 maps them to their real bytes so extraction round-trips. All other
        // code points ≤ 0xFF (and the ASCII operators) encode identically.
        AppendContent(tx.page, Cp1252.GetBytes(tx.sb.ToString()));
    }
}
