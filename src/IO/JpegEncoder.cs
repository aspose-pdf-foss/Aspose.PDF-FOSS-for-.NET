using System.Linq;

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

        // Optimized-Huffman two-pass state. Pass 1 tallies how often each Huffman symbol
        // occurs (per DC/AC × luma/chroma table); tables optimal for THIS image are then
        // built and used to emit the scan in pass 2. Custom tables typically shave a few
        // percent over the standard example tables.
        private bool _gathering;
        private readonly int[] _dcLumFreq = new int[257];
        private readonly int[] _acLumFreq = new int[257];
        private readonly int[] _dcChrFreq = new int[257];
        private readonly int[] _acChrFreq = new int[257];

        // Active encoding tables (bits/vals + derived code/size), set from the optimized
        // tables before pass 2. Default to the standard tables so a first-pass failure or a
        // single-block image still produces a valid stream.
        private byte[] _dcLumBits = DcLumBits, _dcLumVals = DcLumVals;
        private byte[] _dcChrBits = DcChrBits, _dcChrVals = DcChrVals;
        private byte[] _acLumBits = AcLumBits, _acLumVals = AcLumVals;
        private byte[] _acChrBits = AcChrBits, _acChrVals = AcChrVals;
        private int[] _dcLumCo = DcLumEhufco, _dcLumSi = DcLumEhufsi;
        private int[] _dcChrCo = DcChrEhufco, _dcChrSi = DcChrEhufsi;
        private int[] _acLumCo = AcLumEhufco, _acLumSi = AcLumEhufsi;
        private int[] _acChrCo = AcChrEhufco, _acChrSi = AcChrEhufsi;

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
            // Pass 1: run the whole image counting Huffman symbols (no bytes emitted), then
            // build tables optimal for this image and install them for pass 2.
            _gathering = true;
            _dcY = _dcCb = _dcCr = 0;
            WriteImageData();
            _gathering = false;
            BuildOptimizedTables();

            _dcY = _dcCb = _dcCr = 0;
            WriteSOI();
            WriteAPP0();
            WriteDQT(0, _lumQt);
            WriteDQT(1, _chrQt);
            WriteSOF0();
            WriteDHT(0, 0, _dcLumBits, _dcLumVals);   // DC luminance
            WriteDHT(0, 1, _dcChrBits, _dcChrVals);   // DC chrominance
            WriteDHT(1, 0, _acLumBits, _acLumVals);   // AC luminance
            WriteDHT(1, 1, _acChrBits, _acChrVals);   // AC chrominance
            WriteSOS();
            WriteImageData();
            FlushBits();
            WriteEOI();
        }

        /// <summary>Build Huffman tables optimal for this image from the pass-1 frequencies
        /// and derive their encoding (code/size) tables. Any table with no symbols (e.g. a
        /// solid-colour image) keeps the standard table so the derived codes stay valid.</summary>
        private void BuildOptimizedTables()
        {
            if (_dcLumFreq.Any(f => f > 0)) (_dcLumBits, _dcLumVals) = BuildOptimalTable(_dcLumFreq);
            if (_acLumFreq.Any(f => f > 0)) (_acLumBits, _acLumVals) = BuildOptimalTable(_acLumFreq);
            if (_dcChrFreq.Any(f => f > 0)) (_dcChrBits, _dcChrVals) = BuildOptimalTable(_dcChrFreq);
            if (_acChrFreq.Any(f => f > 0)) (_acChrBits, _acChrVals) = BuildOptimalTable(_acChrFreq);
            (_dcLumCo, _dcLumSi) = BuildHuffEnc(_dcLumBits, _dcLumVals, 16);
            (_dcChrCo, _dcChrSi) = BuildHuffEnc(_dcChrBits, _dcChrVals, 16);
            (_acLumCo, _acLumSi) = BuildHuffEnc(_acLumBits, _acLumVals, 256);
            (_acChrCo, _acChrSi) = BuildHuffEnc(_acChrBits, _acChrVals, 256);
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

            // 4:2:0 chroma subsampling: luminance sampled 2x2 per MCU, chrominance 1x1
            // (one Cb/Cr block averaged from each 16x16 luma area). Halving the chroma
            // resolution shrinks the file ~30-40% at a visually negligible cost — matching
            // what mainstream JPEG encoders emit and what a re-encode must beat to be smaller.
            // Y: id=1, sampling 2x2, quant table 0
            _out.WriteByte(1); _out.WriteByte(0x22); _out.WriteByte(0);
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

            // 4:2:0 minimum coded unit: 16x16 pixels → four 8x8 Y blocks, one 8x8 Cb, one 8x8 Cr.
            for (var my = 0; my < _height; my += 16)
            {
                for (var mx = 0; mx < _width; mx += 16)
                {
                    // Four luminance blocks in raster order (top-left, top-right, bottom-left, bottom-right).
                    for (var yb = 0; yb < 2; yb++)
                        for (var xb = 0; xb < 2; xb++)
                        {
                            ExtractLumaBlock(mx + xb * 8, my + yb * 8, block);
                            _dcY = EncodeBlock(block, _lumQt, _dcY, _dcLumCo, _dcLumSi, _acLumCo, _acLumSi, _dcLumFreq, _acLumFreq);
                        }

                    // One chroma block per MCU, each sample averaged over its 2x2 luma footprint.
                    ExtractChromaBlock(mx, my, chroma: 1, block);
                    _dcCb = EncodeBlock(block, _chrQt, _dcCb, _dcChrCo, _dcChrSi, _acChrCo, _acChrSi, _dcChrFreq, _acChrFreq);

                    ExtractChromaBlock(mx, my, chroma: 2, block);
                    _dcCr = EncodeBlock(block, _chrQt, _dcCr, _dcChrCo, _dcChrSi, _acChrCo, _acChrSi, _dcChrFreq, _acChrFreq);
                }
            }
        }

        /// <summary>Fill an 8x8 luminance block (level-shifted by -128) from the pixels at
        /// (bx,by); coordinates past the image edge clamp to the last row/column.</summary>
        private void ExtractLumaBlock(int bx, int by, int[] block)
        {
            for (var y = 0; y < 8; y++)
            {
                var py = Math.Min(by + y, _height - 1);
                for (var x = 0; x < 8; x++)
                {
                    var px = Math.Min(bx + x, _width - 1);
                    _getPixel(px, py, out var r, out var g, out var b);
                    block[y * 8 + x] = (int)Math.Round(0.299 * r + 0.587 * g + 0.114 * b - 128);
                }
            }
        }

        /// <summary>Fill an 8x8 chroma block (<paramref name="chroma"/> 1 = Cb, 2 = Cr) for the
        /// 16x16 MCU at (mx,my): each output sample is the average Cb/Cr of its 2x2 pixel group,
        /// implementing 4:2:0 subsampling. Edge coordinates clamp to the image bounds.</summary>
        private void ExtractChromaBlock(int mx, int my, int chroma, int[] block)
        {
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    double sum = 0;
                    for (var dy = 0; dy < 2; dy++)
                        for (var dx = 0; dx < 2; dx++)
                        {
                            var px = Math.Min(mx + x * 2 + dx, _width - 1);
                            var py = Math.Min(my + y * 2 + dy, _height - 1);
                            _getPixel(px, py, out var r, out var g, out var b);
                            sum += chroma == 1
                                ? -0.168736 * r - 0.331264 * g + 0.5 * b        // Cb
                                :  0.5 * r - 0.418688 * g - 0.081312 * b;       // Cr
                        }
                    block[y * 8 + x] = (int)Math.Round(sum / 4.0);
                }
            }
        }

        private int EncodeBlock(int[] block, int[] qt, int dcPred,
            int[] dcEhufco, int[] dcEhufsi, int[] acEhufco, int[] acEhufsi,
            int[] dcFreq, int[] acFreq)
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
            EmitSymbol(cat, dcEhufco, dcEhufsi, dcFreq);
            if (cat > 0 && !_gathering)
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
                    EmitSymbol(0xF0, acEhufco, acEhufsi, acFreq); // ZRL
                    zeroRun -= 16;
                }
                var acCat = Category(qblock[i]);
                var rs = (zeroRun << 4) | acCat;
                EmitSymbol(rs, acEhufco, acEhufsi, acFreq);
                if (!_gathering)
                    WriteBits(EncodeDiff(qblock[i], acCat), acCat);
                zeroRun = 0;
            }
            if (zeroRun > 0)
                EmitSymbol(0, acEhufco, acEhufsi, acFreq); // EOB

            return dc;
        }

        /// <summary>Pass 1 (gathering): tally the Huffman symbol's frequency. Pass 2: emit its
        /// Huffman code. The raw amplitude bits that follow a symbol are not Huffman-coded, so
        /// they never affect the frequencies — only the symbols do.</summary>
        private void EmitSymbol(int sym, int[] ehufco, int[] ehufsi, int[] freq)
        {
            if (_gathering) freq[sym]++;
            else WriteBits(ehufco[sym], ehufsi[sym]);
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

        /// <summary>Build a Huffman table (bits-per-length counts + symbol list) optimal for the
        /// given symbol frequencies, following the procedure in JPEG/T.81 Annex K.2 (the same
        /// algorithm libjpeg uses for -optimize). A reserved symbol guarantees no all-ones code,
        /// and code lengths are clamped to the 16-bit maximum the format allows.</summary>
        private static (byte[] bits, byte[] vals) BuildOptimalTable(int[] freqIn)
        {
            // freq[256] is a reserved symbol that never appears in the output but keeps the
            // longest code from being all-ones (a JPEG requirement).
            var freq = new long[257];
            for (var i = 0; i < 256; i++) freq[i] = freqIn[i];
            freq[256] = 1;

            var codesize = new int[257];
            var others = new int[257];
            for (var i = 0; i < 257; i++) others[i] = -1;

            // Repeatedly merge the two least-frequent symbols/chains (Annex K.2 Figure K.1).
            while (true)
            {
                var c1 = -1; long v = long.MaxValue;
                for (var i = 0; i < 257; i++) if (freq[i] != 0 && freq[i] <= v) { v = freq[i]; c1 = i; }
                var c2 = -1; v = long.MaxValue;
                for (var i = 0; i < 257; i++) if (freq[i] != 0 && freq[i] <= v && i != c1) { v = freq[i]; c2 = i; }
                if (c2 < 0) break; // only one symbol left with nonzero frequency

                freq[c1] += freq[c2];
                freq[c2] = 0;
                codesize[c1]++;
                while (others[c1] >= 0) { c1 = others[c1]; codesize[c1]++; }
                others[c1] = c2;
                codesize[c2]++;
                while (others[c2] >= 0) { c2 = others[c2]; codesize[c2]++; }
            }

            // Count how many codes are of each length (Figure K.2).
            var bits = new int[33];
            for (var i = 0; i < 257; i++) if (codesize[i] > 0) bits[codesize[i]]++;

            // Enforce the 16-bit maximum code length (Figure K.3).
            for (var i = 32; i > 16; i--)
            {
                while (bits[i] > 0)
                {
                    var j = i - 2;
                    while (bits[j] == 0) j--;
                    bits[i] -= 2;
                    bits[i - 1] += 1;
                    bits[j + 1] += 2;
                    bits[j] -= 1;
                }
            }

            // Remove the reserved symbol's code (the longest one).
            var k = 16;
            while (bits[k] == 0) k--;
            bits[k]--;

            var outBits = new byte[16];
            for (var i = 0; i < 16; i++) outBits[i] = (byte)bits[i + 1];

            // Symbols ordered by ascending code length (Annex K.2), excluding the reserved 256.
            var vals = new List<byte>();
            for (var len = 1; len <= 32; len++)
                for (var sym = 0; sym < 256; sym++)
                    if (codesize[sym] == len) vals.Add((byte)sym);

            return (outBits, vals.ToArray());
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
