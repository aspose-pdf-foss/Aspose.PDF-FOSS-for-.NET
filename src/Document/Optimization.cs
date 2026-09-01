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
    // ── SendTo (DocumentDevice routing) ────────────────────────────

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
    internal bool HasCertificationSignature()
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

    private Aspose.Pdf.Optimization.OptimizationOptions? _optimizationOptions;

    private HashSet<int>? _reachableObjects;

    private bool _prunedFontsThisSave;

    // ── PDF/A conversion ────────────────────────────────────────────────────

    /// <summary>
    /// Convert the document to the specified PDF/A conformance level.
    /// Applies automatic fixes for common violations when ErrorAction is Delete.
    /// Returns true if the document was successfully made compliant (or all fixable issues were addressed).
    /// </summary>
    /// <summary>True for a CONFORMANCE target (PDF/A, PDF/X, PDF/UA …) as opposed to a
    /// plain version target. Only a conformance target carries the embed-everything
    /// requirement that overrides a face's licence.</summary>
    private static bool IsConformanceTarget(Aspose.Pdf.PdfFormat format) =>
        format is not (Aspose.Pdf.PdfFormat.v_1_0 or Aspose.Pdf.PdfFormat.v_1_1
            or Aspose.Pdf.PdfFormat.v_1_2 or Aspose.Pdf.PdfFormat.v_1_3 or Aspose.Pdf.PdfFormat.v_1_4
            or Aspose.Pdf.PdfFormat.v_1_5 or Aspose.Pdf.PdfFormat.v_1_6 or Aspose.Pdf.PdfFormat.v_1_7
            or Aspose.Pdf.PdfFormat.v_2_0 or Aspose.Pdf.PdfFormat.Pdf);

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

        // A LIVE digital signature refuses conformance conversion outright: the
        // conversion rewrites the file and would break the signature's byte range.
        // The log carries the single Catalog-section problem with the document's
        // permanent file ID as its ObjectID (probed), and Convert reports failure
        // without touching the document. Plain version targets stay allowed.
        if (IsConformanceTarget(options.TargetFormat) && Forms.Signature.HasAny(this))
        {
            var refused = Optimization.PdfAValidator.SignedFileBlocked(
                options.TargetFormat, PermanentFileIdHex());
            if (!string.IsNullOrEmpty(options.LogFileName))
            {
                using var fs = File.Create(options.LogFileName);
                using var writer = new StreamWriter(fs, System.Text.Encoding.UTF8);
                WriteValidationLogXml(writer, options.TargetFormat, refused, "Conversion");
            }
            else if (options.LogStream is not null)
            {
                using var writer = new StreamWriter(options.LogStream, System.Text.Encoding.UTF8, leaveOpen: true);
                WriteValidationLogXml(writer, options.TargetFormat, refused, "Conversion");
            }
            return false;
        }

        // A conformance target REQUIRES every face to travel with the file, so it
        // overrides a face's own embedding licence: the format outranks the permission
        // (probed - a licence-restricted face embeds cleanly under PDF/A and is refused
        // on an ordinary save). The override STAYS with the document: paragraph content
        // materialises lazily, so a caller that converts and then reads the page's
        // resources writes its text after this call has returned.
        if (IsConformanceTarget(options.TargetFormat)) EmbeddingLicenceOverridden = true;
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

    /// <summary>The document's permanent file ID (first trailer /ID entry) as
    /// uppercase hex, or null when the trailer carries no /ID — the value the
    /// signed-file refusal stamps as the log problem's ObjectID.</summary>
    private string? PermanentFileIdHex()
    {
        var id = Id?.Original;
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private Aspose.Pdf.PdfFormat? _lastConvertedFormat;

    /// <summary>True once a PDF/A conversion succeeded on this instance. Validation
    /// of file-structure rules that conversion repairs on save (e.g. the PDF/A-1
    /// xref-stream prohibition) treats the document as already fixed.</summary>
    internal bool PdfAConversionApplied => _lastConvertedFormat is not null;

    /// <summary>The last conformance format a successful Convert applied on this
    /// instance, for validation rules that treat conversion-repaired dimensions
    /// as fixed (see <see cref="PdfAConversionApplied"/>).</summary>
    internal Aspose.Pdf.PdfFormat? LastConvertedFormat => _lastConvertedFormat;

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
