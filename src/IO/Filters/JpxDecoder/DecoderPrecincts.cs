using System;
using System.Collections.Generic;

namespace Aspose.Pdf.IO.Filters;

internal static partial class JpxDecoder
{
    private sealed partial class Decoder
    {
        // Precinct size exponents per resolution (ISO 15444-1 A.6.1): 15 (one precinct
        // covering the resolution) unless the COD states its own.
        private const int MaximalPrecinctExponent = 15;
        private const int ProgressionLrcp = 0;
        private const int ProgressionRlcp = 1;
        private int[] _ppx = Array.Empty<int>();
        private int[] _ppy = Array.Empty<int>();

        /// <summary>The COD's precinct sizes, one byte per resolution (width exponent in the
        /// low nibble, height in the high one), or the maximal precinct for every resolution.</summary>
        private void ReadPrecinctSizes(bool custom)
        {
            _ppx = new int[_numLevels + 1];
            _ppy = new int[_numLevels + 1];
            for (int r = 0; r <= _numLevels; r++)
            {
                int v = custom ? U8() : (MaximalPrecinctExponent << 4) | MaximalPrecinctExponent;
                _ppx[r] = v & 0xF;
                _ppy[r] = v >> 4;
            }
        }

        private static int PrecinctExponent(int[] sizes, int r) =>
            r < sizes.Length ? sizes[r] : MaximalPrecinctExponent;

        /// <summary>A precinct of resolution r spans half as many samples in each subband of
        /// that resolution as in the resolution itself (B.6); the lowest resolution is its band.</summary>
        private static int BandPrecinctExponent(int[] sizes, int r) =>
            PrecinctExponent(sizes, r) - (r > 0 ? 1 : 0);

        /// <summary>The resolution's precinct grid, anchored at the reference-grid origin, and
        /// each subband's code-blocks parted among its precincts (B.6, B.7): a packet carries
        /// one precinct, with tag trees of its own.</summary>
        private void BuildPrecincts(Resolution res, int r)
        {
            int ppx = PrecinctExponent(_ppx, r), ppy = PrecinctExponent(_ppy, r);
            int px0 = res.X0 >> ppx, py0 = res.Y0 >> ppy;
            res.PrecW = res.X1 > res.X0 ? CeilDiv(res.X1, 1 << ppx) - px0 : 0;
            res.PrecH = res.Y1 > res.Y0 ? CeilDiv(res.Y1, 1 << ppy) - py0 : 0;
            foreach (var band in res.Bands)
            {
                int cols = Math.Max(1, (1 << BandPrecinctExponent(_ppx, r)) / band.Cbw);
                int rows = Math.Max(1, (1 << BandPrecinctExponent(_ppy, r)) / band.Cbh);
                int firstCol = band.X0 / band.Cbw, firstRow = band.Y0 / band.Cbh;
                band.Precincts = new Precinct[res.PrecW * res.PrecH];
                for (int py = 0; py < res.PrecH; py++)
                    for (int px = 0; px < res.PrecW; px++)
                    {
                        var pc = new Precinct
                        {
                            C0 = Compat.Clamp((px0 + px) * cols - firstCol, 0, band.NumBlocksW),
                            C1 = Compat.Clamp((px0 + px + 1) * cols - firstCol, 0, band.NumBlocksW),
                            R0 = Compat.Clamp((py0 + py) * rows - firstRow, 0, band.NumBlocksH),
                            R1 = Compat.Clamp((py0 + py + 1) * rows - firstRow, 0, band.NumBlocksH),
                        };
                        pc.InclTree = new TagTree(pc.C1 - pc.C0, pc.R1 - pc.R0);
                        pc.ZbpTree = new TagTree(pc.C1 - pc.C0, pc.R1 - pc.R0);
                        band.Precincts[py * res.PrecW + px] = pc;
                    }
            }
        }

        private static IEnumerable<SubbandBlock> PrecinctBlocks(Subband band, Precinct pc)
        {
            for (int by = pc.R0; by < pc.R1; by++)
                for (int bx = pc.C0; bx < pc.C1; bx++)
                    yield return band.Blocks[by * band.NumBlocksW + bx];
        }

        /// <summary>The tile's packets in the order the progression writes them, as (layer,
        /// resolution, component, precinct). The components share one sampling and one precinct
        /// grid, so RPCL's position order is each resolution's precincts in raster order.</summary>
        private List<(int l, int r, int c, int p)> PacketOrder()
        {
            var order = new List<(int, int, int, int)>();
            int levels = _numLevels + 1, comps = _comps.Length;
            int Precincts(int r) => _comps[0].Resolutions[r].PrecW * _comps[0].Resolutions[r].PrecH;
            if (_progOrder == ProgressionLrcp)
            {
                for (int l = 0; l < _numLayers; l++)
                    for (int r = 0; r < levels; r++)
                        for (int c = 0; c < comps; c++)
                            for (int p = 0; p < Precincts(r); p++) order.Add((l, r, c, p));
            }
            else if (_progOrder == ProgressionRlcp)
            {
                for (int r = 0; r < levels; r++)
                    for (int l = 0; l < _numLayers; l++)
                        for (int c = 0; c < comps; c++)
                            for (int p = 0; p < Precincts(r); p++) order.Add((l, r, c, p));
            }
            else
            {
                for (int r = 0; r < levels; r++)
                    for (int p = 0; p < Precincts(r); p++)
                        for (int c = 0; c < comps; c++)
                            for (int l = 0; l < _numLayers; l++) order.Add((l, r, c, p));
            }
            return order;
        }
    }
}
