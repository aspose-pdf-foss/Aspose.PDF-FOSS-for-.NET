using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document : IDisposable
{
    /// <summary>The per-format heads of the image natural-size read: PNG and JPEG.</summary>
    private static (double widthPt, double heightPt)? TryReadJpegNaturalSize(byte[] d, bool applyResolution)
    {
        double widthPt = default;
        double heightPt = default;
        widthPt = heightPt = 0;
        double dpiX = 72, dpiY = 72; int pw = 0, ph = 0;
        int p = 2;
        while (p + 4 < d.Length)
        {
            if (d[p] != 0xFF) { p++; continue; }
            int marker = d[p + 1];
            if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7)) { p += 2; continue; }
            int seg = BE16(d, p + 2);
            if (seg < 2) break;
            if (marker == 0xE0 && p + 4 + 14 <= d.Length
                && d[p + 4] == (byte)'J' && d[p + 5] == (byte)'F' && d[p + 6] == (byte)'I' && d[p + 7] == (byte)'F')
            {
                int units = d[p + 11];
                int dx = BE16(d, p + 12), dy = BE16(d, p + 14);
                if (dx > 0 && dy > 0)
                {
                    if (units == 1) { dpiX = dx; dpiY = dy; }            // dots per inch
                    else if (units == 2) { dpiX = dx * 2.54; dpiY = dy * 2.54; } // dots per cm
                }
            }
            else if ((marker >= 0xC0 && marker <= 0xCF)
                     && marker != 0xC4 && marker != 0xC8 && marker != 0xCC
                     && p + 9 <= d.Length)
            {
                ph = BE16(d, p + 5);
                pw = BE16(d, p + 7);
            }
            p += 2 + seg;
        }
        if (pw <= 0 || ph <= 0) return null;
        if (dpiX <= 0 || !applyResolution) dpiX = 72;
        if (dpiY <= 0 || !applyResolution) dpiY = 72;
        widthPt = pw * 72.0 / dpiX;
        heightPt = ph * 72.0 / dpiY;
        return (widthPt, heightPt);
    }

    /// <summary></summary>
    private static (double widthPt, double heightPt)? TryReadPngNaturalSize(byte[] d, bool applyResolution)
    {
        double widthPt = default;
        double heightPt = default;
        widthPt = heightPt = 0;
        int pw = BE32(d, 16), ph = BE32(d, 20);
        if (pw <= 0 || ph <= 0) return null;
        double dpiX = 72, dpiY = 72;
        for (int i = 8; i + 12 <= d.Length;)
        {
            int len = BE32(d, i);
            if (len < 0) break;
            if (d[i + 4] == 'p' && d[i + 5] == 'H' && d[i + 6] == 'Y' && d[i + 7] == 's' && i + 8 + 9 <= d.Length)
            {
                long ppuX = (uint)BE32(d, i + 8), ppuY = (uint)BE32(d, i + 12);
                if (d[i + 16] == 1 && ppuX > 0 && ppuY > 0) // unit = metre
                {
                    dpiX = ppuX * 0.0254;
                    dpiY = ppuY * 0.0254;
                }
                break;
            }
            if (d[i + 4] == 'I' && d[i + 5] == 'D' && d[i + 6] == 'A' && d[i + 7] == 'T') break;
            i += 12 + len; // length + type + data + CRC
        }
        if (dpiX <= 0 || !applyResolution) dpiX = 72;
        if (dpiY <= 0 || !applyResolution) dpiY = 72;
        widthPt = pw * 72.0 / dpiX;
        heightPt = ph * 72.0 / dpiY;
        return (widthPt, heightPt);
    }
    private static int BE16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
    private static int BE32(byte[] d, int o) => (d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3];
}
