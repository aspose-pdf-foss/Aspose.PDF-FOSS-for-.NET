using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfWriter
{
    /// <summary>The stages of the cross-reference stream write: the layout plan and the entry rows.</summary>
    private void WriteXRefStreamEntries(XRefStreamWriteState xw)
    {
        for (var i = 1; i < xw.size; i++)
        {
            var pos = i * xw.entrySize;

            if (xw.compressedEntries is not null && xw.compressedEntries.TryGetValue(i, out var compressed))
            {
                // Type 2: compressed in object stream
                xw.streamData[pos] = 2;
                WriteField(xw.streamData, pos + 1, xw.w2, compressed.streamObjNum);
                WriteField(xw.streamData, pos + 1 + xw.w2, xw.w3, compressed.indexInStream);
            }
            else if (_offsets.TryGetValue(i, out var offset))
            {
                // Type 1: uncompressed
                xw.streamData[pos] = 1;
                WriteField(xw.streamData, pos + 1, xw.w2, offset);
                WriteField(xw.streamData, pos + 1 + xw.w2, xw.w3, 0); // gen 0
            }
            else if (i == xw.xrefObjNum)
            {
                // The xref stream object itself — type 1
                xw.streamData[pos] = 1;
                WriteField(xw.streamData, pos + 1, xw.w2, xw.xrefOffset);
                WriteField(xw.streamData, pos + 1 + xw.w2, xw.w3, 0);
            }
            else
            {
                // Free entry
                xw.streamData[pos] = 0;
                WriteField(xw.streamData, pos + 1, xw.w2, 0);
                WriteField(xw.streamData, pos + 1 + xw.w2, xw.w3, 65535);
            }
        }
    }

    /// <summary></summary>
    private void PlanXRefStreamLayout(XRefStreamWriteState xw)
    {
        xw.xrefObjNum = AllocateObjectNumber();
        xw.xrefOffset = _output.Position;

        xw.maxObjNum = 0;
        foreach (var num in _offsets.Keys)
            if (num > xw.maxObjNum) xw.maxObjNum = num;

        if (xw.compressedEntries is not null)
        {
            foreach (var num in xw.compressedEntries.Keys)
                if (num > xw.maxObjNum) xw.maxObjNum = num;
        }

        // The xref stream itself is an object, so include it in the range
        if (xw.xrefObjNum > xw.maxObjNum) xw.maxObjNum = xw.xrefObjNum;

        xw.size = xw.maxObjNum + 1;

        xw.maxOffset = xw.xrefOffset; // xref stream position is the largest offset
        foreach (var off in _offsets.Values)
            if (off > xw.maxOffset) xw.maxOffset = off;

        xw.maxField2 = xw.maxOffset;
        if (xw.compressedEntries is not null)
        {
            foreach (var (stm, _) in xw.compressedEntries.Values)
                if (stm > xw.maxField2) xw.maxField2 = stm;
        }

        xw.w2 = ByteWidth(xw.maxField2);
        xw.maxField3 = 65535; // free entry gen
        if (xw.compressedEntries is not null)
        {
            foreach (var (_, idx) in xw.compressedEntries.Values)
                if (idx > xw.maxField3) xw.maxField3 = idx;
        }
    }
}
