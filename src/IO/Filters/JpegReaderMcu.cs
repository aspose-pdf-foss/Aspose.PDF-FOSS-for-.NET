namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>The interleaved scan: every MCU row decodes each component's blocks in turn, honouring the restart interval.</summary>
        private void DecodeInterleavedMcus(BitStream bits, int[] dcPred, byte[][] buffers, int[] bufWidths, int mcuCols, int mcuRows)
        {
            // One reusable coefficient block across the whole scan (see above).
            var mcuBlock = new int[64];
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
                        Array.Clear(dcPred, 0, dcPred.Length);
                        restartCounter = 0;
                    }

                    DecodeMcuComponentBlocks(bits, dcPred, buffers, bufWidths, mcuBlock, mcuRow, mcuCol);
                    restartCounter++;
                }
            }
        }

        /// <summary>A non-interleaved scan walks its single component's own block grid rather than the MCU grid.</summary>
        private void DecodeSingleComponentScan(BitStream bits, int[] dcPred, byte[][] buffers, int[] bufWidths)
        {
            var restartCounter = 0;
            // Non-interleaved scan (T.81 A.2.2): one 8x8 block per MCU, row-major over
            // the component's own block grid — the sampling factors play no role. A
            // single-component frame that still declares 2x2 sampling (common in
            // scanner output) otherwise gets scrambled by the interleaved MCU layout.
            var ciN = _scanComponentIndices[0];
            var compN = _components[ciN];
            var compWidth = (Width * compN.H + _maxH - 1) / _maxH;
            var compHeight = (Height * compN.V + _maxV - 1) / _maxV;
            var blockCols = (compWidth + 7) / 8;
            var blockRows = (compHeight + 7) / 8;
            var dcTableN = _dcTables[_scanDcTableIds[0]];
            var acTableN = _acTables[_scanAcTableIds[0]];
            var qtN = _quantTables[compN.QtId] ?? _quantTables[0];
            var bwBuf = bufWidths[ciN];

            // One reusable coefficient block: tens of thousands of per-block
            // allocations otherwise dominate the decode's allocation profile.
            var blockN = new int[64];
            for (var by = 0; by < blockRows; by++)
            {
                for (var bx = 0; bx < blockCols; bx++)
                {
                    if (_restartInterval > 0 && restartCounter == _restartInterval)
                    {
                        bits.AlignByte();
                        bits.SkipRestartMarker();
                        Array.Clear(dcPred, 0, dcPred.Length);
                        restartCounter = 0;
                    }

                    Array.Clear(blockN, 0, blockN.Length);
                    dcPred[ciN] = DecodeBlock(bits, blockN, dcPred[ciN], dcTableN!, acTableN!);
                    Dequantize(blockN, qtN);
                    IDCT(blockN);

                    var pxN = bx * 8;
                    var pyN = by * 8;
                    for (var y = 0; y < 8; y++)
                    {
                        var dst = (pyN + y) * bwBuf + pxN;
                        var src = y * 8;
                        for (var x = 0; x < 8; x++)
                            buffers[ciN][dst + x] = (byte)Clamp(blockN[src + x] + 128);
                    }
                    restartCounter++;
                }
            }
        }
    }
}

