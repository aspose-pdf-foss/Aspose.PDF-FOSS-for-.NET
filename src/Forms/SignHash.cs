namespace Aspose.Pdf.Forms;

/// <summary>
/// External-signer callback. When supplied via <see cref="Signature.CustomSignHash"/>,
/// the PDF signer hands the to-be-signed hash to the implementation and
/// embeds the returned PKCS#7/CMS envelope into /Contents — letting an
/// HSM, smartcard or remote signing service produce the signature
/// without exposing the private key to the process.
/// </summary>
/// <param name="hash">Detached digest of the PDF byte ranges produced
/// with the algorithm declared in <paramref name="digestHashAlgorithm"/>.</param>
/// <param name="digestHashAlgorithm">Hash algorithm applied to the byte
/// ranges; the implementation must wrap the result in a SignedData
/// envelope whose <c>digestAlgorithm</c> matches.</param>
/// <returns>The full PKCS#7/CMS envelope to write into /Contents.</returns>
public delegate byte[] SignHash(byte[] hash, DigestHashAlgorithm digestHashAlgorithm);
