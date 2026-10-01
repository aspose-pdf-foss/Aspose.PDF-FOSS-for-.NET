using Aspose.Pdf.Core;
namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    private sealed partial class DecodeContext
    {
        /// <summary>The halftone region's grey-scale image (T.88 Annex C): its bitplanes decode most-significant first and Gray-decode into the grey values, through the arithmetic or the MMR coder.</summary>
        private void DecodeHalftoneGrayPlanes(SegmentHeader hdr, int p, int[,] gray, int hgw, int hgh, int bpp, bool hmmr, int hTemplate)
        {
            if (bpp > 0 && !hmmr)
            {
                var ad = new ArithmeticDecoder(_data, p);
                var grd = new GenericRegionDecoder(ad, hTemplate, System.Array.Empty<(int, int)>()); // nominal AT pixels
                int[,]? prev = null;
                for (var j = bpp - 1; j >= 0; j--)
                {
                    var plane = grd.Decode(hgw, hgh);
                    var cur = new int[hgh, hgw];
                    for (var mg = 0; mg < hgh; mg++)
                        for (var ng = 0; ng < hgw; ng++)
                        {
                            var b = plane.GetPixel(ng, mg);
                            if (prev is not null) b ^= prev[mg, ng]; // Gray decode
                            cur[mg, ng] = b;
                            gray[mg, ng] |= b << j;
                        }
                    prev = cur;
                }
            }
            else if (bpp > 0 && hmmr)
            {
                // MMR-coded gray-scale image (§C.5): the bpp bitplanes are consecutive
                // strict-T.6 (Group 4) bitmaps chained on one reader, decoded MSB-first,
                // then Gray-code combined into per-cell values.
                var segEnd = hdr.DataStart + hdr.DataLength;
                var avail = Math.Max(0, Math.Min(segEnd - p, _data.Length - p));
                var seg = new byte[avail];
                Array.Copy(_data, p, seg, 0, avail);
                var reader = new CcittFaxDecodeFilter.CcittBitReader(seg);
                var rowBytes = (hgw + 7) / 8;
                // Each of the GSBPP planes (MSB first) is an INDEPENDENT T.6 image: a fresh
                // all-white reference line, terminated by an EOFB and byte-aligned to the next
                // plane. Decode per plane, consume the EOFB + align, then Gray-code combine.
                int[,]? prev = null;
                for (var j = bpp - 1; j >= 0; j--)
                {
                    var bytes = CcittFaxDecodeFilter.DecodeGroup4Region(reader, hgw, hgh);
                    var cur = new int[hgh, hgw];
                    for (var mg = 0; mg < hgh; mg++)
                        for (var ng = 0; ng < hgw; ng++)
                        {
                            var idx = mg * rowBytes + (ng >> 3);
                            var b = idx < bytes.Length ? (bytes[idx] >> (7 - (ng & 7))) & 1 : 0;
                            if (prev is not null) b ^= prev[mg, ng]; // Gray decode
                            cur[mg, ng] = b;
                            gray[mg, ng] |= b << j;
                        }
                    prev = cur;
                    reader.ConsumeEofbAndByteAlign();
                }
            }
        }
    }
}
