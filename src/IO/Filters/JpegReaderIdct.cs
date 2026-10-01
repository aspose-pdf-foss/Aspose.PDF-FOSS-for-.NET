namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>The row pass of the inverse DCT, over the column pass's workspace.</summary>
        private static void IdctRows(int[] block, int[] ws)
        {
            for (var r = 0; r < 8; r++)
            {
                var off = r * 8;
                long z2 = ws[off + 2];
                long z3 = ws[off + 6];
                long z1 = (z2 + z3) * Fix_0_541196100;
                var tmp2 = z1 + z3 * -Fix_1_847759065;
                var tmp3 = z1 + z2 * Fix_0_765366865;

                z2 = ws[off];
                z3 = ws[off + 4];
                var tmp0 = (z2 + z3) << ConstBits;
                var tmp1 = (z2 - z3) << ConstBits;

                var tmp10 = tmp0 + tmp3;
                var tmp13 = tmp0 - tmp3;
                var tmp11 = tmp1 + tmp2;
                var tmp12 = tmp1 - tmp2;

                long t0 = ws[off + 7];
                long t1 = ws[off + 5];
                long t2 = ws[off + 3];
                long t3 = ws[off + 1];

                z1 = t0 + t3;
                z2 = t1 + t2;
                z3 = t0 + t2;
                var z4 = t1 + t3;
                var z5 = (z3 + z4) * Fix_1_175875602;

                t0 *= Fix_0_298631336;
                t1 *= Fix_2_053119869;
                t2 *= Fix_3_072711026;
                t3 *= Fix_1_501321110;
                z1 *= -Fix_0_899976223;
                z2 *= -Fix_2_562915447;
                z3 = z3 * -Fix_1_961570560 + z5;
                z4 = z4 * -Fix_0_390180644 + z5;

                t0 += z1 + z3;
                t1 += z2 + z4;
                t2 += z2 + z3;
                t3 += z1 + z4;

                block[off]     = Descale(tmp10 + t3, ConstBits + Pass1Bits + 3);
                block[off + 7] = Descale(tmp10 - t3, ConstBits + Pass1Bits + 3);
                block[off + 1] = Descale(tmp11 + t2, ConstBits + Pass1Bits + 3);
                block[off + 6] = Descale(tmp11 - t2, ConstBits + Pass1Bits + 3);
                block[off + 2] = Descale(tmp12 + t1, ConstBits + Pass1Bits + 3);
                block[off + 5] = Descale(tmp12 - t1, ConstBits + Pass1Bits + 3);
                block[off + 3] = Descale(tmp13 + t0, ConstBits + Pass1Bits + 3);
                block[off + 4] = Descale(tmp13 - t0, ConstBits + Pass1Bits + 3);
            }
        }
    }
}
