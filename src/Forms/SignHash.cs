namespace Aspose.Pdf.Forms;

/// <summary>
/// External-signer callback. When supplied via <see cref="Signature.CustomSignHash"/>,
/// the PDF signer hands the to-be-signed hash to the implementation in place of the
/// certificate's private key - an HSM, smartcard or remote signing service signs it
/// without exposing the key to the process - and wraps the returned signature value in
/// the PKCS#7/CMS envelope written to /Contents, under the certificate's algorithm.
/// </summary>
/// <param name="hash">Detached digest of the PDF byte ranges produced
/// with the algorithm declared in <paramref name="digestHashAlgorithm"/>.</param>
/// <param name="digestHashAlgorithm">Hash algorithm applied to the byte ranges; the
/// implementation signs <paramref name="hash"/> as a digest of that algorithm (for RSA,
/// PKCS#1 v1.5 over the DigestInfo of it).</param>
/// <returns>The raw signature value over the hash.</returns>
public delegate byte[] SignHash(byte[] hash, DigestHashAlgorithm digestHashAlgorithm);
