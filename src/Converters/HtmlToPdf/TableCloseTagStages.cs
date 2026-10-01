using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Table close tag: an inline or block element closed - the line and its box segments settled.</summary>
    private static void CloseInlineOrBlockTag(TableStyleConfig cfg, TableParseState ps, string tag)
    {
        // A closing heading bar: its box segment records the line about
        // to push (the segment index is the PUSHED line's), then the
        // line closes — block semantics.
        if (tag is "h1" or "h2" && ps.cell is not null && cfg.chainBase is not null)
        {
            if (ps.chainOpenElems is { Count: > 0 })
                for (var k = ps.chainOpenElems.Count - 1; k >= 0; k--)
                    if (ps.chainOpenElems[k].Tag == tag)
                    {
                        var hPopped = ps.chainOpenElems[k];
                        ps.chainOpenElems.RemoveAt(k);
                        ChainBoxCloseMaybe(ps, hPopped);
                        break;
                    }
            if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe);
        }
        // A closing paragraph ends its line; closing style tags restore the
        // enclosing style context. Band dialect: a whitespace-only <p>
        // (the styled &nbsp; spacer idiom) keeps its line box. A <div>
        // reaches here only when the chain pass pushed an entry for it —
        // nothing else stacks divs, so the scan finds nothing otherwise.
        if (tag == "p" && ps.cell is not null && ps.line.Length > 0)
        {
            var pBlank = cfg.bandDialect && IsAllWhitespace(ps.line)
                && (ps.lineFontPt > 0 || ps.curFontPt > 0);
            if (pBlank && ps.lineFontPt <= 0) ps.lineFontPt = ps.curFontPt;
            PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, keepIfBlank: pBlank, joinNext: true);
        }
        for (var k = ps.styleStack.Count - 1; k >= 0; k--)
            if (ps.styleStack[k].Tag == tag)
            {
                ps.curFontPt = ps.styleStack[k].PrevPt; ps.curFamily = ps.styleStack[k].PrevFamily;
                ps.curColor = ps.styleStack[k].PrevColor;
                if (ps.styleStack[k].BoldBump && ps.boldDepth > 0) { ps.boldDepth--; UaMarkBoldRun(ps, ps.boldDepth > 0); }
                if (ps.styleStack[k].ItalicBump && ps.italicDepth > 0) ps.italicDepth--;
                ps.styleStack.RemoveAt(k);
                ps.cellDecorActive?.RemoveAll(d => d.Depth > ps.styleStack.Count);
                ps.uaUpperDepths.RemoveAll(d => d >= ps.styleStack.Count);
                break;
            }
        // …and closes its chain-ancestor entry (span/font/div opens
        // pushed one whenever the chain pass is active).
        if (ps.chainOpenElems is { Count: > 0 } && tag != "p")
            for (var k = ps.chainOpenElems.Count - 1; k >= 0; k--)
                if (ps.chainOpenElems[k].Tag == tag)
                {
                    var poppedElem = ps.chainOpenElems[k];
                    ps.chainOpenElems.RemoveAt(k);
                    ChainBoxCloseMaybe(ps, poppedElem);
                    break;
                }
        // A font-weight:normal run ends: the enclosing bold resumes.
        if (cfg.chainUnbold.Count > 0 && cfg.chainUnbold[^1].Tag == tag)
        {
            ps.boldDepth = cfg.chainUnbold[^1].PrevBoldDepth;
            cfg.chainUnbold.RemoveAt(cfg.chainUnbold.Count - 1);
        }
    }

    /// <summary>Table close tag: the DataWorks form controls - select, option and textarea - closed.</summary>
    private static void CloseDataWorksFormTag(TableStyleConfig cfg, TableParseState ps, string tag)
    {
        if (cfg.dwFormCells && tag == "option" && ps.dwSelectDepth > 0 && ps.dwOptBuf.Length > 0)
        {
            var dwOpt2 = ps.dwOptBuf.ToString().Trim();
            ps.dwFirstOpt ??= dwOpt2;
            if (ps.dwOptSelected) ps.dwSelectedOpt = dwOpt2;
            ps.dwOptBuf.Clear(); ps.dwOptSelected = false;
        }
        if (cfg.dwFormCells && tag == "select" && ps.dwSelectDepth > 0)
        {
            ps.dwSelectDepth = 0;
            if (ps.cell is not null)
            {
                var dwVal2 = (ps.dwSelectedOpt ?? ps.dwFirstOpt ?? "").Trim();
                ps.line.Append(Table.InlineInputChar);
                // The drop-down draws with its arrow-button chrome and a
                // higher seat (reference box 145.7×16.5 up 2.3); the width
                // model keeps the bare option width.
                (ps.cellInputBoxes ??= new()).Add((DwSelectBoxWPt + DwSelectChromeWPt,
                    DwInputBoxHPt, dwVal2, false, DwSelectLiftPt));
                ps.lineHadText = true;
                ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, DwSelectBoxWPt + 4);
            }
        }
        if (cfg.dwFormCells && tag == "textarea" && ps.dwTextareaOpen)
        {
            ps.dwTextareaOpen = false;
            if (ps.cell is not null)
            {
                if (ps.line.Length > 0) PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, joinNext: true);
                ps.line.Append(Table.InlineInputChar);
                (ps.cellInputBoxes ??= new()).Add((ps.dwTaW, ps.dwTaH, ps.dwTaBuf.ToString().Trim(), true,
                    DwTextareaLiftPt));
                ps.lineHadText = true;
                ps.cellImgWidthPt = Math.Max(ps.cellImgWidthPt, ps.dwTaW + 4);
                PushLine(ps, cfg.redlineCells, cfg.dwFormCells, cfg.widenProbe, joinNext: true);
                ps.rowMinHeightPt = Math.Max(ps.rowMinHeightPt, ps.dwTaH + 4);
            }
        }
    }
}
