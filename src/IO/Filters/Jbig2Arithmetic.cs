using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    /// <summary>
    /// Decoder for the signed-integer arithmetic codings IADH / IADW / IAEX / IADT /
    /// IAFS / IADS / IAIT / IARI / IARDW / IARDH / IARDX / IARDY (T.88 §A.2).
    /// Each instance owns its own 512-context array; values are signed and may indicate
    /// out-of-band (OOB).
    /// </summary>
    private sealed class IntegerDecoder
    {
        private readonly ArithmeticDecoder _ad;
        private readonly ArithmeticContext[] _ctx = new ArithmeticContext[512];

        public IntegerDecoder(ArithmeticDecoder ad)
        {
            _ad = ad;
            for (var i = 0; i < _ctx.Length; i++) _ctx[i] = new ArithmeticContext();
        }

        /// <summary>
        /// Decode the next integer. Returns false when OOB; true otherwise with the value
        /// in <paramref name="value"/>.
        /// </summary>
        public bool Decode(out int value)
        {
            var prev = 1;

            int Read()
            {
                var b = _ad.DecodeBit(_ctx[prev]) ? 1 : 0;
                prev = prev < 256 ? ((prev << 1) | b) : (((prev << 1) | b) & 511) | 256;
                return b;
            }

            var s = Read();
            int bits, offset;
            if (Read() == 0)        { bits = 2;  offset = 0; }
            else if (Read() == 0)   { bits = 4;  offset = 4; }
            else if (Read() == 0)   { bits = 6;  offset = 20; }
            else if (Read() == 0)   { bits = 8;  offset = 84; }
            else if (Read() == 0)   { bits = 12; offset = 340; }
            else                    { bits = 32; offset = 4436; }

            var v = 0;
            for (var i = 0; i < bits; i++)
                v = (v << 1) | Read();

            v += offset;
            if (s == 1 && v == 0) { value = 0; return false; } // OOB
            value = s == 1 ? -v : v;
            return true;
        }
    }

    /// <summary>
    /// QM arithmetic decoder for JBIG2 — T.88 Annex E in the "C-high / C-low split" software form
    /// (the formulation used by pdf.js and the JPEG/JBIG2 reference decoders). The code register is
    /// kept un-complemented; the DECODE comparison is against Qe (LPS sub-interval low). A past-end
    /// read of 0xFF lands the BYTEIN in the terminator branch, producing the required trailing 1-bits.
    /// </summary>
    private sealed class ArithmeticDecoder
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _bp;
        private uint _chigh;
        private uint _clow;
        private uint _a;
        private int _ct;

        public ArithmeticDecoder(byte[] data, int start)
        {
            _data = data;
            _end = data.Length;
            _bp = start;
            _chigh = ByteAt(start);
            _clow = 0;
            ByteIn();
            _chigh = ((_chigh << 7) & 0xffff) | ((_clow >> 9) & 0x7f);
            _clow = (_clow << 7) & 0xffff;
            _ct -= 7;
            _a = 0x8000;
        }

        private uint ByteAt(int p) => (uint)(p < _end ? _data[p] : 0xFF);

        public bool DecodeBit(ArithmeticContext cx)
        {
            var idx = cx.Index;
            var qe = QeTable[idx];
            bool d;

            _a -= qe;
            if (_chigh < qe)
            {
                // LPS sub-interval (the [0, Qe) low range).
                if (_a < qe)
                {
                    _a = qe;
                    d = cx.Mps;
                    cx.Index = NmpsTable[idx];
                }
                else
                {
                    _a = qe;
                    d = !cx.Mps;
                    if (SwitchTable[idx] != 0) cx.Mps = !cx.Mps;
                    cx.Index = NlpsTable[idx];
                }
            }
            else
            {
                _chigh -= qe;
                if ((_a & 0x8000) != 0)
                    return cx.Mps; // MPS, no renormalisation needed

                // MPS_EXCHANGE
                if (_a < qe)
                {
                    d = !cx.Mps;
                    if (SwitchTable[idx] != 0) cx.Mps = !cx.Mps;
                    cx.Index = NlpsTable[idx];
                }
                else
                {
                    d = cx.Mps;
                    cx.Index = NmpsTable[idx];
                }
            }

            // RENORMD
            do
            {
                if (_ct == 0) ByteIn();
                _a <<= 1;
                _chigh = ((_chigh << 1) & 0xffff) | ((_clow >> 15) & 1);
                _clow = (_clow << 1) & 0xffff;
                _ct--;
            } while ((_a & 0x8000) == 0);

            return d;
        }

        private void ByteIn()
        {
            if (ByteAt(_bp) == 0xFF)
            {
                if (ByteAt(_bp + 1) > 0x8F)
                {
                    _clow += 0xFF00;
                    _ct = 8;
                }
                else
                {
                    _bp++;
                    _clow += ByteAt(_bp) << 9;
                    _ct = 7;
                }
            }
            else
            {
                _bp++;
                _clow += _bp < _end ? ByteAt(_bp) << 8 : 0xFF00;
                _ct = 8;
            }
            if (_clow > 0xFFFF)
            {
                _chigh += _clow >> 16;
                _clow &= 0xFFFF;
            }
        }

        // T.88 Table E.1 — Qe value, NMPS, NLPS, SWITCH columns. 47 states.
        private static readonly uint[] QeTable =
        [
            0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401,
            0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601, 0x5601, 0x5401,
            0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201,
            0x1C01, 0x1801, 0x1601, 0x1401, 0x1201, 0x1101, 0x0AC1, 0x09C1,
            0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085,
            0x0049, 0x0025, 0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
        ];

        private static readonly int[] NmpsTable =
        [
             1,  2,  3,  4,  5, 38,  7,  8,  9, 10, 11, 12, 13, 29, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
            33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
        ];

        private static readonly int[] NlpsTable =
        [
             1,  6,  9, 12, 29, 33,  6, 14, 14, 14, 17, 18, 20, 21, 14, 14,
            15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
            30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
        ];

        // SWITCH flag: 1 only at indices 0, 6, 14 (states where MPS may flip under
        // conditional exchange). All others 0.
        private static readonly int[] SwitchTable =
        [
            1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        ];
    }
}
