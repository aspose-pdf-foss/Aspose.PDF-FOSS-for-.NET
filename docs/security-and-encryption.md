# Security and Encryption

## Encryption algorithms

`CryptoAlgorithm`:

| Algorithm  | Key size | PDF version | Notes                       |
|------------|----------|-------------|-----------------------------|
| `RC4x40`   | 40-bit   | PDF 1.1+    | Legacy, weak                |
| `RC4x128`  | 128-bit  | PDF 1.4+    | Legacy                      |
| `AESx128`  | 128-bit  | PDF 1.5+    | Recommended minimum         |
| `AESx256`  | 256-bit  | PDF 2.0     | Strongest available         |

All crypto primitives ship in pure managed C# — no native dependency on the
host OS crypto provider.

### PDF 2.0 deprecation gates

ISO 32000-2 retires the SHA-1-era mechanisms, and this library refuses them up
front rather than writing a file that violates its own declared version. These
throw `DeprecatedFeatureException`:

- **RC4 under the 2.0 encryption flag**, and any legacy security handler applied to
  a document that is already PDF 2.0
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
}
```

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

### Verifying

```csharp
byte[] signedPdf = File.ReadAllBytes("signed.pdf");

// Verify every signature in the document
bool valid = PdfSigner.Verify(signedPdf);

// Verify a single signature field by name
bool fieldValid = PdfSigner.Verify(signedPdf, "Signature1");
```

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
