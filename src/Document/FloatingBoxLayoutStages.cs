using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Floating box layout stages: the chromeless default placement and the positioned placement arms.</summary>
    private bool PlaceFloatingBox(FloatingBoxParagraphState fx)
    {
        if (fx.fbox.PositioningMode == ParagraphPositioningMode.Default
            && fx.fbox.Left == 0 && fx.fbox.Top == 0 && (!fx.fboxIsVisibleBox || fx.fboxChromeOnFlow))
        {
            PlaceDissolvedBox(fx);
            if (fx.fbox.Margin?.Bottom > 0) fx.flow.AdvanceY(fx.fbox.Margin.Bottom);
        }
        else if (fx.fbox.PositioningMode == ParagraphPositioningMode.Default
                 && fx.fbox.Height <= 0 && !fx.fboxIsVisibleBox
                 && fx.fbox.Paragraphs.Count > 0
                 && fx.fbox.Paragraphs.All(p => p is Text.TextFragment))
        {
            // A flow box with Left/Top offsets and no Height is a column of text
            // that starts Top below the cursor at margin + Left, wraps to its
            // Width and runs down to the page's bottom margin; the rest continues
            // on fresh pages at the content top, still at margin + Left (the Top
            // offset is not re-applied). Laid through the flow as one column.
            if (fx.fbox.Top > 0) fx.flow.AdvanceY(fx.fbox.Top);
            var offLeft = fx.marginLeft + fx.fbox.Left;
            var offWidth = fx.fbox.Width > 0 ? fx.fbox.Width : fx.page.Width - fx.marginRight - offLeft;
            LayoutDissolvedFloatingBox(fx.fbox, fx.flow, fx.page, fx.tocEntries, fx.pl, fx.headingAutoCounters,
                fx.overflowPages, fx.marginLeft, fx.marginRight, fx.marginBottom,
                (new[] { offLeft }, new[] { offWidth }));
            if (fx.fbox.Margin?.Bottom > 0) fx.flow.AdvanceY(fx.fbox.Margin.Bottom);
        }
        else if (fx.fbox.PositioningMode == ParagraphPositioningMode.Default
                 && fx.fbox.Left == 0 && fx.fbox.Top == 0 && fx.fbox.Height > 0 && fx.fbox.Width > 0
                 && fx.fbox.BackgroundImage is null
                 && fx.fbox.ColumnInfo is { ColumnCount: > 1 }
                 && fx.fbox.Paragraphs.All(p => p is Text.TextFragment))
        {
            // A sized, multi-column text box at the flow cursor: its background
            // and border paint Width x Height at the cursor, the paragraphs pour
            // down the columns (each column at box left + the widths and spacing
            // before it, cut at the page's right content edge) from Padding.Top
            // below the box top, and the flow resumes one Height below the box
            // top (measured 2026-08-23).
            var cbLeft = fx.marginLeft;
            var cbTop = fx.flow.CurrentY;
            var cbContent = new Content.ContentStreamBuilder();
            cbContent.SaveState();
            if (fx.fbox.BackgroundColor is { } cbBg)
                cbContent.SetFillColor(cbBg).Rectangle(cbLeft, cbTop - fx.fbox.Height, fx.fbox.Width, fx.fbox.Height).Fill();
            if (fx.fbox.Border is { } cbBorder && cbBorder.HasAnySide)
                FloatingBox.StrokeBorder(cbContent, cbBorder, cbLeft, cbTop - fx.fbox.Height, fx.fbox.Width, fx.fbox.Height);
            cbContent.RestoreState();
            fx.flow.InjectContentAtCursor(cbContent.Build());
            var cbPad = fx.fbox.Padding;
            if (cbPad?.Top > 0) fx.flow.AdvanceY(cbPad.Top);
            var (cbLefts, cbWidths) = BuildColumnGeometry(fx.fbox.ColumnInfo, cbLeft + (cbPad?.Left ?? 0),
                fx.page.Width - fx.marginRight - cbLeft - (cbPad?.Left ?? 0));
            LayoutDissolvedFloatingBox(fx.fbox, fx.flow, fx.page, fx.tocEntries, fx.pl, fx.headingAutoCounters,
                fx.overflowPages, fx.marginLeft, fx.marginRight, fx.marginBottom, (cbLefts, cbWidths));
            fx.flow.MoveCursorTo(Math.Min(fx.flow.CurrentY, cbTop - fx.fbox.Height));
            if (fx.fbox.Margin?.Bottom > 0) fx.flow.AdvanceY(fx.fbox.Margin.Bottom);
        }
        else if (fx.fbox.PositioningMode != ParagraphPositioningMode.Default
                 || fx.fbox.Left != 0 || fx.fbox.Top != 0)
        {
            PlaceOutsideFlow(fx);
        }
        else
        {
            PlaceRelativeBox(fx);
        }
        return true;
    }

    /// <summary></summary>
    private bool TryLayoutDefaultFlow(FloatingBoxParagraphState fx)
    {
        if (fx.fbox.PositioningMode == ParagraphPositioningMode.Default
            && fx.fbox.Left == 0 && fx.fbox.Top == 0
            && fx.fbox.Paragraphs.Count == 1
            && fx.fbox.Paragraphs[0] is HtmlFragment colFrag
            && Converters.HtmlToPdfConverter.TryParseColumnArticle(
                colFrag.HtmlContent) is { } colArt)
        {
            const double caFs = 12.0, caLine = 13.5, caAbove = 10.7989;
            var caPad = colArt.PadPx * 0.75;
            var caContentW = colArt.WidthPx * 0.75;
            var caContentH = colArt.HeightPx * 0.75;
            var caLeft = fx.marginLeft;
            var caTop = fx.flow.CurrentY;                       // pdf y of the box top
            var caGap = caFs;                                // CSS 'normal' column gap = 1 em
            var caColW = (caContentW - caGap * (colArt.Columns - 1)) / colArt.Columns;
            var caFace = "Times-Roman";
            var caRes = Table.RegisterFont(fx.flow.CurrentPage, caFace);

            double CaWidth(string t)
            {
                if (t.Length == 0) return 0;
                try
                {
                    return Text.FontRepository.TryFindFont(caFace)?.MeasureString(t, caFs)
                           ?? t.Length * caFs * 0.5;
                }
                catch { return t.Length * caFs * 0.5; }
            }

            // wrap every paragraph to the column width, remembering which
            // line ends its paragraph (those never stretch)
            var caLines = new List<(List<string> Words, bool Last)>();
            foreach (var text in colArt.Paragraphs)
            {
                var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var line = new List<string>();
                var w = 0.0;
                var spaceW = CaWidth(" ");
                foreach (var word in words)
                {
                    var ww = CaWidth(word);
                    var need = line.Count == 0 ? ww : w + spaceW + ww;
                    if (line.Count > 0 && need > caColW + 0.01)
                    {
                        caLines.Add((new List<string>(line), false));
                        line.Clear();
                        w = ww;
                    }
                    else w = need;
                    line.Add(word);
                }
                if (line.Count > 0) caLines.Add((new List<string>(line), true));
            }

            var caB = new Content.ContentStreamBuilder();
            caB.SaveState();
            if (colArt.Background is { } caBg)
                caB.SetFillColor(caBg)
                   .Rectangle(caLeft, caTop - (caContentH + 2 * caPad),
                              caContentW + 2 * caPad, caContentH + 2 * caPad)
                   .Fill();
            caB.SetFillGray(0);

            var caPerCol = (int)Math.Floor(caContentH / caLine);
            var caIdx = 0;
            for (var col = 0; col < colArt.Columns && caIdx < caLines.Count; col++)
            {
                var colLeft = caLeft + caPad + col * (caColW + caGap);
                var y = caTop - caPad - caAbove;              // first baseline
                for (var k = 0; k < caPerCol && caIdx < caLines.Count; k++, caIdx++)
                {
                    var (words, last) = caLines[caIdx];
                    var natural = 0.0;
                    foreach (var word in words) natural += CaWidth(word);
                    var gaps = words.Count - 1;
                    // a justified line stretches its gaps to the column edge;
                    // the line that ends a paragraph keeps its natural spaces
                    var gapW = gaps > 0 && colArt.Justify && !last
                        ? (caColW - natural) / gaps
                        : CaWidth(" ");
                    var x = colLeft;
                    foreach (var word in words)
                    {
                        caB.BeginText().SetFont(caRes, caFs)
                           .MoveTextPosition(x, y)
                           .ShowText(word).EndText();
                        x += CaWidth(word) + gapW;
                    }
                    y -= caLine;
                }
            }
            caB.RestoreState();
            fx.flow.InjectContentAtCursor(caB.Build());
            fx.flow.AdvanceY(caContentH + 2 * caPad);
            return false;
        }
        return true;
    }

    /// <summary></summary>
    private void PlaceRelativeBox(FloatingBoxParagraphState fx)
    {
        // Flow-positioned visible box (background/border): render it at the
        // current cursor — not the page top — so a coloured header band
        // honours the page's top margin, then advance the flow past it.
        var targetPage = fx.flow.CurrentPage;
        var savedMode = fx.fbox.PositioningMode;
        var savedTop = fx.fbox.Top;
        var savedLeft = fx.fbox.Left;
        fx.fbox.PositioningMode = ParagraphPositioningMode.Absolute;
        fx.fbox.Top = targetPage.Height - fx.flow.CurrentY;
        // The box seats at the page's LEFT CONTENT edge, not at x = 0: a
        // flow-positioned box starts where the flow does.
        fx.fbox.Left = fx.marginLeft + fx.fbox.Left;
        fx.fbox.PageBottomMargin = fx.marginBottom;
        targetPage.AddFloatingBox(fx.fbox);
        EmitFloatingBoxLink(fx.fbox, fx.flow, targetPage);
        fx.fbox.PositioningMode = savedMode;
        fx.fbox.Top = savedTop;
        fx.fbox.Left = savedLeft;
        // Content the box could not hold spills onto fresh pages, where the box
        // re-seats with its chrome — without draining this the overflow rows were
        // silently dropped.
        foreach (var spill in fx.fbox.LastOverflowPages)
            fx.overflowPages.Add((spill, fx.page.Width, fx.page.Height));
        fx.flow.AdvanceY(fx.fbox.Height);
    }

    /// <summary></summary>
    private void PlaceOutsideFlow(FloatingBoxParagraphState fx)
    {
        if (fx.fbox.PositioningMode == ParagraphPositioningMode.Default)
        {
            // Default-positioned box with a Left/Top offset: the
            // offsets are relative to the page CONTENT area and the
            // box top anchors at the current flow position
            // (left margin + Left, top at the
            // cursor), so translate before the absolute render.
            var savedFbMode = fx.fbox.PositioningMode;
            var savedFbTop = fx.fbox.Top;
            var savedFbLeft = fx.fbox.Left;
            fx.fbox.PositioningMode = ParagraphPositioningMode.Absolute;
            fx.fbox.Top = fx.page.Height - fx.flow.CurrentY + fx.fbox.Top;
            fx.fbox.Left = fx.marginLeft + fx.fbox.Left;
            fx.fbox.PageBottomMargin = fx.marginBottom;
            fx.page.AddFloatingBox(fx.fbox);
            EmitFloatingBoxLink(fx.fbox, fx.flow, fx.page);
            fx.fbox.PositioningMode = savedFbMode;
            fx.fbox.Top = savedFbTop;
            fx.fbox.Left = savedFbLeft;
            foreach (var spill in fx.fbox.LastOverflowPages)
                fx.overflowPages.Add((spill, fx.page.Width, fx.page.Height));
        }
        else
        {
            // Absolute box — doesn't affect the flow cursor, but Left/Top
            // are relative to the page CONTENT area, not the page edge
            // (probed 2026-08-28: a 100x100 box at Left=300/Top=0 on the
            // default 90/72 margins strokes its border at x 389.95,
            // top y 770.05): translate by the page margins for the render.
            var savedAbsTop = fx.fbox.Top;
            var savedAbsLeft = fx.fbox.Left;
            fx.fbox.Top = fx.marginTop + fx.fbox.Top;
            fx.fbox.Left = fx.marginLeft + fx.fbox.Left;
            fx.fbox.PageBottomMargin = fx.marginBottom;
            fx.page.AddFloatingBox(fx.fbox);
            EmitFloatingBoxLink(fx.fbox, fx.flow, fx.page);
            fx.fbox.Top = savedAbsTop;
            fx.fbox.Left = savedAbsLeft;
            foreach (var spill in fx.fbox.LastOverflowPages)
                fx.overflowPages.Add((spill, fx.page.Width, fx.page.Height));
        }
    }

    /// <summary></summary>
    private void PlaceDissolvedBox(FloatingBoxParagraphState fx)
    {
        var chromeTop = fx.flow.CurrentY;
        var chromePage = fx.flow.CurrentPage;
        FlowLayout.DissolvedBandPlan? plan = null;
        if (!fx.flow.IsDryRun && DissolvedBoxHasNotes(fx.fbox))
        {
            // Every candidate measures from the page's OWN font slot, and the
            // registration a dry run makes is thrown away below: it names a
            // font on the detached sibling it drew, not on the real page.
            var dryFontName = fx.pl.fontName;
            plan = fx.flow.PlanDissolvedBands(candidate =>
            {
                var dryPage = fx.page.CreateDetachedSibling();
                dryPage.Footer = fx.page.Footer;
                var dryFlow = fx.flow.CreateDryRun(dryPage);
                dryFlow.ApplyDissolvedPlan(candidate);
                var dryCounters = new Dictionary<int, int>(fx.headingAutoCounters);
                fx.pl.fontName = dryFontName;
                LayoutDissolvedFloatingBox(fx.fbox, dryFlow, dryPage, fx.tocEntries, fx.pl, dryCounters,
                    new List<(byte[] content, double width, double height)>(),
                    fx.marginLeft, fx.marginRight, fx.marginBottom);
                return dryFlow;
            });
            fx.pl.fontName = dryFontName;
            fx.flow.ApplyDissolvedPlan(plan);
        }
        LayoutDissolvedFloatingBox(fx.fbox, fx.flow, fx.page, fx.tocEntries, fx.pl, fx.headingAutoCounters,
            fx.overflowPages, fx.marginLeft, fx.marginRight, fx.marginBottom);
        if (fx.fboxChromeOnFlow && !fx.flow.IsDryRun && ReferenceEquals(chromePage, fx.flow.CurrentPage))
        {
            var chromeW = fx.page.Width - fx.marginRight - fx.marginLeft;
            var chromeH = chromeTop - fx.flow.CurrentY;
            if (chromeW > 0 && chromeH > 0)
            {
                var cb = new Content.ContentStreamBuilder();
                cb.SaveState();
                if (fx.fbox.BackgroundColor is { } chromeBg)
                    cb.SetFillColor(chromeBg).Rectangle(fx.marginLeft, fx.flow.CurrentY, chromeW, chromeH).Fill();
                if (fx.fbox.Border is { } chromeBorder && chromeBorder.HasAnySide)
                    FloatingBox.StrokeBorder(cb, chromeBorder, fx.marginLeft, fx.flow.CurrentY, chromeW, chromeH);
                cb.RestoreState();
                fx.flow.InjectContentAtCursor(cb.Build());
            }
        }
        // Of the box's own margins only the bottom one acts: it is the gap
        // between the box's last line and the next paragraph. Top and Left
        // do not move a dissolved box (measured 2026-08-23 with each margin
        // in isolation).
    }
}
