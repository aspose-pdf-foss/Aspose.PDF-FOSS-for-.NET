using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>A chained heading opens its own cell run, carrying the chain element's typography onto the line.</summary>
    private static void OpenChainHeadingCell(TableStyleConfig cfg, TableParseState ps, Token tok, string tag)
    {
        if (cfg.chainBase is not null && ps.cell is not null && ps.chainTdElem is not null)
        {
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
            var chHElem = ChainTokElem(tag, tok.Attributes);
            ps.chainOpenElems!.Add(chHElem);
            var hPrevPt = ps.curFontPt; var hPrevFam = ps.curFamily; var hBold = false;
            var hPrevColor = ps.curColor;
            if (MatchChainDecls(cfg.chainRules, BuildOpenChain(ps, cfg.chainBase)) is { } hd)
            {
                if (hd.TryGetValue("font-size", out var hfs))
                {
                    var hBase = ps.curFontPt > 0 ? ps.curFontPt
                        : ps.cellClassPt > 0 ? ps.cellClassPt : cfg.cellFontSize;
                    var hpm = Regex.Match(hfs.Trim(), @"^([\d.]+)\s*%$");
                    if (hpm.Success && double.TryParse(hpm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var hPct)
                        && hPct > 0)
                        ps.curFontPt = hBase * hPct / 100.0;
                    else if (ChainLenPt(hfs, hBase) is > 0 and var hAbs)
                        ps.curFontPt = hAbs;
                }
                if (hd.TryGetValue("font-weight", out var hfw)
                    && Regex.IsMatch(hfw, @"bold|[6-9]00", RegexOptions.IgnoreCase))
                {
                    hBold = true;
                    ps.boldDepth++;
                    if (cfg.widenProbe) ps.line.Append('');
                }
                if ((hd.TryGetValue("background-color", out var hbg)
                        || hd.TryGetValue("background", out hbg))
                    && ParseCssColor(hbg) is { } hFill)
                {
                    var hFontPt = ps.curFontPt > 0 ? ps.curFontPt : cfg.cellFontSize;
                    var hRun = new ChainBoxRun
                    {
                        Elem = chHElem, StartLen = ps.line.Length, Fill = hFill,
                        FullWidth = true,
                        TextCentered = hd.TryGetValue("text-align", out var hta)
                            && hta.Contains("center", StringComparison.OrdinalIgnoreCase),
                    };
                    if (hd.TryGetValue("color", out var hcol)
                        && ParseCssColor(hcol) is { } hTextCol)
                        hRun.TextColor = hTextCol;
                    if (hd.TryGetValue("padding", out var hpv))
                    {
                        var (hpT, hpR, hpB, hpL) = ChainPadPt(hpv, hFontPt);
                        hRun.PadT = hpT; hRun.PadR = hpR; hRun.PadB = hpB; hRun.PadL = hpL;
                    }
                    (ps.chainBoxOpen ??= new List<ChainBoxRun>()).Add(hRun);
                }
            }
            ps.styleStack.Add((tag, hPrevPt, hPrevFam, hBold, hPrevColor, false, false));
        }
    }
}
