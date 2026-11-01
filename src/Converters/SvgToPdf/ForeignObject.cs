using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Converters;

internal static partial class SvgToPdfConverter
{
    // The sans-serif stand-in's ascent and descent in em, which a browser's normal line box is
    // built on, and the normal line height when the blocks state none.
    private const double HtmlAscentEm = 0.905;
    private const double HtmlDescentEm = 0.212;
    private const double HtmlNormalLineHeightEm = 1.15;

    // The namespace of the html a foreignObject carries; html parsed from a page carries none.
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";

    // The inline formatting elements a plain-text block may hold around its text.
    private static readonly string[] InlineTextElements =
        { "b", "i", "u", "em", "strong", "span", "font", "br", "a", "small", "sub", "sup" };

    /// <summary>
    /// A foreignObject whose HTML holds nothing but text - one block, or blocks nested one in another,
    /// down to the one holding the text - is drawn as that text at the top of its box, in the style the
    /// blocks resolve to (the sheet's class rules and their inline styles): the first line's baseline
    /// where a browser seats it, half the line's leading and one ascent under the top, and the line
    /// aligned as text-align says within the box's width. HTML of any other shape is not laid out: its
    /// children are walked as before, so an svg document it wraps still draws and the rest draws nothing.
    /// </summary>
    private static void RenderForeignObjectText(XmlElement elem, Ctx ctx, Dictionary<string, string> style, double[] ctm)
    {
        var blocks = PlainTextBlocks(elem);
        if (blocks is null)
        {
            RenderChildren(elem, ctx, style, ctm);
            return;
        }
        var text = CollapseWs(elem.InnerText);
        if (text.Length == 0) return;
        var blockStyle = style;
        foreach (var block in blocks) blockStyle = ResolveStyle(block, blockStyle, ctx);
        // HTML text takes the colour property; the SVG run reads its fill.
        blockStyle["fill"] = Prop(blockStyle, "color");

        var sb = ctx.Surface.Sb;
        sb.Append("q\n");
        var inner = ApplyTransform(elem, sb, ctm);
        ApplyClipPath(style, ctx);
        var fontSize = ParseLength(Prop(blockStyle, "font-size"));
        if (fontSize <= 0) fontSize = ParseLength(Prop(InitialStyle, "font-size"));
        var lineHeight = double.TryParse(blockStyle.GetValueOrDefault("line-height"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var ratio) && ratio > 0 ? ratio : HtmlNormalLineHeightEm;
        var x = GetLen(elem, "x", ctx.VpW);
        var width = GetLen(elem, "width", ctx.VpW);
        var align = Prop(blockStyle, "text-align");
        var sx = new SvgTextRenderState();
        sx.elem = elem;
        sx.ctx = ctx;
        sx.style = blockStyle;
        sx.ctm = inner;
        sx.sb = sb;
        sx.curY = GetLen(elem, "y", ctx.VpH) + ((lineHeight - HtmlAscentEm - HtmlDescentEm) / 2 + HtmlAscentEm) * fontSize;
        sx.curX = align switch { "center" => x + width / 2, "right" => x + width, _ => x };
        blockStyle["text-anchor"] = align switch { "center" => "middle", "right" => "end", _ => "start" };
        EmitSvgTextRun(sx, text, blockStyle);
        sb.Append("Q\n");
    }

    /// <summary>The chain of HTML blocks of a foreignObject that holds plain text: each level one
    /// element (style sheets and scripts aside) down to the one holding the text, which may hold inline
    /// formatting around it. Null when the HTML has any other structure.</summary>
    private static List<XmlElement>? PlainTextBlocks(XmlElement foreignObject)
    {
        var blocks = new List<XmlElement>();
        var node = foreignObject;
        while (true)
        {
            var elements = node.ChildNodes.OfType<XmlElement>()
                .Where(e => e.LocalName is not ("style" or "script")).ToList();
            var hasText = node.ChildNodes.OfType<XmlCharacterData>()
                .Any(t => t is not XmlComment && t.Value?.Trim().Length > 0);
            if (node != foreignObject && elements.TrueForAll(e => Array.IndexOf(InlineTextElements, e.LocalName) >= 0))
                return blocks;
            if (hasText || elements.Count != 1) return null;
            blocks.Add(elements[0]);
            node = elements[0];
        }
    }

    /// <summary>The plain text a foreignObject holds (see <see cref="PlainTextBlocks"/>), or null.</summary>
    private static string? ForeignObjectText(XmlElement foreignObject)
    {
        if (PlainTextBlocks(foreignObject) is null) return null;
        var text = CollapseWs(foreignObject.InnerText);
        return text.Length > 0 ? text : null;
    }
}
