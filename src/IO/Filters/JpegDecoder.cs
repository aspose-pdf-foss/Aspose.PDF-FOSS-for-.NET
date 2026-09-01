namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Pure C# JPEG (JFIF) decoder.
/// Decodes SOF0 (baseline), SOF1 (extended sequential, 8-bit Huffman) and
/// SOF2 (progressive) DCT JPEG streams into raw RGB or grayscale pixel arrays.
/// Supports chroma subsampling modes 4:4:4, 4:2:2, and 4:2:0.
/// </summary>
internal static partial class JpegDecoder
{
    /// <summary>
    /// Decode a JPEG byte stream into raw pixel data.
    /// </summary>
    /// <param name="data">JPEG bytes.</param>
    /// <param name="invertCmyk">Invert 4-component (CMYK) samples before the ink→RGB
    /// conversion. Set when the image's PDF dictionary carries an inverting /Decode
    /// array ([1 0 1 0 1 0 1 0]) — the embedder's way of saying the file stores
    /// Adobe-inverted CMYK rather than direct ink values.</param>
    /// <returns>Decoded pixels (RGB or grayscale), width, height, component count.</returns>
    public static (byte[] pixels, int width, int height, int components) Decode(byte[] data, bool invertCmyk = false)
    {
        var reader = new JpegReader(data) { InvertCmyk = invertCmyk };
        reader.Parse();
        return (reader.Pixels, reader.Width, reader.Height, reader.Components);
    }

    private sealed partial class JpegReader
    {
        private readonly byte[] _data;
        private int _pos;

        // Frame info
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int Components { get; private set; }
        public byte[] Pixels { get; private set; } = [];

        /// <summary>Invert CMYK samples before the ink→RGB conversion (/Decode [1 0 …]).</summary>
        public bool InvertCmyk { get; init; }

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
                    case 0xFFC1: // SOF1 — Extended sequential DCT (8-bit Huffman decodes as baseline)
                        ParseSOF();
                        break;
                    case 0xFFC2: // SOF2 — Progressive
                        ParseSOF();
                        _progressive = true;
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
                        if (_progressive) DecodeProgressiveScan();
                        else DecodeScan();
                        break;
                    case 0xFFD9: // EOI
                        if (_progressive) FinishProgressive();
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

            // Truncated stream without an EOI — emit what accumulated so far.
            if (_progressive) FinishProgressive();
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

            _ss = _data[_pos++];
            _se = _data[_pos++];
            var ahAl = _data[_pos++];
            _ah = (ahAl >> 4) & 0xF;
            _al = ahAl & 0xF;
        }

        // ── Progressive (SOF2) support ──────────────────────────────────────
        //
        // A progressive JPEG carries several SOS scans that each deliver part of
        // the DCT coefficients (spectral bands Ss..Se, successive-approximation
        // bit positions Ah/Al). Coefficients accumulate in _coefs (natural order
        // per 8x8 block); the final image is produced at EOI by dequantising and
        // inverse-transforming every block.

        private bool _progressive;
        private int _ss, _se, _ah, _al;
        private int[][] _coefs = [];
        private int[] _blocksPerLine = [];   // MCU-aligned block columns per component
        private int[] _blocksPerCol = [];    // MCU-aligned block rows per component
        private int _eobrun;
        private bool _outputDone;

        private void EnsureCoefficientArrays()
        {
            if (_coefs.Length != 0) return;
            var mcuCols = (Width + _maxH * 8 - 1) / (_maxH * 8);
            var mcuRows = (Height + _maxV * 8 - 1) / (_maxV * 8);
            _coefs = new int[_components.Length][];
            _blocksPerLine = new int[_components.Length];
            _blocksPerCol = new int[_components.Length];
            for (var i = 0; i < _components.Length; i++)
            {
                _blocksPerLine[i] = mcuCols * _components[i].H;
                _blocksPerCol[i] = mcuRows * _components[i].V;
                _coefs[i] = new int[_blocksPerLine[i] * _blocksPerCol[i] * 64];
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

        /// <summary>
        /// Inverse DCT using the AAN (Arai, Agui, Nakajima) fast algorithm.
        /// Operates in-place on a 64-element block in zigzag-reordered form.
        /// </summary>
        // IJG "slow" integer IDCT constants: values scaled by 2^CONST_BITS.
        private const int ConstBits = 13;
        private const int Pass1Bits = 2;
        private const int Fix_0_298631336 = 2446;
        private const int Fix_0_390180644 = 3196;
        private const int Fix_0_541196100 = 4433;
        private const int Fix_0_765366865 = 6270;
        private const int Fix_0_899976223 = 7373;
        private const int Fix_1_175875602 = 9633;
        private const int Fix_1_501321110 = 12299;
        private const int Fix_1_847759065 = 15137;
        private const int Fix_1_961570560 = 16069;
        private const int Fix_2_053119869 = 16819;
        private const int Fix_2_562915447 = 20995;
        private const int Fix_3_072711026 = 25172;

        private static int Descale(long x, int n) => (int)((x + (1L << (n - 1))) >> n);

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

        /// <summary>Read <paramref name="count"/> bits MSB-first as an unsigned value.</summary>
        public int ReadBits(int count)
        {
            var v = 0;
            for (var i = 0; i < count; i++)
                v = (v << 1) | ReadBit();
            return v;
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
