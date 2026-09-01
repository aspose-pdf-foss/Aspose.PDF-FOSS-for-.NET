using System;
using System.Collections.Generic;

namespace Aspose.Pdf.IO.Filters;

internal static partial class JpxDecoder
{
    private sealed class Decoder
    {
        private readonly byte[] _d;
        private int _p;

        public int Width, Height, Components;
        public byte[] Pixels = Array.Empty<byte>();

        // SIZ
        private int _xsiz, _ysiz, _xosiz, _yosiz, _xtsiz, _ytsiz, _xtosiz, _ytosiz;
        private int _tpx0, _tpy0; // reference-grid origin of the tile currently being decoded
        private Component[] _comps = Array.Empty<Component>();

        // COD
        private int _progOrder, _numLayers, _mct, _numLevels, _xcb, _ycb, _cbStyle, _transform;
        private bool _useSop, _useEph;
        // QCD
        private int _quantStyle, _guardBits;
        private int[] _qExpn = Array.Empty<int>();
        private int[] _qMant = Array.Empty<int>();
        // QCC — per-component quantization overriding the main QCD (ISO 15444-1
        // A.6.5). Keyed by component index; parsed in both the main and
        // tile-part headers.
        private readonly Dictionary<int, (int style, int guard, int[] expn, int[] mant)> _qcc = new();
        // Component whose subband structure is currently being built — lets
        // AssignQuant pick that component's QCC table over the QCD default.
        private int _curComp;

        public Decoder(byte[] data) { _d = data; }

        private int U8() => _d[_p++];
        private int U16() { int v = (_d[_p] << 8) | _d[_p + 1]; _p += 2; return v; }
        private long U32() { long v = ((long)_d[_p] << 24) | ((long)_d[_p + 1] << 16) | ((uint)_d[_p + 2] << 8) | _d[_p + 3]; _p += 4; return v; }

        public bool Run()
        {
            if (!FindSoc()) return false;
            _p += 2; // SOC

            // Parse the main header (SIZ/COD/QCD) and collect each tile-part's bitstream
            // range. An image may be split into a grid of tiles, each introduced by an SOT
            // marker (carrying its index and byte length) and terminated by SOD.
            var tiles = new List<(int index, int tpsot, int dataStart, int dataEnd)>();
            while (_p + 2 <= _d.Length)
            {
                if (_d[_p] != 0xFF) { _p++; continue; }
                int markerPos = _p;
                int marker = _d[_p + 1];
                _p += 2;
                if (marker == 0xD9) break;                      // EOC
                if (marker == 0x4F || marker == 0x92) continue; // SOC/EPH (no length)
                if (marker == 0x90)                             // SOT — start of a tile-part
                {
                    if (_p + 10 > _d.Length) break;
                    U16();              // Lsot
                    int isot = U16();   // tile index
                    long psot = U32();  // tile-part length, measured from this SOT marker
                    int tpsot = U8(); U8(); // TPsot (tile-part index), TNsot
                    int tileEnd = psot > 0 ? (int)(markerPos + psot) : _d.Length;
                    if (tileEnd <= markerPos || tileEnd > _d.Length) tileEnd = _d.Length;
                    // Tile-part header markers (rare tile-specific COD/QCD) up to SOD.
                    int tileSod = -1;
                    while (_p + 2 <= _d.Length && _p < tileEnd)
                    {
                        if (_d[_p] != 0xFF) { _p++; continue; }
                        int m2 = _d[_p + 1];
                        _p += 2;
                        if (m2 == 0x93) { tileSod = _p; break; } // SOD — bitstream follows
                        if (_p + 2 > _d.Length) break;
                        int l2 = U16();
                        int e2 = _p + l2 - 2;
                        switch (m2)
                        {
                            case 0x52: ParseCod(); break;
                            case 0x5C: ParseQcd(e2); break;
                            case 0x5D: ParseQcc(e2); break;
                        }
                        _p = e2;
                    }
                    if (tileSod < 0) break;
                    tiles.Add((isot, tpsot, tileSod, tileEnd));
                    _p = tileEnd;
                    continue;
                }
                if (_p + 2 > _d.Length) break;
                int len = U16();
                int segEnd = _p + len - 2;
                switch (marker)
                {
                    case 0x51: ParseSiz(); break;
                    case 0x52: ParseCod(); break;
                    case 0x5C: ParseQcd(segEnd); break;
                    case 0x5D: ParseQcc(segEnd); break;
                    default: break; // COC/RGN/POC/TLM/PLT/COM/etc. — ignored in subset
                }
                _p = segEnd;
            }

            if (tiles.Count == 0 || _comps.Length == 0) return false;
            // Constrained subset: one precinct per resolution, any number of quality layers.
            // The LRCP (0), RLCP (1) and RPCL (2) progressions are accepted; the per-tile
            // packet loop below emits packets in the resolution / layer / component order each
            // progression dictates. (_progOrder is forced to -1 above when custom precincts
            // are present.)
            if (_progOrder < 0 || _progOrder > 2 || _numLayers < 1) return false;
            if (_comps.Length != 1 && _comps.Length != 3 && _comps.Length != 4) return false;
            foreach (var c in _comps) if (c.Dx != 1 || c.Dy != 1) return false;
            if (_xtsiz <= 0 || _ytsiz <= 0 || Width <= 0 || Height <= 0) return false;

            foreach (var comp in _comps) comp.Full = new int[Width * Height];
            int numTilesX = Math.Max(1, CeilDiv(_xsiz - _xtosiz, _xtsiz));

            // A tile may be carried by several tile-parts (e.g. one per quality layer); its
            // bitstream is the concatenation of those parts in tile-part (TPsot) order. Group
            // the collected parts by tile index and decode each tile exactly once.
            var byTile = new Dictionary<int, List<(int tpsot, int dataStart, int dataEnd)>>();
            var tileOrder = new List<int>();
            foreach (var (index, tpsot, dataStart, dataEnd) in tiles)
            {
                if (!byTile.TryGetValue(index, out var parts)) { parts = new(); byTile[index] = parts; tileOrder.Add(index); }
                parts.Add((tpsot, dataStart, dataEnd));
            }

            foreach (var index in tileOrder)
            {
                _tpx0 = _xtosiz + (index % numTilesX) * _xtsiz;
                _tpy0 = _ytosiz + (index / numTilesX) * _ytsiz;
                _bandCoeffs.Clear();

                // Build the subband/resolution structure for this tile's components.
                for (int ci = 0; ci < _comps.Length; ci++)
                    BuildComponent(ci);

                // Tier-2: a single shared reader walks the tile's interleaved packet
                // stream. With one precinct per resolution the packet order is governed
                // by the resolution / layer / component nesting of the progression
                // (one precinct collapses the position dimension):
                //   LRCP (0): layer, resolution, component
                //   RLCP (1): resolution, layer, component
                //   RPCL (2): resolution, component, layer
                // Each quality layer adds coding passes to the same code-blocks, so a
                // block's compressed bytes accumulate across the layers it appears in.
                var parts = byTile[index];
                parts.Sort((a, b) => a.tpsot.CompareTo(b.tpsot));
                int totalLen = 0; foreach (var p in parts) totalLen += p.dataEnd - p.dataStart;
                var tileData = new byte[totalLen];
                int off = 0;
                foreach (var p in parts) { int len2 = p.dataEnd - p.dataStart; Array.Copy(_d, p.dataStart, tileData, off, len2); off += len2; }
                var pr = new PacketReader(tileData);
                for (int o = 0; o < (_numLevels + 1) * _numLayers * _comps.Length; o++)
                {
                    int r, l, ci;
                    if (_progOrder == 0)        // LRCP
                    {
                        ci = o % _comps.Length;
                        r = (o / _comps.Length) % (_numLevels + 1);
                        l = o / (_comps.Length * (_numLevels + 1));
                    }
                    else if (_progOrder == 1)   // RLCP
                    {
                        ci = o % _comps.Length;
                        l = (o / _comps.Length) % _numLayers;
                        r = o / (_comps.Length * _numLayers);
                    }
                    else                        // RPCL (single precinct)
                    {
                        l = o % _numLayers;
                        ci = (o / _numLayers) % _comps.Length;
                        r = o / (_numLayers * _comps.Length);
                    }
                    ReadResolutionPacket(pr, _comps[ci].Resolutions[r], l);
                }

                // Tier-1 + dequant + inverse DWT, then scatter the tile into the image.
                for (int ci = 0; ci < _comps.Length; ci++)
                {
                    ReconstructComponent(ci);
                    PlaceTile(_comps[ci]);
                }
            }

            // Switch each component to its assembled full-image buffer for the colour
            // transform, level shift and final byte packing.
            foreach (var comp in _comps) { comp.Data = comp.Full; comp.W = Width; comp.H = Height; }

            InverseMct();
            Assemble();
            return true;
        }

        private void PlaceTile(Component comp)
        {
            for (int y = 0; y < comp.H; y++)
            {
                int fy = comp.Ty0 + y;
                if ((uint)fy >= (uint)Height) continue;
                for (int x = 0; x < comp.W; x++)
                {
                    int fx = comp.Tx0 + x;
                    if ((uint)fx >= (uint)Width) continue;
                    comp.Full[fy * Width + fx] = comp.Data[y * comp.W + x];
                }
            }
        }

        private bool FindSoc()
        {
            for (int i = 0; i + 1 < _d.Length; i++)
                if (_d[i] == 0xFF && _d[i + 1] == 0x4F) { _p = i; return true; }
            return false;
        }

        private void ParseSiz()
        {
            U16(); // Rsiz
            _xsiz = (int)U32(); _ysiz = (int)U32();
            _xosiz = (int)U32(); _yosiz = (int)U32();
            _xtsiz = (int)U32(); _ytsiz = (int)U32();
            _xtosiz = (int)U32(); _ytosiz = (int)U32();
            int csiz = U16();
            _comps = new Component[csiz];
            for (int i = 0; i < csiz; i++)
            {
                int ssiz = U8();
                var c = new Component
                {
                    Signed = (ssiz & 0x80) != 0,
                    Prec = (ssiz & 0x7F) + 1,
                    Dx = U8(),
                    Dy = U8(),
                };
                _comps[i] = c;
            }
            Width = _xsiz - _xosiz;
            Height = _ysiz - _yosiz;
            Components = csiz;
        }

        private void ParseCod()
        {
            int scod = U8();
            _useSop = (scod & 2) != 0; // SOP marker segments precede each packet
            _useEph = (scod & 4) != 0; // EPH marker terminates each packet header
            _progOrder = U8();
            _numLayers = U16();
            _mct = U8();
            _numLevels = U8();
            _xcb = U8() + 2;
            _ycb = U8() + 2;
            _cbStyle = U8();
            _transform = U8(); // 1 = 5/3 reversible, 0 = 9/7 irreversible
            if ((scod & 1) != 0)
            {
                // Custom precincts present — not in subset; consume but flag via numLayers check later.
                // (default-precinct subset only)
                _progOrder = -1; // force rejection
            }
        }

        private void ParseQcd(int segEnd)
        {
            int sqcd = U8();
            _quantStyle = sqcd & 0x1F;
            _guardBits = sqcd >> 5;
            var (expn, mant) = ParseQuantValues(_quantStyle, segEnd);
            _qExpn = expn;
            _qMant = mant;
        }

        /// <summary>QCC (ISO 15444-1 A.6.5): per-component quantization that
        /// overrides the main QCD for that component. Ignoring it mis-scales
        /// every coefficient by the mantissa ratio (a real-world encoder pairs
        /// a mantissa-free QCD with mantissa-bearing per-component QCCs).</summary>
        private void ParseQcc(int segEnd)
        {
            int comp = _comps.Length < 257 ? U8() : U16();
            int sqcc = U8();
            int style = sqcc & 0x1F;
            int guard = sqcc >> 5;
            var (expn, mant) = ParseQuantValues(style, segEnd);
            _qcc[comp] = (style, guard, expn, mant);
        }

        private (int[] expn, int[] mant) ParseQuantValues(int style, int segEnd)
        {
            var expn = new List<int>();
            var mant = new List<int>();
            if (style == 0) // no quantization (reversible)
            {
                while (_p < segEnd) { int v = U8(); expn.Add(v >> 3); mant.Add(0); }
            }
            else // scalar derived (1) or expounded (2)
            {
                while (_p + 2 <= segEnd)
                {
                    int v = U16();
                    expn.Add(v >> 11);
                    mant.Add(v & 0x7FF);
                    if (style == 1) break; // derived: single value
                }
            }
            return (expn.ToArray(), mant.ToArray());
        }

        // ── Per-component decode ────────────────────────────────────

        private void BuildComponent(int ci)
        {
            _curComp = ci;
            var comp = _comps[ci];
            int tcx0 = Math.Max(_tpx0, _xosiz);
            int tcy0 = Math.Max(_tpy0, _yosiz);
            int tcx1 = Math.Min(_tpx0 + _xtsiz, _xsiz);
            int tcy1 = Math.Min(_tpy0 + _ytsiz, _ysiz);
            comp.W = Math.Max(0, tcx1 - tcx0);
            comp.H = Math.Max(0, tcy1 - tcy0);
            comp.Tx0 = tcx0 - _xosiz;
            comp.Ty0 = tcy0 - _yosiz;
            comp.Data = new int[comp.W * comp.H];
            comp.Resolutions = BuildResolutions(tcx0, tcy0, tcx1, tcy1);
        }

        private void ReconstructComponent(int ci)
        {
            var comp = _comps[ci];
            var resolutions = comp.Resolutions;

            // Tier-1 + dequant per code-block, scatter into subband coefficient planes.
            foreach (var res in resolutions)
                foreach (var band in res.Bands)
                    DecodeBandBlocks(comp, band);

            // Inverse DWT to reconstruct tile-component samples.
            InverseDwt(comp, resolutions);
        }

        // Inverse multiple-component transform + DC level shift, producing the final
        // unsigned sample values per component. For mct=1 the three components carry a
        // reversible (RCT) or irreversible (ICT) colour transform that must be undone;
        // otherwise each component is independent. Level shift is applied here so the
        // colour transform operates on the signed (pre-shift) samples.
        private void InverseMct()
        {
            // The multiple-component transform always acts on the first three components
            // (the RGB↔YCC channels); a fourth CMYK component, when present, passes through.
            bool colourTransform = _mct == 1 && _comps.Length >= 3
                && _comps[0].W == _comps[1].W && _comps[0].W == _comps[2].W
                && _comps[0].H == _comps[1].H && _comps[0].H == _comps[2].H;

            if (colourTransform)
            {
                var c0 = _comps[0]; var c1 = _comps[1]; var c2 = _comps[2];
                int n = c0.Data.Length;
                for (int i = 0; i < n; i++)
                {
                    int y = c0.Data[i], u = c1.Data[i], v = c2.Data[i];
                    int r, g, b;
                    if (_transform == 1)
                    {
                        // Reversible colour transform (RCT) inverse.
                        g = y - ((u + v) >> 2);
                        r = v + g;
                        b = u + g;
                    }
                    else
                    {
                        // Irreversible colour transform (ICT / YCbCr) inverse.
                        r = (int)Math.Round(y + 1.402 * v);
                        g = (int)Math.Round(y - 0.34413 * u - 0.71414 * v);
                        b = (int)Math.Round(y + 1.772 * u);
                    }
                    c0.Data[i] = r; c1.Data[i] = g; c2.Data[i] = b;
                }
            }

            // DC level shift + clamp per (now colour-correct) component.
            foreach (var comp in _comps)
            {
                int shift = comp.Signed ? 0 : (1 << (comp.Prec - 1));
                int maxv = (1 << comp.Prec) - 1;
                for (int i = 0; i < comp.Data.Length; i++)
                {
                    int val = comp.Data[i] + shift;
                    comp.Data[i] = val < 0 ? 0 : (val > maxv ? maxv : val);
                }
            }
        }

        private Resolution[] BuildResolutions(int tcx0, int tcy0, int tcx1, int tcy1)
        {
            var res = new Resolution[_numLevels + 1];
            for (int r = 0; r <= _numLevels; r++)
            {
                int nb = _numLevels - r; // number of further decomposition steps
                var R = new Resolution
                {
                    X0 = CeilDiv(tcx0, 1 << nb),
                    Y0 = CeilDiv(tcy0, 1 << nb),
                    X1 = CeilDiv(tcx1, 1 << nb),
                    Y1 = CeilDiv(tcy1, 1 << nb),
                };
                if (r == 0)
                    R.Bands = new[] { MakeBand(0, R.X0, R.Y0, R.X1, R.Y1, r) };
                else
                {
                    // HL, LH, HH bands of this resolution come from level (nb+1).
                    int lev = nb + 1;
                    int ox0 = CeilDiv(tcx0 - (1 << (lev - 1)), 1 << lev);
                    int oy0 = CeilDiv(tcy0 - (1 << (lev - 1)), 1 << lev);
                    int ox1 = CeilDiv(tcx1 - (1 << (lev - 1)), 1 << lev);
                    int oy1 = CeilDiv(tcy1 - (1 << (lev - 1)), 1 << lev);
                    int llx0 = CeilDiv(tcx0, 1 << lev), lly0 = CeilDiv(tcy0, 1 << lev);
                    int llx1 = CeilDiv(tcx1, 1 << lev), lly1 = CeilDiv(tcy1, 1 << lev);
                    R.Bands = new[]
                    {
                        MakeBand(1, ox0, lly0, ox1, lly1, r),   // HL
                        MakeBand(2, llx0, oy0, llx1, oy1, r),   // LH
                        MakeBand(3, ox0, oy0, ox1, oy1, r),     // HH
                    };
                }
                res[r] = R;
            }
            return res;
        }

        private Subband MakeBand(int orient, int x0, int y0, int x1, int y1, int r)
        {
            var b = new Subband { Orient = orient, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 };
            // Code-block nominal dims (precinct = full subband, so cb dims are the COD values,
            // halved at resolution > 0 only when precinct sizes shrink — not in this subset).
            b.Cbw = 1 << Math.Min(_xcb, 15);
            b.Cbh = 1 << Math.Min(_ycb, 15);
            int w = x1 - x0, h = y1 - y0;
            b.Gain = orient == 0 ? 0 : (orient == 3 ? 2 : 1);
            // Subband index for quantization: LL of last level is 0; then per resolution
            // (HL,LH,HH). bandIndex = (numLevels - r==numLevels?0) ...
            AssignQuant(b, r);

            if (w <= 0 || h <= 0) { b.Blocks = Array.Empty<SubbandBlock>(); b.InclTree = new TagTree(1, 1); b.ZbpTree = new TagTree(1, 1); return b; }

            // Code-block grid aligned to multiples of cb dims (anchored at subband origin
            // rounded down to the code-block grid).
            int bx0 = (x0 / b.Cbw) * b.Cbw;
            int by0 = (y0 / b.Cbh) * b.Cbh;
            int nbw = (CeilDiv(x1, b.Cbw) - x0 / b.Cbw);
            int nbh = (CeilDiv(y1, b.Cbh) - y0 / b.Cbh);
            b.NumBlocksW = Math.Max(0, nbw);
            b.NumBlocksH = Math.Max(0, nbh);
            var blocks = new List<SubbandBlock>();
            for (int by = 0; by < b.NumBlocksH; by++)
                for (int bx = 0; bx < b.NumBlocksW; bx++)
                {
                    int cbx0 = Math.Max(x0, bx0 + bx * b.Cbw);
                    int cby0 = Math.Max(y0, by0 + by * b.Cbh);
                    int cbx1 = Math.Min(x1, bx0 + (bx + 1) * b.Cbw);
                    int cby1 = Math.Min(y1, by0 + (by + 1) * b.Cbh);
                    blocks.Add(new SubbandBlock { X0 = cbx0, Y0 = cby0, X1 = cbx1, Y1 = cby1 });
                }
            b.Blocks = blocks.ToArray();
            b.InclTree = new TagTree(b.NumBlocksW, b.NumBlocksH);
            b.ZbpTree = new TagTree(b.NumBlocksW, b.NumBlocksH);
            return b;
        }

        private void AssignQuant(Subband b, int r)
        {
            // Band ordering for QCD values: index 0 = LL (numLevels), then for each
            // resolution level from coarse to fine: HL, LH, HH.
            int idx;
            if (b.Orient == 0) idx = 0;
            else
            {
                int level = _numLevels - r; // 0..numLevels-1
                idx = 1 + (_numLevels - 1 - level) * 3 + (b.Orient - 1);
            }
            // A component with its own QCC uses that table; others fall back to QCD.
            var (style, guard, expn, mant) = _qcc.TryGetValue(_curComp, out var qc)
                ? qc
                : (_quantStyle, _guardBits, _qExpn, _qMant);
            b.Guard = guard;
            b.Prec = _curComp < _comps.Length ? _comps[_curComp].Prec : 8;
            if (style == 1) // derived — scale from single value
            {
                int baseExpn = expn.Length > 0 ? expn[0] : 0;
                int baseMant = mant.Length > 0 ? mant[0] : 0;
                int nb = _numLevels - r;
                b.Expn = Math.Max(0, baseExpn - (_numLevels - nb));
                b.Mant = baseMant;
            }
            else
            {
                b.Expn = idx < expn.Length ? expn[idx] : (expn.Length > 0 ? expn[^1] : 0);
                b.Mant = idx < mant.Length ? mant[idx] : (mant.Length > 0 ? mant[^1] : 0);
            }
        }

        // ── Tier-2 packet reading (one quality layer / single precinct) ──

        private void ReadResolutionPacket(PacketReader pr, Resolution res, int layer)
        {
            // The packet header bits follow on immediately from the previous packet (a
            // non-empty packet's body left the stream byte-aligned). An empty packet is a
            // lone 0 flag bit with no body and no re-alignment.
            if (pr.AtEnd) return;
            // A resilience-marker stream (Scod bits 1/2) wraps every packet: an SOP
            // segment (FF91 + Lsop + Nsop, 6 bytes) before the header and an EPH
            // marker (FF92) terminating the header — both sit outside the header
            // bit-stream, so they must be skipped at the byte level or every
            // subsequent header bit misparses into noise.
            if (_useSop) pr.SkipMarker(0x91, 6);
            bool nonEmpty = pr.ReadBit() == 1;
            if (!nonEmpty)
            {
                // With resilience markers the empty header is still padded to a
                // byte and terminated by EPH before the next packet begins.
                if (_useSop || _useEph)
                {
                    pr.Align();
                    if (_useEph) pr.SkipMarker(0x92, 2);
                }
                return;
            }

            foreach (var band in res.Bands)
            {
                for (int by = 0; by < band.NumBlocksH; by++)
                    for (int bx = 0; bx < band.NumBlocksW; bx++)
                    {
                        var blk = band.Blocks[by * band.NumBlocksW + bx];
                        bool included;
                        if (!blk.Included)
                            // First inclusion: the tag-tree value (the layer at which the
                            // block first appears) must be below this layer's threshold.
                            included = band.InclTree.Decode(pr, bx, by, layer + 1);
                        else
                            included = pr.ReadBit() == 1;

                        if (!included) continue;

                        if (!blk.Included)
                        {
                            // zero bit-planes via tag-tree (unbounded threshold)
                            int zbp = 0;
                            while (zbp < 64 && !band.ZbpTree.Decode(pr, bx, by, zbp + 1)) zbp++;
                            blk.ZeroBitplanes = zbp;
                            blk.Included = true;
                            blk.Lblock = 3;
                        }

                        int passes = ReadNumPasses(pr);
                        blk.NumPasses += passes;

                        // Lblock increments (one bit each, terminated by 0).
                        int incr = 0;
                        while (incr < 32 && pr.ReadBit() == 1) incr++;
                        blk.Lblock += incr;

                        int nbits = blk.Lblock + FloorLog2(passes);
                        int length = pr.ReadBits(nbits);
                        blk.SegmentLengths.Add(length);
                    }
            }

            pr.Align();
            if (_useEph) pr.SkipMarker(0x92, 2);

            // Packet body: code-block compressed bytes in the same scan order. A block
            // may receive bytes in several layers, so each layer's contribution is
            // appended to the bytes already collected for that block.
            foreach (var band in res.Bands)
                for (int i = 0; i < band.Blocks.Length; i++)
                {
                    var blk = band.Blocks[i];
                    if (blk.SegmentLengths.Count == 0) continue;
                    int total = 0; foreach (var l in blk.SegmentLengths) total += l;
                    var add = pr.ReadBytes(total);
                    if (blk.CompressedData.Length == 0) blk.CompressedData = add;
                    else
                    {
                        var merged = new byte[blk.CompressedData.Length + add.Length];
                        Array.Copy(blk.CompressedData, merged, blk.CompressedData.Length);
                        Array.Copy(add, 0, merged, blk.CompressedData.Length, add.Length);
                        blk.CompressedData = merged;
                    }
                    blk.SegmentLengths.Clear();
                }
        }

        private int ReadNumPasses(PacketReader pr)
        {
            // ISO 15444-1 Table B.4
            if (pr.ReadBit() == 0) return 1;
            if (pr.ReadBit() == 0) return 2;
            int v = pr.ReadBits(2);
            if (v != 3) return 3 + v;            // 3..5
            v = pr.ReadBits(5);
            if (v != 31) return 6 + v;           // 6..36
            v = pr.ReadBits(7);
            return 37 + v;                        // 37..164
        }

        // ── Tier-1 EBCOT decode of all blocks in a subband ──────────

        private void DecodeBandBlocks(Component comp, Subband band)
        {
            int bw = band.X1 - band.X0, bh = band.Y1 - band.Y0;
            if (bw <= 0 || bh <= 0) return;
            var bandCoeffs = new int[bw * bh];

            foreach (var blk in band.Blocks)
            {
                if (!blk.Included || blk.CompressedData.Length == 0 || blk.NumPasses == 0) continue;
                int cw = blk.X1 - blk.X0, ch = blk.Y1 - blk.Y0;
                if (cw <= 0 || ch <= 0) continue;

                var t1 = new Tier1(cw, ch, band.Orient, _cbStyle);
                t1.Decode(blk.CompressedData, blk.NumPasses, blk.ZeroBitplanes, GuardMaxBitplanes(band));

                // Scatter into band coefficient plane.
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        int sx = (blk.X0 - band.X0) + x;
                        int sy = (blk.Y0 - band.Y0) + y;
                        bandCoeffs[sy * bw + sx] = t1.Coeff[y * cw + x];
                    }
            }

            Dequantize(band, bandCoeffs, bw, bh);
        }

        private int GuardMaxBitplanes(Subband band)
        {
            // Mb = guard + expn - 1 (guard from the band's governing QCD/QCC)
            return band.Guard + band.Expn - 1;
        }

        private void Dequantize(Subband band, int[] coeffs, int bw, int bh)
        {
            // Store reconstructed coefficients into a band-keyed buffer attached to the
            // resolution via Coeffs on the band for the IDWT to consume.
            var outp = new int[bw * bh];
            if (_transform == 1)
            {
                // Reversible 5/3: coefficients are integers, no scaling.
                Array.Copy(coeffs, outp, coeffs.Length);
            }
            else
            {
                // Irreversible 9/7: dequantize to fixed reconstruction.
                // Reconstruction: value = q * delta, delta = (1+mant/2048) * 2^(Rb - expn),
                // Rb = prec + gain. Tier-1 magnitude is in units of 2^(Mb - numbps); but we
                // decoded full precision, so q already integer at LSB. Use delta directly.
                int Rb = band.Prec + band.Gain;
                // Δ_b = 2^(R_b − ε_b) · (1 + μ_b/2^11) exactly as ISO 15444-1 E.1.1 —
                // the inverse lifting below already carries the K normalisation per
                // level (low ×K, high ÷K), so no extra filter-bank gain belongs here.
                // (A previous K² factor double-compensated that scaling: every
                // irreversible image decoded with ~1.5× contrast about mid-gray, which
                // pushed light scan washes to pure white — measured against both the
                // OpenJPEG and Pillow references on gradient calibration images.)
                double delta = (1.0 + band.Mant / 2048.0) * Math.Pow(2.0, Rb - band.Expn);
                for (int i = 0; i < coeffs.Length; i++)
                {
                    int q = coeffs[i];
                    if (q == 0) { outp[i] = 0; continue; }
                    double v = q * delta;
                    outp[i] = (int)Math.Round(v);
                }
            }
            _bandCoeffs[band] = (outp, bw, bh);
        }

        private readonly Dictionary<Subband, (int[] data, int w, int h)> _bandCoeffs = new();

        // ── Inverse DWT ─────────────────────────────────────────────

        private void InverseDwt(Component comp, Resolution[] res)
        {
            // Start from the LL of resolution 0, iteratively combine with HL/LH/HH of
            // each higher resolution.
            var llBand = res[0].Bands[0];
            var (llData, llw, llh) = _bandCoeffs.TryGetValue(llBand, out var v0) ? v0 : (new int[0], 0, 0);
            int[] cur = llData; int curW = llw, curH = llh;

            for (int r = 1; r <= _numLevels; r++)
            {
                var R = res[r];
                var hl = Band(R, 1); var lh = Band(R, 2); var hh = Band(R, 3);
                int nw = R.X1 - R.X0, nh = R.Y1 - R.Y0;
                var recon = new int[nw * nh];

                // Interleave subbands into the resolution grid.
                Place(recon, nw, nh, cur, curW, curH, 0, 0, 2);                 // LL -> even,even
                Place(recon, nw, nh, Get(hl), W2(hl), H2(hl), 1, 0, 2);         // HL -> odd cols
                Place(recon, nw, nh, Get(lh), W2(lh), H2(lh), 0, 1, 2);         // LH -> odd rows
                Place(recon, nw, nh, Get(hh), W2(hh), H2(hh), 1, 1, 2);         // HH -> odd,odd

                if (_transform == 1) Idwt53(recon, nw, nh, R);
                else Idwt97(recon, nw, nh, R);
                cur = recon; curW = nw; curH = nh;
            }

            // cur is the full tile-component (may differ by rounding from comp dims).
            for (int y = 0; y < comp.H && y < curH; y++)
                for (int x = 0; x < comp.W && x < curW; x++)
                    comp.Data[y * comp.W + x] = cur[y * curW + x];
        }

        private Subband Band(Resolution r, int orient)
        {
            foreach (var b in r.Bands) if (b.Orient == orient) return b;
            return r.Bands[0];
        }
        private int[] Get(Subband b) => _bandCoeffs.TryGetValue(b, out var v) ? v.data : Array.Empty<int>();
        private int W2(Subband b) => _bandCoeffs.TryGetValue(b, out var v) ? v.w : 0;
        private int H2(Subband b) => _bandCoeffs.TryGetValue(b, out var v) ? v.h : 0;

        private static void Place(int[] dst, int dw, int dh, int[] src, int sw, int sh, int ox, int oy, int step)
        {
            if (src.Length == 0) return;
            for (int y = 0; y < sh; y++)
            {
                int dy = y * step + oy;
                if (dy >= dh) break;
                for (int x = 0; x < sw; x++)
                {
                    int dx = x * step + ox;
                    if (dx >= dw) break;
                    dst[dy * dw + dx] = src[y * sw + x];
                }
            }
        }

        // 5/3 reversible inverse transform (one level), in-place on interleaved grid.
        private void Idwt53(int[] a, int w, int h, Resolution R)
        {
            var tmp = new int[Math.Max(w, h)];
            bool evenX = (R.X0 & 1) == 0;
            bool evenY = (R.Y0 & 1) == 0;
            for (int y = 0; y < h; y++) { Row53(a, y * w, 1, w, tmp, evenX); }
            for (int x = 0; x < w; x++) { Row53(a, x, w, h, tmp, evenY); }
        }

        private void Row53(int[] a, int off, int stride, int n, int[] tmp, bool even)
        {
            if (n == 1) return;
            for (int i = 0; i < n; i++) tmp[i] = a[off + i * stride];
            // de-interleave: even indices = low, odd = high (when even start)
            // inverse lifting (5/3): even[i] -= (odd[i-1]+odd[i]+2)>>2 ; odd[i] += (even[i]+even[i+1])>>1
            int[] s = new int[n];
            for (int i = 0; i < n; i++) s[i] = tmp[i];
            for (int i = (even ? 0 : 1); i < n; i += 2) // even samples
                s[i] -= (Get53(s, i - 1, n) + Get53(s, i + 1, n) + 2) >> 2;
            for (int i = (even ? 1 : 0); i < n; i += 2) // odd samples
                s[i] += (Get53(s, i - 1, n) + Get53(s, i + 1, n)) >> 1;
            for (int i = 0; i < n; i++) a[off + i * stride] = s[i];
        }
        private static int Get53(int[] s, int i, int n)
        {
            if (i < 0) i = -i;
            if (i >= n) i = 2 * n - 2 - i;
            if (i < 0) i = 0; if (i >= n) i = n - 1;
            return s[i];
        }

        // 9/7 irreversible inverse transform (one level).
        private const double K = 1.230174104914001;
        private const double A = -1.586134342059924, B = -0.052980118572961, G = 0.882911075530934, D = 0.443506852043971;
        private void Idwt97(int[] a, int w, int h, Resolution R)
        {
            var col = new double[h];
            var row = new double[w];
            bool evenX = (R.X0 & 1) == 0, evenY = (R.Y0 & 1) == 0;
            for (int y = 0; y < h; y++) Row97(a, y * w, 1, w, row, evenX);
            for (int x = 0; x < w; x++) Row97(a, x, w, h, col, evenY);
        }

        private void Row97(int[] a, int off, int stride, int n, double[] t, bool even)
        {
            if (n == 1) return;
            for (int i = 0; i < n; i++) t[i] = a[off + i * stride];
            // Undo scaling: low-pass (even positions) ×K, high-pass (odd) ÷K.
            for (int i = 0; i < n; i++) t[i] = (i % 2 == (even ? 0 : 1)) ? t[i] * K : t[i] / K;
            // Inverse lifting (reverse of forward predict/update), even=low, odd=high.
            Lift97(t, n, even ? 0 : 1, -D); // update-2^-1 on low
            Lift97(t, n, even ? 1 : 0, -G); // predict-2^-1 on high
            Lift97(t, n, even ? 0 : 1, -B); // update-1^-1 on low
            Lift97(t, n, even ? 1 : 0, -A); // predict-1^-1 on high
            for (int i = 0; i < n; i++) a[off + i * stride] = (int)Math.Round(t[i]);
        }
        private static void Lift97(double[] s, int n, int start, double coef)
        {
            for (int i = start; i < n; i += 2)
            {
                double l = i - 1 >= 0 ? s[i - 1] : s[1];
                double r = i + 1 < n ? s[i + 1] : s[n - 2 >= 0 ? n - 2 : 0];
                s[i] += coef * (l + r);
            }
        }

        // ── Output assembly ─────────────────────────────────────────

        private void Assemble()
        {
            Pixels = new byte[Width * Height * Components];
            for (int ci = 0; ci < Components; ci++)
            {
                var c = _comps[ci];
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                    {
                        int v = (y < c.H && x < c.W) ? c.Data[y * c.W + x] : 0;
                        if (c.Prec != 8) v = c.Prec > 8 ? (v >> (c.Prec - 8)) : (v << (8 - c.Prec));
                        Pixels[(y * Width + x) * Components + ci] = (byte)(v < 0 ? 0 : v > 255 ? 255 : v);
                    }
            }

            // A four-component codestream is CMYK; collapse it to RGB so every caller
            // sees the same three-component layout it gets for RGB imagery. The inks go
            // through the same ICC-style conversion as CMYK fills (CmykToRgbLut) so a
            // photo and the flat tint beside it agree; a memo keeps the per-pixel cost
            // down on the flat regions such imagery is full of.
            if (Components == 4)
            {
                var rgb = new byte[Width * Height * 3];
                var memo = new Dictionary<int, (byte r, byte g, byte b)>();
                for (int p = 0, q = 0; p < Pixels.Length; p += 4, q += 3)
                {
                    int c = Pixels[p], m = Pixels[p + 1], yel = Pixels[p + 2], k = Pixels[p + 3];
                    var key = (c << 24) | (m << 16) | (yel << 8) | k;
                    if (!memo.TryGetValue(key, out var col))
                    {
                        col = Aspose.Pdf.Devices.CmykToRgbLut.Convert(c / 255.0, m / 255.0, yel / 255.0, k / 255.0);
                        memo[key] = col;
                    }
                    rgb[q] = col.r;
                    rgb[q + 1] = col.g;
                    rgb[q + 2] = col.b;
                }
                Pixels = rgb;
                Components = 3;
            }
        }

        private static int CeilDiv(int a, int b) => (a + b - 1) / b;
        private static int FloorLog2(int v) { int r = 0; while (v > 1) { v >>= 1; r++; } return r; }
    }
}
