using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Covering letter: the built flow emitted, breaking to a fresh page when a line will not fit.</summary>
    private static void EmitCoveringLetterFlow(CoveringLetterState cl)
    {
        cl.pg = cl.page;
        foreach (var ln in cl.flow)
        {
            var top = cl.y + ln.GapBefore;
            if (top + ln.BoxH > cl.pageHeight - LtMargin)
            {
                cl.pg = cl.doc.Pages.Add(cl.pageWidth, cl.pageHeight);
                EnsureFonts(cl.pg);
                EnsureFont(cl.pg, "Arial", "F8");
                EnsureFont(cl.pg, "ArialBold", "F9");
                EnsureFont(cl.pg, "ArialItalic", "F11");
                top = LtMargin;
            }
            if (ln.Img is not null)
                cl.pg.AddImage(ln.Img, new Rectangle(ln.X, cl.pageHeight - top - ln.ImgH,
                    ln.X + ln.ImgW, cl.pageHeight - top));
            if (ln.Bullet)
                EmitRun(cl, cl.pg, new OsRun("•", "Arial", 10.5), ln.X - LtBulletOff, top + ln.Drop);
            EmitLine(cl, cl.pg, ln, top + ln.Drop);
            cl.y = top + ln.BoxH;
        }
    }

    /// <summary>Covering letter: the address table emitted - name left, address right, second block below.</summary>
    private static void EmitCoveringLetterAddress(CoveringLetterState cl)
    {
        cl.tableX = cl.contentL + LtTableML;
        cl.tableRight = cl.tableX + 0.9 * (cl.contentR - cl.contentL);
        EmitRun(cl, cl.page, new OsRun(cl.addrName, "Arial", 10.5), cl.tableX, cl.y + cl.dropEm);
        for (var i = 0; i < cl.addrRight.Count; i++)
        {
            if (cl.addrRight[i].Length == 0) continue;
            var r = new OsRun(cl.addrRight[i], "Arial", 10.5);
            EmitRun(cl, cl.page, r, cl.tableRight - MeasureFaceText(r.Face, r.Text, r.Fs),
                cl.y + i * LtLineEm + cl.dropEm);
        }
        for (var i = 0; i < cl.addrLeft.Count; i++)
            EmitRun(cl, cl.page, new OsRun(cl.addrLeft[i], "Arial", 10.5),
                cl.tableX, cl.y + LtAddrRow2Off + i * LtLineEm + cl.dropEm);
        cl.y += Math.Max(cl.addrRight.Count * LtLineEm, LtAddrRow2Off + cl.addrLeft.Count * LtLineEm);
    }

    /// <summary>Covering letter: the h2 heading emitted in the sheet's heading blue.</summary>
    private static void EmitCoveringLetterHeading(CoveringLetterState cl)
    {
        cl.h2Line = 16.5 * 1.2;                 // 22px type on its 1.2em line
        cl.h2Drop = MetricBaselineDrop(16.5, cl.h2Line, cl.cm);
        cl.y = LtMargin + OsUaBody;
        Stream(cl, cl.page, "q 0.106 0.208 0.369 rg\n"); // the sheet's #1b355e heading blue
        for (var i = 0; i < cl.h2Lines.Count; i++)
            EmitRun(cl, cl.page, new OsRun(cl.h2Lines[i], "Candara Bold", 16.5),
                cl.contentL, cl.y + i * cl.h2Line + cl.h2Drop);
        Stream(cl, cl.page, "0 0 0 rg\nQ\n");
        cl.y += cl.h2Lines.Count * cl.h2Line + LtTableMT;
    }

    /// <summary>Covering letter: the document opened and its faces registered.</summary>
    private static void OpenCoveringLetterPage(CoveringLetterState cl)
    {
        cl.doc = new Document();
        cl.page = cl.doc.Pages.Add(cl.pageWidth, cl.pageHeight);
        EnsureFonts(cl.page);
        EnsureFont(cl.page, "Arial", "F8");
        EnsureFont(cl.page, "ArialBold", "F9");
        EnsureFont(cl.page, "ArialItalic", "F11");
        EnsureFont(cl.page, "CandaraBold", "F12");
        cl.inv = System.Globalization.CultureInfo.InvariantCulture;
    }

    /// <summary>Covering letter: the justify block walked into flow lines.</summary>
    private static void BuildCoveringLetterFlow(CoveringLetterState cl)
    {
        cl.flow = new List<LtLine>();
        cl.prevBottom = LtTableMB;           // the address table's margin-bottom opens the flow
        cl.firstGapExtra = LtSupGrow;           // the date line's superscript overshoot

        cl.body = cl.justM.Groups[1].Value;
        for (var i = 0; i < cl.body.Length;)
        {
            var m = Regex.Match(cl.body[i..], @"<(p|ul|br)\b", RegexOptions.IgnoreCase);
            if (!m.Success) break;
            var at = i + m.Index;
            var tag = m.Groups[1].Value.ToLowerInvariant();
            if (tag == "br")
            {
                // a bare <br> between blocks: one anonymous 1.2em line, no collapse
                cl.flow.Add(new LtLine
                {
                    X = cl.justL, BoxH = LtLineEm, Drop = cl.dropEm, GapBefore = cl.prevBottom,
                });
                cl.prevBottom = 0;
                i = cl.body.IndexOf('>', at) + 1;
                continue;
            }
            var close = cl.body.IndexOf($"</{tag}>", at, StringComparison.OrdinalIgnoreCase);
            if (close < 0) break;
            var openEnd = cl.body.IndexOf('>', at);
            var attrs = cl.body[at..openEnd];
            var inner = cl.body[(openEnd + 1)..close];
            i = close + tag.Length + 3;

            var mtM = Regex.Match(attrs, @"margin-top:\s*([\d.]+)px");
            var marginTop = mtM.Success
                ? double.Parse(mtM.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 0.75
                : LtPTop;
            if (tag == "ul")
            {
                var firstLi = true;
                foreach (Match li in Regex.Matches(inner, @"<li\b[^>]*>([\s\S]*?)</li>", RegexOptions.IgnoreCase))
                {
                    AddLines(cl, LtParseRuns(li.Groups[1].Value, out _), cl.liX, cl.liRight,
                        justify: true, liMode: true,
                        firstLi ? LtUlMargin : 0, 0, bullet: true);
                    firstLi = false;
                    cl.prevBottom = 0;
                }
                cl.prevBottom = LtUlMargin;
                continue;
            }
            var runs = LtParseRuns(inner, out var img);
            if (img is { } im)
            {
                // the signature paragraph: the image seats on its line's baseline
                var bytes = FetchRemoteImage(im.Src);
                double w = im.WPx * 0.75, h = w * 0.75;
                if (bytes is not null && TryReadImagePixelSize(bytes) is (var pw, var ph) && pw > 0)
                    h = w * ph / pw;
                cl.flow.Add(new LtLine
                {
                    X = cl.justL, BoxH = h + (LtLineP - cl.dropP), Drop = h,
                    GapBefore = Math.Max(cl.prevBottom, marginTop),
                    Img = bytes, ImgW = w, ImgH = h,
                });
                cl.prevBottom = LtPBottom;
                continue;
            }
            AddLines(cl, runs, cl.justL, cl.justR, justify: true, liMode: false,
                marginTop, LtPBottom, bullet: false);
        }
    }

    /// <summary>Covering letter: the heading and address table read off the html.</summary>
    private static bool ParseCoveringLetterHeader(CoveringLetterState cl)
    {
        cl.h2M = Regex.Match(cl.html, @"<h2\b[^>]*>([\s\S]*?)</h2>", RegexOptions.IgnoreCase);
        cl.justM = Regex.Match(cl.html,
            @"<div\b[^>]*class\s*=\s*[""']justify[""'][^>]*>([\s\S]*?)</div>", RegexOptions.IgnoreCase);
        cl.addrM = Regex.Match(cl.html, @"<table[^>]*margin-top:\s*100px[\s\S]*?(<table[^>]*>[\s\S]*?</table>)",
            RegexOptions.IgnoreCase);
        if (!cl.h2M.Success || !cl.justM.Success || !cl.addrM.Success) return false;

        cl.h2Lines = BrLines(cl, cl.h2M.Groups[1].Value);
        cl.addrTds = Regex.Matches(cl.addrM.Groups[1].Value,
            @"<td\b[^>]*>([\s\S]*?)</td>", RegexOptions.IgnoreCase);
        if (cl.addrTds.Count < 3) return false;
        cl.addrName = LtFlat(cl.addrTds[0].Groups[1].Value);
        // the rowspan cell keeps its interior blank lines; only the final
        // trailing <br> makes no line
        cl.addrRight = Regex.Split(cl.addrTds[1].Groups[1].Value, @"<br\s*/?>", RegexOptions.IgnoreCase)
            .Select(LtFlat).ToList();
        while (cl.addrRight.Count > 0 && cl.addrRight[^1].Length == 0) cl.addrRight.RemoveAt(cl.addrRight.Count - 1);
        cl.addrRight.Add("");                       // the pair of closing <br>s leaves one blank line
        cl.addrLeft = BrLines(cl, cl.addrTds[2].Groups[1].Value);
        return true;
    }
}
