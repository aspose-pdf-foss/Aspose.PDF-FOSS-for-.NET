using System.Drawing;
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

public sealed partial class GdiPlusPageRenderer
{
    /// <summary>Box-downsamples the K-times layer into the device layer: every device pixel averages its K by K block, channel by channel.</summary>
    private static void DownsampleSupersampledLayer(Bitmap ssBmp, Bitmap layer, System.Drawing.Rectangle rect, System.Drawing.Rectangle ssRect, int k)
    {
        if (rect.Width > 0 && rect.Height > 0)
        {
            var bd = ssBmp.LockBits(ssRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var ld = layer.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var rows = new byte[k][];
                for (int i = 0; i < k; i++) rows[i] = new byte[ssRect.Width * 4];
                var outRow = new byte[rect.Width * 4];
                float norm = 1f / (k * k);
                for (int y = 0; y < rect.Height; y++)
                {
                    for (int i = 0; i < k; i++)
                        System.Runtime.InteropServices.Marshal.Copy((IntPtr)(bd.Scan0.ToInt64() + (long)(y * k + i) * bd.Stride), rows[i], 0, ssRect.Width * 4);
                    for (int x = 0; x < rect.Width; x++)
                        AverageBlock(rows, x, k, norm, outRow);
                    System.Runtime.InteropServices.Marshal.Copy(outRow, 0, ld.Scan0 + y * ld.Stride, outRow.Length);
                }
            }
            finally { ssBmp.UnlockBits(bd); layer.UnlockBits(ld); }
        }
    }

    /// <summary>Box-average the K by K supersampled block at device column <paramref name="x"/>
    /// into one BGRA pixel of <paramref name="outRow"/>.</summary>
    private static void AverageBlock(byte[][] rows, int x, int k, float norm, byte[] outRow)
    {
        int sb = 0, sg2 = 0, sr2 = 0, sa = 0;
        for (int i = 0; i < k; i++)
        {
            var row = rows[i];
            int o = x * k * 4;
            for (int j = 0; j < k; j++)
            {
                sb += row[o + j * 4]; sg2 += row[o + j * 4 + 1];
                sr2 += row[o + j * 4 + 2]; sa += row[o + j * 4 + 3];
            }
        }
        int o2 = x * 4;
        outRow[o2] = (byte)(sb * norm + 0.5f);
        outRow[o2 + 1] = (byte)(sg2 * norm + 0.5f);
        outRow[o2 + 2] = (byte)(sr2 * norm + 0.5f);
        outRow[o2 + 3] = (byte)(sa * norm + 0.5f);
    }

    /// <summary>Copies the device backdrop under the rect into the K-times layer, each device pixel replicated into its K by K block.</summary>
    private static void UpsampleBackdropCopy(Bitmap savedBmp, Bitmap ssBmp, System.Drawing.Rectangle rect, System.Drawing.Rectangle ssRect, int k)
    {
        if (rect.Width > 0 && rect.Height > 0)
        {
            var br = savedBmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var sr = ssBmp.LockBits(ssRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var srcRow = new byte[rect.Width * 4];
                var ssRow = new byte[ssRect.Width * 4];
                for (int y = 0; y < rect.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(br.Scan0 + y * br.Stride, srcRow, 0, srcRow.Length);
                    for (int x = 0; x < rect.Width; x++)
                        for (int j = 0; j < k; j++)
                            System.Array.Copy(srcRow, x * 4, ssRow, (x * k + j) * 4, 4);
                    for (int i = 0; i < k; i++)
                        System.Runtime.InteropServices.Marshal.Copy(ssRow, 0, (IntPtr)(sr.Scan0.ToInt64() + (long)(y * k + i) * sr.Stride), ssRow.Length);
                }
            }
            finally { savedBmp.UnlockBits(br); ssBmp.UnlockBits(sr); }
        }
    }
}
