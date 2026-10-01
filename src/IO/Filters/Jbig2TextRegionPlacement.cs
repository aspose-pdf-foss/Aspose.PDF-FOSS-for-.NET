using Aspose.Pdf.Core;

namespace Aspose.Pdf.IO.Filters;

internal static partial class Jbig2Decoder
{
    private sealed partial class DecodeContext
    {
        /// <summary>Spec §6.4.5.1: place a symbol instance relative to (T_I, curS) with the
        /// region's REFCORNER offset, where T_I = STRIPT·SBSTRIPS + CURT. The strip coordinate
        /// STRIPT is decoded in units of SBSTRIPS rows, so it is scaled back up before this is
        /// called. Coordinates are in (s, t), which map to (x, y) when the region is not
        /// transposed and to (y, x) when it is.</summary>
        private static (int x, int y) PlaceSymbolInstance(int refCorner, bool transposed,
            int placeS, int placeT, int symW, int symH)
        {
            int x, y;
            if (!transposed)
            {
                // s → x, t → y
                switch (refCorner)
                {
                    case 0: x = placeS; y = placeT - symH + 1; break;               // BL
                    case 1: x = placeS; y = placeT; break;                          // TL
                    case 2: x = placeS - symW + 1; y = placeT - symH + 1; break;    // BR
                    default: x = placeS - symW + 1; y = placeT; break;              // TR
                }
            }
            else
            {
                // s → y, t → x
                switch (refCorner)
                {
                    case 0: x = placeT - symH + 1; y = placeS; break;
                    case 1: x = placeT; y = placeS; break;
                    case 2: x = placeT - symH + 1; y = placeS - symW + 1; break;
                    default: x = placeT; y = placeS - symW + 1; break;
                }
            }
            return (x, y);
        }

        /// <summary>A decoded region becomes the page when there is no page bitmap yet
        /// (the region's own size defines it), and composites onto it otherwise.</summary>
        private void StoreOrCompositeRegion(Jbig2Bitmap region, int regionW, int regionH,
            int regionX, int regionY, int regCombOp)
        {
            if (_pageBitmap is null)
            {
                _pageWidth = regionW;
                _pageHeight = regionH;
                _pageRowBytes = region.RowBytes;
                _pageBitmap = (byte[])region.Data.Clone();
            }
            else
            {
                CompositeRegionOntoPage(region, regionX, regionY, regCombOp);
            }
        }

        /// <summary>The symbol-ID code table (T.88 §7.4.3.1.7): 35 runcode lengths of
        /// 5 bits, a canonical runcode table over them, then one code length per symbol,
        /// then byte alignment. Null means the runcode stream ran out.</summary>
        private static HuffTable? ReadSymbolIdCodeTable(HuffBitReader reader, int symbolCount)
        {
            // Symbol ID code table (§7.4.3.1.7): 35 runcode lengths (5 bits each),
            // a canonical runcode table over them, then one code length per symbol
            // (runcode 0..31 = the length itself; 32 = repeat previous 3–6 times;
            // 33 = 3–10 zeroes; 34 = 11–138 zeroes), then byte alignment.
            var runLens = new HuffLine[35];
            for (var i = 0; i < 35; i++)
                runLens[i] = new HuffLine(reader.ReadBits(4), 0, i);
            var runTable = new HuffTable(runLens);

            var symLens = new int[symbolCount];
            var prevLen = 0;
            for (var i = 0; i < symbolCount;)
            {
                if (runTable.Decode(reader) is not { } code) return null;
                if (code < 32)
                {
                    symLens[i++] = code;
                    prevLen = code;
                }
                else if (code == 32)
                {
                    var rep = 3 + reader.ReadBits(2);
                    while (rep-- > 0 && i < symbolCount) symLens[i++] = prevLen;
                }
                else if (code == 33)
                {
                    var rep = 3 + reader.ReadBits(3);
                    while (rep-- > 0 && i < symbolCount) symLens[i++] = 0;
                }
                else // 34
                {
                    var rep = 11 + reader.ReadBits(7);
                    while (rep-- > 0 && i < symbolCount) symLens[i++] = 0;
                }
            }
            var idLines = new HuffLine[symbolCount];
            for (var i = 0; i < symbolCount; i++)
                idLines[i] = new HuffLine(symLens[i], 0, i);
            var tId = new HuffTable(idLines);
            reader.Align();
            return tId;
        }
    }
}
