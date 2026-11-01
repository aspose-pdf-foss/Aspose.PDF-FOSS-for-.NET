using System.Text;

namespace Aspose.Pdf.Tests.Helpers;

/// <summary>
/// A minimal Type 1 font made from nothing (Adobe Type 1 Font Format): one glyph, "A",
/// a 500 by 700 box on a 600 advance, behind eexec and charstring encryption, written as
/// a PFB, with an AFM that states the same metrics.
/// </summary>
internal static class Type1FontBuilder
{
    public const string FontName = "TestOne";
    public const int AWidth = 600;
    public const int SpaceWidth = 250;

    private const ushort EexecKey = 55665;
    private const ushort CharStringKey = 4330;

    /// <summary>The random bytes before the plain text of the eexec section, always four.</summary>
    private const int EexecSalt = 4;

    /// <summary>The usual count of random bytes before a charstring.</summary>
    public const int DefaultLenIV = 4;

    public static string Afm =>
        "StartFontMetrics 4.1\n" +
        $"FontName {FontName}\n" +
        "EncodingScheme AdobeStandardEncoding\n" +
        "FontBBox 0 0 600 700\n" +
        "Ascender 700\nDescender 0\nCapHeight 700\nStdVW 50\n" +
        "StartCharMetrics 2\n" +
        $"C 32 ; WX {SpaceWidth} ; N space ; B 0 0 0 0 ;\n" +
        $"C 65 ; WX {AWidth} ; N A ; B 50 0 550 700 ;\n" +
        "EndCharMetrics\nEndFontMetrics\n";

    /// <summary>The font as a PFB. Given <paramref name="subrsCount"/>, the glyph's charstring moves into subroutine 0,
    /// which the glyph calls, and the Subrs array's count is written as that text. <paramref name="lenIV"/> is the
    /// count of random bytes before each charstring and subroutine; -1 writes them as they are, not encrypted.</summary>
    public static byte[] Pfb(string? subrsCount = null, int lenIV = DefaultLenIV)
    {
        var clear = Encoding.ASCII.GetBytes(
            "%!PS-AdobeFont-1.0: TestOne 001\n" +
            "11 dict begin\n" +
            $"/FontName /{FontName} def\n" +
            "/Encoding StandardEncoding def\n" +
            "/PaintType 0 def\n/FontType 1 def\n" +
            "/FontMatrix [0.001 0 0 0.001 0 0] readonly def\n" +
            "/FontBBox {0 0 600 700} readonly def\n" +
            "currentdict end\ncurrentfile eexec\n");

        var notdef = new byte[] { 139, 139, 13, 14 };                       // 0 0 hsbw endchar
        var a = new byte[]
        {
            139, 248, 236, 13,                                              // 0 600 hsbw
            189, 139, 21,                                                   // 50 0 rmoveto
            248, 136, 139, 5,                                               // 500 0 rlineto
            139, 249, 80, 5,                                                // 0 700 rlineto
            252, 136, 139, 5,                                               // -500 0 rlineto
            9, 14,                                                          // closepath endchar
        };

        var priv = new List<byte>();
        void Text(string s) => priv.AddRange(Encoding.ASCII.GetBytes(s));
        byte[] Encrypted(byte[] cs) => lenIV < 0 ? cs : Encrypt(new byte[lenIV].Concat(cs).ToArray(), CharStringKey);
        void Glyph(string name, byte[] cs)
        {
            var encrypted = Encrypted(cs);
            Text($"/{name} {encrypted.Length} RD ");
            priv.AddRange(encrypted);
            Text(" ND\n");
        }
        Text("dup /Private 8 dict dup begin\n" +
             "/RD {string currentfile exch readstring pop} executeonly def\n" +
             "/ND {noaccess def} executeonly def\n" +
             "/NP {noaccess put} executeonly def\n" +
             $"/lenIV {lenIV} def\n/password 5839 def\n/MinFeature {{16 16}} def\n");
        if (subrsCount is null) Text("/Subrs 0 array\nND\n");
        else
        {
            var subr = Encrypted(a.Take(a.Length - 1).Append((byte)11).ToArray());   // ... closepath return
            Text($"/Subrs {subrsCount} array\ndup 0 {subr.Length} RD ");
            priv.AddRange(subr);
            Text(" NP\nND\n");
            a = new byte[] { 139, 10, 14 };                                 // 0 callsubr endchar
        }
        Text("2 index /CharStrings 2 dict dup begin\n");
        Glyph(".notdef", notdef);
        Glyph("A", a);
        Text("end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n");
        var binary = Encrypt(new byte[EexecSalt].Concat(priv).ToArray(), EexecKey);

        var trailer = new StringBuilder();
        for (var line = 0; line < 8; line++) trailer.Append(new string('0', 64)).Append('\n');
        trailer.Append("cleartomark\n");

        var pfb = new List<byte>();
        Segment(pfb, 1, clear);
        Segment(pfb, 2, binary);
        Segment(pfb, 1, Encoding.ASCII.GetBytes(trailer.ToString()));
        pfb.Add(0x80);
        pfb.Add(3);
        return pfb.ToArray();
    }

    private static void Segment(List<byte> pfb, byte kind, byte[] data)
    {
        pfb.Add(0x80);
        pfb.Add(kind);
        pfb.AddRange(BitConverter.GetBytes(data.Length));
        pfb.AddRange(data);
    }

    private static byte[] Encrypt(byte[] plain, ushort key)
    {
        const int c1 = 52845, c2 = 22719;
        var r = key;
        var cipher = new byte[plain.Length];
        for (var i = 0; i < plain.Length; i++)
        {
            cipher[i] = (byte)(plain[i] ^ (r >> 8));
            r = (ushort)((cipher[i] + r) * c1 + c2);
        }
        return cipher;
    }
}
