namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>The row pass of the inverse DCT, over the column pass's workspace.</summary>
        /// <summary>The dequantised block to samples, level shift still to come: the full
        /// transform, or the reduced one at a smaller scale.</summary>
        private void InverseTransform(int[] block)
        {
            if (ScaleDenominator == 1) IDCT(block);
            else ReducedIdct(block, BlockSide);
        }

        /// <summary>The transformed block's samples (BlockSide a side, row-major from index 0),
        /// level-shifted and clamped, into the component buffer at (px, py).</summary>
        private void PlaceBlock(int[] block, byte[] buffer, int bufWidth, int px, int py)
        {
            var side = BlockSide;
            for (var y = 0; y < side; y++)
            {
                var dst = (py + y) * bufWidth + px;
                var src = y * side;
                for (var x = 0; x < side; x++)
                    buffer[dst + x] = (byte)Clamp(block[src + x] + 128);
            }
        }

        // The low-frequency n x n corner of the block to n x n samples: the 8-point basis
        // replaced by the n-point one, which keeps the block's mean and drops what n samples
        // cannot show. The constant 1/4 is the 8 x 8 transform's; the n-point basis carries
        // the rest of the scale, so the mean sample stays DC/8.
        private readonly double[] _reducedScratch = new double[64];

        private void ReducedIdct(int[] block, int n)
        {
            if (n == 4)
            {
                ReducedIdct4(block);
                return;
            }
            var basis = ReducedBasis(n);   // [x * n + u] = C(u) cos((2x + 1) u pi / 2n)
            var rows = _reducedScratch;
            for (var v = 0; v < n; v++)
                for (var x = 0; x < n; x++)
                {
                    double sum = 0;
                    for (var u = 0; u < n; u++) sum += basis[x * n + u] * block[v * 8 + u];
                    rows[v * n + x] = sum;
                }
            for (var y = 0; y < n; y++)
                for (var x = 0; x < n; x++)
                {
                    double sum = 0;
                    for (var v = 0; v < n; v++) sum += basis[y * n + v] * rows[v * n + x];
                    block[y * n + x] = RoundToEven(sum / 4);
                }
        }

        /// <summary>The 4-point case of <see cref="ReducedIdct"/> - a half-scale decode - written
        /// out: the same products summed in the same order, so the samples are the same.</summary>
        private void ReducedIdct4(int[] block)
        {
            var b = Basis4;
            var rows = _reducedScratch;
            for (var v = 0; v < 4; v++)
            {
                double c0 = block[v * 8], c1 = block[v * 8 + 1], c2 = block[v * 8 + 2], c3 = block[v * 8 + 3];
                for (var x = 0; x < 4; x++)
                {
                    double sum = 0;
                    sum += b[x * 4] * c0;
                    sum += b[x * 4 + 1] * c1;
                    sum += b[x * 4 + 2] * c2;
                    sum += b[x * 4 + 3] * c3;
                    rows[v * 4 + x] = sum;
                }
            }
            for (var y = 0; y < 4; y++)
            {
                double b0 = b[y * 4], b1 = b[y * 4 + 1], b2 = b[y * 4 + 2], b3 = b[y * 4 + 3];
                for (var x = 0; x < 4; x++)
                {
                    double sum = 0;
                    sum += b0 * rows[x];
                    sum += b1 * rows[4 + x];
                    sum += b2 * rows[8 + x];
                    sum += b3 * rows[12 + x];
                    block[y * 4 + x] = RoundToEven(sum / 4);
                }
            }
        }

        /// <summary>Math.Round's rounding - to the nearest integer, ties to even - without
        /// its call: the floor, one more when the fraction is past a half, the even neighbour
        /// on a tie.</summary>
        private static int RoundToEven(double value)
        {
            var floor = (int)Math.Floor(value);
            var fraction = value - floor;
            if (fraction > 0.5) return floor + 1;
            if (fraction < 0.5) return floor;
            return floor % 2 == 0 ? floor : floor + 1;
        }

        private static readonly double[] Basis4 = BuildReducedBasis(4);
        private static readonly double[] Basis2 = BuildReducedBasis(2);
        private static readonly double[] Basis1 = BuildReducedBasis(1);

        private static double[] ReducedBasis(int n) => n switch { 4 => Basis4, 2 => Basis2, _ => Basis1 };

        private static double[] BuildReducedBasis(int n)
        {
            var basis = new double[n * n];
            for (var x = 0; x < n; x++)
                for (var u = 0; u < n; u++)
                    basis[x * n + u] = (u == 0 ? Math.Sqrt(0.5) : 1) * Math.Cos((2 * x + 1) * u * Math.PI / (2 * n));
            return basis;
        }

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
