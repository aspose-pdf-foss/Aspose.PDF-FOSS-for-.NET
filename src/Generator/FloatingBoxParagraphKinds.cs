using System.Globalization;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class FloatingBox : BaseParagraph
{
    /// <summary>Floating box paragraph kinds: the text fragment, the table and the image.</summary>
    private bool TryBuildImageParagraph(FloatingBoxBuildState bx, Image image)
    {
        // Read the image bytes (stream or file).
        byte[]? data = null;
        if (image.ImageStream is not null)
        {
            var keepPos = image.ImageStream.CanSeek ? image.ImageStream.Position : -1L;
            if (image.ImageStream.CanSeek) image.ImageStream.Position = 0;
            using var mem = new System.IO.MemoryStream();
            image.ImageStream.CopyTo(mem);
            data = mem.ToArray();
            if (keepPos >= 0) image.ImageStream.Position = keepPos;
        }
        else
        {
            data = image.ReadSourceBytes();
        }
        if (data is null) return true;

        // Size: explicit Fix dimensions win; otherwise the image's natural
        // size scaled by ImageScale (the page-level flow path applies the
        // same factor to a box-contained image).
        double imgW = image.FixWidth, imgH = image.FixHeight;
        if (imgW <= 0 || imgH <= 0)
        {
            if (Document.TryGetImageNaturalSizePt(data) is (var natW, var natH))
            {
                var imScale = image.ImageScale > 0 ? image.ImageScale : 1.0;
                if (imgW <= 0) imgW = natW * imScale;
                if (imgH <= 0) imgH = natH * imScale;
            }
        }
        if (imgW <= 0 || imgH <= 0) return true;

        // Offset by the image's own margin within the box content area.
        var imLeft = image.Margin?.Left ?? 0;
        var imTop = image.Margin?.Top ?? 0;
        var imBottom = image.Margin?.Bottom ?? 0;
        bx.contentY -= imTop;
        var ix = bx.contentX + imLeft;
        var iy = bx.contentY - imgH;
        // Flush the background/border so the image draws on top of them, mirroring
        // how the nested-table branch above orders its content.
        bx.builder.RestoreState();
        bx.page.AddContentStream(bx.builder.Build());
        bx.page.AddImage(data, new Rectangle(ix, iy, ix + imgW, iy + imgH));
        bx.contentY -= imgH + imBottom;
        bx.builder = new ContentStreamBuilder();
        bx.builder.SaveState();
        return false;
    }

    /// <summary></summary>
    private void BuildTableParagraph(FloatingBoxBuildState bx, Table table)
    {
        // Position the nested table at the box's content origin. Table.Build
        // would otherwise place it at the page's top-left (its Left/Top default
        // to 0), leaving the table detached from the box background. FlowLeftOffset
        // sets the X; the BuildMultiPage startY argument sets the top edge.
        table.FlowLeftOffset = bx.contentX;
        List<byte[]> tableContents;
        if (table.IsBroken && Height > 0)
        {
            // A breakable table inside a fixed-height box is BOUNDED by the
            // box: rows stop at the box's bottom edge, and the overflow
            // continues on fresh pages where the box re-seats at the page's
            // content top with the same Height (the generator's layout —
            // 100 rows in a 100 pt box run 8 rows on page 1 and 9 rows on
            // each continuation page).
            var contTopMargin = bx.page.PageInfo?.Margin?.Top ?? 0;
            if (contTopMargin <= 0) contTopMargin = bx.marginTop;
            var contBottom = bx.pageHeight - contTopMargin - Height + bx.padBottom;
            table.ContinuationBottomOverride = contBottom > 0 ? contBottom : 0;
            tableContents = table.BuildMultiPage(bx.page, bx.contentY,
                bx.boxY + bx.padBottom, contTopMargin);
            table.ContinuationBottomOverride = 0;
            // ⚠ MEASURED, UNRESOLVED: the box's border should repeat on
            // every continuation page (measured 2026-08-26 — a 20-row table in a
            // 200x100 bordered box comes out as two pages, both bordered).
            // Prepending the chrome to these spill streams does not reach the
            // materialised page, so the source of the spill content is not this
            // list alone; left as a known gap rather than shipped unverified.
            for (var pi = 1; pi < tableContents.Count; pi++)
                LastOverflowPages.Add(tableContents[pi]);
        }
        else
            tableContents = table.BuildMultiPage(bx.page, bx.contentY);
        bx.builder.RestoreState();
        bx.page.AddContentStream(bx.builder.Build());
        if (tableContents.Count > 0) bx.page.AddContentStream(tableContents[0]);
        if (table.LastGraphDraws.Count > 0)
            foreach (var gc in table.LastGraphDraws[0])
                bx.page.AddContentStream(gc);
        if (table.LastImageDraws.Count > 0)
            foreach (var (data, rect) in table.LastImageDraws[0])
                bx.page.AddImage(data, rect);
        bx.contentY -= table.LastRenderedHeight;
        // Start a new builder for remaining paragraphs
        bx.builder = new ContentStreamBuilder();
        bx.builder.SaveState();
    }

    /// <summary></summary>
    private bool BuildTextParagraph(FloatingBoxBuildState bx, TextFragment textFragment)
    {
        var fontSize = textFragment.TextState.FontSize;
        var faceName = textFragment.TextState.FontName ?? "Helvetica";
        var fontResName = EnsureFontResource(bx.page, faceName);
        // A fragment's text carries its own hard breaks; each is a LINE of the box,
        // not part of one long run. Drawing the whole string as a single show ran it
        // off the box (and off the page) and left the newlines in the extracted text.
        var fragLines = (textFragment.Text ?? string.Empty).Split((char)10);
        // Apply foreground color if set
        if (textFragment.TextState.ForegroundColor is { } fg)
            bx.builder.SetFillColor(fg.R / 255.0, fg.G / 255.0, fg.B / 255.0);

        for (var li = 0; li < fragLines.Length; li++)
        {
            var lineText = fragLines[li].TrimEnd((char)13);
            // Move down by font size for each line (PDF text is baseline-positioned)
            bx.contentY -= fontSize;
            if (bx.contentY < bx.boxY + bx.padBottom)
            {
                // Out of box: the rest of the fragment CONTINUES rather than being
                // dropped. A fixed-height box bounds what it shows, and the remainder
                // is carried onto a fresh page where the box re-seats at
                // the page's content top with the same Height - the same rule a
                // breakable table inside a fixed-height box already follows.
                for (var lj = li; lj < fragLines.Length; lj++)
                    bx.overflowLines.Add((fragLines[lj].TrimEnd((char)13), fontSize,
                        fontResName, textFragment.TextState.ForegroundColor));
                break;
            }
            // Where this LINE's own box bottoms, per the box's alignment. Each line of
            // a multi-line fragment is a line box of its own, so the alignment and the
            // seat below are taken per line rather than once for the whole run.
            var lineBottom = bx.contentY;
            if (Height > 0)
            {
                if (VerticalAlignment == VerticalAlignment.Bottom) lineBottom = bx.boxY;
                else if (VerticalAlignment == VerticalAlignment.Center)
                {
                    lineBottom = bx.boxCentreCursor - fontSize / 2;
                    bx.boxCentreCursor = lineBottom;
                }
            }
            // The baseline rides the face's own descent above the line box bottom,
            // as every other generator seat does.
            var seatY = lineBottom + DescentEm(faceName) * fontSize;
            var lineX = bx.contentX;
            if (Width > 0 && HorizontalAlignment is HorizontalAlignment.Center or HorizontalAlignment.Right)
            {
                var runW = Aspose.Pdf.Text.TextPaginator.CreateMeasurer(faceName, fontSize, null)
                    (lineText);
                var slack = Width - runW;
                if (slack > 0)
                    lineX = bx.boxX + (HorizontalAlignment == HorizontalAlignment.Center ? slack / 2 : slack);
            }
            bx.builder.BeginText();
            bx.builder.SetFont(fontResName, fontSize);
            bx.builder.MoveTextPosition(lineX, seatY);
            bx.builder.ShowText(lineText);
            bx.builder.EndText();
        }
        // Add line spacing after the text
        var lineSpacing = textFragment.TextState.LineSpacing > 0
            ? textFragment.TextState.LineSpacing
            : fontSize * 0.2; // Default 20% of font size as inter-paragraph spacing
        bx.contentY -= lineSpacing;
        if (bx.overflowLines.Count > 0) return false;
        return true;
    }
}
