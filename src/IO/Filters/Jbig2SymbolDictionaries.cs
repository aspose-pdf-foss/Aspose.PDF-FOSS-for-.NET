using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
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
            if (Jbig2Debug)
                System.Console.Error.WriteLine("[jbig2] symdict seg " + hdr.Number + " exported " + exported.Count);
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
            if (Jbig2Debug)
                System.Console.Error.WriteLine("[jbig2] symdict seg " + hdr.Number + " exported " + exported.Count);
        }

    }
}
