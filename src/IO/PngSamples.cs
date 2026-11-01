namespace Aspose.Pdf.IO;

/// <summary>
/// A PNG's inflated image data as plain rows of one byte per sample, whatever its bit depth and
/// interlace method: the form the managed PNG readers convert to RGB. 16-bit samples keep their
/// high byte; 1/2/4-bit grey samples are scaled to 0-255, while palette indices stay indices;
/// an Adam7 image's seven passes are unfiltered each with its own width and laid out on the grid.
/// </summary>
internal static class PngSamples
{
    private const int PassCount = 7;
    private const int SixteenBits = 16;
    private const int EightBits = 8;
    private const int ColorTypeIndexed = 3;

    // Adam7 (PNG §8.2): where each pass starts on the 8x8 grid and how far apart its pixels lie.
    private static readonly int[] PassStartX = { 0, 4, 0, 2, 0, 1, 0 };
    private static readonly int[] PassStartY = { 0, 0, 4, 0, 2, 0, 1 };
    private static readonly int[] PassStepX = { 8, 8, 4, 4, 2, 2, 1 };
    private static readonly int[] PassStepY = { 8, 8, 8, 4, 4, 2, 2 };

    /// <summary>Samples per pixel of a PNG colour type.</summary>
    internal static int Channels(int colorType) => colorType switch
    {
        0 => 1, // Grayscale
        2 => 3, // RGB
        3 => 1, // Palette index
        4 => 2, // Grayscale + Alpha
        6 => 4, // RGBA
        _ => 3,
    };

    /// <summary>True when the readers' own row loop takes the data as it is: a non-interlaced image of
    /// 8-bit samples, or of palette indices, which they unpack at any depth.</summary>
    internal static bool ReadersTakeAsIs(int bitDepth, int colorType, bool interlaced) =>
        !interlaced && (bitDepth == EightBits || colorType == ColorTypeIndexed);

    /// <summary>The image as width x height pixels of <see cref="Channels"/> bytes each, top row first.
    /// Data that ends early leaves the rest of the image zero.</summary>
    internal static byte[] ToBytePerSample(byte[] data, int width, int height, int bitDepth, int colorType, bool interlaced)
    {
        var output = new byte[(long)width * height * Channels(colorType)];
        var pass = new PassLayout(width, bitDepth, colorType, output);
        if (!interlaced)
        {
            ReadPass(data, 0, pass, 0, 0, 1, 1, width, height);
            return output;
        }
        var pos = 0;
        for (var p = 0; p < PassCount; p++)
        {
            var passWidth = (width - PassStartX[p] + PassStepX[p] - 1) / PassStepX[p];
            var passHeight = (height - PassStartY[p] + PassStepY[p] - 1) / PassStepY[p];
            if (passWidth <= 0 || passHeight <= 0)
                continue; // an empty pass has no rows, not even filter bytes
            pos = ReadPass(data, pos, pass, PassStartX[p], PassStartY[p], PassStepX[p], PassStepY[p], passWidth, passHeight);
        }
        return output;
    }

    /// <summary>The image-wide facts every pass reads its rows with.</summary>
    private sealed class PassLayout
    {
        public PassLayout(int width, int bitDepth, int colorType, byte[] output)
        {
            Width = width;
            BitDepth = bitDepth;
            Channels = PngSamples.Channels(colorType);
            ScaleToByte = colorType != ColorTypeIndexed && bitDepth < EightBits;
            Output = output;
        }

        public int Width { get; }
        public int BitDepth { get; }
        public int Channels { get; }
        public bool ScaleToByte { get; }
        public byte[] Output { get; }
    }

    // Unfilters one pass (the whole image when not interlaced) and spreads its samples onto the grid.
    // Returns the position after the pass's rows.
    private static int ReadPass(byte[] data, int pos, PassLayout layout, int x0, int y0, int dx, int dy,
        int passWidth, int passHeight)
    {
        var bitsPerPixel = layout.Channels * layout.BitDepth;
        var stride = (passWidth * bitsPerPixel + 7) / 8;
        // The filters' left neighbour is one whole pixel back, or one byte for sub-byte pixels (§9.2).
        var filterBpp = Math.Max(1, bitsPerPixel / 8);
        var prev = new byte[stride];
        var cur = new byte[stride];
        var maxValue = (1 << layout.BitDepth) - 1;
        for (var row = 0; row < passHeight; row++)
        {
            if (pos >= data.Length)
                return pos;
            var filter = data[pos++];
            var available = Math.Max(0, Math.Min(stride, data.Length - pos));
            Array.Copy(data, pos, cur, 0, available);
            Array.Clear(cur, available, stride - available);
            pos += stride;
            Unfilter(filter, cur, prev, filterBpp);

            var y = y0 + row * dy;
            for (var px = 0; px < passWidth; px++)
            {
                var at = ((y * layout.Width) + x0 + px * dx) * layout.Channels;
                for (var c = 0; c < layout.Channels; c++)
                {
                    var sample = px * layout.Channels + c;
                    layout.Output[at + c] = layout.BitDepth switch
                    {
                        EightBits => cur[sample],
                        SixteenBits => cur[sample * 2],
                        _ => SubByteSample(cur, sample, layout.BitDepth, maxValue, layout.ScaleToByte),
                    };
                }
            }
            (prev, cur) = (cur, prev);
        }
        return pos;
    }

    // A 1/2/4-bit sample, packed most significant bits first.
    private static byte SubByteSample(byte[] row, int sample, int bitDepth, int maxValue, bool scaleToByte)
    {
        var bit = sample * bitDepth;
        var value = (row[bit >> 3] >> (EightBits - bitDepth - (bit & 7))) & maxValue;
        return scaleToByte ? (byte)(value * byte.MaxValue / maxValue) : (byte)value;
    }

    private static void Unfilter(byte filter, byte[] row, byte[] prev, int bpp)
    {
        var n = row.Length;
        switch (filter)
        {
            case 1: // Sub
                for (var x = bpp; x < n; x++) row[x] = (byte)(row[x] + row[x - bpp]);
                break;
            case 2: // Up
                for (var x = 0; x < n; x++) row[x] = (byte)(row[x] + prev[x]);
                break;
            case 3: // Average
                for (var x = 0; x < n; x++)
                    row[x] = (byte)(row[x] + ((x >= bpp ? row[x - bpp] : 0) + prev[x]) / 2);
                break;
            case 4: // Paeth
                for (var x = 0; x < n; x++)
                {
                    int a = x >= bpp ? row[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
                    int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                    row[x] = (byte)(row[x] + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c));
                }
                break;
            default: // None
                break;
        }
    }
}
