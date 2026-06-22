namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Pure C# baseline JPEG (JFIF) decoder.
/// Decodes SOF0 (baseline DCT) JPEG streams into raw RGB or grayscale pixel arrays.
/// Supports chroma subsampling modes 4:4:4, 4:2:2, and 4:2:0.
/// </summary>
internal static class JpegDecoder
{
    /// <summary>
    /// Decode a JPEG byte stream into raw pixel data.
    /// </summary>
    /// <returns>Decoded pixels (RGB or grayscale), width, height, component count.</returns>
    public static (byte[] pixels, int width, int height, int components) Decode(byte[] data)
    {
        var reader = new JpegReader(data);
        reader.Parse();
        return (reader.Pixels, reader.Width, reader.Height, reader.Components);
    }

    private sealed class JpegReader
    {
        private readonly byte[] _data;
        private int _pos;

        // Frame info
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Components { get; private set; }
        public byte[] Pixels { get; private set; } = [];

        private ComponentInfo[] _components = [];
        private int[][] _quantTables = new int[4][];
        private HuffmanTable?[] _dcTables = new HuffmanTable[4];
        private HuffmanTable?[] _acTables = new HuffmanTable[4];
        private int _restartInterval;
        private int _maxH, _maxV; // max sampling factors
        // Adobe APP14 colour transform: -1 = no Adobe marker, 0 = CMYK/RGB (no
        // transform), 1 = YCbCr, 2 = YCCK. Determines how 4-component scans are
        // interpreted (raw CMYK vs YCCK) and signals Adobe's inverted-CMYK convention.
        private int _adobeTransform = -1;

        public JpegReader(byte[] data)
        {
            _data = data;
            _pos = 0;
        }

        public void Parse()
        {
            // Expect SOI
            if (Read16() != 0xFFD8)
                throw new InvalidDataException("Not a JPEG file");

            while (_pos < _data.Length)
            {
                var marker = ReadMarker();
                switch (marker)
                {
                    case 0xFFC0: // SOF0 — Baseline DCT
                        ParseSOF();
                        break;
                    case 0xFFC2: // SOF2 — Progressive (not supported, try baseline path)
                        ParseSOF();
                        break;
                    case 0xFFC4: // DHT
                        ParseDHT();
                        break;
                    case 0xFFDB: // DQT
                        ParseDQT();
                        break;
                    case 0xFFDD: // DRI
                        ParseDRI();
                        break;
                    case 0xFFEE: // APP14 — Adobe colour-transform marker
                        ParseAPP14();
                        break;
                    case 0xFFDA: // SOS
                        ParseSOS();
                        DecodeScan();
                        break;
                    case 0xFFD9: // EOI
                        return;
                    default:
                        // Skip unknown markers (APPn, COM, etc.)
                        if (marker >= 0xFF01 && _pos + 1 < _data.Length)
                        {
                            var len = Read16();
                            _pos += len - 2;
                        }
                        break;
                }
            }
        }

        private int ReadMarker()
        {
            // Find 0xFF followed by non-zero marker byte
            while (_pos < _data.Length - 1)
            {
                if (_data[_pos] == 0xFF)
                {
                    var b = _data[_pos + 1];
                    if (b != 0x00 && b != 0xFF)
                    {
                        _pos += 2;
                        return 0xFF00 | b;
                    }
                }
                _pos++;
            }
            return 0;
        }

        private int Read16()
        {
            var val = (_data[_pos] << 8) | _data[_pos + 1];
            _pos += 2;
            return val;
        }

        private void ParseSOF()
        {
            var len = Read16();
            var precision = _data[_pos++]; // bits per sample (usually 8)
            Height = Read16();
            Width = Read16();
            Components = _data[_pos++];

            _components = new ComponentInfo[Components];
            _maxH = 1;
            _maxV = 1;
            for (var i = 0; i < Components; i++)
            {
                var id = _data[_pos++];
                var sampling = _data[_pos++];
                var h = (sampling >> 4) & 0xF;
                var v = sampling & 0xF;
                var qtId = _data[_pos++];
                _components[i] = new ComponentInfo(id, h, v, qtId);
                if (h > _maxH) _maxH = h;
                if (v > _maxV) _maxV = v;
            }
        }

        private void ParseDHT()
        {
            var len = Read16();
            var end = _pos + len - 2;
            while (_pos < end)
            {
                var info = _data[_pos++];
                var tableClass = (info >> 4) & 0xF; // 0=DC, 1=AC
                var tableId = info & 0xF;

                // Read code counts for each bit length (1–16)
                var counts = new int[17];
                var total = 0;
                for (var i = 1; i <= 16; i++)
                {
                    counts[i] = _data[_pos++];
                    total += counts[i];
                }

                // Read symbol values
                var symbols = new byte[total];
                Array.Copy(_data, _pos, symbols, 0, total);
                _pos += total;

                var table = BuildHuffmanTable(counts, symbols);
                if (tableClass == 0)
                    _dcTables[tableId] = table;
                else
                    _acTables[tableId] = table;
            }
        }

        private void ParseDQT()
        {
            var len = Read16();
            var end = _pos + len - 2;
            while (_pos < end)
            {
                var info = _data[_pos++];
                var precision = (info >> 4) & 0xF; // 0=8-bit, 1=16-bit
                var id = info & 0xF;

                // DQT data is in zigzag order; remap to natural (row-major) order
                // so it aligns with the block[] array after DecodeBlock zigzag mapping
                var rawQt = new int[64];
                for (var i = 0; i < 64; i++)
                    rawQt[i] = precision == 0 ? _data[_pos++] : Read16();
                _quantTables[id] = new int[64];
                for (var i = 0; i < 64; i++)
                    _quantTables[id][ZigZag[i]] = rawQt[i];
            }
        }

        private void ParseDRI()
        {
            Read16(); // length
            _restartInterval = Read16();
        }

        private void ParseAPP14()
        {
            var len = Read16();
            var end = _pos + len - 2;
            // Adobe APP14 payload: "Adobe" + version(2) + flags0(2) + flags1(2) + transform(1).
            if (len >= 14 && _pos + 11 < _data.Length
                && _data[_pos] == (byte)'A' && _data[_pos + 1] == (byte)'d' && _data[_pos + 2] == (byte)'o'
                && _data[_pos + 3] == (byte)'b' && _data[_pos + 4] == (byte)'e')
            {
                _adobeTransform = _data[end - 1];
            }
            _pos = end;
        }

        private int[] _scanComponentIndices = [];
        private int[] _scanDcTableIds = [];
        private int[] _scanAcTableIds = [];

        private void ParseSOS()
        {
            var len = Read16();
            var numComponents = _data[_pos++];
            _scanComponentIndices = new int[numComponents];
            _scanDcTableIds = new int[numComponents];
            _scanAcTableIds = new int[numComponents];

            for (var i = 0; i < numComponents; i++)
            {
                var componentId = _data[_pos++];
                var tables = _data[_pos++];
                _scanDcTableIds[i] = (tables >> 4) & 0xF;
                _scanAcTableIds[i] = tables & 0xF;

                // Find component index by ID
                for (var j = 0; j < _components.Length; j++)
                {
                    if (_components[j].Id == componentId)
                    {
                        _scanComponentIndices[i] = j;
                        break;
                    }
                }
            }

            _pos += 3; // Ss, Se, AhAl (spectral selection — used in progressive)
        }

        private void DecodeScan()
        {
            var mcuW = _maxH * 8; // MCU width in pixels
            var mcuH = _maxV * 8; // MCU height in pixels
            var mcuCols = (Width + mcuW - 1) / mcuW;
            var mcuRows = (Height + mcuH - 1) / mcuH;

            // Allocate component buffers (full MCU-aligned dimensions)
            var buffers = new int[Components][];
            var bufWidths = new int[Components];
            var bufHeights = new int[Components];
            for (var i = 0; i < Components; i++)
            {
                var cw = mcuCols * _components[i].H * 8;
                var ch = mcuRows * _components[i].V * 8;
                buffers[i] = new int[cw * ch];
                bufWidths[i] = cw;
                bufHeights[i] = ch;
            }

            var bits = new BitStream(_data, _pos);
            var dcPred = new int[Components];
            var restartCounter = 0;

            for (var mcuRow = 0; mcuRow < mcuRows; mcuRow++)
            {
                for (var mcuCol = 0; mcuCol < mcuCols; mcuCol++)
                {
                    // Check restart interval
                    if (_restartInterval > 0 && restartCounter == _restartInterval)
                    {
                        bits.AlignByte();
                        // Skip restart marker (0xFF 0xDn)
                        bits.SkipRestartMarker();
                        Array.Clear(dcPred);
                        restartCounter = 0;
                    }

                    // Decode each component's blocks in this MCU
                    for (var ci = 0; ci < _scanComponentIndices.Length; ci++)
                    {
                        var compIdx = _scanComponentIndices[ci];
                        var comp = _components[compIdx];
                        var dcTable = _dcTables[_scanDcTableIds[ci]];
                        var acTable = _acTables[_scanAcTableIds[ci]];
                        var qt = _quantTables[comp.QtId] ?? _quantTables[0];

                        for (var bv = 0; bv < comp.V; bv++)
                        {
                            for (var bh = 0; bh < comp.H; bh++)
                            {
                                var block = new int[64];
                                DecodeBlock(bits, block, ref dcPred[compIdx], dcTable!, acTable!);
                                Dequantize(block, qt);
                                IDCT(block);

                                // Write block to component buffer
                                var px = (mcuCol * comp.H + bh) * 8;
                                var py = (mcuRow * comp.V + bv) * 8;
                                var bw = bufWidths[compIdx];
                                for (var y = 0; y < 8; y++)
                                {
                                    var dst = (py + y) * bw + px;
                                    var src = y * 8;
                                    for (var x = 0; x < 8; x++)
                                        buffers[compIdx][dst + x] = Clamp(block[src + x] + 128);
                                }
                            }
                        }
                    }
                    restartCounter++;
                }
            }

            _pos = bits.BytePosition;

            // Convert to output pixels
            if (Components == 1)
            {
                Pixels = new byte[Width * Height];
                for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                        Pixels[y * Width + x] = (byte)buffers[0][y * bufWidths[0] + x];
            }
            else if (Components == 3)
            {
                // A 3-channel JPEG is usually YCbCr, but Adobe images may store
                // direct RGB. An APP14 marker with transform 0 means RGB (1 means
                // YCbCr); with no marker, infer from the component IDs — 'R','G','B'
                // (82,71,66) is direct RGB, otherwise assume YCbCr. Applying the
                // YCbCr matrix to already-RGB samples flips colours (green->magenta).
                bool ycbcr = _adobeTransform >= 0
                    ? _adobeTransform != 0
                    : !(_components[0].Id == 82 && _components[1].Id == 71 && _components[2].Id == 66);
                Pixels = new byte[Width * Height * 3];
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        // Upsample chroma components
                        var sy0 = y * _components[0].V / _maxV;
                        var sx0 = x * _components[0].H / _maxH;
                        int yVal = buffers[0][sy0 * bufWidths[0] + sx0];

                        var sy1 = y * _components[1].V / _maxV;
                        var sx1 = x * _components[1].H / _maxH;
                        int cb = buffers[1][sy1 * bufWidths[1] + sx1];

                        var sy2 = y * _components[2].V / _maxV;
                        var sx2 = x * _components[2].H / _maxH;
                        int cr = buffers[2][sy2 * bufWidths[2] + sx2];

                        var idx = (y * Width + x) * 3;
                        if (!ycbcr)
                        {
                            // Samples are already R, G, B.
                            Pixels[idx] = (byte)Clamp(yVal);
                            Pixels[idx + 1] = (byte)Clamp(cb);
                            Pixels[idx + 2] = (byte)Clamp(cr);
                            continue;
                        }

                        // YCbCr to RGB
                        var r = yVal + 1.402 * (cr - 128);
                        var g = yVal - 0.344136 * (cb - 128) - 0.714136 * (cr - 128);
                        var b = yVal + 1.772 * (cb - 128);

                        Pixels[idx] = (byte)Clamp((int)(r + 0.5));
                        Pixels[idx + 1] = (byte)Clamp((int)(g + 0.5));
                        Pixels[idx + 2] = (byte)Clamp((int)(b + 0.5));
                    }
                }
            }
            else if (Components == 4)
            {
                // 4-component JPEG = CMYK or YCCK (Adobe). Convert to RGB here and
                // report 3 components so every caller takes the uniform RGB path.
                // YCCK (transform 2): the YCbCr triple decodes to the INVERTED C/M/Y
                // (chR = 255-Cink …) while K is stored DIRECTLY, so display = (1-C)(1-K)
                // becomes chR·(255-K)/255. Raw Adobe CMYK (transform 0) stores all four
                // channels inverted, so the same product uses K directly: chR·K/255.
                bool ycck = _adobeTransform == 2;
                Pixels = new byte[Width * Height * 3];
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var sy0 = y * _components[0].V / _maxV;
                        var sx0 = x * _components[0].H / _maxH;
                        int s0 = buffers[0][sy0 * bufWidths[0] + sx0];

                        var sy1 = y * _components[1].V / _maxV;
                        var sx1 = x * _components[1].H / _maxH;
                        int s1 = buffers[1][sy1 * bufWidths[1] + sx1];

                        var sy2 = y * _components[2].V / _maxV;
                        var sx2 = x * _components[2].H / _maxH;
                        int s2 = buffers[2][sy2 * bufWidths[2] + sx2];

                        var sy3 = y * _components[3].V / _maxV;
                        var sx3 = x * _components[3].H / _maxH;
                        int k = buffers[3][sy3 * bufWidths[3] + sx3];

                        int chR, chG, chB;
                        if (ycck)
                        {
                            chR = Clamp((int)(s0 + 1.402 * (s2 - 128) + 0.5));
                            chG = Clamp((int)(s0 - 0.344136 * (s1 - 128) - 0.714136 * (s2 - 128) + 0.5));
                            chB = Clamp((int)(s0 + 1.772 * (s1 - 128) + 0.5));
                        }
                        else
                        {
                            chR = s0; chG = s1; chB = s2;
                        }

                        int r, g, b;
                        if (ycck)
                        {
                            r = chR * (255 - k) / 255;
                            g = chG * (255 - k) / 255;
                            b = chB * (255 - k) / 255;
                        }
                        else
                        {
                            // Raw CMYK (Adobe transform 0 or no APP14 marker): the scan samples
                            // are ink amounts, so RGB = (1-C)(1-K). The earlier "Adobe ⇒ inverted
                            // (C·K)" special case produced black headers on CMYK-JPEG logos
                            // — the decoder already yields ink values, not the
                            // inverted-stored values that formula assumed.
                            r = (255 - chR) * (255 - k) / 255;
                            g = (255 - chG) * (255 - k) / 255;
                            b = (255 - chB) * (255 - k) / 255;
                        }

                        var idx = (y * Width + x) * 3;
                        Pixels[idx] = (byte)r;
                        Pixels[idx + 1] = (byte)g;
                        Pixels[idx + 2] = (byte)b;
                    }
                }
                Components = 3; // output buffer is RGB
            }
        }

        private static void DecodeBlock(BitStream bits, int[] block, ref int dcPred,
            HuffmanTable dcTable, HuffmanTable acTable)
        {
            // DC coefficient
            var dcCategory = DecodeHuffman(bits, dcTable);
            var dcDiff = dcCategory > 0 ? ReceiveExtend(bits, dcCategory) : 0;
            dcPred += dcDiff;
            block[0] = dcPred;

            // AC coefficients (zigzag order)
            var k = 1;
            while (k < 64)
            {
                var rs = DecodeHuffman(bits, acTable);
                var r = (rs >> 4) & 0xF; // run length of zeros
                var s = rs & 0xF;         // category (bit size)

                if (s == 0)
                {
                    if (r == 0) break; // EOB
                    if (r == 0xF) { k += 16; continue; } // ZRL — 16 zeros
                    break;
                }

                k += r;
                if (k < 64)
                    block[ZigZag[k]] = ReceiveExtend(bits, s);
                k++;
            }
        }

        private static int DecodeHuffman(BitStream bits, HuffmanTable table)
        {
            var code = 0;
            for (var len = 1; len <= 16; len++)
            {
                code = (code << 1) | bits.ReadBit();
                if (code <= table.MaxCode[len])
                {
                    var idx = table.ValOffset[len] + code;
                    if (idx >= 0 && idx < table.Values.Length)
                        return table.Values[idx];
                }
            }
            return 0; // shouldn't happen with valid data
        }

        private static int ReceiveExtend(BitStream bits, int category)
        {
            var value = 0;
            for (var i = 0; i < category; i++)
                value = (value << 1) | bits.ReadBit();

            // Extend sign: if MSB is 0, value is negative
            if (value < (1 << (category - 1)))
                value -= (1 << category) - 1;
            return value;
        }

        private static void Dequantize(int[] block, int[] qt)
        {
            for (var i = 0; i < 64; i++)
                block[i] *= qt[i];
        }

        /// <summary>
        /// Inverse DCT using the AAN (Arai, Agui, Nakajima) fast algorithm.
        /// Operates in-place on a 64-element block in zigzag-reordered form.
        /// </summary>
        private static void IDCT(int[] block)
        {
            // Use double workspace for precision
            var ws = new double[64];
            for (var i = 0; i < 64; i++) ws[i] = block[i];

            // 1D IDCT on rows
            for (var row = 0; row < 8; row++)
            {
                var off = row * 8;
                IDCT1D(ws, off);
            }

            // 1D IDCT on columns
            for (var col = 0; col < 8; col++)
            {
                // Copy column to temp
                var tmp = new double[8];
                for (var i = 0; i < 8; i++) tmp[i] = ws[i * 8 + col];
                IDCT1D(tmp, 0);
                for (var i = 0; i < 8; i++) ws[i * 8 + col] = tmp[i];
            }

            // Scale and round
            for (var i = 0; i < 64; i++)
                block[i] = (int)(ws[i] / 8.0 + 0.5);
        }

        /// <summary>
        /// 1D IDCT on 8 elements starting at offset.
        /// Based on the IJG (Independent JPEG Group) "slow" integer IDCT algorithm.
        /// </summary>
        private static void IDCT1D(double[] data, int off)
        {
            var s0 = data[off];
            var s1 = data[off + 1];
            var s2 = data[off + 2];
            var s3 = data[off + 3];
            var s4 = data[off + 4];
            var s5 = data[off + 5];
            var s6 = data[off + 6];
            var s7 = data[off + 7];

            // Even part — rotation of s2, s6
            var p2p6 = (s2 + s6) * 0.541196100; // FIX_0_541196100
            var t2 = p2p6 - s6 * 1.847759065;   // FIX_1_847759065
            var t3 = p2p6 + s2 * 0.765366865;   // FIX_0_765366865

            var t0 = s0 + s4;
            var t1 = s0 - s4;

            var e0 = t0 + t3;
            var e3 = t0 - t3;
            var e1 = t1 + t2;
            var e2 = t1 - t2;

            // Odd part
            var z1 = s7 + s1;
            var z2 = s5 + s3;
            var z3 = s7 + s3;
            var z4 = s5 + s1;
            var z5 = (z3 + z4) * 1.175875602; // FIX_1_175875602

            var t4 = s7 * 0.298631336;   // FIX_0_298631336
            var t5 = s5 * 2.053119869;   // FIX_2_053119869
            var t6 = s3 * 3.072711026;   // FIX_3_072711026
            var t7 = s1 * 1.501321110;   // FIX_1_501321110
            var z1a = z1 * -0.899976223; // FIX_0_899976223
            var z2a = z2 * -2.562915447; // FIX_2_562915447
            var z3a = z3 * -1.961570560 + z5;
            var z4a = z4 * -0.390180644 + z5;

            t4 += z1a + z3a;
            t5 += z2a + z4a;
            t6 += z2a + z3a;
            t7 += z1a + z4a;

            data[off]     = e0 + t7;
            data[off + 7] = e0 - t7;
            data[off + 1] = e1 + t6;
            data[off + 6] = e1 - t6;
            data[off + 2] = e2 + t5;
            data[off + 5] = e2 - t5;
            data[off + 3] = e3 + t4;
            data[off + 4] = e3 - t4;
        }

        private static int Clamp(int v) => v < 0 ? 0 : v > 255 ? 255 : v;

        private static HuffmanTable BuildHuffmanTable(int[] counts, byte[] symbols)
        {
            var table = new HuffmanTable { Values = symbols };
            table.MinCode = new int[17];
            table.MaxCode = new int[17];
            table.ValOffset = new int[17];

            var code = 0;
            var si = 0;
            for (var bits = 1; bits <= 16; bits++)
            {
                table.MinCode[bits] = code;
                table.ValOffset[bits] = si - code;
                for (var i = 0; i < counts[bits]; i++)
                {
                    code++;
                    si++;
                }
                table.MaxCode[bits] = code - 1;
                code <<= 1;
            }
            return table;
        }

        // JPEG zigzag order
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
    }

    private readonly record struct ComponentInfo(int Id, int H, int V, int QtId);

    private sealed class HuffmanTable
    {
        public byte[] Values = [];
        public int[] MinCode = new int[17];
        public int[] MaxCode = new int[17];
        public int[] ValOffset = new int[17];
    }

    /// <summary>Bit-level reader for entropy-coded JPEG data, handling byte stuffing (0xFF00).</summary>
    private sealed class BitStream
    {
        private readonly byte[] _data;
        private int _pos;
        private int _bits;
        private int _bitsLeft;

        public BitStream(byte[] data, int startPos)
        {
            _data = data;
            _pos = startPos;
        }

        public int BytePosition => _pos;

        public int ReadBit()
        {
            if (_bitsLeft == 0)
            {
                _bits = NextByte();
                _bitsLeft = 8;
            }
            _bitsLeft--;
            return (_bits >> _bitsLeft) & 1;
        }

        public void AlignByte()
        {
            _bitsLeft = 0;
        }

        public void SkipRestartMarker()
        {
            // After aligning, look for 0xFF 0xDn restart marker
            while (_pos < _data.Length - 1)
            {
                if (_data[_pos] == 0xFF && _data[_pos + 1] >= 0xD0 && _data[_pos + 1] <= 0xD7)
                {
                    _pos += 2;
                    return;
                }
                _pos++;
            }
        }

        private int NextByte()
        {
            if (_pos >= _data.Length) return 0;
            var b = _data[_pos++];
            // JPEG byte stuffing: 0xFF is followed by 0x00 (which should be skipped)
            if (b == 0xFF)
            {
                if (_pos < _data.Length)
                {
                    var next = _data[_pos];
                    if (next == 0x00)
                        _pos++; // skip the stuffed zero
                    // If it's a marker (not 0x00), don't consume it
                }
            }
            return b;
        }
    }
}
