# Facades

Facades expose task-oriented APIs that complement the `Document` model. They
are particularly handy for byte-array workflows and for the operations that
the legacy Aspose.PDF API surface has historically grouped together (file
editing, signing, form filling, content patching, ...).

## `PdfFileEditor`

Merge, split, extract, insert, and delete pages.

### Concatenate (merge) PDFs

```csharp
using Aspose.Pdf.Facades;

var editor = new PdfFileEditor();

byte[] a = File.ReadAllBytes("doc1.pdf");
byte[] b = File.ReadAllBytes("doc2.pdf");
byte[] c = File.ReadAllBytes("doc3.pdf");

byte[] merged = editor.Concatenate(a, b, c);
File.WriteAllBytes("merged.pdf", merged);

// File-path variant
editor.Concatenate(
    new[] { "doc1.pdf", "doc2.pdf", "doc3.pdf" },
    "merged.pdf");
```

### Extract pages

Page numbers are 1-based; ranges are inclusive.

```csharp
var editor = new PdfFileEditor();
byte[] input = File.ReadAllBytes("document.pdf");

// Pages 3..7
byte[] range = editor.Extract(input, startPage: 3, endPage: 7);

// Explicit page set
byte[] picked = editor.Extract(input, new[] { 1, 5, 10 });

// First N pages
byte[] firstThree = editor.SplitFromFirst(input, pageCount: 3);

// From a page to the end
byte[] tail = editor.SplitToEnd(input, startPage: 5);
```

### Split into individual pages

```csharp
var editor = new PdfFileEditor();
byte[] input = File.ReadAllBytes("document.pdf");

byte[][] perPage = editor.Split(input);
for (int i = 0; i < perPage.Length; i++)
    File.WriteAllBytes($"page_{i + 1}.pdf", perPage[i]);

// Or by custom page ranges
byte[][] chunks = editor.SplitToBulks(input, new[]
{
    new[] { 1, 3 },
    new[] { 4, 6 },
    new[] { 7, 10 },
});
```

### Delete pages

```csharp
byte[] result = editor.Delete(input, 2, 4);
File.WriteAllBytes("trimmed.pdf", result);
```

### Insert pages

```csharp
byte[] main   = File.ReadAllBytes("main.pdf");
byte[] insert = File.ReadAllBytes("insert.pdf");

// Insert pages 1..3 of insert.pdf after page 2 of main.pdf
byte[] result = editor.Insert(main, insertLocation: 2, insert, startPage: 1, endPage: 3);
```

### Append pages

```csharp
byte[] dest   = File.ReadAllBytes("destination.pdf");
byte[] source = File.ReadAllBytes("source.pdf");

byte[] result = editor.Append(dest, source, startPage: 2, endPage: 5);
```

### Booklet layout

```csharp
byte[] input = File.ReadAllBytes("document.pdf");
byte[] booklet = editor.MakeBooklet(input);
File.WriteAllBytes("booklet.pdf", booklet);
```

### Resize page contents

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var editor = new PdfFileEditor();

using var doc = new Document("document.pdf");

var parameters = new PdfFileEditor.ContentsResizeParameters
{
    LeftMargin   = PdfFileEditor.ContentsResizeValue.Units(50),
    RightMargin  = PdfFileEditor.ContentsResizeValue.Units(50),
    TopMargin    = PdfFileEditor.ContentsResizeValue.Units(50),
    BottomMargin = PdfFileEditor.ContentsResizeValue.Units(50),
};

editor.ResizeContents(doc, parameters);
doc.Save("resized.pdf");

// Resize specific pages only
editor.ResizeContents(doc, new[] { 1, 3 }, parameters);
```

## `PdfFileSecurity`

Encrypt, decrypt, and change passwords. See
[Security and Encryption](security-and-encryption.md) for end-to-end coverage.

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var security = new PdfFileSecurity();

byte[] input     = File.ReadAllBytes("input.pdf");
byte[] encrypted = security.EncryptFile(input, "user", "owner",
    DocumentPrivilege.AllowAll, CryptoAlgorithm.AESx256);

byte[] decrypted = security.DecryptFile(encrypted, "owner");

byte[] rekeyed   = security.ChangePasswords(encrypted, "owner", "newUser", "newOwner");
```

## `PdfFileSignature`

Sign and verify PDF documents. The facade is instance-based; bind it to a file
or document first.

```csharp
using Aspose.Pdf.Facades;

var sig = new PdfFileSignature("document.pdf");

foreach (var name in sig.GetSignNames())
{
    Console.WriteLine($"{name}: valid={sig.VerifySignature(name)}, " +
                      $"covers whole={sig.IsCoversWholeDocument(name)}");
    Console.WriteLine($"  Signed at: {sig.GetDateTime(name)}");
}
```

For PKCS#7 signing flow, see
[Security and Encryption](security-and-encryption.md).

## `FormEditor`

Fill, flatten, and manage AcroForm fields. See
[Working with Forms](working-with-forms.md) for the full surface.

```csharp
using Aspose.Pdf.Facades;

var editor = new FormEditor();

// Fill multiple fields from a dictionary
byte[] filled = editor.FillFields(input, new Dictionary<string, string>
{
    ["name"]  = "John Doe",
    ["email"] = "john@example.com",
});

// Flatten the form to static content
byte[] flat = editor.FlattenForm(input);

// Inspect fields
string[]            names = editor.GetFieldNames(input);
string?             value = editor.GetFieldValue(input, "name");
Aspose.Pdf.Forms.FieldType? type = editor.GetFieldType(input, "name");

// Round-trip form data through a name->value dictionary
var data         = editor.ExportFormData(input);
byte[] imported  = editor.ImportFormData(input, data);
```

## `PdfBookmarkEditor`

Create and modify bookmarks (PDF outlines).

```csharp
using Aspose.Pdf.Facades;

var editor = new PdfBookmarkEditor();
editor.BindPdf("document.pdf");

editor.CreateBookmarkOfPage("Introduction", pageNumber: 1);

editor.CreateBookmarkOfPage(
    new[] { "Chapter 1", "Chapter 2", "Chapter 3" },
    new[] {           1,          15,          30 });

editor.Save("bookmarked.pdf");
```

## `PdfContentEditor`

Patch page content, replace text, manage stamps, and add annotations / links
on bytes:

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var editor = new PdfContentEditor();
editor.BindPdf("document.pdf");

// Replace text on every page
editor.ReplaceText("old text", "new text");

// Region-scoped replace: when a search rectangle is set, only matches whose text
// falls inside it are replaced; matches elsewhere are left untouched.
editor.TextSearchOptions.Rectangle = new Rectangle(0, 0, 100, 200);
editor.ReplaceText("Draft", 1, "Final");   // page 1, inside the rectangle only

// Inspect / change viewer preferences (bitmask)
int prefs = editor.GetViewerPreference();
editor.ChangeViewerPreference(0x00000040);   // hide toolbar

// Inspect stamps on a page (1-based)
StampInfo[] stamps = editor.GetStamps(pageNumber: 1);

editor.Save("edited.pdf");
```

### Byte-array helpers

The same instance also offers byte-array overloads:

```csharp
var editor = new PdfContentEditor();

byte[] input = File.ReadAllBytes("document.pdf");

byte[] result = editor.ReplaceText(input, "search", "replace");

result = editor.CreateFreeText(result,
    new Rectangle(100, 700, 300, 730),
    pageNumber: 1,
    text:       "Added note",
    fontName:   "Helvetica",
    fontSize:   12);

result = editor.CreateWebLink(result,
    new Rectangle(100, 650, 300, 670),
    pageNumber: 1,
    url:        "https://example.com");
```

## `PdfAnnotationEditor`

Bulk-manage annotations. See [Working with Annotations](working-with-annotations.md).

```csharp
using Aspose.Pdf;
using Aspose.Pdf.Facades;

var editor = new PdfAnnotationEditor();
editor.BindPdf("annotated.pdf");

editor.DeleteAnnotations();              // every annotation
editor.DeleteAnnotations("Highlight");   // by subtype
editor.FlatteningAnnotations();          // bake into page content

editor.RedactArea(
    pageIndex: 1,
    new Rectangle(100, 400, 300, 420),
    color: new double[] { 0, 0, 0 });

editor.Save("edited.pdf");
```

## `PdfPageEditor`

Page-level edits: rotate pages, override page sizes, and so on.

```csharp
using Aspose.Pdf.Facades;

var editor = new PdfPageEditor();
editor.BindPdf("document.pdf");

byte[] rotated = editor.RotatePages(
    File.ReadAllBytes("document.pdf"),
    rotation: 90,
    1, 3);
```

## `PdfFileInfo`

Read and update document metadata:

```csharp
using Aspose.Pdf.Facades;

var info = new PdfFileInfo();
info.BindPdf("document.pdf");

Dictionary<string, string?> meta = info.GetDocumentInfo();
foreach (var (k, v) in meta)
    Console.WriteLine($"{k} = {v}");

Console.WriteLine($"Encrypted: {info.IsEncrypted}");
```

## `PdfFileMend`

Append plain text or images to existing pages without disturbing other
content:

```csharp
using Aspose.Pdf.Facades;

var mend = new PdfFileMend("input.pdf", "output.pdf");

mend.AddText(
    new FormattedText("Page note"),
    pageNum: 1,
    lowerLeftX: 100f, lowerLeftY: 100f);

mend.AddImage("logo.png",
    pageNum: 1,
    lowerLeftX:  50f, lowerLeftY:  50f,
    upperRightX: 150f, upperRightY: 150f);

mend.Save("output.pdf");
```

## `PdfFileStamp`

Add header / footer / page-number stamps:

```csharp
using Aspose.Pdf.Facades;

var stamper = new PdfFileStamp();
stamper.BindPdf("input.pdf");

stamper.AddHeader(new FormattedText("Confidential"), topMargin: 36f);
stamper.AddFooter(new FormattedText("Internal"),     bottomMargin: 36f);
stamper.AddPageNumber(new FormattedText("Page #"));

stamper.Save("stamped.pdf");
```

## `PdfJavaScriptStripper`

Remove every JavaScript action from a PDF:

```csharp
using Aspose.Pdf.Facades;

var stripper = new PdfJavaScriptStripper();
stripper.Strip("input.pdf", "stripped.pdf");
```

## `PdfConverter`

`PdfConverter` is a small wrapper around `Document.Convert(...)` for batch
PDF/A normalization runs. See [Optimization](optimization.md) for direct
`Document.Convert` usage.
