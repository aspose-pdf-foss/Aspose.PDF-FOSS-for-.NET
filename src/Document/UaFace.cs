namespace Aspose.Pdf;

/// <summary>A TrueType face resolved for the HTML engine's UA flow, with the vertical
/// metrics the CSS line box is built from and the glyph parser its runs are measured
/// with (pair-kerned advances from the face's kern table).</summary>
internal sealed class UaFace
{
    public byte[] Ttf = null!;
    public string Name = "";
    public Text.GlyphOutlineParser Gp = null!;
    public double Upm;
    public double WinAsc;
    public double WinDesc;
    public double HheaSum;
    public double XHeight;
    /// <summary>Resource name the face embeds under (its own name keeps it clear of the
    /// Helvetica an overflow page registers as F1).</summary>
    public string ResName = "";

    /// <summary>CSS reference pixel in points (96 px per inch).</summary>
    public const double CssPxToPt = 0.75;
    /// <summary>x-height share of the em when the face declares none.</summary>
    private const double XHeightFallbackEm = 0.45;
    /// <summary>TJ adjustments are in thousandths of the text space.</summary>
    private const double TextSpaceUnits = 1000.0;

    /// <summary>CSS "normal" line height: the hhea line height rounded to whole CSS pixels.</summary>
    public double LineHeight(double s) => CssPxToPt * System.Math.Floor(HheaSum * (s / CssPxToPt) / Upm + 0.5);
    public double Ascent(double s) => WinAsc * s / Upm;
    public double Descent(double s) => WinDesc * s / Upm;
    /// <summary>Line-box top to baseline: half the surplus leading plus the ascent.</summary>
    public double Above(double s) => (LineHeight(s) - (WinAsc + WinDesc) * s / Upm) / 2 + Ascent(s);
    public double Below(double s) => LineHeight(s) - Above(s);
    public double XHeightPt(double s) => XHeight > 0 ? XHeight * s / Upm : XHeightFallbackEm * s;

    /// <summary>Resolve a family in a style through the repository and read the metrics
    /// the CSS line box needs; null when the face has no TrueType data.</summary>
    public static UaFace? TryLoad(string family, bool bold, bool italic, string resName)
    {
        var styles = (bold ? Text.FontStyles.Bold : (Text.FontStyles)0) | (italic ? Text.FontStyles.Italic : (Text.FontStyles)0);
        Text.Font? font;
        try
        {
            font = styles != 0
                ? Text.FontRepository.TryFindFont(family, styles, ignoreCase: true)
                : Text.FontRepository.TryFindFont(family, ignoreCase: true);
        }
        catch { return null; }
        return FromFont(font, resName);
    }

    /// <summary>The face behind a resolved font object (a page default, a fragment text
    /// state); null when it carries no TrueType data the flow can measure.</summary>
    public static UaFace? FromFont(Text.Font? font, string resName)
    {
        if (font?.SourceFontData?.TtfData is not { Length: > 0 } ttf) return null;
        try
        {
            var tp = new Text.TrueTypeParser(ttf);
            tp.Parse();
            if (tp.UnitsPerEm <= 0 || tp.UsWinAscent <= 0) return null;
            var gp = new Text.GlyphOutlineParser(ttf);
            if (gp.CMap.Count == 0) return null;
            return new UaFace
            {
                Ttf = ttf, Name = font.FontName, Gp = gp,
                Upm = tp.UnitsPerEm, WinAsc = tp.UsWinAscent, WinDesc = tp.UsWinDescent,
                HheaSum = tp.Ascent + System.Math.Abs(tp.Descent) + tp.LineGap, XHeight = tp.SxHeight,
                ResName = resName,
            };
        }
        catch { return null; }
    }

    /// <summary>The glyph for a character; a no-break space draws as the face's space when it
    /// has no glyph of its own.</summary>
    public int Gid(char ch)
    {
        if (Gp.CMap.TryGetValue(ch, out var g) && g > 0) return g;
        if (ch == '\u00A0' && Gp.CMap.TryGetValue(' ', out var sp)) return sp;
        return Gp.GlyphIdOrLookAlike(ch);
    }

    /// <summary>The text as the embedder encodes it: a no-break space the face lacks becomes a space.</summary>
    public string EncodableText(string text) => Gp.CMap.ContainsKey('\u00A0') ? text : text.Replace('\u00A0', ' ');

    /// <summary>The pair-kerned advance of <paramref name="text"/> at <paramref name="size"/>.</summary>
    public double Width(string text, double size)
    {
        var upm = Gp.UnitsPerEm > 0 ? Gp.UnitsPerEm : TextSpaceUnits;
        double w = 0;
        var prev = -1;
        foreach (var ch in text)
        {
            var gid = Gid(ch);
            if (prev >= 0) w += Gp.GetKernAdjustment(prev, gid);
            w += Gp.GetAdvanceWidth(gid);
            prev = gid;
        }
        return w * size / upm;
    }

    /// <summary>TJ adjustment array (thousandths of text space; positive pulls the following
    /// glyphs left) for the pair-kerning of <paramref name="text"/>, or null when no pair kerns.</summary>
    public double[]? KernAdjustments(string text)
    {
        if (text.Length < 2) return null;
        var upm = Gp.UnitsPerEm > 0 ? Gp.UnitsPerEm : TextSpaceUnits;
        double[]? adj = null;
        var prev = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var gid = Gid(text[i]);
            if (prev >= 0)
            {
                var kern = Gp.GetKernAdjustment(prev, gid);
                if (kern != 0)
                {
                    adj ??= new double[text.Length - 1];
                    adj[i - 1] = -kern * TextSpaceUnits / upm;
                }
            }
            prev = gid;
        }
        return adj;
    }
}
