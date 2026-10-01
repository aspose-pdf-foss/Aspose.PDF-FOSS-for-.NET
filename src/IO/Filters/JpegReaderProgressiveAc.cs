namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>The AC refinement pass of a progressive scan: each run of zero-history coefficients is skipped or corrected, and the end-of-band run is consumed.</summary>
        private void DecodeProgressiveAcRefinement(BitStream bits, HuffmanTable acTable, int[] coefs, int blockOff, int p1, int m1, ref int ki)
        {
            if (_eobrun == 0)
            {
                while (ki <= _se)
                {
                    var rs = DecodeHuffman(bits, acTable);
                    var r = (rs >> 4) & 0xF;
                    var s = rs & 0xF;
                    var newVal = 0;
                    if (s != 0)
                    {
                        // s is 1 in valid streams: a coefficient becoming nonzero
                        newVal = bits.ReadBit() != 0 ? p1 : m1;
                    }
                    else
                    {
                        if (r != 15)
                        {
                            _eobrun = 1 << r;
                            if (r > 0) _eobrun += bits.ReadBits(r);
                            break;
                        }
                        // r == 15: skip over 16 zero-history coefficients
                    }

                    // Advance over r zero-history positions, sending correction
                    // bits for every nonzero coefficient passed on the way.
                    while (ki <= _se)
                    {
                        var pos = blockOff + ZigZag[ki];
                        if (coefs[pos] != 0)
                        {
                            if (bits.ReadBit() != 0 && (coefs[pos] & p1) == 0)
                                coefs[pos] += coefs[pos] >= 0 ? p1 : m1;
                        }
                        else
                        {
                            if (r == 0) break;
                            r--;
                        }
                        ki++;
                    }

                    if (newVal != 0 && ki <= _se)
                        coefs[blockOff + ZigZag[ki]] = newVal;
                    ki++;
                }
            }
        }
    }
}
