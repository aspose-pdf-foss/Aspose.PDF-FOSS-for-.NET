using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The inline tokens the block arms leave over: the line breaks, the emphasis and image tags, and the anchors that open a link over the run.</summary>
    private static void ParseInlineAndAnchorToken(ParseBlocksState pb, Token tok)
    {
        if (pb.tagCmp == "small")
            MarkInlineSize(pb.styleStack, factor: 0.85);
        else if (pb.tagCmp is "sub" or "sup")
            // The UA sheet gives sub and sup `font-size: smaller`, which steps the size down
            // one place on the scale. Unlike <small> this cannot be a whole-block promotion:
            // the size belongs to the marked text only, and the text after the end tag is the
            // block's own size again — so it opens a RUN the close tag ends.
            pb.openSubSupRuns.Push(pb.currentText.Length);
        else if (pb.tagCmp is "span" or "font")
        {
            ParseSpanOrFontTag(pb, tok);
        }
        else if (pb.tagCmp is "label")
        {
            // A label is an inline box like a span, and the reference styles it the
            // same way, but it owns none of a span's block behaviour (the display:block
            // ledger, the absolute column, the word-mail saves). It takes a depth of its
            // own so its class runs close on its own end tag, and nothing else.
            pb.spanDepth++;
            OpenInlineClassTypographyRuns(pb, tok);
        }
        else if (pb.tag.Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            // <a href> opens an inline hyperlink span; record the start so the
            // text up to the matching </a> becomes a Link annotation.
            string? href = null;
            tok.Attributes?.TryGetValue("href", out href);
            if (!string.IsNullOrEmpty(href))
                pb.openAnchors.Push((pb.currentText.Length, href));
            // The sheet's `a { color; font-family }` rule dresses the anchor's run the way a
            // <font color face> would (probed: `a { color: pink; font-family: 'Arial Black' }`
            // draws the anchor ArialBlack 12 #ffc0cb at its UA size); the close restores it.
            if (pb.browserUa && !tok.IsSelfClosing && pb.css is not null
                && pb.css.TryGetValue("a", out var uaARule))
            {
                var aTop = pb.styleStack.Peek();
                string? aFam = null; Color? aFore = null;
                if (uaARule.TryGetValue("font-family", out var aFamDecl)
                    && FirstFontFamily(aFamDecl) is { Length: > 0 } aFamName && WinMetricsFor(aFamName) is not null)
                    aFam = aFamName;
                if (uaARule.TryGetValue("color", out var aColDecl) && ParseCssColor(aColDecl.Trim()) is { } aCol)
                    aFore = aCol;
                if (aFam is not null || aFore is not null)
                {
                    Flush(pb, pb.controlBoxes, pb.uaBlockRhythm, pb.articleRhythm, pb.spanPtTypography, false, aTop);
                    pb.uaFontSaves.Push((aTop.FontSize, aTop.ForeColor, aTop.FontFamily));
                    pb.uaAnchorFrames++;
                    if (aFam is not null) aTop.FontFamily = aFam;
                    if (aFore is not null) aTop.ForeColor = aFore;
                }
            }
        }
    }
}
