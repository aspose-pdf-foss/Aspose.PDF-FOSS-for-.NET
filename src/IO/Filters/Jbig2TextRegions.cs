using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    /// <summary>The parsed head of a text-region segment (T.88 §7.4.3): the region box and its
    /// combination operator, the SBFLAGS bits, the refinement AT pixels and the instance count.
    /// <see cref="DataStart"/> is where the coded instance data begins.</summary>
    private sealed class TextRegionHeader
    {
        public int RegionW;
        public int RegionH;
        public int RegionX;
        public int RegionY;
        public int RegCombOp;
        public bool SbHuff;
        public bool SbRefine;
        public int Log2Strips;
        public int RefCorner;
        public bool Transposed;
        public int SbCombOp;
        public bool SbDefPixel;
        public int SbDsOffset;
        public int SbrTemplate;
        public int SbHuffFlags;
        public (int dx, int dy)[] SbrAt = new (int dx, int dy)[2];
        public int SbNumInstances;
        public int DataStart;

        /// <summary>SBSTRIPS: the strip height in rows, 2^LOG2STRIPS.</summary>
        public int Strips => 1 << Log2Strips;
    }

    /// <summary>The arithmetic decoders one text region's strip walk reads through. Each carries
    /// its own adaptive contexts, so a region gets a fresh set; the refinement ones and the GR
    /// context array are only seeded when SBREFINE is set.</summary>
    private sealed class TextRegionDecoders
    {
        public TextRegionDecoders(ArithmeticDecoder ad, int symCodeLen, bool sbRefine)
        {
            Ad = ad;
            Dt = new IntegerDecoder(ad);
            Fs = new IntegerDecoder(ad);
            Ds = new IntegerDecoder(ad);
            It = new IntegerDecoder(ad);
            Ri = new IntegerDecoder(ad);
            Id = new IaidDecoder(ad, symCodeLen);
            // Symbol-instance refinement (T.88 §6.4.11): per-instance refinement deltas and a
            // shared GR context array reused across every refined instance in this region.
            Rdw = new IntegerDecoder(ad);
            Rdh = new IntegerDecoder(ad);
            Rdx = new IntegerDecoder(ad);
            Rdy = new IntegerDecoder(ad);
            GrCtx = new ArithmeticContext[8192];
            if (sbRefine)
                for (var i = 0; i < GrCtx.Length; i++) GrCtx[i] = new ArithmeticContext();
        }

        public ArithmeticDecoder Ad { get; }
        public IntegerDecoder Dt { get; }
        public IntegerDecoder Fs { get; }
        public IntegerDecoder Ds { get; }
        public IntegerDecoder It { get; }
        public IntegerDecoder Ri { get; }
        public IaidDecoder Id { get; }
        public IntegerDecoder Rdw { get; }
        public IntegerDecoder Rdh { get; }
        public IntegerDecoder Rdx { get; }
        public IntegerDecoder Rdy { get; }
        public ArithmeticContext[] GrCtx { get; }
    }

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
            if (iaDt.Decode() is not { } firstDt) return region;
            int stripT = -firstDt, firstS = 0, decoded = 0;

            while (decoded < sbNumInstances)
            {
                var beforeStrip = decoded;
                if (iaDt.Decode() is not { } dt) break;
                stripT += dt;
                if (iaFs.Decode() is not { } dfs) break;   // IAFS: once per strip
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
                        if (iaIt.Decode() is not { } ct) break;
                        curT = ct;
                    }

                    var idVal = iaId.Decode();
                    if (idVal < 0 || idVal >= symbols.Count) idVal = 0;
                    var symBitmap = symbols[idVal];

                    if (sbRefine)
                    {
                        if (iaRi.Decode() is not { } riVal) break;
                        if (riVal != 0 && symBitmap is not null)
                        {
                            if (iaRdw.Decode() is not { } rdw) break;
                            if (iaRdh.Decode() is not { } rdh) break;
                            if (iaRdx.Decode() is not { } rdx) break;
                            if (iaRdy.Decode() is not { } rdy) break;
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

                        var (x, y) = PlaceSymbolInstance(refCorner, transposed, placeS, placeT, symW, symH);

                        region.CompositeAt(symBitmap, x, y, sbCombOp);
                        curS += (transposed ? symH : symW) - 1;
                    }

                    decoded++;
                    if (iaDs.Decode() is not { } dsVal) break;   // OOB → end of strip (consumed)
                    curS += dsVal + sbDsOffset;
                    if (decoded >= sbNumInstances) break;     // overrun guard (malformed stream)
                }

                if (decoded == beforeStrip) break;
            }
            return region;
        }

        private void DecodeTextRegion(SegmentHeader hdr)
        {
            if (ReadTextRegionHeader(hdr) is not { } trh) return;

            // Collect referenced symbols from all referred-to symbol dictionaries (in order).
            var symbols = new List<Jbig2Bitmap>();
            foreach (var refSeg in hdr.ReferredTo)
            {
                if (_symbolDicts.TryGetValue(refSeg, out var syms))
                    symbols.AddRange(syms);
            }
            if (Jbig2Debug)
                System.Console.Error.WriteLine("[jbig2] textRegion seg " + hdr.Number + " refs [" + string.Join(",", hdr.ReferredTo) + "] symbols=" + symbols.Count + " huff=" + trh.SbHuff + " refine=" + trh.SbRefine + " inst=?");
            if (symbols.Count == 0) return;

            if (Captures is not null)
            {
                _capturing = new Jbig2TextRegionPlacements(symbols.Count, trh.RegCombOp, trh.SbCombOp, trh.SbDefPixel)
                {
                    Reencodable = !trh.SbHuff,
                };
                Captures[hdr.Number] = _capturing;
            }
            if (trh.SbHuff) DecodeTextRegionHuffman(hdr, trh, symbols);
            else DecodeTextRegionArithmetic(trh, symbols);
            _capturing = null;
        }

        /// <summary>Reads a text region's fixed head (T.88 §7.4.3) and leaves
        /// <see cref="TextRegionHeader.DataStart"/> on the coded instance data. Null means the
        /// segment is truncated, or asks for a combination this decoder does not handle.</summary>
        private TextRegionHeader? ReadTextRegionHeader(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 17 > _data.Length) return null;

            // Region segment info (17 bytes)
            var trh = new TextRegionHeader
            {
                RegionW = ReadInt32BE(_data, p),
                RegionH = ReadInt32BE(_data, p + 4),
                RegionX = ReadInt32BE(_data, p + 8),
                RegionY = ReadInt32BE(_data, p + 12),
                RegCombOp = _data[p + 16] & 0x07,
            };
            p += 17;

            if (p + 2 > _data.Length) return null;
            // SBFLAGS is a 16-bit big-endian field (T.88 §7.4.3.1.1).
            var trFlags = (_data[p] << 8) | _data[p + 1];
            p += 2;

            trh.SbHuff = (trFlags & 0x0001) != 0;
            trh.SbRefine = (trFlags & 0x0002) != 0;
            trh.Log2Strips = (trFlags >> 2) & 0x03;
            trh.RefCorner = (trFlags >> 4) & 0x03;
            trh.Transposed = (trFlags & 0x0040) != 0;
            trh.SbCombOp = (trFlags >> 7) & 0x03;
            trh.SbDefPixel = (trFlags & 0x0200) != 0;
            // 5-bit signed delta-S offset (bits 10..14)
            var sbDsOffsetRaw = (trFlags >> 10) & 0x1F;
            trh.SbDsOffset = (sbDsOffsetRaw & 0x10) != 0 ? sbDsOffsetRaw - 32 : sbDsOffsetRaw;
            trh.SbrTemplate = (trFlags >> 15) & 0x01;

            // SBHUFFFLAGS (16-bit, §7.4.3.1.2) follows SBFLAGS in the Huffman variant.
            if (trh.SbHuff)
            {
                if (p + 2 > _data.Length) return null;
                trh.SbHuffFlags = (_data[p] << 8) | _data[p + 1];
                p += 2;
                // Huffman + per-instance refinement needs the RDW/RDH/RDX/RDY/RSIZE
                // table plumbing - not in scope (this corpus has SBREFINE=0).
                if (trh.SbRefine) return null;
            }

            // SBRAT (refinement AT pixels) - only if SBREFINE and SBRTEMPLATE=0.
            if (trh.SbRefine)
            {
                var sbrAtCount = trh.SbrTemplate == 0 ? 2 : 0;
                for (var i = 0; i < sbrAtCount; i++)
                {
                    if (p + 2 > _data.Length) return null;
                    trh.SbrAt[i] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                    p += 2;
                }
            }

            if (p + 4 > _data.Length) return null;
            trh.SbNumInstances = ReadInt32BE(_data, p);
            p += 4;
            if (trh.SbNumInstances <= 0 || trh.SbNumInstances > 10_000_000) return null;

            trh.DataStart = p;
            return trh;
        }

        /// <summary>Arithmetic-coded text region (T.88 §6.4, SBHUFF=0): walk the strips, placing
        /// each strip's symbol instances into the region bitmap, then store or composite it.</summary>
        private void DecodeTextRegionArithmetic(TextRegionHeader trh, List<Jbig2Bitmap> symbols)
        {
            var dec = new TextRegionDecoders(new ArithmeticDecoder(_data, trh.DataStart),
                SymCodeLength(symbols.Count), trh.SbRefine);

            // Region bitmap accumulator
            var region = new Jbig2Bitmap(trh.RegionW, trh.RegionH, trh.SbDefPixel);

            if (dec.Dt.Decode() is not { } firstDt) return;
            var stripT = -firstDt;
            var firstS = 0;
            var decoded = 0;

            while (decoded < trh.SbNumInstances)
            {
                var beforeStrip = decoded;
                if (dec.Dt.Decode() is not { } dt) break;
                stripT += dt;
                (decoded, firstS) = WalkTextRegionStrip(region, trh, symbols, dec, stripT, firstS, decoded);
                if (decoded == beforeStrip) break; // strip made no progress - bail out
            }

            StoreOrCompositeRegion(region, trh.RegionW, trh.RegionH, trh.RegionX, trh.RegionY, trh.RegCombOp);
        }

        /// <summary>Places one strip's symbol instances, from its first S coordinate to the OOB
        /// IADS that ends it, and reports the running instance count together with the opening S
        /// the next strip's own delta accumulates onto.</summary>
        private (int decoded, int firstS) WalkTextRegionStrip(Jbig2Bitmap region, TextRegionHeader trh,
            List<Jbig2Bitmap> symbols, TextRegionDecoders dec, int stripT, int firstS, int decoded)
        {
            var curS = 0;
            var first = true;

            while (decoded < trh.SbNumInstances)
            {
                if (first)
                {
                    if (dec.Fs.Decode() is not { } dfs) return (decoded, firstS);
                    firstS += dfs;
                    curS = firstS;
                    first = false;
                }
                else
                {
                    // OOB -> end of strip
                    if (dec.Ds.Decode() is not { } dsVal) return (decoded, firstS);
                    curS += dsVal + trh.SbDsOffset;
                }

                var curT = 0;
                if (trh.Strips > 1)
                {
                    if (dec.It.Decode() is not { } ct) return (decoded, firstS);
                    curT = ct;
                }

                var idVal = dec.Id.Decode();
                if (idVal < 0 || idVal >= symbols.Count) idVal = 0;

                var symBitmap = symbols[idVal];
                var wasRefined = false;
                if (trh.SbRefine)
                {
                    if (dec.Ri.Decode() is not { } riVal) return (decoded, firstS);
                    if (riVal != 0 && symBitmap is not null)
                    {
                        if (RefineInstance(trh, dec, symBitmap) is not { } refined) return (decoded, firstS);
                        symBitmap = refined;
                        wasRefined = true;
                    }
                }

                decoded++;
                if (symBitmap is null) continue;

                // STRIPT is decoded in units of SBSTRIPS rows (T.88 §6.4.5), so it is
                // scaled back up here before the placement rule is applied.
                var (x, y) = PlaceSymbolInstance(trh.RefCorner, trh.Transposed, curS,
                    stripT * trh.Strips + curT, symBitmap.Width, symBitmap.Height);
                _capturing?.Instances.Add(new Jbig2Placement(idVal, trh.RegionX + x, trh.RegionY + y,
                    symBitmap.Width, symBitmap.Height, wasRefined ? (byte[])symBitmap.Data.Clone() : null));

                region.CompositeAt(symBitmap, x, y, trh.SbCombOp);
                curS += (trh.Transposed ? symBitmap.Height : symBitmap.Width) - 1;
            }
            return (decoded, firstS);
        }

        /// <summary>Per-instance refinement (T.88 §6.4.11): decode the size deltas and run the
        /// refinement region with the original symbol as reference. The symbol comes back
        /// unrefined when the deltas put it outside the bitmap limits; null means the stream ran
        /// out mid-instance.</summary>
        private Jbig2Bitmap? RefineInstance(TextRegionHeader trh, TextRegionDecoders dec, Jbig2Bitmap symBitmap)
        {
            if (dec.Rdw.Decode() is not { } rdw) return null;
            if (dec.Rdh.Decode() is not { } rdh) return null;
            if (dec.Rdx.Decode() is not { } rdx) return null;
            if (dec.Rdy.Decode() is not { } rdy) return null;
            var rw = symBitmap.Width + rdw;
            var rh = symBitmap.Height + rdh;
            if (rw <= 0 || rh <= 0 || rw > 65535 || rh > 65535) return symBitmap;
            return DecodeRefinement(dec.Ad, dec.GrCtx, rw, rh, trh.SbrTemplate, trh.SbrAt,
                symBitmap, (rdw >> 1) + rdx, (rdh >> 1) + rdy);
        }


        /// <summary>Huffman-coded text region (T.88 §6.4, SBHUFF=1). Reads the
        /// runcode-compressed symbol-ID code table (§7.4.3.1.7), then places the
        /// symbol instances with the same strip walk as the arithmetic variant,
        /// with FS/DS/DT decoded through the selected standard tables and CURT
        /// read as raw bits. Per-instance refinement is not handled (callers gate
        /// on SBREFINE=0).</summary>
        private void DecodeTextRegionHuffman(SegmentHeader hdr, TextRegionHeader trh,
            List<Jbig2Bitmap> symbols)
        {
            var fsSel = trh.SbHuffFlags & 0x03;         // 0→B.6, 1→B.7
            var dsSel = (trh.SbHuffFlags >> 2) & 0x03;  // 0→B.8, 1→B.9, 2→B.10
            var dtSel = (trh.SbHuffFlags >> 4) & 0x03;  // 0→B.11, 1→B.12, 2→B.13
            // Selector 3 = custom table from referred table segments — unsupported.
            if (fsSel > 1 || dsSel > 2 || dtSel > 2) return;
            var tFs = StdTable(fsSel == 0 ? 6 : 7);
            var tDs = StdTable(8 + dsSel);
            var tDt = StdTable(11 + dtSel);

            var reader = new HuffBitReader(_data, trh.DataStart, hdr.DataStart + hdr.DataLength);

            if (ReadSymbolIdCodeTable(reader, symbols.Count) is not { } tId) return;

            var region = new Jbig2Bitmap(trh.RegionW, trh.RegionH, trh.SbDefPixel);

            if (tDt.Decode(reader) is not { } firstDt) return;
            var stripT = -firstDt;
            var firstS = 0;
            var decoded = 0;

            while (decoded < trh.SbNumInstances)
            {
                var beforeStrip = decoded;
                if (tDt.Decode(reader) is not { } dt) break;
                stripT += dt;

                var curS = 0;
                var first = true;

                while (decoded < trh.SbNumInstances)
                {
                    if (first)
                    {
                        if (tFs.Decode(reader) is not { } dfs) goto endStrip;
                        firstS += dfs;
                        curS = firstS;
                        first = false;
                    }
                    else
                    {
                        if (tDs.Decode(reader) is not { } dsVal) goto endStrip; // OOB → end of strip
                        curS += dsVal + trh.SbDsOffset;
                    }

                    // CURT is raw bits (⌈log2 SBSTRIPS⌉) in the Huffman variant.
                    var curT = trh.Strips > 1 ? reader.ReadBits(trh.Log2Strips) : 0;

                    if (tId.Decode(reader) is not { } idVal) goto endStrip;
                    if (idVal < 0 || idVal >= symbols.Count) idVal = 0;
                    var symBitmap = symbols[idVal];
                    if (symBitmap is null) { decoded++; continue; }

                    var symW = symBitmap.Width;
                    var symH = symBitmap.Height;

                    var placeS = curS;
                    var placeT = stripT * trh.Strips + curT;

                    var (x, y) = PlaceSymbolInstance(trh.RefCorner, trh.Transposed, placeS, placeT, symW, symH);

                    region.CompositeAt(symBitmap, x, y, trh.SbCombOp);

                    curS += (trh.Transposed ? symH : symW) - 1;

                    decoded++;
                }
            endStrip:
                if (decoded == beforeStrip) break; // strip made no progress — bail out
            }

            StoreOrCompositeRegion(region, trh.RegionW, trh.RegionH, trh.RegionX, trh.RegionY, trh.RegCombOp);
        }

    }
}
