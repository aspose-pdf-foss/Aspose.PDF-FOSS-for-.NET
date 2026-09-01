using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>Flattening a redaction annotation APPLIES it: the mark says the
    /// content beneath it must go, so the text under the rect is physically removed
    /// before the annotation is stamped and dropped. Painting the box alone would
    /// leave every redacted word still extractable (the expected result:
    /// an imported XFDF redact, flattened, no longer yields its text).</summary>
    private static void ApplyPendingRedactions(Document doc)
    {
        foreach (var page in doc.Pages)
        {
            List<Aspose.Pdf.Annotations.RedactionAnnotation>? redactions = null;
            foreach (var ann in page.Annotations)
                if (ann is Aspose.Pdf.Annotations.RedactionAnnotation ra)
                    (redactions ??= []).Add(ra);
            if (redactions is null) continue;
            foreach (var ra in redactions) ra.Redact();
        }
    }

    /// <summary>
    /// Redact an area on a page: adds a redaction annotation with the given fill color,
    /// then flattens the redaction (removes the annotation).
    /// </summary>
    /// <param name="pageIndex">1-based page number.</param>
    /// <param name="rect">The area to redact.</param>
    /// <param name="color">Fill color as RGB doubles [r, g, b] in 0.1 range.</param>
    public void RedactArea(int pageIndex, Rectangle rect, double[] color)
    {
        var doc = Document;
        if (pageIndex < 1 || pageIndex > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var page = doc.Pages.At(pageIndex);

        // Add a white content stream rectangle to cover the area
        var sb = new StringBuilder();
        sb.Append("q ");
        if (color is { Length: >= 3 })
            sb.Append($"{F(color[0])} {F(color[1])} {F(color[2])} rg ");
        else
            sb.Append("1 1 1 rg "); // white by default

        sb.Append($"{F(rect.LLX)} {F(rect.LLY)} {F(rect.Width)} {F(rect.Height)} re f Q");
        AppendContentStream(page, Encoding.Latin1.GetBytes(sb.ToString()));

        // Redaction must DESTROY the covered pixels, not merely paint over them: an
        // image extracted from the redacted document has to show the redaction colour
        // where it intersected the area. Off Windows this used to be skipped entirely -
        // the cover rectangle hid the area on screen while the original pixels stayed in
        // the image XObject, so anything that read the image back got the content the
        // redaction was meant to remove.
        if (OperatingSystem.IsWindows()) RedactImagesInArea(page, rect, color);
        else RedactImagesInAreaManaged(page, rect, color);

        // Redaction removes form-field widgets covered by the area (they are no longer
        // visible/usable), pruning them from the page /Annots and the AcroForm /Fields.
        RemoveWidgetsInArea(doc, page, rect);
    }

    /// <summary>The managed half of <see cref="RedactImagesInArea"/>: same placement
    /// arithmetic and the same rule about which images are baked, with the built-in PNG
    /// reader and JPEG writer standing in for the platform codec.</summary>
    private static void RedactImagesInAreaManaged(Page page, Rectangle area, double[]? color)
    {
        var absorber = new ImagePlacementAbsorber();
        try { absorber.Visit(page); } catch { return; }

        foreach (var placement in absorber.ImagePlacements)
        {
            var img = placement.Image;
            if (img is null || img.IsImageMask) continue;
            // Bilevel scans stay in their native 1-bit encoding, exactly as on Windows.
            if (img.BitsPerComponent == 1) continue;
            var r = placement.Rectangle;
            if (r is null || r.Width <= 0 || r.Height <= 0) continue;

            var ox0 = System.Math.Max(r.LLX, area.LLX);
            var oy0 = System.Math.Max(r.LLY, area.LLY);
            var ox1 = System.Math.Min(r.URX, area.URX);
            var oy1 = System.Math.Min(r.URY, area.URY);
            if (ox1 <= ox0 || oy1 <= oy0) continue;

            try
            {
                var (pix, w, h, hasAlpha) = Facades.PdfFileMend.DecodePng(img.ToPng());
                var comps = hasAlpha ? 4 : 3;
                if (w <= 0 || h <= 0 || pix.Length < (long)w * h * comps) continue;

                var px0 = System.Math.Clamp((int)System.Math.Floor((ox0 - r.LLX) / r.Width * w), 0, w);
                var px1 = System.Math.Clamp((int)System.Math.Ceiling((ox1 - r.LLX) / r.Width * w), 0, w);
                var py0 = System.Math.Clamp((int)System.Math.Floor((r.URY - oy1) / r.Height * h), 0, h);
                var py1 = System.Math.Clamp((int)System.Math.Ceiling((r.URY - oy0) / r.Height * h), 0, h);
                if (px1 <= px0 || py1 <= py0) continue;

                byte fr = 255, fg = 255, fb = 255;
                if (color is { Length: >= 3 })
                {
                    fr = (byte)System.Math.Round(System.Math.Clamp(color[0], 0, 1) * 255);
                    fg = (byte)System.Math.Round(System.Math.Clamp(color[1], 0, 1) * 255);
                    fb = (byte)System.Math.Round(System.Math.Clamp(color[2], 0, 1) * 255);
                }

                // Straight to RGBA, filling the covered block as it goes - the encoder
                // takes RGBA and the fill is what has to survive into the stored image.
                var rgba = new byte[(long)w * h * 4];
                for (var y = 0; y < h; y++)
                {
                    var covered = y >= py0 && y < py1;
                    for (var x = 0; x < w; x++)
                    {
                        var d = (y * w + x) * 4;
                        if (covered && x >= px0 && x < px1)
                        {
                            rgba[d] = fr; rgba[d + 1] = fg; rgba[d + 2] = fb; rgba[d + 3] = 255;
                            continue;
                        }
                        var s = (y * w + x) * comps;
                        rgba[d] = pix[s]; rgba[d + 1] = pix[s + 1]; rgba[d + 2] = pix[s + 2];
                        rgba[d + 3] = 255;
                    }
                }

                // JPEG, for the same reason the Windows path re-encodes as JPEG: a
                // lossless re-encode of a whole scan page inflates the document.
                img.ReplaceImageData(IO.JpegEncoderImpl.Encode(rgba, w, h, 75));
            }
            catch
            {
                // Undecodable image: the cover rectangle still hides the area visually.
            }
        }
    }

    /// <summary>Paint the redaction colour into every raster image whose placement
    /// intersects <paramref name="area"/>. Stencil masks are left alone (they carry
    /// no colour to redact; the cover rectangle hides their paint).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RedactImagesInArea(Page page, Rectangle area, double[]? color)
    {
        var absorber = new ImagePlacementAbsorber();
        try { absorber.Visit(page); } catch { return; }

        foreach (var placement in absorber.ImagePlacements)
        {
            var img = placement.Image;
            if (img is null || img.IsImageMask) continue;
            // Bilevel scans (CCITT/JBIG2) stay in their native 1-bit encoding: a
            // contone re-encode of a fax page inflates the document several-fold,
            // and the cover rectangle already hides the area. Only continuous-tone
            // images get the colour baked in.
            if (img.BitsPerComponent == 1) continue;
            var r = placement.Rectangle;
            if (r is null || r.Width <= 0 || r.Height <= 0) continue;

            var ox0 = System.Math.Max(r.LLX, area.LLX);
            var oy0 = System.Math.Max(r.LLY, area.LLY);
            var ox1 = System.Math.Min(r.URX, area.URX);
            var oy1 = System.Math.Min(r.URY, area.URY);
            if (ox1 <= ox0 || oy1 <= oy0) continue;

            try
            {
                using var src = new MemoryStream();
                img.Save(src, System.Drawing.Imaging.ImageFormat.Png);
                src.Position = 0;
                using var bmp = new System.Drawing.Bitmap(src);
                int w = bmp.Width, h = bmp.Height;

                var px0 = System.Math.Clamp((int)System.Math.Floor((ox0 - r.LLX) / r.Width * w), 0, w);
                var px1 = System.Math.Clamp((int)System.Math.Ceiling((ox1 - r.LLX) / r.Width * w), 0, w);
                var py0 = System.Math.Clamp((int)System.Math.Floor((r.URY - oy1) / r.Height * h), 0, h);
                var py1 = System.Math.Clamp((int)System.Math.Ceiling((r.URY - oy0) / r.Height * h), 0, h);
                if (px1 <= px0 || py1 <= py0) continue;

                var fill = color is { Length: >= 3 }
                    ? System.Drawing.Color.FromArgb(
                        (int)System.Math.Round(System.Math.Clamp(color[0], 0, 1) * 255),
                        (int)System.Math.Round(System.Math.Clamp(color[1], 0, 1) * 255),
                        (int)System.Math.Round(System.Math.Clamp(color[2], 0, 1) * 255))
                    : System.Drawing.Color.White;
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                using (var b = new System.Drawing.SolidBrush(fill))
                    g.FillRectangle(b, px0, py0, px1 - px0, py1 - py0);

                // Re-encode as JPEG: the replacement must stay in the same size
                // class as the original photographic/scan data — a lossless PNG
                // re-encode of a whole scan page inflates the document several-fold.
                using var outMs = new MemoryStream();
                bmp.Save(outMs, System.Drawing.Imaging.ImageFormat.Jpeg);
                img.ReplaceImageData(outMs.ToArray());
            }
            catch
            {
                // Undecodable image: the cover rectangle still hides the area visually.
            }
        }
    }

    /// <summary>Remove every Widget annotation whose /Rect intersects <paramref name="area"/>
    /// from the page and, for those that are (or become) empty form fields, from the
    /// AcroForm /Fields tree — a redaction drops the fields under it.</summary>
    private static void RemoveWidgetsInArea(Document doc, Page page, Rectangle area)
    {
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;

        static double Num(PdfObject? o) => o switch
        {
            PdfReal r => r.Value, PdfInteger i => i.Value, _ => 0.0,
        };

        var removeWidgets = new HashSet<PdfDictionary>();
        foreach (var item in annots)
        {
            var ad = reader.ResolveDict(item);
            if (ad is null || ad.GetName("Subtype") != "Widget") continue;
            if (reader.Resolve(ad.Get("Rect")) is not PdfArray r || r.Count < 4) continue;
            double llx = Num(r[0]), lly = Num(r[1]), urx = Num(r[2]), ury = Num(r[3]);
            if (System.Math.Min(urx, area.URX) > System.Math.Max(llx, area.LLX)
                && System.Math.Min(ury, area.URY) > System.Math.Max(lly, area.LLY))
                removeWidgets.Add(ad);
        }
        if (removeWidgets.Count == 0) return;

        RebuildArrayExcluding(page.Dict, "Annots", annots, removeWidgets, reader);
        var pageNum = doc.FindObjectNumber(page.Dict);
        if (pageNum > 0) doc.MarkDirty(pageNum, page.Dict);

        var acro = reader.ResolveDict(reader.Catalog?.Get("AcroForm"));
        var fields = acro is null ? null : reader.Resolve(acro.Get("Fields")) as PdfArray;
        if (fields is null || acro is null) return;

        var removeFields = new HashSet<PdfDictionary>();
        foreach (var w in removeWidgets)
        {
            var parent = reader.ResolveDict(w.Get("Parent"));
            if (parent is null)
            {
                // Merged-leaf field: the widget dict is itself the /Fields entry.
                removeFields.Add(w);
                continue;
            }
            // Detach the widget from its field's /Kids; drop the field if it is left empty.
            if (reader.Resolve(parent.Get("Kids")) is PdfArray pkids)
            {
                RebuildArrayExcluding(parent, "Kids", pkids, removeWidgets, reader);
                if ((reader.Resolve(parent.Get("Kids")) as PdfArray)?.Count == 0)
                    removeFields.Add(parent);
                var pn = doc.FindObjectNumber(parent);
                if (pn > 0) doc.MarkDirty(pn, parent);
            }
        }
        if (removeFields.Count > 0)
        {
            RebuildArrayExcluding(acro, "Fields", fields, removeFields, reader);
            var acroNum = doc.FindObjectNumber(acro);
            if (acroNum > 0) doc.MarkDirty(acroNum, acro);
        }
    }

    private static void RebuildArrayExcluding(PdfDictionary owner, string key,
        PdfArray arr, HashSet<PdfDictionary> exclude, IO.PdfReader reader)
    {
        var kept = new PdfArray();
        foreach (var item in arr)
        {
            var d = reader.ResolveDict(item);
            if (d is not null && exclude.Contains(d)) continue;
            kept.Add(item);
        }
        owner.Set(key, kept);
    }

    /// <summary>
    /// Redact an area on a page with a System.Drawing.Color-compatible (R, G, B) color.
    /// </summary>
    public void RedactArea(int pageIndex, Rectangle rect, int r, int g, int b)
    {
        RedactArea(pageIndex, rect, [r / 255.0, g / 255.0, b / 255.0]);
    }

    /// <summary>
    /// Redact an area on a page with the given fill color.
    /// </summary>
    public void RedactArea(int pageIndex, Rectangle rect, System.Drawing.Color color)
    {
        RedactArea(pageIndex, rect, color.R, color.G, color.B);
    }
}
