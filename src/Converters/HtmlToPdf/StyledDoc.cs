using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Collapse ASCII whitespace runs to a single space and trim leading/
    /// trailing whitespace — identical output to <c>Regex.Replace(raw,"[ \t\r\n\f]+"," ").
    /// Trim(...)</c> (U+00A0 is preserved as content) — while recording, for each output
    /// character, the raw index it originated from. The map lets inline anchor ranges
    /// (tracked in raw coordinates) be re-expressed against the collapsed text.</summary>
    private static (string text, System.Collections.Generic.List<int> rawOf) CollapseWhitespaceWithMap(string raw)
    {
        static bool IsWs(char c) => c is ' ' or '\t' or '\r' or '\n' or '\f';
        var sb = new StringBuilder(raw.Length);
        var rawOf = new System.Collections.Generic.List<int>(raw.Length);
        int i = 0, n = raw.Length;
        while (i < n && IsWs(raw[i])) i++;                 // drop leading whitespace
        while (i < n)
        {
            if (IsWs(raw[i]))
            {
                int runStart = i;
                while (i < n && IsWs(raw[i])) i++;
                if (i < n) { sb.Append(' '); rawOf.Add(runStart); }   // single space between words; trailing run dropped
            }
            else { sb.Append(raw[i]); rawOf.Add(i); i++; }
        }
        return (sb.ToString(), rawOf);
    }

    private sealed class BlockStyle
    {
        public double FontSize;
        // font-variant: small-caps seen on a span of this block (redline dialect):
        // lowercase draws as uppercase at the small-caps ratio.
        public bool SmallCaps;
        public double TextIndentPt;
        public double LetterSpacingPt;
        // The paragraph's own pt right margin: its wrap box ends this far
        // inside the content edge (pt-styled fragment dialect only).
        public double RightInsetPt;
        // An explicit font-size:0 (the "clear:both;height:0;font-size:0" float
        // terminator idiom): a whitespace-only block at size 0 occupies NO line.
        public bool ZeroFontSize;
        public string FontRes = "F1";
        public string? FontFamily;
        // Foreground text color from an inline color: declaration or a legacy <font color>.
        // Null = default black.
        public Color? ForeColor;
        // Point size from a legacy <font size="N"> attribute (0 = none). Kept separate from
        // FontSize so it is inert for the legacy flow and read only by the gated dialect path.
        public double LegacyFontPt;
        // Set when this block's size came from a legacy <font size="N"> attribute — the
        // marker for the legacy-font dialect (summernote / Word-paste HTML).
        public bool LegacyFontSized;
        // Inline emphasis seen anywhere in the block (both true = bold-italic). Read only
        // by the embedded-face page-level path; the legacy flow keeps using FontRes.
        public bool EmBold;
        public bool EmItalic;
        public double MarginTop;
        public double MarginBottom;
        // Apply MarginTop even at the top of a page (the filing dialect's repeated
        // page-header block keeps its CSS top margin below the page margin).
        public bool MarginTopAlways;
        public double LeftIndent;
        // Sum of the width-BILLING container chrome (padding + borders of width:auto
        // ancestors) on this style's chain — containerBoxIndents mode. A width:100%
        // ancestor's chrome indents but overflows its parent, so it does not bill
        // the page-widen; a width:auto ancestor's chrome does both.
        public double BillPadPt;
        // A box-shadow'd container (the widget CARD) on this style's chain: the
        // shadow colour, and the card's own left chrome (padding + border) so the
        // draw can recover the card box from the content position.
        public Color? CardShadowColor;
        public double CardChromePt;
        public bool IsListItem;
        public bool PageBreakBefore; // CSS page-break-before:always on this element
        public bool PageBreakAfter;  // CSS page-break-after:always — break at the close
        // Unitless CSS line-height factor from a class rule (coverStyles mode);
        // 0 = the flow's own default pitch.
        public double LineFactor;
        // True when LineFactor came from a PARSED unitless line-height
        // declaration (inline style or stylesheet rule) — the CSS-box flow
        // seat/margins apply only then, never to a dialect-assigned factor.
        public bool DeclaredLineFactor;
        // style="width:N%" on an enclosing div (browser-UA flow only): the block's
        // wrap box narrows to that fraction of the content width — the source
        // renderer stacks such divs but still wraps their text at the declared width.
        public double WidthFrac;
        // Absolute width (style="width:680" / "width:680px") on an enclosing div —
        // recorded always, honored as the wrap box only by the form-document dialect.
        public double WidthPx;
        // style="padding-top:Npx" on the enclosing div (browser-UA flow only):
        // non-collapsing vertical space above the block.
        public double PadTop;
        // The element's OWN padding-top longhand (any flow) — spent only by the
        // childless-empty close spacer, never carried onto text blocks (a content
        // block's padding stays with the dialect's own PadTop rules above).
        public double OwnPadTopPt;
        // blocks.Count when this element opened; -1 until a block-tag open sets it.
        // At close, equality means the element's whole subtree emitted nothing.
        public int BlocksAtOpen = -1;
        // text-align:right (honored by the print-grid dialect only).
        public bool AlignRight;
        // Print-grid heading band (a ".cls h4" rule's border-bottom).
        public Color? BandColor;
        public double BandPx;
        public double BandPadPx;
        // List context carried on an <ol>/<ul> style so its <li> children can be
        // numbered/bulleted. ListKind: 0 = not a list, 1 = ordered, 2 = unordered.
        // ListCounter holds the last-used ordinal (incremented per <li>); the first
        // <li> renders ListCounter+1, so `start="5"`/`counter-set: item 4` sets it to 4.
        public int ListKind;
        public int ListCounter;
        // CSS list-style-type carried on the <ol> (inline style or attribute):
        // "" = decimal; otherwise upper-alpha / lower-alpha / upper-roman /
        // lower-roman markers, formatted per item from ListCounter.
        public string ListStyleType = "";
        // Styled-article panel list (`.td-toc`): the block-link's padding-bottom,
        // carried on the list style so each item pitches one line box + this pad.
        public double TocLinkPadPt;
        // Styled-article dialect marker, inherited down the style stack so the
        // declaration applier can honour the box-model cases the calibrated
        // dialects never see (e.g. negative gutter margins).
        public bool ArticleRhythm;
        // CSS `li:nth-child(An+B)::before { content: … }` generated markers active for this
        // list (matched to the <ol>/<ul>'s class when it opens); ChildIndex counts the list's
        // children so each <li> can pick the matching rule. Null = no ::before markers → the
        // numeric/bullet default applies.
        public List<BeforeMarker>? BeforeRules;
        public int ChildIndex;
        // Explicit CSS height / min-height in points. When >0 the block's
        // own rendered area must be at least this tall, so empty-body
        // styled divs (common in CMS template HTML) still contribute
        // vertical space to pagination.
        public double ExplicitHeight;
        // CSS box decoration (background-color / border) carried to the emitted Block.
        public Color? BackgroundColor;
        // Pinned-body report band pad (see Block.BandPadPt).
        public double BandPadPt;
        // The CSS `padding` of a block that paints a background (see
        // Block.BgPadTopPt): the fill covers the line boxes plus this much above
        // and below, and the text starts BgPadLeftPt inside the content edge.
        public double BgPadTopPt;
        public double BgPadBottomPt;
        public double BgPadLeftPt;
        public Color? BorderColor;
        public double BorderWidth;
        // Only border-top declared (the `border:none; border-top: solid …` divider).
        public bool BorderTopOnly;
        // border-radius corner rounding (first shorthand value), px→pt.
        public double BorderRadiusPt;
        // UA-serif flow inline-span typography: a px line-height fixes the LINE
        // BOX; the span's own margin-left insets its text within the element box.
        public double LineBoxPt;
        public double TextInsetPt;
        // Declared height/min-height as a FLOOR (see Block.HeightFloorStart): the
        // element's content grows down into it and only what FOLLOWS moves.
        public double HeightFloorPt;
        // True between this element's open and close: its own ExplicitHeight is
        // being spent by the floor markers, so an inner flush must not also emit
        // it as a spacer ahead of the content.
        public bool HeightFloorDeferred;
        // UA-serif flow marker: negative inline margins are real here (the
        // calibrated dialects never met one).
        public bool UaSerif;
        // margin-top came from an AUTHORED declaration (inline/stylesheet),
        // not a UA element default - it MAX-collapses with the body margin.
        public bool MarginTopAuthored;
        // Painted-box dimensions (a tiny repeated background tile over an
        // explicitly sized element): the fill spans this declared box rather
        // than each text line. Zero = no painted box.
        public double BgBoxWidthPt;
        public double BgBoxHeightPt;
        // Form-report dialect (control-group + label documents): opts style parsing
        // into the CSS the expected render honours there — the `margin:` shorthand,
        // padding-bottom, and font-weight:normal undoing a heading's default bold.
        // Off everywhere else so calibrated conversions keep their spacing.
        public bool FormDialect;
        // The enclosing element's resolved font size — the base an em font-size
        // resolves against (1.75em on a 12pt body = 21pt, regardless of the tag's
        // legacy default size). Form dialect only.
        public double ParentFontSize;
        // text-align:center from a class rule — honored by the metric flow only.
        public bool AlignCenter;
        // A CSS text-align:center from anywhere (inline style included) — honored by
        // the sectioned-report flow.
        public bool AlignCenterCss;
        // float:left on this element or one enclosing it — an image inside such a box
        // is taken out of the flow and the text beside it wraps in the space left over.
        public bool FloatLeft;
        // float:right — the UA flow lays such an element as a shrink-to-fit box
        // against the right content edge, sharing its line with adjacent floats.
        public bool FloatRight;

        /// <summary>Left inset from a `margin:` SHORTHAND's fourth value, recorded for
        /// every document but read only by the float flow - the calibrated dialects take
        /// their horizontal margins from the dedicated margin-left handling.</summary>
        public double ShorthandLeftPt;

        /// <summary>The `margin:` shorthand's TOP value, recorded for every document and
        /// read only by the float flow.</summary>
        public double ShorthandTopPt;

        /// <summary>The whole declared `font-family` list, recorded for every document but
        /// read only by the float flow: CSS falls through the stack to the first family
        /// that is actually installed, where FontFamily keeps the first NAMED one.</summary>
        public string? FontFamilyStack;

        /// <summary>A `margin-right` longhand, recorded for every document but read only
        /// by the float flow - the calibrated dialects wrap on their own measured text
        /// columns, where honouring the declaration everywhere would re-break them.</summary>
        public double MarginRightPt;

        /// <summary>A declared `width: Npx`, in points. Recorded for every document and
        /// read only by the float flow, where a block keeps the box it declares even when
        /// that box overflows the content frame.</summary>
        public double DeclaredWidthPt;
        // ALIGN="justify" / text-align:justify — flow lines stretch word gaps to the
        // content box (except a paragraph's last line).
        public bool AlignJustify;
        // This <ul>/<ol> opened INSIDE another block element. A body-level list's
        // top margin vanishes at the document top, but a nested list's survives
        // like an authored margin (max-collapsed with the UA body margin).
        public bool ListNestedInBlock;
        // The legacy ALIGN="center" ATTRIBUTE (not CSS classes, which stay
        // metric-flow-only): centre each measured line in the content box.
        public bool AlignCenterAttr;
    }

    // Initial <ol> counter — the first <li> renders ParseListStart+1. Honours the
    // `start` attribute (start-1) and CSS `counter-set`/`counter-reset: <name> N` (N),
    // the latter used by rich-text editors (EditorJS) to resume numbering.
    private static readonly Regex CounterSetRx = new(
        @"counter-(?:set|reset)\s*:\s*[A-Za-z_][\w-]*\s+(-?\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static int ParseListStart(Dictionary<string, string>? attrs)
    {
        if (attrs is null) return 0;
        if (attrs.TryGetValue("start", out var s) && int.TryParse(s.Trim(), out var st))
            return st - 1;
        if (attrs.TryGetValue("style", out var css) && !string.IsNullOrEmpty(css))
        {
            var m = CounterSetRx.Match(css);
            if (m.Success && int.TryParse(m.Groups[1].Value, out var cv)) return cv;
        }
        return 0;
    }

    /// <summary>Format an ordered-list ordinal per its CSS list-style-type:
    /// alphabetic (A, B, … Z, AA, …) or roman (i, ii, iii, …) markers; anything
    /// else keeps the decimal default.</summary>
    private static string FormatListOrdinal(int n, string listStyleType)
    {
        if (n <= 0 || listStyleType.Length == 0)
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        switch (listStyleType)
        {
            case "upper-alpha":
            case "lower-alpha":
            {
                var s = "";
                var v = n;
                while (v > 0)
                {
                    v--;
                    s = (char)('A' + v % 26) + s;
                    v /= 26;
                }
                return listStyleType[0] == 'l' ? s.ToLowerInvariant() : s;
            }
            case "upper-roman":
            case "lower-roman":
            {
                var vals = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
                var syms = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
                var sb = new StringBuilder();
                var v = n;
                for (var i = 0; i < vals.Length && v > 0; i++)
                    while (v >= vals[i]) { sb.Append(syms[i]); v -= vals[i]; }
                var r = sb.ToString();
                return listStyleType[0] == 'l' ? r.ToLowerInvariant() : r;
            }
            default:
                return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ---- Step-list dialect (single <ul> whose items carry heading blocks) ----------
    // A page-level HtmlFragment whose whole body is one <ul> where a list item nests
    // block children (h1-h3/p, optionally after a leading inline "head" such as a
    // numbering <span>) lays out through the browser-metric HTML engine: serif faces,
    // pixel-quantized CSS line boxes, UA block margins and a real bullet marker.
    // The parser recognises exactly that shape; anything else returns false so the
    // legacy flat flow keeps handling it.

    /// <summary>One inline run of a step-list block: text in regular or bold serif.</summary>
    internal sealed class StepRun
    {
        public string Text = "";
        public bool Bold;
    }

    /// <summary>One block of a step-list item: the leading inline line ("head") or a
    /// h1/h2/h3/p child. Headings render bold regardless of inline markup.</summary>
    internal sealed class StepBlock
    {
        public string Tag = "head";
        public List<StepRun> Runs = new();
    }

    /// <summary>One <li> of the recognised list: CSS padding-left plus its block sequence.</summary>
    internal sealed class StepListItem
    {
        public double PadLeftPt;
        public List<StepBlock> Blocks = new();
    }

    private static readonly Regex StepListShellRegex = new(
        @"^\s*(?:<!doctype[^>]*>\s*)?(?:<html[^>]*>\s*)?(?:<head[^>]*>[\s\S]*?</head>\s*)?(?:<body[^>]*>\s*)?" +
        @"<ul\b[^>]*>(?<inner>[\s\S]*)</ul>\s*(?:</body>\s*)?(?:</html>\s*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StepLiRegex = new(
        @"<li\b(?<attrs>[^>]*)>(?<c>[\s\S]*?)</li>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StepTagRegex = new(
        @"^<(?<close>/?)(?<tag>span|strong|b|h1|h2|h3|p)\b[^>]*>$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StepAnyTagRegex = new(@"<[^>]*>", RegexOptions.Compiled);

    private static readonly Regex StepPadLeftRegex = new(
        @"padding-left\s*:\s*(?<v>\d+(?:\.\d+)?)\s*(?<u>em|px|pt)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Try to read <paramref name="html"/> as the step-list dialect. True only when
    /// the document is a single un-nested <ul> of <li> items, every tag inside the items
    /// belongs to the span/strong/b/h1-h3/p family, and at least one item carries a heading
    /// block — the shape the dedicated page-level renderer covers.</summary>
    internal static bool TryParseHtmlStepList(string html, out List<StepListItem> items)
    {
        items = new List<StepListItem>();
        if (string.IsNullOrEmpty(html) ||
            html.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) < 0) return false;
        var shell = StepListShellRegex.Match(html);
        if (!shell.Success) return false;
        var inner = shell.Groups["inner"].Value;
        if (inner.IndexOf("<ul", StringComparison.OrdinalIgnoreCase) >= 0 ||
            inner.IndexOf("<ol", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        var anyHeading = false;
        var covered = 0;
        foreach (Match li in StepLiRegex.Matches(inner))
        {
            // Only whitespace may sit between consecutive <li> elements.
            if (inner.Substring(covered, li.Index - covered).Trim().Length != 0) return false;
            covered = li.Index + li.Length;
            if (!TryParseStepLi(li.Groups["attrs"].Value, li.Groups["c"].Value, out var item, ref anyHeading))
                return false;
            items.Add(item);
        }
        if (covered == 0 || inner.Substring(covered).Trim().Length != 0) return false;
        return items.Count > 0 && anyHeading;
    }

    private static bool TryParseStepLi(string attrs, string content, out StepListItem item, ref bool anyHeading)
    {
        item = new StepListItem();
        var pad = StepPadLeftRegex.Match(attrs);
        if (pad.Success)
        {
            var v = double.Parse(pad.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
            item.PadLeftPt = pad.Groups["u"].Value.ToLowerInvariant() switch
            {
                "em" => v * 12.0,
                "px" => v * 0.75,
                _ => v,
            };
        }
        foreach (Match any in StepAnyTagRegex.Matches(content))
            if (!StepTagRegex.IsMatch(any.Value)) return false;

        var blocks = item.Blocks;
        var cur = new StepBlock();
        string? openBlock = null;
        var boldDepth = 0;
        var afterBlocks = false;
        var invalid = false;

        void AddText(string raw)
        {
            var text = DecodeEntities(raw);
            if (text.Length == 0) return;
            // Inline content is only valid before the first block child (the "head"
            // line) or inside an open block — trailing prose after a block is a
            // shape this dialect has no layout rule for.
            if (openBlock is null && afterBlocks && text.Trim().Length > 0) { invalid = true; return; }
            if (cur.Runs.Count > 0 && cur.Runs[^1].Bold == boldDepth > 0)
                cur.Runs[^1].Text += text;
            else
                cur.Runs.Add(new StepRun { Text = text, Bold = boldDepth > 0 });
        }

        var pos = 0;
        foreach (Match t in StepAnyTagRegex.Matches(content))
        {
            AddText(content.Substring(pos, t.Index - pos));
            pos = t.Index + t.Length;
            var tm = StepTagRegex.Match(t.Value);
            var tag = tm.Groups["tag"].Value.ToLowerInvariant();
            var close = tm.Groups["close"].Value.Length > 0;
            switch (tag)
            {
                case "span":
                    break;
                case "strong":
                case "b":
                    boldDepth = System.Math.Max(0, boldDepth + (close ? -1 : 1));
                    break;
                default:
                    if (!close)
                    {
                        if (openBlock is not null) return false;
                        if (CollapseStepWs(cur)) blocks.Add(cur);
                        cur = new StepBlock { Tag = tag };
                        openBlock = tag;
                    }
                    else
                    {
                        if (openBlock != tag) return false;
                        if (!CollapseStepWs(cur)) return false;
                        blocks.Add(cur);
                        if (tag is "h1" or "h2" or "h3") anyHeading = true;
                        cur = new StepBlock();
                        openBlock = null;
                        afterBlocks = true;
                    }
                    break;
            }
        }
        AddText(content.Substring(pos));
        if (invalid || openBlock is not null) return false;
        if (CollapseStepWs(cur)) blocks.Add(cur);
        return blocks.Count > 0;
    }

    /// <summary>Apply the HTML whitespace-collapse rule to a block's run stream: every
    /// whitespace run becomes one space attributed to the run holding its FIRST whitespace
    /// character (so a space straddling an inline boundary stays with the earlier run,
    /// which decides the fragment splits); leading/trailing block whitespace drops.
    /// Returns false when no visible content remains.</summary>
    private static bool CollapseStepWs(StepBlock b)
    {
        var built = new List<(System.Text.StringBuilder sb, bool bold)>();
        var pendingOwner = -1;   // index in built[] that receives the collapsed space
        var anyContent = false;
        foreach (var r in b.Runs)
        {
            built.Add((new System.Text.StringBuilder(r.Text.Length), r.Bold));
            var idx = built.Count - 1;
            foreach (var ch in r.Text)
            {
                if (ch is ' ' or '\t' or '\r' or '\n' or '\f')
                {
                    if (anyContent && pendingOwner < 0) pendingOwner = idx;
                }
                else
                {
                    if (pendingOwner >= 0) { built[pendingOwner].sb.Append(' '); pendingOwner = -1; }
                    built[idx].sb.Append(ch);
                    anyContent = true;
                }
            }
        }
        b.Runs = new List<StepRun>();
        foreach (var (sb, bold) in built)
        {
            if (sb.Length == 0) continue;
            if (b.Runs.Count > 0 && b.Runs[^1].Bold == bold) b.Runs[^1].Text += sb.ToString();
            else b.Runs.Add(new StepRun { Text = sb.ToString(), Bold = bold });
        }
        return anyContent;
    }

    // ---- Styled-class data-font flow (class-styled reports with @font-face data fonts) ----
    // A document whose stylesheet embeds its faces as data: URIs and styles a flat
    // sequence of classed paragraphs (the EDGAR TSR shareholder-report shape) renders
    // through the styled HTML engine: the embedded faces at the class
    // sizes/colors, CSS letter-spacing as per-glyph TJ segmentation, and the class
    // margin chain. The parser recognises exactly that shape; anything else falls
    // back to the legacy flow.

    /// <summary>One embedded @font-face: raw TTF bytes for a family (+bold/italic variant).</summary>
    private static readonly Regex FontFaceRx = new(
        @"@font-face\s*\{(?<b>[^{}]*)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FontFaceFamilyRx = new(
        @"font-family\s*:\s*[""']?(?<f>[^;""'}]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex FontFaceDataRx = new(
        @"url\(\s*[""']?data:(?:font/ttf|font/truetype|application/(?:x-)?font-ttf|application/octet-stream);base64,(?<d>[A-Za-z0-9+/=]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Collect the document's @font-face data-URI faces keyed
    /// "family", "family|bold", "family|italic" (lower-case).</summary>
    internal static Dictionary<string, byte[]> ParseDataFontFaces(string html)
    {
        var fonts = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in FontFaceRx.Matches(html))
        {
            var body = m.Groups["b"].Value;
            var fam = FontFaceFamilyRx.Match(body);
            var data = FontFaceDataRx.Match(body);
            if (!fam.Success || !data.Success) continue;
            byte[] ttf;
            try { ttf = System.Convert.FromBase64String(data.Groups["d"].Value); }
            catch { continue; }
            if (ttf.Length < 12) continue;
            var key = fam.Groups["f"].Value.Trim().TrimEnd('"', '\'').ToLowerInvariant();
            if (Regex.IsMatch(body, @"font-weight\s*:\s*(bold|[7-9]00)", RegexOptions.IgnoreCase)) key += "|bold";
            else if (Regex.IsMatch(body, @"font-style\s*:\s*italic", RegexOptions.IgnoreCase)) key += "|italic";
            fonts[key] = ttf;
        }
        return fonts;
    }

    /// <summary>One element of the styled-class dialect tree: nested divs over &lt;p&gt;
    /// leaves whose inline runs split at span boundaries. Style holds the cascaded
    /// declarations after <see cref="TryParseStyledDataFontDoc"/> resolves the rules.</summary>
    internal sealed class StyledNode
    {
        public string Tag = "";
        public List<string> Classes = new();
        public Dictionary<string, string> Attrs = new(StringComparer.OrdinalIgnoreCase);
        public List<StyledNode> Children = new();
        public StyledNode? Parent;
        public List<string>? Runs;            // p leaves only
        public Dictionary<string, string> Style = new(StringComparer.OrdinalIgnoreCase);
        public double FontSizePt = 12.0;
        public byte[]? Ttf;
        public string FontKey = "";
    }

    /// <summary>One stylesheet rule kept with its parsed selector for the mini-cascade —
    /// the flat selector map drops the attribute/combinator selectors this dialect's
    /// margins hang on.</summary>
    private sealed class StyledRule
    {
        public List<(char comb, string tag, List<string> classes,
            List<(string a, string v)> attrs, string pseudo)> Parts = new();
        public Dictionary<string, string> Decls = new(StringComparer.OrdinalIgnoreCase);
        public int Specificity;               // (classes+attrs+pseudos)·100 + types
        public int Order;
    }

    private static readonly Regex StyledParaRx = new(
        @"<p\b(?<attrs>[^>]*)>(?<c>[\s\S]*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StyledDivRx = new(
        @"<(?<close>/?)div\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StyledSpanRx = new(
        @"<(?<close>/?)span\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StyledCompoundRx = new(
        @"\G(?<tag>[a-zA-Z][\w-]*|\*)?(?<rest>(?:\.[\w-]+|\[[\w-]+\s*=\s*""[^""]*""\]|::?[\w-]+)*)",
        RegexOptions.Compiled);

    /// <summary>Parse every &lt;style&gt; rule keeping full selectors, with media
    /// filtering: @media print blocks are inlined and
    /// every other @media block (screen, …) is dropped entirely.</summary>
    private static List<StyledRule> ParseStyledRules(string html)
    {
        var rules = new List<StyledRule>();
        var order = 0;
        foreach (Match block in Regex.Matches(html, @"<style[^>]*>([\s\S]*?)</style>", RegexOptions.IgnoreCase))
        {
            var cssText = Regex.Replace(block.Groups[1].Value, @"/\*[\s\S]*?\*/", "");
            var sbCss = new StringBuilder(cssText.Length);
            var i = 0;
            while (i < cssText.Length)
            {
                var m = cssText.IndexOf("@media", i, StringComparison.OrdinalIgnoreCase);
                if (m < 0) { sbCss.Append(cssText, i, cssText.Length - i); break; }
                sbCss.Append(cssText, i, m - i);
                var braceOpen = cssText.IndexOf('{', m);
                if (braceOpen < 0) break;
                var cond = cssText.Substring(m + 6, braceOpen - m - 6);
                var depth = 1;
                var j = braceOpen + 1;
                while (j < cssText.Length && depth > 0)
                {
                    if (cssText[j] == '{') depth++;
                    else if (cssText[j] == '}') depth--;
                    j++;
                }
                if (cond.Contains("print", StringComparison.OrdinalIgnoreCase))
                    sbCss.Append(cssText, braceOpen + 1, Math.Max(0, j - braceOpen - 2));
                i = j;
            }
            foreach (Match rule in Regex.Matches(sbCss.ToString(), @"([^{}]+)\{([^{}]*)\}"))
            {
                var decls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match d in StyleDeclRx.Matches(rule.Groups[2].Value))
                    decls[d.Groups[1].Value.Trim().ToLowerInvariant()] = d.Groups[2].Value.Trim();
                if (decls.Count == 0) continue;
                foreach (var selRaw in rule.Groups[1].Value.Split(','))
                {
                    var r = ParseStyledSelector(selRaw.Trim());
                    if (r is null) continue;
                    r.Decls = decls;
                    r.Order = order++;
                    rules.Add(r);
                }
            }
        }
        return rules;
    }

    /// <summary>Parse one selector of the supported shape — type/.class/[attr="v"]/
    /// :first-child/:last-child compounds joined by descendant or child combinators.
    /// Null = unsupported form (that rule is ignored).</summary>
    private static StyledRule? ParseStyledSelector(string sel)
    {
        if (sel.Length == 0) return null;
        var rule = new StyledRule();
        // Tokenize by whitespace / '>' OUTSIDE brackets — attribute values may
        // contain both (e.g. [section-template-key="TSR - Report_Intro"]).
        var parts = new List<string>();
        {
            var tok = new StringBuilder();
            var depth = 0;
            foreach (var ch in sel)
            {
                if (ch == '[') depth++;
                else if (ch == ']') depth = Math.Max(0, depth - 1);
                if (depth == 0 && (char.IsWhiteSpace(ch) || ch == '>'))
                {
                    if (tok.Length > 0) { parts.Add(tok.ToString()); tok.Clear(); }
                    if (ch == '>') parts.Add(">");
                }
                else tok.Append(ch);
            }
            if (tok.Length > 0) parts.Add(tok.ToString());
        }
        var comb = '\0';
        foreach (var tok in parts)
        {
            if (tok == ">") { comb = '>'; continue; }
            var m = StyledCompoundRx.Match(tok);
            if (!m.Success || m.Length != tok.Length) return null;
            var classes = new List<string>();
            var attrs = new List<(string a, string v)>();
            var pseudo = "";
            foreach (Match p in Regex.Matches(m.Groups["rest"].Value,
                @"\.(?<c>[\w-]+)|\[(?<a>[\w-]+)\s*=\s*""(?<v>[^""]*)""\]|::?(?<p>[\w-]+)"))
            {
                if (p.Groups["c"].Success) classes.Add(p.Groups["c"].Value);
                else if (p.Groups["a"].Success) attrs.Add((p.Groups["a"].Value, p.Groups["v"].Value));
                else
                {
                    pseudo = p.Groups["p"].Value.ToLowerInvariant();
                    if (pseudo is not ("first-child" or "last-child")) return null;
                }
            }
            var tag = m.Groups["tag"].Value.ToLowerInvariant();
            if (tag == "*") tag = "";
            rule.Parts.Add((comb == '\0' ? ' ' : comb, tag, classes, attrs, pseudo));
            rule.Specificity += (classes.Count + attrs.Count + (pseudo.Length > 0 ? 1 : 0)) * 100
                + (tag.Length > 0 ? 1 : 0);
            comb = '\0';
        }
        return rule.Parts.Count > 0 ? rule : null;
    }

    private static bool StyledCompoundMatches(StyledNode el,
        (char comb, string tag, List<string> classes, List<(string a, string v)> attrs, string pseudo) p)
    {
        if (p.tag.Length > 0 && !el.Tag.Equals(p.tag, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in p.classes)
            if (!el.Classes.Contains(c)) return false;
        foreach (var (a, v) in p.attrs)
            if (!el.Attrs.TryGetValue(a, out var av) || av != v) return false;
        if (p.pseudo == "first-child" && (el.Parent is null || el.Parent.Children[0] != el)) return false;
        if (p.pseudo == "last-child" && (el.Parent is null || el.Parent.Children[^1] != el)) return false;
        return true;
    }

    private static bool StyledSelectorMatches(StyledNode el, StyledRule rule)
    {
        if (!StyledCompoundMatches(el, rule.Parts[^1])) return false;
        var cur = el;
        for (var i = rule.Parts.Count - 2; i >= 0; i--)
        {
            var comb = rule.Parts[i + 1].comb;
            cur = cur.Parent;
            if (comb == '>')
            {
                if (cur is null || !StyledCompoundMatches(cur, rule.Parts[i])) return false;
            }
            else
            {
                while (cur is not null && !StyledCompoundMatches(cur, rule.Parts[i])) cur = cur.Parent;
                if (cur is null) return false;
            }
        }
        return true;
    }

    /// <summary>Cascade the matching rules onto one element in (specificity, order)
    /// sequence; the margin shorthand expands to longhands as it applies.</summary>
    private static void CascadeStyledRules(StyledNode el, List<StyledRule> rules)
    {
        foreach (var r in rules.Where(r => StyledSelectorMatches(el, r))
                     .OrderBy(r => r.Specificity).ThenBy(r => r.Order))
            foreach (var kv in r.Decls)
            {
                if (kv.Key.Equals("margin", StringComparison.OrdinalIgnoreCase))
                {
                    var vals = kv.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (vals.Length == 0) continue;
                    var top = vals[0];
                    var right = vals.Length > 1 ? vals[1] : vals[0];
                    var bottom = vals.Length > 2 ? vals[2] : vals[0];
                    var left = vals.Length > 3 ? vals[3] : right;
                    el.Style["margin-top"] = top;
                    el.Style["margin-right"] = right;
                    el.Style["margin-bottom"] = bottom;
                    el.Style["margin-left"] = left;
                }
                else el.Style[kv.Key] = kv.Value;
            }
    }

    private static double StyledLen(string? v, double em = 12.0)
    {
        if (string.IsNullOrEmpty(v)) return 0;
        v = v.Trim();
        if (v.Equals("auto", StringComparison.OrdinalIgnoreCase)) return 0;
        var m = Regex.Match(v, @"^(-?[\d.]+)\s*(pt|px|em)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "px" => n * 0.75,
            "em" => n * em,
            _ => n,
        };
    }

    /// <summary>Try to read the document body as the styled-class dialect: nothing but
    /// nested divs over classed &lt;p&gt; leaves (spans inside them), and EVERY leaf's
    /// cascade resolving to one of the document's @font-face data faces. Builds the
    /// element tree (with cascaded styles) rooted at body.</summary>
    internal static bool TryParseStyledDataFontDoc(string html, out StyledNode bodyNode)
    {
        bodyNode = new StyledNode { Tag = "body" };
        var dataFonts = ParseDataFontFaces(html);
        if (dataFonts.Count == 0) return false;

        var bodyM = Regex.Match(html, @"<body[^>]*>(?<b>[\s\S]*)</body>", RegexOptions.IgnoreCase);
        if (!bodyM.Success) return false;
        var body = bodyM.Groups["b"].Value;

        // Shape check: removing the p elements wholesale and then the div tags must
        // leave only whitespace — anything else is beyond this dialect.
        var residue = StyledParaRx.Replace(body, "");
        residue = StyledDivRx.Replace(residue, "");
        if (residue.Trim().Length != 0) return false;

        // Build the tree from the interleaved div/p token stream.
        var tokens = new List<(int idx, int len, string kind, Match m)>();
        foreach (Match d in StyledDivRx.Matches(body))
            tokens.Add((d.Index, d.Length, d.Groups["close"].Value.Length > 0 ? "/div" : "div", d));
        foreach (Match p in StyledParaRx.Matches(body))
            tokens.Add((p.Index, p.Length, "p", p));
        tokens.Sort((a, b) => a.idx.CompareTo(b.idx));

        var cur = bodyNode;
        var consumedTo = 0;
        foreach (var (idx, len, kind, m) in tokens)
        {
            if (idx < consumedTo) continue;   // tag inside an already-consumed <p>
            consumedTo = idx + len;
            switch (kind)
            {
                case "div":
                {
                    var node = new StyledNode { Tag = "div", Parent = cur };
                    FillStyledAttrs(node, m.Groups["attrs"].Value);
                    cur.Children.Add(node);
                    cur = node;
                    break;
                }
                case "/div":
                    if (cur.Parent is null) return false;
                    cur = cur.Parent;
                    break;
                case "p":
                {
                    var node = new StyledNode { Tag = "p", Parent = cur };
                    FillStyledAttrs(node, m.Groups["attrs"].Value);
                    if (!ParseStyledRuns(m.Groups["c"].Value, node)) return false;
                    cur.Children.Add(node);
                    break;
                }
            }
        }
        if (cur != bodyNode) return false;

        // Cascade the stylesheet onto every element and resolve the leaf faces.
        var rules = ParseStyledRules(html);
        var allResolved = true;
        var anyLeaf = false;
        void Walk(StyledNode n)
        {
            CascadeStyledRules(n, rules);
            if (n.Tag == "p")
            {
                anyLeaf = true;
                n.FontSizePt = n.Style.TryGetValue("font-size", out var fs) ? StyledLen(fs) : 12.0;
                if (n.FontSizePt <= 0) n.FontSizePt = 12.0;
                var fam = n.Style.TryGetValue("font-family", out var famRaw) ? FirstFontFamily(famRaw) : null;
                var bold = n.Style.TryGetValue("font-weight", out var fw)
                    && Regex.IsMatch(fw, @"bold|[7-9]00", RegexOptions.IgnoreCase);
                var key = (fam ?? "").ToLowerInvariant();
                if (bold && dataFonts.ContainsKey(key + "|bold")) key += "|bold";
                if (dataFonts.TryGetValue(key, out var ttf)) { n.Ttf = ttf; n.FontKey = key; }
                else allResolved = false;
            }
            foreach (var c in n.Children) Walk(c);
        }
        Walk(bodyNode);
        return anyLeaf && allResolved;
    }

    private static void FillStyledAttrs(StyledNode node, string attrText)
    {
        foreach (Match a in Regex.Matches(attrText, @"([\w-]+)\s*=\s*([""'])(.*?)\2"))
            node.Attrs[a.Groups[1].Value] = a.Groups[3].Value;
        if (node.Attrs.TryGetValue("class", out var cls))
            node.Classes.AddRange(cls.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ParseStyledRuns(string inner, StyledNode node)
    {
        foreach (Match any in StepAnyTagRegex.Matches(inner))
            if (!StyledSpanRx.IsMatch(any.Value)) return false;
        var runs = new List<string>();
        var pos = 0;
        void AddRun(string raw)
        {
            // &nbsp; must stay distinct from a collapsible space through decoding.
            var t = DecodeEntities(raw.Replace("&nbsp;", " ").Replace("&#160;", " "));
            t = Regex.Replace(t, @"[ \t\r\n\f]+", " ");
            if (t.Length > 0) runs.Add(t);
        }
        foreach (Match t in StyledSpanRx.Matches(inner))
        {
            AddRun(inner.Substring(pos, t.Index - pos));
            pos = t.Index + t.Length;     // span tags cut run boundaries, nothing more
        }
        AddRun(inner.Substring(pos));
        if (runs.Count > 0) runs[0] = runs[0].TrimStart(' ');
        if (runs.Count > 0) runs[^1] = runs[^1].TrimEnd(' ');
        runs.RemoveAll(r => r.Length == 0);
        if (runs.Count == 0) return false;
        node.Runs = runs;
        return true;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (Text.Font? font, Dictionary<int, int>? cmap)> _uniFontCache = new();
    // The symbol-PUA probe resolves once: an uncached FindFont loads and
    // parses the face file on EVERY symbol run (a bullet-heavy export makes
    // thousands of them and the conversion never finishes). Probed under a lock
    // (with the flag written last, volatile for the lock-free fast path) so a
    // parallel fixture never observes the flag without the font.
    private static Text.Font? _symbolPuaFont;
    private static volatile bool _symbolPuaProbed;
    private static readonly object _symbolPuaLock = new();

    /// <summary>Split a (visual-order) line into (text, font) segments: WinAnsi-encodable
    /// segments carry a null font (drawn with the block's Standard-14 resource); each
    /// non-encodable char resolves to the first fallback face covering it, and adjacent
    /// chars resolving to the same face merge into one segment. A single space between two
    /// same-face non-Latin words stays inside the segment so words don't flap fonts.</summary>
    private static List<(string Text, Text.Font? Font)> SegmentByFont(string s)
    {
        static bool IsAnsi(char c) => c <= 0x7F || Text.Cp1252.TryGetByte(c, out _);
        var result = new List<(string, Text.Font?)>();
        int i = 0;
        while (i < s.Length)
        {
            int start = i;
            if (IsAnsi(s[i]))
            {
                while (i < s.Length && IsAnsi(s[i])) i++;
                result.Add((s[start..i], null));
                continue;
            }
            var font = ResolveUnicodeFont(s[i].ToString());
            while (i < s.Length)
            {
                var c = s[i];
                if (IsAnsi(c))
                {
                    if (c == ' ' && i + 1 < s.Length && !IsAnsi(s[i + 1])
                        && ReferenceEquals(ResolveUnicodeFont(s[i + 1].ToString()), font)) { i++; continue; }
                    break;
                }
                if (!ReferenceEquals(ResolveUnicodeFont(c.ToString()), font)) break;
                i++;
            }
            result.Add((s[start..i], font));
        }
        return result;
    }
}
