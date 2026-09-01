using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    // Render a single-font inline-emphasis fragment (one <font face size>
    // wrapper holding only b/u/i runs) as embedded styled runs with
    // stroked underlines, on the half-leading line model: for a run of
    // s px, box top/bottom = hhea ascent/descent plus half of
    // (round(lineHeight px) - ascent - descent), maxed against the
    // 16px serif strut; the first baseline sits `above` under the
    // cursor and lines advance by (above + below).
    private bool RenderInlineEmphasisRuns(string iface, double ipt,
        List<(string text, bool bold, bool underline, bool italic)> iruns,
        FlowLayout flow, Page page, double marginLeft, double marginRight)
    {
        var regTtf = Text.FontRepository.GetTtfData(iface);
        if (regTtf is null) return false;
        var boldTtf = Text.FontRepository.GetTtfData(iface + " Bold") ?? regTtf;
        var regData = new Text.FontData(iface, Text.FontType.TrueType);
        regData.SetTtfData(regTtf);
        var boldData = new Text.FontData(iface + " Bold", Text.FontType.TrueType);
        boldData.SetTtfData(boldTtf);
        var mReg = Text.TextPaginator.CreateMeasurer(iface, ipt, regData);
        var mBold = Text.TextPaginator.CreateMeasurer(iface, ipt, boldData);

        // Line-box metrics in px (pt = px * 0.75), em = 2048.
        var sPx = ipt / 0.75;
        const double em = 2048.0;
        const double faceAscent = 1854, faceDescent = 434, faceLineGap = 67;
        var ascPx = faceAscent * sPx / em;
        var descPx = faceDescent * sPx / em;
        var lPx = Math.Round(sPx * (faceAscent + faceDescent + faceLineGap) / em,
            MidpointRounding.AwayFromZero);
        var halfLead = (lPx - ascPx - descPx) / 2;
        const double strutTop = 14.3984375, strutBottom = 3.6015625;
        var above = Math.Max(ascPx + halfLead, strutTop);
        var below = Math.Max(descPx + halfLead, strutBottom);
        var firstBaselinePt = above * 0.75;
        var linePitchPt = (above + below) * 0.75;

        // Tokenise the styled runs into word/space atoms for a greedy
        // wrap; a line break drops the space it breaks on.
        var atoms = new List<(string text, bool bold, bool underline, bool space)>();
        foreach (var run in iruns)
        {
            var t = run.text;
            var i0 = 0;
            while (i0 < t.Length)
            {
                var isSpace = t[i0] == ' ';
                var i1 = i0;
                while (i1 < t.Length && (t[i1] == ' ') == isSpace) i1++;
                atoms.Add((t[i0..i1], run.bold, run.underline, isSpace));
                i0 = i1;
            }
        }

        var contentW = page.Width - marginLeft - marginRight;
        var lines2 = new List<List<(string text, bool bold, bool underline, double x, double w)>>();
        var cur = new List<(string, bool, bool, double, double)>();
        double curW = 0;
        foreach (var at in atoms)
        {
            var w = at.bold ? mBold(at.text) : mReg(at.text);
            if (!at.space && curW + w > contentW && cur.Count > 0)
            {
                // Drop the trailing space atom the wrap breaks on.
                while (cur.Count > 0 && cur[^1].Item1.Trim().Length == 0)
                    cur.RemoveAt(cur.Count - 1);
                lines2.Add(cur);
                cur = new List<(string, bool, bool, double, double)>();
                curW = 0;
            }
            cur.Add((at.text, at.bold, at.underline, curW, w));
            curW += w;
        }
        if (cur.Count > 0) lines2.Add(cur);
        if (lines2.Count == 0) return false;

        var frameTop = flow.CurrentY;
        var fontDict2 = Table.ResolvePageFontDict(flow.CurrentPage);
        var b2 = new Content.ContentStreamBuilder();
        for (var li = 0; li < lines2.Count; li++)
        {
            var baseline = frameTop - firstBaselinePt - li * linePitchPt;
            // Merge adjacent same-style atoms into one show per run.
            var line = lines2[li];
            var ri = 0;
            while (ri < line.Count)
            {
                var rj = ri;
                while (rj + 1 < line.Count
                       && line[rj + 1].Item2 == line[ri].Item2
                       && line[rj + 1].Item3 == line[ri].Item3) rj++;
                var textRun = string.Concat(line.GetRange(ri, rj - ri + 1)
                    .ConvertAll(a => a.Item1));
                var xOff = line[ri].Item4;
                var runW = line[rj].Item4 + line[rj].Item5 - xOff;
                var bold2 = line[ri].Item2;
                var (res2, hex2) = Text.Type0FontEmbedder.Embed(fontDict2,
                    bold2 ? boldTtf : regTtf,
                    bold2 ? iface + " Bold" : iface,
                    textRun, stripSpacesInBaseFont: true);
                b2.BeginText();
                b2.SetFont(res2, ipt);
                b2.SetTextMatrix(1, 0, 0, 1, marginLeft + xOff, baseline);
                b2.ShowTextHex(hex2);
                b2.EndText();
                if (line[ri].Item3)
                {
                    // Stroked underline: a 0.1em-thick band whose top
                    // edge sits 0.1em below the baseline, spanning the
                    // run's advances.
                    b2.SaveState();
                    b2.SetStrokeGray(0);
                    b2.SetLineWidth(0.1 * ipt);
                    var uy = baseline - 0.15 * ipt;
                    b2.MoveTo(marginLeft + xOff, uy)
                      .LineTo(marginLeft + xOff + runW, uy)
                      .Stroke();
                    b2.RestoreState();
                }
                ri = rj + 1;
            }
        }
        flow.InjectContentAtCursor(b2.Build());
        // The block consumes its content extent without the outer
        // half-leadings: ascent + (n-1) pitches + descent.
        var blockH = (ascPx + (lines2.Count - 1) * (above + below) + descPx) * 0.75;
        flow.AdvanceY(blockH);
        return true;
    }

    private void RenderUaSerifChunk(string chunk, double uaWrapPt, HtmlFragment html,
        FlowLayout flow, double marginLeft)
    {
        var uaBody = System.Text.RegularExpressions.Regex.Replace(chunk,
            @"(?s)<head\b.*?</head>|<!--.*?-->", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // only p/h elements and inline span/emphasis tags carry text;
        // structural tags (html, body, div, ...) leave the scan so their
        // names never surface as content
        uaBody = System.Text.RegularExpressions.Regex.Replace(uaBody,
            @"</(?!p\b|h[1-6]\b|span\b|strong\b|b\b|em\b|i\b|br\b)[^>]*>" +
            @"|<(?!/|p\b|h[1-6]\b|span\b|strong\b|b\b|em\b|i\b|br\b)[^>]*>", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var uaB = new Content.ContentStreamBuilder();
        uaB.SaveState();
        var uaTimes = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
        var uaTimesB = Table.RegisterFont(flow.CurrentPage, "Times-Bold");
        double UaMeasure(string t, bool bold, double fsM)
        {
            try
            {
                return Text.FontRepository.TryFindFont(bold ? "Times-Bold" : "Times-Roman")
                    ?.MeasureString(t, fsM) ?? t.Length * fsM * 0.5;
            }
            catch { return t.Length * fsM * 0.5; }
        }
        var uaAfterHead = false;
        // at a line-box edge (chunk start, or after a blank <br> line)
        // the next baseline seats at the ascent drop, not a full pitch
        var uaAtBoxEdge = true;
        foreach (System.Text.RegularExpressions.Match em in
            System.Text.RegularExpressions.Regex.Matches(uaBody,
                @"(?s)<(?<tag>p|h[1-6])\b[^>]*>(?<in>.*?)</\k<tag>>|<br\b[^>]*>|(?<bare>[^<]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var isHead = false;
            string inner;
            if (em.Groups["tag"].Success)
            {
                isHead = em.Groups["tag"].Value.StartsWith("h",
                    StringComparison.OrdinalIgnoreCase);
                inner = em.Groups["in"].Value;
            }
            else
            {
                inner = em.Groups["bare"].Value;
                if (inner.Trim().Length == 0) continue;
            }
            // inline runs: span colours and strong/b bold, inherited
            var uaRuns = new List<(string T, Color? C, bool Bold, bool Styled, bool Lead, bool Trail)>();
            var uaStack = new Stack<(Color?, bool)>();
            Color? uaC = null;
            var uaStyled = false;
            var uaBold = isHead ? 1 : 0;
            var rp = 0;
            // edge whitespace decides word seams between adjacent runs;
            // StripHtmlTags trims it, so read it off the raw slice
            var uaForceLead = false;
            void EmitRun(string raw)
            {
                if (raw.Length == 0) return;
                var lead = uaForceLead || char.IsWhiteSpace(raw[0]);
                var t = System.Text.RegularExpressions.Regex.Replace(
                    HtmlFragment.StripHtmlTags(raw), @"\s+", " ").Trim();
                if (t.Length == 0) { uaForceLead = true; return; }
                uaRuns.Add((t, uaC, uaBold > 0, uaStyled, lead,
                    char.IsWhiteSpace(raw[^1])));
                uaForceLead = false;
            }
            foreach (System.Text.RegularExpressions.Match tg in
                System.Text.RegularExpressions.Regex.Matches(inner, @"<[^>]*>"))
            {
                EmitRun(inner[rp..tg.Index]);
                rp = tg.Index + tg.Length;
                var tag2 = tg.Value;
                if (System.Text.RegularExpressions.Regex.IsMatch(tag2, @"^<\s*/\s*span",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                { if (uaStack.Count > 0) (uaC, uaStyled) = uaStack.Pop(); }
                else if (System.Text.RegularExpressions.Regex.IsMatch(tag2, @"^<\s*span",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    uaStack.Push((uaC, uaStyled));
                    var st = System.Text.RegularExpressions.Regex.Match(tag2,
                        @"style\s*=\s*(['""])(?<s>[^'""]*)\1",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (st.Success)
                    {
                        var cm2 = System.Text.RegularExpressions.Regex.Match(
                            st.Groups["s"].Value, @"(?<![-\w])color\s*:\s*([^;]+)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (cm2.Success && Converters.HtmlToPdfConverter
                                .ParseCssColor(cm2.Groups[1].Value.Trim()) is { } cc2)
                        { uaC = cc2; uaStyled = true; }
                    }
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(tag2,
                    @"^<\s*(strong|b)[\s>]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) uaBold++;
                else if (System.Text.RegularExpressions.Regex.IsMatch(tag2,
                    @"^<\s*/\s*(strong|b)[\s>]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)) uaBold--;
            }
            EmitRun(inner[rp..]);
            if (uaRuns.Count == 0)
            {
                // a <p> holding only <br> keeps one blank line box;
                // a truly empty <p> takes nothing
                if (System.Text.RegularExpressions.Regex.IsMatch(inner, @"<br\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    flow.AdvanceY(UaSerifPitchPt);
                    uaAtBoxEdge = true;
                    uaAfterHead = false;
                }
                continue;
            }
            var uaFs = isHead ? UaSerifH2Pt : UaSerifPt;
            // wrap the run stream at the UA text width
            var uaLines = new List<List<(double X, string T, Color? C, bool Bold)>>();
            var uaCur = new List<(double, string, Color?, bool)>();
            var uaLineStyled = false;
            double uaX = 0;
            var uaPrevOpen = false;   // previous run ended mid-word
            for (var ri = 0; ri < uaRuns.Count; ri++)
            {
                var (rt, rc, rb, rstyled, rlead, rtrail) = uaRuns[ri];
                var runWords = rt.Split(' ');
                for (var wi = 0; wi < runWords.Length; wi++)
                {
                    var word = runWords[wi];
                    if (word.Length == 0) continue;
                    var w2 = UaMeasure(word, rb, uaFs);
                    // a run starting without whitespace continues the
                    // previous run's word ("opmaak" + "." = "opmaak.")
                    var glue = wi == 0 && uaPrevOpen && !rlead;
                    if (glue && uaCur.Count > 0)
                        uaX -= UaMeasure(" ", rb, uaFs);
                    if (!glue && uaCur.Count > 0 && uaX + w2 > uaWrapPt)
                    {
                        uaLines.Add(uaCur);
                        uaCur = new List<(double, string, Color?, bool)>();
                        uaX = 0;
                    }
                    uaCur.Add((uaX, word, rc, rb));
                    uaX += w2 + UaMeasure(" ", rb, uaFs);
                    if (rstyled) uaLineStyled = true;
                }
                uaPrevOpen = !rtrail;
            }
            if (uaCur.Count > 0) uaLines.Add(uaCur);
            var firstOfElement = true;
            foreach (var line2 in uaLines)
            {
                var drop = uaAtBoxEdge ? UaSerifSeatPt
                    : isHead && firstOfElement ? UaSerifH2BeforePt
                    : uaAfterHead ? UaSerifH2AfterPt
                    : firstOfElement && uaLineStyled ? UaSerifMixedPitchPt
                    : UaSerifPitchPt;
                flow.AdvanceY(drop);
                var baseY = flow.CurrentY;
                foreach (var (lx, lt, lc, lb) in line2)
                {
                    if (lc is { } lcc)
                        uaB.SetFillColor(lcc.R / 255.0, lcc.G / 255.0, lcc.B / 255.0);
                    uaB.BeginText().SetFont(lb ? uaTimesB : uaTimes, uaFs)
                       .MoveTextPosition(marginLeft + lx, baseY)
                       .ShowText(lt).EndText();
                    if (lc is not null) uaB.SetFillColor(0, 0, 0);
                }
                uaAfterHead = isHead;
                uaAtBoxEdge = false;
                firstOfElement = false;
            }
        }
        uaB.RestoreState();
        flow.InjectContentAtCursor(uaB.Build());
        // close the last line box so following content (a table)
        // starts at the box bottom, not the baseline
        if (!uaAtBoxEdge) flow.AdvanceY(UaSerifPitchPt - UaSerifSeatPt);
    }

    private void RenderHtmlBlocks(string chunk, HtmlFragment html, FlowLayout flow, Page page,
        Text.TextBuilder tb, Color? htmlColor, List<byte[]> inlineSvgs,
        ref bool htmlFragmentLinkEmitted, double htmlFrameIndent,
        double marginLeft, double marginRight, double marginTop)
    {
        // A FontSize the caller set on the fragment is the HTML body size:
        // it seeds the parser's root style, so unsized blocks inherit it
        // while explicit heading/inline sizes still win.
        var bodyFs = html.TextState is { FontSizeTouched: true } bts && bts.FontSize > 0
            ? (double)bts.FontSize : 0;
        // …and when the caller set none, the document's OWN `body { }` rule
        // is the base type — a fragment that ships a stylesheet sets in the
        // size and face it declares, not the 11 pt Standard-14 default.
        // The caller's TextState still wins; this only fills the gap.
        var bodyCss = Converters.HtmlToPdfConverter.BodyCssFont(chunk);
        if (bodyFs <= 0 && bodyCss.SizePt > 0) bodyFs = bodyCss.SizePt;
        var bodyCssFace = html.TextState?.Font is null
            && string.IsNullOrEmpty(html.TextState?.FontName)
            && bodyCss.Face is { Length: > 0 } bcf
            ? SafeFindFont(bcf) : null;
        if (bodyCssFace?.SourceFontData?.TtfData is not { Length: > 0 }) bodyCssFace = null;
        // Inline <strong>/<u> runs are tracked as RANGES here: a
        // paragraph that emphasises only some of its words draws
        // those words bold/underlined instead of promoting the
        // whole block's face.
        var blocks = Converters.HtmlToPdfConverter.ParseHtmlBlocks(
            chunk, bodyFs, inlineEmphasisRuns: true);
        // The body's own background paints the printed content box, on every page
        // the fragment runs over — a browser paints the body box under everything.
        var bodyBgStartSlot = flow.CurrentSlot;
        // The blocks are authored CSS boxes, so they page-break the way a browser
        // prints them: a block leaves no fewer than the CSS default two of its own
        // lines behind, which is why a two-line paragraph moves whole.
        var savedMinLines = flow.MinLinesPerPage;
        flow.MinLinesPerPage = 2;
        // Legacy-font dialect (summernote / Word-paste HTML): every text run
        // is wrapped in <font face size> with a resolvable embedded face and
        // an explicit colour. It renders faithfully — embedded
        // face at the <font size> point size, CSS colour, on a 1.25×em line
        // grid — instead of the Standard-14 legacy flow. Gated tightly so no
        // other page-level HtmlFragment changes.
        Text.Font? legacyFace = null;
        var legacyDialect = false;
        foreach (var b in blocks)
            if (b.LegacyFontSized && b.FontFamily is { Length: > 0 } fam0)
            {
                var f0 = SafeFindFont(fam0);
                if (f0?.SourceFontData?.TtfData is { Length: > 0 })
                { legacyFace = f0; legacyDialect = true; break; }
            }
        foreach (var b in blocks)
        {
            // Page-level emphasis title (e.g. <p style="font-family:X"><b><i>):
            // the named face draws in its bold-italic variant at
            // the browser <p> default size, on the font's natural line height.
            // Gated on combined bold+italic + a resolvable styled face so ordinary
            // page HTML keeps the Standard-14 flow.
            Text.Font? styledFace = null;
            if (!legacyDialect && b.EmBold && b.EmItalic && b.FontFamily is { Length: > 0 } sf)
            {
                var stl = Text.FontStyles.Bold | Text.FontStyles.Italic;
                var cand = SafeFindFontStyled(sf, stl);
                if (cand?.SourceFontData?.TtfData is { Length: > 0 }) styledFace = cand;
            }
            // Emphasis title uses the browser <p> default 12pt (the body
            // default is 11; the styled path uses 12).
            var fontSize = legacyDialect && b.LegacyFontPt > 0 ? b.LegacyFontPt
                : styledFace is not null ? (b.FontSize > 11.0 ? b.FontSize : 12.0)
                : b.FontSize > 0 ? b.FontSize : 11.0;
            // Faithful line grid for the dialect: pitch = 1.25×em.
            var legacyLead = legacyDialect ? fontSize * 0.25 : 0.0;
            // List items carry a top margin (the common
            // `li { margin: .5em 0 }` rule) so the vertical rhythm
            // tracks a browser/CSS layout rather than packing tight.
            var topMargin = b.MarginTop + (b.IsListItem ? fontSize * 0.5 : 0);
            if (topMargin > 0) flow.AdvanceY(topMargin);

            if (b.IsImage
                && !(b.ImageSrc?.StartsWith("inline-svg:", StringComparison.Ordinal) ?? false)
                && (b.ImageSrc is null || LoadHtmlImageBytes(b.ImageSrc) is null))
            {
                // A broken/missing <img> still occupies the CSS default
                // replaced-element box (300x150 px, width capped by the
                // stylesheet), so following content flows below it — reserve
                // that box inline at the image's document position.
                var imgH = (b.ImageHeight > 0 ? b.ImageHeight : 150.0) * 0.75;
                if (flow.CurrentY - imgH < flow.BottomMargin) flow.ForceNewPage();
                flow.AdvanceY(imgH);
                continue;
            }
            if (b.IsCheckbox)
            {
                // <input type="checkbox"> inside an in-page HtmlFragment:
                // reserve a small AcroForm CheckboxField at the flow cursor,
                // queued with the current overflow slot so it binds to the page
                // it actually flows onto (registered on Form by FinaliseFormFields).
                flow.QueueCheckbox(10.0, b.LeftIndent, b.Checked);
                continue;
            }
            if (b.IsInputField)
            {
                // <input>/<textarea> inside an in-page HtmlFragment: place an
                // interactive AcroForm TextBoxField at the flow cursor, named
                // from the HTML name/id so callers can find it by FullName.
                var ifPage = flow.CurrentPage;
                var ifLlx = marginLeft + b.LeftIndent;
                var ifContentW = ifPage.Width - marginLeft - marginRight - b.LeftIndent;
                var ifW = b.InputWidth > 0 ? System.Math.Min(b.InputWidth, ifContentW) : ifContentW;
                var ifH = b.InputHeight > 0 ? b.InputHeight : fontSize * 1.3;
                var ifTop = flow.CurrentY;
                var ifField = new Aspose.Pdf.Forms.TextBoxField(ifPage,
                    new Aspose.Pdf.Rectangle(ifLlx, ifTop - ifH, ifLlx + ifW, ifTop))
                {
                    Multiline = b.InputMultiline,
                    ReadOnly = b.InputReadOnly,
                };
                if (!string.IsNullOrEmpty(b.InputName)) ifField.PartialName = b.InputName;
                if (!string.IsNullOrEmpty(b.InputValue)) ifField.Value = b.InputValue;
                Form.Add(ifField, ifPage.Number);
                flow.AdvanceY(ifH + b.MarginBottom);
                continue;
            }
            if (b.IsHorizontalRule)
            {
                // Draw the <hr> as a thin filled bar across the
                // content width in its CSS border colour.
                var hrPage = flow.CurrentPage;
                var lineW = hrPage.Width - marginLeft - marginRight;
                var th = b.RuleWidth > 0 ? b.RuleWidth : 1.0;
                var hrY = flow.CurrentY;
                var csb = new Content.ContentStreamBuilder();
                csb.SaveState();
                csb.SetFillColor(b.RuleColor ?? Color.FromArgb(128, 128, 128));
                csb.Rectangle(marginLeft, hrY - th, lineW, th);
                csb.Fill();
                csb.RestoreState();
                hrPage.AddContentStream(csb.Build());
                flow.AdvanceY(th + 2);
                continue;
            }
            if (string.IsNullOrEmpty(b.Text))
            {
                // Dialect blank line (<p><br></p>) occupies a full 1.25×em grid
                // row; a caller-set line spacing steps blank rows on the same
                // pitch as text rows.
                var blankLs = (double)(html.TextState?.LineSpacing ?? 0f);
                flow.AdvanceLineBox(b.ExplicitHeight > 0 ? b.ExplicitHeight
                    : blankLs > 0 ? fontSize + blankLs
                    : legacyDialect ? fontSize + legacyLead : fontSize);
                continue;
            }
            var bf = new Text.TextFragment(b.Text);
            bf.TextState.FontSize = (float)fontSize;
            // HTML renders text on roughly a 1.2x line pitch; the legacy-font
            // dialect uses a 1.25×em grid. A LineSpacing the CALLER set on the
            // fragment overrides that pitch, and it carries the same meaning it
            // does on every other TextState: POINTS of extra leading over the
            // font size, so LineSpacing 1.5 at 12 pt steps 13.5 pt per line.
            var htmlCallerLs = (double)(html.TextState?.LineSpacing ?? 0f);
            var htmlBlockLead = htmlCallerLs > 0
                ? htmlCallerLs
                : legacyDialect ? legacyLead : fontSize * 0.2;
            bf.TextState.LineSpacing = (float)htmlBlockLead;
            bf.TextState.IsBold = b.FontRes == "F2";
            bf.TextState.IsItalic = b.FontRes == "F3";
            // Emphasis title: draw with the embedded bold-italic face on the
            // CSS "normal" line height (pixel-quantized win-metric
            // leading), overriding the Standard-14 bold/italic flags.
            if (styledFace is not null)
            {
                bf.TextState.Font = styledFace;
                bf.TextState.IsBold = false;
                bf.TextState.IsItalic = false;
                var pitch = HtmlNormalLineHeightPt(styledFace.SourceFontData?.TtfData, fontSize);
                bf.TextState.LineSpacing = (float)(pitch > 0 ? pitch - fontSize : fontSize * 0.2);
            }
            // A face the CALLER set on the fragment IS the fragment's body font:
            // an unstyled block draws in it (and its ascent/descent then size the
            // link boxes over the block's anchors). The block's own declared face
            // still wins, as does the legacy dialect's embedded one.
            // …and with no face on the fragment, the page's (then the document's)
            // DefaultTextState face is the body font the HTML inherits.
            var callerBodyFace = html.TextState?.Font ?? DefaultTextStateFace(page);
            if (callerBodyFace is not null && styledFace is null && !legacyDialect)
                bf.TextState.Font = callerBodyFace;
            // The document's own body face draws the block, on the CSS
            // `line-height: normal` box that face's own metrics define
            // (pixel-quantized, so the pitch steps in 0.75 pt).
            var cssLineBox = false;
            if (bodyCssFace is not null && styledFace is null && !legacyDialect)
            {
                bf.TextState.Font = bodyCssFace;
                var bodyPitch = HtmlNormalLineHeightPt(
                    bodyCssFace.SourceFontData?.TtfData, fontSize);
                if (bodyPitch > 0)
                {
                    bf.TextState.LineSpacing = (float)(bodyPitch - fontSize);
                    // That pitch IS the CSS `line-height: normal` box, so the first
                    // baseline seats inside the box — half its leading below the box
                    // top, plus the face's ascent — not on the legacy first-line drop.
                    cssLineBox = true;
                }
            }
            // Dialect: draw with the embedded face and the run's CSS colour.
            if (legacyDialect)
            {
                var face = b.FontFamily is { Length: > 0 } fam1 ? SafeFindFont(fam1) : null;
                if (face?.SourceFontData?.TtfData is { Length: > 0 }) bf.TextState.Font = face;
                else if (legacyFace is not null) bf.TextState.Font = legacyFace;
                if (b.ForeColor is { } fc) bf.TextState.ForegroundColor = fc;
            }
            // A CSS `color` on the block draws its text — on a painted band the
            // declared ink is the only thing that makes the band's text legible.
            if (!legacyDialect && b.ForeColor is { } blockFore)
                bf.TextState.ForegroundColor = blockFore;
            if (htmlColor is not null) bf.TextState.ForegroundColor = htmlColor;
            // Split the block into segments so inline <a href> ranges carry a
            // WebHyperlink — the layout engine turns hyperlinked segments into
            // Link annotations over their rendered run.
            if (b.Anchors is { Count: > 0 })
                ApplyHtmlAnchorSegments(bf, b.Text, b.Anchors);
            // A Hyperlink set on the HtmlFragment ITSELF covers the fragment:
            // ONE Link annotation goes over the rendered
            // block (the first when the HTML splits into several).
            if (html.Hyperlink is not null && !htmlFragmentLinkEmitted)
            {
                bf.Hyperlink = html.Hyperlink;
                htmlFragmentLinkEmitted = true;
            }
            // The block pitch above is layout-synthesised, not a caller
            // request — keep the legacy first-line drop. A pitch the CALLER
            // declared is a CSS line box instead, and seats its first baseline
            // on the box's own half-leading plus ascent.
            bf.TextState.LineSpacingSynthetic = true;
            bf.TextState.LineBoxSeat = htmlCallerLs > 0;
            bf.TextState.CssLineBoxSeat = cssLineBox && htmlCallerLs <= 0;
            // A block that declares a background paints a band across the content
            // width: its own line boxes plus the box's padding above and below, with
            // the text inset by the padding on the left. The box announces its chrome
            // and first line box before it opens, so it never starts on a page that
            // cannot hold them — a browser moves such a box whole.
            var bandColor = b.BackgroundColor;
            var bandStartSlot = 0;
            var bandTop = 0.0;
            if (bandColor is not null)
            {
                flow.EnsureRoomFor(b.BgPadTopPt + bf.TextState.FontSize
                    + bf.TextState.LineSpacing + b.BgPadBottomPt);
                bandStartSlot = flow.CurrentSlot;
                bandTop = flow.CurrentY;
                if (b.BgPadTopPt > 0) flow.AdvanceY(b.BgPadTopPt);
            }
            flow.LeftIndent = b.LeftIndent + htmlFrameIndent
                + (bandColor is not null ? b.BgPadLeftPt : 0);
            // A block whose <strong>/<u> runs cover only part of it
            // sets those runs in their own style; the base face of
            // the line stays regular. A block emphasised throughout
            // keeps the whole-block promotion above.
            var emphRuns = HtmlEmphasisRuns(b);
            bool wrote;
            if (emphRuns is not null)
            {
                bf.TextState.IsBold = false;
                wrote = flow.WriteEmphasisRuns(bf, emphRuns);
            }
            else wrote = flow.WriteTextFragment(bf);
            flow.LeftIndent = 0;
            if (!wrote)
            {
                bf.Position = new Text.Position(marginLeft + b.LeftIndent,
                    page.Height - marginTop - bf.TextState.FontSize);
                tb.AppendTextInline(bf);
            }
            if (bandColor is not null)
            {
                if (b.BgPadBottomPt > 0) flow.AdvanceY(b.BgPadBottomPt);
                flow.QueueBandFill(bandStartSlot, bandTop, flow.CurrentY, bandColor);
            }
            if (b.MarginBottom > 0) flow.AdvanceY(b.MarginBottom);
        }
        flow.MinLinesPerPage = savedMinLines;
        if (bodyCss.BgColor is { } bodyBg) flow.QueueBodyBackground(bodyBgStartSlot, bodyBg);
        flow.FlushBackgroundFills();
        // Draw this chunk's <img> elements in-flow (per segment), so a
        // logo lands at its position rather than after all content.
        RenderHtmlImages(chunk, flow, marginLeft, marginRight, inlineSvgs);
    }
}
