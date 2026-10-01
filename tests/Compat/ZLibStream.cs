#if !NET6_0_OR_GREATER
// System.IO.Compression.ZLibStream arrived in .NET 6. The tests use the framework's own
// deflate as an independent reference for the library's codec, so on .NET Framework they get
// the same type here: RFC 1950 framing (a two-byte header and an Adler-32 trailer) around the
// raw deflate stream every BCL has had since .NET Framework 2.0.
using System;
using System.IO;

namespace System.IO.Compression;

internal sealed class ZLibStream : Stream
{
    // CMF: deflate with a 32K window. FLG: the check bits that make the header a
    // multiple of 31, for the three compression-level flags a writer may declare.
    private const byte CompressionMethodAndFlags = 0x78;
    private const byte FlagsFastest = 0x01;
    private const byte FlagsDefault = 0x9C;
    private const byte FlagsBest = 0xDA;
    private const int HeaderLength = 2;
    private const uint AdlerModulus = 65521;

    private readonly Stream _inner;
    private readonly DeflateStream _deflate;
    private readonly bool _leaveOpen;
    private readonly bool _compressing;
    private uint _adlerA = 1, _adlerB;
    private bool _disposed;
    private bool _wroteData;
    // A final fixed-Huffman block holding only end-of-block: the deflate encoding of nothing.
    // .NET Framework's DeflateStream emits no block at all when nothing was written, which
    // would leave the trailer where a block is expected.
    private static readonly byte[] EmptyDeflateBlock = { 0x03, 0x00 };

    public ZLibStream(Stream stream, CompressionMode mode) : this(stream, mode, leaveOpen: false) { }

    public ZLibStream(Stream stream, CompressionMode mode, bool leaveOpen)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        _inner = stream;
        _leaveOpen = leaveOpen;
        _compressing = mode == CompressionMode.Compress;
        if (_compressing)
        {
            WriteHeader(FlagsDefault);
            _deflate = new DeflateStream(stream, CompressionMode.Compress, leaveOpen: true);
        }
        else
        {
            SkipHeader();
            _deflate = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
        }
    }

    public ZLibStream(Stream stream, CompressionLevel level) : this(stream, level, leaveOpen: false) { }

    public ZLibStream(Stream stream, CompressionLevel level, bool leaveOpen)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        _inner = stream;
        _leaveOpen = leaveOpen;
        _compressing = true;
        WriteHeader(level == CompressionLevel.Fastest || level == CompressionLevel.NoCompression ? FlagsFastest
                  : level == CompressionLevel.Optimal ? FlagsDefault : FlagsBest);
        _deflate = new DeflateStream(stream, level, leaveOpen: true);
    }

    private void WriteHeader(byte flags)
    {
        _inner.WriteByte(CompressionMethodAndFlags);
        _inner.WriteByte(flags);
    }

    private void SkipHeader()
    {
        for (int i = 0; i < HeaderLength; i++)
        {
            if (_inner.ReadByte() < 0) throw new InvalidDataException("zlib header truncated");
        }
    }

    private void Accumulate(byte[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            _adlerA = (_adlerA + buffer[offset + i]) % AdlerModulus;
            _adlerB = (_adlerB + _adlerA) % AdlerModulus;
        }
    }

    public override bool CanRead => !_compressing && !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => _compressing && !_disposed;
    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_compressing) throw new NotSupportedException();
        return _deflate.Read(buffer, offset, count);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_compressing) throw new NotSupportedException();
        if (count > 0) _wroteData = true;
        Accumulate(buffer, offset, count);
        _deflate.Write(buffer, offset, count);
    }

    public override void Flush() => _deflate.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _deflate.Dispose();
            if (_compressing)
            {
                if (!_wroteData) _inner.Write(EmptyDeflateBlock, 0, EmptyDeflateBlock.Length);
                uint adler = (_adlerB << 16) | _adlerA;
                _inner.WriteByte((byte)(adler >> 24));
                _inner.WriteByte((byte)(adler >> 16));
                _inner.WriteByte((byte)(adler >> 8));
                _inner.WriteByte((byte)adler);
                _inner.Flush();
            }
            if (!_leaveOpen) _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}
#endif
