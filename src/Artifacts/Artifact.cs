using System.IO;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Represents a PDF artifact — a marked content sequence tagged as /Artifact
/// (PDF 32000 §14.8.2.2). Artifacts carry non-content page elements such as
/// headers, footers, watermarks, and page numbers.
/// </summary>
public class Artifact
{
    /// <summary>Enumerates artifact types as defined by PDF 32000 §14.8.2.2.</summary>
    public enum ArtifactType
    {
        /// <summary>Ancillary page features such as running heads and folios (page numbers).</summary>
        Pagination = 0,
        /// <summary>Purely cosmetic typographical or design elements such as footnote rules or background screens.</summary>
        Layout = 1,
        /// <summary>Production aids extraneous to the document itself, such as cut marks and colour bars.</summary>
        Page = 2,
        /// <summary>Images, patterns or coloured blocks.</summary>
        Background = 3,
        /// <summary>Artifact type is not defined or unknown.</summary>
        Undefined = 4,
    }

    /// <summary>Enumerates artifact subtypes.</summary>
    public enum ArtifactSubtype
    {
        /// <summary>Header artifact.</summary>
        Header = 0,
        /// <summary>Footer artifact.</summary>
        Footer = 1,
        /// <summary>Watermark artifact.</summary>
        Watermark = 2,
        /// <summary>Background artifact.</summary>
        Background = 3,
        /// <summary>Artifact subtype is not defined or unknown.</summary>
        Undefined = 4,
        /// <summary>Bates Numbering artifact.</summary>
        BatesN = 5,
    }

    /// <summary>Default constructor — creates an artifact with Undefined type and subtype.</summary>
    public Artifact() { }

    /// <summary>Creates an artifact with the given type and subtype.</summary>
    public Artifact(ArtifactType type, ArtifactSubtype subType)
    {
        Type = type;
        Subtype = subType;
    }

    /// <summary>Creates an artifact with string-based custom type and subtype.</summary>
    public Artifact(string type, string subType)
    {
        CustomType = type;
        CustomSubtype = subType;
    }

    /// <summary>The artifact's type classification.</summary>
    public ArtifactType Type { get; set; } = ArtifactType.Undefined;

    /// <summary>The artifact's subtype.</summary>
    public ArtifactSubtype Subtype { get; set; } = ArtifactSubtype.Undefined;

    /// <summary>Name of non-standard artifact type.</summary>
    public string? CustomType { get; set; }

    /// <summary>Name of non-standard artifact subtype.</summary>
    public string? CustomSubtype { get; set; }

    /// <summary>Text content extracted from the artifact's content stream operators.</summary>
    public string? Text { get; set; }

    /// <summary>Bounding box from the /BBox entry in the properties dictionary.</summary>
    public Rectangle? Rectangle { get; internal set; }

    /// <summary>Opacity of the artifact (from ExtGState /ca).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>The page this artifact belongs to.</summary>
    public Page? Page { get; internal set; }

    /// <summary>Horizontal alignment (from /Attached entry). Ignored if
    /// <see cref="Position"/> is explicitly set.</summary>
    public HorizontalAlignment ArtifactHorizontalAlignment { get; set; } = HorizontalAlignment.None;

    /// <summary>Vertical alignment (from /Attached entry). Ignored if
    /// <see cref="Position"/> is explicitly set.</summary>
    public VerticalAlignment ArtifactVerticalAlignment { get; set; } = VerticalAlignment.None;

    /// <summary>Whether this is a background artifact.</summary>
    public bool IsBackground { get; set; }

    /// <summary>Rotation angle in degrees.</summary>
    public double Rotation { get; set; }

    /// <summary>Background color.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Right margin. Ignored if <see cref="Position"/> is explicitly set.</summary>
    public double RightMargin { get; set; }

    /// <summary>Left margin. Ignored if <see cref="Position"/> is explicitly set.</summary>
    public double LeftMargin { get; set; }

    /// <summary>Top margin. Ignored if <see cref="Position"/> is explicitly set.</summary>
    public double TopMargin { get; set; }

    /// <summary>Bottom margin. Ignored if <see cref="Position"/> is explicitly set.</summary>
    public double BottomMargin { get; set; }

    /// <summary>Explicit placement coordinates. Overrides margins and alignments.</summary>
    public Point? Position { get; set; }

    /// <summary>Formatted text for the artifact.</summary>
    public FormattedText? FormattedText { get; set; }

    /// <summary>Text state for artifact text.</summary>
    public TextState? TextState { get; set; }

    /// <summary>Substring of <see cref="Text"/> that the renderer replaces with
    /// the 1-based page number when the artifact is added to a page. Default
    /// is "#". Setting it to <c>null</c> or empty disables substitution.</summary>
    internal string? PageNumberReplacementString { get; set; } = "#";

    /// <summary>Sets the page-number replacement token used in <see cref="Text"/>.
    /// Pass <c>null</c> or an empty string to disable substitution.</summary>
    public void SetPageNumberReplacementString(string value)
    {
        PageNumberReplacementString = value;
    }

    /// <summary>Multi-line text content. Populated by <see cref="SetLinesAndState"/>.</summary>
    public List<string> Lines { get; } = new();

    /// <summary>Sets multi-line text content with the given text state.</summary>
    public void SetLinesAndState(string[] text, TextState textState)
    {
        Lines.Clear();
        if (text is not null)
            foreach (var l in text) Lines.Add(l);
        TextState = textState;
        Text = text is { Length: > 0 } ? string.Join("\n", text) : null;
    }

    /// <summary>Internal content-stream operators that draw the artifact.
    /// Empty in this build — artifacts are emitted as marked-content
    /// sequences during page write-out, not stored as operator lists.</summary>
    public List<Operator> Contents { get; } = new();

    /// <summary>The Form XObject backing this artifact when it is rendered as a
    /// reusable form (e.g. watermark templates). Null until SetImage / SetText
    /// promotes the artifact to a Form-backed representation; the FOSS path
    /// keeps it null and emits inline content.</summary>
    public XForm? Form { get; private set; }

    /// <summary>The Image XObject backing this artifact when its content is a
    /// raster image. Populated by <see cref="SetImage(Stream)"/> /
    /// <see cref="SetImage(string)"/>.</summary>
    public XImage? Image { get; internal set; }

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private bool _updating;

    /// <summary>Begin a batched property update. Mutations between
    /// <see cref="BeginUpdates"/> and <see cref="SaveUpdates"/> are coalesced
    /// rather than flushed individually. No-op in this build (the FOSS
    /// renderer doesn't have a queued-write path for artifacts).</summary>
    public void BeginUpdates() { _updating = true; }

    /// <summary>Flush a batched update started by <see cref="BeginUpdates"/>.
    /// No-op in this build.</summary>
    public void SaveUpdates() { _updating = false; }

    /// <summary>Releases resources held by the artifact. Currently a no-op —
    /// artifacts are pure value objects in this build.</summary>
    public void Dispose() { _values.Clear(); _ = _updating; }

    /// <summary>Read a custom name/value pair set via <see cref="SetValue(string, string)"/>.</summary>
    public string? GetValue(string name)
        => name is null ? null : _values.TryGetValue(name, out var v) ? v : null;

    /// <summary>Store a custom name/value pair on the artifact (for downstream
    /// metadata access; not emitted into the PDF in this build).</summary>
    public void SetValue(string name, string value)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        _values[name] = value ?? string.Empty;
    }

    /// <summary>Remove a custom name/value pair previously set via <see cref="SetValue"/>.</summary>
    public void RemoveValue(string name)
    {
        if (name is null) return;
        _values.Remove(name);
    }

    /// <summary>Stored raw bytes for an image attached via
    /// <see cref="SetImage(Stream)"/> / <see cref="SetImage(string)"/>.
    /// The <see cref="Image"/> property stays null because XImage requires
    /// a backing PdfStream that only exists after the artifact has been
    /// written into a page's resource dictionary.</summary>
    internal byte[]? RawImageBytes { get; private set; }

    /// <summary>Stored source path for an image attached via
    /// <see cref="SetImage(string)"/>; null otherwise.</summary>
    internal string? RawImagePath { get; private set; }

    /// <summary>Attach a raster image to the artifact. The stream contents are
    /// captured into <see cref="RawImageBytes"/>; <see cref="Image"/> remains
    /// null until the artifact is flushed into a page (the FOSS image-write
    /// path runs at page-save time).</summary>
    public void SetImage(Stream imageStream)
    {
        if (imageStream is null) throw new ArgumentNullException(nameof(imageStream));
        using var ms = new MemoryStream();
        if (imageStream.CanSeek) imageStream.Position = 0;
        imageStream.CopyTo(ms);
        RawImageBytes = ms.ToArray();
    }

    /// <summary>Attach a raster image by path. The path is captured into
    /// <see cref="RawImagePath"/> and the file is read into
    /// <see cref="RawImageBytes"/>.</summary>
    public void SetImage(string imageName)
    {
        if (imageName is null) throw new ArgumentNullException(nameof(imageName));
        RawImagePath = imageName;
        using var fs = File.OpenRead(imageName);
        SetImage(fs);
    }

    /// <summary>Set the artifact text from a <see cref="FormattedText"/> instance.
    /// The plain-text value is captured into <see cref="Text"/>; the styled
    /// FormattedText is stored on <see cref="FormattedText"/>.</summary>
    public void SetText(FormattedText formattedText)
    {
        FormattedText = formattedText;
        Text = formattedText?.Text;
    }

    /// <summary>Set <see cref="Text"/> and <see cref="TextState"/> together.</summary>
    public void SetTextAndState(string text, TextState textState)
    {
        Text = text;
        TextState = textState;
    }

    /// <summary>Bind this artifact to a specific page. The page reference is
    /// stored for later flush; the page's artifact collection is not mutated
    /// by this call (use <c>page.Artifacts.Add(artifact)</c> for that).</summary>
    public void SetPdfPage(Page page)
    {
        if (page is null) throw new ArgumentNullException(nameof(page));
        Page = page;
    }
}
