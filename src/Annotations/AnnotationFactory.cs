using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Internal factory methods for creating annotation dictionaries.
/// </summary>
internal static class AnnotationFactory
{
    public static PdfDictionary CreateTextAnnotation(Rectangle rect, string contents,
        string? title, bool open)
    {
        var dict = BuildBase("Text", rect, contents);
        if (title is not null)
            dict.Set("T", new PdfString(Encode(title)));
        if (open)
            dict.Set("Open", PdfBoolean.True);
        return dict;
    }

    public static PdfDictionary CreateFreeTextAnnotation(Rectangle rect, string contents,
        string? fontName, double fontSize, double[]? color)
    {
        var dict = BuildBase("FreeText", rect, contents);
        var da = $"/{fontName ?? "Helv"} {F(fontSize)} Tf";
        if (color is { Length: 3 })
            da += $" {F(color[0])} {F(color[1])} {F(color[2])} rg";
        else
            da += " 0 0 0 rg";
        dict.Set("DA", new PdfString(Encoding.Latin1.GetBytes(da)));
        return dict;
    }

    public static PdfDictionary CreateLinkAnnotation(Rectangle rect, string uri)
    {
        var dict = BuildBase("Link", rect);
        var action = new PdfDictionary();
        action.Set("S", new PdfName("URI"));
        action.Set("URI", new PdfString(Encode(uri)));
        dict.Set("A", action);
        var border = new PdfArray();
        border.Add(new PdfInteger(0));
        border.Add(new PdfInteger(0));
        border.Add(new PdfInteger(0));
        dict.Set("Border", border);
        return dict;
    }

    public static PdfDictionary CreateLinkAnnotationWithAction(Rectangle rect, PdfAction action)
    {
        var dict = BuildBase("Link", rect);
        dict.Set("A", action.Dict);
        var border = new PdfArray();
        border.Add(new PdfInteger(0));
        border.Add(new PdfInteger(0));
        border.Add(new PdfInteger(0));
        dict.Set("Border", border);
        return dict;
    }

    public static PdfDictionary CreateHighlightAnnotation(Rectangle rect,
        double[]? quadPoints, double[]? color)
    {
        var dict = BuildBase("Highlight", rect);
        SetColor(dict, color ?? [1, 1, 0]);
        SetDefaultQuadPoints(dict, rect, quadPoints);
        return dict;
    }

    public static PdfDictionary CreateUnderlineAnnotation(Rectangle rect,
        double[]? quadPoints, double[]? color)
    {
        var dict = BuildBase("Underline", rect);
        SetColor(dict, color ?? [0, 1, 0]);
        SetDefaultQuadPoints(dict, rect, quadPoints);
        return dict;
    }

    public static PdfDictionary CreateStrikeOutAnnotation(Rectangle rect,
        double[]? quadPoints, double[]? color)
    {
        var dict = BuildBase("StrikeOut", rect);
        SetColor(dict, color ?? [1, 0, 0]);
        SetDefaultQuadPoints(dict, rect, quadPoints);
        return dict;
    }

    public static PdfDictionary CreateSquareAnnotation(Rectangle rect,
        double[]? borderColor, double[]? fillColor, double lineWidth)
    {
        var dict = BuildBase("Square", rect);
        SetColor(dict, borderColor ?? [0, 0, 0]);
        if (fillColor is { Length: 3 })
        {
            var ic = new PdfArray();
            foreach (var v in fillColor) ic.Add(new PdfReal(v));
            dict.Set("IC", ic);
        }
        SetBorderStyle(dict, lineWidth);
        return dict;
    }

    public static PdfDictionary CreateCircleAnnotation(Rectangle rect,
        double[]? borderColor, double[]? fillColor, double lineWidth)
    {
        var dict = BuildBase("Circle", rect);
        SetColor(dict, borderColor ?? [0, 0, 0]);
        if (fillColor is { Length: 3 })
        {
            var ic = new PdfArray();
            foreach (var v in fillColor) ic.Add(new PdfReal(v));
            dict.Set("IC", ic);
        }
        SetBorderStyle(dict, lineWidth);
        return dict;
    }

    public static PdfDictionary CreateLineAnnotation(Rectangle rect,
        double x1, double y1, double x2, double y2,
        double[]? color, double lineWidth)
    {
        var dict = BuildBase("Line", rect);
        SetColor(dict, color ?? [0, 0, 0]);
        var l = new PdfArray();
        l.Add(new PdfReal(x1)); l.Add(new PdfReal(y1));
        l.Add(new PdfReal(x2)); l.Add(new PdfReal(y2));
        dict.Set("L", l);
        SetBorderStyle(dict, lineWidth);
        return dict;
    }

    public static PdfDictionary CreateInkAnnotation(Rectangle rect,
        double[][] inkPaths, double[]? color, double lineWidth)
    {
        var dict = BuildBase("Ink", rect);
        SetColor(dict, color ?? [0, 0, 0]);
        SetBorderStyle(dict, lineWidth);

        var inkList = new PdfArray();
        foreach (var path in inkPaths)
        {
            var pathArr = new PdfArray();
            foreach (var v in path)
                pathArr.Add(new PdfReal(v));
            inkList.Add(pathArr);
        }
        dict.Set("InkList", inkList);

        return dict;
    }

    public static PdfDictionary CreateStampAnnotation(Rectangle rect,
        string contents, string stampName = "Draft")
    {
        var dict = BuildBase("Stamp", rect, contents);
        dict.Set("Name", new PdfName(stampName));
        return dict;
    }

    public static PdfDictionary CreateCaretAnnotation(Rectangle rect,
        string? contents = null)
    {
        var dict = BuildBase("Caret", rect, contents);
        return dict;
    }

    public static PdfDictionary CreateRedactAnnotation(Rectangle rect,
        double[]? color = null, string? overlayText = null)
    {
        var dict = BuildBase("Redact", rect);
        SetColor(dict, color ?? [0, 0, 0]);
        if (overlayText is not null)
            dict.Set("OverlayText", new PdfString(Encode(overlayText)));
        return dict;
    }

    public static PdfDictionary CreateFileAttachmentAnnotation(Rectangle rect,
        string contents, string fileName, byte[] fileData)
    {
        var dict = BuildBase("FileAttachment", rect, contents);

        // Embedded file stream
        var fileDict = new PdfDictionary();
        fileDict.Set("Type", new PdfName("EmbeddedFile"));
        var fileStream = new PdfStream(fileDict, fileData);

        // File specification
        var fsDict = new PdfDictionary();
        fsDict.Set("Type", new PdfName("Filespec"));
        fsDict.Set("F", new PdfString(Encode(fileName)));
        fsDict.Set("UF", new PdfString(Encode(fileName)));
        var efDict = new PdfDictionary();
        efDict.Set("F", fileStream);
        fsDict.Set("EF", efDict);

        dict.Set("FS", fsDict);
        return dict;
    }

    public static PdfDictionary CreateSquigglyAnnotation(Rectangle rect,
        double[]? quadPoints, double[]? color)
    {
        var dict = BuildBase("Squiggly", rect);
        SetColor(dict, color ?? [0, 0.5, 0]);
        SetDefaultQuadPoints(dict, rect, quadPoints);
        return dict;
    }

    public static PdfDictionary CreatePolygonAnnotation(Rectangle rect,
        double[] vertices, double[]? borderColor, double[]? fillColor, double lineWidth)
    {
        var dict = BuildBase("Polygon", rect);
        SetColor(dict, borderColor ?? [0, 0, 0]);
        if (fillColor is { Length: 3 })
        {
            var ic = new PdfArray();
            foreach (var v in fillColor) ic.Add(new PdfReal(v));
            dict.Set("IC", ic);
        }
        var verts = new PdfArray();
        foreach (var v in vertices) verts.Add(new PdfReal(v));
        dict.Set("Vertices", verts);
        SetBorderStyle(dict, lineWidth);
        return dict;
    }

    public static PdfDictionary CreatePolyLineAnnotation(Rectangle rect,
        double[] vertices, double[]? color, double lineWidth)
    {
        var dict = BuildBase("PolyLine", rect);
        SetColor(dict, color ?? [0, 0, 0]);
        var verts = new PdfArray();
        foreach (var v in vertices) verts.Add(new PdfReal(v));
        dict.Set("Vertices", verts);
        SetBorderStyle(dict, lineWidth);
        return dict;
    }

    public static PdfDictionary CreatePopupAnnotation(Rectangle rect, bool open = false)
    {
        var dict = BuildBase("Popup", rect);
        if (open)
            dict.Set("Open", PdfBoolean.True);
        return dict;
    }

    public static PdfDictionary CreateWatermarkAnnotation(Rectangle rect,
        string? contents = null)
    {
        var dict = BuildBase("Watermark", rect, contents);
        return dict;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PdfDictionary BuildBase(string subtype, Rectangle rect, string? contents = null)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));
        var rectArr = new PdfArray();
        rectArr.Add(new PdfReal(rect.LLX)); rectArr.Add(new PdfReal(rect.LLY));
        rectArr.Add(new PdfReal(rect.URX)); rectArr.Add(new PdfReal(rect.URY));
        dict.Set("Rect", rectArr);
        dict.Set("F", new PdfInteger(4)); // Print flag
        if (contents is not null)
            dict.Set("Contents", new PdfString(Encode(contents)));
        return dict;
    }

    private static void SetColor(PdfDictionary dict, double[] rgb)
    {
        if (rgb.Length < 3) return;
        var c = new PdfArray();
        c.Add(new PdfReal(rgb[0])); c.Add(new PdfReal(rgb[1])); c.Add(new PdfReal(rgb[2]));
        dict.Set("C", c);
    }

    private static void SetBorderStyle(PdfDictionary dict, double lineWidth)
    {
        var bs = new PdfDictionary();
        bs.Set("W", new PdfReal(lineWidth));
        bs.Set("S", new PdfName("S"));
        dict.Set("BS", bs);
    }

    private static void SetDefaultQuadPoints(PdfDictionary dict, Rectangle rect, double[]? quadPoints)
    {
        var qp = new PdfArray();
        if (quadPoints is { Length: >= 8 })
        {
            foreach (var v in quadPoints) qp.Add(new PdfReal(v));
        }
        else
        {
            qp.Add(new PdfReal(rect.LLX)); qp.Add(new PdfReal(rect.URY));
            qp.Add(new PdfReal(rect.URX)); qp.Add(new PdfReal(rect.URY));
            qp.Add(new PdfReal(rect.LLX)); qp.Add(new PdfReal(rect.LLY));
            qp.Add(new PdfReal(rect.URX)); qp.Add(new PdfReal(rect.LLY));
        }
        dict.Set("QuadPoints", qp);
    }

    /// <summary>
    /// Encode a PDF text string. For strings that can be represented in Latin1, use Latin1 encoding.
    /// For strings with characters outside Latin1 (e.g. Thai, Japanese, CJK), use UTF-16BE with BOM
    /// per PDF spec §7.9.2.2 (Text String type).
    /// </summary>
    private static byte[] Encode(string text)
    {
        // Check if all characters fit in Latin1 (code points 0-255)
        bool isLatin1 = true;
        foreach (char c in text)
        {
            if (c > 0xFF) { isLatin1 = false; break; }
        }
        if (isLatin1) return Encoding.Latin1.GetBytes(text);

        // Use UTF-16BE with BOM (PDF text string per spec §7.9.2.2)
        var utf16beBytes = Encoding.BigEndianUnicode.GetBytes(text);
        var result = new byte[2 + utf16beBytes.Length];
        result[0] = 0xFE;
        result[1] = 0xFF;
        utf16beBytes.CopyTo(result, 2);
        return result;
    }
    private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);
}
