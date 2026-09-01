using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
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
}
