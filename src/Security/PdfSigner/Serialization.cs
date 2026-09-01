using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Security;

public sealed partial class PdfSigner
{
    /// <summary>
    /// Serialize an indirect object, tracking the position and length of the /Contents hex string.
    /// </summary>
    private static byte[] SerializeObject(int objNum, PdfDictionary dict, int contentsSize,
        out long contentsOffset, out long contentsLength)
    {
        contentsOffset = 0;
        contentsLength = 0;

        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write($"{objNum} 0 obj\n");
        Write("<< ");
        foreach (var key in dict.Keys)
        {
            Write($"/{key} ");
            var val = dict.Get(key)!;

            if (key == "Contents")
            {
                // Track the hex string position
                contentsOffset = ms.Position;
                var hexStr = new string('0', contentsSize * 2);
                Write($"<{hexStr}>");
                contentsLength = ms.Position - contentsOffset;
            }
            else
            {
                SerializeValue(ms, val);
            }
            Write(" ");
        }
        Write(">>\nendobj\n");

        return ms.ToArray();
    }

    private static void SerializeValue(MemoryStream ms, PdfObject val)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        switch (val)
        {
            case PdfNull:
                Write("null");
                break;
            case PdfBoolean b:
                Write(b.Value ? "true" : "false");
                break;
            case PdfInteger i:
                Write(i.Value.ToString());
                break;
            case PdfReal r:
                Write(r.Value.ToString("G"));
                break;
            case PdfString s when s.IsHex:
                Write($"<{Convert.ToHexString(s.Value)}>");
                break;
            case PdfString s:
                Write("(");
                foreach (var c in s.Value)
                {
                    if (c is (byte)'(' or (byte)')' or (byte)'\\')
                        ms.WriteByte((byte)'\\');
                    ms.WriteByte(c);
                }
                Write(")");
                break;
            case PdfName n:
                Write($"/{n.Value}");
                break;
            case PdfArray arr:
                Write("[");
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) Write(" ");
                    SerializeValue(ms, arr[i]);
                }
                Write("]");
                break;
            case PdfDictionary d:
                Write("<< ");
                foreach (var key in d.Keys)
                {
                    Write($"/{key} ");
                    SerializeValue(ms, d.Get(key)!);
                    Write(" ");
                }
                Write(">>");
                break;
            case PdfIndirectRef iref:
                Write($"{iref.ObjectNumber} {iref.Generation} R");
                break;
        }
    }

    /// <summary>zlib-deflate a stream payload for an appended object.</summary>
    private static byte[] DeflateBytes(byte[] data)
    {
        using var buf = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(
                   buf, System.IO.Compression.CompressionMode.Compress, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return buf.ToArray();
    }

    private static void WriteIndirectObject(MemoryStream ms, int objNum, PdfObject obj)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        Write($"{objNum} 0 obj\n");
        SerializeValue(ms, obj);
        Write("\nendobj\n");
    }

    private static void WriteXRefAndTrailer(MemoryStream ms, Dictionary<int, long> offsets,
        PdfDictionary originalTrailer, int nextObjNum, long originalStartXref)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        var xrefOffset = ms.Position;

        // Group consecutive object numbers into subsections
        var sortedNums = offsets.Keys.OrderBy(k => k).ToList();
        Write("xref\n");

        var i = 0;
        while (i < sortedNums.Count)
        {
            var start = sortedNums[i];
            var count = 1;
            while (i + count < sortedNums.Count && sortedNums[i + count] == start + count)
                count++;

            Write($"{start} {count}\n");
            for (var j = 0; j < count; j++)
            {
                Write($"{offsets[sortedNums[i + j]]:D10} 00000 n \n");
            }
            i += count;
        }

        // Trailer
        var newTrailer = new PdfDictionary();
        foreach (var key in new[] { "Root", "Info", "Encrypt", "ID" })
        {
            var val = originalTrailer.Get(key);
            if (val is not null) newTrailer.Set(key, val);
        }
        newTrailer.Set("Size", new PdfInteger(nextObjNum));
        newTrailer.Set("Prev", new PdfInteger(originalStartXref));

        Write("trailer\n");
        SerializeValue(ms, newTrailer);
        Write($"\nstartxref\n{xrefOffset}\n%%EOF\n");
    }

    private static void PatchByteRange(byte[] fileBytes, long[] byteRange)
    {
        // Find the ByteRange placeholder pattern: [0 9999999999 9999999999 9999999999]
        var placeholder = Encoding.ASCII.GetBytes("[0 9999999999 9999999999 9999999999]");
        var replacement = $"[{byteRange[0]} {byteRange[1]} {byteRange[2]} {byteRange[3]}]";
        // Pad replacement to same length as placeholder
        replacement = replacement.PadRight(placeholder.Length);
        var replacementBytes = Encoding.ASCII.GetBytes(replacement);

        var idx = FindBytes(fileBytes, placeholder);
        if (idx < 0)
            throw new InvalidOperationException("Could not find ByteRange placeholder in signed PDF.");

        Array.Copy(replacementBytes, 0, fileBytes, idx, replacementBytes.Length);
    }

    private static int FindBytes(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    private static PdfDictionary CloneDict(PdfDictionary source)
    {
        var clone = new PdfDictionary();
        foreach (var key in source.Keys)
        {
            var val = source.Get(key);
            if (val is not null) clone.Set(key, val);
        }
        return clone;
    }

    private static string EscapePdfString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    /// <summary>
    /// Encode a text string as a PdfString, using UTF-16BE with BOM if it contains
    /// non-Latin-1 characters, otherwise Latin-1.
    /// </summary>
    private static PdfString EncodePdfText(string text)
    {
        // Check if all characters fit in Latin-1 (0x00-0xFF)
        var needsUnicode = false;
        foreach (var ch in text)
        {
            if (ch > 0xFF) { needsUnicode = true; break; }
        }

        if (!needsUnicode)
            return new PdfString(Encoding.Latin1.GetBytes(text));

        // UTF-16BE with BOM (0xFE 0xFF)
        var utf16 = Encoding.BigEndianUnicode.GetBytes(text);
        var withBom = new byte[2 + utf16.Length];
        withBom[0] = 0xFE;
        withBom[1] = 0xFF;
        Array.Copy(utf16, 0, withBom, 2, utf16.Length);
        return new PdfString(withBom);
    }

    /// <summary>Encrypt the string values (recursively) of an object being
    /// appended to an already-encrypted document, using the document's per-object
    /// key. The signature CMS envelope (/Contents) is exempt (ISO 32000-1
    /// §7.6.1); the "__StreamData" marker is handled by
    /// <see cref="EncryptAppendedStream"/>.</summary>
    private static void EncryptAppendedDict(PdfDecryptor dec, int objNum, PdfDictionary dict)
    {
        foreach (var key in new List<string>(dict.Keys))
        {
            if (key is "Contents" or "__StreamData") continue;
            switch (dict.Get(key))
            {
                case PdfString s:
                    dict.Set(key, new PdfString(dec.EncryptString(s.Value, objNum, 0), isHex: true));
                    break;
                case PdfDictionary sub: // inline sub-dictionary: same object
                    EncryptAppendedDict(dec, objNum, sub);
                    break;
                case PdfArray arr:
                    EncryptAppendedArray(dec, objNum, arr);
                    break;
            }
        }
    }

    private static void EncryptAppendedArray(PdfDecryptor dec, int objNum, PdfArray arr)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            switch (arr[i])
            {
                case PdfString s:
                    arr.ReplaceAt(i, new PdfString(dec.EncryptString(s.Value, objNum, 0), isHex: true));
                    break;
                case PdfDictionary sub: EncryptAppendedDict(dec, objNum, sub); break;
                case PdfArray sa: EncryptAppendedArray(dec, objNum, sa); break;
            }
        }
    }

    /// <summary>Encrypt an appended stream object: its raw stream data (stored
    /// under "__StreamData") and its dictionary strings.</summary>
    private static void EncryptAppendedStream(PdfDecryptor dec, int objNum, PdfDictionary streamDict)
    {
        if (streamDict.Get("__StreamData") is PdfString sd)
        {
            var enc = dec.EncryptStream(sd.Value, objNum, 0);
            streamDict.Set("__StreamData", new PdfString(enc));
            // /Length counts the bytes actually WRITTEN, and an AES cipher writes more
            // than it was given (a 16-byte IV, then padding to the block). Left at the
            // plaintext length the stream reads back truncated, and a reader that cannot
            // decode it simply draws nothing.
            streamDict.Set("Length", new PdfInteger(enc.Length));
        }
        EncryptAppendedDict(dec, objNum, streamDict);
    }

    private static void WriteStreamObject(MemoryStream ms, int objNum, PdfDictionary dict)
    {
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        // Extract stream data
        var streamDataObj = dict.Get("__StreamData") as PdfString;
        var streamData = streamDataObj?.Value ?? [];

        Write($"{objNum} 0 obj\n<< ");
        foreach (var key in dict.Keys)
        {
            if (key == "__StreamData") continue;
            Write($"/{key} ");
            SerializeValue(ms, dict.Get(key)!);
            Write(" ");
        }
        Write(">>\nstream\n");
        ms.Write(streamData);
        Write("\nendstream\nendobj\n");
    }
}
