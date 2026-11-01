using System;
using System.Collections.Generic;
namespace Aspose.Pdf.IO.Filters;

internal static partial class JpxDecoder
{
    private sealed partial class Decoder
    {
        /// <summary>Each tile decodes in raster order, its tile-parts concatenated; false means the codestream ran out.</summary>
        private bool DecodeTilesInOrder(List<int> tileOrder, Dictionary<int, List<(int tpsot, int dataStart, int dataEnd)>> byTile, int numTilesX)
        {
            foreach (var index in tileOrder)
            {
                _tpx0 = _xtosiz + (index % numTilesX) * _xtsiz;
                _tpy0 = _ytosiz + (index / numTilesX) * _ytsiz;
                _bandCoeffs.Clear();

                // Build the subband/resolution structure for this tile's components.
                for (int ci = 0; ci < _comps.Length; ci++)
                    BuildComponent(ci);

                // Tier-2: a single shared reader walks the tile's interleaved packet
                // stream, one packet per layer, resolution, component and precinct in
                // the order of the progression (PacketOrder).
                // Each quality layer adds coding passes to the same code-blocks, so a
                // block's compressed bytes accumulate across the layers it appears in.
                var parts = byTile[index];
                parts.Sort((a, b) => a.tpsot.CompareTo(b.tpsot));
                int totalLen = 0; foreach (var p in parts) totalLen += p.dataEnd - p.dataStart;
                var tileData = new byte[totalLen];
                int off = 0;
                foreach (var p in parts) { int len2 = p.dataEnd - p.dataStart; Array.Copy(_d, p.dataStart, tileData, off, len2); off += len2; }
                var pr = new PacketReader(tileData);
                foreach (var (l, r, ci, p) in PacketOrder())
                    ReadResolutionPacket(pr, _comps[ci].Resolutions[r], l, p);

                // Tier-1 + dequant + inverse DWT, then scatter the tile into the image.
                for (int ci = 0; ci < _comps.Length; ci++)
                {
                    ReconstructComponent(ci);
                    PlaceTile(_comps[ci]);
                }
            }
            return true;
        }
    }
}
