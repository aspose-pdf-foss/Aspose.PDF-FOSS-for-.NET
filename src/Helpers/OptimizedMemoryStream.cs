using System;
using System.Collections.Generic;
using System.IO;

namespace Aspose.Pdf;

/// <summary>
/// A growable in-memory stream that can exceed the 2 GB single-array limit of
/// <see cref="System.IO.MemoryStream"/> by storing data in fixed-size chunks.
/// Matches the Aspose.PDF for .NET OptimizedMemoryStream public surface.
/// </summary>
public sealed class OptimizedMemoryStream : Stream
{
    private const int ChunkSize = 64 * 1024 * 1024; // 64 MB per chunk
    private readonly List<byte[]> _chunks = new();
    private long _length;
    private long _position;

    public OptimizedMemoryStream() { }

    public OptimizedMemoryStream(byte[] buffer)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        Write(buffer, 0, buffer.Length);
        _position = 0;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override void Flush() { }

    private void EnsureCapacity(long required)
    {
        while ((long)_chunks.Count * ChunkSize < required)
            _chunks.Add(new byte[ChunkSize]);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (count <= 0) return;
        EnsureCapacity(_position + count);
        var src = offset;
        var remaining = count;
        while (remaining > 0)
        {
            var chunk = (int)(_position / ChunkSize);
            var within = (int)(_position % ChunkSize);
            var n = Math.Min(remaining, ChunkSize - within);
            Array.Copy(buffer, src, _chunks[chunk], within, n);
            _position += n;
            src += n;
            remaining -= n;
        }
        if (_position > _length) _length = _position;
    }

    public override void WriteByte(byte value)
    {
        EnsureCapacity(_position + 1);
        _chunks[(int)(_position / ChunkSize)][(int)(_position % ChunkSize)] = value;
        _position++;
        if (_position > _length) _length = _position;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (count <= 0 || _position >= _length) return 0;
        var read = (int)Math.Min(count, _length - _position);
        var dst = offset;
        var remaining = read;
        while (remaining > 0)
        {
            var chunk = (int)(_position / ChunkSize);
            var within = (int)(_position % ChunkSize);
            var n = Math.Min(remaining, ChunkSize - within);
            Array.Copy(_chunks[chunk], within, buffer, dst, n);
            _position += n;
            dst += n;
            remaining -= n;
        }
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => _position,
        };
        if (target < 0) throw new IOException("Attempted to seek before the start of the stream.");
        _position = target;
        return _position;
    }

    public override void SetLength(long value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        EnsureCapacity(value);
        _length = value;
        if (_position > _length) _position = _length;
    }

    /// <summary>Copy the entire contents to a single byte[]. Throws when the
    /// length exceeds the 2 GB .NET array limit.</summary>
    public byte[] ToArray()
    {
        if (_length > int.MaxValue)
            throw new InvalidOperationException("Stream length exceeds the 2 GB array limit.");
        var result = new byte[_length];
        var saved = _position;
        _position = 0;
        Read(result, 0, (int)_length);
        _position = saved;
        return result;
    }
}
