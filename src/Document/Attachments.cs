using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>True while a conformance conversion is running. PDF/A requires every face
    /// to be embedded, which overrides a face whose own licence permits only preview and
    /// printing; an ordinary save still refuses such a face.</summary>
    internal bool EmbeddingLicenceOverridden { get; set; }

    /// <summary>
    /// Add an embedded file to the document.
    /// </summary>
    /// <summary>
    /// Whether an embedded file is also listed in the catalogue's <c>/AF</c>
    /// array as a file associated with the document.
    ///
    /// ⚠ FOSS-only: the reference writes no <c>/AF</c> from the attachment path
    /// (probed), so this is off by default and nothing that worked
    /// before changes.
    /// </summary>
    public bool AssociateEmbeddedFiles { get; set; }

    /// <summary>
    /// Whether an embedded file's <c>/Params</c> carries a <c>/CheckSum</c> of
    /// the bytes it embeds.
    ///
    /// ⚠ FOSS-only, and off by default, for the same reason: the reference writes
    /// none.
    /// </summary>
    public bool ChecksumEmbeddedFiles { get; set; }

    public void AddEmbeddedFile(string fileName, byte[]? fileData, string? description = null,
        string? mimeType = null, bool compress = true,
        DateTime? creationDate = null, DateTime? modDate = null)
        => AddEmbeddedFile(fileName, fileData, description, mimeType, compress,
            creationDate, modDate, AFRelationship.None);

    /// <summary>
    /// Add an embedded file that also declares what it is TO the document
    /// (/AFRelationship, PDF 2.0 §7.11.3). <see cref="AFRelationship.None"/>
    /// declares nothing, which is what a file specification says when its owner
    /// never set the property.
    /// </summary>
    internal void AddEmbeddedFile(string fileName, byte[]? fileData, string? description,
        string? mimeType, bool compress,
        DateTime? creationDate, DateTime? modDate, AFRelationship relationship)
        => AddEmbeddedFile(fileName, fileName, fileData, description, mimeType, compress,
            creationDate, modDate, relationship);

    /// <summary>
    /// Embed a file under a tree KEY that need not be the name the file declares.
    ///
    /// ⭐ Probed against the reference: the two are INDEPENDENT. A spec built
    /// as `named.txt` and added under key `the-key` is written as the key
    /// `the-key` with `/F` and `/UF` both `named.txt` — the key never reaches
    /// either entry, and two files of the same name under two keys both survive.
    /// </summary>
    internal void AddEmbeddedFile(string key, string fileName, byte[]? fileData,
        string? description, string? mimeType, bool compress,
        DateTime? creationDate, DateTime? modDate, AFRelationship relationship)
    {
        var fsDict = new PdfDictionary();
        fsDict.Set("Type", new PdfName("Filespec"));
        // The relationship is a property of the SPECIFICATION, so it is written
        // whether or not the bytes travel with it.
        if (relationship != AFRelationship.None)
            fsDict.Set("AFRelationship", new PdfName(relationship.ToString()));
        // /F is a byte string and Latin-1 spells nearly every name; one it cannot spell is
        // written as a text string rather than as a row of question marks. /UF is always the
        // Unicode spelling.
        fsDict.Set("F", FileSpecification.FileNameEntry(fileName));
        fsDict.Set("UF", Forms.Field.EncodePdfTextString(fileName));
        if (description is not null)
            fsDict.Set("Desc", Forms.Field.EncodePdfTextString(description));

        // A null payload registers a reference-only file specification — an external
        // file reference (/F) with no embedded /EF stream, e.g. a path that does not
        // resolve to a local file. A non-null payload embeds the bytes as an /EF stream.
        WriteEmbeddedFileStream(fsDict, fileData, mimeType, creationDate, modDate, compress);

        // Register as new object
        var fsObjNum = AllocateObjectNumber();
        AddNewObject(fsObjNum, fsDict);

        // A document that ASSOCIATES its embedded files also lists each
        // specification in the catalogue's /AF (PDF 2.0 §14.13), which is how a
        // reader finds a file that belongs to the document as a whole rather
        // than to a page. The reference writes no /AF from this path, so a caller
        // asks for it.
        if (AssociateEmbeddedFiles)
        {
            var afArray = _reader.Resolve(_reader.Catalog.Get("AF")) as PdfArray ?? new PdfArray();
            afArray.Add(new PdfIndirectRef(fsObjNum, 0));
            _reader.Catalog.Set("AF", afArray);
        }

        // Get or create /Names dict in catalog
        var namesDict = _reader.ResolveDict(_reader.Catalog.Get("Names"));
        if (namesDict is null)
        {
            namesDict = new PdfDictionary();
            _reader.Catalog.Set("Names", namesDict);
        }

        // Get or create /EmbeddedFiles name tree
        var efTree = _reader.ResolveDict(namesDict.Get("EmbeddedFiles"));
        PdfArray numsArray;
        if (efTree is not null)
        {
            numsArray = _reader.Resolve(efTree.Get("Names")) as PdfArray ?? new PdfArray();
        }
        else
        {
            efTree = new PdfDictionary();
            namesDict.Set("EmbeddedFiles", efTree);
            numsArray = new PdfArray();
        }

        // PDF name trees require lexical ordering on the Names array (PDF 32000-1 §7.9.6).
        // Find the first existing key that compares > fileName and insert before it; if none,
        // append. Reading and 1-based indexing then match the alphabetical order callers
        // expect from /Names/EmbeddedFiles.
        var insertAt = numsArray.Count;
        for (var i = 0; i + 1 < numsArray.Count; i += 2)
        {
            if (_reader.Resolve(numsArray[i]) is not PdfString s) continue;
            if (string.CompareOrdinal(s.ToText(), key) <= 0) continue;
            insertAt = i;
            break;
        }
        // The name-tree key names the same file, and loses the same characters if written
        // as Latin-1 bytes it has no room for.
        numsArray.Insert(insertAt, FileSpecification.FileNameEntry(key));
        numsArray.Insert(insertAt + 1, new PdfIndirectRef(fsObjNum, 0));
        efTree.Set("Names", numsArray);
    }
}
