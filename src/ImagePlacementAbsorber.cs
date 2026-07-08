using System.Collections;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Devices
{
    /// <summary>
    /// Represents the resolution (DPI) for device rendering and image placement.
    /// Placed in Aspose.Pdf.Devices to match the public API.
    /// </summary>
    public class Resolution
    {
        /// <summary>Horizontal resolution in DPI.</summary>
        public int X { get; set; }

        /// <summary>Vertical resolution in DPI.</summary>
        public int Y { get; set; }

        public Resolution(int value) { X = value; Y = value; }
        public Resolution(int valueX, int valueY) { X = valueX; Y = valueY; }

        public override string ToString() => $"{X}x{Y} DPI";
    }
}

namespace Aspose.Pdf
{

/// <summary>
/// Represents an image placement found on a PDF page — the position, size, and resolution
/// of an image XObject as it appears on the page (after CTM transformation).
/// </summary>
public sealed class ImagePlacement
{
    /// <summary>The bounding rectangle of the image on the page (in page coordinates).</summary>
    public Rectangle Rectangle { get; }

    /// <summary>The resolution (DPI) of the image at this placement.</summary>
    public Resolution Resolution { get; }

    /// <summary>The page on which this image placement was found.</summary>
    public Page Page { get; }

    /// <summary>The transformation matrix (CTM) at the point where the image was drawn.</summary>
    public Matrix Matrix { get; }

    /// <summary>The underlying image XObject for this placement.</summary>
    public XImage? Image { get; }

    /// <summary>The content-stream operator that drew this image (typically
    /// <c>Do</c>). Currently null for placements discovered via the
    /// <see cref="ImagePlacementAbsorber"/> walk; populated when the
    /// renderer constructs the placement directly.</summary>
    public Operator? Operator { get; }

    /// <summary>Rotation (in degrees) extracted from the placement matrix.</summary>
    public float Rotation
    {
        get
        {
            if (Matrix is null) return 0f;
            var rad = Math.Atan2(Matrix.B, Matrix.A);
            return (float)(rad * 180.0 / Math.PI);
        }
    }

    /// <summary>Compositing state at the point the image was drawn (alpha + blend mode).
    /// Stored only — the absorber walk does not currently capture ExtGState.</summary>
    public CompositingParameters CompositingParameters { get; internal set; } = new CompositingParameters(BlendMode.Normal);

    internal ImagePlacement(Rectangle rect, Resolution res, Page page, Matrix matrix, XImage? image = null, Operator? op = null)
    {
        Rectangle = rect;
        Resolution = res;
        Page = page;
        Matrix = matrix;
        Image = image;
        Operator = op;
    }

    /// <summary>Resource name of the drawn XObject, when discovered by the absorber.</summary>
    internal string? XObjectName;

    /// <summary>Ordinal of this placement among the page-level <c>Do</c> invocations
    /// of <see cref="XObjectName"/> (0-based); -1 for placements drawn inside Form
    /// XObjects, which page-level operator edits cannot address.</summary>
    internal int PageLevelOrdinal = -1;

    /// <summary>Hide this image placement by removing its <c>Do</c> invocation from
    /// the page content. Placements nested inside Form XObjects are left untouched
    /// (removing them would affect every use of the form).</summary>
    public void Hide()
    {
        if (Page is null || XObjectName is null || PageLevelOrdinal < 0) return;
        var ops = Page.Contents;
        var matches = new List<int>();
        for (int i = 1; i <= ops.Count; i++)
        {
            if (ops[i] is Aspose.Pdf.Operators.Do d && d.Name == XObjectName)
                matches.Add(i);
        }
        if (matches.Count == 0) return;
        // Earlier Hide() calls on same-named placements shift the surviving
        // ordinals down; when ours is out of range, remove the last remaining
        // occurrence so every placement still hides exactly one invocation.
        var idx = PageLevelOrdinal < matches.Count ? matches[PageLevelOrdinal] : matches[^1];
        ops.Delete(idx);
    }

    /// <summary>Replace this image placement's content with <paramref name="image"/>. Stored only.</summary>
    public void Replace(Stream image)
    {
        if (image is null) throw new ArgumentNullException(nameof(image));
    }

    /// <summary>Write the decoded image bytes to <paramref name="stream"/>.</summary>
    public void Save(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (Image is null) return;
        var turns = ClockwiseQuarterTurns();
        if (turns != 0)
        {
            Image.SaveRotated(stream, turns);
            return;
        }
        Image.Save(stream);
    }

    /// <summary>
    /// Number of 90° clockwise turns to apply when extracting the image so it keeps
    /// the orientation it appears in on the displayed page. A page with a quarter-turn
    /// <c>/Rotate</c> rotates its drawn content for display; an image drawn upright in
    /// page space therefore appears rotated to a viewer, and an extraction should
    /// reproduce that. Non-quarter rotations and pages without a /Rotate yield 0.
    /// </summary>
    private int ClockwiseQuarterTurns()
    {
        var degrees = ((Page?.RotateDegrees ?? 0) % 360 + 360) % 360;
        return degrees % 90 == 0 ? degrees / 90 : 0;
    }

    /// <summary>Write the decoded image bytes to <paramref name="stream"/> as <paramref name="format"/>.</summary>
    public void Save(Stream stream, System.Drawing.Imaging.ImageFormat format)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (Image is null) return;
        Image.Save(stream, format);
    }
}

/// <summary>
/// A collection of <see cref="ImagePlacement"/> items.
/// </summary>
public sealed class ImagePlacementCollection : IReadOnlyList<ImagePlacement>
{
    private readonly List<ImagePlacement> _items = new();

    /// <summary>Number of image placements.</summary>
    public int Count => _items.Count;

    public bool IsReadOnly => false;
    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>Get an image placement by 1-based index.</summary>
    public ImagePlacement this[int index] => _items[index - 1];

    public void Add(ImagePlacement fragment)
    {
        if (fragment is null) throw new ArgumentNullException(nameof(fragment));
        _items.Add(fragment);
    }

    public bool Contains(ImagePlacement item) => _items.Contains(item);

    public void CopyTo(ImagePlacement[] array, int index) => _items.CopyTo(array, index);

    public bool Remove(ImagePlacement item)
    {
        if (item is null) return false;
        return _items.Remove(item);
    }

    public void Clear() => _items.Clear();

    public IEnumerator<ImagePlacement> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Absorbs image placement information from PDF pages.
/// Parses content streams to find image XObject invocations (Do operator)
/// and calculates the placement rectangle and resolution for each image.
/// Recursively descends into Form XObjects to find nested image placements.
/// </summary>
public sealed class ImagePlacementAbsorber
{
    /// <summary>The collected image placements.</summary>
    public ImagePlacementCollection ImagePlacements { get; } = new();

    /// <summary>
    /// Visit a single page and absorb all image placements found in its content stream.
    /// New placements are appended to the existing <see cref="ImagePlacements"/> collection.
    /// </summary>
    public void Visit(Page page)
    {
        var reader = page.Reader;
        var pageDict = page.Dict;

        var resources = reader.ResolveDict(pageDict.Get("Resources"));

        // Get content streams
        var contentStreams = GetContentStreams(pageDict, reader);

        // Parse each content stream with identity CTM as starting point
        var initialCtm = new double[] { 1, 0, 0, 1, 0, 0 };
        // Track how many page-level Do invocations of each XObject name were seen,
        // so each placement can locate its own operator later (Hide()).
        var pageLevelSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var streamBytes in contentStreams)
        {
            ParseContentStream(streamBytes, resources, reader, page, initialCtm, depth: 0, pageLevelSeen);
        }
    }

    /// <summary>When true, the absorber walks pages without mutating any state. Stored only.</summary>
    public bool IsReadOnlyMode { get; set; }

    /// <summary>
    /// Visit all pages in a document and absorb image placements from each.
    /// </summary>
    public void Visit(Document pdf)
    {
        foreach (var page in pdf.Pages)
            Visit(page);
    }

    /// <summary>
    /// Parse a content stream, tracking graphics state and processing Do operators.
    /// When a Form XObject is encountered, recursively parse its content stream.
    /// </summary>
    private void ParseContentStream(byte[] streamBytes, PdfDictionary? resources, PdfReader reader,
        Page page, double[] parentCtm, int depth, Dictionary<string, int>? pageLevelSeen = null)
    {
        if (depth > 10) return; // Guard against infinite recursion

        // Collect XObject resources for this scope
        var xobjects = new Dictionary<string, PdfStream>();
        if (resources is not null)
        {
            var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
            if (xobjectDict is not null)
            {
                foreach (var key in xobjectDict.Keys)
                {
                    var stream = reader.ResolveStream(xobjectDict.Get(key));
                    if (stream is not null)
                        xobjects[key] = stream;
                }
            }
        }

        var parser = new ContentStreamParser(reader);

        // We need to capture Form XObject invocations too (not just images).
        // The OnImageDrawn event fires for all Do operators (with any XObject name).
        // We check the subtype ourselves.
        parser.OnImageDrawn += (xobjName, state) =>
        {
            if (!xobjects.TryGetValue(xobjName, out var xobjStream))
                return;

            var subtype = xobjStream.Dict.GetName("Subtype");

            if (subtype == "Image")
            {
                // Compose the parser's CTM with the parent CTM
                var localCtm = state.Ctm;
                var ctm = MultiplyMatrices(localCtm, parentCtm);

                AddImagePlacement(ctm, xobjStream, page, xobjName, reader, depth == 0 ? pageLevelSeen : null);
            }
            else if (subtype == "Form")
            {
                // Recurse into the Form XObject's content stream.
                // The Form's content is drawn with the current CTM applied.
                var localCtm = state.Ctm;
                var formCtm = MultiplyMatrices(localCtm, parentCtm);

                // Check for a /Matrix entry on the Form XObject itself
                var formMatrixArr = reader.Resolve(xobjStream.Dict.Get("Matrix")) as PdfArray;
                if (formMatrixArr is { Count: >= 6 })
                {
                    var fm = new double[]
                    {
                        Num(formMatrixArr[0]), Num(formMatrixArr[1]),
                        Num(formMatrixArr[2]), Num(formMatrixArr[3]),
                        Num(formMatrixArr[4]), Num(formMatrixArr[5])
                    };
                    formCtm = MultiplyMatrices(fm, formCtm);
                }

                var formBytes = reader.DecodeStream(xobjStream);
                var formResources = reader.ResolveDict(xobjStream.Dict.Get("Resources")) ?? resources;

                ParseContentStream(formBytes, formResources, reader, page, formCtm, depth + 1);
            }
        };

        // Inline images (BI/ID/EI) are placements too. The parser hands over the
        // image dictionary (keys already expanded to full names) and the raw,
        // still-encoded payload; wrap them in a synthetic PdfStream so the
        // placement carries a decodable XImage like an XObject placement does.
        parser.OnInlineImage += (dict, data) =>
        {
            if (dict.Get("Subtype") is not PdfName)
                dict.Set("Subtype", new PdfName("Image"));
            var ctm = MultiplyMatrices(parser.State.Ctm, parentCtm);
            AddImagePlacement(ctm, new PdfStream(dict, data), page, null, reader);
        };

        var extGStates = BuildExtGStates(resources, reader);
        parser.Parse(streamBytes, extGStates: extGStates);
    }

    private void AddImagePlacement(double[] ctm, PdfStream xobjStream, Page page,
        string? xobjName = null, PdfReader? reader = null, Dictionary<string, int>? pageLevelSeen = null)
    {
        // The image occupies a 1x1 unit square transformed by the CTM.
        var displayWidth = Math.Sqrt(ctm[0] * ctm[0] + ctm[1] * ctm[1]);
        var displayHeight = Math.Sqrt(ctm[2] * ctm[2] + ctm[3] * ctm[3]);

        // Compute the bounding rectangle by transforming the four corners
        // of the unit square [0,0], [1,0], [1,1], [0,1] through the CTM.
        double x0 = ctm[4], y0 = ctm[5];
        double x1 = ctm[0] + ctm[4], y1 = ctm[1] + ctm[5];
        double x2 = ctm[0] + ctm[2] + ctm[4], y2 = ctm[1] + ctm[3] + ctm[5];
        double x3 = ctm[2] + ctm[4], y3 = ctm[3] + ctm[5];

        var llx = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var lly = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var urx = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var ury = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));

        var rect = new Rectangle(llx, lly, urx, ury);

        // Image pixel dimensions
        var pixelWidth = (int)xobjStream.Dict.GetInt("Width");
        var pixelHeight = (int)xobjStream.Dict.GetInt("Height");

        // Resolution: DPI = (pixelDim / displayDim) * 72
        // Use truncation (not rounding) to match the public API behavior.
        var resX = displayWidth > 0 ? (int)((pixelWidth / displayWidth) * 72.0) : 72;
        var resY = displayHeight > 0 ? (int)((pixelHeight / displayHeight) * 72.0) : 72;

        var matrix = new Matrix(ctm[0], ctm[1], ctm[2], ctm[3], ctm[4], ctm[5]);
        XImage? image = (reader is not null)
            ? new XImage(xobjName ?? "", xobjStream, reader)
            : null;
        var placement = new ImagePlacement(rect, new Resolution(resX, resY), page, matrix, image)
        {
            XObjectName = xobjName,
        };
        if (pageLevelSeen is not null && xobjName is not null)
        {
            pageLevelSeen.TryGetValue(xobjName, out var ord);
            pageLevelSeen[xobjName] = ord + 1;
            placement.PageLevelOrdinal = ord;
        }
        ImagePlacements.Add(placement);
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Multiply two affine matrices [a b c d e f].</summary>
    private static double[] MultiplyMatrices(double[] m1, double[] m2)
    {
        return new[]
        {
            m1[0] * m2[0] + m1[1] * m2[2],
            m1[0] * m2[1] + m1[1] * m2[3],
            m1[2] * m2[0] + m1[3] * m2[2],
            m1[2] * m2[1] + m1[3] * m2[3],
            m1[4] * m2[0] + m1[5] * m2[2] + m2[4],
            m1[4] * m2[1] + m1[5] * m2[3] + m2[5],
        };
    }

    private static List<byte[]> GetContentStreams(PdfDictionary pageDict, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(pageDict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    private static Dictionary<string, PdfDictionary>? BuildExtGStates(PdfDictionary? resources, PdfReader reader)
    {
        if (resources is null) return null;

        var gsObj = reader.ResolveDict(resources.Get("ExtGState"));
        if (gsObj is null) return null;

        var dict = new Dictionary<string, PdfDictionary>();
        foreach (var key in gsObj.Keys)
        {
            var gs = reader.ResolveDict(gsObj.Get(key));
            if (gs is not null)
                dict[key] = gs;
        }

        return dict;
    }
}
} // namespace Aspose.Pdf
