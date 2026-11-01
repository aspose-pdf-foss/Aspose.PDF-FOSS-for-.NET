using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // The boxes of the controls on a line of text (probed on the UA flow): a check box or radio
    // button is a 13 px box standing ON the baseline behind its UA margins, a text box is the UA
    // control face's line in its borders and padding with its value on the line's baseline, a
    // textarea ends where the line's text box ends, a select is as wide as its widest entry and the arrow.
    private const double ClCheckBoxPt = 9.75;            // a check box / radio button box (13 px)
    private const double ClCheckMarginLeftPt = 3.0;      // the check box's UA margins: 4 px left,
    private const double ClCheckMarginRightPt = 2.25;    // 3 px right,
    private const double ClCheckMarginTopPt = 2.25;      // 3 px top
    private const double ClRadioMarginLeftPt = 3.75;     // a radio button's left margin (5 px)
    private const double ClCheckWidgetInsetPt = 1.0;     // the check box widget inside its box
    private const double ClInputColumnPt = 5.777;        // one column of a text box in the UA control face
    private const int ClInputColumns = 20;               // the UA size of a text box
    private const double ClInputBoxPt = 15.75;           // its box: the 15 px line, two 2 px borders, 1 px padding
    private const double ClInputBorderAdvancePt = 3.0;   // its two borders: inside the widget, and again in its advance
    private const double ClInputBaselinePt = 3.475;      // the value's baseline under the middle of the box
    private const double ClWidgetOverhangPt = 0.75;      // a text widget past its box, above and below
    private const double ClTextAreaWidthPt = 123.0;      // a textarea that states no width
    private const double ClSelectChromePt = 18.25;       // a select past its widest entry: padding and arrow
    private const double ClSelectHeightPt = 16.17;       // its widget
    private const double ClSelectInsetPt = 0.25;         // …which starts this far past the pen
    private const double ClSelectOverhangPt = 1.0;       // …and stands this far past its box, above and below
    private const double ClSelectAbovePt = 10.8;         // the select box above the baseline
    private const double ClSelectFontPt = 10.0;          // the UA control face size
    private const double ClLegacyDescentEm = 0.25;       // the plain flow's line under its baseline
    private const double ClInputDefaultMarginPt = 2.0;   // the standalone control's own calibrated bottom margin
    private const double ClFitSlackPt = 1e-6;
    private const double ClPxPt = 0.75;                  // a CSS pixel
    private const double ClButtonOutsetPt = 2.0;         // a push button widget past its box, left and right
    private const double ClButtonFontPt = 10.0;          // its caption, in the UA control face

    /// <summary>A control's place on its line, from the pen and the baseline: its advance, the
    /// widget's offset and size, and how far its box reaches above and below the baseline.</summary>
    private readonly record struct ClBox(double Adv, double Dx, double Dy, double W, double H, double Above, double Below);

    /// <summary>The pen of the line's trailing items.</summary>
    private sealed class ClPen
    {
        public double X, BaseY, Left, Right, Pitch, CursorUnderBase, TextAbove;
        public Page Page = null!;
    }

    private static ClBox ControlLineBox(Block c, double availPt, double descentPt)
    {
        if (c.IsCheckbox)
            return new ClBox(ClCheckMarginLeftPt + ClCheckBoxPt + ClCheckMarginRightPt, ClCheckMarginLeftPt + ClCheckWidgetInsetPt,
                ClCheckWidgetInsetPt, ClCheckBoxPt - 2 * ClCheckWidgetInsetPt, ClCheckBoxPt - 2 * ClCheckWidgetInsetPt,
                ClCheckBoxPt + ClCheckMarginTopPt, 0);
        if (c.IsRadio)
            return new ClBox(ClRadioMarginLeftPt + ClCheckBoxPt + ClCheckMarginRightPt, ClRadioMarginLeftPt, 0,
                ClCheckBoxPt, ClCheckBoxPt, ClCheckBoxPt + ClCheckMarginTopPt, 0);
        if (c.CtlLineImage)
        {
            // the picture's declared box on the baseline; one that cannot be loaded keeps the box and shows its alt text after it
            double w = c.ImageWidth * ClPxPt, h = c.ImageHeight * ClPxPt;
            return new ClBox(w + c.CtlAltWidthPt, 0, 0, w, h, h + c.CtlImageMarginTopPt, 0);
        }
        if (c.IsButton)
        {
            // the widget: the declared box and its outset, else the caption and its chrome; the
            // caption's baseline is the line's
            var w = c.ButtonWidthPt > 0 ? c.ButtonWidthPt + 2 * ClButtonOutsetPt
                : MeasureStd14(ClButtonFace, c.ButtonCaption, ClButtonFontPt) + ButtonChromeWPt;
            var under = ButtonHeightPt - ButtonCaptionDropPt;
            return new ClBox(w - 2 * ClButtonOutsetPt, -ClButtonOutsetPt, -under, w, ButtonHeightPt,
                ButtonCaptionDropPt - ClWidgetOverhangPt, under - ClWidgetOverhangPt);
        }
        if (c.IsSelectBox)
        {
            double widest = 0;
            foreach (var entry in c.SelectOptions ?? new List<string>())
                widest = Math.Max(widest, MeasureStd14("Helvetica", entry, ClSelectFontPt));
            // (a declared width is the box the widget stands a quarter point inside of)
            var w = c.InputWidth > 0 ? c.InputWidth * ClPxPt - 2 * ClSelectInsetPt : widest + ClSelectChromePt;
            var box = ClSelectHeightPt - 2 * ClSelectOverhangPt;
            return new ClBox(w + 2 * ClSelectInsetPt, ClSelectInsetPt, ClSelectAbovePt + ClSelectOverhangPt - ClSelectHeightPt,
                w, ClSelectHeightPt, ClSelectAbovePt, box - ClSelectAbovePt);
        }
        if (c.InputMultiline)
        {
            var w = Math.Min(c.InputWidth > 0 ? c.InputWidth : ClTextAreaWidthPt, availPt);
            var h = c.InputHeight > 0 ? c.InputHeight : ClInputBoxPt + 2 * ClWidgetOverhangPt;
            // (its box ends where the line's text box ends: a line holding it alone is exactly its box)
            return new ClBox(w, 0, -descentPt - ClWidgetOverhangPt, w, h, h - 2 * ClWidgetOverhangPt - descentPt, descentPt);
        }
        var columns = (c.InputColumns > 0 ? c.InputColumns : ClInputColumns) * ClInputColumnPt + ClInputBorderAdvancePt;
        var tw = Math.Min(c.InputWidth > 0 ? c.InputWidth : columns, availPt);
        var tbox = c.InputHeight > 0 ? c.InputHeight : ClInputBoxPt;
        var above = tbox / 2 + ClInputBaselinePt;
        return new ClBox(tw + ClInputBorderAdvancePt, 0, above - tbox - ClWidgetOverhangPt, tw, tbox + 2 * ClWidgetOverhangPt,
            above, tbox - above);
    }

    /// <summary>The line metrics the flow gives a text block, without moving the flow.</summary>
    private static HtmlBlockMetrics ControlLineMetrics(ConvertState cv, Block text)
    {
        var rb = new RenderBlockState { metrics = new HtmlBlockMetrics() };
        var y = cv.flow.y;
        ResolveBlockLineMetrics(cv, rb, text);
        cv.flow.y = y;
        return rb.metrics;
    }

    /// <summary>How far a text line's box reaches above and below its baseline in this flow.</summary>
    private static (double above, double below) ControlLineTextExtents(ConvertState cv, HtmlBlockMetrics m)
    {
        if (cv.profile.metricFlow && m.metricDrop > 0) return (m.metricDrop, Math.Max(0, m.lineHeight - m.metricDrop));
        var below = ClLegacyDescentEm * m.blockFontSize;
        return (Math.Max(0, m.lineHeight - below), below);
    }

    /// <summary>The advance of a text as its flow draws it.</summary>
    private static double ControlLineTextWidth(ConvertState cv, HtmlBlockMetrics m, Block text, string s)
    {
        if (s.Length == 0 || text.CtlStrut) return 0;
        var bold = text.FontRes == "F2" || text.EmBold;
        if (!string.IsNullOrEmpty(text.FontFamily))
            return MeasureFaceText(text.FontFamily + (bold ? " Bold" : ""), s, m.blockFontSize);
        if (cv.profile.uaStdSerif)
            return MeasureFaceText(bold ? "Times New Roman Bold" : "Times New Roman", s, m.blockFontSize);
        return MeasureStd14(bold ? "Helvetica-Bold" : "Helvetica", s, m.blockFontSize);
    }

    /// <summary>Lays out one control line: its first text through the block flow, indented by the
    /// controls before it; those controls on its first baseline; everything after it at a pen on
    /// its last one.</summary>
    private static void RenderControlLine(ConvertState cv, HtmlLoadOptions? options, List<byte[]> inlineSvgs, ControlLine line)
    {
        var items = line.Items;
        var first = items.FindIndex(b => !IsLineControl(b));
        if (first < 0 || cv.profile.rtlDoc) { RenderLineItemsApart(cv, options, inlineSvgs, line, -1); return; }
        var text = items[first];
        var m = ControlLineMetrics(cv, text);
        foreach (var item in items)
        {
            if (item.CtlLineImage) LoadLineImage(cv, options, item);
            // (a percent width resolves against the content box, as the render loop does for a block of its own)
            if (item.InputWidthFrac > 0) { item.InputWidth = item.InputWidthFrac * (cv.pageWidth - 2 * cv.marginLeft); item.InputWidthFrac = 0; }
        }
        var avail = Math.Max(0, cv.flow.contentWidth - text.LeftIndent);
        var (textAbove, textBelow) = ControlLineTextExtents(cv, m);
        double lead = 0, tallest = 0, deepest = 0;
        for (var i = 0; i < items.Count; i++)
        {
            if (!IsLineControl(items[i])) continue;
            var box = ControlLineBox(items[i], avail, textBelow);
            if (i < first) lead += box.Adv;
            tallest = Math.Max(tallest, box.Above);
            deepest = Math.Max(deepest, box.Below);
        }
        var space = ControlLineTextWidth(cv, m, text, " ");
        if (first > 0 && text.CtlSpaceBefore) lead += space;
        line.Reset(Math.Max(0, tallest - textAbove), lead);
        // (the element's bottom margin stands once, under the whole line: a text with items after it holds its own back)
        var bottomMargin = text.MarginBottom;
        text.CtlSeat = line;
        text.LeftIndent += lead;
        if (first + 1 < items.Count) text.MarginBottom = 0;
        try { RenderFlowBlock(cv, options, inlineSvgs, text); }
        finally { text.CtlSeat = null; text.LeftIndent -= lead; text.MarginBottom = bottomMargin; }
        if (!line.Seated) { RenderLineItemsApart(cv, options, inlineSvgs, line, first); return; }

        var x = line.FirstX - lead;
        for (var i = 0; i < first; i++)
        {
            var box = ControlLineBox(items[i], avail, textBelow);
            EmitLineControl(cv, items[i], line.FirstPage!, x, line.FirstBaseY, box);
            x += box.Adv;
        }
        var pen = new ClPen
        {
            X = line.LastX + ControlLineTextWidth(cv, m, text, line.LastText),
            BaseY = line.LastBaseY, Page = line.LastPage!, Left = line.FirstX - lead,
            Right = cv.marginLeft + cv.flow.contentWidth, Pitch = m.lineHeight + line.LeadDropPt,
            CursorUnderBase = cv.flow.y - line.LastBaseY, TextAbove = textAbove + line.LeadDropPt,
        };
        if (text.CtlSpaceAfter && first + 1 < items.Count) pen.X += space;
        for (var i = first + 1; i < items.Count; i++)
        {
            if (!IsLineControl(items[i])) PlaceLineText(cv, items[i], pen, i + 1 < items.Count);
            else
            {
                var box = ControlLineBox(items[i], avail, textBelow);
                if (pen.X + box.Adv > pen.Right + ClFitSlackPt && pen.X > pen.Left + ClFitSlackPt) BreakControlLine(cv, pen);
                EmitLineControl(cv, items[i], pen.Page, pen.X, pen.BaseY, box);
                pen.X += box.Adv;
            }
        }
        CloseControlLine(cv, items, text, Math.Max(0, deepest - textBelow), bottomMargin);
    }

    /// <summary>The line ends: a control deeper than the text stands the difference below it, and
    /// the bottom margin and closing gap of the line's element follow the whole line - the larger
    /// of the margin the seated text held back and the one the markup left on the line's last block.</summary>
    private static void CloseControlLine(ConvertState cv, List<Block> items, Block seated, double dropPt, double heldMarginPt)
    {
        cv.flow.y -= dropPt;
        var last = items[^1];
        if (ReferenceEquals(last, seated)) return;
        var margin = Math.Max(heldMarginPt, last.IsInputField && last.MarginBottom <= ClInputDefaultMarginPt ? 0 : last.MarginBottom);
        if (margin > 0)
        {
            cv.flow.y -= margin;
            cv.flow.prevFlowMarginBottom = margin;
            cv.flow.uaPrevMarginBottom = margin;
        }
        if (last.UaClosingGapPt > 0) cv.flow.uaClosingGap = last.UaClosingGapPt;
    }

    /// <summary>The line's items each as the block they would be alone (the line could not be seated).</summary>
    private static void RenderLineItemsApart(ConvertState cv, HtmlLoadOptions? options, List<byte[]> inlineSvgs, ControlLine line, int done)
    {
        for (var i = 0; i < line.Items.Count; i++)
            if (i != done && !line.Items[i].CtlStrut) RenderFlowBlock(cv, options, inlineSvgs, line.Items[i]);
    }

    /// <summary>The pen moves to the start of a new line under the current one, on a new page when this one is full.</summary>
    private static void BreakControlLine(ConvertState cv, ClPen pen)
    {
        pen.X = pen.Left;
        pen.BaseY -= pen.Pitch;
        cv.flow.y -= pen.Pitch;
        if (cv.flow.y >= cv.marginBottom) return;
        cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
        EnsureFonts(cv.flow.page, cv.docFontDict);
        pen.Page = cv.flow.page;
        pen.BaseY = FreshPageTopY(cv.profile, cv.pageHeight, cv.marginTop) - pen.TextAbove;
        cv.flow.y = pen.BaseY + pen.CursorUnderBase;
        cv.flow.pendingTopDrop = false;
    }

    /// <summary>A text after the line's first one is written at the pen, word by word, breaking the line where it is full.</summary>
    private static void PlaceLineText(ConvertState cv, Block text, ClPen pen, bool more)
    {
        var m = ControlLineMetrics(cv, text);
        var space = ControlLineTextWidth(cv, m, text, " ");
        if (text.CtlSpaceBefore) pen.X += space;
        var s = text.Text;
        var start = 0;
        while (start < s.Length)
        {
            // the longest run of whole words from here that still fits
            int end = start, probe = start;
            while (probe < s.Length)
            {
                var next = s.IndexOf(' ', probe + 1);
                if (next < 0) next = s.Length;
                if (pen.X + ControlLineTextWidth(cv, m, text, s[start..next]) > pen.Right + ClFitSlackPt) break;
                end = probe = next;
            }
            if (end == start)
            {
                if (pen.X > pen.Left + ClFitSlackPt) { BreakControlLine(cv, pen); continue; }
                end = s.IndexOf(' ', start + 1);   // a word wider than the line stands alone on it
                if (end < 0) end = s.Length;
            }
            WriteLineText(cv, text, m, s[start..end], start, pen);
            pen.X += ControlLineTextWidth(cv, m, text, s[start..end]);
            start = end;
            while (start < s.Length && s[start] == ' ') start++;
            if (start < s.Length) BreakControlLine(cv, pen);
        }
        if (text.CtlSpaceAfter && more) pen.X += space;
    }

    /// <summary>Writes a piece of a text block at the pen through the flow's own line writer.</summary>
    private static void WriteLineText(ConvertState cv, Block text, HtmlBlockMetrics m, string piece, int charAt, ClPen pen)
    {
        var flowPage = cv.flow.page;
        var flowY = cv.flow.y;
        cv.flow.page = pen.Page;
        cv.flow.y = pen.BaseY + (cv.profile.metricFlow && m.metricDrop > 0 ? m.metricDrop : 0);
        m.lines = new[] { piece };
        m.cumChar = charAt;
        m.firstLineOfBlock = false;
        var bt = new BlockTextState
        {
            block = text, flow = cv.flow, profile = cv.profile, metrics = m, doc = cv.doc, docFontDict = cv.docFontDict,
            sb = cv.sb, anchorTargets = cv.anchorTargets, pendingLinks = cv.pendingLinks, embeddedFonts = cv.embeddedFonts,
            fontFileCache = cv.fontFileCache, bandStack = cv.bandStack, articleFlow = cv.articleFlow, uaFlow = cv.uaFlow,
            marginBottom = cv.marginBottom, marginLeft = cv.marginLeft, marginRight = cv.marginRight, marginTop = cv.marginTop,
            pageHeight = cv.pageHeight, pageWidth = cv.pageWidth, lineIdx = 1, invc = CultureInfo.InvariantCulture, lineXPos = pen.X,
        };
        bt.fontRes = ResolveFontRes(cv.flow.page, text, cv.flow, cv.profile, cv.doc, cv.embeddedFonts, cv.fontFileCache);
        PaintLine(bt, piece);
        cv.flow.contentPage = pen.Page;
        cv.flow.page = flowPage;
        cv.flow.y = flowY;
    }

    /// <summary>The form field of a control seated at the pen on the line's baseline.</summary>
    private static void EmitLineControl(ConvertState cv, Block c, Page page, double x, double baseY, ClBox box)
    {
        var rect = new Rectangle(x + box.Dx, baseY + box.Dy, x + box.Dx + box.W, baseY + box.Dy + box.H);
        if (c.CtlLineImage)
        {
            EmitLineImage(cv, c, page, rect, baseY);
        }
        else if (c.IsCheckbox)
        {
            cv.doc.Form.Add(NewCheckbox(page, rect, c), page.Number);
        }
        else if (c.IsRadio)
        {
            cv.radioOptions.Add((c.RadioGroup, c.RadioValue, c.Checked, c.InputReadOnly, page, rect));
        }
        else if (c.IsSelectBox)
        {
            EmitComboBox(cv, c, page, rect);
        }
        else if (c.IsButton)
        {
            EmitPushButton(cv, c, page, rect, baseY);
        }
        else
        {
            var flowPage = cv.flow.page;
            cv.flow.page = page;
            EmitControlBox(c, rect.LLX, rect.LLY, box.W, box.H, cv.flow, cv.doc);
            cv.flow.page = flowPage;
        }
        cv.flow.contentPage = page;
    }

    /// <summary>Before its line is measured: the picture's bytes, or - when it cannot be loaded - the advance of its alt text.</summary>
    private static void LoadLineImage(ConvertState cv, HtmlLoadOptions? options, Block picture)
    {
        picture.CtlAltWidthPt = 0;
        try { picture.CtlImageBytes = LoadConverterImage(picture.ImageSrc, options); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException) { picture.CtlImageBytes = null; }
        if (picture.CtlImageBytes is null && picture.CtlAltText is { } alt)
            picture.CtlAltWidthPt = ControlLineTextWidth(cv, ControlLineMetrics(cv, alt), alt, alt.Text);
    }

    /// <summary>A picture on a control line: drawn in its declared box on the baseline, or its alt text after the empty box.</summary>
    private static void EmitLineImage(ConvertState cv, Block picture, Page page, Rectangle rect, double baseY)
    {
        if (picture.CtlImageBytes is { } bytes)
        {
            try { page.AddImage(bytes, rect); }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or InvalidDataException) { /* an undecodable picture leaves its box empty */ }
            picture.CtlImageBytes = null;
            return;
        }
        if (picture.CtlAltText is not { Text.Length: > 0 } alt) return;
        var pen = new ClPen { X = rect.URX, BaseY = baseY, Page = page };
        WriteLineText(cv, alt, ControlLineMetrics(cv, alt), alt.Text, 0, pen);
    }

    private const string ClButtonFace = "Helvetica";

    /// <summary>A push button: the field in a solid one point black border, and its caption as page
    /// text centred in the widget on the line's baseline.</summary>
    private static void EmitPushButton(ConvertState cv, Block c, Page page, Rectangle rect, double baseY)
    {
        var button = NewPushButton(page, rect, c.InputName);
        cv.doc.Form.Add(button, page.Number);
        if (c.ButtonCaption.Length == 0) return;
        var x = rect.LLX + (rect.Width - MeasureStd14(ClButtonFace, c.ButtonCaption, ClButtonFontPt)) / 2;
        page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(CultureInfo.InvariantCulture,
            $"q 0 g BT /F1 {ClButtonFontPt:0.##} Tf 1 0 0 1 {x:0.##} {baseY:0.##} Tm ({EscapePdfString(c.ButtonCaption)}) Tj ET Q\n")));
    }

    /// <summary>A select's combo box on its line, the shown entry as its value.</summary>
    private static void EmitComboBox(ConvertState cv, Block c, Page page, Rectangle rect)
    {
        var combo = NewComboBox(page, rect, c.SelectOptions ?? new List<string>(), c.InputName);
        cv.doc.Form.Add(combo, page.Number);
        if (!string.IsNullOrEmpty(c.InputValue)) combo.Value = c.InputValue;
    }
}
