using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Recursively map one HTML node (and its subtree) onto structure elements
    /// appended under <paramref name="parent"/>, per the mapping in
    /// <see cref="BuildLogicalStructure(Document, string)"/>.</summary>
    private static void EmitStructureElement(HtmlNode node,
        Aspose.Pdf.LogicalStructure.StructureElement parent, Tagged.ITaggedContent tc)
    {
        switch (node.Tag)
        {
            case "div":
            {
                var d = tc.CreateDivElement();
                parent.AppendChild(d);
                foreach (var c in node.Children) EmitStructureElement(c, d, tc);
                break;
            }
            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                parent.AppendChild(tc.CreateHeaderElement(node.Tag[1] - '0'));
                break;
            case "p":
                parent.AppendChild(tc.CreateParagraphElement());
                break;
            case "ul": case "ol":
            {
                var l = tc.CreateListElement();
                parent.AppendChild(l);
                foreach (var c in node.Children) EmitStructureElement(c, l, tc);
                break;
            }
            case "li":
            {
                // A list item expands to LI → { Lbl (bullet/label), [Link], LBody (text) };
                // its inline children are represented by these, not walked further.
                var li = tc.CreateListLIElement();
                parent.AppendChild(li);
                li.AppendChild(tc.CreateListLblElement());
                if (HasDescendant(node, "a"))
                    li.AppendChild(tc.CreateLinkElement());
                li.AppendChild(tc.CreateListLBodyElement());
                break;
            }
            case "img":
            {
                var fig = tc.CreateFigureElement();
                if (node.Attrs is not null && node.Attrs.TryGetValue("alt", out var alt)
                    && !string.IsNullOrEmpty(alt))
                    fig.AlternativeText = alt;
                parent.AppendChild(fig);
                break;
            }
            case "input":
            {
                // Each rendered interactive control becomes a Form structure element
                // (wrapping the widget's object reference). A type="hidden" input has no
                // widget and produces nothing.
                var type = node.Attrs is not null && node.Attrs.TryGetValue("type", out var ty)
                    ? ty.Trim().ToLowerInvariant() : "";
                if (type != "hidden")
                    parent.AppendChild(tc.CreateFormElement());
                break;
            }
            case "textarea":
            case "select":
            case "button":
                parent.AppendChild(tc.CreateFormElement());
                break;
            case "b": case "strong": case "u": case "i": case "em":
            {
                // Inline emphasis inside a TABLE CELL becomes a Span element — the
                // cell's content model is structured per run. Free-flow emphasis
                // (and empty icon elements) melts into its paragraph's text and
                // produces no element of its own.
                var inCell = false;
                for (var a = node.Parent; a is not null; a = a.Parent)
                    if (a.Tag is "td" or "th") { inCell = true; break; }
                var hasText = !string.IsNullOrWhiteSpace(node.Text);
                if (!hasText)
                    foreach (var dnode in node.Descendants())
                        if (!string.IsNullOrWhiteSpace(dnode.Text)) { hasText = true; break; }
                if (!inCell || !hasText) break;
                var sp = tc.CreateSpanElement();
                parent.AppendChild(sp);
                foreach (var c in node.Children) EmitStructureElement(c, sp, tc);
                break;
            }
            case "table":
            {
                var tbl = tc.CreateTableElement();
                parent.AppendChild(tbl);
                foreach (var c in node.Children) EmitStructureElement(c, tbl, tc);
                break;
            }
            case "thead":
            {
                var th = tc.CreateTableTHeadElement();
                parent.AppendChild(th);
                foreach (var c in node.Children) EmitStructureElement(c, th, tc);
                break;
            }
            case "tbody":
            {
                var tb = tc.CreateTableTBodyElement();
                parent.AppendChild(tb);
                foreach (var c in node.Children) EmitStructureElement(c, tb, tc);
                break;
            }
            case "tfoot":
            {
                var tf = tc.CreateTableTFootElement();
                parent.AppendChild(tf);
                foreach (var c in node.Children) EmitStructureElement(c, tf, tc);
                break;
            }
            case "tr":
            {
                var tr = tc.CreateTableTRElement();
                parent.AppendChild(tr);
                foreach (var c in node.Children) EmitStructureElement(c, tr, tc);
                break;
            }
            case "th":
            {
                var thc = tc.CreateTableTHElement();
                parent.AppendChild(thc);
                foreach (var c in node.Children) EmitStructureElement(c, thc, tc);
                break;
            }
            case "td":
            {
                var td = tc.CreateTableTDElement();
                parent.AppendChild(td);
                foreach (var c in node.Children) EmitStructureElement(c, td, tc);
                break;
            }
            // Transparent wrappers: descend without emitting an element of their own.
            case "html": case "body": case "#root": case "section": case "article": case "main":
                foreach (var c in node.Children) EmitStructureElement(c, parent, tc);
                break;
            // Everything else (inline runs, text nodes, br, label, span, a-in-flow, select,
            // button, …) produces no structure element of its own, but may CONTAIN block,
            // table or form descendants (e.g. an <input> wrapped in a <label> or <span>), so
            // descend into it without emitting.
            default:
                foreach (var c in node.Children) EmitStructureElement(c, parent, tc);
                break;
        }
    }

    private static bool HasDescendant(HtmlNode node, string tag)
    {
        foreach (var d in node.Descendants())
            if (d.Tag == tag) return true;
        return false;
    }

    /// <summary>Register an embedded-font indirect reference under <paramref name="resName"/>
    /// in a page's /Resources/Font (resolving indirect Resources/Font so the originals
    /// aren't replaced); idempotent per page.</summary>
    private static void RegisterPageFont(Page page, string resName, Core.PdfIndirectRef fontRef)
    {
        var reader = page.Reader;
        var resources = page.Dict.Get("Resources") as Core.PdfDictionary
            ?? reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new Core.PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as Core.PdfDictionary
            ?? reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new Core.PdfDictionary();
            resources.Set("Font", fontDict);
        }
        if (!fontDict.ContainsKey(resName)) fontDict.Set(resName, fontRef);
    }

    /// <summary>Build one <see cref="Forms.RadioButtonField"/> per HTML radio group (keyed by
    /// the input `name`; unnamed radios each form their own group) from the options collected
    /// during layout. Each option becomes a circle-styled <see cref="Forms.RadioButtonOptionField"/>
    /// kid with a visible border, so after save+reload it surfaces on Form.Fields.</summary>
    private static void EmitRadioGroups(Document doc,
        List<(string group, bool chk, Page page, Rectangle rect)> options)
    {
        if (options.Count == 0) return;
        var groups = new List<(string key, List<(bool chk, Page page, Rectangle rect)> opts)>();
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        var anon = 0;
        foreach (var (g, chk, page, rect) in options)
        {
            var key = string.IsNullOrEmpty(g) ? "__radio" + anon++ : g;
            if (!index.TryGetValue(key, out var gi))
            {
                gi = groups.Count; index[key] = gi;
                groups.Add((key, new List<(bool, Page, Rectangle)>()));
            }
            groups[gi].opts.Add((chk, page, rect));
        }

        foreach (var (key, opts) in groups)
        {
            try
            {
                var firstPage = opts[0].page;
                var rbf = new Forms.RadioButtonField(firstPage);
                var oi = 0;
                foreach (var (chk, page, rect) in opts)
                {
                    var opt = new Forms.RadioButtonOptionField(page, rect)
                    {
                        Style = Forms.BoxStyle.Circle,
                        OptionName = key + "_" + oi++,
                    };
                    opt.Characteristics.Border = System.Drawing.Color.Black;
                    rbf.Add(opt);
                }
                doc.Form.Add(rbf, firstPage.Number);
            }
            catch { /* best-effort radio emission */ }
        }
    }

    /// <summary>Remove /Font entries on each page that no content stream references via a
    /// "/Name … Tf" operator. Only provably-unused fonts are dropped (rendering unchanged).</summary>
    private static void PruneUnusedFonts(Document doc)
    {
        // Every page of one conversion shares a single /Font dictionary (EnsureFonts),
        // so the used-name set must be the UNION across all pages sharing that dict —
        // pruning per page would let a page with no text wipe the fonts of the others.
        var usedByDict = new Dictionary<Core.PdfDictionary, HashSet<string>>(ReferenceEqualityComparer.Instance);
        foreach (var page in doc.Pages)
        {
            var reader = page.Reader;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            var fontDict = resources is null ? null : reader.ResolveDict(resources.Get("Font"));
            if (fontDict is null) continue;

            if (!usedByDict.TryGetValue(fontDict, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByDict[fontDict] = used;
            }
            var content = page.GetContentStreamBytes();
            if (content is null) continue;
            var text = Encoding.ASCII.GetString(content);
            foreach (Match m in Regex.Matches(text, @"/([A-Za-z0-9.+\-]+)\s+[-\d.]+\s+Tf"))
                used.Add(m.Groups[1].Value);
        }

        foreach (var (fontDict, used) in usedByDict)
            foreach (var key in new List<string>(fontDict.Keys))
                if (!used.Contains(key)) fontDict.Remove(key);
    }

    /// <summary>A JSON-escaped export writes every attribute quote as <c>\"</c>, so a value
    /// arrives still wrapped in them. A value that is READ — a control's type, its size /
    /// cols / rows — must lose the wrappers; a value that is DRAWN keeps them:
    /// <c>\"Simon\"</c> typesets literally.</summary>
    private static string UnescapeAttrValue(string? v)
    {
        var s = v ?? "";
        return s.Length >= 4 && s.StartsWith("\\\"", StringComparison.Ordinal)
            && s.EndsWith("\\\"", StringComparison.Ordinal)
            ? s.Substring(2, s.Length - 4)
            : s;
    }

    // A control with no usable CSS size still occupies its INTRINSIC box — the character
    // grid its size/cols attribute declares (20 columns by default), one row per `rows`:
    // 5.852 pt per column, a first row of 15.75 pt (21 px) and
    // 11.25 pt for each further row; a textarea's box is one column wider than its `cols`
    // (the scrollbar gutter).
    private const double ControlColWidthPt = 5.852;

    private const double ControlFirstRowPt = 15.75;

    private const double ControlNextRowPt = 11.25;

    // …and it ADVANCES the flow by less than it draws: the box hangs 1.5 pt over the line
    // that follows it, so a label/control pair costs 13.5 + 14.25, not 13.5 + 15.75.
    private const double ControlFirstRowAdvancePt = 14.25;

    // Where the control box sits on its line: the top edge rides above the text
    // baseline, so the box straddles the line rather than hanging under it.
    private const double InputBoxAboveBaselinePt = 11.34;

    private const double SelectBoxAboveBaselinePt = 11.55;

    private const double SelectBoxHeightPt = 16.17;

    // A combo box is as wide as its WIDEST option (10 pt UI face) plus the dropdown
    // arrow and padding; the selected text alone does not size it.
    private const double SelectChromePt = 18.25;

    // …and it keeps a hairline side bearing on both sides of the pen.
    private const double SelectSideBearingPt = 0.25;

    // A control line that also carries body text advances a touch further than a
    // control alone: the text descent clears the box bottom.
    private const double InlineMixedExtraPt = 0.2;

    // The escaped-attr dialect's UA base font size (16 px = 12 pt).
    private const double EscapedBodyFontPt = 12;

    // The UA default body margin: 8px at 0.75 pt/px. A body-level element's box
    // sits this far inside the page content origin on both axes.
    private const double UaBodyMarginPt = 6.0;

    // A top-level table opening after a body text line sits this far below
    // the line's box (measured: table top 43.0 = line bottom 39.5
    // + 3.5, identical in the shipped template and the current render).
    private const double TableAfterTextGapPt = 3.5;

    // Gap between a UA list marker's right edge and the item's text indent,
    // in em of the item's font (probed: 4.5 at 12pt, 9 at 24pt — bullet pen
    // 117.3 = 126 − 4.2 advance − 4.5 at 12pt).
    private const double UaMarkerGapEm = 0.375;

    // Default cell chrome of a chrome-less table: border-spacing 2px +
    // cellpadding 1px = 3px at 0.75 pt/px. A single-column wrapper table's
    // flowed content sits this far inside the content origin on both axes
    // (reference: text x 98.25 = 96 + 2.25, first line top 80.25 = 78 + 2.25).
    private const double UaCellChromePt = 2.25;

    // The source renderer's UA block margin (p/ul/…): 1.12 em of the element's
    // font (probed: nested-list offsets 7.44/9.72/14.16/20.88 = 1.12·fs − the
    // 6 pt body margin across 12/14/18/24 pt, and the mid-flow p↔ul gap 13.44).
    private const double UaBlockMarginEm = 1.12;

    // Fieldset chrome (probed on the worksheet reference): content sits 9.75 pt
    // inside the frame (margin 2px + border + 0.75em padding), the frame's
    // right pad is 8.25, and its box closes 12.82 under the last baseline.
    private const double FsPadLeftPt = 9.75;
    private const double FsPadRightPt = 8.25;
    private const double FsBoxBottomPadPt = 12.82;
    private const double FsWidenRightPt = 90.75;      // frame right edge → page edge
    private const double FsFrameGray = 0.502;         // the UA fieldset border ink
    // Frame top below a leading legend's LINE TOP: the legend's 14.4pt baseline
    // drop (13.11) + the probed 4.86 baseline→border seat.
    private const double FsLegendFrameAdjPt = 17.97;

    // Room a line needs under its baseline at the page bottom — the serif descent
    // (a line may keep its baseline as little as 2.7 pt over the margin).
    private const double SerifDescentRoomPt = 2.7;

    // A line carrying an inline broken image grows its box by the icon: its baseline
    // lands this much lower than a bare text line (rule → icon-label baseline 41.12,
    // bare 11.9, both measured).
    private const double InlineIconLineExtraPt = 29.2;

    // The first line under a section rule sits 17.9 (text) / 17.2 (inline run) below
    // it, not the bare 11.9 — headings carry their own margins instead.
    private const double RuleToTextExtraPt = 6.0;

    private const double RuleToRunExtraPt = 5.3;

    // A mid-line textarea anchors its box BOTTOM this far under the baseline and
    // grows upward.
    private const double TextareaBottomHangPt = 0.75;

    // The multiline pitch that seats a textarea's first value line 10.11 under the
    // box top (2 pt inset + the Courier ascent).
    private const double TextareaValuePitchPt = 8.11;

    // Push-button chrome: caption width + 10.4, 18.75 tall (11.5×7.5 when empty),
    // caption 5.75 in from the left edge with its baseline 12.84 under the top.
    private const double ButtonChromeWPt = 10.4;

    private const double ButtonHeightPt = 18.75;

    private const double EmptyButtonWPt = 11.5;

    private const double EmptyButtonHPt = 7.5;

    private const double ButtonCaptionInsetXPt = 5.75;

    private const double ButtonCaptionDropPt = 12.84;

    /// <summary>Advance of a run in a Standard-14 face (AFM widths).</summary>
    private static double MeasureStd14(string baseFont, string s, double pt)
    {
        double total = 0;
        foreach (var ch in s) total += Text.Standard14Fonts.GetWidth(baseFont, ch);
        return total / 1000.0 * pt;
    }

    private static (double w, double h, double adv) IntrinsicControlBox(
        Dictionary<string, string>? attrs, bool multiline)
    {
        int Attr(string n, int dflt) =>
            attrs is not null && attrs.TryGetValue(n, out var raw)
            && int.TryParse(UnescapeAttrValue(raw), out var v) && v > 0 ? v : dflt;
        var cols = multiline ? Attr("cols", 20) + 1 : Attr("size", 20);
        var rows = multiline ? Attr("rows", 2) : 1;
        var extra = (rows - 1) * ControlNextRowPt;
        return (cols * ControlColWidthPt, ControlFirstRowPt + extra,
                ControlFirstRowAdvancePt + extra);
    }

    /// <summary>Build a Block describing an <input> control: its value and any CSS
    /// width/height, so layout can emit a TextBoxField of the right size.</summary>
    private static Block BuildInputBlock(Dictionary<string, string>? attrs, BlockStyle style,
        bool controlBoxes = false, bool multiline = false, string? innerText = null)
    {
        string? value = null, styleAttr = null, name = null, id = null;
        attrs?.TryGetValue("value", out value);
        attrs?.TryGetValue("style", out styleAttr);
        attrs?.TryGetValue("name", out name);
        attrs?.TryGetValue("id", out id);
        var (w, h) = ParseInputSize(styleAttr);
        double advance = 0;
        if (controlBoxes)
        {
            var (iw, ih, iadv) = IntrinsicControlBox(attrs, multiline);
            if (w <= 0) w = iw;
            if (h <= 0) h = ih;
            advance = iadv;
        }
        if (multiline && !string.IsNullOrEmpty(innerText)) value = innerText;
        // A disabled or readonly input maps to a ReadOnly AcroForm field.
        var readOnly = attrs is not null && (attrs.ContainsKey("disabled") || attrs.ContainsKey("readonly"));
        // AcroForm field name: prefer the HTML name attribute, fall back to id.
        var fieldName = !string.IsNullOrEmpty(name) ? name : id;
        return new Block
        {
            IsInputField = true,
            InputValue = DecodeEntities(value ?? ""),
            InputName = string.IsNullOrEmpty(fieldName) ? null : fieldName,
            InputWidth = w,
            InputHeight = h,
            InputMultiline = multiline,
            InputReadOnly = readOnly,
            // A control the flow draws as a box shows its own value inside it: a text
            // input in the UI face, a textarea in the typewriter face.
            InputDrawValue = controlBoxes,
            InputValueMono = multiline,
            InputAdvance = advance,
            FontSize = style.FontSize,
            FontRes = style.FontRes,
            LeftIndent = style.LeftIndent,
            // The intrinsic box carries its own leading in the advance; the legacy
            // 1/2 pt padding is the fallback path's calibration, not this one's.
            MarginTop = controlBoxes ? 0 : 1,
            MarginBottom = controlBoxes ? 0 : 2,
        };
    }

    /// <summary>Read width:/height: pixel lengths from an inline style string.</summary>
    private static (double w, double h) ParseInputSize(string? styleAttr)
    {
        double w = 0, h = 0;
        if (string.IsNullOrEmpty(styleAttr)) return (w, h);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var wm = Regex.Match(styleAttr, @"(?:^|[;\s])width\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out w);
        var hm = Regex.Match(styleAttr, @"(?:^|[;\s])height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
        if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float, ci, out h);
        return (w, h);
    }

    /// <summary>Turn HTML into a list of Block records. The parser is a
    /// small hand-rolled tokeniser (no external DOM): it tracks the stack
    /// of open block elements to decide font + margins for each text run.</summary>
    /// <summary>Resolve a container-chrome CSS length (px/pt/rem/em; for a border
    /// shorthand, its first length term) to POINTS. rem/em resolve at the 16px
    /// root — this feeds class-rule chrome on structural divs, which carry no
    /// authored font size of their own in these documents.</summary>
    private static double BoxChromeLen(string value)
    {
        var m = Regex.Match(value, @"([\d.]+)\s*(px|pt|rem|em)", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var n)) return 0;
        return m.Groups[2].Value.ToLowerInvariant() switch
        {
            "pt" => n,
            "px" => n * 0.75,
            _ => n * 16 * 0.75,
        };
    }

    private static List<Block> ParseBlocks(string html,
        IReadOnlyDictionary<string, Dictionary<string, string>>? css,
        IReadOnlyList<BeforeMarker>? beforeMarkers = null,
        IReadOnlyList<Block>? rowBlocks = null,
        bool metricLayout = false,
        bool uaDefaults = false,
        bool browserUa = false,
        double bodyFontSize = 0,
        bool bandDialect = false,
        bool formDialect = false,
        bool brBlankLines = false,
        bool uaBlockRhythm = false,
        bool controlBoxes = false,
        bool inlineEmphasisRuns = false,
        bool articleRhythm = false,
        bool bodyBoxRhythm = false,
        bool containerBoxIndents = false,
        bool coverStyles = false,
        bool inlineBlockCols = false,
        bool absSpanLedger = false,
        bool spanClassTypography = false,
        bool fieldsetBoxes = false,
        bool uaPMargins = false,
        bool msoParagraphs = false)
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
        // XML processing instructions (<?xml …?> prolog of an XHTML file) are markup,
        // not text — a browser never renders them.
        html = Regex.Replace(html, @"<\?[\s\S]*?\?>", "");
        // Strip leading BOM if present — UTF-8 HTMLs often ship with one.
        if (html.Length > 0 && html[0] == '\uFEFF') html = html.Substring(1);
        // Decode entities once at the text layer.
        var tokens = Tokenize(html);

        var blocks = new List<Block>();
        var currentText = new StringBuilder();
        var styleStack = new Stack<BlockStyle>();
        styleStack.Push(new BlockStyle
        {
            // UA base = 16px serif (12pt); a caller-set body size replaces the default.
            FontSize = bodyFontSize > 0 ? bodyFontSize : uaDefaults ? 12 : 11,
            FontRes = "F1", MarginTop = 0, MarginBottom = 0, LeftIndent = 0,
            FormDialect = formDialect,
            ArticleRhythm = articleRhythm,
            UaSerif = browserUa,
        });
        // Inline <a href> spans accumulated for the block currently being built, in
        // currentText (raw, pre-collapse) coordinates. Flushed (and translated to the
        // collapsed Text's coordinates) when the block is emitted.
        var rawAnchors = new List<(int start, int end, string url)>();
        var openAnchors = new Stack<(int start, string url)>();
        // Anchor-target names (id / <a name>) seen since the last flush; attached to
        // the block being emitted so #fragment links can resolve to its page. If the
        // block is empty they carry forward to the next non-empty block.
        var pendingAnchorNames = new List<string>();
        // A list-item marker ("5." / "•") set when an <li> opens; attaches to the FIRST
        // non-empty block emitted inside that <li> (its text may be nested in child divs,
        // e.g. EditorJS markup), then clears so only the item's first line is marked.
        string? pendingMarker = null;
        // UA-serif flow <font> scoping: each open saves the enclosing style's
        // typography so the matching close restores it.
        var uaFontSaves = new Stack<(double Fs, Color? Fore, string? Fam)>();
        // True when pendingMarker is CSS ::before generated content on an RTL list: it renders
        // after the item text (to its right) rather than before, so the item text is the earlier
        // fragment on the line.
        bool pendingMarkerAfter = false;
        // Open `display:inline` divs (styled-article) — their closes must not pop.
        var inlineDivDepth = 0;
        // Span nesting depth, and the depths at which `display:block` spans opened
        // (class-rule block-spans break their line at open AND close; metric flow).
        var spanDepth = 0;
        var blockSpanDepths = new Stack<int>();
        // Ledger column state: a position:absolute+left span class opened — its
        // text flushes as its OWN block at the column x when the span closes.
        double absSpanLeftPt = -1;
        int absSpanLabelIdx = -1;
        // Browser-UA flow: an EMPTY paragraph (a self-closed <p/> or a stray
        // </p> with no open <p> — both quirks-parse as an empty p element)
        // contributes its UA margin to the next block by max-collapse.
        double pendingEmptyPMarginPt = 0;
        int pOpenDepth = 0;
        // fieldsetBoxes state: the open <legend>'s saved typography.
        var fsLegendSave = (0.0, "F1");
        var fsInLegend = false;
        // Inline-block title columns (quirks CSS-run docs): a span whose class rule
        // declares display:inline-block with a width is a TITLE column — its text
        // becomes its own run and the text that follows seats at the column's
        // right edge on the same line. State: the open column's width + span depth,
        // the pending indent for the value run, and a keep-trailing-space marker
        // for the flush a <br> triggers (a collapsed newline before the <br>
        // survives as the fragment's trailing space).
        double openTitleColW = 0;
        int titleColSpanDepth = -1;
        double pendingColIndent = 0;
        bool keepTrailingSpace = false;
        // Container box chrome (containerBoxIndents mode): the vertical border+padding
        // of divs opened since the last content block lands on the NEXT block's top
        // margin (the card's chrome above its first line), and a class-rule HEIGHT on
        // a container (the widget header band) floors that block's height.
        double pendingBoxPadTop = 0;
        double pendingBoxHeight = 0;
        // Border-only declared box (browser-UA flow): a block element with inline
        // width+height+border and no background strokes its declared box while its
        // content flows INSIDE it — the box travels to the first block that flushes
        // within the element (usually a bare wrapper child's text), and to a
        // text-less spacer at the element's close if nothing flushed.
        (double w, double h, double bw, Color c, double r)? pendingBorderBox = null;
        int pendingBorderBoxDepth = 0;
        // True between <textarea> and </textarea>: the element becomes an AcroForm field,
        // so its inner text is the field's default value, not body content — suppress it.
        bool inTextarea = false;
        // Inside a <select>: its <option> list is the control's VALUE SET, not flow content
        // — a closed dropdown shows exactly one entry. The chosen one is captured here and
        // drawn where the control sits when the tag closes.
        bool inSelect = false, inSelectedOption = false;
        Block? textareaBlock = null;
        var textareaText = new StringBuilder();
        var selectedText = new StringBuilder();
        // Control-box dialect: every option's text is kept — the combo box is sized
        // by its widest entry — and the select's name carries to the AcroForm field.
        var selectOptions = new List<string>();
        var curOptionText = new StringBuilder();
        string? selectName = null;
        // Inline-run bookkeeping (control-box dialect): a run opens at the first
        // control after a block boundary; text flushed while it is open joins it, and
        // any block boundary closes it. runPrevWasControl preserves the single
        // collapsed space between a control and the label text that follows it.
        int inlineRunId = 0, nextInlineRunId = 1;
        bool runPrevWasControl = false;
        // Control-box dialect: an <i>/<em> that closes without enclosing any text (an
        // icon placeholder) must not leave the whole rest of its block italic.
        int italicOpenTextLen = -1;
        // Between <button> and </button>: the inner text is the push-button's caption,
        // not flow content.
        bool inButton = false;
        var buttonText = new StringBuilder();
        // A mid-line broken <img> waiting to ride the end of the text block the
        // pending run flushes into (control-box dialect).
        bool pendingInlineIcon = false;
        // A page-break-before seen on an element that emitted no block of its own; the
        // next emitted block (text or image) starts the fresh page instead.
        bool pendingPageBreak = false;
        // Suppression of display:none / visibility:hidden subtrees: while hiddenTag is
        // set, every token is dropped until the matching close tag (same-name depth
        // count) is reached. Hidden content is not part of the rendering — no text,
        // no fields, no reserved space.
        string? hiddenTag = null;
        int hiddenDepth = 0;
        // <center> nesting depth — content inside is horizontally centered.
        int centerDepth = 0;
        // Inline <b>/<strong> run tracking (browser-UA flow and the in-page
        // HtmlFragment flow): raw-coordinate ranges over currentText, re-mapped to
        // collapsed coordinates at Flush.
        var rawBolds = new List<(int start, int end)>();
        int inlineBoldDepth = 0;
        int inlineBoldStart = -1;
        // The same bookkeeping for <u>: an underlined run inside the block's line.
        var rawUnders = new List<(int start, int end)>();
        // Spans whose inline style opened an underline run (text-decoration:
        // underline) — keyed by span depth so the matching </span> closes it.
        var uaUnderSpanDepths = new Stack<int>();
        int inlineUnderDepth = 0;
        int inlineUnderStart = -1;
        // And for <i>/<em>: an italic run inside the block's line (browser-UA flow).
        var rawItalics = new List<(int start, int end)>();
        int inlineItalicDepth = 0;
        int inlineItalicStart = -1;
        // Both flows track bold RANGES; only the browser-UA flow suppresses the
        // whole-block face promotion that <b> otherwise performs.
        var trackBoldRuns = browserUa || inlineEmphasisRuns;
        // Open block elements' class attributes (pushed per BlockTags open) — lets a
        // descendant rule like ".blueh4 h4 { border-bottom: … }" resolve its ancestor.
        var divClassStack = new List<string>();

        void Flush(bool _unused, BlockStyle styleUsed)
        {
            // An <a> still open at the flush boundary covers text up to here in THIS block.
            foreach (var oa in openAnchors)
                rawAnchors.Add((oa.start, currentText.Length, oa.url));
            // A bold run still open at the flush boundary covers text up to here and
            // re-opens at the start of the next block.
            if (inlineBoldDepth > 0 && currentText.Length > inlineBoldStart && inlineBoldStart >= 0)
                rawBolds.Add((inlineBoldStart, currentText.Length));
            if (inlineBoldDepth > 0) inlineBoldStart = 0;
            if (inlineUnderDepth > 0 && currentText.Length > inlineUnderStart && inlineUnderStart >= 0)
                rawUnders.Add((inlineUnderStart, currentText.Length));
            if (inlineUnderDepth > 0) inlineUnderStart = 0;
            if (inlineItalicDepth > 0 && currentText.Length > inlineItalicStart && inlineItalicStart >= 0)
                rawItalics.Add((inlineItalicStart, currentText.Length));
            if (inlineItalicDepth > 0) inlineItalicStart = 0;
            var raw = currentText.ToString();
            // Collapse runs of *ASCII* whitespace only — U+00A0 (from
            // &nbsp;) is intentional visual content and must survive
            // collapse+Trim so an &nbsp;-only <p> still emits a line.
            // CollapseWhitespaceWithMap reproduces that collapse+Trim while tracking,
            // for each output char, the raw index it came from — so inline anchor
            // spans can be re-expressed in the collapsed Text's coordinates.
            var (collapsed, rawOf) = CollapseWhitespaceWithMap(raw);
            // Text continuing an inline run after a control keeps the one collapsed
            // space the markup put between them — " State: " draws
            // with its leading space right at the control's edge.
            if (controlBoxes && inlineRunId != 0 && runPrevWasControl
                && collapsed.Length > 0 && raw.Length > 0 && char.IsWhiteSpace(raw[0]))
            {
                collapsed = " " + collapsed;
                rawOf.Insert(0, -1);
            }
            // A collapsed newline before a <br> survives as the fragment's trailing
            // space (quirks CSS-run docs — the flush the <br> triggers sets the
            // marker; asserted fragment values carry it).
            if (keepTrailingSpace && collapsed.Length > 0 && raw.Length > 0
                && char.IsWhiteSpace(raw[^1]) && collapsed[^1] != ' ')
            {
                collapsed += " ";
                rawOf.Add(raw.Length - 1);
            }
            // font-size:0 spacer (the float-terminator "clear:both;height:0;
            // font-size:0" idiom): its &nbsp; occupies a zero-height line box —
            // emit nothing rather than a default-size blank line.
            if (styleUsed.ZeroFontSize && collapsed.Trim(' ', ' ').Length == 0)
            {
                currentText.Clear();
                rawAnchors.Clear();
                rawBolds.Clear();
                rawUnders.Clear();
                rawItalics.Clear();
                return;
            }
            if (collapsed.Length > 0)
            {
                var blk = new Block
                {
                    Text = collapsed,
                    FontSize = styleUsed.FontSize,
                    FontRes = styleUsed.FontRes,
                    FontFamily = styleUsed.FontFamily,
                    ForeColor = styleUsed.ForeColor,
                    LegacyFontPt = styleUsed.LegacyFontPt,
                    LegacyFontSized = styleUsed.LegacyFontSized,
                    EmBold = styleUsed.EmBold,
                    EmItalic = styleUsed.EmItalic,
                    MarginTop = styleUsed.MarginTop,
                    MarginBottom = styleUsed.MarginBottom,
                    MarginTopAlways = styleUsed.MarginTopAlways,
                    MarginTopAuthored = styleUsed.MarginTopAuthored,
                    LeftIndent = styleUsed.LeftIndent,
                    IsListItem = styleUsed.IsListItem,
                    PageBreakBefore = styleUsed.PageBreakBefore || pendingPageBreak,
                    ExplicitHeight = styleUsed.ExplicitHeight,
                    LineFactor = styleUsed.LineFactor,
                    BackgroundColor = styleUsed.BackgroundColor,
                    BgBoxWidthPt = styleUsed.BgBoxWidthPt,
                    BgBoxHeightPt = styleUsed.BgBoxHeightPt,
                    BorderColor = styleUsed.BorderColor,
                    BorderTopOnly = styleUsed.BorderTopOnly,
                    BorderWidth = styleUsed.BorderWidth,
                    LineBoxPt = styleUsed.LineBoxPt,
                    TextInsetPt = styleUsed.TextInsetPt,
                    AlignCenter = styleUsed.AlignCenter,
                    AlignCenterCss = styleUsed.AlignCenterCss,
                    AlignJustify = styleUsed.AlignJustify,
                    AlignCenterAttr = styleUsed.AlignCenterAttr,
                    WidthFrac = styleUsed.WidthFrac,
                    WidthPx = styleUsed.WidthPx,
                    PadTop = styleUsed.PadTop,
                    AlignRight = styleUsed.AlignRight,
                    BandColor = styleUsed.BandColor,
                    BandPx = styleUsed.BandPx,
                    BandPadPx = styleUsed.BandPadPx,
                    FloatLeft = styleUsed.FloatLeft,
                    FloatRight = styleUsed.FloatRight,
                };
                // An empty paragraph's margin max-collapses onto this block.
                if (pendingEmptyPMarginPt > 0)
                {
                    blk.MarginTop = Math.Max(blk.MarginTop, pendingEmptyPMarginPt);
                    pendingEmptyPMarginPt = 0;
                }
                // The div's padding-top spaces only its FIRST flushed block;
                // a heading band draws once, under the first flushed block.
                styleUsed.PadTop = 0;
                styleUsed.BandColor = null;
                // The run following a title column seats at the column's right edge
                // on the SAME line (the title block gave its row back).
                if (pendingColIndent > 0 && !blk.IsHardBreak)
                {
                    blk.LeftIndent += pendingColIndent;
                    pendingColIndent = 0;
                }
                // A painted box (background tile × declared size) fills once, on
                // the element's first flushed block; its fill dies with it.
                if (styleUsed.BgBoxHeightPt > 0)
                {
                    styleUsed.BgBoxWidthPt = 0;
                    styleUsed.BgBoxHeightPt = 0;
                    styleUsed.BackgroundColor = null;
                }
                // Container chrome above this block, and a container's class-rule
                // height flooring it (containerBoxIndents mode) — both one-shot.
                if (pendingBoxPadTop > 0)
                {
                    blk.MarginTop += pendingBoxPadTop;
                    blk.MarginTopAlways = true;
                    pendingBoxPadTop = 0;
                }
                if (pendingBoxHeight > 0)
                {
                    blk.ExplicitHeight = Math.Max(blk.ExplicitHeight, pendingBoxHeight);
                    blk.BandBoxHeight = true;
                    pendingBoxHeight = 0;
                }
                // The border-only declared box lands on the element's first flushed
                // block: the box strokes at that block's top and the content height
                // reserves the flow below its lines.
                if (pendingBorderBox is { } pbb)
                {
                    blk.BorderBoxWPt = pbb.w;
                    blk.BorderRadiusPt = pbb.r;
                    blk.BorderWidth = pbb.bw;
                    blk.BorderColor = pbb.c;
                    blk.ExplicitHeight = Math.Max(blk.ExplicitHeight, pbb.h);
                    pendingBorderBox = null;
                }
                // A block's margins belong to the BOX, not to each line a <br> splits
                // off inside it: the top margin is spent on the first flushed line and
                // the bottom margin is re-attached when the element closes.
                if (uaBlockRhythm)
                {
                    styleUsed.MarginTop = 0;
                    blk.MarginBottom = 0;
                }
                // Attach a pending list marker to this first content block of the <li>.
                if (pendingMarker is not null)
                {
                    // Styled-article: "• item" draws as ONE run
                    // starting AT the list indent — the marker rides the text,
                    // it does not hang left of it.
                    if (articleRhythm && !pendingMarkerAfter)
                        blk.Text = pendingMarker + " " + blk.Text;
                    else
                    {
                        blk.Marker = pendingMarker;
                        blk.MarkerAfter = pendingMarkerAfter;
                    }
                    pendingMarker = null;
                    pendingMarkerAfter = false;
                }
                if (rawAnchors.Count > 0)
                {
                    foreach (var (s, e, url) in rawAnchors)
                    {
                        if (string.IsNullOrEmpty(url)) continue;
                        int cs = -1, ce = -1;
                        for (int k = 0; k < rawOf.Count; k++)
                            if (rawOf[k] >= s && rawOf[k] < e) { if (cs < 0) cs = k; ce = k + 1; }
                        if (cs >= 0)
                            (blk.Anchors ??= new()).Add((cs, ce - cs, url));
                    }
                }
                // Re-express a raw-coordinate emphasis range in the collapsed Text's
                // coordinates: the chars the collapse kept whose source index falls
                // inside the range span [first, last].
                void MapRuns(List<(int start, int end)> src,
                    Func<System.Collections.Generic.List<(int Start, int Length)>> target)
                {
                    foreach (var (s, e) in src)
                    {
                        int cs = -1, ce = -1;
                        for (int k = 0; k < rawOf.Count; k++)
                            if (rawOf[k] >= s && rawOf[k] < e) { if (cs < 0) cs = k; ce = k + 1; }
                        if (cs >= 0) target().Add((cs, ce - cs));
                    }
                }
                if (rawBolds.Count > 0) MapRuns(rawBolds, () => blk.BoldRuns ??= new());
                if (rawUnders.Count > 0) MapRuns(rawUnders, () => blk.UnderlineRuns ??= new());
                if (rawItalics.Count > 0) MapRuns(rawItalics, () => blk.ItalicRuns ??= new());
                if (pendingAnchorNames.Count > 0)
                {
                    blk.AnchorNames = new List<string>(pendingAnchorNames);
                    pendingAnchorNames.Clear();
                }
                if (controlBoxes && inlineRunId != 0)
                {
                    blk.InlineRunId = inlineRunId;
                    runPrevWasControl = false;
                }
                if (pendingInlineIcon)
                {
                    blk.InlineIconAfter = true;
                    pendingInlineIcon = false;
                }
                blocks.Add(blk);
                pendingPageBreak = false;
            }
            else if (styleUsed.ExplicitHeight > 0)
            {
                // Empty block with explicit height (e.g. `<div style="height:50px">`
                // used as a visual separator bar). Emit a text-less spacer
                // so pagination sees the reserved vertical space — once: an inner
                // <br> flush and the element's close both land here otherwise.
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
                styleUsed.ExplicitHeight = 0;
            }
            // Empty block close-tags without explicit height do not emit a
            // spacer — nested empty containers (e.g. <div><div></div></div>)
            // would otherwise inflate page count well beyond the text
            // volume. Explicit vertical spacing comes from <br>, <hr>,
            // block margins, and any CSS height/min-height override.
            currentText.Clear();
            rawAnchors.Clear();
            rawBolds.Clear();
            rawUnders.Clear();
            rawItalics.Clear();
            // An <a> left open across a block/line boundary continues in the next
            // block; record what it covered here and re-anchor it at offset 0.
            if (openAnchors.Count > 0)
            {
                var carried = openAnchors.ToArray();
                openAnchors.Clear();
                for (int oi = carried.Length - 1; oi >= 0; oi--)
                    openAnchors.Push((0, carried[oi].url));
            }
        }

        foreach (var tok in tokens)
        {
            if (hiddenTag is not null)
            {
                if (tok.Kind == TokenKind.Tag && !tok.IsSelfClosing
                    && tok.Tag!.Equals(hiddenTag, StringComparison.OrdinalIgnoreCase))
                {
                    if (tok.IsClose) { if (--hiddenDepth == 0) hiddenTag = null; }
                    else hiddenDepth++;
                }
                continue;
            }
            if (tok.Kind == TokenKind.Text)
            {
                // Text inside a <textarea> is the field's value, not flow content.
                if (inTextarea || inSelect)
                {
                    if (inSelectedOption) selectedText.Append(DecodeEntities(tok.Value));
                    else if (inTextarea) textareaText.Append(DecodeEntities(tok.Value));
                    if (inSelect && !inTextarea) curOptionText.Append(DecodeEntities(tok.Value));
                    continue;
                }
                if (inButton) { buttonText.Append(DecodeEntities(tok.Value)); continue; }
                currentText.Append(DecodeEntities(tok.Value));
                continue;
            }
            var tag = tok.Tag!;
            if (SkipTags.Contains(tag)) continue;
            if (tag.Equals("rowmark", StringComparison.OrdinalIgnoreCase))
            {
                // Placeholder for a prebuilt styled-run row (see ExtractRowBlocks).
                if (!tok.IsClose && rowBlocks is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("i", out var riStr)
                    && int.TryParse(riStr, out var ri) && ri >= 0 && ri < rowBlocks.Count)
                {
                    Flush(false, styleStack.Peek());
                    blocks.Add(rowBlocks[ri]);
                }
                continue;
            }
            if (!tok.IsClose && IsHiddenElement(tag, tok.Attributes, css))
            {
                if (!tok.IsSelfClosing && !VoidTags.Contains(tag))
                {
                    hiddenTag = tag;
                    hiddenDepth = 1;
                }
                continue;
            }

            // fieldsetBoxes mode: a <fieldset> opens a bordered box — marker
            // blocks bracket its content (the frame draws at the close) and its
            // padding indents everything inside; a <legend> is its own bold
            // 1.2em block riding the frame's top edge.
            if (fieldsetBoxes && tag.Equals("fieldset", StringComparison.OrdinalIgnoreCase))
            {
                Flush(false, styleStack.Peek());
                blocks.Add(new Block { Text = "", IsHardBreak = true, FsBox = tok.IsClose ? -1 : 1 });
                // clamped at zero: the close tag often parses in a LATER segment
                // (its table split the parse) whose fresh root never saw the open
                styleStack.Peek().LeftIndent = tok.IsClose
                    ? Math.Max(0, styleStack.Peek().LeftIndent - FsPadLeftPt)
                    : styleStack.Peek().LeftIndent + FsPadLeftPt;
                continue;
            }
            if (fieldsetBoxes && tag.Equals("legend", StringComparison.OrdinalIgnoreCase))
            {
                Flush(false, styleStack.Peek());
                var lgdTop = styleStack.Peek();
                if (!tok.IsClose)
                {
                    fsLegendSave = (lgdTop.FontSize, lgdTop.FontRes);
                    var lgdFactor = 1.2;
                    if (css is not null && css.TryGetValue("legend", out var lgdRule)
                        && lgdRule.TryGetValue("font-size", out var lgdFs)
                        && Regex.Match(lgdFs, @"([\d.]+)\s*em") is { Success: true } lgdEm)
                        lgdFactor = double.Parse(lgdEm.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture);
                    lgdTop.FontSize *= lgdFactor;
                    lgdTop.FontRes = "F2";
                    fsInLegend = true;
                }
                else if (fsInLegend)
                {
                    if (blocks.Count > 0 && blocks[^1].Text.Length > 0)
                        blocks[^1].FsLegend = true;
                    (lgdTop.FontSize, lgdTop.FontRes) = fsLegendSave;
                    fsInLegend = false;
                }
                continue;
            }

            if (tok.IsClose)
            {
                // A stray </p> with no open <p> quirks-parses as an EMPTY
                // paragraph: it contributes its UA margin (max-collapsed onto
                // the next block) and must NOT pop the enclosing element's
                // style. Browser-UA flow only.
                if (browserUa && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    if (pOpenDepth > 0) pOpenDepth--;
                    else
                    {
                        Flush(false, styleStack.Peek());
                        // Word-filtered idiom: the stray </p> is an EMPTY
                        // MsoNormal paragraph — one full base-size line box
                        // (the sheet reset its margins to zero), not a
                        // collapsible UA margin.
                        if (msoParagraphs)
                            blocks.Add(new Block
                            {
                                Text = "", IsHardBreak = true, IsLineBreak = true,
                                FontSize = 12,
                            });
                        else
                            pendingEmptyPMarginPt = Math.Max(pendingEmptyPMarginPt,
                                UaBlockMarginEm * styleStack.Peek().FontSize);
                        continue;
                    }
                }
                // Metric flow: a closed body-level <p> leaves its UA 1.12 em
                // bottom margin to collapse onto whatever follows it — carried
                // both as the next block's pending top margin and on the flushed
                // block itself (a following TABLE reads only the latter).
                else if (metricLayout && uaPMargins && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    // only the flushed block's bottom margin — the NEXT
                    // paragraph's own open-margin covers text-to-text collapse;
                    // a following TABLE reads MarginBottom alone. The margin is
                    // 1.12 em of the p's INHERITED size (the block style's own
                    // FontSize carries the legacy flow default, not the CSS one).
                    var pMb = UaBlockMarginEm * (styleStack.Peek().ParentFontSize > 0
                        ? styleStack.Peek().ParentFontSize : styleStack.Peek().FontSize);
                    Flush(false, styleStack.Peek());
                    if (blocks.Count > 0)
                        blocks[^1].MarginBottom = Math.Max(blocks[^1].MarginBottom, pMb);
                }
                if (trackBoldRuns && tag.ToLowerInvariant() is "b" or "strong")
                {
                    if (inlineBoldDepth > 0 && --inlineBoldDepth == 0
                        && currentText.Length > inlineBoldStart)
                        rawBolds.Add((inlineBoldStart, currentText.Length));
                    // The browser-UA flow draws bold purely as a run; the in-page
                    // fragment flow keeps the historical whole-block promotion as
                    // its fallback, so let the close reach the generic handling.
                    if (browserUa) continue;
                }
                if ((inlineEmphasisRuns || browserUa)
                    && tag.Equals("u", StringComparison.OrdinalIgnoreCase))
                {
                    if (inlineUnderDepth > 0 && --inlineUnderDepth == 0
                        && currentText.Length > inlineUnderStart)
                        rawUnders.Add((inlineUnderStart, currentText.Length));
                    continue;
                }
                if (trackBoldRuns && tag.ToLowerInvariant() is "i" or "em")
                {
                    if (inlineItalicDepth > 0 && --inlineItalicDepth == 0
                        && currentText.Length > inlineItalicStart)
                        rawItalics.Add((inlineItalicStart, currentText.Length));
                    // Browser-UA flow draws italic purely as a run; other flows
                    // keep the historical whole-block promotion.
                    if (browserUa) continue;
                }
                if (tag.Equals("center", StringComparison.OrdinalIgnoreCase))
                {
                    Flush(false, styleStack.Peek());
                    if (centerDepth > 0) centerDepth--;
                    continue;
                }
                if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
                {
                    inTextarea = false;
                    if (textareaBlock is not null)
                        textareaBlock.InputValue = CollapseWs(textareaText.ToString());
                    textareaBlock = null; textareaText.Clear();
                    continue;
                }
                if (tag.Equals("select", StringComparison.OrdinalIgnoreCase))
                {
                    inSelect = false; inSelectedOption = false;
                    var chosen = CollapseWs(selectedText.ToString());
                    selectedText.Clear();
                    if (curOptionText.Length > 0)
                    {
                        selectOptions.Add(CollapseWs(curOptionText.ToString()));
                        curOptionText.Clear();
                    }
                    if (controlBoxes)
                    {
                        // The combo box occupies its own control box on the line: as
                        // wide as its widest option in the 10 pt UI face plus the
                        // dropdown chrome, the chosen entry typeset inside.
                        double maxOpt = 0;
                        foreach (var opt in selectOptions)
                        {
                            var ow = MeasureStd14("Helvetica", opt, 10);
                            if (ow > maxOpt) maxOpt = ow;
                        }
                        var st = styleStack.Peek();
                        blocks.Add(new Block
                        {
                            IsInputField = true,
                            IsSelectBox = true,
                            InputValue = chosen,
                            InputName = string.IsNullOrEmpty(selectName) ? null : selectName,
                            InputWidth = maxOpt + SelectChromePt,
                            InputHeight = SelectBoxHeightPt,
                            InputAdvance = ControlFirstRowAdvancePt,
                            InputDrawValue = true,
                            FontSize = st.FontSize,
                            FontRes = st.FontRes,
                            LeftIndent = st.LeftIndent,
                            InlineRunId = inlineRunId,
                        });
                        runPrevWasControl = true;
                    }
                    else if (chosen.Length > 0) { currentText.Append(chosen); Flush(true, styleStack.Peek()); }
                    selectOptions.Clear();
                    selectName = null;
                    continue;
                }
                if (tag.Equals("option", StringComparison.OrdinalIgnoreCase))
                {
                    if (curOptionText.Length > 0)
                    {
                        selectOptions.Add(CollapseWs(curOptionText.ToString()));
                        curOptionText.Clear();
                    }
                    inSelectedOption = false;
                    continue;
                }
                if (controlBoxes && tag.Equals("button", StringComparison.OrdinalIgnoreCase))
                {
                    inButton = false;
                    blocks.Add(new Block
                    {
                        IsButton = true,
                        ButtonCaption = CollapseWs(buttonText.ToString()),
                        FontSize = styleStack.Peek().FontSize,
                    });
                    buttonText.Clear();
                    continue;
                }
                if (tag.Equals("a", StringComparison.OrdinalIgnoreCase) && openAnchors.Count > 0)
                {
                    var (st, url) = openAnchors.Pop();
                    if (currentText.Length > st) rawAnchors.Add((st, currentText.Length, url));
                }
                // An empty <i>/<em> (icon placeholder) reverts its promotion — the
                // text that follows the close is not emphasised.
                if (controlBoxes && tag.ToLowerInvariant() is "i" or "em"
                    && italicOpenTextLen >= 0 && currentText.Length == italicOpenTextLen)
                {
                    var itop = styleStack.Peek();
                    if (itop.FontRes == "F3") itop.FontRes = "F1";
                    itop.EmItalic = false;
                    italicOpenTextLen = -1;
                }
                // A UA-flow </font> restores the typography its open saved —
                // flushing the styled run it closes first.
                if (browserUa && tag.Equals("font", StringComparison.OrdinalIgnoreCase)
                    && uaFontSaves.Count > 0)
                {
                    Flush(false, styleStack.Peek());
                    var (sFs, sFore, sFam) = uaFontSaves.Pop();
                    var fTop = styleStack.Peek();
                    fTop.FontSize = sFs;
                    fTop.ForeColor = sFore;
                    fTop.FontFamily = sFam;
                }
                // A block-span's close breaks its line like its open did.
                if (tag.Equals("span", StringComparison.OrdinalIgnoreCase))
                {
                    // Close an inline-style underline run opened by this span.
                    if (uaUnderSpanDepths.Count > 0 && uaUnderSpanDepths.Peek() == spanDepth)
                    {
                        uaUnderSpanDepths.Pop();
                        if (inlineUnderDepth > 0 && --inlineUnderDepth == 0
                            && currentText.Length > inlineUnderStart)
                            rawUnders.Add((inlineUnderStart, currentText.Length));
                    }
                    if (blockSpanDepths.Count > 0 && blockSpanDepths.Peek() == spanDepth)
                    {
                        blockSpanDepths.Pop();
                        Flush(false, styleStack.Peek());
                    }
                    // The ledger's absolute column closes: its text flushes as its
                    // own block at the column x. position:absolute anchors at the
                    // page margin box — one UA body margin OUTSIDE the flow's
                    // content origin (probed: label 96 + margin-left, value
                    // 90 + left, on one line).
                    if (absSpanLeftPt >= 0)
                    {
                        var lgTop = styleStack.Peek();
                        var lgSavLi = lgTop.LeftIndent;
                        var lgSavTi = lgTop.TextInsetPt;
                        var lgBefore = blocks.Count;
                        lgTop.LeftIndent = Math.Max(0, absSpanLeftPt - UaBodyMarginPt);
                        lgTop.TextInsetPt = 0;
                        Flush(false, lgTop);
                        lgTop.LeftIndent = lgSavLi;
                        lgTop.TextInsetPt = lgSavTi;
                        // An EMPTY column emitted nothing — the label must advance
                        // the row itself, or the next row overprints it.
                        if (blocks.Count == lgBefore && absSpanLabelIdx >= 0
                            && absSpanLabelIdx < blocks.Count)
                            blocks[absSpanLabelIdx].NoAdvanceY = false;
                        absSpanLeftPt = -1;
                        absSpanLabelIdx = -1;
                    }
                    // The title column closes: its text is its own run that gives the
                    // row back — the value that follows seats at the column's edge.
                    if (titleColSpanDepth == spanDepth && openTitleColW > 0)
                    {
                        Flush(false, styleStack.Peek());
                        if (blocks.Count > 0 && !blocks[^1].IsHardBreak
                            && blocks[^1].Text.Length > 0)
                        {
                            blocks[^1].NoAdvanceY = true;
                            pendingColIndent = openTitleColW;
                        }
                        openTitleColW = 0;
                        titleColSpanDepth = -1;
                    }
                    if (spanDepth > 0) spanDepth--;
                }
                // An inline div's close is as inline as its open — nothing to pop.
                if (inlineDivDepth > 0 && tag.Equals("div", StringComparison.OrdinalIgnoreCase))
                {
                    inlineDivDepth--;
                    continue;
                }
                if (BlockTags.Contains(tag))
                {
                    var popped = styleStack.Count > 1 ? styleStack.Pop() : styleStack.Peek();
                    if (divClassStack.Count > 0) divClassStack.RemoveAt(divClassStack.Count - 1);
                    var closingMarginBottom = uaBlockRhythm || articleRhythm || bodyBoxRhythm
                        ? popped.MarginBottom : 0;
                    Flush(true,popped);
                    // A border-only declared box whose element closed without any
                    // block flushing inside it still strokes its box: emit the
                    // reserved-height spacer carrying it.
                    if (pendingBorderBox is { } cbb && styleStack.Count == pendingBorderBoxDepth - 1)
                    {
                        blocks.Add(new Block
                        {
                            Text = "",
                            IsHardBreak = true,
                            FontSize = popped.FontSize,
                            FontRes = popped.FontRes,
                            LeftIndent = popped.LeftIndent,
                            ExplicitHeight = cbb.h,
                            BorderBoxWPt = cbb.w,
                            BorderRadiusPt = cbb.r,
                            BorderWidth = cbb.bw,
                            BorderColor = cbb.c,
                        });
                        pendingBorderBox = null;
                    }
                    // page-break-after breaks AFTER this element's content — even when
                    // it emitted nothing (the empty cover-separator <p>): the break
                    // carries to whatever block flushes next.
                    if (popped.PageBreakAfter) pendingPageBreak = true;
                    inlineRunId = 0; runPrevWasControl = false;
                    // The box's bottom margin lands on its LAST line, whatever the
                    // <br>s inside it split off (see the flush).
                    if (closingMarginBottom > 0 && blocks.Count > 0)
                        blocks[^1].MarginBottom = Math.Max(blocks[^1].MarginBottom, closingMarginBottom);
                }
                // Inline close tags are no-ops for block layout.
                continue;
            }

            // Opening tag (or self-closing).
            // Anchor targets: an `id` on any element, or a `name` on <a>, marks a
            // destination that a #fragment hyperlink can jump to. Record it against
            // the block currently being built.
            if (tok.Attributes is not null)
            {
                if (tok.Attributes.TryGetValue("id", out var idName) && !string.IsNullOrEmpty(idName))
                    pendingAnchorNames.Add(idName);
                if (tag.Equals("a", StringComparison.OrdinalIgnoreCase)
                    && tok.Attributes.TryGetValue("name", out var aName) && !string.IsNullOrEmpty(aName))
                    pendingAnchorNames.Add(aName);
            }
            if (tag.Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                Flush(false, styleStack.Peek());
                if (!tok.IsSelfClosing) centerDepth++;
                continue;
            }
            if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                // A <br> directly after a styled row block ends the full default-size
                // line box the row's markup opened (the browser's ~16px body line) —
                // there is no pending text for the usual flush-based break to space.
                if (currentText.Length == 0 && blocks.Count > 0 && blocks[^1].RowRuns is not null)
                {
                    blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true, ExplicitHeight = 13.5,
                        // The enclosing style's size: the metric flow sizes the <br>'s line
                        // box from it (a <br> between 9pt paragraphs is still an 11pt line
                        // when the paragraph closed before it).
                        FontSize = styleStack.Peek().FontSize,
                    });
                    continue;
                }
                // Metric flow: a standalone <br> (no pending text — it sits between
                // blocks) is one full empty line box at the enclosing style's size.
                // A <br> after text just ends the line (the flush below), same as legacy.
                if (metricLayout && currentText.ToString().Trim().Length == 0)
                {
                    currentText.Clear();
                    blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true, ExplicitHeight = 13.5,
                        FontSize = styleStack.Peek().FontSize,
                    });
                    continue;
                }
                // Form-document dialect: a standalone <br> between flow runs (the
                // notice divs' <br><br> rhythm) keeps one empty line box at the
                // enclosing style's size instead of collapsing.
                if (brBlankLines && currentText.ToString().Trim().Length == 0)
                {
                    currentText.Clear();
                    var fbk = styleStack.Peek();
                    blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true,
                        FontSize = fbk.FontSize, FontFamily = fbk.FontFamily,
                        WidthPx = fbk.WidthPx,
                    });
                    continue;
                }
                // Sectioned-report rhythm: N consecutive <br> in block context are an
                // anonymous block of exactly N line boxes, carrying no margin of their own.
                if (uaBlockRhythm && currentText.ToString().Trim().Length == 0)
                {
                    currentText.Clear();
                    var brk = styleStack.Peek();
                    blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true,
                        FontSize = brk.FontSize, FontFamily = brk.FontFamily,
                        // The empty line box occupies one full line of the enclosing
                        // style — without a height of its own it would take no space.
                        ExplicitHeight = NormalLineHeightPt(brk.FontSize),
                    });
                    continue;
                }
                // Legacy-font dialect: a <br> with no pending text (an empty
                // <p><…><br></…></p>) is a full blank line on the 1.25×em grid, like
                // the metric flow above. Gated on LegacyFontSized so no other legacy
                // HTML gains blank lines where it previously collapsed them.
                if (currentText.ToString().Trim().Length == 0 && styleStack.Peek().LegacyFontSized)
                {
                    currentText.Clear();
                    var pk = styleStack.Peek();
                    blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true, IsLineBreak = true,
                        FontFamily = pk.FontFamily, ForeColor = pk.ForeColor,
                        LegacyFontPt = pk.LegacyFontPt, LegacyFontSized = true,
                    });
                    continue;
                }
                // <br> inserts a newline *within* the current block. We
                // flush as an empty forced-break block so the next text
                // starts on a new line at the same style. Quirks CSS-run docs
                // keep a collapsed newline before the <br> as a trailing space.
                keepTrailingSpace = inlineBlockCols;
                Flush(true,styleStack.Peek());
                keepTrailingSpace = false;
                pendingColIndent = 0;
                inlineRunId = 0; runPrevWasControl = false;
                continue;
            }
            if (tag.Equals("hr", StringComparison.OrdinalIgnoreCase))
            {
                Flush(true,styleStack.Peek());
                inlineRunId = 0; runPrevWasControl = false;
                // Draw <hr> as a horizontal rule. The line colour/width come
                // from the CSS border (e.g. "border: 1px solid red"); default
                // to a thin grey line when unspecified.
                ParseHrStyle(tok.Attributes, out var hrColor, out var hrWidth);
                // Form dialect: the rule's own CSS margins (carried over from the
                // divider div it replaced) set the section rhythm around it.
                // The UA rule is `hr { margin: 0.5em 0 }` — smaller than a paragraph's,
                // so beside one it collapses away entirely.
                double hrMarginTop = 6, hrMarginBottom = 6;
                if (uaBlockRhythm)
                    hrMarginTop = hrMarginBottom = 0.5 * styleStack.Peek().FontSize;
                if (formDialect && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var hrStyle) && hrStyle is not null)
                {
                    var hmt = Regex.Match(hrStyle, @"margin-top\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                    if (hmt.Success) hrMarginTop = double.Parse(hmt.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * 0.75;
                    var hmb = Regex.Match(hrStyle, @"margin-bottom\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                    if (hmb.Success) hrMarginBottom = double.Parse(hmb.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * 0.75;
                }
                blocks.Add(new Block
                {
                    Text = "",
                    FontSize = styleStack.Peek().FontSize,
                    FontRes = "F1",
                    MarginTop = hrMarginTop,
                    MarginBottom = hrMarginBottom,
                    // Not IsHardBreak: a rule is drawn content, so it must
                    // survive the trailing-spacer trim and be rendered.
                    IsHorizontalRule = true,
                    RuleColor = hrColor,
                    RuleWidth = hrWidth,
                    // A pending page-break (a break-only <p> right before the rule)
                    // belongs to the rule itself — otherwise the rule stays at the
                    // old page's tail and the break jumps past it.
                    PageBreakBefore = pendingPageBreak,
                });
                pendingPageBreak = false;
                // The rule's OWN page-break-after (the `<hr style="page-break-after:
                // always">` section-divider idiom): the rule closes the page it sits
                // on, and whatever block flushes next opens a fresh one.
                if (tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var hrBreakStyle) && hrBreakStyle is not null
                    && Regex.IsMatch(hrBreakStyle, @"(page-)?break-after\s*:\s*(always|page)",
                        RegexOptions.IgnoreCase))
                    pendingPageBreak = true;
                continue;
            }

            // <img>: emit an in-flow image block (drawn at layout time). A display:none image
            // is not part of the rendering — skip it entirely (no draw, no reserved space).
            if (tag.Equals("img", StringComparison.OrdinalIgnoreCase))
            {
                string? src = null;
                tok.Attributes?.TryGetValue("src", out src);
                bool imgHidden = tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var istyle)
                    && Regex.IsMatch(istyle, @"display\s*:\s*none", RegexOptions.IgnoreCase);
                // A src-less image renders as its alt TEXT in a browser (the broken-image
                // placeholder line) — it still occupies a line box in the flow.
                if (string.IsNullOrEmpty(src) && !imgHidden
                    && tok.Attributes is not null && tok.Attributes.TryGetValue("alt", out var altText)
                    && !string.IsNullOrWhiteSpace(altText))
                {
                    currentText.Append(DecodeEntities(altText));
                }
                if (!string.IsNullOrEmpty(src) && !imgHidden)
                {
                    // Control-box dialect: an image arriving MID-LINE (after label
                    // text) rides inline at that line's end — defer it onto the text
                    // block the pending run will flush into.
                    if (controlBoxes && currentText.ToString().Trim().Length > 0)
                    {
                        pendingInlineIcon = true;
                        continue;
                    }
                    // Leading inline whitespace before an image (e.g. "&nbsp;&nbsp; <img>")
                    // shares the image's line box in a browser — it is not a line of its
                    // own. Keep its horizontal advance as the image's indent, but drop the
                    // run so it doesn't reserve a phantom text line above the image (which
                    // would push the image down a line).
                    double imgIndentPt = 0;
                    if (IsAllWhitespace(currentText) && currentText.Length > 0)
                    {
                        var (leadTxt, _) = CollapseWhitespaceWithMap(currentText.ToString());
                        if (leadTxt.Length > 0)
                        {
                            var lst = styleStack.Peek();
                            var leadFace = !string.IsNullOrEmpty(lst.FontFamily)
                                && PosFace(lst.FontFamily!).ttf is not null ? lst.FontFamily! : "Arial";
                            imgIndentPt = MeasureFaceText(leadFace, leadTxt, lst.FontSize);
                        }
                        currentText.Clear();
                    }
                    // A sized container is FILLED by its image content — the
                    // declared height must not also bill as an empty spacer at
                    // this flush (the 600×400 chart div held exactly its svg).
                    styleStack.Peek().ExplicitHeight = 0;
                    Flush(false, styleStack.Peek());
                    double iw = 0, ih = 0;
                    if (tok.Attributes is not null)
                    {
                        if (tok.Attributes.TryGetValue("width", out var ws)) double.TryParse(
                            Regex.Match(ws, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out iw);
                        if (tok.Attributes.TryGetValue("height", out var hs)) double.TryParse(
                            Regex.Match(hs, @"[\d.]+").Value, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out ih);
                        if (tok.Attributes.TryGetValue("style", out var st2) && !string.IsNullOrEmpty(st2))
                        {
                            // Property-name anchored: "border-width: 0px" must not
                            // satisfy the width lookup (nor min-height the height one).
                            // A unitless value is CSS quirks px ("width:500;").
                            var wm = Regex.Match(st2, @"(?<![-\w])width\s*:\s*([\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                            if (wm.Success) double.TryParse(wm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out iw);
                            var hm = Regex.Match(st2, @"(?<![-\w])height\s*:\s*([\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                            if (hm.Success) double.TryParse(hm.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out ih);
                        }
                    }
                    // position:absolute + left/top on the image's inline style: the
                    // image seats at the page margins + left/top (CSS px) and leaves
                    // the flow entirely (the cursor never moves for it).
                    var imgAbsPos = false;
                    double imgAbsLeftPx = 0, imgAbsTopPx = 0;
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var apSt)
                        && !string.IsNullOrEmpty(apSt)
                        && Regex.IsMatch(apSt, @"position\s*:\s*absolute", RegexOptions.IgnoreCase))
                    {
                        var alM = Regex.Match(apSt, @"(?<![-\w])left\s*:\s*(-?[\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                        var atM = Regex.Match(apSt, @"(?<![-\w])top\s*:\s*(-?[\d.]+)\s*(?:px)?\s*(?:;|$)", RegexOptions.IgnoreCase);
                        if (alM.Success && atM.Success
                            && double.TryParse(alM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out imgAbsLeftPx)
                            && double.TryParse(atM.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out imgAbsTopPx))
                            imgAbsPos = true;
                    }
                    string? alt = null;
                    tok.Attributes?.TryGetValue("alt", out alt);
                    // Form dialect: the image's CSS margin-left indents it within the
                    // flow (a browser applies it to the image box; the legacy flow's
                    // calibrated conversions keep ignoring it).
                    if (formDialect && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("style", out var imStyle) && imStyle is not null)
                    {
                        var iml = Regex.Match(imStyle, @"(?<![-\w])margin-left\s*:\s*(\d+(?:\.\d+)?)px", RegexOptions.IgnoreCase);
                        if (iml.Success) imgIndentPt += double.Parse(iml.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) * 0.75;
                    }
                    // CSS vertical padding on the image (style="padding:28px 0 14px").
                    double padT = 0, padB = 0;
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var ist)
                        && !string.IsNullOrEmpty(ist))
                    {
                        var pm = Regex.Match(ist, @"padding\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                        if (pm.Success)
                        {
                            var parts = pm.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 1) padT = padB = ParsePxValue(parts[0]);
                            if (parts.Length >= 3) padB = ParsePxValue(parts[2]);
                        }
                    }
                    // CSS transform: rotate(Ndeg) — from the img's inline style or a class
                    // rule. Only the UNPREFIXED property qualifies (a vendor-mangled
                    // "-webkit - transform" parses under a different property name and a
                    // real vendor prefix would shadow the standard one anyway).
                    double imgRotDeg = 0;
                    string? rotSrc = null;
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var rst)
                        && !string.IsNullOrEmpty(rst)
                        && Regex.Match(rst, @"(?<![-\w])transform\s*:\s*([^;]+)", RegexOptions.IgnoreCase)
                            is { Success: true } rim)
                        rotSrc = rim.Groups[1].Value;
                    else if (css is not null && tok.Attributes is not null
                             && tok.Attributes.TryGetValue("class", out var rcls) && rcls is not null)
                        foreach (var rc in rcls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + rc, out var rrules)
                                && rrules.TryGetValue("transform", out var rv))
                            { rotSrc = rv; break; }
                    if (rotSrc is not null)
                    {
                        var rm = Regex.Match(rotSrc, @"rotate\(\s*(-?[\d.]+)\s*deg\s*\)", RegexOptions.IgnoreCase);
                        if (rm.Success) double.TryParse(rm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out imgRotDeg);
                    }
                    // An inline percentage max-width caps the drawn box at that
                    // share of the content width and keeps the sheet from widening.
                    double imgMaxWFrac = 0;
                    if (tok.Attributes is not null && tok.Attributes.TryGetValue("style", out var mwSt)
                        && mwSt is not null
                        && Regex.Match(mwSt, @"max-width\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase)
                            is { Success: true } mwM
                        && double.TryParse(mwM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var mwPct)
                        && mwPct > 0)
                        imgMaxWFrac = mwPct / 100.0;
                    blocks.Add(new Block { IsImage = true, ImageSrc = src, ImageWidth = iw, ImageHeight = ih,
                        ImageMaxWFrac = imgMaxWFrac,
                        ImageAbsPos = imgAbsPos, ImageAbsLeftPx = imgAbsLeftPx, ImageAbsTopPx = imgAbsTopPx,
                        ImageAlt = alt, PageBreakBefore = pendingPageBreak,
                        // Centered inside <center> or an ALIGN="center" block (a legacy
                        // <P ALIGN="center"><IMG> centers the image line).
                        ImageCentered = centerDepth > 0 || styleStack.Peek().AlignCenterAttr,
                        ImagePadTopPx = padT, ImagePadBottomPx = padB,
                        // Container chrome (containerBoxIndents mode): the enclosing
                        // divs' padding+border chain indents the image like any block,
                        // and the width-billing part sizes the page-widen.
                        ImageIndentPt = imgIndentPt
                            + (containerBoxIndents ? styleStack.Peek().LeftIndent : 0),
                        ImageWidenPadPt = containerBoxIndents ? styleStack.Peek().BillPadPt : 0,
                        ImageCardShadow = containerBoxIndents ? styleStack.Peek().CardShadowColor : null,
                        ImageCardChromePt = containerBoxIndents ? styleStack.Peek().CardChromePt : 0,
                        ImageRotateDeg = imgRotDeg,
                        FloatLeft = styleStack.Peek().FloatLeft });
                    pendingPageBreak = false;
                }
                continue;
            }

            // <button>: its inner text is the caption of a push-button box, not flow
            // content (control-box dialect only; other dialects keep it as text).
            if (controlBoxes && tag.Equals("button", StringComparison.OrdinalIgnoreCase))
            {
                Flush(false, styleStack.Peek());
                inlineRunId = 0; runPrevWasControl = false;
                inButton = true; buttonText.Clear();
                continue;
            }

            // <input> / <textarea>: emit an interactive AcroForm field.
            // Text-like inputs become a TextBoxField; a checkbox becomes a CheckboxField
            // (its `checked` attribute → Checked); a radio becomes a RadioButtonOptionField
            // grouped by name. hidden/submit/button/image are skipped.
            if (tag.Equals("textarea", StringComparison.OrdinalIgnoreCase))
            {
                // <textarea> → a multi-line AcroForm text field. Its inner text is the
                // default value (suppressed via inTextarea), not flow content.
                if (controlBoxes && inlineRunId == 0) inlineRunId = nextInlineRunId++;
                var taTrailWs = currentText.Length > 0 && char.IsWhiteSpace(currentText[^1]);
                var taBefore = blocks.Count;
                Flush(false, styleStack.Peek());
                if (controlBoxes && taTrailWs && blocks.Count > taBefore)
                    blocks[^1].Text += " ";
                blocks.Add(BuildInputBlock(tok.Attributes, styleStack.Peek(),
                    controlBoxes, multiline: true));
                if (controlBoxes)
                {
                    blocks[^1].InlineRunId = inlineRunId;
                    runPrevWasControl = true;
                }
                textareaBlock = blocks[^1];
                textareaText.Clear();
                inTextarea = true;
                continue;
            }
            // <select>: the control occupies its box; only its chosen entry is text.
            if (tag.Equals("select", StringComparison.OrdinalIgnoreCase))
            {
                // The label text on this line joins the control's inline run, keeping
                // the collapsed space the markup left before the control.
                if (controlBoxes && inlineRunId == 0) inlineRunId = nextInlineRunId++;
                var trailWs = currentText.Length > 0 && char.IsWhiteSpace(currentText[^1]);
                var nBefore = blocks.Count;
                Flush(false, styleStack.Peek());
                if (controlBoxes && trailWs && blocks.Count > nBefore)
                    blocks[^1].Text += " ";
                inSelect = true; inSelectedOption = false; selectedText.Clear();
                selectOptions.Clear(); curOptionText.Clear();
                string? selNm = null, selId = null;
                tok.Attributes?.TryGetValue("name", out selNm);
                tok.Attributes?.TryGetValue("id", out selId);
                selectName = !string.IsNullOrEmpty(selNm) ? selNm : selId;
                continue;
            }
            if (inSelect && tag.Equals("option", StringComparison.OrdinalIgnoreCase))
            {
                if (curOptionText.Length > 0)
                {
                    selectOptions.Add(CollapseWs(curOptionText.ToString()));
                    curOptionText.Clear();
                }
                inSelectedOption = tok.Attributes is not null
                    && tok.Attributes.ContainsKey("selected") && selectedText.Length == 0;
                continue;
            }
            if (tag.Equals("input", StringComparison.OrdinalIgnoreCase))
            {
                string? type = null;
                tok.Attributes?.TryGetValue("type", out type);
                type = UnescapeAttrValue(type);
                type = string.IsNullOrEmpty(type) ? "text" : type.ToLowerInvariant();
                if (type is "text" or "password" or "email" or "tel" or "url"
                    or "number" or "search" or "date" or "datetime-local" or "month" or "week" or "time"
                    // The control-box dialect has no radio/checkbox/hidden widgets:
                    // they ALL render as ordinary text boxes — the
                    // intrinsic 20-column box with the value (wrappers and all)
                    // typeset inside (an escaped type never reaches its handler).
                    || (controlBoxes && type is "radio" or "checkbox" or "hidden"))
                {
                    if (controlBoxes && inlineRunId == 0) inlineRunId = nextInlineRunId++;
                    var inTrailWs = currentText.Length > 0 && char.IsWhiteSpace(currentText[^1]);
                    var inBefore = blocks.Count;
                    Flush(false, styleStack.Peek());
                    if (controlBoxes && inTrailWs && blocks.Count > inBefore)
                        blocks[^1].Text += " ";
                    blocks.Add(BuildInputBlock(tok.Attributes, styleStack.Peek(), controlBoxes));
                    if (controlBoxes)
                    {
                        blocks[^1].InlineRunId = inlineRunId;
                        runPrevWasControl = true;
                    }
                }
                else if (type == "checkbox")
                {
                    Flush(false, styleStack.Peek());
                    var st = styleStack.Peek();
                    blocks.Add(new Block
                    {
                        IsCheckbox = true,
                        Checked = tok.Attributes?.ContainsKey("checked") == true,
                        FontSize = st.FontSize,
                        FontRes = st.FontRes,
                        LeftIndent = st.LeftIndent,
                    });
                }
                else if (type == "radio")
                {
                    Flush(false, styleStack.Peek());
                    var st = styleStack.Peek();
                    string? grp = null;
                    tok.Attributes?.TryGetValue("name", out grp);
                    blocks.Add(new Block
                    {
                        IsRadio = true,
                        RadioGroup = grp ?? "",
                        Checked = tok.Attributes?.ContainsKey("checked") == true,
                        FontSize = st.FontSize,
                        FontRes = st.FontRes,
                        LeftIndent = st.LeftIndent,
                    });
                }
                continue;
            }

            if (BlockTags.Contains(tag))
            {
                // Browser-UA flow: a self-closed <p/> is an EMPTY paragraph —
                // its UA margin max-collapses onto the next block; nothing is
                // pushed. A real <p> open is counted so a matching </p> is told
                // apart from the stray-close quirk above.
                if (browserUa && tag.Equals("p", StringComparison.OrdinalIgnoreCase))
                {
                    if (tok.IsSelfClosing)
                    {
                        Flush(false, styleStack.Peek());
                        pendingEmptyPMarginPt = Math.Max(pendingEmptyPMarginPt,
                            UaBlockMarginEm * styleStack.Peek().FontSize);
                        continue;
                    }
                    pOpenDepth++;
                }
                // Metric flow: a body-level <p> opens one UA block margin above
                // it (the same 1.12 em the browser flow gives every paragraph).
                else if (metricLayout && uaPMargins && tag.Equals("p", StringComparison.OrdinalIgnoreCase)
                         && !tok.IsSelfClosing)
                    pendingEmptyPMarginPt = Math.Max(pendingEmptyPMarginPt,
                        UaBlockMarginEm * styleStack.Peek().FontSize);
                // A div the sheet explicitly sets `display:inline` — directly or
                // through a descendant rule from an enclosing class
                // (`.content-center-text .bold { display:inline }`, the panel
                // header) — rides the current line like a span: no flush, no
                // block break, no style push.
                if (articleRhythm && css is not null
                    && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
                    && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var inlDivCls))
                {
                    var divInline = false;
                    foreach (var c in inlDivCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (css.TryGetValue("." + c, out var idr)
                            && idr.TryGetValue("display", out var idd)
                            && idd.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                            divInline = true;
                        for (var di = divClassStack.Count - 1; di >= 0 && !divInline; di--)
                            foreach (var ec in divClassStack[di].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                                if (css.TryGetValue("." + ec + " ." + c, out var edr)
                                    && edr.TryGetValue("display", out var edd)
                                    && edd.Trim().Equals("inline", StringComparison.OrdinalIgnoreCase))
                                { divInline = true; break; }
                        if (divInline) break;
                    }
                    if (divInline)
                    {
                        inlineDivDepth++;
                        continue;
                    }
                }
                // Start a new block: flush any pending inline text at the
                // outer style, then push the new style.
                Flush(false,styleStack.Peek());
                inlineRunId = 0; runPrevWasControl = false;
                var parent = styleStack.Peek();
                var style = new BlockStyle
                {
                    FontSize = parent.FontSize,
                    FontRes = parent.FontRes,
                    FontFamily = parent.FontFamily,
                    MarginTop = 0,
                    MarginBottom = 0,
                    LeftIndent = parent.LeftIndent,
                    BillPadPt = parent.BillPadPt,
                    CardShadowColor = parent.CardShadowColor,
                    CardChromePt = parent.CardChromePt,
                    FormDialect = parent.FormDialect,
                    ParentFontSize = parent.FontSize,
                    WidthFrac = parent.WidthFrac,
                    WidthPx = parent.WidthPx,
                    AlignRight = parent.AlignRight,
                    // A float is inherited by the boxes inside it: the image that
                    // actually gets taken out of the flow is usually nested a few
                    // wrappers below the element the rule names.
                    FloatLeft = parent.FloatLeft,
                    FloatRight = parent.FloatRight,
                    ArticleRhythm = parent.ArticleRhythm,
                    UaSerif = parent.UaSerif,
                };
                // A container's pending padding-top spaces the FIRST block that
                // actually flushes — hand it to the child opening now so a <p>
                // inside a padded div does not orphan it on the div's own style.
                if (uaPMargins && parent.PadTop > 0)
                {
                    style.PadTop += parent.PadTop;
                    parent.PadTop = 0;
                }
                ApplyBlockTagStyle(tag, style, uaDefaults, browserUa, bandDialect, uaBlockRhythm,
                    articleRhythm);
                // A sheet rule addressed at this block — `p.MsoNormal { margin:
                // 0cm; margin-bottom: .0001pt }` (the Word-filtered idiom) or a
                // bare element rule — replaces the UA paragraph margins: the
                // sheet authors its own rhythm.
                if (browserUa && css is not null)
                {
                    Dictionary<string, string>? bmRule = null;
                    if (tok.Attributes is { } bmAttrs0
                        && bmAttrs0.TryGetValue("class", out var bmCls) && bmCls is not null)
                        foreach (var pc in bmCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue(tag.ToLowerInvariant() + "." + pc, out bmRule)
                                || css.TryGetValue("." + pc, out bmRule))
                                break;
                    if (bmRule is null && css.TryGetValue(tag.ToLowerInvariant(), out var bmBare))
                        bmRule = bmBare;
                    if (bmRule is not null && (bmRule.ContainsKey("margin")
                        || bmRule.ContainsKey("margin-top") || bmRule.ContainsKey("margin-bottom")))
                    {
                        var bmSb = new StringBuilder();
                        foreach (var kv in bmRule)
                            bmSb.Append(kv.Key).Append(':').Append(kv.Value).Append(';');
                        var bmDecl = bmSb.ToString();
                        var bmBox = ParseInlineMarginBox(bmDecl, style.FontSize);
                        if (bmRule.ContainsKey("margin") || bmRule.ContainsKey("margin-top"))
                            style.MarginTop = bmBox.top;
                        if (bmRule.ContainsKey("margin") || bmRule.ContainsKey("margin-bottom"))
                            style.MarginBottom = bmBox.bottom;
                    }
                    // …and its typography: a PERCENT font-size resolves against
                    // the inherited size (h1 { font-size: 120% } = 14.4 on the
                    // UA base), a length replaces it, and a RESOLVABLE family
                    // rides the block's runs (h6 { font-family: Verdana }).
                    if (bmRule is not null)
                    {
                        if (bmRule.TryGetValue("font-size", out var bmFsV))
                        {
                            var bmFs = bmFsV.Trim();
                            if (bmFs.EndsWith("%", StringComparison.Ordinal)
                                && double.TryParse(bmFs.TrimEnd('%'),
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out var bmPct) && bmPct > 0)
                                style.FontSize *= bmPct / 100.0;
                            else if (TryParseCssFontSize(bmFs, out var bmPt) && bmPt > 0)
                                style.FontSize = bmPt;
                        }
                        if (bmRule.TryGetValue("font-family", out var bmFamV)
                            && FirstFontFamily(bmFamV) is { Length: > 0 } bmFam
                            && WinMetricsFor(bmFam) is not null)
                            style.FontFamily = bmFam;
                        if (bmRule.TryGetValue("font-weight", out var bmFwV)
                            && (bmFwV.Trim() is "bold" or "bolder"
                                || (int.TryParse(bmFwV.Trim(), out var bmFwN) && bmFwN >= 600)))
                            style.FontRes = "F2";
                        if (bmRule.TryGetValue("text-align", out var bmTaV))
                        {
                            var bmTa = bmTaV.Trim().ToLowerInvariant();
                            if (bmTa == "center") style.AlignCenterAttr = true;
                            else if (bmTa == "justify") style.AlignJustify = true;
                        }
                    }
                }
                // The sheet's own element reset ("h1, h2, …, p { margin: 0 }") beats
                // the legacy calibrated heading/paragraph margins — the widget card
                // measures its header purely from the class-rule chrome
                // (containerBoxIndents mode only).
                if (containerBoxIndents && css is not null
                    && css.TryGetValue(tag.ToLowerInvariant(), out var tagReset)
                    && tagReset.TryGetValue("margin", out var tagResetMargin)
                    && Regex.IsMatch(tagResetMargin.Trim(), @"^0(px)?(\s+0(px)?){0,3}$"))
                {
                    style.MarginTop = 0;
                    style.MarginBottom = 0;
                }
                // Control-box dialect: headings render at the UA scale of the 12 pt
                // base with the dialect's heading gaps (27.34 pt above
                // an h3 = 13.5 line + 13.84 margin; 25.97 below = 16.5 line + 9.47).
                if (controlBoxes && tag.ToLowerInvariant() is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
                {
                    style.FontSize = tag.ToLowerInvariant() switch
                    {
                        "h1" => 24, "h2" => 18, "h3" => 14.039, "h4" => 12,
                        "h5" => 9.96, _ => 8.04,
                    };
                    style.FontRes = "F2";
                    style.MarginTop = 13.84;
                    style.MarginBottom = 9.47;
                }
                divClassStack.Add(tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var openCls) ? openCls : "");
                // Container box chrome from CLASS rules (containerBoxIndents mode):
                // padding+border-left indent the content; the vertical chrome stacks
                // onto the next block's top margin; a class-rule HEIGHT (the widget
                // header band) floors the next block's height. A width:100%
                // container's horizontal chrome overflows its parent (CSS content-box:
                // its content box equals the parent's, the chrome paints outside), so
                // it indents but must NOT bill the page-widen; width:auto chrome does.
                if (containerBoxIndents && css is not null && !string.IsNullOrEmpty(divClassStack[^1]))
                {
                    double bxPadL = 0, bxPadR = 0, bxPadT = 0, bxBorder = 0, bxHeight = 0;
                    var bxPctWidth = false;
                    Color? bxShadow = null;
                    var bxClasses = divClassStack[^1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    void ReadBoxRule(Dictionary<string, string> rule)
                    {
                        if ((rule.TryGetValue("box-shadow", out var bsh)
                             || rule.TryGetValue("-webkit-box-shadow", out bsh))
                            && ParseCssColor(bsh) is { } bshCol)
                            bxShadow = bshCol;
                        if (rule.TryGetValue("padding", out var pSh) && BoxChromeLen(pSh) is > 0 and var pv)
                        { bxPadL = Math.Max(bxPadL, pv); bxPadR = Math.Max(bxPadR, pv); bxPadT = Math.Max(bxPadT, pv); }
                        if (rule.TryGetValue("padding-left", out var pl)) bxPadL = Math.Max(bxPadL, BoxChromeLen(pl));
                        if (rule.TryGetValue("padding-right", out var pr)) bxPadR = Math.Max(bxPadR, BoxChromeLen(pr));
                        if (rule.TryGetValue("padding-top", out var pt)) bxPadT = Math.Max(bxPadT, BoxChromeLen(pt));
                        if (rule.TryGetValue("border", out var bd)) bxBorder = Math.Max(bxBorder, BoxChromeLen(bd));
                        if (rule.TryGetValue("height", out var bh)) bxHeight = Math.Max(bxHeight, BoxChromeLen(bh));
                        // Only width:100% marks the chrome-overflow case (its content
                        // box equals the parent's). Any other percent is a responsive
                        // grid column's @media width leaking into the flattened map —
                        // on paper the column is width:auto and its chrome bills.
                        if (rule.TryGetValue("width", out var bw) && bw.Trim() == "100%") bxPctWidth = true;
                    }
                    foreach (var bc in bxClasses)
                        if (css.TryGetValue("." + bc, out var bcr)) ReadBoxRule(bcr);
                    // Compound two-class selectors (".card.default { border: … }").
                    foreach (var ca in bxClasses)
                        foreach (var cb in bxClasses)
                            if (!ReferenceEquals(ca, cb) && css.TryGetValue("." + ca + "." + cb, out var ccr))
                                ReadBoxRule(ccr);
                    if (bxPadL + bxBorder > 0) style.LeftIndent += bxPadL + bxBorder;
                    if (bxPadT + bxBorder > 0) pendingBoxPadTop += bxPadT + bxBorder;
                    if (bxHeight > 0) pendingBoxHeight = Math.Max(pendingBoxHeight, bxHeight);
                    if (!bxPctWidth) style.BillPadPt += bxPadL + bxPadR + 2 * bxBorder;
                    // A box-shadow'd container is the widget CARD: remember its shadow
                    // colour and its own chrome so the chart image can frame it.
                    if (bxShadow is not null)
                    {
                        style.CardShadowColor = bxShadow;
                        style.CardChromePt = bxPadL + bxBorder;
                    }
                }
                // Band annotation injected by the print-grid pre-pass (the ancestry was
                // resolved before segmentation split the host div away).
                if (browserUa && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("band", out var bandSpec))
                {
                    var bandParts = bandSpec.Split('|');
                    var rgbParts = bandParts[0].Split(',');
                    if (rgbParts.Length == 3
                        && int.TryParse(rgbParts[0], out var bandR)
                        && int.TryParse(rgbParts[1], out var bandG)
                        && int.TryParse(rgbParts[2], out var bandB))
                    {
                        style.BandColor = Color.FromRgb(bandR, bandG, bandB);
                        style.BandPx = bandParts.Length > 1 && double.TryParse(bandParts[1],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var bandPxA) ? bandPxA : 1;
                        style.BandPadPx = bandParts.Length > 2 && double.TryParse(bandParts[2],
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var bandPadA) ? bandPadA : 0;
                    }
                }
                // A ".cls h4"-style descendant rule with a border-bottom paints a band
                // under the heading (the print-grid section-header underline).
                else if (browserUa && css is not null && tag.ToLowerInvariant() is "h4" or "h3" or "h2")
                {
                    for (var di = divClassStack.Count - 1; di >= 0 && style.BandColor is null; di--)
                        foreach (var c in divClassStack[di].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + c + " " + tag.ToLowerInvariant(), out var bandRule)
                                && bandRule.TryGetValue("border-bottom", out var bandDecl))
                            {
                                var bw = Regex.Match(bandDecl, @"(\d+(?:\.\d+)?)\s*px");
                                style.BandColor = ParseCssColor(bandDecl);
                                style.BandPx = bw.Success ? double.Parse(bw.Groups[1].Value,
                                    System.Globalization.CultureInfo.InvariantCulture) : 1;
                                if (bandRule.TryGetValue("padding-bottom", out var bandPad)
                                    && TryParseLength(bandPad, out var bandPadPt))
                                    style.BandPadPx = bandPadPt / 0.75;
                                break;
                            }
                }
                // Browser-UA flow: a div's style="width:N%" narrows the wrap box (the
                // source renderer stacks such divs but wraps at the declared width),
                // and its padding-top is non-collapsing space above the content.
                if (browserUa && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var uaSt) && !string.IsNullOrEmpty(uaSt))
                {
                    var uwm = Regex.Match(uaSt, @"(?:^|[;\s])width\s*:\s*(\d+(?:\.\d+)?)\s*%");
                    if (uwm.Success && double.TryParse(uwm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var uwPct)
                        && uwPct is > 0 and < 100)
                        style.WidthFrac = uwPct / 100.0;
                    var upm = Regex.Match(uaSt, @"padding(?:-top)?\s*:\s*(\d+(?:\.\d+)?)\s*(px|pt)");
                    if (upm.Success && double.TryParse(upm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var upPx)
                        && upPx > 0)
                        style.PadTop += upm.Groups[2].Value.Equals("pt", StringComparison.OrdinalIgnoreCase)
                            ? upPx : upPx * 0.75;
                }
                // Metric flow: a div's inline padding-top is real space above its
                // first block (the newsletter's #body_style 7px frame).
                else if (metricLayout && uaPMargins && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
                    && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var mpSt) && !string.IsNullOrEmpty(mpSt))
                {
                    var mpm = Regex.Match(mpSt, @"padding(?:-top)?\s*:\s*(\d+(?:\.\d+)?)\s*px");
                    if (mpm.Success && double.TryParse(mpm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var mpPx)
                        && mpPx > 0)
                        style.PadTop += mpPx * 0.75;
                }
                // A div's ABSOLUTE width (style="width:680" — quirks unitless = px, or
                // "width:680px") is recorded on every flow; only the form-document
                // dialect honors it as the wrap box at layout time.
                if (tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var awSt) && !string.IsNullOrEmpty(awSt))
                {
                    var awm = Regex.Match(awSt, @"(?:^|[;\s])width\s*:\s*(\d+(?:\.\d+)?)\s*(?:px)?\s*(?:;|$)");
                    if (awm.Success && double.TryParse(awm.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var awPx)
                        && awPx > 0)
                        style.WidthPx = awPx;
                }
                // CSS rules: type selector then class selector(s), each overriding the
                // previous, before the inline style="…" (highest specificity).
                ApplyCssRules(css, tag, tok.Attributes, style, metricLayout, coverStyles);
                // Ledger: a class WIDTH on a block element is that element's box —
                // the wrap/centring frame its lines lay out in.
                if (absSpanLedger && css is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var lgDivCls) && lgDivCls is not null)
                    foreach (var dc in lgDivCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        if (css.TryGetValue("." + dc, out var dcr)
                            && dcr.TryGetValue("width", out var dcw)
                            && Regex.Match(dcw, @"([\d.]+)\s*px", RegexOptions.IgnoreCase)
                                is { Success: true } dcwM
                            && double.TryParse(dcwM.Groups[1].Value,
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var dcwPx))
                            style.WidthPx = dcwPx;
                // Styled-article panel (a div class declaring border + background,
                // the `.td-toc` box): its vertical box is real space — a pad above
                // its header now, the bottom pad + panel margin when it closes.
                if (articleRhythm && css is not null
                    && tag.Equals("div", StringComparison.OrdinalIgnoreCase)
                    && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var panelCls))
                    foreach (var pc in panelCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        if (css.TryGetValue("." + pc, out var panelRule)
                            && panelRule.ContainsKey("border")
                            && (panelRule.ContainsKey("background-color")
                                || panelRule.ContainsKey("background")))
                        {
                            blocks.Add(new Block
                            {
                                Text = "", IsHardBreak = true,
                                ExplicitHeight = ArticlePanelPadTopPt,
                                FontSize = style.FontSize,
                            });
                            style.MarginBottom +=
                                ArticlePanelPadBottomPt + ArticlePanelMarginBottomPt;
                            break;
                        }
                // Inline style="…" overrides tag defaults: if the author
                // explicitly set padding-left / margin-left we drop the
                // list-style indent the tag default added so that e.g.
                // `<ol style="padding-left:0">` sits flush with body text.
                if (HasInlineIndentOverride(tok.Attributes))
                    style.LeftIndent = parent.LeftIndent;
                ApplyInlineStyle(tok.Attributes, style);
                // A border-TOP-only element is a DIVIDER: one rule above its
                // content, emitted as its own marker block so the border never
                // rides the element's text blocks.
                if (browserUa && style.BorderTopOnly && style.BorderColor is { } tdCol
                    && style.BorderWidth > 0)
                {
                    Flush(false, styleStack.Peek());
                    blocks.Add(new Block
                    {
                        Text = "", IsHardBreak = true,
                        BorderTopOnly = true, BorderColor = tdCol,
                        BorderWidth = style.BorderWidth,
                    });
                    style.BorderColor = null;
                    style.BorderWidth = 0;
                    style.BorderTopOnly = false;
                }
                // Border-only declared box (browser-UA flow): inline width+height+
                // border with no background. The box is handed to the first block
                // that flushes inside this element (style's own height/border are
                // cleared so the close emits no trailing spacer and the line-box
                // border model stays off).
                if (browserUa && pendingBorderBox is null
                    && style.BorderWidth > 0 && style.BorderColor is not null
                    && style.BackgroundColor is null && style.BgBoxHeightPt <= 0
                    && style.ExplicitHeight > 0
                    && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var bbSt) && bbSt is not null
                    && Regex.Match(bbSt, @"(?<![-\w])width\s*:\s*([^;""']+)",
                        RegexOptions.IgnoreCase) is { Success: true } bbW
                    && TryParseLength(bbW.Groups[1].Value.Trim(), out var bbWPt))
                {
                    pendingBorderBox = (bbWPt, style.ExplicitHeight, style.BorderWidth,
                        style.BorderColor, style.BorderRadiusPt);
                    pendingBorderBoxDepth = styleStack.Count + 1;
                    style.ExplicitHeight = 0;
                    style.BorderWidth = 0;
                    style.BorderColor = null;
                }
                // The legacy ALIGN attribute: justify stretches word gaps at draw time,
                // center centres each measured line — both layout-neutral (wrap points
                // and pagination are unchanged).
                if (tok.Attributes is not null && tok.Attributes.TryGetValue("align", out var alignAttr))
                {
                    var alignVal = alignAttr.Trim();
                    if (alignVal.Equals("justify", StringComparison.OrdinalIgnoreCase))
                        style.AlignJustify = true;
                    else if (alignVal.Equals("center", StringComparison.OrdinalIgnoreCase))
                        style.AlignCenterAttr = true;
                }
                // An element opening with page-break-before must break even when it emits
                // no block itself (the `<div style="page-break-before:always"></div>` idiom):
                // carry the break to whatever block flushes next.
                if (style.PageBreakBefore) pendingPageBreak = true;
                // List context: an <ol>/<ul> style carries a counter its <li> children
                // draw from; an <li> takes the next marker from its enclosing list. A list
                // whose CSS supplies its own `li:nth-child(..)::before { content }` markers uses
                // those (indexed by child position) instead of the numeric/bullet default.
                if (tag is "ol" or "ul")
                {
                    style.ListKind = tag == "ol" ? 1 : 2;
                    // Root stack depth 1 = body level; anything deeper means the
                    // list opened inside another block element (div/h1/…).
                    style.ListNestedInBlock = styleStack.Count > 1;
                    if (tag == "ol") style.ListCounter = ParseListStart(tok.Attributes);
                    // Styled-article: an enclosing container class may restyle the
                    // list wholesale (`.td-toc ol { list-style-type: disc }` bullets
                    // an <ol>), and its `a { padding-bottom }` block-link rule sets
                    // the item pitch. Measured: panel items sit 33.3pt in with a
                    // 36pt step per nesting level, one line box + the link pad apart.
                    if (articleRhythm && css is not null)
                    {
                        Dictionary<string, string>? tocList = null, tocLink = null;
                        for (var di = divClassStack.Count - 1;
                             di >= 0 && tocList is null; di--)
                            foreach (var c in divClassStack[di].Split((char[]?)null,
                                         StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (tocList is null
                                    && css.TryGetValue("." + c + " ol", out var clr)
                                    && clr.ContainsKey("list-style-type"))
                                    tocList = clr;
                                if (tocLink is null
                                    && css.TryGetValue("." + c + " a", out var cla)
                                    && cla.ContainsKey("padding-bottom"))
                                    tocLink = cla;
                            }
                        if (tocList is not null
                            && tocList["list-style-type"].Trim()
                                .Equals("disc", StringComparison.OrdinalIgnoreCase))
                            style.ListKind = 2;
                        if (tocList is not null || tocLink is not null)
                        {
                            // Panel list geometry: replace the plain-article indent
                            // with the panel's own (level 1 at 33.3, +36 per level),
                            // and drop the article list margin — panel items pitch
                            // uniformly across nesting boundaries (measured 24
                            // between EVERY pair, group ends included).
                            style.LeftIndent += ArticleTocIndentPt - ArticleListIndentPt
                                + (parent.ListKind != 0 ? ArticleTocLevelPt - ArticleTocIndentPt : 0);
                            style.MarginBottom = 0;
                            style.TocLinkPadPt = tocLink is not null
                                && TryParseLength(tocLink["padding-bottom"], out var tlp)
                                ? tlp : 0;
                        }
                    }
                    if (parent.TocLinkPadPt > 0) style.TocLinkPadPt = parent.TocLinkPadPt;
                    style.BeforeRules = ResolveListBeforeRules(beforeMarkers,
                        tok.Attributes is not null && tok.Attributes.TryGetValue("class", out var lc) ? lc : null);
                    style.ChildIndex = 0;
                }
                else if (tag == "li" && parent.ListKind != 0)
                {
                    // The enclosing list's own top margin lands on its FIRST item
                    // block (one-shot). At the document top a body-level list's
                    // margin then vanishes with the other UA defaults, but a list
                    // nested inside another block keeps it like an authored margin
                    // — max-collapsed with the UA body margin (probed on div- and
                    // h1..h3-wrapped lists). Browser-UA flow only; the legacy
                    // calibrated flows keep their line-on-line stacking.
                    if (browserUa && parent.MarginTop > 0)
                    {
                        style.MarginTop = parent.MarginTop;
                        if (parent.ListNestedInBlock) style.MarginTopAuthored = true;
                        parent.MarginTop = 0;
                    }
                    // Panel items pitch one line box + the link's block pad.
                    if (articleRhythm && parent.TocLinkPadPt > 0)
                        style.MarginBottom = parent.TocLinkPadPt;
                    parent.ChildIndex++;
                    BeforeMarker? before = null;
                    if (parent.BeforeRules is not null)
                        foreach (var r in parent.BeforeRules)
                            if (r.Matches(parent.ChildIndex)) { before = r; break; }
                    if (before is not null)
                    {
                        // CSS-supplied generated marker (list-style:none + ::before): render it as
                        // its own run AFTER the item text so, on an RTL line, the text is the earlier
                        // fragment and the marker the later one.
                        pendingMarker = before.Content;
                        pendingMarkerAfter = true;
                    }
                    else if (parent.BeforeRules is null)
                    {
                        // No CSS markers for this list → numeric ordinal / bullet default.
                        pendingMarker = parent.ListKind == 1
                            ? (++parent.ListCounter).ToString(System.Globalization.CultureInfo.InvariantCulture) + "."
                            : "•";
                        pendingMarkerAfter = false;
                    }
                    // BeforeRules present but no rule matched this index → no marker.
                }
                styleStack.Push(style);
                continue;
            }

            // Inline tags: mutate the top-of-stack style for <b>/<i>/<strong>/<em>.
            // <span style="font-size:..."> also adjusts size for the inner run.
            // Metric flow: MSHTML-saved documents write UPPERCASE tags, and bold drives
            // the metric wrap width — match case-insensitively there. Legacy keeps the
            // historical ordinal match so no existing conversion changes face.
            var tagCmp = metricLayout ? tag.ToLowerInvariant() : tag;
            // A nested <html>/<body> open (a forwarded email pasted whole inside a
            // paragraph) implicitly closes any open <p> — the browser recovery —
            // so a later stray </p> parses as the empty paragraph it is.
            if (browserUa && tagCmp is "html" or "body") pOpenDepth = 0;
            if (tagCmp is "b" or "strong")
            {
                // Browser-UA flow: bold is an inline RUN (tracked start..end over the
                // raw text), not a whole-block face promotion. The in-page fragment
                // flow records the run AND keeps the promotion — the writer prefers
                // the runs and falls back to the promoted face when they cover the
                // whole block anyway.
                if (trackBoldRuns && inlineBoldDepth++ == 0) inlineBoldStart = currentText.Length;
                if (!browserUa) MarkInline(styleStack, "F2");
            }
            else if ((inlineEmphasisRuns || browserUa)
                && tag.Equals("u", StringComparison.OrdinalIgnoreCase))
            {
                if (inlineUnderDepth++ == 0) inlineUnderStart = currentText.Length;
            }
            else if (tagCmp is "i" or "em")
            {
                if (controlBoxes) italicOpenTextLen = currentText.Length;
                // Browser-UA flow: italic is an inline RUN like bold — no
                // whole-block promotion (which would stick to the enclosing
                // element's style and bleed past the close tag).
                if (trackBoldRuns && inlineItalicDepth++ == 0)
                    inlineItalicStart = currentText.Length;
                if (!browserUa) MarkInline(styleStack, "F3");
            }
            else if (tagCmp == "small")
                MarkInlineSize(styleStack, factor: 0.85);
            else if (tagCmp is "span" or "font")
            {
                // A span a class rule sets `display:block` is a block box: it breaks
                // the line before its content and again at its close (metric flow
                // only — the `.year { display:block }` date-stamp idiom). Vendor-
                // mangled transform debris in the same rule stays inert.
                if (tagCmp == "span" && !tok.IsSelfClosing)
                {
                    spanDepth++;
                    if (metricLayout && css is not null && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("class", out var spCls) && spCls is not null)
                        foreach (var sc in spCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + sc, out var spr)
                                && spr.TryGetValue("display", out var spd)
                                && spd.Trim().Equals("block", StringComparison.OrdinalIgnoreCase))
                            {
                                Flush(false, styleStack.Peek());
                                blockSpanDepths.Push(spanDepth);
                                break;
                            }
                    // Inline-block title column (quirks CSS-run docs): the span's
                    // class rule declares display:inline-block with a width — its
                    // text becomes its own run, closed off from what preceded it.
                    if (inlineBlockCols && titleColSpanDepth < 0 && css is not null
                        && tok.Attributes is not null
                        && tok.Attributes.TryGetValue("class", out var ibCls) && ibCls is not null)
                        foreach (var sc in ibCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            if (css.TryGetValue("." + sc, out var ibr)
                                && ibr.TryGetValue("display", out var ibd)
                                && ibd.Trim().Equals("inline-block", StringComparison.OrdinalIgnoreCase)
                                && ibr.TryGetValue("width", out var ibw)
                                && TryParseLength(ibw, out var ibwPt) && ibwPt > 0)
                            {
                                Flush(false, styleStack.Peek());
                                openTitleColW = ibwPt;
                                titleColSpanDepth = spanDepth;
                                break;
                            }
                }
                // Ledger span classes (browser-UA flow): a margin-left class insets
                // the label run's line; a position:absolute+left class makes the
                // span its OWN column block, seated on the SAME line as the label
                // that precedes it.
                if (absSpanLedger && browserUa && tagCmp == "span" && !tok.IsSelfClosing
                    && css is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var lgCls) && lgCls is not null)
                    foreach (var sc in lgCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + sc, out var scr)) continue;
                        if (scr.TryGetValue("position", out var scPos)
                            && scPos.Contains("absolute", StringComparison.OrdinalIgnoreCase)
                            && scr.TryGetValue("left", out var scLeft)
                            && TryParseLength(scLeft, out var scLeftPt))
                        {
                            Flush(false, styleStack.Peek());
                            absSpanLabelIdx = -1;
                            if (blocks.Count > 0 && !blocks[^1].IsHardBreak
                                && blocks[^1].Text.Length > 0)
                            {
                                blocks[^1].NoAdvanceY = true;
                                absSpanLabelIdx = blocks.Count - 1;
                            }
                            absSpanLeftPt = scLeftPt;
                        }
                        else if (scr.TryGetValue("margin-left", out var scMl)
                                 && TryParseLength(scMl, out var scMlPt))
                            styleStack.Peek().TextInsetPt += scMlPt;
                    }
                // The pt-report flow: a span CLASS's typography (font-size,
                // weight) styles the rest of its block — the report's .title
                // span (rules resolve bare and tag-prefixed).
                if (spanClassTypography && tagCmp == "span" && !tok.IsSelfClosing
                    && css is not null && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("class", out var tySpCls) && tySpCls is not null)
                    foreach (var sc in tySpCls.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!css.TryGetValue("." + sc, out var tyScr))
                            css.TryGetValue("span." + sc, out tyScr);
                        if (tyScr is null) continue;
                        var tyTop = styleStack.Peek();
                        if (tyScr.TryGetValue("font-size", out var tyFs)
                            && TryParseCssFontSize(tyFs.Trim(), out var tyFsPt))
                            tyTop.FontSize = tyFsPt;
                        if (tyScr.TryGetValue("font-weight", out var tyFw)
                            && tyFw.Contains("bold", StringComparison.OrdinalIgnoreCase))
                        { tyTop.FontRes = "F2"; tyTop.EmBold = true; }
                    }
                // Inline <span style="font-family:…"> / <font face="…"> selects a
                // custom face for the enclosed run (resolved+embedded at layout).
                MarkInlineFontFamily(styleStack, tok.Attributes);
                // UA-serif flow: a <font size=N> sizes the rest of its block
                // through the legacy 1..7 ladder (measured: size2 draws 9.75,
                // size3 12, size4 13.5); its color attribute tints the run.
                if (browserUa && tagCmp == "font" && tok.Attributes is { } uaFa)
                {
                    var uaFTop = styleStack.Peek();
                    if (!tok.IsSelfClosing)
                        uaFontSaves.Push((uaFTop.FontSize, uaFTop.ForeColor, uaFTop.FontFamily));
                    if (uaFa.TryGetValue("size", out var uaFsAttr)
                        && TryParseHtmlFontSize(uaFsAttr, out var uaFsPt))
                        uaFTop.FontSize = uaFsPt;
                    if (uaFa.TryGetValue("color", out var uaFcAttr)
                        && ParseCssColor(uaFcAttr.Trim()) is { } uaFCol)
                        uaFTop.ForeColor = uaFCol;
                    // A RESOLVABLE face draws its runs in the named family
                    // (embedded at layout); unknown faces keep the UA serif.
                    if (uaFa.TryGetValue("face", out var uaFfAttr)
                        && FirstFontFamily(uaFfAttr) is { Length: > 0 } uaFfName
                        && WinMetricsFor(uaFfName) is not null)
                        uaFTop.FontFamily = uaFfName;
                }
                // UA-serif flow: an inline span's typography styles its element's
                // block — pt/px font-size, a px line-height LINE BOX, and the
                // span's own margin-left insetting its text (the legacy corpus
                // wraps whole lines in one styled span).
                if (browserUa && tok.Attributes is not null
                    && tok.Attributes.TryGetValue("style", out var uaSpSt) && uaSpSt is not null)
                {
                    // quote entities decode BEFORE the property scan — the ';'
                    // inside &quot; would otherwise truncate a value mid-entity
                    // (font-family: &quot;Tahoma&quot; parsed as '&quot')
                    if (uaSpSt.IndexOf('&') >= 0)
                        uaSpSt = uaSpSt.Replace("&quot;", "\"").Replace("&#34;", "\"")
                                       .Replace("&apos;", "'").Replace("&#39;", "'");
                    var uaTop = styleStack.Peek();
                    var fsM = Regex.Match(uaSpSt, @"font-size\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (fsM.Success && TryParseCssFontSize(fsM.Groups[1].Value.Trim(), out var uaSpFs))
                        uaTop.FontSize = uaSpFs;
                    var lhM = Regex.Match(uaSpSt, @"line-height\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                    if (lhM.Success && double.TryParse(lhM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var uaLhPx))
                        uaTop.LineBoxPt = uaLhPx * 0.75;
                    // …and a percentage one resolves against the span's own size
                    // (which the same style attribute set just above).
                    var lhPctM = Regex.Match(uaSpSt, @"line-height\s*:\s*([\d.]+)\s*%", RegexOptions.IgnoreCase);
                    if (lhPctM.Success && double.TryParse(lhPctM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var uaLhPct)
                        && uaLhPct > 0 && uaTop.FontSize > 0)
                        uaTop.LineBoxPt = uaLhPct / 100.0 * uaTop.FontSize;
                    // A span's RESOLVABLE font-family styles its element's runs,
                    // exactly like a <font face> (Word-filtered markup carries the
                    // face on spans).
                    var famM = Regex.Match(uaSpSt, @"font-family\s*:\s*([^;]+)", RegexOptions.IgnoreCase);
                    if (famM.Success && FirstFontFamily(famM.Groups[1].Value) is { Length: > 0 } uaSpFam
                        && WinMetricsFor(uaSpFam) is not null)
                        uaTop.FontFamily = uaSpFam;
                    // text-decoration: underline on an inline span opens an
                    // underline run over the span's extent (its </span> closes it).
                    if (tagCmp == "span" && !tok.IsSelfClosing
                        && Regex.IsMatch(uaSpSt, @"text-decoration\s*:\s*[^;]*\bunderline",
                            RegexOptions.IgnoreCase))
                    {
                        if (inlineUnderDepth++ == 0) inlineUnderStart = currentText.Length;
                        uaUnderSpanDepths.Push(spanDepth);
                    }
                    var mlM = Regex.Match(uaSpSt, @"margin-left\s*:\s*([\d.]+)\s*px", RegexOptions.IgnoreCase);
                    if (mlM.Success && double.TryParse(mlM.Groups[1].Value,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var uaMlPx))
                        uaTop.TextInsetPt += uaMlPx * 0.75;
                }
            }
            else if (tag.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                // <a href> opens an inline hyperlink span; record the start so the
                // text up to the matching </a> becomes a Link annotation.
                string? href = null;
                tok.Attributes?.TryGetValue("href", out href);
                if (!string.IsNullOrEmpty(href))
                    openAnchors.Push((currentText.Length, href));
            }
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
        // A page-break still pending at the segment boundary (the following content is a
        // <table> segment parsed separately): emit a break-carrier block so the table
        // starts on the fresh page.
        if (pendingPageBreak)
            blocks.Add(new Block { Text = "", IsHardBreak = true, PageBreakBefore = true });
        // Control-box dialect: consecutive blocks sharing an inline-run id — the label
        // text and controls of one markup line — merge into a single container the
        // layout lays out with a pen (shared wrapping line boxes). A run that ended up
        // with a single member keeps its ordinary standalone layout.
        if (controlBoxes)
        {
            var merged = new List<Block>(blocks.Count);
            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.InlineRunId > 0)
                {
                    int j = i;
                    while (j + 1 < blocks.Count && blocks[j + 1].InlineRunId == b.InlineRunId) j++;
                    if (j > i)
                    {
                        var items = new List<Block>();
                        for (int k = i; k <= j; k++) items.Add(blocks[k]);
                        merged.Add(new Block { InlineItems = items, FontSize = b.FontSize });
                        i = j;
                        continue;
                    }
                }
                merged.Add(b);
            }
            return merged;
        }
        return blocks;
    }
}
