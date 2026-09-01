namespace Aspose.Pdf;

/// <summary>Small security helpers shared by the encryption paths.</summary>
internal static partial class SecureUtils
{
    /// <summary>A cryptographically random integer in <paramref name="min"/>..<paramref name="max"/>
    /// inclusive — used to salt values that must not repeat across documents.</summary>
    public static int GenerateRandomSalt(int min, int max)
    {
        if (min > max) (min, max) = (max, min);
        if (min == max) return min;
        return System.Security.Cryptography.RandomNumberGenerator.GetInt32(min, max == int.MaxValue ? max : max + 1);
    }
}
