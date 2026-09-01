using System.Collections.Generic;
using System.Globalization;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Stamps;

namespace Aspose.Pdf;

/// <summary>
/// Stamps the content of one PDF page onto another page.
/// The source page is drawn as a Form XObject at the specified position.
/// </summary>
public sealed class PdfPageStamp : Aspose.Pdf.Stamps.Stamp
{
    private Page _sourcePage;
    private PdfReader _sourceReader;

    // The imported source-page Form XObject, registered once per target document. When a
    // single stamp instance is applied to many target pages (Stamp.Pages spanning a whole
    // document), every page references this one shared indirect object instead of importing
    // a fresh copy of the source page's resource graph — otherwise the output grows by the
    // full form size per page (a 100-page stamp ballooned to tens of MB).
    private readonly Dictionary<Document, PdfIndirectRef> _importedForm = new();

    /// <summary>When set (the PdfFileStamp facade path), the imported form's /Font
    /// entries are hoisted to the TARGET page's /Resources/Font and removed from the
    /// form's own resources — the expected layout, where the stamped page
    /// exposes the stamp's fonts (F1/F2…) at page level and the form inherits them.</summary>
    internal bool PromoteFontsToPage { get; set; }

    // Font entries hoisted out of the (shared) imported form, re-applied to every
    // target page the stamp lands on.
    private List<(string Name, PdfObject Font)>? _promotedFonts;

    /// <summary>Whether ApplyTo also imports the source page's annotations onto
    /// the target page (cross-document stamps only). Callers that carry
    /// annotations themselves — MakeNUp transforms them to the placed tile's
    /// geometry — turn this off to avoid double-adding.</summary>
    internal bool CarryAnnotations { get; set; } = true;

    /// <summary>Width of the stamp in points. Defaults to source page width.</summary>
    public double Width { get; set; }

    /// <summary>Height of the stamp in points. Defaults to source page height.</summary>
    public double Height { get; set; }

    /// <summary>The source page being stamped.</summary>
    public Page PdfPage
    {
        get => _sourcePage;
        set
        {
            _sourcePage = value;
            if (value is not null) _sourceReader = value.Reader;
        }
    }

    /// <summary>
    /// Create a PdfPageStamp from a page of another document.
    /// </summary>
    public PdfPageStamp(Page pdfPage)
    {
        _sourcePage = pdfPage;
        _sourceReader = pdfPage.Reader;
        Width = pdfPage.Width;
        Height = pdfPage.Height;
    }

    /// <summary>Alias for <see cref="ApplyTo"/> matching the public surface.</summary>
    public void Put(Page page) => ApplyTo(page);

    /// <summary>Create a PdfPageStamp from page <paramref name="pageIndex"/>
    /// (1-based) of the PDF at <paramref name="fileName"/>.</summary>
    public PdfPageStamp(string fileName, int pageIndex)
        : this(Document.Open(File.ReadAllBytes(fileName)).Pages.At(pageIndex)) { }

    /// <summary>Create a PdfPageStamp from page <paramref name="pageIndex"/>
    /// (1-based) of the PDF read from <paramref name="stream"/>.</summary>
    public PdfPageStamp(Stream stream, int pageIndex)
        : this(Document.Open(ReadStream(stream)).Pages.At(pageIndex)) { }

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    internal override byte[] BuildContentStream(Page targetPage)
    {
        var sourcePage = _sourcePage;
        var sourceReader = _sourceReader;

        // Get source page content
        var sourceContent = GetPageContent(sourcePage.Dict, sourceReader);
        if (sourceContent.Length == 0) return [];

        var mb = sourcePage.MediaBox;
        var targetReader = targetPage.Reader;
        var targetDoc = targetReader.OwnerDocument;

        // Build (or reuse) the source-page Form XObject. When the same stamp is applied to
        // several pages of one target document the form is imported once and shared via a
        // single indirect reference; each page's /XObject entry points at it.
        PdfObject formObject;
        if (targetDoc is not null && _importedForm.TryGetValue(targetDoc, out var sharedRef))
        {
            formObject = sharedRef;
        }
        else
        {
            var formDict = new PdfDictionary();
            formDict.Set("Type", new PdfName("XObject"));
            formDict.Set("Subtype", new PdfName("Form"));

            var bbox = new PdfArray();
            bbox.Add(new PdfReal(mb.LLX));
            bbox.Add(new PdfReal(mb.LLY));
            bbox.Add(new PdfReal(mb.URX));
            bbox.Add(new PdfReal(mb.URY));
            formDict.Set("BBox", bbox);

            // Import the source page's resources into the TARGET document. The source
            // /Resources dictionary holds indirect references into the source document's
            // object table (fonts, ICC colour spaces, images, ExtGStates); copying them
            // verbatim would leave dangling references in the target. ImportDict resolves
            // the whole object graph against the source reader and re-registers it with
            // fresh object numbers in the target so the form is self-contained.
            var srcResources = ResolveEffectiveResources(sourcePage.Dict, sourceReader);
            if (srcResources is not null && targetDoc is not null)
                formDict.Set("Resources", targetDoc.ImportDict(srcResources, sourceReader,
                    targetDoc.GetSharedImportCloneMap(sourceReader)));
            else if (srcResources is not null)
                formDict.Set("Resources", srcResources);

            // Hoist the imported form's fonts for page-level promotion (facade path):
            // capture the /Font entries and strip the key from the form's resources so
            // the form inherits them from the page (the expected layout).
            if (PromoteFontsToPage)
            {
                var formRes = targetReader.ResolveDict(formDict.Get("Resources"))
                    ?? formDict.Get("Resources") as PdfDictionary;
                var formFonts = formRes is null ? null
                    : targetReader.ResolveDict(formRes.Get("Font")) ?? formRes.Get("Font") as PdfDictionary;
                if (formRes is not null && formFonts is not null)
                {
                    _promotedFonts = new List<(string, PdfObject)>();
                    foreach (var key in formFonts.Keys.ToList())
                    {
                        var v = formFonts.Get(key);
                        if (v is not null) _promotedFonts.Add((key, v));
                    }
                    formRes.Remove("Font");
                }
            }

            var formStream = new PdfStream(formDict, sourceContent);

            if (targetDoc is not null)
            {
                // Register the form as a single indirect object so repeated applications
                // (and the writer) reference one shared copy.
                var objNum = targetDoc.AllocateObjectNumber();
                targetDoc.AddNewObject(objNum, formStream, registerOverlay: true);
                var formRef = new PdfIndirectRef(objNum, 0);
                _importedForm[targetDoc] = formRef;
                formObject = formRef;
            }
            else
            {
                formObject = formStream;
            }
        }

        // Register the Form XObject in target page resources
        var targetResources = targetReader.ResolveDict(targetPage.Dict.Get("Resources"));
        if (targetResources is null)
        {
            targetResources = new PdfDictionary();
            targetPage.Dict.Set("Resources", targetResources);
        }

        var xobjectDict = targetReader.ResolveDict(targetResources.Get("XObject"));
        if (xobjectDict is null)
        {
            xobjectDict = new PdfDictionary();
            targetResources.Set("XObject", xobjectDict);
        }

        // Re-apply the hoisted stamp fonts to this target page's /Resources/Font
        // (fresh names are NOT invented: the source names are kept,
        // e.g. F1/F2; existing page entries win on collision).
        if (PromoteFontsToPage && _promotedFonts is { Count: > 0 })
        {
            var pageFonts = targetReader.ResolveDict(targetResources.Get("Font"))
                ?? targetResources.Get("Font") as PdfDictionary;
            if (pageFonts is null)
            {
                pageFonts = new PdfDictionary();
                targetResources.Set("Font", pageFonts);
            }
            foreach (var (name, font) in _promotedFonts)
                if (!pageFonts.ContainsKey(name))
                    pageFonts.Set(name, font);
        }

        // Find unique name for the form XObject on this page
        var xobjName = "Fm0";
        var counter = 0;
        while (xobjectDict.ContainsKey(xobjName))
            xobjName = $"Fm{++counter}";

        xobjectDict.Set(xobjName, formObject);

        // Build content stream to draw the form XObject
        var sx = Width / mb.Width * ZoomX;
        var sy = Height / mb.Height * ZoomY;

        // Stamp placement: alignment (with margins) when set; Left/Bottom keep the
        // legacy XIndent/YIndent placement so indent-positioned stamps are unchanged.
        // Negative/explicit indents pass through untouched; margins only kick in
        // when the indent is exactly unset (0) under the default alignment.
        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (targetPage.Width - Width * ZoomX) / 2 + LeftMargin - RightMargin,
            HorizontalAlignment.Right => targetPage.Width - Width * ZoomX - RightMargin - XIndent,
            HorizontalAlignment.Left => XIndent != 0 ? XIndent : LeftMargin,
            _ => XIndent,
        };
        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => targetPage.Height - Height * ZoomY - TopMargin - YIndent,
            VerticalAlignment.Center => (targetPage.Height - Height * ZoomY) / 2 + BottomMargin - TopMargin,
            VerticalAlignment.Bottom => YIndent != 0 ? YIndent : BottomMargin,
            _ => YIndent,
        };

        var f = (double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

        // Placement matrix. Normally axis-aligned (sx 0 0 sy X Y), but when the target
        // page is displayed rotated 90° the stamp must be rotated with it so it lands
        // upright in the displayed view — the /Rotate 90 is baked into
        // the matrix as (0 sx -sy 0  W-YIndent  XIndent), W being the page width.
        string matrix;
        var rot = ((targetPage.RotateDegrees % 360) + 360) % 360;
        if (rot == 90)
        {
            var tmb = targetPage.MediaBox;
            matrix = $"0 {f(sx)} {f(-sy)} 0 {f(tmb.Width - y)} {f(x)}";
        }
        else
        {
            // Indents are measured from the TARGET page's box origin (a page whose
            // MediaBox lower-left is not (0,0) still stamps at its visible corner),
            // and the SOURCE page's own box origin maps to the placement point.
            var tmb = targetPage.MediaBox;
            matrix = $"{f(sx)} 0 0 {f(sy)} {f(tmb.LLX + x - mb.LLX * sx)} {f(tmb.LLY + y - mb.LLY * sy)}";
        }

        // Draw the stamp form inside an /Artifact marked-content block with a default
        // graphics state: the overlay is a pagination
        // artifact, not real page content, and the leading `gs` resets the graphics
        // state so the stamp is isolated from whatever state the page content left.
        var gsName = targetPage.AddExtGState(new Content.ExtGState());
        // %StampId identifies the block to GetStamps/DeleteStampById; the parser
        // expects the comment immediately before the q that opens the stamp block.
        var idComment = StampId != 0 ? $"%StampId={StampId}\n" : "";
        var content =
            $"/Artifact BDC\n{idComment}q\n/{gsName} gs\n{matrix} cm\n/{xobjName} Do\nQ\nEMC\n";
        return System.Text.Encoding.ASCII.GetBytes(content);
    }

    /// <summary>
    /// Apply this stamp to a page. Overrides base to support Background mode.
    /// </summary>
    public void ApplyTo(Page page)
    {
        var stampBytes = BuildContentStream(page);
        if (stampBytes.Length == 0) return;
        // A session-stamped artifact belongs to THIS page alone — the flow's
        // continuation-page artifact copy must not repeat it (see
        // Page.SessionStampBlocks).
        page.SessionStampBlocks.Add(stampBytes);

        if (IsBackground)
        {
            // Prepend stamp content before existing page content. The page's
            // /Contents may be a single stream or an array of streams — decode
            // both so the underlying page content is preserved beneath the stamp.
            byte[] existingData = GetPageContent(page.Dict, page.Reader);

            var combined = new byte[stampBytes.Length + 1 + existingData.Length];
            stampBytes.CopyTo(combined, 0);
            combined[stampBytes.Length] = (byte)'\n';
            existingData.CopyTo(combined, stampBytes.Length + 1);

            page.SetContentStream(combined);
        }
        else
        {
            // Isolate the existing page content in its own q…Q so the appended stamp
            // starts from a clean graphics state (the original content is
            // bracketed before the stamp is overlaid).
            page.PrependContentStream("q\n"u8.ToArray());
            var wrapped = new byte[2 + stampBytes.Length];
            wrapped[0] = (byte)'Q';
            wrapped[1] = (byte)'\n';
            stampBytes.CopyTo(wrapped, 2);
            page.AddContentStream(wrapped);
        }

        if (CarryAnnotations)
            ImportSourceAnnotations(page);
    }

    /// <summary>A page stamp carries the stamped page's annotations onto the
    /// target page: each source /Annots entry is imported into the target
    /// document (fresh object numbers; the shared clone map de-duplicates
    /// objects across repeated imports) and appended to the target /Annots.
    /// Scoped to cross-document stamping — within one document the source
    /// annotations already live on their own page and re-homing would alias
    /// the same dictionaries onto two pages.</summary>
    private void ImportSourceAnnotations(Page target)
    {
        if (_sourcePage is null || _sourceReader is null || target.Reader is null) return;
        if (ReferenceEquals(_sourceReader, target.Reader)) return;
        if (_sourceReader.Resolve(_sourcePage.Dict.Get("Annots")) is not PdfArray srcAnnots
            || srcAnnots.Count == 0) return;
        var doc = target.Reader.OwnerDocument;
        if (doc is null) return;
        var map = doc.GetSharedImportCloneMap(_sourceReader);
        foreach (var item in srcAnnots)
        {
            var srcDict = _sourceReader.ResolveDict(item);
            if (srcDict is null) continue;
            var clone = doc.ImportDict(srcDict, _sourceReader, map);
            clone.Remove("P"); // re-homed: the old page ref must not survive the import
            target.Annotations.AddImportedDict(clone);
        }
    }

    /// <summary>Resolve a source page's effective /Resources, walking the /Parent chain when
    /// the page itself declares none (an inheritable attribute). Without this, a stamped page
    /// whose resources live on an ancestor /Pages node would import an empty resource graph,
    /// so the form's fonts/images would be missing.</summary>
    internal static PdfDictionary? ResolveEffectiveResources(PdfDictionary pageDict, PdfReader reader)
    {
        var res = reader.ResolveDict(pageDict.Get("Resources"));
        if (res is not null) return res;
        var parentObj = pageDict.Get("Parent");
        var visited = new HashSet<int>();
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef iref && !visited.Add(iref.ObjectNumber)) break;
            var parent = reader.ResolveDict(parentObj);
            if (parent is null) break;
            var pr = reader.ResolveDict(parent.Get("Resources"));
            if (pr is not null) return pr;
            parentObj = parent.Get("Parent");
        }
        return null;
    }

    internal static byte[] GetPageContent(PdfDictionary pageDict, PdfReader reader)
    {
        var obj = reader.Resolve(pageDict.Get("Contents"));
        if (obj is PdfStream stream) return reader.DecodeStream(stream);
        if (obj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }
}
