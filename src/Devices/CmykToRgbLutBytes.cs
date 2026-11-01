using System.Numerics;
using System.Runtime.CompilerServices;

namespace Aspose.Pdf.Devices;

/// <summary>
/// The CMYK conversion for 8-bit samples (0 = no ink), the form an image's pixels come in: the
/// per-pixel loops of image decoding and blitting run it for every pixel, where the plain
/// subtractive formula they used before cost a few integer operations. The quadrilinear blend is
/// folded along K once per process - for each of the 256 K samples the grid's C/M/Y cube, blended
/// to that K - and each cube entry holds its R, G and B in one vector, so a pixel takes the seven
/// blends of a trilinear step once for all three channels, in one SIMD register each where the
/// platform has them. It agrees with <see cref="Convert"/> to within a level.
/// </summary>
internal static partial class CmykToRgbLut
{
    /// <summary>How many values a byte sample takes.</summary>
    private const int ByteLevels = 256;
    private const int Channels = 3;
    private const float RoundingHalf = 0.5f;

    /// <summary>Entry strides of one grid step along Y, M and C inside a K plane.</summary>
    private const int PlaneStepY = 1;
    private const int PlaneStepM = Grid * PlaneStepY;
    private const int PlaneStepC = Grid * PlaneStepM;
    private const int PlaneSize = Grid * PlaneStepC;

    /// <summary>Per byte sample and axis: the offset of the grid cell it falls in, and its weight
    /// toward the cell's upper side, as <see cref="Convert"/> places it.</summary>
    private static readonly int[] CellC = BuildCellOffsets(PlaneStepC);
    private static readonly int[] CellM = BuildCellOffsets(PlaneStepM);
    private static readonly int[] CellY = BuildCellOffsets(PlaneStepY);
    private static readonly float[] CellWeight = BuildCellWeights();

    private static Vector4[]? _kPlanes;

    private static int[] BuildCellOffsets(int stride)
    {
        var offsets = new int[ByteLevels];
        for (var v = 0; v < ByteLevels; v++) offsets[v] = CellOf(v).index * stride;
        return offsets;
    }

    private static float[] BuildCellWeights()
    {
        var weights = new float[ByteLevels];
        for (var v = 0; v < ByteLevels; v++) weights[v] = (float)CellOf(v).fraction;
        return weights;
    }

    /// <summary>The grid cell a byte sample falls in and how far along it, as <see cref="Convert"/> places it.</summary>
    private static (int index, double fraction) CellOf(int v)
    {
        var f = v / (double)(ByteLevels - 1) * (Grid - 1);
        var i = Math.Min((int)f, Grid - 2);
        return (i, f - i);
    }

    /// <summary>For every K sample, the grid's C/M/Y cube blended to that K.</summary>
    private static Vector4[] KPlanes
    {
        get
        {
            if (_kPlanes is not null) return _kPlanes;
            lock (_lock)
            {
                if (_kPlanes is not null) return _kPlanes;
                var lut = Table;
                var planes = new Vector4[ByteLevels * PlaneSize];
                for (var k = 0; k < ByteLevels; k++)
                {
                    var (ki, dk) = CellOf(k);
                    for (var cmy = 0; cmy < PlaneSize; cmy++)
                    {
                        var p = (cmy * Grid + ki) * Channels;
                        double Blended(int ch) => lut[p + ch] * (1 - dk) + lut[p + Channels + ch] * dk;
                        planes[k * PlaneSize + cmy] = new Vector4((float)Blended(0), (float)Blended(1), (float)Blended(2), 0);
                    }
                }
                _kPlanes = planes;
                return planes;
            }
        }
    }

    /// <summary><see cref="Convert"/> for 8-bit samples (0 = no ink).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static (byte r, byte g, byte b) ConvertBytes(byte c, byte m, byte y, byte k)
    {
        var planes = _kPlanes ?? KPlanes;
        var p = k * PlaneSize + CellC[c] + CellM[m] + CellY[y];
        float wc = CellWeight[c], wm = CellWeight[m], wy = CellWeight[y];
        var c0 = Vector4.Lerp(Vector4.Lerp(planes[p], planes[p + PlaneStepY], wy),
            Vector4.Lerp(planes[p + PlaneStepM], planes[p + PlaneStepM + PlaneStepY], wy), wm);
        var q = p + PlaneStepC;
        var c1 = Vector4.Lerp(Vector4.Lerp(planes[q], planes[q + PlaneStepY], wy),
            Vector4.Lerp(planes[q + PlaneStepM], planes[q + PlaneStepM + PlaneStepY], wy), wm);
        var rgb = Vector4.Lerp(c0, c1, wc);
        return ((byte)(rgb.X + RoundingHalf), (byte)(rgb.Y + RoundingHalf), (byte)(rgb.Z + RoundingHalf));
    }

    /// <summary>
    /// <see cref="ConvertBytes"/> that skips the blend while the colour repeats the one before it,
    /// as a flat area's pixels do. Not thread-safe: one per decode or blit.
    /// </summary>
    internal sealed class Memo
    {
        private const int CyanShift = 24;
        private const int MagentaShift = 16;
        private const int YellowShift = 8;

        private int _lastKey;
        private (byte r, byte g, byte b) _last = ConvertBytes(0, 0, 0, 0);

        public (byte r, byte g, byte b) Convert(byte c, byte m, byte y, byte k)
        {
            var key = (c << CyanShift) | (m << MagentaShift) | (y << YellowShift) | k;
            if (key == _lastKey) return _last;
            _lastKey = key;
            _last = ConvertBytes(c, m, y, k);
            return _last;
        }
    }
}
