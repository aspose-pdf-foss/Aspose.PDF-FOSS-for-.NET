using Aspose.Pdf.Text;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Renders the visible page content for a document authored purely through
/// <see cref="ITaggedContent"/> (headers, paragraphs and their inline spans).
/// A from-scratch tagged document starts with no pages; without a render pass
/// it would save with an empty page canvas even though the logical structure
/// carries the text. This flow layout places each block element into a single
/// A4 page, honouring a paragraph's authored margin (Left/Right indent the
/// column, Top adds space above the block), so the saved PDF shows the text
/// the caller supplied via SetText.
/// </summary>
internal static class TaggedContentRenderer
{
    // A4 portrait with the default authoring margins the reference layout uses.
    private const double PageWidth = 595.0;
    private const double PageHeight = 842.0;
    private const double MarginLeft = 90.0;
    private const double MarginRight = 90.0;
    private const double MarginTop = 72.0;

    /// <summary>Try to lay out and render the authored structure tree onto a new
    /// page. Never throws — on any failure the document is left as-is (an empty
    /// canvas), preserving the previous behaviour.</summary>
    internal static void TryRender(Document document, LS.StructureElement root)
    {
        try
        {
            var blocks = new List<LS.StructureElement>();
            CollectBlocks(root, blocks);
            if (blocks.Count == 0) return;
            // Only render when there is genuine text to draw.
            bool anyText = false;
            foreach (var b in blocks)
                if (!string.IsNullOrEmpty(GetBlockText(b))) { anyText = true; break; }
            if (!anyText) return;

            var page = document.Pages.Add(PageWidth, PageHeight);
            var builder = new TextBuilder(page);

            double contentTop = PageHeight - MarginTop;   // 770
            double contentLeft = MarginLeft;              // 90
            double contentRight = PageWidth - MarginRight; // 505
            double flowTop = contentTop;

            foreach (var block in blocks)
            {
                if (block is LS.HeaderElement header)
                    RenderHeader(builder, header, contentLeft, ref flowTop);
                else if (block is LS.ParagraphElement para)
                    RenderParagraph(builder, para, contentLeft, contentRight, ref flowTop);
                else
                    RenderParagraph(builder, block, contentLeft, contentRight, ref flowTop);
            }
        }
        catch
        {
            // Rendering is best-effort; the structure tree is already linked.
        }
    }

    /// <summary>Walk the tree collecting text block elements (headers,
    /// paragraphs) in document order; container elements (Part/Sect/Div/Document)
    /// are transparent and recursed into.</summary>
    private static void CollectBlocks(LS.StructureElement element, List<LS.StructureElement> blocks)
    {
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case LS.HeaderElement:
                case LS.ParagraphElement:
                    blocks.Add(child);
                    break;
                case LS.PartElement:
                case LS.SectElement:
                case LS.DivElement:
                case LS.DocumentElement:
                    CollectBlocks(child, blocks);
                    break;
                default:
                    // A text-bearing leaf directly under the root (rare) is drawn
                    // as its own block; inline spans under a paragraph are handled
                    // by RenderParagraph, not here.
                    if (!string.IsNullOrEmpty(child.ActualText))
                        blocks.Add(child);
                    break;
            }
        }
    }

    private static int HeaderLevel(LS.StructureElement header)
    {
        var s = header.StructureType; // "H1".."H6" or "H"
        if (s.Length == 2 && s[0] == 'H' && s[1] >= '1' && s[1] <= '6')
            return s[1] - '0';
        return 1;
    }

    private static void RenderHeader(TextBuilder builder, LS.HeaderElement header,
        double x, ref double flowTop)
    {
        var text = GetBlockText(header);
        if (string.IsNullOrEmpty(text)) return;

        int level = HeaderLevel(header);
        double size = level switch { 1 => 18, 2 => 16, 3 => 14, 4 => 12, 5 => 11, _ => 10 };

        var fragment = new TextFragment(text);
        fragment.TextState.FontName = "Helvetica-Bold";
        fragment.TextState.FontSize = (float)size;
        // Reference headings render in a dark grey.
        fragment.TextState.ForegroundColor = Color.FromArgb(89, 89, 89);
        double baseline = flowTop - size; // glyph top ≈ flowTop
        fragment.Position = new Position(x, baseline);
        builder.AppendText(fragment);

        // Consume the header line plus a little space before the next block. The
        // 1.95x factor reproduces the reference gap between an H1 and the first
        // body paragraph (contentTop 770 → natural paragraph top ≈ 735).
        flowTop -= size * 1.95;
    }

    private readonly struct Run
    {
        public readonly string Text;
        public readonly Font? Font;
        public readonly float Size;
        public readonly bool Bold;
        public readonly bool Italic;
        public Run(string text, Font? font, float size, bool bold, bool italic)
        { Text = text; Font = font; Size = size; Bold = bold; Italic = italic; }
    }

    private static void RenderParagraph(TextBuilder builder, LS.StructureElement para,
        double contentLeft, double contentRight, ref double flowTop)
    {
        // Base font/size for the paragraph (inline spans inherit it unless they
        // carry their own style).
        var baseFont = para.StructureTextState.Font;
        float baseSize = para.StructureTextState.FontSize > 0 ? para.StructureTextState.FontSize : 12f;

        // Gather inline runs. A paragraph with inline children (spans/quotes)
        // flows them in order; a bare paragraph uses its own text.
        var runs = new List<Run>();
        if (para.ChildElements.Count > 0)
        {
            foreach (var child in para.ChildElements)
            {
                var t = child.ActualText;
                if (string.IsNullOrEmpty(t)) continue;
                var st = child.StructureTextState;
                bool bold = (st.FontStyle & Aspose.Pdf.Text.FontStyles.Bold) != 0;
                bool italic = (st.FontStyle & Aspose.Pdf.Text.FontStyles.Italic) != 0;
                var font = st.Font ?? baseFont;
                float size = st.FontSize > 0 ? st.FontSize : baseSize;
                runs.Add(new Run(t, ResolveStyledFont(font, bold, italic), size, bold, italic));
            }
        }
        else
        {
            var t = para.ActualText;
            if (!string.IsNullOrEmpty(t))
                runs.Add(new Run(t, baseFont, baseSize, false, false));
        }
        if (runs.Count == 0) return;

        // Resolve the paragraph's authored position margin (AdjustPosition or
        // StructureTextState.MarginInfo). MarginInfo is (Left, Bottom, Right, Top).
        var margin = para._positionMargin ?? para.StructureTextState.MarginInfo;
        double left = contentLeft + (margin?.Left ?? 0);
        double right = contentRight - (margin?.Right ?? 0);
        double top = margin?.Top ?? 0;
        if (right <= left + 10) right = left + 10; // guard degenerate widths
        double width = right - left;

        double lineSize = baseSize;
        double pitch = lineSize; // reference body uses single (font-size) leading
        // Block top: natural flow top pushed down by the Top margin.
        double blockTop = flowTop - top;
        double firstBaseline = blockTop - lineSize * 0.75; // glyph top ≈ blockTop

        // Tokenise into (word, run) then greedily wrap into lines.
        var tokens = new List<(string word, Run run)>();
        foreach (var run in runs)
        {
            foreach (var w in run.Text.Split(' '))
                if (w.Length > 0) tokens.Add((w, run));
        }

        double baseline = firstBaseline;
        double x = left;
        // Accumulate contiguous same-style words into one fragment per style-run
        // on a line, flushing when the style changes, a line wraps, or at the end.
        var pieceText = new System.Text.StringBuilder();
        Run pieceRun = default;
        double pieceX = left;
        bool piecePending = false;

        void Flush()
        {
            if (!piecePending || pieceText.Length == 0) { piecePending = false; pieceText.Clear(); return; }
            EmitPiece(builder, pieceText.ToString(), pieceRun, pieceX, baseline);
            pieceText.Clear();
            piecePending = false;
        }

        int lineCount = 1;
        foreach (var (word, run) in tokens)
        {
            double wordW = Measure(run, word);
            double spaceW = piecePending || x > left + 0.01 ? Measure(run, " ") : 0;
            if (spaceW <= 0 && (piecePending || x > left + 0.01)) spaceW = run.Size * 0.25;

            // Wrap if this word would overflow the column (but always place at
            // least one word per line).
            if (x > left + 0.01 && x + spaceW + wordW > right + 0.5)
            {
                Flush();
                baseline -= pitch;
                x = left;
                spaceW = 0;
                lineCount++;
            }

            bool sameStyle = piecePending && SameStyle(pieceRun, run);
            if (!sameStyle)
            {
                Flush();
                pieceRun = run;
                pieceX = x + spaceW;
                piecePending = true;
            }
            else if (spaceW > 0)
            {
                pieceText.Append(' ');
            }
            pieceText.Append(word);
            x += spaceW + wordW;
        }
        Flush();

        flowTop = baseline - pitch;
    }

    private static bool SameStyle(Run a, Run b)
        => a.Bold == b.Bold && a.Italic == b.Italic && a.Size == b.Size
           && ReferenceEquals(a.Font, b.Font);

    private static void EmitPiece(TextBuilder builder, string text, Run run, double x, double baseline)
    {
        if (string.IsNullOrEmpty(text)) return;
        var fragment = new TextFragment(text);
        if (run.Font is not null)
            fragment.TextState.Font = run.Font;
        else
        {
            fragment.TextState.FontName = "Helvetica";
            fragment.TextState.FontStyle =
                (run.Bold ? Aspose.Pdf.Text.FontStyles.Bold : 0) |
                (run.Italic ? Aspose.Pdf.Text.FontStyles.Italic : 0);
        }
        fragment.TextState.FontSize = run.Size;
        fragment.Position = new Position(x, baseline);
        builder.AppendText(fragment);
    }

    /// <summary>Resolve the styled face for an inline run: when the run is
    /// bold/italic, prefer the matching family variant so the glyph metrics
    /// (and thus wrapping) track the reference; fall back to the base font.</summary>
    private static Font? ResolveStyledFont(Font? baseFont, bool bold, bool italic)
    {
        if (baseFont is null) return null;
        if (!bold && !italic) return baseFont;
        var style = (bold ? Aspose.Pdf.Text.FontStyles.Bold : 0) |
                    (italic ? Aspose.Pdf.Text.FontStyles.Italic : 0);
        try
        {
            var styled = FontRepository.FindFont(baseFont.FontName, style);
            if (styled is not null) return styled;
        }
        catch { }
        return baseFont;
    }

    private static double Measure(Run run, string s)
    {
        try
        {
            // Measure through the raw TTF glyph advances (SourceFontData) — the
            // Font.MeasureString path needs a PDF font dict, which a FindFont-loaded
            // face lacks, so it would fall back to a 1-em default per glyph.
            var fd = run.Font?.SourceFontData;
            if (fd is not null)
                return fd.MeasureString(s, run.Size);
        }
        catch { }
        // Standard-14 fallback metric.
        double w = 0;
        foreach (var ch in s)
        {
            var cw = Standard14Fonts.GetWidth(run.Bold ? "Helvetica-Bold" : "Helvetica", ch < 256 ? ch : '?');
            w += (cw >= 0 ? cw : 500) * run.Size / 1000.0;
        }
        return w;
    }

    private static string GetBlockText(LS.StructureElement block)
    {
        if (!string.IsNullOrEmpty(block.ActualText)) return block.ActualText;
        var sb = new System.Text.StringBuilder();
        foreach (var child in block.ChildElements)
        {
            if (!string.IsNullOrEmpty(child.ActualText))
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(child.ActualText);
            }
        }
        return sb.ToString();
    }
}
