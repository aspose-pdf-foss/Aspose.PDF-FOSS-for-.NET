using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileEditor
{
    /// <summary>Route an Append through Concatenate when the destination and at least
    /// one port carry an XFA template (the /XFA packets then merge instead of the
    /// ports' being dropped). Returns the concatenation inputs — the destination plus
    /// each port trimmed to the requested page range — or null when no XFA merge
    /// applies. A port is passed whole when the range spans it entirely, so its /XFA +
    /// AcroForm survive verbatim (Extract rebuilds a plain page document and would
    /// shed them).</summary>
    private byte[][]? BuildXfaAppendInputs(byte[] inputPdf, byte[][] portPdfs, int startPage, int endPage)
    {
        if (!HasXfaTemplate(inputPdf)) return null;
        var pieces = new List<byte[]>(portPdfs.Length + 1) { inputPdf };
        int withTemplate = 1;
        foreach (var portData in portPdfs)
        {
            var piece = portData;
            int pageCount;
            using (var portDoc = Document.Open(portData)) pageCount = portDoc.PageCount;
            if (startPage > 1 || endPage < pageCount)
                piece = Extract(portData, startPage, endPage);
            if (HasXfaTemplate(piece)) withTemplate++;
            pieces.Add(piece);
        }
        return withTemplate >= 2 ? pieces.ToArray() : null;
    }

    /// <summary>True when the PDF's AcroForm carries an XFA template packet.</summary>
    private static bool HasXfaTemplate(byte[] pdf)
    {
        try
        {
            TryGetXfaPackets(PdfReader.FromBytes(pdf), out var tplXml, out _);
            return tplXml is not null;
        }
        catch { return false; }
    }

    /// <summary>Compute, per input, the rename map (original top-subform name → merged
    /// name) that <see cref="BuildMergedXfaArray"/> applies to the /XFA template, so the
    /// AcroForm field tree can be re-parented under the same synthetic "root" with matching
    /// names. Returns null when fewer than two inputs carry an XFA template (no XFA merge
    /// happens — the flat AcroForm merge is used instead). Mirrors the disambiguation policy
    /// in <see cref="BuildMergedXfaArray"/> exactly.</summary>
    private List<Dictionary<string, string>>? ComputeXfaTopSubformRenames(List<PdfReader> readers)
    {
        var tplRoots = new List<XmlElement?>();
        int withTemplate = 0;
        foreach (var r in readers)
        {
            TryGetXfaPackets(r, out var tplXml, out _);
            var doc = LoadXmlOrNull(tplXml);
            tplRoots.Add(doc?.DocumentElement);
            if (doc?.DocumentElement is not null) withTemplate++;
        }
        if (withTemplate < 2) return null;

        var result = new List<Dictionary<string, string>>();
        var firstXmlByName = new Dictionary<string, string>();
        var dupCount = new Dictionary<string, int>();
        foreach (var tRoot in tplRoots)
        {
            var map = new Dictionary<string, string>();
            result.Add(map);
            if (tRoot is null) continue;
            foreach (var sf in TopContainerChildren(tRoot))
            {
                var orig = sf.GetAttribute("name");
                string newName;
                if (!firstXmlByName.ContainsKey(orig))
                {
                    newName = orig;
                    firstXmlByName[orig] = sf.OuterXml;
                }
                else
                {
                    dupCount.TryGetValue(orig, out var n); n++; dupCount[orig] = n;
                    if (_uniqueSuffixSet)
                        newName = orig + ApplyUniqueSuffix(_uniqueSuffix, n);
                    else if (_keepFieldsUnique == false)
                        newName = orig;
                    else
                        newName = sf.OuterXml == firstXmlByName[orig]
                            ? orig
                            : orig + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (!map.ContainsKey(orig)) map[orig] = newName;
            }
        }
        return result;
    }

    /// <summary>Deep-clone an AcroForm field node (and its whole /Kids subtree) into the
    /// output, wiring each node's /Parent to its new parent so <c>BuildFullName</c> and
    /// <c>CollectGroupFields</c> can reconstruct the hierarchical names. Unlike
    /// <see cref="RemapObject"/> (which strips /Parent), this rebuilds the parent chain;
    /// /P (page back-ref) is dropped to avoid cloning entire pages. Returns the new object
    /// number of the cloned node.</summary>
    private static int CloneFieldNode(PdfDictionary src, PdfReader reader,
        Dictionary<int, int> remap, PdfWriter writer, int parentNum, HashSet<int> onPath)
    {
        int myNum = writer.AllocateObjectNumber();
        var clone = new PdfDictionary();
        foreach (var key in src.Keys)
        {
            if (key is "Parent" or "Kids" or "P") continue;
            var v = src.Get(key);
            if (v is not null) clone.Set(key, RemapObject(v, reader, remap, writer));
        }
        clone.Set("Parent", new PdfIndirectRef(parentNum, 0));
        CloneFieldKids(src, clone, reader, remap, writer, myNum, onPath);
        writer.WriteIndirectObject(myNum, clone);
        return myNum;
    }

    /// <summary>Clone a field node's /Kids into <paramref name="clone"/>. A malformed form can
    /// list a node that already sits on the path from the root as its own descendant; such a
    /// kid is DROPPED rather than followed, because cloning it would descend for ever. (The
    /// merge path survives those files only because its object map makes the second visit a
    /// no-op.) <paramref name="onPath"/> holds the source object numbers from the root down to
    /// this node; each branch gets its own copy so a node reached twice by DIFFERENT branches
    /// is still cloned into both.</summary>
    private static void CloneFieldKids(PdfDictionary src, PdfDictionary clone, PdfReader reader,
        Dictionary<int, int> remap, PdfWriter writer, int myNum, HashSet<int> onPath)
    {
        if (reader.Resolve(src.Get("Kids")) is not PdfArray kids) return;
        var outKids = new PdfArray();
        foreach (var kid in kids)
        {
            var kd = reader.ResolveDict(kid);
            if (kd is null) continue;
            var branch = new HashSet<int>(onPath);
            if (kid is PdfIndirectRef r && !branch.Add(r.ObjectNumber)) continue;
            outKids.Add(new PdfIndirectRef(CloneFieldNode(kd, reader, remap, writer, myNum, branch), 0));
        }
        clone.Set("Kids", outKids);
    }

    /// <summary>Deep-clone a TOP-LEVEL AcroForm field (a root of the /Fields array) with its
    /// whole /Kids subtree, preserving the /Parent chain on descendants so their hierarchical
    /// FullNames survive. The root itself gets no /Parent (top-level fields have none), and its
    /// /T is replaced by <paramref name="overrideName"/> when non-null (duplicate-name rename).
    /// /P (page back-ref) is dropped to avoid cloning entire pages. Returns the new object number.</summary>
    private static int CloneTopFieldNode(PdfDictionary src, PdfReader reader,
        Dictionary<int, int> remap, PdfWriter writer, string? overrideName, int? srcObjNum = null)
    {
        int myNum = writer.AllocateObjectNumber();
        var clone = new PdfDictionary();
        foreach (var key in src.Keys)
        {
            if (key is "Parent" or "Kids" or "P") continue;
            if (key == "T" && overrideName is not null) continue;
            var v = src.Get(key);
            if (v is not null) clone.Set(key, RemapObject(v, reader, remap, writer));
        }
        if (overrideName is not null)
            clone.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(overrideName)));
        var root = new HashSet<int>();
        if (srcObjNum is { } sn) root.Add(sn);
        CloneFieldKids(src, clone, reader, remap, writer, myNum, root);
        writer.WriteIndirectObject(myNum, clone);
        return myNum;
    }

    /// <summary>Build the merged /XFA array, or null when fewer than two inputs carry
    /// an XFA template (nothing to merge).</summary>
    private PdfArray? BuildMergedXfaArray(List<PdfReader> readers)
    {
        var parts = new List<(XmlDocument? tpl, XmlDocument? ds)>();
        int withTemplate = 0;
        foreach (var r in readers)
        {
            TryGetXfaPackets(r, out var tplXml, out var dsXml);
            var tplDoc = LoadXmlOrNull(tplXml);
            var dsDoc = LoadXmlOrNull(dsXml);
            parts.Add((tplDoc, dsDoc));
            if (tplDoc is not null) withTemplate++;
        }
        if (withTemplate < 2) return null;

        // ── Merged template ──
        XmlDocument? mergedTpl = null;
        XmlElement? tplRootSub = null;                       // synthetic <subform name="root">
        var renameByInput = new Dictionary<int, Dictionary<string, string>>();
        var firstXmlByName = new Dictionary<string, string>();   // origName → first occurrence's subtree
        var dupCount = new Dictionary<string, int>();

        for (int i = 0; i < parts.Count; i++)
        {
            var tRoot = parts[i].tpl?.DocumentElement;
            if (tRoot is null) continue;
            var subforms = TopContainerChildren(tRoot);
            if (subforms.Count == 0) continue;

            if (mergedTpl is null)
            {
                mergedTpl = parts[i].tpl;
                tplRootSub = mergedTpl!.CreateElement(tRoot.Prefix, "subform", tRoot.NamespaceURI);
                tplRootSub.SetAttribute("name", "root");
            }

            var map = new Dictionary<string, string>();
            renameByInput[i] = map;
            foreach (var sf in subforms)
            {
                var orig = sf.GetAttribute("name");
                string newName;
                if (!firstXmlByName.ContainsKey(orig))
                {
                    newName = orig;
                    firstXmlByName[orig] = sf.OuterXml;
                }
                else
                {
                    dupCount.TryGetValue(orig, out var n); n++; dupCount[orig] = n;
                    if (_uniqueSuffixSet)
                        newName = orig + ApplyUniqueSuffix(_uniqueSuffix, n);
                    else if (_keepFieldsUnique == false)
                        newName = orig;
                    else
                        newName = sf.OuterXml == firstXmlByName[orig]
                            ? orig
                            : orig + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (!map.ContainsKey(orig)) map[orig] = newName;

                var imported = (XmlElement)mergedTpl!.ImportNode(sf, deep: true);
                imported.SetAttribute("name", newName);
                tplRootSub!.AppendChild(imported);
            }
        }
        if (mergedTpl?.DocumentElement is null || tplRootSub is null) return null;
        RemoveTopContainerChildren(mergedTpl.DocumentElement);
        mergedTpl.DocumentElement.AppendChild(tplRootSub);
        var mergedTemplateXml = mergedTpl.DocumentElement.OuterXml;

        // ── Merged datasets ──
        XmlDocument? mergedDs = null;
        XmlElement? dsRootEl = null;                         // synthetic <root>
        XmlElement? dataEl = null;
        for (int i = 0; i < parts.Count; i++)
        {
            var dRoot = parts[i].ds?.DocumentElement;
            if (dRoot is null) continue;
            var thisData = FindDataElement(dRoot);
            if (thisData is null) continue;
            var map = renameByInput.TryGetValue(i, out var m) ? m : new Dictionary<string, string>();

            if (mergedDs is null)
            {
                mergedDs = parts[i].ds;
                dataEl = thisData;
                dsRootEl = mergedDs!.CreateElement("root");
            }
            foreach (var dc in ElementChildren(thisData))
            {
                var imported = (XmlElement)mergedDs!.ImportNode(dc, deep: true);
                if (map.TryGetValue(dc.LocalName, out var nn) && nn != dc.LocalName)
                    imported = RenameElement(mergedDs, imported, nn);
                dsRootEl!.AppendChild(imported);
            }
        }
        string? mergedDatasetsXml = null;
        if (mergedDs?.DocumentElement is not null && dsRootEl is not null && dataEl is not null)
        {
            RemoveElementChildren(dataEl);
            dataEl.AppendChild(dsRootEl);
            mergedDatasetsXml = mergedDs.DocumentElement.OuterXml;
        }

        // ── Emit /XFA array ──
        var arr = new PdfArray();
        arr.Add(new PdfString(Encoding.Latin1.GetBytes("template")));
        arr.Add(new PdfStream(new PdfDictionary(), Encoding.UTF8.GetBytes(mergedTemplateXml)));
        if (mergedDatasetsXml is not null)
        {
            arr.Add(new PdfString(Encoding.Latin1.GetBytes("datasets")));
            arr.Add(new PdfStream(new PdfDictionary(), Encoding.UTF8.GetBytes(mergedDatasetsXml)));
        }
        return arr;
    }

    /// <summary>Read the template / datasets XML from an input's /XFA (array of
    /// named parts, or a single-stream XDP).</summary>
    private static void TryGetXfaPackets(PdfReader reader, out string? templateXml, out string? datasetsXml)
    {
        templateXml = null; datasetsXml = null;
        var acro = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acro is null) return;
        var xfa = reader.Resolve(acro.Get("XFA"));
        if (xfa is PdfArray arr)
        {
            for (int i = 0; i + 1 < arr.Count; i += 2)
            {
                if (arr[i] is not PdfString s) continue;
                var part = Encoding.Latin1.GetString(s.Value);
                if (reader.Resolve(arr[i + 1]) is not PdfStream stream) continue;
                var txt = StripXfaBom(Encoding.UTF8.GetString(reader.DecodeStream(stream)));
                if (part == "template") templateXml = txt;
                else if (part == "datasets") datasetsXml = txt;
            }
        }
        else if (xfa is PdfStream single)
        {
            var xdp = StripXfaBom(Encoding.UTF8.GetString(reader.DecodeStream(single)));
            var doc = LoadXmlOrNull(xdp);
            if (doc?.DocumentElement is not null)
            {
                var tpl = FindDescendantByLocalName(doc.DocumentElement, "template");
                var ds = FindDescendantByLocalName(doc.DocumentElement, "datasets");
                templateXml = tpl?.OuterXml;
                datasetsXml = ds?.OuterXml;
            }
        }
    }

    private static XmlDocument? LoadXmlOrNull(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return null;
        var doc = new XmlDocument { PreserveWhitespace = false };
        try { doc.LoadXml(xml); } catch { return null; }
        return doc.DocumentElement is null ? null : doc;
    }

    /// <summary>Top-level container children (subform / exclGroup) of a template root.</summary>
    private static List<XmlElement> TopContainerChildren(XmlElement templateRoot)
    {
        var list = new List<XmlElement>();
        foreach (XmlNode ch in templateRoot.ChildNodes)
            if (ch is XmlElement el && (el.LocalName == "subform" || el.LocalName == "exclGroup"))
                list.Add(el);
        return list;
    }

    private static void RemoveTopContainerChildren(XmlElement templateRoot)
    {
        foreach (var el in TopContainerChildren(templateRoot))
            templateRoot.RemoveChild(el);
    }

    /// <summary>Find the &lt;data&gt; element (xfa-data packet content) under a datasets root.</summary>
    private static XmlElement? FindDataElement(XmlElement datasetsRoot)
    {
        if (datasetsRoot.LocalName == "data") return datasetsRoot;
        foreach (XmlNode ch in datasetsRoot.ChildNodes)
            if (ch is XmlElement el && el.LocalName == "data") return el;
        return null;
    }

    private static List<XmlElement> ElementChildren(XmlElement node)
    {
        var list = new List<XmlElement>();
        foreach (XmlNode ch in node.ChildNodes)
            if (ch is XmlElement el) list.Add(el);
        return list;
    }

    private static void RemoveElementChildren(XmlElement node)
    {
        foreach (var el in ElementChildren(node))
            node.RemoveChild(el);
    }

    /// <summary>Return a copy of <paramref name="el"/> renamed to <paramref name="newName"/>,
    /// preserving its namespace, attributes and children.</summary>
    private static XmlElement RenameElement(XmlDocument doc, XmlElement el, string newName)
    {
        var ne = doc.CreateElement(el.Prefix, newName, el.NamespaceURI);
        foreach (XmlAttribute a in el.Attributes)
            ne.SetAttributeNode((XmlAttribute)a.CloneNode(true));
        while (el.FirstChild is not null)
            ne.AppendChild(el.FirstChild);
        return ne;
    }

    private static XmlElement? FindDescendantByLocalName(XmlElement root, string localName)
    {
        if (root.LocalName == localName) return root;
        foreach (XmlNode ch in root.ChildNodes)
        {
            if (ch is not XmlElement el) continue;
            var found = FindDescendantByLocalName(el, localName);
            if (found is not null) return found;
        }
        return null;
    }

    private static string StripXfaBom(string s) =>
        s.Length > 0 && s[0] == '﻿' ? s.Substring(1) : s;
}
