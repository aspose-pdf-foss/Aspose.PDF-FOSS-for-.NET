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

public sealed partial class Document : IDisposable
{
    /// <summary>
    /// Serialize the document into a fresh byte array.
    /// </summary>
    public byte[] ToArray()
    {
        FireBeforePageGenerateEvents();

        if (_sourceStream is not null && _sourceStream.CanWrite)
        {
            SaveIncremental(_sourceStream);
            _sourceStream.Seek(0, SeekOrigin.Begin);
            using var copy = new MemoryStream();
            _sourceStream.CopyTo(copy);
            return copy.ToArray();
        }

        using var ms = new MemoryStream();
        Save(ms);
        return ms.ToArray();
    }

    // ── Background / Actions / FontSubstitution / CustomSecurityHandler

    /// <summary>Document-wide background colour painted on every page
    /// before content during Save. Real — emits a content-stream prologue
    /// that fills the MediaBox with the configured colour.</summary>
    public Color? Background { get; set; }

    private Annotations.DocumentActionCollection? _actions;

    /// <summary>Catalog /AA additional-action dictionary. Real — slots
    /// configured via the returned collection are written to the catalog
    /// /AA dict during Save.</summary>
    public Annotations.DocumentActionCollection Actions
        => _actions ??= new Annotations.DocumentActionCollection(this);

    /// <summary>Delegate fired when the renderer fails to resolve a font
    /// referenced by a content stream. <paramref name="originalFont"/> is
    /// the font that couldn't be loaded; <paramref name="newFont"/> is the
    /// substitute the renderer fell back to. Real wiring lives in the
    /// FontResolver path — when this event has subscribers, the resolver
    /// invokes them before completing the substitution.</summary>
    public delegate void FontSubstitutionHandler(Text.Font oldFont, Text.Font newFont);

    /// <summary>Fired by the font-loading pipeline when a referenced font
    /// must be substituted. Real — the renderer raises this through
    /// <see cref="RaiseFontSubstitution"/>.</summary>
    public event FontSubstitutionHandler? FontSubstitution;

    /// <summary>Internal hook invoked by the font-loading pipeline.</summary>
    internal void RaiseFontSubstitution(Text.Font original, Text.Font replacement)
        => FontSubstitution?.Invoke(original, replacement);

    /// <summary>Stored custom security handler when set via the
    /// ICustomSecurityHandler Encrypt overloads. Get-only.</summary>
    public Security.ICustomSecurityHandler? CustomSecurityHandler { get; private set; }

    /// <summary>Encrypt with a custom security handler, which supplies the /O and
    /// /U values, the file key and the per-object cipher in place of the Standard
    /// handler. Like the Standard overloads this only arms the encryptor — the
    /// /Encrypt dictionary is written at Save.</summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Facades.DocumentPrivilege privileges,
        Security.ICustomSecurityHandler customHandler)
        // The reserved-bit layout of Table 22 belongs to the Standard handler; an
        // alternative /Filter defines its own permission encoding, so the privilege
        // value travels to /P unchanged and reads back identically.
        => EncryptWithCustomHandler(userPassword, ownerPassword,
            privileges?.Value ?? -1, customHandler);

    /// <summary>Same — Permissions-typed overload.</summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions,
        Security.ICustomSecurityHandler customHandler)
        => EncryptWithCustomHandler(userPassword, ownerPassword,
            (int)permissions, customHandler);

    private void EncryptWithCustomHandler(string userPassword, string ownerPassword,
        int permissions, Security.ICustomSecurityHandler customHandler)
    {
        if (customHandler is null) throw new System.ArgumentNullException(nameof(customHandler));
        if (HasCertificationSignature())
            throw new System.InvalidOperationException(
                "Cannot encrypt a document carrying a certification signature.");
        CustomSecurityHandler = customHandler;
        _encryptor = Security.PdfEncryptor.CreateWithCustomHandler(
            customHandler, userPassword, ownerPassword, permissions);
    }

    /// <summary>Encrypt with a list of recipient public certificates
    /// (Public-Key /Filter security handler). Not implemented; throws
    /// NotSupportedException to make the gap explicit.</summary>
    public void Encrypt(Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm,
        System.Collections.Generic.IList<System.Security.Cryptography.X509Certificates.X509Certificate2> publicCertificates)
    {
        // Public-key (certificate) security handler — Adobe.PPKLite. The file key is
        // enveloped to each recipient certificate; object encryption reuses the
        // Standard handler's RC4/AES pass through the built encryptor.
        _encryptor = Security.PubSecHandler.CreateEncryptor(permissions, cryptoAlgorithm, publicCertificates);
    }

    /// <summary>
    /// Where image sidecars (page SVGs/PNGs and raster images) go:
    /// <see cref="HtmlSaveOptions.SpecialFolderForAllImages"/> when set, else the
    /// regular <c>&lt;stem&gt;_files</c> folder. Returns the folder plus the URL
    /// prefix that references it from the HTML's directory ("" when they coincide).
    /// </summary>
    private static (string ImagesDir, string ImagesUrl) ResolveImagesFolder(
        HtmlSaveOptions options, string htmlDir, string filesDir, string filesUrl)
    {
        if (string.IsNullOrEmpty(options.SpecialFolderForAllImages))
            return (filesDir, filesUrl);
        var imagesDir = Path.GetFullPath(options.SpecialFolderForAllImages);
        var rel = Path.GetRelativePath(string.IsNullOrEmpty(htmlDir) ? "." : htmlDir, imagesDir)
            .Replace(Path.DirectorySeparatorChar, '/');
        return (imagesDir, rel == "." ? "" : rel);
    }

    /// <summary>
    /// Register a structure tree builder for auto-finalization on save.
    /// </summary>
    internal void RegisterStructureTreeBuilder(StructureTreeBuilder builder)
    {
        _structureTreeBuilder = builder;
    }

    /// <summary>
    /// Register an outline builder for auto-finalization on save.
    /// </summary>
    internal void RegisterOutlineBuilder(OutlineBuilder builder)
    {
        _outlineBuilder = builder;
    }

    /// <summary>Materialise any pending <see cref="OutlineBuilder"/> into the catalog
    /// /Outlines tree now, instead of deferring to Save. Needed so a read path that
    /// inspects the live outline tree (e.g. chaining the document into a second
    /// <c>PdfBookmarkEditor</c> and calling <c>ExtractBookmarks</c>) sees bookmarks
    /// that were just added via <c>CreateBookmarkOfPage</c>. Idempotent — the builder
    /// is consumed once, and the cached outline collection is dropped so the next
    /// access re-reads the freshly written tree.</summary>
    internal void FlushPendingOutlineBuilder()
    {
        if (_outlineBuilder is null) return;
        _outlineBuilder.Build();
        _outlineBuilder = null;
        _outlines = null;
    }

    /// <summary>
    /// Register a page label builder for auto-finalization on save.
    /// </summary>
    internal void RegisterPageLabelBuilder(PageLabelBuilder builder)
    {
        _pageLabelBuilder = builder;
    }

    private static byte[] ReadStreamToBytes(Stream stream)
    {
        if (stream.CanSeek && stream.Position != 0)
            stream.Position = 0;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] CreateEmptyPdf()
    {
        using var ms = new MemoryStream();
        var writer = new PdfWriter(ms);
        // Newly-created documents default to PDF 1.7 (matches the modern baseline
        // and the PdfFormat default). Loaded documents keep their own header version.
        writer.WriteHeader("1.7");

        // Catalog
        var catalogDict = new PdfDictionary();
        catalogDict.Set("Type", new PdfName("Catalog"));
        catalogDict.Set("Pages", new PdfIndirectRef(2, 0));
        writer.WriteIndirectObject(1, catalogDict);

        // Pages (empty — no kids)
        var pagesDict = new PdfDictionary();
        pagesDict.Set("Type", new PdfName("Pages"));
        pagesDict.Set("Kids", new PdfArray());
        pagesDict.Set("Count", new PdfInteger(0));
        writer.WriteIndirectObject(2, pagesDict);

        var trailer = new PdfDictionary();
        trailer.Set("Root", new PdfIndirectRef(1, 0));
        writer.WriteXRefAndTrailer(trailer);

        return ms.ToArray();
    }

    // ── Internal write infrastructure ────────────────────────────────────────

    private readonly List<(int objNum, PdfObject obj)> _newObjects = [];

    /// <summary>Images built by BindXml; their File paths are validated during Save.</summary>
    internal List<Image>? PendingXmlImages { get; set; }

    /// <summary>Default page-tree branching factor (PDF table 30 /Count vs /Kids ratio).</summary>
    public const byte DefaultNodesNumInSubtrees = 10;

    // ── Stored-only flag props (public-API compatibility; no behaviour) ──

    /// <summary>Whether the saver may reuse identical page-content streams. Stored only.</summary>
    public bool AllowReusePageContent { get; set; }

    /// <summary>Whether the document emits notification log entries. Stored only.</summary>
    public bool EnableNotificationLogging { get; set; }

    /// <summary>Whether signature fields fire change-handlers when their dict mutates. Stored only.</summary>
    public bool HandleSignatureChange { get; set; }

    /// <summary>Viewer preference: hide the menu bar (/ViewerPreferences /HideMenubar).</summary>
    public bool HideMenubar
    {
        get => GetViewerPrefBool("HideMenubar");
        set => SetViewerPrefBool("HideMenubar", value);
    }

    /// <summary>Viewer preference: hide the toolbar (/ViewerPreferences /HideToolbar).</summary>
    public bool HideToolBar
    {
        get => GetViewerPrefBool("HideToolbar");
        set => SetViewerPrefBool("HideToolbar", value);
    }

    /// <summary>Viewer preference: hide window UI chrome (/ViewerPreferences /HideWindowUI).</summary>
    public bool HideWindowUI
    {
        get => GetViewerPrefBool("HideWindowUI");
        set => SetViewerPrefBool("HideWindowUI", value);
    }

    /// <summary>Allow gaps in the xref table during parse. Stored only.</summary>
    public bool IsXrefGapsAllowed { get; set; }

    /// <summary>Viewer preference: pick the paper tray by PDF page size
    /// (/ViewerPreferences /PickTrayByPDFSize).</summary>
    public bool PickTrayByPdfSize
    {
        get => GetViewerPrefBool("PickTrayByPDFSize");
        set => SetViewerPrefBool("PickTrayByPDFSize", value);
    }

    /// <summary>Maximum file size (bytes) loaded entirely into memory. Stored only.</summary>
    public int FileSizeLimitToMemoryLoading { get; set; } = int.MaxValue;

    /// <summary>Reset <see cref="FileSizeLimitToMemoryLoading"/> to its built-in default.</summary>
    public void SetDefaultFileSizeLimitToMemoryLoading() => FileSizeLimitToMemoryLoading = int.MaxValue;

    /// <summary>Update the /Info /Title entry.</summary>
    public void SetTitle(string title) { if (Info is { } info) info.Title = title; }

    /// <summary>Rebuild the page tree so each subtree has <paramref name="nodesNumInSubtrees"/> children. Stored only.</summary>
    public void PageNodesToBalancedTree(byte nodesNumInSubtrees) { _ = nodesNumInSubtrees; }

    /// <summary>Remove all metadata. Stored only — clears /Info and /Metadata in a future change.</summary>
    public void RemoveMetadata() { }

    /// <summary>Remove PDF/UA compliance markers. Stored only.</summary>
    public void RemovePdfUaCompliance() { }

    /// <summary>Flatten transparency to opaque graphics. Stored only — no-op in FOSS.</summary>
    public void FlattenTransparency() { }

    /// <summary>
    /// Give each of the passed pages a private copy of the image XObjects they
    /// share, so an in-place edit reached through one page (e.g. a redaction
    /// erasing image regions) no longer alters the others. Shared /Resources
    /// and /XObject dictionaries are unshared per page as well; the first page
    /// that uses an object keeps the original.
    /// </summary>
    public void SplitSharedImages(params Page[] pages)
    {
        if (pages is null || pages.Length < 2) return;

        var seenRefs = new HashSet<int>();
        var seenStreams = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        var seenResources = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var seenXobjDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var page in pages)
        {
            if (page is null) continue;
            var rd = page.Reader;
            var resources = rd.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null) continue;
            if (!seenResources.Add(resources))
            {
                var resClone = new PdfDictionary();
                foreach (var k in resources.Keys)
                    if (resources.Get(k) is { } rv) resClone.Set(k, rv);
                page.Dict.Set("Resources", resClone);
                resources = resClone;
            }

            var xobjs = rd.ResolveDict(resources.Get("XObject"));
            if (xobjs is null) continue;
            if (!seenXobjDicts.Add(xobjs))
            {
                var xClone = new PdfDictionary();
                foreach (var k in xobjs.Keys)
                    if (xobjs.Get(k) is { } xv) xClone.Set(k, xv);
                resources.Set("XObject", xClone);
                xobjs = xClone;
            }

            foreach (var name in xobjs.Keys.ToList())
            {
                var entry = xobjs.Get(name);
                var stream = rd.ResolveStream(entry);
                if (stream is null || stream.Dict.GetName("Subtype") != "Image") continue;

                var sharedByRef = entry is PdfIndirectRef ir && !seenRefs.Add(ir.ObjectNumber);
                var sharedByInstance = !seenStreams.Add(stream);
                if (!sharedByRef && !sharedByInstance) continue;

                var dictClone = new PdfDictionary();
                foreach (var k in stream.Dict.Keys)
                    if (stream.Dict.Get(k) is { } sv) dictClone.Set(k, sv);
                var dataClone = new byte[stream.RawData.Length];
                stream.RawData.CopyTo(dataClone, 0);
                var clone = new PdfStream(dictClone, dataClone);

                var objNum = AllocateObjectNumber();
                AddNewObject(objNum, clone, registerOverlay: true);
                xobjs.Set(name, new PdfIndirectRef(objNum, 0));
            }
        }
    }

    /// <summary>Apply the requested repair pass. Stored only.</summary>
    public void Repair(RepairOptions options) { _ = options; }

    /// <summary>Export every annotation in the document to an XFDF stream.</summary>
    public void ExportAnnotationsToXfdf(Stream stream)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ExportAnnotationsToXfdf(stream);
    }

    /// <summary>Export every annotation in the document to an XFDF file.</summary>
    public void ExportAnnotationsToXfdf(string fileName)
    {
        using var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write);
        ExportAnnotationsToXfdf(fs);
    }

    /// <summary>Import annotations from an XFDF stream into the document.</summary>
    public void ImportAnnotationsFromXfdf(Stream stream)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ImportAnnotationsFromXfdf(stream);
    }

    /// <summary>Import annotations from an XFDF file into the document.</summary>
    public void ImportAnnotationsFromXfdf(string fileName)
    {
        new Aspose.Pdf.Facades.PdfAnnotationEditor(this).ImportAnnotationsFromXfdf(fileName);
    }

    /// <summary>Raw XMP packet supplied via <see cref="SetXmpMetadata(Stream)"/>;
    /// written verbatim as the /Metadata stream on save (bypasses the property model
    /// so arbitrary XMP byte content round-trips exactly).</summary>
    private byte[]? _rawXmpOverride;

    /// <summary>Convert one page to a PNG memory stream.</summary>
    public MemoryStream ConvertPageToPNGMemoryStream(Page page)
    {
        var ms = new MemoryStream();
        new Aspose.Pdf.Devices.PngDevice().Process(page, ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Write the document's generator DOM as Aspose.Pdf template XML — the
    /// counterpart of <see cref="BindXml(string)"/>, so a document can be saved and read
    /// back.</summary>
    /// <remarks>This used to accept the path and write nothing, which is the worst
    /// shape for a save method: the caller is told the file was written and finds it
    /// missing, or reads back a stale one.</remarks>
    public void SaveXml(string file)
    {
        using var stream = File.Create(file);
        XmlSerialization.Save(this, stream);
    }

    /// <summary>Write the generator DOM as template XML to a stream.</summary>
    public void SaveXml(Stream stream) => XmlSerialization.Save(this, stream);

    /// <summary>Validate / repair the document. Always returns true (FOSS doesn't run validation).</summary>
    public bool Check(bool doRepair) { _ = doRepair; return true; }

    /// <summary>
    /// Validate the document's page content streams and write an XML report of any
    /// problems to <paramref name="output"/>. Currently checks that each page's
    /// graphics-state operators are balanced (every <c>q</c> has a matching
    /// <c>Q</c>); an unbalanced page leaves a residual transform that corrupts
    /// later content. When <paramref name="doRepair"/> is set the imbalance is
    /// corrected in place. Returns true when the document is valid (no problems).
    /// </summary>
    public bool Check(bool doRepair, System.IO.Stream output)
    {
        var issues = new List<string>();
        foreach (Page page in Pages)
        {
            int depth = 0;
            try
            {
                foreach (var op in page.Contents)
                {
                    if (op is Aspose.Pdf.Operators.GSave) depth++;
                    else if (op is Aspose.Pdf.Operators.GRestore) depth--;
                }
            }
            catch { depth = 0; }

            if (depth != 0)
            {
                issues.Add($"Page {page.Number}: unbalanced graphics-state operators "
                    + $"(q/Q), net depth {depth}.");
                if (doRepair && depth > 0)
                    page.AddContentStream(System.Text.Encoding.ASCII.GetBytes(
                        string.Concat(Enumerable.Repeat("Q\n", depth))));
                else if (doRepair && depth < 0)
                    page.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(
                        string.Concat(Enumerable.Repeat("q\n", -depth))));
            }
        }

        if (output is not null)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<report>\n");
            foreach (var issue in issues)
                sb.Append("  <item>\n    <description>")
                  .Append(System.Security.SecurityElement.Escape(issue))
                  .Append("</description>\n  </item>\n");
            sb.Append("</report>\n");
            var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            output.Write(bytes, 0, bytes.Length);
            output.Flush();
        }
        return issues.Count == 0;
    }

    /// <summary>Async file load wrapper (currently synchronous).</summary>
    public void LoadFrom(string filename, LoadOptions options)
    {
        _ = options;
        if (string.IsNullOrEmpty(filename)) return;
        FileName = filename;
    }

    /// <summary>Single-arg ctor whose parameter keeps the established name <paramref name="filename"/>.</summary>
    public Document(string filename, bool isManagedStream) : this(filename) { _ = isManagedStream; }

    /// <summary>Stream ctor with managed-stream flag.</summary>
    public Document(Stream input, bool isManagedStream) : this(input) { _ = isManagedStream; }

    /// <summary>Stream ctor with password + managed-stream flag.</summary>
    public Document(Stream input, string password, bool isManagedStream) : this(input, password) { _ = isManagedStream; }

    /// <summary>File ctor with password + managed-stream flag.</summary>
    public Document(string filename, string password, bool isManagedStream) : this(filename, password) { _ = isManagedStream; }

    /// <summary>Stream ctor with password + ICustomSecurityHandler.</summary>
    public Document(Stream input, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(ReadAll(input), password, customSecurityHandler) { }

    /// <summary>Stream ctor with password + managed-stream + ICustomSecurityHandler.</summary>
    public Document(Stream input, string password, bool isManagedStream, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(ReadAll(input), password, customSecurityHandler) { _ = isManagedStream; }

    /// <summary>File ctor with password + ICustomSecurityHandler.</summary>
    public Document(string filename, string password, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(File.ReadAllBytes(filename), password, customSecurityHandler) { }

    /// <summary>File ctor with password + managed-stream + ICustomSecurityHandler.</summary>
    public Document(string filename, string password, bool isManagedStream, Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
        : this(File.ReadAllBytes(filename), password, customSecurityHandler) { _ = isManagedStream; }

    /// <summary>Open bytes whose /Encrypt names an alternative /Filter, decrypting
    /// through the supplied handler.</summary>
    private Document(byte[] data, string password,
        Aspose.Pdf.Security.ICustomSecurityHandler customSecurityHandler)
    {
        if (customSecurityHandler is null)
            throw new System.ArgumentNullException(nameof(customSecurityHandler));
        _data = data;
        CustomSecurityHandler = customSecurityHandler;
        _reader = PdfReader.FromBytes(data, password,
            new PdfReaderOptions { CustomSecurityHandler = customSecurityHandler });
        _ = _reader.Catalog;
        _reader.OwnerDocument = this;
    }

    private static byte[] ReadAll(Stream input)
    {
        using var ms = new MemoryStream();
        if (input.CanSeek) input.Position = 0;
        input.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Stream ctor with LoadOptions. Dispatches on the RUNTIME options type:
    /// callers that hold their typed options in a <see cref="LoadOptions"/> variable bind
    /// this overload, and without dispatch an HTML/SVG/text/Markdown source would be
    /// parsed as PDF and die on 'Incorrect file header'.</summary>
    public Document(Stream input, LoadOptions options) : this(SourceToPdfBytes(input, options)) { }

    /// <summary>File ctor with LoadOptions — same runtime dispatch as the stream overload.
    /// FileName is only recorded for actual PDF sources: a converted source must never
    /// become the target of a parameterless Save().</summary>
    public Document(string filename, LoadOptions options) : this(SourceToPdfBytes(filename, options))
    {
        if (options is not (HtmlLoadOptions or SvgLoadOptions or TxtLoadOptions or MdLoadOptions))
            FileName = filename;
    }

    private static byte[] SourceToPdfBytes(string path, LoadOptions options) => options switch
    {
        HtmlLoadOptions h => Converters.HtmlToPdfConverter.Convert(path, h).ToArray(),
        SvgLoadOptions s => SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(path, s)),
        TxtLoadOptions t => Converters.TxtToPdfConverter.Convert(path, t),
        MdLoadOptions m => Converters.MarkdownToPdfConverter.Convert(path, m).ToArray(),
        _ => File.ReadAllBytes(path),
    };

    private static byte[] SourceToPdfBytes(Stream input, LoadOptions options)
    {
        var data = ReadStreamToBytes(input);
        return options switch
        {
            HtmlLoadOptions h => Converters.HtmlToPdfConverter.Convert(data, h).ToArray(),
            SvgLoadOptions s => SvgConvertToPdfBytes(Converters.SvgToPdfConverter.Convert(data, s)),
            TxtLoadOptions t => Converters.TxtToPdfConverter.Convert(data, t),
            MdLoadOptions m => Converters.MarkdownToPdfConverter.Convert(data, m).ToArray(),
            _ => data,
        };
    }

    /// <summary>
    /// Track existing objects modified in-memory (for incremental save).
    /// Key = object number, Value = the modified PdfObject.
    /// </summary>
    private readonly Dictionary<int, PdfObject> _dirtyObjects = new();

    private int? _newInfoObjNum;

    /// <summary>
    /// Structure elements authored against a page that has no object number yet
    /// (a page added to a fresh document). Save stamps each element's /Pg once
    /// the page's object number is decided, before the catalog is serialized.
    /// </summary>
    internal List<(PdfDictionary Element, Page Page)> PendingStructPgFixups { get; } = new();

    /// <summary>
    /// Allocate the next available object number for new objects.
    /// </summary>
    /// <summary>The object number the XMP metadata packet claimed during the save in
    /// progress. The writer emits that stream directly rather than through the pending-object
    /// list, so it would otherwise be invisible to every allocator.</summary>
    private int _reservedMetadataObjNum;

    /// <summary>
    /// Ensure the document has an Info dictionary and return its object number.
    /// If the original document had no Info dict, a new one is created.
    /// </summary>
    /// <summary>Resolve the existing /Info dictionary without creating one. Returns null
    /// when the document has no Info dict. Used by DocumentInfo read access so a
    /// <c>new DocumentInfo(doc)</c> reflects on-disk metadata without side effects.</summary>
    /// <summary>The /Info dict a DocumentInfo setter created for a document that had
    /// none. It lives in the pending-object list (not reachable through the reader
    /// until save), so in-memory readers — Document.Info, the PDF/A conversion's
    /// Info↔XMP sync — resolve it through here.</summary>
    private PdfDictionary? _pendingInfoDict;

    internal (PdfDictionary dict, int objNum) EnsureInfoDict()
    {
        var infoRef = _reader.Trailer.Get("Info");
        if (infoRef is PdfIndirectRef iref)
        {
            var dict = _reader.ResolveDict(infoRef);
            if (dict is not null)
            {
                // The Info dict already exists on disk; a DocumentInfo setter is about to
                // mutate it in place. Mark it dirty so the (incremental) writer re-emits the
                // object — otherwise metadata edits like ModDate are silently dropped on save.
                MarkDirty(iref.ObjectNumber, dict);
                return (dict, iref.ObjectNumber);
            }
        }

        // No Info dict — create one. Cached on the document so every other
        // DocumentInfo instance materialised before save shares it (the pending
        // object is not reachable through the reader yet).
        var newDict = new PdfDictionary();
        var objNum = AllocateObjectNumber();
        _newInfoObjNum = objNum;
        AddNewObject(objNum, newDict);
        _pendingInfoDict = newDict;
        return (newDict, objNum);
    }

    /// <summary>Clears memory.</summary>
    public void FreeMemory()
    {
        _pages?.FreeMemory();
    }

    // FreeMemory keeps the document usable (caches rebuild lazily from the reader);
    // Dispose additionally drops the source buffer, so the file bytes stop being
    // reachable even while the disposed instance itself is still referenced.
    public void Dispose()
    {
        FreeMemory();
        _data = Array.Empty<byte>();
        _reader.ReleaseBuffers();
    }
}
