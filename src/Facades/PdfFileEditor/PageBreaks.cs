using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>
    /// Describes a single horizontal cut on a source page: <see cref="PageNumber"/>
    /// (1-based) identifies the page in the source document, <see cref="Position"/>
    /// the PDF y-coordinate where the page is split. Used as input to
    /// <see cref="AddPageBreak(Document, Document, PageBreak[])"/>. Multiple
    /// <c>PageBreak</c>s targeting the same source page produce multiple
    /// horizontal bands; the source page becomes that many destination pages
    /// in reading order (top of the original first).
    /// </summary>
    public class PageBreak
    {
        /// <summary>1-based source page number to split.</summary>
        public int PageNumber { get; set; }

        /// <summary>Y coordinate (in PDF user space) at which to split the page.</summary>
        public double Position { get; set; }

        public PageBreak() { }

        public PageBreak(int pageNumber, double position)
        {
            PageNumber = pageNumber;
            Position = position;
        }
    }

    /// <summary>
    /// Copy every source page into <paramref name="destination"/>, splitting any
    /// source page that is referenced by one or more <see cref="PageBreak"/>
    /// entries into separate destination pages whose MediaBoxes describe the
    /// horizontal band each page occupies in the original. Pages without a
    /// break are deep-cloned unchanged.
    /// </summary>
    /// <remarks>
    /// PDF readers (Adobe, our renderer) treat each page's MediaBox
    /// as the physical paper size and clip drawing operators outside it. So a
    /// source page with MediaBox [0,0,612,792] and a PageBreak at y=450 becomes:
    ///   - destination page with MediaBox [0,450,612,792] (top half)
    ///   - destination page with MediaBox [0,0,612,450] (bottom half)
    /// Both share the original content stream; the band that's visible at
    /// render time is the one whose MediaBox contains the drawing's y.
    ///
    /// Reading order is top-down: the band with the highest y range comes first.
    /// Multiple breaks on the same page produce that many extra destination pages
    /// (n breaks ⇒ n+1 bands).
    /// </remarks>
    /// <summary>
    /// Lift a break position clear of any text line it would cut. A line's guard band is
    /// its fragment rectangle raised by the font's own descent -
    /// <c>|FontDescriptor.Descent| / 1000 * fontSize</c> - and a break strictly inside a
    /// band moves to that band's TOP, so the line leaves with the band below it. A break
    /// that lands in the white space between lines is used exactly as asked.
    /// </summary>
    private static double SnapBreakClearOfTextLines(Page page, double y)
    {
        try
        {
            var absorber = new Text.TextFragmentAbsorber();
            absorber.Visit(page);
            var snapped = y;
            foreach (Text.TextFragment tf in absorber.TextFragments)
            {
                var rect = tf.Rectangle;
                if (rect is null) continue;
                var size = tf.TextState?.FontSize ?? 0;
                if (size <= 0) continue;
                // Descent is negative in a descriptor; the band sits that far ABOVE the
                // glyph box the absorber reports.
                var metrics = tf.TextState?.Font?.GetMetrics();
                var descent = metrics is null ? 0 : System.Math.Abs(metrics.Descent);
                var lift = descent * size / 1000.0;
                var bandBottom = rect.LLY + lift;
                var bandTop = rect.URY + lift;
                if (y > bandBottom && y < bandTop && bandTop > snapped) snapped = bandTop;
            }
            return snapped;
        }
        catch { return y; }
    }

    public void AddPageBreak(Document src, Document dest, PageBreak[] pageBreaks)
    {
        if (src is null || dest is null) return;

        // Group break y-positions by 1-based source page number.
        var breaksByPage = new Dictionary<int, List<double>>();
        if (pageBreaks is not null)
        {
            foreach (var b in pageBreaks)
            {
                if (b is null) continue;
                if (!breaksByPage.TryGetValue(b.PageNumber, out var ys))
                    breaksByPage[b.PageNumber] = ys = new List<double>();
                ys.Add(b.Position);
            }
        }

        for (var i = 1; i <= src.PageCount; i++)
        {
            var srcPage = src.Pages[i];
            if (!breaksByPage.TryGetValue(i, out var ys))
            {
                dest.Pages.Add(srcPage);
                continue;
            }

            // Build top-to-bottom bands from the source page's MediaBox + break ys.
            // PDF y increases upward, so reading order = descending y. n breaks
            // ⇒ n+1 bands. Bands include the original MediaBox's full x extent
            // and only restrict y to the band.
            var media = srcPage.MediaBox;
            // A break must not CUT a text line: one asked for inside a line moves up to
            // that line's top, so the line travels whole onto the next page (probed - see
            // the AddPageBreak law). Using the raw position instead put the band
            // 5.32 pt too high for a break landing inside a heading.
            for (var bi = 0; bi < ys.Count; bi++) ys[bi] = SnapBreakClearOfTextLines(srcPage, ys[bi]);
            ys.Sort();
            var edges = new List<double> { media.LLY };
            foreach (var y in ys) edges.Add(y);
            edges.Add(media.URY);

            for (var k = edges.Count - 1; k > 0; k--)
            {
                var bandTop = edges[k];
                var bandBottom = edges[k - 1];
                if (bandTop <= bandBottom) continue; // skip zero-height bands
                var added = dest.Pages.Add(srcPage);
                // Each band keeps the FULL original page size; only this band's content
                // is shown by clipping the page content to the band's y-range. The content
                // is split across full-size pages (the band sits at its original
                // position with the rest blank) rather than shrinking the page to the band.
                added.SetMediaBox(new Rectangle(media.LLX, media.LLY, media.URX, media.URY));
                added.Dict.Remove("CropBox");
                added.Dict.Remove("BleedBox");
                added.Dict.Remove("TrimBox");
                added.Dict.Remove("ArtBox");
                // Translate the band up so its top edge aligns with the top of the page
                // (each band is drawn at the top of a full page, not at its
                // original y), then clip to the band's y-range.
                var dy = media.URY - bandTop;
                var clip = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "q 1 0 0 1 0 {0:0.####} cm {1:0.####} {2:0.####} {3:0.####} {4:0.####} re W n\n",
                    dy, media.LLX, bandBottom, media.URX - media.LLX, bandTop - bandBottom);
                added.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(clip));
                added.AddContentStream(System.Text.Encoding.ASCII.GetBytes("\nQ"));

                // Annotations (form fields, links) draw from /Annots, not the content
                // stream, so the clip doesn't touch them. Keep only those whose rectangle
                // lies in this band and shift them up with the content; drop the rest.
                ClipAnnotationsToBand(added, bandBottom, bandTop, dy);
            }
        }
    }

    private static void ClipAnnotationsToBand(Page page, double bandBottom, double bandTop, double dy)
    {
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;
        var kept = new PdfArray();
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null) { kept.Add(item); continue; }
            if (reader.Resolve(annot.Get("Rect")) is not PdfArray rect || rect.Count < 4)
            {
                kept.Add(item);
                continue;
            }
            double y1 = NumberFrom(rect[1]), y3 = NumberFrom(rect[3]);
            // Assign by the annotation's bottom edge: one that straddles the break goes
            // entirely to the lower band (a field cut by the break
            // moves wholesale to the next page rather than being split).
            var bottomY = Math.Min(y1, y3);
            if (bottomY < bandBottom || bottomY >= bandTop) continue; // outside band — drop
            var newRect = new PdfArray();
            newRect.Add(new PdfReal(NumberFrom(rect[0])));
            newRect.Add(new PdfReal(y1 + dy));
            newRect.Add(new PdfReal(NumberFrom(rect[2])));
            newRect.Add(new PdfReal(y3 + dy));
            annot.Set("Rect", newRect);
            kept.Add(item);
        }
        page.Dict.Set("Annots", kept);
    }

    /// <summary>Apply the content-resize affine (x' = x*sx+tx, y' = y*sy+ty) to each
    /// annotation's /InkList stroke points. Only /InkList is handled here — the
    /// resize path already transforms /Rect and /QuadPoints, so they must NOT be
    /// touched again (double-transform). Mutates the annotation dictionaries in
    /// place.</summary>
    private static void TransformAnnotationGeometry(Page page, double sx, double sy, double tx, double ty)
    {
        var reader = page.Reader;
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;
        foreach (var item in annots)
        {
            var annot = reader.ResolveDict(item);
            if (annot is null) continue;

            // /InkList: an array of strokes, each a flat [x1 y1 x2 y2 …] coordinate list.
            if (reader.Resolve(annot.Get("InkList")) is PdfArray inkList)
            {
                var newInk = new PdfArray();
                foreach (var strokeObj in inkList)
                {
                    if (reader.Resolve(strokeObj) is PdfArray stroke)
                    {
                        var ns = new PdfArray();
                        for (var i = 0; i + 1 < stroke.Count; i += 2)
                        {
                            ns.Add(new PdfReal(NumberFrom(stroke[i]) * sx + tx));
                            ns.Add(new PdfReal(NumberFrom(stroke[i + 1]) * sy + ty));
                        }
                        newInk.Add(ns);
                    }
                    else newInk.Add(strokeObj);
                }
                annot.Set("InkList", newInk);
            }

            // /L: a line annotation's endpoints, a flat [x1 y1 x2 y2] list.
            if (reader.Resolve(annot.Get("L")) is PdfArray line)
            {
                var nl = new PdfArray();
                for (var i = 0; i + 1 < line.Count; i += 2)
                {
                    nl.Add(new PdfReal(NumberFrom(line[i]) * sx + tx));
                    nl.Add(new PdfReal(NumberFrom(line[i + 1]) * sy + ty));
                }
                annot.Set("L", nl);
            }

            // /Vertices: a flat [x1 y1 x2 y2 …] coordinate list (Polygon / PolyLine).
            if (reader.Resolve(annot.Get("Vertices")) is PdfArray vertices)
            {
                var nv = new PdfArray();
                for (var i = 0; i + 1 < vertices.Count; i += 2)
                {
                    nv.Add(new PdfReal(NumberFrom(vertices[i]) * sx + tx));
                    nv.Add(new PdfReal(NumberFrom(vertices[i + 1]) * sy + ty));
                }
                annot.Set("Vertices", nv);
            }
        }
    }

    private static double NumberFrom(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0.0,
    };

    /// <summary>Regenerate the normal appearance of Square/Circle annotations whose
    /// existing /AP /N is missing or carries no drawing operators (a degenerate stream,
    /// e.g. an empty body with a NaN BBox). Resize-with-normalization rebuilds
    /// such appearances, which would otherwise be left degenerate/absent.
    /// Scoped to <see cref="Annotations.CommonFigureAnnotation"/> with an already-degenerate
    /// appearance so valid appearances and other annotation types are left untouched.</summary>
    private static void NormalizeDegenerateShapeAppearances(Page page)
    {
        foreach (var annot in page.Annotations)
        {
            if (annot is not Annotations.CommonFigureAnnotation figure) continue;

            bool degenerate;
            try
            {
                var na = annot.NormalAppearance;
                degenerate = na is null || na.Contents.Count == 0;
            }
            catch { degenerate = true; }

            if (degenerate) figure.EnsureNormalizedAppearance();
        }
    }

    /// <summary>Stream overload of <see cref="AddPageBreak(Document, Document, PageBreak[])"/>.
    /// Reads <paramref name="src"/> into a <see cref="Document"/>, runs the page-break
    /// logic, and writes the result to <paramref name="dest"/>.</summary>
    public void AddPageBreak(Stream src, Stream dest, PageBreak[] pageBreaks)
    {
        if (src is null || dest is null) return;
        using var srcDoc = new Document(src);
        var dstDoc = new Document();
        AddPageBreak(srcDoc, dstDoc, pageBreaks);
        dstDoc.Save(dest);
    }

    /// <summary>File-path overload of <see cref="AddPageBreak(Document, Document, PageBreak[])"/>.</summary>
    public void AddPageBreak(string src, string dest, PageBreak[] pageBreaks)
    {
        if (src is null || dest is null) return;
        using var srcDoc = new Document(src);
        var dstDoc = new Document();
        AddPageBreak(srcDoc, dstDoc, pageBreaks);
        dstDoc.Save(dest);
    }
}
