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
        bool wordFirst = false)
    {
        // Hard breaks (a cell's <br>) split first; each segment wraps on its own.
        if (text.Contains('\u0001'))
        {
            var all = new List<string>();
            foreach (var seg in text.Split('\u0001'))
                all.AddRange(MeasuredWordWrap(seg.Trim(' '), maxWidth, face, sizePt, wordFirst));
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
        foreach (var word in words)
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
            if (line.Length > 0 && lineW + spaceW + w > maxWidth)
            {
                result.Add(line.ToString());
                line.Clear(); lineW = 0;
            }
            if (line.Length > 0) { line.Append(' '); lineW += spaceW; }
            line.Append(word); lineW += w;
        }
        if (line.Length > 0) result.Add(line.ToString());
        return result.Count == 0 ? [""] : result.ToArray();
    }

    /// <summary>Cut every table nested INSIDE the top-level table out of
    /// <paramref name="tableHtml"/>, leaving a \u0002{index}\u0003 marker where
    /// each stood; the extracted HTML goes to <paramref name="subTables"/> in
    /// marker order. The cell that carries a marker renders that table as its
    /// own grid inside the cell.</summary>
    private static string ExtractNestedTables(string tableHtml, out List<string> subTables)
    {
        subTables = new List<string>();
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
        return sb.ToString();
    }

    /// <summary>Rough height of a nested table: its own rows at the given
    /// pitch plus its nested tables', recursively. Wrapped cell text is not
    /// modelled; the caller reserves at least this much row height.</summary>
    /// <summary>Emit one metric-cell line. Ideographs go out as SEPARATE runs at
    /// their cumulative advances — the expected output segments CJK shaping runs
    /// per character, so each ideograph is its own text fragment (a plain latin
    /// line stays one run).</summary>
    private static void EmitCellLineRuns(Page page, string fontRes, double fontSize,
        double x, double y, string text, string measureFace)
    {
        var hasCjk = false;
        if (Environment.GetEnvironmentVariable("ASPOSE_H4_NOCJKSPLIT") is null)
        foreach (var ch in text) if (ch >= '⺀') { hasCjk = true; break; }
        if (!hasCjk)
        {
            EmitPositionedRun(page, fontRes, fontSize, x, y, text);
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
                EmitPositionedRun(page, fontRes, fontSize, runX, y, seg);
                runX += MeasureFaceText(measureFace, seg, fontSize);
            }
            if (i < text.Length)
            {
                if (text[i] == ' ')
                    runX += MeasureFaceText(measureFace, " ", fontSize);
                else
                {
                    var ideo = text[i].ToString();
                    EmitPositionedRun(page, fontRes, fontSize, runX, y, ideo);
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
        var inner = ExtractNestedTables(html, out var subs);
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

    private static bool TrySplitWrapperStack(string tableHtml, out string wrapperAttrs,
        out List<(string Html, bool NewCell)> children)
    {
        wrapperAttrs = "";
        children = new List<(string, bool)>();
        var open = Regex.Match(tableHtml, @"<table\b([^>]*)>", RegexOptions.IgnoreCase);
        if (!open.Success) return false;
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
        if (bodyEnd < 0) { return false; }
        var body = tableHtml[bodyStart..bodyEnd];

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
                if (!ct.Success) { return false; }
                var cDepth = 0; var cEnd = -1;
                foreach (Match t in Regex.Matches(rest, @"<(/?)table\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    if (t.Groups[1].Value.Length == 0) cDepth++;
                    else if (--cDepth == 0) { cEnd = t.Index + t.Length; break; }
                }
                if (cEnd < 0) { return false; }
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
        if (!sawChild) { return false; }
        var stripped = Regex.Replace(body, @"<table\b[\s\S]*", "", RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"<[^>]+>", "");
        if (DecodeEntities(stripped).Trim().Length > 0) { return false; }
        return true;
    }

    /// <summary>Draw one styled inline row at the flow cursor: optional full-content-width
    /// background bar (+1px bottom border), then the runs — left group at the row's left
    /// pad, right group right-aligned, or the whole group centered. Text renders in
    /// Arial (bold variant per run) as an embedded Type0 face so Cyrillic labels carry.</summary>
    private static void RenderRowBlock(Page page, Block block, ref double y,
        double marginLeft, double contentWidth,
        List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)> pendingLinks)
    {
        const double PxPt = 0.75;
        var invc = System.Globalization.CultureInfo.InvariantCulture;
        y -= block.RowMarginTopPx * PxPt;
        var rowTop = y;
        var runs = block.RowRuns!;

        var fontDict = page.Dict.Get("Resources") is Core.PdfDictionary res
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
                    g.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
                    g.Append("ET ");
                }
            }

            if (!string.IsNullOrEmpty(r.Url))
                pendingLinks.Add((page, new Aspose.Pdf.Rectangle(x, baseline - 0.3 * fpt, x + boxW, baseline + fpt), r.Url!, r.Text));

            if (r.RightGroup) rightX += boxW + (r.MarginLeftPx + r.MarginRightPx) * PxPt;
            else leftX = x + boxW + r.MarginRightPx * PxPt;
        }

        if (g.Length > 0)
            page.AddContentStream(Encoding.ASCII.GetBytes(g.ToString()));
        y = rowTop - (block.RowHeightPx + block.RowMarginBottomPx) * PxPt;
    }
}
