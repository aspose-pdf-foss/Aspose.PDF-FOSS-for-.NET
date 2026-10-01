using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>The stages of the merged XFA array: the part collection and the part emit, one part at a time.</summary>
    private void EmitMergedXfaPart(MergedXfaState xm, int i)
    {
        var dRoot = xm.parts[i].ds?.DocumentElement;
        if (dRoot is null) return;
        var thisData = FindDataElement(dRoot);
        if (thisData is null) return;
        var map = xm.renameByInput.TryGetValue(i, out var m) ? m : new Dictionary<string, string>();

        if (xm.mergedDs is null)
        {
            xm.mergedDs = xm.parts[i].ds;
            xm.dataEl = thisData;
            xm.dsRootEl = xm.mergedDs!.CreateElement("root");
        }
        foreach (var dc in ElementChildren(thisData))
        {
            var imported = (XmlElement)xm.mergedDs!.ImportNode(dc, deep: true);
            if (map.TryGetValue(dc.LocalName, out var nn) && nn != dc.LocalName)
                imported = RenameElement(xm.mergedDs, imported, nn);
            xm.dsRootEl!.AppendChild(imported);
        }
    }

    /// <summary>The stages of the merged XFA array: the part collection and the part emit, one part at a time.</summary>
    private void CollectMergedXfaPart(MergedXfaState xm, int i)
    {
        var tRoot = xm.parts[i].tpl?.DocumentElement;
        if (tRoot is null) return;
        var subforms = TopContainerChildren(tRoot);
        if (subforms.Count == 0) return;

        if (xm.mergedTpl is null)
        {
            xm.mergedTpl = xm.parts[i].tpl;
            xm.tplRootSub = xm.mergedTpl!.CreateElement(tRoot.Prefix, "subform", tRoot.NamespaceURI);
            xm.tplRootSub.SetAttribute("name", "root");
        }

        var map = new Dictionary<string, string>();
        xm.renameByInput[i] = map;
        foreach (var sf in subforms)
        {
            var orig = sf.GetAttribute("name");
            string newName;
            if (!xm.firstXmlByName.ContainsKey(orig))
            {
                newName = orig;
                xm.firstXmlByName[orig] = sf.OuterXml;
            }
            else
            {
                xm.dupCount.TryGetValue(orig, out var n); n++; xm.dupCount[orig] = n;
                if (_uniqueSuffixSet)
                    newName = orig + ApplyUniqueSuffix(_uniqueSuffix, n);
                else if (_keepFieldsUnique == false)
                    newName = orig;
                else
                    newName = sf.OuterXml == xm.firstXmlByName[orig]
                        ? orig
                        : orig + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            if (!map.ContainsKey(orig)) map[orig] = newName;

            var imported = (XmlElement)xm.mergedTpl!.ImportNode(sf, deep: true);
            imported.SetAttribute("name", newName);
            xm.tplRootSub!.AppendChild(imported);
        }
    }
}
