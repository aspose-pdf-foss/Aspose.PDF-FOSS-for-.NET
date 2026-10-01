
namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        private void DecodeProgressiveScan()
        {
            EnsureCoefficientArrays();
            var bits = new BitStream(_data, _pos);
            _eobrun = 0;
            var dcPred = new int[_components.Length];
            var restartCounter = 0;

            if (_scanComponentIndices.Length == 1)
            {
                // Non-interleaved scan: one data unit per component block, row-major
                // over the component's own (unaligned) block grid.
                var ci = _scanComponentIndices[0];
                var comp = _components[ci];
                var compWidth = (Width * comp.H + _maxH - 1) / _maxH;
                var compHeight = (Height * comp.V + _maxV - 1) / _maxV;
                var bw = (compWidth + 7) / 8;
                var bh = (compHeight + 7) / 8;

                for (var by = 0; by < bh; by++)
                {
                    for (var bx = 0; bx < bw; bx++)
                    {
                        if (_restartInterval > 0 && restartCounter == _restartInterval)
                        {
                            bits.AlignByte();
                            bits.SkipRestartMarker();
                            Array.Clear(dcPred, 0, dcPred.Length);
                            _eobrun = 0;
                            restartCounter = 0;
                        }
                        dcPred[ci] = DecodeProgressiveBlock(bits, ci, bx, by, dcPred[ci]);
                        restartCounter++;
                    }
                }
            }
            else
            {
                var mcuCols = (Width + _maxH * 8 - 1) / (_maxH * 8);
                var mcuRows = (Height + _maxV * 8 - 1) / (_maxV * 8);
                for (var mcuRow = 0; mcuRow < mcuRows; mcuRow++)
                {
                    for (var mcuCol = 0; mcuCol < mcuCols; mcuCol++)
                    {
                        if (_restartInterval > 0 && restartCounter == _restartInterval)
                        {
                            bits.AlignByte();
                            bits.SkipRestartMarker();
                            Array.Clear(dcPred, 0, dcPred.Length);
                            _eobrun = 0;
                            restartCounter = 0;
                        }
                        for (var sc = 0; sc < _scanComponentIndices.Length; sc++)
                        {
                            var ci = _scanComponentIndices[sc];
                            var comp = _components[ci];
                            for (var bv = 0; bv < comp.V; bv++)
                                for (var bhh = 0; bhh < comp.H; bhh++)
                                    dcPred[ci] = DecodeProgressiveBlock(bits, ci,
                                        mcuCol * comp.H + bhh, mcuRow * comp.V + bv,
                                        dcPred[ci], sc);
                        }
                        restartCounter++;
                    }
                }
            }

            _pos = bits.BytePosition;
        }

        /// <returns>The component's DC predictor after this block.</returns>
        private int DecodeProgressiveBlock(BitStream bits, int ci, int bx, int by,
            int dcPred, int scanComponent = 0)
        {
            var blockOff = (by * _blocksPerLine[ci] + bx) * 64;
            var coefs = _coefs[ci];

            if (_ss == 0)
            {
                var dcTable = _dcTables[_scanDcTableIds[scanComponent]];
                if (_ah == 0)
                {
                    // DC first scan
                    var cat = DecodeHuffman(bits, dcTable!);
                    var diff = cat > 0 ? ReceiveExtend(bits, cat) : 0;
                    dcPred += diff;
                    coefs[blockOff] = dcPred << _al;
                }
                else
                {
                    // DC refinement — one correction bit
                    if (bits.ReadBit() != 0)
                        coefs[blockOff] |= 1 << _al;
                }
                return dcPred;
            }

            var acTable = _acTables[_scanAcTableIds[scanComponent]]!;
            if (_ah == 0)
            {
                // AC first scan
                if (_eobrun > 0) { _eobrun--; return dcPred; }
                var k = _ss;
                while (k <= _se)
                {
                    var rs = DecodeHuffman(bits, acTable);
                    var r = (rs >> 4) & 0xF;
                    var s = rs & 0xF;
                    if (s == 0)
                    {
                        if (r < 15)
                        {
                            _eobrun = (1 << r) - 1;
                            if (r > 0) _eobrun += bits.ReadBits(r);
                            break;
                        }
                        k += 16;
                        continue;
                    }
                    k += r;
                    if (k > _se) break;
                    coefs[blockOff + ZigZag[k]] = ReceiveExtend(bits, s) << _al;
                    k++;
                }
                return dcPred;
            }

            // AC refinement scan (IJG decode_mcu_AC_refine structure)
            var p1 = 1 << _al;
            var m1 = -1 << _al;
            var ki = _ss;
            DecodeProgressiveAcRefinement(bits, acTable, coefs, blockOff, p1, m1, ref ki);

            if (_eobrun > 0)
            {
                // Inside an EOB run: only correction bits for already-nonzero coefficients.
                while (ki <= _se)
                {
                    var pos = blockOff + ZigZag[ki];
                    if (coefs[pos] != 0 && bits.ReadBit() != 0 && (coefs[pos] & p1) == 0)
                        coefs[pos] += coefs[pos] >= 0 ? p1 : m1;
                    ki++;
                }
                _eobrun--;
            }
            return dcPred;
        }

        private void FinishProgressive()
        {
            if (_outputDone || _coefs.Length == 0) return;
            _outputDone = true;

            // Component sample buffers hold clamped 0..255 values — byte[], not
            // int[]: at scan size these arrays live on the LOH and a 4× width
            // quadruples the per-image heap spike.
            var buffers = new byte[_components.Length][];
            var bufWidths = new int[_components.Length];
            for (var i = 0; i < _components.Length; i++)
            {
                var bw = _blocksPerLine[i];
                var bh = _blocksPerCol[i];
                bufWidths[i] = bw * 8;
                buffers[i] = new byte[bw * 8 * bh * 8];
                var qt = _quantTables[_components[i].QtId] ?? _quantTables[0];
                var block = new int[64];
                for (var by = 0; by < bh; by++)
                {
                    for (var bx = 0; bx < bw; bx++)
                    {
                        Array.Copy(_coefs[i], (by * bw + bx) * 64, block, 0, 64);
                        Dequantize(block, qt!);
                        IDCT(block);
                        var px = bx * 8;
                        var py = by * 8;
                        for (var y = 0; y < 8; y++)
                        {
                            var dst = (py + y) * bufWidths[i] + px;
                            var src = y * 8;
                            for (var x = 0; x < 8; x++)
                                buffers[i][dst + x] = (byte)Clamp(block[src + x] + 128);
                        }
                    }
                }
            }

            ConvertBuffersToPixels(buffers, bufWidths);
        }

        private void DecodeScan()
        {
            var mcuW = _maxH * 8; // MCU width in pixels
            var mcuH = _maxV * 8; // MCU height in pixels
            var mcuCols = (Width + mcuW - 1) / mcuW;
            var mcuRows = (Height + mcuH - 1) / mcuH;

            // Allocate component buffers (full MCU-aligned dimensions). Samples are
            // clamped 0..255 — byte[] keeps the LOH spike a quarter of int[]'s.
            var buffers = new byte[Components][];
            var bufWidths = new int[Components];
            var bufHeights = new int[Components];
            for (var i = 0; i < Components; i++)
            {
                var cw = mcuCols * _components[i].H * 8;
                var ch = mcuRows * _components[i].V * 8;
                buffers[i] = new byte[cw * ch];
                bufWidths[i] = cw;
                bufHeights[i] = ch;
            }

            var bits = new BitStream(_data, _pos);
            var dcPred = new int[Components];

            // A scan is ONE of the two walks, never both: a single-component scan that
            // also ran the interleaved walk would decode the same, exhausted bit stream a
            // second time over its own finished buffers.
            if (_scanComponentIndices.Length == 1)
                DecodeSingleComponentScan(bits, dcPred, buffers, bufWidths);
            else
                DecodeInterleavedMcus(bits, dcPred, buffers, bufWidths, mcuCols, mcuRows);

            _pos = bits.BytePosition;

            ConvertBuffersToPixels(buffers, bufWidths);
        }

        /// <returns>The component's DC predictor after this block.</returns>
        private static int DecodeBlock(BitStream bits, int[] block, int dcPred,
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
            return dcPred;
        }

    }
}
