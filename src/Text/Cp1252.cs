using System.Collections.Generic;

namespace Aspose.Pdf.Text;

/// <summary>
/// Managed Windows-1252 ("WinAnsi") single-byte codec. Replaces
/// <c>Encoding.GetEncoding(1252)</c>, which on .NET requires the
/// <c>System.Text.Encoding.CodePages</c> provider package. Bytes 0x00-0x7F are
/// ASCII, 0xA0-0xFF match Latin-1, and 0x80-0x9F follow the Windows-1252 layout
/// (the five undefined slots 0x81/0x8D/0x8F/0x90/0x9D map to their matching C1
/// control codepoint so decode/encode round-trips).
/// </summary>
internal static class Cp1252
{
    private static readonly char[] ToCharTable = BuildToChar();
    private static readonly Dictionary<char, byte> ToByteTable = BuildToByte();

    private static char[] BuildToChar()
    {
        var t = new char[256];
        for (var i = 0; i < 256; i++) t[i] = (char)i; // ASCII / C1 / Latin-1 supplement
        t[0x80] = '€'; t[0x82] = '‚'; t[0x83] = 'ƒ'; t[0x84] = '„';
        t[0x85] = '…'; t[0x86] = '†'; t[0x87] = '‡'; t[0x88] = 'ˆ';
        t[0x89] = '‰'; t[0x8A] = 'Š'; t[0x8B] = '‹'; t[0x8C] = 'Œ';
        t[0x8E] = 'Ž'; t[0x91] = '‘'; t[0x92] = '’'; t[0x93] = '“';
        t[0x94] = '”'; t[0x95] = '•'; t[0x96] = '–'; t[0x97] = '—';
        t[0x98] = '˜'; t[0x99] = '™'; t[0x9A] = 'š'; t[0x9B] = '›';
        t[0x9C] = 'œ'; t[0x9E] = 'ž'; t[0x9F] = 'Ÿ';
        return t;
    }

    private static Dictionary<char, byte> BuildToByte()
    {
        var d = new Dictionary<char, byte>(256);
        for (var i = 0; i < 256; i++) d[ToCharTable[i]] = (byte)i;
        return d;
    }

    /// <summary>Try to encode a single character to its Windows-1252 byte.</summary>
    public static bool TryGetByte(char ch, out byte b) => ToByteTable.TryGetValue(ch, out b);

    /// <summary>Encode a string; characters outside Windows-1252 become '?'.</summary>
    public static byte[] GetBytes(string s)
    {
        if (string.IsNullOrEmpty(s)) return System.Array.Empty<byte>();
        var bytes = new byte[s.Length];
        for (var i = 0; i < s.Length; i++)
            bytes[i] = ToByteTable.TryGetValue(s[i], out var b) ? b : (byte)'?';
        return bytes;
    }

    /// <summary>Decode Windows-1252 bytes to a string.</summary>
    public static string GetString(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        var chars = new char[bytes.Length];
        for (var i = 0; i < bytes.Length; i++) chars[i] = ToCharTable[bytes[i]];
        return new string(chars);
    }
}
