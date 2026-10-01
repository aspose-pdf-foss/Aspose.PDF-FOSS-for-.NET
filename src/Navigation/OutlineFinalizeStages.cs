using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public sealed partial class OutlineCollection : Outlines, System.Collections.Generic.IEnumerable<OutlineItem>
{
// The stages of the outline finalize: the flatten walk and one outline item.
    private static void Flatten(OutlineFinalizeState ol, IReadOnlyList<OutlineItem> items)
    {
        foreach (var item in items)
        {
            ol.flatItems.Add((item, ol.baseObjNum++));
            if (item.Children.Count > 0)
                Flatten(ol, (IReadOnlyList<OutlineItem>)item.Children);
        }
    }

    /// <summary>x</summary>
    private void WriteOutlineItem(OutlineFinalizeState ol, OutlineItem item, int objNum)
    {
        var dict = new PdfDictionary();

        // Title (PDFDocEncoding for ASCII, UTF-16BE BOM for non-ASCII)
        dict.Set("Title", OutlineItem.EncodePdfText(item.Title ?? string.Empty));

        // Parent
        var parentItem = FindParent(item);
        var parentObjNum = parentItem is not null && ol.objMap.ContainsKey(parentItem)
            ? ol.objMap[parentItem]
            : ol.outlinesObjNum;
        dict.Set("Parent", new PdfIndirectRef(parentObjNum, 0));

        // Prev / Next siblings
        var siblings = parentItem is not null
            ? (IReadOnlyList<OutlineItem>)parentItem.Children
            : (IReadOnlyList<OutlineItem>)_items!;
        var idx = IndexOf(siblings!, item);
        if (idx > 0)
            dict.Set("Prev", new PdfIndirectRef(ol.objMap[siblings![idx - 1]], 0));
        if (idx < siblings!.Count - 1)
            dict.Set("Next", new PdfIndirectRef(ol.objMap[siblings[idx + 1]], 0));

        // First / Last children
        if (item.Children.Count > 0)
        {
            dict.Set("First", new PdfIndirectRef(ol.objMap[item.Children[0]], 0));
            dict.Set("Last", new PdfIndirectRef(ol.objMap[item.Children[^1]], 0));
            // PDF /Count semantics: visible-descendant magnitude, negated
            // while the node is closed (so Open state survives reload).
            var count = item.VisibleMagnitude;
            dict.Set("Count", new PdfInteger(item.IsOpen ? count : -count));
        }

        // Copy /Dest or /A from the original dict if available
        if (item.Dict is not null)
        {
            var sourceReader = item.Reader;
            CopyEntryIfPresent(item.Dict, dict, "Dest", sourceReader, ol.doc);
            CopyEntryIfPresent(item.Dict, dict, "A", sourceReader, ol.doc);
            CopyEntryIfPresent(item.Dict, dict, "C", sourceReader, ol.doc);
            CopyEntryIfPresent(item.Dict, dict, "F", sourceReader, ol.doc);
        }

        ol.doc.AddNewObject(objNum, dict);
    }
}
