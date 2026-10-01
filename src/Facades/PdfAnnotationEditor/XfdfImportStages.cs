using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>An annotation's child nodes are its popup and its replies - each imported against the annotation it answers.</summary>
    private static void ImportXfdfChildAnnotations(Document doc, XmlNode node, Page page, PdfDictionary dict)
    {
        foreach (XmlNode childNode in node.ChildNodes)
        {
            if (childNode.NodeType == XmlNodeType.Element && childNode.LocalName.ToLowerInvariant() == "popup")
            {
                var popupDict = new PdfDictionary();
                popupDict.Set("Type", new PdfName("Annot"));
                popupDict.Set("Subtype", new PdfName("Popup"));

                var popupRectAttr = childNode.Attributes?["rect"];
                if (popupRectAttr is not null)
                {
                    var parts = popupRectAttr.Value.Split(',');
                    if (parts.Length >= 4)
                    {
                        var pr = new PdfArray();
                        foreach (var p in parts)
                        {
                            if (double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                                pr.Add(new PdfReal(v));
                        }
                        popupDict.Set("Rect", pr);
                    }
                }

                var popupFlagsAttr = childNode.Attributes?["flags"];
                if (popupFlagsAttr is not null)
                    popupDict.Set("F", new PdfInteger(ParseFlags(popupFlagsAttr.Value)));

                // Open state: check attribute first, then child element
                var popupOpenAttr = childNode.Attributes?["open"];
                string? openVal = popupOpenAttr?.Value;
                if (openVal is null)
                {
                    foreach (XmlNode pc in childNode.ChildNodes)
                        if (pc.NodeType == XmlNodeType.Element && pc.LocalName == "open")
                            { openVal = pc.InnerText; break; }
                }
                if (openVal is not null)
                    popupDict.Set("Open", openVal == "yes" ? PdfBoolean.True : PdfBoolean.False);

                // Link popup to parent
                popupDict.Set("Parent", dict);
                dict.Set("Popup", popupDict);

                // Add popup to page annotations array too
                AppendAnnotationDict(page, popupDict);
            }
        }
    }

    /// <summary>A stamp's face: the image it carries, the appearance tree it exported, or the standard vector artwork baked at import.</summary>
    private static void ReadXfdfStampAppearance(Document doc, PdfDictionary dict, XmlNode node, string subtype)
    {
        if (subtype == "Stamp")
        {
            var imgNode = node.SelectSingleNode("imagedata")
                ?? node.SelectSingleNode("*[local-name()='imagedata']");
            var imgBytes = DecodeDataUriBase64(imgNode?.InnerText);
            if (imgBytes is { Length: > 0 })
            {
                var stamp = new Aspose.Pdf.Annotations.StampAnnotation(dict, doc.Reader);
                stamp.Image = new MemoryStream(imgBytes);
            }

            // An <appearance> child carries the exported /AP object tree —
            // rebuild it so the imported stamp renders its original face
            // instead of the synthesized icon banner.
            var apNode = node.SelectSingleNode("appearance")
                ?? node.SelectSingleNode("*[local-name()='appearance']");
            if (apNode?.InnerText is { Length: > 0 } apB64)
            {
                try
                {
                    var apXml = System.Text.Encoding.ASCII.GetString(
                        Convert.FromBase64String(apB64.Trim()));
                    if (XfdfAppearanceCodec.Deserialize(apXml) is { } apObj)
                        dict.Set("AP", apObj);
                }
                catch (FormatException) { /* not base64 — leave the default face */ }
            }

            // Neither an image nor an exported /AP: bake the standard vector face for
            // the icon (DRAFT / APPROVED artwork), rotated by the XFDF "rotation"
            // attribute, and refit /Rect to the rotated art's aspect — the
            // appearance is written into the file at import (see
            // Annotations.StampVectorFaces for the laws).
            if (imgBytes is not { Length: > 0 } && dict.Get("AP") is null)
                Aspose.Pdf.Annotations.StampVectorFaces.TryBuildAppearance(dict);
        }
    }

    /// <summary>An ink annotation's gesture: every stroke's point list, in page space.</summary>
    private static void ReadXfdfInkList(PdfDictionary dict, XmlNode node)
    {
        var inkListNode = node.SelectSingleNode("inklist") ?? node.SelectSingleNode("*[local-name()='inklist']");
        if (inkListNode is not null)
        {
            var inkList = new PdfArray();
            foreach (XmlNode gesture in inkListNode.ChildNodes)
            {
                if (gesture.NodeType != XmlNodeType.Element) continue;
                var points = gesture.InnerText.Split(';');
                var pathArr = new PdfArray();
                foreach (var pt in points)
                {
                    var coords = pt.Split(',');
                    if (coords.Length >= 2)
                    {
                        if (double.TryParse(coords[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
                            pathArr.Add(new PdfReal(x));
                        if (double.TryParse(coords[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                            pathArr.Add(new PdfReal(y));
                    }
                }
                inkList.Add(pathArr);
            }
            dict.Set("InkList", inkList);
        }
    }

    /// <summary>The annotation's intent, its reply chain, its review state and its opacity.</summary>
    private static void ReadXfdfIntentAndState(PdfDictionary dict, XmlNode node)
    {
        var intentAttr = node.Attributes?["intent"] ?? node.Attributes?["it"] ?? node.Attributes?["IT"];
        if (intentAttr is not null)
        {
            // Map the lowercase-hyphenated polygon/polyline XFDF intent back to the
            // PascalCase /IT name; other intents (line) pass through unchanged.
            var itName = intentAttr.Value switch
            {
                "polygon-cloud" => "PolygonCloud",
                "polygon-dimension" => "PolygonDimension",
                "polyline-dimension" => "PolyLineDimension",
                _ => intentAttr.Value,
            };
            dict.Set("IT", new PdfName(itName));
        }

        // InReplyTo
        var irtAttr = node.Attributes?["inreplyto"];
        if (irtAttr is not null)
            dict.Set("IRT_Name", new PdfString(Compat.Latin1.GetBytes(irtAttr.Value)));

        // State / StateModel — stored as PDF text strings (/State, /StateModel)
        var stateAttr = node.Attributes?["state"];
        if (stateAttr is not null)
            dict.Set("State", MakePdfTextString(stateAttr.Value));

        var stateModelAttr = node.Attributes?["statemodel"];
        if (stateModelAttr is not null)
            dict.Set("StateModel", MakePdfTextString(stateModelAttr.Value));

        // ReplyType (reply | group → /RT)
        var replyTypeAttr = node.Attributes?["replyType"];
        if (replyTypeAttr is not null)
        {
            string? rt = replyTypeAttr.Value.ToLowerInvariant() switch
            {
                "reply" => "R",
                "group" => "Group",
                _ => null
            };
            if (rt is not null) dict.Set("RT", new PdfName(rt));
        }

        // Opacity (/CA)
        var opacityAttr = node.Attributes?["opacity"];
        if (opacityAttr is not null && double.TryParse(opacityAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double op))
            dict.Set("CA", new PdfReal(op));
    }

    /// <summary>Markup geometry: the quad points a text markup covers and the vertices a polygon walks.</summary>
    private static void ReadXfdfCoordsAndVertices(PdfDictionary dict, XmlNode node)
    {
        var coordsAttr = node.Attributes?["coords"];
        if (coordsAttr is not null)
        {
            var qp = ParseDoubleList(coordsAttr.Value);
            if (qp.Length > 0)
            {
                var qpArr = new PdfArray();
                foreach (var v in qp) qpArr.Add(new PdfReal(v));
                dict.Set("QuadPoints", qpArr);
            }
        }

        // Vertices (polygon / polyline) — child element "x,y;x,y;..."
        var verticesNode = node.SelectSingleNode("vertices") ?? node.SelectSingleNode("*[local-name()='vertices']");
        if (verticesNode is not null)
        {
            var vArr = new PdfArray();
            foreach (var pair in verticesNode.InnerText.Split(';'))
            {
                var xy = pair.Split(',');
                if (xy.Length >= 2
                    && double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vx)
                    && double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var vy))
                {
                    vArr.Add(new PdfReal(vx));
                    vArr.Add(new PdfReal(vy));
                }
            }
            if (vArr.Count > 0) dict.Set("Vertices", vArr);
        }
    }

    /// <summary>A file attachment carries its embedded file, a stamp its embedded image.</summary>
    private static void ReadXfdfEmbeddedContent(PdfDictionary dict, XmlNode node, string subtype)
    {
        var dataNode = node.SelectSingleNode("data") ?? node.SelectSingleNode("*[local-name()='data']");
        var fileAttr = node.Attributes?["file"];
        if (subtype == "FileAttachment" && (fileAttr is not null || dataNode is not null))
        {
            byte[] fileBytes = Array.Empty<byte>();
            if (dataNode is not null)
            {
                var enc = dataNode.Attributes?["encoding"]?.Value;
                var text = dataNode.InnerText.Trim();
                try { fileBytes = enc == "base64" ? Convert.FromBase64String(text) : Encoding.UTF8.GetBytes(dataNode.InnerText); }
                catch (FormatException) { fileBytes = Encoding.UTF8.GetBytes(dataNode.InnerText); }
            }

            var name = fileAttr?.Value ?? string.Empty;
            var fsDict = new PdfDictionary();
            fsDict.Set("Type", new PdfName("Filespec"));
            fsDict.Set("F", MakePdfTextString(name));
            fsDict.Set("UF", MakePdfTextString(name));

            var efStreamDict = new PdfDictionary();
            efStreamDict.Set("Type", new PdfName("EmbeddedFile"));
            var mimeAttr = node.Attributes?["mimetype"];
            if (mimeAttr is not null) efStreamDict.Set("Subtype", new PdfName(mimeAttr.Value));

            var prmDict = new PdfDictionary();
            var sizeA = node.Attributes?["size"];
            if (sizeA is not null && int.TryParse(sizeA.Value, out var sz)) prmDict.Set("Size", new PdfInteger(sz));
            var csA = node.Attributes?["checksum"];
            if (csA is not null) prmDict.Set("CheckSum", new PdfString(FromHex(csA.Value)));
            var crA = node.Attributes?["creation"];
            if (crA is not null) prmDict.Set("CreationDate", new PdfString(Compat.Latin1.GetBytes(crA.Value)));
            var mdA = node.Attributes?["modification"];
            if (mdA is not null) prmDict.Set("ModDate", new PdfString(Compat.Latin1.GetBytes(mdA.Value)));
            efStreamDict.Set("Params", prmDict);

            efStreamDict.Set("Length", new PdfInteger(fileBytes.Length));
            var efStream = new PdfStream(efStreamDict, fileBytes);
            var efDict = new PdfDictionary();
            efDict.Set("F", efStream);
            fsDict.Set("EF", efDict);
            dict.Set("FS", fsDict);
        }

        // Sound annotation embedded audio (/Sound): rebuild the sound stream from
        // the sampling attributes and the base64 <data> child.
        if (subtype == "Sound" && (dataNode is not null || node.Attributes?["rate"] is not null))
        {
            byte[] soundBytes = Array.Empty<byte>();
            string? dataMode = null, dataFilter = null;
            if (dataNode is not null)
            {
                var enc = dataNode.Attributes?["encoding"]?.Value;
                dataMode = dataNode.Attributes?["mode"]?.Value;
                dataFilter = dataNode.Attributes?["filter"]?.Value;
                var text = dataNode.InnerText.Trim();
                try
                {
                    soundBytes = enc switch
                    {
                        "hex" => FromHex(text),
                        "base64" => Convert.FromBase64String(text),
                        _ => Encoding.ASCII.GetBytes(dataNode.InnerText),
                    };
                }
                catch (FormatException) { soundBytes = Encoding.UTF8.GetBytes(dataNode.InnerText); }
            }
            var soundDict = new PdfDictionary();
            soundDict.Set("Type", new PdfName("Sound"));
            var rateA = node.Attributes?["rate"];
            soundDict.Set("R", new PdfReal(rateA is not null && double.TryParse(rateA.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rr) ? rr : 11025));
            var bitsA = node.Attributes?["bits"];
            if (bitsA is not null && int.TryParse(bitsA.Value, out var bb)) soundDict.Set("B", new PdfInteger(bb));
            var chA = node.Attributes?["channels"];
            if (chA is not null && int.TryParse(chA.Value, out var ch)) soundDict.Set("C", new PdfInteger(ch));
            // /E (sound encoding) — element "encoding" attr, normalised to the PDF name.
            var encA = node.Attributes?["encoding"];
            if (encA is not null)
                soundDict.Set("E", new PdfName(encA.Value.ToLowerInvariant() switch
                {
                    "signed" => "Signed",
                    "mulaw" => "muLaw",
                    "alaw" => "ALaw",
                    _ => "Raw",
                }));
            // "raw" data mode keeps the bytes filter-encoded, so preserve the filter
            // and let reads decode them; "filtered" data is already decoded.
            if (dataMode == "raw" && dataFilter is not null)
                soundDict.Set("Filter", new PdfName(dataFilter));
            soundDict.Set("Length", new PdfInteger(soundBytes.Length));
            var soundStream = new PdfStream(soundDict, soundBytes);
            dict.Set("Sound", soundStream);
        }
    }

    /// <summary>A measured annotation's scale: the ratio and the three number formats it reports in.</summary>
    private static void ReadXfdfMeasure(PdfDictionary dict, XmlNode node)
    {
        var measureNode = node.SelectSingleNode("measure") ?? node.SelectSingleNode("*[local-name()='measure']");
        if (measureNode is not null)
        {
            var m = new PdfDictionary();
            m.Set("Type", new PdfName("Measure"));
            if (measureNode.Attributes?["rateValue"] is { } rv)
                m.Set("R", new PdfString(System.Text.Encoding.UTF8.GetBytes(rv.Value)));
            foreach (var (xfdfName, pdfKey) in new[]
                     { ("area", "A"), ("distance", "D"), ("xformat", "X"), ("yformat", "Y"), ("angle", "T"), ("slope", "S") })
            {
                PdfArray? arr = null;
                foreach (System.Xml.XmlNode child in measureNode.ChildNodes)
                {
                    if (!string.Equals(child.LocalName, xfdfName, StringComparison.OrdinalIgnoreCase)) continue;
                    var nf = new PdfDictionary();
                    nf.Set("Type", new PdfName("NumberFormat"));
                    if (child.Attributes?["u"] is { } u)
                        nf.Set("U", new PdfString(System.Text.Encoding.UTF8.GetBytes(u.Value)));
                    if (child.Attributes?["c"] is { } c
                        && double.TryParse(c.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var cv))
                        nf.Set("C", new PdfReal(cv));
                    if (child.Attributes?["f"] is { } fAttr) nf.Set("F", new PdfName(fAttr.Value));
                    if (child.Attributes?["d"] is { } d && int.TryParse(d.Value, out var dv2))
                        nf.Set("D", new PdfInteger(dv2));
                    foreach (var (attr, k) in new[] { ("rd", "RD"), ("rt", "RT"), ("ps", "PS"), ("ss", "SS") })
                        if (child.Attributes?[attr] is { } sv)
                            nf.Set(k, new PdfString(System.Text.Encoding.UTF8.GetBytes(sv.Value)));
                    arr ??= new PdfArray();
                    arr.Add(nf);
                }
                if (arr is not null) m.Set(pdfKey, arr);
            }
            dict.Set("Measure", m);
        }
    }

    /// <summary>The line's fringe, its symbol, its endings and the caption riding on it.</summary>
    private static void ReadXfdfLineDecoration(PdfDictionary dict, XmlNode node)
    {
        var fringeAttr = node.Attributes?["fringe"];
        if (fringeAttr is not null)
        {
            var rdv = ParseDoubleList(fringeAttr.Value);
            if (rdv.Length >= 4)
            {
                var rdArr = new PdfArray();
                foreach (var v in rdv) rdArr.Add(new PdfReal(v));
                dict.Set("RD", rdArr);
            }
        }

        // Symbol (/Sy — caret)
        var symbolAttr = node.Attributes?["symbol"];
        if (symbolAttr is not null && symbolAttr.Value.ToLowerInvariant() == "paragraph")
            dict.Set("Sy", new PdfName("P"));

        // Line geometry (/L, /LE, leader lines, caption — line annotations)
        var startAttr = node.Attributes?["start"];
        var endAttr = node.Attributes?["end"];
        if (startAttr is not null || endAttr is not null)
        {
            var s = startAttr is not null ? ParseDoubleList(startAttr.Value) : Array.Empty<double>();
            var e = endAttr is not null ? ParseDoubleList(endAttr.Value) : Array.Empty<double>();
            var lArr = new PdfArray();
            lArr.Add(new PdfReal(s.Length > 0 ? s[0] : 0));
            lArr.Add(new PdfReal(s.Length > 1 ? s[1] : 0));
            lArr.Add(new PdfReal(e.Length > 0 ? e[0] : 0));
            lArr.Add(new PdfReal(e.Length > 1 ? e[1] : 0));
            dict.Set("L", lArr);
        }
        var headAttr = node.Attributes?["head"];
        var tailAttr = node.Attributes?["tail"];
        if (headAttr is not null || tailAttr is not null)
        {
            var leArr = new PdfArray();
            leArr.Add(new PdfName(headAttr?.Value ?? "None"));
            leArr.Add(new PdfName(tailAttr?.Value ?? "None"));
            dict.Set("LE", leArr);
        }
        SetRealAttr(dict, node, "leaderLength", "LL");
        SetRealAttr(dict, node, "leaderExtend", "LLE");
        SetRealAttr(dict, node, "leaderOffset", "LLO");
        var captionAttr = node.Attributes?["caption"];
        if (captionAttr is not null)
            dict.Set("Cap", captionAttr.Value == "yes" ? PdfBoolean.True : PdfBoolean.False);
        var captionStyleAttr = node.Attributes?["caption-style"];
        if (captionStyleAttr is not null)
            dict.Set("CP", new PdfName(captionStyleAttr.Value));
        var coh = node.Attributes?["caption-offset-h"];
        var cov = node.Attributes?["caption-offset-v"];
        if (coh is not null || cov is not null)
        {
            var coArr = new PdfArray();
            coArr.Add(new PdfReal(coh is not null && double.TryParse(coh.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hh) ? hh : 0));
            coArr.Add(new PdfReal(cov is not null && double.TryParse(cov.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var vv) ? vv : 0));
            dict.Set("CO", coArr);
        }
    }

    /// <summary>The note icon, its open state and the border width, style and dash the annotation draws with.</summary>
    private static void ReadXfdfIconAndBorder(PdfDictionary dict, XmlNode node)
    {
        var iconAttr = node.Attributes?["icon"];
        if (iconAttr is not null)
            dict.Set("Name", new PdfName(iconAttr.Value));

        // Open (for text annotations)
        var openAttr = node.Attributes?["open"];
        if (openAttr is not null)
            dict.Set("Open", openAttr.Value == "yes" ? PdfBoolean.True : PdfBoolean.False);

        // Width / border style (/BS)
        var widthAttr = node.Attributes?["width"];
        var styleAttr = node.Attributes?["style"];
        var dashesAttr = node.Attributes?["dashes"];
        if (widthAttr is not null || styleAttr is not null || dashesAttr is not null)
        {
            var bs = new PdfDictionary();
            if (widthAttr is not null && double.TryParse(widthAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double bw))
                bs.Set("W", new PdfReal(bw));
            string styleName = styleAttr?.Value.ToLowerInvariant() switch
            {
                "dash" => "D",
                "bevel" => "B",
                "inset" => "I",
                "underline" => "U",
                _ => "S",
            };
            bs.Set("S", new PdfName(styleName));
            if (dashesAttr is not null)
            {
                var d = ParseDoubleList(dashesAttr.Value);
                if (d.Length > 0)
                {
                    var dArr = new PdfArray();
                    foreach (var v in d) dArr.Add(new PdfReal(v));
                    bs.Set("D", dArr);
                }
            }
            dict.Set("BS", bs);
        }
    }

    /// <summary>The annotation's contents, its rich text and the appearance, colour and justification they set.</summary>
    private static void ReadXfdfTextAndStyle(PdfDictionary dict, XmlNode node)
    {
        var contentsNode = node.SelectSingleNode("contents") ?? node.SelectSingleNode("*[local-name()='contents']");
        if (contentsNode is not null)
            dict.Set("Contents", MakePdfTextString(contentsNode.InnerText));

        // Rich text (contents-richtext → /RC entry). The /RC value carries an
        // XML declaration prefix that cannot live inside the XFDF element, so
        // it is reconstructed here to round-trip faithfully.
        var richTextNode = node.SelectSingleNode("contents-richtext") ?? node.SelectSingleNode("*[local-name()='contents-richtext']");
        if (richTextNode is not null)
            dict.Set("RC", MakePdfTextString("<?xml version=\"1.0\"?>" + richTextNode.InnerXml));

        // Default appearance
        var daNode = node.SelectSingleNode("defaultappearance") ?? node.SelectSingleNode("*[local-name()='defaultappearance']");
        var daText = daNode?.InnerText;
        // FreeText text colour is carried by the Adobe XFDF "TextColor" attribute,
        // which takes precedence over the colour in the default appearance string.
        // Fold it into the /DA fill colour so the generated appearance renders it.
        var textColorAttr = node.Attributes?["TextColor"] ?? node.Attributes?["textcolor"];
        if (textColorAttr is not null)
        {
            var rgb = ParseHexColor(textColorAttr.Value);
            if (rgb is not null)
            {
                var tf = System.Text.RegularExpressions.Regex.Match(daText ?? "", @"/\S+\s+[\d.]+\s+Tf");
                var tfPart = tf.Success ? tf.Value : "/Helvetica 12 Tf";
                daText = string.Format(CultureInfo.InvariantCulture, "{0:0.######} {1:0.######} {2:0.######} rg {3}",
                    rgb[0], rgb[1], rgb[2], tfPart);
            }
        }
        if (daText is not null)
            dict.Set("DA", new PdfString(Compat.Latin1.GetBytes(daText)));

        // Default style (/DS — free text)
        var dsNode = node.SelectSingleNode("defaultstyle") ?? node.SelectSingleNode("*[local-name()='defaultstyle']");
        if (dsNode is not null)
            dict.Set("DS", MakePdfTextString(dsNode.InnerText));

        // Justification (/Q — free text)
        var justAttr = node.Attributes?["justification"];
        if (justAttr is not null)
        {
            int q = justAttr.Value.ToLowerInvariant() switch { "centered" => 1, "center" => 1, "right" => 2, _ => 0 };
            dict.Set("Q", new PdfInteger(q));
        }
    }

    /// <summary>The rotation, the callout line, the interior colour and the overlay the annotation stamps with.</summary>
    private static void ReadXfdfCalloutAndOverlay(PdfDictionary dict, XmlNode node)
    {
        var rotationAttr = node.Attributes?["rotation"];
        if (rotationAttr is not null
            && double.TryParse(rotationAttr.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double rotDeg)
            && Math.Abs(rotDeg % 360.0) > 1e-6)
        {
            dict.Set("Rotate", new PdfReal(rotDeg));
        }

        // FreeText callout line (Adobe XFDF "callout" attribute → /CL array of
        // 4 or 6 numbers: [x1 y1 x2 y2] or [x1 y1 x2 y2 x3 y3]).
        var calloutAttr = node.Attributes?["callout"];
        if (calloutAttr is not null)
        {
            var cl = new PdfArray();
            foreach (var n in calloutAttr.Value.Split(','))
                if (double.TryParse(n.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var cv))
                    cl.Add(new PdfReal(cv));
            if (cl.Count >= 4) dict.Set("CL", cl);
        }

        // Interior color (for redact)
        var icAttr = node.Attributes?["interior-color"];
        if (icAttr is not null)
        {
            var rgb = ParseHexColor(icAttr.Value);
            if (rgb is not null)
            {
                var ic = new PdfArray();
                ic.Add(new PdfReal(rgb[0]));
                ic.Add(new PdfReal(rgb[1]));
                ic.Add(new PdfReal(rgb[2]));
                dict.Set("IC", ic);
            }
        }

        // Redaction overlay text (/OverlayText, /Repeat)
        var overlayAttr = node.Attributes?["overlay-text"];
        if (overlayAttr is not null)
            dict.Set("OverlayText", MakePdfTextString(overlayAttr.Value));
        var repeatAttr = node.Attributes?["repeat"];
        if (repeatAttr is not null)
            dict.Set("Repeat", repeatAttr.Value is "yes" or "true" ? PdfBoolean.True : PdfBoolean.False);
    }

    /// <summary>The attributes every annotation carries: its name, dates, title, subject, flags and colour.</summary>
    private static void ReadXfdfCommonAttributes(PdfDictionary dict, XmlNode node)
    {
        SetIfPresent(dict, node, "name", "NM");
        SetIfPresent(dict, node, "title", "T");
        SetIfPresent(dict, node, "subject", "Subj");
        SetIfPresent(dict, node, "date", "M");
        SetIfPresent(dict, node, "creationdate", "CreationDate");

        // Flags
        var flagsAttr = node.Attributes?["flags"];
        if (flagsAttr is not null)
        {
            int flags = ParseFlags(flagsAttr.Value);
            dict.Set("F", new PdfInteger(flags));
        }
        else
        {
            dict.Set("F", new PdfInteger(4)); // Print
        }

        // Color
        var colorAttr = node.Attributes?["color"];
        if (colorAttr is not null)
        {
            var rgb = ParseHexColor(colorAttr.Value);
            if (rgb is not null)
            {
                var c = new PdfArray();
                c.Add(new PdfReal(rgb[0]));
                c.Add(new PdfReal(rgb[1]));
                c.Add(new PdfReal(rgb[2]));
                dict.Set("C", c);
            }
        }

        // Arbitrary FreeText rotation angle (Adobe XFDF) → /Rotate in degrees.
        // GenerateAppearance bakes this into the appearance stream and expands /Rect
    }

    /// <summary>The annotation's own dictionary, seated on the rectangle the XFDF node declares.</summary>
    private static PdfDictionary BuildXfdfAnnotationDict(XmlNode node, Page page, string subtype)
    {
        PdfDictionary? dict = default;
        var rectAttr = node.Attributes?["rect"];
        Rectangle rect;
        if (rectAttr is not null)
        {
            var parts = rectAttr.Value.Split(',');
            if (parts.Length >= 4)
            {
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double llx);
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lly);
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double urx);
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double ury);
                rect = new Rectangle(llx, lly, urx, ury);
            }
            else
            {
                rect = new Rectangle(0, 0, 0, 0);
            }
        }
        else
        {
            rect = new Rectangle(0, 0, 0, 0);
        }

        // Build annotation dictionary
        dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));

        var rectArr = new PdfArray();
        rectArr.Add(new PdfReal(rect.LLX));
        rectArr.Add(new PdfReal(rect.LLY));
        rectArr.Add(new PdfReal(rect.URX));
        rectArr.Add(new PdfReal(rect.URY));
        dict.Set("Rect", rectArr);
        return dict;
    }
}
