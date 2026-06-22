namespace Aspose.Pdf.Security;

/// <summary>
/// Minimal ASN.1 DER reader for parsing X.509 certificates, PKCS#8 keys, and PKCS#12 files.
/// </summary>
internal sealed class Asn1Reader
{
    private readonly byte[] _data;
    private int _pos;
    private readonly int _end;

    public Asn1Reader(byte[] data) : this(data, 0, data.Length) { }

    public Asn1Reader(byte[] data, int offset, int length)
    {
        _data = data;
        _pos = offset;
        _end = offset + length;
    }

    public int Position => _pos;
    public int Remaining => _end - _pos;
    public bool HasData => _pos < _end;

    /// <summary>Read tag byte (including class + constructed bits).</summary>
    public int PeekTag() => _pos < _end ? _data[_pos] : -1;

    /// <summary>Read a TLV and return tag, content bytes, advancing past the TLV.</summary>
    public (int tag, byte[] value) ReadTlv()
    {
        var tag = ReadByte();
        if ((tag & 0x1F) == 0x1F) // multi-byte tag
        {
            tag = tag << 8 | ReadByte();
            while ((_data[_pos - 1] & 0x80) != 0)
                tag = tag << 8 | ReadByte();
        }
        var len = ReadLength();
        var value = new byte[len];
        Array.Copy(_data, _pos, value, 0, len);
        _pos += len;
        ConsumeEoc();
        return (tag, value);
    }

    /// <summary>Read a SEQUENCE and return a sub-reader over its contents.</summary>
    public Asn1Reader ReadSequence()
    {
        ExpectTag(0x30);
        var len = ReadLength();
        var reader = new Asn1Reader(_data, _pos, len);
        _pos += len;
        ConsumeEoc();
        return reader;
    }

    /// <summary>Read a SET and return a sub-reader.</summary>
    public Asn1Reader ReadSet()
    {
        ExpectTag(0x31);
        var len = ReadLength();
        var reader = new Asn1Reader(_data, _pos, len);
        _pos += len;
        ConsumeEoc();
        return reader;
    }

    /// <summary>Read a context-tagged [N] CONSTRUCTED and return a sub-reader.</summary>
    public Asn1Reader ReadContextConstructed(int n)
    {
        ExpectTag(0xA0 | n);
        var len = ReadLength();
        var reader = new Asn1Reader(_data, _pos, len);
        _pos += len;
        ConsumeEoc();
        return reader;
    }

    /// <summary>Try to read a context-tagged [N] CONSTRUCTED, returning null if tag doesn't match.</summary>
    public Asn1Reader? TryReadContextConstructed(int n)
    {
        if (_pos >= _end || _data[_pos] != (0xA0 | n)) return null;
        return ReadContextConstructed(n);
    }

    public int ReadInteger()
    {
        ExpectTag(0x02);
        var len = ReadLength();
        var val = 0;
        for (var i = 0; i < len; i++)
            val = (val << 8) | _data[_pos++];
        return val;
    }

    public byte[] ReadIntegerBytes()
    {
        ExpectTag(0x02);
        var len = ReadLength();
        var val = new byte[len];
        Array.Copy(_data, _pos, val, 0, len);
        _pos += len;
        // Strip leading zero padding (sign byte)
        if (val.Length > 1 && val[0] == 0) val = val[1..];
        return val;
    }

    public byte[] ReadOctetString()
    {
        var tag = ReadByte();
        if (tag == 0x24)
        {
            // BER constructed OCTET STRING: concatenate all primitive segments
            var len = ReadLength();
            var sub = new Asn1Reader(_data, _pos, len);
            _pos += len;
            ConsumeEoc();
            using var ms = new System.IO.MemoryStream();
            while (sub.HasData)
            {
                var segment = sub.ReadOctetString();
                ms.Write(segment);
            }
            return ms.ToArray();
        }
        if (tag != 0x04)
            throw new InvalidOperationException($"ASN.1: expected tag 0x04, got 0x{tag:X2} at position {_pos - 1}");
        var len2 = ReadLength();
        var val = new byte[len2];
        Array.Copy(_data, _pos, val, 0, len2);
        _pos += len2;
        ConsumeEoc();
        return val;
    }

    public byte[] ReadBitString()
    {
        ExpectTag(0x03);
        var len = ReadLength();
        var unusedBits = _data[_pos++]; // usually 0
        var val = new byte[len - 1];
        Array.Copy(_data, _pos, val, 0, len - 1);
        _pos += len - 1;
        return val;
    }

    /// <summary>Read an OID and return as dotted string (e.g., "1.2.840.113549.1.1.1").</summary>
    public string ReadOid()
    {
        ExpectTag(0x06);
        var len = ReadLength();
        var endPos = _pos + len;

        var parts = new List<long>();
        // First byte encodes two components: a*40+b
        var first = _data[_pos++];
        parts.Add(first / 40);
        parts.Add(first % 40);

        // Remaining bytes use base-128 variable-length encoding
        while (_pos < endPos)
        {
            long val = 0;
            byte b;
            do
            {
                b = _data[_pos++];
                val = (val << 7) | (long)(b & 0x7F);
            } while ((b & 0x80) != 0);
            parts.Add(val);
        }

        return string.Join(".", parts);
    }

    public string ReadUtf8String()
    {
        ExpectTag(0x0C);
        var len = ReadLength();
        var val = System.Text.Encoding.UTF8.GetString(_data, _pos, len);
        _pos += len;
        return val;
    }

    public string ReadPrintableString()
    {
        ExpectTag(0x13);
        var len = ReadLength();
        var val = System.Text.Encoding.ASCII.GetString(_data, _pos, len);
        _pos += len;
        return val;
    }

    public string ReadAnyString()
    {
        var tag = _data[_pos];
        return tag switch
        {
            0x0C => ReadUtf8String(),
            0x13 => ReadPrintableString(),
            0x16 => ReadIA5String(),
            0x1E => ReadBMPString(),
            _ => ReadUtf8String(), // fallback
        };
    }

    public string ReadIA5String()
    {
        ExpectTag(0x16);
        var len = ReadLength();
        var val = System.Text.Encoding.ASCII.GetString(_data, _pos, len);
        _pos += len;
        return val;
    }

    public string ReadBMPString()
    {
        ExpectTag(0x1E);
        var len = ReadLength();
        var val = System.Text.Encoding.BigEndianUnicode.GetString(_data, _pos, len);
        _pos += len;
        return val;
    }

    public bool ReadBoolean()
    {
        ExpectTag(0x01);
        var len = ReadLength();
        var val = _data[_pos] != 0;
        _pos += len;
        return val;
    }

    /// <summary>Skip the current TLV entirely.</summary>
    public void Skip()
    {
        _pos++; // tag
        if ((_data[_pos - 1] & 0x1F) == 0x1F) // multi-byte tag
            while ((_data[_pos++] & 0x80) != 0) { }
        var len = ReadLength();
        _pos += len;
        ConsumeEoc();
    }

    /// <summary>Read the raw DER bytes of the current TLV (including tag + length + end-of-contents).</summary>
    public byte[] ReadRawTlv()
    {
        var start = _pos;
        Skip();
        var raw = new byte[_pos - start];
        Array.Copy(_data, start, raw, 0, raw.Length);
        return raw;
    }

    // ── Private helpers ────────────────────────────────────────────

    private int ReadByte()
    {
        if (_pos >= _end) throw new InvalidOperationException("ASN.1: unexpected end of data");
        return _data[_pos++];
    }

    private int ReadLength()
    {
        var first = ReadByte();
        if (first < 0x80) return first;
        var numBytes = first & 0x7F;
        if (numBytes == 0)
        {
            // BER indefinite length (0x80): scan for end-of-contents (00 00)
            return ScanIndefiniteLength();
        }
        var length = 0;
        for (var i = 0; i < numBytes; i++)
            length = (length << 8) | ReadByte();
        return length;
    }

    /// <summary>
    /// Scan forward from current position to find the end-of-contents (00 00) octets
    /// for a BER indefinite-length construct. Returns the content length (excluding the 00 00).
    /// Restores _pos to start so the caller can read the content.
    /// </summary>
    private int ScanIndefiniteLength()
    {
        var start = _pos;
        var scanPos = _pos;
        var depth = 1;
        while (scanPos < _end - 1 && depth > 0)
        {
            if (_data[scanPos] == 0x00 && _data[scanPos + 1] == 0x00)
            {
                depth--;
                if (depth == 0)
                    break;
                scanPos += 2;
            }
            else
            {
                // Read tag
                scanPos++;
                if ((_data[scanPos - 1] & 0x1F) == 0x1F)
                    while (scanPos < _end && (_data[scanPos++] & 0x80) != 0) { }

                // Read length
                if (scanPos >= _end) break;
                var lb = _data[scanPos++];
                if (lb == 0x80)
                    depth++;
                else if (lb < 0x80)
                    scanPos += lb;
                else
                {
                    var nb = lb & 0x7F;
                    var innerLen = 0;
                    for (var i = 0; i < nb && scanPos < _end; i++)
                        innerLen = (innerLen << 8) | _data[scanPos++];
                    scanPos += innerLen;
                }
            }
        }
        // _pos stays at start; caller reads content of this length
        // After caller processes content, we need to skip the 00 00
        _eocSkip = 2; // remember to skip end-of-contents after content
        return scanPos - start;
    }

    // Bytes to skip after an indefinite-length content is consumed (the 00 00 end-of-contents)
    private int _eocSkip;

    /// <summary>Skip end-of-contents octets (00 00) if the last ReadLength was indefinite.</summary>
    private void ConsumeEoc()
    {
        if (_eocSkip > 0)
        {
            _pos += _eocSkip;
            _eocSkip = 0;
        }
    }

    private void ExpectTag(int expected)
    {
        var actual = ReadByte();
        if (actual != expected)
            throw new InvalidOperationException($"ASN.1: expected tag 0x{expected:X2}, got 0x{actual:X2} at position {_pos - 1}");
    }
}
