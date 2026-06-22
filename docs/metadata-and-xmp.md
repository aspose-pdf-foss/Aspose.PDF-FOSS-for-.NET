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
`DateTime`; `CreationTimeZone` / `ModTimeZone` expose the offset).

> `ModDate` is auto-stamped to the current time on save unless you set it
> yourself.

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

Read safely (the indexer throws `KeyNotFoundException` for an absent key, so
probe first):

```csharp
if (doc.Metadata.TryGetValue("dc:title", out var title))
    Console.WriteLine(title);
```

### Custom namespaces

Register a prefix → namespace-URI mapping before using a non-standard prefix:

```csharp
doc.Metadata.RegisterNamespaceUri("contoso", "http://contoso.com/ns/1.0/");
doc.Metadata["contoso:project"] = new XmpValue("Phoenix");
```

The common prefixes (`dc`, `pdf`, `xmp`) are registered out of the box.

## Info vs XMP

Both stores can coexist; XMP is the ISO-standard mechanism and is what PDF/A
requires. When you need a value in both places, set it in each — the library
does not automatically mirror `Info` ⇄ XMP.

## What's next

- [Security and Encryption](security-and-encryption.md) — permissions and signing
- [Optimization](optimization.md) — PDF/A conversion (which relies on XMP)
- [Facades](facades.md#pdffileinfo) — `PdfFileInfo` for stream-based info editing
