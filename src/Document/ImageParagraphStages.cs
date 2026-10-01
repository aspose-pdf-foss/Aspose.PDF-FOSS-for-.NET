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
    /// <summary>Image paragraph: one frame's drawn width and height settled from Fix box, SVG viewport or natural size.</summary>
    private static (double imgW, double imgH) MeasureImageFrame(ImageParagraphState ip, byte[] frameData)
    {
        double imgW, imgH;
        var (fixNatW, fixNatH) = TryGetImageNaturalSizePt(frameData, ip.img.IsApplyResolution) ?? (0, 0);
        var haveNat = fixNatW > 0 && fixNatH > 0;
        if (ip.img.FixWidth > 0 || ip.img.FixHeight > 0)
        {
            // EITHER Fix dimension counts on its own and the OTHER axis keeps the
            // source's own pixel measure — probed 2026-08-26: a 240x60 picture
            // under FixHeight 20 draws 240x20, under FixWidth 100 draws 100x60.
            // (Spanning the band on the unset axis stretched every lone-Fix
            // picture to the content width.)
            imgW = ip.img.FixWidth > 0 ? ip.img.FixWidth : haveNat ? fixNatW : ip.availW;
            imgH = ip.img.FixHeight > 0 ? ip.img.FixHeight : haveNat ? fixNatH : ip.availH;
            // A Fix box larger than the content band is squashed to the
            // band on that axis (never clipped or spilled to a fresh
            // page): a 662 pt FixHeight on a 451 pt landscape band
            // draws as a 451 pt image, width kept - unless the caller asked
            // for the box exactly, in which case the page cuts it.
            if (!ip.img.FixBoxIsExact)
            {
                imgW = Math.Min(imgW, ip.availW);
                imgH = Math.Min(imgH, ip.availH);
            }
        }
        else if (ip.svgNatW > 0 && ip.svgNatH > 0)
        {
            // Vector (SVG) source: the authored viewport size in points
            // (1:1, rounded), each axis independently clamped into the
            // content box — a too-wide chart is squeezed
            // to the page width while keeping vertical scale 1:1.
            var scale = ip.img.ImageScale > 0 ? ip.img.ImageScale : 1.0;
            imgW = Math.Min(Math.Round(ip.svgNatW) * scale, ip.availW);
            imgH = Math.Min(Math.Round(ip.svgNatH) * scale, ip.availH);
        }
        else if (haveNat && ip.img.FitToBandWidth)
        {
            // The picture spans the band between its margins, at its own aspect.
            var (ml, mr) = (ip.img.Margin?.Left ?? 0, ip.img.Margin?.Right ?? 0);
            var scale = Math.Max(0, ip.availW - ml - mr) / fixNatW;
            imgW = fixNatW * scale;
            imgH = fixNatH * scale;
        }
        else if (haveNat && ip.img.FitToRemainingArea)
        {
            // The picture fills what is free: the band's width or the room left
            // under the cursor on this page, whichever it reaches first, at its
            // own aspect - up as well as down.
            var (ml, mr) = (ip.img.Margin?.Left ?? 0, ip.img.Margin?.Right ?? 0);
            var roomW = Math.Max(0, ip.availW - ml - mr);
            var roomH = Math.Max(0, ip.flow.CurrentY - ip.marginBottom);
            var scale = Math.Min(roomW / fixNatW, roomH / fixNatH);
            imgW = fixNatW * scale;
            imgH = fixNatH * scale;
        }
        else if (haveNat)
        {
            var natWpt = fixNatW;
            var natHpt = fixNatH;
            // No explicit size: start from the image's intrinsic dimensions
            // (pixels mapped 1:1 to points unless IsApplyResolution honours the
            // embedded DPI), optionally scaled by ImageScale.
            var scale = ip.img.ImageScale > 0 ? ip.img.ImageScale : 1.0;
            imgW = natWpt * scale;
            imgH = natHpt * scale;
            if (ip.img.IsApplyResolution)
            {
                // Resolution-aware: fit to the content width preserving the
                // aspect ratio (the IsApplyResolution contract).
                if (imgW > ip.availW && imgW > 0)
                {
                    imgH *= ip.availW / imgW;
                    imgW = ip.availW;
                }
            }
            else
            {
                // Default: an oversized image is fitted into the content area by
                // clamping each axis independently to the available width/height
                // -- no aspect preservation.
                imgW = Math.Min(imgW, ip.availW);
                imgH = Math.Min(imgH, ip.availH);
            }
        }
        else
        {
            imgW = ip.availW;
            imgH = ip.availH;
        }
        return (imgW, imgH);
    }

    /// <summary>Image paragraph: one frame seated on its page and drawn.</summary>
    private bool LayoutImageFrame(ImageParagraphState ip, int frameIdx)
    {
        var frameData = ip.frames![frameIdx];
        var (imgW, imgH) = MeasureImageFrame(ip, frameData);
        // The box the image occupies: the picture plus its border bands, inside
        // its own left and right margins.
        var (bl, bb, br, bt) = ip.img.Border is { } border ? FlowLayout.BorderBands(border) : (0, 0, 0, 0);
        var (marginL, marginR) = (ip.img.Margin?.Left ?? 0, ip.img.Margin?.Right ?? 0);
        var boxW = imgW + bl + br;
        var boxH = imgH + bt + bb;

        // The first frame follows the flow; every extra frame starts a fresh page.
        // An image too tall for ANY page (taller than the full content band)
        // stays on the current page and lets the page clip it — pushing it to
        // a fresh page would still not fit and loses the flow position. A fresh
        // page is the flow's NEXT region, so it follows this page wherever the
        // document already has pages after it.
        var fitsNowhere = boxH > ip.page.Height - ip.marginTop - ip.marginBottom;
        var fitsHere = ip.flow.CurrentY - boxH >= ip.marginBottom
            || (fitsNowhere && ip.flow.CurrentY >= ip.page.Height - ip.marginTop - 1e-6);
        if (frameIdx > 0 || !fitsHere) ip.flow.ForceNewPage();
        var yTop = ip.flow.CurrentY;
        // Honour the image's horizontal alignment within the content
        // box; without this every image is pinned to the left margin
        // regardless of HorizontalAlignment.Right / Center.
        var bandLeft = ip.marginLeft + marginL;
        var bandWidth = ip.availW - marginL - marginR;
        double boxX = ip.img.HorizontalAlignment switch
        {
            HorizontalAlignment.Right => bandLeft + bandWidth - boxW,
            HorizontalAlignment.Center => bandLeft + (bandWidth - boxW) / 2,
            _ => bandLeft,
        };
        var box = new Rectangle(boxX, yTop - boxH, boxX + boxW, yTop);
        var rect = new Rectangle(boxX + bl, yTop - bt - imgH, boxX + bl + imgW, yTop - bt);
        // The background and the border go down first, under the picture.
        var paint = FlowLayout.BuildBlockPaint(ip.img.Border, box, ip.img.BackgroundColor, new FlowLayout.BlockFill(box, null, null));
        try
        {
            ip.flow.PlaceImageFrame(frameData, rect, paint, ip.embedBlackWhite);
        }
        catch (ArgumentException)
        {
            return true;
        }
        // A Hyperlink on the image covers its placed rectangle with a
        // Link annotation, the same way hyperlinked text runs do.
        if (ip.img.Hyperlink is not null && ip.flow.CurrentSlot < 0)
            ip.flow.CurrentPage.EmitHyperlinkAnnotation(rect, ip.img.Hyperlink);
        // Inline images keep the cursor on the shared line and only
        // record their height; the line is closed (cursor dropped) by
        // the next block image or the end-of-flow flush below.
        if (ip.img.IsInLineParagraph && ip.frames.Count == 1)
            ip.pendingInline = Math.Max(ip.pendingInline, boxH);
        else
            ip.flow.AdvanceY(boxH);
        return true;
    }

    /// <summary>Image paragraph: the source sniffed for JPEG/PNG/JPX and split into frames.</summary>
    private static bool SplitImageFrames(ImageParagraphState ip)
    {
        ip.hdr0 = ip.imgData!.Length > 0 ? ip.imgData[0] : (byte)0;
        ip.hdr1 = ip.imgData.Length > 1 ? ip.imgData[1] : (byte)0;
        ip.isJpeg = ip.hdr0 == 0xFF && ip.hdr1 == 0xD8 && !IsProgressiveJpeg(ip.imgData);
        ip.isPng = ip.imgData.Length >= 4 && ip.hdr0 == 0x89 && ip.hdr1 == 0x50
                    && ip.imgData[2] == 0x4E && ip.imgData[3] == 0x47;
        ip.isJpx = (ip.imgData.Length >= 12 && ip.hdr0 == 0x00 && ip.hdr1 == 0x00
                     && ip.imgData[2] == 0x00 && ip.imgData[3] == 0x0C && ip.imgData[4] == 0x6A
                     && ip.imgData[5] == 0x50 && ip.imgData[6] == 0x20 && ip.imgData[7] == 0x20)
                    || (ip.imgData.Length >= 4 && ip.hdr0 == 0xFF && ip.hdr1 == 0x4F
                        && ip.imgData[2] == 0xFF && ip.imgData[3] == 0x51);
        ip.frames = ip.isJpeg || ip.isPng || ip.isJpx
            ? new System.Collections.Generic.List<byte[]> { ip.imgData }
            : TryDecodeImageFramesAsPng(ip.imgData);
        if (ip.frames is null || ip.frames.Count == 0) return false;
        return true;
    }

    /// <summary>Image paragraph: a bilevel Group 4 TIFF embeds its existing CCITT strips, no re-encode.</summary>
    private bool TryEmitBilevelFrames(ImageParagraphState ip)
    {
        // IsBlackWhite fast path: a bilevel Group 4 TIFF embeds its existing
        // CCITT strips directly (no re-encode), giving the compact 1-bit output
        // the property promises instead of a bulky re-rasterised copy.
        if (ip.img.IsBlackWhite
            && IO.CcittTiffExtractor.TryExtract(ip.imgData!) is { Count: > 0 } g4Frames)
        {
            var availWbw = ip.page.Width - ip.marginLeft - ip.marginRight;
            var availHbw = ip.page.Height - ip.marginTop - ip.marginBottom;
            for (int fi = 0; fi < g4Frames.Count; fi++)
            {
                var g4 = g4Frames[fi];
                double imgWbw, imgHbw;
                if (ip.img.FixWidth > 0 || ip.img.FixHeight > 0)
                {
                    // A Fix dimension counts on its own and the OTHER axis keeps the
                    // source's own pixel measure — see LoadFlowImage.
                    imgWbw = Math.Min(ip.img.FixWidth > 0 ? ip.img.FixWidth : g4.Width, availWbw);
                    imgHbw = Math.Min(ip.img.FixHeight > 0 ? ip.img.FixHeight : g4.Height, availHbw);
                }
                else
                {
                    // Pixels map 1:1 to points (optionally scaled by ImageScale),
                    // clamped per-axis into the content box.
                    var scaleBw = ip.img.ImageScale > 0 ? ip.img.ImageScale : 1.0;
                    imgWbw = Math.Min(g4.Width * scaleBw, availWbw);
                    imgHbw = Math.Min(g4.Height * scaleBw, availHbw);
                }
                Page targetPageBw;
                double yTopBw;
                if (fi == 0 && ip.flow.CurrentY - imgHbw >= ip.marginBottom)
                {
                    targetPageBw = ip.flow.CurrentPage;
                    yTopBw = ip.flow.CurrentY;
                }
                else
                {
                    ip.flow.Commit();
                    targetPageBw = Pages.Add();
                    targetPageBw.MediaBox = new Rectangle(0, 0, ip.page.Width, ip.page.Height);
                    Table.RegisterFont(targetPageBw);
                    yTopBw = ip.page.Height - ip.marginTop;
                }
                var rectBw = new Rectangle(ip.marginLeft, yTopBw - imgHbw,
                                           ip.marginLeft + imgWbw, yTopBw);
                targetPageBw.AddCcittImage(g4.Data, g4.Width, g4.Height, g4.BlackIs1, rectBw);
                if (ip.img.Hyperlink is not null)
                    targetPageBw.EmitHyperlinkAnnotation(rectBw, ip.img.Hyperlink);
                if (ReferenceEquals(targetPageBw, ip.flow.CurrentPage))
                    ip.flow.AdvanceY(imgHbw);
                else
                    ip.flow.ResetToTopOfNextPage();
            }
            return false;
        }
        return true;
    }

    /// <summary>Image paragraph: an SVG source rasterised, letterboxed onto the Fix box when one is set.</summary>
    private static void RasterizeSvgSource(ImageParagraphState ip)
    {
        ip.svgNatW = 0;
        ip.svgNatH = 0;
        if (XImageCollection.IsSvg(ip.imgData!))
        {
            // With a Fix box the artwork is letterboxed (aspect-fit,
            // centred) onto a canvas of the box's aspect;
            // the box itself may then stretch/clamp freely.
            byte[]? svgPng;
            if (ip.img.FixWidth > 0 && ip.img.FixHeight > 0)
                svgPng = ImageRasterizer.RasterizeSvgOnCanvas(ip.imgData!, ip.img.FixWidth, ip.img.FixHeight);
            else
                (svgPng, ip.svgNatW, ip.svgNatH) = ImageRasterizer.RasterizeSvgWithSize(ip.imgData!);
            if (svgPng is not null)
            {
                if (ip.img.FixWidth > 0 && ip.img.FixHeight > 0)
                    (ip.svgNatW, ip.svgNatH) = (ip.img.FixWidth, ip.img.FixHeight);
                ip.imgData = svgPng;
            }
            else { ip.svgNatW = 0; ip.svgNatH = 0; }
        }
    }

    /// <summary>Image paragraph: a Base64 source decoded to raw image bytes.</summary>
    private static void DecodeBase64Source(ImageParagraphState ip)
    {
        // ImageFileType.Base64: the stream carries base64 TEXT (optionally a
        // full data:image/...;base64, URI), not raw image bytes — decode it
        // first or the raster embed below silently drops the image.
        if (ip.img.FileType == ImageFileType.Base64 && ip.imgData!.Length > 0)
        {
            try
            {
                var s64 = System.Text.Encoding.ASCII.GetString(ip.imgData).Trim();
                var comma = s64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                    ? s64.IndexOf(',') : -1;
                if (comma >= 0) s64 = s64[(comma + 1)..];
                ip.imgData = System.Convert.FromBase64String(s64);
            }
            catch (FormatException) { /* not base64 after all: keep the raw bytes */ }
        }
    }

    /// <summary>Image paragraph: the source bytes read from the stream or the file.</summary>
    private static void ReadImageSourceBytes(ImageParagraphState ip)
    {
        ip.imgData = null;
        if (ip.img.ImageStream is not null)
        {
            var pos = ip.img.ImageStream.CanSeek ? ip.img.ImageStream.Position : -1L;
            // Rewind when seekable: callers commonly hand us a stream after
            // reading dimensions with `new Bitmap(stream)`, which leaves the
            // position at end-of-stream. Without this the image silently disappears.
            if (ip.img.ImageStream.CanSeek) ip.img.ImageStream.Position = 0;
            using var imgMem = new System.IO.MemoryStream();
            ip.img.ImageStream.CopyTo(imgMem);
            ip.imgData = imgMem.ToArray();
            if (pos >= 0) ip.img.ImageStream.Position = pos;
        }
        else
        {
            ip.imgData = ip.img.ReadSourceBytes();
        }
    }
}
