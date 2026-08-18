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
    public static Document MergeDocuments(params byte[][] documents)
    {
        if (documents.Length == 0) return Create();
        var result = Open(documents[0]);
        for (int i = 1; i < documents.Length; i++)
        {
            using var source = Open(documents[i]);
            var pageNums = Enumerable.Range(1, source.PageCount).ToArray();
            result.ImportPages(source, pageNums);
        }
        return result;
    }

    public static Document MergeDocuments(params string[] files)
    {
        var bytes = new byte[files.Length][];
        for (int i = 0; i < files.Length; i++)
            bytes[i] = File.ReadAllBytes(files[i]);
        return MergeDocuments(bytes);
    }

    /// <summary>Merge every page of <paramref name="documents"/> into a new
    /// destination <see cref="Document"/>. Source documents are read but
    /// left unchanged.</summary>
    public static Document MergeDocuments(params Document[] documents)
    {
        var target = Create();
        target.Merge(documents);
        return target;
    }

    /// <summary>Merge every page of each <paramref name="documents"/> entry
    /// into this document, preserving source order.</summary>
    public void Merge(params Document[] documents)
    {
        if (documents is null) return;
        foreach (var d in documents)
            if (d is not null) Pages.Add(d.Pages);
    }

    /// <summary>Merge every page of each file in <paramref name="files"/>
    /// into this document. Sources are opened and disposed by this method.</summary>
    public void Merge(params string[] files)
    {
        if (files is null) return;
        foreach (var f in files)
        {
            using var d = new Document(f);
            Pages.Add(d.Pages);
        }
    }

    /// <summary>Merge with explicit options. Real — RemoveSignatures strips
    /// /V from every signature field; MergeDuplicateOutlines deduplicates
    /// catalog outline trees by title+page; KeepFieldsUnique appends
    /// "_2", "_3" suffixes to colliding form-field names.</summary>
    public void Merge(MergeOptions mergeOptions, params Document[] documents)
    {
        Merge(documents);
        ApplyMergeOptions(mergeOptions);
    }

    /// <summary>Merge files with explicit options — same semantics as
    /// the Document[] overload.</summary>
    public void Merge(MergeOptions mergeOptions, params string[] files)
    {
        Merge(files);
        ApplyMergeOptions(mergeOptions);
    }

    /// <summary>Static Merge: build a fresh Document containing every
    /// page from <paramref name="files"/>, then apply <paramref name="mergeOptions"/>.</summary>
    public static Document MergeDocuments(MergeOptions mergeOptions, params Document[] files)
    {
        var target = Create();
        target.Merge(mergeOptions, files);
        return target;
    }

    /// <summary>Static Merge: file-paths variant.</summary>
    public static Document MergeDocuments(MergeOptions mergeOptions, params string[] files)
    {
        var target = Create();
        target.Merge(mergeOptions, files);
        return target;
    }

    private void ApplyMergeOptions(MergeOptions? options)
    {
        if (options is null) return;
        if (options.RemoveSignatures)
        {
            var form = Form;
            if (form is not null)
            {
                foreach (var field in form.Fields)
                {
                    if (field.Type != Forms.FieldType.Signature) continue;
                    field.Dict.Remove("V");
                }
            }
        }
        if (options.MergeDuplicateOutlines)
            DeduplicateOutlines();
        if (options.KeepFieldsUnique)
            DisambiguateFormFieldNames();
        // RemoveUserRights: strip /Perms /UR / /UR3 from catalog.
        if (options.RemoveUserRights)
        {
            var perms = _reader.ResolveDict(_reader.Catalog.Get("Perms"));
            if (perms is not null)
            {
                perms.Remove("UR");
                perms.Remove("UR3");
                if (!perms.Keys.Any())
                    _reader.Catalog.Remove("Perms");
            }
        }
    }

    private void DeduplicateOutlines()
    {
        // The outline tree may contain duplicate entries with identical
        // /Title + /Dest after a merge. Walk /Outlines + /First/Next and
        // remove later items whose (Title, page-number) tuple matches an
        // earlier one.
        var outlinesObj = _reader.ResolveDict(_reader.Catalog.Get("Outlines"));
        if (outlinesObj is null) return;
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        DedupeOutlineList(outlinesObj, seen);
    }

    private void DedupeOutlineList(Aspose.Pdf.Core.PdfDictionary parent, HashSet<string> seen)
    {
        var current = _reader.ResolveDict(parent.Get("First"));
        Aspose.Pdf.Core.PdfDictionary? prev = null;
        while (current is not null)
        {
            var title = current.Get("Title") is Aspose.Pdf.Core.PdfString s ? s.ToText() : "";
            var key = title + "|" + DescribeDestination(current);
            var next = _reader.ResolveDict(current.Get("Next"));
            if (seen.Contains(key))
            {
                // Splice current out of the list.
                if (prev is null) parent.Set("First", current.Get("Next") ?? (Aspose.Pdf.Core.PdfObject)Aspose.Pdf.Core.PdfNull.Instance);
                else if (current.Get("Next") is { } nxt) prev.Set("Next", nxt);
                else prev.Remove("Next");
            }
            else
            {
                seen.Add(key);
                // Recurse into nested children.
                if (current.ContainsKey("First")) DedupeOutlineList(current, seen);
                prev = current;
            }
            current = next;
        }
    }

    private string DescribeDestination(Aspose.Pdf.Core.PdfDictionary outlineItem)
    {
        var dest = _reader.Resolve(outlineItem.Get("Dest"));
        return dest switch
        {
            Aspose.Pdf.Core.PdfArray arr when arr.Count > 0 => arr[0]?.ToString() ?? "",
            Aspose.Pdf.Core.PdfString s => s.ToText(),
            Aspose.Pdf.Core.PdfName n => n.Value,
            _ => "",
        };
    }

    private void DisambiguateFormFieldNames()
    {
        var form = Form;
        if (form is null) return;
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var field in form.Fields)
        {
            var name = field.FullName ?? string.Empty;
            if (string.IsNullOrEmpty(name)) continue;
            if (seen.Add(name)) continue;
            var idx = 2;
            string candidate;
            do { candidate = $"{name}_{idx++}"; } while (!seen.Add(candidate));
            field.SetPartialName(candidate);
        }
    }

    // ── SendTo (DocumentDevice routing) ────────────────────────────

    /// <summary>Render this document through <paramref name="device"/> into
    /// <paramref name="output"/> — delegates to <see cref="Devices.DocumentDevice.Process(Document, System.IO.Stream)"/>.</summary>
    public void SendTo(Devices.DocumentDevice device, Stream output)
        => device?.Process(this, output);

    /// <summary>File overload of <see cref="SendTo(Devices.DocumentDevice, Stream)"/>.</summary>
    public void SendTo(Devices.DocumentDevice device, string outputFileName)
        => device?.Process(this, outputFileName);

    /// <summary>Render a page range — delegates to the device's
    /// page-range Process overload.</summary>
    public void SendTo(Devices.DocumentDevice device, int fromPage, int toPage, Stream output)
        => device?.Process(this, fromPage, toPage, output);

    /// <summary>File-pair page-range overload.</summary>
    public void SendTo(Devices.DocumentDevice device, int fromPage, int toPage, string outputFileName)
        => device?.Process(this, fromPage, toPage, outputFileName);

    // ── Convert(Fixup, …) — real for Rotate; throws on rendering-grade fixups

    /// <summary>Run a pre-defined fixup operation. Real implementations:
    /// <see cref="Fixup.RotatePagesToLandscape"/> and <see cref="Fixup.RotatePagesToPortrait"/>
    /// set every page's /Rotate to produce the requested orientation.
    /// Other fixups throw <see cref="System.NotSupportedException"/>:
    /// EmbedMissingFonts / ConvertFontsToOutlines need a font-embedding
    /// or text→outlines pass; DerivePageGeometryBoxesFromCropMarks needs
    /// crop-mark detection; ConvertAllPagesIntoCMYKImagesAndPreserveText
    /// Information needs a full CMYK rasterisation pipeline.</summary>
    public bool Convert(Fixup fixup, Stream outputLog, bool onlyValidation, params object[] parameters)
    {
        _ = parameters;
        if (onlyValidation)
        {
            // Validation-only: report whether the fix would apply without
            // mutating the document.
            return fixup is Fixup.RotatePagesToLandscape or Fixup.RotatePagesToPortrait;
        }
        switch (fixup)
        {
            case Fixup.RotatePagesToLandscape:
                ApplyRotateFixup(targetLandscape: true);
                LogFixup(outputLog, "RotatePagesToLandscape applied.");
                return true;
            case Fixup.RotatePagesToPortrait:
                ApplyRotateFixup(targetLandscape: false);
                LogFixup(outputLog, "RotatePagesToPortrait applied.");
                return true;
            default:
                throw new System.NotSupportedException(
                    $"Fixup {fixup} is not implemented in the FOSS build. Only RotatePagesToLandscape and RotatePagesToPortrait are real; the other fixups require renderer-grade features (font embedding / outlining / CMYK rasterisation / crop-mark detection).");
        }
    }

    /// <summary>File-log overload of <see cref="Convert(Fixup, Stream, bool, object[])"/>.</summary>
    public bool Convert(Fixup fixup, string outputLog, bool onlyValidation, params object[] parameters)
    {
        using var fs = string.IsNullOrEmpty(outputLog) ? null : File.Create(outputLog);
        return Convert(fixup, (Stream?)fs ?? Stream.Null, onlyValidation, parameters);
    }

    /// <summary>Apply <paramref name="fixup"/> and write a log to
    /// <paramref name="outputLog"/> (the common, non-validation case).</summary>
    public bool Convert(Fixup fixup, string outputLog) => Convert(fixup, outputLog, false);

    /// <summary>Open <paramref name="srcFileName"/> via the load-options
    /// hierarchy (HtmlLoadOptions / SvgLoadOptions / MdLoadOptions all
    /// derive from <see cref="LoadOptions"/>) and save to
    /// <paramref name="dstFileName"/>. Save-options dispatch: HtmlSaveOptions
    /// routes to the PDF→HTML writer; anything else saves as PDF.</summary>
    public static void Convert(string srcFileName, LoadOptions loadOptions,
        string dstFileName, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcFileName, loadOptions);
        SaveWithSaveOptions(doc, dstFileName, saveOptions);
    }

    public static void Convert(string srcFileName, LoadOptions loadOptions,
        Stream dstStream, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcFileName, loadOptions);
        SaveWithSaveOptions(doc, dstStream, saveOptions);
    }

    public static void Convert(Stream srcStream, LoadOptions loadOptions,
        string dstFileName, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcStream, loadOptions);
        SaveWithSaveOptions(doc, dstFileName, saveOptions);
    }

    public static void Convert(Stream srcStream, LoadOptions loadOptions,
        Stream dstStream, SaveOptions saveOptions)
    {
        using var doc = OpenWithLoadOptions(srcStream, loadOptions);
        SaveWithSaveOptions(doc, dstStream, saveOptions);
    }

    private static Document OpenWithLoadOptions(string srcFileName, LoadOptions loadOptions) => loadOptions switch
    {
        HtmlLoadOptions h => Open(srcFileName, h),
        MdLoadOptions md => Open(srcFileName, md),
        SvgLoadOptions svg => Open(srcFileName, svg),
        _ => Open(srcFileName),
    };

    private static Document OpenWithLoadOptions(Stream srcStream, LoadOptions loadOptions)
    {
        if (loadOptions is HtmlLoadOptions h) return new Document(srcStream, h);
        if (loadOptions is MdLoadOptions md)
            return Open(ReadStreamBytes(srcStream), md);
        if (loadOptions is SvgLoadOptions svg)
            return Open(ReadStreamBytes(srcStream), svg);
        return new Document(srcStream);
    }

    private static byte[] ReadStreamBytes(Stream s)
    {
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static void SaveWithSaveOptions(Document doc, string dst, SaveOptions saveOptions)
    {
        if (saveOptions is HtmlSaveOptions h) doc.Save(dst, h);
        else if (saveOptions is PdfToMarkdown.MarkdownSaveOptions) doc.Save(dst, saveOptions);
        else doc.Save(dst);
    }

    private static void SaveWithSaveOptions(Document doc, Stream dst, SaveOptions saveOptions)
    {
        if (saveOptions is HtmlSaveOptions h) doc.Save(dst, h);
        else if (saveOptions is PdfToMarkdown.MarkdownSaveOptions) doc.Save(dst, saveOptions);
        else doc.Save(dst);
    }

    private void ApplyRotateFixup(bool targetLandscape)
    {
        for (var i = 1; i <= PageCount; i++)
        {
            var page = Pages[i];
            var media = page.MediaBox;
            var existing = (int)(page.Dict.Get("Rotate") is Aspose.Pdf.Core.PdfInteger n ? n.Value : 0);
            // Displayed orientation depends on /Rotate: a 90°/270° rotation swaps
            // the media box's width and height on screen.
            var norm = ((existing % 360) + 360) % 360;
            var quarterTurned = norm == 90 || norm == 270;
            var displaysLandscape = quarterTurned ? media.Height > media.Width : media.Width > media.Height;
            // Already in the requested orientation — leave the page untouched.
            if (displaysLandscape == targetLandscape) continue;
            // Otherwise a single 90° clockwise turn flips landscape↔portrait.
            page.Dict.Set("Rotate", new Aspose.Pdf.Core.PdfInteger((existing + 90) % 360));
        }
    }

    private static void LogFixup(Stream? logStream, string line)
    {
        if (logStream is null || logStream == Stream.Null) return;
        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(line + "\n");
            logStream.Write(bytes, 0, bytes.Length);
        }
        catch { }
    }

    /// <summary>Merge tuning knobs honored by
    /// <see cref="Merge(MergeOptions, Document[])"/> and friends.</summary>
    public sealed class MergeOptions
    {
        /// <summary>Strip /V from every signature field after merge.</summary>
        public bool RemoveSignatures { get; set; }

        /// <summary>Deduplicate identical entries from the merged outline tree.</summary>
        public bool MergeDuplicateOutlines { get; set; }

        /// <summary>Append "_2", "_3", … suffixes to colliding form-field names.</summary>
        public bool KeepFieldsUnique { get; set; }

        /// <summary>Strip /Perms /UR /UR3 usage-rights entries after merge.</summary>
        public bool RemoveUserRights { get; set; }

        /// <summary>Merge duplicate optional-content groups (layers). Stored
        /// only — the FOSS writer does not yet emit a deduplicated /OCProperties
        /// tree.</summary>
        public bool MergeDuplicateLayers { get; set; }

        /// <summary>Streaming buffer size (in bytes) for the source-side
        /// reader during merge. Stored only — the FOSS merge path keeps
        /// the full document in memory and does not split reads into
        /// packets.</summary>
        public int ConcatenationPacketSize { get; set; }

        /// <summary>When true, the merged /Pages tree is balanced into a
        /// fixed-fanout subtree shape. Stored only — the FOSS merge
        /// emits a flat /Pages /Kids list and does not balance the tree.</summary>
        public bool IsNeedPageTreeBalance { get; set; }

        /// <summary>Maximum entries per /Pages subtree node when
        /// <see cref="IsNeedPageTreeBalance"/> is set. Stored only.</summary>
        public byte MaximumNodesInLevel { get; set; }

        /// <summary>Spill intermediate state to a temp file rather than
        /// keeping it in memory. Stored only — the FOSS merge path is
        /// always in-memory.</summary>
        public bool UseDiskBuffer { get; set; }
    }

    /// <summary>
    /// The raw PDF catalog dictionary for power-user access.
    /// </summary>
    internal PdfDictionary Catalog => _reader.Catalog;

    /// <summary>
    /// The internal reader (for sub-components that need object resolution).
    /// </summary>
    internal PdfReader Reader => _reader;

    /// <summary>
    /// Encrypt the document with the specified algorithm, passwords, and permissions.
    /// Encryption is applied on the next save.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        DocumentPrivilege? permissions = null, CryptoAlgorithm algorithm = Aspose.Pdf.CryptoAlgorithm.AESx128)
    {
        // Encryption is gated on the SIGNATURE TYPE, not the mere
        // presence of a signature:
        // a DocMDP CERTIFICATION signature refuses encryption (it would break the
        // certification), while an ordinary APPROVAL signature encrypts normally.
        // So an approval signature succeeds; a certified (DocMDP) one throws.
        if (HasCertificationSignature())
            throw new PdfException(
                "You cannot change this document because it is certified.");

        var p = permissions is not null
            ? GetPermissionFlags(permissions)
            : -4; // all permissions

        _encryptor = algorithm switch
        {
            Aspose.Pdf.CryptoAlgorithm.RC4x40 => PdfEncryptor.CreateRC4x40(userPassword, ownerPassword, p),
            Aspose.Pdf.CryptoAlgorithm.RC4x128 => PdfEncryptor.CreateRC4x128(userPassword, ownerPassword, p),
            Aspose.Pdf.CryptoAlgorithm.AESx128 => PdfEncryptor.CreateAES128(userPassword, ownerPassword, p),
            Aspose.Pdf.CryptoAlgorithm.AESx256 => PdfEncryptor.CreateAES256(userPassword, ownerPassword, p),
            _ => PdfEncryptor.CreateAES128(userPassword, ownerPassword, p),
        };
    }

    /// <summary>True when the document carries a DocMDP certification (author) signature.
    /// A certification is recorded in the catalog as <c>/Perms &lt;&lt; /DocMDP &lt;sigref&gt; &gt;&gt;</c>
    /// (PDF 32000-1 §12.8.2.2), so it is detected by a direct catalog lookup rather than by
    /// enumerating the AcroForm fields — the latter can recurse on a pathological field tree.
    /// Ordinary approval signatures leave /Perms absent, so they are not blocked.</summary>
    private bool HasCertificationSignature()
    {
        try
        {
            var perms = _reader.ResolveDict(_reader.Catalog?.Get("Perms"));
            return perms is not null && perms.Get("DocMDP") is not null;
        }
        catch
        {
            // A malformed catalog must not turn encryption into a crash — treat it as
            // "not certified" and let encryption proceed (the pre-enforcement behaviour).
            return false;
        }
    }

    /// <summary>
    /// Encrypt overload accepting an explicit <c>usePdf20</c> flag.
    /// With <c>usePdf20</c> true, AES-256 writes the ISO 32000-2 revision-6
    /// handler (the existing AES-256 path already derives revision-6 values);
    /// deprecated combinations throw <see cref="DeprecatedFeatureException"/>.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        DocumentPrivilege? privileges, CryptoAlgorithm cryptoAlgorithm, bool usePdf20)
    {
        GuardPdf20Encryption(cryptoAlgorithm, usePdf20);
        Encrypt(userPassword, ownerPassword, privileges, cryptoAlgorithm);
    }

    /// <summary>
    /// Encrypt overload accepting the <see cref="Permissions"/> flags enum and
    /// an explicit <c>usePdf20</c> flag. Same usePdf20 contract as the
    /// DocumentPrivilege-typed overload above.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm, bool usePdf20)
    {
        GuardPdf20Encryption(cryptoAlgorithm, usePdf20);
        Encrypt(userPassword, ownerPassword, permissions, cryptoAlgorithm);
    }

    /// <summary>
    /// PDF 2.0 (ISO 32000-2 §7.6) keeps only the AES-256 revision-6 security
    /// handler: the RC4 crypt filters are removed outright, and on a document
    /// that is already PDF 2.0 the legacy handlers (RC4, AESV2, and the interim
    /// AES-256 revision 5 selected by <c>usePdf20:false</c>) are deprecated too.
    /// </summary>
    private void GuardPdf20Encryption(CryptoAlgorithm algorithm, bool usePdf20)
    {
        var isRc4 = algorithm is Aspose.Pdf.CryptoAlgorithm.RC4x40 or Aspose.Pdf.CryptoAlgorithm.RC4x128;
        if (usePdf20 && isRc4)
            throw new DeprecatedFeatureException(
                "RC4 encryption is deprecated in PDF 2.0; use AES-256.");
        if (!usePdf20 && Version == "2.0")
            throw new DeprecatedFeatureException(
                "Legacy security handlers are deprecated in PDF 2.0; use AES-256 with usePdf20 set to true.");
    }

    /// <summary>
    /// Encrypt overload accepting the legacy <see cref="Permissions"/> flags
    /// enum. Maps each flag to the corresponding bit and forwards to the
    /// DocumentPrivilege overload.
    /// </summary>
    public void Encrypt(string userPassword, string ownerPassword,
        Aspose.Pdf.Permissions permissions, CryptoAlgorithm cryptoAlgorithm = Aspose.Pdf.CryptoAlgorithm.AESx128)
    {
        var dp = new DocumentPrivilege();
        if ((permissions & Aspose.Pdf.Permissions.PrintDocument) != 0) dp.AllowPrint = true;
        if ((permissions & Aspose.Pdf.Permissions.ModifyContent) != 0) dp.AllowModifyContents = true;
        if ((permissions & Aspose.Pdf.Permissions.ExtractContent) != 0) dp.AllowCopy = true;
        if ((permissions & Aspose.Pdf.Permissions.ModifyTextAnnotations) != 0) dp.AllowModifyAnnotations = true;
        if ((permissions & Aspose.Pdf.Permissions.FillForm) != 0) dp.AllowFillIn = true;
        if ((permissions & Aspose.Pdf.Permissions.ExtractContentWithDisabilities) != 0) dp.AllowScreenReaders = true;
        if ((permissions & Aspose.Pdf.Permissions.AssembleDocument) != 0) dp.AllowAssembly = true;
        if ((permissions & Aspose.Pdf.Permissions.PrintingQuality) != 0) dp.HighQualityPrinting = true;
        Encrypt(userPassword, ownerPassword, dp, cryptoAlgorithm);
    }

    /// <summary>
    /// Change the password on the bound document. Reads the current
    /// permissions, then re-encrypts with the supplied new passwords
    /// (owner-password is required to authorise the change).
    /// </summary>
    public void ChangePasswords(string ownerPassword, string newUserPassword, string newOwnerPassword)
    {
        var existing = new DocumentPrivilege(Permissions);
        Encrypt(newUserPassword, newOwnerPassword, existing, Aspose.Pdf.CryptoAlgorithm.AESx128);
    }

    /// <summary>Remove encryption from the document.</summary>
    public void Decrypt()
    {
        _encryptor = null;
        // Remove Encrypt entry from trailer so the saved PDF is unencrypted
        _reader.Trailer?.Remove("Encrypt");
    }

    private PdfEncryptor? _encryptor;

#pragma warning disable CS0649
    private bool _forceWriteId;

#pragma warning restore CS0649

    private static int GetPermissionFlags(DocumentPrivilege priv)
    {
        var flags = unchecked((int)0xFFFFF0C0); // Required reserved bits
        if (priv.AllowPrint) flags |= 1 << 2;
        if (priv.AllowModifyContents) flags |= 1 << 3;
        if (priv.AllowCopy) flags |= 1 << 4;
        if (priv.AllowModifyAnnotations) flags |= 1 << 5;
        if (priv.AllowFillIn) flags |= 1 << 8;
        if (priv.AllowScreenReaders) flags |= 1 << 9;
        if (priv.AllowAssembly) flags |= 1 << 10;
        if (priv.HighQualityPrinting) flags |= 1 << 11;
        return flags;
    }

    /// <summary>
    /// Optimize document resources by removing unused objects and deduplicating streams.
    /// The optimization is applied on the next save.
    /// </summary>
    /// <summary>
    /// Linearize the document for fast web view (mirrors the public API
    /// <c>Document.Optimize()</c>). Default optimization is applied.
    /// </summary>
    public void Optimize()
    {
        OptimizeResources();
        // Optimize() also enables fast-web-view (linearization) on the next save,
        // so that Document.Optimize() produces a
        // linearized output. LinearizeDocument() sets the same flag.
        _linearize = true;
    }

    /// <summary>
    /// Process paragraphs in the document (layout step before save).
    /// Renders queued paragraphs (TextFragment, Heading, Table, HtmlFragment, ...) and
    /// applies TOC info to each page's content stream. Idempotent; the same pass runs
    /// again at Save but each page is gated by Page.LayoutApplied.
    /// </summary>
    public void ProcessParagraphs() => ApplyPageContent();

    /// <summary>
    /// Gets or sets a flag indicating whether the document should be optimized for size on save.
    /// When true, redundant data is removed during save to reduce file size.
    /// </summary>
    public bool OptimizeSize { get; set; }

    /// <summary>When true, the standard 14 PostScript fonts (Helvetica /
    /// Times / Courier × 4 styles + Symbol + ZapfDingbats) are embedded
    /// into the saved document so the output renders identically without
    /// relying on viewer-side font fallbacks. Stored only; the saver does
    /// not currently embed the Standard 14 font files.</summary>
    public bool EmbedStandardFonts { get; set; }

    /// <summary>When true, the parser tolerates corrupted indirect-object
    /// declarations (extra/garbled bytes between objects) instead of
    /// throwing. Stored only; the parser is already lenient about most
    /// malformed input.</summary>
    public bool IgnoreCorruptedObjects { get; set; }

    public void OptimizeResources() => OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions.Default);

    public void OptimizeResources(Aspose.Pdf.Optimization.OptimizationOptions strategy)
    {
        var options = strategy ?? Aspose.Pdf.Optimization.OptimizationOptions.Default;

        // Drop /Resources entries (fonts, XObjects, ...) that no content stream
        // references. Done before the reachability pass below so the now-orphaned
        // resource objects also fall out of the saved file.
        if (options.RemoveUnusedStreams)
        {
            PruneUnusedResources();
        }

        // Apply image compression if requested
        if (options.CompressImages)
        {
            ImageCompressor.CompressImages(_reader, options.ImageQuality);
        }

        // Downsample images exceeding max DPI
        if (options.MaxImageDpi > 0)
        {
            ImageCompressor.DownsampleImages(_reader, options.MaxImageDpi, options.ImageQuality);
        }

        // Convert images to grayscale
        if (options.ConvertImagesToGrayscale)
        {
            ImageCompressor.ConvertToGrayscale(_reader);
        }

        // Remove duplicate images
        if (options.RemoveDuplicateImages)
        {
            ImageCompressor.RemoveDuplicateImages(_reader);
        }

        // Apply font subsetting if requested. The public SubsetFonts option means
        // real embedded-program subsetting, not just the
        // standard-14 strip — routing only the internal SubsetEmbeddedFonts flag to
        // the TrueType subsetter left full 400KB font programs in "optimized" files.
        if (options.SubsetFonts || options.SubsetEmbeddedFonts)
        {
            FontSubsetter.SubsetFonts(_reader, subsetEmbedded: true);
        }

        // Drop embedded font programs for fonts a viewer can substitute (Standard 14 or
        // installed system faces). The now-orphaned font streams fall out via the
        // reachability pass below.
        if (options.UnembedFonts)
        {
            FontSubsetter.UnembedFonts(_reader);
        }

        // Remove metadata if requested
        if (options.RemoveMetadata)
        {
            _reader.Catalog.Remove("Metadata");
        }

        // Link duplicate streams
        if (options.LinkDuplicateStreams)
        {
            LinkDuplicateStreams();
        }

        // Compute reachable objects from the trailer. Done LAST, after the resource prune,
        // font unembedding, and duplicate-stream linking above, so any object those steps
        // orphaned (e.g. an unembedded /FontFile2 program or a linked-away duplicate) is
        // excluded from the saved file rather than written from a stale snapshot.
        var reachable = new HashSet<int>();
        if (options.RemoveUnusedObjects)
        {
            CollectReachable(_reader.Trailer, reachable);
            // Cross-document page imports aren't linked into the trailer's /Pages tree
            // until save (RebuildPagesTree), so walk the pending page dicts explicitly.
            // Otherwise their still-referenced imported resource objects look unreachable
            // and a copied page's images would be dropped from the saved file.
            if (_pages is not null)
                foreach (var pending in _pages.PendingAdds)
                    CollectReachable(pending.Dict, reachable);
        }

        // Mark document as needing optimization on next save
        _optimizationOptions = options;
        _reachableObjects = reachable.Count > 0 ? reachable : null;
    }

    private Aspose.Pdf.Optimization.OptimizationOptions? _optimizationOptions;

    private HashSet<int>? _reachableObjects;

    private bool _prunedFontsThisSave;

    private void CollectReachable(PdfObject? root, HashSet<int> visited)
    {
        if (root is null or PdfNull) return;

        // Iterative traversal with explicit stack to avoid stack overflow on large PDFs
        var stack = new Stack<PdfObject>();
        stack.Push(root);
        var seenDicts = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        while (stack.Count > 0)
        {
            var obj = stack.Pop();
            if (obj is null or PdfNull) continue;

            if (obj is PdfIndirectRef iref)
            {
                if (!visited.Add(iref.ObjectNumber)) continue;
                var resolved = _reader.Resolve(iref);
                if (resolved is not null) stack.Push(resolved);
                continue;
            }

            if (obj is PdfStream stream)
            {
                stack.Push(stream.Dict);
                continue;
            }

            if (obj is PdfDictionary dict)
            {
                if (!seenDicts.Add(dict)) continue;
                foreach (var key in dict.Keys)
                {
                    var val = dict.Get(key);
                    if (val is not null) stack.Push(val);
                }
                continue;
            }

            if (obj is PdfArray arr)
            {
                foreach (var item in arr)
                    if (item is not null) stack.Push(item);
            }
        }
    }

    /// <summary>The /Resources sub-dictionaries whose entries are name-referenced
    /// from content streams and so can be pruned when unreferenced.</summary>
    private static readonly string[] PrunableResourceCategories =
        { "Font", "XObject", "ExtGState", "Pattern", "Shading", "ColorSpace", "Properties" };

    /// <summary>
    /// Remove /Resources entries (fonts, XObjects, ExtGStates, ...) that no content
    /// stream reachable from a page actually references. Conservative by design: an
    /// entry is kept whenever its resource name appears as a /Name token in the page
    /// content or in any form XObject invoked (directly or transitively) from it, so
    /// a face used only through a form's parent-resource fallback is never dropped.
    /// Only page-level resource dictionaries are pruned; per-form resources are left
    /// intact (they are small and self-contained).
    /// </summary>
    private void PruneUnusedResources()
    {
        // A /Resources dict may be SHARED by several pages (e.g. inherited from a
        // common parent in the page tree). Pruning it per-page would drop entries
        // another page still uses, so accumulate the UNION of used names per shared
        // resources dict (by reference identity) and prune each dict once.
        var usedByResources = new Dictionary<PdfDictionary, HashSet<string>>(ReferenceEqualityComparer.Instance);
        var keepAll = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);

        foreach (var page in Pages)
        {
            var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null) continue;

            var content = page.GetContentStreamBytes();
            // No analysable content => keep every resource on this dict rather than guess.
            if (content is null || content.Length == 0)
            {
                keepAll.Add(resources);
                continue;
            }

            if (!usedByResources.TryGetValue(resources, out var used))
            {
                used = new HashSet<string>(StringComparer.Ordinal);
                usedByResources[resources] = used;
            }
            var visitedForms = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
            CollectContentResourceNames(content, resources, used, visitedForms);
        }

        foreach (var (resources, used) in usedByResources)
        {
            if (keepAll.Contains(resources)) continue;
            AddReferencedXObjectNames(resources, used);
            PruneResourceCategories(resources, used);
        }
    }

    /// <summary>Drop /Font resource entries no longer referenced by any content after a
    /// <see cref="Text.TextEditOptions.FontReplace.RemoveUnusedFonts"/> text edit. Walks
    /// the page content and every invoked form XObject, pruning each scope's OWN /Font
    /// dictionary against the fonts its content selects with <c>Tf</c>.</summary>
    private void PruneUnusedFontsForPage(Page page)
    {
        var resources = _reader.ResolveDict(page.Dict.Get("Resources"));
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var rewritten = PruneFontsInScope(page.GetContentStreamBytes(), resources, visited);
        if (rewritten is not null) page.SetContentStream(rewritten);
    }

    /// <summary>Prune unused /Font entries in a content scope and rename the replacement
    /// fonts to sequential F0, F1, … keys (a replacement font is
    /// named "F0"). Returns the rewritten content when a rename changed it,
    /// else null. Form XObject scopes are rewritten in place.</summary>
    private byte[]? PruneFontsInScope(byte[]? content, PdfDictionary? resources,
        HashSet<PdfDictionary> visitedForms)
    {
        if (content is null || content.Length == 0 || resources is null) return null;

        // Collect the fonts a `Tf` selects that actually SHOW text, and the form
        // XObjects a `Do` invokes. A font selected only by an empty run (`/F Tf`
        // followed by `[] TJ` with no glyphs, then another `Tf`) is not really used —
        // counting it would keep an orphan font after a full RemoveUnusedFonts replace.
        var usedFonts = new HashSet<string>(StringComparer.Ordinal);
        var formNames = new List<string>();
        var lexer = new IO.PdfLexer(content);
        string? lastName = null;      // most recent /Name operand (font for Tf, form for Do)
        string? currentFont = null;   // font selected by the last Tf
        bool sawGlyphs = false;       // a non-empty string appeared since the last operator
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            switch (token.Kind)
            {
                case IO.TokenKind.Name when token.StringValue is { } n:
                    lastName = n;
                    break;
                case IO.TokenKind.LiteralString:
                case IO.TokenKind.HexString:
                    if (token.BytesValue is { Length: > 0 }) sawGlyphs = true;
                    break;
                case IO.TokenKind.Keyword:
                    var kw = token.StringValue;
                    if (kw == "BI") { SkipInlineImage(lexer, usedFonts); break; }
                    if (kw == "Tf") currentFont = lastName;
                    else if (kw == "Do" && lastName is not null) formNames.Add(lastName);
                    else if ((kw == "Tj" || kw == "TJ" || kw == "'" || kw == "\"")
                             && sawGlyphs && currentFont is not null)
                        usedFonts.Add(currentFont);
                    sawGlyphs = false; // operator boundary resets the operand scan
                    break;
            }
        }

        byte[]? rewritten = null;
        var fontDict = _reader.ResolveDict(resources.Get("Font"));
        if (fontDict is not null)
        {
            foreach (var key in fontDict.Keys.ToList())
                if (!usedFonts.Contains(key))
                    fontDict.Remove(key);

            // Rename the replacement fonts (registered under an "AsRp…" key) to F0, F1, …,
            // avoiding collision with any surviving original font, and patch the content's
            // Tf operands to match.
            var survivors = fontDict.Keys.ToList();
            var taken = new HashSet<string>(survivors.Where(k => !k.StartsWith("AsRp", StringComparison.Ordinal)),
                StringComparer.Ordinal);
            var renameMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var n = 0;
            foreach (var rk in survivors.Where(k => k.StartsWith("AsRp", StringComparison.Ordinal)))
            {
                string fn;
                do { fn = "F" + n++; } while (taken.Contains(fn));
                taken.Add(fn);
                renameMap[rk] = fn;
            }
            if (renameMap.Count > 0)
            {
                foreach (var (oldKey, newKey) in renameMap)
                {
                    var val = fontDict.Get(oldKey);
                    fontDict.Remove(oldKey);
                    if (val is not null) fontDict.Set(newKey, val);
                }
                rewritten = RenameFontNamesInContent(content, renameMap);
            }
        }

        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is not null)
        {
            foreach (var name in formNames)
            {
                var xstream = _reader.ResolveStream(xobjects.Get(name));
                if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
                if (!visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
                var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources"));
                // Only prune a form's OWN /Font dict — a form inheriting the page's
                // resources shares that dict, handled at the page scope.
                if (formRes is not null && !ReferenceEquals(formRes, resources))
                {
                    var newForm = PruneFontsInScope(_reader.DecodeStream(xstream), formRes, visitedForms);
                    if (newForm is not null)
                    {
                        xstream.Dict.Remove("Filter");
                        xstream.Dict.Remove("DecodeParms");
                        xstream.Dict.Set("Length", new PdfInteger(newForm.Length));
                        xstream.ReplaceData(newForm);
                    }
                }
            }
        }
        return rewritten;
    }

    /// <summary>Rewrite a content stream, replacing every /Name token that is a key of
    /// <paramref name="renameMap"/> with its mapped name (used to repoint Tf font
    /// operands after a resource-key rename).</summary>
    private static byte[] RenameFontNamesInContent(byte[] content, Dictionary<string, string> renameMap)
    {
        var lexer = new IO.PdfLexer(content);
        var patches = new List<(int start, int end, string nw)>();
        while (true)
        {
            var startPos = (int)lexer.Position;
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } nm
                && renameMap.TryGetValue(nm, out var nw))
                patches.Add((startPos, (int)lexer.Position, nw));
        }
        // Apply right-to-left so earlier offsets stay valid.
        patches.Sort((a, b) => b.start.CompareTo(a.start));
        foreach (var (s, e, nw) in patches)
        {
            var nameBytes = System.Text.Encoding.ASCII.GetBytes("/" + nw);
            var result = new byte[content.Length - (e - s) + nameBytes.Length];
            Array.Copy(content, 0, result, 0, s);
            Array.Copy(nameBytes, 0, result, s, nameBytes.Length);
            Array.Copy(content, e, result, s + nameBytes.Length, content.Length - e);
            content = result;
        }
        return content;
    }

    /// <summary>Expand <paramref name="used"/> with the /XObject names that a used
    /// image references through its /SMask or /Mask — a soft mask is part of the
    /// image even though no content stream names it directly, so pruning it would
    /// drop a glyph/picture's transparency. Iterates to a fixpoint for mask chains.</summary>
    private void AddReferencedXObjectNames(PdfDictionary resources, HashSet<string> used)
    {
        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;

        // Map each entry's underlying stream object to its name so a mask referenced
        // by object can be matched back to the /XObject key the test inspects.
        var streamToName = new Dictionary<PdfStream, string>(ReferenceEqualityComparer.Instance);
        foreach (var name in xobjects.Keys)
            if (_reader.ResolveStream(xobjects.Get(name)) is { } s)
                streamToName[s] = name;

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var name in used.ToList())
            {
                if (_reader.ResolveStream(xobjects.Get(name)) is not { } s) continue;
                foreach (var maskKey in new[] { "SMask", "Mask" })
                    if (_reader.ResolveStream(s.Dict.Get(maskKey)) is { } mask
                        && streamToName.TryGetValue(mask, out var maskName)
                        && used.Add(maskName))
                        changed = true;
            }
        }
    }

    /// <summary>Add every /Name token in <paramref name="content"/> to
    /// <paramref name="used"/>, then recurse through the form XObjects it invokes so
    /// names referenced only inside a nested form (or via its parent-resource
    /// fallback) are counted as used too.</summary>
    private void CollectContentResourceNames(byte[] content, PdfDictionary resources,
        HashSet<string> used, HashSet<PdfDictionary> visitedForms)
    {
        var localNames = new List<string>();
        var lexer = new IO.PdfLexer(content);
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) break;
            // Inline images carry raw binary between ID and EI that must not be
            // tokenised — left unskipped it desyncs the lexer and the /Name tokens
            // after it (e.g. later `Do` references) are missed, pruning live images.
            if (token.Kind == IO.TokenKind.Keyword && token.StringValue == "BI")
            {
                SkipInlineImage(lexer, used);
                continue;
            }
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } name && used.Add(name))
                localNames.Add(name);
        }

        var xobjects = _reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is null) return;
        foreach (var name in localNames)
        {
            var xstream = _reader.ResolveStream(xobjects.Get(name));
            if (xstream is null || xstream.Dict.GetName("Subtype") != "Form") continue;
            if (!visitedForms.Add(xstream.Dict)) continue; // cycle / shared-form guard
            var formContent = _reader.DecodeStream(xstream);
            if (formContent.Length == 0) continue;
            // A form may declare its own /Resources; absent, it inherits the page's.
            var formRes = _reader.ResolveDict(xstream.Dict.Get("Resources")) ?? resources;
            CollectContentResourceNames(formContent, formRes, used, visitedForms);
        }
    }

    /// <summary>Consume an inline image (the lexer has just read its <c>BI</c>):
    /// collect any /Name values among its parameters — an inline image's <c>/CS</c>
    /// may name a colour space declared in /Resources/ColorSpace — then skip the raw
    /// image bytes up to and including the <c>EI</c> terminator.</summary>
    private static void SkipInlineImage(IO.PdfLexer lexer, HashSet<string> used)
    {
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) return;
            if (token.Kind == IO.TokenKind.Keyword && token.StringValue == "ID") break;
            if (token.Kind == IO.TokenKind.Name && token.StringValue is { } name)
                used.Add(name);
        }
        lexer.ReadInlineImageData();
    }

    /// <summary>Remove entries not present in <paramref name="used"/> from each
    /// prunable sub-dictionary of <paramref name="resources"/>.</summary>
    private void PruneResourceCategories(PdfDictionary resources, HashSet<string> used)
    {
        foreach (var category in PrunableResourceCategories)
        {
            var dict = _reader.ResolveDict(resources.Get(category));
            if (dict is null) continue;
            var unused = dict.Keys.Where(k => !used.Contains(k)).ToList();
            foreach (var key in unused)
                dict.Remove(key);
            if (!dict.Keys.Any())
                resources.Remove(category);
        }
    }

    /// <summary>
    /// Find streams with identical content and redirect duplicate references to a single canonical object.
    /// This reduces file size when the same content (e.g., images) appears multiple times.
    /// </summary>
    private void LinkDuplicateStreams()
    {
        // Phase 1: Hash all stream objects
        var hashToObjNum = new Dictionary<string, int>(StringComparer.Ordinal);
        var redirections = new Dictionary<int, int>(); // oldObjNum → canonicalObjNum

        foreach (var entry in _reader.XRefTable.Entries.Values)
        {
            if (!entry.InUse || entry.ObjectNumber == 0) continue;

            var obj = _reader.Resolve(new PdfIndirectRef(entry.ObjectNumber, entry.Generation));
            if (obj is not PdfStream stream) continue;

            // Decode the stream data for content comparison
            byte[] decoded;
            try
            {
                decoded = StreamFilter.Decode(stream.RawData, stream.Dict);
            }
            catch
            {
                continue; // Skip streams that fail to decode
            }

            // Build a hash that includes stream properties (width/height/colorspace for images)
            var hash = System.Convert.ToHexString(Security.ShaDigest.Sha256(decoded));

            // Append key properties to distinguish structurally different streams
            var width = stream.Dict.GetInt("Width");
            var height = stream.Dict.GetInt("Height");
            if (width > 0) hash += $"_W{width}_H{height}";

            if (hashToObjNum.TryGetValue(hash, out var canonicalObjNum))
            {
                redirections[entry.ObjectNumber] = canonicalObjNum;
            }
            else
            {
                hashToObjNum[hash] = entry.ObjectNumber;
            }
        }

        if (redirections.Count == 0) return;

        // Phase 2: Replace indirect references throughout the document
        RedirectReferences(_reader.Catalog, redirections);

        // Also redirect in each page's annotations and resources
        foreach (var page in Pages)
        {
            RedirectReferences(page.Dict, redirections);
        }
    }

    /// <summary>
    /// Recursively replace indirect references in a dictionary tree.
    /// </summary>
    private void RedirectReferences(PdfDictionary dict, Dictionary<int, int> redirections)
    {
        foreach (var key in dict.Keys.ToList())
        {
            var value = dict.Get(key);
            switch (value)
            {
                case PdfIndirectRef iref when redirections.TryGetValue(iref.ObjectNumber, out var newObjNum):
                    dict.Set(key, new PdfIndirectRef(newObjNum, 0));
                    break;
                case PdfDictionary childDict:
                    RedirectReferences(childDict, redirections);
                    break;
                case PdfArray arr:
                    RedirectReferencesInArray(arr, redirections);
                    break;
            }
        }
    }

    private void RedirectReferencesInArray(PdfArray arr, Dictionary<int, int> redirections)
    {
        for (var i = 0; i < arr.Count; i++)
        {
            switch (arr[i])
            {
                case PdfIndirectRef iref when redirections.TryGetValue(iref.ObjectNumber, out var newObjNum):
                    arr.ReplaceAt(i, new PdfIndirectRef(newObjNum, 0));
                    break;
                case PdfDictionary childDict:
                    RedirectReferences(childDict, redirections);
                    break;
                case PdfArray nested:
                    RedirectReferencesInArray(nested, redirections);
                    break;
            }
        }
    }

    // ── PDF/A conversion ────────────────────────────────────────────────────

    /// <summary>
    /// Convert the document to the specified PDF/A conformance level.
    /// Applies automatic fixes for common violations when ErrorAction is Delete.
    /// Returns true if the document was successfully made compliant (or all fixable issues were addressed).
    /// </summary>
    public bool Convert(PdfFormatConversionOptions options)
    {
        // Converting to PDF 2.0 with a pending RC4 encryption cannot succeed —
        // 2.0 removes the RC4 crypt filters (ISO 32000-2 §7.6) — so the request
        // is refused instead of producing a file that violates its own header.
        if (options.TargetFormat == Aspose.Pdf.PdfFormat.v_2_0 &&
            _encryptor?.Algorithm is Aspose.Pdf.CryptoAlgorithm.RC4x40 or Aspose.Pdf.CryptoAlgorithm.RC4x128)
            throw new DeprecatedFeatureException(
                "RC4 encryption is deprecated in PDF 2.0; re-encrypt with AES-256 before converting.");

        // A permission-restricted document (user access, modify-contents withheld)
        // refuses conformance conversion the same way validation does: the log
        // carries the single permission problem and the conversion reports failure.
        // Plain version targets are not conformance work and stay allowed.
        if (PdfAConversionBlockedByPermissions &&
            options.TargetFormat is not (Aspose.Pdf.PdfFormat.v_1_0 or Aspose.Pdf.PdfFormat.v_1_1
                or Aspose.Pdf.PdfFormat.v_1_2 or Aspose.Pdf.PdfFormat.v_1_3 or Aspose.Pdf.PdfFormat.v_1_4
                or Aspose.Pdf.PdfFormat.v_1_5 or Aspose.Pdf.PdfFormat.v_1_6 or Aspose.Pdf.PdfFormat.v_1_7
                or Aspose.Pdf.PdfFormat.v_2_0 or Aspose.Pdf.PdfFormat.Pdf))
        {
            var blocked = Optimization.PdfAValidator.PermissionBlocked(options.TargetFormat);
            if (!string.IsNullOrEmpty(options.LogFileName))
            {
                using var fs = File.Create(options.LogFileName);
                using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
                WriteValidationLogXml(writer, options.TargetFormat, blocked, "Conversion");
            }
            else if (options.LogStream is not null)
            {
                using var writer = new StreamWriter(options.LogStream, System.Text.Encoding.UTF8, leaveOpen: true);
                WriteValidationLogXml(writer, options.TargetFormat, blocked, "Conversion");
            }
            return false;
        }

        var result = ConvertInternal(options);

        // Remember last successful conversion target so IsPdfaCompliant / PdfFormat
        // can report the converted state in-memory (tests run the assertion on the
        // same Document instance after Convert + Save).
        if (result) _lastConvertedFormat = options.TargetFormat;

        // Write log if requested — the conversion log shares the validation log's
        // Compliance/File/Problem schema (the shape the corpus parses; the Clause
        // and Convertable attributes ride on each Problem).
        var logResult = new Optimization.PdfAValidationResult
        {
            IsValid = result,
            Format = options.TargetFormat,
            Violations = options.ConversionLog,
        };
        if (!string.IsNullOrEmpty(options.LogFileName))
        {
            using var fs = File.Create(options.LogFileName);
            using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
            WriteValidationLogXml(writer, options.TargetFormat, logResult, "Conversion");
        }
        else if (options.LogStream is not null && options.ConversionLog.Count > 0)
        {
            using var writer = new StreamWriter(options.LogStream, System.Text.Encoding.UTF8, leaveOpen: true);
            WriteValidationLogXml(writer, options.TargetFormat, logResult, "Conversion");
        }

        return result;
    }

    private Aspose.Pdf.PdfFormat? _lastConvertedFormat;

    /// <summary>True once a PDF/A conversion succeeded on this instance. Validation
    /// of file-structure rules that conversion repairs on save (e.g. the PDF/A-1
    /// xref-stream prohibition) treats the document as already fixed.</summary>
    internal bool PdfAConversionApplied => _lastConvertedFormat is not null;

    /// <summary>
    /// Convert the document to a specific PDF/A format with a log file.
    /// </summary>
    public bool Convert(string outputLogFileName, PdfFormat format, ConvertErrorAction action)
    {
        var options = new PdfFormatConversionOptions(format, action) { LogFileName = outputLogFileName };
        return Convert(options);
    }

    /// <summary>
    /// Convert the document to a specific PDF/A format with a log file, specifying transparency handling.
    /// </summary>
    public bool Convert(string outputLogFileName, PdfFormat format, ConvertErrorAction action, ConvertTransparencyAction transparencyAction)
    {
        var options = new PdfFormatConversionOptions(format, action)
        {
            LogFileName = outputLogFileName,
            TransparencyAction = transparencyAction,
        };
        return Convert(options);
    }

    /// <summary>
    /// Convert the document to a specific PDF/A format with a log stream.
    /// </summary>
    public bool Convert(Stream outputLogStream, PdfFormat format, ConvertErrorAction action)
    {
        var options = new PdfFormatConversionOptions(format, action) { LogStream = outputLogStream };
        return Convert(options);
    }

    /// <summary>Stream-log overload with explicit transparency action.</summary>
    public bool Convert(Stream outputLogStream, PdfFormat format, ConvertErrorAction action, ConvertTransparencyAction transparencyAction)
    {
        var options = new PdfFormatConversionOptions(format, action)
        {
            LogStream = outputLogStream,
            TransparencyAction = transparencyAction,
        };
        return Convert(options);
    }

    // The 4 public static Convert(src, LoadOptions, dst, SaveOptions)
    // overloads are deferred: in this FOSS branch HtmlLoadOptions /
    // MdLoadOptions / SvgLoadOptions don't derive from LoadOptions, so a
    // typed `LoadOptions` parameter can't compile-time dispatch into the
    // real HTML/SVG/MD Open overloads. Adding them as
    // `Open(srcFileName) + Save(dstFileName)` passthroughs would silently
    // drop the options — exactly the kind of stub we don't want.
    // Re-add after unifying the load-options hierarchy.

    private static void WriteConversionLog(Stream output, PdfFormatConversionOptions options, string sourceVersion = "1.7")
    {
        using var writer = new StreamWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        writer.WriteLine($"<ConversionLog Operation=\"Conversion\" From=\"{sourceVersion}\" To=\"{GetVersionString(options.TargetFormat)}\">");
        foreach (var v in options.ConversionLog)
        {
            writer.WriteLine($"  <Violation Rule=\"{EscapeXml(v.Rule)}\">{EscapeXml(v.Description)}</Violation>");
        }
        writer.WriteLine("</ConversionLog>");
    }

    private static string GetVersionString(PdfFormat format) => format switch
    {
        PdfFormat.v_1_0 => "1.0",
        PdfFormat.v_1_1 => "1.1",
        PdfFormat.v_1_2 => "1.2",
        PdfFormat.v_1_3 => "1.3",
        PdfFormat.v_1_4 => "1.4",
        PdfFormat.v_1_5 => "1.5",
        PdfFormat.v_1_6 => "1.6",
        PdfFormat.v_1_7 => "1.7",
        PdfFormat.v_2_0 => "2.0",
        _ => format.ToString(),
    };

    private static string EscapeXml(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>
    /// Internal conversion implementation.
    /// </summary>
    /// <summary>Round content-stream real literals whose magnitude exceeds the PDF/A-1
    /// implementation limit (32767) and that carry a fractional part, to plain integers.
    /// String (…), hex &lt;…&gt; and comment regions are left untouched. Returns the
    /// rewritten bytes, or null when nothing needed changing.</summary>
    private static byte[]? RoundOutOfRangeReals(byte[] content)
    {
        var outBytes = new List<byte>(content.Length + 16);
        bool changed = false;
        int i = 0, n = content.Length;
        void CopyRange(int from, int to) { for (var k = from; k < to; k++) outBytes.Add(content[k]); }
        while (i < n)
        {
            byte b = content[i];
            if (b == (byte)'(')
            {
                // literal string: copy verbatim honouring escapes + nesting
                int depth = 0, start = i;
                while (i < n)
                {
                    byte sc = content[i];
                    if (sc == (byte)'\\' && i + 1 < n) { i += 2; continue; }
                    if (sc == (byte)'(') depth++;
                    else if (sc == (byte)')' && --depth == 0) { i++; break; }
                    i++;
                }
                CopyRange(start, i);
                continue;
            }
            if (b == (byte)'<' && i + 1 < n && content[i + 1] != (byte)'<')
            {
                int start = i;
                while (i < n && content[i] != (byte)'>') i++;
                if (i < n) i++;
                CopyRange(start, i);
                continue;
            }
            if (b == (byte)'%')
            {
                int start = i;
                while (i < n && content[i] != (byte)'\n' && content[i] != (byte)'\r') i++;
                CopyRange(start, i);
                continue;
            }
            // Inline image: copy BI … EI verbatim — the raw sample bytes after ID
            // must never be scanned as tokens.
            if (b == (byte)'B' && i + 1 < n && content[i + 1] == (byte)'I'
                && (i == 0 || IsPdfDelimiterOrWs(content[i - 1]))
                && (i + 2 >= n || IsPdfDelimiterOrWs(content[i + 2])))
            {
                int start = i;
                i += 2;
                while (i + 1 < n
                       && !(content[i] == (byte)'E' && content[i + 1] == (byte)'I'
                            && (i + 2 >= n || IsPdfDelimiterOrWs(content[i + 2]))
                            && IsPdfDelimiterOrWs(content[i - 1])))
                    i++;
                i = Math.Min(n, i + 2);
                CopyRange(start, i);
                continue;
            }
            if (b == (byte)'-' || b == (byte)'+' || b == (byte)'.' || (b >= (byte)'0' && b <= (byte)'9'))
            {
                int start = i;
                bool hasDot = false;
                if (b == (byte)'-' || b == (byte)'+') i++;
                while (i < n)
                {
                    byte nc = content[i];
                    if (nc >= (byte)'0' && nc <= (byte)'9') { i++; continue; }
                    if (nc == (byte)'.' && !hasDot) { hasDot = true; i++; continue; }
                    break;
                }
                if (hasDot)
                {
                    var token = System.Text.Encoding.ASCII.GetString(content, start, i - start);
                    if (double.TryParse(token, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var v)
                        && Math.Abs(v) >= 32767 && v != Math.Truncate(v))
                    {
                        var repl = Math.Round(v).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                        foreach (var rb in System.Text.Encoding.ASCII.GetBytes(repl)) outBytes.Add(rb);
                        changed = true;
                        continue;
                    }
                }
                CopyRange(start, i);
                continue;
            }
            outBytes.Add(b);
            i++;
        }
        return changed ? outBytes.ToArray() : null;
    }

    private static bool IsPdfDelimiterOrWs(byte c) =>
        c is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or (byte)'\f' or 0
          or (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']'
          or (byte)'/' or (byte)'%';

    /// <summary>Walk the page-tree /Parent chain and return the RAW (unresolved) value
    /// of an inheritable page attribute — an indirect reference is returned as-is so a
    /// materialised entry shares the ancestor's object instead of copying it.</summary>
    private PdfObject? FindInheritedRaw(PdfDictionary pageDict, string name)
    {
        var parentObj = pageDict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
            var parent = _reader.ResolveDict(parentObj);
            if (parent is null) break;
            var value = parent.Get(name);
            if (value is not null and not PdfNull) return value;
            parentObj = parent.Get("Parent");
        }
        return null;
    }
}
