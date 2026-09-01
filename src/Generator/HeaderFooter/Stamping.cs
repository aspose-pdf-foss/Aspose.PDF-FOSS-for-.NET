using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
    private void StampText(Page page, string textContent, bool isHeader,
        Document? document = null, int pageNumber = 0, bool tableContent = true)
    {
        // If we have Paragraphs, render them instead of plain text
        if (Paragraphs.Count > 0)
        {
            StampParagraphs(page, isHeader, document, pageNumber, tableContent);
            return;
        }

        var fontName = TextState.FontName ?? "Helvetica";
        var fontSize = TextState.FontSize > 0 ? TextState.FontSize : 10;
        var fontResName = EnsureFontResource(page, fontName);

        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var textWidth = textContent.Length * fontSize * 0.5;

        // The plain-text stamp keeps its historical 20 pt default band for
        // untouched margins (the Paragraphs path resolves differently).
        var mLeft = Margin.LeftTouched ? Margin.Left : 20;
        var mRight = Margin.RightTouched ? Margin.Right : 20;
        var mTop = Margin.TopTouched ? Margin.Top : 20;
        var mBottom = Margin.BottomTouched ? Margin.Bottom : 20;

        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Left => mLeft,
            HorizontalAlignment.Right => pageWidth - mRight - textWidth,
            _ => (pageWidth - textWidth) / 2,
        };

        var y = isHeader
            ? pageHeight - mTop - fontSize
            : mBottom;

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        var fg = TextState.ForegroundColor;
        if (fg is not null)
            builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);

        builder.BeginText()
            .SetFont(fontResName, fontSize)
            .MoveTextPosition(x, y)
            .ShowText(textContent)
            .EndText()
            .RestoreState();

        page.AddContentStream(builder.Build());
    }

    /// <summary>Apply CSS <c>text-transform</c> declared on inline elements to their
    /// text nodes (uppercase/lowercase), so the flattened tag-stripped render keeps
    /// the authored casing ("Planning and Zoning" styled uppercase reads back as
    /// "PLANNING AND ZONING").</summary>
    private static string ApplyTextTransforms(string html)
    {
        return System.Text.RegularExpressions.Regex.Replace(html,
            @"(?is)<(\w+)\b[^>]*style\s*=\s*(['""])[^'""]*?text-transform\s*:\s*(uppercase|lowercase)[^'""]*\2[^>]*>(.*?)</\1>",
            m =>
            {
                var upper = m.Groups[3].Value.Equals("uppercase", StringComparison.OrdinalIgnoreCase);
                return System.Text.RegularExpressions.Regex.Replace(
                    m.Groups[4].Value, @"(?<=^|>)[^<>]+(?=<|$)",
                    t => upper ? t.Value.ToUpperInvariant() : t.Value.ToLowerInvariant());
            });
    }

    /// <summary>A fragment's fill colour: its own ForegroundColor, else the first segment
    /// that carries one. Mirrors <see cref="FragmentSizeAndFace"/>'s fallback — a caller
    /// that styles the segments rather than the fragment means the same thing.</summary>
    private static Aspose.Pdf.Color? FragmentForeground(TextFragment tf)
    {
        if (tf.TextState.ForegroundColor is { } own) return own;
        foreach (var seg in tf.Segments)
            if (seg.TextState.ForegroundColor is { } segColor) return segColor;
        return null;
    }

    /// <summary>Split a band fragment's text into drawn lines: hard breaks at its
    /// own newlines, then a greedy word wrap at the band width in the face's real
    /// metrics (the probed band wraps exactly where the era line broke).</summary>
    private static List<string> WrapBandLines(string text, string fontName, double fontSize, double bandWidth)
    {
        var lines = new List<string>();
        var face = FontRepository.TryFindFont(fontName);
        double Width(string s)
        {
            try
            {
                var w = face?.MeasureString(s, fontSize) ?? 0;
                return w > 0 ? w : EstimateWidth(s, fontSize);
            }
            catch { return EstimateWidth(s, fontSize); }
        }
        foreach (var hard in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = "";
            foreach (var word in hard.Split(' '))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Width(candidate) > bandWidth)
                {
                    lines.Add(line);
                    line = word;
                }
                else line = candidate;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>A fragment's leading: its own LineSpacing, else its first segment's.</summary>
    private static double FragmentLeading(TextFragment tf)
    {
        if (tf.TextState.LineSpacing > 0) return tf.TextState.LineSpacing;
        foreach (var seg in tf.Segments)
            if (seg.TextState.LineSpacing > 0) return seg.TextState.LineSpacing;
        return 0;
    }

    /// <summary>Descent of the face a band member draws in, as a fraction of its
    /// size: the embedded face's own, else the Standard-14 face's.</summary>
    private static double FaceDescentEm(string fontName, Aspose.Pdf.Text.Font? embedFont)
    {
        if (embedFont?.SourceFontData?.TtfData is { Length: > 0 } ttf)
        {
            var (_, descent, _, _) = FontRepository.ReadTtfMetrics(ttf);
            if (descent != 0) return Math.Abs(descent) / 1000.0;
        }
        var d = Aspose.Pdf.Text.Standard14Fonts.GetDescent(Aspose.Pdf.Text.TextBuilder.MapToStandard14Public(
            new Aspose.Pdf.Text.TextState { FontName = fontName }));
        return Math.Abs(d) / 1000.0;
    }

    /// <summary>A `cm` operator mapping the page's VISUAL (rotation-adjusted) space — where
    /// header/footer content is laid out using <c>page.Width</c>/<c>page.Height</c> — into raw
    /// page-content space, so content drawn for a <c>/Rotate 90/180/270</c> page appears upright
    /// at its laid-out position. Returns null for an unrotated page (Wm/Hm = raw MediaBox dims;
    /// same mapping the watermark-on-rotated-page path uses).</summary>
    private static string? VisualToRawRotationCm(Page page)
    {
        var mb = page.MediaBox;
        var wm = mb.Width.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        var hm = mb.Height.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
        return page.Rotate switch
        {
            Rotation.on90 => $"0 1 -1 0 {wm} 0 cm",
            Rotation.on180 => $"-1 0 0 -1 {wm} {hm} cm",
            Rotation.on270 => $"0 -1 1 0 0 {hm} cm",
            _ => null,
        };
    }

    /// <summary>Walk a HeaderFooter's Paragraphs tree (descending into Tables/
    /// Rows/Cells and into FloatingBox/HeaderFooter containers) and emit one
    /// LinkAnnotation per TextFragment carrying a Hyperlink. Rects are
    /// approximate -- only the destination is asserted,
    /// not the rect -- but staying near the header/footer band
    /// keeps the click target plausible.</summary>
    private static void EmitNestedHyperlinks(Page page, Paragraphs paragraphs,
        double x, double y, double fontSize)
    {
        foreach (var para in paragraphs)
            EmitFromParagraph(page, para, x, y, fontSize);
    }

    private static void EmitFromParagraph(Page page, BaseParagraph para,
        double x, double y, double fontSize)
    {
        if (para is TextFragment tf)
        {
            if (tf.HyperlinkValue is { } h)
                EmitLinkAt(page, x, y, fontSize, EstimateWidth(tf.Text, fontSize), h);
            foreach (var seg in tf.Segments)
                if (seg.Hyperlink is { } sh)
                    EmitLinkAt(page, x, y, fontSize, EstimateWidth(seg.Text, fontSize), sh);
            return;
        }
        if (para is Table table)
        {
            foreach (var row in table.Rows)
                foreach (var cell in row.Cells)
                    foreach (var inner in cell.Paragraphs)
                        EmitFromParagraph(page, inner, x, y, fontSize);
        }
    }

    private static double EstimateWidth(string? text, double fontSize) =>
        (text?.Length ?? 0) * fontSize * 0.5;

    private static void EmitLinkAt(Page page, double x, double y, double fontSize,
        double width, Hyperlink hyperlink)
    {
        var rect = new Rectangle(x, y - fontSize, x + width, y);
        if (hyperlink is LocalHyperlink lh && lh.TargetPageNumber > 0)
        {
            page.Annotations.AddLinkAnnotation(rect,
                new Annotations.GoToAction(
                    new Annotations.XYZExplicitDestination(lh.TargetPageNumber, 0, 0, 0)));
        }
        else if (hyperlink is WebHyperlink wh && !string.IsNullOrEmpty(wh.Url))
        {
            page.Annotations.AddLinkAnnotation(rect, wh.Url);
        }
    }

    /// <summary>Find the first CSS <c>font-family</c> declared in the fragment's HTML
    /// that resolves (via FontRepository, including FolderFontSource registrations) to a
    /// face with a real font program. Returns null when nothing resolves — the caller
    /// then keeps the Standard-14 path.</summary>
    private static Aspose.Pdf.Text.Font? ResolveDeclaredFont(string html)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
            html, @"font-family\s*:\s*[""']{0,2}([^;""'}]+)"))
        {
            var family = m.Groups[1].Value.Trim();
            if (family.Length == 0) continue;
            try
            {
                var f = Aspose.Pdf.Text.FontRepository.TryFindFont(family);
                if (f?.SourceFontData?.TtfData is { Length: > 0 }) return f;
            }
            catch { /* unknown family: try the next declaration */ }
        }
        return null;
    }

    /// <summary>The page's /Resources /Font dictionary, created on demand.</summary>
    private static PdfDictionary ResolvePageFontDict(Page page)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) { resources = new PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) { fontDict = new PdfDictionary(); resources.Set("Font", fontDict); }
        return fontDict;
    }

    /// <summary>
    /// Ensure the Standard 14 font is registered in the page's /Resources /Font dictionary.
    /// Returns the resource name (e.g. "F1").
    /// </summary>
    private static string EnsureFontResource(Page page, string baseFontName)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary;
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }

        var fontDict = resources.Get("Font") as PdfDictionary;
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        // Check if this base font is already registered
        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary;
            if (entry is null)
            {
                var raw = fontDict.Get(key);
                entry = page.Reader.ResolveDict(raw);
            }

            if (entry is not null)
            {
                var existing = entry.GetName("BaseFont");
                if (string.Equals(existing, baseFontName, StringComparison.Ordinal))
                    return key;
            }
        }

        // Find a unique resource name in the header/footer's own "HF" namespace so it
        // never collides with the body's F1..Fn fonts on a shared (overflow) page —
        // otherwise the footer could claim e.g. /F2 and the body text drawn with /F2
        // would render in the footer's face (wrong glyph metrics).
        var name = "HF1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"HF{++counter}";

        // Create the font dictionary
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        fontDict.Set(name, font);

        return name;
    }
}
