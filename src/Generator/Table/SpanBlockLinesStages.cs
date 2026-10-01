using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>The stages of the span-block lines: one paragraph.</summary>
    private bool BuildSpanBlockParagraph(SpanBlockLinesState sk, BaseParagraph paragraph)
    {
        sk.text = null;
        sk.fragFontSize = sk.defaultFontSize;
        sk.color = null;
        sk.fragBold = false;
        sk.fragAlign = sk.cellAlign;
        sk.quarterTurned = paragraph is TextFragment qt && IsQuarterTurn(qt.TextState.Rotation);
        ReadSpanBlockText(sk, paragraph);
        // A deliberately-kept blank paragraph (a styled &nbsp; spacer <p>) reserves
        // its line box in the spanning cell as vertical space.
        if (string.IsNullOrEmpty(sk.text) && paragraph is TextFragment kb && kb.CssKeepBlank)
        {
            var kfs = ResolveFragmentFontSize(kb, sk.defaultFontSize);
            sk.block.Lines.Add(new CellLine { Text = " ", FontSize = kfs, Align = sk.fragAlign });
            if (kfs > sk.maxLine) { sk.maxLine = kfs; sk.tight = kfs; }
            return true;
        }
        if (string.IsNullOrEmpty(sk.text)) return true;
        // A spanning cell's paragraph stands on the same line box a grid cell's
        // does: its declared leading is part of the line's height here too.
        sk.fragLeading = CallerLineSpacing(paragraph, sk.cell, sk.row);
        sk.fragLineBox = DeclaredCellLineBox(paragraph);
        sk.fragHangingBreakSpace = (paragraph as TextFragment)?.TextState.FormattingOptions?.HangingBreakSpace ?? false;
        var spanLineHeight = sk.fragFontSize + sk.fragLeading;
        if (spanLineHeight > sk.maxLine) { sk.maxLine = spanLineHeight; sk.tight = spanLineHeight; }
        sk.fragFirstLine = sk.block.Lines.Count;
        // A MULTI-SEGMENT fragment is how a caller mixes weight, slant, colour and
        // underline inside one line. A spanning cell draws from these lines rather
        // than from the inline layout, so laying the segments out as styled runs
        // here is what keeps the emphasis a ColSpan cell already gets.
        if (paragraph is TextFragment segTf && CountInkSegments(segTf) > 1
            && BuildSpanSegLines(segTf, sk.availWidth, sk.defaultFontSize, sk.textState,
                   sk.fragAlign, sk.fragFontSize) is { Count: > 0 } segLines)
        {
            foreach (var sl in segLines) sk.block.Lines.Add(sl);
            StampSpanBlockParagraph(sk);
            if (ParagraphMargin(paragraph) is { Top: > 0 } segM && sk.block.Lines.Count > sk.fragFirstLine)
                sk.block.Lines[sk.fragFirstLine].TopGap = segM.Top;
            return true;
        }
        // Opted-in band tables (HonorCellFontFaces): a spanning cell's serif
        // fragment draws in the embedded serif face with its real kerned
        // metrics, exactly like the grid-cell path — the Standard-14 Helvetica
        // fallback wraps wider than the serif-measured span width.
        if (HonorCellFontFaces && paragraph is TextFragment sbtf
            && IsSerifCssFamily(sbtf.TextState.Font?.FontName)
            && (sk.fragBold ? BoldSerifTtf() : SerifTtf()) is { } spanSerifTtf)
        {
            LayoutSpanBlockFaceLines(sk, paragraph, spanSerifTtf);
            StampSpanBlockParagraph(sk);
            return true;
        }
        AppendSpanBlockSegments(sk);
        StampSpanBlockParagraph(sk);
        // The fragment's own top margin separates it from the previous
        // fragment in the spanning cell (applied above its first line).
        if (paragraph.Margin is { Top: > 0 } fm && sk.block.Lines.Count > sk.fragFirstLine)
            sk.block.Lines[sk.fragFirstLine].TopGap = fm.Top;
        return true;
    }

    /// <summary>Record the leading and the line box this paragraph declared on
    /// the lines it just produced -- the same stamp the grid cells get.</summary>
    private static void StampSpanBlockParagraph(SpanBlockLinesState sk)
    {
        StampLeading(sk.block.Lines, sk.fragFirstLine, sk.fragLeading);
        StampDeclaredLineBox(sk.block.Lines, sk.fragFirstLine, sk.fragLineBox);
    }

    /// <summary></summary>
    private void AppendSpanBlockSegments(SpanBlockLinesState sk)
    {
        foreach (var segment in sk.text!.Split('\n'))
        {
            if (segment.Length == 0) continue;
            // The generator measures on the bare AFM advance; the HTML dialects keep
            // the calibrated estimate (see the cell wrap in BuildRowPlan).
            var spanMeas = GeneratorDialect && !XmlGeneratorModel
                ? new Func<string, double>(s => MeasureWidthExactAfm(s, sk.fragFontSize))
                : null;
            if (!sk.quarterTurned
                && sk.cell.IsWordWrapped
                && (spanMeas is null ? MeasureWidth(segment, sk.fragFontSize) : spanMeas(segment)) > sk.availWidth)
            {
                foreach (var l in WrapText(segment, sk.fragFontSize, sk.availWidth, spanMeas,
                             hangingBreakSpace: sk.fragHangingBreakSpace))
                    sk.block.Lines.Add(new CellLine { Text = l, FontSize = sk.fragFontSize, ForegroundColor = sk.color, Bold = sk.fragBold, Align = sk.fragAlign });
            }
            else
                sk.block.Lines.Add(new CellLine { Text = segment, FontSize = sk.fragFontSize, ForegroundColor = sk.color, Bold = sk.fragBold, Align = sk.fragAlign });
        }
    }

    /// <summary></summary>
    private void LayoutSpanBlockFaceLines(SpanBlockLinesState sk, BaseParagraph paragraph, byte[] spanSerifTtf)
    {
        if (sk.text!.IndexOf('☐') >= 0 || sk.text.IndexOf('☒') >= 0)
            sk.text = sk.text.Replace('☐', '□').Replace('☒', '□');
        foreach (var segment in sk.text.Split('\n'))
        {
            if (segment.Length == 0) continue;
            // A whitespace-only paragraph (an &nbsp; spacer <p>) keeps its
            // line box — the kerned wrap would swallow it entirely.
            if (string.IsNullOrWhiteSpace(segment.Replace(' ', ' ')))
            {
                sk.block.Lines.Add(new CellLine
                    { Text = segment, FontSize = sk.fragFontSize, ForegroundColor = sk.color, Align = sk.fragAlign });
                continue;
            }
            foreach (var l in WrapKernedLines(segment, sk.fragFontSize, spanSerifTtf, sk.availWidth))
                sk.block.Lines.Add(new CellLine
                {
                    Text = l,
                    FontSize = sk.fragFontSize,
                    ForegroundColor = sk.color,
                    Align = sk.fragAlign,
                    Type0Ttf = spanSerifTtf,
                    Type0FontName = sk.fragBold ? "Times New Roman Bold" : "Times New Roman",
                    KernTj = true,
                    KernedWidth = MeasureWidthKerned(l, sk.fragFontSize, spanSerifTtf),
                });
        }
        if (ParagraphMargin(paragraph) is { Top: > 0 } sbm && sk.block.Lines.Count > sk.fragFirstLine)
            sk.block.Lines[sk.fragFirstLine].TopGap = sbm.Top;
    }

    /// <summary></summary>
    private void ReadSpanBlockText(SpanBlockLinesState sk, BaseParagraph paragraph)
    {
        if (paragraph is TextFragment tf)
        {
            sk.text = tf.Text;
            sk.fragFontSize = ResolveCellParagraphFontSize(tf, sk.defaultFontSize, sk.cell, sk.row);
            sk.color = tf.TextState.ForegroundColor ?? sk.textState?.ForegroundColor;
            sk.fragBold = tf.TextState.IsBold || DeclaredCellBold(sk.cell, sk.row);
            if (!sk.fragBold)
                foreach (var fseg in tf.Segments)
                    if (fseg.TextState.IsBold && !string.IsNullOrEmpty(fseg.Text))
                    { sk.fragBold = true; break; }
        }
        else if (paragraph is HtmlFragment html)
        {
            sk.text = HtmlFragment.StripHtmlTags(html.HtmlContent ?? "");
            sk.color = sk.textState?.ForegroundColor;
            // The fragment's own stylesheet text-align overrides the
            // spanning-cell default (a block declared left stays left in a
            // centred span cell). Its stylesheet font-size (px) sizes the
            // lines the same way it does in a grid cell.
            var hAligned = (html.HtmlContent ?? "").Replace(" ", string.Empty);
            if (hAligned.IndexOf("text-align:left", StringComparison.OrdinalIgnoreCase) >= 0)
                sk.fragAlign = HorizontalAlignment.Left;
            else if (hAligned.IndexOf("text-align:right", StringComparison.OrdinalIgnoreCase) >= 0)
                sk.fragAlign = HorizontalAlignment.Right;
            else if (hAligned.IndexOf("text-align:center", StringComparison.OrdinalIgnoreCase) >= 0)
                sk.fragAlign = HorizontalAlignment.Center;
            var hfs = Regex.Match(html.HtmlContent ?? "", @"font-size\s*:\s*([\d.]+)\s*px",
                RegexOptions.IgnoreCase);
            if (hfs.Success && double.TryParse(hfs.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var hpx) && hpx > 0)
                sk.fragFontSize = hpx * 0.75;
        }
    }
}
