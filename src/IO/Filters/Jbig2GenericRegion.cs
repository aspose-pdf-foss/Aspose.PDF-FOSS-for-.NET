using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    /// <summary>
    /// Decodes an arithmetic-coded generic region. Holds its own context array so
    /// repeated calls on the same instance accumulate state (matches the symbol-
    /// dictionary requirement of preserving GBSTATS across symbols).
    /// </summary>
    private sealed class GenericRegionDecoder
    {
        private readonly ArithmeticDecoder _ad;
        private readonly int _template;
        private readonly ArithmeticContext[] _ctx;
        private readonly bool _tpgdon;
        // The template's pixels, adaptive ones included, in the order T.88 forms a context
        // from them (row by row, left to right, most significant first). Stepping one column
        // right, a pixel whose right neighbour is the template's next entry takes that entry's
        // bit by a shift; only the others (one per template row, as a rule) are read again.
        private readonly (int dx, int dy)[] _pixels;
        private readonly int _reuseMask;
        private readonly int[] _fresh;
        // Template 0 with its nominal adaptive pixels - the common case - has a loop of its own.
        private readonly bool _nominal0;

        public GenericRegionDecoder(ArithmeticDecoder ad, int template, (int, int)[] at, bool tpgdon = false)
        {
            _ad = ad;
            _template = template;
            _tpgdon = tpgdon;
            (int dx, int dy)[] adaptive = at?.Length > 0 ? at.Select(p => (p.Item1, p.Item2)).ToArray() : DefaultAt(template);
            // 16-bit context for template 0; 13-bit for templates 1/2/3.
            var n = template == 0 ? 65536 : 8192;
            _ctx = new ArithmeticContext[n];
            _nominal0 = template == 0 && adaptive.Take(4).SequenceEqual(DefaultAt(0).Select(p => (p.Item1, p.Item2)));
            _pixels = NominalPixels(template).Concat(adaptive.Take(template == 0 ? 4 : 1))
                .OrderBy(p => p.dy).ThenBy(p => p.dx).ToArray();
            var fresh = new List<int>();
            for (var k = 0; k < _pixels.Length; k++)
            {
                var reusable = k + 1 < _pixels.Length && _pixels[k + 1] == (_pixels[k].dx + 1, _pixels[k].dy);
                if (reusable) _reuseMask |= 1 << (_pixels.Length - 2 - k);
                else fresh.Add(k);
            }
            _fresh = fresh.ToArray();
        }

        // Pseudo-pixel context used to decode the typical-prediction (LTP) bit per row
        // when TPGDON is set (T.88 §6.2.5.7). One fixed value per template.
        private static int SltpContext(int template) => template switch
        {
            0 => 0x9B25,
            1 => 0x0795,
            2 => 0x00E5,
            _ => 0x0195,
        };

        private static (int, int)[] DefaultAt(int template) => template switch
        {
            0 => new (int, int)[] { (3, -1), (-3, -1), (2, -2), (-2, -2) },
            1 => new (int, int)[] { (3, -1) },
            2 => new (int, int)[] { (2, -1) },
            _ => new (int, int)[] { (2, -1) },
        };

        // The fixed pixels of each template (T.88 Figures 4 to 7), without the adaptive ones.
        private static (int dx, int dy)[] NominalPixels(int template) => template switch
        {
            0 => new[] { (-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-4, 0), (-3, 0), (-2, 0), (-1, 0) },
            1 => new[] { (-1, -2), (0, -2), (1, -2), (2, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-3, 0), (-2, 0), (-1, 0) },
            _ => new[] { (-1, -2), (0, -2), (1, -2), (-2, -1), (-1, -1), (0, -1), (1, -1), (2, -1), (-2, 0), (-1, 0) },
        };

#pragma warning disable S3218 // the nested type's own member, called unqualified inside it on purpose
        public Jbig2Bitmap Decode(int width, int height)
#pragma warning restore S3218
        {
            var bm = new Jbig2Bitmap(width, height);
            if (_nominal0) { DecodeTemplate0Nominal(bm, width, height); return bm; }
            // Row-major scan with the JBIG2 template-0/1/2/3 contexts. Two prior rows are
            // accessed; out-of-bounds pixels read as 0 per the spec.
            var ltp = false;
            for (var y = 0; y < height; y++)
            {
                if (_tpgdon)
                {
                    // Typical-prediction: a per-row LTP bit toggles "this row equals the
                    // previous one". When set, copy row y-1 verbatim and skip pixel coding.
                    if (_ad.DecodeBit(ref _ctx[SltpContext(_template)])) ltp = !ltp;
                    if (ltp)
                    {
                        if (y > 0)
                            System.Array.Copy(bm.Data, (y - 1) * bm.RowBytes, bm.Data, y * bm.RowBytes, bm.RowBytes);
                        continue;
                    }
                }
                var ctx = 0;
                for (var x = 0; x < width; x++)
                {
                    ctx = x == 0 ? ContextAt(bm, x, y) : NextContext(bm, ctx, x, y);
                    if (_ad.DecodeBit(ref _ctx[ctx]))
                        bm.Data[y * bm.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            return bm;
        }

        /// <summary>Template 0 with its nominal adaptive pixels: the context is three windows
        /// over the rows - row y-2 at x-2..x+2, row y-1 at x-3..x+3, row y at x-4..x-1 - each
        /// stepped by a shift and one new bit, so a pixel costs two bit reads; a byte of the
        /// row is written at a time. The contexts are those the general scan forms.</summary>
        private void DecodeTemplate0Nominal(Jbig2Bitmap bm, int width, int height)
        {
            var data = bm.Data;
            var stride = bm.RowBytes;
            var ltp = false;
            for (var y = 0; y < height; y++)
            {
                if (_tpgdon)
                {
                    if (_ad.DecodeBit(ref _ctx[SltpContext(0)])) ltp = !ltp;
                    if (ltp)
                    {
                        if (y > 0) System.Array.Copy(data, (y - 1) * stride, data, y * stride, stride);
                        continue;
                    }
                }
                var row = y * stride;
                int rowM1 = row - stride, rowM2 = row - 2 * stride;
                var w2 = 0;
                for (var p = 0; p <= 2; p++) w2 = (w2 << 1) | PixelOrZero(data, rowM2, y >= 2, p, width);
                var w1 = 0;
                for (var p = 0; p <= 3; p++) w1 = (w1 << 1) | PixelOrZero(data, rowM1, y >= 1, p, width);
                var w0 = 0;
                for (var x = 0; x < width; x += 8)
                {
                    var result = 0;
                    var count = Math.Min(8, width - x);
                    for (var k = 0; k < count; k++)
                    {
                        var bit = _ad.DecodeBit(ref _ctx[(w2 << 11) | (w1 << 4) | w0]) ? 1 : 0;
                        result |= bit << (7 - k);
                        w2 = ((w2 << 1) | PixelOrZero(data, rowM2, y >= 2, x + k + 3, width)) & 0x1F;
                        w1 = ((w1 << 1) | PixelOrZero(data, rowM1, y >= 1, x + k + 4, width)) & 0x7F;
                        w0 = ((w0 << 1) | bit) & 0xF;
                    }
                    data[row + (x >> 3)] = (byte)result;
                }
            }
        }

        /// <summary>Pixel <paramref name="x"/> of a packed row, 0 past the row's width or
        /// when the row lies above the bitmap.</summary>
        private static int PixelOrZero(byte[] data, int rowOffset, bool rowExists, int x, int width)
            => rowExists && x < width ? (data[rowOffset + (x >> 3)] >> (7 - (x & 7))) & 1 : 0;

        /// <summary>The context of pixel (x, y) read in full - the first pixel of a row.</summary>
        private int ContextAt(Jbig2Bitmap bm, int x, int y)
        {
            var c = 0;
            foreach (var (dx, dy) in _pixels) c = (c << 1) | bm.GetPixel(x + dx, y + dy);
            return c;
        }

        /// <summary>The context of pixel (x, y) from that of pixel (x - 1, y): the bits a shift
        /// carries over, and the pixels read anew.</summary>
        private int NextContext(Jbig2Bitmap bm, int previous, int x, int y)
        {
            var c = (previous & _reuseMask) << 1;
            var top = _pixels.Length - 1;
            foreach (var k in _fresh)
                c |= bm.GetPixel(x + _pixels[k].dx, y + _pixels[k].dy) << (top - k);
            return c;
        }
    }
}
