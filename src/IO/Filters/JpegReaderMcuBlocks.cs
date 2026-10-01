namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>Every component's blocks inside one MCU: each is decoded, inverse-transformed and written into that component's own buffer at the MCU's offset.</summary>
        private void DecodeMcuComponentBlocks(BitStream bits, int[] dcPred, byte[][] buffers, int[] bufWidths, int[] mcuBlock, int mcuRow, int mcuCol)
        {
            // Decode each component's blocks in this MCU
            for (var ci = 0; ci < _scanComponentIndices.Length; ci++)
            {
                var compIdx = _scanComponentIndices[ci];
                var comp = _components[compIdx];
                var dcTable = _dcTables[_scanDcTableIds[ci]];
                var acTable = _acTables[_scanAcTableIds[ci]];
                var qt = _quantTables[comp.QtId] ?? _quantTables[0];

                for (var bv = 0; bv < comp.V; bv++)
                {
                    for (var bh = 0; bh < comp.H; bh++)
                    {
                        Array.Clear(mcuBlock, 0, mcuBlock.Length);
                        var block = mcuBlock;
                        dcPred[compIdx] = DecodeBlock(bits, block, dcPred[compIdx], dcTable!, acTable!);
                        Dequantize(block, qt);
                        IDCT(block);

                        // Write block to component buffer
                        var px = (mcuCol * comp.H + bh) * 8;
                        var py = (mcuRow * comp.V + bv) * 8;
                        var bw = bufWidths[compIdx];
                        for (var y = 0; y < 8; y++)
                        {
                            var dst = (py + y) * bw + px;
                            var src = y * 8;
                            for (var x = 0; x < 8; x++)
                                buffers[compIdx][dst + x] = (byte)Clamp(block[src + x] + 128);
                        }
                    }
                }
            }
        }
    }
}
