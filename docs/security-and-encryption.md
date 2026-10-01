# Security and Encryption

Besides encryption and signatures, `HiddenDataSanitizer`
(`Aspose.Pdf.Security.HiddenDataSanitization`) strips hidden data from a
document in place: `Sanitize(doc)` applies the `HiddenDataSanitizationOptions`
flags (`RemoveAnnotations`, `RemoveJavaScriptsAndActions`, `RemoveAttachments`,
`RemoveMetadata`, `RemoveSearchIndexAndPrivateInfo`, `FlattenForms`,
`FlattenLayers`; `HiddenDataSanitizationOptions.All()` sets them all), and
`ConvertPagesToImages` / `SanitizeAllToImages(doc, dpi)` replace every page with
a rendered image. A sanitized document is always saved whole, so the removed
data does not survive in an earlier revision.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Security.HiddenDataSanitization;

using var doc = new Document("input.pdf");
new HiddenDataSanitizer(HiddenDataSanitizationOptions.All()).Sanitize(doc);
doc.Save("sanitized.pdf");
```

## Encryption algorithms

`CryptoAlgorithm`:

| Algorithm  | Key size | PDF version | Notes                       |
|------------|----------|-------------|-----------------------------|
| `RC4x40`   | 40-bit   | PDF 1.1+    | Legacy, weak                |
| `RC4x128`  | 128-bit  | PDF 1.4+    | Legacy                      |
| `AESx128`  | 128-bit  | PDF 1.5+    | Recommended minimum         |
| `AESx256`  | 256-bit  | PDF 2.0     | Strongest available         |
| `Custom`   | —        | —           | Reported for a non-standard security handler (`ICustomSecurityHandler`) |

The ciphers and digests the security handlers use (AES, RC4, SHA-2, SHA-3, MD5,
HMAC) ship in pure managed C#; on the encryption path the only call into the
platform crypto provider is the random-number generator used for salts, file keys and file IDs (a runtime
without one falls back to GUID-derived random bytes).

### PDF 2.0 deprecation gates

ISO 32000-2 retires the SHA-1-era mechanisms, and this library refuses them up
front rather than writing a file that violates its own declared version. These
throw `DeprecatedFeatureException`:

- **RC4 under the 2.0 encryption flag** — the `Encrypt(user, owner, privileges,
  algorithm, usePdf20: true)` overload rejects `RC4x40` / `RC4x128` — and any
  legacy security handler (`usePdf20: false`) applied to a document that is
  already PDF 2.0
- **Converting to PDF 2.0 while an RC4 encryptor is pending** — rejected instead of
  producing a self-violating document
- **Signing a 2.0 document with a retired subfilter** — raw-RSA
  (`adbe.x509.rsa_sha1`) and enveloping PKCS#7 (`adbe.pkcs7.sha1`). Detached
  PKCS#7 (`adbe.pkcs7.detached`) stays legal and is what you should use.

`AESx256` is the encryption to pair with PDF 2.0.

## Encrypting a PDF

### Via `Document.Encrypt`

`doc.Encrypt(userPassword, ownerPassword, permissions, algorithm)` applies
encryption on the next save:

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

using var doc = new Document("input.pdf");

var permissions = DocumentPrivilege.AllowAll;
doc.Encrypt("user123", "owner456", permissions, CryptoAlgorithm.AESx256);

doc.Save("encrypted.pdf");
```

`permissions` defaults to all-allowed and `algorithm` to `AESx128`; an overload
takes the `Permissions` flags enum instead of `DocumentPrivilege`. A document
carrying a **certification (DocMDP) signature** refuses encryption with
`PdfException`, because encrypting would break the certification;
ordinary approval signatures are not affected.

`Document.EncryptMetadata` (default `true`) decides whether the XMP metadata
packet is encrypted with the rest of the file. Set it to `false` before the
save to leave the packet readable in the clear, so that indexers and
viewers can read the title, author and keywords without the password; the
`/EncryptMetadata` entry is written and honoured on the next open.

Two further handlers are available: `Encrypt(user, owner, privileges,
ICustomSecurityHandler)` installs your own `/Filter` implementation, and
`Encrypt(Permissions, CryptoAlgorithm, IList<X509Certificate2>)` writes a
public-key (certificate) security handler. A certificate-encrypted file is
opened with the `Document(path, CertificateEncryptionOptions)` constructors.

### Via the `PdfFileSecurity` facade

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var security = new PdfFileSecurity();

byte[] input = File.ReadAllBytes("input.pdf");
byte[] encrypted = security.EncryptFile(
    input,
    userPassword: "user123",
    ownerPassword: "owner456",
    permissions: DocumentPrivilege.AllowAll,
    algorithm: CryptoAlgorithm.AESx256);

File.WriteAllBytes("encrypted.pdf", encrypted);
```

The facade also has a bound-document form: construct it with an input/output
path or stream (or call `BindPdf`), then `EncryptFile(userPassword,
ownerPassword, privilege, keySize, cipher)`, `SetPrivilege`,
`DecryptFile(ownerPassword)` or `ChangePassword(...)`, and `Save`. Those
members return `bool`; set `AllowExceptions = true` to have failures throw
instead (the last error is in `LastException`).

### Document privileges

`DocumentPrivilege` wraps the PDF /P bit mask. Use the static helpers for the
common presets, or set individual flags:

```csharp
using Aspose.Pdf.Facades;

var allPerms = DocumentPrivilege.AllowAll;
var noPerms  = DocumentPrivilege.ForbidAll;

var custom = new DocumentPrivilege
{
    AllowPrint              = true,
    AllowCopy               = false,
    AllowModifyContents     = false,
    AllowModifyAnnotations  = true,
    AllowFillIn             = true,
    AllowScreenReaders      = true,
    AllowAssembly           = false,
    AllowDegradedPrinting   = true,
};
```

| Property                | Description                                  |
|-------------------------|----------------------------------------------|
| `AllowPrint`            | Print the document                           |
| `AllowModifyContents`   | Modify document contents                     |
| `AllowCopy`             | Copy / extract text and graphics             |
| `AllowModifyAnnotations`| Add or modify annotations                    |
| `AllowFillIn`           | Fill in form fields                          |
| `AllowScreenReaders`    | Extract text for accessibility               |
| `AllowAssembly`         | Insert, rotate, or delete pages              |
| `AllowDegradedPrinting` | Low-resolution printing only                 |

Single-permission presets (`DocumentPrivilege.Print`, `.Copy`, `.FillIn`, …)
exist for each flag, `ChangeAllowLevel` / `CopyAllowLevel` / `PrintAllowLevel`
give the graded view, and `Value` is the raw /P integer.

## Decrypting a PDF

### Open with a password

Pass the user or owner password to the `Document` constructor; the document is
decrypted in memory and saved back without encryption when re-emitted.

```csharp
using Aspose.Pdf;

using var doc = new Document("encrypted.pdf", "user123");
doc.Save("decrypted.pdf");
```

### Via the facade

```csharp
using Aspose.Pdf.Facades;

var security = new PdfFileSecurity();

byte[] encrypted = File.ReadAllBytes("encrypted.pdf");
byte[] decrypted = security.DecryptFile(encrypted, "owner456");

File.WriteAllBytes("decrypted.pdf", decrypted);
```

## Changing passwords

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var security = new PdfFileSecurity();

byte[] input = File.ReadAllBytes("encrypted.pdf");
byte[] result = security.ChangePasswords(
    input,
    oldOwnerPassword: "owner456",
    newUserPassword:  "newuser",
    newOwnerPassword: "newowner",
    algorithm: CryptoAlgorithm.AESx256);

File.WriteAllBytes("rekeyed.pdf", result);
```

## Inspecting encryption info

```csharp
using Aspose.Pdf;

using var doc = new Document("encrypted.pdf", "password");

var info = doc.EncryptionInfo;
if (info is not null)
{
    Console.WriteLine($"Algorithm:  {info.Algorithm}");
    Console.WriteLine($"Key length: {info.KeyLength} bits");
    Console.WriteLine($"V/R:        {info.Version}/{info.Revision}");
    Console.WriteLine($"Passwords:  user={info.HasUserPassword} owner={info.HasOwnerPassword}");
}
```

`EncryptionInfo` is `null` for an unencrypted document; `doc.CryptoAlgorithm`
is the nullable shortcut to `EncryptionInfo.Algorithm`.

## Digital signatures

### Signing

```csharp
using Aspose.Pdf.Security;

var cert = PdfCertificate.FromPfx("certificate.pfx", "certPassword");

byte[] input = File.ReadAllBytes("document.pdf");
byte[] signed = PdfSigner.Sign(input, cert, new SignatureOptions
{
    Reason      = "Approval",
    Location    = "New York",
    ContactInfo = "john@example.com",
    FieldName   = "Signature1",
});

File.WriteAllBytes("signed.pdf", signed);
```

Signing is an incremental update, so existing signatures stay valid. Further
`SignatureOptions`: `SubFilter` (default `adbe.pkcs7.detached`), `Digest`
(`DigestHashAlgorithm`, `Auto` picks by key type), `SigningDate`,
`SignerName`, `DocMdpPermissions` (a certification signature), `UseLtv`,
`TimestampUrl` / `TimestampBasicAuth` / `TimestampDigest` (RFC 3161
timestamp), `CustomSignHash` and `Password` (for an encrypted input).
`PdfSigner.SignDocumentTimestamp` adds a document-level timestamp signature.

`CustomSignHash` takes a `SignHash` delegate — `byte[] SignHash(byte[] hash,
DigestHashAlgorithm digestHashAlgorithm)` — for keys that never enter the
process (an HSM, a smartcard, a remote signing service): the signer passes it
the digest of the signed byte ranges, the delegate returns the raw signature
value, and the library wraps it in the CMS envelope around the certificate it
was given. On the `SignatureField` / `PdfFileSignature` path the same delegate is
set on `Signature.CustomSignHash`, and `ExternalSignature` (in
`Aspose.Pdf.Forms`) signs with an `X509Certificate2` from the OS store or a
device — or with a base64 public certificate (`ExternalSignature(base64, detached)`)
whose key is reached only through the delegate.

```csharp
using Aspose.Pdf.Forms;
using Aspose.Pdf.Security;

var signed = PdfSigner.Sign(input, cert, new SignatureOptions
{
    FieldName      = "Signature1",
    CustomSignHash = (hash, digest) => hsm.SignDigest(hash, digest),   // your signer
});
```

The `/Contents` placeholder is `ContentsSize` bytes (default 8192). A signature
that does not fit throws `InvalidOperationException`, or
`SignatureLengthMismatchException` when `AvoidEstimating` is set; raise
`ContentsSize` (long certificate chains, timestamps, LTV data) in either case.

### Signing with a visible appearance

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Security;

var cert = PdfCertificate.FromPfx("certificate.pfx", "certPassword");

byte[] input = File.ReadAllBytes("document.pdf");
byte[] signed = PdfSigner.SignWithAppearance(input, cert,
    new SignatureOptions
    {
        Reason   = "Approved",
        Location = "London",
    },
    new SignatureAppearance
    {
        SignerName = "Jane Smith",
        Reason     = "Document approved",
        Location   = "London",
        FontSize   = 10,
        Rect       = new Rectangle(100, 50, 300, 120),
        PageNumber = 1,
    });

File.WriteAllBytes("signed-visible.pdf", signed);
```

`SignatureAppearance` also takes `ContactInfo`, `SignDate`, `FontFamily`, and
`ImageBytes` (a raster image drawn in the signature box).

### Loading certificates

```csharp
using Aspose.Pdf.Security;

// From PFX path
var cert1 = PdfCertificate.FromPfx("cert.pfx", "password");

// From PFX bytes
byte[] pfx = File.ReadAllBytes("cert.pfx");
var cert2  = PdfCertificate.FromPfx(pfx, "password");

// From DER-encoded certificate + PKCS#8 private key bytes
byte[] certDer = File.ReadAllBytes("cert.der");
byte[] keyDer  = File.ReadAllBytes("key.der");
var cert3      = PdfCertificate.FromDerFiles(certDer, keyDer);

Console.WriteLine($"Subject: {cert1.SubjectName}");
Console.WriteLine($"Issuer:  {cert1.IssuerName}");
```

`SerialNumber` (bytes) is exposed as well.

### Verifying

```csharp
byte[] signedPdf = File.ReadAllBytes("signed.pdf");

// Verify every signature in the document
bool valid = PdfSigner.Verify(signedPdf);

// Verify a single signature field by name
bool fieldValid = PdfSigner.Verify(signedPdf, "Signature1");
```

Both overloads accept an optional `password` for an encrypted file. These
checks are cryptographic: byte range, digest and signature against the
embedded signer certificate. RSA, DSA and ECDSA signatures are verified; an
ECDSA signature on a NIST prime curve is checked in managed code, so it verifies
on runtimes without platform crypto too.

For a verdict without exceptions, `PdfFileSignature.TryVerifySignature(SignatureName,
out VerificationResult)` and `Signature.TryVerify(out VerificationResult)` report a
`VerificationState` (`Valid`, `Invalid` or `Undefined`), a `Message`, the captured
`VerificationException`, and `IsCompromised` when the signature's structure is a
recognised forgery.

**Compromise detection.** `SignaturesCompromiseDetector` (namespace `Aspose.Pdf`)
checks every signature of a document for the known structural attacks — a
universal signature forgery (an empty, hollow or fabricated `/Contents` or
`/ByteRange`) and a signature wrapping attack (unsigned bytes hidden in the
`/Contents` gap) — and measures whether the sound signatures cover the whole file.
`Check` returns `true` only when every signature is sound and together they cover
the document; the `CompromiseCheckResult` lists the `CompromisedSignatures` and
gives the `SignaturesCoverage` (`EntirelySigned`, `PartiallySigned` when content
was appended after the last signature, `Undefined` when there is nothing to
measure). `UnsignedContentAbsorber` (`Aspose.Pdf.Security`) then shows what that
appended content is: the pages, form widgets, other annotations and form
XObjects added or changed after signing.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Security;
using Aspose.Pdf.Signatures;

using var doc = new Document("signed.pdf");

if (!new SignaturesCompromiseDetector(doc).Check(out CompromiseCheckResult check))
{
    foreach (var forged in check.CompromisedSignatures)
        Console.WriteLine($"Forged: {forged.FullName}");

    if (check.SignaturesCoverage == SignaturesCoverage.PartiallySigned)
    {
        var unsigned = new UnsignedContentAbsorber(new PdfFileSignature(doc)).TryGetContent();
        Console.WriteLine($"Pages changed after signing: {unsigned.UnsignedContent.Pages.Count}");
    }
}
```

`PdfFileSignature.GetSignaturesInfo()` describes each signature's algorithm as a
`SignatureAlgorithmInfo` (`AlgorithmType`, `CryptographicStandard`,
`DigestHashAlgorithm`); a document timestamp comes back as a
`TimestampAlgorithmInfo` whose `ContentHashAlgorithm` is the digest of the
timestamp's message imprint.

Certificate **trust** is evaluated by the facade's
`VerifySignature(name, ValidationOptions, out ValidationResult)` when
`ValidationOptions.CheckCertificateChain` is set (the default). The chain is
built from the certificates embedded in the signature plus the host's
certificate stores — the machine-wide and the per-user intermediate CA stores,
and the machine-wide and per-user root stores. Nothing is downloaded, every
link is verified cryptographically, and validity is judged at the signing
time, so a signer certificate that expired after signing still validates.
Revocation checking (`ValidationMode.Strict` with OCSP / CRL) is not performed;
such a request yields `ValidationStatus.Unknown` rather than `Valid`.

### `PdfFileSignature` facade

Inspect signatures in detail and remove them:

```csharp
using Aspose.Pdf.Facades;

var sig = new PdfFileSignature("signed.pdf");

foreach (var name in sig.GetSignNames())
{
    Console.WriteLine($"{name}: valid={sig.VerifySignature(name)}, " +
                      $"covers whole={sig.IsCoversWholeDocument(name)}");
    Console.WriteLine($"  Signed at: {sig.GetDateTime(name)}");
}
```

`GetSignerName`, `GetReason`, `GetLocation`, `GetContactInfo`, `GetRevision`
and `GetTotalRevision` read the signature dictionaries; `IsContainSignature()`
and `IsCertified` summarise the document; `GetBlankSignNames()` lists unsigned
signature fields. `RemoveSignature(name)` clears a signature (pass
`removeField: true` to drop the field too), `RemoveSignatures()` clears them
all, and `Save(path)` / `Save(stream)` writes the result. `IsLtvEnabled`
reports whether long-term-validation data is present.
