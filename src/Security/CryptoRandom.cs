using System.Runtime.InteropServices;

namespace Aspose.Pdf.Security;

/// <summary>
/// Cryptographically secure random bytes using OS entropy. macOS/Linux read
/// from <c>/dev/urandom</c>; Windows P/Invokes <c>bcrypt.dll!BCryptGenRandom</c>.
/// </summary>
internal static class CryptoRandom
{
    public static byte[] GetBytes(int count)
    {
        var buffer = new byte[count];
        Fill(buffer);
        return buffer;
    }

    public static void Fill(byte[] buffer)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            FillFromBCrypt(buffer);
        }
        else
        {
            FillFromDevUrandom(buffer);
        }
    }

    private static void FillFromDevUrandom(byte[] buffer)
    {
        using var stream = File.OpenRead("/dev/urandom");
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0) throw new IOException("Failed to read from /dev/urandom");
            offset += read;
        }
    }

    // BCRYPT_USE_SYSTEM_PREFERRED_RNG = 0x00000002 — pulls from the Windows
    // platform CSPRNG without needing a separately-opened algorithm provider.
    private const uint BCRYPT_USE_SYSTEM_PREFERRED_RNG = 0x00000002;

    [DllImport("bcrypt.dll", EntryPoint = "BCryptGenRandom")]
    private static extern int BCryptGenRandom(IntPtr hAlgorithm, byte[] pbBuffer, int cbBuffer, uint dwFlags);

    private static void FillFromBCrypt(byte[] buffer)
    {
        if (buffer.Length == 0) return;
        var status = BCryptGenRandom(IntPtr.Zero, buffer, buffer.Length, BCRYPT_USE_SYSTEM_PREFERRED_RNG);
        // BCryptGenRandom returns an NTSTATUS — non-negative is success.
        if (status < 0) throw new IOException($"BCryptGenRandom failed with NTSTATUS 0x{status:X8}");
    }
}
