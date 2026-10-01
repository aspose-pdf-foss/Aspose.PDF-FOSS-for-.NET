using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Transparency regions: each region's composite drawn back onto the page.</summary>
    private void CompositeTransparencyRegions(TransparencyRegionState ts)
    {
        foreach (var (r, mulColor) in ts.regions)
        {
            var x0 = Math.Max(0, r[0]);
            var y0 = Math.Max(0, r[1]);
            var x1 = Math.Min(ts.pageRect.Width, r[2]);
            var y1 = Math.Min(ts.pageRect.Height, r[3]);
            if (x1 - x0 < 1 || y1 - y0 < 1) continue;

            var px0 = Math.Max(0, (int)Math.Floor(x0 * ts.scalePx));
            var px1 = Math.Min(ts.pngW, (int)Math.Ceiling(x1 * ts.scalePx));
            var py0 = Math.Max(0, (int)Math.Floor((ts.pageRect.Height - y1) * ts.scalePy));
            var py1 = Math.Min(ts.pngH, (int)Math.Ceiling((ts.pageRect.Height - y0) * ts.scalePy));
            var cw = px1 - px0;
            var ch = py1 - py0;
            if (cw < 2 || ch < 2) continue;

            // Crop to opaque RGB24 (alpha composited over white).
            var comps = ts.hasAlpha ? 4 : 3;
            var crop = new byte[cw * ch * 3];
            for (var row = 0; row < ch; row++)
            {
                var src = ((py0 + row) * ts.pngW + px0) * comps;
                var dst = row * cw * 3;
                for (var col = 0; col < cw; col++)
                {
                    if (ts.hasAlpha)
                    {
                        var a = ts.pixels[src + 3];
                        crop[dst] = (byte)((ts.pixels[src] * a + 255 * (255 - a)) / 255);
                        crop[dst + 1] = (byte)((ts.pixels[src + 1] * a + 255 * (255 - a)) / 255);
                        crop[dst + 2] = (byte)((ts.pixels[src + 2] * a + 255 * (255 - a)) / 255);
                    }
                    else
                    {
                        crop[dst] = ts.pixels[src];
                        crop[dst + 1] = ts.pixels[src + 1];
                        crop[dst + 2] = ts.pixels[src + 2];
                    }
                    src += comps;
                    dst += 3;
                }
            }

            // Highlight annotation: reproduce its Multiply blend by scaling the
            // rendered backdrop by the appearance colour.
            if (mulColor is not null)
            {
                for (var k = 0; k < crop.Length; k += 3)
                {
                    crop[k] = (byte)(crop[k] * mulColor[0]);
                    crop[k + 1] = (byte)(crop[k + 1] * mulColor[1]);
                    crop[k + 2] = (byte)(crop[k + 2] * mulColor[2]);
                }
            }

            var stamp = ImageStamp.FromRgb(crop, cw, ch);
            var imgName = stamp.RegisterXObject(ts.page);
            ts.drawOps.Append("q\n")
                .Append(Fmt(x1 - x0)).Append(" 0 0 ").Append(Fmt(y1 - y0)).Append(' ')
                .Append(Fmt(x0)).Append(' ').Append(Fmt(y0)).Append(" cm\n/")
                .Append(imgName).Append(" Do\nQ\n");
        }
    }

    /// <summary>Transparency regions: the nested forms' transparent paints turned into no-ops.</summary>
    private void ApplyTransparentFormRewrites(TransparencyRegionState ts)
    {
        // Original appearance captured — the nested forms' transparent paints can now
        // become no-ops (the region composites drawn below carry their look).
        foreach (var (form, bytes) in ts.formRewrites)
        {
            form.ReplaceData(bytes);
            form.Dict.Remove("Filter");
            form.Dict.Remove("DecodeParms");
            form.Dict.Set("Length", new PdfInteger(bytes.Length));
        }
        ts.scalePx = ts.pngW / ts.pageRect.Width;
        ts.scalePy = ts.pngH / ts.pageRect.Height;
    }

    /// <summary>Transparency regions: the content regions merged, or collapsed to one bounding box when there are too many.</summary>
    private void CollectTransparencyRegions(TransparencyRegionState ts)
    {
        ts.maxDiscreteRegions = 400;
        if (ts.recolorConstantAlpha && ts.contentRegions.Count > ts.maxDiscreteRegions)
        {
            double bx0 = double.MaxValue, by0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue;
            foreach (var r in ts.contentRegions)
            {
                if (r[0] < bx0) bx0 = r[0];
                if (r[1] < by0) by0 = r[1];
                if (r[2] > bx1) bx1 = r[2];
                if (r[3] > by1) by1 = r[3];
            }
            ts.contentRegions.Clear();
            ts.contentRegions.Add(new[] { bx0, by0, bx1, by1 });
        }
        MergeRegions(ts.contentRegions);
        foreach (var box in ts.contentRegions) ts.regions.Add((box, null));
    }

    /// <summary>Transparency regions: the transparent annotations collected into regions and removed.</summary>
    private void CollectTransparentAnnotations(TransparencyRegionState ts)
    {
        if (ts.annotsArr is not null)
        {
            for (var i = 0; i < ts.annotsArr.Count; i++)
            {
                var annot = _reader.ResolveDict(ts.annotsArr[i]);
                if (annot?.GetName("Subtype") != "Highlight") continue;
                if (_reader.Resolve(annot.Get("Rect")) is not PdfArray rectArr || rectArr.Count != 4)
                    continue;
                var llx = Num(rectArr[0]);
                var lly = Num(rectArr[1]);
                var urx = Num(rectArr[2]);
                var ury = Num(rectArr[3]);
                var rw = Math.Abs(urx - llx);
                var rh = Math.Abs(ury - lly);
                if (rw < 1 || rh < 1) continue;

                var ap = _reader.ResolveDict(annot.Get("AP"));
                var form = ap is not null ? _reader.Resolve(ap.Get("N")) as PdfStream : null;
                if (form is null || form.Dict.GetName("Subtype") != "Form") continue;

                // Register the appearance form on the page and draw it at the
                // rect (BBox scaled to the rect like an annotation appearance).
                double bw = rw, bh = rh;
                if (_reader.Resolve(form.Dict.Get("BBox")) is PdfArray bbox && bbox.Count == 4)
                {
                    bw = Math.Abs(Num(bbox[2]) - Num(bbox[0]));
                    bh = Math.Abs(Num(bbox[3]) - Num(bbox[1]));
                }
                var scaleX = bw > 0 ? rw / bw : 1;
                var scaleY = bh > 0 ? rh / bh : 1;
                var name = RegisterPageXObject(ts.page, form);
                ts.flattenOps.Append("q\n")
                    .Append(Fmt(scaleX)).Append(" 0 0 ").Append(Fmt(scaleY)).Append(' ')
                    .Append(Fmt(Math.Min(llx, urx))).Append(' ').Append(Fmt(Math.Min(lly, ury))).Append(" cm\n/")
                    .Append(name).Append(" Do\nQ\n");
                var mulColor = FindAppearanceFillColor(form) ?? new[] { 1.0, 1.0, 0.0 };
                ts.regions.Add((new[] { Math.Min(llx, urx), Math.Min(lly, ury), Math.Max(llx, urx), Math.Max(lly, ury) }, mulColor));
                ts.annotIndices.Add(i);
            }
        }
    }

    /// <summary>Transparency regions: a pure recolour outcome applies its rewrites and needs no raster.</summary>
    private bool ApplyRecolourOnlyRewrites(TransparencyRegionState ts)
    {
        // Pure recolour outcome (Mask): every transparent paint kept its geometry with
        // a backdrop-blended colour — no regions to rasterise, just apply the rewrites.
        if (ts.contentRegions.Count == 0 && (ts.rewritten is not null || ts.formRewrites.Count > 0))
        {
            var annots = _reader.Resolve(ts.page.Dict.Get("Annots")) as PdfArray;
            var anyHighlight = false;
            if (annots is not null)
                for (var i = 0; i < annots.Count; i++)
                    if (_reader.ResolveDict(annots[i])?.GetName("Subtype") == "Highlight") anyHighlight = true;
            if (!anyHighlight)
            {
                foreach (var (form, bytes) in ts.formRewrites)
                {
                    form.ReplaceData(bytes);
                    form.Dict.Remove("Filter");
                    form.Dict.Remove("DecodeParms");
                    form.Dict.Set("Length", new PdfInteger(bytes.Length));
                }
                if (ts.rewritten is not null) ts.page.SetContentStream(ts.rewritten);
                return false;
            }
        }
        return true;
    }
}
