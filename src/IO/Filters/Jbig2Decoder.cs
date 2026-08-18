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
internal static class Jbig2Decoder
{
    public static byte[] Decode(byte[] data, byte[]? globals)
    {
        var combined = CombineGlobalsAndPage(globals, data);
        try
        {
            var ctx = new DecodeContext(combined);
            return ctx.DecodeAll();
        }
        catch
        {
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
    private sealed class DecodeContext
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
        /// Pre-scan all segment headers. Per T.88 §7.2.5, the referred-to-segment-number
        /// field width depends on the maximum segment number in the file. We do a first pass
        /// with refByteSize=1 to discover the max segment number, then redo if larger.
        /// </summary>
        private void ScanHeaders()
        {
            // First pass with refByteSize = 1
            var pass1 = ScanWithRefSize(1);
            var max = 0;
            foreach (var h in pass1)
                if (h.Number > max) max = h.Number;

            int finalRefSize;
            if (max <= 0xFF) finalRefSize = 1;
            else if (max <= 0xFFFF) finalRefSize = 2;
            else finalRefSize = 4;

            if (finalRefSize == 1)
            {
                _headers.AddRange(pass1);
                return;
            }

            // Re-scan with proper ref size
            var pass2 = ScanWithRefSize(finalRefSize);
            _headers.AddRange(pass2);
        }

        private List<SegmentHeader> ScanWithRefSize(int refByteSize)
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
                if (!TryParseSegmentHeader(ref pos, refByteSize, out var hdr))
                    break;
                result.Add(hdr);
                pos = hdr.DataStart + hdr.DataLength;
            }

            return result;
        }

        private bool TryParseSegmentHeader(ref int pos, int refByteSize, out SegmentHeader hdr)
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

            // Referred-to segment numbers
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
            catch
            {
                // Best-effort decode: a single corrupt segment shouldn't abort the whole stream.
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

        private void DecodeGenericRegion(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 18 > _data.Length) return;

            // Region segment info (17 bytes): W, H, X, Y, combOp
            var regionW = ReadInt32BE(_data, p);
            var regionH = ReadInt32BE(_data, p + 4);
            var regionX = ReadInt32BE(_data, p + 8);
            var regionY = ReadInt32BE(_data, p + 12);
            var combOp = _data[p + 16] & 0x07;
            p += 17;

            if (p >= _data.Length) return;
            var grFlags = _data[p++];
            var mmr = (grFlags & 0x01) != 0;
            var template = (grFlags >> 1) & 0x03;
            var tpgdon = (grFlags & 0x08) != 0; // typical prediction (T.88 §6.2.5.7)
            // useSkip (bit 4) ignored for our basic decoder

            (int, int)[] atPixels;
            if (!mmr)
            {
                var atCount = template == 0 ? 4 : 1;
                if (p + atCount * 2 > _data.Length) return;
                atPixels = new (int, int)[atCount];
                for (var i = 0; i < atCount; i++)
                {
                    atPixels[i] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                    p += 2;
                }
            }
            else
            {
                atPixels = [];
            }

            var dataAvail = hdr.DataStart + hdr.DataLength - p;
            if (dataAvail <= 0) return;

            Jbig2Bitmap regionBitmap;
            if (mmr)
            {
                regionBitmap = DecodeMmrG4(regionW, regionH, p, dataAvail);
            }
            else
            {
                var ad = new ArithmeticDecoder(_data, p);
                var grd = new GenericRegionDecoder(ad, template, atPixels, tpgdon);
                regionBitmap = grd.Decode(regionW, regionH);
            }

            // Composite onto page bitmap (or use as full page if no page-info yet).
            if (_pageBitmap is not null)
            {
                CompositeRegionOntoPage(regionBitmap, regionX, regionY, combOp);
            }
            else
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = regionBitmap.RowBytes;
                _pageBitmap = (byte[])regionBitmap.Data.Clone();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Pattern dictionary (16) — T.88 §7.4.4 + §6.7
        // ────────────────────────────────────────────────────────────────────

        // Decoded pattern dictionaries, keyed by segment number; halftone regions
        // reference these via their ReferredTo list.
        private readonly Dictionary<int, Jbig2Bitmap[]> _patternDicts = new();

        private void DecodePatternDictionary(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 7 > _data.Length) return;
            var flags = _data[p];
            var hdmmr = (flags & 0x01) != 0;
            var hdTemplate = (flags >> 1) & 0x03;
            int hdpw = _data[p + 1];
            int hdph = _data[p + 2];
            var grayMax = ReadInt32BE(_data, p + 3);
            p += 7;
            if (hdpw <= 0 || hdph <= 0 || grayMax < 0 || grayMax > 1 << 20) return;

            var numPatterns = grayMax + 1;
            var collectiveW = numPatterns * hdpw;
            var dataAvail = hdr.DataStart + hdr.DataLength - p;
            if (dataAvail <= 0) return;

            // The collective bitmap holds all patterns side by side; slice it up.
            Jbig2Bitmap collective;
            if (hdmmr)
            {
                collective = DecodeMmrG4(collectiveW, hdph, p, dataAvail);
            }
            else
            {
                // Pattern-dict AT1 is non-nominal: (-HDPW, 0); the rest are nominal.
                var at = hdTemplate == 0
                    ? new (int, int)[] { (-hdpw, 0), (-3, -1), (2, -2), (-2, -2) }
                    : new (int, int)[] { (-hdpw, 0) };
                var ad = new ArithmeticDecoder(_data, p);
                collective = new GenericRegionDecoder(ad, hdTemplate, at).Decode(collectiveW, hdph);
            }

            var patterns = new Jbig2Bitmap[numPatterns];
            for (var m = 0; m < numPatterns; m++)
            {
                var pat = new Jbig2Bitmap(hdpw, hdph);
                for (var y = 0; y < hdph; y++)
                    for (var x = 0; x < hdpw; x++)
                        pat.SetPixel(x, y, collective.GetPixel(m * hdpw + x, y));
                patterns[m] = pat;
            }
            _patternDicts[hdr.Number] = patterns;
        }

        // ────────────────────────────────────────────────────────────────────
        // Halftone region (20/22/23) — T.88 §7.4.5 + §6.6 (gray-scale image §C.5)
        // ────────────────────────────────────────────────────────────────────

        private void DecodeHalftoneRegion(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 18 > _data.Length) return;

            var regionW = ReadInt32BE(_data, p);
            var regionH = ReadInt32BE(_data, p + 4);
            var regionX = ReadInt32BE(_data, p + 8);
            var regionY = ReadInt32BE(_data, p + 12);
            var combOp = _data[p + 16] & 0x07;
            p += 17;

            var hFlags = _data[p++];
            var hmmr = (hFlags & 0x01) != 0;
            var hTemplate = (hFlags >> 1) & 0x03;
            var hEnableSkip = (hFlags & 0x08) != 0;
            var hCombOp = (hFlags >> 4) & 0x07;
            var hDefPixel = (hFlags >> 7) & 0x01;

            if (p + 20 > _data.Length) return;
            var hgw = ReadInt32BE(_data, p);
            var hgh = ReadInt32BE(_data, p + 4);
            var hgx = ReadInt32BE(_data, p + 8);
            var hgy = ReadInt32BE(_data, p + 12);
            var hrx = (_data[p + 16] << 8) | _data[p + 17];
            var hry = (_data[p + 18] << 8) | _data[p + 19];
            p += 20;

            // The referenced pattern dictionary (from a referred-to segment).
            Jbig2Bitmap[]? patterns = null;
            foreach (var refSeg in hdr.ReferredTo)
                if (_patternDicts.TryGetValue(refSeg, out var pd)) { patterns = pd; break; }
            if (patterns is null || patterns.Length == 0
                || regionW <= 0 || regionH <= 0 || hgw <= 0 || hgh <= 0) return;

            var hpw = patterns[0].Width;
            var hph = patterns[0].Height;
            var numPatterns = patterns.Length;
            var bpp = 0;
            while ((1 << bpp) < numPatterns) bpp++;

            var region = new Jbig2Bitmap(regionW, regionH, hDefPixel != 0);

            // Optional skip bitmap: grid cells whose pattern lands fully outside the region.
            Jbig2Bitmap? skip = null;
            if (hEnableSkip)
            {
                skip = new Jbig2Bitmap(hgw, hgh);
                for (var mg = 0; mg < hgh; mg++)
                    for (var ng = 0; ng < hgw; ng++)
                    {
                        var xs = (hgx + mg * hry + ng * hrx) >> 8;
                        var ys = (hgy + mg * hrx - ng * hry) >> 8;
                        if (xs + hpw <= 0 || xs >= regionW || ys + hph <= 0 || ys >= regionH)
                            skip.SetPixel(ng, mg, 1);
                    }
            }

            // Gray-scale image (§C.5): bpp bitplanes decoded MSB-first from one shared
            // generic-region context, then Gray-code combined into per-cell values.
            var gray = new int[hgh, hgw];
            if (bpp > 0 && !hmmr)
            {
                var ad = new ArithmeticDecoder(_data, p);
                var grd = new GenericRegionDecoder(ad, hTemplate, System.Array.Empty<(int, int)>()); // nominal AT pixels
                int[,]? prev = null;
                for (var j = bpp - 1; j >= 0; j--)
                {
                    var plane = grd.Decode(hgw, hgh);
                    var cur = new int[hgh, hgw];
                    for (var mg = 0; mg < hgh; mg++)
                        for (var ng = 0; ng < hgw; ng++)
                        {
                            var b = plane.GetPixel(ng, mg);
                            if (prev is not null) b ^= prev[mg, ng]; // Gray decode
                            cur[mg, ng] = b;
                            gray[mg, ng] |= b << j;
                        }
                    prev = cur;
                }
            }
            else if (bpp > 0 && hmmr)
            {
                // MMR-coded gray-scale image (§C.5): the bpp bitplanes are consecutive
                // strict-T.6 (Group 4) bitmaps chained on one reader, decoded MSB-first,
                // then Gray-code combined into per-cell values.
                var segEnd = hdr.DataStart + hdr.DataLength;
                var avail = Math.Max(0, Math.Min(segEnd - p, _data.Length - p));
                var seg = new byte[avail];
                Array.Copy(_data, p, seg, 0, avail);
                var reader = new CcittFaxDecodeFilter.CcittBitReader(seg);
                var rowBytes = (hgw + 7) / 8;
                // Each of the GSBPP planes (MSB first) is an INDEPENDENT T.6 image: a fresh
                // all-white reference line, terminated by an EOFB and byte-aligned to the next
                // plane. Decode per plane, consume the EOFB + align, then Gray-code combine.
                int[,]? prev = null;
                for (var j = bpp - 1; j >= 0; j--)
                {
                    var bytes = CcittFaxDecodeFilter.DecodeGroup4Region(reader, hgw, hgh);
                    var cur = new int[hgh, hgw];
                    for (var mg = 0; mg < hgh; mg++)
                        for (var ng = 0; ng < hgw; ng++)
                        {
                            var idx = mg * rowBytes + (ng >> 3);
                            var b = idx < bytes.Length ? (bytes[idx] >> (7 - (ng & 7))) & 1 : 0;
                            if (prev is not null) b ^= prev[mg, ng]; // Gray decode
                            cur[mg, ng] = b;
                            gray[mg, ng] |= b << j;
                        }
                    prev = cur;
                    reader.ConsumeEofbAndByteAlign();
                }
            }

            // Render each grid cell's pattern at its rotated/scaled position.
            for (var mg = 0; mg < hgh; mg++)
                for (var ng = 0; ng < hgw; ng++)
                {
                    if (skip is not null && skip.GetPixel(ng, mg) != 0) continue;
                    var gv = gray[mg, ng];
                    if (gv < 0) gv = 0;
                    else if (gv >= numPatterns) gv = numPatterns - 1;
                    var x = (hgx + mg * hry + ng * hrx) >> 8;
                    var y = (hgy + mg * hrx - ng * hry) >> 8;
                    region.CompositeAt(patterns[gv], x, y, hCombOp);
                }

            if (_pageBitmap is not null)
            {
                CompositeRegionOntoPage(region, regionX, regionY, combOp);
            }
            else
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = region.RowBytes;
                _pageBitmap = (byte[])region.Data.Clone();
            }
        }

        // ────────────────────────────────────────────────────────────────────
        // Symbol dictionary segment (0) — T.88 §7.4.2 + §6.5
        // ────────────────────────────────────────────────────────────────────

        private void DecodeSymbolDictionary(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 2 > _data.Length) return;

            // SDFLAGS is a 16-bit big-endian field (T.88 §7.4.3.1.1).
            var sdFlags = (_data[p] << 8) | _data[p + 1];
            p += 2;

            var sdHuff = (sdFlags & 0x0001) != 0;       // bit 0  SDHUFF
            var sdRefAgg = (sdFlags & 0x0002) != 0;     // bit 1  SDREFAGG
            var sdTemplate = (sdFlags >> 10) & 0x03;    // bits 10-11  SDTEMPLATE
            var sdrTemplate = (sdFlags >> 12) & 0x01;   // bit 12      SDRTEMPLATE

            if (sdHuff)
            {
                DecodeSymbolDictionaryHuffman(hdr, p, sdFlags);
                return;
            }

            // AT pixels for the symbol dictionary's generic region (SDAT).
            // 8 bytes for SDTEMPLATE=0, 2 bytes otherwise.
            var sdAtCount = sdTemplate == 0 ? 4 : 1;
            if (p + sdAtCount * 2 > _data.Length) return;
            var sdAt = new (int dx, int dy)[sdAtCount];
            for (var i = 0; i < sdAtCount; i++)
            {
                sdAt[i] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                p += 2;
            }

            // SDRAT (refinement AT pixels) — present when SDREFAGG=1 and SDRTEMPLATE=0.
            var sdrAt = new (int dx, int dy)[2];
            if (sdRefAgg && sdrTemplate == 0)
            {
                if (p + 4 > _data.Length) return;
                sdrAt[0] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                sdrAt[1] = ((sbyte)_data[p + 2], (sbyte)_data[p + 3]);
                p += 4;
            }

            // SDNUMEXSYMS + SDNUMNEWSYMS
            if (p + 8 > _data.Length) return;
            var sdNumExSyms = ReadInt32BE(_data, p);
            var sdNumNewSyms = ReadInt32BE(_data, p + 4);
            p += 8;

            if (sdNumNewSyms < 0 || sdNumNewSyms > 1_000_000) return;
            if (sdNumExSyms < 0 || sdNumExSyms > 1_000_000) return;

            // Imported symbols come from referred-to symbol-dictionary segments.
            var imported = new List<Jbig2Bitmap>();
            foreach (var refSeg in hdr.ReferredTo)
            {
                if (_symbolDicts.TryGetValue(refSeg, out var importedSyms))
                    imported.AddRange(importedSyms);
            }

            var totalSyms = imported.Count + sdNumNewSyms;
            var allSyms = new Jbig2Bitmap[totalSyms];
            for (var i = 0; i < imported.Count; i++) allSyms[i] = imported[i];

            // Decode SDNUMNEWSYMS new symbols using the arithmetic direct method.
            var ad = new ArithmeticDecoder(_data, p);
            var iaDh = new IntegerDecoder(ad);
            var iaDw = new IntegerDecoder(ad);
            var iaEx = new IntegerDecoder(ad);
            var grd = new GenericRegionDecoder(ad, sdTemplate, sdAt);
            // Refinement/aggregate symbols (SDREFAGG, T.88 §6.5.8.2): each new symbol is a
            // refinement of an existing one. Reuse the generic refinement-region decoder.
            var iaAi = new IntegerDecoder(ad);
            var iaRdx = new IntegerDecoder(ad);
            var iaRdy = new IntegerDecoder(ad);
            var sdSymCodeLen = Math.Max(1, SymCodeLength(totalSyms));
            var iaIdSd = new IaidDecoder(ad, sdSymCodeLen);
            var grCtxSd = new ArithmeticContext[8192];
            if (sdRefAgg) for (var i = 0; i < grCtxSd.Length; i++) grCtxSd[i] = new ArithmeticContext();

            // Extra integer decoders used only by aggregate (REFAGGNINST>1) symbols,
            // which are coded as an embedded text region (§6.5.8.2.2). Created once so
            // their contexts persist across all aggregate symbols in this dictionary.
            var iaDt = new IntegerDecoder(ad);
            var iaFs = new IntegerDecoder(ad);
            var iaDs = new IntegerDecoder(ad);
            var iaIt = new IntegerDecoder(ad);
            var iaRi = new IntegerDecoder(ad);
            var iaRdw = new IntegerDecoder(ad);
            var iaRdh = new IntegerDecoder(ad);

            var hcHeight = 0;
            var newIdx = 0;

            while (newIdx < sdNumNewSyms)
            {
                if (!iaDh.Decode(out var hcDelta)) break;
                hcHeight += hcDelta;
                if (hcHeight <= 0 || hcHeight > 65535) break;

                var symWidth = 0;
                var beforeClass = newIdx;
                while (true)
                {
                    if (!iaDw.Decode(out var dw))
                    {
                        // OOB: end of height class
                        break;
                    }
                    symWidth += dw;
                    if (symWidth <= 0 || symWidth > 65535) goto endDict;
                    if (newIdx >= sdNumNewSyms) break;

                    Jbig2Bitmap bitmap;
                    if (sdRefAgg)
                    {
                        if (!iaAi.Decode(out var nInst)) goto endDict;
                        if (nInst == 1)
                        {
                            // Single-symbol refinement: refine the referenced symbol.
                            var refId = iaIdSd.Decode();
                            if (!iaRdx.Decode(out var rdx)) goto endDict;
                            if (!iaRdy.Decode(out var rdy)) goto endDict;
                            var reference = (refId >= 0 && refId < imported.Count + newIdx)
                                ? allSyms[refId] : new Jbig2Bitmap(symWidth, hcHeight);
                            bitmap = DecodeRefinement(ad, grCtxSd, symWidth, hcHeight, sdrTemplate, sdrAt, reference, rdx, rdy);
                        }
                        else
                        {
                            // Aggregate of >1 instances (§6.5.8.2.2): the symbol bitmap is a
                            // text region placing nInst of the symbols decoded so far, sharing
                            // this dictionary's arithmetic decoder and contexts.
                            var avail = new System.ArraySegment<Jbig2Bitmap>(allSyms, 0, imported.Count + newIdx);
                            bitmap = DecodeTextRegionBitmap(ad, avail,
                                iaDt, iaFs, iaDs, iaIt, iaRi, iaIdSd, iaRdw, iaRdh, iaRdx, iaRdy, grCtxSd,
                                symWidth, hcHeight, nInst, sbStrips: 1, refCorner: 1, transposed: false,
                                sbCombOp: 0, sbDefPixel: false, sbDsOffset: 0, sbRefine: true,
                                sbrTemplate: sdrTemplate, sbrAt: sdrAt);
                        }
                    }
                    else
                    {
                        bitmap = grd.Decode(symWidth, hcHeight);
                    }
                    allSyms[imported.Count + newIdx] = bitmap;
                    newIdx++;
                }
                if (newIdx == beforeClass) break; // no progress in this class — malformed stream
            }
        endDict:;

            // Decode export flags. Bits alternate: starts at false (don't export),
            // each IAEX run length flips the export bit.
            var exported = new List<Jbig2Bitmap>(sdNumExSyms);
            var exporting = false;
            var i2 = 0;
            while (i2 < totalSyms)
            {
                if (!iaEx.Decode(out var run)) break;
                if (run < 0 || run > totalSyms - i2) run = totalSyms - i2;
                if (exporting)
                {
                    for (var k = 0; k < run; k++)
                    {
                        var sym = allSyms[i2 + k];
                        if (sym is not null) exported.Add(sym);
                    }
                }
                i2 += run;
                exporting = !exporting;
            }

            _symbolDicts[hdr.Number] = exported.ToArray();
        }

        /// <summary>
        /// Core arithmetic text-region strip walk (T.88 §6.4.5): place symbol
        /// instances into an SBW×SBH bitmap. Shared by aggregate-symbol decoding
        /// (§6.5.8.2.2). All arithmetic/integer decoders and contexts are supplied by
        /// the caller so state persists across invocations.
        /// </summary>
        private Jbig2Bitmap DecodeTextRegionBitmap(
            ArithmeticDecoder ad, IReadOnlyList<Jbig2Bitmap> symbols,
            IntegerDecoder iaDt, IntegerDecoder iaFs, IntegerDecoder iaDs, IntegerDecoder iaIt,
            IntegerDecoder iaRi, IaidDecoder iaId, IntegerDecoder iaRdw, IntegerDecoder iaRdh,
            IntegerDecoder iaRdx, IntegerDecoder iaRdy, ArithmeticContext[] grCtx,
            int sbw, int sbh, int sbNumInstances, int sbStrips, int refCorner, bool transposed,
            int sbCombOp, bool sbDefPixel, int sbDsOffset, bool sbRefine, int sbrTemplate, (int, int)[] sbrAt)
        {
            var region = new Jbig2Bitmap(sbw, sbh, sbDefPixel);
            if (!iaDt.Decode(out var firstDt)) return region;
            int stripT = -firstDt, firstS = 0, decoded = 0;

            while (decoded < sbNumInstances)
            {
                var beforeStrip = decoded;
                if (!iaDt.Decode(out var dt)) break;
                stripT += dt;
                if (!iaFs.Decode(out var dfs)) break;   // IAFS: once per strip
                firstS += dfs;
                var curS = firstS;

                // Instances within a strip are terminated by an OOB IADS — NOT by the
                // instance count. That terminating OOB must always be consumed (even after
                // the final instance) so an embedded aggregate text region (§6.5.8.2.2)
                // leaves the shared arithmetic stream aligned for the rest of the dictionary.
                while (true)
                {
                    var curT = 0;
                    if (sbStrips > 1)
                    {
                        if (!iaIt.Decode(out var ct)) break;
                        curT = ct;
                    }

                    var idVal = iaId.Decode();
                    if (idVal < 0 || idVal >= symbols.Count) idVal = 0;
                    var symBitmap = symbols[idVal];

                    if (sbRefine)
                    {
                        if (!iaRi.Decode(out var riVal)) break;
                        if (riVal != 0 && symBitmap is not null)
                        {
                            if (!iaRdw.Decode(out var rdw)) break;
                            if (!iaRdh.Decode(out var rdh)) break;
                            if (!iaRdx.Decode(out var rdx)) break;
                            if (!iaRdy.Decode(out var rdy)) break;
                            var rw = symBitmap.Width + rdw;
                            var rh = symBitmap.Height + rdh;
                            if (rw > 0 && rh > 0 && rw <= 65535 && rh <= 65535)
                                symBitmap = DecodeRefinement(ad, grCtx, rw, rh, sbrTemplate, sbrAt,
                                    symBitmap, (rdw >> 1) + rdx, (rdh >> 1) + rdy);
                        }
                    }

                    if (symBitmap is not null)
                    {
                        var symW = symBitmap.Width;
                        var symH = symBitmap.Height;
                        int placeS = curS, placeT = stripT * sbStrips + curT;

                        int x, y;
                        if (!transposed)
                            switch (refCorner)
                            {
                                case 0: x = placeS; y = placeT - symH + 1; break;
                                case 1: x = placeS; y = placeT; break;
                                case 2: x = placeS - symW + 1; y = placeT - symH + 1; break;
                                default: x = placeS - symW + 1; y = placeT; break;
                            }
                        else
                            switch (refCorner)
                            {
                                case 0: x = placeT - symH + 1; y = placeS; break;
                                case 1: x = placeT; y = placeS; break;
                                case 2: x = placeT - symH + 1; y = placeS - symW + 1; break;
                                default: x = placeT; y = placeS - symW + 1; break;
                            }

                        region.CompositeAt(symBitmap, x, y, sbCombOp);
                        curS += (transposed ? symH : symW) - 1;
                    }

                    decoded++;
                    if (!iaDs.Decode(out var dsVal)) break;   // OOB → end of strip (consumed)
                    curS += dsVal + sbDsOffset;
                    if (decoded >= sbNumInstances) break;     // overrun guard (malformed stream)
                }

                if (decoded == beforeStrip) break;
            }
            return region;
        }

        /// <summary>Huffman-coded symbol dictionary (T.88 §6.5, SDHUFF=1). Symbols
        /// arrive in height classes: per class a height delta, then per symbol a
        /// width delta (OOB ends the class); the class's glyphs are carried in one
        /// COLLECTIVE bitmap — raw rows when BMSIZE=0, MMR-coded otherwise — that
        /// is sliced up by the recorded widths. Refinement/aggregation (SDREFAGG)
        /// is not handled in the Huffman path.</summary>
        private void DecodeSymbolDictionaryHuffman(SegmentHeader hdr, int p, int sdFlags)
        {
            var sdRefAgg = (sdFlags & 0x0002) != 0;
            if (sdRefAgg) return; // Huffman + refinement/aggregation: out of scope

            var dhSel = (sdFlags >> 2) & 0x03;   // 0→B.4, 1→B.5
            var dwSel = (sdFlags >> 4) & 0x03;   // 0→B.2, 1→B.3
            var bmSel = (sdFlags >> 6) & 0x01;   // 0→B.1
            // Custom tables (selector 3 / 1 for BMSIZE) come from referred table
            // segments, which this decoder does not parse yet.
            if (dhSel > 1 || dwSel > 1 || bmSel != 0) return;
            var tDh = StdTable(dhSel == 0 ? 4 : 5);
            var tDw = StdTable(dwSel == 0 ? 2 : 3);
            var tBm = StdTable(1);
            var tEx = StdTable(1);

            if (p + 8 > _data.Length) return;
            var sdNumExSyms = ReadInt32BE(_data, p);
            var sdNumNewSyms = ReadInt32BE(_data, p + 4);
            p += 8;
            if (sdNumNewSyms < 0 || sdNumNewSyms > 1_000_000) return;
            if (sdNumExSyms < 0 || sdNumExSyms > 1_000_000) return;

            var imported = new List<Jbig2Bitmap>();
            foreach (var refSeg in hdr.ReferredTo)
            {
                if (_symbolDicts.TryGetValue(refSeg, out var importedSyms))
                    imported.AddRange(importedSyms);
            }

            var totalSyms = imported.Count + sdNumNewSyms;
            var allSyms = new Jbig2Bitmap[totalSyms];
            for (var i = 0; i < imported.Count; i++) allSyms[i] = imported[i];

            var reader = new HuffBitReader(_data, p, hdr.DataStart + hdr.DataLength);
            var hcHeight = 0;
            var newIdx = 0;

            while (newIdx < sdNumNewSyms)
            {
                if (!tDh.Decode(reader, out var hcDelta)) break;
                hcHeight += hcDelta;
                if (hcHeight <= 0 || hcHeight > 65535) break;

                var symWidth = 0;
                var totWidth = 0;
                var classStart = newIdx;
                var widths = new List<int>();
                while (true)
                {
                    // The OOB code terminates every height class and must always be
                    // consumed — even when the class supplies the last symbol — or
                    // the bit stream desyncs before the class's BMSIZE field.
                    if (!tDw.Decode(reader, out var dw)) break;
                    symWidth += dw;
                    if (symWidth <= 0 || symWidth > 65535) return;
                    if (newIdx >= sdNumNewSyms) return; // more widths than declared symbols
                    widths.Add(symWidth);
                    totWidth += symWidth;
                    newIdx++;
                }
                if (widths.Count == 0) break; // no progress — malformed stream

                // Height-class collective bitmap (§6.5.9): BMSIZE=0 → uncompressed
                // rows (each row padded to a byte); otherwise BMSIZE bytes of MMR.
                if (!tBm.Decode(reader, out var bmSize) || bmSize < 0) return;
                reader.Align();
                Jbig2Bitmap collective;
                if (bmSize == 0)
                {
                    collective = new Jbig2Bitmap(totWidth, hcHeight);
                    var rowBytes = (totWidth + 7) / 8;
                    var src = reader.BytePos;
                    if (src + (long)rowBytes * hcHeight > _data.Length) return;
                    for (var y = 0; y < hcHeight; y++)
                        for (var x = 0; x < totWidth; x++)
                            collective.SetPixel(x, y, (_data[src + y * rowBytes + x / 8] >> (7 - x % 8)) & 1);
                    reader.SkipBytes(rowBytes * hcHeight);
                }
                else
                {
                    // The class's MMR payload is T.6 (Group 4) — decode with the
                    // shared CCITT filter (the JBIG2-local MMR line decoder predates
                    // it and mishandles real G4 streams).
                    var avail = Math.Max(0, Math.Min(bmSize, _data.Length - reader.BytePos));
                    var seg = new byte[avail];
                    Array.Copy(_data, reader.BytePos, seg, 0, avail);
                    var parms = new PdfDictionary();
                    parms.Set("K", new PdfInteger(-1));
                    parms.Set("Columns", new PdfInteger(totWidth));
                    parms.Set("Rows", new PdfInteger(hcHeight));
                    parms.Set("BlackIs1", PdfBoolean.True);
                    byte[] rows;
                    // JBIG2 MMR is strict T.6 — opt out of the CCITT image-producer column shift.
                    try { rows = CcittFaxDecodeFilter.Decode(seg, parms, group4ColumnShift: false); }
                    catch { return; }
                    var cRowBytes = (totWidth + 7) / 8;
                    if (rows.Length < cRowBytes * hcHeight) return;
                    collective = new Jbig2Bitmap(totWidth, hcHeight, cRowBytes, rows);
                    reader.SkipBytes(bmSize);
                }

                // Slice the collective bitmap into the class's symbols.
                var xOff = 0;
                for (var s = 0; s < widths.Count; s++)
                {
                    var w = widths[s];
                    var sym = new Jbig2Bitmap(w, hcHeight);
                    for (var y = 0; y < hcHeight; y++)
                        for (var x = 0; x < w; x++)
                            sym.SetPixel(x, y, collective.GetPixel(xOff + x, y));
                    allSyms[imported.Count + classStart + s] = sym;
                    xOff += w;
                }
            }

            // Export flags: runlengths over Table B.1, alternating starting at
            // "don't export" — same shape as the arithmetic variant.
            var exported = new List<Jbig2Bitmap>(sdNumExSyms);
            var exporting = false;
            var i2 = 0;
            while (i2 < totalSyms)
            {
                if (!tEx.Decode(reader, out var run)) break;
                if (run < 0 || run > totalSyms - i2) run = totalSyms - i2;
                if (exporting)
                {
                    for (var k = 0; k < run; k++)
                    {
                        var sym = allSyms[i2 + k];
                        if (sym is not null) exported.Add(sym);
                    }
                }
                i2 += run;
                exporting = !exporting;
            }

            _symbolDicts[hdr.Number] = exported.ToArray();
        }

        // ────────────────────────────────────────────────────────────────────
        // Text region segments (6/7) — T.88 §7.4.3 + §6.4
        // ────────────────────────────────────────────────────────────────────

        private void DecodeTextRegion(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 17 > _data.Length) return;

            // Region segment info (17 bytes)
            var regionW = ReadInt32BE(_data, p);
            var regionH = ReadInt32BE(_data, p + 4);
            var regionX = ReadInt32BE(_data, p + 8);
            var regionY = ReadInt32BE(_data, p + 12);
            var regCombOp = _data[p + 16] & 0x07;
            p += 17;

            if (p + 2 > _data.Length) return;
            // SBFLAGS is a 16-bit big-endian field (T.88 §7.4.3.1.1).
            var trFlags = (_data[p] << 8) | _data[p + 1];
            p += 2;

            var sbHuff = (trFlags & 0x0001) != 0;
            var sbRefine = (trFlags & 0x0002) != 0;
            var log2Strips = (trFlags >> 2) & 0x03;
            var refCorner = (trFlags >> 4) & 0x03;
            var transposed = (trFlags & 0x0040) != 0;
            var sbCombOp = (trFlags >> 7) & 0x03;
            var sbDefPixel = (trFlags & 0x0200) != 0;
            // 5-bit signed delta-S offset (bits 10..14)
            var sbDsOffsetRaw = (trFlags >> 10) & 0x1F;
            var sbDsOffset = (sbDsOffsetRaw & 0x10) != 0 ? sbDsOffsetRaw - 32 : sbDsOffsetRaw;
            var sbrTemplate = (trFlags >> 15) & 0x01;

            // SBHUFFFLAGS (16-bit, §7.4.3.1.2) follows SBFLAGS in the Huffman variant.
            var sbHuffFlags = 0;
            if (sbHuff)
            {
                if (p + 2 > _data.Length) return;
                sbHuffFlags = (_data[p] << 8) | _data[p + 1];
                p += 2;
                // Huffman + per-instance refinement needs the RDW/RDH/RDX/RDY/RSIZE
                // table plumbing — not in scope (this corpus has SBREFINE=0).
                if (sbRefine) return;
            }

            // SBRAT (refinement AT pixels) — only if SBREFINE and SBRTEMPLATE=0.
            var sbrAt = new (int dx, int dy)[2];
            if (sbRefine)
            {
                var sbrAtCount = sbrTemplate == 0 ? 2 : 0;
                for (var i = 0; i < sbrAtCount; i++)
                {
                    if (p + 2 > _data.Length) return;
                    sbrAt[i] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                    p += 2;
                }
            }

            if (p + 4 > _data.Length) return;
            var sbNumInstances = ReadInt32BE(_data, p);
            p += 4;
            if (sbNumInstances <= 0 || sbNumInstances > 10_000_000) return;

            // Collect referenced symbols from all referred-to symbol dictionaries (in order).
            var symbols = new List<Jbig2Bitmap>();
            foreach (var refSeg in hdr.ReferredTo)
            {
                if (_symbolDicts.TryGetValue(refSeg, out var syms))
                    symbols.AddRange(syms);
            }
            if (symbols.Count == 0) return;

            var sbSymCodeLen = SymCodeLength(symbols.Count);
            var sbStrips = 1 << log2Strips;

            if (sbHuff)
            {
                DecodeTextRegionHuffman(hdr, p, sbHuffFlags, symbols, sbStrips, log2Strips,
                    refCorner, transposed, sbCombOp, sbDefPixel, sbDsOffset, sbNumInstances,
                    regionW, regionH, regionX, regionY, regCombOp);
                return;
            }

            // Decode instances with arithmetic IA decoders.
            var ad = new ArithmeticDecoder(_data, p);
            var iaDt = new IntegerDecoder(ad);
            var iaFs = new IntegerDecoder(ad);
            var iaDs = new IntegerDecoder(ad);
            var iaIt = new IntegerDecoder(ad);
            var iaRi = new IntegerDecoder(ad);
            var iaId = new IaidDecoder(ad, sbSymCodeLen);
            // Symbol-instance refinement (T.88 §6.4.11): per-instance refinement deltas and
            // a shared GR context array reused across every refined instance in this region.
            var iaRdw = new IntegerDecoder(ad);
            var iaRdh = new IntegerDecoder(ad);
            var iaRdx = new IntegerDecoder(ad);
            var iaRdy = new IntegerDecoder(ad);
            var grCtx = new ArithmeticContext[8192];
            if (sbRefine) for (var i = 0; i < grCtx.Length; i++) grCtx[i] = new ArithmeticContext();

            // Region bitmap accumulator
            var region = new Jbig2Bitmap(regionW, regionH, sbDefPixel);

            int stripT;
            if (!iaDt.Decode(out var firstDt)) return;
            stripT = -firstDt;
            int firstS = 0;
            int decoded = 0;

            while (decoded < sbNumInstances)
            {
                var beforeStrip = decoded;
                if (!iaDt.Decode(out var dt)) break;
                stripT += dt;

                int curS = 0;
                bool first = true;

                while (decoded < sbNumInstances)
                {
                    if (first)
                    {
                        if (!iaFs.Decode(out var dfs)) goto endStrip;
                        firstS += dfs;
                        curS = firstS;
                        first = false;
                    }
                    else
                    {
                        if (!iaDs.Decode(out var dsVal))
                        {
                            // OOB → end of strip
                            goto endStrip;
                        }
                        curS += dsVal + sbDsOffset;
                    }

                    int curT;
                    if (sbStrips > 1)
                    {
                        if (!iaIt.Decode(out var ct)) goto endStrip;
                        curT = ct;
                    }
                    else
                    {
                        curT = 0;
                    }

                    var idVal = iaId.Decode();
                    if (idVal < 0 || idVal >= symbols.Count) idVal = 0;

                    var symBitmap = symbols[idVal];

                    if (sbRefine)
                    {
                        // Per-instance refinement (T.88 §6.4.11): RI selects whether this
                        // instance's glyph is refined. When set, decode the size deltas and
                        // run the refinement region with the original symbol as reference.
                        if (!iaRi.Decode(out var riVal)) goto endStrip;
                        if (riVal != 0 && symBitmap is not null)
                        {
                            if (!iaRdw.Decode(out var rdw)) goto endStrip;
                            if (!iaRdh.Decode(out var rdh)) goto endStrip;
                            if (!iaRdx.Decode(out var rdx)) goto endStrip;
                            if (!iaRdy.Decode(out var rdy)) goto endStrip;
                            var rw = symBitmap.Width + rdw;
                            var rh = symBitmap.Height + rdh;
                            if (rw > 0 && rh > 0 && rw <= 65535 && rh <= 65535)
                                symBitmap = DecodeRefinement(ad, grCtx, rw, rh, sbrTemplate, sbrAt,
                                    symBitmap, (rdw >> 1) + rdx, (rdh >> 1) + rdy);
                        }
                    }

                    if (symBitmap is null)
                    {
                        decoded++;
                        if (!transposed) curS += 0;
                        else curS += 0;
                        continue;
                    }

                    var symW = symBitmap.Width;
                    var symH = symBitmap.Height;

                    // Spec §6.4.5.1: place symbol relative to (T_I, curS) with REFCORNER offset,
                    // where T_I = STRIPT·SBSTRIPS + CURT. The strip coordinate STRIPT is decoded in
                    // units of SBSTRIPS rows, so it must be scaled back up at placement time.
                    // Coordinates are in (s, t) which map to (x, y) when not transposed and (y, x) when transposed.
                    int placeS = curS;
                    int placeT = stripT * sbStrips + curT;

                    int x, y;
                    if (!transposed)
                    {
                        // s → x, t → y
                        switch (refCorner)
                        {
                            case 0: x = placeS; y = placeT - symH + 1; break;            // BL
                            case 1: x = placeS; y = placeT; break;                         // TL
                            case 2: x = placeS - symW + 1; y = placeT - symH + 1; break;   // BR
                            default: x = placeS - symW + 1; y = placeT; break;             // TR
                        }
                    }
                    else
                    {
                        // s → y, t → x
                        switch (refCorner)
                        {
                            case 0: x = placeT - symH + 1; y = placeS; break;
                            case 1: x = placeT; y = placeS; break;
                            case 2: x = placeT - symH + 1; y = placeS - symW + 1; break;
                            default: x = placeT; y = placeS - symW + 1; break;
                        }
                    }

                    region.CompositeAt(symBitmap, x, y, sbCombOp);

                    if (!transposed)
                        curS += symW - 1;
                    else
                        curS += symH - 1;

                    decoded++;
                }
            endStrip:
                if (decoded == beforeStrip) break; // strip made no progress — bail out
            }

            if (_pageBitmap is null)
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = region.RowBytes;
                _pageBitmap = (byte[])region.Data.Clone();
            }
            else
            {
                CompositeRegionOntoPage(region, regionX, regionY, regCombOp);
            }
        }

        /// <summary>Huffman-coded text region (T.88 §6.4, SBHUFF=1). Reads the
        /// runcode-compressed symbol-ID code table (§7.4.3.1.7), then places the
        /// symbol instances with the same strip walk as the arithmetic variant,
        /// with FS/DS/DT decoded through the selected standard tables and CURT
        /// read as raw bits. Per-instance refinement is not handled (callers gate
        /// on SBREFINE=0).</summary>
        private void DecodeTextRegionHuffman(SegmentHeader hdr, int p, int sbHuffFlags,
            List<Jbig2Bitmap> symbols, int sbStrips, int log2Strips, int refCorner,
            bool transposed, int sbCombOp, bool sbDefPixel, int sbDsOffset, int sbNumInstances,
            int regionW, int regionH, int regionX, int regionY, int regCombOp)
        {
            var fsSel = sbHuffFlags & 0x03;         // 0→B.6, 1→B.7
            var dsSel = (sbHuffFlags >> 2) & 0x03;  // 0→B.8, 1→B.9, 2→B.10
            var dtSel = (sbHuffFlags >> 4) & 0x03;  // 0→B.11, 1→B.12, 2→B.13
            // Selector 3 = custom table from referred table segments — unsupported.
            if (fsSel > 1 || dsSel > 2 || dtSel > 2) return;
            var tFs = StdTable(fsSel == 0 ? 6 : 7);
            var tDs = StdTable(8 + dsSel);
            var tDt = StdTable(11 + dtSel);

            var reader = new HuffBitReader(_data, p, hdr.DataStart + hdr.DataLength);

            // Symbol ID code table (§7.4.3.1.7): 35 runcode lengths (5 bits each),
            // a canonical runcode table over them, then one code length per symbol
            // (runcode 0..31 = the length itself; 32 = repeat previous 3–6 times;
            // 33 = 3–10 zeroes; 34 = 11–138 zeroes), then byte alignment.
            var runLens = new HuffLine[35];
            for (var i = 0; i < 35; i++)
                runLens[i] = new HuffLine(reader.ReadBits(4), 0, i);
            var runTable = new HuffTable(runLens);

            var symLens = new int[symbols.Count];
            var prevLen = 0;
            for (var i = 0; i < symbols.Count;)
            {
                if (!runTable.Decode(reader, out var code)) return;
                if (code < 32)
                {
                    symLens[i++] = code;
                    prevLen = code;
                }
                else if (code == 32)
                {
                    var rep = 3 + reader.ReadBits(2);
                    while (rep-- > 0 && i < symbols.Count) symLens[i++] = prevLen;
                }
                else if (code == 33)
                {
                    var rep = 3 + reader.ReadBits(3);
                    while (rep-- > 0 && i < symbols.Count) symLens[i++] = 0;
                }
                else // 34
                {
                    var rep = 11 + reader.ReadBits(7);
                    while (rep-- > 0 && i < symbols.Count) symLens[i++] = 0;
                }
            }
            var idLines = new HuffLine[symbols.Count];
            for (var i = 0; i < symbols.Count; i++)
                idLines[i] = new HuffLine(symLens[i], 0, i);
            var tId = new HuffTable(idLines);
            reader.Align();

            var region = new Jbig2Bitmap(regionW, regionH, sbDefPixel);

            if (!tDt.Decode(reader, out var firstDt)) return;
            var stripT = -firstDt;
            var firstS = 0;
            var decoded = 0;

            while (decoded < sbNumInstances)
            {
                var beforeStrip = decoded;
                if (!tDt.Decode(reader, out var dt)) break;
                stripT += dt;

                var curS = 0;
                var first = true;

                while (decoded < sbNumInstances)
                {
                    if (first)
                    {
                        if (!tFs.Decode(reader, out var dfs)) goto endStrip;
                        firstS += dfs;
                        curS = firstS;
                        first = false;
                    }
                    else
                    {
                        if (!tDs.Decode(reader, out var dsVal)) goto endStrip; // OOB → end of strip
                        curS += dsVal + sbDsOffset;
                    }

                    // CURT is raw bits (⌈log2 SBSTRIPS⌉) in the Huffman variant.
                    var curT = sbStrips > 1 ? reader.ReadBits(log2Strips) : 0;

                    if (!tId.Decode(reader, out var idVal)) goto endStrip;
                    if (idVal < 0 || idVal >= symbols.Count) idVal = 0;
                    var symBitmap = symbols[idVal];
                    if (symBitmap is null) { decoded++; continue; }

                    var symW = symBitmap.Width;
                    var symH = symBitmap.Height;

                    var placeS = curS;
                    var placeT = stripT * sbStrips + curT;

                    int x, y;
                    if (!transposed)
                    {
                        switch (refCorner)
                        {
                            case 0: x = placeS; y = placeT - symH + 1; break;              // BL
                            case 1: x = placeS; y = placeT; break;                         // TL
                            case 2: x = placeS - symW + 1; y = placeT - symH + 1; break;   // BR
                            default: x = placeS - symW + 1; y = placeT; break;             // TR
                        }
                    }
                    else
                    {
                        switch (refCorner)
                        {
                            case 0: x = placeT - symH + 1; y = placeS; break;
                            case 1: x = placeT; y = placeS; break;
                            case 2: x = placeT - symH + 1; y = placeS - symW + 1; break;
                            default: x = placeT; y = placeS - symW + 1; break;
                        }
                    }

                    region.CompositeAt(symBitmap, x, y, sbCombOp);

                    if (!transposed) curS += symW - 1;
                    else curS += symH - 1;

                    decoded++;
                }
            endStrip:
                if (decoded == beforeStrip) break; // strip made no progress — bail out
            }

            if (_pageBitmap is null)
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = region.RowBytes;
                _pageBitmap = (byte[])region.Data.Clone();
            }
            else
            {
                CompositeRegionOntoPage(region, regionX, regionY, regCombOp);
            }
        }

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

    // ────────────────────────────────────────────────────────────────────────
    // Integer arithmetic decoders — T.88 Annex A
    // ────────────────────────────────────────────────────────────────────────

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
