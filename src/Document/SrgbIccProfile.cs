using System.Text;

namespace Aspose.Pdf;

/// <summary>
/// The sRGB colour profile a PDF/A output intent embeds as its DestOutputProfile (ISO 19005-1 6.2.2:
/// the intent's profile is in the file, not just named). It is built here from the numbers IEC 61966-2-1
/// defines - the primaries adapted to the profile connection space's D50 white, and the transfer curve -
/// as a version 2 matrix/curve display profile (ICC.1:2001-04), the form every PDF/A part accepts.
/// </summary>
internal static class SrgbIccProfile
{
    /// <summary>Number of colour components, the profile stream's /N.</summary>
    internal const int Components = 3;

    internal const string ConditionIdentifier = "sRGB IEC61966-2.1";

    // ICC.1:2001-04 layout: a 128-byte header, a tag table of 12-byte entries, 4-byte aligned tag data.
    private const int HeaderSize = 128;
    private const int TagEntrySize = 12;
    private const int TagAlignment = 4;
    private const int TypeHeaderSize = 8;
    private const int VersionOffset = 8;
    private const uint Version21 = 0x02100000;
    private const int DeviceClassOffset = 12;
    private const int ColourSpaceOffset = 16;
    private const int ConnectionSpaceOffset = 20;
    private const int DateOffset = 24;
    private const int FixedYear = 2000;
    private const int SignatureOffset = 36;
    private const int IlluminantOffset = 68;
    private const int ScriptCodePartSize = 70;
    // One sample per 8-bit code: the profile travels in every converted file, and a finer
    // table adds bytes that no reader of 8-bit device colour can tell apart.
    private const int CurveSamples = 256;
    private const double FixedOne = 65536.0;

    // IEC 61966-2-1 transfer curve: linear below the knee, a 2.4 power above it.
    private const double CurveKnee = 0.04045;
    private const double CurveLinearDivisor = 12.92;
    private const double CurveOffset = 0.055;
    private const double CurveGamma = 2.4;

    // The sRGB primaries as XYZ, chromatically adapted to D50 (the columns of the sRGB to
    // connection-space matrix); the media white is the display's own D65.
    private static readonly double[] Red = [0.4360747, 0.2225045, 0.0139322];
    private static readonly double[] Green = [0.3850649, 0.7168786, 0.0971045];
    private static readonly double[] Blue = [0.1430804, 0.0606169, 0.7141733];
    private static readonly double[] MediaWhite = [0.9504559, 1.0, 1.0890578];
    private static readonly double[] ConnectionWhite = [0.9642, 1.0, 0.8249];

    private static readonly Lazy<byte[]> Profile = new(Build);

    /// <summary>The profile's bytes. The array is shared: callers must not write to it.</summary>
    internal static byte[] Bytes => Profile.Value;

    private static byte[] Build()
    {
        var curve = CurveTag();
        var tags = new List<(string Signature, byte[] Data)>
        {
            ("desc", DescriptionTag(ConditionIdentifier)),
            ("cprt", TextTag("No copyright, use freely")),
            ("wtpt", XyzTag(MediaWhite)),
            ("rXYZ", XyzTag(Red)),
            ("gXYZ", XyzTag(Green)),
            ("bXYZ", XyzTag(Blue)),
            ("rTRC", curve),
            ("gTRC", curve),
            ("bTRC", curve),
        };

        // The three channels share one curve: its data is placed once and named three times.
        var table = new byte[sizeof(uint) + tags.Count * TagEntrySize];
        PutUInt32(table, 0, (uint)tags.Count);
        var dataStart = HeaderSize + table.Length;
        var body = new List<byte>();
        var placed = new Dictionary<byte[], int>();
        for (var i = 0; i < tags.Count; i++)
        {
            var (signature, data) = tags[i];
            if (!placed.TryGetValue(data, out var offset))
            {
                offset = dataStart + body.Count;
                placed[data] = offset;
                body.AddRange(data);
                while (body.Count % TagAlignment != 0) body.Add(0);
            }
            var entry = sizeof(uint) + i * TagEntrySize;
            PutAscii(table, entry, signature);
            PutUInt32(table, entry + sizeof(uint), (uint)offset);
            PutUInt32(table, entry + 2 * sizeof(uint), (uint)data.Length);
        }

        var profile = new byte[dataStart + body.Count];
        PutUInt32(profile, 0, (uint)profile.Length);
        PutUInt32(profile, VersionOffset, Version21);
        PutAscii(profile, DeviceClassOffset, "mntr");
        PutAscii(profile, ColourSpaceOffset, "RGB ");
        PutAscii(profile, ConnectionSpaceOffset, "XYZ ");
        // a fixed date (1 January 2000), so the same document always converts to the same bytes
        PutUInt16(profile, DateOffset, FixedYear);
        PutUInt16(profile, DateOffset + sizeof(ushort), 1);
        PutUInt16(profile, DateOffset + 2 * sizeof(ushort), 1);
        PutAscii(profile, SignatureOffset, "acsp");
        PutXyz(profile, IlluminantOffset, ConnectionWhite);
        table.CopyTo(profile, HeaderSize);
        body.CopyTo(profile, dataStart);
        return profile;
    }

    private static byte[] XyzTag(double[] xyz)
    {
        var tag = new byte[TypeHeaderSize + xyz.Length * sizeof(int)];
        PutAscii(tag, 0, "XYZ ");
        PutXyz(tag, TypeHeaderSize, xyz);
        return tag;
    }

    private static byte[] CurveTag()
    {
        var tag = new byte[TypeHeaderSize + sizeof(uint) + CurveSamples * sizeof(ushort)];
        PutAscii(tag, 0, "curv");
        PutUInt32(tag, TypeHeaderSize, CurveSamples);
        for (var i = 0; i < CurveSamples; i++)
        {
            var encoded = i / (double)(CurveSamples - 1);
            var linear = encoded <= CurveKnee
                ? encoded / CurveLinearDivisor
                : Math.Pow((encoded + CurveOffset) / (1 + CurveOffset), CurveGamma);
            PutUInt16(tag, TypeHeaderSize + sizeof(uint) + i * sizeof(ushort), (int)Math.Round(linear * ushort.MaxValue));
        }
        return tag;
    }

    /// <summary>A version 2 description: the ASCII name with its terminating zero, then empty Unicode and script-code parts.</summary>
    private static byte[] DescriptionTag(string text)
    {
        var terminated = text.Length + 1;
        var tag = new byte[TypeHeaderSize + sizeof(uint) + terminated + 2 * sizeof(uint) + ScriptCodePartSize];
        PutAscii(tag, 0, "desc");
        PutUInt32(tag, TypeHeaderSize, (uint)terminated);
        PutAscii(tag, TypeHeaderSize + sizeof(uint), text);
        return tag;
    }

    /// <summary>A text tag: ASCII with its terminating zero.</summary>
    private static byte[] TextTag(string text)
    {
        var tag = new byte[TypeHeaderSize + text.Length + 1];
        PutAscii(tag, 0, "text");
        PutAscii(tag, TypeHeaderSize, text);
        return tag;
    }

    private static void PutXyz(byte[] target, int at, double[] xyz)
    {
        for (var i = 0; i < xyz.Length; i++)
            PutUInt32(target, at + i * sizeof(int), unchecked((uint)(int)Math.Round(xyz[i] * FixedOne)));
    }

    private static void PutAscii(byte[] target, int at, string text) =>
        Encoding.ASCII.GetBytes(text, 0, text.Length, target, at);

    private static void PutUInt32(byte[] target, int at, uint value)
    {
        target[at] = (byte)(value >> 24);
        target[at + 1] = (byte)(value >> 16);
        target[at + 2] = (byte)(value >> 8);
        target[at + 3] = (byte)value;
    }

    private static void PutUInt16(byte[] target, int at, int value)
    {
        target[at] = (byte)(value >> 8);
        target[at + 1] = (byte)value;
    }
}
