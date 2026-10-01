using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO;

internal sealed partial class XRefTable
{
    /// <summary>The stages of the cross-reference stream read: the /Index parse and one subsection of entries.</summary>
    private void ReadXrefSubsection(XrefStreamState xs, int start, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (xs.dataPos + xs.entrySize > xs.decodedData.Length) break;

            var type = xs.w1 > 0 ? ReadFieldValue(xs.decodedData, xs.dataPos, xs.w1) : 1; // default type=1
            var field2 = ReadFieldValue(xs.decodedData, xs.dataPos + xs.w1, xs.w2);
            var field3 = ReadFieldValue(xs.decodedData, xs.dataPos + xs.w1 + xs.w2, xs.w3);
            xs.dataPos += xs.entrySize;

            var objNum = start + i;
            // In hybrid-reference PDFs, the traditional xref marks compressed objects
            // as free while the xref stream has the real entries. Allow in-use entries
            // from the xref stream to override free entries from the traditional table.
            if (_entries.TryGetValue(objNum, out var existing))
            {
                if (existing.InUse || type == 0) continue;
                // Existing is free but xref stream says in-use — override below
            }

            switch (type)
            {
                case 0: // free
                    _entries[objNum] = new XRefEntry
                    {
                        ObjectNumber = objNum,
                        Generation = (int)field3,
                        InUse = false
                    };
                    break;
                case 1: // uncompressed
                    _entries[objNum] = new XRefEntry
                    {
                        ObjectNumber = objNum,
                        Offset = field2,
                        Generation = (int)field3,
                        InUse = true
                    };
                    break;
                case 2: // compressed in object stream
                    _entries[objNum] = new XRefEntry
                    {
                        ObjectNumber = objNum,
                        InUse = true,
                        IsCompressed = true,
                        StreamObjectNumber = (int)field2,
                        IndexInStream = (int)field3
                    };
                    break;
            }
        }
    }

    /// <summary>The stages of the cross-reference stream read: the /Index parse and one subsection of entries.</summary>
    private void ReadXrefIndex(XrefStreamState xs)
    {
        xs.subsections = new List<(int, int)>();
        for (var i = 0; i < xs.indexArray!.Count; i += 2)
        {
            xs.subsections.Add((
                (int)((PdfInteger)xs.indexArray[i]).Value,
                (int)((PdfInteger)xs.indexArray[i + 1]).Value
            ));
        }
    }
}
