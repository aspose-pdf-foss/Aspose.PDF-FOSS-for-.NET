using System.IO.Compression;
using System.Text;
using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class ZipWriterTests
{
    [Fact]
    public void Archive_ReadsBackWithTheFrameworkReader()
    {
        var first = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("<svg><rect/></svg>\n", 200)));
        var second = Encoding.UTF8.GetBytes("second page");
        using var ms = new MemoryStream();
        using (var zip = new ZipWriter(ms, new DateTime(2026, 9, 4, 12, 30, 0), leaveOpen: true))
        {
            zip.AddEntry("page.svg", first);
            zip.AddEntry("page_2.svg", second);
        }

        using var archive = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(new[] { "page.svg", "page_2.svg" }, archive.Entries.Select(e => e.FullName).ToArray());
        Assert.Equal(first, ReadAll(archive.Entries[0]));
        Assert.Equal(second, ReadAll(archive.Entries[1]));
        Assert.Equal(first.Length, archive.Entries[0].Length);
        Assert.True(archive.Entries[0].CompressedLength < first.Length / 10);
        Assert.Equal(new DateTime(2026, 9, 4, 12, 30, 0), archive.Entries[0].LastWriteTime.DateTime);
    }

    [Fact]
    public void Archive_BytesDependOnlyOnTheInput()
    {
        byte[] Build()
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipWriter(ms, new DateTime(2026, 9, 4), leaveOpen: true))
                zip.AddEntry("a.txt", Encoding.ASCII.GetBytes("deterministic archive"));
            return ms.ToArray();
        }
        Assert.Equal(Build(), Build());
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
