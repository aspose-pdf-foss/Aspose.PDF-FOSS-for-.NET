using Aspose.Pdf.IO;
using Xunit;

namespace Aspose.Pdf.Tests.IO;

public class ZipReaderTests
{
    // Written by an independent implementation (.NET's ZipArchive): a deflated text entry in a
    // folder, a stored binary entry, and an entry with a UTF-8 name.
    private const string Archive =
        "UEsDBBQAAAAIAGlVN113Qk3TGQAAAPQBAAAPAAAAZG9jcy9yZWFkbWUudHh0ykjNyclXqMosUMgYZSmMjDAAAAAA//8DAFBLAwQUAAAAAABpVTdd9JkLRwUAAAAFAAAABwAAAHJhdy5iaW4BAgMEBVBL"
        + "AwQUAAYICABpVTddX3/OyhEAAAAJAAAACgAAAOWQjeWJjS50eHQqLUmzUMhLzE0FAAAA//8DAFBLAQIUABQAAAAIAGlVN113Qk3TGQAAAPQBAAAPAAAAAAAAAAAAAAAAAAAAAABkb2NzL3JlYWRtZS50"
        + "eHRQSwECFAAUAAAAAABpVTdd9JkLRwUAAAAFAAAABwAAAAAAAAAAAAAAAABGAAAAcmF3LmJpblBLAQIUABQABggIAGlVN11ff87KEQAAAAkAAAAKAAAAAAAAAAAAAAAAAHAAAADlkI3liY0udHh0"
        + "UEsFBgAAAAADAAMAqgAAAKkAAAAAAA==";

    [Fact]
    public void Reads_AnArchiveAnotherWriterMade()
    {
        var zip = new ZipReader(System.Convert.FromBase64String(Archive));
        Assert.Equal(new[] { "docs/readme.txt", "raw.bin", "名前.txt" }, System.Linq.Enumerable.Select(zip.Entries, e => e.Name));
        Assert.Equal(string.Concat(System.Linq.Enumerable.Repeat("hello zip ", 50)), System.Text.Encoding.UTF8.GetString(zip.Extract(zip.Find("docs/readme.txt")!)));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, zip.Extract(zip.Find("raw.bin")!));
        Assert.Equal("utf8 name", System.Text.Encoding.UTF8.GetString(zip.Extract(zip.Find("名前.txt")!)));
        Assert.Null(zip.Find("absent"));
    }

    [Fact]
    public void Reads_WhatTheWriterWrote()
    {
        var output = new System.IO.MemoryStream();
        using (var writer = new ZipWriter(output, leaveOpen: true))
        {
            writer.AddEntry("a.txt", System.Text.Encoding.ASCII.GetBytes("alpha"));
            writer.AddEntry("b/c.bin", new byte[1000]);
        }
        var zip = new ZipReader(output.ToArray());
        Assert.Equal(2, zip.Entries.Count);
        Assert.Equal("alpha", System.Text.Encoding.ASCII.GetString(zip.Extract(zip.Find("a.txt")!)));
        var zeros = zip.Find("b/c.bin")!;
        Assert.Equal(1000, zeros.Size);
        Assert.True(zeros.CompressedSize < 100);
        Assert.Equal(new byte[1000], zip.Extract(zeros));
    }

    [Fact]
    public void Refuses_BytesThatAreNotAnArchive()
    {
        Assert.Throws<System.IO.InvalidDataException>(() => new ZipReader(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23 }));
    }
}
