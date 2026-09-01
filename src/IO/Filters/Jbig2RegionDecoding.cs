using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    private sealed partial class DecodeContext
    {
        private void DecodeGenericRegion(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 18 > _data.Length) return;

            // Region segment info (17 bytes): W, H, X, Y, combOp
            var regionW = ReadInt32BE(_data, p);
            var regionH = ReadInt32BE(_data, p + 4);
            var regionX = ReadInt32BE(_data, p + 8);
            var regionY = ReadInt32BE(_data, p + 12);
            var combOp = _data[p + 16] & 0x07;
            p += 17;

            if (p >= _data.Length) return;
            var grFlags = _data[p++];
            var mmr = (grFlags & 0x01) != 0;
            var template = (grFlags >> 1) & 0x03;
            var tpgdon = (grFlags & 0x08) != 0; // typical prediction (T.88 §6.2.5.7)
            // useSkip (bit 4) ignored for our basic decoder

            (int, int)[] atPixels;
            if (!mmr)
            {
                var atCount = template == 0 ? 4 : 1;
                if (p + atCount * 2 > _data.Length) return;
                atPixels = new (int, int)[atCount];
                for (var i = 0; i < atCount; i++)
                {
                    atPixels[i] = ((sbyte)_data[p], (sbyte)_data[p + 1]);
                    p += 2;
                }
            }
            else
            {
                atPixels = [];
            }

            var dataAvail = hdr.DataStart + hdr.DataLength - p;
            if (dataAvail <= 0) return;

            Jbig2Bitmap regionBitmap;
            if (mmr)
            {
                regionBitmap = DecodeMmrG4(regionW, regionH, p, dataAvail);
            }
            else
            {
                var ad = new ArithmeticDecoder(_data, p);
                var grd = new GenericRegionDecoder(ad, template, atPixels, tpgdon);
                regionBitmap = grd.Decode(regionW, regionH);
            }

            // Composite onto page bitmap (or use as full page if no page-info yet).
            if (_pageBitmap is not null)
            {
                CompositeRegionOntoPage(regionBitmap, regionX, regionY, combOp);
            }
            else
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = regionBitmap.RowBytes;
                _pageBitmap = (byte[])regionBitmap.Data.Clone();
            }
        }

        private void DecodeHalftoneRegion(SegmentHeader hdr)
        {
            var p = hdr.DataStart;
            if (p + 18 > _data.Length) return;

            var regionW = ReadInt32BE(_data, p);
            var regionH = ReadInt32BE(_data, p + 4);
            var regionX = ReadInt32BE(_data, p + 8);
            var regionY = ReadInt32BE(_data, p + 12);
            var combOp = _data[p + 16] & 0x07;
            p += 17;

            var hFlags = _data[p++];
            var hmmr = (hFlags & 0x01) != 0;
            var hTemplate = (hFlags >> 1) & 0x03;
            var hEnableSkip = (hFlags & 0x08) != 0;
            var hCombOp = (hFlags >> 4) & 0x07;
            var hDefPixel = (hFlags >> 7) & 0x01;

            if (p + 20 > _data.Length) return;
            var hgw = ReadInt32BE(_data, p);
            var hgh = ReadInt32BE(_data, p + 4);
            var hgx = ReadInt32BE(_data, p + 8);
            var hgy = ReadInt32BE(_data, p + 12);
            var hrx = (_data[p + 16] << 8) | _data[p + 17];
            var hry = (_data[p + 18] << 8) | _data[p + 19];
            p += 20;

            // The referenced pattern dictionary (from a referred-to segment).
            Jbig2Bitmap[]? patterns = null;
            foreach (var refSeg in hdr.ReferredTo)
                if (_patternDicts.TryGetValue(refSeg, out var pd)) { patterns = pd; break; }
            if (patterns is null || patterns.Length == 0
                || regionW <= 0 || regionH <= 0 || hgw <= 0 || hgh <= 0) return;

            var hpw = patterns[0].Width;
            var hph = patterns[0].Height;
            var numPatterns = patterns.Length;
            var bpp = 0;
            while ((1 << bpp) < numPatterns) bpp++;

            var region = new Jbig2Bitmap(regionW, regionH, hDefPixel != 0);

            // Optional skip bitmap: grid cells whose pattern lands fully outside the region.
            Jbig2Bitmap? skip = null;
            if (hEnableSkip)
            {
                skip = new Jbig2Bitmap(hgw, hgh);
                for (var mg = 0; mg < hgh; mg++)
                    for (var ng = 0; ng < hgw; ng++)
                    {
                        var xs = (hgx + mg * hry + ng * hrx) >> 8;
                        var ys = (hgy + mg * hrx - ng * hry) >> 8;
                        if (xs + hpw <= 0 || xs >= regionW || ys + hph <= 0 || ys >= regionH)
                            skip.SetPixel(ng, mg, 1);
                    }
            }

            // Gray-scale image (§C.5): bpp bitplanes decoded MSB-first from one shared
            // generic-region context, then Gray-code combined into per-cell values.
            var gray = new int[hgh, hgw];
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

            // Render each grid cell's pattern at its rotated/scaled position.
            for (var mg = 0; mg < hgh; mg++)
                for (var ng = 0; ng < hgw; ng++)
                {
                    if (skip is not null && skip.GetPixel(ng, mg) != 0) continue;
                    var gv = gray[mg, ng];
                    if (gv < 0) gv = 0;
                    else if (gv >= numPatterns) gv = numPatterns - 1;
                    var x = (hgx + mg * hry + ng * hrx) >> 8;
                    var y = (hgy + mg * hrx - ng * hry) >> 8;
                    region.CompositeAt(patterns[gv], x, y, hCombOp);
                }

            if (_pageBitmap is not null)
            {
                CompositeRegionOntoPage(region, regionX, regionY, combOp);
            }
            else
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = region.RowBytes;
                _pageBitmap = (byte[])region.Data.Clone();
            }
        }

    }
}
