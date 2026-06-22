namespace Aspose.Pdf.Security;

/// <summary>
/// Minimal ASN.1 DER writer for building CMS/PKCS#7 signatures.
/// </summary>
internal sealed class Asn1Writer
{
    private readonly MemoryStream _ms = new();

    public byte[] ToArray() => _ms.ToArray();

    public void WriteSequence(Action<Asn1Writer> inner)
        => WriteConstructed(0x30, inner);

    public void WriteSet(Action<Asn1Writer> inner)
        => WriteConstructed(0x31, inner);

    public void WriteContextConstructed(int n, Action<Asn1Writer> inner)
        => WriteConstructed(0xA0 | n, inner);

    public void WriteContextImplicit(int n, byte[] value)
    {
        WriteByte(0x80 | n);
        WriteLength(value.Length);
        _ms.Write(value);
    }

    public void WriteInteger(int value)
    {
        WriteByte(0x02);
        if (value == 0) { WriteLength(1); WriteByte(0); return; }
        var bytes = new List<byte>();
        var v = value;
        while (v > 0) { bytes.Insert(0, (byte)(v & 0xFF)); v >>= 8; }
        if (bytes[0] >= 0x80) bytes.Insert(0, 0);
        WriteLength(bytes.Count);
        foreach (var b in bytes) WriteByte(b);
    }

    public void WriteIntegerBytes(byte[] value)
    {
        WriteByte(0x02);
        // Ensure positive (add leading 0 if high bit set)
        if (value.Length > 0 && value[0] >= 0x80)
        {
            WriteLength(value.Length + 1);
            WriteByte(0);
        }
        else
        {
            WriteLength(value.Length);
        }
        _ms.Write(value);
    }

    public void WriteOctetString(byte[] value)
    {
        WriteByte(0x04);
        WriteLength(value.Length);
        _ms.Write(value);
    }

    public void WriteBitString(byte[] value)
    {
        WriteByte(0x03);
        WriteLength(value.Length + 1);
        WriteByte(0); // unused bits = 0
        _ms.Write(value);
    }

    public void WriteOid(string oid)
    {
        var parts = oid.Split('.').Select(long.Parse).ToArray();
        var encoded = new MemoryStream();
        encoded.WriteByte((byte)(parts[0] * 40 + parts[1]));
        for (var i = 2; i < parts.Length; i++)
            WriteBase128(encoded, parts[i]);

        WriteByte(0x06);
        WriteLength((int)encoded.Length);
        _ms.Write(encoded.ToArray());
    }

    public void WriteNull()
    {
        WriteByte(0x05);
        WriteByte(0x00);
    }

    public void WriteUtf8String(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        WriteByte(0x0C);
        WriteLength(bytes.Length);
        _ms.Write(bytes);
    }

    public void WritePrintableString(string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        WriteByte(0x13);
        WriteLength(bytes.Length);
        _ms.Write(bytes);
    }

    public void WriteIA5String(string value)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(value);
        WriteByte(0x16);
        WriteLength(bytes.Length);
        _ms.Write(bytes);
    }

    public void WriteGeneralizedTime(DateTime dt)
    {
        var str = dt.ToString("yyyyMMddHHmmss") + "Z";
        var bytes = System.Text.Encoding.ASCII.GetBytes(str);
        WriteByte(0x18);
        WriteLength(bytes.Length);
        _ms.Write(bytes);
    }

    public void WriteUtcTime(DateTime dt)
    {
        var str = dt.ToString("yyMMddHHmmss") + "Z";
        var bytes = System.Text.Encoding.ASCII.GetBytes(str);
        WriteByte(0x17);
        WriteLength(bytes.Length);
        _ms.Write(bytes);
    }

    public void WriteBoolean(bool value)
    {
        WriteByte(0x01);
        WriteLength(1);
        WriteByte(value ? (byte)0xFF : (byte)0x00);
    }

    /// <summary>Write pre-encoded DER bytes verbatim.</summary>
    public void WriteRaw(byte[] der) => _ms.Write(der);

    // ── Private helpers ────────────────────────────────────────────

    private void WriteConstructed(int tag, Action<Asn1Writer> inner)
    {
        var child = new Asn1Writer();
        inner(child);
        var childBytes = child.ToArray();
        WriteByte(tag);
        WriteLength(childBytes.Length);
        _ms.Write(childBytes);
    }

    private void WriteByte(int b) => _ms.WriteByte((byte)b);

    private void WriteLength(int length)
    {
        if (length < 0x80)
        {
            WriteByte(length);
        }
        else if (length < 0x100)
        {
            WriteByte(0x81);
            WriteByte(length);
        }
        else if (length < 0x10000)
        {
            WriteByte(0x82);
            WriteByte(length >> 8);
            WriteByte(length & 0xFF);
        }
        else if (length < 0x1000000)
        {
            WriteByte(0x83);
            WriteByte(length >> 16);
            WriteByte((length >> 8) & 0xFF);
            WriteByte(length & 0xFF);
        }
        else
        {
            WriteByte(0x84);
            WriteByte(length >> 24);
            WriteByte((length >> 16) & 0xFF);
            WriteByte((length >> 8) & 0xFF);
            WriteByte(length & 0xFF);
        }
    }

    private static void WriteBase128(MemoryStream ms, long value)
    {
        var bytes = new List<byte>();
        bytes.Add((byte)(value & 0x7F));
        value >>= 7;
        while (value > 0)
        {
            bytes.Add((byte)(0x80 | (value & 0x7F)));
            value >>= 7;
        }
        bytes.Reverse();
        ms.Write(bytes.ToArray());
    }
}
