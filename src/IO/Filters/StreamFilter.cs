using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Dispatches PDF stream decoding to the appropriate filter implementation
/// based on the /Filter entry in the stream dictionary (PDF 32000 §7.4.1).
/// Supports filter pipelines (arrays of filters applied in sequence).
/// </summary>
internal static class StreamFilter
{
    public static byte[] Decode(byte[] data, PdfDictionary streamDict)
    {
        var filterObj = streamDict.Get("Filter");
        if (filterObj is null)
            return data;

        var decodeParms = streamDict.Get("DecodeParms");

        if (filterObj is PdfName singleFilter)
        {
            var parms = decodeParms as PdfDictionary;
            return ApplyFilter(data, singleFilter.Value, parms);
        }

        if (filterObj is PdfArray filterArray)
        {
            var parmsArray = decodeParms as PdfArray;
            var result = data;
            for (var i = 0; i < filterArray.Count; i++)
            {
                var name = ((PdfName)filterArray[i]).Value;
                var parms = parmsArray is not null && i < parmsArray.Count
                    ? parmsArray[i] as PdfDictionary
                    : null;
                result = ApplyFilter(result, name, parms);
            }
            return result;
        }

        return data;
    }

    private static byte[] ApplyFilter(byte[] data, string filterName, PdfDictionary? parms)
    {
        return filterName switch
        {
            "FlateDecode" or "Fl" => FlateDecodeFilter.Decode(data, parms),
            "ASCII85Decode" or "A85" => Ascii85DecodeFilter.Decode(data),
            "ASCIIHexDecode" or "AHx" => AsciiHexDecodeFilter.Decode(data),
            "LZWDecode" or "LZW" => LzwDecodeFilter.Decode(data, parms),
            "RunLengthDecode" or "RL" => RunLengthDecodeFilter.Decode(data),
            "CCITTFaxDecode" or "CCF" => CcittFaxDecodeFilter.Decode(data, parms),
            // Pass-through filters: data is already in the target format
            // JPEG images are stored as complete JFIF byte streams
            "DCTDecode" or "DCT" => data,
            // JPEG2000 images are stored as complete JP2 codestreams
            "JPXDecode" or "JPX" => data,
            "JBIG2Decode" => DecodeJbig2(data, parms),
            // Crypt filter: Identity means no encryption; actual decryption is
            // handled by PdfReader before filter pipeline runs
            "Crypt" => data,
            _ => data, // Unknown filters: pass through rather than throw
        };
    }

    private static byte[] DecodeJbig2(byte[] data, PdfDictionary? parms)
    {
        try
        {
            byte[]? globals = null;
            if (parms is not null)
            {
                var globalsStream = parms.Get("JBIG2Globals");
                if (globalsStream is PdfStream gs)
                {
                    // Globals may themselves be compressed
                    globals = Decode(gs.RawData, gs.Dict);
                }
            }
            var bits = Jbig2Decoder.Decode(data, globals);
            // JBIG2 uses 1 = black, the opposite of the 1-bit DeviceGray convention the
            // image model assumes (0 = black). Invert so the decoded samples render with
            // the right polarity (PDF 32000 §7.4.7); otherwise a scan comes out inverted.
            for (var i = 0; i < bits.Length; i++) bits[i] = (byte)~bits[i];
            return bits;
        }
        catch
        {
            return data; // fallback to pass-through on decode failure
        }
    }
}
