using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    private static double MeasureStyledFaceRun(string faceName, string s, double fontSizePt)
    {
        if (PosFace(faceName).parser is not null || string.IsNullOrEmpty(faceName))
            return MeasureFaceText(faceName, s, fontSizePt);
        if (!_styledMeasureCache.TryGetValue(faceName, out var e))
        {
            Text.GlyphOutlineParser? p2 = null; double upm2 = 1000;
            try
            {
                var styled = faceName.EndsWith(" Bold", StringComparison.OrdinalIgnoreCase)
                    ? (faceName[..^5], Text.FontStyles.Bold)
                    : faceName.EndsWith(" Italic", StringComparison.OrdinalIgnoreCase)
                    ? (faceName[..^7], Text.FontStyles.Italic)
                    : ((string?)null, Text.FontStyles.Regular);
                if (styled.Item1 is { Length: > 0 } fam
                    && Text.FontRepository.FindFont(fam, styled.Item2, ignoreCase: true)
                        ?.SourceFontData?.TtfData is { } ttf2)
                {
                    p2 = new Text.GlyphOutlineParser(ttf2);
                    upm2 = p2.UnitsPerEm > 0 ? p2.UnitsPerEm : 1000;
                }
            }
            catch { p2 = null; }
            e = (p2, upm2);
            _styledMeasureCache[faceName] = e;
        }
        if (e.parser is null) return MeasureFaceText(faceName, s, fontSizePt);
        double w = 0;
        for (var i = 0; i < s.Length; i++)
        {
            int cp = s[i] == ' ' ? ' ' : s[i];
            var gid = e.parser.CMap.TryGetValue(cp, out var g) ? g : 0;
            w += gid != 0
                ? e.parser.GetAdvanceWidth(gid) * fontSizePt / e.upm
                : UnmappedAdvance(cp, fontSizePt);
        }
        return w;
    }

    /// <summary>Greedy wrap on space and after-dash breakpoints with real face
    /// advances (the quirks CSS-run model): a line takes breakpoints while its
    /// text fits maxWidth, and a segment longer than maxWidth occupies its line
    /// whole (the limit is the document's widest segment, so only the defining
    /// segment ever hits this). Trailing whitespace left after the final
    /// breakpoint stays on the last line — a collapsed newline before a br
    /// survives as the fragment's trailing space.</summary>
    private static string[] DashAwareWordWrap(string text, double maxWidth, string face, double fontSize)
    {
        var lines = new List<string>();
        var n = text.Length;
        var start = 0;
        while (start < n)
        {
            var end = -1;          // best line end (exclusive)
            var nextStart = n;
            var scan = start;
            while (scan < n)
            {
                var sp = text.IndexOf(' ', scan);
                var da = text.IndexOf('-', scan);
                int cut, resume;
                if (sp < 0 && da < 0) { cut = n; resume = n; }
                else if (da < 0 || (sp >= 0 && sp < da)) { cut = sp; resume = sp + 1; }
                else { cut = da + 1; resume = da + 1; }
                var w = MeasureFaceText(face, text[start..cut].TrimEnd(' '), fontSize);
                if (w <= maxWidth + 1e-6 || end < 0)
                {
                    end = cut;
                    nextStart = resume;
                    if (w > maxWidth + 1e-6) break;   // over-long first segment, taken whole
                    scan = resume;
                    continue;
                }
                break;
            }
            if (end < 0) { end = n; nextStart = n; }
            // Only whitespace left past the final breakpoint: it belongs to this line.
            if (nextStart >= n && end < n && text[end..].Trim().Length == 0) end = n;
            lines.Add(text[start..end]);
            start = Math.Max(nextStart, end);
        }
        return lines.Count == 0 ? new[] { text } : lines.ToArray();
    }

    private static string[] MeasuredWordWrap(string text, double maxWidth, string face, double sizePt,
        // CSS break-word semantics: words wrap on SPACES first, and only a word
        // that alone overflows a whole line char-splits (after moving to its own
        // line). The default char-packs the WHOLE run once any word overflows -
        // the calibrated legacy dialects keep that.
        bool wordFirst = false,
        // A hyphen is a break opportunity too, the hyphen staying on its line (probed on the
        // UA grid: `(866) 331-3925` in a 65.14 pt column draws `(866) 331-` over `3925`).
        bool dashBreaks = false)
    {
        // Hard breaks (a cell's <br>) split first; each segment wraps on its own.
        if (text.Contains('\u0001'))
        {
            var all = new List<string>();
            foreach (var seg in text.Split('\u0001'))
                all.AddRange(MeasuredWordWrap(seg.Trim(' '), maxWidth, face, sizePt, wordFirst, dashBreaks));
            return all.Count == 0 ? [""] : all.ToArray();
        }
        if (string.IsNullOrEmpty(text)) return [""];
        if (MeasureFaceText(face, text, sizePt) <= maxWidth) return [text];
        // An all-whitespace run (an &nbsp; spacer chain) is ONE line box —
        // U+00A0 offers no break opportunity and blank ink never wraps.
        var allWs = true;
        foreach (var ch in text) if (ch is not (' ' or '\u00A0')) { allWs = false; break; }
        if (allWs) return [text];
        // Spaceless CJK runs and over-wide words break per character: pack
        // greedily to the width (the expected render char-splits long words
        // inside table cells).
        if (!text.Contains(' ') || (!wordFirst && MaxSpaceWordWidth(text, face, sizePt) > maxWidth))
        {
            var outLines = new List<string>();
            var ln = new StringBuilder();
            double lw = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var chw = MeasureFaceText(face, text[i].ToString(), sizePt);
                if (ln.Length > 0 && lw + chw > maxWidth)
                {
                    outLines.Add(ln.ToString());
                    ln.Clear();
                    lw = 0;
                    if (text[i] == ' ') continue;   // a break eats the space
                }
                ln.Append(text[i]);
                lw += chw;
            }
            if (ln.Length > 0) outLines.Add(ln.ToString());
            return outLines.Count == 0 ? [""] : outLines.ToArray();
        }
        var words = text.Split(' ');
        var spaceW = MeasureFaceText(face, " ", sizePt);
        var result = new List<string>();
        var line = new StringBuilder();
        double lineW = 0;
        foreach (var (word, spaced) in WrapUnits(words, dashBreaks))
        {
            var w = MeasureFaceText(face, word, sizePt);
            // break-word: a word that alone overflows a whole line moves to its
            // own line and char-splits there; the tail stays open so following
            // words continue on it.
            if (wordFirst && w > maxWidth)
            {
                if (line.Length > 0) { result.Add(line.ToString()); line.Clear(); lineW = 0; }
                var segs = MeasuredWordWrap(word, maxWidth, face, sizePt);
                for (var si = 0; si < segs.Length - 1; si++) result.Add(segs[si]);
                line.Append(segs[^1]);
                lineW = MeasureFaceText(face, segs[^1], sizePt);
                continue;
            }
            var gap = line.Length > 0 && spaced ? spaceW : 0;
            if (line.Length > 0 && lineW + gap + w > maxWidth)
            {
                result.Add(line.ToString());
                line.Clear(); lineW = 0; gap = 0;
            }
            if (gap > 0) line.Append(' ');
            line.Append(word); lineW += gap + w;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    /// <summary>The metric flow's wrap of a block carrying emphasis runs: every word measured in
    /// the face its run draws it in (measured: a bold fund name inside a serif sentence ends its
    /// line where its BOLD advance does, `are as follows:` wrapping whole under it).</summary>
    private static string[] MeasuredWordWrapRuns(Block block, double maxWidth, string face, double sizePt)
    {
        var text = block.Text;
        if (text.Contains('\u0001') || !text.Contains(' ')) return MeasuredWordWrap(text, maxWidth, face, sizePt);
        var result = new List<string>();
        var line = new StringBuilder();
        double lineW = 0;
        var pos = 0;
        foreach (var word in text.Split(' '))
        {
            var w = RunsMeasuredWidth(block, pos, word, face, sizePt);
            var gap = line.Length > 0 ? RunsMeasuredWidth(block, pos - 1, " ", face, sizePt) : 0;
            if (line.Length > 0 && lineW + gap + w > maxWidth)
            {
                result.Add(line.ToString());
                line.Clear(); lineW = 0; gap = 0;
            }
            if (gap > 0) line.Append(' ');
            line.Append(word); lineW += gap + w;
            pos += word.Length + 1;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    /// <summary>The advance of a piece of the block's text starting at a character offset, each
    /// run of it in the bold / italic variant its emphasis runs put it in.</summary>
    private static double RunsMeasuredWidth(Block block, int at, string s, string face, double sizePt)
    {
        double w = 0;
        var i = 0;
        while (i < s.Length)
        {
            var bold = InEmphasisRuns(block.BoldRuns, at + i);
            var ital = InEmphasisRuns(block.ItalicRuns, at + i);
            var j = i + 1;
            while (j < s.Length && InEmphasisRuns(block.BoldRuns, at + j) == bold && InEmphasisRuns(block.ItalicRuns, at + j) == ital) j++;
            w += MeasureFaceText(face + (bold ? " Bold" : "") + (ital ? " Italic" : ""), s[i..j], sizePt);
            i = j;
        }
        return w;
    }

    private static bool InEmphasisRuns(List<(int Start, int Length)>? runs, int p)
    {
        if (runs is null) return false;
        foreach (var (start, length) in runs) if (p >= start && p < start + length) return true;
        return false;
    }

    /// <summary>The units a wrap places one at a time: every word, and with dash breaks every
    /// hyphen-ended piece of a word; a unit that continues its word joins the line with no space.</summary>
    /// <summary>The characters inside a word that open a break opportunity: a hyphen (kept on its line) and a zero-width space (dropped).</summary>
    private static readonly char[] DashBreakChars = { '-', '\u200B' };

    private static IEnumerable<(string unit, bool spaced)> WrapUnits(string[] words, bool dashBreaks)
    {
        foreach (var word in words)
        {
            if (!dashBreaks || word.IndexOfAny(DashBreakChars) < 0 || word.Length < 2) { yield return (word, true); continue; }
            var first = true;
            foreach (var piece in DashSegments(word))
            {
                yield return (piece, first);
                first = false;
            }
        }
    }

    /// <summary>Cut every table nested INSIDE the top-level table out of
    /// <paramref name="tableHtml"/>, leaving a \u0002{index}\u0003 marker where
    /// each stood; the extracted HTML goes to <c>subTables</c> in
    /// marker order. The cell that carries a marker renders that table as its
    /// own grid inside the cell.</summary>
    private static (string result, List<string> subTables) ExtractNestedTables(string tableHtml)
    {
        var (result, subTables, _) = ExtractNestedTablesWithHosts(tableHtml);
        return (result, subTables);
    }

    /// <summary>The nested tables lifted out of a table's cells into markers, each with the classes of the divs open round it.</summary>
    private static (string result, List<string> subTables, List<string[]?> hostClasses) ExtractNestedTablesWithHosts(string tableHtml)
    {
        List<string>? subTables = default;
        subTables = new List<string>();
        var hostClasses = new List<string[]?>();
        var sb = new StringBuilder(tableHtml.Length);
        var pos = 0; var depth = 0;
        foreach (Match t in Regex.Matches(tableHtml, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var closing = t.Groups[1].Value.Length > 0;
            if (!closing)
            {
                depth++;
                if (depth == 2)
                {
                    sb.Append(tableHtml[pos..t.Index]);
                    sb.Append('\u0002').Append(subTables.Count).Append('\u0003');
                    hostClasses.Add(OpenDivClassesBefore(tableHtml, t.Index));
                    pos = t.Index;              // start of the nested table
                }
            }
            else
            {
                if (depth == 2)
                {
                    subTables.Add(tableHtml[pos..(t.Index + t.Length)]);
                    pos = t.Index + t.Length;
                }
                depth--;
            }
        }
        sb.Append(tableHtml[pos..]);
        return (sb.ToString(), subTables, hostClasses);
    }

    /// <summary>The classes of the divs still open at <paramref name="index"/> within the same
    /// cell (outermost first); null when none carries a class.</summary>
    private static string[]? OpenDivClassesBefore(string html, int index)
    {
        List<string>? found = null;
        var depth = 0;
        var cellDepth = 0;
        foreach (Match m in Regex.Matches(html[..index], @"<(/?)(div|td|th)\b([^>]*)>", RegexOptions.IgnoreCase | RegexOptions.RightToLeft))
        {
            var tag = m.Groups[2].Value.ToLowerInvariant();
            var closing = m.Groups[1].Value.Length > 0;
            // (a sibling grid's cells before the table are closed pairs; the first UNPAIRED cell
            //  opening is the host cell - the scan ends there)
            if (tag != "div") { if (closing) cellDepth++; else if (cellDepth > 0) cellDepth--; else break; continue; }
            if (cellDepth > 0) continue;
            if (closing) { depth++; continue; }
            if (depth > 0) { depth--; continue; }
            var cm = Regex.Match(m.Groups[3].Value, @"\bclass\s*=\s*(?:""([^""]*)""|'([^']*)'|([\w-]+))", RegexOptions.IgnoreCase);
            if (!cm.Success) continue;
            var names = (cm.Groups[1].Success ? cm.Groups[1].Value : cm.Groups[2].Success ? cm.Groups[2].Value : cm.Groups[3].Value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 0) continue;
            found ??= new List<string>();
            found.InsertRange(0, names);
        }
        return found?.ToArray();
    }

    /// <summary>Rough height of a nested table: its own rows at the given
    /// pitch plus its nested tables', recursively. Wrapped cell text is not
    /// modelled; the caller reserves at least this much row height.</summary>
    /// <summary>Emit one metric-cell line. Ideographs go out as SEPARATE runs at
    /// their cumulative advances — the expected output segments CJK shaping runs
    /// per character, so each ideograph is its own text fragment (a plain latin
    /// line stays one run).</summary>
    private static void EmitCellLineRuns(Page page, string fontRes, double fontSize,
        double x, double y, string text, string measureFace, double shear = 0)
    {
        var hasCjk = false;
        if (Environment.GetEnvironmentVariable("ASPOSE_H4_NOCJKSPLIT") is null)
        foreach (var ch in text) if (ch >= '⺀') { hasCjk = true; break; }
        if (!hasCjk)
        {
            EmitPositionedRun(page, fontRes, fontSize, x, y, text, shear);
            return;
        }
        var runX = x;
        var runStart = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            var boundary = i == text.Length || text[i] >= '⺀' || text[i] == ' ';
            if (!boundary) continue;
            if (i > runStart)
            {
                var seg = text[runStart..i];
                EmitPositionedRun(page, fontRes, fontSize, runX, y, seg, shear);
                runX += MeasureFaceText(measureFace, seg, fontSize);
            }
            if (i < text.Length)
            {
                if (text[i] == ' ')
                    runX += MeasureFaceText(measureFace, " ", fontSize);
                else
                {
                    var ideo = text[i].ToString();
                    EmitPositionedRun(page, fontRes, fontSize, runX, y, ideo, shear);
                    runX += MeasureFaceText(measureFace, ideo, fontSize);
                }
            }
            runStart = i + 1;
        }
    }

    /// <summary>Wrap-aware height of a nested table: the bordered draw strokes and
    /// fills each row box BEFORE its cells render, so it needs the real extent a
    /// nested grid will occupy. Each row is its tallest cell's wrapped line count
    /// on the row pitch — wrapping at the cell's width attribute (hard &lt;br&gt;
    /// breaks kept) — plus the table's cellpadding band.</summary>
    private static double NestedTableWrappedHeight(string html, double rowPitch,
        string face, double fontSize, double fallbackW)
    {
        (var inner, var subs) = ExtractNestedTables(html);
        var p = 0.75;
        var cpm = Regex.Match(inner, @"<table\b[^>]*\bcellpadding\s*=\s*[""']?(\d+(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (cpm.Success) p = double.Parse(cpm.Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture) * 0.75;
        var h = 2 * p;
        foreach (Match rm in Regex.Matches(inner,
            @"<tr\b[^>]*>([\s\S]*?)(?=<tr\b|</table)", RegexOptions.IgnoreCase))
        {
            double rowH = 0;
            foreach (Match cm in Regex.Matches(rm.Groups[1].Value,
                @"<t[dh]\b([^>]*)>([\s\S]*?)</t[dh]>", RegexOptions.IgnoreCase))
            {
                var wAttr = Regex.Match(cm.Groups[1].Value, @"\bwidth\s*=\s*[""']?(\d+(?:\.\d+)?)");
                var cw = wAttr.Success
                    ? double.Parse(wAttr.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * 0.75
                    : fallbackW;
                var brText = Regex.Replace(cm.Groups[2].Value, @"<br\s*/?\s*>",
                    "\u0001", RegexOptions.IgnoreCase);
                var txt = CollapseWs(DecodeEntities(Regex.Replace(brText, "<[^>]+>", " "))).Trim();
                if (txt.Length == 0) continue;
                rowH = Math.Max(rowH,
                    MeasuredWordWrap(txt, cw, face, fontSize).Length * rowPitch);
            }
            h += rowH;
        }
        foreach (var sub in subs)
            h += NestedTableWrappedHeight(sub, rowPitch, face, fontSize, fallbackW);
        return h;
    }

    /// <summary>Whether the wrapper's own cell (the first td under the table tag) is centred.</summary>
    private static bool WrapperCellCentred(string tableHtml)
        => Regex.Match(tableHtml, @"<table\b[^>]*>[\s\S]*?<td\b([^>]*)>", RegexOptions.IgnoreCase) is { Success: true } wTd
           && Regex.IsMatch(wTd.Groups[1].Value, @"\balign\s*=\s*[""']?center", RegexOptions.IgnoreCase);

    /// <summary>A centred wrapper cell centres a child grid narrower than its box: the
    /// child's left inset (the invoice's 958px grids sit 3 pt inside the 966px body box).</summary>
    private static double WrapperChildInset(string childHtml, bool centred, double avail)
    {
        if (!centred) return 0;
        var childW = DeclaredTableWidthPt(Regex.Match(childHtml, @"<table\b[^>]*>", RegexOptions.IgnoreCase).Value);
        return childW > 0 && childW < avail ? (avail - childW) / 2 : 0;
    }

    /// <summary>Whether every row of a wrapper body is a SINGLE cell: a row of several cells -
    /// a spacer column beside the grid-holding one included - is a grid of columns, not a
    /// stack (the invoice's 15px spacer column indents every nested grid).</summary>
    private static bool WrapperRowsStackSingleCells(string body)
    {
        var depth = 0;
        var cells = 0;
        foreach (Match t in Regex.Matches(body, @"<(/?)(table|tr|td)\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var closing = t.Groups[1].Value.Length > 0;
            var tag = t.Groups[2].Value.ToLowerInvariant();
            if (tag == "table") { depth += closing ? -1 : 1; continue; }
            if (depth > 0) continue;
            if (tag == "td" && !closing && ++cells > 1) return false;
            if (tag == "tr" && !closing) cells = 0;
        }
        return true;
    }

    private static (string wrapperAttrs, List<(string Html, bool NewCell)> children)? TrySplitWrapperStack(string tableHtml)
    {
        string? wrapperAttrs = default;
        List<(string Html, bool NewCell)>? children = default;
        wrapperAttrs = "";
        children = new List<(string, bool)>();
        var open = Regex.Match(tableHtml, @"<table\b([^>]*)>", RegexOptions.IgnoreCase);
        if (!open.Success) return null;
        wrapperAttrs = open.Groups[1].Value;
        // body of the OUTER table = up to its matching close
        var depth = 0;
        var bodyStart = open.Index + open.Length;
        var bodyEnd = -1;
        foreach (Match t in Regex.Matches(tableHtml, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
        {
            if (t.Groups[1].Value.Length == 0) depth++;
            else if (--depth == 0) { bodyEnd = t.Index; break; }
        }
        if (bodyEnd < 0) { return null; }
        var body = tableHtml[bodyStart..bodyEnd];
        if (!WrapperRowsStackSingleCells(body)) return null;

        // Every row must be a single td; every td must contain only tables.
        var pos = 0;
        var sawChild = false;
        while (true)
        {
            var td = Regex.Match(body[pos..], @"<td\b[^>]*>", RegexOptions.IgnoreCase);
            if (!td.Success) break;
            var cellStart = pos + td.Index + td.Length;
            // find the matching </td> at table-depth 0
            var scan = cellStart;
            var tDepth = 0;
            var cellEnd = -1;
            foreach (Match t in Regex.Matches(body[cellStart..], @"<(/?)(table|td)\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var closing = t.Groups[1].Value.Length > 0;
                var tag = t.Groups[2].Value.ToLowerInvariant();
                if (tag == "table") { tDepth += closing ? -1 : 1; continue; }
                if (tag == "td" && closing && tDepth == 0) { cellEnd = cellStart + t.Index; break; }
                if (tag == "td" && !closing && tDepth == 0) { cellEnd = cellStart + t.Index; break; }
            }
            if (cellEnd < 0) cellEnd = body.Length;
            var cell = body[cellStart..cellEnd];
            // the cell must be ONLY tables (+ whitespace); collect them —
            // tables sharing one cell stack flush, a new CELL starts a padded row
            var firstInCell = true;
            var rest = cell;
            while (true)
            {
                rest = rest.TrimStart();
                if (rest.Length == 0) break;
                var ct = Regex.Match(rest, @"^<table\b", RegexOptions.IgnoreCase);
                if (!ct.Success) { return null; }
                var cDepth = 0; var cEnd = -1;
                foreach (Match t in Regex.Matches(rest, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    if (t.Groups[1].Value.Length == 0) cDepth++;
                    else if (--cDepth == 0) { cEnd = t.Index + t.Length; break; }
                }
                if (cEnd < 0) { return null; }
                children.Add((rest[..cEnd], firstInCell));
                firstInCell = false;
                sawChild = true;
                rest = rest[cEnd..];
            }
            pos = cellEnd;
            var closeTd = Regex.Match(body[pos..], @"</td\s*>", RegexOptions.IgnoreCase);
            pos = closeTd.Success ? pos + closeTd.Index + closeTd.Length : body.Length;
        }
        // rows with more than one td disqualify: a second <td> before a </tr>
        // was consumed above only when it held tables; a mixed grid keeps the
        // normal path. Approximate by requiring at least one child and NO bare
        // text between the wrapper's structural tags.
        if (!sawChild) { return null; }
        var stripped = Regex.Replace(body, @"<table\b[\s\S]*", "", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<[^>]+>", "");
        if (DecodeEntities(stripped).Trim().Length > 0) { return null; }
        return (wrapperAttrs, children);
    }

    /// <summary>Draw one styled inline row at the flow cursor: optional full-content-width
    /// background bar (+1px bottom border), then the runs — left group at the row's left
    /// pad, right group right-aligned, or the whole group centered. Text renders in
    /// Arial (bold variant per run) as an embedded Type0 face so Cyrillic labels carry.</summary>
    private static void RenderRowBlock(FlowPosition cursor, Block block,
        double marginLeft, double contentWidth,
        List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks)
    {
        const double PxPt = 0.75;
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        cursor.y -= block.RowMarginTopPx * PxPt;
        var rowTop = cursor.y;
        var runs = block.RowRuns!;

        var fontDict = cursor.page.Dict.Get("Resources") is Core.PdfDictionary res
            ? res.Get("Font") as Core.PdfDictionary : null;

        var g = new StringBuilder();
        static void Rect(StringBuilder sb, System.Globalization.CultureInfo inv,
            Color c, double x, double yBot, double w, double h)
        {
            sb.Append($"{(c.R / 255.0).ToString("F5", inv)} {(c.G / 255.0).ToString("F5", inv)} {(c.B / 255.0).ToString("F5", inv)} rg ");
            sb.Append($"{x.ToString("F2", inv)} {yBot.ToString("F2", inv)} {w.ToString("F2", inv)} {h.ToString("F2", inv)} re f ");
        }

        if (block.RowBarColor is { } barc)
        {
            var bh = block.RowBarHeightPx * PxPt;
            g.Append("q ");
            Rect(g, invc, barc, marginLeft, rowTop - bh, contentWidth, bh);
            if (block.RowBarBorderColor is { } bbc)
                Rect(g, invc, bbc, marginLeft, rowTop - bh - PxPt, contentWidth, PxPt);
            g.Append("Q ");
        }

        double RunBoxWidth(RowRun r) => r.ImgSrc is not null
            ? r.ImgWPx * PxPt
            : MeasureFaceText(r.Bold ? "Arial Bold" : "Arial", r.Text, r.FontPx * PxPt)
              + (r.PadLeftPx + r.PadRightPx) * PxPt;

        double leftX = marginLeft + block.RowLeftPadPx * PxPt;
        if (block.RowCentered)
        {
            double total = 0;
            foreach (var r in runs) total += RunBoxWidth(r) + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
            leftX = marginLeft + (contentWidth - total) / 2;
        }
        double rightTotal = 0;
        foreach (var r in runs)
            if (r.RightGroup) rightTotal += RunBoxWidth(r) + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
        var rightX = marginLeft + contentWidth - block.RowRightPadPx * PxPt - rightTotal;

        foreach (var r in runs)
        {
            var boxW = RunBoxWidth(r);
            var x = r.RightGroup ? rightX : leftX;
            x += r.MarginLeftPx * PxPt;

            // baseline: centered in the row box (bar rows), or a plain first-line
            // baseline drop for bar-less rows.
            var fpt = r.FontPx * PxPt;
            var baseline = block.RowBarColor is not null
                ? rowTop - ((block.RowHeightPx - r.FontPx) / 2 + 0.82 * r.FontPx) * PxPt
                : rowTop - 0.85 * fpt;

            if (r.TopStripColor is { } sc)
            {
                g.Append("q ");
                Rect(g, invc, sc, x, rowTop - r.TopStripHeightPx * PxPt, boxW, r.TopStripHeightPx * PxPt);
                g.Append("Q ");
            }

            if (r.Text.Length > 0 && fontDict is not null)
            {
                var faceName = r.Bold ? "Arial Bold" : "Arial";
                var face = PosFace(faceName);
                if (face.ttf is not null)
                {
                    var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf,
                        faceName, r.Text, stripSpacesInBaseFont: true);
                    g.Append("BT ");
                    g.Append($"{(r.Color.R / 255.0).ToString("F5", invc)} {(r.Color.G / 255.0).ToString("F5", invc)} {(r.Color.B / 255.0).ToString("F5", invc)} rg ");
                    g.Append($"/{rn} {fpt.ToString("F1", invc)} Tf ");
                    g.Append($"1 0 0 1 {(x + r.PadLeftPx * PxPt).ToString("F2", invc)} {baseline.ToString("F2", invc)} Tm ");
                    g.Append('<').Append(Compat.ToHexString(hex)).Append("> Tj ");
                    g.Append("ET ");
                }
            }

            if (!string.IsNullOrEmpty(r.Url))
                pendingLinks.Add((cursor.page, new Aspose.Pdf.Rectangle(x, baseline - 0.3 * fpt, x + boxW, baseline + fpt), r.Url!, r.Text));

            if (r.RightGroup) rightX += boxW + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
            else leftX = x + boxW + r.MarginRightPx * PxPt;
        }

        if (g.Length > 0)
            cursor.page.AddContentStream(Encoding.ASCII.GetBytes(g.ToString()));
        cursor.y = rowTop - (block.RowHeightPx + block.RowMarginBottomPx) * PxPt;
    }
}
