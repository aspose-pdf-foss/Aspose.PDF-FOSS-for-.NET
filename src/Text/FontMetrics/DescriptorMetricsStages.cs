using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class FontMetrics
{
    /// <summary>The stages of the descriptor metrics: reading the embedded program's own tables.</summary>
    private static void ReadEmbeddedProgramMetrics(FontMetrics metrics, PdfStream fontFileStream, PdfReader reader)
    {
        try
        {
            var fontData = reader.DecodeStream(fontFileStream);
            var ttf = new TrueTypeParser(fontData);
            ttf.Parse();
            if (ttf.UnitsPerEm > 0 && ttf.Ascent > 0)
            {
                var scale = 1000.0 / ttf.UnitsPerEm;
                // Use GDI+-style cell ascent for the text rectangle.
                // .NET uses em-height = usWinAscent + usWinDescent as the em square,
                // and cell ascent = usWinAscent scaled by (1000 / em-height-in-design-units).
                // Use GDI+-style cell ascent when usWin metrics define a significantly
                // larger em-square than upem (common in Calibri, Arial, etc. where
                // usWinAsc+usWinDesc > upem). This matches .NET's text rectangle.
                // For fonts where usWin metrics are close to upem, use hhea ascent
                // directly (standard upem scaling).
                var emDesignUnits = ttf.UsWinAscent + ttf.UsWinDescent;
                if (ttf.UsWinAscent > 0 && emDesignUnits > ttf.UnitsPerEm * 1.2)
                {
                    metrics._ascent = (int)Math.Round(ttf.UsWinAscent * 1000.0 / emDesignUnits);
                }
                else
                {
                    var lineGapHalf = ttf.LineGap > 0 ? ttf.LineGap / 2 : 0;
                    metrics._ascent = (int)Math.Round((ttf.Ascent + lineGapHalf) * scale);
                }
                // Override descent from embedded TrueType only when the descriptor value
                // is clearly in non-standard units (|Descent| > 1000 in 1000-unit space).
                // E.g., Cambria: descriptor=-2463, sTypoDescender=-455 (2048 upem).
                // For fonts where the descriptor Descent is already in ~1000-unit range
                // (e.g., ArialMT Descent=-325), keep the descriptor value as-is.
                if (ttf.STypoDescender != 0 && Math.Abs(metrics._descent) > 1000)
                    metrics._descent = ttf.STypoDescender * scale;

                // OS/2 usWinAscent + usWinDescent gives the visual line height.
                // Used for background rectangle sizing where the full cell height matters.
                if (ttf.UsWinAscent > 0)
                    metrics._winLineHeight = (int)Math.Round((ttf.UsWinAscent + ttf.UsWinDescent) * scale);

                // Underline metrics from the post table
                if (ttf.UnderlinePosition != 0)
                    metrics._underlinePosition = (int)Math.Round(ttf.UnderlinePosition * scale);
                if (ttf.UnderlineThickness != 0)
                    metrics._underlineThickness = (int)Math.Round(ttf.UnderlineThickness * scale);
            }
        }
        catch
        {
            // Fall back to descriptor values
        }
    }
}
