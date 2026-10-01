using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal partial class HtmlToPdfConverter
{
    /// <summary>An input tag: a checkbox or a text box sized by the id-keyed width and height tables, added to the open cell as a run carrying the pending line and br state.</summary>
    private static void HandleMsoInputOpen(MsoFormParseState mp, Token tok)
    {
        FlushText(mp);
        if (mp.cell is null) return;
        var inp = new MsoInputBox();
        if (tok.Attributes is { } ia)
        {
            if (ia.TryGetValue("type", out var ty)
                && ty.Trim().Equals("checkbox", StringComparison.OrdinalIgnoreCase))
            { inp.Checkbox = true; inp.WPt = MsoCheckboxPt; inp.HPt = MsoCheckboxPt; }
            if (ia.ContainsKey("checked")) inp.Checked = true;
            if (ia.TryGetValue("value", out var v)) inp.Value = DecodeEntities(v);
            if (ia.TryGetValue("id", out var id) && !inp.Checkbox)
            {
                if (mp.idW.TryGetValue(id.Trim(), out var w)) inp.WPt = w;
                if (mp.idH.TryGetValue(id.Trim(), out var h)) inp.HPt = h;
            }
        }
        mp.cell.Runs.Add(new MsoRun
        { Input = inp, NewLine = mp.pendingLine, BrLine = mp.pendingBr, Center = mp.center });
        mp.pendingLine = false;
        mp.pendingBr = false;
    }

    /// <summary>A span tag: flushes the run and reads the inline style's font size, face and colour onto the open style.</summary>
    private static void HandleMsoSpanOpen(MsoFormParseState mp, Token tok)
    {
        FlushText(mp);
        if (tok.Attributes is { } sa && sa.TryGetValue("style", out var sst))
        {
            var fm = Regex.Match(sst, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (fm.Success) mp.fs = DtpNum(fm.Groups[1].Value);
            else if (Regex.IsMatch(sst, @"font-size\s*:\s*large", RegexOptions.IgnoreCase))
                mp.fs = 13.5;   // `large` at the Word base (measured header)
            if (Regex.IsMatch(sst, @"Arial", RegexOptions.IgnoreCase)) mp.face = "Arial";
            if (Regex.IsMatch(sst, @"color\s*:\s*white", RegexOptions.IgnoreCase)) mp.white = true;
            if (Regex.IsMatch(sst, @"color\s*:\s*teal", RegexOptions.IgnoreCase)) mp.teal = true;
        }
    }

    /// <summary>A p tag: flushes the run, starts a line and picks up a centre alignment.</summary>
    private static void HandleMsoParagraphOpen(MsoFormParseState mp, Token tok)
    {
        FlushText(mp);
        mp.pendingLine = true;
        if (tok.Attributes is { } pa
            && ((pa.TryGetValue("align", out var al)
                 && al.Trim().Equals("center", StringComparison.OrdinalIgnoreCase))
                || (pa.TryGetValue("style", out var pst)
                    && Regex.IsMatch(pst, @"text-align\s*:\s*center", RegexOptions.IgnoreCase))))
            mp.center = true;
    }

    /// <summary>A td tag: closes the open cell, resets the inline style and reads colspan, width, background and the border model (border:none/solid, then per-side overrides).</summary>
    private static void HandleMsoCellOpen(MsoFormParseState mp, Token tok)
    {
        CloseCell(mp);
        mp.row ??= new MsoRow();
        mp.cell = new MsoCell();
        mp.pendingLine = false;
        mp.bold = false; mp.ital = false; mp.fs = 12; mp.face = "Times New Roman";
        mp.white = false; mp.teal = false; mp.center = false;
        if (tok.Attributes is { } ca)
        {
            if (ca.TryGetValue("colspan", out var cs)
                && int.TryParse(cs.Trim(), out var csn) && csn > 1)
                mp.cell.ColSpan = csn;
            if (ca.TryGetValue("style", out var st))
            {
                var wm = Regex.Match(st, @"width\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                if (wm.Success) mp.cell.StyleWPt = DtpNum(wm.Groups[1].Value);
                if (Regex.IsMatch(st, @"background\s*:\s*teal", RegexOptions.IgnoreCase))
                    mp.cell.BgTeal = true;
                // border model: `border:none` clears all; `border:solid`
                // sets all; per-side declarations override afterwards.
                if (Regex.IsMatch(st, @"(?<!-)border\s*:\s*none", RegexOptions.IgnoreCase))
                    mp.cell.BTop = mp.cell.BLeft = mp.cell.BRight = mp.cell.BBottom = false;
                else if (Regex.IsMatch(st, @"(?<!-)border\s*:\s*solid", RegexOptions.IgnoreCase))
                    mp.cell.BTop = mp.cell.BLeft = mp.cell.BRight = mp.cell.BBottom = true;
                foreach (var (side, setter) in new (string, Action<bool>)[]
                {
                    ("top", v => mp.cell.BTop = v), ("left", v => mp.cell.BLeft = v),
                    ("right", v => mp.cell.BRight = v), ("bottom", v => mp.cell.BBottom = v),
                })
                {
                    var bm = Regex.Match(st, @"border-" + side + @"\s*:\s*(none|solid)",
                        RegexOptions.IgnoreCase);
                    if (bm.Success) setter(bm.Groups[1].Value.Equals("solid",
                        StringComparison.OrdinalIgnoreCase));
                    var bs = Regex.Match(st, @"border-" + side + @"-style\s*:\s*(none|solid)",
                        RegexOptions.IgnoreCase);
                    if (bs.Success) setter(bs.Groups[1].Value.Equals("solid",
                        StringComparison.OrdinalIgnoreCase));
                }
            }
        }
    }

    /// <summary>A tr tag: closes the open row and starts one, reading a style height in inches or points.</summary>
    private static void HandleMsoRowOpen(MsoFormParseState mp, Token tok)
    {
        CloseRow(mp);
        mp.row = new MsoRow();
        if (tok.Attributes is { } tra && tra.TryGetValue("style", out var trst))
        {
            var hm = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*in", RegexOptions.IgnoreCase);
            if (hm.Success) mp.row.StyleHPt = DtpNum(hm.Groups[1].Value) * 72.0;
            var hp = Regex.Match(trst, @"height\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
            if (hp.Success) mp.row.StyleHPt = DtpNum(hp.Groups[1].Value);
        }
    }

    /// <summary>A close tag: pops the nested table, select, option, cell, row or inline style it closes.</summary>
    private static void HandleMsoCloseTag(MsoFormParseState mp, string tag)
    {
        switch (tag)
        {
            case "table":
                if (mp.nestedDepth > 0) { CloseRow(mp); mp.nestedDepth--; }
                break;
            case "select": mp.inSelect = false; mp.inSelectedOption = false; break;
            case "option": mp.inSelectedOption = false; break;
            case "td": CloseCell(mp); mp.inSelect = false; break;
            case "tr": CloseRow(mp); mp.inSelect = false; break;
            case "b": case "strong": FlushText(mp); mp.bold = false; break;
            case "i": case "em": FlushText(mp); mp.ital = false; break;
            case "span": FlushText(mp); mp.fs = 12; mp.face = "Times New Roman"; mp.white = false; mp.teal = false; break;
            case "p": FlushText(mp); mp.pendingLine = true; mp.pendingBr = false; mp.center = false; break;
        }
    }
}
