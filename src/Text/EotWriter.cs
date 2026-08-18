using System;
using System.IO;
using System.Text;

namespace Aspose.Pdf.Text;

/// <summary>
/// Wraps a TrueType sfnt into an Embedded OpenType (EOT) container
/// (version 0x00020001, uncompressed font data, no root strings). Header metrics
/// (PANOSE, weight, fsType, unicode/codepage ranges, checksum adjustment) are read
/// from the sfnt's OS/2 and head tables when present and left zeroed otherwise —
/// consumers treat zeroed ranges as "unrestricted".
/// </summary>
internal static class EotWriter
{
    public static byte[] Wrap(byte[] ttf, string familyName)
    {
        var panose = new byte[10];
        byte italic = 0;
        uint weight = 400;
        ushort fsType = 0;
        var unicodeRanges = new uint[4];
        var codePageRanges = new uint[2];
        uint checkSumAdjustment = 0;
        try
        {
            if (FindTable(ttf, "OS/2") is var (os2Off, os2Len) && os2Len >= 42 + 16)
            {
                weight = ReadU16(ttf, os2Off + 4);
                fsType = (ushort)ReadU16(ttf, os2Off + 8);
                Array.Copy(ttf, os2Off + 32, panose, 0, 10);
                for (var r = 0; r < 4; r++) unicodeRanges[r] = ReadU32(ttf, os2Off + 42 + 4 * r);
                if (os2Len >= 78 + 8)
                    for (var r = 0; r < 2; r++) codePageRanges[r] = ReadU32(ttf, os2Off + 78 + 4 * r);
                var fsSelection = ReadU16(ttf, os2Off + 62);
                italic = (fsSelection & 0x01) != 0 ? (byte)1 : (byte)0;
            }
            if (FindTable(ttf, "head") is var (headOff, headLen) && headLen >= 12)
                checkSumAdjustment = ReadU32(ttf, headOff + 8);
        }
        catch { /* zeroed header fields are valid */ }

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(0u);                          // EOTSize (patched below)
        w.Write((uint)ttf.Length);            // FontDataSize
        w.Write(0x00020001u);                 // Version
        w.Write(0u);                          // Flags (TTEMBED_SUBSET off, plain data)
        w.Write(panose);
        w.Write((byte)0x01);                  // Charset (DEFAULT_CHARSET)
        w.Write(italic);
        w.Write(weight);
        w.Write(fsType);
        w.Write((ushort)0x504C);              // MagicNumber
        foreach (var r in unicodeRanges) w.Write(r);
        foreach (var r in codePageRanges) w.Write(r);
        w.Write(checkSumAdjustment);
        w.Write(0u); w.Write(0u); w.Write(0u); w.Write(0u);   // Reserved1..4
        WriteName(w, familyName);             // Padding1 + FamilyName
        WriteName(w, "Regular");              // Padding2 + StyleName
        WriteName(w, "Version 1.0");          // Padding3 + VersionName
        WriteName(w, familyName);             // Padding4 + FullName
        w.Write((ushort)0);                   // Padding5
        w.Write((ushort)0);                   // RootStringSize (none)
        w.Write(ttf);
        w.Flush();

        var eot = ms.ToArray();
        var size = (uint)eot.Length;
        eot[0] = (byte)size; eot[1] = (byte)(size >> 8);
        eot[2] = (byte)(size >> 16); eot[3] = (byte)(size >> 24);
        return eot;
    }

    private static void WriteName(BinaryWriter w, string value)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        w.Write((ushort)0);                   // padding before each name
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }

    private static (int Offset, int Length)? FindTable(byte[] sfnt, string tag)
    {
        if (sfnt.Length < 12) return null;
        int numTables = (sfnt[4] << 8) | sfnt[5];
        for (var t = 0; t < numTables; t++)
        {
            var rec = 12 + 16 * t;
            if (rec + 16 > sfnt.Length) return null;
            var recTag = Encoding.ASCII.GetString(sfnt, rec, 4);
            if (recTag != tag) continue;
            var off = (int)ReadU32Be(sfnt, rec + 8);
            var len = (int)ReadU32Be(sfnt, rec + 12);
            return off >= 0 && len >= 0 && off + len <= sfnt.Length ? (off, len) : null;
        }
        return null;
    }

    // sfnt tables are big-endian.
    private static uint ReadU16(byte[] b, int off) => (uint)((b[off] << 8) | b[off + 1]);
    private static uint ReadU32(byte[] b, int off) => ReadU32Be(b, off);
    private static uint ReadU32Be(byte[] b, int off) =>
        (uint)((b[off] << 24) | (b[off + 1] << 16) | (b[off + 2] << 8) | b[off + 3]);
}
