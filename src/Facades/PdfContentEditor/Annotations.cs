using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
    /// <summary>
    /// Create a local link annotation that navigates to a page in the same document.
    /// </summary>
    /// <param name="input">Source PDF bytes.</param>
    /// <param name="rect">Link rectangle on the page.</param>
    /// <param name="pageNumber">The page where the link is placed (1-based).</param>
    /// <param name="destinationPage">The target page number (1-based).</param>
    public byte[] CreateLocalLink(byte[] input, Rectangle rect, int pageNumber, int destinationPage)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = BuildLinkAnnotation(rect, destinationPage);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a URI link annotation that opens a URL.
    /// </summary>
    public byte[] CreateWebLink(byte[] input, Rectangle rect, int pageNumber, string url)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = BuildUriAnnotation(rect, url);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a free text annotation on a page.
    /// </summary>
    public byte[] CreateFreeText(byte[] input, Rectangle rect, int pageNumber, string text, string? fontName = null, double fontSize = 12)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = BuildFreeTextAnnotation(rect, text, fontName ?? "Helvetica", fontSize);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a text (sticky note) annotation on a page.
    /// </summary>
    public byte[] CreateText(byte[] input, Rectangle rect, int pageNumber, string title, string contents)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("Text"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(title)));
        annotDict.Set("Contents", new PdfString(System.Text.Encoding.Latin1.GetBytes(contents)));
        annotDict.Set("Open", PdfBoolean.False);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Delete all annotations on a specific page.
    /// </summary>
    public byte[] DeleteAnnotations(byte[] input, int pageNumber)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        page.Dict.Set("Annots", new PdfArray());
        return doc.ToArray();
    }

    /// <summary>
    /// Delete annotations of a specific subtype from a page.
    /// </summary>
    public byte[] DeleteAnnotations(byte[] input, int pageNumber, string annotationType)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annots = doc.Reader.Resolve(page.Dict.Get("Annots"));
        if (annots is not PdfArray annotArray) return input;

        var kept = new PdfArray();
        foreach (var item in annotArray)
        {
            var resolved = doc.Reader.ResolveDict(item);
            if (resolved is not null)
            {
                var subtype = resolved.GetName("Subtype");
                if (subtype != annotationType)
                    kept.Add(item);
            }
        }
        page.Dict.Set("Annots", kept);
        return doc.ToArray();
    }

    private static PdfDictionary BuildLinkAnnotation(Rectangle rect, int destinationPage)
    {
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("Link"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("Border", new PdfArray(new List<PdfObject>
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)
        }));

        var dest = new PdfArray();
        dest.Add(new PdfInteger(destinationPage - 1)); // 0-based page index
        dest.Add(new PdfName("Fit"));
        annotDict.Set("Dest", dest);
        return annotDict;
    }

    private static PdfDictionary BuildUriAnnotation(Rectangle rect, string url)
    {
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("Link"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("Border", new PdfArray(new List<PdfObject>
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)
        }));

        var actionDict = new PdfDictionary();
        actionDict.Set("S", new PdfName("URI"));
        actionDict.Set("URI", new PdfString(System.Text.Encoding.Latin1.GetBytes(url)));
        annotDict.Set("A", actionDict);
        return annotDict;
    }

    private static PdfDictionary BuildFreeTextAnnotation(Rectangle rect, string text, string fontName, double fontSize)
    {
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("FreeText"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("Contents", new PdfString(System.Text.Encoding.Latin1.GetBytes(text)));
        // Print flag (bit 3 = 4): created annotations must be printable.
        // Same default as AnnotationCollection.
        annotDict.Set("F", new PdfInteger(4));
        annotDict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(
            $"/{fontName} {fontSize.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} Tf")));
        return annotDict;
    }

    private static PdfArray RectToPdfArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    private static void AppendAnnotation(Page page, PdfDictionary annotDict)
    {
        var existing = page.Dict.Get("Annots");
        PdfArray annotArray;
        if (existing is PdfArray arr)
        {
            annotArray = arr;
        }
        else
        {
            annotArray = new PdfArray();
            page.Dict.Set("Annots", annotArray);
        }
        annotArray.Add(annotDict);
    }

    /// <summary>
    /// Overload that converts <see cref="System.Drawing.Rectangle"/>
    /// to the PDF rectangle form and delegates.
    /// </summary>
    public void DrawCurve(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents)
        => DrawCurve(lineInfo, page, DrawingRectToPdfRect(annotRect), annotContents);

    /// <summary>
    /// Draw a curve (polyline) on a page. The curve is added as a path in the content stream,
    /// not as an annotation — existing annotations are not affected.
    /// </summary>
    public void DrawCurve(LineInfo lineInfo, int pageNumber, Rectangle rect, string? message)
    {
        if (_document is null) throw new InvalidOperationException("No PDF bound");
        if (pageNumber < 1 || pageNumber > _document.PageCount) return;

        var page = _document.Pages.At(pageNumber);
        var verts = lineInfo.VerticeCoordinate;
        if (verts is null || verts.Length < 4) return;

        var builder = new Content.ContentStreamBuilder();
        builder.SaveState();
        builder.SetLineWidth(lineInfo.LineWidth);

        var lr = lineInfo.LineColorR / 255.0;
        var lg = lineInfo.LineColorG / 255.0;
        var lb = lineInfo.LineColorB / 255.0;
        builder.SetStrokeColor(lr, lg, lb);

        builder.MoveTo(verts[0], verts[1]);
        for (int i = 2; i + 1 < verts.Length; i += 2)
            builder.LineTo(verts[i], verts[i + 1]);
        builder.Stroke();
        builder.RestoreState();

        page.AddContentStream(builder.Build());
    }

    private static Rectangle DrawingRectToPdfRect(System.Drawing.Rectangle r)
        => new(r.X, r.Y, r.X + r.Width, r.Y + r.Height);

    private static PdfArray DrawingColorToPdfArray(System.Drawing.Color c)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(c.R / 255.0));
        arr.Add(new PdfReal(c.G / 255.0));
        arr.Add(new PdfReal(c.B / 255.0));
        return arr;
    }

    private static PdfString Latin1(string s)
        => new(System.Text.Encoding.Latin1.GetBytes(s ?? ""));

    private Page GetPage1Based(int pageNumber)
    {
        var doc = EnsureBound();
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        return doc.Pages.At(pageNumber);
    }

    private void AddAnnotation(int page, PdfDictionary annotDict)
        => AppendAnnotation(GetPage1Based(page), annotDict);

    public void CreateLocalLink(System.Drawing.Rectangle rect, int desPage, int originalPage)
    {
        var dict = BuildLinkAnnotation(DrawingRectToPdfRect(rect), desPage);
        AddAnnotation(originalPage, dict);
    }

    public void CreateLocalLink(System.Drawing.Rectangle rect, int desPage, int originalPage, System.Drawing.Color clr)
    {
        var dict = BuildLinkAnnotation(DrawingRectToPdfRect(rect), desPage);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(originalPage, dict);
    }

    public void CreateLocalLink(System.Drawing.Rectangle rect, int desPage, int originalPage, System.Drawing.Color clr, System.Enum[] actionName)
    {
        // actionName carries an "additional action" sequence; we accept it for API compatibility
        // and apply only the color + destination — additional actions are an advanced feature.
        CreateLocalLink(rect, desPage, originalPage, clr);
    }

    public void CreateWebLink(System.Drawing.Rectangle rect, string url, int originalPage)
    {
        var dict = BuildUriAnnotation(DrawingRectToPdfRect(rect), url);
        AddAnnotation(originalPage, dict);
    }

    public void CreateWebLink(System.Drawing.Rectangle rect, string url, int originalPage, System.Drawing.Color clr)
    {
        var dict = BuildUriAnnotation(DrawingRectToPdfRect(rect), url);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(originalPage, dict);
    }

    public void CreateWebLink(System.Drawing.Rectangle rect, string url, int originalPage, System.Drawing.Color clr, System.Enum[] actionName)
    {
        CreateWebLink(rect, url, originalPage, clr);
    }

    public void CreateApplicationLink(System.Drawing.Rectangle rect, string application, int page)
    {
        var dict = BuildLaunchAnnotation(DrawingRectToPdfRect(rect), application);
        AddAnnotation(page, dict);
    }

    public void CreateApplicationLink(System.Drawing.Rectangle rect, string application, int page, System.Drawing.Color clr)
    {
        var dict = BuildLaunchAnnotation(DrawingRectToPdfRect(rect), application);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(page, dict);
    }

    public void CreateApplicationLink(System.Drawing.Rectangle rect, string application, int page, System.Drawing.Color clr, System.Enum[] actionName)
    {
        CreateApplicationLink(rect, application, page, clr);
    }

    public void CreatePdfDocumentLink(System.Drawing.Rectangle rect, string remotePdf, int originalPage, int destinationPage)
    {
        var dict = BuildGoToRAnnotation(DrawingRectToPdfRect(rect), remotePdf, destinationPage);
        AddAnnotation(originalPage, dict);
    }

    public void CreatePdfDocumentLink(System.Drawing.Rectangle rect, string remotePdf, int originalPage, int destinationPage, System.Drawing.Color clr)
    {
        var dict = BuildGoToRAnnotation(DrawingRectToPdfRect(rect), remotePdf, destinationPage);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(originalPage, dict);
    }

    public void CreatePdfDocumentLink(System.Drawing.Rectangle rect, string remotePdf, int originalPage, int destinationPage, System.Drawing.Color clr, System.Enum[] actionName)
    {
        CreatePdfDocumentLink(rect, remotePdf, originalPage, destinationPage, clr);
    }

    public void CreateJavaScriptLink(string code, System.Drawing.Rectangle rect, int originalPage, System.Drawing.Color color)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("C", DrawingColorToPdfArray(color));
        var act = new PdfDictionary();
        act.Set("S", new PdfName("JavaScript"));
        act.Set("JS", Latin1(code ?? ""));
        dict.Set("A", act);
        AddAnnotation(originalPage, dict);
    }

    public void CreateCustomActionLink(System.Drawing.Rectangle rect, int originalPage, System.Drawing.Color color, System.Enum[] actionName)
    {
        // No specific subtype — emit a Link with the colour; additional-action chain is no-op.
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("C", DrawingColorToPdfArray(color));
        AddAnnotation(originalPage, dict);
    }

    public void CreateFreeText(System.Drawing.Rectangle rect, string contents, int page)
    {
        var dict = BuildFreeTextAnnotation(DrawingRectToPdfRect(rect), contents ?? "", "Helvetica", 12);
        AddAnnotation(page, dict);
    }

    public void CreateText(System.Drawing.Rectangle rect, string title, string contents, bool open, string icon, int page)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Text"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("T", Latin1(title ?? ""));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("Open", open ? PdfBoolean.True : PdfBoolean.False);
        if (!string.IsNullOrEmpty(icon))
            dict.Set("Name", new PdfName(icon));
        AddAnnotation(page, dict);
    }

    public void CreateCaret(int page, System.Drawing.Rectangle annotRect, System.Drawing.Rectangle caretRect, string symbol, string annotContents, System.Drawing.Color color)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Caret"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(annotRect)));
        dict.Set("Contents", Latin1(annotContents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(color));
        if (!string.IsNullOrEmpty(symbol))
            dict.Set("Sy", new PdfName(symbol));
        // RD (differences between Rect and caret bbox): [left, top, right, bottom]
        var rd = new PdfArray();
        rd.Add(new PdfReal(Math.Max(0, caretRect.X - annotRect.X)));
        rd.Add(new PdfReal(Math.Max(0, (annotRect.Y + annotRect.Height) - (caretRect.Y + caretRect.Height))));
        rd.Add(new PdfReal(Math.Max(0, (annotRect.X + annotRect.Width) - (caretRect.X + caretRect.Width))));
        rd.Add(new PdfReal(Math.Max(0, caretRect.Y - annotRect.Y)));
        dict.Set("RD", rd);
        AddAnnotation(page, dict);
    }

    public void CreateMarkup(System.Drawing.Rectangle rect, string contents, int type, int page, System.Drawing.Color clr)
    {
        var subtype = type switch
        {
            0 => "Highlight",
            1 => "Underline",
            2 => "StrikeOut",
            3 => "Squiggly",
            _ => "Highlight",
        };
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(clr));
        // QuadPoints — single quad covering Rect
        var qp = new PdfArray();
        qp.Add(new PdfReal(rect.X));                  qp.Add(new PdfReal(rect.Y + rect.Height));
        qp.Add(new PdfReal(rect.X + rect.Width));     qp.Add(new PdfReal(rect.Y + rect.Height));
        qp.Add(new PdfReal(rect.X));                  qp.Add(new PdfReal(rect.Y));
        qp.Add(new PdfReal(rect.X + rect.Width));     qp.Add(new PdfReal(rect.Y));
        dict.Set("QuadPoints", qp);
        AddAnnotation(page, dict);
    }

    public void CreateSquareCircle(System.Drawing.Rectangle rect, string contents, System.Drawing.Color clr, bool square, int page, int borderWidth)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(square ? "Square" : "Circle"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(clr));
        var bs = new PdfDictionary();
        bs.Set("W", new PdfInteger(borderWidth));
        bs.Set("S", new PdfName("S"));
        dict.Set("BS", bs);
        AddAnnotation(page, dict);
    }

    public void CreateLine(System.Drawing.Rectangle rect, string contents, float x1, float y1, float x2, float y2,
        int page, int border, System.Drawing.Color clr, string borderStyle, int[] dashArray, string[] LEArray)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Line"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(clr));
        var line = new PdfArray();
        line.Add(new PdfReal(x1)); line.Add(new PdfReal(y1));
        line.Add(new PdfReal(x2)); line.Add(new PdfReal(y2));
        dict.Set("L", line);
        var bs = new PdfDictionary();
        bs.Set("W", new PdfInteger(border));
        if (!string.IsNullOrEmpty(borderStyle)) bs.Set("S", new PdfName(borderStyle));
        if (dashArray is { Length: > 0 })
        {
            var da = new PdfArray();
            foreach (var d in dashArray) da.Add(new PdfInteger(d));
            bs.Set("D", da);
        }
        dict.Set("BS", bs);
        if (LEArray is { Length: >= 2 })
        {
            var le = new PdfArray();
            le.Add(new PdfName(LEArray[0])); le.Add(new PdfName(LEArray[1]));
            dict.Set("LE", le);
        }
        AddAnnotation(page, dict);
    }

    public void CreatePolygon(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents)
        => CreatePolyShape(lineInfo, page, annotRect, annotContents, "Polygon");

    public void CreatePolyLine(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents)
        => CreatePolyShape(lineInfo, page, annotRect, annotContents, "PolyLine");

    private void CreatePolyShape(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents, string subtype)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));
        dict.Set("Contents", Latin1(annotContents ?? ""));
        var verts = lineInfo?.VerticeCoordinate;
        if (verts is { Length: > 0 })
        {
            var v = new PdfArray();
            foreach (var coord in verts) v.Add(new PdfReal(coord));
            dict.Set("Vertices", v);
        }
        double r = (lineInfo?.LineColorR ?? 0) / 255.0;
        double g = (lineInfo?.LineColorG ?? 0) / 255.0;
        double b = (lineInfo?.LineColorB ?? 0) / 255.0;
        double width = lineInfo?.LineWidth ?? 1;
        if (lineInfo is not null)
        {
            var c = new PdfArray();
            c.Add(new PdfReal(r)); c.Add(new PdfReal(g)); c.Add(new PdfReal(b));
            dict.Set("C", c);
            var bs = new PdfDictionary();
            bs.Set("W", new PdfReal(lineInfo.LineWidth));
            dict.Set("BS", bs);
        }

        // The /Vertices alone don't render — viewers (and the FOSS renderer) draw the
        // shape from its /AP /N appearance. Synthesise one that strokes the polyline /
        // polygon in page space, and set /Rect to the vertices' bounding box so the
        // appearance maps 1:1 onto the page (otherwise the line does not show).
        if (verts is { Length: >= 4 })
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i + 1 < verts.Length; i += 2)
            {
                minX = System.Math.Min(minX, verts[i]); maxX = System.Math.Max(maxX, verts[i]);
                minY = System.Math.Min(minY, verts[i + 1]); maxY = System.Math.Max(maxY, verts[i + 1]);
            }
            // The polygon/polyline /Rect is padded beyond the vertex
            // bounding box by (LineWidth + 3) on every side (the
            // CreatePolygon padding: width 1/3/5 → pad 4/6/8), leaving room for the
            // stroke and end caps so the appearance is never clipped.
            double pad = width + 3.0;
            minX -= pad; minY -= pad; maxX += pad; maxY += pad;
            dict.Set("Rect", RectToPdfArray(new Rectangle(minX, minY, maxX, maxY)));

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(r.ToString(ci)).Append(' ').Append(g.ToString(ci)).Append(' ').Append(b.ToString(ci)).Append(" RG\n");
            sb.Append(width.ToString(ci)).Append(" w\n");
            sb.Append(verts[0].ToString(ci)).Append(' ').Append(verts[1].ToString(ci)).Append(" m\n");
            for (int i = 2; i + 1 < verts.Length; i += 2)
                sb.Append(verts[i].ToString(ci)).Append(' ').Append(verts[i + 1].ToString(ci)).Append(" l\n");
            if (subtype == "Polygon") sb.Append("h\n");
            sb.Append("S\n");
            var content = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

            var form = new PdfDictionary();
            form.Set("Type", new PdfName("XObject"));
            form.Set("Subtype", new PdfName("Form"));
            form.Set("FormType", new PdfInteger(1));
            var bb = new PdfArray();
            bb.Add(new PdfReal(minX)); bb.Add(new PdfReal(minY));
            bb.Add(new PdfReal(maxX)); bb.Add(new PdfReal(maxY));
            form.Set("BBox", bb);
            form.Set("Length", new PdfInteger(content.Length));
            var ap = new PdfDictionary();
            ap.Set("N", new PdfStream(form, content));
            dict.Set("AP", ap);
        }
        else
        {
            dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(annotRect)));
        }
        AddAnnotation(page, dict);
    }

    public void CreatePopup(System.Drawing.Rectangle rect, string contents, bool open, int page)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Popup"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("Open", open ? PdfBoolean.True : PdfBoolean.False);
        AddAnnotation(page, dict);
    }

    public void CreateRubberStamp(int page, System.Drawing.Rectangle annotRect, string annotContents, System.Drawing.Color color, string appearanceFile)
    {
        var dict = BuildRubberStamp(annotRect, annotContents, color, icon: null);
        // appearanceFile is read but the bytes aren't synthesised into a /N appearance stream
        // here — appearance streams require XObject form rendering which is not implemented here.
        AddAnnotation(page, dict);
    }

    public void CreateRubberStamp(int page, System.Drawing.Rectangle annotRect, string annotContents, System.Drawing.Color color, Stream appearanceStream)
    {
        var dict = BuildRubberStamp(annotRect, annotContents, color, icon: null);
        AddAnnotation(page, dict);
    }

    public void CreateRubberStamp(int page, System.Drawing.Rectangle annotRect, string icon, string annotContents, System.Drawing.Color color)
    {
        var dict = BuildRubberStamp(annotRect, annotContents, color, icon);
        AddAnnotation(page, dict);
    }

    private static PdfDictionary BuildRubberStamp(System.Drawing.Rectangle annotRect, string annotContents, System.Drawing.Color color, string? icon)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Stamp"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(annotRect)));
        dict.Set("Contents", Latin1(annotContents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(color));
        dict.Set("CreationDate", Latin1("D:" + System.DateTime.Now.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z"));
        if (!string.IsNullOrEmpty(icon))
            dict.Set("Name", new PdfName(icon));
        return dict;
    }

    public void CreateMovie(System.Drawing.Rectangle rect, string filePath, int page)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Movie"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        var movie = new PdfDictionary();
        movie.Set("F", Latin1(filePath ?? ""));
        dict.Set("Movie", movie);
        AddAnnotation(page, dict);
    }

    public void CreateSound(System.Drawing.Rectangle rect, string filePath, string name, int page, string rate)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Sound"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        if (!string.IsNullOrEmpty(name))
            dict.Set("Name", new PdfName(name));
        var sound = new PdfDictionary();
        sound.Set("F", Latin1(filePath ?? ""));
        sound.Set("Type", new PdfName("Sound"));
        if (!string.IsNullOrEmpty(rate) && double.TryParse(rate, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var r))
            sound.Set("R", new PdfReal(r));
        dict.Set("Sound", sound);
        AddAnnotation(page, dict);
    }

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, string filePath, int page, string name)
        => CreateFileAttachment(rect, contents, filePath, page, name, 1.0);

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, string filePath, int page, string name, double opacity)
    {
        var bytes = File.ReadAllBytes(filePath);
        var attachmentName = string.IsNullOrEmpty(name) ? Path.GetFileName(filePath) : name;
        AddFileAttachmentAnnotation(rect, contents, bytes, attachmentName, page, name, opacity);
    }

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, Stream attachmentStream, string attachmentName, int page, string name)
        => CreateFileAttachment(rect, contents, attachmentStream, attachmentName, page, name, 1.0);

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, Stream attachmentStream, string attachmentName, int page, string name, double opacity)
    {
        using var ms = new MemoryStream();
        attachmentStream.CopyTo(ms);
        AddFileAttachmentAnnotation(rect, contents, ms.ToArray(), attachmentName, page, name, opacity);
    }

    private void AddFileAttachmentAnnotation(System.Drawing.Rectangle rect, string contents, byte[] data, string attachmentName, int page, string iconName, double opacity)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("FileAttachment"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        if (!string.IsNullOrEmpty(iconName))
            dict.Set("Name", new PdfName(iconName));
        if (opacity is >= 0 and < 1.0)
            dict.Set("CA", new PdfReal(opacity));
        dict.Set("FS", BuildFileSpec(attachmentName, data));
        AddAnnotation(page, dict);
    }

    private static PdfDictionary BuildFileSpec(string name, byte[] data)
    {
        var fs = new PdfDictionary();
        fs.Set("Type", new PdfName("Filespec"));
        fs.Set("F", Latin1(name));
        var ef = new PdfDictionary();
        var streamDict = new PdfDictionary();
        streamDict.Set("Type", new PdfName("EmbeddedFile"));
        streamDict.Set("Length", new PdfInteger(data.Length));
        var ms = new PdfStream(streamDict, data);
        ef.Set("F", ms);
        fs.Set("EF", ef);
        return fs;
    }

    public void CreateBookmarksAction(string title, System.Drawing.Color color, bool boldFlag, bool italicFlag,
        string file, string actionType, string destination)
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        var outlinesObj = doc.Reader.Resolve(catalog.Get("Outlines"));
        var outlines = outlinesObj as PdfDictionary ?? new PdfDictionary();
        if (outlinesObj is null)
        {
            outlines.Set("Type", new PdfName("Outlines"));
            outlines.Set("Count", new PdfInteger(0));
            catalog.Set("Outlines", outlines);
        }
        var item = new PdfDictionary();
        item.Set("Title", Latin1(title ?? ""));
        item.Set("C", DrawingColorToPdfArray(color));
        var flags = (boldFlag ? 2 : 0) | (italicFlag ? 1 : 0);
        if (flags != 0) item.Set("F", new PdfInteger(flags));
        if (!string.IsNullOrEmpty(file))
        {
            var act = new PdfDictionary();
            act.Set("S", new PdfName(string.IsNullOrEmpty(actionType) ? "Launch" : actionType));
            act.Set("F", Latin1(file));
            if (!string.IsNullOrEmpty(destination)) act.Set("D", Latin1(destination));
            item.Set("A", act);
        }
        // Append to outlines: simple flat list at the root.
        var first = outlines.Get("First");
        if (first is null)
        {
            outlines.Set("First", item);
            outlines.Set("Last", item);
        }
        else
        {
            var lastObj = doc.Reader.Resolve(outlines.Get("Last"));
            if (lastObj is PdfDictionary last)
            {
                last.Set("Next", item);
                item.Set("Prev", last);
                outlines.Set("Last", item);
            }
        }
        outlines.Set("Count",
            new PdfInteger((outlines.Get("Count") is PdfInteger ic ? (int)ic.Value : 0) + 1));
    }

    public void AddDocumentAdditionalAction(string eventType, string code)
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        if (eventType == DocumentOpen)
        {
            // OpenAction lives at the catalog root, not under /AA.
            var act = new PdfDictionary();
            act.Set("S", new PdfName("JavaScript"));
            act.Set("JS", Latin1(code ?? ""));
            catalog.Set("OpenAction", act);
            return;
        }
        var aaObj = doc.Reader.Resolve(catalog.Get("AA"));
        var aa = aaObj as PdfDictionary;
        if (aa is null) { aa = new PdfDictionary(); catalog.Set("AA", aa); }
        var entry = new PdfDictionary();
        entry.Set("S", new PdfName("JavaScript"));
        entry.Set("JS", Latin1(code ?? ""));
        aa.Set(eventType, entry);
    }

    public void RemoveDocumentOpenAction()
    {
        var doc = EnsureBound();
        doc.Reader.Catalog.Remove("OpenAction");
    }

    public IList<Annotation> ExtractLink()
    {
        var result = new List<Annotation>();
        if (_document is null) return result;
        foreach (var page in _document.Pages)
        {
            var annots = _document.Reader.Resolve(page.Dict.Get("Annots"));
            if (annots is not PdfArray arr) continue;
            foreach (var item in arr)
            {
                var d = _document.Reader.ResolveDict(item);
                if (d?.GetName("Subtype") != "Link") continue;
                result.Add(new LinkAnnotation(d, _document.Reader));
            }
        }
        return result;
    }

    private static PdfDictionary BuildLaunchAnnotation(Rectangle rect, string application)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(rect));
        var act = new PdfDictionary();
        act.Set("S", new PdfName("Launch"));
        act.Set("F", Latin1(application ?? ""));
        dict.Set("A", act);
        return dict;
    }

    private static PdfDictionary BuildGoToRAnnotation(Rectangle rect, string remotePdf, int destinationPage)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(rect));
        var act = new PdfDictionary();
        act.Set("S", new PdfName("GoToR"));
        act.Set("F", Latin1(remotePdf ?? ""));
        var dest = new PdfArray();
        dest.Add(new PdfInteger(destinationPage - 1));
        dest.Add(new PdfName("Fit"));
        act.Set("D", dest);
        dict.Set("A", act);
        return dict;
    }
}
