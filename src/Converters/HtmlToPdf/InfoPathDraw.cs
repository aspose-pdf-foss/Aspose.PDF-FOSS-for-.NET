using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    // Painting: every rect is given in page space from the top; the y flip happens here.

    private static void IpFillRect(InfoPathState ip, double x, double top, double w, double h, string rgb)
    {
        if (w <= 0 || h <= 0) return;
        ip.sb.Append(Compat.Format(ip.invc, $"q {rgb} rg {x:0.###} {ip.pageH - top - h:0.###} {w:0.###} {h:0.###} re f Q\n"));
    }

    private static void IpStrokeRect(InfoPathState ip, double x, double top, double w, double h, string rgb, double lw)
        => ip.sb.Append(Compat.Format(ip.invc, $"q {rgb} RG {lw:0.###} w {x:0.###} {ip.pageH - top - h:0.###} {w:0.###} {h:0.###} re S Q\n"));

    private static void IpStrokeLine(InfoPathState ip, double x0, double y0, double x1, double y1, string rgb, double lw)
        => ip.sb.Append(Compat.Format(ip.invc, $"q {rgb} RG {lw:0.###} w {x0:0.###} {ip.pageH - y0:0.###} m {x1:0.###} {ip.pageH - y1:0.###} l S Q\n"));

    /// <summary>A text run: the face's WinAnsi resource for Latin text, its program embedded as a
    /// Type0 font (glyph ids) when the run carries a character outside it (CJK, the ellipsis).</summary>
    private static void IpText(InfoPathState ip, string face, double fs, string rgb, double x, double baseTd, string text)
    {
        if (text.Length == 0) return;
        var latin = true;
        foreach (var ch in text) if (ch > 0x7E) { latin = false; break; }
        if (latin)
        {
            ip.sb.Append(Compat.Format(ip.invc, $"BT {rgb} rg /{IpRes(face)} {fs:0.###} Tf 1 0 0 1 {x:0.###} {ip.pageH - baseTd:0.###} Tm ({EscapePdfString(text)}) Tj ET\n"));
            return;
        }
        var ttf = Text.FontRepository.GetTtfData(IpMeasureFace(face));
        if (ttf is null || ip.page.Dict.Get("Resources") is not Core.PdfDictionary res || res.Get("Font") is not Core.PdfDictionary fontDict) return;
        var (rn, hex) = Text.Type0FontEmbedder.Embed(fontDict, ttf, IpMeasureFace(face).Replace(" ", ""), text, stripSpacesInBaseFont: true);
        ip.sb.Append(Compat.Format(ip.invc, $"BT {rgb} rg /{rn} {fs:0.###} Tf 1 0 0 1 {x:0.###} {ip.pageH - baseTd:0.###} Tm <"))
            .Append(Compat.ToHexString(hex)).Append("> Tj ET\n");
    }

    /// <summary>A circle of four Béziers, stroked or filled.</summary>
    private static void IpCircle(InfoPathState ip, double cx, double cyTd, double r, string rgb, bool fill)
    {
        const double k = 0.5523;
        var cy = ip.pageH - cyTd;
        var sb = ip.sb;
        if (fill) sb.Append(Compat.Format(ip.invc, $"q {rgb} rg "));
        else sb.Append(Compat.Format(ip.invc, $"q {rgb} RG 1 w "));
        sb.Append(Compat.Format(ip.invc, $"{cx + r:0.###} {cy:0.###} m "));
        sb.Append(Compat.Format(ip.invc, $"{cx + r:0.###} {cy + k * r:0.###} {cx + k * r:0.###} {cy + r:0.###} {cx:0.###} {cy + r:0.###} c "));
        sb.Append(Compat.Format(ip.invc, $"{cx - k * r:0.###} {cy + r:0.###} {cx - r:0.###} {cy + k * r:0.###} {cx - r:0.###} {cy:0.###} c "));
        sb.Append(Compat.Format(ip.invc, $"{cx - r:0.###} {cy - k * r:0.###} {cx - k * r:0.###} {cy - r:0.###} {cx:0.###} {cy - r:0.###} c "));
        sb.Append(Compat.Format(ip.invc, $"{cx + k * r:0.###} {cy - r:0.###} {cx + r:0.###} {cy - k * r:0.###} {cx + r:0.###} {cy:0.###} c "));
        sb.Append(fill ? "f Q\n" : "S Q\n");
    }

    /// <summary>The tr / tbody backgrounds at their grid boxes (the reference converter fills a
    /// rect with the band's right and bottom edges as its width and height, clipped to the
    /// band itself - so the band is what shows).</summary>
    private static void IpPaintBands(InfoPathState ip, IpNode table, List<(IpNode tr, List<IpCell> cells, double h)> rows,
        List<(IpNode group, int first, int count)> bands, double[] gridX, double[] gridY)
    {
        for (var r = 0; r < rows.Count; r++)
        {
            var tr = rows[r].tr;
            var rgb = IpRgb(tr.Css("background-color")) ?? IpClassBandRgb(tr);
            if (rgb is not null) IpFillRect(ip, gridX[0], gridY[r], gridX[^1] - gridX[0], gridY[r + 1] - gridY[r], rgb);
        }
        foreach (var (group, first, count) in bands)
        {
            var rgb = IpRgb(group.Css("background-color")) ?? IpClassBandRgb(group);
            if (rgb is not null && count > 0) IpFillRect(ip, gridX[0], gridY[first], gridX[^1] - gridX[0], gridY[first + count] - gridY[first], rgb);
        }
    }

    private static string? IpClassBandRgb(IpNode el)
    {
        if (el.HasClass("primaryVeryDark")) return "0.118 0.235 0.482";      // #1e3c7b
        if (el.HasClass("primaryLight") || el.HasClass("primaryMedium") || el.HasClass("accentLight") || el.HasClass("xdTableHeader"))
            return el.HasClass("primaryMedium") ? "0.796 0.847 0.922" : "0.922 0.941 0.976";   // #cbd8eb / #ebf0f9
        if (el.HasClass("primaryDark") || el.HasClass("accentDark")) return "0.318 0.49 0.749";   // #517dbf
        return null;
    }

    private static void IpPaintCellFills(InfoPathState ip, List<(IpNode tr, List<IpCell> cells, double h)> rows, double[] gridX, double[] gridY)
    {
        for (var r = 0; r < rows.Count; r++)
            foreach (var cell in rows[r].cells)
                if (cell.FillRgb is { } rgb)
                    IpFillRect(ip, gridX[cell.Col], gridY[r], gridX[cell.Col + cell.Span] - gridX[cell.Col], gridY[r + 1] - gridY[r], rgb);
    }

    /// <summary>The collapsed grid's 1pt black borders, one piece per grid cell edge: an edge is
    /// drawn where either cell beside it declares the border, and a piece reaches half a pt past
    /// its end where a crossing border meets it there from either side.</summary>
    private static void IpPaintGridEdges(InfoPathState ip, List<(IpNode tr, List<IpCell> cells, double h)> rows, double[] gridX, double[] gridY)
    {
        int nr = rows.Count, nc = gridX.Length - 1;
        bool[,] top = new bool[nr, nc], bottom = new bool[nr, nc], left = new bool[nr, nc + 1], right = new bool[nr, nc + 1];
        for (var r = 0; r < nr; r++)
            foreach (var cell in rows[r].cells)
            {
                for (var c = cell.Col; c < cell.Col + cell.Span; c++) { top[r, c] = cell.Top; bottom[r, c] = cell.Bottom; }
                left[r, cell.Col] = cell.Left;
                right[r, cell.Col + cell.Span] = cell.Right;
            }
        bool H(int rb, int c) => c >= 0 && c < nc && ((rb > 0 && bottom[rb - 1, c]) || (rb < nr && top[rb, c]));
        bool V(int r, int cb) => r >= 0 && r < nr && (right[r, cb] || left[r, cb]);
        bool Cross(int rb, int cb) => V(rb - 1, cb) || V(rb, cb);                 // a vertical meeting a row line
        bool Meet(int r, int cb) => H(r, cb - 1) || H(r, cb);                       // a horizontal meeting a column line
        const string black = "0 0 0";
        for (var rb = 0; rb <= nr; rb++)
            for (var c = 0; c < nc; c++)
                if (H(rb, c))
                    IpStrokeLine(ip, gridX[c] - (Cross(rb, c) ? IpHalfBorder : 0), gridY[rb], gridX[c + 1] + (Cross(rb, c + 1) ? IpHalfBorder : 0), gridY[rb], black, IpBorder);
        for (var cb = 0; cb <= nc; cb++)
            for (var r = 0; r < nr; r++)
                if (V(r, cb))
                    IpStrokeLine(ip, gridX[cb], gridY[r] - (Meet(r, cb) ? IpHalfBorder : 0), gridX[cb], gridY[r + 1] + (Meet(r + 1, cb) ? IpHalfBorder : 0), black, IpBorder);
    }

    /// <summary>Paint a line's items at their pens from the line's left, on its baseline.</summary>
    private static void IpPaintLine(InfoPathState ip, IpLine line, double x, double top)
    {
        var baseTd = top + line.Above;
        foreach (var it in line.Items)
        {
            var px = x + it.X;
            switch (it.Kind)
            {
                case IpKind.Text: IpText(ip, it.Face, it.Fs, it.Rgb, px, baseTd, it.Text); break;
                case IpKind.Select: IpPaintSelect(ip, it, px, baseTd); break;
                case IpKind.Radio: IpPaintRadio(ip, it, px, baseTd); break;
                case IpKind.Check: IpPaintCheck(ip, it, px, baseTd); break;
                case IpKind.Button: IpPaintButton(ip, it.Text, it.Face, it.Fs, px, baseTd - it.Above, it.W, it.H); break;
                case IpKind.TextBox: IpPaintTextBox(ip, it, px, baseTd); break;
                case IpKind.DtPicker: IpPaintDtPicker(ip, it, px, baseTd); break;
                case IpKind.HyperBox: IpPaintHyperBox(ip, it, px, baseTd); break;
                case IpKind.Image: IpPaintBrokenImage(ip, px, baseTd - it.Above, it.W, it.H); break;
            }
        }
    }

    /// <summary>A select: the white fill a px inside the pen and a quarter pt over the box's top
    /// and bottom, the 1pt black rect half a pt inside it, the value at its measured seat.</summary>
    private static void IpPaintSelect(InfoPathState ip, IpItem it, double px, double baseTd)
    {
        var boxTop = baseTd - IpSelectAbove;
        var fillX = px + IpSelectFillInset;
        var fillW = it.W - IpSelectFillTrim;
        var fillTop = boxTop - IpSelectFillOutset;
        var fillH = IpSelectBoxH + 2 * IpSelectFillOutset + (it.Cjk ? IpSelectCjkExtra : 0);
        IpFillRect(ip, fillX, fillTop, fillW, fillH, "1 1 1");
        IpStrokeRect(ip, fillX + IpSelectStrokeInset, fillTop + IpSelectStrokeInset, fillW - 2 * IpSelectStrokeInset, fillH - 2 * IpSelectStrokeInset, "0 0 0", IpBorder);
        var face = it.Cjk ? IpCjkFace : IpFace;
        IpText(ip, face, it.Fs, "0 0 0", fillX + IpSelectTextX, boxTop + IpSelectTextDrop + (it.Cjk ? IpSelectCjkDrop : 0), it.Text);
    }

    private static void IpPaintRadio(InfoPathState ip, IpItem it, double px, double baseTd)
    {
        var left = px + it.Advance - IpInputMarginRight - IpInputBoxSize;
        var top = baseTd - IpInputBoxSize;
        var cx = left + IpInputBoxSize / 2;
        var cy = top + IpInputBoxSize / 2;
        IpCircle(ip, cx, cy, IpRadioRadius, "0 0 0", false);
        if (it.Checked) IpCircle(ip, cx, cy, IpRadioDotRadius, "0 0 0", true);
    }

    private static void IpPaintCheck(InfoPathState ip, IpItem it, double px, double baseTd)
    {
        var left = px + it.Advance - IpInputMarginRight - IpInputBoxSize;
        var top = baseTd - IpInputBoxSize;
        var inset = (IpInputBoxSize - IpCheckSquare) / 2;
        IpFillRect(ip, left + inset, top + inset, IpCheckSquare, IpCheckSquare, "1 1 1");
    }

    /// <summary>A push button: the #f0f0f0 fill, the #a9a9a9 bevel strokes inside it, the 1pt
    /// black rect outside it and the caption at the top pad.</summary>
    private static void IpPaintButton(InfoPathState ip, string text, string face, double fs, double x, double top, double w, double h)
    {
        IpFillRect(ip, x, top, w, h, IpButtonFillRgb);
        var i = IpButtonBevelInset;
        IpStrokeLine(ip, x, top + i, x + w, top + i, IpButtonBevelRgb, IpButtonBevelW);
        IpStrokeLine(ip, x + w - i, top, x + w - i, top + h, IpButtonBevelRgb, IpButtonBevelW);
        IpStrokeLine(ip, x, top + h - i, x + w, top + h - i, IpButtonBevelRgb, IpButtonBevelW);
        IpStrokeLine(ip, x + i, top, x + i, top + h, IpButtonBevelRgb, IpButtonBevelW);
        IpStrokeRect(ip, x - IpButtonRectX, top - IpButtonRectY, w + 2 * IpButtonRectX, h + 2 * IpButtonRectY, "0 0 0", IpBorder);
        if (text.Length > 0) IpText(ip, face, fs, "0 0 0", x + IpButtonPadX, top + IpButtonPadTop + IpAbove(fs), text);
    }

    /// <summary>A textbox: the white fill of its pads and inner line inside its margins, the
    /// inner line at the top pad; a nowrap box clips what overflows its padding box (the
    /// reference template cuts the text at the box edge).</summary>
    private static void IpPaintTextBox(InfoPathState ip, IpItem it, double px, double baseTd)
    {
        var fillX = px + IpBoxMargin;
        var fillTop = baseTd - it.Above + IpBoxMargin;
        var fillW = it.W + 2 * IpBoxPad;
        var fillH = it.H + 2 * IpBoxPad;
        IpFillRect(ip, fillX, fillTop, fillW, fillH, "1 1 1");
        if (it.Inner is not { Items.Count: > 0 } inner) return;
        var clipped = it.Nowrap && inner.Width > it.W + IpBoundary;
        if (clipped) ip.sb.Append(Compat.Format(ip.invc, $"q {fillX:0.###} {ip.pageH - fillTop - fillH:0.###} {fillW:0.###} {fillH:0.###} re W n\n"));
        IpPaintLine(ip, inner, fillX + IpBoxPad, fillTop + IpBoxPad);
        if (clipped) ip.sb.Append("Q\n");
    }

    /// <summary>The date picker: the xdDTText span's white content area (its text, or the
    /// swallowed-button box) and the picker's own white extent to the button, then the button.</summary>
    private static void IpPaintDtPicker(InfoPathState ip, IpItem it, double px, double baseTd)
    {
        var x0 = px + IpBoxMargin;
        var areaTop = baseTd - it.Fs * IpFaceAsc;
        var areaH = it.Fs * IpFaceSum;
        var buttonTop = baseTd - IpDtButtonAbove;
        if (it.Swallowed)
        {
            IpFillRect(ip, x0, areaTop, IpDtSwallowedW, areaH, "1 1 1");
            IpPaintButton(ip, "", IpFace, it.Fs, x0 - IpDtSwallowedW, buttonTop, IpDtButtonW, IpDtButtonH);
            return;
        }
        IpFillRect(ip, x0, areaTop, it.W, areaH, "1 1 1");
        IpText(ip, IpFace, it.Fs, it.Rgb, x0, baseTd, it.Text);
        var buttonX = x0 + it.W - IpDtButtonW;
        IpPaintButton(ip, "", IpFace, it.Fs, buttonX, buttonTop, IpDtButtonW, IpDtButtonH);
    }

    private static void IpPaintHyperBox(InfoPathState ip, IpItem it, double px, double baseTd)
    {
        var x = px + IpBoxMargin;
        var top = baseTd - it.Above + IpBoxMargin;
        var i = IpHyperBorder / 2;
        IpStrokeLine(ip, x, top + i, x + it.W, top + i, IpHyperRgb, IpHyperBorder);
        IpStrokeLine(ip, x + it.W - i, top, x + it.W - i, top + it.H, IpHyperRgb, IpHyperBorder);
        IpStrokeLine(ip, x, top + it.H - i, x + it.W, top + it.H - i, IpHyperRgb, IpHyperBorder);
        IpStrokeLine(ip, x + i, top, x + i, top + it.H, IpHyperRgb, IpHyperBorder);
    }

    /// <summary>The broken-image frame: a 1pt bevel (dark top and left, light right and bottom)
    /// a pt outside the declared box, with the 32px icon's frame inside its corner.</summary>
    private static void IpPaintBrokenImage(InfoPathState ip, double x, double top, double w, double h)
    {
        var fw = w + 2 * IpBrokenFrame;
        var fh = h + 2 * IpBrokenFrame;
        var i = IpBrokenFrame / 2;
        IpStrokeLine(ip, x, top + i, x + fw, top + i, IpBevelDarkRgb, IpBrokenFrame);
        IpStrokeLine(ip, x + i, top, x + i, top + fh, IpBevelDarkRgb, IpBrokenFrame);
        IpStrokeLine(ip, x + fw - i, top, x + fw - i, top + fh, IpBevelLightRgb, IpBrokenFrame);
        IpStrokeLine(ip, x, top + fh - i, x + fw, top + fh - i, IpBevelLightRgb, IpBrokenFrame);
        var ix = x + IpBrokenFrame + IpBrokenIconInset;
        var iy = top + IpBrokenFrame + IpBrokenIconInset;
        IpStrokeLine(ip, ix, iy + i, ix + IpBrokenIcon, iy + i, IpBevelDarkRgb, IpBrokenFrame);
        IpStrokeLine(ip, ix + i, iy, ix + i, iy + IpBrokenIcon, IpBevelDarkRgb, IpBrokenFrame);
        IpStrokeLine(ip, ix + IpBrokenIcon - i, iy, ix + IpBrokenIcon - i, iy + IpBrokenIcon, IpBevelLightRgb, IpBrokenFrame);
        IpStrokeLine(ip, ix, iy + IpBrokenIcon - i, ix + IpBrokenIcon, iy + IpBrokenIcon - i, IpBevelLightRgb, IpBrokenFrame);
    }
}
