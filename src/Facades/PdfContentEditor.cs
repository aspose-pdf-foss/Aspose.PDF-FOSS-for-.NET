using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for content-level editing: text replacement, link creation, image operations.
/// Supports both stateless (byte[]-in / byte[]-out) and stateful (BindPdf / Save) modes.
/// </summary>
public sealed class PdfContentEditor : System.IDisposable
{
    // ── Document additional-action event-type constants ──
    // Values are the documented event names that
    // AddDocumentAdditionalAction accepts. They map to keys of the /AA dict in
    // the document catalog.
    public const string DocumentOpen       = "DO";
    public const string DocumentClose      = "WC";
    public const string DocumentWillSave   = "WS";
    public const string DocumentSaved      = "DS";
    public const string DocumentWillPrint  = "WP";
    public const string DocumentPrinted    = "DP";

    private Document? _document;
    private byte[]? _boundData;
    private bool _ownsDocument;

    /// <summary>
    /// The PDF document currently bound for editing.
    /// </summary>
    public Document Document => _document
        ?? throw new InvalidOperationException("No document bound. Call BindPdf first.");

    /// <summary>Default ctor — bind a PDF later via <see cref="BindPdf(string)"/>.</summary>
    public PdfContentEditor() { }

    /// <summary>Bind to an existing <see cref="Document"/> at construction.</summary>
    public PdfContentEditor(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _ownsDocument = false;
    }

    private TextReplaceOptions _textReplaceOptions = new TextReplaceOptions();
    private bool _textReplaceOptionsAssigned;

    /// <summary>Text-replacement options used by <see cref="ReplaceText(string,string)"/> family.</summary>
    public TextReplaceOptions TextReplaceOptions
    {
        get => _textReplaceOptions;
        set { _textReplaceOptions = value; _textReplaceOptionsAssigned = true; }
    }

    /// <summary>Text-edit options forwarded to the underlying replacement engine.</summary>
    public TextEditOptions TextEditOptions { get; set; } = new TextEditOptions(true);

    /// <summary>Text-search options used by the stateful text-replacement variants.</summary>
    public TextSearchOptions TextSearchOptions { get; set; } = new TextSearchOptions();

    // ── Stateful (BindPdf / Save) API ─────────────────────────────────────────

    /// <summary>
    /// Bind a PDF file for editing.
    /// </summary>
    public void BindPdf(byte[] pdfData)
    {
        _boundData = pdfData;
        _document = Document.Open(pdfData);
        _ownsDocument = true;
    }

    /// <summary>
    /// Bind a PDF file by path for editing.
    /// </summary>
    public void BindPdf(string inputFile)
    {
        _boundData = File.ReadAllBytes(inputFile);
        _document = Document.Open(_boundData);
        _ownsDocument = true;
    }

    /// <summary>
    /// Bind a PDF stream for editing.
    /// </summary>
    public void BindPdf(Stream inputStream)
    {
        using var ms = new MemoryStream();
        if (inputStream.CanSeek && inputStream.Position != 0) inputStream.Position = 0;
        inputStream.CopyTo(ms);
        _boundData = ms.ToArray();
        _document = Document.Open(_boundData);
        _ownsDocument = true;
    }

    /// <summary>
    /// Bind an in-memory PDF document for editing. The caller retains ownership of the document.
    /// </summary>
    public void BindPdf(Document srcDoc)
    {
        _document = srcDoc ?? throw new ArgumentNullException(nameof(srcDoc));
        _boundData = null;
        _ownsDocument = false;
    }

    /// <summary>
    /// Save the bound document.
    /// </summary>
    public byte[] Save()
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        // Editing operations (DeleteStamp/MoveStamp) replace a page's /Contents stream and
        // drop XObjects from /Resources, orphaning the previous content stream and the
        // removed image. Those orphans are still reachable in the source xref table, so
        // without a prune they are re-serialised and the file grows on every edit cycle.
        // Eliminate unreferenced objects (reachability sweep from the trailer) before
        // writing so the saved file shrinks after a stamp is removed.
        _document.OptimizeResources(new Aspose.Pdf.Optimization.OptimizationOptions
        {
            RemoveUnusedObjects = true,
            RemoveUnusedStreams = false,
            LinkDuplicateStreams = false,
        });
        var result = _document.ToArray();
        // Re-open so further operations work on the saved state.
        // Skip re-open when caller owns the document — they hold the reference.
        if (_ownsDocument)
        {
            _boundData = result;
            _document.Dispose();
            _document = Document.Open(result);
        }
        return result;
    }

    /// <summary>
    /// Save the bound document to a file path.
    /// </summary>
    public void Save(string path)
    {
        var bytes = Save();
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Save the bound document to a stream.
    /// </summary>
    public void Save(Stream stream)
    {
        var bytes = Save();
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Close the bound document.
    /// </summary>
    public void Close()
    {
        if (_ownsDocument)
            _document?.Dispose();
        _document = null;
        _boundData = null;
        _ownsDocument = false;
    }

    /// <summary>Releases the bound document (equivalent to <see cref="Close"/>).</summary>
    public void Dispose() => Close();

    // ── ViewerPreference (stateful) ─────────────────────────────────────────

    /// <summary>
    /// Get the current viewer preference flags as an integer bitmask.
    /// Use bitwise AND with ViewerPreference constants to check individual flags.
    /// </summary>
    public int GetViewerPreference()
    {
        var doc = EnsureBound();
        int result = 0;

        // PageMode (from catalog /PageMode)
        var pageMode = doc.Reader.Catalog.GetName("PageMode");
        result |= pageMode switch
        {
            "UseOutlines" => ViewerPreference.PageModeUseOutlines,
            "UseThumbs" => ViewerPreference.PageModeUseThumbs,
            "FullScreen" => ViewerPreference.PageModeFullScreen,
            "UseOC" => ViewerPreference.PageModeUseOC,
            "UseAttachments" => ViewerPreference.PageModeUseAttachment,
            _ => ViewerPreference.PageModeUseNone,
        };

        // PageLayout (from catalog /PageLayout)
        var pageLayout = doc.Reader.Catalog.GetName("PageLayout");
        result |= pageLayout switch
        {
            "SinglePage" => ViewerPreference.PageLayoutSinglePage,
            "OneColumn" => ViewerPreference.PageLayoutOneColumn,
            "TwoColumnLeft" => ViewerPreference.PageLayoutTwoColumnLeft,
            "TwoColumnRight" => ViewerPreference.PageLayoutTwoColumnRight,
            "TwoPageLeft" => ViewerPreference.PageLayoutTwoPageLeft,
            "TwoPageRight" => ViewerPreference.PageLayoutTwoPageRight,
            _ => ViewerPreference.PageLayoutSinglePage,
        };

        // ViewerPreferences dictionary flags
        var vpDict = doc.Reader.ResolveDict(doc.Reader.Catalog.Get("ViewerPreferences"));
        if (vpDict is not null)
        {
            if (vpDict.Get("HideMenubar") is PdfBoolean hm && hm.Value)
                result |= ViewerPreference.HideMenubar;
            if (vpDict.Get("HideToolbar") is PdfBoolean ht && ht.Value)
                result |= ViewerPreference.HideToolbar;
            if (vpDict.Get("HideWindowUI") is PdfBoolean hw && hw.Value)
                result |= ViewerPreference.HideWindowUI;
            if (vpDict.Get("FitWindow") is PdfBoolean fw && fw.Value)
                result |= ViewerPreference.FitWindow;
            if (vpDict.Get("CenterWindow") is PdfBoolean cw && cw.Value)
                result |= ViewerPreference.CenterWindow;
            if (vpDict.Get("DisplayDocTitle") is PdfBoolean dt && dt.Value)
                result |= ViewerPreference.DisplayDocTitle;

            result |= vpDict.GetName("Duplex") switch
            {
                "Simplex" => ViewerPreference.Simplex,
                "DuplexFlipLongEdge" => ViewerPreference.DuplexFlipLongEdge,
                "DuplexFlipShortEdge" => ViewerPreference.DuplexFlipShortEdge,
                _ => 0,
            };
            if (vpDict.Get("PickTrayByPDFSize") is PdfBoolean pt && pt.Value)
                result |= ViewerPreference.PickTrayByPDFSize;
        }

        return result;
    }

    /// <summary>
    /// Change the viewer preference. Sets the specified flags (replaces all previous settings).
    /// Parameter is named <c>viewerAttribution</c> per the published signature.
    /// </summary>
    public void ChangeViewerPreference(int viewerAttribution)
    {
        var preference = viewerAttribution;
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;

        // PageMode
        string? pageModeVal = null;
        if ((preference & ViewerPreference.PageModeUseOutlines) != 0) pageModeVal = "UseOutlines";
        else if ((preference & ViewerPreference.PageModeUseThumbs) != 0) pageModeVal = "UseThumbs";
        else if ((preference & ViewerPreference.PageModeFullScreen) != 0) pageModeVal = "FullScreen";
        else if ((preference & ViewerPreference.PageModeUseOC) != 0) pageModeVal = "UseOC";
        else if ((preference & ViewerPreference.PageModeUseAttachment) != 0) pageModeVal = "UseAttachments";
        else if ((preference & ViewerPreference.PageModeUseNone) != 0) pageModeVal = "UseNone";

        if (pageModeVal is not null)
            catalog.Set("PageMode", new PdfName(pageModeVal));
        else
            catalog.Remove("PageMode");

        // PageLayout
        string? pageLayoutVal = null;
        if ((preference & ViewerPreference.PageLayoutOneColumn) != 0) pageLayoutVal = "OneColumn";
        else if ((preference & ViewerPreference.PageLayoutTwoColumnLeft) != 0) pageLayoutVal = "TwoColumnLeft";
        else if ((preference & ViewerPreference.PageLayoutTwoColumnRight) != 0) pageLayoutVal = "TwoColumnRight";
        else if ((preference & ViewerPreference.PageLayoutTwoPageLeft) != 0) pageLayoutVal = "TwoPageLeft";
        else if ((preference & ViewerPreference.PageLayoutTwoPageRight) != 0) pageLayoutVal = "TwoPageRight";
        else if ((preference & ViewerPreference.PageLayoutSinglePage) != 0) pageLayoutVal = "SinglePage";

        if (pageLayoutVal is not null)
            catalog.Set("PageLayout", new PdfName(pageLayoutVal));
        else
            catalog.Remove("PageLayout");

        // ViewerPreferences dictionary flags
        var vpDict = doc.Reader.ResolveDict(catalog.Get("ViewerPreferences")) ?? new PdfDictionary();
        catalog.Set("ViewerPreferences", vpDict);

        SetOrRemoveBool(vpDict, "HideMenubar", (preference & ViewerPreference.HideMenubar) != 0);
        SetOrRemoveBool(vpDict, "HideToolbar", (preference & ViewerPreference.HideToolbar) != 0);
        SetOrRemoveBool(vpDict, "HideWindowUI", (preference & ViewerPreference.HideWindowUI) != 0);
        SetOrRemoveBool(vpDict, "FitWindow", (preference & ViewerPreference.FitWindow) != 0);
        SetOrRemoveBool(vpDict, "CenterWindow", (preference & ViewerPreference.CenterWindow) != 0);
        SetOrRemoveBool(vpDict, "DisplayDocTitle", (preference & ViewerPreference.DisplayDocTitle) != 0);

        // Duplex (/Duplex name entry, alongside Document.Duplex)
        string? duplexVal = null;
        if ((preference & ViewerPreference.DuplexFlipShortEdge) != 0) duplexVal = "DuplexFlipShortEdge";
        else if ((preference & ViewerPreference.DuplexFlipLongEdge) != 0) duplexVal = "DuplexFlipLongEdge";
        else if ((preference & ViewerPreference.Simplex) != 0) duplexVal = "Simplex";

        if (duplexVal is not null)
            vpDict.Set("Duplex", new PdfName(duplexVal));
        else
            vpDict.Remove("Duplex");

        SetOrRemoveBool(vpDict, "PickTrayByPDFSize", (preference & ViewerPreference.PickTrayByPDFSize) != 0);
    }

    private static void SetOrRemoveBool(PdfDictionary dict, string key, bool value)
    {
        if (value)
            dict.Set(key, PdfBoolean.True);
        else
            dict.Remove(key);
    }

    // ── Stamp operations (stateful) ───────────────────────────────────────────

    /// <summary>
    /// Get information about stamps on a page.
    /// Stamps are identified as q/Q graphics state blocks in the content stream
    /// that contain image or form XObject Do operators.
    /// </summary>
    /// <param name="pageNumber">1-based page number.</param>
    /// <returns>Array of StampInfo objects.</returns>
    public StampInfo[] GetStamps(int pageNumber)
    {
        var doc = EnsureBound();
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var contentBytes = GetPageContentBytes(page, doc);
        if (contentBytes.Length == 0) return [];

        var contentText = Encoding.ASCII.GetString(contentBytes);
        return ParseStamps(contentText, page, doc);
    }

    /// <summary>
    /// Delete stamps on a page by their 0-based indices.
    /// </summary>
    public void DeleteStamp(int pageNumber, int[] index)
    {
        var stampIndices = index;
        var doc = EnsureBound();
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var contentBytes = GetPageContentBytes(page, doc);
        if (contentBytes.Length == 0) return;

        var contentText = Encoding.ASCII.GetString(contentBytes);
        var stampBlocks = FindStampBlocks(contentText, page, doc);

        var indicesToRemove = new HashSet<int>(stampIndices.Where(i => i >= 0 && i < stampBlocks.Count));
        if (indicesToRemove.Count == 0) return;

        // Collect the byte ranges to cut. FindQBlocks emits nested q/Q blocks ordered
        // by their closing Q, so a selected block can sit inside (or start before the
        // end of) another selected block; sort by Start and coalesce overlapping or
        // nested ranges so the cut never walks backwards.
        var removedXNames = new List<string>();
        var ranges = new List<(int Start, int End)>();
        for (int i = 0; i < stampBlocks.Count; i++)
        {
            if (!indicesToRemove.Contains(i)) continue;
            var b = stampBlocks[i];
            ranges.Add((b.Start, b.End));
            if (b.XName is not null) removedXNames.Add(b.XName);
        }
        ranges.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

        // Build new content by removing the coalesced stamp ranges.
        var sb = new StringBuilder(contentText.Length);
        int lastEnd = 0;
        foreach (var (start, end) in ranges)
        {
            if (end <= lastEnd) continue;           // fully inside an already-cut range
            var cutStart = Math.Max(start, lastEnd); // clip a partial overlap
            sb.Append(contentText, lastEnd, cutStart - lastEnd);
            lastEnd = end;
        }
        sb.Append(contentText, lastEnd, contentText.Length - lastEnd);

        var newContent = sb.ToString();
        page.SetContentStream(Encoding.ASCII.GetBytes(newContent));

        // Drop each deleted stamp's XObject from the page resources when the
        // rewritten content no longer draws it (a `/Name Do`). Leaving the
        // orphaned XObject in /Resources keeps its (often large image) bytes
        // reachable, so the file would not shrink after the stamp is removed.
        if (removedXNames.Count > 0)
        {
            var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
            var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
            if (xobjects is not null)
            {
                foreach (var name in removedXNames.Distinct())
                {
                    if (Regex.IsMatch(newContent, $@"/{Regex.Escape(name)}\s+Do\b")) continue;
                    xobjects.Remove(name);
                }
            }
        }
    }

    /// <summary>
    /// Delete a stamp by its stamp ID (the ID assigned via Stamp.StampId).
    /// </summary>
    public void DeleteStampById(int pageNumber, int stampId)
    {
        var stamps = GetStamps(pageNumber);
        var indices = stamps
            .Where(s => s.StampId == stampId)
            .Select(s => s.IndexOnPage)
            .ToArray();
        if (indices.Length > 0)
            DeleteStamp(pageNumber, indices);
    }

    /// <summary>
    /// Delete stamps by their stamp IDs.
    /// </summary>
    public void DeleteStampByIds(int pageNumber, int[] stampIds)
    {
        var idSet = new HashSet<int>(stampIds);
        var stamps = GetStamps(pageNumber);
        var indices = stamps
            .Where(s => idSet.Contains(s.StampId))
            .Select(s => s.IndexOnPage)
            .ToArray();
        if (indices.Length > 0)
            DeleteStamp(pageNumber, indices);
    }

    // ── Stamp parsing internals ───────────────────────────────────────────────

    /// <summary>
    /// A content-stream q/Q block recognised as a managed stamp, with its byte range
    /// (start extended to cover a leading <c>%StampId=</c> comment), the resolved stamp
    /// id, and — when the stamp draws an XObject — that XObject and its name.
    /// </summary>
    private readonly record struct StampBlock(
        int Start, int End, int StampId, bool IsImage, string? XName, PdfStream? XObject, Rectangle? Rect,
        bool Hidden);

    /// <summary>
    /// Marker comment written by <see cref="HideStampById(int,int)"/> immediately before a
    /// stamp's <c>%StampId</c>/<c>%StampRect</c> comment cluster to record that the stamp is
    /// hidden. The drawing itself is suppressed by an empty-clip prologue inside the block.
    /// </summary>
    private const string StampHiddenMarker = "%StampHidden=1\n";

    /// <summary>Empty-clip prologue injected after a hidden stamp block's opening <c>q</c> so the
    /// stamp paints nothing while its operators remain present (and recoverable by Show).</summary>
    private const string StampHiddenClip = "0 0 0 0 re W n\n";

    /// <summary>
    /// Locate the managed-stamp blocks on a page. A q/Q block is a stamp when either
    /// (a) it is preceded by a <c>%StampId=NNN</c> comment (the convention written by
    /// this library's stamp facades), or (b) it draws an XObject (<c>/Name Do</c>) whose
    /// dictionary carries a <c>/StampId</c> entry (a convention
    /// present in externally-produced files).
    /// </summary>
    private static List<StampBlock> FindStampBlocks(string content, Page page, Document doc)
    {
        var blocks = FindQBlocks(content);
        var result = new List<StampBlock>();

        var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));

        foreach (var block in blocks)
        {
            // (a) %StampId= and/or %StampRect= comment immediately preceding the block.
            // Either marker identifies a stamp block; %StampRect carries the exact
            // page-space bounds (header/footer bands) so we can report them verbatim.
            var extendedStart = block.Start;
            var commentId = 0;
            var hasComment = false;
            Rectangle? commentRect = null;
            if (block.Start > 0)
            {
                var searchStart = Math.Max(0, block.Start - 90);
                var preceding = content.Substring(searchStart, block.Start - searchStart);
                // %StampId, when present, sits immediately before the block or just
                // before an optional %StampRect line — anchor at the end so a previous
                // block's id inside the window is not picked up.
                var idLineMatch = Regex.Match(preceding,
                    @"%StampId=(\d+)\s*(?:%StampRect=[^\n]*)?\s*$");
                var rectMatch = Regex.Match(preceding,
                    @"%StampRect=(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*$");
                if (idLineMatch.Success)
                {
                    hasComment = true;
                    commentId = int.Parse(idLineMatch.Groups[1].Value);
                    extendedStart = searchStart + idLineMatch.Index;
                }
                if (rectMatch.Success)
                {
                    hasComment = true;
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    commentRect = new Rectangle(
                        double.Parse(rectMatch.Groups[1].Value, ci), double.Parse(rectMatch.Groups[2].Value, ci),
                        double.Parse(rectMatch.Groups[3].Value, ci), double.Parse(rectMatch.Groups[4].Value, ci));
                    if (!idLineMatch.Success) extendedStart = searchStart + rectMatch.Index;
                }
            }

            var blockContent = content.Substring(block.Start, block.End - block.Start);

            // (b) /Name Do referencing an XObject. Capture the first XObject drawn by
            // the block (used to recover the stamp image) and detect the
            // /StampId dictionary marker.
            var hasDictMarker = false;
            var dictId = 0;
            var isImage = false;
            string? xname = null;
            PdfStream? xobject = null;
            if (xobjects is not null)
            {
                foreach (Match dm in Regex.Matches(blockContent, @"/([A-Za-z0-9_.\-]+)\s+Do\b"))
                {
                    var name = dm.Groups[1].Value;
                    if (doc.Reader.Resolve(xobjects.Get(name)) is not PdfStream xs) continue;
                    if (xobject is null)
                    {
                        xobject = xs;
                        xname = name;
                        isImage = xs.Dict.GetName("Subtype") == "Image";
                    }
                    if (xs.Dict.ContainsKey("StampId"))
                    {
                        hasDictMarker = true;
                        dictId = (int)xs.Dict.GetInt("StampId", 0);
                        isImage = xs.Dict.GetName("Subtype") == "Image";
                        xname = name;
                        xobject = xs;
                        break;
                    }
                }
            }

            // (c) Unmarked image stamp: a block whose only operators are q/Q/gs/cm and a
            // single image Do is the canonical image-placement shape emitted for an
            // image stamp. GetStamps must rediscover these even when the %StampId marker
            // was never written (or was stripped by an earlier re-serialisation), matching
            // the GetStamps contract, which reports such blocks with StampId 0.
            var isCleanImageStamp = isImage && xobject is not null &&
                                    IsCleanImagePlacementBlock(blockContent);

            if (!hasComment && !hasDictMarker && !isCleanImageStamp) continue;

            // A %StampHidden=1 marker sits immediately before the comment cluster when the
            // stamp was hidden via HideStampById. Detect it and extend Start to cover it so
            // a later DeleteStamp/MoveStamp rewrite preserves (or removes) it as one unit.
            var hidden = false;
            if (extendedStart >= StampHiddenMarker.Length &&
                string.CompareOrdinal(content, extendedStart - StampHiddenMarker.Length,
                    StampHiddenMarker, 0, StampHiddenMarker.Length) == 0)
            {
                hidden = true;
                extendedStart -= StampHiddenMarker.Length;
            }

            // The %StampId comment (when present) names the id; otherwise use the dict id.
            var stampId = commentId != 0 ? commentId : dictId;
            result.Add(new StampBlock(extendedStart, block.End, stampId, isImage, xname, xobject, commentRect, hidden));
        }

        return result;
    }

    private static StampInfo[] ParseStamps(string content, Page page, Document doc)
    {
        var stampBlocks = FindStampBlocks(content, page, doc);
        var result = new List<StampInfo>(stampBlocks.Count);

        for (var idx = 0; idx < stampBlocks.Count; idx++)
        {
            var sb = stampBlocks[idx];
            var blockContent = content.Substring(sb.Start, sb.End - sb.Start);

            // A form-wrapped stamp draws its caption inside the referenced Form XObject
            // (BT…Tj…ET), not in the page-level block; report it as StampType.Form and pull
            // the text from the form.
            var isFormStamp = sb.XObject is not null && !sb.IsImage &&
                              sb.XObject.Dict.GetName("Subtype") == "Form";

            var info = new StampInfo
            {
                IndexOnPage = idx,
                StampId = sb.StampId,
                StampType = sb.IsImage ? StampType.Image
                    : isFormStamp ? StampType.Form
                    : DetermineStampType(blockContent),
                Visible = !sb.Hidden,
            };

            if (isFormStamp)
            {
                info.Text = ExtractFormStampText(doc, sb.XObject!);
            }
            else if (info.StampType == StampType.Text)
            {
                var textMatch = Regex.Match(blockContent, @"\(([^)]*)\)\s*Tj");
                if (textMatch.Success)
                    info.Text = textMatch.Groups[1].Value;
            }
            else if (sb.XObject is not null)
            {
                info.ImageBytes = TryGetStampImageBytes(sb.XObject);
            }

            // Bounding rectangle of the stamp. A %StampRect comment (emitted by the
            // header/footer/page band APIs) carries the exact page-space bounds; otherwise
            // derive the box from the drawing matrix in the page's displayed coordinates.
            if (sb.Rect is { } exact)
            {
                info.Rect = exact;
            }
            else if (TryParseStampMatrix(blockContent, out var matrix))
            {
                var box = page.MediaBox;
                info.Rect = ComputeStampRect(matrix, page.RotateDegrees, box.Width, box.Height);
            }
            else if (isFormStamp && doc.Reader.Resolve(sb.XObject!.Dict.Get("BBox")) is PdfArray bb && bb.Count >= 4)
            {
                // A plain form stamp carries no %StampRect and no page-level matrix (the
                // transform lives inside the form); fall back to the form's /BBox so callers
                // that read StampInfo.Rectangle don't hit a null.
                double N(int i) => bb[i] is PdfReal r ? r.Value : bb[i] is PdfInteger n ? n.Value : 0;
                info.Rect = new Rectangle(N(0), N(1), N(2), N(3));
            }

            result.Add(info);
        }

        return result.ToArray();
    }

    /// <summary>
    /// Compose every <c>cm</c> operator that applies to the stamp's drawn XObject (all
    /// <c>cm</c>s before its <c>Do</c>) into a single transformation matrix [a b c d e f],
    /// matching the CTM under which the image is painted.
    /// </summary>
    private static bool TryParseStampMatrix(string block, out double[] matrix)
    {
        matrix = [1, 0, 0, 1, 0, 0];
        var doMatch = Regex.Match(block, @"/[\w.\-]+\s+Do\b");
        var prefix = doMatch.Success ? block.Substring(0, doMatch.Index) : block;
        var cms = Regex.Matches(prefix,
            @"(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+cm\b");
        if (cms.Count == 0) return false;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (Match cm in cms)
        {
            var t = new double[6];
            for (int i = 0; i < 6; i++)
                if (!double.TryParse(cm.Groups[i + 1].Value, System.Globalization.NumberStyles.Float, ci, out t[i]))
                    return false;
            // CTM after executing this cm: point × t × CTM_prev (the cm applies closest to the point).
            matrix = MultiplyMatrix(t, matrix);
        }
        return true;
    }

    /// <summary>Row-vector matrix product: the result R satisfies <c>point × R == (point × A) × B</c>.</summary>
    private static double[] MultiplyMatrix(double[] a, double[] b) =>
    [
        a[0] * b[0] + a[1] * b[2],
        a[0] * b[1] + a[1] * b[3],
        a[2] * b[0] + a[3] * b[2],
        a[2] * b[1] + a[3] * b[3],
        a[4] * b[0] + a[5] * b[2] + b[4],
        a[4] * b[1] + a[5] * b[3] + b[5],
    ];

    /// <summary>
    /// Bounding box of the unit square transformed by <paramref name="m"/>, then mapped
    /// through the page rotation so the result is in displayed coordinates.
    /// </summary>
    private static Rectangle ComputeStampRect(double[] m, int rotate, double pageW, double pageH)
    {
        Span<(double x, double y)> corners =
        [
            (m[4], m[5]),
            (m[0] + m[4], m[1] + m[5]),
            (m[2] + m[4], m[3] + m[5]),
            (m[0] + m[2] + m[4], m[1] + m[3] + m[5]),
        ];
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var c in corners)
        {
            var (rx, ry) = RotatePoint(c.x, c.y, rotate, pageW, pageH);
            minX = Math.Min(minX, rx); minY = Math.Min(minY, ry);
            maxX = Math.Max(maxX, rx); maxY = Math.Max(maxY, ry);
        }
        return new Rectangle(minX, minY, maxX, maxY);
    }

    /// <summary>Map a content-space point into the page's displayed coordinate system for a /Rotate value.</summary>
    private static (double x, double y) RotatePoint(double x, double y, int rotate, double w, double h)
        => (((rotate % 360) + 360) % 360) switch
        {
            90 => (y, w - x),
            180 => (w - x, h - y),
            270 => (h - y, x),
            _ => (x, y),
        };

    /// <summary>
    /// Recover decodable image bytes for an image-stamp XObject. JPEG/JPEG2000 streams
    /// are returned verbatim (their raw stream content is already a self-contained file);
    /// other encodings are not recovered here and yield <c>null</c>.
    /// </summary>
    private static byte[]? TryGetStampImageBytes(PdfStream xobj)
    {
        if (xobj.Dict.GetName("Subtype") != "Image") return null;

        var filterName = xobj.Dict.GetName("Filter");
        if (filterName is null && xobj.Dict.Get("Filter") is PdfArray fa && fa.Count > 0)
            filterName = (fa[fa.Count - 1] as PdfName)?.Value;

        if (filterName is "DCTDecode" or "JPXDecode")
            return xobj.RawData;

        return null;
    }

    private static bool IsStampBlockContent(string blockContent)
    {
        // A stamp block contains:
        if (Regex.IsMatch(blockContent, @"/\w+\s+Do\b"))
            return true;
        if (blockContent.Contains("BT") && blockContent.Contains("ET"))
            return true;
        if (blockContent.Contains("%StampId="))
            return true;
        return false;
    }

    /// <summary>
    /// True when a q/Q block is the canonical image-stamp shape —
    /// <c>q /GSx gs cm /Imx Do Q</c>: its only operators are <c>q</c>/<c>Q</c>/<c>gs</c>/<c>cm</c>
    /// and a single image <c>Do</c>, AND it sets a graphics state (<c>gs</c>). The <c>gs</c> is
    /// the distinguishing signature of an image stamp (it carries the stamp's
    /// opacity/blend ExtGState); ordinary page-content image placements (<c>q cm /Im Do Q</c>,
    /// no <c>gs</c>) are NOT stamps and must not be reported or deleted. Any other operator
    /// (text, paths, clipping, marked content) also disqualifies the block. The caller checks
    /// separately that the drawn XObject is an image.
    /// </summary>
    private static bool IsCleanImagePlacementBlock(string blockContent)
    {
        int i = 0, n = blockContent.Length;
        bool sawDo = false, sawGs = false;
        while (i < n)
        {
            char ch = blockContent[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '%') { int nl = blockContent.IndexOf('\n', i); i = nl < 0 ? n : nl + 1; continue; }
            if (ch == '/') { i++; while (i < n && !IsPdfDelimiterOrWhitespace(blockContent[i])) i++; continue; }
            if (ch == '(') { i = SkipParenString(blockContent, i); continue; }
            if (ch == '<') { int gt = blockContent.IndexOf('>', i); i = gt < 0 ? n : gt + 1; continue; }
            if (ch == '[' || ch == ']') { i++; continue; }
            if (ch is '+' or '-' or '.' || char.IsDigit(ch))
            {
                i++;
                while (i < n && (char.IsDigit(blockContent[i]) || blockContent[i] is '.' or '-' or '+' or 'e' or 'E')) i++;
                continue;
            }
            int s = i;
            while (i < n && !IsPdfDelimiterOrWhitespace(blockContent[i])) i++;
            switch (blockContent.Substring(s, i - s))
            {
                case "q": case "Q": case "cm": break;
                case "gs": sawGs = true; break;
                case "Do": sawDo = true; break;
                default: return false;
            }
        }
        return sawDo && sawGs;
    }

    private static bool IsPdfDelimiterOrWhitespace(char c)
        => char.IsWhiteSpace(c) || c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

    /// <summary>Pull the caption out of a text-stamp Form XObject's content: the concatenated
    /// operands of its text-showing operators. Handles literal <c>(..)Tj</c>, hex
    /// <c>&lt;..&gt;Tj</c> and <c>[..]TJ</c> arrays (hex decoded as WinAnsi) so a rotated stamp
    /// drawn with hex glyphs still reports its text.</summary>
    private static string ExtractFormStampText(Document doc, PdfStream form)
    {
        string content;
        try { content = Encoding.Latin1.GetString(doc.Reader.DecodeStream(form)); }
        catch { return ""; }
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(content,
            @"(\((?:[^()\\]|\\.)*\)|<[0-9A-Fa-f\s]*>)\s*Tj|\[((?:[^\]])*)\]\s*TJ"))
        {
            if (m.Groups[1].Success) sb.Append(DecodePdfStringToken(m.Groups[1].Value));
            else foreach (Match t in Regex.Matches(m.Groups[2].Value, @"\((?:[^()\\]|\\.)*\)|<[0-9A-Fa-f\s]*>"))
                sb.Append(DecodePdfStringToken(t.Value));
        }
        return sb.ToString();
    }

    /// <summary>Decode a single PDF string token — literal <c>(..)</c> (with escapes) or hex
    /// <c>&lt;..&gt;</c> — into text using WinAnsi for the byte values.</summary>
    private static string DecodePdfStringToken(string token)
    {
        if (token.Length >= 2 && token[0] == '<')
        {
            var hex = new StringBuilder();
            foreach (var c in token) if (Uri.IsHexDigit(c)) hex.Append(c);
            if (hex.Length % 2 == 1) hex.Append('0');
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.ToString(i * 2, 2), 16);
            return Aspose.Pdf.Text.Cp1252.GetString(bytes);
        }
        var inner = token.Substring(1, token.Length - 2);
        return Regex.Replace(inner, @"\\([nrtbf()\\]|[0-7]{1,3})", mm =>
        {
            var e = mm.Groups[1].Value;
            return e switch
            {
                "n" => "\n", "r" => "\r", "t" => "\t", "b" => "\b", "f" => "\f",
                "(" => "(", ")" => ")", "\\" => "\\",
                _ => ((char)Convert.ToInt32(e, 8)).ToString(),
            };
        });
    }

    private static StampType DetermineStampType(string blockContent)
    {
        // Check for Do operator (XObject invocation)
        if (Regex.IsMatch(blockContent, @"/\w+\s+Do\b"))
        {
            // If the content has a cm matrix + Do, it's likely an image or form stamp
            if (Regex.IsMatch(blockContent, @"\bcm\b"))
                return StampType.Image;
            return StampType.Form;
        }
        // Check for text (BT.ET)
        if (blockContent.Contains("BT") && blockContent.Contains("ET"))
            return StampType.Text;

        return StampType.Form;
    }

    // IsStampBlock replaced by IsStampBlockContent — see ParseStamps and DeleteStamp.

    private record struct QBlock(int Start, int End);

    private static List<QBlock> FindQBlocks(string content)
    {
        var result = new List<QBlock>();
        var stack = new Stack<int>();
        int i = 0;
        while (i < content.Length)
        {
            // Skip whitespace
            if (char.IsWhiteSpace(content[i])) { i++; continue; }

            // Look for 'q' operator (must be standalone, not part of another word)
            if (content[i] == 'q' && IsOperatorBoundary(content, i, 1))
            {
                stack.Push(i);
                i++;
                continue;
            }

            // Look for 'Q' operator
            if (content[i] == 'Q' && IsOperatorBoundary(content, i, 1))
            {
                if (stack.Count > 0)
                {
                    var start = stack.Pop();
                    result.Add(new QBlock(start, i + 1));
                }
                i++;
                continue;
            }

            // Skip strings (.)
            if (content[i] == '(')
            {
                i = SkipParenString(content, i);
                continue;
            }

            // Skip hex strings <.>
            if (content[i] == '<' && i + 1 < content.Length && content[i + 1] != '<')
            {
                i = content.IndexOf('>', i + 1);
                if (i < 0) break;
                i++;
                continue;
            }

            // Skip comments
            if (content[i] == '%')
            {
                i = content.IndexOf('\n', i + 1);
                if (i < 0) break;
                i++;
                continue;
            }

            i++;
        }

        return result;
    }

    private static bool IsOperatorBoundary(string content, int pos, int len)
    {
        // Check that the character before is a boundary (whitespace or start)
        if (pos > 0 && !char.IsWhiteSpace(content[pos - 1]) && content[pos - 1] != '\n')
            return false;
        // Check that the character after is a boundary
        var after = pos + len;
        if (after < content.Length && !char.IsWhiteSpace(content[after]) && content[after] != '\n')
            return false;
        return true;
    }

    private static int SkipParenString(string content, int start)
    {
        int depth = 0;
        int i = start;
        while (i < content.Length)
        {
            if (content[i] == '\\') { i += 2; continue; }
            if (content[i] == '(') depth++;
            else if (content[i] == ')') { depth--; if (depth == 0) return i + 1; }
            i++;
        }
        return content.Length;
    }

    private static byte[] GetPageContentBytes(Page page, Document doc)
    {
        var contentsObj = doc.Reader.Resolve(page.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
            return doc.Reader.DecodeStream(stream);
        if (contentsObj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var resolved = doc.Reader.Resolve(item);
                if (resolved is PdfStream s)
                {
                    var data = doc.Reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    private Document EnsureBound()
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        return _document;
    }

    // ── Stateful ReplaceText ───────────────────────────────────────────────────

    /// <summary>
    /// Strategy controlling how <see cref="ReplaceText(string, string)"/> matches
    /// (literal vs. regex) and how many matches it substitutes per call.
    /// </summary>
    private ReplaceTextStrategy? _replaceTextStrategy;

    public ReplaceTextStrategy ReplaceTextStrategy
    {
        // Bound to this editor so its settings are live views over
        // TextSearchOptions / TextEditOptions / TextReplaceOptions (legacy API parity).
        get => _replaceTextStrategy ??= new ReplaceTextStrategy { Owner = this };
        set { _replaceTextStrategy = value; value?.BindTo(this); }
    }

    /// <summary>
    /// Replace text in the bound document according to <see cref="ReplaceTextStrategy"/>.
    /// Returns true if at least one replacement was made.
    /// </summary>
    public bool ReplaceText(string srcString, string destString)
    {
        var doc = EnsureBound();
        if (TextSearchOptions?.Rectangle is not null)
            return ReplaceTextInRectangle(doc, null, srcString, destString);
        var replacer = new TextReplacer
        {
            ReplaceFirstOnly = ReplaceTextStrategy.ReplaceScope == ReplaceTextStrategy.Scope.ReplaceFirst
                && TextReplaceOptions.ReplaceScope == TextReplaceOptions.Scope.REPLACE_FIRST,
            // Facade ReplaceText owns the whole replacement: font-switch a run whose glyphs
            // are absent from the source embedded subset to a fallback (Times), so they render.
            AllowSubsetGlyphFallback = true,
            // Anchoring (keep trailing text at its absolute position when the
            // replacement is narrower/wider than the match) is a request the caller
            // makes by setting ReplaceAdjustment.None explicitly. The untouched facade
            // default reflows the line, closing the gap instead.
            AnchorTrailingOnReplace = _textReplaceOptionsAssigned
                && TextReplaceOptions.ReplaceAdjustmentAction
                == TextReplaceOptions.ReplaceAdjustment.None,
        };
        replacer.Replace(doc, srcString, destString, ReplaceTextStrategy.IsRegularExpressionUsed);
        return replacer.ReplacementCount > 0;
    }

    /// <summary>Replace text on a specific page (1-based).</summary>
    public bool ReplaceText(string srcString, int thePage, string destString)
    {
        var doc = EnsureBound();
        if (thePage < 1 || thePage > doc.PageCount) return false;
        if (TextSearchOptions?.Rectangle is not null)
            return ReplaceTextInRectangle(doc, thePage, srcString, destString);
        var replacer = new TextReplacer
        {
            ReplaceFirstOnly = ReplaceTextStrategy.ReplaceScope == ReplaceTextStrategy.Scope.ReplaceFirst
                && TextReplaceOptions.ReplaceScope == TextReplaceOptions.Scope.REPLACE_FIRST,
            // Facade ReplaceText owns the whole replacement: font-switch a run whose glyphs
            // are absent from the source embedded subset to a fallback (Times), so they render.
            AllowSubsetGlyphFallback = true,
            // Anchoring (keep trailing text at its absolute position when the
            // replacement is narrower/wider than the match) is a request the caller
            // makes by setting ReplaceAdjustment.None explicitly. The untouched facade
            // default reflows the line, closing the gap instead.
            AnchorTrailingOnReplace = _textReplaceOptionsAssigned
                && TextReplaceOptions.ReplaceAdjustmentAction
                == TextReplaceOptions.ReplaceAdjustment.None,
        };
        replacer.Replace(doc.Pages.At(thePage), srcString, destString, IsRegexSearch);
        return replacer.ReplacementCount > 0;
    }

    /// <summary>
    /// Region-scoped replacement used when <see cref="TextSearchOptions"/>.Rectangle is
    /// set: only occurrences whose text fragment falls inside that rectangle are replaced.
    /// Matching fragments are located through <see cref="TextFragmentAbsorber"/> (which
    /// honours the rectangle) and rewritten via the fragment's Text setter, which scopes
    /// each rewrite to the producing operator's page-space position.
    /// </summary>
    private bool ReplaceTextInRectangle(Document doc, int? thePage, string srcString, string destString)
    {
        var opts = new TextSearchOptions(TextSearchOptions.Rectangle!)
        {
            IsRegularExpression = IsRegexSearch,
            CaseSensitive = TextSearchOptions.CaseSensitive,
            WholeWord = TextSearchOptions.WholeWord,
        };
        var absorber = new TextFragmentAbsorber(srcString, opts);
        if (thePage is int p)
            doc.Pages.At(p).Accept(absorber);
        else
            absorber.Visit(doc);

        bool replaceFirst = ReplaceTextStrategy.ReplaceScope == ReplaceTextStrategy.Scope.ReplaceFirst
            && TextReplaceOptions.ReplaceScope == TextReplaceOptions.Scope.REPLACE_FIRST;
        int count = 0;
        foreach (TextFragment frag in absorber.TextFragments)
        {
            var page = frag.Page;
            if (page is null) continue;
            // Scope the rewrite to this fragment's exact page-space position (X and
            // Y), not just its baseline Y: a rectangle can include some matches on a
            // line while excluding others on the same baseline, so a Y-only scope
            // (as used by the generic TextFragment.Text setter) would bleed into the
            // neighbouring out-of-rectangle word.
            // Cross-operator ON so a word drawn glyph-by-glyph (one Tj per glyph) is matched;
            // TargetY/TargetX (set below) keep the cross-op replacement scoped to this fragment.
            var replacer = new TextReplacer { AllowSubsetGlyphFallback = true, AllowCrossOperator = true };
            if (frag.PositionOrNull is { } pos)
            {
                replacer.TargetY = pos.YIndent;
                replacer.TargetX = pos.XIndent;
            }
            replacer.Replace(page, srcString, destString, IsRegexSearch);
            if (replacer.ReplacementCount > 0) count += replacer.ReplacementCount;
            if (replaceFirst && count > 0) break;
        }
        return count > 0;
    }

    /// <summary>Replace text with explicit <see cref="TextState"/> formatting (font/size/colour).</summary>
    public bool ReplaceText(string srcString, string destString, TextState textState)
    {
        var doc = EnsureBound();
        if (!ReplaceText(srcString, destString)) return false;
        ApplyReplacementState(doc, null, destString, textState);
        return true;
    }

    /// <summary>Replace text on a specific page with explicit <see cref="TextState"/>.</summary>
    public bool ReplaceText(string srcString, int thePage, string destString, TextState textState)
    {
        var doc = EnsureBound();
        if (!ReplaceText(srcString, thePage, destString)) return false;
        ApplyReplacementState(doc, thePage, destString, textState);
        return true;
    }

    /// <summary>Replace text and override the font size of the replacement run.</summary>
    public bool ReplaceText(string srcString, string destString, int fontSize)
    {
        var doc = EnsureBound();
        if (!ReplaceText(srcString, destString)) return false;
        ApplyReplacementState(doc, null, destString, new TextState { FontSize = fontSize });
        return true;
    }

    /// <summary>Whether the current search/replace should treat the source as a regex —
    /// driven by either the legacy <see cref="ReplaceTextStrategy"/> flag or the
    /// <see cref="TextSearchOptions"/> the caller set before replacing.</summary>
    private bool IsRegexSearch =>
        ReplaceTextStrategy.IsRegularExpressionUsed || TextSearchOptions.IsRegularExpression;

    /// <summary>Apply the replacement run's font size / colour / font by re-finding the
    /// inserted text and pushing the state onto each matched fragment. The fragment's
    /// TextState setters propagate to the content stream (via TextStateModifier), so the
    /// change survives the save. Font embedding is the caller's font's responsibility.</summary>
    private static void ApplyReplacementState(Document doc, int? thePage, string destString, TextState? textState)
    {
        if (textState is null || string.IsNullOrEmpty(destString)) return;
        var absorber = new TextFragmentAbsorber(destString);
        if (thePage is int p && p >= 1 && p <= doc.PageCount)
            doc.Pages.At(p).Accept(absorber);
        else
            absorber.Visit(doc);

        // Drive the content-stream rewrite through TextStateModifier (text + page
        // based): fragment-level TextState setters don't propagate because their
        // OwnerSegment is unset, so set the run's Tf size / fill colour directly.
        var modifier = new TextStateModifier();
        var done = new HashSet<Page>();
        foreach (TextFragment frag in absorber.TextFragments)
        {
            var pg = frag.Page;
            if (pg is null || !done.Add(pg)) continue;
            // Apply colour and size first (they match the run by its current encoding);
            // the font change re-encodes the run, so it must run last or the colour/size
            // text-match would no longer find the run.
            if (textState.ForegroundColor is not null)
                modifier.ModifyForegroundColor(pg, destString, textState.ForegroundColor);
            if (textState.FontSize > 0)
                modifier.ModifyFontSize(pg, destString, frag.TextState.FontSize, textState.FontSize);
            // Only swap the font when the caller actually requested a family. TextState.Font
            // always resolves to a default (Helvetica), so key on the explicitly-set FontName
            // (the family/style ctors set it) — otherwise a colour-or-size-only replacement
            // would needlessly re-font the run.
            if (!string.IsNullOrEmpty(textState.FontName))
            {
                // Resolve the styled variant from the requested family + FontStyle
                // (e.g. Times + Bold -> Times-Bold, Courier + Italic -> Courier-Oblique).
                // Prefer a host TrueType so the swap carries a glyph program ModifyFont can
                // embed; the metric-only Standard-14 stub has none and would no-op.
                var resolved = Aspose.Pdf.Text.FontRepository.FindEmbeddableStyledFont(textState.FontName!, textState.FontStyle)
                           ?? Aspose.Pdf.Text.FontRepository.FindFont(textState.FontName!, textState.FontStyle);
                var font = resolved ?? textState.Font;
                if (font is not null)
                {
                    // Only a resolved repository face carries a program to embed. The
                    // fallback is the run's own font, already in the document — asking to
                    // embed it would pull in a system face the caller never named.
                    if (resolved is not null) resolved.IsEmbedded = true;
                    modifier.ModifyFont(pg, destString, font);
                }
            }
        }
    }

    // ── Stateless API (existing) ──────────────────────────────────────────────

    /// <summary>
    /// Replace all occurrences of text across all pages.
    /// Returns the modified PDF bytes.
    /// </summary>
    public byte[] ReplaceText(byte[] input, string searchText, string replaceText)
    {
        using var doc = Document.Open(input);
        var replacer = new TextReplacer();
        replacer.Replace(doc, searchText, replaceText);
        return doc.ToArray();
    }

    /// <summary>
    /// Replace all occurrences of text across all pages using search options.
    /// Returns the modified PDF bytes.
    /// </summary>
    public byte[] ReplaceText(byte[] input, string searchText, string replaceText, TextSearchOptions options)
    {
        using var doc = Document.Open(input);
        var absorber = new TextFragmentAbsorber(searchText, options);
        absorber.Visit(doc);

        // For each found fragment, do a content-stream level replacement
        // We use TextReplacer for actual stream modification, building the effective pattern
        var pattern = options.IsRegularExpression ? searchText : Regex.Escape(searchText);
        if (options.WholeWord)
            pattern = @"\b" + pattern + @"\b";
        var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        foreach (var page in doc.Pages)
        {
            var text = ExtractPageText(doc, page);
            if (Regex.IsMatch(text, pattern, regexOptions))
            {
                var replacer = new TextReplacer();
                replacer.Replace(page, searchText, replaceText);
            }
        }
        return doc.ToArray();
    }

    private static string ExtractPageText(Document doc, Page page)
    {
        var absorber = new TextAbsorber();
        absorber.Visit(page);
        return absorber.Text;
    }

    /// <summary>
    /// Replace text on a specific page (1-based).
    /// </summary>
    public byte[] ReplaceTextOnPage(byte[] input, int pageNumber, string searchText, string replaceText)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages.At(pageNumber), searchText, replaceText);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a local link annotation that navigates to a page in the same document.
    /// </summary>
    /// <param name="input">Source PDF bytes.</param>
    /// <param name="rect">Link rectangle on the page.</param>
    /// <param name="pageNumber">The page where the link is placed (1-based).</param>
    /// <param name="destinationPage">The target page number (1-based).</param>
    public byte[] CreateLocalLink(byte[] input, Rectangle rect, int pageNumber, int destinationPage)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = BuildLinkAnnotation(rect, destinationPage);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a URI link annotation that opens a URL.
    /// </summary>
    public byte[] CreateWebLink(byte[] input, Rectangle rect, int pageNumber, string url)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = BuildUriAnnotation(rect, url);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a free text annotation on a page.
    /// </summary>
    public byte[] CreateFreeText(byte[] input, Rectangle rect, int pageNumber, string text, string? fontName = null, double fontSize = 12)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = BuildFreeTextAnnotation(rect, text, fontName ?? "Helvetica", fontSize);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Create a text (sticky note) annotation on a page.
    /// </summary>
    public byte[] CreateText(byte[] input, Rectangle rect, int pageNumber, string title, string contents)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("Text"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("T", new PdfString(System.Text.Encoding.Latin1.GetBytes(title)));
        annotDict.Set("Contents", new PdfString(System.Text.Encoding.Latin1.GetBytes(contents)));
        annotDict.Set("Open", PdfBoolean.False);
        AppendAnnotation(page, annotDict);
        return doc.ToArray();
    }

    /// <summary>
    /// Delete all annotations on a specific page.
    /// </summary>
    public byte[] DeleteAnnotations(byte[] input, int pageNumber)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        page.Dict.Set("Annots", new PdfArray());
        return doc.ToArray();
    }

    /// <summary>
    /// Delete annotations of a specific subtype from a page.
    /// </summary>
    public byte[] DeleteAnnotations(byte[] input, int pageNumber, string annotationType)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var annots = doc.Reader.Resolve(page.Dict.Get("Annots"));
        if (annots is not PdfArray annotArray) return input;

        var kept = new PdfArray();
        foreach (var item in annotArray)
        {
            var resolved = doc.Reader.ResolveDict(item);
            if (resolved is not null)
            {
                var subtype = resolved.GetName("Subtype");
                if (subtype != annotationType)
                    kept.Add(item);
            }
        }
        page.Dict.Set("Annots", kept);
        return doc.ToArray();
    }

    /// <summary>
    /// Extract text from a specific page.
    /// </summary>
    public string ExtractText(byte[] input, int pageNumber)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages.At(pageNumber));
        return absorber.Text;
    }

    /// <summary>
    /// Extract text from all pages.
    /// </summary>
    public string ExtractText(byte[] input)
    {
        using var doc = Document.Open(input);
        var absorber = new TextAbsorber();
        absorber.Visit(doc);
        return absorber.Text;
    }

    private static PdfDictionary BuildLinkAnnotation(Rectangle rect, int destinationPage)
    {
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("Link"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("Border", new PdfArray(new List<PdfObject>
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)
        }));

        var dest = new PdfArray();
        dest.Add(new PdfInteger(destinationPage - 1)); // 0-based page index
        dest.Add(new PdfName("Fit"));
        annotDict.Set("Dest", dest);
        return annotDict;
    }

    private static PdfDictionary BuildUriAnnotation(Rectangle rect, string url)
    {
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("Link"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("Border", new PdfArray(new List<PdfObject>
        {
            new PdfInteger(0), new PdfInteger(0), new PdfInteger(0)
        }));

        var actionDict = new PdfDictionary();
        actionDict.Set("S", new PdfName("URI"));
        actionDict.Set("URI", new PdfString(System.Text.Encoding.Latin1.GetBytes(url)));
        annotDict.Set("A", actionDict);
        return annotDict;
    }

    private static PdfDictionary BuildFreeTextAnnotation(Rectangle rect, string text, string fontName, double fontSize)
    {
        var annotDict = new PdfDictionary();
        annotDict.Set("Type", new PdfName("Annot"));
        annotDict.Set("Subtype", new PdfName("FreeText"));
        annotDict.Set("Rect", RectToPdfArray(rect));
        annotDict.Set("Contents", new PdfString(System.Text.Encoding.Latin1.GetBytes(text)));
        // Print flag (bit 3 = 4): created annotations must be printable.
        // Same default as AnnotationCollection.
        annotDict.Set("F", new PdfInteger(4));
        annotDict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(
            $"/{fontName} {fontSize.ToString("G", System.Globalization.CultureInfo.InvariantCulture)} Tf")));
        return annotDict;
    }

    private static PdfArray RectToPdfArray(Rectangle rect)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(rect.LLX));
        arr.Add(new PdfReal(rect.LLY));
        arr.Add(new PdfReal(rect.URX));
        arr.Add(new PdfReal(rect.URY));
        return arr;
    }

    private static void AppendAnnotation(Page page, PdfDictionary annotDict)
    {
        var existing = page.Dict.Get("Annots");
        PdfArray annotArray;
        if (existing is PdfArray arr)
        {
            annotArray = arr;
        }
        else
        {
            annotArray = new PdfArray();
            page.Dict.Set("Annots", annotArray);
        }
        annotArray.Add(annotDict);
    }

    /// <summary>
    /// Overload that converts <see cref="System.Drawing.Rectangle"/>
    /// to the PDF rectangle form and delegates.
    /// </summary>
    public void DrawCurve(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents)
        => DrawCurve(lineInfo, page, DrawingRectToPdfRect(annotRect), annotContents);

    /// <summary>
    /// Draw a curve (polyline) on a page. The curve is added as a path in the content stream,
    /// not as an annotation — existing annotations are not affected.
    /// </summary>
    public void DrawCurve(LineInfo lineInfo, int pageNumber, Rectangle rect, string? message)
    {
        if (_document is null) throw new InvalidOperationException("No PDF bound");
        if (pageNumber < 1 || pageNumber > _document.PageCount) return;

        var page = _document.Pages.At(pageNumber);
        var verts = lineInfo.VerticeCoordinate;
        if (verts is null || verts.Length < 4) return;

        var builder = new Content.ContentStreamBuilder();
        builder.SaveState();
        builder.SetLineWidth(lineInfo.LineWidth);

        var lr = lineInfo.LineColorR / 255.0;
        var lg = lineInfo.LineColorG / 255.0;
        var lb = lineInfo.LineColorB / 255.0;
        builder.SetStrokeColor(lr, lg, lb);

        builder.MoveTo(verts[0], verts[1]);
        for (int i = 2; i + 1 < verts.Length; i += 2)
            builder.LineTo(verts[i], verts[i + 1]);
        builder.Stroke();
        builder.RestoreState();

        page.AddContentStream(builder.Build());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Annotation, stamp, attachment, and document-action helpers. Most write a
    // single annotation dict into the page /Annots array or update a catalog-
    // level dict. Complex per-feature operations (rich appearance streams,
    // media playback) are not implemented; structural correctness — annotation
    // present with the right Subtype and Rect — is what's guaranteed here.
    // ──────────────────────────────────────────────────────────────────────────

    private static Rectangle DrawingRectToPdfRect(System.Drawing.Rectangle r)
        => new(r.X, r.Y, r.X + r.Width, r.Y + r.Height);

    private static PdfArray DrawingColorToPdfArray(System.Drawing.Color c)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(c.R / 255.0));
        arr.Add(new PdfReal(c.G / 255.0));
        arr.Add(new PdfReal(c.B / 255.0));
        return arr;
    }

    private static PdfString Latin1(string s)
        => new(System.Text.Encoding.Latin1.GetBytes(s ?? ""));

    private Page GetPage1Based(int pageNumber)
    {
        var doc = EnsureBound();
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        return doc.Pages.At(pageNumber);
    }

    private void AddAnnotation(int page, PdfDictionary annotDict)
        => AppendAnnotation(GetPage1Based(page), annotDict);

    // ── Link annotations (stateful, void-return, System.Drawing.Rectangle) ──

    public void CreateLocalLink(System.Drawing.Rectangle rect, int desPage, int originalPage)
    {
        var dict = BuildLinkAnnotation(DrawingRectToPdfRect(rect), desPage);
        AddAnnotation(originalPage, dict);
    }

    public void CreateLocalLink(System.Drawing.Rectangle rect, int desPage, int originalPage, System.Drawing.Color clr)
    {
        var dict = BuildLinkAnnotation(DrawingRectToPdfRect(rect), desPage);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(originalPage, dict);
    }

    public void CreateLocalLink(System.Drawing.Rectangle rect, int desPage, int originalPage, System.Drawing.Color clr, System.Enum[] actionName)
    {
        // actionName carries an "additional action" sequence; we accept it for API parity
        // and apply only the color + destination — additional actions are an advanced feature.
        CreateLocalLink(rect, desPage, originalPage, clr);
    }

    public void CreateWebLink(System.Drawing.Rectangle rect, string url, int originalPage)
    {
        var dict = BuildUriAnnotation(DrawingRectToPdfRect(rect), url);
        AddAnnotation(originalPage, dict);
    }

    public void CreateWebLink(System.Drawing.Rectangle rect, string url, int originalPage, System.Drawing.Color clr)
    {
        var dict = BuildUriAnnotation(DrawingRectToPdfRect(rect), url);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(originalPage, dict);
    }

    public void CreateWebLink(System.Drawing.Rectangle rect, string url, int originalPage, System.Drawing.Color clr, System.Enum[] actionName)
    {
        CreateWebLink(rect, url, originalPage, clr);
    }

    public void CreateApplicationLink(System.Drawing.Rectangle rect, string application, int page)
    {
        var dict = BuildLaunchAnnotation(DrawingRectToPdfRect(rect), application);
        AddAnnotation(page, dict);
    }

    public void CreateApplicationLink(System.Drawing.Rectangle rect, string application, int page, System.Drawing.Color clr)
    {
        var dict = BuildLaunchAnnotation(DrawingRectToPdfRect(rect), application);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(page, dict);
    }

    public void CreateApplicationLink(System.Drawing.Rectangle rect, string application, int page, System.Drawing.Color clr, System.Enum[] actionName)
    {
        CreateApplicationLink(rect, application, page, clr);
    }

    public void CreatePdfDocumentLink(System.Drawing.Rectangle rect, string remotePdf, int originalPage, int destinationPage)
    {
        var dict = BuildGoToRAnnotation(DrawingRectToPdfRect(rect), remotePdf, destinationPage);
        AddAnnotation(originalPage, dict);
    }

    public void CreatePdfDocumentLink(System.Drawing.Rectangle rect, string remotePdf, int originalPage, int destinationPage, System.Drawing.Color clr)
    {
        var dict = BuildGoToRAnnotation(DrawingRectToPdfRect(rect), remotePdf, destinationPage);
        dict.Set("C", DrawingColorToPdfArray(clr));
        AddAnnotation(originalPage, dict);
    }

    public void CreatePdfDocumentLink(System.Drawing.Rectangle rect, string remotePdf, int originalPage, int destinationPage, System.Drawing.Color clr, System.Enum[] actionName)
    {
        CreatePdfDocumentLink(rect, remotePdf, originalPage, destinationPage, clr);
    }

    public void CreateJavaScriptLink(string code, System.Drawing.Rectangle rect, int originalPage, System.Drawing.Color color)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("C", DrawingColorToPdfArray(color));
        var act = new PdfDictionary();
        act.Set("S", new PdfName("JavaScript"));
        act.Set("JS", Latin1(code ?? ""));
        dict.Set("A", act);
        AddAnnotation(originalPage, dict);
    }

    public void CreateCustomActionLink(System.Drawing.Rectangle rect, int originalPage, System.Drawing.Color color, System.Enum[] actionName)
    {
        // No specific subtype — emit a Link with the colour; additional-action chain is no-op.
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("C", DrawingColorToPdfArray(color));
        AddAnnotation(originalPage, dict);
    }

    // ── Markup / shape annotations ──

    public void CreateFreeText(System.Drawing.Rectangle rect, string contents, int page)
    {
        var dict = BuildFreeTextAnnotation(DrawingRectToPdfRect(rect), contents ?? "", "Helvetica", 12);
        AddAnnotation(page, dict);
    }

    public void CreateText(System.Drawing.Rectangle rect, string title, string contents, bool open, string icon, int page)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Text"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("T", Latin1(title ?? ""));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("Open", open ? PdfBoolean.True : PdfBoolean.False);
        if (!string.IsNullOrEmpty(icon))
            dict.Set("Name", new PdfName(icon));
        AddAnnotation(page, dict);
    }

    public void CreateCaret(int page, System.Drawing.Rectangle annotRect, System.Drawing.Rectangle caretRect, string symbol, string annotContents, System.Drawing.Color color)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Caret"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(annotRect)));
        dict.Set("Contents", Latin1(annotContents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(color));
        if (!string.IsNullOrEmpty(symbol))
            dict.Set("Sy", new PdfName(symbol));
        // RD (differences between Rect and caret bbox): [left, top, right, bottom]
        var rd = new PdfArray();
        rd.Add(new PdfReal(Math.Max(0, caretRect.X - annotRect.X)));
        rd.Add(new PdfReal(Math.Max(0, (annotRect.Y + annotRect.Height) - (caretRect.Y + caretRect.Height))));
        rd.Add(new PdfReal(Math.Max(0, (annotRect.X + annotRect.Width) - (caretRect.X + caretRect.Width))));
        rd.Add(new PdfReal(Math.Max(0, caretRect.Y - annotRect.Y)));
        dict.Set("RD", rd);
        AddAnnotation(page, dict);
    }

    public void CreateMarkup(System.Drawing.Rectangle rect, string contents, int type, int page, System.Drawing.Color clr)
    {
        var subtype = type switch
        {
            0 => "Highlight",
            1 => "Underline",
            2 => "StrikeOut",
            3 => "Squiggly",
            _ => "Highlight",
        };
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(clr));
        // QuadPoints — single quad covering Rect
        var qp = new PdfArray();
        qp.Add(new PdfReal(rect.X));                  qp.Add(new PdfReal(rect.Y + rect.Height));
        qp.Add(new PdfReal(rect.X + rect.Width));     qp.Add(new PdfReal(rect.Y + rect.Height));
        qp.Add(new PdfReal(rect.X));                  qp.Add(new PdfReal(rect.Y));
        qp.Add(new PdfReal(rect.X + rect.Width));     qp.Add(new PdfReal(rect.Y));
        dict.Set("QuadPoints", qp);
        AddAnnotation(page, dict);
    }

    public void CreateSquareCircle(System.Drawing.Rectangle rect, string contents, System.Drawing.Color clr, bool square, int page, int borderWidth)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(square ? "Square" : "Circle"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(clr));
        var bs = new PdfDictionary();
        bs.Set("W", new PdfInteger(borderWidth));
        bs.Set("S", new PdfName("S"));
        dict.Set("BS", bs);
        AddAnnotation(page, dict);
    }

    public void CreateLine(System.Drawing.Rectangle rect, string contents, float x1, float y1, float x2, float y2,
        int page, int border, System.Drawing.Color clr, string borderStyle, int[] dashArray, string[] LEArray)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Line"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(clr));
        var line = new PdfArray();
        line.Add(new PdfReal(x1)); line.Add(new PdfReal(y1));
        line.Add(new PdfReal(x2)); line.Add(new PdfReal(y2));
        dict.Set("L", line);
        var bs = new PdfDictionary();
        bs.Set("W", new PdfInteger(border));
        if (!string.IsNullOrEmpty(borderStyle)) bs.Set("S", new PdfName(borderStyle));
        if (dashArray is { Length: > 0 })
        {
            var da = new PdfArray();
            foreach (var d in dashArray) da.Add(new PdfInteger(d));
            bs.Set("D", da);
        }
        dict.Set("BS", bs);
        if (LEArray is { Length: >= 2 })
        {
            var le = new PdfArray();
            le.Add(new PdfName(LEArray[0])); le.Add(new PdfName(LEArray[1]));
            dict.Set("LE", le);
        }
        AddAnnotation(page, dict);
    }

    public void CreatePolygon(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents)
        => CreatePolyShape(lineInfo, page, annotRect, annotContents, "Polygon");

    public void CreatePolyLine(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents)
        => CreatePolyShape(lineInfo, page, annotRect, annotContents, "PolyLine");

    private void CreatePolyShape(LineInfo lineInfo, int page, System.Drawing.Rectangle annotRect, string annotContents, string subtype)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName(subtype));
        dict.Set("Contents", Latin1(annotContents ?? ""));
        var verts = lineInfo?.VerticeCoordinate;
        if (verts is { Length: > 0 })
        {
            var v = new PdfArray();
            foreach (var coord in verts) v.Add(new PdfReal(coord));
            dict.Set("Vertices", v);
        }
        double r = (lineInfo?.LineColorR ?? 0) / 255.0;
        double g = (lineInfo?.LineColorG ?? 0) / 255.0;
        double b = (lineInfo?.LineColorB ?? 0) / 255.0;
        double width = lineInfo?.LineWidth ?? 1;
        if (lineInfo is not null)
        {
            var c = new PdfArray();
            c.Add(new PdfReal(r)); c.Add(new PdfReal(g)); c.Add(new PdfReal(b));
            dict.Set("C", c);
            var bs = new PdfDictionary();
            bs.Set("W", new PdfReal(lineInfo.LineWidth));
            dict.Set("BS", bs);
        }

        // The /Vertices alone don't render — viewers (and the FOSS renderer) draw the
        // shape from its /AP /N appearance. Synthesise one that strokes the polyline /
        // polygon in page space, and set /Rect to the vertices' bounding box so the
        // appearance maps 1:1 onto the page (otherwise the line does not show).
        if (verts is { Length: >= 4 })
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i + 1 < verts.Length; i += 2)
            {
                minX = System.Math.Min(minX, verts[i]); maxX = System.Math.Max(maxX, verts[i]);
                minY = System.Math.Min(minY, verts[i + 1]); maxY = System.Math.Max(maxY, verts[i + 1]);
            }
            // The polygon/polyline /Rect is padded beyond the vertex
            // bounding box by (LineWidth + 3) on every side (the
            // CreatePolygon padding: width 1/3/5 → pad 4/6/8), leaving room for the
            // stroke and end caps so the appearance is never clipped.
            double pad = width + 3.0;
            minX -= pad; minY -= pad; maxX += pad; maxY += pad;
            dict.Set("Rect", RectToPdfArray(new Rectangle(minX, minY, maxX, maxY)));

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append(r.ToString(ci)).Append(' ').Append(g.ToString(ci)).Append(' ').Append(b.ToString(ci)).Append(" RG\n");
            sb.Append(width.ToString(ci)).Append(" w\n");
            sb.Append(verts[0].ToString(ci)).Append(' ').Append(verts[1].ToString(ci)).Append(" m\n");
            for (int i = 2; i + 1 < verts.Length; i += 2)
                sb.Append(verts[i].ToString(ci)).Append(' ').Append(verts[i + 1].ToString(ci)).Append(" l\n");
            if (subtype == "Polygon") sb.Append("h\n");
            sb.Append("S\n");
            var content = System.Text.Encoding.Latin1.GetBytes(sb.ToString());

            var form = new PdfDictionary();
            form.Set("Type", new PdfName("XObject"));
            form.Set("Subtype", new PdfName("Form"));
            form.Set("FormType", new PdfInteger(1));
            var bb = new PdfArray();
            bb.Add(new PdfReal(minX)); bb.Add(new PdfReal(minY));
            bb.Add(new PdfReal(maxX)); bb.Add(new PdfReal(maxY));
            form.Set("BBox", bb);
            form.Set("Length", new PdfInteger(content.Length));
            var ap = new PdfDictionary();
            ap.Set("N", new PdfStream(form, content));
            dict.Set("AP", ap);
        }
        else
        {
            dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(annotRect)));
        }
        AddAnnotation(page, dict);
    }

    public void CreatePopup(System.Drawing.Rectangle rect, string contents, bool open, int page)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Popup"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        dict.Set("Open", open ? PdfBoolean.True : PdfBoolean.False);
        AddAnnotation(page, dict);
    }

    public void CreateRubberStamp(int page, System.Drawing.Rectangle annotRect, string annotContents, System.Drawing.Color color, string appearanceFile)
    {
        var dict = BuildRubberStamp(annotRect, annotContents, color, icon: null);
        // appearanceFile is read but the bytes aren't synthesised into a /N appearance stream
        // here — appearance streams require XObject form rendering which is not implemented here.
        AddAnnotation(page, dict);
    }

    public void CreateRubberStamp(int page, System.Drawing.Rectangle annotRect, string annotContents, System.Drawing.Color color, Stream appearanceStream)
    {
        var dict = BuildRubberStamp(annotRect, annotContents, color, icon: null);
        AddAnnotation(page, dict);
    }

    public void CreateRubberStamp(int page, System.Drawing.Rectangle annotRect, string icon, string annotContents, System.Drawing.Color color)
    {
        var dict = BuildRubberStamp(annotRect, annotContents, color, icon);
        AddAnnotation(page, dict);
    }

    private static PdfDictionary BuildRubberStamp(System.Drawing.Rectangle annotRect, string annotContents, System.Drawing.Color color, string? icon)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Stamp"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(annotRect)));
        dict.Set("Contents", Latin1(annotContents ?? ""));
        dict.Set("C", DrawingColorToPdfArray(color));
        dict.Set("CreationDate", Latin1("D:" + System.DateTime.Now.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z"));
        if (!string.IsNullOrEmpty(icon))
            dict.Set("Name", new PdfName(icon));
        return dict;
    }

    // ── Media annotations ──

    public void CreateMovie(System.Drawing.Rectangle rect, string filePath, int page)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Movie"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        var movie = new PdfDictionary();
        movie.Set("F", Latin1(filePath ?? ""));
        dict.Set("Movie", movie);
        AddAnnotation(page, dict);
    }

    public void CreateSound(System.Drawing.Rectangle rect, string filePath, string name, int page, string rate)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Sound"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        if (!string.IsNullOrEmpty(name))
            dict.Set("Name", new PdfName(name));
        var sound = new PdfDictionary();
        sound.Set("F", Latin1(filePath ?? ""));
        sound.Set("Type", new PdfName("Sound"));
        if (!string.IsNullOrEmpty(rate) && double.TryParse(rate, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var r))
            sound.Set("R", new PdfReal(r));
        dict.Set("Sound", sound);
        AddAnnotation(page, dict);
    }

    // ── File attachments ──

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, string filePath, int page, string name)
        => CreateFileAttachment(rect, contents, filePath, page, name, 1.0);

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, string filePath, int page, string name, double opacity)
    {
        var bytes = File.ReadAllBytes(filePath);
        var attachmentName = string.IsNullOrEmpty(name) ? Path.GetFileName(filePath) : name;
        AddFileAttachmentAnnotation(rect, contents, bytes, attachmentName, page, name, opacity);
    }

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, Stream attachmentStream, string attachmentName, int page, string name)
        => CreateFileAttachment(rect, contents, attachmentStream, attachmentName, page, name, 1.0);

    public void CreateFileAttachment(System.Drawing.Rectangle rect, string contents, Stream attachmentStream, string attachmentName, int page, string name, double opacity)
    {
        using var ms = new MemoryStream();
        attachmentStream.CopyTo(ms);
        AddFileAttachmentAnnotation(rect, contents, ms.ToArray(), attachmentName, page, name, opacity);
    }

    private void AddFileAttachmentAnnotation(System.Drawing.Rectangle rect, string contents, byte[] data, string attachmentName, int page, string iconName, double opacity)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("FileAttachment"));
        dict.Set("Rect", RectToPdfArray(DrawingRectToPdfRect(rect)));
        dict.Set("Contents", Latin1(contents ?? ""));
        if (!string.IsNullOrEmpty(iconName))
            dict.Set("Name", new PdfName(iconName));
        if (opacity is >= 0 and < 1.0)
            dict.Set("CA", new PdfReal(opacity));
        dict.Set("FS", BuildFileSpec(attachmentName, data));
        AddAnnotation(page, dict);
    }

    private static PdfDictionary BuildFileSpec(string name, byte[] data)
    {
        var fs = new PdfDictionary();
        fs.Set("Type", new PdfName("Filespec"));
        fs.Set("F", Latin1(name));
        var ef = new PdfDictionary();
        var streamDict = new PdfDictionary();
        streamDict.Set("Type", new PdfName("EmbeddedFile"));
        streamDict.Set("Length", new PdfInteger(data.Length));
        var ms = new PdfStream(streamDict, data);
        ef.Set("F", ms);
        fs.Set("EF", ef);
        return fs;
    }

    // ── Bookmarks / outlines ──

    public void CreateBookmarksAction(string title, System.Drawing.Color color, bool boldFlag, bool italicFlag,
        string file, string actionType, string destination)
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        var outlinesObj = doc.Reader.Resolve(catalog.Get("Outlines"));
        var outlines = outlinesObj as PdfDictionary ?? new PdfDictionary();
        if (outlinesObj is null)
        {
            outlines.Set("Type", new PdfName("Outlines"));
            outlines.Set("Count", new PdfInteger(0));
            catalog.Set("Outlines", outlines);
        }
        var item = new PdfDictionary();
        item.Set("Title", Latin1(title ?? ""));
        item.Set("C", DrawingColorToPdfArray(color));
        var flags = (boldFlag ? 2 : 0) | (italicFlag ? 1 : 0);
        if (flags != 0) item.Set("F", new PdfInteger(flags));
        if (!string.IsNullOrEmpty(file))
        {
            var act = new PdfDictionary();
            act.Set("S", new PdfName(string.IsNullOrEmpty(actionType) ? "Launch" : actionType));
            act.Set("F", Latin1(file));
            if (!string.IsNullOrEmpty(destination)) act.Set("D", Latin1(destination));
            item.Set("A", act);
        }
        // Append to outlines: simple flat list at the root.
        var first = outlines.Get("First");
        if (first is null)
        {
            outlines.Set("First", item);
            outlines.Set("Last", item);
        }
        else
        {
            var lastObj = doc.Reader.Resolve(outlines.Get("Last"));
            if (lastObj is PdfDictionary last)
            {
                last.Set("Next", item);
                item.Set("Prev", last);
                outlines.Set("Last", item);
            }
        }
        outlines.Set("Count",
            new PdfInteger((outlines.Get("Count") is PdfInteger ic ? (int)ic.Value : 0) + 1));
    }

    // ── Document-level actions / attachments ──

    public void AddDocumentAdditionalAction(string eventType, string code)
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        if (eventType == DocumentOpen)
        {
            // OpenAction lives at the catalog root, not under /AA.
            var act = new PdfDictionary();
            act.Set("S", new PdfName("JavaScript"));
            act.Set("JS", Latin1(code ?? ""));
            catalog.Set("OpenAction", act);
            return;
        }
        var aaObj = doc.Reader.Resolve(catalog.Get("AA"));
        var aa = aaObj as PdfDictionary;
        if (aa is null) { aa = new PdfDictionary(); catalog.Set("AA", aa); }
        var entry = new PdfDictionary();
        entry.Set("S", new PdfName("JavaScript"));
        entry.Set("JS", Latin1(code ?? ""));
        aa.Set(eventType, entry);
    }

    public void RemoveDocumentOpenAction()
    {
        var doc = EnsureBound();
        doc.Reader.Catalog.Remove("OpenAction");
    }

    public void AddDocumentAttachment(string fileAttachmentPath, string description)
    {
        var bytes = File.ReadAllBytes(fileAttachmentPath);
        AddAttachmentEntry(Path.GetFileName(fileAttachmentPath), bytes, description);
    }

    public void AddDocumentAttachment(Stream fileAttachmentStream, string fileAttachmentName, string description)
    {
        using var ms = new MemoryStream();
        fileAttachmentStream.CopyTo(ms);
        AddAttachmentEntry(fileAttachmentName, ms.ToArray(), description);
    }

    private void AddAttachmentEntry(string name, byte[] data, string description)
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        var namesObj = doc.Reader.Resolve(catalog.Get("Names"));
        var names = namesObj as PdfDictionary;
        if (names is null) { names = new PdfDictionary(); catalog.Set("Names", names); }
        var efObj = doc.Reader.Resolve(names.Get("EmbeddedFiles"));
        var ef = efObj as PdfDictionary;
        if (ef is null) { ef = new PdfDictionary(); names.Set("EmbeddedFiles", ef); }
        var arrObj = doc.Reader.Resolve(ef.Get("Names"));
        var arr = arrObj as PdfArray ?? new PdfArray();
        if (arrObj is null) ef.Set("Names", arr);
        var fs = BuildFileSpec(name, data);
        if (!string.IsNullOrEmpty(description))
            fs.Set("Desc", Latin1(description));
        arr.Add(Latin1(name));
        arr.Add(fs);
    }

    public void DeleteAttachments()
    {
        var doc = EnsureBound();
        var catalog = doc.Reader.Catalog;
        var names = doc.Reader.ResolveDict(catalog.Get("Names"));
        if (names is null) return;
        names.Remove("EmbeddedFiles");
    }

    public IList<Annotation> ExtractLink()
    {
        var result = new List<Annotation>();
        if (_document is null) return result;
        foreach (var page in _document.Pages)
        {
            var annots = _document.Reader.Resolve(page.Dict.Get("Annots"));
            if (annots is not PdfArray arr) continue;
            foreach (var item in arr)
            {
                var d = _document.Reader.ResolveDict(item);
                if (d?.GetName("Subtype") != "Link") continue;
                result.Add(new LinkAnnotation(d, _document.Reader));
            }
        }
        return result;
    }

    // ── Image operations ──

    public void DeleteImage()
    {
        // Delete the "current" image — first image on the first page that has one.
        var doc = EnsureBound();
        for (int p = 1; p <= doc.PageCount; p++)
        {
            var page = doc.Pages.At(p);
            var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
            var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
            if (xobjects is null) continue;
            foreach (var key in xobjects.Keys)
            {
                var xobj = doc.Reader.Resolve(xobjects.Get(key)) as PdfStream;
                if (xobj?.Dict.GetName("Subtype") == "Image")
                {
                    xobjects.Remove(key);
                    return;
                }
            }
        }
    }

    public void DeleteImage(int pageNumber, int[] index)
    {
        var page = GetPage1Based(pageNumber);
        var doc = EnsureBound();
        var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
        if (xobjects is null) return;
        var imageKeys = xobjects.Keys
            .Where(k => doc.Reader.Resolve(xobjects.Get(k)) is PdfStream s && s.Dict.GetName("Subtype") == "Image")
            .ToList();
        var toRemove = new HashSet<int>(index ?? []);
        for (int i = 0; i < imageKeys.Count; i++)
        {
            if (toRemove.Contains(i + 1) || toRemove.Contains(i))
                xobjects.Remove(imageKeys[i]);
        }
    }

    public void ReplaceImage(int pageNumber, int index, string imageFile)
    {
        var page = GetPage1Based(pageNumber);
        var doc = EnsureBound();
        var resources = doc.Reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = doc.Reader.ResolveDict(resources?.Get("XObject"));
        if (xobjects is null) return;
        var imageKeys = xobjects.Keys
            .Where(k => doc.Reader.Resolve(xobjects.Get(k)) is PdfStream s && s.Dict.GetName("Subtype") == "Image")
            .ToList();
        var target = index - 1;
        if (target < 0 || target >= imageKeys.Count) return;

        var raw = File.ReadAllBytes(imageFile);
        var streamDict = new PdfDictionary();
        streamDict.Set("Type", new PdfName("XObject"));
        streamDict.Set("Subtype", new PdfName("Image"));
        streamDict.Set("Filter", new PdfName("DCTDecode"));
        streamDict.Set("Length", new PdfInteger(raw.Length));
        // Width/Height/ColorSpace are required by spec — caller would need to set if known.
        var newStream = new PdfStream(streamDict, raw);
        xobjects.Set(imageKeys[target], newStream);
    }

    // ── Stamp move/hide (extend the existing GetStamps/DeleteStamp impl) ──

    /// <summary>
    /// Hide the stamp(s) with the given <paramref name="stampId"/> on a page without removing
    /// them: the stamp's drawing operators are kept but suppressed by an empty-clip prologue, and
    /// a <c>%StampHidden=1</c> marker records the state so <see cref="GetStamps"/> reports
    /// <see cref="StampInfo.Visible"/> = <c>false</c> and <see cref="ShowStampById"/> can restore it.
    /// </summary>
    public void HideStampById(int pageNumber, int stampId)
        => SetStampVisibility(pageNumber, stampId, visible: false);

    /// <summary>Show a previously-hidden stamp: remove the hidden marker and the empty-clip
    /// prologue so the stamp paints again. A no-op for stamps that are already visible.</summary>
    public void ShowStampById(int pageNumber, int stampId)
        => SetStampVisibility(pageNumber, stampId, visible: true);

    /// <summary>Toggle the persisted visibility of every stamp with <paramref name="stampId"/> on
    /// the given page by rewriting the page content stream.</summary>
    private void SetStampVisibility(int pageNumber, int stampId, bool visible)
    {
        var doc = EnsureBound();
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var page = doc.Pages.At(pageNumber);
        var contentBytes = GetPageContentBytes(page, doc);
        if (contentBytes.Length == 0) return;

        var content = Encoding.ASCII.GetString(contentBytes);
        var blocks = FindStampBlocks(content, page, doc);

        // Rewrite matching blocks back-to-front so each edit leaves earlier blocks' offsets valid.
        var targets = blocks
            .Where(b => b.StampId == stampId)
            .OrderByDescending(b => b.Start)
            .ToList();
        if (targets.Count == 0) return;

        var changed = false;
        foreach (var b in targets)
        {
            var block = content.Substring(b.Start, b.End - b.Start);
            var updated = visible ? UnhideStampBlock(block) : HideStampBlock(block);
            if (updated == block) continue;
            content = content[..b.Start] + updated + content[b.End..];
            changed = true;
        }

        if (changed)
            page.SetContentStream(Encoding.ASCII.GetBytes(content));
    }

    /// <summary>Add the hidden marker and an empty-clip prologue to a stamp block. Idempotent.</summary>
    private static string HideStampBlock(string block)
    {
        if (block.Contains(StampHiddenMarker.TrimEnd('\n'))) return block;
        var qIdx = FindOpeningQ(block);
        if (qIdx < 0) return block;

        // Insert the empty clip immediately after the opening 'q' and its trailing delimiter
        // so all painting in the q/Q block is clipped away.
        var afterQ = qIdx + 1;
        string withClip;
        if (afterQ < block.Length && (block[afterQ] == '\n' || block[afterQ] == ' ' || block[afterQ] == '\t'))
            withClip = block[..(afterQ + 1)] + StampHiddenClip + block[(afterQ + 1)..];
        else
            withClip = block[..afterQ] + "\n" + StampHiddenClip + block[afterQ..];

        return StampHiddenMarker + withClip;
    }

    /// <summary>Remove the hidden marker and the empty-clip prologue from a stamp block. Idempotent.</summary>
    private static string UnhideStampBlock(string block)
    {
        if (!block.Contains(StampHiddenMarker.TrimEnd('\n'))) return block;
        var b = block.Replace(StampHiddenMarker, string.Empty);
        var ci = b.IndexOf(StampHiddenClip, StringComparison.Ordinal);
        if (ci >= 0)
            b = b[..ci] + b[(ci + StampHiddenClip.Length)..];
        return b;
    }

    /// <summary>Index of the graphics-block opening <c>q</c> (the first <c>q</c> token that starts a
    /// line, after any leading <c>%</c> comment lines), or -1 if none.</summary>
    private static int FindOpeningQ(string block)
    {
        var i = 0;
        while (i < block.Length)
        {
            var nl = block.IndexOf('\n', i);
            var lineEnd = nl < 0 ? block.Length : nl;
            var j = i;
            while (j < lineEnd && (block[j] == ' ' || block[j] == '\t')) j++;
            if (j < lineEnd && block[j] == 'q' &&
                (j + 1 == lineEnd || block[j + 1] == ' ' || block[j + 1] == '\t' || block[j + 1] == '\n'))
                return j;
            i = nl < 0 ? block.Length : nl + 1;
        }
        return -1;
    }

    /// <summary>Delete all stamps with the given <paramref name="stampId"/> across every page.</summary>
    public void DeleteStampById(int stampId)
    {
        if (_document is null) return;
        for (int p = 1; p <= _document.PageCount; p++)
            DeleteStampById(p, stampId);
    }

    /// <summary>Delete all stamps whose ID is in <paramref name="stampIds"/> across every page.</summary>
    public void DeleteStampByIds(int[] stampIds)
    {
        if (_document is null) return;
        for (int p = 1; p <= _document.PageCount; p++)
            DeleteStampByIds(p, stampIds);
    }

    public void MoveStamp(int pageNumber, int stampIndex, double x, double y)
    {
        MoveStampInternal(pageNumber, stamps => stampIndex >= 0 && stampIndex < stamps.Length ? stampIndex : -1, x, y);
    }

    public void MoveStampById(int pageNumber, int stampId, double x, double y)
    {
        MoveStampInternal(pageNumber, stamps =>
        {
            for (int i = 0; i < stamps.Length; i++)
                if (stamps[i].StampId == stampId) return stamps[i].IndexOnPage;
            return -1;
        }, x, y);
    }

    private void MoveStampInternal(int pageNumber, Func<StampInfo[], int> pickIndex, double x, double y)
    {
        var doc = EnsureBound();
        if (pageNumber < 1 || pageNumber > doc.PageCount) return;
        var page = doc.Pages.At(pageNumber);
        var bytes = GetPageContentBytes(page, doc);
        if (bytes.Length == 0) return;
        var text = Encoding.ASCII.GetString(bytes);
        var stamps = ParseStamps(text, page, doc);
        var idx = pickIndex(stamps);
        if (idx < 0 || idx >= stamps.Length) return;

        var stampBlocks = FindStampBlocks(text, page, doc);
        if (idx >= stampBlocks.Count) return;
        var sb = stampBlocks[idx];
        var block = text.Substring(sb.Start, sb.End - sb.Start);

        if (!TryParseStampMatrix(block, out var m)) return;
        // Move so the stamp's displayed lower-left corner lands on (x, y): solve for the
        // content-space translation that yields that corner under the current page rotation.
        var w = Math.Abs(m[0]);
        var h = Math.Abs(m[3]);
        var box = page.MediaBox;
        var (ne, nf) = DisplayedLowerLeftToContent(x, y, w, h, page.RotateDegrees, box.Width, box.Height);
        // Bake the new origin into the placement cm's translation in place (keeping
        // its scale/rotation), so the stamp draws as `… <sx 0 0 sy ne nf cm> /XObj Do …`
        // — a single cm carrying the position. Emitting a
        // separate translation cm would push the placement matrix one operator further
        // from the end, past the /Artifact EMC that now closes an image stamp, so callers
        // reading the third-from-last operator would see the (zeroed) scale cm instead.
        var moved = RewriteMatrixTranslation(block, ne, nf);
        if (moved is null) return;
        var newText = text.Substring(0, sb.Start) + moved + text.Substring(sb.End);
        page.SetContentStream(Encoding.ASCII.GetBytes(newText));
    }

    /// <summary>
    /// Invert <see cref="RotatePoint"/> for a stamp of size (w,h): given the desired
    /// displayed lower-left corner, return the content-space translation (e,f) the cm needs.
    /// </summary>
    private static (double e, double f) DisplayedLowerLeftToContent(
        double x, double y, double w, double h, int rotate, double pageW, double pageH)
        => (((rotate % 360) + 360) % 360) switch
        {
            90 => (pageW - w - y, x),
            180 => (pageW - w - x, pageH - h - y),
            270 => (y, pageH - h - x),
            _ => (x, y),
        };

    /// <summary>Zero the translation of the stamp's positioning cm (the last cm
    /// before its <c>Do</c>) and insert an absolute <c>1 0 0 1 e f cm</c> immediately
    /// before it, so the stamp's net placement becomes (e,f) while the operator layout
    /// ends <c>… 1 0 0 1 e f cm  &lt;scale cm&gt;  /XObj Do Q</c>.</summary>
    private static string? PrependTranslationCm(string block, double e, double f)
    {
        var doMatch = Regex.Match(block, @"/[\w.\-]+\s+Do\b");
        var prefixEnd = doMatch.Success ? doMatch.Index : block.Length;
        var cms = Regex.Matches(block.Substring(0, prefixEnd),
            @"(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+cm\b");
        if (cms.Count == 0) return null;
        var last = cms[cms.Count - 1];
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var repl = $"1 0 0 1 {e.ToString(ci)} {f.ToString(ci)} cm " +
                   $"{last.Groups[1].Value} {last.Groups[2].Value} {last.Groups[3].Value} " +
                   $"{last.Groups[4].Value} 0 0 cm";
        return block.Substring(0, last.Index) + repl + block.Substring(last.Index + last.Length);
    }

    /// <summary>Rewrite the translation (e,f) of the stamp's positioning cm, leaving its scale/rotation intact.</summary>
    private static string? RewriteMatrixTranslation(string block, double e, double f)
    {
        var doMatch = Regex.Match(block, @"/[\w.\-]+\s+Do\b");
        var prefixEnd = doMatch.Success ? doMatch.Index : block.Length;
        var prefix = block.Substring(0, prefixEnd);
        var cms = Regex.Matches(prefix,
            @"(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+cm\b");
        if (cms.Count == 0) return null;
        var last = cms[cms.Count - 1];
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var repl = $"{last.Groups[1].Value} {last.Groups[2].Value} {last.Groups[3].Value} " +
                   $"{last.Groups[4].Value} {e.ToString(ci)} {f.ToString(ci)} cm";
        return block.Substring(0, last.Index) + repl + block.Substring(last.Index + last.Length);
    }

    // ── Annotation builders ──

    private static PdfDictionary BuildLaunchAnnotation(Rectangle rect, string application)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(rect));
        var act = new PdfDictionary();
        act.Set("S", new PdfName("Launch"));
        act.Set("F", Latin1(application ?? ""));
        dict.Set("A", act);
        return dict;
    }

    private static PdfDictionary BuildGoToRAnnotation(Rectangle rect, string remotePdf, int destinationPage)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("Annot"));
        dict.Set("Subtype", new PdfName("Link"));
        dict.Set("Rect", RectToPdfArray(rect));
        var act = new PdfDictionary();
        act.Set("S", new PdfName("GoToR"));
        act.Set("F", Latin1(remotePdf ?? ""));
        var dest = new PdfArray();
        dest.Add(new PdfInteger(destinationPage - 1));
        dest.Add(new PdfName("Fit"));
        act.Set("D", dest);
        dict.Set("A", act);
        return dict;
    }
}

/// <summary>
/// Line drawing parameters for PdfContentEditor.DrawCurve.
/// </summary>
public sealed class LineInfo
{
    /// <summary>Vertex coordinates as flat array [x1,y1, x2,y2, .].</summary>
    public float[]? VerticeCoordinate { get; set; }

    /// <summary>Whether the line is visible.</summary>
    public bool Visibility { get; set; } = true;

    /// <summary>Line color — red component (0-255).</summary>
    public byte LineColorR { get; set; }

    /// <summary>Line color — green component (0-255).</summary>
    public byte LineColorG { get; set; }

    /// <summary>Line color — blue component (0-255).</summary>
    public byte LineColorB { get; set; }

    /// <summary>Line colour (System.Drawing compatible).</summary>
    public System.Drawing.Color LineColor
    {
        get => System.Drawing.Color.FromArgb(LineColorR, LineColorG, LineColorB);
        set { LineColorR = value.R; LineColorG = value.G; LineColorB = value.B; }
    }

    /// <summary>Line width in points.</summary>
    public int LineWidth { get; set; } = 1;

    /// <summary>Border style indicator (PDF table-228 /BS /S code: 0=Solid, 1=Dashed, 2=Beveled, 3=Inset, 4=Underline).</summary>
    public int BorderStyle { get; set; }

    /// <summary>Dash on/off lengths in points.</summary>
    public int[]? LineDashPattern { get; set; }
}
