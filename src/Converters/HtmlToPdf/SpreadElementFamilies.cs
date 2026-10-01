using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Spread element families: the heading, the paragraph and the floated figures.</summary>
    private static void PlaceHeading(SpreadPagesState sp, SpreadRenderState rs, SpreadElementState se, Match el, string tag)
    {
        switch (tag)
        {
            case "h1":
            {
                var cleared = Regex.IsMatch(se.attrs, "clear\\s*:\\s*both", RegexOptions.IgnoreCase);
                var top = Regex.IsMatch(se.attrs, "class\\s*=\\s*[\"'][^\"']*top", RegexOptions.IgnoreCase);
                var padTopEm = top ? CssEm(sp, "h1.top", "padding-top", 1.0)
                    : CssEm(sp, ".page3 h1", "padding-top", 2.0);
                if (cleared && rs.floatBottom > rs.y)
                {
                    // clear: both — the heading's box opens AT the float
                    // bottom, its own top margin suppressed (measured 700.0
                    // = floats' 648.4 + the 2em padding + the ascent).
                    rs.y = rs.floatBottom;
                }
                else
                {
                    rs.y += sp.h1MarginY;
                }
                rs.y += padTopEm * sp.h1Fs;
                var baseline = rs.y + sp.h1Asc;
                EmitLine(rs, sp, "F2", sp.h1Fs, sp.contentL, baseline, Flat(rs, se.elBody));
                rs.y = baseline + (sp.h1Fs * 1.15 - sp.h1Asc) + sp.h1MarginY;   // desc + margin-bottom
                break;
            }
        }
    }

    /// <summary></summary>
    private static void PlaceParagraph(SpreadPagesState sp, SpreadRenderState rs, SpreadElementState se, Match el, string tag)
    {
        switch (tag)
        {
            case "p":
            {
                // a noind paragraph with a .national initial-cap float
                var natM = Regex.Match(se.elBody,
                    "<span\\b[^>]*class\\s*=\\s*[\"'][^\"']*national[^\"']*[\"'][^>]*>(?<cap>[\\s\\S]*?)</span>",
                    RegexOptions.IgnoreCase);
                var text = Flat(rs, natM.Success
                    ? se.elBody.Remove(natM.Index, natM.Length) : se.elBody);
                var pX = sp.contentL + sp.noindML;
                var pW = sp.contentR - pX;
                if (natM.Success)
                {
                    var cap = Flat(rs, natM.Groups["cap"].Value);
                    var capW = MeasureFaceText("Times New Roman", cap, sp.natFs)
                        + 0.1 * sp.natFs;                        // margin-right 0.1em
                    var narrowLines = (int)System.Math.Ceiling(sp.natFs / sp.bodyPitch);
                    EmitLine(rs, sp, "F5", sp.natFs, pX, rs.y + sp.natAsc, cap);
                    var lines = MeasuredWordWrapPastFloat(text, pW - capW, pW,
                        narrowLines, "Times New Roman", sp.bodyFs);
                    for (var i = 0; i < lines.Length; i++)
                        EmitLine(rs, sp, "F5", sp.bodyFs,
                            i < narrowLines ? pX + capW : pX,
                            rs.y + sp.bodyDrop + i * sp.bodyPitch, lines[i]);
                    rs.y += lines.Length * sp.bodyPitch;
                }
                else
                {
                    var lines = MeasuredWordWrap(text, pW, "Times New Roman", sp.bodyFs);
                    foreach (var t in lines)
                    {
                        EmitLine(rs, sp, "F5", sp.bodyFs, pX, rs.y + sp.bodyDrop, t);
                        rs.y += sp.bodyPitch;
                    }
                }
                rs.pendingRightTop = rs.y;                          // a float-right column
                break;                                        // opens on this grid
            }
        }
    }

    /// <summary></summary>
    private static void PlaceFloat(SpreadPagesState sp, SpreadRenderState rs, SpreadElementState se, Match el, string tag)
    {
        switch (tag)
        {
            case "float:right":
            {
                // 20% column against the content right edge, its text on
                // the body grid continuing from the paragraph above.
                var share = 0.20;
                var fm2 = Regex.Match(sp.css.TryGetValue(".page3 div.floatright", out var frRule)
                    && frRule.TryGetValue("width", out var frW) ? frW : "20%", "([\\d.]+)\\s*%");
                if (fm2.Success) share = double.Parse(fm2.Groups[1].Value, sp.invc) / 100.0;
                var colW = share * sp.contentW;
                var colX = sp.contentR - colW;
                var fy = rs.pendingRightTop > 0 ? rs.pendingRightTop : rs.y;
                var lines = MeasuredWordWrap(Flat(rs, se.elBody), colW, "Times New Roman", sp.bodyFs);
                foreach (var t in lines)
                {
                    EmitLine(rs, sp, "F5", sp.bodyFs, colX, fy + sp.bodyDrop, t);
                    fy += sp.bodyPitch;
                }
                if (fy > rs.floatBottom) rs.floatBottom = fy;
                break;
            }
            case "float:left":
            {
                // 80% figure: caption head (bold, centred), the image at
                // width:100% keeping its aspect, the italic caption.
                var share = 0.80;
                var flm = Regex.Match(sp.css.TryGetValue(".floatleft", out var flRule)
                    && flRule.TryGetValue("width", out var flW) ? flW : "80%", "([\\d.]+)\\s*%");
                if (flm.Success) share = double.Parse(flm.Groups[1].Value, sp.invc) / 100.0;
                var boxW = share * sp.contentW;
                var fy = rs.y + sp.contentEmPx * 0.75;              // margin: 1em 0
                var headM = Regex.Match(se.elBody,
                    "<p\\b[^>]*captionhead[^>]*>(?<t>[\\s\\S]*?)</p>", RegexOptions.IgnoreCase);
                if (headM.Success)
                {
                    var t = Flat(rs, headM.Groups["t"].Value);
                    var tw = MeasureFaceText("Arial-Bold", t, sp.bodyFs);
                    var drop = MetricBaselineDrop(sp.bodyFs, sp.headPitch, sp.arialM);
                    rs.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                        Compat.Format(sp.invc, $"{sp.capColor.R / 255.0:0.###} {sp.capColor.G / 255.0:0.###} {sp.capColor.B / 255.0:0.###} rg\n")));
                    EmitLine(rs, sp, "F2", sp.bodyFs, sp.contentL + (boxW - tw) / 2, fy + drop, t);
                    rs.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes("0 g\n"));
                    fy += sp.headPitch;
                }
                var imM = Regex.Match(se.elBody, "<img\\b[^>]*src\\s*=\\s*[\"']([^\"']+)[\"']",
                    RegexOptions.IgnoreCase);
                if (imM.Success)
                {
                    var data = LoadConverterImage(imM.Groups[1].Value, sp.options);
                    if (data is not null && TryReadImagePixelSize(data) is (var iw, var ih)
                        && iw > 0 && ih > 0)
                    {
                        var h = boxW * ih / iw;
                        try
                        {
                            rs.page.AddImage(data, new Rectangle(sp.contentL,
                                sp.pageHeight - fy - h, sp.contentL + boxW, sp.pageHeight - fy));
                        }
                        catch { }
                        fy += h;
                    }
                }
                var capM = Regex.Match(se.elBody,
                    "<p\\b[^>]*class\\s*=\\s*[\"']caption[\"'][^>]*>(?<t>[\\s\\S]*?)</p>",
                    RegexOptions.IgnoreCase);
                if (capM.Success)
                {
                    fy += CaptionGapPt;
                    rs.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                        Compat.Format(sp.invc, $"{sp.capColor.R / 255.0:0.###} {sp.capColor.G / 255.0:0.###} {sp.capColor.B / 255.0:0.###} rg\n")));
                    foreach (var t in MeasuredWordWrap(Flat(rs, capM.Groups["t"].Value),
                                 boxW, "Arial-Italic", sp.capFs))
                    {
                        EmitLine(rs, sp, "F3", sp.capFs, sp.contentL, fy + sp.capDrop, t);
                        fy += sp.capPitch;
                    }
                    rs.page.AddContentStream(System.Text.Encoding.ASCII.GetBytes("0 g\n"));
                }
                fy += sp.contentEmPx * 0.75;                     // margin-bottom 1em
                if (fy > rs.floatBottom) rs.floatBottom = fy;
                break;
            }
        }
    }
}
