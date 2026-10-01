using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>
    /// Imports a single annotation from an XFDF XML node into the document.
    /// Maps XFDF element/attribute names to PDF annotation dictionary entries
    /// per the XFDF specification (PDF 32000 §12.7.8). Each section handles
    /// one XFDF construct: rect, flags, color, contents, ink gestures, popup, etc.
    /// </summary>
    private static void ImportXfdfAnnotation(Document doc, XmlNode node)
    {
        var pageAttr = node.Attributes?["page"];
        int pageIdx = 0;
        if (pageAttr is not null)
            int.TryParse(pageAttr.Value, out pageIdx);

        // XFDF page is 0-based
        int pageNum = pageIdx + 1;
        if (pageNum < 1 || pageNum > doc.PageCount) return;

        var page = doc.Pages.At(pageNum);
        var subtype = XfdfTagToSubtype(node.LocalName.ToLowerInvariant());
        if (subtype is null) return;

        // Parse rect
        var dict = BuildXfdfAnnotationDict(node, page, subtype);

        // Standard attributes
        ReadXfdfCommonAttributes(dict, node);
        // to the rotated bounding box.
        ReadXfdfCalloutAndOverlay(dict, node);

        // Contents (child element or attribute)
        ReadXfdfTextAndStyle(dict, node);

        // Icon (for text annotations)
        ReadXfdfIconAndBorder(dict, node);

        // Fringe (/RD rectangle differences — square/circle/caret)
        ReadXfdfLineDecoration(dict, node);

        // Measure element -> /Measure dictionary (the inverse of the export's
        // <measure> child: rateValue = /R, each area/distance/xformat/yformat/
        // angle/slope child contributes one NumberFormat entry to its list).
        ReadXfdfMeasure(dict, node);

        // File-attachment embedded file (/FS): rebuild the file spec + embedded
        // stream from the file metadata attributes and the base64 <data> child.
        ReadXfdfEmbeddedContent(dict, node, subtype);

        // QuadPoints / coords
        ReadXfdfCoordsAndVertices(dict, node);

        // Intent (/IT). Adobe XFDF names the FreeText intent attribute "IT"
        // (uppercase); accept that spelling alongside "intent"/"it".
        ReadXfdfIntentAndState(dict, node);

        // Ink gesture data, drawn into the ink's own appearance (colour, border width and
        // opacity from the same node) so an imported signature shows without a viewer
        // synthesising it - the reference's import renders every ink stroke.
        ReadXfdfInkList(dict, node);
        if (subtype == "Ink")
        {
            // The appearance builder re-derives /Rect from the strokes; the XFDF's own
            // rect is the imported one and stays.
            var importedRect = dict.Get("Rect");
            new InkAnnotation(dict, page.Reader).UpdateAppearances();
            if (importedRect is not null) dict.Set("Rect", importedRect);
        }

        // Stamp image appearance: an XFDF <imagedata> child carries the rubber
        // stamp's actual picture as a base64 data: URI (e.g. a scanned/scripted
        // "Guest" signature stamp). Decode it and build the /AP /N image
        // appearance so the stamp renders its real image instead of the fallback
        // icon banner (e.g. the "Draft" box synthesised from /Name).
        ReadXfdfStampAppearance(doc, dict, node, subtype);

        // Append the main annotation first so it precedes its popup in /Annots
        // (round-trip consumers index the markup annotation at position 1).
        AppendAnnotationDict(page, dict);

        // Popup child
        ImportXfdfChildAnnotations(doc, node, page, dict);
    }

    private static void AppendAnnotationDict(Page page, PdfDictionary annotDict)
    {
        // Resolve /Annots — on many documents it is an INDIRECT reference to the
        // array, not an inline array. Without resolving, the existing annotations
        // (e.g. a page's own markup) would be mistaken for "none" and overwritten.
        // Rebuild as a direct array (existing items + the new one) so the page dict
        // is marked dirty and the full list — originals included — is written on save.
        var existing = page.Reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        var annotArray = new PdfArray();
        if (existing is not null)
            foreach (var item in existing) annotArray.Add(item);
        annotArray.Add(annotDict);
        page.Dict.Set("Annots", annotArray);
    }

    private static void AppendContentStream(Page page, byte[] streamData)
    {
        // Append a new content stream to the page
        var newStream = new PdfStream(new PdfDictionary(), streamData);
        newStream.Dict.Set("Length", new PdfInteger(streamData.Length));

        var contentsObj = page.Dict.Get("Contents");
        if (contentsObj is PdfArray contentsArr)
        {
            contentsArr.Add(newStream);
        }
        else if (contentsObj is PdfStream || contentsObj is PdfIndirectRef)
        {
            var arr = new PdfArray();
            arr.Add(contentsObj);
            arr.Add(newStream);
            page.Dict.Set("Contents", arr);
        }
        else
        {
            var arr = new PdfArray();
            arr.Add(newStream);
            page.Dict.Set("Contents", arr);
        }
    }

    /// <summary>
    /// Creates a PdfString with proper encoding: Latin1 for ASCII-only text,
    /// UTF-16BE with BOM for text containing non-Latin1 characters.
    /// </summary>
    private static PdfString MakePdfTextString(string text)
    {
        // Check if all characters fit in Latin1 (0-255)
        bool needsUnicode = false;
        foreach (char c in text)
        {
            if (c > 255) { needsUnicode = true; break; }
        }

        if (needsUnicode)
        {
            // PDF spec: UTF-16BE with BOM \xFE\xFF
            var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
            var withBom = new byte[utf16.Length + 2];
            withBom[0] = 0xFE;
            withBom[1] = 0xFF;
            Array.Copy(utf16, 0, withBom, 2, utf16.Length);
            return new PdfString(withBom);
        }

        return new PdfString(Compat.Latin1.GetBytes(text));
    }

    private static void SetIfPresent(PdfDictionary dict, XmlNode node, string xmlAttr, string pdfKey)
    {
        // Check attribute first
        var attr = node.Attributes?[xmlAttr];
        if (attr is not null)
        {
            dict.Set(pdfKey, MakePdfTextString(attr.Value));
            return;
        }
        // Fallback: check child element (XFDF allows both forms)
        foreach (XmlNode child in node.ChildNodes)
        {
            if (child.NodeType == XmlNodeType.Element &&
                child.LocalName.Equals(xmlAttr, StringComparison.OrdinalIgnoreCase))
            {
                dict.Set(pdfKey, MakePdfTextString(child.InnerText));
                return;
            }
        }
    }

    private static int ParseFlags(string flagsStr)
    {
        int flags = 0;
        foreach (var f in flagsStr.Split(','))
        {
            switch (f.Trim().ToLowerInvariant())
            {
                case "invisible": flags |= 1; break;
                case "hidden": flags |= 2; break;
                case "print": flags |= 4; break;
                case "nozoom": flags |= 8; break;
                case "norotate": flags |= 16; break;
                case "noview": flags |= 32; break;
                case "readonly": flags |= 64; break;
                case "locked": flags |= 128; break;
                case "togglenoview": flags |= 256; break;
                case "lockedcontents": flags |= 512; break;
            }
        }
        return flags;
    }

    private static double[]? ParseHexColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return null;
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return [r / 255.0, g / 255.0, b / 255.0];
    }

    /// <summary>Decode an XFDF <c>&lt;imagedata&gt;</c> payload — a base64 string
    /// optionally prefixed by a <c>data:image/...;base64,</c> URI header (and possibly
    /// wrapped in whitespace) — into raw image bytes. Returns null on empty/invalid input.</summary>
    private static byte[]? DecodeDataUriBase64(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        var comma = s.IndexOf(',');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
            s = s[(comma + 1)..];
        // Strip any interior whitespace the XML pretty-printer may have inserted.
        s = new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        try { return Convert.FromBase64String(s); }
        catch { return null; }
    }

    private static void SetRealAttr(PdfDictionary dict, XmlNode node, string attr, string key)
    {
        var a = node.Attributes?[attr];
        if (a is not null && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            dict.Set(key, new PdfReal(v));
    }

    private static double[] ParseDoubleList(string csv)
    {
        var parts = csv.Split(',');
        var result = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                result.Add(v);
        }
        return result.ToArray();
    }

    private static string? XfdfTagToSubtype(string tag) => tag switch
    {
        "text" => "Text",
        "link" => "Link",
        "freetext" => "FreeText",
        "line" => "Line",
        "square" => "Square",
        "circle" => "Circle",
        "polygon" => "Polygon",
        "polyline" => "PolyLine",
        "highlight" => "Highlight",
        "underline" => "Underline",
        "squiggly" => "Squiggly",
        "strikeout" => "StrikeOut",
        "stamp" => "Stamp",
        "caret" => "Caret",
        "ink" => "Ink",
        "popup" => "Popup",
        "fileattachment" => "FileAttachment",
        "sound" => "Sound",
        "movie" => "Movie",
        "widget" => "Widget",
        "screen" => "Screen",
        "printermark" => "PrinterMark",
        "trapnet" => "TrapNet",
        "watermark" => "Watermark",
        "3d" => "3D",
        "redact" => "Redact",
        "richmedia" => "RichMedia",
        _ => null,
    };

    private static double GetDouble(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static byte[] FromHex(string hex)
    {
        hex = hex.Trim();
        if (hex.Length < 2) return Array.Empty<byte>();
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        return bytes;
    }
}
