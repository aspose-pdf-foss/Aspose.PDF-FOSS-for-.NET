using System;
using System.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf;

/// <summary>
/// zlib (RFC 1950) deflating output stream — a public helper on the
/// public API surface. Bytes written are zlib-compressed into the
/// destination stream. Call <see cref="Finish"/> (or dispose) to flush the
/// compression trailer; the destination stream is left open.
/// The compression is the library's own deflater, so the bytes written are
/// the same on every host and target framework.
/// </summary>
public sealed class ZDeflaterOutputStream : Stream
{
    private readonly Stream _destination;
    private readonly MemoryStream _pending = new();
    private bool _finished;

    /// <summary>Wrap <paramref name="destination"/> for zlib compression.</summary>
    public ZDeflaterOutputStream(Stream destination)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
    }

    /// <summary>Compress everything written so far into the destination and write the
    /// zlib trailer. Idempotent; does not close the underlying destination stream.</summary>
    public void Finish()
    {
        if (_finished) return;
        _finished = true;
        var compressed = ManagedDeflater.DeflateZlib(_pending.ToArray());
        _destination.Write(compressed, 0, compressed.Length);
        _destination.Flush();
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

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (_finished) throw new InvalidOperationException("The stream has been finished.");
        _pending.Write(buffer, offset, count);
    }

    public override void Flush() { }
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
/// zlib (RFC 1950) inflating input stream — a public helper on the
/// public API surface. Reads zlib-compressed data from the source
/// stream and yields the decompressed bytes. The source stream is left open.
/// The decompression is the library's own inflater: the first read takes the rest
/// of the source and inflates it; a seekable source is then left just past the
/// compressed stream. A stream cut short yields what it holds; a malformed one
/// yields the bytes before the fault and then throws <see cref="InvalidDataException"/>.
/// </summary>
public sealed class ZInflaterInputStream : Stream
{
    private readonly Stream _source;
    private byte[]? _inflated;
    private int _position;
    private InvalidDataException? _fault;
    private bool _disposed;

    /// <summary>Wrap <paramref name="source"/> for zlib decompression.</summary>
    public ZInflaterInputStream(Stream source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    private byte[] Inflated()
    {
        if (_inflated is not null) return _inflated;
        var start = _source.CanSeek ? _source.Position : -1;
        var rest = new MemoryStream();
        _source.CopyTo(rest);
        var input = rest.ToArray();
        var progress = ManagedInflater.Inflate(input, input.Length, zlibWrapper: true, dictionary: null);
        if (progress.State == InflateState.Failed)
            _fault = new InvalidDataException("zlib: " + progress.Message);
        else if (progress.State == InflateState.NeedsDictionary)
            _fault = new InvalidDataException("zlib: preset dictionary not supported");
        if (start >= 0 && progress.State == InflateState.Done)
            _source.Position = start + progress.Consumed;
        return _inflated = progress.Output;
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

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ZInflaterInputStream));
        var inflated = Inflated();
        var n = Math.Min(count, inflated.Length - _position);
        if (n <= 0)
        {
            if (_fault is not null && count > 0) throw _fault;
            return 0;
        }
        Array.Copy(inflated, _position, buffer, offset, n);
        _position += n;
        return n;
    }
    public override void Flush() { }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
