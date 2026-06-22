namespace Aspose.Pdf.IO;

/// <summary>
/// Reads one RGB triplet from a logical pixel source.
/// Used by the streaming JPEG encoder to avoid materialising a full
/// RGBA buffer for huge images (byte[] caps at 2.1 GB).
/// </summary>
internal delegate void PixelGetter(int x, int y, out byte r, out byte g, out byte b);

/// <summary>
/// Pure C# baseline JPEG encoder.
/// Encodes RGB pixels (provided either as a flat RGBA byte[] or via a
/// streaming PixelGetter) into a JFIF byte stream.
/// </summary>
internal static class JpegEncoderImpl
{
    /// <summary>Encode RGBA pixels (4 bytes per pixel) to JPEG.</summary>
    public static byte[] Encode(byte[] rgba, int width, int height, int quality, int xDpi = 0, int yDpi = 0)
    {
        return Encode((int x, int y, out byte r, out byte g, out byte b) =>
        {
            var idx = (y * width + x) * 4;
            r = rgba[idx];
            g = rgba[idx + 1];
            b = rgba[idx + 2];
        }, width, height, quality, xDpi, yDpi);
    }

    /// <summary>
    /// Encode pixels supplied via a streaming getter — no flat-buffer allocation.
    /// When <paramref name="xDpi"/>/<paramref name="yDpi"/> are positive the JFIF APP0
    /// header advertises them as inch-based density; 0 falls back to the unit-less
    /// 1×1 default that callers used before DPI propagation existed.
    /// </summary>
    public static byte[] Encode(PixelGetter getter, int width, int height, int quality, int xDpi = 0, int yDpi = 0)
    {
        if (quality < 1) quality = 1;
        if (quality > 100) quality = 100;

        using var ms = new MemoryStream();
        var writer = new JpegWriter(ms, width, height, quality, getter, xDpi, yDpi);
        writer.Write();
        return ms.ToArray();
    }

    private sealed class JpegWriter
    {
        private readonly Stream _out;
        private readonly int _width, _height, _quality;
        private readonly int _xDpi, _yDpi;
        private readonly PixelGetter _getPixel;

        // Quantization tables (luminance and chrominance)
        private readonly int[] _lumQt = new int[64];
        private readonly int[] _chrQt = new int[64];

        // Bit accumulator for entropy coding
        private int _bitBuf;
        private int _bitCount;

        // DC predictors
        private int _dcY, _dcCb, _dcCr;

        public JpegWriter(Stream output, int width, int height, int quality, PixelGetter getter, int xDpi, int yDpi)
        {
            _out = output;
            _width = width;
            _height = height;
            _quality = quality;
            _xDpi = xDpi;
            _yDpi = yDpi;
            _getPixel = getter;

            BuildQuantTable(_lumQt, StdLumQt, quality);
            BuildQuantTable(_chrQt, StdChrQt, quality);
        }

        public void Write()
        {
            WriteSOI();
            WriteAPP0();
            WriteDQT(0, _lumQt);
            WriteDQT(1, _chrQt);
            WriteSOF0();
            WriteDHT(0, 0, DcLumBits, DcLumVals);   // DC luminance
            WriteDHT(0, 1, DcChrBits, DcChrVals);   // DC chrominance
            WriteDHT(1, 0, AcLumBits, AcLumVals);   // AC luminance
            WriteDHT(1, 1, AcChrBits, AcChrVals);   // AC chrominance
            WriteSOS();
            WriteImageData();
            FlushBits();
            WriteEOI();
        }

        private void WriteSOI()
        {
            _out.WriteByte(0xFF);
            _out.WriteByte(0xD8);
        }

        private void WriteEOI()
        {
            _out.WriteByte(0xFF);
            _out.WriteByte(0xD9);
        }

        private void WriteAPP0()
        {
            // JFIF density units = 1 (DPI) when caller supplied a positive resolution,
            // otherwise unit-less 1×1 (matches pre-DPI behaviour). XResolution/
            // YResolution are 16-bit unsigned, so clamp anything beyond u16.
            var hasDpi = _xDpi > 0 && _yDpi > 0;
            var xDensity = hasDpi ? Math.Min(_xDpi, 0xFFFF) : 1;
            var yDensity = hasDpi ? Math.Min(_yDpi, 0xFFFF) : 1;
            WriteMarker(0xE0);
            Write16(16); // length
            _out.Write("JFIF\0"u8);
            Write16(0x0102); // version 1.02
            _out.WriteByte((byte)(hasDpi ? 1 : 0)); // density units: 0=none, 1=DPI
            Write16(xDensity);
            Write16(yDensity);
            _out.WriteByte(0); // thumbnail width
            _out.WriteByte(0); // thumbnail height
        }

        private void WriteDQT(int tableId, int[] qt)
        {
            WriteMarker(0xDB);
            Write16(67); // length: 2 + 1 + 64
            _out.WriteByte((byte)tableId);
            for (var i = 0; i < 64; i++)
                _out.WriteByte((byte)qt[ZigZag[i]]);
        }

        private void WriteSOF0()
        {
            WriteMarker(0xC0);
            Write16(17); // length
            _out.WriteByte(8); // precision
            Write16(_height);
            Write16(_width);
            _out.WriteByte(3); // components

            // Y: id=1, sampling 1x1, quant table 0
            _out.WriteByte(1); _out.WriteByte(0x11); _out.WriteByte(0);
            // Cb: id=2, sampling 1x1, quant table 1
            _out.WriteByte(2); _out.WriteByte(0x11); _out.WriteByte(1);
            // Cr: id=3, sampling 1x1, quant table 1
            _out.WriteByte(3); _out.WriteByte(0x11); _out.WriteByte(1);
        }

        private void WriteDHT(int tableClass, int tableId, byte[] bits, byte[] vals)
        {
            WriteMarker(0xC4);
            var totalVals = 0;
            for (var i = 0; i < 16; i++) totalVals += bits[i];
            Write16(3 + 16 + totalVals);
            _out.WriteByte((byte)((tableClass << 4) | tableId));
            _out.Write(bits, 0, 16);
            _out.Write(vals, 0, totalVals);
        }

        private void WriteSOS()
        {
            WriteMarker(0xDA);
            Write16(12); // length
            _out.WriteByte(3); // components
            _out.WriteByte(1); _out.WriteByte(0x00); // Y: DC table 0, AC table 0
            _out.WriteByte(2); _out.WriteByte(0x11); // Cb: DC table 1, AC table 1
            _out.WriteByte(3); _out.WriteByte(0x11); // Cr: DC table 1, AC table 1
            _out.WriteByte(0);  // Ss
            _out.WriteByte(63); // Se
            _out.WriteByte(0);  // Ah/Al
        }

        private void WriteImageData()
        {
            var block = new int[64];

            for (var by = 0; by < _height; by += 8)
            {
                for (var bx = 0; bx < _width; bx += 8)
                {
                    // Extract Y, Cb, Cr blocks
                    ExtractBlock(bx, by, 0, block); // Y
                    _dcY = EncodeBlock(block, _lumQt, _dcY, DcLumEhufco, DcLumEhufsi, AcLumEhufco, AcLumEhufsi);

                    ExtractBlock(bx, by, 1, block); // Cb
                    _dcCb = EncodeBlock(block, _chrQt, _dcCb, DcChrEhufco, DcChrEhufsi, AcChrEhufco, AcChrEhufsi);

                    ExtractBlock(bx, by, 2, block); // Cr
                    _dcCr = EncodeBlock(block, _chrQt, _dcCr, DcChrEhufco, DcChrEhufsi, AcChrEhufco, AcChrEhufsi);
                }
            }
        }

        private void ExtractBlock(int bx, int by, int component, int[] block)
        {
            for (var y = 0; y < 8; y++)
            {
                var py = by + y;
                if (py >= _height) py = _height - 1;
                for (var x = 0; x < 8; x++)
                {
                    var px = bx + x;
                    if (px >= _width) px = _width - 1;
                    _getPixel(px, py, out var r, out var g, out var b);

                    var val = component switch
                    {
                        0 => 0.299 * r + 0.587 * g + 0.114 * b - 128,       // Y
                        1 => -0.168736 * r - 0.331264 * g + 0.5 * b,         // Cb
                        _ => 0.5 * r - 0.418688 * g - 0.081312 * b,          // Cr
                    };
                    block[y * 8 + x] = (int)Math.Round(val);
                }
            }
        }

        private int EncodeBlock(int[] block, int[] qt, int dcPred,
            int[] dcEhufco, int[] dcEhufsi, int[] acEhufco, int[] acEhufsi)
        {
            // Forward DCT
            FDCT(block);

            // Quantize. Clamp every coefficient to the amplitude range the baseline
            // Huffman tables can represent (JPEG/T.81 Table F.1/F.2): AC magnitudes map to
            // categories 1-10 (|v| ≤ 1023) and DC differences to categories 0-11 (|d| ≤ 2047).
            // At quality 100 the quant divisors are 1, so an extreme high-frequency block can
            // round to |coef| ≥ 1024 — category 11 has no AC code, so an unclamped value would
            // emit a zero-length code and desync the decoder (a chroma drift / colour cast over
            // the rest of the scan). Clamping costs an imperceptible amount of the top coefficient.
            var qblock = new int[64];
            for (var i = 0; i < 64; i++)
            {
                var zigIdx = ZigZag[i];
                var q = (int)Math.Round((double)block[zigIdx] / qt[zigIdx]);
                if (i > 0) q = q > 1023 ? 1023 : (q < -1023 ? -1023 : q);
                qblock[i] = q;
            }

            // Encode DC. The difference is what gets coded, so clamp the difference (not the
            // absolute level) to category 11, then carry the reconstructed value forward as the
            // predictor so the decoder stays in lock-step.
            var dc = qblock[0];
            var diff = dc - dcPred;
            diff = diff > 2047 ? 2047 : (diff < -2047 ? -2047 : diff);
            dc = dcPred + diff;
            var cat = Category(diff);
            WriteBits(dcEhufco[cat], dcEhufsi[cat]);
            if (cat > 0)
                WriteBits(EncodeDiff(diff, cat), cat);

            // Encode AC
            var zeroRun = 0;
            for (var i = 1; i < 64; i++)
            {
                if (qblock[i] == 0)
                {
                    zeroRun++;
                    continue;
                }
                while (zeroRun >= 16)
                {
                    WriteBits(acEhufco[0xF0], acEhufsi[0xF0]); // ZRL
                    zeroRun -= 16;
                }
                var acCat = Category(qblock[i]);
                var rs = (zeroRun << 4) | acCat;
                WriteBits(acEhufco[rs], acEhufsi[rs]);
                WriteBits(EncodeDiff(qblock[i], acCat), acCat);
                zeroRun = 0;
            }
            if (zeroRun > 0)
                WriteBits(acEhufco[0], acEhufsi[0]); // EOB

            return dc;
        }

        private static void FDCT(int[] block)
        {
            // Simple forward DCT (rows then columns)
            var ws = new double[64];
            for (var i = 0; i < 64; i++) ws[i] = block[i];

            for (var row = 0; row < 8; row++)
                FDCT1D(ws, row * 8);
            for (var col = 0; col < 8; col++)
            {
                var tmp = new double[8];
                for (var i = 0; i < 8; i++) tmp[i] = ws[i * 8 + col];
                FDCT1D(tmp, 0);
                for (var i = 0; i < 8; i++) ws[i * 8 + col] = tmp[i];
            }

            for (var i = 0; i < 64; i++)
                block[i] = (int)Math.Round(ws[i] / 8.0);
        }

        private static void FDCT1D(double[] data, int off)
        {
            var d0 = data[off] + data[off + 7];
            var d7 = data[off] - data[off + 7];
            var d1 = data[off + 1] + data[off + 6];
            var d6 = data[off + 1] - data[off + 6];
            var d2 = data[off + 2] + data[off + 5];
            var d5 = data[off + 2] - data[off + 5];
            var d3 = data[off + 3] + data[off + 4];
            var d4 = data[off + 3] - data[off + 4];

            // Even part
            var t0 = d0 + d3;
            var t3 = d0 - d3;
            var t1 = d1 + d2;
            var t2 = d1 - d2;

            data[off] = t0 + t1;
            data[off + 4] = t0 - t1;
            data[off + 2] = t2 * 1.847759065022574 + t3 * 0.7653668647301796;
            data[off + 6] = t3 * 1.847759065022574 - t2 * 0.7653668647301796;

            // Odd part
            var z1 = d4 + d7;
            var z2 = d5 + d6;
            var z3 = d4 + d6;
            var z4 = d5 + d7;
            var z5 = (z3 + z4) * 1.175875602419359;

            var t4 = d4 * 0.298631336 - z1 * 0.899976223 + z3 * (-1.961570560) + z5;
            var t5 = d5 * 2.053119869 - z2 * 2.562915447 + z4 * (-0.390180644) + z5;
            var t6 = d6 * 3.072711026 - z2 * 2.562915447 + z3 * (-1.961570560) + z5;
            var t7 = d7 * 1.501321110 - z1 * 0.899976223 + z4 * (-0.390180644) + z5;

            data[off + 7] = t4;
            data[off + 5] = t5;
            data[off + 3] = t6;
            data[off + 1] = t7;
        }

        private static int Category(int val)
        {
            if (val < 0) val = -val;
            var cat = 0;
            while (val > 0) { val >>= 1; cat++; }
            return cat;
        }

        private static int EncodeDiff(int diff, int cat)
        {
            return diff >= 0 ? diff : diff + (1 << cat) - 1;
        }

        private void WriteBits(int code, int size)
        {
            _bitBuf = (_bitBuf << size) | (code & ((1 << size) - 1));
            _bitCount += size;

            while (_bitCount >= 8)
            {
                _bitCount -= 8;
                var b = (byte)((_bitBuf >> _bitCount) & 0xFF);
                _out.WriteByte(b);
                if (b == 0xFF)
                    _out.WriteByte(0x00); // byte stuffing
            }
        }

        private void FlushBits()
        {
            if (_bitCount > 0)
            {
                var b = (byte)((_bitBuf << (8 - _bitCount)) & 0xFF);
                _out.WriteByte(b);
                if (b == 0xFF)
                    _out.WriteByte(0x00);
            }
            _bitBuf = 0;
            _bitCount = 0;
        }

        private void WriteMarker(int marker)
        {
            _out.WriteByte(0xFF);
            _out.WriteByte((byte)marker);
        }

        private void Write16(int value)
        {
            _out.WriteByte((byte)(value >> 8));
            _out.WriteByte((byte)(value & 0xFF));
        }

        private static void BuildQuantTable(int[] dst, int[] src, int quality)
        {
            var s = quality < 50 ? 5000 / quality : 200 - quality * 2;
            for (var i = 0; i < 64; i++)
            {
                var val = (src[i] * s + 50) / 100;
                dst[i] = Math.Clamp(val, 1, 255);
            }
        }

        // Standard JPEG zigzag order
        private static readonly int[] ZigZag =
        [
             0,  1,  8, 16,  9,  2,  3, 10,
            17, 24, 32, 25, 18, 11,  4,  5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13,  6,  7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63,
        ];

        // Standard luminance quantization table
        private static readonly int[] StdLumQt =
        [
            16, 11, 10, 16,  24,  40,  51,  61,
            12, 12, 14, 19,  26,  58,  60,  55,
            14, 13, 16, 24,  40,  57,  69,  56,
            14, 17, 22, 29,  51,  87,  80,  62,
            18, 22, 37, 56,  68, 109, 103,  77,
            24, 35, 55, 64,  81, 104, 113,  92,
            49, 64, 78, 87, 103, 121, 120, 101,
            72, 92, 95, 98, 112, 100, 103,  99,
        ];

        // Standard chrominance quantization table
        private static readonly int[] StdChrQt =
        [
            17, 18, 24, 47, 99, 99, 99, 99,
            18, 21, 26, 66, 99, 99, 99, 99,
            24, 26, 56, 99, 99, 99, 99, 99,
            47, 66, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
            99, 99, 99, 99, 99, 99, 99, 99,
        ];

        // Standard Huffman tables (bits counts + values)
        // DC Luminance
        private static readonly byte[] DcLumBits = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
        private static readonly byte[] DcLumVals = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

        // DC Chrominance
        private static readonly byte[] DcChrBits = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
        private static readonly byte[] DcChrVals = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

        // AC Luminance
        private static readonly byte[] AcLumBits = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7D];
        private static readonly byte[] AcLumVals =
        [
            0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61,
            0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52,
            0xD1, 0xF0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x25,
            0x26, 0x27, 0x28, 0x29, 0x2A, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44, 0x45,
            0x46, 0x47, 0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64,
            0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83,
            0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99,
            0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6,
            0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3,
            0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8,
            0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA,
        ];

        // AC Chrominance
        private static readonly byte[] AcChrBits = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];
        private static readonly byte[] AcChrVals =
        [
            0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61,
            0x71, 0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91, 0xA1, 0xB1, 0xC1, 0x09, 0x23, 0x33,
            0x52, 0xF0, 0x15, 0x62, 0x72, 0xD1, 0x0A, 0x16, 0x24, 0x34, 0xE1, 0x25, 0xF1, 0x17, 0x18,
            0x19, 0x1A, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3A, 0x43, 0x44,
            0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63,
            0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A,
            0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97,
            0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xB2, 0xB3, 0xB4,
            0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA,
            0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7,
            0xE8, 0xE9, 0xEA, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA,
        ];

        // Pre-computed Huffman encoding tables (code, size) for each symbol
        // Built from the standard tables above
        private static readonly int[] DcLumEhufco;
        private static readonly int[] DcLumEhufsi;
        private static readonly int[] DcChrEhufco;
        private static readonly int[] DcChrEhufsi;
        private static readonly int[] AcLumEhufco;
        private static readonly int[] AcLumEhufsi;
        private static readonly int[] AcChrEhufco;
        private static readonly int[] AcChrEhufsi;

        static JpegWriter()
        {
            (DcLumEhufco, DcLumEhufsi) = BuildHuffEnc(DcLumBits, DcLumVals, 16);
            (DcChrEhufco, DcChrEhufsi) = BuildHuffEnc(DcChrBits, DcChrVals, 16);
            (AcLumEhufco, AcLumEhufsi) = BuildHuffEnc(AcLumBits, AcLumVals, 256);
            (AcChrEhufco, AcChrEhufsi) = BuildHuffEnc(AcChrBits, AcChrVals, 256);
        }

        private static (int[] codes, int[] sizes) BuildHuffEnc(byte[] bits, byte[] vals, int maxSym)
        {
            var codes = new int[maxSym];
            var sizes = new int[maxSym];
            var code = 0;
            var valIdx = 0;
            for (var len = 1; len <= 16; len++)
            {
                for (var i = 0; i < bits[len - 1]; i++)
                {
                    if (valIdx < vals.Length)
                    {
                        var sym = vals[valIdx++];
                        if (sym < maxSym)
                        {
                            codes[sym] = code;
                            sizes[sym] = len;
                        }
                    }
                    code++;
                }
                code <<= 1;
            }
            return (codes, sizes);
        }
    }
}
