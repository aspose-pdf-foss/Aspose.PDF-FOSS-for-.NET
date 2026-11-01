using System.Collections.Generic;
using System.Globalization;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Stamps the content of one PDF page onto another page.
/// The source page is drawn as a Form XObject at the specified position.
/// </summary>
public sealed partial class PdfPageStamp : Stamp
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

    /// <summary>How far the imported form's box reaches past the source page on every side.
    /// A stroke drawn on the page's very edge (an SVG's viewport rectangle) is half outside
    /// the box and clipped by it; the reference draws such artwork onto the page itself, where
    /// that edge stroke shows in full.</summary>
    internal double BBoxOutsetPt { get; set; }

    // Font entries hoisted out of the (shared) imported form, re-applied to every
    // target page the stamp lands on.
    private List<(string Name, PdfObject Font)>? _promotedFonts;

    /// <summary>Whether ApplyTo also imports the source page's annotations onto
    /// the target page (cross-document stamps only). Callers that carry
    /// annotations themselves — MakeNUp transforms them to the placed tile's
    /// geometry — turn this off to avoid double-adding.</summary>
    internal bool CarryAnnotations { get; set; } = true;

    /// <summary>Draw the source page as stored - its whole media box, unturned - for a caller that draws
    /// over it in the page's own media coordinates (the side-by-side comparison sheet).</summary>
    internal bool KeepsMediaFrame { get; set; }

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
        (Width, Height) = ShownPageSize(pdfPage);
    }

    private const int QuarterTurn = 90;
    private const int HalfTurn = 180;
    private const int ThreeQuarterTurn = 270;

    /// <summary>The part of the page a viewer shows: its crop box, cut to the media box.</summary>
    private static Rectangle VisibleBox(Page page)
    {
        var media = page.MediaBox;
        var visible = page.CropBox.Intersect(media);
        return visible.IsEmpty ? media : visible;
    }

    /// <summary>The size <paramref name="box"/> shows at: a quarter-turned page shows it on its side.</summary>
    private static (double Width, double Height) ShownSize(int rotation, Rectangle box)
        => rotation is QuarterTurn or ThreeQuarterTurn ? (box.Height, box.Width) : (box.Width, box.Height);

    /// <summary>The size a page is stamped at by default: its visible box as a viewer shows it.</summary>
    internal static (double Width, double Height) ShownPageSize(Page page) => ShownSize(page.RotationQuarter, VisibleBox(page));

    /// <summary>The form /Matrix that turns the page's visible box the way its /Rotate shows it and puts
    /// the box's displayed lower-left corner at the form origin; null for an unturned page, whose form
    /// keeps the page's own coordinates.</summary>
    private static PdfArray? ShownFormMatrix(Rectangle box, int rotation)
    {
        if (ShownTurn(box, rotation) is not { } m) return null;
        var array = new PdfArray();
        foreach (var v in m) array.Add(new PdfReal(v));
        return array;
    }

    private static double[]? ShownTurn(Rectangle box, int rotation) => rotation switch
    {
        QuarterTurn => [0, -1, 1, 0, -box.LLY, box.URX],
        HalfTurn => [-1, 0, 0, -1, box.URX, box.URY],
        ThreeQuarterTurn => [0, 1, -1, 0, box.URY, -box.LLX],
        _ => null,
    };

    /// <summary>The matrix (a b c d e f) a turned page's stamp form draws through, for callers that place the
    /// page's annotations with it; null for an unturned page.</summary>
    internal static double[]? ShownTurn(Page page) => ShownTurn(VisibleBox(page), page.RotationQuarter);

    /// <summary>Create a PdfPageStamp from page <paramref name="pageIndex"/>
    /// (1-based) of the PDF at <paramref name="fileName"/>.</summary>
    public PdfPageStamp(string fileName, int pageIndex)
        : this(Document.Open(File.ReadAllBytes(fileName)).Pages.At(pageIndex)) { }

    /// <summary>Create a PdfPageStamp from page <paramref name="pageIndex"/>
    /// (1-based) of the PDF read from <paramref name="stream"/>.</summary>
    public PdfPageStamp(Stream stream, int pageIndex)
        : this(Document.Open(ReadStream(stream)).Pages.At(pageIndex)) { }

    /// <summary>Alias for <see cref="ApplyTo"/> matching the public surface.</summary>
    public override void Put(Page page) => ApplyTo(page);

    private static byte[] ReadStream(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A matrix coefficient in the content stream's number format.</summary>
    private static string Fmt(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    internal override byte[] BuildContentStream(Page page)
    {
        var pg = new PageStampContentState();
        pg.targetPage = page;
        pg.sourcePage = _sourcePage;
        pg.sourceReader = _sourceReader;
        // Paragraphs and a background a save has not laid out yet are stamped too, as a device draws them:
        // the pass runs once per page.
        if (!pg.sourcePage.LayoutApplied && (pg.sourcePage.Paragraphs is { Count: > 0 } || pg.sourcePage.ExplicitBackground is not null))
            pg.sourcePage.ProcessParagraphs();

        pg.sourceContent = GetPageContent(pg.sourcePage.Dict, pg.sourceReader);
        if (pg.sourceContent.Length == 0) return [];

        // A caller drawing over the page in its own media coordinates keeps the page as it is stored.
        pg.mb = KeepsMediaFrame ? pg.sourcePage.MediaBox : VisibleBox(pg.sourcePage);
        pg.srcRot = KeepsMediaFrame ? 0 : pg.sourcePage.RotationQuarter;
        (pg.shownWidth, pg.shownHeight) = ShownSize(pg.srcRot, pg.mb);
        pg.targetReader = pg.targetPage.Reader;
        pg.targetDoc = pg.targetReader.OwnerDocument;

        if (pg.targetDoc is not null && _importedForm.TryGetValue(pg.targetDoc, out var sharedRef))
        {
            pg.formObject = sharedRef;
        }
        else
        {
            ImportSourceForm(pg);
        }

        var targetResources = pg.targetReader.ResolveDict(pg.targetPage.Dict.Get("Resources"));
        if (targetResources is null)
        {
            targetResources = new PdfDictionary();
            pg.targetPage.Dict.Set("Resources", targetResources);
        }
        pg.targetResources = targetResources;

        var xobjectDict = pg.targetReader.ResolveDict(pg.targetResources.Get("XObject"));
        if (xobjectDict is null)
        {
            xobjectDict = new PdfDictionary();
            pg.targetResources.Set("XObject", xobjectDict);
        }
        pg.xobjectDict = xobjectDict;

        // Re-apply the hoisted stamp fonts to this target page's /Resources/Font
        // (fresh names are NOT invented: the source names are kept,
        // e.g. F1/F2; existing page entries win on collision).
        PromoteFormFonts(pg);

        pg.xobjName = "Fm0";
        pg.counter = 0;
        while (pg.xobjectDict.ContainsKey(pg.xobjName))
            pg.xobjName = $"Fm{++pg.counter}";

        pg.xobjectDict.Set(pg.xobjName, pg.formObject);

        pg.sx = Width / pg.shownWidth * ZoomX;
        pg.sy = Height / pg.shownHeight * ZoomY;

        ComputeStampPlacement(pg);

        // The stamp's opacity rides the graphics state the form is drawn under.
        pg.gsName = pg.targetPage.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
        if (Opacity < FullOpacity) DrawFormAsTransparencyGroup(pg);
        pg.idComment = StampId != 0 ? $"%StampId={StampId}\n" : "";
        // A tag with no property list opens marked content with BMC; BDC takes both.
        pg.content = $"/Artifact BMC\n{pg.idComment}q\n/{pg.gsName} gs\n{pg.matrix} cm\n/{pg.xobjName} Do\nQ\nEMC\n";
        return System.Text.Encoding.ASCII.GetBytes(pg.content);
    }

    private const double FullOpacity = 1.0;

    /// <summary>Make the stamped page's form a transparency group (PDF 32000 §11.6.6), so it is
    /// composited as one object under the stamp's alpha. Drawn as a plain form, its content's own
    /// graphics states - a page converted from a word processor sets ca 1 - replaced the alpha the
    /// stamp set, and a stamp at Opacity 0.05 came out opaque. A group drawn at full opacity paints
    /// as the plain form did, so the form shared by every application of the stamp keeps it.</summary>
    private static void DrawFormAsTransparencyGroup(PageStampContentState pg)
    {
        var form = pg.formObject as PdfStream ?? pg.targetReader.ResolveStream(pg.formObject);
        if (form is null || form.Dict.ContainsKey("Group")) return;
        var group = new PdfDictionary();
        group.Set("Type", new PdfName("Group"));
        group.Set("S", new PdfName("Transparency"));
        form.Dict.Set("Group", group);
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
            // The stamp goes in as a stream of its own ahead of the page's content,
            // as Page.AddStamp puts a background. Rewriting /Contents as one stream
            // of stamp and content lost the stamp on a page drawn through
            // page.Contents: the operators held there are the page's content, and
            // saving wrote them over the stream that had taken their place.
            var background = new byte[stampBytes.Length + 1];
            stampBytes.CopyTo(background, 0);
            background[stampBytes.Length] = (byte)'\n';
            page.PrependContentStream(background);
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
