namespace Aspose.Pdf.Security;

/// <summary>
/// The two wire forms of a DSA/ECDSA signature: the RFC 3279 DER SEQUENCE { INTEGER r,
/// INTEGER s } that CMS carries, and the fixed-width IEEE P1363 concatenation r‖s that
/// the platform primitives produce and consume.
/// </summary>
internal static class SignatureEncoding
{
    /// <summary>DER (r, s) to r‖s, each element left-padded to <paramref name="elementLength"/> bytes.</summary>
    public static byte[] DerToP1363(byte[] der, int elementLength)
    {
        var seq = new Asn1Reader(der).ReadSequence();
        var r = StripLeadingZeros(seq.ReadIntegerBytes());
        var s = StripLeadingZeros(seq.ReadIntegerBytes());
        var result = new byte[elementLength * 2];
        Array.Copy(r, 0, result, elementLength - r.Length, r.Length);
        Array.Copy(s, 0, result, elementLength * 2 - s.Length, s.Length);
        return result;
    }

    /// <summary>r‖s (two equal halves) to DER (r, s).</summary>
    public static byte[] P1363ToDer(byte[] p1363)
    {
        var elementLength = p1363.Length / 2;
        var r = StripLeadingZeros(p1363[..elementLength]);
        var s = StripLeadingZeros(p1363[elementLength..]);
        var writer = new Asn1Writer();
        writer.WriteSequence(inner =>
        {
            inner.WriteIntegerBytes(r);
            inner.WriteIntegerBytes(s);
        });
        return writer.ToArray();
    }

    private static byte[] StripLeadingZeros(byte[] b)
    {
        var i = 0;
        while (i < b.Length - 1 && b[i] == 0) i++;
        return i == 0 ? b : b[i..];
    }
}
