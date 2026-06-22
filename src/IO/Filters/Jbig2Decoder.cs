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
                    // 36/40 (refinement), 4 (intermediate text region), 16/22/23/24
                    // (pattern dict + halftone), 50/51 (end of page/file): no-op for us.
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
                regionBitmap = DecodeMmrBitmap(regionW, regionH, p, dataAvail);
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
                // Out of scope: the Huffman-coded symbol-dictionary variant. Text regions
                // referring to it will paint blanks but the rest of the stream still decodes.
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
                            // Aggregate of >1 instances: decode as a small text region over the
                            // symbols available so far. Rare; bail to avoid desync if unsupported.
                            goto endDict;
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

            if (sbHuff)
            {
                // Out of scope: Huffman variant.
                return;
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

        private Jbig2Bitmap DecodeMmrBitmap(int width, int height, int p, int dataLen)
        {
            var rowBytes = (width + 7) / 8;
            var result = new byte[rowBytes * height];
            var endPos = Math.Min(p + dataLen, _data.Length);

            var bitReader = new MmrBitReader(_data, p, endPos);
            var refLine = new bool[width];

            for (var row = 0; row < height; row++)
            {
                var curLine = new bool[width];
                DecodeMmrLine(bitReader, refLine, curLine, width);

                for (var x = 0; x < width; x++)
                {
                    if (curLine[x])
                        result[row * rowBytes + x / 8] |= (byte)(0x80 >> (x % 8));
                }

                Array.Copy(curLine, refLine, width);
            }

            return new Jbig2Bitmap(width, height, rowBytes, result);
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

        // ────────────────────────────────────────────────────────────────────
        // MMR (Group 4) line decoder — retained from the prior implementation
        // ────────────────────────────────────────────────────────────────────

        private static void DecodeMmrLine(MmrBitReader bits, bool[] ref_, bool[] cur, int width)
        {
            var a0 = 0;
            var a0Color = false;

            while (a0 < width)
            {
                var b1 = FindChangingElement(ref_, a0, !a0Color, width);
                var b2 = FindChangingElement(ref_, b1, a0Color, width);

                var mode = ReadMmrMode(bits);
                switch (mode)
                {
                    case 0:
                        a0 = b2;
                        break;
                    case 1:
                        var run1 = ReadMmrRunLength(bits, a0Color);
                        FillRun(cur, a0, a0 + run1, a0Color);
                        a0 += run1;
                        a0Color = !a0Color;
                        var run2 = ReadMmrRunLength(bits, a0Color);
                        FillRun(cur, a0, a0 + run2, a0Color);
                        a0 += run2;
                        a0Color = !a0Color;
                        break;
                    default:
                        var offset = mode - 5;
                        var a1 = b1 + offset;
                        if (a1 < a0) a1 = a0;
                        if (a1 > width) a1 = width;
                        FillRun(cur, a0, a1, a0Color);
                        a0 = a1;
                        a0Color = !a0Color;
                        break;
                }
            }
        }

        private static int FindChangingElement(bool[] line, int start, bool color, int width)
        {
            for (var i = Math.Max(start, 0); i < width; i++)
                if (line[i] == color) return i;
            return width;
        }

        private static void FillRun(bool[] line, int from, int to, bool color)
        {
            if (!color) return;
            for (var i = Math.Max(from, 0); i < Math.Min(to, line.Length); i++)
                line[i] = true;
        }

        private static int ReadMmrMode(MmrBitReader bits)
        {
            if (bits.ReadBit() == 1) return 5;
            if (bits.ReadBit() == 1)
            {
                if (bits.ReadBit() == 1) return 1;
                return 0;
            }
            if (bits.ReadBit() == 1)
                return bits.ReadBit() == 0 ? 0 : 5;
            if (bits.ReadBit() == 1)
                return bits.ReadBit() == 0 ? 6 : 4;
            if (bits.ReadBit() == 1)
                return bits.ReadBit() == 0 ? 7 : 3;
            if (bits.ReadBit() == 1)
                return bits.ReadBit() == 0 ? 8 : 2;
            return 5;
        }

        private static int ReadMmrRunLength(MmrBitReader bits, bool isBlack)
        {
            var runLen = 0;
            while (true)
            {
                var code = ReadHuffmanCode(bits, isBlack);
                runLen += code;
                if (code < 64) break;
            }
            return runLen;
        }

        private static int ReadHuffmanCode(MmrBitReader bits, bool isBlack)
        {
            var code = 0;
            for (var len = 1; len <= 13; len++)
            {
                code = (code << 1) | bits.ReadBit();
                var rl = LookupHuffman(code, len, isBlack);
                if (rl >= 0) return rl;
            }
            return 0;
        }

        private static int LookupHuffman(int code, int bits, bool isBlack)
        {
            if (!isBlack)
            {
                return (bits, code) switch
                {
                    (4, 0x7) => 2, (4, 0x8) => 3, (4, 0xB) => 4, (4, 0xC) => 5, (4, 0xE) => 6, (4, 0xF) => 7,
                    (5, 0x13) => 8, (5, 0x14) => 9, (5, 0x07) => 10, (5, 0x08) => 11,
                    (6, 0x08) => 12, (6, 0x03) => 13, (6, 0x34) => 14, (6, 0x35) => 15,
                    (6, 0x2A) => 16, (6, 0x2B) => 17,
                    (7, 0x27) => 18, (7, 0x0C) => 19, (7, 0x08) => 20, (7, 0x17) => 21,
                    (7, 0x03) => 22, (7, 0x04) => 23, (7, 0x28) => 24, (7, 0x2B) => 25,
                    (7, 0x13) => 26, (7, 0x24) => 27, (7, 0x18) => 28,
                    (8, 0x02) => 0, (8, 0x03) => 1, (8, 0x1A) => 29, (8, 0x1B) => 30,
                    (8, 0x12) => 31, (8, 0x13) => 32, (8, 0x14) => 33, (8, 0x15) => 34,
                    (8, 0x16) => 35, (8, 0x17) => 36, (8, 0x28) => 37, (8, 0x29) => 38,
                    (8, 0x2A) => 39, (8, 0x2B) => 40, (8, 0x2C) => 41, (8, 0x2D) => 42,
                    (8, 0x04) => 43, (8, 0x05) => 44, (8, 0x0A) => 45, (8, 0x0B) => 46,
                    (8, 0x52) => 47, (8, 0x53) => 48, (8, 0x54) => 49, (8, 0x55) => 50,
                    (8, 0x24) => 51, (8, 0x25) => 52, (8, 0x58) => 53, (8, 0x59) => 54,
                    (8, 0x5A) => 55, (8, 0x5B) => 56, (8, 0x4A) => 57, (8, 0x4B) => 58,
                    (8, 0x32) => 59, (8, 0x33) => 60, (8, 0x34) => 61, (8, 0x35) => 62,
                    (8, 0x36) => 63,
                    (5, 0x1B) => 64, (5, 0x12) => 128,
                    (6, 0x17) => 192, (7, 0x37) => 256,
                    (9, 0x66) => 320, (9, 0x67) => 384, (8, 0x64) => 448, (8, 0x65) => 512,
                    (8, 0x68) => 576, (8, 0x67) => 640,
                    (9, 0xCC) => 704, (9, 0xCD) => 768, (9, 0xD2) => 832, (9, 0xD3) => 896,
                    (9, 0xD4) => 960, (9, 0xD5) => 1024, (9, 0xD6) => 1088, (9, 0xD7) => 1152,
                    (9, 0xD8) => 1216, (9, 0xD9) => 1280, (9, 0xDA) => 1344, (9, 0xDB) => 1408,
                    (9, 0x98) => 1472, (9, 0x99) => 1536, (9, 0x9A) => 1600,
                    (6, 0x18) => 1664, (9, 0x9B) => 1728,
                    _ => -1,
                };
            }
            else
            {
                return (bits, code) switch
                {
                    (2, 0x3) => 2, (3, 0x2) => 3, (3, 0x3) => 4,
                    (4, 0x2) => 5, (4, 0x3) => 6,
                    (5, 0x3) => 7,
                    (6, 0x5) => 8, (6, 0x4) => 9,
                    (7, 0x4) => 10, (7, 0x5) => 11, (7, 0x7) => 12,
                    (8, 0x04) => 13, (8, 0x07) => 14,
                    (9, 0x18) => 15,
                    (10, 0x17) => 0, (10, 0x18) => 1, (10, 0x08) => 16,
                    (10, 0x37) => 17, (10, 0x33) => 18, (10, 0x34) => 19,
                    (11, 0x6C) => 20, (11, 0x37) => 21, (11, 0x28) => 22,
                    (11, 0x17) => 23, (11, 0x18) => 24,
                    (12, 0xCA) => 25, (12, 0xCB) => 26, (12, 0xCC) => 27,
                    (12, 0xCD) => 28, (12, 0x68) => 29, (12, 0x69) => 30,
                    (12, 0x6A) => 31, (12, 0x6B) => 32, (12, 0xD2) => 33,
                    (12, 0xD3) => 34, (12, 0xD4) => 35, (12, 0xD5) => 36,
                    (12, 0xD6) => 37, (12, 0xD7) => 38, (12, 0x6C) => 39,
                    (12, 0x6D) => 40, (12, 0xDA) => 41, (12, 0xDB) => 42,
                    (12, 0x54) => 43, (12, 0x55) => 44, (12, 0x56) => 45,
                    (12, 0x57) => 46, (12, 0x64) => 47, (12, 0x65) => 48,
                    (12, 0x52) => 49, (12, 0x53) => 50, (12, 0x24) => 51,
                    (12, 0x37) => 52, (12, 0x38) => 53, (12, 0x27) => 54,
                    (12, 0x28) => 55, (12, 0x58) => 56, (12, 0x59) => 57,
                    (12, 0x2B) => 58, (12, 0x2C) => 59, (12, 0x5A) => 60,
                    (12, 0x66) => 61, (12, 0x67) => 62, (13, 0x0F) => 63,
                    (10, 0x0F) => 64, (12, 0xC8) => 128,
                    (12, 0xC9) => 192, (12, 0x5B) => 256, (12, 0x33) => 320,
                    (12, 0x34) => 384, (12, 0x35) => 448,
                    (13, 0x6C) => 512, (13, 0x6D) => 576, (13, 0x4A) => 640,
                    (13, 0x4B) => 704, (13, 0x4C) => 768, (13, 0x4D) => 832,
                    (13, 0x72) => 896, (13, 0x73) => 960, (13, 0x74) => 1024,
                    (13, 0x75) => 1088, (13, 0x76) => 1152, (13, 0x77) => 1216,
                    (13, 0x52) => 1280, (13, 0x53) => 1344, (13, 0x54) => 1408,
                    (13, 0x55) => 1472, (13, 0x5A) => 1536, (13, 0x5B) => 1600,
                    (13, 0x64) => 1664, (13, 0x65) => 1728,
                    _ => -1,
                };
            }
        }
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
    // MMR / arithmetic primitives — retained from the prior implementation
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>Bit-level reader for MMR data.</summary>
    private sealed class MmrBitReader
    {
        private readonly byte[] _data;
        private int _pos;
        private readonly int _end;
        private int _bits;
        private int _bitsLeft;

        public MmrBitReader(byte[] data, int start, int end)
        {
            _data = data;
            _pos = start;
            _end = end;
        }

        public int ReadBit()
        {
            if (_bitsLeft == 0)
            {
                _bits = _pos < _end ? _data[_pos++] : 0;
                _bitsLeft = 8;
            }
            _bitsLeft--;
            return (_bits >> _bitsLeft) & 1;
        }
    }

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
