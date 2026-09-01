using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    private sealed partial class DecodeContext
    {
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
            if (Jbig2Debug)
                System.Console.Error.WriteLine("[jbig2] textRegion seg " + hdr.Number + " refs [" + string.Join(",", hdr.ReferredTo) + "] symbols=" + symbols.Count + " huff=" + sbHuff + " refine=" + sbRefine + " inst=?");
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

    }
}
