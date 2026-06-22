using System;
using System.IO;
using System.IO.Compression;

namespace Aspose.Pdf;

/// <summary>
/// zlib (RFC 1950) deflating output stream — a public helper matching the
/// Aspose.PDF for .NET surface. Bytes written are zlib-compressed into the
/// destination stream. Call <see cref="Finish"/> (or dispose) to flush the
/// compression trailer; the destination stream is left open.
/// </summary>
public sealed class ZDeflaterOutputStream : Stream
{
    private readonly ZLibStream _z;
    private bool _finished;

    /// <summary>Wrap <paramref name="destination"/> for zlib compression.</summary>
    public ZDeflaterOutputStream(Stream destination)
    {
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        _z = new ZLibStream(destination, CompressionMode.Compress, leaveOpen: true);
    }

    /// <summary>Flush remaining compressed data and write the zlib trailer.
    /// Idempotent; does not close the underlying destination stream.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        _z.Dispose(); // flushes the deflate trailer; destination kept open (leaveOpen)
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_finished;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) => _z.Write(buffer, offset, count);
    public override void Flush() => _z.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) Finish();
        base.Dispose(disposing);
    }
}

/// <summary>
/// zlib (RFC 1950) inflating input stream — a public helper matching the
/// Aspose.PDF for .NET surface. Reads zlib-compressed data from the source
/// stream and yields the decompressed bytes. The source stream is left open.
/// </summary>
public sealed class ZInflaterInputStream : Stream
{
    private readonly ZLibStream _z;

    /// <summary>Wrap <paramref name="source"/> for zlib decompression.</summary>
    public ZInflaterInputStream(Stream source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        _z = new ZLibStream(source, CompressionMode.Decompress, leaveOpen: true);
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => _z.Read(buffer, offset, count);
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _z.Dispose();
        base.Dispose(disposing);
    }
}
