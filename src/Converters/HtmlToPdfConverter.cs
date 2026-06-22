using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

/// <summary>
/// Converts HTML into a PDF document using a minimal block-layout model:
/// block elements (p/div/h1-h6/blockquote/li/tr) stack vertically with
/// per-block top and bottom margins, inline elements flow inside a block,
/// and text wraps to the content width. Not a CSS-complete renderer —
/// enough structure for pagination to match block-level document shape.
/// </summary>
internal static class HtmlToPdfConverter
{
    public static Document Convert(string htmlPath, HtmlLoadOptions? options = null)
    {
        var encoding = GetEncoding(options);
        var html = File.ReadAllText(htmlPath, encoding);
        return ConvertFromHtml(html, options);
    }

    public static Document Convert(byte[] htmlData, HtmlLoadOptions? options = null)
    {
        var encoding = GetEncoding(options);
        var html = encoding.GetString(htmlData);
        return ConvertFromHtml(html, options);
    }

    private static Encoding GetEncoding(HtmlLoadOptions? options)
    {
        if (options?.InputEncoding is not null)
        {
            try { return Encoding.GetEncoding(options.InputEncoding); }
            catch { /* fall through to UTF-8 */ }
        }
        return Encoding.UTF8;
    }

    /// <summary>One rendered block: a run of text with uniform style and
    /// vertical spacing on either side. One Block becomes N wrapped lines
    /// at layout time.</summary>
    internal sealed class Block
    {
        public string Text = "";
        public double FontSize;
        public string FontRes = "F1";    // F1=Helvetica, F2=Helvetica-Bold, F3=Helvetica-Oblique
        public double MarginTop;
        public double MarginBottom;
        public double LeftIndent;
        public bool IsListItem;
        public bool IsHardBreak;         // hidden spacer (e.g. <br> inside block)
        // Floor on the block's rendered height (from CSS height/min-height).
        // Zero = let the text content alone decide.
        public double ExplicitHeight;
        // <hr>: draw a horizontal rule line in RuleColor / RuleWidth instead
        // of just consuming vertical space.
        public bool IsHorizontalRule;
        public Color? RuleColor;
        public double RuleWidth;
    }

    /// <summary>True when the markup carries block-level structure (lists,
    /// paragraphs, headings, tables) that needs vertical/indented block layout
    /// rather than a single flat run of stripped text.</summary>
    internal static bool HasBlockStructure(string html) =>
        Regex.IsMatch(html ?? "", @"<\s*(ul|ol|li|p|div|h[1-6]|table|tr|blockquote|hr)\b",
            RegexOptions.IgnoreCase);

    /// <summary>Extract the rule colour and width for an &lt;hr&gt; from its
    /// inline style. Reads the CSS border shorthand / border-color / color.</summary>
    private static void ParseHrStyle(Dictionary<string, string>? attrs,
        out Color? color, out double width)
    {
        color = null;
        width = 1;
        if (attrs is null) return;
        attrs.TryGetValue("style", out var style);
        style ??= "";
        // Width from the first pixel length in a border declaration.
        var wm = Regex.Match(style, @"border[^:]*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success && double.TryParse(wm.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var w) && w > 0)
            width = w;
        // Colour: scan the style string (covers border/border-color/color).
        color = ParseCssColor(style);
    }

    /// <summary>Parse the first CSS colour token (hex, rgb(), or a common
    /// named colour) found in <paramref name="text"/>. Null when none.</summary>
    private static Color? ParseCssColor(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var hex = Regex.Match(text, @"#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b");
        if (hex.Success)
        {
            var h = hex.Groups[1].Value;
            if (h.Length == 3) h = $"{h[0]}{h[0]}{h[1]}{h[1]}{h[2]}{h[2]}";
            return Color.FromRgb(System.Convert.ToInt32(h[..2], 16),
                System.Convert.ToInt32(h[2..4], 16), System.Convert.ToInt32(h[4..6], 16));
        }
        var rgb = Regex.Match(text, @"rgb\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
        if (rgb.Success)
            return Color.FromRgb(int.Parse(rgb.Groups[1].Value),
                int.Parse(rgb.Groups[2].Value), int.Parse(rgb.Groups[3].Value));
        foreach (Match nm in Regex.Matches(text, @"[a-zA-Z]+"))
        {
            switch (nm.Value.ToLowerInvariant())
            {
                case "black": return Color.FromRgb(0, 0, 0);
                case "white": return Color.FromRgb(255, 255, 255);
                case "red": return Color.FromRgb(255, 0, 0);
                case "green": return Color.FromRgb(0, 128, 0);
                case "blue": return Color.FromRgb(0, 0, 255);
                case "yellow": return Color.FromRgb(255, 255, 0);
                case "gray": case "grey": return Color.FromRgb(128, 128, 128);
                case "orange": return Color.FromRgb(255, 165, 0);
                case "purple": return Color.FromRgb(128, 0, 128);
                case "navy": return Color.FromRgb(0, 0, 128);
            }
        }
        return null;
    }

    /// <summary>Parse HTML into the flat block list used by the layout pass.
    /// Exposed for the in-page HtmlFragment renderer.</summary>
    internal static List<Block> ParseHtmlBlocks(string html) => ParseBlocks(html);

    // Tags that open a block-level element; each starts a new Block on exit.
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "blockquote", "ul", "ol", "li", "tr", "td", "th",
        "h1", "h2", "h3", "h4", "h5", "h6",
        "table", "pre", "hr",
    };

    // Tags whose inner content is discarded entirely.
    private static readonly HashSet<string> SkipTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "head", "meta", "link", "title",
    };

    private static Document ConvertFromHtml(string html, HtmlLoadOptions? options)
    {
        var pageInfo = options?.PageInfo;
        var pageWidth   = pageInfo?.Width  is > 0 ? pageInfo.Width  : 612.0;
        var pageHeight  = pageInfo?.Height is > 0 ? pageInfo.Height : 792.0;
        var pageMargin  = pageInfo?.Margin;
        // Respect user-set margins verbatim (including explicit zeros); fall back to
        // 72 pt only when MarginInfo was never touched.
        bool marginsExplicit = pageMargin?.IsTouched ?? false;
        var marginLeft   = marginsExplicit ? pageMargin!.Left   : 72.0;
        var marginRight  = marginsExplicit ? pageMargin!.Right  : 72.0;
        var marginTop    = marginsExplicit ? pageMargin!.Top    : 72.0;
        var marginBottom = marginsExplicit ? pageMargin!.Bottom : 72.0;

        var doc = Document.Create();
        var page = doc.Pages.Add(pageWidth, pageHeight);
        EnsureFonts(page);

        // Pull <title> for doc metadata before we lose it in stripping.
        var titleMatch = Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (titleMatch.Success)
            doc.Info.Title = DecodeEntities(titleMatch.Groups[1].Value).Trim();

        var blocks = ParseBlocks(html);
        if (blocks.Count == 0) return doc;

        var contentWidth = pageWidth - marginLeft - marginRight;
        var y = pageHeight - marginTop;
        var sb = new StringBuilder();

        bool lastWasHardBreak = false;
        foreach (var block in blocks)
        {
            var blockFontSize = block.FontSize;
            var lineHeight = blockFontSize * 1.3;

            // Hard-break blocks (<br>, empty <p>, <hr>) only consume vertical
            // space — never emit an empty BT/ET run, which would surface as
            // extra zero-length TextFragments to TextFragmentAbsorber. Coalesce
            // runs of consecutive hard-breaks so deeply-nested empty containers
            // don't explode page count (HTML like <div><div></div></div> emits
            // a chain of closes that would otherwise each become a blank line).
            if (block.IsHardBreak || string.IsNullOrEmpty(block.Text))
            {
                // Prefer the explicit CSS height over the default half-line
                // spacer — CMS template HTML often uses empty styled divs as
                // visual separator bars, and ignoring their height would
                // collapse intended pagination.
                var spacer = block.ExplicitHeight > 0
                    ? block.ExplicitHeight
                    : (lastWasHardBreak ? 0 : lineHeight * 0.5);
                if (spacer > 0)
                {
                    if (y - spacer < marginBottom)
                    {
                        page = doc.Pages.Add(pageWidth, pageHeight);
                        EnsureFonts(page);
                        y = pageHeight - marginTop;
                    }
                    y -= spacer;
                }
                lastWasHardBreak = true;
                continue;
            }
            lastWasHardBreak = false;

            // Apply top margin (unless we're at the start of a fresh page).
            if (y < pageHeight - marginTop - 1e-3)
                y -= block.MarginTop;

            var availWidth = contentWidth - block.LeftIndent;
            var lines = WordWrap(block.Text, availWidth, blockFontSize * 0.52);
            // Pad the block's rendered area up to ExplicitHeight so styled
            // fixed-height elements keep their reserved vertical space even
            // when the text inside wraps to fewer lines.
            var textHeight = lines.Length * lineHeight;
            var paddingBelow = block.ExplicitHeight > textHeight ? block.ExplicitHeight - textHeight : 0;

            foreach (var line in lines)
            {
                if (y - lineHeight < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                sb.Clear();
                sb.AppendLine("BT");
                sb.Append($"/{block.FontRes} {blockFontSize.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} Tf ");
                sb.Append($"1 0 0 1 {(marginLeft + block.LeftIndent).ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} {y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} Tm ");
                sb.Append($"({EscapePdfString(line)}) Tj ");
                sb.AppendLine("ET");
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));
                y -= lineHeight;
            }
            if (paddingBelow > 0)
            {
                if (y - paddingBelow < marginBottom)
                {
                    page = doc.Pages.Add(pageWidth, pageHeight);
                    EnsureFonts(page);
                    y = pageHeight - marginTop;
                }
                else
                {
                    y -= paddingBelow;
                }
            }
            y -= block.MarginBottom;
        }

        return doc;
    }

    /// <summary>Turn HTML into a list of Block records. The parser is a
    /// small hand-rolled tokeniser (no external DOM): it tracks the stack
    /// of open block elements to decide font + margins for each text run.</summary>
    private static List<Block> ParseBlocks(string html)
    {
        // Strip script/style/head bodies whole; inline tags inside them are
        // not semantic content.
        html = Regex.Replace(html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        // Strip DOCTYPE, comments and CDATA sections — the tag tokenizer
        // below only recognises <Name …> shapes, so these would otherwise
        // surface as literal text content.
        html = Regex.Replace(html, @"<!DOCTYPE[^>]*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<!--[\s\S]*?-->", "");
        html = Regex.Replace(html, @"<!\[CDATA\[[\s\S]*?\]\]>", "");
        // Strip leading BOM if present — UTF-8 HTMLs often ship with one.
        if (html.Length > 0 && html[0] == '\uFEFF') html = html.Substring(1);
        // Decode entities once at the text layer.
        var tokens = Tokenize(html);

        var blocks = new List<Block>();
        var currentText = new StringBuilder();
        var styleStack = new Stack<BlockStyle>();
        styleStack.Push(new BlockStyle { FontSize = 11, FontRes = "F1", MarginTop = 0, MarginBottom = 0, LeftIndent = 0 });

        void Flush(bool _unused, BlockStyle styleUsed)
        {
            var raw = currentText.ToString();
            // Collapse runs of *ASCII* whitespace only — U+00A0 (from
            // &nbsp;) is intentional visual content and must survive
            // collapse+Trim so an &nbsp;-only <p> still emits a line.
            var collapsed = Regex.Replace(raw, @"[ \t\r\n\f]+", " ").Trim(' ', '\t', '\r', '\n', '\f');
            if (collapsed.Length > 0)
            {
                blocks.Add(new Block
                {
                    Text = collapsed,
                    FontSize = styleUsed.FontSize,
                    FontRes = styleUsed.FontRes,
                    MarginTop = styleUsed.MarginTop,
                    MarginBottom = styleUsed.MarginBottom,
                    LeftIndent = styleUsed.LeftIndent,
                    IsListItem = styleUsed.IsListItem,
                    ExplicitHeight = styleUsed.ExplicitHeight,
                });
            }
            else if (styleUsed.ExplicitHeight > 0)
            {
                // Empty block with explicit height (e.g. `<div style="height:50px">`
                // used as a visual separator bar). Emit a text-less spacer
                // so pagination sees the reserved vertical space.
                blocks.Add(new Block
                {
                    Text = "",
                    FontSize = styleUsed.FontSize,
                    FontRes = styleUsed.FontRes,
                    MarginTop = 0,
                    MarginBottom = 0,
                    LeftIndent = styleUsed.LeftIndent,
                    IsHardBreak = true,
                    ExplicitHeight = styleUsed.ExplicitHeight,
                });
            }
            // Empty block close-tags without explicit height do not emit a
            // spacer — nested empty containers (e.g. <div><div></div></div>)
            // would otherwise inflate page count well beyond the text
            // volume. Explicit vertical spacing comes from <br>, <hr>,
            // block margins, and any CSS height/min-height override.
            currentText.Clear();
        }

        foreach (var tok in tokens)
        {
            if (tok.Kind == TokenKind.Text)
            {
                currentText.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!;
            if (SkipTags.Contains(tag)) continue;

            if (tok.IsClose)
            {
                if (BlockTags.Contains(tag))
                {
                    var popped = styleStack.Count > 1 ? styleStack.Pop() : styleStack.Peek();
                    Flush(true,popped);
                }
                // Inline close tags are no-ops for block layout.
                continue;
            }

            // Opening tag (or self-closing).
            if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                // <br> inserts a newline *within* the current block. We
                // flush as an empty forced-break block so the next text
                // starts on a new line at the same style.
                Flush(true,styleStack.Peek());
                continue;
            }
            if (tag.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                Flush(true,styleStack.Peek());
                // Draw <hr> as a horizontal rule. The line colour/width come
                // from the CSS border (e.g. "border: 1px solid red"); default
                // to a thin grey line when unspecified.
                ParseHrStyle(tok.Attributes, out var hrColor, out var hrWidth);
                blocks.Add(new Block
                {
                    Text = "",
                    FontSize = styleStack.Peek().FontSize,
                    FontRes = "F1",
                    MarginTop = 6,
                    MarginBottom = 6,
                    // Not IsHardBreak: a rule is drawn content, so it must
                    // survive the trailing-spacer trim and be rendered.
                    IsHorizontalRule = true,
                    RuleColor = hrColor,
                    RuleWidth = hrWidth,
                });
                continue;
            }

            if (BlockTags.Contains(tag))
            {
                // Start a new block: flush any pending inline text at the
                // outer style, then push the new style.
                Flush(false,styleStack.Peek());
                var parent = styleStack.Peek();
                var style = new BlockStyle
                {
                    FontSize = parent.FontSize,
                    FontRes = parent.FontRes,
                    MarginTop = 0,
                    MarginBottom = 0,
                    LeftIndent = parent.LeftIndent,
                };
                ApplyBlockTagStyle(tag, style);
                // Inline style="…" overrides tag defaults: if the author
                // explicitly set padding-left / margin-left we drop the
                // list-style indent the tag default added so that e.g.
                // `<ol style="padding-left:0">` sits flush with body text.
                if (HasInlineIndentOverride(tok.Attributes))
                    style.LeftIndent = parent.LeftIndent;
                ApplyInlineStyle(tok.Attributes, style);
                styleStack.Push(style);
                continue;
            }

            // Inline tags: mutate the top-of-stack style for <b>/<i>/<strong>/<em>.
            // <span style="font-size:..."> also adjusts size for the inner run.
            if (tag is "b" or "strong")
                MarkInline(styleStack, "F2");
            else if (tag is "i" or "em")
                MarkInline(styleStack, "F3");
            else if (tag == "small")
                MarkInlineSize(styleStack, factor: 0.85);
        }
        // Final flush
        Flush(false,styleStack.Peek());
        // Drop trailing hard-break spacers so the doc doesn't grow a blank
        // tail page for HTML that ends with close-tags.
        // Drop trailing spacer-only hardbreaks so HTML that ends with close-tags
        // doesn't grow a blank tail page. Hardbreaks with an explicit CSS
        // height are intentional layout spacers — keep those.
        while (blocks.Count > 0 && blocks[^1].IsHardBreak && blocks[^1].ExplicitHeight <= 0)
            blocks.RemoveAt(blocks.Count - 1);
        return blocks;
    }

    private sealed class BlockStyle
    {
        public double FontSize;
        public string FontRes = "F1";
        public double MarginTop;
        public double MarginBottom;
        public double LeftIndent;
        public bool IsListItem;
        // Explicit CSS height / min-height in points. When >0 the block's
        // own rendered area must be at least this tall, so empty-body
        // styled divs (common in CMS template HTML) still contribute
        // vertical space to pagination.
        public double ExplicitHeight;
    }

    private static void ApplyBlockTagStyle(string tag, BlockStyle s)
    {
        // Minimal margins — only headings and blockquotes get meaningful
        // spacing. p/div/ul/tr stack line-on-line so page counts mirror what
        // the tag-strip + wrap path would produce for the same text volume.
        switch (tag.ToLowerInvariant())
        {
            case "h1": s.FontSize = 18; s.FontRes = "F2"; s.MarginTop = 4; s.MarginBottom = 2; break;
            case "h2": s.FontSize = 15; s.FontRes = "F2"; s.MarginTop = 3; s.MarginBottom = 2; break;
            case "h3": s.FontSize = 13; s.FontRes = "F2"; s.MarginTop = 3; s.MarginBottom = 2; break;
            case "h4": s.FontSize = 12; s.FontRes = "F2"; s.MarginTop = 2; s.MarginBottom = 1; break;
            case "h5": s.FontSize = 11; s.FontRes = "F2"; s.MarginTop = 2; s.MarginBottom = 1; break;
            case "h6": s.FontSize = 10; s.FontRes = "F2"; s.MarginTop = 1; s.MarginBottom = 1; break;
            case "blockquote": s.MarginTop = 3; s.MarginBottom = 3; s.LeftIndent += 20; break;
            case "ul":
            case "ol":         s.LeftIndent += 20; break;
            case "li":         s.IsListItem = true; break;
            case "pre":        s.FontRes = "F4"; break;
            // p, div, tr, td, th, table: inherit parent margins (0 by default).
        }
    }

    // Parse a tiny subset of inline style="…" — enough to let per-block
    // font-size overrides (common in email-style HTML) affect layout.
    private static readonly Regex StyleDeclRx = new(
        @"([a-z-]+)\s*:\s*([^;]+?)\s*(?:;|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool HasInlineIndentOverride(Dictionary<string, string>? attrs)
    {
        if (attrs is null || !attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr))
            return false;
        return Regex.IsMatch(styleStr, @"(padding-left|margin-left)\s*:", RegexOptions.IgnoreCase);
    }

    private static void ApplyInlineStyle(Dictionary<string, string>? attrs, BlockStyle s)
    {
        if (attrs is null) return;
        if (!attrs.TryGetValue("style", out var styleStr) || string.IsNullOrWhiteSpace(styleStr)) return;
        foreach (Match m in StyleDeclRx.Matches(styleStr))
        {
            var prop = m.Groups[1].Value.ToLowerInvariant();
            var val = m.Groups[2].Value.Trim();
            if (prop == "font-size")
            {
                if (TryParseLength(val, out var pts)) s.FontSize = pts;
            }
            else if (prop == "font-weight")
            {
                if (val is "bold" or "bolder" || (int.TryParse(val, out var n) && n >= 600))
                    s.FontRes = s.FontRes == "F3" ? "F2" : "F2";
            }
            else if (prop == "font-style")
            {
                if (val is "italic" or "oblique")
                    s.FontRes = s.FontRes == "F2" ? "F2" : "F3";
            }
            else if (prop == "margin-top")
            {
                if (TryParseLength(val, out var pts)) s.MarginTop = pts;
            }
            else if (prop == "margin-bottom")
            {
                if (TryParseLength(val, out var pts)) s.MarginBottom = pts;
            }
            else if (prop == "margin-left" || prop == "padding-left")
            {
                if (TryParseLength(val, out var pts)) s.LeftIndent += pts;
            }
            else if (prop == "height" || prop == "min-height")
            {
                if (TryParseLength(val, out var pts) && pts > s.ExplicitHeight)
                    s.ExplicitHeight = pts;
            }
        }
    }

    private static bool TryParseLength(string s, out double pts)
    {
        pts = 0;
        // Accept "13px" / "10pt" / "1em". Reject percent / calc / etc.
        var m = Regex.Match(s, @"^(-?\d+(?:\.\d+)?)\s*(px|pt|em|rem)?$", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var n = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Success ? m.Groups[2].Value.ToLowerInvariant() : "px";
        pts = unit switch
        {
            "pt" => n,
            "px" => n * 0.75,          // 96dpi: 1px = 0.75pt
            "em" or "rem" => n * 11,   // against our default body 11pt
            _ => n,
        };
        return pts > 0;
    }

    private static void MarkInline(Stack<BlockStyle> stack, string fontRes)
    {
        // Inline emphasis modifies the *current* block's style mid-stream.
        // Minimal fidelity: promote the whole block to the emphasised font
        // when any part of it uses <b>/<i>. Real mixed-style output would
        // require splitting Block into sub-runs.
        if (stack.Count == 0) return;
        var top = stack.Peek();
        if (top.FontRes == "F1") top.FontRes = fontRes;
    }

    private static void MarkInlineSize(Stack<BlockStyle> stack, double factor)
    {
        if (stack.Count == 0) return;
        var top = stack.Peek();
        top.FontSize *= factor;
    }

    private enum TokenKind { Text, Tag }
    private sealed class Token
    {
        public TokenKind Kind;
        public string? Tag;
        public bool IsClose;
        public bool IsSelfClosing;
        public Dictionary<string, string>? Attributes;
        public string Value = "";
    }

    private static readonly Regex TagRx = new(
        @"<(/?)([A-Za-z][A-Za-z0-9]*)\s*([^>]*?)(/?)>",
        RegexOptions.Compiled);

    private static readonly Regex AttrRx = new(
        "([A-Za-z_:][-A-Za-z0-9_:.]*)\\s*(?:=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s\">]+)))?",
        RegexOptions.Compiled);

    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        int idx = 0;
        foreach (Match m in TagRx.Matches(html))
        {
            if (m.Index > idx)
            {
                var text = html.Substring(idx, m.Index - idx);
                if (text.Length > 0)
                    tokens.Add(new Token { Kind = TokenKind.Text, Value = text });
            }
            var attrs = ParseAttributes(m.Groups[3].Value);
            tokens.Add(new Token
            {
                Kind = TokenKind.Tag,
                Tag = m.Groups[2].Value,
                IsClose = m.Groups[1].Value == "/",
                IsSelfClosing = m.Groups[4].Value == "/",
                Attributes = attrs,
            });
            idx = m.Index + m.Length;
        }
        if (idx < html.Length)
        {
            var text = html.Substring(idx);
            if (text.Length > 0)
                tokens.Add(new Token { Kind = TokenKind.Text, Value = text });
        }
        return tokens;
    }

    private static Dictionary<string, string>? ParseAttributes(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttrRx.Matches(s))
        {
            var name = m.Groups[1].Value;
            var val = m.Groups[2].Success ? m.Groups[2].Value
                     : m.Groups[3].Success ? m.Groups[3].Value
                     : m.Groups[4].Success ? m.Groups[4].Value
                     : "";
            dict[name] = val;
        }
        return dict.Count > 0 ? dict : null;
    }

    private static string DecodeEntities(string text)
    {
        // Numeric + named; covers the common set. Full HTML5 entity table is out of scope.
        text = Regex.Replace(text, @"&#(\d+);", m =>
            int.TryParse(m.Groups[1].Value, out var code) ? char.ConvertFromUtf32(code) : m.Value);
        text = Regex.Replace(text, @"&#x([0-9A-Fa-f]+);", m =>
            int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var code)
                ? char.ConvertFromUtf32(code) : m.Value);
        return text
            // Use a real no-break space (U+00A0) so Trim() leaves it in
            // place; an &nbsp;-only paragraph is a deliberate vertical
            // spacer in many CMS-generated HTMLs and should occupy a line.
            .Replace("&nbsp;", "\u00A0")
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&apos;", "'");
    }

    private static string[] WordWrap(string text, double maxWidth, double charWidth)
    {
        if (string.IsNullOrEmpty(text)) return [""];
        var maxChars = (int)(maxWidth / Math.Max(charWidth, 1));
        if (maxChars <= 0) maxChars = 1;
        if (text.Length <= maxChars) return [text];

        var result = new List<string>();
        var remaining = text;
        while (remaining.Length > maxChars)
        {
            var breakAt = remaining.LastIndexOf(' ', maxChars);
            if (breakAt <= 0) breakAt = maxChars;
            result.Add(remaining[..breakAt]);
            remaining = remaining[breakAt..].TrimStart();
        }
        if (remaining.Length > 0) result.Add(remaining);
        return result.ToArray();
    }

    private static string EscapePdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static void EnsureFonts(Page page)
    {
        EnsureFont(page, "Helvetica", "F1");
        EnsureFont(page, "Helvetica-Bold", "F2");
        EnsureFont(page, "Helvetica-Oblique", "F3");
        EnsureFont(page, "Courier", "F4");
    }

    private static void EnsureFont(Page page, string baseFontName, string resName)
    {
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary;
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName))
        {
            var font = new Core.PdfDictionary();
            font.Set("Type", new Core.PdfName("Font"));
            font.Set("Subtype", new Core.PdfName("Type1"));
            font.Set("BaseFont", new Core.PdfName(baseFontName));
            font.Set("Encoding", new Core.PdfName("WinAnsiEncoding"));
            fontDict.Set(resName, font);
        }
    }
}
