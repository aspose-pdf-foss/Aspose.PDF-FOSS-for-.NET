using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

/// <summary>
/// Pure C# JBIG2 decoder for PDF streams (ITU-T T.88).
///
/// Status: segment-level infrastructure is in place — header pre-scan (with referred-to-segment
/// tracking, T.88 §7.2.5 ref-byte-size autodetection), per-type dispatch (0/6/7/38/39/48), the
/// arithmetic-integer decoders (IA / IAID per T.88 Annex A), a symbol-dictionary parser (§6.5),
/// and a text-region parser (§6.4). Generic-region decoding (38/39) uses the existing arithmetic
/// generic-region template machinery. The QM arithmetic decoder follows the T.88 Annex F
/// "software convention" form with proper NMPS / NLPS / SWITCH state-transition tables.
///
/// Not yet supported (the affected segments are intentionally no-op'd):
///   * 36/40 — generic refinement region segments
///   * 4 — intermediate text region
///   * 16/22/23/24 — pattern dictionaries and halftone regions
///   * Symbol dictionaries with refinement aggregation (SDREFAGG=1)
///   * Huffman-coded variants of symbol dictionary and text region (SDHUFF=1 / SBHUFF=1)
/// </summary>
internal static partial class Jbig2Decoder
{
    internal static readonly bool Jbig2Debug =
        System.Environment.GetEnvironmentVariable("ASPOSE_FOSS_JBIG2DEBUG") == "1";

    public static byte[] Decode(byte[] data, byte[]? globals)
    {
        var combined = CombineGlobalsAndPage(globals, data);
        try
        {
            var ctx = new DecodeContext(combined);
            return ctx.DecodeAll();
        }
        catch (System.Exception ex)
        {
            if (System.Environment.GetEnvironmentVariable("ASPOSE_FOSS_JBIG2DEBUG") == "1")
                System.Console.Error.WriteLine("[jbig2] whole-stream decode failed: " + ex.Message);
            return [];
        }
    }

    private static byte[] CombineGlobalsAndPage(byte[]? globals, byte[] pageData)
    {
        if (globals is null || globals.Length == 0) return pageData;
        var combined = new byte[globals.Length + pageData.Length];
        Array.Copy(globals, 0, combined, 0, globals.Length);
        Array.Copy(pageData, 0, combined, globals.Length, pageData.Length);
        return combined;
    }

    // ────────────────────────────────────────────────────────────────────────
    // Top-level decoder state
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Metadata about a single JBIG2 segment, captured during pre-scan.</summary>
    private sealed class SegmentHeader
    {
        public int Number;
        public int Type;
        public int[] ReferredTo = [];
        public int PageAssoc;
        public int DataStart;
        public int DataLength;
    }

    /// <summary>Holds per-decode state: parsed headers, current page, accumulated symbol library.</summary>
    private sealed partial class DecodeContext
    {
        private readonly byte[] _data;
        private readonly List<SegmentHeader> _headers = new();

        // Page state
        private int _pageWidth;
        private int _pageHeight;
        private int _pageRowBytes;
        private byte[]? _pageBitmap;

        // Symbol library — keyed by symbol-dictionary segment number; value is the exported symbols
        // in order. Text regions reference these by segment number via their ReferredTo list.
        private readonly Dictionary<int, Jbig2Bitmap[]> _symbolDicts = new();

        public DecodeContext(byte[] data) => _data = data;

        public byte[] DecodeAll()
        {
            ScanHeaders();
            foreach (var hdr in _headers)
                ProcessSegment(hdr);
            return _pageBitmap ?? [];
        }

        /// <summary>
        /// Pre-scan all segment headers. Per T.88 §7.2.5 the referred-to-segment-number
        /// field width is PER SEGMENT, decided by the referring segment's OWN number:
        /// one byte while it is ≤ 256, two while ≤ 65536, else four (referred numbers
        /// are always smaller than the referring one). A file-global width derived from
        /// the max segment number mis-parses the boundary file whose last segment is
        /// exactly 256 (its refs are still 1-byte; a 2-byte read produced ghost refs
        /// and dropped the globals dictionary — a blank mask).
        /// </summary>
        private void ScanHeaders()
        {
            _headers.AddRange(ScanAllHeaders());
        }

        private List<SegmentHeader> ScanAllHeaders()
        {
            var result = new List<SegmentHeader>();
            var pos = 0;

            // Skip JBIG2 file header if present (magic bytes: 0x97 0x4A 0x42 0x32)
            if (_data.Length >= 8 && _data[0] == 0x97 && _data[1] == 0x4A &&
                _data[2] == 0x42 && _data[3] == 0x32)
            {
                var flags = _data[8];
                pos = 9;
                if ((flags & 0x01) == 0) pos += 4; // known number of pages
            }

            while (pos < _data.Length - 6)
            {
                if (!TryParseSegmentHeader(ref pos, out var hdr))
                    break;
                result.Add(hdr);
                pos = hdr.DataStart + hdr.DataLength;
            }

            return result;
        }

        private bool TryParseSegmentHeader(ref int pos, out SegmentHeader hdr)
        {
            hdr = new SegmentHeader();
            if (pos + 6 > _data.Length) return false;

            // 4-byte segment number
            hdr.Number = ReadInt32BE(_data, pos);
            pos += 4;

            if (pos >= _data.Length) return false;
            var flags = _data[pos++];
            hdr.Type = flags & 0x3F;
            var pageAssocSize = (flags & 0x40) != 0 ? 4 : 1;

            // Referred-to-segment count + retention flags
            if (pos >= _data.Length) return false;
            var refFlags = _data[pos++];
            var refCount = (refFlags >> 5) & 0x07;
            if (refCount == 7)
            {
                // Long form: 4-byte count + retain flags
                if (pos + 3 > _data.Length) return false;
                refCount = ((refFlags & 0x1F) << 24) |
                           (_data[pos] << 16) | (_data[pos + 1] << 8) | _data[pos + 2];
                pos += 3;
                // Retention flags: ceil((refCount + 1) / 8) bytes
                var retainBytes = (refCount + 8) / 8;
                pos += retainBytes;
            }
            else
            {
                // Short form: retention flag bits live in the same byte (bits 0-4)
            }

            // Referred-to segment numbers — width from THIS segment's number (§7.2.5).
            var refByteSize = hdr.Number <= 256 ? 1 : hdr.Number <= 65536 ? 2 : 4;
            hdr.ReferredTo = new int[Math.Max(0, refCount)];
            for (var i = 0; i < refCount; i++)
            {
                if (pos + refByteSize > _data.Length) return false;
                hdr.ReferredTo[i] = ReadIntBE(_data, pos, refByteSize);
                pos += refByteSize;
            }

            // Page association
            if (pageAssocSize == 4)
            {
                if (pos + 4 > _data.Length) return false;
                hdr.PageAssoc = ReadInt32BE(_data, pos);
                pos += 4;
            }
            else
            {
                if (pos >= _data.Length) return false;
                hdr.PageAssoc = _data[pos++];
            }

            // Segment data length
            if (pos + 4 > _data.Length) return false;
            hdr.DataLength = ReadInt32BE(_data, pos);
            pos += 4;
            if (hdr.DataLength == -1) hdr.DataLength = _data.Length - pos;
            if (hdr.DataLength < 0 || pos + hdr.DataLength > _data.Length)
                hdr.DataLength = _data.Length - pos;

            hdr.DataStart = pos;
            return true;
        }

        private void ProcessSegment(SegmentHeader hdr)
        {
            try
            {
                switch (hdr.Type)
                {
                    case 0:
                        DecodeSymbolDictionary(hdr);
                        break;
                    case 6:
                    case 7:
                        DecodeTextRegion(hdr);
                        break;
                    case 38:
                    case 39:
                        DecodeGenericRegion(hdr);
                        break;
                    case 48:
                        DecodePageInfo(hdr);
                        break;
                    case 16:
                        DecodePatternDictionary(hdr);
                        break;
                    case 20:
                    case 22:
                    case 23:
                        DecodeHalftoneRegion(hdr);
                        break;
                    // 36/40 (refinement), 4 (intermediate text region), 50/51
                    // (end of page/file): no-op for us.
                }
            }
            catch (System.Exception ex)
            {
                // Best-effort decode: a single corrupt segment shouldn't abort the whole stream.
                if (System.Environment.GetEnvironmentVariable("ASPOSE_FOSS_JBIG2DEBUG") == "1")
                    System.Console.Error.WriteLine("[jbig2] segment " + hdr.Number + " type " + hdr.Type + " failed: " + ex.Message);
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Page-info segment (48) — T.88 §7.4.8
        // ────────────────────────────────────────────────────────────────────

        private void DecodePageInfo(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 19 > _data.Length) return;
            _pageWidth = ReadInt32BE(_data, p);
            _pageHeight = ReadInt32BE(_data, p + 4);
            // Skip resolution (8 bytes); flags @ p+16; striping @ p+17/18
            var pageFlags = _data[p + 16];

            // Striped pages declare height = 0xFFFFFFFF; size will be discovered from segments.
            if (_pageHeight <= 0)
            {
                _pageHeight = 0;
                _pageRowBytes = 0;
                _pageBitmap = null;
                return;
            }
            if (_pageWidth <= 0) return;

            _pageRowBytes = (_pageWidth + 7) / 8;
            _pageBitmap = new byte[_pageRowBytes * _pageHeight];

            if ((pageFlags & 0x04) != 0) // default pixel = 1
                Array.Fill(_pageBitmap, (byte)0xFF);
        }

        // ────────────────────────────────────────────────────────────────────
        // Generic region segments (38/39) — T.88 §7.4.6 + §6.2
        // ────────────────────────────────────────────────────────────────────

        // ────────────────────────────────────────────────────────────────────
        // Pattern dictionary (16) — T.88 §7.4.4 + §6.7
        // ────────────────────────────────────────────────────────────────────

        // Decoded pattern dictionaries, keyed by segment number; halftone regions
        // reference these via their ReferredTo list.
        private readonly Dictionary<int, Jbig2Bitmap[]> _patternDicts = new();

        // ────────────────────────────────────────────────────────────────────
        // Halftone region (20/22/23) — T.88 §7.4.5 + §6.6 (gray-scale image §C.5)
        // ────────────────────────────────────────────────────────────────────

        // ────────────────────────────────────────────────────────────────────
        // Symbol dictionary segment (0) — T.88 §7.4.2 + §6.5
        // ────────────────────────────────────────────────────────────────────

        // ────────────────────────────────────────────────────────────────────
        // Text region segments (6/7) — T.88 §7.4.3 + §6.4
        // ────────────────────────────────────────────────────────────────────

        // ────────────────────────────────────────────────────────────────────
        // Generic refinement region decoder (arithmetic) — T.88 §6.3
        // ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Decode a refined bitmap from a reference bitmap. The context combines the
        /// coding template (pixels of the bitmap being produced) with the reference
        /// template (pixels of the reference, shifted by the given offset). Template 0
        /// appends the two refinement AT pixels; template 1 uses fixed footprints.
        /// </summary>
        private static Jbig2Bitmap DecodeRefinement(ArithmeticDecoder ad, ArithmeticContext[] grCtx,
            int width, int height, int template, (int dx, int dy)[] at, Jbig2Bitmap reference,
            int offsetX, int offsetY)
        {
            (int dx, int dy)[] coding = template == 0
                ? new[] { (0, -1), (1, -1), (-1, 0), at[0] }
                : new (int, int)[] { (-1, -1), (0, -1), (1, -1), (-1, 0) };
            (int dx, int dy)[] refer = template == 0
                ? new[] { (0, -1), (1, -1), (-1, 0), (0, 0), (1, 0), (-1, 1), (0, 1), (1, 1), at[1] }
                : new (int, int)[] { (0, -1), (-1, 0), (0, 0), (1, 0), (0, 1), (1, 1) };

            var bm = new Jbig2Bitmap(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var ctx = 0;
                    foreach (var (dx, dy) in coding)
                        ctx = (ctx << 1) | bm.GetPixel(x + dx, y + dy);
                    foreach (var (dx, dy) in refer)
                        ctx = (ctx << 1) | reference.GetPixel(x + dx - offsetX, y + dy - offsetY);
                    if (ad.DecodeBit(grCtx[ctx]))
                        bm.Data[y * bm.RowBytes + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            return bm;
        }

        private static int SymCodeLength(int n)
        {
            // ceil(log2(n)) for n>=1
            var bits = 0;
            while ((1 << bits) < n) bits++;
            return bits == 0 ? 1 : bits;
        }

        // ────────────────────────────────────────────────────────────────────
        // Region → page composition
        // ────────────────────────────────────────────────────────────────────

        private void CompositeRegionOntoPage(Jbig2Bitmap region, int rx, int ry, int combOp)
        {
            if (_pageBitmap is null) return;
            var rRow = region.RowBytes;
            for (var y = 0; y < region.Height; y++)
            {
                var py = ry + y;
                if (py < 0 || py >= _pageHeight) continue;

                for (var x = 0; x < region.Width; x++)
                {
                    var px = rx + x;
                    if (px < 0 || px >= _pageWidth) continue;

                    var srcBit = (region.Data[y * rRow + (x >> 3)] >> (7 - (x & 7))) & 1;
                    var dstIdx = py * _pageRowBytes + (px >> 3);
                    var dstMask = (byte)(0x80 >> (px & 7));
                    var dstBit = (_pageBitmap[dstIdx] & dstMask) != 0 ? 1 : 0;

                    var result = combOp switch
                    {
                        0 => srcBit | dstBit,
                        1 => srcBit & dstBit,
                        2 => srcBit ^ dstBit,
                        3 => ~(srcBit ^ dstBit) & 1,
                        _ => srcBit,
                    };

                    if (result != 0) _pageBitmap[dstIdx] |= dstMask;
                    else _pageBitmap[dstIdx] &= (byte)~dstMask;
                }
            }
        }

        /// <summary>Decode a strict-T.6 (Group 4) MMR bitmap of the given size from
        /// <c>_data[p..p+len]</c> using the shared CCITT decoder (black = 1, no column shift).</summary>
        private Jbig2Bitmap DecodeMmrG4(int width, int height, int p, int len)
        {
            var avail = Math.Max(0, Math.Min(len, _data.Length - p));
            var seg = new byte[avail];
            Array.Copy(_data, p, seg, 0, avail);
            var reader = new CcittFaxDecodeFilter.CcittBitReader(seg);
            var rowBytes = (width + 7) / 8;
            var bytes = CcittFaxDecodeFilter.DecodeGroup4Region(reader, width, height);
            if (bytes.Length < rowBytes * height) Array.Resize(ref bytes, rowBytes * height);
            return new Jbig2Bitmap(width, height, rowBytes, bytes);
        }

        // ────────────────────────────────────────────────────────────────────
        // Byte-order helpers
        // ────────────────────────────────────────────────────────────────────

        private static int ReadInt32BE(byte[] data, int p)
            => (data[p] << 24) | (data[p + 1] << 16) | (data[p + 2] << 8) | data[p + 3];

        private static int ReadIntBE(byte[] data, int p, int byteSize) => byteSize switch
        {
            1 => data[p],
            2 => (data[p] << 8) | data[p + 1],
            4 => ReadInt32BE(data, p),
            _ => ReadInt32BE(data, p),
        };

    }

    // ────────────────────────────────────────────────────────────────────────
    // Jbig2Bitmap — 1-bit packed bitmap (MSB-first within each byte)
    // ────────────────────────────────────────────────────────────────────────

    private sealed class Jbig2Bitmap
    {
        public int Width { get; }
        public int Height { get; }
        public int RowBytes { get; }
        public byte[] Data { get; }

        public Jbig2Bitmap(int width, int height, bool defaultPixel = false)
        {
            Width = width;
            Height = height;
            RowBytes = (width + 7) / 8;
            Data = new byte[RowBytes * height];
            if (defaultPixel) Array.Fill(Data, (byte)0xFF);
        }

        public Jbig2Bitmap(int width, int height, int rowBytes, byte[] data)
        {
            Width = width;
            Height = height;
            RowBytes = rowBytes;
            Data = data;
        }

        public int GetPixel(int x, int y)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
            return (Data[y * RowBytes + (x >> 3)] >> (7 - (x & 7))) & 1;
        }

        public void SetPixel(int x, int y, int bit)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return;
            var idx = y * RowBytes + (x >> 3);
            var mask = (byte)(0x80 >> (x & 7));
            if (bit != 0) Data[idx] |= mask;
            else Data[idx] &= (byte)~mask;
        }

        public void CompositeAt(Jbig2Bitmap src, int destX, int destY, int combOp)
        {
            for (var y = 0; y < src.Height; y++)
            {
                var dy = destY + y;
                if ((uint)dy >= (uint)Height) continue;
                for (var x = 0; x < src.Width; x++)
                {
                    var dx = destX + x;
                    if ((uint)dx >= (uint)Width) continue;
                    var srcBit = src.GetPixel(x, y);
                    var dstBit = GetPixel(dx, dy);
                    var result = combOp switch
                    {
                        0 => srcBit | dstBit,
                        1 => srcBit & dstBit,
                        2 => srcBit ^ dstBit,
                        3 => ~(srcBit ^ dstBit) & 1,
                        _ => srcBit,
                    };
                    SetPixel(dx, dy, result);
                }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Generic region decoder (arithmetic) — T.88 §6.2
    // ────────────────────────────────────────────────────────────────────────

    // ────────────────────────────────────────────────────────────────────────
    // Integer arithmetic decoders — T.88 Annex A
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decoder for symbol IDs (IAID) — T.88 §A.3. Reads SBSYMCODELEN bits MSB-first
    /// with the running PREV register supplying the context.
    /// </summary>
    private sealed class IaidDecoder
    {
        private readonly ArithmeticDecoder _ad;
        private readonly int _bits;
        private readonly ArithmeticContext[] _ctx;

        public IaidDecoder(ArithmeticDecoder ad, int symCodeLen)
        {
            _ad = ad;
            _bits = symCodeLen;
            _ctx = new ArithmeticContext[1 << (symCodeLen + 1)];
            for (var i = 0; i < _ctx.Length; i++) _ctx[i] = new ArithmeticContext();
        }

        public int Decode()
        {
            var prev = 1;
            for (var i = 0; i < _bits; i++)
            {
                var b = _ad.DecodeBit(_ctx[prev]) ? 1 : 0;
                prev = (prev << 1) | b;
            }
            return prev - (1 << _bits);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Huffman-coded variant primitives (T.88 Annex B + §6.5/§6.4 SDHUFF/SBHUFF)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>MSB-first bit reader with byte-position tracking (Huffman paths
    /// interleave bit-coded fields with byte-aligned MMR / raw bitmap payloads).</summary>
    private sealed class HuffBitReader
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _pos;
        private int _bitPos; // 0..7 within _data[_pos]

        public HuffBitReader(byte[] data, int start, int end)
        {
            _data = data;
            _pos = start;
            _end = end;
        }

        /// <summary>Byte offset of the next unread byte after aligning.</summary>
        public int BytePos => _bitPos == 0 ? _pos : _pos + 1;

        public bool AtEnd => _pos >= _end && _bitPos == 0;

        public int ReadBit()
        {
            if (_pos >= _end) return 0;
            var bit = (_data[_pos] >> (7 - _bitPos)) & 1;
            if (++_bitPos == 8) { _bitPos = 0; _pos++; }
            return bit;
        }

        public int ReadBits(int count)
        {
            var v = 0;
            for (var i = 0; i < count; i++) v = (v << 1) | ReadBit();
            return v;
        }

        public void Align()
        {
            if (_bitPos != 0) { _bitPos = 0; _pos++; }
        }

        /// <summary>Skip <paramref name="count"/> bytes from the aligned position.</summary>
        public void SkipBytes(int count)
        {
            Align();
            _pos = Math.Min(_end, _pos + count);
        }
    }

    /// <summary>One line of a JBIG2 Huffman table (T.88 Annex B): a prefix code of
    /// <see cref="PrefLen"/> bits selects a range of 2^<see cref="RangeLen"/> values
    /// starting at <see cref="RangeLow"/> (or ending at it, for the lower-range line).</summary>
    private readonly struct HuffLine
    {
        public readonly int PrefLen, RangeLen, RangeLow;
        public readonly bool IsLower, IsOob;
        public HuffLine(int prefLen, int rangeLen, int rangeLow, bool isLower = false)
        { PrefLen = prefLen; RangeLen = rangeLen; RangeLow = rangeLow; IsLower = isLower; IsOob = false; }
        public HuffLine(int oobPrefLen)
        { PrefLen = oobPrefLen; RangeLen = 0; RangeLow = 0; IsLower = false; IsOob = true; }
    }

    /// <summary>A JBIG2 Huffman table: assigns canonical prefix codes to its lines
    /// (T.88 §B.3) and decodes values from a bit stream.</summary>
    private sealed class HuffTable
    {
        private readonly HuffLine[] _lines;
        private readonly int[] _codes;

        public HuffTable(HuffLine[] lines)
        {
            _lines = lines;
            _codes = new int[lines.Length];
            // Canonical assignment: count codes per length, derive each length's
            // first code, then hand codes out in line order.
            var maxLen = 0;
            foreach (var l in lines) if (l.PrefLen > maxLen) maxLen = l.PrefLen;
            var lenCount = new int[maxLen + 1];
            foreach (var l in lines) if (l.PrefLen > 0) lenCount[l.PrefLen]++;
            var firstCode = new int[maxLen + 2];
            lenCount[0] = 0;
            for (var len = 1; len <= maxLen; len++)
                firstCode[len] = (firstCode[len - 1] + lenCount[len - 1]) << 1;
            var nextCode = (int[])firstCode.Clone();
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].PrefLen == 0) { _codes[i] = -1; continue; }
                _codes[i] = nextCode[lines[i].PrefLen]++;
            }
        }

        /// <summary>Decode one value. Returns false for the OOB symbol (or on a
        /// code that matches no line — treated as end-of-data).</summary>
        public bool Decode(HuffBitReader r, out int value)
        {
            value = 0;
            var code = 0;
            for (var len = 1; len <= 32; len++)
            {
                code = (code << 1) | r.ReadBit();
                for (var i = 0; i < _lines.Length; i++)
                {
                    if (_lines[i].PrefLen != len || _codes[i] != code) continue;
                    var line = _lines[i];
                    if (line.IsOob) return false;
                    if (line.IsLower)
                    {
                        value = line.RangeLow - r.ReadBits(line.RangeLen);
                        return true;
                    }
                    value = line.RangeLow + r.ReadBits(line.RangeLen);
                    return true;
                }
            }
            return false;
        }
    }

    // Standard tables B.1–B.15 (T.88 Annex B).
    private static readonly HuffTable[] StandardHuffTables =
    {
        new(new HuffLine[] { new(1,4,0), new(2,8,16), new(3,16,272), new(3,32,65808) }),                       // B.1
        new(new HuffLine[] { new(1,0,0), new(2,0,1), new(3,0,2), new(4,3,3), new(5,6,11), new(6,32,75), new(6) }), // B.2
        new(new HuffLine[] { new(8,8,-256), new(1,0,0), new(2,0,1), new(3,0,2), new(4,3,3), new(5,6,11),
                             new(8,32,-257,true), new(7,32,75), new(6) }),                                     // B.3
        new(new HuffLine[] { new(1,0,1), new(2,0,2), new(3,0,3), new(4,3,4), new(5,6,12), new(5,32,76) }),      // B.4
        new(new HuffLine[] { new(7,8,-255), new(1,0,1), new(2,0,2), new(3,0,3), new(4,3,4), new(5,6,12),
                             new(7,32,-256,true), new(6,32,76) }),                                             // B.5
        new(new HuffLine[] { new(5,10,-2048), new(4,9,-1024), new(4,8,-512), new(4,7,-256), new(5,6,-128),
                             new(5,5,-64), new(4,5,-32), new(2,7,0), new(3,7,128), new(3,8,256), new(4,9,512),
                             new(4,10,1024), new(6,32,-2049,true), new(6,32,2048) }),                          // B.6
        new(new HuffLine[] { new(4,9,-1024), new(3,8,-512), new(4,7,-256), new(5,6,-128), new(5,5,-64),
                             new(4,5,-32), new(4,5,0), new(5,5,32), new(5,6,64), new(4,7,128), new(3,8,256),
                             new(3,9,512), new(3,10,1024), new(5,32,-1025,true), new(5,32,2048) }),            // B.7
        new(new HuffLine[] { new(8,3,-15), new(9,1,-7), new(8,1,-5), new(9,0,-3), new(7,0,-2), new(4,0,-1),
                             new(2,1,0), new(5,0,2), new(6,0,3), new(3,4,4), new(6,1,20), new(4,4,22),
                             new(4,5,38), new(5,6,70), new(5,7,134), new(6,7,262), new(7,8,390), new(6,10,646),
                             new(9,32,-16,true), new(9,32,1670), new(2) }),                                    // B.8
        new(new HuffLine[] { new(8,4,-31), new(9,2,-15), new(8,2,-11), new(9,1,-7), new(7,1,-5), new(4,1,-3),
                             new(3,1,-1), new(3,1,1), new(5,1,3), new(6,1,5), new(3,5,7), new(6,2,39),
                             new(4,5,43), new(4,6,75), new(5,7,139), new(5,8,267), new(6,8,523), new(7,9,779),
                             new(6,11,1291), new(9,32,-32,true), new(9,32,3339), new(2) }),                    // B.9
        new(new HuffLine[] { new(7,4,-21), new(8,0,-5), new(7,0,-4), new(5,0,-3), new(2,2,-2), new(5,0,2),
                             new(6,0,3), new(7,0,4), new(8,0,5), new(2,6,6), new(5,5,70), new(6,5,102),
                             new(6,6,134), new(6,7,198), new(6,8,326), new(6,9,582), new(6,10,1094),
                             new(7,11,2118), new(8,32,-22,true), new(8,32,4166), new(2) }),                    // B.10
        new(new HuffLine[] { new(1,0,1), new(2,1,2), new(4,0,4), new(4,1,5), new(5,1,7), new(5,2,9),
                             new(6,2,13), new(7,2,17), new(7,3,21), new(7,4,29), new(7,5,45), new(7,6,77),
                             new(7,32,141) }),                                                                 // B.11
        new(new HuffLine[] { new(1,0,1), new(2,0,2), new(3,1,3), new(5,0,5), new(5,1,6), new(6,1,8),
                             new(7,0,10), new(7,1,11), new(7,2,13), new(7,3,17), new(7,4,25), new(8,5,41),
                             new(8,32,73) }),                                                                  // B.12
        new(new HuffLine[] { new(1,0,1), new(3,0,2), new(4,0,3), new(5,0,4), new(4,1,5), new(3,3,7),
                             new(6,1,15), new(6,2,17), new(6,3,21), new(6,4,29), new(6,5,45), new(7,6,77),
                             new(7,32,141) }),                                                                 // B.13
        new(new HuffLine[] { new(3,0,-2), new(3,0,-1), new(1,0,0), new(3,0,1), new(3,0,2) }),                  // B.14
        new(new HuffLine[] { new(7,4,-24), new(6,2,-8), new(5,1,-4), new(4,0,-2), new(3,0,-1), new(1,0,0),
                             new(3,0,1), new(4,0,2), new(5,1,3), new(6,2,5), new(7,4,9),
                             new(7,32,-25,true), new(7,32,25) }),                                              // B.15
    };

    private static HuffTable StdTable(int number) => StandardHuffTables[number - 1];

    // ────────────────────────────────────────────────────────────────────────
    // MMR / arithmetic primitives — retained from the prior implementation
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>QM arithmetic decoder context cell — state index + MPS bit.</summary>
    private sealed class ArithmeticContext
    {
        public int Index;
        public bool Mps;
    }

}
