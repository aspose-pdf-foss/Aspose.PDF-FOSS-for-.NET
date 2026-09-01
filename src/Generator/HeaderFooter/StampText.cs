using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class HeaderFooter
{
    /// <summary>Stamps one text-bearing paragraph - a text fragment or an HTML fragment - at the band's cursor.</summary>
    private void StampText(StampParagraphsState hf, BaseParagraph para)
    {
        var ts = new StampTextState();
        ts.text = null;
        ts.fn = TextState.FontName ?? "Helvetica";
        ts.fs = hf.fontSize;
        ts.embedFont = null;
        ts.cssLeftIndent = 0.0;
        ts.cssAlign = HorizontalAlignment.None;

        if (para is TextFragment tf)
        {
            ts.text = ApplyLabelMacros(tf.Text ?? "", hf.document, hf.pageNumber);
            // The fragment's own size when the caller set one, else the size
            // its first sized segment carries (generator-style fragments hold
            // their size and face on the segment).
            var (tfFs, tfFace) = FragmentSizeAndFace(tf);
            if (tfFace?.FontName is { Length: > 0 } tfFaceName) ts.fn = tfFaceName;
            else if (tf.TextState.FontName is not null) ts.fn = tf.TextState.FontName;
            if (tfFs > 0) ts.fs = (float)tfFs;
            if (tfFace?.SourceFontData?.TtfData is { Length: > 0 }) ts.embedFont = tfFace;
        }
        else if (para is HtmlFragment htmlFrag)
        {
            if (!ResolveHtmlFragmentText(hf, ts, htmlFrag)) return;
        }

        if (string.IsNullOrWhiteSpace(ts.text)) return;

        PlaceStampText(hf, ts, para);
        // Probed footer band, text member: the fragment WRAPS at the band width,
        // breaks at its own newlines, steps ONE fontSize per line (no leading),
        // and each line's baseline sits (fontSize - descent) under its line top
        // (probed: baselines 30.592/18.592/6.592 on line tops 40/28/16
        // at 12 pt Times New Roman, descent 0.216 em).
        if (!FitStampTextToBand(hf, ts, para)) return;

        DrawStampText(hf, ts, para);
        hf.lastTextY = ts.drawY;
        hf.lastTextEndX = ts.drawX + ts.textW;
        if (!ts.inline) hf.y -= ts.fs * 1.2;
        hf.firstParagraph = false;
    }

    /// <summary>Writes the resolved text at its seat and measures the advance it took.</summary>
    private void DrawStampText(StampParagraphsState hf, StampTextState ts, BaseParagraph para)
    {
        ts.builder = new ContentStreamBuilder();
        ts.builder.SaveState();
        // A header/footer fragment's own fill colour. The band writer used to show the
        // run under whatever fill was current — always the default black — so a header
        // styled through TextState.ForegroundColor rendered black while the SAME
        // fragment placed in the page body rendered in its colour. Scoped by the
        // enclosing q…Q, exactly as the body writer scopes it.
        if (para is TextFragment colorTf && FragmentForeground(colorTf) is { } headerFore)
            ts.builder.SetFillColor(headerFore);
        if (ts.embedFont?.SourceFontData?.TtfData is { Length: > 0 } embedTtf)
        {
            // Embedded face: register (or reuse) the Type0/CID font on the page and
            // show the run as hex glyph ids. The Type0 embedder presents the program
            // under a subset-tagged BaseFont, so the saved document's fonts read
            // back as embedded subsets (FontUtilities.SubsetFonts semantics).
            var pageFontDict = ResolvePageFontDict(hf.page);
            var (embedRes, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                pageFontDict, embedTtf, ts.embedFont.FontName ?? "Embedded", ts.text!,
                stripSpacesInBaseFont: true);
            ts.builder.BeginText()
                .SetFont(embedRes, ts.fs)
                .MoveTextPosition(ts.drawX, ts.drawY)
                .ShowTextHex(hex)
                .EndText()
                .RestoreState();
        }
        else
        {
            var fontRes = EnsureFontResource(hf.page, ts.fn);
            ts.builder.BeginText()
                .SetFont(fontRes, ts.fs)
                .MoveTextPosition(ts.drawX, ts.drawY)
                .ShowText(ts.text!)
                .EndText()
                .RestoreState();
        }
        hf.page.AddContentStream(ts.builder.Build());

        try
        {
            var mw = FontRepository.TryFindFont(ts.fn)?.MeasureString(ts.text!, ts.fs) ?? 0;
            ts.textW = mw > 0 ? mw : EstimateWidth(ts.text, ts.fs);
        }
        catch { ts.textW = EstimateWidth(ts.text, ts.fs); }
    }

    /// <summary>Fits the line into the footer band and aligns it; false when the band has no room for it.</summary>
    private bool FitStampTextToBand(StampParagraphsState hf, StampTextState ts, BaseParagraph para)
    {
        if (hf.probedFooterBand && para is TextFragment && !ts.inline)
        {
            var bandRightMargin = Margin.RightTouched ? Margin.Right : hf.mLeft;
            var bandWidth = hf.page.Width - hf.mLeft - bandRightMargin;
            var descEm = FaceDescentEm(ts.fn, ts.embedFont);
            var bandLines = WrapBandLines(ts.text!, ts.fn, ts.fs, bandWidth);
            var lineB = new ContentStreamBuilder();
            lineB.SaveState();
            if (para is TextFragment bandTf && FragmentForeground(bandTf) is { } bandFore)
                lineB.SetFillColor(bandFore);
            double lastBase = hf.y, lastEnd = hf.x + ts.cssLeftIndent;
            foreach (var lnText in bandLines)
            {
                var baseline = hf.y - (ts.fs - descEm * ts.fs);
                hf.y -= ts.fs;
                if (lnText.Length == 0) continue;
                double lnW;
                try
                {
                    var mw = FontRepository.TryFindFont(ts.fn)?.MeasureString(lnText, ts.fs) ?? 0;
                    lnW = mw > 0 ? mw : EstimateWidth(lnText, ts.fs);
                }
                catch { lnW = EstimateWidth(lnText, ts.fs); }
                var lnX = ts.alignment switch
                {
                    HorizontalAlignment.Center => hf.mLeft + (bandWidth - lnW) / 2,
                    HorizontalAlignment.Right => hf.page.Width - bandRightMargin - lnW,
                    _ => hf.x + ts.cssLeftIndent,
                };
                if (ts.embedFont?.SourceFontData?.TtfData is { Length: > 0 } bandTtf)
                {
                    var bandFontDict = ResolvePageFontDict(hf.page);
                    var (bandRes, bandHex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                        bandFontDict, bandTtf, ts.embedFont.FontName ?? "Embedded", lnText,
                        stripSpacesInBaseFont: true);
                    lineB.BeginText().SetFont(bandRes, ts.fs)
                        .MoveTextPosition(lnX, baseline).ShowTextHex(bandHex).EndText();
                }
                else
                {
                    var bandFontRes = EnsureFontResource(hf.page, ts.fn);
                    lineB.BeginText().SetFont(bandFontRes, ts.fs)
                        .MoveTextPosition(lnX, baseline).ShowText(lnText).EndText();
                }
                lastBase = baseline;
                lastEnd = lnX + lnW;
            }
            lineB.RestoreState();
            hf.page.AddContentStream(lineB.Build());
            hf.lastTextY = lastBase;
            hf.lastTextEndX = lastEnd;
            hf.firstParagraph = false;
            return false;
        }
        // A centred / right member sets its own x even when it shares the line.
        if (!ts.inline || ts.alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
        {
            if (ts.alignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
            {
                double alignW;
                try
                {
                    alignW = FontRepository.TryFindFont(ts.fn)?.MeasureString(ts.text!, ts.fs)
                             ?? EstimateWidth(ts.text, ts.fs);
                }
                catch { alignW = EstimateWidth(ts.text, ts.fs); }
                var mRight = Margin.RightTouched ? Margin.Right : hf.mLeft;
                var bandRight = hf.page.Width - mRight;
                ts.drawX = ts.alignment == HorizontalAlignment.Center
                    ? hf.mLeft + (bandRight - hf.mLeft - alignW) / 2
                    : bandRight - alignW;
            }
        }
        return true;
    }

    /// <summary>Seats the line: inline continuation, first-footer-line anchoring and its alignment.</summary>
    private void PlaceStampText(StampParagraphsState hf, StampTextState ts, BaseParagraph para)
    {
        ts.inline = para is TextFragment inlineTf && inlineTf.IsInLineParagraph
            && !double.IsNaN(hf.lastTextY);
        ts.drawX = ts.inline ? hf.lastTextEndX : hf.x + ts.cssLeftIndent;
        ts.drawY = ts.inline ? hf.lastTextY : hf.y;
        // A footer's first text line: each member's box hangs its own pitch
        // (size + leading) from the line top, its baseline one descent up.
        // (Legacy bottom-up band only — the probed band seats below.)
        if (!hf.isHeader && !hf.probedFooterBand && para is TextFragment footTf && hf.firstParagraph && !ts.inline)
        {
            var (fs0, _) = FragmentSizeAndFace(footTf);
            if (fs0 <= 0) fs0 = ts.fs;
            var ls0 = FragmentLeading(footTf);
            double maxFs = fs0, maxLs = ls0;
            var selfIdx = -1;
            for (var q = 0; q < Paragraphs.Count; q++) if (ReferenceEquals(Paragraphs[q], para)) { selfIdx = q; break; }
            for (var q = selfIdx + 1; selfIdx >= 0 && q < Paragraphs.Count; q++)
            {
                if (Paragraphs[q] is not TextFragment nextTf || !nextTf.IsInLineParagraph) break;
                var (nfs, _) = FragmentSizeAndFace(nextTf);
                if (nfs <= 0) nfs = ts.fs;
                maxFs = Math.Max(maxFs, nfs);
                maxLs = Math.Max(maxLs, FragmentLeading(nextTf));
            }
            // The line top is the bottom margin plus the tallest member's size
            // LESS the tallest leading: a leaded footer line hangs that much
            // further below the margin (each member's box still hangs its own
            // pitch from the line top).
            hf.footerLineTop = hf.mBottom + maxFs - maxLs;
            ts.drawY = hf.footerLineTop - (fs0 + ls0) + FaceDescentEm(ts.fn, ts.embedFont) * fs0;
        }
        else if (!hf.isHeader && ts.inline && para is TextFragment footInlineTf && !double.IsNaN(hf.footerLineTop))
        {
            var ls1 = FragmentLeading(footInlineTf);
            ts.drawY = hf.footerLineTop - (ts.fs + ls1) + FaceDescentEm(ts.fn, ts.embedFont) * ts.fs;
        }
        ts.alignment = para is TextFragment alignTf
            ? alignTf.HorizontalAlignment != HorizontalAlignment.None
                    && alignTf.HorizontalAlignment != HorizontalAlignment.Left
                ? alignTf.HorizontalAlignment
                : alignTf.TextState.HorizontalAlignment
            : ts.cssAlign;
    }
}
