using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Security;

namespace Aspose.Pdf.IO;

internal sealed partial class PdfWriter
{
    /// <summary>The stages of the object-stream write: one group of compressible objects at a time.</summary>
    private void WriteObjectStreamGroup(ObjectStreamWriteState ow, List<int> group)
    {
        var objStmNum = AllocateObjectNumber();

        // Build the object stream content:
        // Header: N pairs of "objNum offset\n"
        // Body: serialized objects at those offsets
        var headerBuilder = new StringBuilder();
        var bodyBuilder = new MemoryStream();
        var offsets = new List<int>();

        foreach (var objNum in group)
        {
            var obj = _allObjects[objNum];
            var serialized = SerializeObject(obj);
            offsets.Add((int)bodyBuilder.Position);
            headerBuilder.Append($"{objNum} {bodyBuilder.Position} ");
            bodyBuilder.Write(serialized);
            bodyBuilder.WriteByte((byte)' '); // separator between objects
        }

        var headerBytes = Encoding.ASCII.GetBytes(headerBuilder.ToString());
        var bodyBytes = bodyBuilder.ToArray();

        // Combine header + body
        var combined = new byte[headerBytes.Length + bodyBytes.Length];
        headerBytes.CopyTo(combined, 0);
        bodyBytes.CopyTo(combined, headerBytes.Length);

        // Compress with FlateDecode
        var compressed = Compress(combined);

        // Build ObjStm dictionary
        var objStmDict = new PdfDictionary();
        objStmDict.Set("Type", new PdfName("ObjStm"));
        objStmDict.Set("N", new PdfInteger(group.Count));
        objStmDict.Set("First", new PdfInteger(headerBytes.Length));
        objStmDict.Set("Filter", new PdfName("FlateDecode"));
        objStmDict.Set("Length", new PdfInteger(compressed.Length));

        // Write the ObjStm as a regular indirect object
        var objStmOffset = _output.Position;
        _offsets[objStmNum] = objStmOffset;

        _currentObjectNumber = objStmNum;
        WriteRaw($"{objStmNum} 0 obj\n");
        WriteDictionary(objStmDict);
        WriteRaw("\nstream\n");
        _output.Write(compressed);
        WriteRaw("\nendstream\nendobj\n");
        _currentObjectNumber = -1;

        // Record compressed entries and remove standalone offsets
        for (var i = 0; i < group.Count; i++)
        {
            ow.compressedEntries[group[i]] = (objStmNum, i);
            _offsets.Remove(group[i]); // Remove from standalone offsets
        }
    }
}
