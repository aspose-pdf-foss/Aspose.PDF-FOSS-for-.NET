using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    /// <summary>The parsed head of a symbol-dictionary segment (T.88 §7.4.4): the SDFLAGS bits,
    /// the generic and refinement AT pixels, and the exported/new symbol counts.
    /// <see cref="DataStart"/> is where the coded symbol data begins — for the Huffman variant
    /// that is right after SDFLAGS, since its own head is read by the Huffman decoder.</summary>
    private sealed class SymbolDictionaryHeader
    {
        public int SdFlags;
        public bool SdHuff;
        public bool SdRefAgg;
        public int SdTemplate;
        public int SdrTemplate;
        public (int dx, int dy)[] SdAt = [];
        public (int dx, int dy)[] SdrAt = new (int dx, int dy)[2];
        public int SdNumExSyms;
        public int SdNumNewSyms;
        public int DataStart;
    }

    /// <summary>The arithmetic decoders one symbol dictionary reads through. Each carries its own
    /// adaptive contexts, so a dictionary gets a fresh set and they persist across every height
    /// class and every aggregate symbol in it.</summary>
    private sealed class SymbolDictionaryDecoders
    {
        public SymbolDictionaryDecoders(ArithmeticDecoder ad, SymbolDictionaryHeader sdh, int symCodeLen)
        {
            Ad = ad;
            Dh = new IntegerDecoder(ad);
            Dw = new IntegerDecoder(ad);
            Ex = new IntegerDecoder(ad);
            Grd = new GenericRegionDecoder(ad, sdh.SdTemplate, sdh.SdAt);
            // Refinement/aggregate symbols (SDREFAGG, T.88 §6.5.8.2): each new symbol is a
            // refinement of an existing one. Reuse the generic refinement-region decoder.
            Ai = new IntegerDecoder(ad);
            Rdx = new IntegerDecoder(ad);
            Rdy = new IntegerDecoder(ad);
            Id = new IaidDecoder(ad, symCodeLen);
            GrCtx = new ArithmeticContext[8192];
            if (sdh.SdRefAgg)
                for (var i = 0; i < GrCtx.Length; i++) GrCtx[i] = new ArithmeticContext();
            // Extra integer decoders used only by aggregate (REFAGGNINST>1) symbols, which are
            // coded as an embedded text region (§6.5.8.2.2).
            Dt = new IntegerDecoder(ad);
            Fs = new IntegerDecoder(ad);
            Ds = new IntegerDecoder(ad);
            It = new IntegerDecoder(ad);
            Ri = new IntegerDecoder(ad);
            Rdw = new IntegerDecoder(ad);
            Rdh = new IntegerDecoder(ad);
        }

        public ArithmeticDecoder Ad { get; }
        public IntegerDecoder Dh { get; }
        public IntegerDecoder Dw { get; }
        public IntegerDecoder Ex { get; }
        public GenericRegionDecoder Grd { get; }
        public IntegerDecoder Ai { get; }
        public IntegerDecoder Rdx { get; }
        public IntegerDecoder Rdy { get; }
        public IaidDecoder Id { get; }
        public ArithmeticContext[] GrCtx { get; }
        public IntegerDecoder Dt { get; }
        public IntegerDecoder Fs { get; }
        public IntegerDecoder Ds { get; }
        public IntegerDecoder It { get; }
        public IntegerDecoder Ri { get; }
        public IntegerDecoder Rdw { get; }
        public IntegerDecoder Rdh { get; }
    }

    private sealed partial class DecodeContext
    {
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

        private void DecodeSymbolDictionary(SegmentHeader hdr)
        {
            if (ReadSymbolDictionaryHeader(hdr) is not { } sdh) return;
            if (sdh.SdHuff) DecodeSymbolDictionaryHuffman(hdr, sdh);
            else DecodeSymbolDictionaryArithmetic(hdr, sdh);
        }

        /// <summary>Reads a symbol dictionary's fixed head (T.88 §7.4.4). Null means the segment
        /// is truncated or declares a symbol count this decoder will not allocate for. The
        /// Huffman variant stops after SDFLAGS: its own head follows a different layout.</summary>
        private SymbolDictionaryHeader? ReadSymbolDictionaryHeader(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 2 > _data.Length) return null;

            // SDFLAGS is a 16-bit big-endian field (T.88 §7.4.3.1.1).
            var sdh = new SymbolDictionaryHeader { SdFlags = (_data[p] << 8) | _data[p + 1] };
            p += 2;

            sdh.SdHuff = (sdh.SdFlags & 0x0001) != 0;       // bit 0  SDHUFF
            sdh.SdRefAgg = (sdh.SdFlags & 0x0002) != 0;     // bit 1  SDREFAGG
            sdh.SdTemplate = (sdh.SdFlags >> 10) & 0x03;    // bits 10-11  SDTEMPLATE
            sdh.SdrTemplate = (sdh.SdFlags >> 12) & 0x01;   // bit 12      SDRTEMPLATE

            if (sdh.SdHuff)
            {
                sdh.DataStart = p;
                return sdh;
            }

            // AT pixels for the symbol dictionary's generic region (SDAT).
            // 8 bytes for SDTEMPLATE=0, 2 bytes otherwise.
            var sdAtCount = sdh.SdTemplate == 0 ? 4 : 1;
            if (p + sdAtCount * 2 > _data.Length) return null;
            sdh.SdAt = new (int dx, int dy)[sdAtCount];
            for (var i = 0; i < sdAtCount; i++)
            {
                sdh.SdAt[i] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                p += 2;
            }

            // SDRAT (refinement AT pixels) - present when SDREFAGG=1 and SDRTEMPLATE=0.
            if (sdh.SdRefAgg && sdh.SdrTemplate == 0)
            {
                if (p + 4 > _data.Length) return null;
                sdh.SdrAt[0] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                sdh.SdrAt[1] = ((sbyte)_data[p + 2], (sbyte)_data[p + 3]);
                p += 4;
            }

            // SDNUMEXSYMS + SDNUMNEWSYMS
            if (p + 8 > _data.Length) return null;
            sdh.SdNumExSyms = ReadInt32BE(_data, p);
            sdh.SdNumNewSyms = ReadInt32BE(_data, p + 4);
            p += 8;

            if (sdh.SdNumNewSyms < 0 || sdh.SdNumNewSyms > 1_000_000) return null;
            if (sdh.SdNumExSyms < 0 || sdh.SdNumExSyms > 1_000_000) return null;

            sdh.DataStart = p;
            return sdh;
        }

        /// <summary>The dictionary's symbol array: the symbols imported from every referred-to
        /// dictionary, in order, with room after them for the new ones this segment codes.</summary>
        private (Jbig2Bitmap[] allSyms, int importedCount) OpenSymbolArray(SegmentHeader hdr, int sdNumNewSyms)
        {
            var imported = new List<Jbig2Bitmap>();
            foreach (var refSeg in hdr.ReferredTo)
            {
                if (_symbolDicts.TryGetValue(refSeg, out var importedSyms))
                    imported.AddRange(importedSyms);
            }
            var allSyms = new Jbig2Bitmap[imported.Count + sdNumNewSyms];
            for (var i = 0; i < imported.Count; i++) allSyms[i] = imported[i];
            return (allSyms, imported.Count);
        }

        /// <summary>Arithmetic-coded symbol dictionary (T.88 §6.5, SDHUFF=0): decode the new
        /// symbols height class by height class, then publish the exported ones.</summary>
        private void DecodeSymbolDictionaryArithmetic(SegmentHeader hdr, SymbolDictionaryHeader sdh)
        {
            var (allSyms, importedCount) = OpenSymbolArray(hdr, sdh.SdNumNewSyms);
            var dec = new SymbolDictionaryDecoders(new ArithmeticDecoder(_data, sdh.DataStart),
                sdh, Math.Max(1, SymCodeLength(allSyms.Length)));

            var hcHeight = 0;
            var newIdx = 0;
            while (newIdx < sdh.SdNumNewSyms)
            {
                if (dec.Dh.Decode() is not { } hcDelta) break;
                hcHeight += hcDelta;
                if (hcHeight <= 0 || hcHeight > 65535) break;
                var beforeClass = newIdx;
                if (DecodeHeightClass(sdh, dec, allSyms, importedCount, hcHeight, newIdx)
                    is not { } advanced) break;
                newIdx = advanced;
                if (newIdx == beforeClass) break; // no progress in this class - malformed stream
            }

            PublishExportedSymbols(hdr, allSyms, ExportRuns(dec.Ex, allSyms.Length), sdh.SdNumExSyms);
        }

        /// <summary>One height class: symbols of this height, left to right, until the OOB IADW
        /// that ends the class. Returns the running new-symbol count, or null when the stream ran
        /// out mid-class and the whole dictionary must stop.</summary>
        private int? DecodeHeightClass(SymbolDictionaryHeader sdh, SymbolDictionaryDecoders dec,
            Jbig2Bitmap[] allSyms, int importedCount, int hcHeight, int newIdx)
        {
            var symWidth = 0;
            while (true)
            {
                // OOB: end of height class
                if (dec.Dw.Decode() is not { } dw) return newIdx;
                symWidth += dw;
                if (symWidth <= 0 || symWidth > 65535) return null;
                if (newIdx >= sdh.SdNumNewSyms) return newIdx;

                if (DecodeDictionarySymbol(sdh, dec, allSyms, importedCount + newIdx,
                        symWidth, hcHeight) is not { } bitmap) return null;
                allSyms[importedCount + newIdx] = bitmap;
                newIdx++;
            }
        }

        /// <summary>One new symbol: the generic region it is coded as, or - under SDREFAGG - a
        /// refinement of an earlier symbol, or a text region aggregating several of them. Null
        /// means the stream ran out mid-symbol.</summary>
        private Jbig2Bitmap? DecodeDictionarySymbol(SymbolDictionaryHeader sdh,
            SymbolDictionaryDecoders dec, Jbig2Bitmap[] allSyms, int available,
            int symWidth, int hcHeight)
        {
            if (!sdh.SdRefAgg) return dec.Grd.Decode(symWidth, hcHeight);
            if (dec.Ai.Decode() is not { } nInst) return null;
            if (nInst != 1)
            {
                // Aggregate of >1 instances (§6.5.8.2.2): the symbol bitmap is a text region
                // placing nInst of the symbols decoded so far, sharing this dictionary's
                // arithmetic decoder and contexts.
                var avail = new System.ArraySegment<Jbig2Bitmap>(allSyms, 0, available);
                return DecodeTextRegionBitmap(dec.Ad, avail,
                    dec.Dt, dec.Fs, dec.Ds, dec.It, dec.Ri, dec.Id, dec.Rdw, dec.Rdh, dec.Rdx, dec.Rdy, dec.GrCtx,
                    symWidth, hcHeight, nInst, sbStrips: 1, refCorner: 1, transposed: false,
                    sbCombOp: 0, sbDefPixel: false, sbDsOffset: 0, sbRefine: true,
                    sbrTemplate: sdh.SdrTemplate, sbrAt: sdh.SdrAt);
            }

            // Single-symbol refinement: refine the referenced symbol.
            var refId = dec.Id.Decode();
            if (dec.Rdx.Decode() is not { } rdx) return null;
            if (dec.Rdy.Decode() is not { } rdy) return null;
            var reference = refId >= 0 && refId < available
                ? allSyms[refId] : new Jbig2Bitmap(symWidth, hcHeight);
            return DecodeRefinement(dec.Ad, dec.GrCtx, symWidth, hcHeight, sdh.SdrTemplate,
                sdh.SdrAt, reference, rdx, rdy);
        }

        /// <summary>The export run lengths an arithmetic dictionary ends with (IAEX).</summary>
        private static IEnumerable<int> ExportRuns(IntegerDecoder iaEx, int totalSyms)
        {
            for (var seen = 0; seen < totalSyms;)
            {
                if (iaEx.Decode() is not { } run) yield break;
                if (run < 0 || run > totalSyms - seen) run = totalSyms - seen;
                seen += run;
                yield return run;
            }
        }

        /// <summary>The export run lengths a Huffman dictionary ends with (Table B.1).</summary>
        private static IEnumerable<int> ExportRuns(HuffTable tEx, HuffBitReader reader, int totalSyms)
        {
            for (var seen = 0; seen < totalSyms;)
            {
                if (tEx.Decode(reader) is not { } run) yield break;
                if (run < 0 || run > totalSyms - seen) run = totalSyms - seen;
                seen += run;
                yield return run;
            }
        }

        /// <summary>Publishes the dictionary's exported symbols. The export bits alternate: the
        /// walk starts at "don't export" and each run flips the bit.</summary>
        private void PublishExportedSymbols(SegmentHeader hdr, Jbig2Bitmap[] allSyms,
            IEnumerable<int> runs, int sdNumExSyms)
        {
            var exported = new List<Jbig2Bitmap>(sdNumExSyms);
            var exporting = false;
            var seen = 0;
            foreach (var run in runs)
            {
                if (exporting)
                {
                    for (var k = 0; k < run; k++)
                    {
                        var sym = allSyms[seen + k];
                        if (sym is not null) exported.Add(sym);
                    }
                }
                seen += run;
                exporting = !exporting;
            }

            _symbolDicts[hdr.Number] = exported.ToArray();
            if (Jbig2Debug)
                System.Console.Error.WriteLine("[jbig2] symdict seg " + hdr.Number + " exported " + exported.Count);
        }

        /// <summary>Huffman-coded symbol dictionary (T.88 §6.5, SDHUFF=1). Symbols
        /// arrive in height classes: per class a height delta, then per symbol a
        /// width delta (OOB ends the class); the class's glyphs are carried in one
        /// COLLECTIVE bitmap — raw rows when BMSIZE=0, MMR-coded otherwise — that
        /// is sliced up by the recorded widths. Refinement/aggregation (SDREFAGG)
        /// is not handled in the Huffman path.</summary>
        private void DecodeSymbolDictionaryHuffman(SegmentHeader hdr, SymbolDictionaryHeader sdh)
        {
            if (sdh.SdRefAgg) return; // Huffman + refinement/aggregation: out of scope

            var dhSel = (sdh.SdFlags >> 2) & 0x03;   // 0→B.4, 1→B.5
            var dwSel = (sdh.SdFlags >> 4) & 0x03;   // 0→B.2, 1→B.3
            var bmSel = (sdh.SdFlags >> 6) & 0x01;   // 0→B.1
            // Custom tables (selector 3 / 1 for BMSIZE) come from referred table
            // segments, which this decoder does not parse yet.
            if (dhSel > 1 || dwSel > 1 || bmSel != 0) return;
            var tDh = StdTable(dhSel == 0 ? 4 : 5);
            var tDw = StdTable(dwSel == 0 ? 2 : 3);
            var tBm = StdTable(1);
            var tEx = StdTable(1);

            var p = sdh.DataStart;
            if (p + 8 > _data.Length) return;
            var sdNumExSyms = ReadInt32BE(_data, p);
            var sdNumNewSyms = ReadInt32BE(_data, p + 4);
            p += 8;
            if (sdNumNewSyms < 0 || sdNumNewSyms > 1_000_000) return;
            if (sdNumExSyms < 0 || sdNumExSyms > 1_000_000) return;

            var (allSyms, importedCount) = OpenSymbolArray(hdr, sdNumNewSyms);
            var reader = new HuffBitReader(_data, p, hdr.DataStart + hdr.DataLength);
            var hcHeight = 0;
            var newIdx = 0;

            while (newIdx < sdNumNewSyms)
            {
                if (tDh.Decode(reader) is not { } hcDelta) break;
                hcHeight += hcDelta;
                if (hcHeight <= 0 || hcHeight > 65535) break;

                var classStart = newIdx;
                if (ReadHeightClassWidths(tDw, reader, sdNumNewSyms - newIdx) is not { } widths) return;
                newIdx += widths.Count;
                if (widths.Count == 0) break; // no progress — malformed stream

                var totWidth = 0;
                foreach (var w in widths) totWidth += w;
                if (DecodeCollectiveBitmap(tBm, reader, totWidth, hcHeight) is not { } collective) return;
                SliceCollectiveBitmap(collective, widths, hcHeight, allSyms, importedCount + classStart);
            }

            PublishExportedSymbols(hdr, allSyms, ExportRuns(tEx, reader, allSyms.Length), sdNumExSyms);
        }

        /// <summary>The widths of one height class's symbols. The OOB code terminates every height
        /// class and must always be consumed — even when the class supplies the last symbol — or
        /// the bit stream desyncs before the class's BMSIZE field. Null means the class declared
        /// an impossible width, or more widths than the dictionary has symbols.</summary>
        private static List<int>? ReadHeightClassWidths(HuffTable tDw, HuffBitReader reader, int remaining)
        {
            var widths = new List<int>();
            var symWidth = 0;
            while (true)
            {
                if (tDw.Decode(reader) is not { } dw) return widths;
                symWidth += dw;
                if (symWidth <= 0 || symWidth > 65535) return null;
                if (widths.Count >= remaining) return null; // more widths than declared symbols
                widths.Add(symWidth);
            }
        }

        /// <summary>The height class's collective bitmap (§6.5.9): BMSIZE=0 → uncompressed rows
        /// (each row padded to a byte); otherwise BMSIZE bytes of MMR. Null means the stream ran
        /// out, or the MMR payload would not decode.</summary>
        private Jbig2Bitmap? DecodeCollectiveBitmap(HuffTable tBm, HuffBitReader reader,
            int totWidth, int hcHeight)
        {
            if (tBm.Decode(reader) is not { } bmSize || bmSize < 0) return null;
            reader.Align();
            if (bmSize != 0) return DecodeCollectiveMmr(reader, bmSize, totWidth, hcHeight);

            var collective = new Jbig2Bitmap(totWidth, hcHeight);
            var rowBytes = (totWidth + 7) / 8;
            var src = reader.BytePos;
            if (src + (long)rowBytes * hcHeight > _data.Length) return null;
            for (var y = 0; y < hcHeight; y++)
                for (var x = 0; x < totWidth; x++)
                    collective.SetPixel(x, y, (_data[src + y * rowBytes + x / 8] >> (7 - x % 8)) & 1);
            reader.SkipBytes(rowBytes * hcHeight);
            return collective;
        }

        /// <summary>The class's MMR payload is T.6 (Group 4) — decode with the shared CCITT filter
        /// (the JBIG2-local MMR line decoder predates it and mishandles real G4 streams).</summary>
        private Jbig2Bitmap? DecodeCollectiveMmr(HuffBitReader reader, int bmSize,
            int totWidth, int hcHeight)
        {
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
            catch { return null; }
            var cRowBytes = (totWidth + 7) / 8;
            if (rows.Length < cRowBytes * hcHeight) return null;
            reader.SkipBytes(bmSize);
            return new Jbig2Bitmap(totWidth, hcHeight, cRowBytes, rows);
        }

        /// <summary>Slices a height class's collective bitmap into its symbols.</summary>
        private static void SliceCollectiveBitmap(Jbig2Bitmap collective, List<int> widths,
            int hcHeight, Jbig2Bitmap[] allSyms, int firstSlot)
        {
            var xOff = 0;
            for (var s = 0; s < widths.Count; s++)
            {
                var w = widths[s];
                var sym = new Jbig2Bitmap(w, hcHeight);
                for (var y = 0; y < hcHeight; y++)
                    for (var x = 0; x < w; x++)
                        sym.SetPixel(x, y, collective.GetPixel(xOff + x, y));
                allSyms[firstSlot + s] = sym;
                xOff += w;
            }
        }


    }
}
