using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>The stages of the XFA datasets replace: the packet rewrite, the single-stream form and the array form.</summary>
    private void ReplaceXfaDatasetsInArray(XfaDatasetsReplaceState xd, PdfArray xfaArray)
    {
        // XFA array without a named "datasets" part — create one
        const string xfaNs2 = "http://www.xfa.org/schema/xfa-data/1.0/";
        var datasetsDoc = new XmlDocument();
        var datasetsEl2 = datasetsDoc.CreateElement("xfa", "datasets", xfaNs2);
        datasetsDoc.AppendChild(datasetsEl2);
        var dataEl2 = datasetsDoc.CreateElement("xfa", "data", xfaNs2);
        datasetsEl2.AppendChild(dataEl2);

        ImportDataChildren(xd.importedXml, datasetsDoc, dataEl2);

        using var ms = new MemoryStream();
        SaveXmlNoBom(datasetsDoc, ms);
        var newData = ms.ToArray();
        var newStream = new PdfStream(new PdfDictionary(), newData);
        newStream.Dict.Set("Length", new PdfInteger(newData.Length));

        // Insert "datasets" name + stream before "postamble" (or at end)
        int insertIdx = xfaArray.Count;
        for (int i = 0; i < xfaArray.Count - 1; i += 2)
        {
            if (xfaArray[i] is PdfString s &&
                Compat.Latin1.GetString(s.Value) == "postamble")
            {
                insertIdx = i;
                break;
            }
        }
        xfaArray.Insert(insertIdx, new PdfString(Compat.Latin1.GetBytes("datasets")));
        xfaArray.Insert(insertIdx + 1, newStream);
    }

    /// <summary></summary>
    private bool ReplaceXfaDatasetsInStream(XfaDatasetsReplaceState xd, PdfStream singleStream)
    {
        var xdpData = xd.rdr!.DecodeStream(singleStream);
        var xdpXml = Encoding.UTF8.GetString(xdpData);
        var xdpDoc = new XmlDocument();
        xdpDoc.LoadXml(xdpXml);

        // Find or create the <datasets> element
        var datasetsEl = xdpDoc.DocumentElement?.SelectSingleNode("//*[local-name()='datasets']");
        if (datasetsEl is null && xdpDoc.DocumentElement is not null)
        {
            // Create <xfa:datasets> element and insert before postamble
            const string xfaNs = "http://www.xfa.org/schema/xfa-data/1.0/";
            datasetsEl = xdpDoc.CreateElement("xfa", "datasets", xfaNs);
            // Try to insert before the closing </xdp:xdp> (last child or before postamble)
            xdpDoc.DocumentElement.AppendChild(datasetsEl);
        }
        if (datasetsEl is null) return false;

        // Find or create the <data> element inside <datasets>
        var dataEl = datasetsEl.SelectSingleNode("*[local-name()='data']");
        if (dataEl is null)
        {
            var ns = datasetsEl.NamespaceURI;
            var prefix = datasetsEl.Prefix;
            dataEl = string.IsNullOrEmpty(prefix)
                ? xdpDoc.CreateElement("data", ns)
                : xdpDoc.CreateElement(prefix, "data", ns);
            datasetsEl.AppendChild(dataEl);
        }

        // Clear existing data and import the root element of the imported XML
        dataEl.InnerXml = "";
        ImportDataChildren(xd.importedXml, xdpDoc, dataEl);

        // Write updated XDP back to the stream (no BOM)
        using var ms = new MemoryStream();
        SaveXmlNoBom(xdpDoc, ms);
        var newData = ms.ToArray();
        singleStream.ReplaceData(newData);
        singleStream.Dict.Set("Length", new PdfInteger(newData.Length));
        singleStream.Dict.Remove("Filter");
        MarkXfaStreamDirty(singleStream);
        return false;
    }

    /// <summary></summary>
    private bool RewriteXfaDatasetsPacket(XfaDatasetsReplaceState xd, PdfStream stream, string existingXml)
    {
        // Existing datasets part — merge imported data into it
        var existingDoc = new XmlDocument();
        existingDoc.LoadXml(existingXml);

        var dataNs = existingDoc.DocumentElement?.SelectSingleNode("//*[local-name()='data']");
        // If <data> doesn't exist (only <dataDescription>), create it
        if (dataNs is null && existingDoc.DocumentElement is not null)
        {
            var ns = existingDoc.DocumentElement.NamespaceURI;
            var prefix = existingDoc.DocumentElement.Prefix;
            dataNs = string.IsNullOrEmpty(prefix)
                ? existingDoc.CreateElement("data", ns)
                : existingDoc.CreateElement(prefix, "data", ns);
            existingDoc.DocumentElement.AppendChild(dataNs);
        }
        if (dataNs is null) return false;

        dataNs.InnerXml = "";
        // Unwrap xfa:data / xfa:datasets wrapper if present in the imported XML,
        // so we don't double-nest (e.g. <data><xfa:data><form1>.)
        ImportDataChildren(xd.importedXml, existingDoc, dataNs);

        using var ms = new MemoryStream();
        SaveXmlNoBom(existingDoc, ms);
        var newData = ms.ToArray();
        stream.ReplaceData(newData);
        stream.Dict.Set("Length", new PdfInteger(newData.Length));
        stream.Dict.Remove("Filter");
        MarkXfaStreamDirty(stream);
        return false;
    }
}
