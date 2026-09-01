using System.Collections;
using System.Globalization;
using System.Text;
namespace Aspose.Pdf.Core;

internal sealed class PdfStream : PdfObject
{
    public PdfDictionary Dict { get; }
    public byte[] RawData { get; private set; }

    /// <summary>Object number for decryption context.</summary>
    internal int ObjectNumber { get; set; }

    /// <summary>Generation number for decryption context.</summary>
    internal int Generation { get; set; }

    /// <summary>When true, the writer must emit the raw bytes verbatim with
    /// no /Filter — used for embedded files added with FileEncoding.None.</summary>
    internal bool DoNotCompress { get; set; }

    public PdfStream(PdfDictionary dict, byte[] rawData)
    {
        Dict = dict;
        RawData = rawData;
    }

    /// <summary>Replace the raw stream data (used by optimization).</summary>
    internal void ReplaceData(byte[] newData) => RawData = newData;
}
