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
        private readonly (int dx, int dy)[] _at;
        private readonly ArithmeticContext[] _ctx;
        private readonly bool _tpgdon;

        public GenericRegionDecoder(ArithmeticDecoder ad, int template, (int, int)[] at, bool tpgdon = false)
        {
            _ad = ad;
            _template = template;
            _tpgdon = tpgdon;
            _at = at?.Length > 0 ? at.Select(p => (p.Item1, p.Item2)).ToArray() : DefaultAt(template);
            // 16-bit context for template 0; 13-bit for templates 1/2/3.
            var n = template == 0 ? 65536 : 8192;
            _ctx = new ArithmeticContext[n];
            for (var i = 0; i < n; i++) _ctx[i] = new ArithmeticContext();
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

        public Jbig2Bitmap Decode(int width, int height)
        {
            var bm = new Jbig2Bitmap(width, height);
            // Row-major scan with the JBIG2 template-0/1/2/3 contexts. Two prior rows are
            // accessed; out-of-bounds pixels read as 0 per the spec.
            var ltp = false;
            for (var y = 0; y < height; y++)
            {
                if (_tpgdon)
                {
                    // Typical-prediction: a per-row LTP bit toggles "this row equals the
                    // previous one". When set, copy row y-1 verbatim and skip pixel coding.
                    if (_ad.DecodeBit(_ctx[SltpContext(_template)])) ltp = !ltp;
                    if (ltp)
                    {
                        if (y > 0)
                            System.Array.Copy(bm.Data, (y - 1) * bm.RowBytes, bm.Data, y * bm.RowBytes, bm.RowBytes);
                        continue;
                    }
                }
                for (var x = 0; x < width; x++)
                {
                    var ctx = BuildContext(bm, x, y);
                    var bit = _ad.DecodeBit(_ctx[ctx]) ? 1 : 0;
                    if (bit != 0)
                    {
                        bm.Data[y * bm.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                    }
                }
            }
            return bm;
        }

        private int BuildContext(Jbig2Bitmap bm, int x, int y)
        {
            // The template pixel layouts below mirror T.88 Figures 6.2.5.3.
            // 'P' below denotes the AT pixels filling out the 16/13 bits.
            if (_template == 0)
            {
                // 16-bit context (template 0):
                //   row y-2: (-1,-2) (0,-2) (1,-2)                              → 3 bits
                //   row y-1: (-2,-1) (-1,-1) (0,-1) (1,-1) (2,-1)               → 5 bits
                //   row y  : (-4, 0) (-3, 0) (-2, 0) (-1, 0)                    → 4 bits
                //   AT pixels (4)                                               → 4 bits
                var c = 0;
                c = (c << 1) | bm.GetPixel(x - 1, y - 2);
                c = (c << 1) | bm.GetPixel(x,     y - 2);
                c = (c << 1) | bm.GetPixel(x + 1, y - 2);
                c = (c << 1) | bm.GetPixel(x - 2, y - 1);
                c = (c << 1) | bm.GetPixel(x - 1, y - 1);
                c = (c << 1) | bm.GetPixel(x,     y - 1);
                c = (c << 1) | bm.GetPixel(x + 1, y - 1);
                c = (c << 1) | bm.GetPixel(x + 2, y - 1);
                c = (c << 1) | bm.GetPixel(x - 4, y);
                c = (c << 1) | bm.GetPixel(x - 3, y);
                c = (c << 1) | bm.GetPixel(x - 2, y);
                c = (c << 1) | bm.GetPixel(x - 1, y);
                for (var i = 0; i < 4 && i < _at.Length; i++)
                    c = (c << 1) | bm.GetPixel(x + _at[i].dx, y + _at[i].dy);
                return c & 0xFFFF;
            }
            else if (_template == 1)
            {
                // 13-bit context (template 1):
                //   row y-2: (-1,-2) (0,-2) (1,-2) (2,-2)                       → 4 bits
                //   row y-1: (-2,-1) (-1,-1) (0,-1) (1,-1) (2,-1)               → 5 bits
                //   row y  : (-3,0) (-2,0) (-1,0)                                → 3 bits
                //   AT (1)                                                       → 1 bit
                var c = 0;
                c = (c << 1) | bm.GetPixel(x - 1, y - 2);
                c = (c << 1) | bm.GetPixel(x,     y - 2);
                c = (c << 1) | bm.GetPixel(x + 1, y - 2);
                c = (c << 1) | bm.GetPixel(x + 2, y - 2);
                c = (c << 1) | bm.GetPixel(x - 2, y - 1);
                c = (c << 1) | bm.GetPixel(x - 1, y - 1);
                c = (c << 1) | bm.GetPixel(x,     y - 1);
                c = (c << 1) | bm.GetPixel(x + 1, y - 1);
                c = (c << 1) | bm.GetPixel(x + 2, y - 1);
                c = (c << 1) | bm.GetPixel(x - 3, y);
                c = (c << 1) | bm.GetPixel(x - 2, y);
                c = (c << 1) | bm.GetPixel(x - 1, y);
                c = (c << 1) | bm.GetPixel(x + _at[0].dx, y + _at[0].dy);
                return c & 0x1FFF;
            }
            else // template 2 or 3 (13-bit, smaller footprint)
            {
                var c = 0;
                c = (c << 1) | bm.GetPixel(x - 1, y - 2);
                c = (c << 1) | bm.GetPixel(x,     y - 2);
                c = (c << 1) | bm.GetPixel(x + 1, y - 2);
                c = (c << 1) | bm.GetPixel(x - 2, y - 1);
                c = (c << 1) | bm.GetPixel(x - 1, y - 1);
                c = (c << 1) | bm.GetPixel(x,     y - 1);
                c = (c << 1) | bm.GetPixel(x + 1, y - 1);
                c = (c << 1) | bm.GetPixel(x + 2, y - 1);
                c = (c << 1) | bm.GetPixel(x - 2, y);
                c = (c << 1) | bm.GetPixel(x - 1, y);
                c = (c << 1) | bm.GetPixel(x + _at[0].dx, y + _at[0].dy);
                return c & 0x07FF;
            }
        }
    }
}
