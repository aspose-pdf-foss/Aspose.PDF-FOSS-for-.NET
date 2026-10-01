

namespace Aspose.Pdf.Devices.Rasterizer;

internal static partial class ScanlineFiller
{

    /// <summary>The inputs and working set of one call, carried as one object through its stages.</summary>
    private sealed class ScanlineFillState
    {
        public EdgeTable edgeTable = default!;
        public byte[] pixels = default!;
        public int pixelW = 0;
        public int pixelH = 0;
        public byte r = 0;
        public byte g = 0;
        public byte b = 0;
        public byte a = 0;
        public bool evenOdd = false;
        public byte[]? clipMask = null;
        public string blendMode = default!;
        public bool knockout = false;
        public byte[]? softMask = null;
        public List<Edge> active = null!;
        public int[] coverage = null!;
        public List<EdgeHit> hits = null!;
        public int maxTouchedX;
        public int pending;
        public int rowXMax;
        public int rowXMin;
        public Edge[] sorted = null!;
    }
}
