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
// UA serif table helpers: face resolution, CSS box and seat metrics, glyph advances and styled widths.
    private static (byte[], string, Text.GlyphOutlineParser, Text.TrueTypeParser)? Face(UaSerifTableState ua, string family)
    {
        var key = family.ToLowerInvariant();
        if (ua.faces.TryGetValue(key, out var have)) return have;
        var name = System.Globalization.CultureInfo.InvariantCulture
            .TextInfo.ToTitleCase(key);
        // the repository's ttf data drops the legacy kern
        // table wrap measurement relies on — read the system
        // file itself when the face is a known one
        var file = key switch
        {
            "calibri" => "calibri.ttf",
            "times new roman" => "times.ttf",
            "microsoft sans serif" => "micross.ttf",
            _ => null,
        };
        byte[]? ttf = null;
        if (file is not null)
            try
            {
                ttf = System.IO.File.ReadAllBytes(System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file));
            }
            catch { ttf = null; }
        if (ttf is null)
            try { ttf = Text.FontRepository.GetTtfData(name); }
            catch { return null; }
        if (ttf is null) return null;
        try
        {
            var tp2 = new Text.TrueTypeParser(ttf);
            tp2.Parse();
            var got = (ttf, name, new Text.GlyphOutlineParser(ttf), tp2);
            ua.faces[key] = got;
            return got;
        }
        catch { return null; }
    }

    // css line box: whole-css-px rounded hhea line height
    private static double CssBox(UaSerifTableState ua, Text.TrueTypeParser t, double s) =>
        0.75 * Math.Floor((t.Ascent + Math.Abs(t.Descent) + t.LineGap)
            * (s * 96.0 / 72.0) / t.UnitsPerEm + 0.5);

    private static double SeatIn(UaSerifTableState ua, Text.TrueTypeParser t, double s, double box) =>
        t.UsWinAscent * s / t.UnitsPerEm
        + (box - (t.UsWinAscent + t.UsWinDescent) * s / t.UnitsPerEm) / 2;

    private static double GlyphAdv(UaSerifTableState ua, Text.GlyphOutlineParser gp, int gid, double s) =>
        gp.GetAdvanceWidth(gid) * s / (gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0);

    private static double StyledWidth(UaSerifTableState ua, string t, Text.GlyphOutlineParser gp,
        Text.GlyphOutlineParser? gpFb, double s)
    {
        double w = 0;
        var prev = -1;
        foreach (var c in t)
        {
            if (c == UaFallbackChar && gpFb is not null)
            {
                var gf = gpFb.CMap.TryGetValue(c, out var g2) ? g2 : 0;
                w += GlyphAdv(ua, gpFb, gf, s);
                prev = -1;
                continue;
            }
            var gid = gp.CMap.TryGetValue(c, out var g) ? g : 0;
            if (prev >= 0)
                w += gp.GetKernAdjustment(prev, gid) * s
                     / (gp.UnitsPerEm > 0 ? gp.UnitsPerEm : 1000.0);
            w += GlyphAdv(ua, gp, gid, s);
            prev = gid;
        }
        return w;
    }

    private static double TimesWidth(UaSerifTableState ua, string t)
    {
        try
        {
            return Text.FontRepository.TryFindFont("Times-Roman")
                ?.MeasureString(t, UaSerifPt) ?? t.Length * UaSerifPt * 0.5;
        }
        catch { return t.Length * UaSerifPt * 0.5; }
    }
}
