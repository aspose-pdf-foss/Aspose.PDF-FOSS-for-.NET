# Metadata & XMP

A PDF carries metadata in two places:

- the **Document Information dictionary** (`Document.Info`) — the classic
  `/Info` entries (Title, Author, …) plus arbitrary custom keys;
- the **XMP packet** (`Document.Metadata`) — the modern, namespaced
  (`dc:`, `pdf:`, `xmp:`, …) metadata stream.

## Document Info dictionary

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

// Read
Console.WriteLine(doc.Info.Title);
Console.WriteLine(doc.Info.Author);
Console.WriteLine(doc.Info.CreationDate);   // DateTime

// Write
doc.Info.Title    = "Quarterly Report";
doc.Info.Author   = "Finance Team";
doc.Info.Subject  = "Q3 results";
doc.Info.Keywords = "finance, q3, report";
doc.Info.Creator  = "Aspose.PDF FOSS";
doc.Info.CreationDate = DateTime.UtcNow;

doc.Save("output.pdf");
```

Standard properties: `Title`, `Author`, `Subject`, `Keywords`, `Creator`,
`Producer`, `Trapped`, `CreationDate`, `ModDate` (the date properties are
`DateTime`, `DateTime.MinValue` when absent; `CreationTimeZone` / `ModTimeZone`
expose the offset as a `TimeSpan`).

Text values that fit PDFDocEncoding / Latin-1 are written as such; any other
text (`Œ`, CJK, …) is written as UTF-16BE with a byte-order mark, so it
round-trips unchanged.

> `ModDate` is auto-stamped to the current time on save unless you set it
> yourself. `Producer` is stamped with the library's own identity on every
> save unless you assign it explicitly (through `Info.Producer` or the XMP
> `pdf:Producer` property); `Creator` is defaulted the same way only when it is
> empty.

### Custom info properties

Any non-standard key is a custom entry, addressed through the indexer:

```csharp
doc.Info["Company"]    = "Contoso";
doc.Info["Department"] = "Research";

foreach (var key in doc.Info.Keys)
    Console.WriteLine($"{key} = {doc.Info[key]}");

doc.Info.ClearCustomData();   // drop custom keys, keep Title/Author/…
doc.Info.Clear();             // drop everything
```

`Info.ContainsKey(key)`, `Info.Remove(key)`, `Info.Count` and
`DocumentInfo.IsPredefinedKey(key)` round out the dictionary surface;
`SetCustom` / `GetCustom` / `Add` are aliases of the indexer.

## XMP metadata

`Document.Metadata` reads and writes XMP properties by their raw
`"prefix:name"` key. Values are `XmpValue` (constructible from `string`, `int`,
`double`, `DateTime`, or an `XmpValue[]` array):

```csharp
using Aspose.Pdf;

using var doc = new Document("input.pdf");

// Write standard XMP properties
doc.Metadata["dc:title"]    = new XmpValue("Quarterly Report");
doc.Metadata["dc:creator"]  = new XmpValue("Finance Team");
doc.Metadata["xmp:CreateDate"] = new XmpValue(DateTime.UtcNow);
doc.Metadata["pdf:Keywords"] = new XmpValue("finance, q3");

doc.Save("output.pdf");
```

An array value (or a structured `XmpValue`) is stored as nested RDF and comes
back as the same shape; scalars are stored as text and surface typed
(`IsInteger`, `IsDouble`, `IsDateTime`) on read.

Read safely (the indexer throws `KeyNotFoundException` for an absent key, so
probe first):

```csharp
if (doc.Metadata.TryGetValue("dc:title", out var title))
    Console.WriteLine(title);
```

`Metadata` implements `IDictionary<string, XmpValue>` (`Keys`, `Values`,
`Count`, `ContainsKey`, `Remove`, `Clear`, enumeration). `Document.HasMetadata`
reports whether the file carries a packet at all; `Metadata.PdfAidPart` /
`PdfAidConformance` read the PDF/A identification, and
`SetPdfAidPart(part, conformance)` writes it.

### Custom namespaces

Register a prefix → namespace-URI mapping before using a non-standard prefix:

```csharp
doc.Metadata.RegisterNamespaceUri("contoso", "http://contoso.com/ns/1.0/");
doc.Metadata["contoso:project"] = new XmpValue("Phoenix");
```

The common prefixes are registered out of the box: `rdf`, `xmp`, `dc`, `pdf`,
`xmpMM`, `xmpRights`, `pdfaid`, `pdfuaid` and `pdfe`.
`GetNamespaceUriByPrefix` / `GetPrefixByNamespaceUri` query the registry.

## Info vs XMP

Both stores can coexist; XMP is the ISO-standard mechanism and is what PDF/A
requires. The library keeps the two in step on save as follows:

- **XMP → Info fill.** When an `/Info` entry is missing or empty and the packet
  carries the equivalent property, the packet value is copied in:
  `dc:title` → `Title`, `dc:description` → `Subject`, `dc:creator` → `Author`,
  `pdf:Keywords` → `Keywords`, `xmp:CreatorTool` → `Creator`,
  `pdf:Producer` → `Producer`. An existing `/Info` value is never overwritten
  this way.
- **Dates.** When the packet is being rewritten anyway and already carries
  `xmp:ModifyDate`, it follows the freshly stamped `/ModDate`; a packet that was
  not touched is left byte-identical.
- **PDF 2.0 saves.** When the file is written as PDF 2.0 (a document created
  with `new Document(PdfVersion.v_2_0)`, or converted with
  `PdfFormat.v_2_0`), every documentary entry — `Title`, `Author`, `Subject`,
  `Keywords`, `Producer`, `Creator`, `CreationDate`, `ModDate` — is mirrored
  into the packet under the `xmp:` prefix, and the four descriptive text
  entries (`Title`, `Author`, `Subject`, `Keywords`) are then removed from
  `/Info`, as ISO 32000-2 deprecates them there. Dates and the producing
  application stay in `/Info`.

For any other value you want in both places (custom keys, `dc:` properties on
a 1.x file), set it in each store.

## What's next

- [Security and Encryption](security-and-encryption.md) — permissions and signing
- [Optimization](optimization.md) — PDF/A conversion (which relies on XMP)
- [Facades](facades.md#pdffileinfo) — `PdfFileInfo` for stream-based info editing
