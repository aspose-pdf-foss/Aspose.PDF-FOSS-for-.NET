using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class TextStamp
{
    /// <summary>Draw the whole stamp with an embedded Type0 replacement font (Identity-H +
    /// ToUnicode), so Unicode/CJK text both renders and round-trips through text extraction.
    /// Handles multi-line text: rows stack downward one em apart, each aligned within the
    /// block per <see cref="TextAlignment"/>, the block placed per the stamp alignments
    /// (margins honoured) — measured with the embedded font's /W advances so extraction
    /// re-measures the exact same widths.</summary>
    private byte[] BuildCidStamp(Page page, byte[] ttf, string fontName, double fontSize, Color color)
    {
        var fontDict = GetPageFontDict(page);
        var wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;
        var x0 = XIndent > 0 ? XIndent : LeftMargin;

        // Wrap budget: the stamp Width when declared, else the page's own width
        // (probed: a wrapped stamp with no Width fills 49 × 12 pt rows in a 595 page).
        var budget = WrapWidth > 0 ? WrapWidth : Math.Max(0.0, page.Width - x0);

        // Display rows: the explicit '\n' lines, each re-filled against the budget when
        // wrapping is on. The fill is GREEDY AT NATURAL ADVANCES with no kinsoku —
        // DiscretionaryHyphenation breaks anywhere (mid-word), the word modes at spaces
        // (probed; see the CID wrap law).
        var charLevel = WordWrapMode == TextFormattingOptions.WordWrapMode.DiscretionaryHyphenation;
        var displayRows = new System.Collections.Generic.List<System.Collections.Generic.List<(byte[] ttf, string name, string text)>>();
        foreach (var sourceRow in Text.Replace("\r\n", "\n").Split('\n'))
        {
            var runs = SplitFontRuns(sourceRow, ttf, fontName);
            if (wrapping && budget > 0)
                displayRows.AddRange(FillCidRows(fontDict, runs, fontSize, budget, charLevel));
            else
                displayRows.Add(runs);
        }

        var n = displayRows.Count;
        var rowRuns = new System.Collections.Generic.List<(string res, byte[] hex, double width)>[n];
        var widths = new double[n];
        for (var i = 0; i < n; i++)
        {
            rowRuns[i] = new();
            foreach (var (runTtf, runName, runText) in displayRows[i])
            {
                var (res, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, runTtf, runName, runText, stripSpacesInBaseFont: true);
                var w = Aspose.Pdf.Text.Type0FontEmbedder.MeasureText(
                    fontDict, runTtf, runName, runText, fontSize, stripSpacesInBaseFont: true);
                rowRuns[i].Add((res, hex, w));
                widths[i] += w;
            }
        }
        var naturalBlockW = n > 0 ? widths.Max() : 0.0;

        // Probed block scaling: sx = Width / widest row (squeezes as well as stretches),
        // on BY DEFAULT and off only under an explicit Scale=false; sy = Height /
        // ((rows + 0.1) · fs) — the rows never re-flow at the scaled size.
        var sx = Width > 0 && naturalBlockW > 0 && CidScaleEnabled ? Width / naturalBlockW : 1.0;
        var sy = Height > 0 && CidScaleEnabled ? Height / ((n + 0.1) * fontSize) : 1.0;
        var pitch = fontSize * sy;
        var blockW = naturalBlockW * sx;

        // Block placement: alignment when set (margins honoured), else XIndent/YIndent
        // with the block BOTTOM-anchored at YIndent (probed: the bottom row's baseline
        // sits exactly on YIndent — 0 included, the page's own bottom edge — and the
        // rows grow upward).
        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (page.Width - blockW) / 2,
            HorizontalAlignment.Right => page.Width - blockW - XIndent - RightMargin,
            _ => x0,
        };
        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => page.Height - TopMargin - YIndent - pitch,
            VerticalAlignment.Center => (page.Height + n * pitch) / 2 - pitch,
            VerticalAlignment.Bottom => YIndent + BottomMargin + n * pitch - pitch,
            _ => YIndent + (n - 1) * pitch,
        };

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        for (var i = 0; i < n; i++)
        {
            var rowW = widths[i] * sx;
            var rowX = TextAlignment switch
            {
                HorizontalAlignment.Right => x + blockW - rowW,
                HorizontalAlignment.Center => x + (blockW - rowW) / 2,
                _ => x,
            };
            builder.SetTextMatrix(sx, 0, 0, sy, rowX, y - i * pitch);
            foreach (var (res, hex, _) in rowRuns[i])
                builder.SetFont(res, fontSize).ShowTextHex(hex);
        }
        builder
            .EndText();
        builder.RestoreState();
        return builder.Build();
    }

    internal override byte[] BuildContentStream(Page page)
    {
        var cb = new StampContentState();
        cb.page = page;
        cb.baseFontName = ResolveBaseFontName();
        cb.fontSize = TextState?.FontSize > 0 ? (double)TextState.FontSize : FontSize;
        cb.color = TextState?.ForegroundColor ?? Color;

        (cb.encoded, cb.diffMap) = EncodeForWinAnsi(Text);

        // Auto-fit: pick the largest font size at which the word-wrapped text still
        // fits the Width×Height box (bisection to AutoFitPrecision). Do this before
        // any layout so the chosen size flows into the render below and is readable
        // via the FontSize property once the stamp has been added.
        if (TryBuildSpecialForm(cb) is { } special) return special;

        cb.stampDeg = (int)Math.Round(Math.Abs(RotateAngle != 0 ? RotateAngle : (double)Rotate));
        cb.wrapVertical = cb.stampDeg % 180 == 90;
        cb.mb = cb.page.MediaBox;
        cb.advanceDim = cb.wrapVertical ? cb.mb.Height : cb.mb.Width;
        cb.wrapLead = cb.wrapVertical ? (YIndent > 0 ? YIndent : BottomMargin) : (XIndent > 0 ? XIndent : LeftMargin);
        cb.wrapTrail = cb.wrapVertical ? TopMargin : RightMargin;
        LayoutRows(cb);
        cb.rotQuarter = ((int)Math.Round(Math.Abs(cb.rotateDeg))) % 360;
        PlaceRotated(cb);

        cb.bgYOffset = 0.0;
        EmitStampText(cb);
        return cb.builder.Build();
    }

    // Scale=true layout: lay the text out at the base font in a natural-size block,
    // then emit one cm that non-uniformly scales that block to fill the Width×Height
    // box at (XIndent, YIndent). Wrapped text breaks to Width (so scaleX ≈ 1 and only
    // the height stretches); un-wrapped text is a single line (newlines → spaces) that
    // is squished horizontally to Width and stretched to Height.
    private byte[] BuildScaledToBox(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, bool wrapping)
    {
        var rows = wrapping
            ? WrapEncoded(encoded, baseFontName, fontSize, Width)
            : new List<byte[]> { JoinToOneLine(encoded) };
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        var lineHeight = fontSize;
        var blockHeight = Math.Max(1, rows.Count) * lineHeight;
        if (blockWidth <= 0) blockWidth = Width;

        var sX = Width / blockWidth;
        var sY = Height / blockHeight;
        // Baseline of the bottom row inside the natural block (leave the font's
        // descent below it); rows stack upward from there.
        var descent = fontSize * 0.2;

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.SetMatrix(sX, 0, 0, sY, XIndent, YIndent);

        // Background box: fills the whole natural block in block-local space, so the
        // cm scale above stretches it to exactly the Width×Height box.
        if (TextState?.BackgroundColor is { IsEmpty: false } bg)
        {
            builder.SaveState();
            builder.Rectangle(0, 0, blockWidth, blockHeight);
            builder.SetFillColor(bg);
            builder.SetStrokeColor(bg);
            builder.FillEvenOdd();
            builder.RestoreState();
        }

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            // Row 0 is the top line; the bottom line sits at `descent`.
            var localY = (rows.Count - 1 - li) * lineHeight + descent;
            builder.SetTextMatrix(1, 0, 0, 1, localX, localY).ShowTextBytes(rows[li]);
        }
        builder.EndText().RestoreState();
        return builder.Build();
    }

    // Rotated, box-fitted stamp: scale the natural text block to fill
    // the Width×Height box (sx=Width/blockWidth, sy=Height/blockHeight), place the box
    // per Horizontal/VerticalAlignment (or XIndent/YIndent when alignment is unset),
    // and rotate about the box centre. The emitted cm is
    //   [sx·cosθ, sx·sinθ, -sy·sinθ, sy·cosθ, tx, ty]
    // with (tx,ty) chosen so the (scaled) box centre maps to the target page centre.
    private byte[] BuildBoxRotated(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, bool wrapping, double rotateDeg)
    {
        // An auto-fitted stamp must render the SAME wrap the fit was computed
        // against (trailing-space-trimmed rows) or the scales below distort what
        // the fit sized to be exact.
        var rows = wrapping
            ? (AutoFitToBox
                ? AutoFitWrap(encoded, baseFontName, fontSize, Width)
                : WrapEncoded(encoded, baseFontName, fontSize, Width))
            : new List<byte[]> { JoinToOneLine(encoded) };
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        if (blockWidth <= 0) blockWidth = Width;
        var lineHeight = fontSize;
        // Vertical box-fit law (probed, see the scaled-box path): the block is
        // (N + 0.1) lines tall - one line of leading per row plus a tenth of a
        // line for the last row's descender band.
        var blockHeight = (Math.Max(1, rows.Count) + 0.1) * lineHeight;

        var sX = Width / blockWidth;
        var sY = Height / blockHeight;
        double sw = Width, sh = Height; // scaled box dimensions

        // Alignment places the box's ROTATED footprint, not the unrotated box: a
        // 90-degree stamp whose Width x Height mirrors the page (the swap the
        // caller makes so the rotated text fills it) must land ON the page.
        // Anchoring the unrotated dimensions hung 123 pt of a page-filling
        // rotated stamp off both edges (measured against the expected render,
        // which spans the full page). The footprint's axis-aligned bounds:
        var rotRad = rotateDeg * Math.PI / 180;
        var fw = Math.Abs(Math.Cos(rotRad)) * sw + Math.Abs(Math.Sin(rotRad)) * sh;
        var fh = Math.Abs(Math.Sin(rotRad)) * sw + Math.Abs(Math.Cos(rotRad)) * sh;

        // Box centre on the page: alignment when set, else XIndent/YIndent corner.
        // Anchoring is within the MEDIA BOX rectangle - a page whose box has a
        // non-zero origin (the mediabox-swap pattern leaves y running 247..842)
        // otherwise draws the stamp a full origin-offset below the visible page.
        var pmb = page.MediaBox;
        double cx = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => pmb.LLX + pmb.Width / 2.0,
            HorizontalAlignment.Right => pmb.URX - fw / 2.0 - XIndent,
            _ => pmb.LLX + XIndent + fw / 2.0,
        };
        double cy = VerticalAlignment switch
        {
            VerticalAlignment.Center => pmb.LLY + pmb.Height / 2.0,
            VerticalAlignment.Top => pmb.URY - fh / 2.0 - YIndent,
            _ => pmb.LLY + YIndent + fh / 2.0,
        };

        var cos = Math.Cos(rotateDeg * Math.PI / 180);
        var sin = Math.Sin(rotateDeg * Math.PI / 180);
        // tx,ty: map the scaled box centre (sw/2, sh/2) through the rotation to (cx,cy).
        var tx = cx - (cos * (sw / 2.0) - sin * (sh / 2.0));
        var ty = cy - (sin * (sw / 2.0) + cos * (sh / 2.0));

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.SetMatrix(sX * cos, sX * sin, -sY * sin, sY * cos, tx, ty);
        // The stamp's background fills the whole box (a FormattedText backdrop
        // colour paints behind every row, edge to edge).
        var boxBg = TextState?.BackgroundColor;
        if (boxBg is { IsEmpty: false })
        {
            builder.SetFillColor(boxBg.R / 255.0, boxBg.G / 255.0, boxBg.B / 255.0);
            builder.Rectangle(0, 0, blockWidth, blockHeight);
            builder.Fill();
        }
        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        var descent = fontSize * 0.2;
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            var localY = (rows.Count - 1 - li) * lineHeight + descent;
            builder.SetTextMatrix(1, 0, 0, 1, localX, localY).ShowTextBytes(rows[li]);
        }
        builder.EndText().RestoreState();
        return builder.Build();
    }

    // Word-wrapped stamp with a background box (Scale=false). Wrap to the stamp's Width
    // (the margins place the box, they do not narrow the wrap: a 100 pt stamp with 10/5 pt
    // side margins wraps a 20 pt Lorem ipsum into 50 rows, not the 58 of an 85 pt fill),
    // grow the box to the widest wrapped line, and emit:
    //   q  x y w h re  r g b rg  r g b RG  f*  BT ... ET  Q
    // so the rectangle is the first painted operator. The box
    // is placed per the stamp's Horizontal/Vertical alignment; the text fills it top-down.
    private byte[] BuildWrappedBackgroundBox(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, Color bgColor)
    {
        var innerW = Width;

        // Break at spaces only, a word joining its line when the line WITHOUT its trailing
        // space still fits: a word wider than the width is NOT hyphenated — it sits on
        // its own line and overflows, which (with Scale=false) is what grows the box width.
        var rows = AutoFitWrap(encoded, baseFontName, fontSize, innerW);
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        // With Scale=false a word wider than the inner width grows the box rather than being
        // squeezed, so the box width is the widest wrapped line.
        var boxW = Math.Max(innerW, blockWidth);
        var lineHeight = fontSize;
        var descent = fontSize * 0.1;                 // baseline inset below the last line
        var boxH = rows.Count * lineHeight + descent;

        double boxX = HorizontalAlignment switch
        {
            HorizontalAlignment.Right => page.Width - RightMargin - boxW,
            HorizontalAlignment.Center => (page.Width - boxW) / 2.0,
            _ => LeftMargin,
        };
        double boxY = VerticalAlignment switch
        {
            VerticalAlignment.Bottom => BottomMargin,
            VerticalAlignment.Center => (page.Height - boxH) / 2.0,
            _ => page.Height - TopMargin - boxH,       // Top (default)
        };

        var builder = new ContentStreamBuilder();
        builder.SaveState();                            // q
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.Rectangle(boxX, boxY, boxW, boxH);      // re
        builder.SetFillColor(bgColor);                  // rg
        builder.SetStrokeColor(bgColor);                // RG
        builder.FillEvenOdd();                          // f*

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        var topBaseline = boxY + boxH - fontSize;       // first line near the box top
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = boxW - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            builder.SetTextMatrix(1, 0, 0, 1, boxX + localX, topBaseline - li * lineHeight)
                   .ShowTextBytes(rows[li]);
        }
        builder.EndText();
        builder.RestoreState();                         // Q
        return builder.Build();
    }
}
