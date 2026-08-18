using System.Globalization;
using System.IO;
using System.Text;
using Aspose.Pdf.Content;
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
    /// and flushed together by <see cref="SaveUpdates"/>.</summary>
    public void BeginUpdates() { _updating = true; }

    /// <summary>Flush a batched update started by <see cref="BeginUpdates"/>.
    /// For an artifact parsed from a page, this rewrites its /Artifact
    /// marked-content block in the page content stream so the mutated
    /// properties and text round-trip through save + reopen.</summary>
    public void SaveUpdates()
    {
        _updating = false;
        Page?.Artifacts.RewriteArtifactBlockFor(this);
    }

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
        SourcePage = page;
    }

    /// <summary>Source page captured by <see cref="SetPdfPage"/>; drawn as a
    /// Form XObject when the artifact is rendered.</summary>
    internal Page? SourcePage { get; private set; }

    // ── Round-trip emission (PDF 32000 §14.8.2.2) ─────────────────────────
    //
    // Adding an artifact via ArtifactCollection.Add renders it into the page
    // content stream as an /Artifact BMC…EMC marked-content block. The block's
    // BDC properties dictionary carries the artifact's /Type, /Subtype, /BBox
    // and the round-trip metadata (/Opacity, /Rotation, /Position, plus any
    // custom name/value pairs) so the same artifact reappears — with the same
    // properties — when the page is reopened and reparsed by ArtifactCollection.

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string EscapeLiteral(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "").Replace("\n", " ");

    /// <summary>Property keys written by <see cref="BuildPropsDict"/> that are
    /// consumed structurally — everything else in the dictionary is a custom
    /// name/value pair set via <see cref="SetValue"/>.</summary>
    internal static readonly HashSet<string> ReservedPropertyKeys =
        new(StringComparer.Ordinal) { "Type", "Subtype", "BBox", "Attached", "Opacity", "Rotation", "Position" };

    private string BuildPropsDict(Rectangle bbox)
    {
        var sb = new StringBuilder("<<");
        var typeName = CustomType ?? Type.ToString();
        var subName = CustomSubtype ?? Subtype.ToString();
        sb.Append($"/Type /{typeName} /Subtype /{subName}");
        sb.Append($" /BBox [{F(bbox.LLX)} {F(bbox.LLY)} {F(bbox.URX)} {F(bbox.URY)}]");
        if (Opacity < 0.999) sb.Append($" /Opacity {F(Opacity)}");
        if (Math.Abs(Rotation) > 0.001) sb.Append($" /Rotation {F(Rotation)}");
        if (Position is { } p) sb.Append($" /Position [{F(p.X)} {F(p.Y)}]");
        foreach (var kv in _values)
            sb.Append($" /{kv.Key} ({EscapeLiteral(kv.Value)})");
        sb.Append(">>");
        return sb.ToString();
    }

    private void AppendOpacity(Page page, StringBuilder sb)
    {
        if (Opacity >= 0.999) return;
        var gs = new ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity };
        sb.Append($"/{page.AddExtGState(gs)} gs\n");
    }

    private void Flush(Page page, StringBuilder sb)
    {
        var bytes = Encoding.ASCII.GetBytes(sb.ToString());
        if (IsBackground) page.PrependContentStream(bytes);
        else page.AddContentStream(bytes);
    }

    private (double x, double y) ComputePlacement(Page page, double w, double h)
    {
        if (Position is { } p) return (p.X, p.Y);
        double x = ArtifactHorizontalAlignment switch
        {
            HorizontalAlignment.Left => LeftMargin,
            HorizontalAlignment.Right => page.Width - w - RightMargin,
            _ => (page.Width - w) / 2,
        };
        double y = ArtifactVerticalAlignment switch
        {
            VerticalAlignment.Top => page.Height - h - TopMargin,
            VerticalAlignment.Bottom => BottomMargin,
            _ => (page.Height - h) / 2,
        };
        return (x, y);
    }

    /// <summary>Render this artifact into <paramref name="page"/> as an /Artifact
    /// marked-content block. Called by <see cref="ArtifactCollection.Add(Artifact)"/>.</summary>
    internal void RenderToPage(Page page)
    {
        byte[]? imageBytes = RawImageBytes;
        if (imageBytes is null && this is BackgroundArtifact { BackgroundImage: { } bgImg })
        {
            using var ms = new MemoryStream();
            if (bgImg.CanSeek) bgImg.Position = 0;
            bgImg.CopyTo(ms);
            imageBytes = ms.ToArray();
        }

        if (imageBytes is not null) { EmitImage(page, imageBytes); return; }
        if (BackgroundColor is { } bg) { EmitBackgroundColor(page, bg); return; }
        if (!string.IsNullOrEmpty(Text) || FormattedText is not null) { EmitText(page); return; }
        if (SourcePage is not null) { EmitPageSource(page); return; }
    }

    /// <summary>Render a page-source artifact (set via <see cref="SetPdfPage"/>): the
    /// foreign page is imported as a Form XObject into this page's resources (cross-doc
    /// object remapping) and drawn inside the /Artifact … BDC … EMC block, so the
    /// artifact round-trips through save + reopen (and Delete splices the block out).</summary>
    private void EmitPageSource(Page page)
    {
        var src = SourcePage;
        if (src is null) return;

        var stamp = new PdfPageStamp(src);
        var (x, y) = Position is { } p ? (p.X, p.Y) : (0.0, 0.0);
        stamp.XIndent = x;
        stamp.YIndent = y;

        // BuildContentStream imports the source page's resource graph into this document,
        // registers a Form XObject in the page resources, and returns the draw ops
        // (q sx 0 0 sy x y cm /FmN Do Q).
        var drawBytes = stamp.BuildContentStream(page);
        if (drawBytes.Length == 0) return;
        var draw = Encoding.ASCII.GetString(drawBytes).Trim();

        var bbox = new Rectangle(x, y, x + stamp.Width, y + stamp.Height);
        Rectangle = bbox;

        var sb = new StringBuilder("q\n");
        AppendOpacity(page, sb);
        sb.Append($"/Artifact {BuildPropsDict(bbox)} BDC\n");
        sb.Append(draw).Append('\n');
        sb.Append("EMC\nQ\n");
        Flush(page, sb);
    }

    private void EmitImage(Page page, byte[] imageBytes)
    {
        var stamp = new ImageStamp(new MemoryStream(imageBytes));
        int iw = stamp.PixelWidth, ih = stamp.PixelHeight;
        var imgName = stamp.RegisterXObject(page);

        double dw = iw, dh = ih, x, y;
        if (IsBackground) { dw = page.Width; dh = page.Height; x = 0; y = 0; }
        else (x, y) = ComputePlacement(page, iw, ih);

        var bbox = new Rectangle(x, y, x + dw, y + dh);
        Rectangle = bbox;

        var sb = new StringBuilder("q\n");
        AppendOpacity(page, sb);
        sb.Append($"/Artifact {BuildPropsDict(bbox)} BDC\n");
        sb.Append($"{F(dw)} 0 0 {F(dh)} {F(x)} {F(y)} cm\n");
        sb.Append($"/{imgName} Do\n");
        sb.Append("EMC\nQ\n");
        Flush(page, sb);
    }

    private void EmitBackgroundColor(Page page, Color color)
    {
        double w = page.Width, h = page.Height;
        var bbox = new Rectangle(0, 0, w, h);
        Rectangle = bbox;

        var sb = new StringBuilder("q\n");
        AppendOpacity(page, sb);
        sb.Append($"{F(color.R / 255.0)} {F(color.G / 255.0)} {F(color.B / 255.0)} rg\n");
        sb.Append($"/Artifact {BuildPropsDict(bbox)} BDC\n");
        sb.Append($"0 0 {F(w)} {F(h)} re\nf\n");
        sb.Append("EMC\nQ\n");
        Flush(page, sb);
    }

    private void EmitText(Page page)
    {
        var renderText = Text ?? FormattedText?.Text;
        if (!string.IsNullOrEmpty(renderText) && !string.IsNullOrEmpty(PageNumberReplacementString))
            renderText = renderText.Replace(PageNumberReplacementString,
                page.Number.ToString(CultureInfo.InvariantCulture));
        if (string.IsNullOrEmpty(renderText)) return;

        var fontName = Table.RegisterFont(page);
        double fontSize = TextState?.FontSize ?? FormattedText?.FontSize ?? 12;
        var baseFont = TextState?.FontName ?? FormattedText?.FontName ?? "Helvetica";

        var lines = renderText.Replace("\r\n", "\n").Split('\n');
        double textW = 0;
        foreach (var l in lines) textW = Math.Max(textW, MeasureTextWidth(l, baseFont, fontSize));
        double lineHeight = TextLineHeight(baseFont, fontSize);
        double descent = Standard14Fonts.IsStandard14(baseFont)
            ? Math.Abs(Standard14Fonts.GetDescent(baseFont)) * fontSize / 1000.0
            : fontSize * 0.2;
        double textH = lineHeight + (lines.Length - 1) * fontSize * 1.2;

        // Render the text into a Form XObject sized to the text box, then place the
        // form on the page with a single `cm` translate. Emitting the text through a
        // form (rather than inline) keeps the page-content operator sequence a clean
        // q / BDC / cm / Do / EMC / Q — the artifact's placement is the ConcatenateMatrix.
        var color = TextState?.ForegroundColor ?? FormattedText?.ForegroundColor;
        var inner = new StringBuilder();
        if (color is { } c) inner.Append($"{F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)} rg\n");
        inner.Append("BT\n");
        inner.Append($"/{fontName} {F(fontSize)} Tf\n");
        // Baseline of the bottom line sits a descent above the box floor; upper lines stack above.
        double topBaseline = descent + (lines.Length - 1) * fontSize * 1.2;
        inner.Append($"1 0 0 1 0 {F(topBaseline)} Tm\n");
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) inner.Append($"0 {F(-fontSize * 1.2)} Td\n");
            inner.Append($"({EscapeLiteral(lines[i])}) Tj\n");
        }
        inner.Append("ET\n");

        var formName = page.AddStampForm(Encoding.ASCII.GetBytes(inner.ToString()),
            new Rectangle(0, 0, textW, textH));

        var (x, y) = ComputePlacement(page, textW, textH);
        var bbox = new Rectangle(x, y, x + textW, y + textH);
        Rectangle = bbox;

        var sb = new StringBuilder("q\n");
        AppendOpacity(page, sb);
        sb.Append($"/Artifact {BuildPropsDict(bbox)} BDC\n");
        sb.Append($"1 0 0 1 {F(x)} {F(y)} cm\n");
        sb.Append($"/{formName} Do\n");
        sb.Append("EMC\nQ\n");
        Flush(page, sb);
    }

    /// <summary>Build a self-contained <c>/Artifact «props» BDC … EMC</c> block that
    /// reflects this artifact's current (mutated) properties and text. Used by
    /// <see cref="ArtifactCollection.RewriteArtifactBlockFor"/> to replace a parsed
    /// artifact's existing block in place when <see cref="SaveUpdates"/> is called,
    /// so the edits round-trip through save + reopen.</summary>
    internal string BuildInPlaceBlock(Page page)
    {
        var renderText = Text ?? FormattedText?.Text ?? string.Empty;
        double fontSize = TextState?.FontSize ?? FormattedText?.FontSize ?? 12;
        var baseFont = TextState?.FontName ?? FormattedText?.FontName ?? "Helvetica";
        double textW = Math.Max(MeasureTextWidth(renderText, baseFont, fontSize), 1);
        double textH = Math.Max(TextLineHeight(baseFont, fontSize), 1);

        var (x, y) = Position is { } p ? (p.X, p.Y) : (0.0, 0.0);
        var bbox = new Rectangle(x, y, x + textW, y + textH);
        Rectangle = bbox;

        var sb = new StringBuilder();
        sb.Append($"/Artifact {BuildPropsDict(bbox)} BDC\n");
        if (!string.IsNullOrEmpty(renderText))
        {
            var fontName = Table.RegisterFont(page);
            var color = TextState?.ForegroundColor ?? FormattedText?.ForegroundColor;
            sb.Append("q\n");
            AppendOpacity(page, sb);
            sb.Append("BT\n");
            if (color is { } c) sb.Append($"{F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)} rg\n");
            sb.Append($"/{fontName} {F(fontSize)} Tf\n");
            sb.Append($"1 0 0 1 {F(x)} {F(y)} Tm\n");
            sb.Append($"({EscapeLiteral(renderText)}) Tj\n");
            sb.Append("ET\nQ\n");
        }
        sb.Append("EMC");
        return sb.ToString();
    }

    /// <summary>Advance width of <paramref name="text"/> in points for a Standard-14
    /// base font (AFM widths); falls back to a half-em estimate for embedded fonts.</summary>
    protected static double MeasureTextWidth(string text, string baseFont, double fontSize)
    {
        if (!Standard14Fonts.IsStandard14(baseFont)) return text.Length * fontSize * 0.5;
        var sum = 0;
        foreach (var ch in text)
        {
            var w = ch <= 255 ? Standard14Fonts.GetWidth(baseFont, ch) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth(baseFont);
            sum += w;
        }
        return sum * fontSize / 1000.0;
    }

    /// <summary>Single-line text-box height in points: cap height + descent for a
    /// Standard-14 font, else the font size.</summary>
    private static double TextLineHeight(string baseFont, double fontSize)
    {
        if (!Standard14Fonts.IsStandard14(baseFont)) return fontSize;
        var cap = Standard14Fonts.GetCapHeight(baseFont);
        var desc = Math.Abs(Standard14Fonts.GetDescent(baseFont));
        return (cap + desc) * fontSize / 1000.0;
    }
}
