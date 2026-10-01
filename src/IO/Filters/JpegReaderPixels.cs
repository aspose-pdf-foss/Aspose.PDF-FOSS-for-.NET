
namespace Aspose.Pdf.IO.Filters;

internal static partial class JpegDecoder
{
    private sealed partial class JpegReader
    {
        /// <summary>Expand a 2×-subsampled component buffer to full resolution with the
        /// TRIANGLE filter, the "fancy upsampling" every mainstream JPEG decoder applies
        /// by default. Returns null for ratios this filter does not cover, leaving the
        /// caller on sample replication.</summary>
        /// <remarks>
        /// Replicating each chroma sample (the obvious <c>x·H/maxH</c> index) is visibly
        /// wrong on detailed images: it lands the full chroma step on one pixel boundary
        /// instead of ramping it across two. Measured against the expected render of a
        /// 300 dpi scan, replication left ~1400 pixels past the comparator's tolerance,
        /// all of them in HIGH-DETAIL areas — mean error 4.05 levels where the image has
        /// gradient against 0.023 in flat areas, worst on the blue channel — while flat
        /// regions and the 8×8 block phase showed nothing. That is the chroma-edge
        /// signature, and interpolating removes it.
        ///
        /// The weights are libjpeg's: each output sample is 3 parts nearest input to 1
        /// part next-nearest, so the reconstructed samples sit at the right sub-pixel
        /// centres. Edge columns and rows have no outer neighbour and take the nearest
        /// sample unfiltered. h2v2 filters vertically into a column sum first (weights
        /// 3:1 again) and then horizontally, giving the 16ths the shifts below use, with
        /// libjpeg's asymmetric rounding constants (+8 / +7) preserved.
        /// </remarks>
        private static (byte[]? result, int dstWidth) FancyUpsample(byte[] src, int srcWidth, int hRatio, int vRatio)
        {
            int dstWidth = default;
            dstWidth = srcWidth * hRatio;
            var srcHeight = src.Length / Math.Max(1, srcWidth);
            if (srcWidth <= 0 || srcHeight <= 0) return (null, dstWidth);
            if (hRatio != 2 || (vRatio != 1 && vRatio != 2)) return (null, dstWidth);

            var dstHeight = srcHeight * vRatio;
            var dst = new byte[dstWidth * dstHeight];

            if (vRatio == 1)
            {
                // h2v1: horizontal triangle filter only.
                for (var y = 0; y < srcHeight; y++)
                {
                    var s = y * srcWidth;
                    var d = y * dstWidth;
                    dst[d] = src[s];
                    dst[d + 1] = (byte)((src[s] * 3 + src[s + Math.Min(1, srcWidth - 1)] + 2) >> 2);
                    for (var x = 1; x < srcWidth - 1; x++)
                    {
                        var cur = src[s + x] * 3;
                        dst[d + 2 * x] = (byte)((cur + src[s + x - 1] + 1) >> 2);
                        dst[d + 2 * x + 1] = (byte)((cur + src[s + x + 1] + 2) >> 2);
                    }
                    if (srcWidth > 1)
                    {
                        var last = srcWidth - 1;
                        dst[d + 2 * last] = (byte)((src[s + last] * 3 + src[s + last - 1] + 1) >> 2);
                        dst[d + 2 * last + 1] = src[s + last];
                    }
                }
                return (dst, dstWidth);
            }

            // h2v2: vertical triangle filter into a column sum, then horizontal.
            var colSum = new int[srcWidth];
            for (var dy = 0; dy < dstHeight; dy++)
            {
                var near = dy >> 1;                       // input row this output row sits in
                var far = (dy & 1) == 0 ? near - 1 : near + 1;
                if (far < 0) far = 0;
                if (far > srcHeight - 1) far = srcHeight - 1;
                for (var x = 0; x < srcWidth; x++)
                    colSum[x] = src[near * srcWidth + x] * 3 + src[far * srcWidth + x];

                var d = dy * dstWidth;
                dst[d] = (byte)((colSum[0] * 4 + 8) >> 4);
                if (srcWidth > 1)
                    dst[d + 1] = (byte)((colSum[0] * 3 + colSum[1] + 7) >> 4);
                for (var x = 1; x < srcWidth - 1; x++)
                {
                    var cur = colSum[x] * 3;
                    dst[d + 2 * x] = (byte)((cur + colSum[x - 1] + 8) >> 4);
                    dst[d + 2 * x + 1] = (byte)((cur + colSum[x + 1] + 7) >> 4);
                }
                if (srcWidth > 1)
                {
                    var last = srcWidth - 1;
                    dst[d + 2 * last] = (byte)((colSum[last] * 3 + colSum[last - 1] + 8) >> 4);
                    dst[d + 2 * last + 1] = (byte)((colSum[last] * 4 + 7) >> 4);
                }
            }
            return (dst, dstWidth);
        }

        /// <summary>Colour-convert the per-component sample buffers (MCU-aligned)
        /// into the packed output pixel array. Shared by the baseline and
        /// progressive paths.</summary>
        private void ConvertBuffersToPixels(byte[][] buffers, int[] bufWidths)
        {
            // Expand a 2×-subsampled component to full resolution FIRST, so the colour
            // conversion below reads one chroma sample per pixel. Doing it here rather
            // than by indexing (sx = x·H/maxH, which REPLICATES each sample) is what
            // makes the smooth triangle filter possible — see FancyUpsample. Marking the
            // component full-sampling afterwards leaves every reader below unchanged:
            // its sx/sy then reduce to x/y.
            for (var ci = 0; ci < Components && ci < _components.Length; ci++)
            {
                var c = _components[ci];
                if (c.H == _maxH && c.V == _maxV) continue;
                if (ReplicateChroma) continue;
                (var expanded, var newWidth) = FancyUpsample(buffers[ci], bufWidths[ci], _maxH / c.H, _maxV / c.V);
                if (expanded is null) continue;
                buffers[ci] = expanded;
                bufWidths[ci] = newWidth;
                _components[ci] = c with { H = _maxH, V = _maxV };
            }

            // Convert to output pixels
            if (Components == 1)
            {
                Pixels = new byte[Width * Height];
                for (var y = 0; y < Height; y++)
                    for (var x = 0; x < Width; x++)
                        Pixels[y * Width + x] = (byte)buffers[0][y * bufWidths[0] + x];
            }
            else if (Components == 2)
            {
                // A 2-channel JPEG has no standard colour transform — it carries a
                // 2-colorant ink space (e.g. a /DeviceN [C M] image). Emit the raw
                // samples interleaved; the caller maps them through the tint
                // transform.
                Pixels = new byte[Width * Height * 2];
                for (var y = 0; y < Height; y++)
                {
                    for (var x = 0; x < Width; x++)
                    {
                        var o = (y * Width + x) * 2;
                        for (var c = 0; c < 2; c++)
                        {
                            var sy = y * _components[c].V / _maxV;
                            var sx = x * _components[c].H / _maxH;
                            Pixels[o + c] = buffers[c][sy * bufWidths[c] + sx];
                        }
                    }
                }
            }
            else if (Components == 3)
            {
                ConvertYcbcrBuffersToPixels(buffers, bufWidths);
            }
            else if (Components == 4)
            {
                ConvertCmykBuffersToPixels(buffers, bufWidths);
            }
        }

        private static void Dequantize(int[] block, int[] qt)
        {
            for (var i = 0; i < 64; i++)
                block[i] *= qt[i];
        }

        /// <summary>
        /// The IJG "slow" integer IDCT, replicated bit-for-bit: fixed-point
        /// (CONST_BITS=13), columns first with a PASS1_BITS intermediate descale,
        /// then rows with the final descale. The staged rounding is the point —
        /// a float IDCT rounded once at the end drifts ±1 from the IJG
        /// output, which is enough to visibly alter a pale watermark's
        /// ink coverage.
        /// </summary>
        private static void IDCT(int[] block)
        {
            var ws = new int[64];

            // Pass 1: process columns, store scaled by 2^PASS1_BITS.
            for (var c = 0; c < 8; c++)
            {
                long z2 = block[16 + c];
                long z3 = block[48 + c];
                long z1 = (z2 + z3) * Fix_0_541196100;
                var tmp2 = z1 + z3 * -Fix_1_847759065;
                var tmp3 = z1 + z2 * Fix_0_765366865;

                z2 = block[c];
                z3 = block[32 + c];
                var tmp0 = (z2 + z3) << ConstBits;
                var tmp1 = (z2 - z3) << ConstBits;

                var tmp10 = tmp0 + tmp3;
                var tmp13 = tmp0 - tmp3;
                var tmp11 = tmp1 + tmp2;
                var tmp12 = tmp1 - tmp2;

                long t0 = block[56 + c];
                long t1 = block[40 + c];
                long t2 = block[24 + c];
                long t3 = block[8 + c];

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

                ws[c]      = Descale(tmp10 + t3, ConstBits - Pass1Bits);
                ws[56 + c] = Descale(tmp10 - t3, ConstBits - Pass1Bits);
                ws[8 + c]  = Descale(tmp11 + t2, ConstBits - Pass1Bits);
                ws[48 + c] = Descale(tmp11 - t2, ConstBits - Pass1Bits);
                ws[16 + c] = Descale(tmp12 + t1, ConstBits - Pass1Bits);
                ws[40 + c] = Descale(tmp12 - t1, ConstBits - Pass1Bits);
                ws[24 + c] = Descale(tmp13 + t0, ConstBits - Pass1Bits);
                ws[32 + c] = Descale(tmp13 - t0, ConstBits - Pass1Bits);
            }

            // Pass 2: process rows, final descale to sample scale (caller adds the
            // +128 level shift and clamps, mirroring the IJG range limiter).
            IdctRows(block, ws);
        }

    }
}
