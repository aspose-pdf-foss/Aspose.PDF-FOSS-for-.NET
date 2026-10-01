using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Aspose.Pdf;

/// <summary>
/// Static BCL members that netstandard2.0 and net48 lack. A missing instance method
/// can be supplied as an extension (the build-time polyfill package does that); a missing static
/// member cannot, so its call sites name this class instead. On the modern targets every
/// member forwards to the BCL, so behaviour there is exactly the framework's own.
/// The conditional compilation stays inside this file: feature code is target-blind.
/// </summary>
internal static class Compat
{
    // ── string.Create(provider, $"...") ────────────────────────────────────────────
    // The interpolated-string handler formats every hole with the provider and never
    // allocates the intermediate FormattableString.
    public static string Format(IFormatProvider provider,
        [InterpolatedStringHandlerArgument(nameof(provider))] ref DefaultInterpolatedStringHandler handler)
        => handler.ToStringAndClear();

    // ── Encoding.Latin1 ───────────────────────────────────────────────────────────
#if NET5_0_OR_GREATER
    public static Encoding Latin1 => Encoding.Latin1;
#else
    private const int Latin1CodePage = 28591;
    public static Encoding Latin1 { get; } = Encoding.GetEncoding(Latin1CodePage);
#endif

    // ── Math ──────────────────────────────────────────────────────────────────────
#if NETCOREAPP2_0_OR_GREATER
    public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    public static long Clamp(long value, long min, long max) => Math.Clamp(value, min, max);
    public static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);
    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    public static byte Clamp(byte value, byte min, byte max) => Math.Clamp(value, min, max);
#else
    public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    public static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;
    public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
    public static byte Clamp(byte value, byte min, byte max) => value < min ? min : value > max ? max : value;
#endif

#if NETCOREAPP3_0_OR_GREATER
    public static double Log2(double value) => Math.Log2(value);
#else
    /// <summary>Base-2 logarithm. A power of two answers with an exact integer, which
    /// the callers' floor() depends on and which Log(v)/Log(2) does not guarantee.</summary>
    public static double Log2(double value)
    {
        var approximate = Math.Log(value) / Math.Log(2);
        var rounded = Math.Round(approximate);
        return Math.Pow(2, rounded) == value ? rounded : approximate;
    }
#endif

#if NETCOREAPP3_0_OR_GREATER
    public static bool IsFinite(double value) => double.IsFinite(value);
    public static bool IsFinite(float value) => float.IsFinite(value);
#else
    public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
#endif

#if NETCOREAPP3_0_OR_GREATER
    public static int PopCount(byte value) => BitOperations.PopCount(value);
#else
    public static int PopCount(byte value)
    {
        var count = 0;
        for (var bits = (int)value; bits != 0; bits &= bits - 1) count++;
        return count;
    }
#endif

    // ── char / Convert ────────────────────────────────────────────────────────────
#if NET7_0_OR_GREATER
    public static bool IsAsciiDigit(char c) => char.IsAsciiDigit(c);
#else
    public static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
#endif

#if NET5_0_OR_GREATER
    public static string ToHexString(byte[] bytes) => Convert.ToHexString(bytes);
    public static string ToHexString(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);
    public static byte[] FromHexString(string hex) => Convert.FromHexString(hex);
#else
    public static string ToHexString(byte[] bytes) => ToHexString(bytes.AsSpan());
    public static string ToHexString(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }
    public static byte[] FromHexString(string hex)
    {
        if ((hex.Length & 1) != 0) throw new FormatException("The input is not a valid hex string as its length is not a multiple of 2.");
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(hex.Substring(2 * i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return bytes;
    }
#endif

    // ── OperatingSystem ───────────────────────────────────────────────────────────
    // The guard attributes keep the platform-compatibility analyzer aware that a call
    // inside `if (Compat.IsWindows())` is Windows-only code, exactly as with the BCL guards.
#if NET5_0_OR_GREATER
    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindows() => OperatingSystem.IsWindows();
    [SupportedOSPlatformGuard("macos")]
    public static bool IsMacOS() => OperatingSystem.IsMacOS();
    [SupportedOSPlatformGuard("linux")]
    public static bool IsLinux() => OperatingSystem.IsLinux();
#else
    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    [SupportedOSPlatformGuard("macos")]
    public static bool IsMacOS() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    [SupportedOSPlatformGuard("linux")]
    public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
#endif

    // ── Array ─────────────────────────────────────────────────────────────────────
#if NETCOREAPP2_0_OR_GREATER
    public static void Fill<T>(T[] array, T value) => Array.Fill(array, value);
    public static void Fill<T>(T[] array, T value, int startIndex, int count) => Array.Fill(array, value, startIndex, count);
#else
    public static void Fill<T>(T[] array, T value)
    {
        for (var i = 0; i < array.Length; i++) array[i] = value;
    }
    public static void Fill<T>(T[] array, T value, int startIndex, int count)
    {
        for (var i = 0; i < count; i++) array[startIndex + i] = value;
    }
#endif

    // ── ArgumentNullException ─────────────────────────────────────────────────────
#if NET6_0_OR_GREATER
    public static void ThrowIfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
        => ArgumentNullException.ThrowIfNull(argument, paramName);
#else
    public static void ThrowIfNull(object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null) throw new ArgumentNullException(paramName);
    }
#endif

    // ── Cryptography ──────────────────────────────────────────────────────────────
    // ⚠ The WebAssembly (WASI) build answers System.Security.Cryptography with PlatformNotSupportedException (see
    // RandomBytes below), and HTML export and the image extractor name each picture by its SHA-1. Where the platform
    // hash is missing, the library's own SHA-1 (the one its signature and PKCS#12 code already use) answers the same
    // digest; every platform that has the hash keeps using it.
    private static readonly bool HasPlatformSha1 = ProbePlatformSha1();

    private static bool ProbePlatformSha1()
    {
        try
        {
            PlatformSha1(Array.Empty<byte>());
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static byte[] Sha1(byte[] data) =>
        HasPlatformSha1 ? PlatformSha1(data) : Aspose.Pdf.Security.HmacSha.Sha1Hash(data);

#if NET5_0_OR_GREATER
    private static byte[] PlatformSha1(byte[] data) => SHA1.HashData(data);
    public static byte[] Sha256(byte[] data) => SHA256.HashData(data);
    public static byte[] Md5(byte[] data) => MD5.HashData(data);
#else
    private static byte[] PlatformSha1(byte[] data)
    {
        using var sha1 = SHA1.Create();
        return sha1.ComputeHash(data);
    }
    public static byte[] Sha256(byte[] data)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(data);
    }
    public static byte[] Md5(byte[] data)
    {
        using var md5 = MD5.Create();
        return md5.ComputeHash(data);
    }
#endif

    // ⚠ A platform can lack System.Security.Cryptography altogether: the WebAssembly (WASI) build answers every
    // call into it with PlatformNotSupportedException, and the library's encryption, its public-key envelopes and
    // PDF/A conversion all need random bytes. The generator is therefore asked once, and where it is missing the
    // bytes come from Guid.NewGuid, which the runtime fills from the platform's own secure generator (on WASI that
    // is wasi:random, which the specification requires to be cryptographically secure). Every platform that has
    // the generator keeps using it and nothing about those builds changes.
    private static readonly bool HasPlatformRandom = ProbePlatformRandom();

    private static bool ProbePlatformRandom()
    {
        try
        {
            PlatformRandomBytes(1);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static byte[] PlatformRandomBytes(int count)
    {
#if NET6_0_OR_GREATER
        return RandomNumberGenerator.GetBytes(count);
#else
        var bytes = new byte[count];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return bytes;
#endif
    }

    /// <summary>Cryptographically secure random bytes.</summary>
    public static byte[] RandomBytes(int count) =>
        HasPlatformRandom ? PlatformRandomBytes(count) : GuidRandomBytes(count);

    /// <summary>
    /// Random bytes taken from version-4 GUIDs, for a platform whose framework offers no generator of its own.
    ///
    /// ⚠ Six of a GUID's 128 bits carry its version and variant rather than randomness, and they sit in known
    /// places: the high nibble of byte 7 and the top two bits of byte 8. Those two bytes are dropped rather than
    /// passed on, so every byte handed out is a random one.
    /// </summary>
    internal static byte[] GuidRandomBytes(int count)
    {
        var bytes = new byte[count];
        var filled = 0;
        while (filled < count)
        {
            var guid = Guid.NewGuid().ToByteArray();
            for (var i = 0; i < guid.Length && filled < count; i++)
            {
                if (i == 7 || i == 8) continue;
                bytes[filled++] = guid[i];
            }
        }
        return bytes;
    }

    /// <summary>Uniform value in [fromInclusive, toExclusive) from the cryptographic
    /// generator; rejection sampling keeps the distribution unbiased.</summary>
    public static int RandomInt32(int fromInclusive, int toExclusive)
    {
#if NETCOREAPP3_0_OR_GREATER
        if (HasPlatformRandom) return RandomNumberGenerator.GetInt32(fromInclusive, toExclusive);
#endif
        if (fromInclusive >= toExclusive) throw new ArgumentException("Range must be positive.", nameof(toExclusive));
        var range = (uint)(toExclusive - fromInclusive);
        var limit = uint.MaxValue - uint.MaxValue % range;
        while (true)
        {
            var candidate = BitConverter.ToUInt32(RandomBytes(sizeof(uint)), 0);
            if (candidate < limit) return fromInclusive + (int)(candidate % range);
        }
    }

    // ── Random.Shared ─────────────────────────────────────────────────────────────
#if NET6_0_OR_GREATER
    public static Random SharedRandom => Random.Shared;
#else
    [ThreadStatic] private static Random? t_random;
    public static Random SharedRandom => t_random ??= new Random();
#endif

    // ── BigInteger unsigned big-endian round trip ─────────────────────────────────
#if NETCOREAPP2_1_OR_GREATER
    public static BigInteger BigIntegerFromUnsignedBigEndian(byte[] bytes)
        => new(bytes, isUnsigned: true, isBigEndian: true);
    public static byte[] ToUnsignedBigEndian(BigInteger value)
        => value.ToByteArray(isUnsigned: true, isBigEndian: true);
#else
    public static BigInteger BigIntegerFromUnsignedBigEndian(byte[] bytes)
    {
        // little-endian two's complement with a spare zero byte so the top bit reads as positive
        var littleEndian = new byte[bytes.Length + 1];
        for (var i = 0; i < bytes.Length; i++) littleEndian[i] = bytes[bytes.Length - 1 - i];
        return new BigInteger(littleEndian);
    }
    public static byte[] ToUnsignedBigEndian(BigInteger value)
    {
        if (value.Sign < 0) throw new OverflowException("Negative values do not have an unsigned representation.");
        var littleEndian = value.ToByteArray();
        var length = littleEndian.Length;
        if (length > 1 && littleEndian[length - 1] == 0) length--;   // the sign byte
        var bigEndian = new byte[length];
        for (var i = 0; i < length; i++) bigEndian[i] = littleEndian[length - 1 - i];
        return bigEndian;
    }
#endif

    // ── HttpClient synchronous send / read ────────────────────────────────────────
    public static HttpResponseMessage Send(HttpClient client, HttpRequestMessage request, HttpCompletionOption completion)
#if NET5_0_OR_GREATER
        => client.Send(request, completion);
#else
        => client.SendAsync(request, completion).GetAwaiter().GetResult();
#endif

    public static Stream ReadAsStream(HttpContent content)
#if NET5_0_OR_GREATER
        => content.ReadAsStream();
#else
        => content.ReadAsStreamAsync().GetAwaiter().GetResult();
#endif

    // ── StreamWriter / StreamReader that leave the stream open ────────────────────
    // The old constructors have no defaulted bufferSize, so the framework's own default
    // is named here.
    private const int DefaultTextBufferSize = 1024;
    public static StreamWriter LeaveOpenWriter(Stream stream, Encoding encoding)
        => new(stream, encoding, DefaultTextBufferSize, leaveOpen: true);
    public static StreamReader LeaveOpenReader(Stream stream, Encoding encoding)
        => new(stream, encoding, detectEncodingFromByteOrderMarks: true, DefaultTextBufferSize, leaveOpen: true);

    // ── OperatingSystem.IsWindowsVersionAtLeast ───────────────────────────────────
#if NET5_0_OR_GREATER
    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindowsVersionAtLeast(int major, int minor) => OperatingSystem.IsWindowsVersionAtLeast(major, minor);
#else
    [SupportedOSPlatformGuard("windows")]
    public static bool IsWindowsVersionAtLeast(int major, int minor)
        => IsWindows() && Environment.OSVersion.Version >= new Version(major, minor);
#endif

    // ── Path.GetRelativePath ──────────────────────────────────────────────────────
#if NETCOREAPP2_0_OR_GREATER
    public static string GetRelativePath(string relativeTo, string path) => Path.GetRelativePath(relativeTo, path);
#else
    public static string GetRelativePath(string relativeTo, string path)
    {
        var from = Path.GetFullPath(relativeTo);
        if (!from.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)) from += Path.DirectorySeparatorChar;
        var relative = new Uri(from).MakeRelativeUri(new Uri(Path.GetFullPath(path))).ToString();
        return Uri.UnescapeDataString(relative).Replace('/', Path.DirectorySeparatorChar);
    }
#endif

    // ── Array.MaxLength ───────────────────────────────────────────────────────────
#if NET6_0_OR_GREATER
    public static int ArrayMaxLength => Array.MaxLength;
#else
    public static int ArrayMaxLength => 0x7FFFFFC7;
#endif

    // ── RegexOptions.NonBacktracking ──────────────────────────────────────────────
    // The linear-time engine is a .NET 7 addition; without it the option is simply absent.
#if NET7_0_OR_GREATER
    public static System.Text.RegularExpressions.RegexOptions NonBacktracking => System.Text.RegularExpressions.RegexOptions.NonBacktracking;
#else
    public static System.Text.RegularExpressions.RegexOptions NonBacktracking => System.Text.RegularExpressions.RegexOptions.None;
#endif

    // ── int.Parse / int.TryParse over a span ──────────────────────────────────────
#if NETCOREAPP2_1_OR_GREATER
    public static int ParseInt32(ReadOnlySpan<char> text) => int.Parse(text);
    public static bool TryParseInt32(ReadOnlySpan<char> text, out int value) => int.TryParse(text, out value);
#else
    public static int ParseInt32(ReadOnlySpan<char> text) => int.Parse(text.ToString());
    public static bool TryParseInt32(ReadOnlySpan<char> text, out int value) => int.TryParse(text.ToString(), out value);
#endif

    // ── string.Concat over spans ──────────────────────────────────────────────────
#if NETCOREAPP3_0_OR_GREATER
    public static string Concat(ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c) => string.Concat(a, b, c);
#else
    public static string Concat(ReadOnlySpan<char> a, ReadOnlySpan<char> b, ReadOnlySpan<char> c)
        => new StringBuilder(a.Length + b.Length + c.Length).Append(a.ToString()).Append(b.ToString()).Append(c.ToString()).ToString();
#endif

    // ── Color.IsKnownColor ────────────────────────────────────────────────────────
#if NETCOREAPP2_0_OR_GREATER || NETFRAMEWORK
    public static bool IsKnownColor(System.Drawing.Color color) => color.IsKnownColor;
#else
    // Color.FromName answers an unknown name with a named colour whose ARGB is zero; no
    // known colour has that value (Transparent is 0x00FFFFFF).
    public static bool IsKnownColor(System.Drawing.Color color) => color.IsNamedColor && color.ToArgb() != 0;
#endif

    // ── X509KeyStorageFlags.EphemeralKeySet ───────────────────────────────────────
#if NETSTANDARD2_0
    // The flag is missing from the netstandard2.0 reference assembly only; every runtime
    // this target runs on knows its value.
    private const int EphemeralKeySetValue = 32;
    public static System.Security.Cryptography.X509Certificates.X509KeyStorageFlags EphemeralKeySet
        => (System.Security.Cryptography.X509Certificates.X509KeyStorageFlags)EphemeralKeySetValue;
#else
    public static System.Security.Cryptography.X509Certificates.X509KeyStorageFlags EphemeralKeySet
        => System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet;
#endif

    // ── X509Certificate2 loading ──────────────────────────────────────────────────
    // .NET 9 retires the byte[]/file constructors in favour of X509CertificateLoader.
#if NET9_0_OR_GREATER
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificate(byte[] der)
        => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(der);
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificateFromFile(string path)
        => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(path);
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(
        byte[] pfx, string password, System.Security.Cryptography.X509Certificates.X509KeyStorageFlags flags)
        => System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12(pfx, password, flags);
#else
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificate(byte[] der) => new(der);
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadCertificateFromFile(string path) => new(path);
    public static System.Security.Cryptography.X509Certificates.X509Certificate2 LoadPkcs12(
        byte[] pfx, string password, System.Security.Cryptography.X509Certificates.X509KeyStorageFlags flags)
        => new(pfx, password, flags);
#endif

    // ── DSA / ECDSA signatures in RFC 3279 DER form ───────────────────────────────
    // .NET 5 lets the primitives speak DER directly; before that they only produce and
    // accept the fixed-width IEEE P1363 r‖s, so the conversion happens here.
#if NET5_0_OR_GREATER
    public static byte[] SignHashDer(ECDsa key, byte[] hash) => key.SignHash(hash, DSASignatureFormat.Rfc3279DerSequence);
    public static bool VerifyHashDer(ECDsa key, byte[] hash, byte[] derSignature)
        => key.VerifyHash(hash, derSignature, DSASignatureFormat.Rfc3279DerSequence);
    public static bool VerifyDataDer(ECDsa key, byte[] data, byte[] derSignature, HashAlgorithmName hashAlgorithm)
        => key.VerifyData(data, derSignature, hashAlgorithm, DSASignatureFormat.Rfc3279DerSequence);
    public static byte[] CreateSignatureDer(DSA key, byte[] hash) => key.CreateSignature(hash, DSASignatureFormat.Rfc3279DerSequence);
#else
    private const int BitsPerByte = 8;
    private static int FieldBytes(ECDsa key) => (key.KeySize + BitsPerByte - 1) / BitsPerByte;
    public static byte[] SignHashDer(ECDsa key, byte[] hash) => Security.SignatureEncoding.P1363ToDer(key.SignHash(hash));
    public static bool VerifyHashDer(ECDsa key, byte[] hash, byte[] derSignature)
        => key.VerifyHash(hash, Security.SignatureEncoding.DerToP1363(derSignature, FieldBytes(key)));
    public static bool VerifyDataDer(ECDsa key, byte[] data, byte[] derSignature, HashAlgorithmName hashAlgorithm)
        => key.VerifyData(data, Security.SignatureEncoding.DerToP1363(derSignature, FieldBytes(key)), hashAlgorithm);
    public static byte[] CreateSignatureDer(DSA key, byte[] hash) => Security.SignatureEncoding.P1363ToDer(key.CreateSignature(hash));
#endif
}
