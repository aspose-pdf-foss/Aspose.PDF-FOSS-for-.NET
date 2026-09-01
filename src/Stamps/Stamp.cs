using Aspose.Pdf;
using Aspose.Pdf.Annotations;

namespace Aspose.Pdf.Stamps;

/// <summary>
/// Base class for stamps that can be applied to PDF pages.
/// </summary>
public abstract class Stamp
{
    /// <summary>Optional identifier embedded as a %StampId content-stream comment
    /// when the stamp is applied, so PdfContentEditor.GetStamps / DeleteStampById can
    /// find it later. 0 means unmarked (no comment emitted).</summary>
    public int StampId { get; set; }

    /// <summary>Sets <see cref="StampId"/>.</summary>
    public void setStampId(int id) { StampId = id; }

    /// <summary>PdfFileStamp facade naming:
    /// the stamp's /Fm{n} index starts at the COUNT of entries already in the page's
    /// /XObject dict, then advances to the next free name — a page with no XObjects
    /// gets /Fm0, a page already holding /Xf1 gets /Fm1, a page holding three forms
    /// gets /Fm3. The public Page.AddStamp path keeps plain next-free-from-0.</summary>
    internal bool NameFormAfterExistingXObjects { get; set; }

    /// <summary>Horizontal scale factor applied to the stamp (1.0 = original size).</summary>
    public double ZoomX { get; set; } = 1.0;

    /// <summary>Vertical scale factor applied to the stamp (1.0 = original size).</summary>
    public double ZoomY { get; set; } = 1.0;

    /// <summary>X position on the page.</summary>
    public double XIndent { get; set; }

    /// <summary>Y position on the page.</summary>
    public double YIndent { get; set; }

    /// <summary>Rotation (0, 90, 180, 270).</summary>
    public Rotation Rotate { get; set; }

    /// <summary>Arbitrary rotation angle in degrees.</summary>
    public double RotateAngle { get; set; }

    /// <summary>Opacity (0.0 = transparent, 1.0 = opaque).</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Whether the stamp is drawn behind page content.</summary>
    public bool IsBackground { get; set; }

    /// <summary>Alias for IsBackground.</summary>
    public bool Background { get => IsBackground; set => IsBackground = value; }

    /// <summary>Top margin offset in points.</summary>
    public double TopMargin { get; set; }

    /// <summary>Bottom margin offset in points.</summary>
    public double BottomMargin { get; set; }

    /// <summary>Left margin offset in points.</summary>
    public double LeftMargin { get; set; }

    /// <summary>Right margin offset in points.</summary>
    public double RightMargin { get; set; }

    /// <summary>Horizontal alignment.</summary>
    public HorizontalAlignment HorizontalAlignment { get; set; } = HorizontalAlignment.Left;

    /// <summary>Vertical alignment.</summary>
    public VerticalAlignment VerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Stroke width of the stamp's glyph/graphic outline, in points. Zero (the
    /// default) draws filled content with no outline.</summary>
    public double OutlineWidth { get; set; }

    /// <summary>Optional pre-computed bounding rectangle (page space) for this stamp.
    /// When set, the stamp emits a <c>%StampRect</c> content-stream comment so
    /// <see cref="Aspose.Pdf.Facades.PdfContentEditor.GetStamps"/> can report the stamp's
    /// exact geometry on reload instead of deriving it from the drawing matrix.</summary>
    internal Aspose.Pdf.Rectangle? MetaRect { get; set; }

    /// <summary>Apply this stamp to a page (modifies the page's content stream).</summary>
    internal abstract byte[] BuildContentStream(Page page);

    /// <summary>Apply this stamp using a caller-resolved font resource name.
    /// Default implementation ignores the name (preserves legacy behaviour for
    /// stamps that don't draw text). Text-drawing stamps override to honour
    /// the registered resource name, since a page may already use "F1" for an
    /// embedded subset that lacks Latin glyphs.</summary>
    internal virtual byte[] BuildContentStream(Page page, string fontResourceName) => BuildContentStream(page);
}
