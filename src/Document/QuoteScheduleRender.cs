using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Render the class-width form letter if <paramref name="html"/> is one.
    /// False leaves the fragment to the ordinary block flow.</summary>
    private bool TryRenderQuoteScheduleFragment(string html, FlowLayout flow, Page page,
        double marginLeft, double marginBottom, HtmlLoadOptions? options)
    {
        var qs = new QuoteScheduleState();
        qs.html = html;
        qs.flow = flow;
        qs.page = page;
        qs.marginLeft = marginLeft;
        qs.marginBottom = marginBottom;
        qs.options = options;
        qs.css = QsParseCss(qs.html);
        if (qs.css.Count == 0) return false;
        qs.colClasses = 0;
        foreach (var kv in qs.css)
            if (kv.Value.width > 0 && Regex.IsMatch(qs.html,
                    @"<td\b[^>]*class\s*=\s*""[^""]*\b" + Regex.Escape(kv.Key) + @"\b",
                    RegexOptions.IgnoreCase))
                qs.colClasses++;
        if (qs.colClasses < 3) return false;

        qs.bodyPx = qs.css.TryGetValue("body", out var bodyRule) && bodyRule.width > 0
            ? bodyRule.width : QsBodyPx;
        qs.bodyW = qs.bodyPx * QsPxToPt;

        qs.blocks = QsParseBlocks(qs.html, qs.css, qs.options);
        if (qs.blocks.Count == 0) return false;

        qs.regular = SafeFindFont("Arial");
        qs.bold = SafeFindFontStyled("Arial", Text.FontStyles.Bold) ?? qs.regular;
        qs.italic = SafeFindFontStyled("Arial", Text.FontStyles.Italic) ?? qs.regular;
        qs.boldItalic = SafeFindFontStyled("Arial", Text.FontStyles.Bold | Text.FontStyles.Italic)
            ?? qs.bold;
        if (qs.regular is null) return false;

        qs.inv = CultureInfo.InvariantCulture;
        qs.left = qs.marginLeft;
        qs.contentTop = qs.flow.ContentTop;
        qs.y = qs.flow.CurrentY;                        // PDF coords, decreasing downward

        foreach (var b in qs.blocks)
        {
            RenderQuoteBlock(qs, b);
        }
        qs.flow.MoveCursorTo(qs.y);
        _ = qs.page;
        return true;
    }
}
