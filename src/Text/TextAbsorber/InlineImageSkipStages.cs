using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
    /// <summary>The stages of the inline-image skip: the dictionary scan, the flate data skip and the data-end search.</summary>
    private static bool FindInlineImageEnd(InlineImageSkipState im)
    {
        while (im.pos < im.len - 1)
        {
            if (im.lexer.ByteAt(im.pos) == (byte)'E' && im.lexer.ByteAt(im.pos + 1) == (byte)'I')
            {
                var after = im.pos + 2;
                if (after >= im.len || IsInlineImageWhitespace(im.lexer.ByteAt(after)))
                {
                    // Verify this is the real EI by checking that what follows
                    // looks like valid PDF operators (not random image data).
                    // A valid operator context after EI would be: Q, BT, numbers, /, etc.
                    if (after < im.len)
                    {
                        // Skip whitespace after EI
                        var checkPos = after;
                        while (checkPos < im.len && IsInlineImageWhitespace(im.lexer.ByteAt(checkPos)))
                            checkPos++;
                        if (checkPos < im.len)
                        {
                            var nextByte = im.lexer.ByteAt(checkPos);
                            // Valid PDF operator starts: letter, number, /, (, <, [
                            bool looksValid = (nextByte >= (byte)'A' && nextByte <= (byte)'Z')
                                || (nextByte >= (byte)'a' && nextByte <= (byte)'z')
                                || (nextByte >= (byte)'0' && nextByte <= (byte)'9')
                                || nextByte == (byte)'/' || nextByte == (byte)'('
                                || nextByte == (byte)'<' || nextByte == (byte)'['
                                || nextByte == (byte)'-' || nextByte == (byte)'.';
                            if (!looksValid) { im.pos++; continue; }
                        }
                    }
                    im.lexer.Position = after;
                    return false;
                }
            }
            im.pos++;
        }
        return true;
    }
    private static bool IsInlineImageWhitespace(byte b) =>
            b == 0x00 || b == 0x09 || b == 0x0A || b == 0x0C || b == 0x0D || b == 0x20;

    /// <summary></summary>
    private static bool SkipFlateInlineImageData(InlineImageSkipState im)
    {
        if (im.imgFlate && im.imgW > 0 && im.imgH > 0)
        {
            int bytesPerRow = (im.imgW * im.imgColors * im.imgBpc + 7) / 8;
            int expected = im.imgH * bytesPerRow; // lower bound (a row predictor only adds bytes)
            int tailLen = (int)Math.Max(0, im.lenAll - im.dataStart0);
            var tail = new byte[tailLen];
            for (int i = 0; i < tailLen; i++) tail[i] = im.lexer.ByteAt(im.dataStart0 + i);
            for (int p = 1; p < tailLen - 1; p++)
            {
                if (tail[p] != (byte)'E' || tail[p + 1] != (byte)'I') continue;
                if (p + 2 < tailLen && !IsInlineImageWhitespace(tail[p + 2])) continue;
                var slice = new byte[p];
                Array.Copy(tail, 0, slice, 0, p);
                try
                {
                    var inflated = Aspose.Pdf.IO.Filters.FlateDecodeFilter.Decode(slice, null);
                    if (inflated.Length >= expected) { im.lexer.Position = im.dataStart0 + p + 2; return false; }
                }
                catch { /* truncated deflate at this candidate — keep scanning */ }
            }
        }
        return true;
    }

    /// <summary></summary>
    private static bool ScanInlineImageDictionary(InlineImageSkipState im)
    {
        while (true)
        {
            var t = im.lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return false;
            if (t.Kind == TokenKind.Keyword && t.StringValue == "ID") break;
            if (t.Kind == TokenKind.Name)
            {
                var n = t.StringValue!;
                if (im.key is "F" or "Filter" && im.firstFilter is null) im.firstFilter = n;
                switch (n)
                {
                    case "RGB": case "DeviceRGB": if (im.key is "CS" or "ColorSpace") im.imgColors = 3; break;
                    case "CMYK": case "DeviceCMYK": if (im.key is "CS" or "ColorSpace") im.imgColors = 4; break;
                    case "G": case "DeviceGray": if (im.key is "CS" or "ColorSpace") im.imgColors = 1; break;
                    case "Fl": case "FlateDecode": if (im.key is "F" or "Filter") im.imgFlate = true; break;
                }
                im.key = n;
            }
            else if (t.Kind == TokenKind.Integer)
            {
                int v = (int)t.IntValue;
                switch (im.key) { case "W": case "Width": im.imgW = v; break; case "H": case "Height": im.imgH = v; break;
                    case "BPC": case "BitsPerComponent": im.imgBpc = v; break; case "Colors": im.imgColors = v; break; }
                im.key = null;
            }
            // A filter array (/F [/A85 /Fl]) keeps the key alive so its first element
            // is still attributed to F/Filter.
            else if (t.Kind != TokenKind.ArrayStart) im.key = null;
        }
        return true;
    }
}
