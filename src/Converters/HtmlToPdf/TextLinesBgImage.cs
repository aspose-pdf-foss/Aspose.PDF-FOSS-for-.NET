using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Load a block's background image once (an SVG is rasterised and keeps its root
    /// size as the natural size; a bitmap's natural size is its pixel size at 0.75 pt/px).</summary>
    private static void ResolveBackgroundImage(Block block, HtmlLoadOptions? options)
    {
        if (block.BgImageSrc is not { Length: > 0 } src || block.BgImageBytes is not null) return;
        var bytes = LoadConverterImage(src, options);
        if (bytes is null) return;
        if (src.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || LooksLikeSvg(bytes))
        {
            var (png, _, _) = ImageRasterizer.RasterizeSvgWithSize(bytes);
            if (png is null) return;
            block.BgImageBytes = png;
            // the root's width/height are CSS px (a 42 x 42 svg is a 31.5 pt picture)
            if (ImageRasterizer.SvgRootSizePt(bytes) is var (svgW, svgH))
            {
                block.BgImageNatWPt = svgW * 0.75;
                block.BgImageNatHPt = svgH * 0.75;
            }
            return;
        }
        block.BgImageBytes = bytes;
        if (TryReadImagePixelSize(bytes) is (var pxW, var pxH) && pxW > 0 && pxH > 0)
        {
            block.BgImageNatWPt = pxW * 0.75;
            block.BgImageNatHPt = pxH * 0.75;
        }
    }

    private static bool LooksLikeSvg(byte[] bytes)
    {
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(512, bytes.Length));
        return head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>The image size `background-size` gives a natural natW x natH image inside a
    /// boxW x boxH box: `cover` / `contain` scale it to the box by the larger / smaller ratio, one
    /// length (or `auto`) keeps the aspect, two lengths size both sides, a percentage is of the
    /// box, and none keeps the natural size.</summary>
    private static (double w, double h) BackgroundImageSize(string size, double natW, double natH, double boxW, double boxH)
    {
        if (natW <= 0 || natH <= 0) return (boxW, boxH);
        var v = size.Trim().ToLowerInvariant();
        if (v == "cover" || v == "contain")
        {
            var scale = v == "cover" ? Math.Max(boxW / natW, boxH / natH) : Math.Min(boxW / natW, boxH / natH);
            return (natW * scale, natH * scale);
        }
        double? Side(string t, double box)
        {
            if (t == "auto" || t.Length == 0) return null;
            if (Regex.Match(t, @"^([0-9.]+)%$") is { Success: true } pm)
                return box * double.Parse(pm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) / 100.0;
            return TryParseLength(t);
        }
        var parts = v.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var w = parts.Length > 0 ? Side(parts[0], boxW) : null;
        var h = parts.Length > 1 ? Side(parts[1], boxH) : null;
        if (w is null && h is null) return (natW, natH);
        if (w is null) return (h!.Value * natW / natH, h.Value);
        if (h is null) return (w.Value, w.Value * natH / natW);
        return (w.Value, h.Value);
    }

    /// <summary>The painted box's rectangle on the page - x, top, border-box width and height,
    /// and the border width it carries. A viewport-relative height spans the page's content
    /// height; the box inset is the body margin, which a zero-margin body has not got. A bordered
    /// painted box (background + width/height + border rule) fills its BORDER box - declared
    /// content + border on each side - and hangs from the flow's content origin; the borderless
    /// tile box keeps its calibrated one-body-margin inset.</summary>
    private static (double x, double top, double w, double h, double borderW) PaintedBoxRect(BlockTextState bt)
    {
        var boxH = bt.block.BgBoxHeightPt > 0 ? bt.block.BgBoxHeightPt
            : bt.block.BgBoxHeightVh * (bt.pageHeight - bt.marginTop - bt.marginBottom);
        var pbBw = bt.block.BorderWidth > 0 && bt.block.BorderColor is not null ? bt.block.BorderWidth : 0;
        // The metric flow: the cursor rests on the LINE BOX TOP (the text draws its drop below
        // it) and the content origin already carries the body inset, so the box hangs from the
        // cursor at the block's indent (measured on the sized-div sheet: the box at 96 x 111.81
        // for a first line box of 13.5 whose top the cursor sat on).
        // The box stands at the indent of the element that declared it and above the padding
        // its first line spent; an undeclared width is the content width from there.
        if (bt.profile.metricFlow && bt.metrics.metricDrop > 0)
        {
            // (the flow's content width reaches the page margin; the body inset on the right
            // is charged per block, so the box stops one inset short of it)
            var boxW = bt.block.BgBoxWidthPt > 0 ? bt.block.BgBoxWidthPt
                : bt.flow.contentWidth - UaBodyMarginPt - bt.block.BgBoxIndentPt;
            return (bt.marginLeft + bt.block.BgBoxIndentPt,
                Math.Min(bt.pageHeight - bt.marginTop, bt.flow.y + bt.block.PadTop),
                boxW + 2 * pbBw, boxH + 2 * pbBw, pbBw);
        }
        var boxInset = bt.profile.bodyZeroMargin ? 0.0 : UaBodyMarginPt;
        var bbX = bt.marginLeft + bt.block.LeftIndent + (pbBw > 0 ? 0 : boxInset);
        var bbTop = Math.Min(bt.pageHeight - bt.marginTop, bt.metrics.yBeforeBlockLines + bt.metrics.lineHeight)
            - boxInset;
        return (bbX, bbTop, bt.block.BgBoxWidthPt + 2 * pbBw, boxH + 2 * pbBw, pbBw);
    }

    /// <summary>Paint the block's background image over its declared box on the block's first
    /// line: the image sits at the box's top-left corner (the default `background-position`),
    /// sized by `background-size`, and is clipped to the box, so a picture larger than the box
    /// shows only its top-left part. The box origin is the one the painted-box fill uses.</summary>
    private static void PaintBackgroundImageBox(BlockTextState bt, double bbX, double bbTop, double boxW, double boxH)
    {
        if (bt.block.BgImageBytes is not { } bytes || boxW <= 0 || boxH <= 0) return;
        var (w, h) = BackgroundImageSize(bt.block.BgImageSize, bt.block.BgImageNatWPt, bt.block.BgImageNatHPt, boxW, boxH);
        if (w <= 0 || h <= 0) return;
        var page = bt.flow.page;
        page.AddContentStream(Encoding.ASCII.GetBytes(
            $"q {bbX.ToString("F2", bt.invc)} {(bbTop - boxH).ToString("F2", bt.invc)} {boxW.ToString("F2", bt.invc)} {boxH.ToString("F2", bt.invc)} re W n "));
        try
        {
            page.AddImage(bytes, new Rectangle(bbX, bbTop - h, bbX + w, bbTop));
        }
        catch { /* undecodable image: the box stays empty */ }
        page.AddContentStream(Encoding.ASCII.GetBytes("Q "));
    }

    /// <summary>An empty sized element with a background fills its box: the content width from
    /// its indent, its declared height down from the cursor (the UA-serif flow).</summary>
    private static void PaintSpacerBox(ConvertState cv, Block block, double spacer)
    {
        if (block.BackgroundColor is not { } fill || block.BgBoxHeightPt <= 0 || spacer <= 0) return;
        var x = cv.marginLeft + block.BgBoxIndentPt;
        var w = cv.flow.contentWidth - UaBodyMarginPt - block.BgBoxIndentPt - block.RightInsetPt;
        if (w <= 0) return;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(Compat.Format(inv,
            $"q {fill.R / 255.0:0.#####} {fill.G / 255.0:0.#####} {fill.B / 255.0:0.#####} rg {x:0.##} {cv.flow.y - spacer:0.##} {w:0.##} {spacer:0.##} re f Q ")));
    }
}
