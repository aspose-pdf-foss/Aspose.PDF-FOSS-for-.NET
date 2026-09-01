using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    private const double HtmlCellFontSize = 12.0;

    private static readonly Regex BoldOnlyHtmlRegex = new(
        @"^\s*<(b|strong)\b[^>]*>(?<t>[^<>]+)</\1\s*>\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool TryBoldOnlyHtml(string? html, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(html)) return false;
        var m = BoldOnlyHtmlRegex.Match(html);
        if (!m.Success) return false;
        text = HtmlFragment.StripHtmlTags(m.Groups["t"].Value);
        return text.Length > 0;
    }

    private const double HtmlSmallFontSize = 10.0;

    /// <summary>One styled run on an HTML-engine cell line: text at an x-offset from the
    /// cell content-left, regular or bold serif, at its own size.</summary>
    private sealed class HtmlRun
    {
        public string Text = "";
        public double X;
        public double Size;
        public bool Bold;
        /// The href of the enclosing anchor, when this run sits inside one.
        public string? Url;
        /// A span-styled run's own colour (null = the line's colour).
        public Color? Color;
        /// text-decoration: underline on the enclosing span.
        public bool Underline;
    }

    private static readonly Regex HtmlEngineTagRegex = new(
        @"<(/?)(b|strong|small|div|br|p|a|ul|ol|li|span)\b[^>]*?>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A list item's text starts this far inside its list's content edge —
    /// the UA `padding-inline-start: 40px`.</summary>
    private const double UaListIndentPt = 40 * 0.75;

    /// <summary>…and the item's marker ends this far before that text (0.375 em, the
    /// UA marker gap): a bullet or an ordinal is laid RIGHT-aligned against it, so
    /// "4." and "10." end on the same x.</summary>
    private const double UaListMarkerGapEm = 0.375;

    private static readonly Regex HrefRegex = new(
        @"\bhref\s*=\s*(?:'(?<u>[^']*)'|""(?<u>[^""]*)""|(?<u>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    /// <summary>Decode the common HTML entities in tag-free text WITHOUT trimming or
    /// tag-stripping (unlike <see cref="HtmlFragment.StripHtmlTags"/>), so inter-word
    /// spaces at run boundaries survive.</summary>
    private static string DecodeHtmlEntities(string s) => s
        .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
        .Replace("&quot;", "\"").Replace("&apos;", "'").Replace("&#39;", "'")
        .Replace("&nbsp;", " ");

    /// <summary>Parse an HtmlFragment whose markup uses only the b/strong/small/div/br
    /// family into HTML-engine cell lines: serif runs (bold via b/strong, 10pt via small
    /// — nested smalls do NOT compound), greedy kerned wrap at <paramref name="availWidth"/>,
    /// div/small as block boundaries, br as a forced (possibly empty) line. Returns null
    /// when the markup falls outside the family (legacy path) or the faces are missing.</summary>
    private static List<CellLine>? ParseHtmlEngineCell(string? html, double availWidth,
        double baseSize = HtmlCellFontSize, bool breakWords = false, bool plainText = false)
    {
        if (string.IsNullOrEmpty(html)) return null;
        // Markup-free text is the legacy (inherited-face) path unless the caller's
        // dialect sets every HtmlFragment through the engine.
        if (html.IndexOf('<') < 0 && !plainText) return null;
        if (BoldSerifTtf() is null) return null;
        if (baseSize <= 0) baseSize = HtmlCellFontSize;
        var smallSize = baseSize * HtmlSmallFontSize / HtmlCellFontSize;
        var (rootBox, baseDrop) = SerifLineBox(baseSize);
        // Every tag present must belong to the allowed family.
        foreach (Match any in AnyTagRegex.Matches(html))
            if (!HtmlEngineTagRegex.IsMatch(any.Value)) return null;

        var lines = new List<CellLine>();
        var curRuns = new List<HtmlRun>();
        double curX = 0;
        var boldDepth = 0;
        var smallDepth = 0;
        var anyText = false;
        var anchors = new Stack<string>();
        // Open <span style=...> elements, innermost last: colour, an own font size
        // (unitless/pt = points, px = CSS px), and text-decoration: underline.
        var spans = new List<(Color? Color, double Size, bool Underline)>();
        // Open <ul>/<ol> elements, innermost last: each carries its kind and, for an
        // ordered list, the ordinal its next item takes.
        var lists = new List<(bool Ordered, int Counter)>();
        // The x every line of the item in progress starts at (its list's indent), and
        // the marker its FIRST line still owes.
        var lineIndent = 0.0;
        string? pendingMarker = null;

        void FlushLine(bool force)
        {
            // Trim the trailing spaces of the last run (a wrapped line never ends
            // in a visible space; fragment widths exclude it).
            while (curRuns.Count > 0)
            {
                var last = curRuns[^1];
                var trimmed = last.Text.TrimEnd(' ');
                if (trimmed.Length == 0) { curRuns.RemoveAt(curRuns.Count - 1); continue; }
                last.Text = trimmed;
                break;
            }
            if (curRuns.Count == 0 && !force) { curX = 0; return; }
            double maxSize = 0;
            var sb = new System.Text.StringBuilder();
            foreach (var r in curRuns)
            {
                if (r.Size > maxSize) maxSize = r.Size;
                sb.Append(r.Text);
            }
            // Consecutive runs of one anchor become a single Link rectangle over exactly
            // the anchor's characters, measured with the metrics that laid the line out.
            List<(double XOff, double W, Hyperlink Link)>? linkRuns = null;
            for (var ri = 0; ri < curRuns.Count; ri++)
            {
                var url = curRuns[ri].Url;
                if (string.IsNullOrEmpty(url)) continue;
                var x0 = curRuns[ri].X;
                var end = curRuns[ri].X + MeasureWidthKerned(curRuns[ri].Text, curRuns[ri].Size,
                    curRuns[ri].Bold ? _serifBoldTtf! : _serifTtf!);
                while (ri + 1 < curRuns.Count && curRuns[ri + 1].Url == url)
                {
                    ri++;
                    end = curRuns[ri].X + MeasureWidthKerned(curRuns[ri].Text, curRuns[ri].Size,
                        curRuns[ri].Bold ? _serifBoldTtf! : _serifTtf!);
                }
                (linkRuns ??= new()).Add((x0, end - x0, new WebHyperlink(url)));
            }
            // The line's CSS box is the larger of the root strut and the box of its
            // own biggest run (a 33pt span opens a 33pt line box; a 10pt small still
            // sits in the root 12pt strut) - probed: a mixed 12/33 line drops its
            // baseline winAscent+halfLead of the 33px box, and wrapped 33 lines
            // pitch at the pixel-rounded 33 box.
            var (ownBox, ownDrop) = SerifLineBox(maxSize > 0 ? maxSize : baseSize);
            lines.Add(new CellLine
            {
                Text = sb.ToString(),
                FontSize = maxSize > 0 ? maxSize : baseSize,
                Runs = curRuns.Count > 0 ? new List<HtmlRun>(curRuns) : null,
                LinkRuns = linkRuns,
                KernTj = true,
                HtmlEngine = true,
                BoxH = Math.Max(rootBox, ownBox),
                BaseOff = Math.Max(baseDrop, ownDrop),
            });
            curRuns.Clear();
            curX = lineIndent;
        }

        // The marker of the item whose first line is about to take ink: laid RIGHT
        // against the marker gap, so every ordinal of a list ends on the same x.
        void EmitPendingMarker()
        {
            if (pendingMarker is null) return;
            var marker = pendingMarker;
            pendingMarker = null;
            var gap = UaListMarkerGapEm * baseSize;
            var w = MeasureWidthKerned(marker, baseSize, _serifTtf!);
            curRuns.Add(new HtmlRun { Text = marker, X = lineIndent - gap - w, Size = baseSize });
            // The gap itself is written as a space run — it carries no
            // ink but keeps the extracted text readable.
            curRuns.Add(new HtmlRun { Text = " ", X = lineIndent - gap, Size = baseSize });
            anyText = true;
        }

        void EmitText(string raw)
        {
            // HTML whitespace collapse: any run of whitespace is one space. The text
            // between tags carries no markup, so decode entities WITHOUT trimming — the
            // trailing space before an inline element (e.g. "Min. Fee " before <small>)
            // is a real inter-word space that must render; leading spaces at a line
            // start are dropped separately below.
            var text = DecodeHtmlEntities(Regex.Replace(raw, @"\s+", " "));
            if (text.Length == 0) return;
            if (text == " ")
            {
                // Inter-tag whitespace: a space only mid-line, never at a line start.
                if (curRuns.Count == 0 && curX <= lineIndent) return;
            }
            var size = smallDepth > 0 ? smallSize : baseSize;
            Color? spanColor = null;
            var spanUnderline = false;
            foreach (var sp in spans)
            {
                if (sp.Size > 0) size = sp.Size;
                if (sp.Color is not null) spanColor = sp.Color;
                if (sp.Underline) spanUnderline = true;
            }
            var ttf = boldDepth > 0 ? _serifBoldTtf! : _serifTtf!;
            HtmlRun? run = null;   // runs split at tag boundaries: one piece = one run chain
            foreach (var token in SplitKeepingSpaces(text))
            {
                if (curRuns.Count == 0 && curX <= lineIndent && token.TrimStart(' ').Length == 0) continue;
                var tokenText = curRuns.Count == 0 && curX <= lineIndent && run is null
                    ? token.TrimStart(' ') : token;
                if (tokenText.Length == 0) continue;
                var w = MeasureWidthKerned(tokenText, size, ttf);
                var visible = tokenText.TrimEnd(' ');
                var visibleW = visible.Length == tokenText.Length ? w : MeasureWidthKerned(visible, size, ttf);
                if (availWidth > 0 && curX + visibleW > availWidth + 1e-6
                    && (curRuns.Count > 0 || curX > lineIndent))
                {
                    FlushLine(force: false);
                    run = null;
                    tokenText = tokenText.TrimStart(' ');
                    if (tokenText.Length == 0) continue;
                    w = MeasureWidthKerned(tokenText, size, ttf);
                }
                // IsBreakWords: a word still too wide for an EMPTY line breaks inside
                // itself, as many characters as fit per line, instead of overflowing
                // the column (which is what the flag off does).
                while (breakWords && availWidth > 0 && curX <= lineIndent + 1e-6
                       && MeasureWidthKerned(tokenText, size, ttf) > availWidth + 1e-6)
                {
                    var fit = 0;
                    while (fit + 1 < tokenText.Length
                           && MeasureWidthKerned(tokenText[..(fit + 1)], size, ttf) <= availWidth + 1e-6)
                        fit++;
                    if (fit <= 0) break;
                    curRuns.Add(new HtmlRun
                    {
                        Text = tokenText[..fit], X = 0, Size = size, Bold = boldDepth > 0,
                        Url = anchors.Count > 0 ? anchors.Peek() : null,
                        Color = spanColor, Underline = spanUnderline,
                    });
                    anyText = true;
                    FlushLine(force: false);
                    run = null;
                    tokenText = tokenText[fit..];
                    if (tokenText.Length == 0) break;
                    w = MeasureWidthKerned(tokenText, size, ttf);
                }
                if (tokenText.Length == 0) continue;
                EmitPendingMarker();
                if (run is null)
                {
                    run = new HtmlRun
                    {
                        Text = tokenText, X = curX, Size = size, Bold = boldDepth > 0,
                        Url = anchors.Count > 0 ? anchors.Peek() : null,
                        Color = spanColor, Underline = spanUnderline,
                    };
                    curRuns.Add(run);
                }
                else run.Text += tokenText;
                curX += w;
                anyText = true;
            }
        }

        var pos = 0;
        foreach (Match m in HtmlEngineTagRegex.Matches(html))
        {
            if (m.Index > pos) EmitText(html.Substring(pos, m.Index - pos));
            pos = m.Index + m.Length;
            var closing = m.Groups[1].Value.Length > 0;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            switch (tag)
            {
                case "b" or "strong":
                    boldDepth += closing ? -1 : 1;
                    if (boldDepth < 0) boldDepth = 0;
                    break;
                case "small":
                    // Inline: size drops to 10pt (no compounding when nested); the line
                    // structure comes from div/br only.
                    smallDepth += closing ? -1 : 1;
                    if (smallDepth < 0) smallDepth = 0;
                    break;
                case "div" or "p":
                    FlushLine(force: false);  // block boundary on open AND close
                    break;
                case "a":
                    // Inline anchor: its runs draw like their neighbours and carry the
                    // href so the line can annotate them.
                    if (closing) { if (anchors.Count > 0) anchors.Pop(); }
                    else
                    {
                        var href = HrefRegex.Match(m.Value);
                        anchors.Push(href.Success ? href.Groups["u"].Value.Trim() : "");
                    }
                    break;
                case "span":
                    if (closing)
                    {
                        if (spans.Count > 0) spans.RemoveAt(spans.Count - 1);
                    }
                    else
                        spans.Add(ParseSpanStyle(m.Value));
                    break;
                case "br":
                    FlushLine(force: true);   // forced line — empty box when nothing pending
                    break;
                case "ul" or "ol":
                    FlushLine(force: false);
                    if (closing)
                    {
                        if (lists.Count > 0) lists.RemoveAt(lists.Count - 1);
                    }
                    else
                    {
                        // A top-level list opens on its own block margin — one empty
                        // line box above its first item (a nested one takes none, the
                        // UA `ol ol { margin: 0 }` reset).
                        if (lists.Count == 0) FlushLine(force: true);
                        lists.Add((tag == "ol", 0));
                    }
                    pendingMarker = null;
                    lineIndent = lists.Count * UaListIndentPt;
                    curX = lineIndent;
                    break;
                case "li":
                    FlushLine(force: false);
                    if (!closing && lists.Count > 0)
                    {
                        var top = lists[^1];
                        if (top.Ordered)
                        {
                            top.Counter++;
                            lists[^1] = top;
                            pendingMarker = top.Counter.ToString(CultureInfo.InvariantCulture) + ".";
                        }
                        else pendingMarker = "\u2022";
                    }
                    else pendingMarker = null;
                    curX = lineIndent;
                    break;
            }
        }
        if (pos < html.Length) EmitText(html.Substring(pos));
        FlushLine(force: false);

        if (!anyText || lines.Count == 0) return null;
        // The cell's content box ends at the LAST baseline + the last line's win
        // descent — not at the full line-box bottom (no bottom leading).
        var last = lines[^1];
        last.BoxH = last.BaseOff + _serifDescFrac * last.FontSize;
        return lines;
    }

    /// <summary>Draw an HtmlFragment's HTML-engine lines with the fragment's content box
    /// top-left at (<paramref name="x"/>, <paramref name="topY"/>) — the same serif line
    /// model a table cell uses, so a fragment hosted outside a cell (a FloatingBox child)
    /// sets in the identical face, size and rhythm. Returns the height consumed, or null
    /// when the markup falls outside the engine family (the caller keeps its own path).</summary>
    internal static double? DrawHtmlEngineFragment(ContentStreamBuilder b, Page page,
        string? html, double x, double topY, double availWidth)
    {
        if (page is null) return null;
        if (ParseHtmlEngineCell(html, availWidth) is not { Count: > 0 } lines) return null;
        var fontDict = ResolvePageFontDict(page);
        for (var i = 0; i < lines.Count; i++)
        {
            var lineBase = topY - _serifBaseDrop - i * _serifRootBox;
            if (lines[i].Runs is not { Count: > 0 } runs) continue;
            foreach (var run in runs)
            {
                if (run.Text.Length == 0) continue;
                var ttf = run.Bold ? _serifBoldTtf : _serifTtf;
                if (ttf is null) continue;
                var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, ttf, run.Bold ? "Times New Roman Bold" : "Times New Roman",
                    run.Text, stripSpacesInBaseFont: true);
                b.BeginText();
                b.SetFont(resName, run.Size);
                b.MoveTextPosition(x + run.X, lineBase);
                if (KernAdjustments(run.Text, ttf) is { } kern) b.ShowTextHexKerned(hex, kern);
                else b.ShowTextHex(hex);
                b.EndText();
            }
        }
        // The last line ends at its own baseline + descent, not at a full box.
        return (lines.Count - 1) * _serifRootBox + lines[^1].BoxH;
    }

    private static readonly Regex EscNlDoctypeRegex = new(@"<!DOCTYPE[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlStyleRegex = new(@"<style[^>]*>[\s\S]*?</style\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlCellRegex = new(
        @"<(t[dh])\b[^>]*>(?<c>[\s\S]*?)</t[dh]\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlRowRegex = new(@"<tr\b[^>]*>(?<r>[\s\S]*?)</tr\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex EscNlBrRegex = new(@"<br\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>HTML default table chrome, in points: cellspacing 2px, cellpadding 1px.</summary>
    private const double EscNlCellSpacing = 2 * 0.75;

    private const double EscNlCellPadding = 1 * 0.75;

    /// <summary>A shrink-wrapped centred table centres over the band shrunk by this
    /// side margin each side (measured at two left margins: table left
    /// 23.34 on the margin-0 A4 band, 38.34 at margin 30 — both exactly
    /// bandLeft + (bandW − 2·60 − tableW)/2).</summary>
    private const double EscNlCenterSideMargin = 60;

    /// <summary>Render the escaped-newline footer fragment (see the block comment
    /// above) top-down from <paramref name="bandTop"/> across the band
    /// [<paramref name="bandLeft"/>, <paramref name="bandRight"/>]. Returns the
    /// content stream, or null when the markup is not this dialect's
    /// (no literal "\n" text or no table). <paramref name="consumedH"/> is the
    /// stack height consumed below <paramref name="bandTop"/>.</summary>
    internal static byte[]? DrawEscapedNewlineFooterHtml(Page page, string? html,
        double bandLeft, double bandRight, double bandTop, out double consumedH)
    {
        consumedH = 0;
        if (page is null || string.IsNullOrEmpty(html)) return null;
        if (!html.Contains("\\n", StringComparison.Ordinal)) return null;
        if (BoldSerifTtf() is null || _serifTtf is null || _serifBoldTtf is null) return null;

        var src = EscNlStyleRegex.Replace(EscNlDoctypeRegex.Replace(html, ""), "");
        var tblOpen = Regex.Match(src, @"<table\b[^>]*>", RegexOptions.IgnoreCase);
        var tblClose = Regex.Match(src, @"</table\s*>", RegexOptions.IgnoreCase);
        if (!tblOpen.Success || !tblClose.Success || tblClose.Index < tblOpen.Index) return null;
        var preMk = src[..tblOpen.Index];
        var tblMk = src[tblOpen.Index..(tblClose.Index + tblClose.Length)];
        var postMk = src[(tblClose.Index + tblClose.Length)..];

        const double fs = HtmlCellFontSize;                    // the serif default, 12 pt
        var (rootBox, baseDrop) = SerifLineBox(fs);
        var desc = _serifDescFrac * fs;
        var bandW = bandRight - bandLeft;
        if (bandW < 40) return null;

        // Tag-free text of a markup span, entities decoded, whitespace-only → "".
        static string TextOf(string mk)
        {
            var t = DecodeHtmlEntities(AnyTagRegex.Replace(mk, ""));
            return t.Trim().Length == 0 ? "" : t;
        }

        // Pre-table text splits at the <center> boundary: the part outside sets at
        // the band's left edge, the part inside centres — and the fostered text
        // (between the table's structural tags, outside every cell) glues onto the
        // inside-centre part, both being the table container's inline content.
        var centerOpen = Regex.Match(preMk, @"<center\b[^>]*>", RegexOptions.IgnoreCase);
        var preOutside = TextOf(centerOpen.Success ? preMk[..centerOpen.Index] : preMk);
        var preCenter = centerOpen.Success ? TextOf(preMk[centerOpen.Index..]) : "";
        // The structural tags sit BACK TO BACK in this markup (the "\n"s between
        // them are text), so removing the cells must not inject separators — the
        // reference glues the fostered "\n"s into one unspaced run.
        var fostered = TextOf(EscNlCellRegex.Replace(tblMk, ""));
        var tableCentred = centerOpen.Success;
        var preCentreRun = preCenter + fostered;
        // Post-table text before </center> centres; anything after it is outside.
        var centerClose = Regex.Match(postMk, @"</center\s*>", RegexOptions.IgnoreCase);
        var postCenter = TextOf(centerClose.Success ? postMk[..centerClose.Index] : "");
        var postOutside = TextOf(centerClose.Success ? postMk[centerClose.Index..] : postMk);

        // ── parse the table: bare <th>s before the first <tr> form their own row ──
        var rows = new List<List<(List<string> lines, bool bold)>>();
        var firstTr = EscNlRowRegex.Match(tblMk);
        var headSpan = firstTr.Success ? tblMk[..firstTr.Index] : tblMk;
        static List<string> CellLines(string inner)
        {
            var outLines = new List<string>();
            foreach (var piece in EscNlBrRegex.Split(inner))
            {
                var t = DecodeHtmlEntities(AnyTagRegex.Replace(piece, "")).Trim(' ');
                if (t.Length > 0) outLines.Add(t);
            }
            return outLines;
        }
        void AddRow(string rowMk)
        {
            var cells = new List<(List<string>, bool)>();
            foreach (Match cm in EscNlCellRegex.Matches(rowMk))
                cells.Add((CellLines(cm.Groups["c"].Value),
                    cm.Groups[1].Value.Equals("th", StringComparison.OrdinalIgnoreCase)));
            if (cells.Count > 0) rows.Add(cells);
        }
        if (EscNlCellRegex.IsMatch(headSpan)) AddRow(headSpan);
        foreach (Match rm in EscNlRowRegex.Matches(tblMk)) AddRow(rm.Groups["r"].Value);
        if (rows.Count == 0) return null;

        double Measure(string s, bool bold) =>
            MeasureWidthKerned(s, fs, bold ? _serifBoldTtf! : _serifTtf!);

        // ── column widths: HTML default chrome over the full band ──
        var nCols = 0;
        foreach (var r in rows) nCols = Math.Max(nCols, r.Count);
        var minBox = new double[nCols];
        var maxBox = new double[nCols];
        for (var c = 0; c < nCols; c++) minBox[c] = maxBox[c] = 2 * EscNlCellPadding;
        foreach (var r in rows)
            for (var c = 0; c < r.Count; c++)
            {
                var (cellLines, bold) = r[c];
                foreach (var ln in cellLines)
                {
                    maxBox[c] = Math.Max(maxBox[c], Measure(ln, bold) + 2 * EscNlCellPadding);
                    foreach (var w in ln.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        minBox[c] = Math.Max(minBox[c], Measure(w, bold) + 2 * EscNlCellPadding);
                }
            }
        var avail = bandW - (nCols + 1) * EscNlCellSpacing;
        double sumMin = 0, sumSlack = 0;
        for (var c = 0; c < nCols; c++) { sumMin += minBox[c]; sumSlack += maxBox[c] - minBox[c]; }
        var colBox = new double[nCols];
        for (var c = 0; c < nCols; c++)
        {
            colBox[c] = minBox[c];
            if (avail > sumMin && sumSlack > 1e-9)
                colBox[c] = Math.Min(maxBox[c],
                    minBox[c] + (avail - sumMin) * (maxBox[c] - minBox[c]) / sumSlack);
        }
        // ── wrap the cells at their content widths ──
        List<string> Wrap(List<string> cellLines, bool bold, double contentW)
        {
            var res = new List<string>();
            foreach (var ln in cellLines)
            {
                var cur = "";
                foreach (var w in ln.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var cand = cur.Length == 0 ? w : cur + " " + w;
                    if (cur.Length == 0 || Measure(cand, bold) <= contentW + 0.01) cur = cand;
                    else { res.Add(cur); cur = w; }
                }
                if (cur.Length > 0) res.Add(cur);
            }
            return res;
        }

        // ── emit top-down; the first line box hangs one win-descent below the band
        // top (measured: the first baseline = bandTop − desc − baseDrop) ──
        var fontDict = ResolvePageFontDict(page);
        var b = new ContentStreamBuilder();
        var topCursor = bandTop - desc;

        void EmitRun(string text, bool bold, double x, double baseline)
        {
            var ttf = bold ? _serifBoldTtf! : _serifTtf!;
            var (resName, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                fontDict, ttf, bold ? "Times New Roman Bold" : "Times New Roman",
                text, stripSpacesInBaseFont: true);
            b.BeginText();
            b.SetFont(resName, fs);
            b.MoveTextPosition(x, baseline);
            if (KernAdjustments(text, ttf) is { } kern) b.ShowTextHexKerned(hex, kern);
            else b.ShowTextHex(hex);
            b.EndText();
        }

        void EmitFlowText(string text, bool centred)
        {
            if (text.Length == 0) return;
            var cur = "";
            void Flush()
            {
                if (cur.Length == 0) return;
                var w = Measure(cur, false);
                var x = centred ? bandLeft + Math.Max(0, (bandW - w) / 2) : bandLeft;
                // A flow line whose baseline falls below the page bottom is not
                // drawn (measured: the after-</center> run renders at baseline
                // 3.61 on the taller band and vanishes at −0.39 on the shorter).
                var flowBase = topCursor - baseDrop;
                if (flowBase >= 0) EmitRun(cur, false, x, flowBase);
                topCursor -= rootBox;
                cur = "";
            }
            foreach (var w in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var cand = cur.Length == 0 ? w : cur + " " + w;
                if (cur.Length == 0 || Measure(cand, false) <= bandW + 0.01) cur = cand;
                else { Flush(); cur = w; }
            }
            Flush();
        }

        EmitFlowText(preOutside, centred: false);
        EmitFlowText(preCentreRun, centred: tableCentred);

        // The table: spacing above, rows of padded 13.5 pt line stacks, spacing
        // between and below. Cells centre vertically in their row (the td/th
        // default); th centres horizontally, td sets flush left. A table narrower
        // than the band centres over the band SHRUNK by a 60 pt side margin each
        // side, never left of the band edge (measured: table left 23.34 on the
        // margin-0 A4 band and 38.34 at margin 30 — both exactly
        // bandLeft + (bandW − 120 − tableW)/2).
        var tableW = (nCols + 1) * EscNlCellSpacing;
        for (var c = 0; c < nCols; c++) tableW += colBox[c];
        var tableLeft = tableCentred
            ? Math.Max(bandLeft, bandLeft + (bandW - 2 * EscNlCenterSideMargin - tableW) / 2)
            : bandLeft;
        topCursor -= EscNlCellSpacing;
        foreach (var r in rows)
        {
            var wrapped = new List<List<string>>();
            var rowLines = 1;
            for (var c = 0; c < r.Count; c++)
            {
                var lines2 = Wrap(r[c].lines, r[c].bold, colBox[c] - 2 * EscNlCellPadding);
                wrapped.Add(lines2);
                rowLines = Math.Max(rowLines, lines2.Count);
            }
            var rowTop = topCursor - EscNlCellPadding;
            var cellX = tableLeft + EscNlCellSpacing;
            for (var c = 0; c < r.Count; c++)
            {
                var (_, bold) = r[c];
                var contentX = cellX + EscNlCellPadding;
                var contentW = colBox[c] - 2 * EscNlCellPadding;
                var vOff = (rowLines - wrapped[c].Count) * rootBox / 2;
                for (var li = 0; li < wrapped[c].Count; li++)
                {
                    var ln = wrapped[c][li];
                    var x = bold
                        ? contentX + Math.Max(0, (contentW - Measure(ln, bold)) / 2)
                        : contentX;
                    EmitRun(ln, bold, x, rowTop - vOff - li * rootBox - baseDrop);
                }
                cellX += colBox[c] + EscNlCellSpacing;
            }
            topCursor -= 2 * EscNlCellPadding + rowLines * rootBox + EscNlCellSpacing;
        }

        EmitFlowText(postCenter, centred: tableCentred);
        EmitFlowText(postOutside, centred: false);

        consumedH = bandTop - topCursor;
        return b.Build();
    }

    /// <summary>True when every paragraph of the cell is a bold-only HtmlFragment (and the
    /// serif face resolves), i.e. the cell lays out on HTML-engine metrics
    /// with zero autofit padding.</summary>
    /// <summary>True when any of the cell's paragraphs is an <see cref="HtmlFragment"/>.
    /// Such a cell measures MIN-content under AutoFitToContent: the HTML shrink-to-fit
    /// measure reports the widest unbreakable word, so its column wraps.</summary>
    private static bool HasHtmlContent(Cell cell)
    {
        foreach (var p in cell.Paragraphs)
            if (p is HtmlFragment) return true;
        return false;
    }

    /// <summary>True when the cell holds an HtmlFragment of BLOCK-structured markup
    /// (paragraphs, lists, headings). Such a fragment is a block box, and its column
    /// FILLS the width the other columns leave: probed on a three-column auto-fit
    /// table, the list column took every point of the content box the two text columns
    /// did not, and moving the list to the middle column moved the fill with it. An
    /// INLINE fragment keeps the shrink-to-fit min-content measure.</summary>
    private static bool HasFillHtmlContent(Cell cell)
    {
        foreach (var p in cell.Paragraphs)
            if (p is HtmlFragment h
                && Converters.HtmlToPdfConverter.HasBlockStructure(h.HtmlContent ?? ""))
                return true;
        return false;
    }

    private static bool AllBoldSerifHtml(Cell cell)
    {
        if (cell.Paragraphs.Count == 0 || BoldSerifTtf() is null) return false;
        foreach (var p in cell.Paragraphs)
            if (p is not HtmlFragment h || !TryBoldOnlyHtml(h.HtmlContent, out _)) return false;
        return true;
    }
}
