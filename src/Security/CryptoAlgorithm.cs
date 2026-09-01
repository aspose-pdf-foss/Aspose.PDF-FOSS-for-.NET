namespace Aspose.Pdf
{
    /// <summary>
    /// PDF encryption algorithm.
    /// </summary>
    public enum CryptoAlgorithm
    {
        /// <summary>RC4 with 40-bit key (PDF 1.1).</summary>
        RC4x40,
        /// <summary>RC4 with 128-bit key (PDF 1.4).</summary>
        RC4x128,
        /// <summary>AES with 128-bit key (PDF 1.5).</summary>
        AESx128,
        /// <summary>AES with 256-bit key (PDF 2.0).</summary>
        AESx256,
        /// <summary>A custom encryption handler is used (placeholder value for API compatibility).</summary>
        Custom,
    }
}

namespace Aspose.Pdf.Security
{
    /// <summary>
    /// Information about a document's encryption.
    /// </summary>
    public sealed class EncryptionInfo
    {
        public CryptoAlgorithm Algorithm { get; init; }
        public int KeyLength { get; init; }
        public int Version { get; init; }
        public int Revision { get; init; }
        public bool HasUserPassword { get; init; }
        public bool HasOwnerPassword { get; init; }
    }
}
