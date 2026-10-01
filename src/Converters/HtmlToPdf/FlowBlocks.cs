using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static List<Block> BuildFlowBlocks(ConvertState cv, bool absSpanLedger, List<BeforeMarker> beforeMarkers, string? elementGridFace, bool htmlHasFormInput, bool inlineBlockColRules, bool perTableFormGate, List<Block> rowBlocks, bool sheetChromesCells, string frag)
    {
        var fbk = new FlowBlocksState();
        fbk.cv = cv;
        fbk.absSpanLedger = absSpanLedger;
        fbk.beforeMarkers = beforeMarkers;
        fbk.elementGridFace = elementGridFace;
        fbk.htmlHasFormInput = htmlHasFormInput;
        fbk.inlineBlockColRules = inlineBlockColRules;
        fbk.perTableFormGate = perTableFormGate;
        fbk.rowBlocks = rowBlocks;
        fbk.sheetChromesCells = sheetChromesCells;
        fbk.frag = frag;
        fbk.list = new List<Block>();
        // A UA document whose tables all stand inside ONE bordered, width-declared div: that div
        // is a frame around them (measured on the valuation report: a 645 px `border: 2px solid`
        // div draws a 1.5 pt black box round its 640 px tables, centred inside it).
        if (fbk.cv.profile.uaStdSerif) fbk.frag = MarkFramedWrapperDiv(fbk.frag);
        // …and in the chain dialect the border may come from the div's CLASS rule, with no declared
        // width: the frame then spans the content box (measured on the data grid: `.datagrid
        // { border: 1px solid #006699 }` frames 96..499 round its width:100% table).
        else if (_quirksChainSheet) fbk.frag = MarkFramedWrapperDiv(fbk.frag, fbk.cv.css);
        // (a UA form document grids its tables even when one wrapper table holds every control)
        if (ContainsTable(fbk.frag)
            && (fbk.cv.profile.formDialectTables || !fbk.htmlHasFormInput || fbk.perTableFormGate || fbk.cv.profile.escapedAttrDoc
                || fbk.cv.profile.uaFormCells))
        {
            BuildTableSegmentBlocks(fbk);
        }
        else BuildPlainFlowBlocks(fbk);
        return fbk.list;
    }

    /// <summary>The fragment with its framed wrapper div renamed to the frame marker tag, when one
    /// bordered, width-declared div opens before the first table and closes after the last.</summary>
    private static string MarkFramedWrapperDiv(string frag, IReadOnlyDictionary<string, Dictionary<string, string>>? css = null)
    {
        if (FindFramedWrapperDiv(frag, css) is not var (open, closeAt)) return frag;
        var closeLen = Regex.Match(frag[closeAt..], @"</div\b[^>]*>", RegexOptions.IgnoreCase).Length;
        var openTag = open.Value;
        // a class-bordered div carries its rule's border on the marker's own style, where the
        // block parser reads the frame from
        if (css is not null && ClassBorderDecl(openTag, css) is { } clsBorder
            && !Regex.IsMatch(DivStyleOf(openTag), @"(?<![-\w])border\s*:", RegexOptions.IgnoreCase))
        {
            var st = DivStyleOf(openTag);
            var merged = "border:" + clsBorder + (st.Length > 0 ? ";" + st : "");
            openTag = Regex.IsMatch(openTag, @"\bstyle\s*=", RegexOptions.IgnoreCase)
                ? Regex.Replace(openTag, @"\bstyle\s*=\s*([""']).*?\1", "style=\"" + merged.Replace("\"", "'") + "\"", RegexOptions.IgnoreCase)
                : openTag[..^1] + " style=\"" + merged.Replace("\"", "'") + "\">";
        }
        return frag[..open.Index] + "<" + FrameDivTag + openTag[4..]
            + frag[(open.Index + open.Length)..closeAt] + "</" + FrameDivTag + ">" + frag[(closeAt + closeLen)..];
    }

    /// <summary>The `border` shorthand a div's class rule declares (a real stroke, not none/0), or null.</summary>
    private static string? ClassBorderDecl(string openTag, IReadOnlyDictionary<string, Dictionary<string, string>> css)
    {
        var cm = Regex.Match(openTag, @"\bclass\s*=\s*(?:[""']([^""']*)[""']|([\w-]+))", RegexOptions.IgnoreCase);
        if (!cm.Success) return null;
        var v = cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Value;
        foreach (var c in v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            foreach (var key in new[] { "." + c, "div." + c })
                if (css.TryGetValue(key, out var rule) && rule.TryGetValue("border", out var b)
                    && !Regex.IsMatch(b.Trim(), @"^(none|0(px|pt)?)\b", RegexOptions.IgnoreCase))
                    return b.Trim();
        return null;
    }

    /// <summary>The one bordered, width-declared div that opens before a fragment's first table and
    /// closes after its last: its open tag and where its close tag starts; null when there is none.</summary>
    private static (Match open, int closeAt)? FindFramedWrapperDiv(string frag, IReadOnlyDictionary<string, Dictionary<string, string>>? css = null)
    {
        var firstTable = Regex.Match(frag, @"<table\b", RegexOptions.IgnoreCase);
        if (!firstTable.Success) return null;
        var lastTableEnd = frag.LastIndexOf("</table", StringComparison.OrdinalIgnoreCase);
        var candidates = new List<Match>();
        foreach (Match m in Regex.Matches(frag[..firstTable.Index], @"<div\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var st = DivStyleOf(m.Value);
            if (Regex.IsMatch(st, @"(?<![-\w])border\s*:", RegexOptions.IgnoreCase)
                && Regex.IsMatch(st, @"(?<![-\w])width\s*:", RegexOptions.IgnoreCase))
                candidates.Add(m);
            // (a class-bordered div needs no declared width: its frame spans the content box)
            else if (css is not null && ClassBorderDecl(m.Value, css) is not null)
                candidates.Add(m);
        }
        // Innermost first, because the frame closest to the grids is the one that frames them.
        // An OUTER candidate is tried only when the inner one closes before the last table:
        // that is the sheet which wraps its whole page in a bordered div, whose inner blocks
        // are framed too but where only the page div spans the grids.
        for (var i = candidates.Count - 1; i >= 0; i--)
        {
            var open = candidates[i];
            // the div's matching close: the first </div> past its open at depth zero
            var depth = 0;
            var closeAt = -1;
            foreach (Match t in Regex.Matches(frag[(open.Index + open.Length)..], @"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase))
            {
                if (t.Groups[1].Value.Length == 0) { depth++; continue; }
                if (depth == 0) { closeAt = open.Index + open.Length + t.Index; break; }
                depth--;
            }
            if (closeAt >= lastTableEnd) return (open, closeAt);
        }
        return null;
    }

    /// <summary>A framed wrapper div's outer box width (its declared width plus both borders), or 0.</summary>
    private static double FramedWrapperDivOuterWidthPt(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css = null)
    {
        if (FindFramedWrapperDiv(html, css) is not var (open, _)) return 0;
        // The wrapper's box comes from the CASCADE: a sheet is as free to size and frame it in a
        // class rule as in the tag's own style attribute, and the reference reads both. The inline
        // style wins where it declares the same property.
        var st = WrapperDivDeclarations(open.Value, css);
        if (Regex.Match(st, @"(?<![-\w])width\s*:\s*([^;]+)", RegexOptions.IgnoreCase) is not { Success: true } wm
            || TryParseLength(wm.Groups[1].Value.Trim()) is not { } wPt) return 0;
        var sides = CssBorderSides(st);
        return wPt + sides[3].W + sides[1].W;
    }

    /// <summary>A wrapper div's declarations as one inline-style string: its class rules in source
    /// order, then its own style attribute. Returned in that spelling because the box readers
    /// (<see cref="CssBorderSides"/> and the width match) are written against a style string.</summary>
    private static string WrapperDivDeclarations(string openTag,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css)
    {
        var sb = new StringBuilder();
        if (css is not null
            && Regex.Match(openTag, @"\bclass\s*=\s*(?:[""']([^""']*)[""']|([\w-]+))", RegexOptions.IgnoreCase)
               is { Success: true } cm)
        {
            var names = (cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Value)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (var c in names)
                foreach (var key in new[] { "." + c, "div." + c })
                    if (css.TryGetValue(key, out var rule))
                        foreach (var kv in rule) sb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
        }
        return sb.Append(DivStyleOf(openTag)).ToString();
    }

    /// <summary>The marker tag a framed wrapper div is renamed to for the block parser.</summary>
    private const string FrameDivTag = "framediv";
}
