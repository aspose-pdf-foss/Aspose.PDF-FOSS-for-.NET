using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
// The stages of the box downsample: the source read and the destination write, each inside its bitmap lock.

    /// <summary>Per-destination-pixel channel sums and sample counts of a box downsample.</summary>
    private sealed class BoxSums
    {
        public readonly long[] R, G, B, A, Count;

        public BoxSums(int pixels)
        {
            R = new long[pixels]; G = new long[pixels]; B = new long[pixels]; A = new long[pixels]; Count = new long[pixels];
        }
    }

    /// <summary>One lock-scoped stage of the box downsample.</summary>
    private static void WriteDownsampledRows(int dw, int dh, BoxSums sums, BitmapData ddata)
    {
        var drow = new byte[dw * 4];
        for (int y = 0; y < dh; y++)
        {
            int b = y * dw;
            for (int x = 0; x < dw; x++)
            {
                long n = Math.Max(1, sums.Count[b + x]);
                int o = x * 4;
                drow[o] = (byte)(sums.B[b + x] / n);
                drow[o + 1] = (byte)(sums.G[b + x] / n);
                drow[o + 2] = (byte)(sums.R[b + x] / n);
                drow[o + 3] = (byte)(sums.A[b + x] / n);
            }
            System.Runtime.InteropServices.Marshal.Copy(drow, 0, ddata.Scan0 + (nint)y * ddata.Stride, drow.Length);
        }
    }

    /// <summary>One lock-scoped stage of the box downsample.</summary>
    private static void ReadSourceBoxes(int sw, int sh, int dw, int dh, PixelFormat fmt, BoxSums sums, GdiColor[]? pal, BitmapData data)
    {
        int stride = data.Stride;
        var row = new byte[Math.Abs(stride)];
        for (int y = 0; y < sh; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + (nint)y * stride, row, 0, row.Length);
            int dy = (int)((long)y * dh / sh);
            int rowBase = dy * dw;
            switch (fmt)
            {
                case PixelFormat.Format1bppIndexed:
                {
                    var c0 = pal![0]; var c1 = pal[1];
                    for (int x = 0; x < sw; x++)
                    {
                        int bit = (row[x >> 3] >> (7 - (x & 7))) & 1;
                        var c = bit == 0 ? c0 : c1;
                        int di = rowBase + (int)((long)x * dw / sw);
                        sums.R[di] += c.R; sums.G[di] += c.G; sums.B[di] += c.B; sums.A[di] += c.A; sums.Count[di]++;
                    }
                    break;
                }
                case PixelFormat.Format8bppIndexed:
                {
                    for (int x = 0; x < sw; x++)
                    {
                        var c = pal![row[x]];
                        int di = rowBase + (int)((long)x * dw / sw);
                        sums.R[di] += c.R; sums.G[di] += c.G; sums.B[di] += c.B; sums.A[di] += c.A; sums.Count[di]++;
                    }
                    break;
                }
                case PixelFormat.Format24bppRgb:
                {
                    for (int x = 0; x < sw; x++)
                    {
                        int o = x * 3;
                        int di = rowBase + (int)((long)x * dw / sw);
                        sums.B[di] += row[o]; sums.G[di] += row[o + 1]; sums.R[di] += row[o + 2]; sums.A[di] += 255; sums.Count[di]++;
                    }
                    break;
                }
                default: // 32bpp
                {
                    bool hasAlpha = fmt == PixelFormat.Format32bppArgb;
                    for (int x = 0; x < sw; x++)
                    {
                        int o = x * 4;
                        int di = rowBase + (int)((long)x * dw / sw);
                        sums.B[di] += row[o]; sums.G[di] += row[o + 1]; sums.R[di] += row[o + 2];
                        sums.A[di] += hasAlpha ? row[o + 3] : 255; sums.Count[di]++;
                    }
                    break;
                }
            }
        }
    }
}
