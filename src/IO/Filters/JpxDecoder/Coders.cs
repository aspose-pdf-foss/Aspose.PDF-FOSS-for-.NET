using System;
using System.Collections.Generic;

namespace Aspose.Pdf.IO.Filters;

internal static partial class JpxDecoder
{
    private sealed class Tier1
    {
        private readonly int _w, _h, _orient;
        public readonly int[] Coeff;     // signed magnitude per sample (row-major, w*h)

        // bordered state arrays (w+2)*(h+2), index (y+1)*(w+2)+(x+1)
        private readonly int _sw;
        private readonly byte[] _sig;
        private readonly byte[] _sgn;    // 1 = negative
        private readonly byte[] _vis;    // visited in significance pass this bitplane
        private readonly byte[] _ref;    // refined at least once
        private readonly int[] _mag;     // accumulating magnitude
        private readonly bool _segSym;   // SEGMARK: a segmentation symbol ends each cleanup pass

        private Mq _mq = null!;

        public Tier1(int w, int h, int orient, int cbStyle)
        {
            _w = w; _h = h; _orient = orient;
            _segSym = (cbStyle & 0x20) != 0;
            _sw = w + 2;
            Coeff = new int[w * h];
            _sig = new byte[_sw * (h + 2)];
            _sgn = new byte[_sw * (h + 2)];
            _vis = new byte[_sw * (h + 2)];
            _ref = new byte[_sw * (h + 2)];
            _mag = new int[w * h];
        }

        private int Idx(int x, int y) => (y + 1) * _sw + (x + 1);

        public void Decode(byte[] data, int numPasses, int zeroBitplanes, int Mb)
        {
            _mq = new Mq(data);
            int numBitplanes = Mb - zeroBitplanes; // bit-planes that may contain data
            if (numBitplanes <= 0) return;

            int pass = 0;
            int bp = numBitplanes - 1; // MSB-first bit-plane index (1<<bp magnitude)
            // The first coding pass is always a cleanup pass on the most significant plane.
            CleanupPass(bp); pass++; bp--;
            while (bp >= 0 && pass < numPasses)
            {
                if (pass < numPasses) { SigPropPass(bp); pass++; }
                if (pass < numPasses) { MagRefPass(bp); pass++; }
                if (pass < numPasses) { CleanupPass(bp); pass++; }
                bp--;
            }

            // Build signed coefficients from accumulated magnitude + sign.
            for (int y = 0; y < _h; y++)
                for (int x = 0; x < _w; x++)
                {
                    int m = _mag[y * _w + x];
                    if (m == 0) { Coeff[y * _w + x] = 0; continue; }
                    Coeff[y * _w + x] = _sgn[Idx(x, y)] != 0 ? -m : m;
                }
        }

        private int ZcContext(int x, int y)
        {
            int i = Idx(x, y);
            int h = _sig[i - 1] + _sig[i + 1];
            int v = _sig[i - _sw] + _sig[i + _sw];
            int d = _sig[i - _sw - 1] + _sig[i - _sw + 1] + _sig[i + _sw - 1] + _sig[i + _sw + 1];
            if (_orient == 1) { int t = h; h = v; v = t; } // HL swaps h/v
            if (_orient == 3) // HH
            {
                int hv = h + v;
                if (d >= 3) return 8;
                if (d == 2) return hv >= 1 ? 7 : 6;
                if (d == 1) return hv >= 2 ? 5 : (hv == 1 ? 4 : 3);
                return hv >= 2 ? 2 : (hv == 1 ? 1 : 0);
            }
            // LL / LH (and HL after swap)
            if (h == 2) return 8;
            if (h == 1) return v >= 1 ? 7 : (d >= 1 ? 6 : 5);
            if (v == 2) return 4;
            if (v == 1) return 3;
            if (d >= 2) return 2;
            if (d == 1) return 1;
            return 0;
        }

        private void ScContext(int x, int y, out int ctx, out int xorbit)
        {
            int i = Idx(x, y);
            int hc = Math.Max(-1, Math.Min(1, Contrib(i - 1) + Contrib(i + 1)));
            int vc = Math.Max(-1, Math.Min(1, Contrib(i - _sw) + Contrib(i + _sw)));
            ctx = ScCtx(hc, vc, out xorbit);
        }

        private int ScCtx(int hc, int vc, out int xorbit)
        {
            // ISO 15444-1 Table D.2
            if (hc == 1 && vc == 1) { xorbit = 0; return 13; }
            if (hc == 1 && vc == 0) { xorbit = 0; return 12; }
            if (hc == 1 && vc == -1) { xorbit = 0; return 11; }
            if (hc == 0 && vc == 1) { xorbit = 0; return 10; }
            if (hc == 0 && vc == 0) { xorbit = 0; return 9; }
            if (hc == 0 && vc == -1) { xorbit = 1; return 10; }
            if (hc == -1 && vc == 1) { xorbit = 1; return 11; }
            if (hc == -1 && vc == 0) { xorbit = 1; return 12; }
            xorbit = 1; return 13; // (-1,-1)
        }

        private int Contrib(int i) => _sig[i] == 0 ? 0 : (_sgn[i] != 0 ? -1 : 1);

        private int MrContext(int x, int y)
        {
            int i = Idx(x, y);
            if (_ref[i] != 0) return 16;
            int n = _sig[i - 1] + _sig[i + 1] + _sig[i - _sw] + _sig[i + _sw]
                  + _sig[i - _sw - 1] + _sig[i - _sw + 1] + _sig[i + _sw - 1] + _sig[i + _sw + 1];
            return n > 0 ? 15 : 14;
        }

        private void DecodeSign(int x, int y)
        {
            ScContext(x, y, out int ctx, out int xorbit);
            int bit = _mq.Decode(ctx);
            _sgn[Idx(x, y)] = (byte)((bit ^ xorbit) != 0 ? 1 : 0);
        }

        private void SigPropPass(int bp)
        {
            int val = 1 << bp;
            for (int y0 = 0; y0 < _h; y0 += 4)
                for (int x = 0; x < _w; x++)
                    for (int y = y0; y < Math.Min(y0 + 4, _h); y++)
                    {
                        int i = Idx(x, y);
                        _vis[i] = 0;
                        if (_sig[i] != 0) continue;
                        int ctx = ZcContext(x, y);
                        if (ctx == 0) continue; // no significant neighbours — skip in SPP
                        if (_mq.Decode(ctx) == 1)
                        {
                            DecodeSign(x, y);
                            _sig[i] = 1;
                            _mag[y * _w + x] |= val;
                        }
                        _vis[i] = 1;
                    }
        }

        private void MagRefPass(int bp)
        {
            int val = 1 << bp;
            for (int y0 = 0; y0 < _h; y0 += 4)
                for (int x = 0; x < _w; x++)
                    for (int y = y0; y < Math.Min(y0 + 4, _h); y++)
                    {
                        int i = Idx(x, y);
                        if (_sig[i] == 0 || _vis[i] != 0) continue;
                        int ctx = MrContext(x, y);
                        if (_mq.Decode(ctx) == 1) _mag[y * _w + x] |= val;
                        _ref[i] = 1;
                    }
        }

        private void CleanupPass(int bp)
        {
            int val = 1 << bp;
            for (int y0 = 0; y0 < _h; y0 += 4)
                for (int x = 0; x < _w; x++)
                {
                    int rows = Math.Min(4, _h - y0);
                    int y = y0;
                    // Run-length: all 4 samples insignificant with zero ZC context.
                    if (rows == 4)
                    {
                        bool allZeroCtx = true;
                        for (int k = 0; k < 4; k++)
                        {
                            int i = Idx(x, y0 + k);
                            if (_sig[i] != 0 || _vis[i] != 0 || ZcContext(x, y0 + k) != 0) { allZeroCtx = false; break; }
                        }
                        if (allZeroCtx)
                        {
                            if (_mq.Decode(17) == 0) continue; // run of 4 zeros
                            int r = (_mq.Decode(18) << 1) | _mq.Decode(18);
                            y = y0 + r;
                            int i = Idx(x, y);
                            DecodeSign(x, y);
                            _sig[i] = 1;
                            _mag[y * _w + x] |= val;
                            y++;
                            for (; y < y0 + 4; y++)
                            {
                                int j = Idx(x, y);
                                if (_sig[j] != 0 || _vis[j] != 0) continue;
                                int ctx2 = ZcContext(x, y);
                                if (_mq.Decode(ctx2) == 1) { DecodeSign(x, y); _sig[j] = 1; _mag[y * _w + x] |= val; }
                            }
                            continue;
                        }
                    }
                    for (y = y0; y < y0 + rows; y++)
                    {
                        int i = Idx(x, y);
                        if (_sig[i] != 0 || _vis[i] != 0) { _vis[i] = 0; continue; }
                        int ctx = ZcContext(x, y);
                        if (_mq.Decode(ctx) == 1) { DecodeSign(x, y); _sig[i] = 1; _mag[y * _w + x] |= val; }
                    }
                }
            // SEGMARK (T.88 §D.5): a 4-bit segmentation symbol (the pattern 1010) coded
            // with the uniform context terminates every cleanup pass. We don't need its
            // value, but must consume the four bits to keep the arithmetic decoder aligned.
            if (_segSym)
                for (int k = 0; k < 4; k++) _mq.Decode(18);

            // reset visited for next bit-plane
            Array.Clear(_vis, 0, _vis.Length);
        }
    }

    private sealed class Mq
    {
        private readonly byte[] _d;
        private int _bp;
        private readonly int _end;
        private uint _c, _a;
        private int _ct;
        private readonly int[] _i = new int[19];
        private readonly int[] _mps = new int[19];

        public Mq(byte[] data)
        {
            _d = data; _end = data.Length; _bp = 0;
            for (int k = 0; k < 19; k++) { _i[k] = 0; _mps[k] = 0; }
            _i[0] = 4; _i[17] = 3; _i[18] = 46;
            _c = (uint)((_bp < _end ? _d[_bp] : 0xFF) << 16);
            ByteIn();
            _c <<= 7; _ct -= 7; _a = 0x8000;
        }

        private void ByteIn()
        {
            if (_bp < _end && _d[_bp] == 0xFF)
            {
                int b1 = (_bp + 1 < _end) ? _d[_bp + 1] : 0xFF;
                if (b1 > 0x8F) { _c += 0xFF00; _ct = 8; }
                else { _bp++; _c += (uint)(b1 << 9); _ct = 7; }
            }
            else
            {
                int b1 = (_bp + 1 < _end) ? _d[_bp + 1] : 0xFF;
                _bp++; _c += (uint)(b1 << 8); _ct = 8;
            }
        }

        public int Decode(int cx)
        {
            int idx = _i[cx];
            uint qe = Qe[idx];
            _a -= qe;
            int d;
            if ((_c >> 16) < qe)
            {
                // LPS exchange
                if (_a < qe) { _a = qe; d = _mps[cx]; _i[cx] = Nmps[idx]; }
                else { _a = qe; d = 1 - _mps[cx]; if (Sw[idx] != 0) _mps[cx] = 1 - _mps[cx]; _i[cx] = Nlps[idx]; }
                Renorm();
            }
            else
            {
                _c -= qe << 16;
                if ((_a & 0x8000) == 0)
                {
                    // MPS exchange
                    if (_a < qe) { d = 1 - _mps[cx]; if (Sw[idx] != 0) _mps[cx] = 1 - _mps[cx]; _i[cx] = Nlps[idx]; }
                    else { d = _mps[cx]; _i[cx] = Nmps[idx]; }
                    Renorm();
                }
                else d = _mps[cx];
            }
            return d;
        }

        private void Renorm()
        {
            do { if (_ct == 0) ByteIn(); _a <<= 1; _c <<= 1; _ct--; } while ((_a & 0x8000) == 0);
        }

        private static readonly uint[] Qe =
        {
            0x5601,0x3401,0x1801,0x0AC1,0x0521,0x0221,0x5601,0x5401,0x4801,0x3801,0x3001,0x2401,
            0x1C01,0x1601,0x5601,0x5401,0x5101,0x4801,0x3801,0x3401,0x3001,0x2801,0x2401,0x2201,
            0x1C01,0x1801,0x1601,0x1401,0x1201,0x1101,0x0AC1,0x09C1,0x08A1,0x0521,0x0441,0x02A1,
            0x0221,0x0141,0x0111,0x0085,0x0049,0x0025,0x0015,0x0009,0x0005,0x0001,0x5601,
        };
        private static readonly int[] Nmps = { 1,2,3,4,5,38,7,8,9,10,11,12,13,29,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,45,46 };
        private static readonly int[] Nlps = { 1,6,9,12,29,33,6,14,14,14,17,18,20,21,14,14,15,16,17,18,19,19,20,21,22,23,24,25,26,27,28,29,30,31,32,33,34,35,36,37,38,39,40,41,42,43,46 };
        private static readonly int[] Sw = { 1,0,0,0,0,0,1,0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0 };
    }

    private sealed class TagTree
    {
        private readonly int _levels;
        private readonly int[] _lw;
        private readonly int[][] _value; // current lower-bound value per node
        private readonly bool[][] _done; // value confirmed (final) per node

        public TagTree(int w, int h)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            var lw = new List<int>(); var lh = new List<int>();
            int cw = w, ch = h;
            lw.Add(cw); lh.Add(ch);
            while (cw > 1 || ch > 1) { cw = (cw + 1) >> 1; ch = (ch + 1) >> 1; lw.Add(cw); lh.Add(ch); }
            _levels = lw.Count;
            _lw = lw.ToArray();
            _value = new int[_levels][];
            _done = new bool[_levels][];
            for (int i = 0; i < _levels; i++) { _value[i] = new int[lw[i] * lh[i]]; _done[i] = new bool[lw[i] * lh[i]]; }
        }

        /// <summary>True iff the leaf value is &lt; <paramref name="threshold"/>.</summary>
        public bool Decode(PacketReader pr, int x, int y, int threshold)
        {
            int minv = 0;
            for (int l = _levels - 1; l >= 0; l--)
            {
                int idx = (y >> l) * _lw[l] + (x >> l);
                if (_value[l][idx] < minv) _value[l][idx] = minv;
                int guard = 0;
                while (!_done[l][idx] && _value[l][idx] < threshold && guard++ < 64)
                {
                    if (pr.ReadBit() == 1) _done[l][idx] = true;
                    else _value[l][idx]++;
                }
                minv = _value[l][idx];
                if (!_done[l][idx]) return false; // value >= threshold somewhere up the path
            }
            return _value[0][y * _lw[0] + x] < threshold;
        }
    }

    private sealed class PacketReader
    {
        private readonly byte[] _d;
        private int _pos;          // next byte to consume
        private uint _buffer;      // bit buffer (LSB-aligned)
        private int _bufferSize;   // valid bits in _buffer
        private bool _skipNextBit; // last consumed byte was 0xFF → next byte is stuffed

        public PacketReader(byte[] data) { _d = data; }

        public bool AtEnd => _pos >= _d.Length && _bufferSize == 0;

        public int ReadBit() => ReadBits(1);

        public int ReadBits(int count)
        {
            while (_bufferSize < count)
            {
                int b = _pos < _d.Length ? _d[_pos] : 0xFF;
                _pos++;
                if (_skipNextBit) { _buffer = (_buffer << 7) | (uint)(b & 0x7F); _bufferSize += 7; _skipNextBit = false; }
                else { _buffer = (_buffer << 8) | (uint)b; _bufferSize += 8; }
                if (b == 0xFF) _skipNextBit = true;
            }
            _bufferSize -= count;
            return (int)((_buffer >> _bufferSize) & (uint)((1 << count) - 1));
        }

        // Byte-align before reading a packet body. Discards the buffered partial bits and,
        // if the header ended on an 0xFF, skips the following stuffing byte.
        public void Align()
        {
            _bufferSize = 0;
            if (_skipNextBit) { _pos++; _skipNextBit = false; }
        }

        /// <summary>Skip a fixed-size marker segment (SOP/EPH) at the current
        /// byte-aligned position. A missing marker is tolerated — the encoder may
        /// omit SOP on some packets even when Scod signals it.</summary>
        public void SkipMarker(byte second, int totalLength)
        {
            if (_bufferSize != 0) return; // not byte-aligned: markers only sit between packets
            if (_skipNextBit) { _pos++; _skipNextBit = false; }
            if (_pos + 1 < _d.Length && _d[_pos] == 0xFF && _d[_pos + 1] == second)
                _pos += totalLength;
        }

        public byte[] ReadBytes(int n)
        {
            if (n < 0) n = 0;
            var r = new byte[n];
            for (int i = 0; i < n; i++) r[i] = _pos < _d.Length ? _d[_pos++] : (byte)0;
            return r;
        }
    }
}
