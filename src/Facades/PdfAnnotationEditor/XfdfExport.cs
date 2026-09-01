using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfAnnotationEditor
{
    /// <summary>
    /// Export annotations to an XFDF stream.
    /// </summary>
    /// <param name="xfdfStream">Output stream.</param>
    /// <param name="startPage">Start page (1-based).</param>
    /// <param name="endPage">End page (1-based).</param>
    /// <param name="annotTypes">Annotation types to export.</param>
    public void ExportAnnotationsXfdf(Stream xmlOutputStream, int start, int end, AnnotationType[] annotTypes)
    {
        var doc = Document;
        var typeSet = new HashSet<AnnotationType>(annotTypes);

        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            // Entitize carriage returns (&#xD;) instead of the default Replace, which
            // rewrites a lone \r in text as \r\n. XML parsers leave character references
            // unchanged, so an annotation's /Contents survives the export/import round-trip
            // byte-for-byte (a bare \r stays a bare \r).
            NewLineHandling = NewLineHandling.Entitize,
        };

        using var writer = XmlWriter.Create(xmlOutputStream, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("xfdf", "http://ns.adobe.com/xfdf/");
        writer.WriteAttributeString("xml", "space", null, "preserve");

        writer.WriteStartElement("fields");
        writer.WriteEndElement(); // fields

        writer.WriteStartElement("annots");

        for (int pageIdx = Math.Max(1, start); pageIdx <= Math.Min(end, doc.PageCount); pageIdx++)
        {
            var page = doc.Pages.At(pageIdx);
            foreach (var annot in page.Annotations)
            {
                if (!typeSet.Contains(annot.AnnotationType)) continue;
                // Skip Popup annotations — they are written as children of their parent annotations
                if (annot.AnnotationType == AnnotationType.Popup) continue;
                WriteXfdfAnnotation(writer, annot, pageIdx - 1, doc.Reader); // XFDF uses 0-based pages
            }
        }

        writer.WriteEndElement(); // annots
        writer.WriteEndElement(); // xfdf
        writer.WriteEndDocument();
        writer.Flush();

        // Truncate any stale data if the stream was previously longer (e.g. FileMode.OpenOrCreate)
        if (xmlOutputStream.CanSeek && xmlOutputStream.CanWrite)
            xmlOutputStream.SetLength(xmlOutputStream.Position);
    }

    /// <summary>
    /// Writes a single annotation as an XFDF XML element.
    /// Maps PDF annotation dictionary entries back to XFDF attributes/elements
    /// per the XFDF specification (PDF 32000 §12.7.8). Inverse of ImportXfdfAnnotation.
    /// </summary>
    internal static void WriteXfdfAnnotation(XmlWriter writer, Annotation annot, int zeroBasedPage,
        IO.PdfReader? reader = null, bool writeContents = true, bool normalizeRichText = false)
    {
        var tag = AnnotationTypeToXfdfTag(annot.AnnotationType);
        if (tag == "unknown") return;

        writer.WriteStartElement(tag);

        // Page
        writer.WriteAttributeString("page", zeroBasedPage.ToString(CultureInfo.InvariantCulture));

        // Rect
        var r = annot.Rect;
        if (r is not null)
            writer.WriteAttributeString("rect",
                $"{F(r.LLX)},{F(r.LLY)},{F(r.URX)},{F(r.URY)}");

        // Color
        var colorArr = annot.Dict.Get("C");
        if (colorArr is PdfArray ca && ca.Count >= 3)
        {
            double cr = GetDouble(ca[0]), cg = GetDouble(ca[1]), cb = GetDouble(ca[2]);
            writer.WriteAttributeString("color",
                $"#{(int)Math.Round(cr * 255):X2}{(int)Math.Round(cg * 255):X2}{(int)Math.Round(cb * 255):X2}");
        }

        // Flags
        int flags = (int)annot.Flags;
        if (flags != 0)
            writer.WriteAttributeString("flags", FormatFlags(flags));

        // Title
        if (annot.Title is not null)
            writer.WriteAttributeString("title", annot.Title);

        // Subject
        var subj = annot.Dict.Get("Subj");
        string? subject = subj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null
        };
        if (subject is not null)
            writer.WriteAttributeString("subject", subject);

        // Date
        if (annot.ModifiedDate is not null)
            writer.WriteAttributeString("date", annot.ModifiedDate);

        // CreationDate — written as child element below (XFDF convention)

        // Icon (/Name — text, stamp, file-attachment and sound annotations)
        if (annot.AnnotationType is AnnotationType.Text or AnnotationType.Stamp
            or AnnotationType.FileAttachment or AnnotationType.Sound)
        {
            var iconName = annot.Dict.GetName("Name");
            if (iconName is not null)
                writer.WriteAttributeString("icon", iconName);
        }

        // Interior color (for redact)
        var icArr = annot.Dict.Get("IC");
        if (icArr is PdfArray ica && ica.Count >= 3)
        {
            double ir = GetDouble(ica[0]), ig = GetDouble(ica[1]), ib = GetDouble(ica[2]);
            writer.WriteAttributeString("interior-color",
                $"#{(int)Math.Round(ir * 255):X2}{(int)Math.Round(ig * 255):X2}{(int)Math.Round(ib * 255):X2}");
        }

        // Redaction overlay text (/OverlayText, /Repeat — redact annotations)
        if (annot.AnnotationType == AnnotationType.Redact)
        {
            var otObj = reader is not null ? reader.Resolve(annot.Dict.Get("OverlayText")) : annot.Dict.Get("OverlayText");
            if (otObj is PdfString otStr)
                writer.WriteAttributeString("overlay-text", otStr.ToText());
            if (annot.Dict.Get("Repeat") is PdfBoolean repB && repB.Value)
                writer.WriteAttributeString("repeat", "yes");
        }

        // Width / style / dashes (/BS — may be an indirect reference)
        bool styleWritten = false;
        var bsObj = annot.Dict.Get("BS");
        var bsd = bsObj as PdfDictionary ?? (reader is not null ? reader.ResolveDict(bsObj) : null);
        if (bsd is not null)
        {
            var wObj = bsd.Get("W");
            if (wObj is not null)
                writer.WriteAttributeString("width", F(GetDouble(wObj)));
            var styleXfdf = bsd.GetName("S") switch
            {
                "D" => "dash",
                "B" => "bevel",
                "I" => "inset",
                "U" => "underline",
                _ => null,
            };
            if (styleXfdf is not null)
            {
                writer.WriteAttributeString("style", styleXfdf);
                styleWritten = true;
            }
            var dObj = reader is not null ? reader.Resolve(bsd.Get("D")) : bsd.Get("D");
            if (dObj is PdfArray dArr && dArr.Count > 0)
            {
                var ds = new StringBuilder();
                for (int i = 0; i < dArr.Count; i++) { if (i > 0) ds.Append(','); ds.Append(F(GetDouble(dArr[i]))); }
                writer.WriteAttributeString("dashes", ds.ToString());
            }
        }

        // Border effect (/BE — cloudy borders on square/circle/freetext): style="cloudy" + intensity
        var beDict = annot.Dict.Get("BE") as PdfDictionary ?? (reader is not null ? reader.ResolveDict(annot.Dict.Get("BE")) : null);
        if (beDict is not null && beDict.GetName("S") == "C")
        {
            if (!styleWritten) writer.WriteAttributeString("style", "cloudy");
            var beI = beDict.Get("I");
            if (beI is PdfInteger || beI is PdfReal)
                writer.WriteAttributeString("intensity", F(GetDouble(beI!)));
        }

        // Fringe (/RD rectangle differences — square/circle/caret)
        var rdObj = annot.Dict.Get("RD");
        if (rdObj is PdfArray rdArr && rdArr.Count >= 4)
        {
            var fr = new StringBuilder();
            for (int i = 0; i < rdArr.Count; i++) { if (i > 0) fr.Append(','); fr.Append(F(GetDouble(rdArr[i]))); }
            writer.WriteAttributeString("fringe", fr.ToString());
        }

        // Symbol (/Sy — caret)
        if (annot.Dict.GetName("Sy") == "P")
            writer.WriteAttributeString("symbol", "paragraph");

        // FreeText callout line (/CL → "callout" attribute: comma-separated coords).
        var clObj = reader is not null ? reader.Resolve(annot.Dict.Get("CL")) : annot.Dict.Get("CL");
        if (clObj is PdfArray clArr && clArr.Count >= 4)
        {
            var co = new StringBuilder();
            for (int i = 0; i < clArr.Count; i++) { if (i > 0) co.Append(','); co.Append(F(GetDouble(clArr[i]))); }
            writer.WriteAttributeString("callout", co.ToString());
        }

        // Coords (QuadPoints)
        var qpObj = annot.Dict.Get("QuadPoints");
        if (qpObj is PdfArray qpa && qpa.Count > 0)
        {
            var coords = new StringBuilder();
            for (int i = 0; i < qpa.Count; i++)
            {
                if (i > 0) coords.Append(',');
                coords.Append(F(GetDouble(qpa[i])));
            }
            writer.WriteAttributeString("coords", coords.ToString());
        }

        // Intent (/IT). The polyline dimension intent uses the lowercase-hyphenated
        // XFDF form; all other intents (PolygonCloud, LineArrow, …) keep the raw name.
        var intentName = annot.Dict.GetName("IT");
        if (intentName is not null)
        {
            string intentXfdf = intentName == "PolyLineDimension" ? "polyline-dimension" : intentName;
            // Free-text annotations name the intent attribute "IT" (Adobe XFDF); others use "intent".
            writer.WriteAttributeString(annot.AnnotationType == AnnotationType.FreeText ? "IT" : "intent", intentXfdf);
        }

        // Justification (/Q — free text)
        if (annot.Dict.Get("Q") is PdfInteger qi)
        {
            string? just = qi.Value switch { 1 => "centered", 2 => "right", 0 => "left", _ => null };
            if (just is not null) writer.WriteAttributeString("justification", just);
        }

        // Line geometry (/L, /LE, leader lines, caption — line annotations)
        var lObj = reader is not null ? reader.Resolve(annot.Dict.Get("L")) : annot.Dict.Get("L");
        if (lObj is PdfArray lArr && lArr.Count >= 4)
        {
            writer.WriteAttributeString("start", $"{F(GetDouble(lArr[0]))},{F(GetDouble(lArr[1]))}");
            writer.WriteAttributeString("end", $"{F(GetDouble(lArr[2]))},{F(GetDouble(lArr[3]))}");
        }
        var leObj = reader is not null ? reader.Resolve(annot.Dict.Get("LE")) : annot.Dict.Get("LE");
        if (leObj is PdfArray leArr && leArr.Count >= 2)
        {
            if ((reader is not null ? reader.Resolve(leArr[0]) : leArr[0]) is PdfName headName)
                writer.WriteAttributeString("head", headName.Value);
            if ((reader is not null ? reader.Resolve(leArr[1]) : leArr[1]) is PdfName tailName)
                writer.WriteAttributeString("tail", tailName.Value);
        }
        else if (leObj is PdfName leName)
        {
            // Callout annotations (free text) carry a single /LE line-ending name.
            writer.WriteAttributeString("head", leName.Value);
            writer.WriteAttributeString("tail", "None");
        }
        var llObj = annot.Dict.Get("LL");
        if (llObj is PdfReal || llObj is PdfInteger)
            writer.WriteAttributeString("leaderLength", F(GetDouble(llObj!)));
        var lleObj = annot.Dict.Get("LLE");
        if (lleObj is PdfReal || lleObj is PdfInteger)
            writer.WriteAttributeString("leaderExtend", F(GetDouble(lleObj!)));
        var lloObj = annot.Dict.Get("LLO");
        if (lloObj is PdfReal || lloObj is PdfInteger)
            writer.WriteAttributeString("leaderOffset", F(GetDouble(lloObj!)));
        if (annot.Dict.Get("Cap") is PdfBoolean cap)
            writer.WriteAttributeString("caption", cap.Value ? "yes" : "no");
        var cpName = annot.Dict.GetName("CP");
        if (cpName is not null)
            writer.WriteAttributeString("caption-style", cpName);
        var coObj = reader is not null ? reader.Resolve(annot.Dict.Get("CO")) : annot.Dict.Get("CO");
        if (coObj is PdfArray coArr && coArr.Count >= 2)
        {
            writer.WriteAttributeString("caption-offset-h", F(GetDouble(coArr[0])));
            writer.WriteAttributeString("caption-offset-v", F(GetDouble(coArr[1])));
        }

        // File-attachment embedded-file metadata (/FS) — read straight from the
        // /FS/EF/F stream so the raw /Params strings round-trip verbatim. The file
        // bytes follow as a <data> child element below.
        if (annot.AnnotationType == AnnotationType.FileAttachment && reader is not null)
        {
            var fsd = reader.ResolveDict(annot.Dict.Get("FS"));
            var efd0 = reader.ResolveDict(fsd?.Get("EF"));
            var efStream0 = efd0 is null ? null : reader.ResolveStream(efd0.Get("F"));
            var nameObj = reader.Resolve(fsd?.Get("UF")) ?? reader.Resolve(fsd?.Get("F"));
            if (nameObj is PdfString nameStr) writer.WriteAttributeString("file", nameStr.ToText());
            var mimeName = efStream0?.Dict.GetName("Subtype");
            if (mimeName is not null) writer.WriteAttributeString("mimetype", mimeName);
            var prm = reader.ResolveDict(efStream0?.Dict.Get("Params"));
            long sz = prm?.Get("Size") is PdfInteger szi ? szi.Value : 0;
            writer.WriteAttributeString("size", sz.ToString(CultureInfo.InvariantCulture));
            if (prm?.Get("ModDate") is PdfString md) writer.WriteAttributeString("modification", md.ToText());
            if (prm?.Get("CreationDate") is PdfString cr) writer.WriteAttributeString("creation", cr.ToText());
            // /Params/CheckSum holds the raw 16-byte MD5; XFDF carries it hex-encoded.
            if (prm?.Get("CheckSum") is PdfString cs) writer.WriteAttributeString("checksum", ToHex(cs.Value));
        }

        // Sound annotation sampling parameters (/Sound R/B/C/E) — the audio bytes
        // follow as a <data> child element below.
        if (annot is Aspose.Pdf.Annotations.SoundAnnotation saMeta && saMeta.SoundData is { } sndMeta)
        {
            writer.WriteAttributeString("rate", sndMeta.Rate.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("bits", sndMeta.Bits.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("channels", sndMeta.Channels.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("encoding", sndMeta.Encoding switch
            {
                Aspose.Pdf.Annotations.SoundEncoding.Signed => "signed",
                Aspose.Pdf.Annotations.SoundEncoding.MuLaw => "muLaw",
                Aspose.Pdf.Annotations.SoundEncoding.ALaw => "aLaw",
                _ => "raw",
            });
        }

        // InReplyTo (/IRT — may be an indirect reference to the replied-to annotation)
        var irtD = annot.Dict.Get("IRT") as PdfDictionary
            ?? (reader is not null ? reader.ResolveDict(annot.Dict.Get("IRT")) : null);
        if (irtD is not null)
        {
            var irtNm = reader is not null ? reader.Resolve(irtD.Get("NM")) : irtD.Get("NM");
            if (irtNm is PdfString irtNmStr)
                writer.WriteAttributeString("inreplyto", irtNmStr.ToText());
        }

        // State / StateModel
        if (annot.AnnotationState is not null)
            writer.WriteAttributeString("state", annot.AnnotationState);
        if (annot.AnnotationStateModel is not null)
            writer.WriteAttributeString("statemodel", annot.AnnotationStateModel);

        // Open
        var openObj = annot.Dict.Get("Open");
        if (openObj is PdfBoolean ob)
            writer.WriteAttributeString("open", ob.Value ? "yes" : "no");

        // Opacity (/CA)
        var caObj = annot.Dict.Get("CA");
        if (caObj is PdfReal || caObj is PdfInteger)
            writer.WriteAttributeString("opacity", F(GetDouble(caObj)));

        // ReplyType (/RT → reply | group)
        var rtName = annot.Dict.GetName("RT");
        if (rtName is not null)
        {
            string? rt = rtName switch { "R" => "reply", "Group" => "group", _ => null };
            if (rt is not null) writer.WriteAttributeString("replyType", rt);
        }

        // Name (/NM) — XFDF attribute
        if (annot.Name is not null)
            writer.WriteAttributeString("name", annot.Name);

        // CreationDate (/CreationDate) — XFDF attribute
        var creationObj = annot.Dict.Get("CreationDate");
        if (creationObj is PdfString creationStr)
            writer.WriteAttributeString("creationdate", creationStr.ToText());

        // ── Child elements, ordered to match the XFDF output: geometry/data
        //    children, then contents-richtext, then popup, then default-appearance.

        // InkList (ink annotations)
        var inkListObj = annot.Dict.Get("InkList");
        if (inkListObj is PdfArray inkList && inkList.Count > 0)
        {
            writer.WriteStartElement("inklist");
            foreach (var gestureObj in inkList)
            {
                if (gestureObj is PdfArray gesture)
                {
                    writer.WriteStartElement("gesture");
                    var sb = new StringBuilder();
                    for (int i = 0; i < gesture.Count; i += 2)
                    {
                        if (i > 0) sb.Append(';');
                        if (i + 1 < gesture.Count)
                            sb.Append($"{F(GetDouble(gesture[i]))},{F(GetDouble(gesture[i + 1]))}");
                    }
                    writer.WriteString(sb.ToString());
                    writer.WriteEndElement();
                }
            }
            writer.WriteEndElement();
        }

        // Measure dictionary (line/polyline dimension annotations) — the XFDF
        // <measure> element: rateValue carries /R, and one child element per
        // number-format list (area/distance/xformat/yformat/angle/slope), each
        // child holding the first format's attributes (u/c/f/d/rd/rt/ss/fd).
        var measureObj = reader is not null ? reader.Resolve(annot.Dict.Get("Measure")) : annot.Dict.Get("Measure");
        if (measureObj is PdfDictionary measureDict)
        {
            writer.WriteStartElement("measure");
            if ((reader?.Resolve(measureDict.Get("R")) ?? measureDict.Get("R")) is PdfString rate)
                writer.WriteAttributeString("rateValue", rate.ToText());
            foreach (var (pdfKey, xfdfName) in new[]
                     { ("A", "area"), ("D", "distance"), ("X", "xformat"), ("Y", "yformat"), ("T", "angle"), ("S", "slope") })
            {
                if ((reader?.Resolve(measureDict.Get(pdfKey)) ?? measureDict.Get(pdfKey)) is not PdfArray fmts) continue;
                foreach (var fObj in fmts)
                {
                    if ((reader?.Resolve(fObj) ?? fObj) is not PdfDictionary nf) continue;
                    writer.WriteStartElement(xfdfName);
                    if ((reader?.Resolve(nf.Get("U")) ?? nf.Get("U")) is PdfString u)
                        writer.WriteAttributeString("u", u.ToText());
                    var c = reader?.Resolve(nf.Get("C")) ?? nf.Get("C");
                    if (c is PdfReal cr) writer.WriteAttributeString("c", F(cr.Value));
                    else if (c is PdfInteger ci) writer.WriteAttributeString("c", ci.Value.ToString(CultureInfo.InvariantCulture));
                    if (nf.GetName("F") is { } fCode) writer.WriteAttributeString("f", fCode);
                    if ((reader?.Resolve(nf.Get("D")) ?? nf.Get("D")) is PdfInteger den)
                        writer.WriteAttributeString("d", den.Value.ToString(CultureInfo.InvariantCulture));
                    foreach (var (k, attr) in new[] { ("RD", "rd"), ("RT", "rt"), ("PS", "ps"), ("SS", "ss") })
                        if ((reader?.Resolve(nf.Get(k)) ?? nf.Get(k)) is PdfString sv)
                            writer.WriteAttributeString(attr, sv.ToText());
                    writer.WriteEndElement();
                }
            }
            writer.WriteEndElement();
        }

        // Vertices (polygon / polyline) — child element, points separated by ';'
        var verticesChild = reader is not null ? reader.Resolve(annot.Dict.Get("Vertices")) : annot.Dict.Get("Vertices");
        if (verticesChild is PdfArray vca && vca.Count >= 2)
        {
            var vsb = new StringBuilder();
            for (int i = 0; i + 1 < vca.Count; i += 2)
            {
                if (i > 0) vsb.Append(';');
                vsb.Append($"{F(GetDouble(vca[i]))},{F(GetDouble(vca[i + 1]))}");
            }
            writer.WriteStartElement("vertices");
            writer.WriteString(vsb.ToString());
            writer.WriteEndElement();
        }

        // Default style (/DS — free text), before contents-richtext per XFDF order
        var dsObj = annot.Dict.Get("DS");
        if (dsObj is PdfString dsStr)
        {
            writer.WriteStartElement("defaultstyle");
            writer.WriteString(dsStr.ToText());
            writer.WriteEndElement();
        }

        // File-attachment embedded file (/FS/EF/F) as a <data> child.
        if (annot.AnnotationType == AnnotationType.FileAttachment && reader is not null)
        {
            var efd = reader.ResolveDict(reader.ResolveDict(annot.Dict.Get("FS"))?.Get("EF"));
            var efStream = efd is null ? null : reader.ResolveStream(efd.Get("F"));
            if (efStream is not null) WriteDataElement(writer, reader, efStream);
        }

        // Sound (/Sound stream) as a <data> child.
        if (annot.AnnotationType == AnnotationType.Sound && reader is not null)
        {
            var soundStream = reader.ResolveStream(annot.Dict.Get("Sound"));
            if (soundStream is not null) WriteDataElement(writer, reader, soundStream);
        }

        // Contents — emitted for the round-trip export (so /Contents survives),
        // omitted by WriteXfdf which carries the text only via contents-richtext.
        if (writeContents && annot.Contents is not null)
        {
            writer.WriteStartElement("contents");
            writer.WriteString(annot.Contents);
            writer.WriteEndElement();
        }

        // Rich text content (RC → contents-richtext)
        var rcObj = annot.Dict.Get("RC");
        if (rcObj is PdfString rcStr)
        {
            var rcText = rcStr.ToText();
            // Strip XML declaration if present — it can't appear inside another XML document
            if (rcText.StartsWith("<?xml", StringComparison.Ordinal))
            {
                int end = rcText.IndexOf("?>", StringComparison.Ordinal);
                if (end >= 0)
                    rcText = rcText.Substring(end + 2).TrimStart();
            }
            // Some XFDF producers normalise whitespace before a tag close
            // (" >") and after the xfa:spec attribute; the round-trip export
            // preserves the source verbatim.
            if (normalizeRichText)
                rcText = rcText.Replace(" >", ">").Replace("\"2.0.2\"  ", "\"2.0.2\" ");
            writer.WriteStartElement("contents-richtext");
            writer.WriteRaw(rcText);
            writer.WriteEndElement();
        }

        // Popup child (may be an indirect reference)
        var popupObj = annot.Dict.Get("Popup");
        var popup = popupObj as PdfDictionary
            ?? (reader is not null ? reader.ResolveDict(popupObj) : null);
        if (popup is not null)
        {
            writer.WriteStartElement("popup");
            var prObj = popup.Get("Rect");
            if (prObj is PdfArray pr && pr.Count >= 4)
                writer.WriteAttributeString("rect",
                    $"{F(GetDouble(pr[0]))},{F(GetDouble(pr[1]))},{F(GetDouble(pr[2]))},{F(GetDouble(pr[3]))}");

            var pf = popup.Get("F");
            if (pf is PdfInteger pfi && pfi.Value != 0)
                writer.WriteAttributeString("flags", FormatFlags((int)pfi.Value));

            writer.WriteAttributeString("page", zeroBasedPage.ToString(CultureInfo.InvariantCulture));

            // Open state as attribute (per XFDF spec for popup elements)
            var po = popup.Get("Open");
            if (po is PdfBoolean pob)
                writer.WriteAttributeString("open", pob.Value ? "yes" : "no");

            writer.WriteEndElement();
        }

        // Default appearance (/DA — free text), after contents-richtext per XFDF order
        var daObj = annot.Dict.Get("DA");
        if (daObj is PdfString daStr)
        {
            writer.WriteStartElement("defaultappearance");
            writer.WriteString(daStr.ToText());
            writer.WriteEndElement();
        }

        // Appearance (/AP) as a base64 <appearance> child (stamp annotations):
        // the WHOLE appearance object tree — the /N form XObject with its
        // resources, fonts and nested streams — serialized so the importer can
        // rebuild an identical appearance in the destination document.
        if (annot.AnnotationType == AnnotationType.Stamp && reader is not null)
        {
            var apDict = reader.ResolveDict(annot.Dict.Get("AP"));
            if (apDict is not null)
            {
                var xml = XfdfAppearanceCodec.Serialize(reader, "AP", apDict);
                writer.WriteStartElement("appearance");
                writer.WriteString(System.Convert.ToBase64String(
                    System.Text.Encoding.ASCII.GetBytes(xml),
                    System.Base64FormattingOptions.InsertLineBreaks));
                writer.WriteEndElement();
            }
        }

        writer.WriteEndElement(); // tag
    }

    private static string FormatFlags(int flags)
    {
        var parts = new List<string>();
        if ((flags & 1) != 0) parts.Add("invisible");
        if ((flags & 2) != 0) parts.Add("hidden");
        if ((flags & 4) != 0) parts.Add("print");
        if ((flags & 8) != 0) parts.Add("nozoom");
        if ((flags & 16) != 0) parts.Add("norotate");
        if ((flags & 32) != 0) parts.Add("noview");
        if ((flags & 64) != 0) parts.Add("readonly");
        if ((flags & 128) != 0) parts.Add("locked");
        if ((flags & 256) != 0) parts.Add("togglenoview");
        if ((flags & 512) != 0) parts.Add("lockedcontents");
        return string.Join(",", parts);
    }

    private static string AnnotationTypeToXfdfTag(AnnotationType type) => type switch
    {
        AnnotationType.Text => "text",
        AnnotationType.Link => "link",
        AnnotationType.FreeText => "freetext",
        AnnotationType.Line => "line",
        AnnotationType.Square => "square",
        AnnotationType.Circle => "circle",
        AnnotationType.Polygon => "polygon",
        AnnotationType.PolyLine => "polyline",
        AnnotationType.Highlight => "highlight",
        AnnotationType.Underline => "underline",
        AnnotationType.Squiggly => "squiggly",
        AnnotationType.StrikeOut => "strikeout",
        AnnotationType.Stamp => "stamp",
        AnnotationType.Caret => "caret",
        AnnotationType.Ink => "ink",
        AnnotationType.Popup => "popup",
        AnnotationType.FileAttachment => "fileattachment",
        AnnotationType.Sound => "sound",
        AnnotationType.Movie => "movie",
        AnnotationType.Widget => "widget",
        AnnotationType.Screen => "screen",
        AnnotationType.PrinterMark => "printermark",
        AnnotationType.TrapNet => "trapnet",
        AnnotationType.Watermark => "watermark",
        AnnotationType.ThreeD => "3d",
        AnnotationType.Redact => "redact",
        AnnotationType.RichMedia => "richmedia",
        _ => "unknown",
    };

    private static string F(double v) => v.ToString("G", CultureInfo.InvariantCulture);

    private static string FormatPdfDate(System.DateTime dt)
        => "D:" + dt.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    /// <summary>Write an embedded-stream payload as an XFDF &lt;data&gt; child:
    /// printable content is emitted filtered/ascii (decoded text); binary content
    /// is emitted raw/hex (the original encoded bytes). The original /Length and
    /// /Filter are recorded as attributes.</summary>
    private static void WriteDataElement(XmlWriter writer, IO.PdfReader reader, PdfStream stream)
    {
        var raw = stream.RawData ?? Array.Empty<byte>();
        var decoded = reader.DecodeStream(stream) ?? raw;
        long length = stream.Dict.Get("Length") is PdfInteger li ? li.Value : raw.Length;
        var filterName = stream.Dict.GetName("Filter");
        bool isAscii = decoded.Length > 0
            && Array.TrueForAll(decoded, b => b == 9 || b == 10 || b == 13 || (b >= 32 && b <= 126));
        writer.WriteStartElement("data");
        writer.WriteAttributeString("mode", isAscii ? "filtered" : "raw");
        writer.WriteAttributeString("encoding", isAscii ? "ascii" : "hex");
        writer.WriteAttributeString("length", length.ToString(CultureInfo.InvariantCulture));
        if (filterName is not null) writer.WriteAttributeString("filter", filterName);
        // Raw/hex payloads carry a single leading space (XFDF convention);
        // filtered/ascii payloads are written verbatim.
        writer.WriteString(isAscii ? Encoding.ASCII.GetString(decoded) : " " + ToHex(raw));
        writer.WriteEndElement();
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    /// <summary>Export every annotation in the bound document to an XFDF stream.</summary>
    public void ExportAnnotationsToXfdf(Stream xmlOutputStream)
    {
        var doc = Document;
        var allTypes = (AnnotationType[])System.Enum.GetValues(typeof(AnnotationType));
        ExportAnnotationsXfdf(xmlOutputStream, start: 1, end: doc.PageCount, allTypes);
    }

    /// <summary>Export annotations filtered by Subtype name strings.</summary>
    public void ExportAnnotationsXfdf(Stream xmlOutputStream, int start, int end, string[] annotTypes)
    {
        var enumTypes = MapStringToAnnotationTypes(annotTypes);
        ExportAnnotationsXfdf(xmlOutputStream, start, end, enumTypes);
    }
}
