using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The table parser's working set, lifted out of BuildTableFromHtml: each
// method takes the parse state, the column model and the settled dialect
// scalars it reads. Bodies are verbatim.
    private static List<CssElem> BuildOpenChain(TableParseState ps, List<CssElem>? chainBase)
    {
        var ch = new List<CssElem>(chainBase!);
        if (ps.chainTdElem is not null) ch.Add(ps.chainTdElem);
        if (ps.chainOpenElems is not null) ch.AddRange(ps.chainOpenElems);
        return ch;
    }

    private static string? EffectiveChainDisplay(TableParseState ps)
    {
        if (ps.chainOpenElems is null) return null;
        for (var k = ps.chainOpenElems.Count - 1; k >= 0; k--)
            if (ps.chainOpenElems[k].Display is { } dsp) return dsp;
        return null;
    }

    private static void ChainBoxOpenMaybe(TableParseState ps, HtmlLoadOptions? options, double cellFontSize, CssElem el, Dictionary<string, string> decls)
    {
        if (ps.cell is null || ps.chainTdElem is null) return;
        if (BackgroundBadge(decls, options) is { } badge)
        {
            // Inside an open box (a status pill) the badge is its trailing
            // circle; standing alone (the risks grid's category cells) it is
            // its OWN circle-only box run.
            ChainBoxRun host;
            if (ps.chainBoxOpen is { Count: > 0 })
                host = ps.chainBoxOpen[^1];
            else
            {
                host = new ChainBoxRun { Elem = el, StartLen = ps.line.Length };
                (ps.chainBoxOpen ??= new List<ChainBoxRun>()).Add(host);
            }
            host.CircleFill = badge.Fill;
            host.CircleD = badge.DiameterPt;
            if (decls.TryGetValue("color", out var bcol) && ParseCssColor(bcol) is { } bcolc)
                host.CircleLetterColor = bcolc;
            ps.chainTrafficElem = el;
            ps.chainTrafficRun = host;
            return;
        }
        if ((decls.TryGetValue("background-color", out var bgv)
                || decls.TryGetValue("background", out bgv))
            && ParseCssColor(bgv) is { } bfill
            && (el.Display ?? EffectiveChainDisplay(ps)) == "inline-block")
        {
            var runFontPt = ps.curFontPt > 0 ? ps.curFontPt : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
            var run = new ChainBoxRun { Elem = el, StartLen = ps.line.Length, Fill = bfill };
            if (decls.TryGetValue("padding", out var bpv))
            {
                var (bt, br3, bb, bl3) = ChainPadPt(bpv, runFontPt);
                run.PadT = bt; run.PadR = br3; run.PadB = bb; run.PadL = bl3;
            }
            if (decls.TryGetValue("border-radius", out var brv))
                run.Radius = Math.Max(0, ChainLenPt(brv, runFontPt));
            if (decls.TryGetValue("height", out var hv2))
                run.DeclH = Math.Max(0, ChainLenPt(hv2, runFontPt));
            if (decls.TryGetValue("letter-spacing", out var lsv))
                run.LetterSpacing = Math.Max(0, ChainLenPt(lsv, runFontPt));
            (ps.chainBoxOpen ??= new List<ChainBoxRun>()).Add(run);
        }
        // A plain styled run inside an open box: its padding-top spaces the
        // box's continuation line (the smaller ID line under a title plate).
        else if (ps.chainBoxOpen is { Count: > 0 }
            && decls.TryGetValue("padding-top", out var cptv))
        {
            var cptBase = ps.curFontPt > 0 ? ps.curFontPt
                : ps.cellClassPt > 0 ? ps.cellClassPt : cellFontSize;
            ps.chainBoxOpen[^1].ContPadTop = Math.Max(0, ChainLenPt(cptv, cptBase));
        }
    }

    private static void ChainBoxCloseMaybe(TableParseState ps, CssElem el)
    {
        if (ps.chainBoxOpen is { Count: > 0 } && ReferenceEquals(ps.chainBoxOpen[^1].Elem, el))
        {
            AddBoxSeg(ps, ps.chainBoxOpen[^1]);
            ps.chainBoxOpen.RemoveAt(ps.chainBoxOpen.Count - 1);
        }
        if (ReferenceEquals(ps.chainTrafficElem, el))
        {
            ps.chainTrafficElem = null;
            ps.chainTrafficRun = null;
        }
    }
}
