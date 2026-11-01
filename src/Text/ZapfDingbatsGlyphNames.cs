namespace Aspose.Pdf.Text;

/// <summary>The glyph names of the ZapfDingbats face's built-in encoding (PDF 32000 Annex D.6):
/// every glyph is "a" and a number, and the numbers do not follow the codes. An encoding that
/// names a code (/Differences [1 /a35]) is resolved through here to the code the face itself
/// gives that glyph, which the code tables then turn into a character.</summary>
internal static class ZapfDingbatsGlyphNames
{
    private const int SpaceCode = 0x20;

    /// <summary>The runs of codes the built-in encoding defines, each with the numbers of its
    /// glyph names in code order.</summary>
    private static readonly (int FirstCode, int[] Numbers)[] Runs =
    {
        (0x21, new[]
        {
            1, 2, 202, 3, 4, 5, 119, 118, 117, 11, 12, 13, 14, 15, 16,
            105, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 6, 7, 8,
            9, 10, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42,
            43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58,
            59, 60, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74,
            203, 75, 204, 76, 77, 78, 79, 81, 82, 83, 84, 97, 98, 99, 100,
        }),
        (0x80, new[] { 89, 90, 93, 94, 91, 92, 205, 85, 206, 86, 87, 88, 95, 96 }),
        (0xA1, new[]
        {
            101, 102, 103, 104, 106, 107, 108, 112, 111, 110, 109, 120, 121, 122, 123,
            124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139,
            140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155,
            156, 157, 158, 159, 160, 161, 163, 164, 196, 165, 192, 166, 167, 168, 169, 170,
            171, 172, 173, 162, 174, 175, 176, 177, 178, 179, 193, 180, 199, 181, 200, 182,
        }),
        (0xF1, new[] { 201, 183, 184, 197, 185, 194, 198, 186, 195, 187, 188, 189, 190, 191 }),
    };

    private static readonly Dictionary<string, byte> CodeByName = BuildCodeByName();

    private static Dictionary<string, byte> BuildCodeByName()
    {
        var map = new Dictionary<string, byte>(StringComparer.Ordinal) { ["space"] = SpaceCode };
        foreach (var (firstCode, numbers) in Runs)
        {
            for (var i = 0; i < numbers.Length; i++)
                map["a" + numbers[i].ToString(System.Globalization.CultureInfo.InvariantCulture)] = (byte)(firstCode + i);
        }
        return map;
    }

    /// <summary>The code the face's built-in encoding gives a glyph name, or 0 when the face has
    /// no glyph of that name.</summary>
    public static byte StandardCode(string glyphName) => CodeByName.TryGetValue(glyphName, out var code) ? code : (byte)0;
}
