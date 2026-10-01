using System.Globalization;
using System.Numerics;

namespace Aspose.Pdf.Security;

/// <summary>
/// ECDSA signature verification over the NIST prime curves P-256, P-384 and P-521 (FIPS 186-4 § 6.4), in
/// System.Numerics.BigInteger rather than System.Security.Cryptography - the same reason <see cref="RsaKey"/>
/// exists: a runtime without the platform's crypto (a WebAssembly build) still verifies a signature. A key on
/// another curve answers null, and the caller asks the platform.
/// </summary>
internal static class EcdsaVerifier
{
    private const string OidP256 = "1.2.840.10045.3.1.7";
    private const string OidP384 = "1.3.132.0.34";
    private const string OidP521 = "1.3.132.0.35";

    /// <summary>The first byte of an uncompressed point (SEC 1 § 2.3.3): X and Y follow at full length.</summary>
    private const byte UncompressedPoint = 0x04;

    private const int BitsPerByte = 8;

    private static readonly Dictionary<string, Curve> Curves = new(StringComparer.Ordinal)
    {
        [OidP256] = new Curve(
            "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF",
            "5AC635D8AA3A93E7B3EBBD55769886BC651D06B0CC53B0F63BCE3C3E27D2604B",
            "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296",
            "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5",
            "FFFFFFFF00000000FFFFFFFFFFFFFFFFBCE6FAADA7179E84F3B9CAC2FC632551"),
        [OidP384] = new Curve(
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFFFF0000000000000000FFFFFFFF",
            "B3312FA7E23EE7E4988E056BE3F82D19181D9C6EFE8141120314088F5013875AC656398D8A2ED19D2A85C8EDD3EC2AEF",
            "AA87CA22BE8B05378EB1C71EF320AD746E1D3B628BA79B9859F741E082542A385502F25DBF55296C3A545E3872760AB7",
            "3617DE4A96262C6F5D9E98BF9292DC29F8F41DBD289A147CE9DA3113B5F0B8C00A60B1CE1D7E819D7A431D7C90EA0E5F",
            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFC7634D81F4372DDF581A0DB248B0A77AECEC196ACCC52973"),
        [OidP521] = new Curve(
            "01FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF",
            "0051953EB9618E1C9A1F929A21A0B68540EEA2DA725B99B315F3B8B489918EF109E156193951EC7E937B1652C0BD3BB1BF073573DF883D2C34F1EF451FD46B503F00",
            "00C6858E06B70404E9CD9E3ECB662395B4429C648139053FB521F828AF606B4D3DBAA14B5E77EFE75928FE1DC127A2FFA8DE3348B3C1856A429BF97E7E31C2E5BD66",
            "011839296A789A3BC0045C8A5FB42C7D1BD998F54449579B446817AFBD17273E662C97EE72995EF42640C550B9013FAD0761353C7086A272C24088BE94769FD16650",
            "01FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFA51868783BF2F966B7FCC0148F709A5D03BB5C9B8899C47AEBB6FB71E91386409"),
    };

    /// <summary>
    /// Whether <paramref name="derSignature"/> (RFC 3279: SEQUENCE { r, s }) signs <paramref name="hash"/> under the
    /// EC public key of the certificate <paramref name="certDer"/>: true or false for a key on a curve this knows,
    /// null for any other curve.
    /// </summary>
    public static bool? Verify(byte[] certDer, byte[] hash, byte[] derSignature)
    {
        var (curveOid, point) = PublicKey(certDer);
        if (curveOid is null || !Curves.TryGetValue(curveOid, out var curve)) return null;
        var q = curve.Decode(point);
        if (q is null) return false;
        var sig = new Asn1Reader(derSignature).ReadSequence();
        var r = Compat.BigIntegerFromUnsignedBigEndian(sig.ReadIntegerBytes());
        var s = Compat.BigIntegerFromUnsignedBigEndian(sig.ReadIntegerBytes());
        return curve.Verifies(q.Value, Truncated(hash, curve.N), r, s);
    }

    /// <summary>The named curve and the encoded point of a certificate's SubjectPublicKeyInfo.</summary>
    private static (string? curveOid, byte[] point) PublicKey(byte[] certDer)
    {
        var tbs = new Asn1Reader(certDer).ReadSequence().ReadSequence();
        tbs.TryReadContextConstructed(0); // version
        tbs.Skip(); // serial
        tbs.Skip(); // signature algorithm
        tbs.Skip(); // issuer
        tbs.Skip(); // validity
        tbs.Skip(); // subject
        var spki = tbs.ReadSequence();
        var algorithm = spki.ReadSequence();
        algorithm.ReadOid(); // id-ecPublicKey
        // The parameters are the namedCurve OID; explicit curve parameters (a SEQUENCE) name no curve here.
        var curveOid = algorithm.PeekTag() == Asn1Tags.Oid ? algorithm.ReadOid() : null;
        return (curveOid, spki.ReadBitString());
    }

    /// <summary>The hash as an integer of at most the order's bit length, its leftmost bits (FIPS 186-4 § 6.4).</summary>
    private static BigInteger Truncated(byte[] hash, BigInteger n)
    {
        var e = Compat.BigIntegerFromUnsignedBigEndian(hash);
        var excess = hash.Length * BitsPerByte - BitLength(n);
        return excess > 0 ? e >> excess : e;
    }

    /// <summary>The bit length of a non-negative integer (BigInteger.GetBitLength is not on every target).</summary>
    private static int BitLength(BigInteger value)
    {
        var bytes = value.ToByteArray(); // little-endian, a trailing 0 byte keeps the sign positive
        var top = bytes.Length - 1;
        while (top > 0 && bytes[top] == 0) top--;
        var bits = top * BitsPerByte;
        for (int rest = bytes[top]; rest != 0; rest >>= 1) bits++;
        return bits;
    }

    private static class Asn1Tags
    {
        public const int Oid = 0x06;
    }

    /// <summary>A short Weierstrass curve y² = x³ - 3x + b over GF(p), with base point G of prime order n.</summary>
    private sealed class Curve
    {
        public BigInteger P { get; }
        public BigInteger B { get; }
        public BigInteger N { get; }
        private readonly (BigInteger x, BigInteger y) _g;
        private readonly int _fieldLength;

        public Curve(string p, string b, string gx, string gy, string n)
        {
            P = Hex(p);
            B = Hex(b);
            _g = (Hex(gx), Hex(gy));
            N = Hex(n);
            _fieldLength = (BitLength(P) + BitsPerByte - 1) / BitsPerByte;
        }

        private static BigInteger Hex(string digits) =>
            BigInteger.Parse("0" + digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        /// <summary>An uncompressed point on this curve, or null for any other encoding or a point off the curve.</summary>
        public (BigInteger x, BigInteger y)? Decode(byte[] encoded)
        {
            if (encoded.Length != 1 + 2 * _fieldLength || encoded[0] != UncompressedPoint) return null;
            var x = Compat.BigIntegerFromUnsignedBigEndian(encoded[1..(1 + _fieldLength)]);
            var y = Compat.BigIntegerFromUnsignedBigEndian(encoded[(1 + _fieldLength)..]);
            if (x >= P || y >= P) return null;
            var right = Mod(x * x * x - 3 * x + B);
            return Mod(y * y) == right ? (x, y) : null;
        }

        /// <summary>FIPS 186-4 § 6.4.2: r and s in [1, n-1], and the x of u1·G + u2·Q is r modulo n.</summary>
        public bool Verifies((BigInteger x, BigInteger y) q, BigInteger e, BigInteger r, BigInteger s)
        {
            if (r.Sign <= 0 || r >= N || s.Sign <= 0 || s >= N) return false;
            var w = BigInteger.ModPow(s, N - 2, N);
            var u1 = e * w % N;
            var u2 = r * w % N;
            var sum = Add(Multiply(_g, u1), Multiply(q, u2));
            if (sum.z.IsZero) return false;
            var zInverse = BigInteger.ModPow(sum.z, P - 2, P);
            var x = Mod(sum.x * zInverse * zInverse);
            return x % N == r;
        }

        // Jacobian coordinates (X, Y, Z) for x = X/Z², y = Y/Z³; Z = 0 is the point at infinity.
        private (BigInteger x, BigInteger y, BigInteger z) Multiply((BigInteger x, BigInteger y) point, BigInteger k)
        {
            var result = (x: BigInteger.One, y: BigInteger.One, z: BigInteger.Zero);
            var addend = (point.x, point.y, z: BigInteger.One);
            for (var bit = BitLength(k) - 1; bit >= 0; bit--)
            {
                result = Double(result);
                if (!(k >> bit).IsEven) result = Add(result, addend);
            }
            return result;
        }

        private (BigInteger x, BigInteger y, BigInteger z) Double((BigInteger x, BigInteger y, BigInteger z) p)
        {
            if (p.z.IsZero || p.y.IsZero) return (BigInteger.One, BigInteger.One, BigInteger.Zero);
            // a = -3: M = 3(X - Z²)(X + Z²).
            var zz = Mod(p.z * p.z);
            var m = Mod(3 * (p.x - zz) * (p.x + zz));
            var yy = Mod(p.y * p.y);
            var s = Mod(4 * p.x * yy);
            var x = Mod(m * m - 2 * s);
            var y = Mod(m * (s - x) - 8 * yy * yy);
            var z = Mod(2 * p.y * p.z);
            return (x, y, z);
        }

        private (BigInteger x, BigInteger y, BigInteger z) Add(
            (BigInteger x, BigInteger y, BigInteger z) a, (BigInteger x, BigInteger y, BigInteger z) b)
        {
            if (a.z.IsZero) return b;
            if (b.z.IsZero) return a;
            var az2 = Mod(a.z * a.z);
            var bz2 = Mod(b.z * b.z);
            var u1 = Mod(a.x * bz2);
            var u2 = Mod(b.x * az2);
            var s1 = Mod(a.y * bz2 * b.z);
            var s2 = Mod(b.y * az2 * a.z);
            if (u1 == u2)
                return s1 == s2 ? Double(a) : (BigInteger.One, BigInteger.One, BigInteger.Zero);
            var h = Mod(u2 - u1);
            var r = Mod(s2 - s1);
            var hh = Mod(h * h);
            var hhh = Mod(hh * h);
            var v = Mod(u1 * hh);
            var x = Mod(r * r - hhh - 2 * v);
            var y = Mod(r * (v - x) - s1 * hhh);
            var z = Mod(a.z * b.z * h);
            return (x, y, z);
        }

        private BigInteger Mod(BigInteger value)
        {
            var reduced = value % P;
            return reduced.Sign < 0 ? reduced + P : reduced;
        }
    }
}
