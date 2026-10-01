using System.Text;

namespace Aspose.Pdf.Tests.Redaction;

/// <summary>One-page PDFs written byte by byte, so a redaction test knows exactly what the
/// page paints and where: the content stream, the page resources and any extra objects
/// (images, forms, fonts) are the caller's. Objects 1-3 are the catalog, the page tree and
/// the page; the content stream is object 4; extra objects are numbered from 5 in order.</summary>
internal static class RawPage
{
    internal const string Helvetica =
        "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>";

    /// <summary>An object body and, for a stream, its bytes (the /Length is added).</summary>
    internal sealed record Obj(string Dict, byte[]? Stream = null);

    internal static byte[] Build(string content, string resources, params Obj[] extra) =>
        Build(content, resources, string.Empty, string.Empty, extra);

    /// <summary>As <see cref="Build(string, string, Obj[])"/>, with entries added to the catalog and
    /// the page dictionaries.</summary>
    internal static byte[] Build(string content, string resources, string catalogEntries, string pageEntries,
        params Obj[] extra)
    {
        using var ms = new MemoryStream();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));
        var offsets = new List<long>();

        void Object(int number, string dict, byte[]? stream)
        {
            offsets.Add(ms.Position);
            if (stream is null) { Write($"{number} 0 obj\n{dict}\nendobj\n"); return; }
            var open = dict.TrimEnd();
            open = open.Substring(0, open.Length - 2) + $" /Length {stream.Length} >>";
            Write($"{number} 0 obj\n{open}\nstream\n");
            ms.Write(stream);
            Write("\nendstream\nendobj\n");
        }

        Write("%PDF-1.7\n");
        Object(1, $"<< /Type /Catalog /Pages 2 0 R {catalogEntries} >>", null);
        Object(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>", null);
        Object(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R " +
                  $"/Resources {resources} {pageEntries} >>", null);
        Object(4, "<< >>", Encoding.ASCII.GetBytes(content));
        for (var i = 0; i < extra.Length; i++) Object(5 + i, extra[i].Dict, extra[i].Stream);

        var xref = ms.Position;
        Write($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Write($"{o:D10} 00000 n \n");
        Write($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    /// <summary>An uncompressed DeviceRGB image, every pixel the given colour.</summary>
    internal static Obj SolidRgbImage(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 3] = r;
            pixels[i * 3 + 1] = g;
            pixels[i * 3 + 2] = b;
        }
        return new Obj($"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} " +
                       "/ColorSpace /DeviceRGB /BitsPerComponent 8 >>", pixels);
    }
}
