using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>One item of a class-styled outline list: its CSS padding-left, the number
    /// span's text and right padding, and the inline-block description's lines (one per
    /// inner <c>br</c>, whitespace collapsed, no-break spaces kept).</summary>
    internal sealed class OutlineListItem
    {
        public double PadLeftPt;
        public string Number = "";
        public double NumberPadRightPt;
        public List<string> DescriptionLines = new();
        /// <summary>Collapsible whitespace followed the description before the item's
        /// line break (it takes a space on the line, or a line of its own).</summary>
        public bool SpaceAfterDescription;
        /// <summary>Collapsible whitespace separated the number from the description.</summary>
        public bool SpaceBeforeDescription;
        /// <summary>The item closes its last line with a <c>br</c>.</summary>
        public bool TrailingBreak;
    }

    /// <summary>A class-styled outline list: an optional inline title ahead of one
    /// unbulleted <c>ul</c> whose items pair a number span with an inline-block
    /// description span.</summary>
    internal sealed class OutlineListDoc
    {
        public string Title = "";
        public double ItemMarginTopPt;
        public double ItemMarginBottomPt;
        public List<OutlineListItem> Items = new();
    }

    private static readonly Regex OutlineStyleRegex = new(@"<style\b[^>]*>(?<css>[\s\S]*?)</style>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OutlineTokenRegex = new(@"<[^>]+>|[^<]+", RegexOptions.Compiled);
    private static readonly Regex OutlineTagRegex = new(@"^<(?<close>/?)(?<tag>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>[^>]*?)/?>$", RegexOptions.Compiled);
    private static readonly Regex OutlineClassRegex = new(@"\bclass\s*=\s*""(?<v>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OutlineStyleAttrRegex = new(@"\bstyle\s*=\s*""(?<v>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OutlineLengthRegex = new(@"^\s*(?<v>-?\d*\.?\d+)\s*(?<u>em|px|pt)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Body size of the UA model the list lays out in (16 px).</summary>
    private const double OutlineBodyEmPt = 12.0;
    private const double CssPxPt = 0.75;

    /// <summary>Marks an inner <c>br</c> in a span's collected text.</summary>
    private const char OutlineBreakMark = '\u0001';

    /// <summary>Read <paramref name="html"/> as the outline-list dialect: a style sheet,
    /// optional inline title content, then a single <c>ul</c> styled without markers whose
    /// every <c>li</c> holds a number span, an inline-block description span, a line
    /// break and only empty spans after it. Null for anything else.</summary>
    internal static OutlineListDoc? TryParseHtmlOutlineList(string html)
    {
        if (string.IsNullOrEmpty(html) || html.IndexOf("inline-block", StringComparison.OrdinalIgnoreCase) < 0) return null;
        var styleMatch = OutlineStyleRegex.Match(html);
        if (!styleMatch.Success) return null;
        var rules = ParseOutlineCss(styleMatch.Groups["css"].Value);
        var body = OutlineStyleRegex.Replace(html, "");
        body = Regex.Replace(body, @"<!--[\s\S]*?-->", "");
        body = Regex.Replace(body, @"<head\b[^>]*>[\s\S]*?</head>", "", RegexOptions.IgnoreCase);
        var op = new OutlineParseState { Rules = rules, Doc = new OutlineListDoc() };
        foreach (Match t in OutlineTokenRegex.Matches(body))
        {
            if (!OutlineStep(op, t.Value)) return null;
        }
        if (op.Phase != OutlinePhase.AfterList || op.Doc.Items.Count == 0) return null;
        op.Doc.Title = CollapseWs(op.Title.ToString());
        return op.Doc;
    }

    private enum OutlinePhase { BeforeList, InList, InItem, AfterList }

    private sealed class OutlineParseState
    {
        public List<(List<(string tag, string[] classes)> parts, string prop, string value)> Rules = null!;
        public OutlineListDoc Doc = null!;
        public OutlinePhase Phase = OutlinePhase.BeforeList;
        public StringBuilder Title = new();
        public string[] UlClasses = Array.Empty<string>();
        // Inside an item: the spans seen so far (classes, text with '\n' per inner br),
        // the open span's text, and whether a top-level br has closed the item's line.
        public List<(string[] classes, string text, bool spaceBefore)> Spans = new();
        public string[]? OpenSpanClasses;
        public StringBuilder SpanText = new();
        public bool ItemBreak;
        public bool SpaceAfterLastSpan;
        public bool SpaceBeforeBreak;
        public double ItemPadLeft;
    }

    private static readonly HashSet<string> OutlineTransparentTags = new(StringComparer.OrdinalIgnoreCase)
        { "html", "body", "div", "section", "a", "font" };

    /// <summary>Consume one token; false when the markup leaves the dialect.</summary>
    private static bool OutlineStep(OutlineParseState op, string token)
    {
        if (token[0] != '<')
        {
            return OutlineText(op, token);
        }
        var tm = OutlineTagRegex.Match(token);
        if (!tm.Success) return token.StartsWith("<!", StringComparison.Ordinal);
        var tag = tm.Groups["tag"].Value.ToLowerInvariant();
        var close = tm.Groups["close"].Value.Length > 0;
        var attrs = tm.Groups["attrs"].Value;
        if (OutlineTransparentTags.Contains(tag)) return op.Phase != OutlinePhase.InItem;
        switch (tag)
        {
            case "ul":
                if (close) return OutlineCloseList(op);
                if (op.Phase != OutlinePhase.BeforeList) return false;
                op.UlClasses = OutlineClasses(attrs);
                if (!string.Equals(OutlineCss(op, "list-style-type", ("ul", op.UlClasses)), "none", StringComparison.OrdinalIgnoreCase)) return false;
                op.Phase = OutlinePhase.InList;
                return true;
            case "li":
                if (close) return OutlineCloseItem(op);
                if (op.Phase != OutlinePhase.InList) return false;
                op.Phase = OutlinePhase.InItem;
                op.Spans.Clear();
                op.OpenSpanClasses = null;
                op.ItemBreak = false;
                op.SpaceAfterLastSpan = false;
                op.ItemPadLeft = OutlineLengthPt(OutlineInlineStyle(attrs, "padding-left"));
                return true;
            case "span":
                return OutlineSpan(op, close, attrs);
            case "br":
                if (op.Phase == OutlinePhase.InItem && op.OpenSpanClasses is not null) { op.SpanText.Append(OutlineBreakMark); return true; }
                if (op.Phase == OutlinePhase.InItem)
                {
                    if (!op.ItemBreak) op.SpaceBeforeBreak = op.SpaceAfterLastSpan;
                    op.ItemBreak = true;
                    return true;
                }
                return op.Phase == OutlinePhase.BeforeList && op.Title.Length == 0;
            default:
                return false;
        }
    }

    private static bool OutlineText(OutlineParseState op, string text)
    {
        switch (op.Phase)
        {
            case OutlinePhase.BeforeList:
                op.Title.Append(DecodeEntities(text));
                return true;
            case OutlinePhase.InItem:
                if (op.OpenSpanClasses is not null) { op.SpanText.Append(DecodeEntities(text)); return true; }
                if (text.Trim().Length != 0) return false;
                if (op.Spans.Count > 0) op.SpaceAfterLastSpan = true;
                return true;
            default:
                return text.Trim().Length == 0;
        }
    }

    private static bool OutlineSpan(OutlineParseState op, bool close, string attrs)
    {
        if (op.Phase == OutlinePhase.BeforeList)
        {
            return true;
        }
        if (op.Phase != OutlinePhase.InItem) return false;
        if (!close)
        {
            if (op.OpenSpanClasses is not null) return false;
            op.OpenSpanClasses = OutlineClasses(attrs);
            op.SpanText.Clear();
            return true;
        }
        if (op.OpenSpanClasses is null) return false;
        op.Spans.Add((op.OpenSpanClasses, op.SpanText.ToString(), op.SpaceAfterLastSpan));
        op.OpenSpanClasses = null;
        op.SpaceAfterLastSpan = false;
        return true;
    }

    private static bool OutlineCloseItem(OutlineParseState op)
    {
        if (op.Phase != OutlinePhase.InItem || op.OpenSpanClasses is not null) return false;
        var item = new OutlineListItem { PadLeftPt = op.ItemPadLeft };
        var chain = new[] { ("ul", op.UlClasses), ("li", Array.Empty<string>()) };
        var i = 0;
        // The number: the first span, unless it is the inline-block itself.
        if (i < op.Spans.Count && !OutlineIsInlineBlock(op, chain, op.Spans[i].classes))
        {
            item.Number = CollapseWs(op.Spans[i].text);
            item.NumberPadRightPt = OutlineLengthPt(OutlineCss(op, "padding-right", chain[0], chain[1], ("span", op.Spans[i].classes)));
            i++;
        }
        if (i >= op.Spans.Count || !OutlineIsInlineBlock(op, chain, op.Spans[i].classes)) return false;
        item.SpaceBeforeDescription = op.Spans[i].spaceBefore;
        foreach (var line in op.Spans[i].text.Split(OutlineBreakMark))
            item.DescriptionLines.Add(CollapseWsKeepTrailing(line));
        // A br closing the block's last line opens no line box of its own.
        if (item.DescriptionLines.Count > 1 && item.DescriptionLines[^1].Length == 0)
            item.DescriptionLines.RemoveAt(item.DescriptionLines.Count - 1);
        i++;
        // Only empty spans may follow the description.
        for (; i < op.Spans.Count; i++)
            if (op.Spans[i].text.Trim().Length != 0) return false;
        item.SpaceAfterDescription = op.ItemBreak ? op.SpaceBeforeBreak : op.SpaceAfterLastSpan;
        item.TrailingBreak = op.ItemBreak;
        op.Doc.Items.Add(item);
        op.Phase = OutlinePhase.InList;
        return true;
    }

    private static bool OutlineCloseList(OutlineParseState op)
    {
        if (op.Phase != OutlinePhase.InList) return false;
        var chain = new[] { ("ul", op.UlClasses), ("li", Array.Empty<string>()) };
        var margin = OutlineCss(op, "margin", chain[0], chain[1]);
        var parts = margin is null ? Array.Empty<string>() : margin.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        op.Doc.ItemMarginTopPt = parts.Length > 0 ? OutlineLengthPt(parts[0]) : 0;
        op.Doc.ItemMarginBottomPt = parts.Length > 2 ? OutlineLengthPt(parts[2]) : op.Doc.ItemMarginTopPt;
        if (OutlineCss(op, "margin-top", chain[0], chain[1]) is { } mt) op.Doc.ItemMarginTopPt = OutlineLengthPt(mt);
        if (OutlineCss(op, "margin-bottom", chain[0], chain[1]) is { } mb) op.Doc.ItemMarginBottomPt = OutlineLengthPt(mb);
        op.Phase = OutlinePhase.AfterList;
        return true;
    }

    private static bool OutlineIsInlineBlock(OutlineParseState op, (string tag, string[] classes)[] chain, string[] classes) =>
        string.Equals(OutlineCss(op, "display", chain[0], chain[1], ("span", classes)), "inline-block", StringComparison.OrdinalIgnoreCase);

    private static string[] OutlineClasses(string attrs)
    {
        var m = OutlineClassRegex.Match(attrs);
        return m.Success ? m.Groups["v"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
    }

    private static string? OutlineInlineStyle(string attrs, string prop)
    {
        var m = OutlineStyleAttrRegex.Match(attrs);
        if (!m.Success) return null;
        foreach (var decl in m.Groups["v"].Value.Split(';'))
        {
            var colon = decl.IndexOf(':');
            if (colon > 0 && decl.Substring(0, colon).Trim().Equals(prop, StringComparison.OrdinalIgnoreCase))
                return decl.Substring(colon + 1).Trim();
        }
        return null;
    }

    /// <summary>A CSS length in points: em of the UA body size, px at 0.75, else points.</summary>
    private static double OutlineLengthPt(string? value)
    {
        if (value is null) return 0;
        var m = OutlineLengthRegex.Match(value);
        if (!m.Success) return 0;
        var v = double.Parse(m.Groups["v"].Value, System.Globalization.CultureInfo.InvariantCulture);
        return m.Groups["u"].Value.ToLowerInvariant() switch
        {
            "em" => v * OutlineBodyEmPt,
            "px" => v * CssPxPt,
            _ => v,
        };
    }

    /// <summary>Class-selector rules of a style sheet: each rule's compound parts (tag +
    /// classes, descendant order) and one declaration.</summary>
    private static List<(List<(string tag, string[] classes)> parts, string prop, string value)> ParseOutlineCss(string css)
    {
        var rules = new List<(List<(string tag, string[] classes)>, string, string)>();
        css = Regex.Replace(css, @"/\*[\s\S]*?\*/", "");
        foreach (var block in css.Split('}'))
        {
            var brace = block.IndexOf('{');
            if (brace < 0) continue;
            var decls = block.Substring(brace + 1);
            foreach (var selector in block.Substring(0, brace).Split(','))
            {
                var parts = new List<(string tag, string[] classes)>();
                foreach (var compound in selector.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var pieces = compound.Split('.');
                    parts.Add((pieces[0].ToLowerInvariant(), pieces.Skip(1).Where(p => p.Length > 0).ToArray()));
                }
                if (parts.Count == 0) continue;
                foreach (var decl in decls.Split(';'))
                {
                    var colon = decl.IndexOf(':');
                    if (colon <= 0) continue;
                    rules.Add((parts, decl.Substring(0, colon).Trim().ToLowerInvariant(), decl.Substring(colon + 1).Trim()));
                }
            }
        }
        return rules;
    }

    /// <summary>The last rule's value for <paramref name="prop"/> whose selector matches the
    /// element at the end of <paramref name="chain"/> (ancestors first, descendant
    /// combinators only), null when none does.</summary>
    private static string? OutlineCss(OutlineParseState op, string prop, params (string tag, string[] classes)[] chain)
    {
        string? found = null;
        foreach (var (parts, p, value) in op.Rules)
        {
            if (p != prop) continue;
            if (OutlineSelectorMatches(parts, chain)) found = value;
        }
        return found;
    }

    private static bool OutlineSelectorMatches(List<(string tag, string[] classes)> parts, (string tag, string[] classes)[] chain)
    {
        var ci = chain.Length - 1;
        if (!OutlineCompoundMatches(parts[^1], chain[ci])) return false;
        var pi = parts.Count - 2;
        ci--;
        while (pi >= 0)
        {
            while (ci >= 0 && !OutlineCompoundMatches(parts[pi], chain[ci])) ci--;
            if (ci < 0) return false;
            pi--;
            ci--;
        }
        return true;
    }

    private static bool OutlineCompoundMatches((string tag, string[] classes) part, (string tag, string[] classes) element)
    {
        if (part.tag.Length > 0 && part.tag != "*" && !string.Equals(part.tag, element.tag, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in part.classes)
            if (Array.IndexOf(element.classes, c) < 0) return false;
        return true;
    }

    /// <summary>Whitespace collapsed to single spaces with the leading run dropped (a line
    /// start) and a trailing run kept as one space.</summary>
    private static string CollapseWsKeepTrailing(string s)
    {
        return Regex.Replace(s, @"[^\S\u00A0]+", " ").TrimStart(' ');
    }
}
