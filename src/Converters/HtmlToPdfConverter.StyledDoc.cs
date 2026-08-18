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
        // into the CSS the source renderer honours there — the `margin:` shorthand,
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

    /// <summary>Render a styled-class data-font document (see
    /// <see cref="TryParseStyledDataFontDoc"/>) through the styled HTML engine's
    /// emission: A4 with the width grown to the content extent, one Y-flipped clipped
    /// frame, one 11-op text block per run (color, BT, Tf, color, Tm reset, Tm position,
    /// TJ, Tm reset, Tm identity, 0 g, ET), embedded Type0 faces, and CSS letter-spacing
    /// as per-glyph TJ segmentation whose adjustment is the float32 of the 3-decimal-
    /// rounded spacing. Geometry and margin model: page margins 90/72;
    /// body margin defaults to 6pt (auto → 0) and its top COLLAPSES (max) with the
    /// element chain; block gap = descLB + max(adjoining margins incl. div boundaries)
    /// + ascLB; line height is the font-independent 1.5·round(0.78125·size); the page
    /// width is max(595, rightmost text extent + 90). Returns null when a face fails
    /// to parse or the content overruns one page.</summary>
    internal static Document? RenderStyledDataFontDoc(StyledNode bodyNode)
    {
        const double marginTop = 72.0;
        const double marginLeftLay = 90.0;

        var bodyWidth = bodyNode.Style.TryGetValue("width", out var bw) ? StyledLen(bw) : 0;
        if (bodyWidth <= 0) bodyWidth = 595.0 - marginLeftLay - 90.0;
        var bodyMarginLeft = bodyNode.Style.TryGetValue("margin-left", out var bml) ? StyledLen(bml) : 6.0;
        var bodyMarginTop = bodyNode.Style.TryGetValue("margin-top", out var bmt) ? StyledLen(bmt) : 6.0;

        // Per-face parsers and vertical metrics.
        var glyphParsers = new Dictionary<string, Text.GlyphOutlineParser>(StringComparer.Ordinal);
        var faceMetrics = new Dictionary<string, (double winAsc, double winDesc, double upm)>(StringComparer.Ordinal);
        bool Faces(StyledNode p, out Text.GlyphOutlineParser gp, out (double winAsc, double winDesc, double upm) fm)
        {
            gp = null!;
            fm = default;
            if (!glyphParsers.TryGetValue(p.FontKey, out var g))
            {
                try
                {
                    g = new Text.GlyphOutlineParser(p.Ttf!);
                    var tp = new Text.TrueTypeParser(p.Ttf!);
                    tp.Parse();
                    if (tp.UnitsPerEm <= 0 || tp.UsWinAscent <= 0) return false;
                    faceMetrics[p.FontKey] = (tp.UsWinAscent, tp.UsWinDescent, tp.UnitsPerEm);
                }
                catch { return false; }
                glyphParsers[p.FontKey] = g;
            }
            gp = g;
            fm = faceMetrics[p.FontKey];
            return true;
        }

        // Font-independent CSS "normal" line height (LH(9)=10.5, LH(11.52)=13.5,
        // LH(12)=13.5), with win-metric half-leading baselines.
        static double NormalLh(double f) => 1.5 * Math.Floor(0.78125 * f + 0.5);

        // ---- Layout pass: place every run, tracking the rightmost extent ----
        var runsOut = new List<(double y, double x, string text, StyledNode p)>();
        var maxUrx = 0.0;
        var y = marginTop;                     // top-down; baseline set per leaf
        var pendingMargins = new List<double> { bodyMarginTop };
        var prevDesc = 0.0;

        bool LayoutLeaf(StyledNode p)
        {
            if (!Faces(p, out var gp, out var fm)) return false;
            var size = p.FontSizePt;
            var lh = NormalLh(size);
            var asc = fm.winAsc * size / fm.upm + (lh - (fm.winAsc + fm.winDesc) * size / fm.upm) / 2;
            var ls = p.Style.TryGetValue("letter-spacing", out var lsv) ? StyledLen(lsv) : 0.0;
            var lsF = (double)(float)Math.Round(ls, 3);
            var upper = p.Style.TryGetValue("text-transform", out var tt)
                && tt.Trim().Equals("uppercase", StringComparison.OrdinalIgnoreCase);

            var x0 = marginLeftLay + bodyMarginLeft;
            var colWidth = bodyWidth;
            for (var a = p.Parent; a is not null && a.Tag == "div"; a = a.Parent)
            {
                var aml = a.Style.TryGetValue("margin-left", out var v1) ? StyledLen(v1) : 0;
                var amr = a.Style.TryGetValue("margin-right", out var v2) ? StyledLen(v2) : 0;
                x0 += aml;
                colWidth -= aml + amr;
            }
            if (colWidth <= 0) return false;

            // Character stream tagged with run index (span boundaries stay separate ops).
            var stream = new List<(char c, int run)>();
            for (var r = 0; r < p.Runs!.Count; r++)
            {
                var rt = upper ? p.Runs[r].ToUpperInvariant() : p.Runs[r];
                foreach (var ch in rt) stream.Add((ch, r));
            }
            double AdvW(char c) =>
                (gp.CMap.TryGetValue(c, out var g) ? gp.GetAdvanceWidth(g) : 0) * size / fm.upm;

            // Greedy space-break wrap; letter-spacing widens every glyph advance
            // (including a run's last — the next run starts that much further right).
            var lines = new List<List<(char c, int run)>>();
            {
                var line = new List<(char c, int run)>();
                var w = 0.0;
                var i = 0;
                while (i < stream.Count)
                {
                    var j = i + (stream[i].c == ' ' ? 1 : 0);
                    while (j < stream.Count && stream[j].c != ' ') j++;
                    var segW = 0.0;
                    for (var k = i; k < j; k++) segW += AdvW(stream[k].c) + lsF;
                    if (line.Count > 0 && w + segW > colWidth + 1e-9)
                    {
                        lines.Add(line);
                        line = new List<(char c, int run)>();
                        w = 0;
                        var from = stream[i].c == ' ' ? i + 1 : i;
                        for (var k = from; k < j; k++)
                        {
                            line.Add(stream[k]);
                            w += AdvW(stream[k].c) + lsF;
                        }
                    }
                    else
                    {
                        for (var k = i; k < j; k++) line.Add(stream[k]);
                        w += segW;
                    }
                    i = j;
                }
                if (line.Count > 0) lines.Add(line);
            }

            var mt = p.Style.TryGetValue("margin-top", out var mtv) ? StyledLen(mtv, size) : 0.0;
            pendingMargins.Add(mt);
            y += prevDesc + pendingMargins.Max() + asc;

            for (var li = 0; li < lines.Count; li++)
            {
                var ln = lines[li];
                var x = x0;
                var gi = 0;
                while (gi < ln.Count)
                {
                    var runIdx = ln[gi].run;
                    var piece = new StringBuilder();
                    var pieceW = 0.0;
                    while (gi < ln.Count && ln[gi].run == runIdx)
                    {
                        piece.Append(ln[gi].c);
                        pieceW += AdvW(ln[gi].c) + lsF;
                        gi++;
                    }
                    runsOut.Add((y, x, piece.ToString(), p));
                    // The trailing letter-spacing advances the next run's X but does
                    // not extend the drawn extent of this one.
                    var urx = x + pieceW - (lsF > 0 ? lsF : 0);
                    if (urx > maxUrx) maxUrx = urx;
                    x += pieceW;
                }
                if (li < lines.Count - 1) y += lh;
            }

            prevDesc = lh - asc;
            pendingMargins.Clear();
            // UA default paragraph bottom margin is 1.12em when nothing is declared.
            pendingMargins.Add(p.Style.TryGetValue("margin-bottom", out var mbv)
                ? StyledLen(mbv, size) : 1.12 * size);
            return true;
        }

        bool WalkLayout(StyledNode n)
        {
            if (n.Tag == "p") return LayoutLeaf(n);
            foreach (var c in n.Children)
            {
                if (c.Tag == "div")
                {
                    pendingMargins.Add(c.Style.TryGetValue("margin-top", out var v) ? StyledLen(v) : 0);
                    if (!WalkLayout(c)) return false;
                    pendingMargins.Add(c.Style.TryGetValue("margin-bottom", out var v2) ? StyledLen(v2) : 0);
                }
                else if (!WalkLayout(c)) return false;
            }
            return true;
        }
        if (!WalkLayout(bodyNode) || runsOut.Count == 0) return null;
        return BuildStyledPage(runsOut, maxUrx);
    }

    /// <summary>Emit the laid-out runs as the fixed op pattern onto a fresh
    /// single-page document whose width is the content extent + the right margin.</summary>
    private static Document? BuildStyledPage(
        List<(double y, double x, string text, StyledNode p)> runsOut, double maxUrx)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        const double pageHeight = 842.0;
        const double marginLeft = 90.0;
        var pageWidth = Math.Max(595.0, maxUrx + 90.0);
        if (runsOut.Any(r => r.y > pageHeight - 60)) return null;   // beyond one page: legacy flow

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        var fontDict = Table.ResolvePageFontDict(page);

        static string F(double v) => ((double)(float)v).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
        static string FD(double v) => v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append("q\n");
        sb.Append($"1 0 0 -1 0 {FD(pageHeight)} cm\n");
        sb.Append("q\nQ\nq\n");
        sb.Append($"{FD(marginLeft)} 0 {F(pageWidth - marginLeft)} {FD(pageHeight)} re\n");
        sb.Append("W*\nn\nq\nq\n");

        foreach (var (y, x, text, p) in runsOut)
        {
            var ls = p.Style.TryGetValue("letter-spacing", out var lsv) ? StyledLen(lsv) : 0.0;
            var lsF = (double)(float)Math.Round(ls, 3);
            var (rr, gg, bb) = ((byte)0, (byte)0, (byte)0);
            if (p.Style.TryGetValue("color", out var col))
            {
                var cm = Regex.Match(col, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
                if (cm.Success)
                    (rr, gg, bb) = (byte.Parse(cm.Groups[1].Value), byte.Parse(cm.Groups[2].Value),
                        byte.Parse(cm.Groups[3].Value));
            }
            var (res, hex) = Text.Type0FontEmbedder.Embed(fontDict, p.Ttf!,
                StyledFontDisplayName(p.FontKey), text, stripSpacesInBaseFont: true);
            sb.Append($"{F(rr / 255.0)} {F(gg / 255.0)} {F(bb / 255.0)} rg").Append('\n');
            sb.Append("BT\n");
            sb.Append($"/{res} {p.FontSizePt.ToString("0.000", ic)} Tf\n");
            sb.Append($"{FD(rr / 255.0)} {FD(gg / 255.0)} {FD(bb / 255.0)} rg").Append('\n');
            sb.Append("1 0 0 -1 0 0 Tm\n");
            sb.Append($"1 0 0 -1 {F(x)} {F(y)} Tm\n");
            sb.Append(BuildStyledTj(hex, lsF, p.FontSizePt));
            sb.Append("1 0 0 -1 0 0 Tm\n");
            sb.Append("1 0 0 1 0 0 Tm\n");
            sb.Append("0 g\n");
            sb.Append("ET\n");
        }

        sb.Append("Q\nQ\nQ\nq\nq\nQ\nQ\nQ\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
        return doc;
    }

    /// <summary>Human display name for an embedded data-face key ("firasans-medium" →
    /// "FiraSans-Medium", "firasans|bold" → "FiraSans Bold") — the space-stripped form
    /// becomes the /BaseFont name.</summary>
    private static string StyledFontDisplayName(string key)
    {
        var parts = key.Split('|');
        // Preserve the hyphenated form the @font-face declared (family case is lost in
        // the key; recapitalize per segment — cosmetic only, the test reads structure).
        var display = string.Join("-", parts[0].Split('-').Select(seg =>
            seg.Length == 0 ? seg : char.ToUpperInvariant(seg[0]) + seg.Substring(1)));
        return parts.Length > 1 ? display + " " + char.ToUpperInvariant(parts[1][0]) + parts[1].Substring(1) : display;
    }

    /// <summary>Build the TJ op for one line: letter-spaced text emits every glyph as its
    /// own 2-byte hex segment with the spacing adjustment (−spacing/size·1000) between
    /// consecutive glyphs and none after the last; unspaced text is one whole segment.</summary>
    private static string BuildStyledTj(byte[] hexGlyphIds, double letterSpacingPt, double fontSizePt)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append('[');
        var n = hexGlyphIds.Length / 2;
        if (letterSpacingPt <= 0 || n <= 1)
        {
            sb.Append('<');
            foreach (var b in hexGlyphIds) sb.Append(b.ToString("X2"));
            sb.Append('>');
        }
        else
        {
            var adj = (-letterSpacingPt / fontSizePt * 1000.0).ToString("0.##########", ic);
            for (var i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ').Append(adj).Append(' ');
                sb.Append('<');
                sb.Append(hexGlyphIds[i * 2].ToString("X2"));
                sb.Append(hexGlyphIds[i * 2 + 1].ToString("X2"));
                sb.Append('>');
            }
        }
        sb.Append("] TJ\n");
        return sb.ToString();
    }

    /// <summary>True when the string contains a CJK / Han / Kana / Hangul character — text
    /// that needs an embedded Unicode font (the Standard-14 fonts have no such glyphs).</summary>
    private static bool HasCjk(string s)
    {
        foreach (var ch in s)
        {
            int o = ch;
            if ((o >= 0x3400 && o <= 0x9FFF)   // CJK Unified + Ext-A
                || (o >= 0x3000 && o <= 0x30FF) // CJK symbols, Hiragana, Katakana
                || (o >= 0xF900 && o <= 0xFAFF) // CJK compatibility ideographs
                || (o >= 0xAC00 && o <= 0xD7AF)) // Hangul syllables
                return true;
        }
        return false;
    }

    /// <summary>True when the run contains any character the WinAnsi (Cp1252) Tf/Tj path
    /// cannot encode — Cyrillic, Greek, Armenian, Arabic, Hebrew, CJK, … . Such a run must
    /// go through an embedded Unicode face or its non-Latin characters flatten to '?'.</summary>
    private static bool NeedsUnicode(string s)
    {
        foreach (var ch in s)
            if (ch > 0x7F && !Text.Cp1252.TryGetByte(ch, out _)) return true;
        return false;
    }

    /// <summary>Convert the embedded RTL segments of a MIXED LTR+RTL line to visual order
    /// in place: each maximal run of RTL characters (with any neutrals bounded by RTL chars
    /// on both sides) is shaped/reversed via <see cref="ToVisualRtl"/> while the LTR text
    /// around it keeps its logical position. The extraction-side logicalizer reverses the
    /// per-run transformation, so round-tripped text stays token-identical.</summary>
    private static string VisualizeMixedRtl(string s)
    {
        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            if (!Text.BidiReorderer.IsRtlChar(s[i])) { sb.Append(s[i]); i++; continue; }
            // Extend to the LAST RTL char of this cluster, keeping internal neutrals
            // (spaces, digits, punctuation between two RTL words) inside the segment.
            int end = i, j = i;
            while (j < s.Length)
            {
                if (Text.BidiReorderer.IsRtlChar(s[j])) { end = j; j++; }
                else if (s[j] == ' ' || s[j] == ' ' || char.IsPunctuation(s[j]) || char.IsDigit(s[j])) j++;
                else break;
            }
            sb.Append(ToVisualRtl(s.Substring(i, end - i + 1)));
            i = end + 1;
        }
        return sb.ToString();
    }

    /// <summary>True when the line is entirely RTL (Arabic/Hebrew/…) letters plus neutral
    /// punctuation/whitespace — the case where the run can be written wholesale in visual order.
    /// Mixed LTR+RTL lines need full bidi and fall through to the Standard-14 path.</summary>
    private static bool IsPureRtl(string s)
    {
        var hasRtl = false;
        foreach (var c in s)
        {
            if (Text.BidiReorderer.IsRtlChar(c)) hasRtl = true;
            else if (c == ' ' || c == '\t' || (c >= '!' && c <= '@')
                     || (c >= '[' && c <= '`') || (c >= '{' && c <= '~'))
            { /* neutral */ }
            else return false;
        }
        return hasRtl;
    }

    /// <summary>Convert a pure-RTL logical string to the VISUAL order drawn left-to-right:
    /// Arabic gets contextual shaping (which already emits visual order); other RTL scripts
    /// (Hebrew, …) are simply reversed.</summary>
    private static string ToVisualRtl(string s)
    {
        if (Text.ArabicTextShaper.ContainsArabic(s)) return Text.ArabicTextShaper.Shape(s);
        // Reverse the line run-wise: DIGIT sequences — including their internal
        // separators (14:00-16:30, 1/11/2014, 99.5%) — read left-to-right inside
        // an RTL line, so they keep their logical order while everything else
        // reverses around them.
        var runs = new List<string>();
        var i = 0;
        while (i < s.Length)
        {
            int j;
            if (char.IsDigit(s[i]))
            {
                j = i + 1;
                while (j < s.Length && (char.IsDigit(s[j])
                    || (s[j] is ':' or '/' or '-' or '.' or ','
                        && j + 1 < s.Length && char.IsDigit(s[j + 1]))))
                    j++;
                runs.Add(s[i..j]);
            }
            else
            {
                j = i + 1;
                while (j < s.Length && !char.IsDigit(s[j])) j++;
                var seg = s[i..j].ToCharArray();
                System.Array.Reverse(seg);
                runs.Add(new string(seg));
            }
            i = j;
        }
        runs.Reverse();
        return string.Concat(runs);
    }

    /// <summary>Emit a single positioned text run at (<paramref name="x"/>,<paramref name="y"/>).
    /// A pure Arabic/Hebrew or CJK run is written in visual order through an embedded Type0/CID
    /// face (the Standard-14 fonts would collapse it to '?'); everything else uses the WinAnsi
    /// Tf/Tj path. Used for list markers, which may themselves be non-Latin (a CSS ::before
    /// generated Arabic marker).</summary>
    private static void EmitPositionedRun(Page page, string fontRes, double fontSize, double x, double y, string text)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var isRtl = IsPureRtl(text);
        var visual = isRtl ? ToVisualRtl(text)
            : Text.BidiReorderer.ContainsRtl(text) ? VisualizeMixedRtl(text) : text;
        var uniFont = NeedsUnicode(text) ? ResolveUnicodeFont(visual) : null;
        var ttf = uniFont?.SourceFontData?.TtfData;
        var sb = new StringBuilder();
        sb.AppendLine("BT");
        if (ttf is not null
            && page.Dict.Get("Resources") as Core.PdfDictionary is { } res
            && res.Get("Font") as Core.PdfDictionary is { } fontDict)
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                fontDict, ttf, uniFont!.FontName ?? "Unicode", visual, stripSpacesInBaseFont: true);
            sb.Append($"/{rn} {fontSize.ToString("F1", inv)} Tf ");
            sb.Append($"1 0 0 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
            sb.Append('<').Append(System.Convert.ToHexString(hex)).Append("> Tj ");
        }
        else
        {
            sb.Append($"/{fontRes} {fontSize.ToString("F1", inv)} Tf ");
            sb.Append($"1 0 0 1 {x.ToString("F2", inv)} {y.ToString("F2", inv)} Tm ");
            sb.Append($"({EscapePdfString(text)}) Tj ");
        }
        sb.AppendLine("ET");
        page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
    }

    // Broad-Unicode faces (installed on most Windows systems) tried in order; the first
    // whose embedded program covers every non-WinAnsi char in the run is used.
    private static readonly string[] UnicodeFallbackFonts =
        { "Arial", "SimSun", "Malgun Gothic", "Microsoft YaHei", "MS Gothic", "Arial Unicode MS",
          // Script-specific faces shipped with Windows 10/11, tried after the broad CJK
          // set: Indic, Myanmar, Ethiopic/NKo, Canadian Syllabics, Thaana, Syriac,
          // Thai/Lao/Khmer, and historic scripts (Gothic, Old Italic, …).
          "Nirmala UI", "Myanmar Text", "Ebrima", "Gadugi", "MV Boli", "Estrangelo Edessa",
          "Leelawadee UI", "Segoe UI Historic" };

    private static readonly Dictionary<string, (Text.Font? font, Dictionary<int, int>? cmap)> _uniFontCache = new();

    /// <summary>Resolve an embedded Unicode fallback face that covers every non-WinAnsi
    /// character in <paramref name="text"/>, or null when none is available.</summary>
    private static Text.Font? ResolveUnicodeFont(string text)
    {
        foreach (var name in UnicodeFallbackFonts)
        {
            if (!_uniFontCache.TryGetValue(name, out var entry))
            {
                Text.Font? f = null; Dictionary<int, int>? cmap = null;
                try
                {
                    f = Text.FontRepository.FindFont(name);
                    if (f?.SourceFontData?.TtfData is { } ttf) cmap = new Text.GlyphOutlineParser(ttf).CMap;
                }
                catch { f = null; cmap = null; }
                entry = (f, cmap);
                _uniFontCache[name] = entry;
            }
            if (entry.font?.SourceFontData is null || entry.cmap is null) continue;
            var covers = true;
            foreach (var ch in text)
            {
                if (ch <= 0x7F || Text.Cp1252.TryGetByte(ch, out _)) continue;
                if (!entry.cmap.TryGetValue(ch, out var gid) || gid == 0) { covers = false; break; }
            }
            if (covers) return entry.font;
        }
        return null;
    }

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
