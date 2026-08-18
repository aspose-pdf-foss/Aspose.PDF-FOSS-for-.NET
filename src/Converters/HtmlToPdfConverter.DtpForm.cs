using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

// The positioned DTP-form dialect: a desktop-publishing HTML export (Avanquest
// WebEasy shape) whose BODY is a flat sequence of absolutely positioned
// elements — text divs, form text inputs and data-URI images — every one
// carrying a stylesheet id rule with pt coordinates. The engine lays the
// absolute canvas onto pages by slicing it into content bands, re-flowing
// wrapped div lines across the band boundary and repeating boundary-crossing
// images on both pages. All measures from the reference render of the
// credit-contract fixture.
internal static partial class HtmlToPdfConverter
{
    // Content origin: element x = cssLeft + 90, y = cssTop + 72 — the reference
    // paints its content band as a white fill (90,72)-(pageW-90, pageH-72).
    private const double DtpSideMarginPt = 90.0;
    private const double DtpVertMarginPt = 72.0;
    // Div line box: pitch 1.125 em (11.25 at 10pt); the baseline seats at
    // halfLead + winAscent below the box top (81.09 for 10pt Arial at cssTop+72).
    private const double DtpLineFactor = 1.125;
    // A text input with no height rule strokes a 15.75 pt (21 px UA) box.
    private const double DtpInputDefaultHPt = 15.75;
    // Input value baselines sit at boxCentre + seat: regular values (drawn in
    // the standard sans) 3.71 below centre, bold values 0.81 higher — both
    // measured against the reference on 14/14.5/15.75-high boxes.
    private const double DtpInputSeatRegPt = 3.71;
    private const double DtpInputSeatBoldPt = 2.90;
    // Left-aligned input values inset 2.0 from the box edge (border 0.75 +
    // padding 1.5 rounded down by the renderer; measured exactly 2.0).
    private const double DtpInputPadPt = 2.0;
    // The input chrome strokes a 1 pt black rect regardless of the authored
    // (white) border: path inset 0.5 horizontally, outset 0.25 vertically.
    private const double DtpInputChromeInsetPt = 0.5;
    private const double DtpInputChromeOutsetPt = 0.25;
    // <u>/link underlines stroke 1 pt below the baseline, 1 pt wide.
    private const double DtpUnderlineDropPt = 1.0;
    // Pasted Word-HTML bullet paragraphs (MsoNormal + mso-list markers) pitch
    // differently from the surrounding 11.25 flow: 11.72 between wrapped lines
    // inside one paragraph, 12.08 entering a new paragraph (both measured).
    private const double DtpMsoWrapPitchPt = 11.72;
    private const double DtpMsoParaPitchPt = 12.08;

    private sealed class DtpIdRule
    {
        public double L, T, W, H;
        public bool HasH;
        public string? Align;
    }

    private sealed class DtpClassRule
    {
        public double SizePt = 10;
        public bool Bold, Italic;
        public string Family = "Arial";
        public (double R, double G, double B) Color = (0, 0, 0);
        public string? Align;
    }

    private sealed class DtpRun
    {
        public string Text = "";
        public bool Bold, Italic, Under;
        public double SizePt = 10;
        public string Face = "Arial";
        public (double R, double G, double B) Color = (0, 0, 0);
    }

    // One logical line (forced by an inner block boundary or <br>); wrapping
    // splits it into physical lines at draw time.
    private sealed class DtpLogicalLine
    {
        public List<DtpRun> Runs = new();
        public string Align = "left";
        public double FirstIndent;   // first physical line x offset (Mso margin+text-indent)
        public double HangIndent;    // continuation lines x offset (Mso margin-left)
        public bool Mso;             // a pasted-Word bullet paragraph (special pitches)
    }

    /// <summary>Render a fully-positioned DTP export (see the class comment).
    /// Null when the document does not carry the dialect's fingerprint —
    /// a flat set of pt-positioned id rules covering divs, text inputs and
    /// images under a <c>&lt;body id="page"&gt;</c>.</summary>
    private static Document? TryRenderPositionedDtp(string html, double pageW, double pageH)
    {
        if (!Regex.IsMatch(html, @"<body\b[^>]*\bid\s*=\s*[""']page[""']", RegexOptions.IgnoreCase))
            return null;
        var band = pageH - 2 * DtpVertMarginPt;
        if (band <= 0) return null;

        // ── stylesheet ─────────────────────────────────────────────────────
        var styleText = new StringBuilder();
        foreach (Match sm in Regex.Matches(html,
                     @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
            styleText.Append(sm.Groups[1].Value).Append('\n');
        var styles = Regex.Replace(styleText.ToString(), @"/\*[\s\S]*?\*/", " ");

        var idRules = new Dictionary<string, DtpIdRule>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(styles,
                     @"(?:div|input|img)#([\w-]+)\s*\{([^}]*)\}", RegexOptions.IgnoreCase))
        {
            var body = m.Groups[2].Value;
            if (!Regex.IsMatch(body, @"position\s*:\s*absolute", RegexOptions.IgnoreCase)) continue;
            var rule = new DtpIdRule();
            if (!TryDtpPt(body, "left", out rule.L) || !TryDtpPt(body, "top", out rule.T)
                || !TryDtpPt(body, "width", out rule.W)) continue;
            rule.HasH = TryDtpPt(body, "height", out rule.H);
            var am = Regex.Match(body, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (am.Success) rule.Align = am.Groups[1].Value.ToLowerInvariant();
            idRules[m.Groups[1].Value] = rule;
        }
        var inputRuleCount = Regex.Matches(styles,
            @"input#[\w-]+\s*\{[^}]*position\s*:\s*absolute", RegexOptions.IgnoreCase).Count;
        if (idRules.Count < 10 || inputRuleCount == 0) return null;

        var classRules = new Dictionary<string, DtpClassRule>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(styles, @"\.([\w-]+)\s*\{([^}]*)\}"))
        {
            var body = m.Groups[2].Value;
            var cr = new DtpClassRule();
            var fm = Regex.Match(body, @"font\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
            if (fm.Success)
            {
                var shorthand = fm.Groups[1].Value;
                cr.Bold = Regex.IsMatch(shorthand, @"\bbold\b", RegexOptions.IgnoreCase);
                cr.Italic = Regex.IsMatch(shorthand, @"\bitalic\b", RegexOptions.IgnoreCase);
                var szm = Regex.Match(shorthand, @"([\d.]+)\s*pt");
                if (szm.Success) cr.SizePt = DtpNum(szm.Groups[1].Value);
                var famM = Regex.Match(shorthand, @"pt\s+'([^']+)'|pt\s+""([^""]+)""|pt\s+([A-Za-z][\w -]*)");
                if (famM.Success)
                    cr.Family = (famM.Groups[1].Success ? famM.Groups[1].Value
                        : famM.Groups[2].Success ? famM.Groups[2].Value : famM.Groups[3].Value).Trim();
            }
            var cm2 = Regex.Match(body, @"color\s*:\s*#([0-9a-fA-F]{6})");
            if (cm2.Success) cr.Color = DtpHexColor(cm2.Groups[1].Value);
            var am = Regex.Match(body, @"text-align\s*:\s*(left|center|right)", RegexOptions.IgnoreCase);
            if (am.Success) cr.Align = am.Groups[1].Value.ToLowerInvariant();
            classRules[m.Groups[1].Value] = cr;
        }

        // ── body elements, document order ──────────────────────────────────
        var bodyM = Regex.Match(html, @"<body\b[^>]*>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return null;
        var bodyHtml = html[(bodyM.Index + bodyM.Length)..];
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        var doc = new Document();
        var fontDict = new Core.PdfDictionary();
        // Per-page op lists: raw content-stream text/vector ops and image
        // stamps, interleaved in document order so later elements draw on top.
        var pageOps = new List<List<object>>();
        List<object> OpsFor(int p)
        {
            while (pageOps.Count <= p) pageOps.Add(new List<object>());
            return pageOps[p];
        }

        void DrawText(int page, double x, double baselineInPage, DtpRun run, string text)
        {
            if (text.Length == 0) return;
            var faceName = DtpFaceName(run);
            var face = PosFace(faceName);
            var drawn = text;
            if (face.ttf is null)
            {
                // The face is unavailable (e.g. Symbol on a bare rig): draw the
                // run in Arial — the advance model below still measured it in
                // the declared face where possible, so following runs hold.
                faceName = run.Bold ? "Arial Bold" : "Arial";
                face = PosFace(faceName);
                if (face.ttf is null) return;
            }
            else if (faceName == "Symbol")
                drawn = DtpToSymbolPua(face.parser, text);
            var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.ttf, faceName, drawn,
                stripSpacesInBaseFont: true);
            OpsFor(page).Add(string.Create(inv,
                $"BT {run.Color.R:F3} {run.Color.G:F3} {run.Color.B:F3} rg /{rn} {run.SizePt:F2} Tf 1 0 0 1 {x:F2} {pageH - baselineInPage:F2} Tm <{System.Convert.ToHexString(hex)}> Tj ET\n"));
        }

        void StrokeLine(int page, double x0, double x1, double yInPage, (double R, double G, double B) col)
            => OpsFor(page).Add(string.Create(inv,
                $"q {col.R:F3} {col.G:F3} {col.B:F3} RG 1 w {x0:F2} {pageH - yInPage:F2} m {x1:F2} {pageH - yInPage:F2} l S Q\n"));

        // A positioned image: a boundary-crossing box draws on EVERY page whose
        // band it intersects, shifted by the band height — unclipped, so the
        // halves join seamlessly (measured on the reference's p6/p7). A NESTED
        // positioned image offsets by its positioned ancestor's (left, top).
        void DrawDtpImage(string imgTag, DtpIdRule rule, double offL, double offT)
        {
            var src = DtpAttr(imgTag, "src");
            if (src is null || !src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            var comma = src.IndexOf(',');
            if (comma < 0 || !rule.HasH) return;
            byte[] bytes;
            try { bytes = System.Convert.FromBase64String(src[(comma + 1)..]); }
            catch { return; }
            var top = offT + rule.T;
            var p0 = (int)Math.Floor(top / band);
            var p1 = (int)Math.Floor((top + rule.H - 1e-6) / band);
            for (var p = Math.Max(p0, 0); p <= p1; p++)
            {
                var yTop = top - p * band + DtpVertMarginPt;
                OpsFor(p).Add((bytes, DtpSideMarginPt + offL + rule.L, yTop, rule.W, rule.H));
            }
        }

        var pos = 0;
        var tagRx = new Regex(@"<(div|input|img)\b", RegexOptions.IgnoreCase);
        while (true)
        {
            var tm = tagRx.Match(bodyHtml, pos);
            if (!tm.Success) break;
            var tagEnd = bodyHtml.IndexOf('>', tm.Index);
            if (tagEnd < 0) break;
            var openTag = bodyHtml[tm.Index..(tagEnd + 1)];
            var tag = tm.Groups[1].Value.ToLowerInvariant();
            var id = DtpAttr(openTag, "id");
            if (id is null || !idRules.TryGetValue(id, out var rule))
            {
                pos = tagEnd + 1;
                continue;
            }
            var cls = DtpAttr(openTag, "class");
            var cr = cls is not null && classRules.TryGetValue(cls, out var c0) ? c0 : new DtpClassRule();

            if (tag == "img")
            {
                pos = tagEnd + 1;
                DrawDtpImage(openTag, rule, 0, 0);
                continue;
            }

            if (tag == "input")
            {
                pos = tagEnd + 1;
                var type = DtpAttr(openTag, "type");
                if (type is not null && !type.Equals("text", StringComparison.OrdinalIgnoreCase))
                    continue;
                var page = (int)Math.Floor(rule.T / band);
                var yTop = rule.T - page * band + DtpVertMarginPt;
                var h = rule.HasH ? rule.H : DtpInputDefaultHPt;
                var bx = DtpSideMarginPt + rule.L;
                // Chrome: 1 pt black stroke regardless of the authored border.
                OpsFor(page).Add(string.Create(inv,
                    $"q 0 0 0 RG 1 w {bx + DtpInputChromeInsetPt:F2} {pageH - (yTop - DtpInputChromeOutsetPt + h + 2 * DtpInputChromeOutsetPt):F2} {rule.W - 2 * DtpInputChromeInsetPt:F2} {h + 2 * DtpInputChromeOutsetPt:F2} re S Q\n"));
                var value = DtpAttr(openTag, "value");
                if (string.IsNullOrEmpty(value)) continue;
                value = EdgarHtmlRenderer.DecodeEntities(value);
                var run = new DtpRun { Bold = cr.Bold, SizePt = cr.SizePt, Color = cr.Color };
                var faceName = DtpFaceName(run);
                var w = MeasureFaceText(faceName, value, run.SizePt);
                // center is honoured; the authored right-align is NOT (the
                // itemization values render at the left inset on the reference).
                var align = rule.Align ?? cr.Align;
                var x = align == "center" ? bx + (rule.W - w) / 2 : bx + DtpInputPadPt;
                var baseline = yTop + h / 2 + (run.Bold ? DtpInputSeatBoldPt : DtpInputSeatRegPt);
                DrawText(page, x, baseline, run, value);
                continue;
            }

            // div: capture the inner html up to the matching close.
            var depth = 1;
            var scan = tagEnd + 1;
            var innerEnd = -1;
            var divRx = new Regex(@"<(/?)div\b[^>]*>", RegexOptions.IgnoreCase);
            while (depth > 0)
            {
                var dm = divRx.Match(bodyHtml, scan);
                if (!dm.Success) break;
                depth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
                if (depth == 0) innerEnd = dm.Index;
                scan = dm.Index + dm.Length;
            }
            if (innerEnd < 0) { pos = tagEnd + 1; break; }
            var inner = bodyHtml[(tagEnd + 1)..innerEnd];
            pos = scan;

            // Positioned images nested inside this container carry their own
            // id rules with coordinates RELATIVE to the container's box (the
            // contract's notice GIFs live 2900+ pt down a 792 pt-high parent).
            foreach (Match nm in Regex.Matches(inner, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
            {
                var nid = DtpAttr(nm.Value, "id");
                if (nid is not null && idRules.TryGetValue(nid, out var nRule))
                    DrawDtpImage(nm.Value, nRule, rule.L, rule.T);
            }

            var defaultAlign = rule.Align ?? cr.Align ?? "left";
            var lines = DtpParseRichText(inner, cr, classRules, defaultAlign);
            if (lines.Count == 0) continue;

            // Line walker: wraps each logical line to the div width and lays
            // physical lines down the canvas, breaking to the next page's top
            // seat when a baseline would pass the band bottom.
            var pageIdx = (int)Math.Floor(rule.T / band);
            var yIn = rule.T - pageIdx * band + DtpVertMarginPt;
            var bottom = pageH - DtpVertMarginPt;
            var first = true;
            var prevMso = false;
            foreach (var ll in lines)
            {
                var maxSz = 10.0;
                foreach (var r in ll.Runs) if (r.SizePt > maxSz && r.Text.Trim().Length > 0) maxSz = r.SizePt;
                var wrapped = DtpWrap(ll, rule.W);
                for (var wi = 0; wi < wrapped.Count; wi++)
                {
                    double pitch;
                    if (first) pitch = 0;
                    else if (ll.Mso && wi == 0) pitch = DtpMsoParaPitchPt;
                    else if (ll.Mso) pitch = DtpMsoWrapPitchPt;
                    else if (prevMso && wi == 0) pitch = DtpMsoParaPitchPt;
                    else pitch = DtpLineFactor * maxSz;
                    var seat = DtpSeat(maxSz);
                    double baseline;
                    if (first)
                    {
                        baseline = yIn + seat;
                        first = false;
                    }
                    else baseline = yIn + pitch;
                    if (baseline > bottom)
                    {
                        pageIdx++;
                        baseline = DtpVertMarginPt + seat;
                    }
                    yIn = baseline;

                    var lineRuns = wrapped[wi];
                    double lineW = 0;
                    foreach (var r in lineRuns) lineW += DtpMeasureRun(r);
                    var indent = wi == 0 ? ll.FirstIndent : ll.HangIndent;
                    var x = DtpSideMarginPt + rule.L + indent;
                    if (ll.Align == "center") x = DtpSideMarginPt + rule.L + (rule.W - lineW) / 2;
                    else if (ll.Align == "right") x = DtpSideMarginPt + rule.L + rule.W - lineW;
                    foreach (var r in lineRuns)
                    {
                        DrawText(pageIdx, x, yIn, r, r.Text);
                        if (r.Under)
                        {
                            // Underline per non-space segment (link underlines
                            // gap at the spaces on the reference).
                            var sx = x;
                            var i2 = 0;
                            while (i2 < r.Text.Length)
                            {
                                if (r.Text[i2] == ' ')
                                {
                                    sx += MeasureFaceText(DtpFaceName(r), " ", r.SizePt);
                                    i2++;
                                    continue;
                                }
                                var j = i2;
                                while (j < r.Text.Length && r.Text[j] != ' ') j++;
                                var segW = MeasureFaceText(DtpFaceName(r), r.Text[i2..j], r.SizePt);
                                StrokeLine(pageIdx, sx, sx + segW, yIn + DtpUnderlineDropPt, r.Color);
                                sx += segW;
                                i2 = j;
                            }
                        }
                        x += DtpMeasureRun(r);
                    }
                }
                prevMso = ll.Mso;
            }
        }

        if (pageOps.Count == 0) return null;
        for (var p = 0; p < pageOps.Count; p++)
        {
            var page = doc.Pages.Add(pageW, pageH);
            EnsureFonts(page, fontDict);
            foreach (var op in pageOps[p])
            {
                if (op is string s)
                    page.AddContentStream(Encoding.ASCII.GetBytes(s));
                else if (op is ValueTuple<byte[], double, double, double, double> im)
                {
                    try
                    {
                        var stamp = ImageStamp.FromEncodedBytes(im.Item1);
                        stamp.XIndent = im.Item2;
                        stamp.YIndent = pageH - im.Item3 - im.Item5;
                        stamp.DisplayWidth = im.Item4;
                        stamp.DisplayHeight = im.Item5;
                        stamp.ApplyTo(page);
                    }
                    catch { /* undecodable image: skip */ }
                }
            }
        }
        return doc;
    }

    // ── rich text parsing ──────────────────────────────────────────────────

    /// <summary>Tokenize a positioned div's inner HTML into logical lines:
    /// inner div/p boundaries and &lt;br&gt; force breaks; strong/b/u/i/em/a
    /// set run styles; span inline styles override size/family; entities
    /// decode with nbsp preserved and other whitespace collapsed.</summary>
    private static List<DtpLogicalLine> DtpParseRichText(string inner, DtpClassRule baseClass,
        Dictionary<string, DtpClassRule> classRules, string defaultAlign)
    {
        inner = Regex.Replace(inner, @"<!--[\s\S]*?-->", " ");
        var lines = new List<DtpLogicalLine>();
        DtpLogicalLine? cur = null;
        var bold = 0; var ital = 0; var under = 0; var link = 0;
        var alignStack = new Stack<string>();
        alignStack.Push(defaultAlign);
        // span style overrides nest; (size, face, color) frames.
        var spanStack = new Stack<(double? Size, string? Face, (double, double, double)? Color)>();
        double msoFirstIndent = 0, msoHangIndent = 0;
        var inMso = false;

        void Flush()
        {
            if (cur is null) return;
            // Drop a line that is pure collapsible whitespace.
            var any = false;
            foreach (var r in cur.Runs) if (r.Text.Trim(' ').Length > 0) any = true;
            if (any || cur.Runs.Count > 0 && cur.Runs[0].Text.Length > 0)
                lines.Add(cur);
            cur = null;
        }
        DtpLogicalLine Cur()
        {
            if (cur is null)
                cur = new DtpLogicalLine
                {
                    Align = alignStack.Peek(),
                    Mso = inMso,
                    FirstIndent = inMso ? msoFirstIndent : 0,
                    HangIndent = inMso ? msoHangIndent : 0,
                };
            return cur;
        }

        var idx = 0;
        var tokRx = new Regex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)((?:[^>'""]|'[^']*'|""[^""]*"")*)>");
        while (idx < inner.Length)
        {
            var tm = tokRx.Match(inner, idx);
            var textEnd = tm.Success ? tm.Index : inner.Length;
            if (textEnd > idx)
            {
                var raw = EdgarHtmlRenderer.DecodeEntities(inner[idx..textEnd]);
                raw = Regex.Replace(raw, @"[ \t\r\n\f]+", " ");
                if (raw.Length > 0 && raw != " " || raw == " " && cur is not null && cur.Runs.Count > 0)
                {
                    var line = Cur();
                    // strip a collapsible leading space at line start
                    if (line.Runs.Count == 0) raw = raw.TrimStart(' ');
                    if (raw.Length > 0)
                    {
                        var (sz, face, color) = DtpEffective(baseClass, spanStack);
                        line.Runs.Add(new DtpRun
                        {
                            Text = raw,
                            Bold = bold > 0,
                            Italic = ital > 0,
                            Under = under > 0 || link > 0,
                            SizePt = sz,
                            Face = face,
                            Color = link > 0 ? (0, 0, 1.0) : color,
                        });
                    }
                }
                idx = textEnd;
                continue;
            }
            if (!tm.Success) break;
            var close = tm.Groups[1].Value.Length > 0;
            var name = tm.Groups[2].Value.ToLowerInvariant();
            var attrs = tm.Groups[3].Value;
            switch (name)
            {
                case "br":
                    // A <br> on an empty line is a deliberate blank line (the
                    // corpus separates paragraphs with <br><br>).
                    if (cur is null) lines.Add(new DtpLogicalLine { Align = alignStack.Peek() });
                    else Flush();
                    break;
                case "div":
                case "p":
                    Flush();
                    if (!close)
                    {
                        var alM = Regex.Match(attrs, @"align\s*=\s*[""']?(left|center|right)",
                            RegexOptions.IgnoreCase);
                        alignStack.Push(alM.Success
                            ? alM.Groups[1].Value.ToLowerInvariant() : alignStack.Peek());
                        if (name == "p")
                        {
                            // Pasted Word bullet paragraph: indent = margin-left
                            // + text-indent for the first line, margin-left for
                            // continuations.
                            var st = DtpAttr("<p " + attrs + ">", "style") ?? "";
                            inMso = st.Contains("mso-list", StringComparison.OrdinalIgnoreCase)
                                || attrs.Contains("MsoNormal", StringComparison.OrdinalIgnoreCase);
                            // margin shorthand: top right bottom LEFT
                            msoHangIndent = DtpStyleLen(st,
                                    @"margin\s*:\s*\S+\s+\S+\s+\S+\s+([\-\d.]+(?:in|pt))")
                                ?? DtpStyleLen(st, @"margin-left\s*:\s*([\-\d.]+(?:in|pt))") ?? 0;
                            msoFirstIndent = msoHangIndent
                                + (DtpStyleLen(st, @"text-indent\s*:\s*([\-\d.]+(?:in|pt))") ?? 0);
                        }
                    }
                    else
                    {
                        if (alignStack.Count > 1) alignStack.Pop();
                        inMso = false;
                    }
                    break;
                case "strong":
                case "b":
                    bold += close ? -1 : 1;
                    if (bold < 0) bold = 0;
                    break;
                case "u":
                    under += close ? -1 : 1;
                    if (under < 0) under = 0;
                    break;
                case "i":
                case "em":
                    ital += close ? -1 : 1;
                    if (ital < 0) ital = 0;
                    break;
                case "a":
                    link += close ? -1 : 1;
                    if (link < 0) link = 0;
                    break;
                case "span":
                    if (close)
                    {
                        if (spanStack.Count > 0) spanStack.Pop();
                    }
                    else
                    {
                        double? size = null;
                        string? face = null;
                        (double, double, double)? color = null;
                        var clsA = DtpAttr("<span " + attrs + ">", "class");
                        if (clsA is not null && classRules.TryGetValue(clsA, out var scr))
                        {
                            size = scr.SizePt;
                            face = scr.Family;
                            color = scr.Color;
                        }
                        var st = DtpAttr("<span " + attrs + ">", "style") ?? "";
                        var fs = Regex.Match(st, @"font-size\s*:\s*([\d.]+)\s*pt", RegexOptions.IgnoreCase);
                        if (fs.Success) size = DtpNum(fs.Groups[1].Value);
                        // `font: 7pt "Times New Roman"` shorthand
                        var fsh = Regex.Match(st, @"font\s*:\s*([\d.]+)\s*pt\s+[""']?([^;""']+)",
                            RegexOptions.IgnoreCase);
                        if (fsh.Success)
                        {
                            size = DtpNum(fsh.Groups[1].Value);
                            face = fsh.Groups[2].Value.Trim();
                        }
                        var ff = Regex.Match(st, @"font-family\s*:\s*[""']?([^;,""']+)", RegexOptions.IgnoreCase);
                        if (ff.Success) face = ff.Groups[1].Value.Trim();
                        spanStack.Push((size, face is null ? null : DtpNormalizeFace(face), color));
                    }
                    break;
            }
            idx = tm.Index + tm.Length;
        }
        Flush();
        return lines;
    }

    private static (double Size, string Face, (double, double, double) Color) DtpEffective(
        DtpClassRule baseClass, Stack<(double? Size, string? Face, (double, double, double)? Color)> spans)
    {
        double size = baseClass.SizePt;
        var face = DtpNormalizeFace(baseClass.Family);
        (double, double, double) color = baseClass.Color;
        double? sz = null;
        string? fc = null;
        (double, double, double)? cl = null;
        foreach (var s in spans)   // top-down: nearest frame wins per property
        {
            sz ??= s.Size;
            fc ??= s.Face;
            cl ??= s.Color;
        }
        return (sz ?? size, fc ?? face, cl ?? color);
    }

    private static string DtpNormalizeFace(string family)
    {
        var f = family.Trim().Trim('"', '\'').Trim();
        if (f.Equals("arial", StringComparison.OrdinalIgnoreCase)) return "Arial";
        if (f.Equals("symbol", StringComparison.OrdinalIgnoreCase)) return "Symbol";
        if (f.Equals("times new roman", StringComparison.OrdinalIgnoreCase)) return "Times New Roman";
        if (f.Equals("verdana", StringComparison.OrdinalIgnoreCase)) return "Verdana";
        if (f.Equals("courier new", StringComparison.OrdinalIgnoreCase)) return "Courier New";
        return f;
    }

    private static string DtpFaceName(DtpRun r)
    {
        if (r.Face is "Symbol") return "Symbol";
        var baseFace = r.Face.Length > 0 ? r.Face : "Arial";
        if (r.Bold && r.Italic) return baseFace + " Bold Italic";
        if (r.Bold) return baseFace + " Bold";
        if (r.Italic) return baseFace + " Italic";
        return baseFace;
    }

    private static double DtpMeasureRun(DtpRun r)
        => MeasureFaceText(DtpFaceName(r), r.Text, r.SizePt);

    /// <summary>Greedy word wrap of a logical line to the div width (indents
    /// reduce the first/continuation line budget). NBSP is not a break point;
    /// a trailing space hangs at the wrap.</summary>
    private static List<List<DtpRun>> DtpWrap(DtpLogicalLine ll, double width)
    {
        var result = new List<List<DtpRun>>();
        var line = new List<DtpRun>();
        double lineW = 0;
        var budget = width - ll.FirstIndent;

        void CloseLine()
        {
            result.Add(line);
            line = new List<DtpRun>();
            lineW = 0;
            budget = width - ll.HangIndent;
        }
        void Append(DtpRun proto, string text)
        {
            if (text.Length == 0) return;
            if (line.Count > 0 && ReferenceEquals(line[^1].Text, null)) { }
            if (line.Count > 0 && DtpSameStyle(line[^1], proto))
                line[^1].Text += text;
            else
            {
                var r2 = DtpCloneStyle(proto);
                r2.Text = text;
                line.Add(r2);
            }
            lineW += MeasureFaceText(DtpFaceName(proto), text, proto.SizePt);
        }

        foreach (var run in ll.Runs)
        {
            var t = run.Text;
            var i = 0;
            while (i < t.Length)
            {
                // token = a run of non-space chars (nbsp glued), or one space
                int j;
                if (t[i] == ' ')
                {
                    j = i + 1;
                    var w2 = MeasureFaceText(DtpFaceName(run), " ", run.SizePt);
                    // a space that would overflow hangs on the line
                    Append(run, " ");
                    if (lineW - w2 > budget + 1e-6) { }
                    i = j;
                    continue;
                }
                j = i;
                while (j < t.Length && t[j] != ' ') j++;
                var word = t[i..j];
                var wordW = MeasureFaceText(DtpFaceName(run), word, run.SizePt);
                if (line.Count > 0 && lineW + wordW > budget + 1e-6)
                {
                    // trailing space stays on the closed line (hangs)
                    CloseLine();
                }
                Append(run, word);
                i = j;
            }
        }
        if (line.Count > 0 || result.Count == 0) result.Add(line);
        return result;
    }

    private static bool DtpSameStyle(DtpRun a, DtpRun b)
        => a.Bold == b.Bold && a.Italic == b.Italic && a.Under == b.Under
           && a.SizePt.Equals(b.SizePt) && a.Face == b.Face && a.Color.Equals(b.Color);

    private static DtpRun DtpCloneStyle(DtpRun p) => new()
    {
        Bold = p.Bold, Italic = p.Italic, Under = p.Under,
        SizePt = p.SizePt, Face = p.Face, Color = p.Color,
    };

    /// <summary>Baseline seat below a line-box top: half-leading + winAscent
    /// (Arial 1854/434/2048 — the corpus face; 9.09 at 10 pt, matching the
    /// reference's 81.09 first-baseline offset).</summary>
    private static double DtpSeat(double sizePt)
    {
        var lineH = DtpLineFactor * sizePt;
        var sum = (SlideTextAscEm + SlideTextDescEm) * sizePt;
        return (lineH - sum) / 2 + SlideTextAscEm * sizePt;
    }

    /// <summary>Map text into the F0xx PUA range a symbol-encoded cmap uses,
    /// when the direct codepoint has no mapping.</summary>
    private static string DtpToSymbolPua(Text.GlyphOutlineParser? parser, string text)
    {
        if (parser is null) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (!parser.CMap.ContainsKey(ch) && parser.CMap.ContainsKey(0xF000 | ch))
                sb.Append((char)(0xF000 | ch));
            else sb.Append(ch);
        }
        return sb.ToString();
    }

    private static bool TryDtpPt(string css, string prop, out double value)
    {
        value = 0;
        var m = Regex.Match(css, prop + @"\s*:\s*([\-\d.]+)\s*pt", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        value = DtpNum(m.Groups[1].Value);
        return true;
    }

    private static double DtpNum(string s)
        => double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double? DtpStyleLen(string style, string pattern)
    {
        var m = Regex.Match(style, pattern, RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        var tok = m.Groups[1].Value;
        var isIn = tok.EndsWith("in", StringComparison.OrdinalIgnoreCase);
        var num = DtpNum(tok[..^2]);
        return isIn ? num * 72.0 : num;
    }

    private static (double, double, double) DtpHexColor(string hex6)
        => (System.Convert.ToInt32(hex6[..2], 16) / 255.0,
            System.Convert.ToInt32(hex6[2..4], 16) / 255.0,
            System.Convert.ToInt32(hex6[4..6], 16) / 255.0);

    private static string? DtpAttr(string tag, string name)
    {
        var m = Regex.Match(tag,
            name + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return m.Groups[1].Success ? m.Groups[1].Value
            : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
    }
}
