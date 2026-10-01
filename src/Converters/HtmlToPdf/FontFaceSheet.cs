using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // ── The authored-width @font-face sheet ─────────────────────────────────
    //
    // A hand-authored document that declares its OWN faces (@font-face with
    // relative url() sources), pins the body width in points, and styles its
    // paragraphs through class rules (colour, centering, border-bottom bands).
    //
    // Measured on the faux-bolding fixture:
    //  - page width = side margin + body width + side margin (90 + 612 + 90 =
    //    792), page height stays the default sheet (842); flow starts at a
    //    72 pt top margin;
    //  - a text line is a CSS line box on the face's WIN metrics: line height
    //    1.2 em, half-leading (1.2 − (winAsc+winDesc)) · size / 2, baseline at
    //    halfLead + winAsc · size from the box top (reproduces baselines
    //    117.42 / 139.92 to 0.001 with ITCFrankGoth 0.934/0.250);
    //  - adjacent block margins COLLAPSE to the larger (the div's 36 swallows
    //    the paragraph's 5); padding-bottom then border-bottom follow the line
    //    box (border stroked at its centre line, full content width);
    //  - a <b> run selects the family's own bold-declared @font-face source —
    //    the same program when the author mapped bold to the regular file, so
    //    no synthetic bolding ever happens.
    private const double FontFaceSheetSideMargin = 90.0;
    private const double FontFaceSheetTopMargin = 72.0;
    private const double FontFaceSheetLineHeightEm = 1.2;

    private sealed class SheetFace
    {
        public byte[] Ttf = System.Array.Empty<byte>();
        public Text.GlyphOutlineParser Parser = null!;
        public double Upm = 1000;
        public double WinAsc = 0.75;   // em fractions
        public double WinDesc = 0.25;
        public bool Bold;
        public bool Italic;
    }

    /// <summary>OS/2 usWinAscent/usWinDescent as em fractions (falling back to
    /// hhea ascent/descent when the table is absent).</summary>
    private static void ReadWinMetrics(byte[] sfnt, SheetFace face)
    {
        try
        {
            int U16(int o) => (sfnt[o] << 8) | sfnt[o + 1];
            var num = U16(4);
            int os2 = -1, hhea = -1;
            for (var i = 0; i < num; i++)
            {
                var rec = 12 + 16 * i;
                var tag = Encoding.ASCII.GetString(sfnt, rec, 4);
                var off = (sfnt[rec + 8] << 24) | (sfnt[rec + 9] << 16) | (sfnt[rec + 10] << 8) | sfnt[rec + 11];
                if (tag == "OS/2") os2 = off;
                else if (tag == "hhea") hhea = off;
            }
            if (os2 >= 0 && os2 + 78 <= sfnt.Length)
            {
                face.WinAsc = U16(os2 + 74) / face.Upm;
                face.WinDesc = U16(os2 + 76) / face.Upm;
            }
            else if (hhea >= 0 && hhea + 10 <= sfnt.Length)
            {
                short S16(int o) => (short)U16(o);
                face.WinAsc = S16(hhea + 4) / face.Upm;
                face.WinDesc = System.Math.Abs(S16(hhea + 6)) / face.Upm;
            }
        }
        catch { /* keep the defaults */ }
    }
}
