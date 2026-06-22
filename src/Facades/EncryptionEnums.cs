namespace Aspose.Pdf.Facades;

/// <summary>Key strength selector paired with <see cref="Algorithm"/> to pick
/// the concrete RC4/AES variant used by <see cref="PdfFileSecurity"/>.</summary>
public enum KeySize
{
    x40 = 40,
    x128 = 128,
    x256 = 256,
}

/// <summary>Encryption algorithm family used by <see cref="PdfFileSecurity"/>.
/// The <see cref="KeySize"/> argument paired with <see cref="AES"/> or
/// <see cref="RC4"/> picks the concrete variant. The legacy
/// <c>AESx128/AESx256/RC4x40/RC4x128</c> values are retained for source
/// compatibility with earlier releases.</summary>
public enum Algorithm
{
    RC4x40 = 0,
    RC4x128 = 1,
    AESx128 = 2,
    AESx256 = 3,
    /// <summary>AES — strength selected by the paired KeySize.</summary>
    AES = 4,
    /// <summary>RC4 — strength selected by the paired KeySize.</summary>
    RC4 = 5,
}
