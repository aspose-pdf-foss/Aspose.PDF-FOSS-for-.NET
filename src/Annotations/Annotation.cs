using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Represents the default appearance of a free text annotation.
/// Encapsulates font name, size, and text color used to generate the /DA string.
/// </summary>
public class DefaultAppearance
{
    /// <summary>Create a default appearance with font, size, and color.</summary>
    public DefaultAppearance(string fontName, double fontSize, System.Drawing.Color textColor)
    {
        FontName = fontName ?? "Helvetica";
        FontSize = fontSize;
        TextColor = textColor;
    }

    /// <summary>Create a default appearance with just a font name and size.</summary>
    public DefaultAppearance(string fontName, double fontSize)
        : this(fontName, fontSize, System.Drawing.Color.Black) { }

    /// <summary>Create a default appearance from a typed Font instance plus
    /// size and color. The font name is taken from the Font's normalized name
    /// (subset prefix and comma-separated style stripped); the Font is retained so
    /// an embeddable face can be written into the form when the owning field is
    /// added.</summary>
    public DefaultAppearance(Aspose.Pdf.Text.Font font, double fontSize, System.Drawing.Color textColor)
        : this(font?.FontName ?? "Helvetica", fontSize, textColor)
    {
        EmbeddedFont = font;
    }

    /// <summary>Create with default values (Helvetica 12pt black).</summary>
    public DefaultAppearance()
        : this("Helvetica", 12, System.Drawing.Color.Black) { }

    /// <summary>Font name (e.g. "Arial", "Helvetica").</summary>
    public string FontName { get; set; }

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; }

    /// <summary>Text color.</summary>
    public System.Drawing.Color TextColor { get; set; }

    /// <summary>PDF resource name used to reference this font in /DA. Defaults to
    /// the font name with spaces stripped; once the font is embedded into the form
    /// (a composite face), this is the generated resource name (e.g. "C0_0").</summary>
    public string FontResourceName
    {
        get => _fontResourceName ?? FontName.Replace(" ", "");
        set => _fontResourceName = value;
    }
    private string? _fontResourceName;

    /// <summary>The typed font this appearance was built from (when constructed from
    /// a Font); null otherwise. Retained so the field can embed an embeddable face.</summary>
    public Aspose.Pdf.Text.Font? Font => EmbeddedFont;

    /// <summary>Backing font supplied to the typed constructor.</summary>
    internal Aspose.Pdf.Text.Font? EmbeddedFont { get; set; }

    /// <summary>Raw /DA appearance string.</summary>
    public string Text => ToAppearanceString();

    /// <summary>Generate the PDF /DA appearance string (e.g. "/Helv 12 Tf 0 0 0 rg").</summary>
    internal string ToAppearanceString()
    {
        var r = TextColor.R / 255.0;
        var g = TextColor.G / 255.0;
        var b = TextColor.B / 255.0;
        // Use a resource name derived from the font name (strip spaces, prefix with /)
        var resName = "/" + FontName.Replace(" ", "");
        return $"{resName} {FontSize:G} Tf {r:F3} {g:F3} {b:F3} rg";
    }
}

/// <summary>
/// Annotation flags as defined in PDF spec Table 165.
/// </summary>
[Flags]
public enum AnnotationFlags
{
    /// <summary>Default flag set — alias for <see cref="None"/>.</summary>
    Default = 0,
    None = 0,
    Invisible = 1 << 0,
    Hidden = 1 << 1,
    Print = 1 << 2,
    NoZoom = 1 << 3,
    NoRotate = 1 << 4,
    NoView = 1 << 5,
    ReadOnly = 1 << 6,
    Locked = 1 << 7,
    ToggleNoView = 1 << 8,
    LockedContents = 1 << 9,
}

/// <summary>
/// Annotation subtype as defined in PDF spec Table 169.
/// </summary>
public enum AnnotationType
{
    Unknown,
    Text,
    Link,
    FreeText,
    Line,
    Square,
    Circle,
    Polygon,
    PolyLine,
    Highlight,
    Underline,
    Squiggly,
    StrikeOut,
    Stamp,
    Caret,
    Ink,
    Popup,
    FileAttachment,
    Sound,
    Movie,
    Widget,
    Screen,
    PrinterMark,
    TrapNet,
    Watermark,
    ThreeD,
    /// <summary>alias for <see cref="ThreeD"/>.</summary>
    PDF3D = ThreeD,
    Redact,
    /// <summary>alias for <see cref="Redact"/>.</summary>
    Redaction = Redact,
    RichMedia,
    /// <summary>Pre-press bleed-mark annotation.</summary>
    BleedMark,
    /// <summary>Pre-press color-bar annotation.</summary>
    ColorBar,
    /// <summary>Pre-press page-information annotation.</summary>
    PageInformation,
    /// <summary>Pre-press registration-mark annotation.</summary>
    RegistrationMark,
    /// <summary>Pre-press trim-mark annotation.</summary>
    TrimMark,
}

/// <summary>
/// Represents a PDF annotation.
/// </summary>
public partial class Annotation : BaseParagraph
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;
    private int _dictObjNum = -1;
    private PdfDictionary? _pageDict;
    private Page? _ownerPage;
    private Page? _creationPage;

    internal Annotation(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>Constructor for creating new annotations programmatically.</summary>
    protected Annotation(Page page, Rectangle rect)
    {
        _dict = new PdfDictionary();
        _reader = PdfReader.Empty;
        _creationPage = page;
        if (rect != null)
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(rect.LLX));
            arr.Add(new PdfReal(rect.LLY));
            arr.Add(new PdfReal(rect.URX));
            arr.Add(new PdfReal(rect.URY));
            _dict.Set("Rect", arr);
        }
    }

    /// <summary>Document-bound ctor for creating an annotation that isn't yet
    /// attached to a specific page; the caller adds it via
    /// <c>page.Annotations.Add(annot)</c>. The same dict can be reused across
    /// multiple pages.</summary>
    protected Annotation(Document document, Rectangle rect)
    {
        _dict = new PdfDictionary();
        _reader = PdfReader.Empty;
        if (rect != null)
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(rect.LLX));
            arr.Add(new PdfReal(rect.LLY));
            arr.Add(new PdfReal(rect.URX));
            arr.Add(new PdfReal(rect.URY));
            _dict.Set("Rect", arr);
        }
    }


    /// <summary>The annotation subtype.</summary>
    public AnnotationType AnnotationType => ParseSubtype(_dict.GetName("Subtype"));

    /// <summary>Dispatch this annotation to the typed
    /// <c>Visit(SubType)</c> overload of <paramref name="visitor"/>. The
    /// base implementation is a no-op so callers operating on a raw
    /// <see cref="Annotation"/> reference can still invoke
    /// <c>Accept</c>; concrete subclasses override this to land on the
    /// correct typed Visit overload.</summary>
    public virtual void Accept(AnnotationSelector visitor) { _ = visitor; }

    /// <summary>The annotation rectangle (position on page).</summary>
    public Rectangle? Rect
    {
        get
        {
            var arr = _reader.Resolve(_dict.Get("Rect")) as PdfArray;
            return arr is { Count: >= 4 } ? Rectangle.FromPdfArray(arr, _reader) : null;
        }
        set
        {
            if (value is null)
            {
                _dict.Remove("Rect");
            }
            else
            {
                var arr = new PdfArray();
                arr.Add(new PdfReal(value.LLX));
                arr.Add(new PdfReal(value.LLY));
                arr.Add(new PdfReal(value.URX));
                arr.Add(new PdfReal(value.URY));
                _dict.Set("Rect", arr);
            }
        }
    }

    /// <summary>The width of the annotation rectangle. Setting widens
    /// the rectangle by moving URX, preserving LLX.</summary>
    public double Width
    {
        get => Rect is { } r ? r.Width : 0;
        set
        {
            var r = Rect ?? new Rectangle(0, 0, 0, 0);
            Rect = new Rectangle(r.LLX, r.LLY, r.LLX + value, r.URY);
        }
    }

    /// <summary>The height of the annotation rectangle. Setting widens
    /// the rectangle by moving URY, preserving LLY.</summary>
    public double Height
    {
        get => Rect is { } r ? r.Height : 0;
        set
        {
            var r = Rect ?? new Rectangle(0, 0, 0, 0);
            Rect = new Rectangle(r.LLX, r.LLY, r.URX, r.LLY + value);
        }
    }

    /// <summary>Bounding rectangle adjusted by <paramref name="considerRotation"/>.
    /// When true, rotates the rectangle's corners by the annotation's
    /// /Rotate entry before returning the axis-aligned bounding box;
    /// otherwise returns <see cref="Rect"/> as-is.</summary>
    public Rectangle? GetRectangle(bool considerRotation)
    {
        var r = Rect;
        if (r is null) return null;
        if (!considerRotation) return r;
        var rotate = (int)(_dict.Get("Rotate") is PdfInteger pi ? pi.Value : 0);
        if (rotate == 0) return r;
        var clone = new Rectangle(r.LLX, r.LLY, r.URX, r.URY);
        clone.Rotate(rotate);
        return clone;
    }

    /// <summary>Adjust the annotation's geometry after the user resized
    /// the page or the host rectangle. The transform is applied to /Rect;
    /// other geometry-bearing entries (QuadPoints, InkList, etc.) are
    /// not updated in this build.</summary>
    public void ChangeAfterResize(Matrix transform)
    {
        if (transform is null || Rect is not { } r) return;
        transform.Transform(r.LLX, r.LLY, out var x0, out var y0);
        transform.Transform(r.URX, r.URY, out var x1, out var y1);
        Rect = new Rectangle(
            System.Math.Min(x0, x1),
            System.Math.Min(y0, y1),
            System.Math.Max(x0, x1),
            System.Math.Max(y0, y1));
    }

    /// <summary>Currently-active appearance state (/AS entry). Used when
    /// the annotation has multiple <see cref="States"/>.</summary>
    public string? ActiveState
    {
        get => _dict.GetName("AS");
        set
        {
            if (value is null) _dict.Remove("AS");
            else _dict.Set("AS", new PdfName(value));
            // Mark this annotation's object dirty so an in-place/incremental
            // Save() (which only re-writes changed objects) persists the
            // appearance-state change — e.g. selecting a checkbox widget kid
            // via field[i].ActiveState. Inline (non-indirect) annotations have
            // no object number and are written via their parent instead.
            var doc = _reader?.OwnerDocument;
            if (doc is not null)
            {
                var objNum = doc.FindObjectNumber(_dict);
                if (objNum >= 0) doc.MarkDirty(objNum, _dict);
            }
        }
    }

    /// <summary>Text alignment (for free-text / widget annotations carrying
    /// rich text). Stored on the annotation directly as /Alignment.</summary>
    public TextAlignment Alignment { get; set; } = TextAlignment.Left;

    /// <summary>Horizontal alignment (Aspose.PDF for .NET exposes this in addition
    /// to <see cref="Alignment"/>; the two are independently settable).</summary>
    public new Aspose.Pdf.HorizontalAlignment HorizontalAlignment { get; set; } = Aspose.Pdf.HorizontalAlignment.Left;

    /// <summary>Text-specific horizontal alignment override.</summary>
    public Aspose.Pdf.HorizontalAlignment TextHorizontalAlignment { get; set; } = Aspose.Pdf.HorizontalAlignment.Left;

    /// <summary>Appearance dictionary (/AP entry) — keyed by appearance
    /// stream name (N, D, R). Lazily populated from the annotation's /AP on
    /// first access; mutations are kept in memory until save.</summary>
    public AppearanceDictionary Appearance { get { EnsureAppearance(); return _appearance!; } }

    /// <summary>Appearance-state dictionary (the per-state sub-dict of
    /// /AP/N, e.g. checkbox /Yes /Off). Same shape as <see cref="Appearance"/>.</summary>
    public AppearanceDictionary States { get { EnsureAppearance(); return _states!; } }

    private AppearanceDictionary? _appearance;
    private AppearanceDictionary? _states;

    /// <summary>
    /// Build the <see cref="Appearance"/> and <see cref="States"/> dictionaries
    /// from the annotation's /AP entry on first access. Each /AP/N, /AP/D, /AP/R
    /// entry that is a stream maps directly to an <see cref="XForm"/> under its
    /// key ("N", "D", "R"). When the entry is a state-keyed sub-dictionary (e.g.
    /// a checkbox's /Yes and /Off forms, or a radio's numbered options), each
    /// state's form is exposed under the compound key "&lt;K&gt;.&lt;state&gt;"
    /// (e.g. "D.0", "N.Off") and also added to <see cref="States"/> by bare
    /// state name.
    /// </summary>
    private void EnsureAppearance()
    {
        if (_appearance is not null) return;
        _appearance = new AppearanceDictionary();
        _states = new AppearanceDictionary();

        var ap = _reader.ResolveDict(_dict.Get("AP"));
        if (ap is null) return;

        foreach (var key in ap.Keys)
        {
            var obj = _reader.Resolve(ap.Get(key));
            if (obj is Core.PdfStream stream)
            {
                _appearance[key] = new XForm(stream, _reader);
            }
            else if (obj is Core.PdfDictionary stateDict)
            {
                foreach (var stateKey in stateDict.Keys)
                {
                    if (_reader.Resolve(stateDict.Get(stateKey)) is Core.PdfStream stStream)
                    {
                        var form = new XForm(stStream, _reader);
                        _appearance[key + "." + stateKey] = form;
                        _states[stateKey] = form;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The annotation's normal (/AP /N) appearance as an <see cref="XForm"/>, or
    /// <c>null</c> when it has none. Walk its content-stream operators via
    /// <c>NormalAppearance.Contents</c>. For a state-keyed /N the on-state form is returned.
    /// </summary>
    public virtual XForm? NormalAppearance
    {
        get
        {
            var ap = _reader.ResolveDict(_dict.Get("AP"));
            if (ap is null) return null;
            var nObj = _reader.Resolve(ap.Get("N"));
            if (nObj is Core.PdfStream direct) return new XForm(direct, _reader);
            if (nObj is Core.PdfDictionary stateDict)
            {
                Core.PdfStream? firstAny = null;
                foreach (var key in stateDict.Keys)
                {
                    var resolved = _reader.ResolveStream(stateDict.Get(key));
                    if (resolved is null) continue;
                    firstAny ??= resolved;
                    if (key != "Off") return new XForm(resolved, _reader);
                }
                return firstAny is null ? null : new XForm(firstAny, _reader);
            }
            return null;
        }
    }

    /// <summary>Whether to regenerate the appearance stream when an annotation is saved
    /// into a converted document. A global flag (static), matching the Aspose.PDF for .NET API
    /// (set via <c>Annotation.UpdateAppearanceOnConvert</c> / <c>Field.UpdateAppearanceOnConvert</c>).
    /// Stored only.</summary>
    public static bool UpdateAppearanceOnConvert { get; set; } = true;

    /// <summary>Whether the embedded font (if any) should be subsetted.
    /// Static global toggle in the Aspose.PDF for .NET API; stored only — the FOSS
    /// appearance writer always embeds the full referenced glyph set.</summary>
    public static bool UseFontSubset { get; set; }

    /// <summary>The annotation contents (text).</summary>
    public string? Contents
    {
        get => GetString("Contents");
        set
        {
            if (value is null) _dict.Remove("Contents");
            else _dict.Set("Contents", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Annotation opacity (0.0 = transparent, 1.0 = opaque). Maps to /CA entry.</summary>
    public double Opacity
    {
        get
        {
            var ca = _dict.Get("CA");
            if (ca is PdfReal r) return r.Value;
            if (ca is PdfInteger i) return i.Value;
            return 1.0;
        }
        set => _dict.Set("CA", new PdfReal(value));
    }

    /// <summary>Annotation border. Maps to /Border entry.</summary>
    public Border? Border
    {
        get => new Border(this);
        set
        {
            if (value is null) { _dict.Remove("Border"); _dict.Remove("BS"); return; }
            var arr = new PdfArray();
            arr.Add(new PdfInteger(0));
            arr.Add(new PdfInteger(0));
            arr.Add(new PdfInteger((int)value.Width));
            _dict.Set("Border", arr);

            // The /Border array can't express the style or dash pattern; emit a
            // /BS border-style dictionary (PDF 32000 §12.5.4) that carries them.
            var bs = new PdfDictionary();
            bs.Set("W", new PdfInteger((int)value.Width));
            bs.Set("S", new PdfName(value.Style switch
            {
                Aspose.Pdf.Annotations.BorderStyle.Dashed => "D",
                Aspose.Pdf.Annotations.BorderStyle.Beveled => "B",
                Aspose.Pdf.Annotations.BorderStyle.Inset => "I",
                Aspose.Pdf.Annotations.BorderStyle.Underline => "U",
                _ => "S",
            }));
            if (value.Style == Aspose.Pdf.Annotations.BorderStyle.Dashed
                && value.Dash is { Pattern.Length: > 0 } dash)
            {
                var d = new PdfArray();
                foreach (var seg in dash.Pattern) d.Add(new PdfInteger(seg));
                bs.Set("D", d);
            }
            _dict.Set("BS", bs);
        }
    }

    /// <summary>The annotation border color (/C entry).</summary>
    public Color Color
    {
        get
        {
            var arr = _reader.Resolve(_dict.Get("C")) as PdfArray;
            if (arr is null || arr.Count < 3) return Color.Black;
            double r = PdfArrayHelper.GetDouble(arr, 0);
            double g = PdfArrayHelper.GetDouble(arr, 1);
            double b = PdfArrayHelper.GetDouble(arr, 2);
            return Color.FromRgb(r, g, b);
        }
        set
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(value.R / 255.0));
            arr.Add(new PdfReal(value.G / 255.0));
            arr.Add(new PdfReal(value.B / 255.0));
            _dict.Set("C", arr);
        }
    }

    /// <summary>The annotation interior color (/IC entry, for circle/square/line annotations).</summary>
    public Color? InteriorColor
    {
        get
        {
            var arr = _reader.Resolve(_dict.Get("IC")) as PdfArray;
            if (arr is null || arr.Count < 3) return null;
            double r = PdfArrayHelper.GetDouble(arr, 0);
            double g = PdfArrayHelper.GetDouble(arr, 1);
            double b = PdfArrayHelper.GetDouble(arr, 2);
            return Color.FromRgb(r, g, b);
        }
        set
        {
            if (value is null) { _dict.Remove("IC"); return; }
            var arr = new PdfArray();
            arr.Add(new PdfReal(value.R / 255.0));
            arr.Add(new PdfReal(value.G / 255.0));
            arr.Add(new PdfReal(value.B / 255.0));
            _dict.Set("IC", arr);
        }
    }

    /// <summary>Regenerate the normal appearance stream (/AP /N) from the
    /// annotation's current geometry and colours. The base implementation
    /// strokes the annotation's rectangle; shape annotations override this
    /// to draw their own geometry.</summary>
    public virtual void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) return;
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        b.Rectangle(r.LLX, r.LLY, r.URX - r.LLX, r.URY - r.LLY);
        b.Stroke();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }

    /// <summary>Store <paramref name="content"/> as this annotation's normal
    /// appearance (/AP /N), wrapped in a Form XObject with the given bounding box.</summary>
    private protected void SetNormalAppearance(byte[] content, Rectangle bbox)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(bbox.LLX)); bb.Add(new PdfReal(bbox.LLY));
        bb.Add(new PdfReal(bbox.URX)); bb.Add(new PdfReal(bbox.URY));
        form.Set("BBox", bb);
        form.Set("Resources", new PdfDictionary());
        form.Set("Length", new PdfInteger(content.Length));
        var stream = new PdfStream(form, content);
        var ap = _reader.ResolveDict(_dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", stream);
        _dict.Set("AP", ap);
    }

    /// <summary>Like <see cref="SetNormalAppearance"/> but adds a Helvetica
    /// (/Helv) font resource so the appearance content can show text.</summary>
    private protected void SetNormalAppearanceWithHelvetica(byte[] content, Rectangle bbox)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(bbox.LLX)); bb.Add(new PdfReal(bbox.LLY));
        bb.Add(new PdfReal(bbox.URX)); bb.Add(new PdfReal(bbox.URY));
        form.Set("BBox", bb);

        var helv = new PdfDictionary();
        helv.Set("Type", new PdfName("Font"));
        helv.Set("Subtype", new PdfName("Type1"));
        helv.Set("BaseFont", new PdfName("Helvetica"));
        var fonts = new PdfDictionary();
        fonts.Set("Helv", helv);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        form.Set("Resources", res);
        form.Set("Length", new PdfInteger(content.Length));

        var ap = _reader.ResolveDict(_dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", new PdfStream(form, content));
        _dict.Set("AP", ap);
    }

    /// <summary>Serialize this annotation as an XFDF element to
    /// <paramref name="writer"/> (PDF 32000 §12.7.8 / XFDF 3.0).</summary>
    public void WriteXfdf(System.Xml.XmlWriter writer)
    {
        if (writer is null) return;
        var idx = PageIndex;
        Aspose.Pdf.Facades.PdfAnnotationEditor.WriteXfdfAnnotation(
            writer, this, idx >= 1 ? idx - 1 : 0, _reader,
            writeContents: false, normalizeRichText: true);
    }

    /// <summary>The annotation title (author for markup annotations).</summary>
    public string? Title
    {
        get => GetString("T");
        set
        {
            if (value is null) _dict.Remove("T");
            else _dict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>The annotation name (unique identifier — the /NM entry).</summary>
    public string? Name
    {
        get => GetString("NM");
        set
        {
            if (value is null) _dict.Remove("NM");
            else _dict.Set("NM", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Fully-qualified annotation name. For most annotation
    /// types this is the unique /NM entry; for widget annotations (form
    /// fields) the field hierarchy is reflected via <see cref="Field.FullName"/>
    /// instead.</summary>
    public string? FullName => Name;

    /// <summary>The annotation subject (/Subj entry).</summary>
    public string? Subject
    {
        get => GetString("Subj");
        set
        {
            if (value is null) _dict.Remove("Subj");
            else _dict.Set("Subj", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>The modification date.</summary>
    public string? ModifiedDate => GetString("M");

    /// <summary>The /M (modified) date as a typed DateTime. Settable.</summary>
    public DateTime Modified
    {
        // Resolve through GetString so an indirect /M reference is followed
        // (a direct "_dict.Get as PdfString" would miss it and return default).
        get => ParsePdfDate(GetString("M"));
        set => _dict.Set("M", new PdfString(System.Text.Encoding.Latin1.GetBytes(FormatPdfDate(value))));
    }

    /// <summary>Parse a PDF date string (D:YYYYMMDDHHmmSS) into DateTime.</summary>
    internal static DateTime ParsePdfDate(string? s)
    {
        if (string.IsNullOrEmpty(s)) return default;
        if (s.StartsWith("D:")) s = s.Substring(2);
        if (s.Length < 4) return default;
        try
        {
            // Every field after the year is optional, and a timezone marker
            // (Z / + / -) may follow any of them — e.g. "201605250156Z00'00'"
            // stops at minutes. Read each 2-digit field only when both chars are
            // digits, so a timezone marker isn't mis-parsed as the next number.
            int Field(int start, int def) =>
                s.Length >= start + 2 && char.IsDigit(s[start]) && char.IsDigit(s[start + 1])
                    ? int.Parse(s.Substring(start, 2)) : def;
            int year = int.Parse(s.Substring(0, 4));
            int mon = Field(4, 1);
            int day = Field(6, 1);
            int hour = Field(8, 0);
            int min = Field(10, 0);
            int sec = Field(12, 0);
            return new DateTime(year, mon, day, hour, min, sec);
        }
        catch { return default; }
    }

    /// <summary>Format a DateTime as a PDF date string.</summary>
    internal static string FormatPdfDate(DateTime dt) => "D:" + dt.ToString("yyyyMMddHHmmss");

    /// <summary>Annotation flags (/F entry) — used as the storage backing
    /// for both the int and typed forms.</summary>
    public AnnotationFlags Flags
    {
        get => (AnnotationFlags)(int)_dict.GetInt("F");
        set => _dict.Set("F", new Aspose.Pdf.Core.PdfInteger((int)value));
    }

    /// <summary>Typed alias of <see cref="Flags"/> matching the Aspose.PDF for .NET
    /// public surface that exposes <c>AnnotationFlags</c> as a property.</summary>
    public AnnotationFlags AnnotationFlags
    {
        get => Flags;
        set => Flags = value;
    }

    /// <summary>The action associated with this annotation (/A entry).</summary>
    public PdfAction? Action
    {
        get
        {
            var actionDict = _reader.ResolveDict(_dict.Get("A"));
            return actionDict is not null ? PdfAction.Create(actionDict, _reader) : null;
        }
        set
        {
            if (value is null) _dict.Remove("A");
            else _dict.Set("A", value.Dict);
        }
    }

    /// <summary>
    /// Collection of actions associated with this annotation.
    /// In PDF, multiple actions are represented via the /Next chain or as an array on the /A entry.
    /// </summary>
    public PdfActionCollection Actions
    {
        get
        {
            var coll = new PdfActionCollection();
            foreach (var action in new ActionCollection(_dict, _reader))
                coll.Add(action);
            return coll;
        }
    }

    /// <summary>The annotation author (/T entry).</summary>
    public string? Author => GetString("T");

    /// <summary>The annotation state (/State entry), e.g. "Accepted", "Rejected", "Marked".</summary>
    public string? AnnotationState => GetString("State");

    /// <summary>The annotation state model (/StateModel entry), e.g. "Review", "Marked".</summary>
    public string? AnnotationStateModel => GetString("StateModel");

    /// <summary>
    /// The reply type (/RT entry), e.g. "State" for state replies. Null if absent.
    /// </summary>
    public string? ReplyType
    {
        get
        {
            var obj = _reader.Resolve(_dict.Get("RT"));
            return obj is PdfName n ? n.Value : null;
        }
    }

    /// <summary>
    /// The annotation this one is replying to (/IRT entry).
    /// </summary>
    public Annotation? InReplyTo
    {
        get
        {
            var irtObj = _reader.ResolveDict(_dict.Get("IRT"));
            return irtObj is not null ? Create(irtObj, _reader) : null;
        }
        set
        {
            if (value is null) _dict.Remove("IRT");
            else _dict.Set("IRT", value._dict);
        }
    }

    /// <summary>
    /// QuadPoints as an array of Point values. Used by Highlight, Underline, StrikeOut,
    /// Squiggly, and Redact annotations to specify the quadrilateral(s) covered.
    /// Returns an empty array if the entry is absent.
    /// </summary>
    public Point[] QuadPoints
    {
        get
        {
            var arr = _reader.Resolve(_dict.Get("QuadPoints")) as PdfArray;
            if (arr is null || arr.Count < 2) return [];
            var points = new Point[arr.Count / 2];
            for (int i = 0; i < points.Length; i++)
            {
                double x = arr[i * 2] switch { PdfInteger pi => pi.Value, PdfReal pr => pr.Value, _ => 0 };
                double y = arr[i * 2 + 1] switch { PdfInteger pi => pi.Value, PdfReal pr => pr.Value, _ => 0 };
                points[i] = new Point(x, y);
            }
            return points;
        }
        set
        {
            if (value is null || value.Length == 0)
            {
                _dict.Remove("QuadPoints");
                return;
            }
            var arr = new PdfArray();
            foreach (var p in value)
            {
                arr.Add(new PdfReal(p.X));
                arr.Add(new PdfReal(p.Y));
            }
            _dict.Set("QuadPoints", arr);
        }
    }

    /// <summary>
    /// Flatten this annotation — render its visual appearance into the page content
    /// and remove it from the page's annotations array.
    /// Requires the annotation's /P (page) entry to be set, which is standard for most PDFs.
    /// </summary>
    public void Flatten()
    {
        // Get the page this annotation belongs to
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (pageDict is null) return;

        var subtype = _dict.GetName("Subtype");

        // Stamp the annotation appearance onto the page content (if it has one).
        // Shape/markup annotations are often stored without an /AP; synthesise one
        // from the geometry so the figure is baked in instead of vanishing.
        if (ResolveAppearanceStream() is null && this is SquareAnnotation or CircleAnnotation or TextAnnotation)
            UpdateAppearances();
        var appearanceStream = ResolveAppearanceStream();
        if (appearanceStream is not null)
        {
            var rectArr = _reader.Resolve(_dict.Get("Rect")) as PdfArray;
            if (rectArr is not null && rectArr.Count >= 4)
            {
                var rect = Rectangle.FromPdfArray(rectArr);

                var bboxArr = _reader.Resolve(appearanceStream.Dict.Get("BBox")) as PdfArray;
                double bboxW = rect.Width, bboxH = rect.Height;
                double bboxX = 0, bboxY = 0;
                if (bboxArr is { Count: >= 4 })
                {
                    var bbox = Rectangle.FromPdfArray(bboxArr);
                    bboxW = bbox.Width;
                    bboxH = bbox.Height;
                    bboxX = bbox.LLX;
                    bboxY = bbox.LLY;
                }

                var sx = bboxW > 0 ? rect.Width / bboxW : 1.0;
                var sy = bboxH > 0 ? rect.Height / bboxH : 1.0;
                var tx = rect.LLX - bboxX * sx;
                var ty = rect.LLY - bboxY * sy;

                // When the appearance is itself a Form XObject (the typical case for
                // Ink, Stamp, FreeText, ...), preserve it as a named XForm in the
                // page's /Resources/XObject and reference it via /FRMn Do. This
                // matches the widget-flatten path in Forms.Form and keeps the form's
                // own BBox/Matrix/Resources contracts intact — visual output is
                // unchanged but tests can still inspect the appearance stream's
                // operators via Resources.Forms[name].
                var isFormXobject = appearanceStream.Dict.GetName("Subtype") == "Form";
                using var appendContent = new MemoryStream();
                var writer = new StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
                if (isFormXobject)
                {
                    var xformName = Forms.Form.RegisterAppearanceAsXForm(pageDict, appearanceStream, _reader);
                    writer.Write($"q {Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm /{xformName} Do Q\n");
                    writer.Flush();
                }
                else
                {
                    // Fallback for non-Form appearance streams — inline the bytes and
                    // merge the annotation's /Resources into the page's /Resources so
                    // the operators have access to their fonts/xobjects.
                    var streamData = _reader.DecodeStream(appearanceStream);
                    writer.Write($"q {Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm\n");
                    writer.Flush();
                    appendContent.Write(streamData);
                    writer.Write("\nQ\n");
                    writer.Flush();
                    Forms.Form.MergeAnnotResources(pageDict, appearanceStream.Dict, _reader);
                }

                var existing = _reader.Resolve(pageDict.Get("Contents"));
                byte[] existingData = existing is PdfStream es ? _reader.DecodeStream(es) : [];

                var contentArr = appendContent.ToArray();
                var combined = new byte[existingData.Length + 1 + contentArr.Length];
                existingData.CopyTo(combined, 0);
                if (existingData.Length > 0)
                    combined[existingData.Length] = (byte)'\n';
                contentArr.CopyTo(combined, existingData.Length + (existingData.Length > 0 ? 1 : 0));

                pageDict.Set("Contents", new PdfStream(new PdfDictionary(), combined));
            }
        }

        // Always remove the annotation from the page's /Annots array
        RemoveFromAnnotsArray(pageDict);
    }

    private PdfStream? ResolveAppearanceStream()
    {
        var apDict = _reader.ResolveDict(_dict.Get("AP"));
        if (apDict is null) return null;

        var nResolved = _reader.Resolve(apDict.Get("N"));
        if (nResolved is PdfStream ns) return ns;

        if (nResolved is PdfDictionary stateDict)
        {
            var asName = _dict.GetName("AS");
            if (asName is not null)
            {
                var s = _reader.ResolveStream(stateDict.Get(asName));
                if (s is not null) return s;
            }
            foreach (var key in stateDict.Keys)
            {
                if (key == "Off") continue;
                var s = _reader.ResolveStream(stateDict.Get(key));
                if (s is not null) return s;
            }
        }
        return null;
    }

    private void RemoveFromAnnotsArray(PdfDictionary pageDict)
    {
        var annotsObj = _reader.Resolve(pageDict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        var remaining = new PdfArray();
        foreach (var annotRef in annotsObj)
        {
            bool isThis = false;
            if (annotRef is PdfIndirectRef iref && _dictObjNum >= 0)
                isThis = iref.ObjectNumber == _dictObjNum;
            else
            {
                var annotDict = _reader.ResolveDict(annotRef);
                isThis = annotDict is not null && ReferenceEquals(annotDict, _dict);
            }
            if (isThis) continue;
            remaining.Add(annotRef);
        }
        if (remaining.Count > 0)
            pageDict.Set("Annots", remaining);
        else
            pageDict.Remove("Annots");
    }

    private static string Fmt(double v) =>
        v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The raw annotation dictionary.</summary>
    internal PdfDictionary Dict => _dict;
    internal PdfReader InternalReader => _reader;

    private Characteristics? _characteristics;
    /// <summary>Appearance characteristics (border color, background color, rotation).</summary>
    public Characteristics Characteristics => _characteristics ??= new Characteristics();

    protected string? GetString(string key)
    {
        var obj = _reader.Resolve(_dict.Get(key));
        return obj switch
        {
            PdfString s => s.ToText(),
            PdfName n => n.Value,
            _ => null,
        };
    }

    internal static Annotation Create(PdfDictionary dict, PdfReader reader, int objectNumber = -1)
    {
        var subtype = dict.GetName("Subtype");
        Annotation annot = subtype switch
        {
            "Link" => new LinkAnnotation(dict, reader),
            "Text" => new TextAnnotation(dict, reader),
            "FreeText" => new FreeTextAnnotation(dict, reader),
            "Highlight" => new HighlightAnnotation(dict, reader),
            "Underline" => new UnderlineAnnotation(dict, reader),
            "StrikeOut" => new StrikeOutAnnotation(dict, reader),
            "Squiggly" => new SquigglyAnnotation(dict, reader),
            "Widget" => new WidgetAnnotation(dict, reader),
            "Redact" => new RedactionAnnotation(dict, reader),
            "FileAttachment" => new FileAttachmentAnnotation(dict, reader),
            "Popup" => new PopupAnnotation(dict, reader),
            "Stamp" => new StampAnnotation(dict, reader),
            "Ink" => new InkAnnotation(dict, reader),
            "Line" => new LineAnnotation(dict, reader),
            "Square" => new SquareAnnotation(dict, reader),
            "Circle" => new CircleAnnotation(dict, reader),
            "Polygon" => new PolygonAnnotation(dict, reader),
            "PolyLine" => new PolylineAnnotation(dict, reader),
            "Caret" => new CaretAnnotation(dict, reader),
            "Sound" => new SoundAnnotation(dict, reader),
            "Movie" => new MovieAnnotation(dict, reader),
            "Screen" => new ScreenAnnotation(dict, reader),
            "RichMedia" => new RichMediaAnnotation(dict, reader),
            // "Watermark" has a separate non-Annotation class — keep as generic
            _ => new Annotation(dict, reader),
        };
        annot._dictObjNum = objectNumber;
        return annot;
    }

    internal void SetPageDict(PdfDictionary pageDict) => _pageDict = pageDict;
    internal void SetOwnerPage(Page page) => _ownerPage = page;

    /// <summary>Read a review-state text-string entry (/State or /StateModel)
    /// from this annotation, falling back to a reply annotation (/IRT → this)
    /// that carries one. PDF §12.5.6.4 stores review states on a separate reply
    /// annotation, not on the annotation being reviewed; the last reply wins.</summary>
    internal string? ResolveReviewStateValue(string key)
    {
        if (_dict.Get(key) is PdfString own && own.ToText() is { Length: > 0 } ownText)
            return ownText;

        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (pageDict is null || _reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots)
            return null;

        string? latest = null;
        foreach (var item in annots)
        {
            var reply = _reader.ResolveDict(item);
            if (reply is null || ReferenceEquals(reply, _dict)) continue;
            if (!IsReplyToThis(reply)) continue;
            if (reply.Get(key) is PdfString s && s.ToText() is { Length: > 0 } t)
                latest = t;
        }
        return latest;
    }

    /// <summary>True when <paramref name="reply"/> is a reply (/IRT) to this
    /// annotation. Matches by object identity, falling back to the annotation
    /// name (/NM) — FOSS may serialize /IRT as an inline copy of the target
    /// rather than an indirect reference, which breaks identity on reload.</summary>
    private bool IsReplyToThis(PdfDictionary reply)
    {
        var irt = _reader.ResolveDict(reply.Get("IRT"));
        if (irt is null) return false;
        if (ReferenceEquals(irt, _dict)) return true;
        var myName = (_dict.Get("NM") as PdfString)?.ToText();
        var irtName = (irt.Get("NM") as PdfString)?.ToText();
        return !string.IsNullOrEmpty(myName) && myName == irtName;
    }

    /// <summary>Locate the most-recent reply annotation (/IRT → this) that
    /// carries a /State entry, wrapping it as a <see cref="TextAnnotation"/>.
    /// Resolves the owning page from /P, the cached page dict, or the page the
    /// annotation was created/attached to (so it works in-memory and after a
    /// save/reload). Returns null when none exists.</summary>
    internal TextAnnotation? FindStateReply()
    {
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict
            ?? (_ownerPage ?? _creationPage)?.Dict;
        if (pageDict is null || _reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots)
            return null;

        TextAnnotation? latest = null;
        foreach (var item in annots)
        {
            var reply = _reader.ResolveDict(item);
            if (reply is null || ReferenceEquals(reply, _dict)) continue;
            if (!IsReplyToThis(reply)) continue;
            if (reply.Get("State") is PdfString)
                latest = new TextAnnotation(reply, _reader);
        }
        return latest;
    }

    /// <summary>Create and attach the reply annotation (/IRT → this) that
    /// records a review state, mirroring how viewers store review states on a
    /// separate annotation. No-op when this annotation isn't attached to a
    /// page (the /State written on the annotation itself still resolves).</summary>
    internal void AttachStateReply(string state, string model, string author)
    {
        var page = _ownerPage ?? _creationPage;
        if (page is null) return;

        var reply = new TextAnnotation(page, Rect ?? new Rectangle(0, 0, 0, 0))
        {
            Contents = state + " set by " + author,
        };
        if (!string.IsNullOrEmpty(author)) reply.Title = author;
        reply.Dict.Set("State", new PdfString(System.Text.Encoding.Latin1.GetBytes(state)));
        reply.Dict.Set("StateModel", new PdfString(System.Text.Encoding.Latin1.GetBytes(model)));
        reply.Dict.Set("IRT", _dict);
        page.Annotations.Add(reply);
    }

    /// <summary>Remove /State and /StateModel from this annotation and from any
    /// reply annotations (/IRT → this) that carry them — used by ClearState so
    /// the cleared state survives save/reload.</summary>
    internal void ClearReviewStateOnReplies()
    {
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (pageDict is null || _reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots)
            return;
        foreach (var item in annots)
        {
            var reply = _reader.ResolveDict(item);
            if (reply is null || ReferenceEquals(reply, _dict)) continue;
            if (!IsReplyToThis(reply)) continue;
            reply.Remove("State");
            reply.Remove("StateModel");
        }
    }

    /// <summary>
    /// 1-based index of the page that owns this annotation. Resolved from the
    /// owning Page reference when known, otherwise by walking the document's
    /// page tree and matching the dict referenced by the annotation's /P
    /// entry, or by scanning each page's /Annots. Returns -1 when no owning
    /// page can be located.
    /// </summary>
    public int PageIndex
    {
        get
        {
            if (_ownerPage is { } p) return p.Index + 1;
            var pages = new PageCollection(_reader);
            var idx = FindPageIndexOf(_dict, _pageDict, pages);
            if (idx >= 1) return idx;
            // Fallback for popups (and other child annotations): the page is the
            // parent markup annotation's page. Used when the annotation isn't in
            // any page's /Annots itself (e.g. referenced only via /Popup).
            var parentDict = _reader.ResolveDict(_dict.Get("Parent"));
            if (parentDict is not null && !ReferenceEquals(parentDict, _dict))
            {
                idx = FindPageIndexOf(parentDict, null, pages);
                if (idx >= 1) return idx;
            }
            return -1;
        }
    }

    /// <summary>Locate the 1-based page index that owns <paramref name="dict"/>,
    /// via its /P entry or by scanning each page's /Annots. Returns -1 if not found.</summary>
    private int FindPageIndexOf(PdfDictionary dict, PdfDictionary? pageHint, PageCollection pages)
    {
        var pageDict = _reader.ResolveDict(dict.Get("P")) ?? pageHint;
        if (pageDict is not null)
        {
            for (var i = 1; i <= pages.Count; i++)
                if (ReferenceEquals(pages[i].Dict, pageDict)) return i;
        }
        for (var i = 1; i <= pages.Count; i++)
        {
            var annots = _reader.Resolve(pages[i].Dict.Get("Annots")) as Core.PdfArray;
            if (annots is null) continue;
            foreach (var item in annots)
            {
                if (ReferenceEquals(_reader.ResolveDict(item), dict))
                    return i;
            }
        }
        return -1;
    }

    /// <summary>The page that owns this annotation, or null when it can't be
    /// resolved (e.g. an annotation not yet attached to any page).</summary>
    public Page? Page
    {
        get
        {
            if (_ownerPage is { } op) return op;
            if (_creationPage is { } cp) return cp;
            var idx = PageIndex;
            return idx >= 1 ? new PageCollection(_reader)[idx] : null;
        }
    }

    private static AnnotationType ParseSubtype(string? subtype) => subtype switch
    {
        "Text" => AnnotationType.Text,
        "Link" => AnnotationType.Link,
        "FreeText" => AnnotationType.FreeText,
        "Line" => AnnotationType.Line,
        "Square" => AnnotationType.Square,
        "Circle" => AnnotationType.Circle,
        "Polygon" => AnnotationType.Polygon,
        "PolyLine" => AnnotationType.PolyLine,
        "Highlight" => AnnotationType.Highlight,
        "Underline" => AnnotationType.Underline,
        "Squiggly" => AnnotationType.Squiggly,
        "StrikeOut" => AnnotationType.StrikeOut,
        "Stamp" => AnnotationType.Stamp,
        "Caret" => AnnotationType.Caret,
        "Ink" => AnnotationType.Ink,
        "Popup" => AnnotationType.Popup,
        "FileAttachment" => AnnotationType.FileAttachment,
        "Sound" => AnnotationType.Sound,
        "Movie" => AnnotationType.Movie,
        "Widget" => AnnotationType.Widget,
        "Screen" => AnnotationType.Screen,
        "PrinterMark" => AnnotationType.PrinterMark,
        "TrapNet" => AnnotationType.TrapNet,
        "Watermark" => AnnotationType.Watermark,
        "3D" => AnnotationType.ThreeD,
        "Redact" => AnnotationType.Redact,
        "RichMedia" => AnnotationType.RichMedia,
        _ => AnnotationType.Unknown,
    };
}

/// <summary>Highlighting mode for link annotations.</summary>
public enum HighlightingMode
{
    /// <summary>No highlighting.</summary>
    None,
    /// <summary>Invert the contents of the annotation rectangle.</summary>
    Invert,
    /// <summary>Invert the annotation border.</summary>
    Outline,
    /// <summary>Push the annotation appearance.</summary>
    Push,
    /// <summary>Toggle the annotation's appearance on/off.</summary>
    Toggle,
}

public partial class LinkAnnotation : Annotation
{
    internal LinkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a new link annotation for the given page and rectangle.</summary>
    public LinkAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Link"));
    }

    /// <summary>Highlighting mode when the link is activated.</summary>
    public HighlightingMode Highlighting { get; set; } = HighlightingMode.Invert;

    /// <summary>The destination for this link annotation (ExplicitDestination, NamedDestination, or null).</summary>
    public IAppointment? Destination
    {
        get
        {
            var destObj = InternalReader.Resolve(Dict.Get("Dest"));
            if (destObj is PdfArray arr)
                return ExplicitDestination.FromArray(arr, InternalReader);
            if (destObj is PdfString s)
                return new NamedDestination(s.ToText());
            if (destObj is PdfName n)
                return new NamedDestination(n.Value);
            // Check action for GoTo
            var action = InternalReader.ResolveDict(Dict.Get("A"));
            if (action is not null && action.GetName("S") == "GoTo")
            {
                var d = InternalReader.Resolve(action.Get("D"));
                if (d is PdfArray destArr)
                    return ExplicitDestination.FromArray(destArr, InternalReader);
                if (d is PdfString ds)
                    return new NamedDestination(ds.ToText());
            }
            return null;
        }
        set
        {
            if (value is ExplicitDestination ed)
            {
                Dict.Set("Dest", ed.ToPdfArray());
                Dict.Remove("A"); // Remove action when setting explicit destination
            }
            else if (value is NamedDestination nd)
            {
                Dict.Set("Dest", new PdfString(System.Text.Encoding.Latin1.GetBytes(nd.Name)));
                Dict.Remove("A");
            }
            else if (value is null)
            {
                Dict.Remove("Dest");
            }
        }
    }

    public string? Uri
    {
        get
        {
            var action = InternalReader.ResolveDict(Dict.Get("A"));
            if (action is null) return null;
            if (action.GetName("S") != "URI") return null;
            var uri = InternalReader.Resolve(action.Get("URI"));
            return uri is PdfString s ? s.ToText() : null;
        }
    }

    /// <summary>
    /// Target page number (1-based) for GoTo/GoToR link actions, or null if the action
    /// is not a page link (e.g. URI, JavaScript).
    /// </summary>
    public int? TargetPageNumber
    {
        get
        {
            var action = InternalReader.ResolveDict(Dict.Get("A"));
            if (action is not null)
            {
                var subtype = action.GetName("S");
                if (subtype != "GoTo" && subtype != "GoToR")
                    return null; // URI, JavaScript, etc.
                var dest = InternalReader.Resolve(action.Get("D"));
                return ResolveDestPageNumber(dest);
            }
            // Direct /Dest on annotation
            return ResolveDestPageNumber(InternalReader.Resolve(Dict.Get("Dest")));
        }
    }

    private int? ResolveDestPageNumber(PdfObject? dest)
    {
        if (dest is null) return null;
        if (dest is PdfArray arr && arr.Count > 0)
        {
            var pageRef = InternalReader.Resolve(arr[0]);
            if (pageRef is PdfInteger idx)
                return (int)(idx.Value + 1); // 0-based to 1-based for GoToR remote
        }
        return null;
    }

    /// <summary>Always <see cref="AnnotationType.Link"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Link;

    /// <summary>The /A entry parsed as a <see cref="PdfAction"/>. Setting writes the action dictionary.</summary>
    public new PdfAction? Action
    {
        get
        {
            var aDict = InternalReader.ResolveDict(Dict.Get("A"));
            return aDict is null ? null : PdfAction.Create(aDict, InternalReader);
        }
        set
        {
            if (value is null) Dict.Remove("A");
            else Dict.Set("A", value.Dict);
        }
    }
}

public partial class TextAnnotation : MarkupAnnotation
{
    internal TextAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Programmatic ctor — creates a /Text (sticky note) annotation
    /// at <paramref name="rect"/> on <paramref name="page"/>.</summary>
    public TextAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Text"));
    }

    public bool Open
    {
        get => Dict.Get("Open") is PdfBoolean b ? b.Value : Dict.GetInt("Open") != 0;
        set => Dict.Set("Open", (value ? PdfBoolean.True : PdfBoolean.False));
    }

    /// <summary>Review / marked state of this text annotation
    /// (/State entry per PDF 32000 §12.5.6.3). The base annotation
    /// exposes /State as a string via <see cref="AnnotationState"/>;
    /// this typed enum surface complements it.</summary>
    public AnnotationState State
    {
        get
        {
            return AnnotationState switch
            {
                "Marked" => Aspose.Pdf.Annotations.AnnotationState.Marked,
                "Unmarked" => Aspose.Pdf.Annotations.AnnotationState.Unmarked,
                "Accepted" => Aspose.Pdf.Annotations.AnnotationState.Accepted,
                "Rejected" => Aspose.Pdf.Annotations.AnnotationState.Rejected,
                "Cancelled" => Aspose.Pdf.Annotations.AnnotationState.Cancelled,
                "Completed" => Aspose.Pdf.Annotations.AnnotationState.Completed,
                "None" => Aspose.Pdf.Annotations.AnnotationState.None,
                _ => Aspose.Pdf.Annotations.AnnotationState.None,
            };
        }
        set
        {
            if (value == Aspose.Pdf.Annotations.AnnotationState.None) Dict.Remove("State");
            else Dict.Set("State", new PdfName(value.ToString()));
        }
    }

    /// <summary>Review-state model of this text annotation
    /// (/StateModel entry per PDF 32000 §12.5.6.3).</summary>
    public AnnotationStateModel StateModel
    {
        get
        {
            return AnnotationStateModel switch
            {
                "Marked" => Aspose.Pdf.Annotations.AnnotationStateModel.Marked,
                "Review" => Aspose.Pdf.Annotations.AnnotationStateModel.Review,
                _ => Aspose.Pdf.Annotations.AnnotationStateModel.Undefined,
            };
        }
        set
        {
            if (value == Aspose.Pdf.Annotations.AnnotationStateModel.Undefined) Dict.Remove("StateModel");
            else Dict.Set("StateModel", new PdfName(value.ToString()));
        }
    }

    /// <summary>The icon shown for the closed sticky note (/Name entry).</summary>
    public TextIcon Icon
    {
        get
        {
            var n = Dict.GetName("Name");
            return n switch
            {
                "Comment" => TextIcon.Comment,
                "Key" => TextIcon.Key,
                "Note" => TextIcon.Note,
                "Help" => TextIcon.Help,
                "NewParagraph" => TextIcon.NewParagraph,
                "Paragraph" => TextIcon.Paragraph,
                "Insert" => TextIcon.Insert,
                "Check" => TextIcon.Check,
                "Circle" => TextIcon.Circle,
                "Cross" => TextIcon.Cross,
                "Star" => TextIcon.Star,
                _ => TextIcon.Note,
            };
        }
        set => Dict.Set("Name", new PdfName(value.ToString()));
    }

    /// <summary>Generate the normal appearance (/AP /N) for the note icon
    /// (PDF 32000 §12.5.6.4) so the annotation renders and flattens. Uses the
    /// standard 35x35 icon path for the current /Name, recoloured to /C.</summary>
    public override void UpdateAppearances()
    {
        var rect = Rect;
        if (rect is null) return;
        var name = Dict.GetName("Name") ?? "Note";
        if (!TextAnnotationIcons.Streams.TryGetValue(name, out var content))
            content = TextAnnotationIcons.Streams["Note"];

        // The icon strokes/fills black by default; recolour to the annotation's /C.
        var c = Color;
        if (c is not null)
        {
            string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            var col = $"{F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)}";
            content = content.Replace("0 0 0 RG", col + " RG").Replace("0 0 0 rg", col + " rg");
        }

        var data = System.Text.Encoding.ASCII.GetBytes(content);
        SetNormalAppearance(data, new Rectangle(0, 0, 35, 35));
    }

    /// <summary>Document-bound ctor; rectangle defaults to empty.</summary>
    public TextAnnotation(Document document) : base(document)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Text"));
    }

    /// <summary>Always <see cref="AnnotationType.Text"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Text;

    /// <summary>Apply <paramref name="transform"/> to the annotation's rect.</summary>
    public new void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        var r = Rect;
        if (r is null) return;
        transform.Transform(r.LLX, r.LLY, out var x1, out var y1);
        transform.Transform(r.URX, r.URY, out var x2, out var y2);
        Rect = new Rectangle(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
    }

    /// <summary>The popup annotation associated with this sticky note (/Popup entry).</summary>
    public new PopupAnnotation? Popup
    {
        get
        {
            var p = InternalReader.ResolveDict(Dict.Get("Popup"));
            return p is null ? null : new PopupAnnotation(p, InternalReader);
        }
        set
        {
            if (value is null) Dict.Remove("Popup");
            else Dict.Set("Popup", value.Dict);
        }
    }
}


public partial class FreeTextAnnotation : MarkupAnnotation
{
    internal FreeTextAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a new FreeTextAnnotation with a default appearance.</summary>
    public FreeTextAnnotation(Page page, Rectangle rect, DefaultAppearance appearance)
        : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("FreeText"));
        _defaultAppearance = appearance ?? new DefaultAppearance();
        if (appearance is not null)
            Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(appearance.ToAppearanceString())));
    }

    /// <summary>Document-bound ctor for creating a FreeTextAnnotation that
    /// isn't yet attached to a specific page; the caller adds it via
    /// <c>page.Annotations.Add(annot)</c>.</summary>
    public FreeTextAnnotation(Document document, DefaultAppearance appearance)
        : base(document, rect: null!)
    {
        Dict.Set("Subtype", new PdfName("FreeText"));
        _defaultAppearance = appearance ?? new DefaultAppearance();
        if (appearance is not null)
            Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(appearance.ToAppearanceString())));
    }

    private DefaultAppearance? _defaultAppearance;

    /// <summary>The /DA default-appearance string.</summary>
    public string? DefaultAppearance
    {
        get => (Dict.Get("DA") as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("DA");
            else Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(value)));
        }
    }

    /// <summary>The strongly-typed default-appearance object backing
    /// <see cref="DefaultAppearance"/>. Construction-time appearance is
    /// stored; reading after a string-set returns the last-constructed
    /// object (the string→object round-trip is not parsed back).</summary>
    public DefaultAppearance DefaultAppearanceObject => _defaultAppearance ??= new DefaultAppearance();

    /// <summary>Inline default rich-text style carried in /DS.</summary>
    public string? DefaultStyle
    {
        get => (Dict.Get("DS") as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("DS");
            else Dict.Set("DS", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Rich text string (XHTML) for the annotation (/RC entry).</summary>
    public new string? RichText
    {
        get => GetString("RC");
        set
        {
            if (value is null) Dict.Remove("RC");
            else Dict.Set("RC", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Always <see cref="AnnotationType.FreeText"/>. Redeclared on
    /// the derived class to match Aspose.PDF for .NET reflection (DeclaredOnly).</summary>
    public new AnnotationType AnnotationType => AnnotationType.FreeText;

    /// <summary>
    /// Build the /AP /N appearance stream from <see cref="Annotation.Contents"/>,
    /// laying the text out as word-wrapped lines inside the annotation rectangle using
    /// the /DA font, size and colour. No-op when an appearance already exists or there
    /// is no text. Invoked by the save pipeline so a freshly-created FreeText annotation
    /// renders (and exposes <see cref="Annotation.NormalAppearance"/>).
    /// </summary>
    internal void GenerateAppearance()
    {
        if (InternalReader.ResolveDict(Dict.Get("AP")) is not null) return;
        // Text is taken from /Contents, falling back to the plain text of the
        // /RC rich-text packet when /Contents is empty (a FreeText can carry its
        // text only as rich text).
        var text = Contents;
        if (string.IsNullOrEmpty(text)) text = PlainTextFromRichText(RichText);
        if (string.IsNullOrEmpty(text)) return;
        var rect = Rect;
        if (rect is null || rect.Width <= 0 || rect.Height <= 0) return;

        var da = DefaultAppearanceObject;
        var fontName = string.IsNullOrWhiteSpace(da.FontName) ? "Helvetica" : da.FontName!;
        var fontSize = da.FontSize > 0 ? da.FontSize : 12.0;
        var color = da.TextColor;

        // Explicit TextStyle values (those differing from its defaults) take precedence
        // over /DA, so formatting set via TextStyle after creation is honoured.
        var ts = TextStyle;
        if (ts is not null)
        {
            if (ts.FontSize > 0 && System.Math.Abs(ts.FontSize - 12.0) > 1e-6) fontSize = ts.FontSize;
            if (ts.Color.ToArgb() != System.Drawing.Color.Black.ToArgb()) color = ts.Color;
            if (!string.IsNullOrWhiteSpace(ts.FontName) && ts.FontName != "Helvetica") fontName = ts.FontName;
        }

        double w = rect.Width, h = rect.Height;
        var borderWidth = ReadBorderWidth();
        // Text is inset from the rectangle by the border plus a 2pt readability margin.
        var inset = borderWidth + 2.0;
        var avail = System.Math.Max(1.0, w - 2 * inset);

        // Arbitrary rotation (Adobe XFDF /Rotate, in degrees). When set, the text is
        // rotated about the rectangle centre and /Rect is expanded to the rotated
        // bounding box so the rotated text isn't clipped by the appearance /BBox.
        double rotateDeg = InternalReader.Resolve(Dict.Get("Rotate")) switch
        {
            PdfReal rrv => rrv.Value,
            PdfInteger riv => riv.Value,
            _ => 0,
        };
        bool rotated = System.Math.Abs(rotateDeg % 360.0) > 1e-6;
        double bboxW = w, bboxH = h, rcos = 1, rsin = 0, ehw = w / 2, ehh = h / 2;
        if (rotated)
        {
            double th = rotateDeg * System.Math.PI / 180.0;
            rcos = System.Math.Cos(th); rsin = System.Math.Sin(th);
            ehw = System.Math.Abs(w / 2 * rcos) + System.Math.Abs(h / 2 * rsin);
            ehh = System.Math.Abs(w / 2 * rsin) + System.Math.Abs(h / 2 * rcos);
            bboxW = 2 * ehw; bboxH = 2 * ehh;
            double cx = (rect.LLX + rect.URX) / 2, cy = (rect.LLY + rect.URY) / 2;
            var exp = new PdfArray();
            exp.Add(new PdfReal(cx - ehw)); exp.Add(new PdfReal(cy - ehh));
            exp.Add(new PdfReal(cx + ehw)); exp.Add(new PdfReal(cy + ehh));
            Dict.Set("Rect", exp);
        }

        var fontDict = MakeFreeTextFontDict(fontName);
        var metrics = Aspose.Pdf.Text.FontMetrics.FromFontDict(fontDict, InternalReader);
        var lines = WrapText(text, metrics, fontSize, avail);

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string Fmt(double v) => v.ToString("0.###", ci);

        var leading = fontSize * 1.2;
        var sb = new System.Text.StringBuilder();

        // A FreeText annotation's /C entry is its background colour: fill the
        // rectangle with it (behind border and text) when present, matching the
        // Aspose.PDF for .NET appearance. Only the unrotated rect is filled —
        // BBox == rect there; rotated FreeText backgrounds are rare and skipped.
        if (!rotated && InternalReader.Resolve(Dict.Get("C")) is PdfArray bgArr && bgArr.Count >= 3)
        {
            var bg = Color;
            sb.Append("q\n");
            sb.Append(Fmt(bg.R / 255.0)).Append(' ').Append(Fmt(bg.G / 255.0)).Append(' ')
              .Append(Fmt(bg.B / 255.0)).Append(" rg\n");
            sb.Append("0 0 ").Append(Fmt(w)).Append(' ').Append(Fmt(h)).Append(" re\nf\nQ\n");
        }

        // Stroke the border rectangle so a styled/dashed border is visible. The
        // stroke uses the text colour and is inset by half the line width so it
        // sits centred on the rectangle edge (matching the Aspose.PDF for .NET appearance).
        // Only an explicitly-set border is drawn — a FreeText without a /BS or
        // /Border entry keeps its borderless appearance.
        var bsDict = InternalReader.ResolveDict(Dict.Get("BS"));
        bool hasExplicitBorder = bsDict is not null
            || InternalReader.Resolve(Dict.Get("Border")) is PdfArray;
        if (hasExplicitBorder && borderWidth > 0)
        {
            bool dashed = bsDict?.Get("S") is PdfName sn && sn.Value == "D";
            sb.Append("q\n");
            sb.Append(Fmt(color.R / 255.0)).Append(' ').Append(Fmt(color.G / 255.0)).Append(' ')
              .Append(Fmt(color.B / 255.0)).Append(" RG\n");
            sb.Append(Fmt(borderWidth / 2)).Append(' ').Append(Fmt(borderWidth / 2)).Append(' ')
              .Append(Fmt(w - borderWidth)).Append(' ').Append(Fmt(h - borderWidth)).Append(" re\n");
            sb.Append(Fmt(borderWidth)).Append(" w\n");
            if (dashed && InternalReader.Resolve(bsDict!.Get("D")) is PdfArray dArr && dArr.Count > 0)
            {
                sb.Append('[');
                for (int k = 0; k < dArr.Count; k++)
                {
                    if (k > 0) sb.Append(' ');
                    sb.Append(Fmt(PdfArrayHelper.GetDouble(dArr, k)));
                }
                sb.Append("] 0 d\n");
            }
            sb.Append("s\nQ\n");
        }

        sb.Append("/Tx BMC\nq\n");
        if (rotated)
        {
            // Rotate the text about the expanded box centre. The frame origin is the
            // box left edge (inset) at the vertical centre, so the rotated text stays
            // anchored to the rectangle's leading edge (matches the Aspose.PDF for .NET layout).
            string Fmt6(double v) => v.ToString("0.######", ci);
            double e = ehw - (ehw - inset) * rcos;
            double f = ehh - (ehw - inset) * rsin;
            sb.Append(Fmt6(rcos)).Append(' ').Append(Fmt6(rsin)).Append(' ')
              .Append(Fmt6(-rsin)).Append(' ').Append(Fmt6(rcos)).Append(' ')
              .Append(Fmt6(e)).Append(' ').Append(Fmt6(f)).Append(" cm\n");
        }
        sb.Append("BT\n");
        sb.Append('/').Append(ResName(fontName)).Append(' ').Append(Fmt(fontSize)).Append(" Tf\n");
        sb.Append(Fmt(color.R / 255.0)).Append(' ').Append(Fmt(color.G / 255.0)).Append(' ')
          .Append(Fmt(color.B / 255.0)).Append(" rg\n");
        sb.Append(Fmt(leading)).Append(" TL\n");
        if (rotated)
            sb.Append("2 ").Append(Fmt(-fontSize)).Append(" Td\n");
        else
            sb.Append(Fmt(inset)).Append(' ').Append(Fmt(h - inset - fontSize)).Append(" Td\n");
        for (int i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append("T*\n");
            sb.Append('(').Append(EscapePdfString(lines[i])).Append(") Tj\n");
        }
        sb.Append("ET\nQ\nEMC\n");

        var apStream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(sb.ToString()));
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(bboxW)); bbox.Add(new PdfReal(bboxH));
        apStream.Dict.Set("BBox", bbox);
        var fonts = new PdfDictionary();
        fonts.Set(ResName(fontName), fontDict);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        apStream.Dict.Set("Resources", res);

        var ap = new PdfDictionary();
        ap.Set("N", apStream);
        Dict.Set("AP", ap);
    }

    /// <summary>Extract the plain text of a FreeText /RC rich-text packet: strip the
    /// XHTML/XFA markup (and the XML declaration) and decode the basic entities,
    /// leaving the concatenated character data. Returns the input unchanged when it
    /// carries no markup.</summary>
    private static string? PlainTextFromRichText(string? rc)
    {
        if (string.IsNullOrEmpty(rc) || rc.IndexOf('<') < 0) return rc;
        var sb = new System.Text.StringBuilder(rc.Length);
        var inTag = false;
        foreach (var c in rc)
        {
            if (c == '<') inTag = true;
            else if (c == '>') inTag = false;
            else if (!inTag) sb.Append(c);
        }
        return sb.ToString()
            .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&apos;", "'").Trim();
    }

    /// <summary>Greedy word-wrap: a word starts a new line when appending it would
    /// exceed <paramref name="avail"/>; a single word wider than the line still occupies
    /// its own line (no mid-word breaking), matching the Aspose.PDF for .NET layout.</summary>
    private static System.Collections.Generic.List<string> WrapText(
        string text, Aspose.Pdf.Text.FontMetrics metrics, double fontSize, double avail)
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var words = rawLine.Split(' ');
            var current = "";
            foreach (var word in words)
            {
                if (current.Length == 0)
                {
                    current = word;
                    continue;
                }
                var candidate = current + " " + word;
                if (metrics.MeasureString(candidate, fontSize) <= avail)
                    current = candidate;
                else
                {
                    result.Add(current);
                    current = word;
                }
            }
            result.Add(current);
        }
        return result;
    }

    /// <summary>Border width from /BS /W or the legacy /Border array (third element); defaults to 1.</summary>
    private double ReadBorderWidth()
    {
        var bs = InternalReader.ResolveDict(Dict.Get("BS"));
        if (bs is not null)
        {
            var wObj = bs.Get("W");
            if (wObj is PdfInteger bi) return bi.Value;
            if (wObj is PdfReal br) return br.Value;
        }
        if (InternalReader.Resolve(Dict.Get("Border")) is PdfArray arr && arr.Count >= 3)
        {
            if (arr[2] is PdfInteger ai) return ai.Value;
            if (arr[2] is PdfReal ar) return ar.Value;
        }
        return 1.0;
    }

    private static string ResName(string fontName) => fontName.Replace(" ", "");

    private static string EscapePdfString(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>A standard-14 Type1 font dictionary for the appearance, mapping common
    /// aliases (Arial→Helvetica) so metrics and rendering resolve.</summary>
    private static PdfDictionary MakeFreeTextFontDict(string fontName)
    {
        var n = fontName.Replace(" ", "");
        string baseFont = n switch
        {
            "Arial" or "Helv" or "Helvetica" => "Helvetica",
            "ArialBold" or "Arial-Bold" or "HelveticaBold" => "Helvetica-Bold",
            "TimesNewRoman" or "Times" or "TiRo" => "Times-Roman",
            "CourierNew" or "Cour" or "Courier" => "Courier",
            _ => Aspose.Pdf.Text.Standard14Fonts.IsStandard14(n) ? n : "Helvetica",
        };
        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFont));
        return font;
    }

    /// <summary>Callout polyline (/CL entry, PDF 32000 §12.5.6.6 — Free
    /// text). Three points: leader-end, knee, baseline-anchor.</summary>
    public Point[]? Callout
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("CL")) as PdfArray;
            if (arr is null || arr.Count < 4) return null;
            var pts = new List<Point>(arr.Count / 2);
            for (var i = 0; i + 1 < arr.Count; i += 2)
            {
                double x = arr[i] is PdfReal rx ? rx.Value : arr[i] is PdfInteger ix ? ix.Value : 0;
                double y = arr[i + 1] is PdfReal ry ? ry.Value : arr[i + 1] is PdfInteger iy ? iy.Value : 0;
                pts.Add(new Point(x, y));
            }
            return pts.ToArray();
        }
        set
        {
            if (value is null) { Dict.Remove("CL"); return; }
            var arr = new PdfArray();
            foreach (var p in value)
            {
                arr.Add(new PdfReal(p.X));
                arr.Add(new PdfReal(p.Y));
            }
            Dict.Set("CL", arr);
        }
    }

    /// <summary>Justification of the rendered text (/Q: 0=Left, 1=Center, 2=Right).</summary>
    public Justification Justification
    {
        get
        {
            var q = (int)(Dict.Get("Q") is PdfInteger qi ? qi.Value : 0);
            return q switch
            {
                1 => Justification.Center,
                2 => Justification.Right,
                _ => Justification.Left,
            };
        }
        set => Dict.Set("Q", new PdfInteger(value switch
        {
            Justification.Center => 1,
            Justification.Right => 2,
            _ => 0,
        }));
    }

    /// <summary>Free-text intent (/IT — FreeTextCallout / FreeTextTypeWriter).</summary>
    public FreeTextIntent Intent
    {
        get => Dict.GetName("IT") switch
        {
            "FreeTextCallout" => FreeTextIntent.FreeTextCallout,
            "FreeTextTypeWriter" => FreeTextIntent.FreeTextTypeWriter,
            _ => FreeTextIntent.Undefined,
        };
        set
        {
            if (value == FreeTextIntent.Undefined) Dict.Remove("IT");
            else Dict.Set("IT", new PdfName(value.ToString()));
        }
    }

    /// <summary>Starting line-ending style (/LE first entry; callout intent only).</summary>
    public LineEnding StartingStyle { get; set; } = LineEnding.None;

    /// <summary>Ending line-ending style (/LE second entry; callout intent only).</summary>
    public LineEnding EndingStyle { get; set; } = LineEnding.None;

    /// <summary>Page-rotation of the rendered text (/Rotate). Multiples of 90°.</summary>
    public Aspose.Pdf.Rotation Rotate
    {
        get
        {
            var r = (int)(Dict.Get("Rotate") is PdfInteger ri ? ri.Value : 0);
            return r switch
            {
                90 => Aspose.Pdf.Rotation.on90,
                180 => Aspose.Pdf.Rotation.on180,
                270 => Aspose.Pdf.Rotation.on270,
                _ => Aspose.Pdf.Rotation.None,
            };
        }
        set => Dict.Set("Rotate", new PdfInteger((int)value));
    }

    /// <summary>Inner text rectangle (/RD inset from /Rect).</summary>
    public Rectangle? TextRectangle
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("RD")) as PdfArray;
            if (arr is null || arr.Count < 4) return null;
            double L = arr[0] is PdfReal r0 ? r0.Value : 0;
            double B = arr[1] is PdfReal r1 ? r1.Value : 0;
            double R = arr[2] is PdfReal r2 ? r2.Value : 0;
            double T = arr[3] is PdfReal r3 ? r3.Value : 0;
            return new Rectangle(L, B, R, T);
        }
        set
        {
            if (value is null) { Dict.Remove("RD"); return; }
            var arr = new PdfArray();
            arr.Add(new PdfReal(value.LLX));
            arr.Add(new PdfReal(value.LLY));
            arr.Add(new PdfReal(value.URX));
            arr.Add(new PdfReal(value.URY));
            Dict.Set("RD", arr);
        }
    }

    /// <summary>Bundled text style (font / size / colour / alignment) applied
    /// when rendering the rich-text contents. Stored only — the FOSS write
    /// path doesn't currently re-render free-text bodies.</summary>
    public TextStyle TextStyle { get; set; } = new TextStyle();

    /// <summary>Apply a font/size/colour bundle to the whole rich-text run.</summary>
    public void SetTextStyle(RichTextFontStyles textStyles, string fontName, double fontSize, System.Drawing.Color fontColor)
    {
        TextStyle = new TextStyle
        {
            FontName = fontName,
            FontSize = fontSize,
            Color = fontColor,
        };
        // textStyles flags are honoured at FOSS render time only when
        // RichText is re-emitted; stored here for round-trip access.
        _ = textStyles;
    }

    /// <summary>Apply rich-text style flags to a substring (fromInd..toInd
    /// inclusive). The current FOSS rich-text emitter doesn't honour the
    /// indices; the call is captured for parity with Aspose.PDF for .NET.</summary>
    public void SetTextStyle(int fromInd, int toInd, RichTextFontStyles textStyles)
    {
        _ = fromInd; _ = toInd; _ = textStyles;
    }
}

public partial class MarkupAnnotation : Annotation
{
    internal MarkupAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected MarkupAnnotation(Page page, Rectangle rect) : base(page, rect) { CreationDate = System.DateTime.Now; }
    protected MarkupAnnotation(Document document, Rectangle rect) : base(document, rect) { CreationDate = System.DateTime.Now; }

    /// <summary>Document-bound ctor for creating a markup annotation that
    /// isn't yet attached to a specific page; callers add it later via
    /// <c>page.Annotations.Add(annot)</c>.</summary>
    public MarkupAnnotation(Document document) : base(document, rect: null!) { CreationDate = System.DateTime.Now; }

    /// <summary>Set default QuadPoints from the annotation rectangle (4 corners).</summary>
    protected void SetDefaultQuadPoints(Rectangle rect)
    {
        if (rect is null) return;
        var arr = new PdfArray();
        // QuadPoints order: x1,y1 x2,y2 x3,y3 x4,y4 (LL, LR, UL, UR per spec, but commonly: UL, UR, LL, LR)
        arr.Add(new PdfReal(rect.LLX)); arr.Add(new PdfReal(rect.URY)); // upper-left
        arr.Add(new PdfReal(rect.URX)); arr.Add(new PdfReal(rect.URY)); // upper-right
        arr.Add(new PdfReal(rect.LLX)); arr.Add(new PdfReal(rect.LLY)); // lower-left
        arr.Add(new PdfReal(rect.URX)); arr.Add(new PdfReal(rect.LLY)); // lower-right
        Dict.Set("QuadPoints", arr);
    }

    // ── Review / marked-state surface (PDF 32000 §12.5.6.3) ─────────────────

    /// <summary>Read the /State entry on the annotation's properties
    /// dictionary. Returns <see cref="Aspose.Pdf.Annotations.AnnotationState.None"/> when no
    /// state has been recorded.</summary>
    public AnnotationState GetState()
    {
        return ResolveReviewStateValue("State") switch
        {
            "Marked" => Aspose.Pdf.Annotations.AnnotationState.Marked,
            "Unmarked" => Aspose.Pdf.Annotations.AnnotationState.Unmarked,
            "Accepted" => Aspose.Pdf.Annotations.AnnotationState.Accepted,
            "Rejected" => Aspose.Pdf.Annotations.AnnotationState.Rejected,
            "Cancelled" => Aspose.Pdf.Annotations.AnnotationState.Cancelled,
            "Completed" => Aspose.Pdf.Annotations.AnnotationState.Completed,
            "None" => Aspose.Pdf.Annotations.AnnotationState.None,
            // No /State anywhere (e.g. after ClearState) reads back as None.
            _ => Aspose.Pdf.Annotations.AnnotationState.None,
        };
    }

    /// <summary>Read the /StateModel entry, mapping the missing /entry to
    /// <see cref="Aspose.Pdf.Annotations.AnnotationStateModel.Undefined"/>.</summary>
    public AnnotationStateModel GetStateModel() => ResolveReviewStateValue("StateModel") switch
    {
        "Marked" => Aspose.Pdf.Annotations.AnnotationStateModel.Marked,
        "Review" => Aspose.Pdf.Annotations.AnnotationStateModel.Review,
        _ => Aspose.Pdf.Annotations.AnnotationStateModel.Undefined,
    };

    // /State and /StateModel are text strings (PDF §12.5.6.4), not names.
    private static PdfString StateString(string value) =>
        new PdfString(System.Text.Encoding.Latin1.GetBytes(value));

    /// <summary>Set /State to Marked or Unmarked plus /StateModel = Marked.</summary>
    public void SetMarkedState(bool marked)
    {
        Dict.Set("State", StateString(marked ? "Marked" : "Unmarked"));
        Dict.Set("StateModel", StateString("Marked"));
    }

    /// <summary>Set the review state. The state is recorded on this
    /// annotation (/State + /StateModel = Review) and, when the annotation
    /// is attached to a page, also on a reply annotation (/IRT → this) that
    /// <see cref="FindStateAnnotation"/> resolves after a save/reload, per
    /// PDF 32000 §12.5.6.3.</summary>
    public void SetReviewState(AnnotationState state)
    {
        Dict.Set("State", StateString(state.ToString()));
        Dict.Set("StateModel", StateString("Review"));
        AttachStateReply(state.ToString(), "Review", Title ?? string.Empty);
    }

    /// <summary>Set the review state along with the reviewer's username
    /// (recorded in /T per the PDF spec).</summary>
    public void SetReviewState(AnnotationState state, string userName)
    {
        Dict.Set("State", StateString(state.ToString()));
        Dict.Set("StateModel", StateString("Review"));
        if (!string.IsNullOrEmpty(userName))
            Dict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(userName)));
        AttachStateReply(state.ToString(), "Review",
            string.IsNullOrEmpty(userName) ? (Title ?? string.Empty) : userName);
    }

    /// <summary>Remove any recorded /State and /StateModel.</summary>
    public void ClearState()
    {
        Dict.Remove("State");
        Dict.Remove("StateModel");
        ClearReviewStateOnReplies();
    }

    /// <summary>Find the state-tracking annotation linked to this markup
    /// (the most-recent /IRT reply annotation that carries a /State entry,
    /// per PDF 32000 §12.5.6.3). Returns null when no such reply exists.</summary>
    public TextAnnotation? FindStateAnnotation() => FindStateReply();

    // ── Common markup-annotation properties (PDF 32000 §12.5.6.2) ───────────

    /// <summary>Creation timestamp recorded in /CreationDate.</summary>
    public System.DateTime CreationDate
    {
        get
        {
            var raw = (Dict.Get("CreationDate") as PdfString)?.ToText();
            return string.IsNullOrEmpty(raw) ? System.DateTime.MinValue
                : ParsePdfDate(raw) ?? System.DateTime.MinValue;
        }
        set => Dict.Set("CreationDate",
            new PdfString(System.Text.Encoding.Latin1.GetBytes(
                "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z")));
    }

    /// <summary>Opacity (0..1) carried in /CA.</summary>
    public new double Opacity
    {
        get
        {
            var ca = InternalReader.Resolve(Dict.Get("CA"));
            return ca switch
            {
                PdfReal r => r.Value,
                PdfInteger i => i.Value,
                _ => 1.0,
            };
        }
        set => Dict.Set("CA", new PdfReal(value));
    }

    /// <summary>Associated popup annotation (/Popup).</summary>
    public PopupAnnotation? Popup
    {
        get
        {
            var p = InternalReader.ResolveDict(Dict.Get("Popup"));
            return p is null ? null : new PopupAnnotation(p, InternalReader);
        }
        set
        {
            if (value is null) Dict.Remove("Popup");
            else Dict.Set("Popup", value.Dict);
        }
    }

    /// <summary>Reply relationship to <see cref="InReplyTo"/> (/RT).</summary>
    public new ReplyType ReplyType
    {
        get => Dict.GetName("RT") switch
        {
            "R" => ReplyType.Reply,
            "Group" => ReplyType.Group,
            _ => ReplyType.Undefined,
        };
        set => Dict.Set("RT", new PdfName(value == ReplyType.Reply ? "R"
            : value == ReplyType.Group ? "Group" : ""));
    }

    /// <summary>Rich-text contents (/RC), XHTML-formatted.</summary>
    public string? RichText
    {
        get => (Dict.Get("RC") as PdfString)?.ToText();
        set => Dict.Set("RC",
            new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Subject line (/Subj).</summary>
    public new string? Subject
    {
        get => (Dict.Get("Subj") as PdfString)?.ToText();
        set => Dict.Set("Subj",
            new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Author / title carried in /T.</summary>
    public new string? Title
    {
        get => (Dict.Get("T") as PdfString)?.ToText();
        set => Dict.Set("T",
            new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Parse a PDF date string (D:YYYYMMDDHHmmSS) to .NET DateTime
    /// (UTC); returns null on malformed input. Local to MarkupAnnotation —
    /// the base Annotation type also declares one but with a nullable
    /// parameter, so a `new` keyword shields the local version.</summary>
    private static new System.DateTime? ParsePdfDate(string s)
    {
        if (s.StartsWith("D:")) s = s.Substring(2);
        if (s.Length >= 14
            && System.DateTime.TryParseExact(s.Substring(0, 14), "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }
}

/// <summary>
/// Base class for the text-markup annotations (Highlight, Underline, StrikeOut,
/// Squiggly) — those whose geometry is a set of QuadPoints over page text. Adds
/// the ability to recover the text the markup covers.
/// </summary>
public partial class TextMarkupAnnotation : MarkupAnnotation
{
    internal TextMarkupAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected TextMarkupAnnotation(Page page, Rectangle rect) : base(page, rect) { }
    protected TextMarkupAnnotation(Document document, Rectangle rect) : base(document, rect) { }
    /// <summary>Document-bound ctor (added to a page later).</summary>
    public TextMarkupAnnotation(Document document) : base(document) { }

    /// <summary>The text covered by this annotation's QuadPoints, with the text of
    /// each quad (each highlighted line) separated by <see cref="System.Environment.NewLine"/>.</summary>
    public string GetMarkedText()
    {
        if (Page is not { } page || QuadPoints is not { Length: >= 4 } quads)
            return string.Empty;
        var fragments = AbsorbFragments(page);
        var parts = new System.Collections.Generic.List<string>();
        for (var i = 0; i + 3 < quads.Length; i += 4)
        {
            var (minX, minY, maxX, maxY) = QuadBounds(quads, i);
            var sb = new System.Text.StringBuilder();
            foreach (var f in fragments)
                CollectChars(f, minX, minY, maxX, maxY, sb, null);
            parts.Add(sb.ToString());
        }
        return string.Join(System.Environment.NewLine, parts);
    }

    /// <summary>The page text fragments covered by this annotation's QuadPoints —
    /// one clipped fragment per source fragment that each quad overlaps.</summary>
    public Aspose.Pdf.Text.TextFragmentCollection GetMarkedTextFragments()
    {
        var result = new Aspose.Pdf.Text.TextFragmentCollection();
        if (Page is not { } page || QuadPoints is not { Length: >= 4 } quads)
            return result;
        var fragments = AbsorbFragments(page);
        for (var i = 0; i + 3 < quads.Length; i += 4)
        {
            var (minX, minY, maxX, maxY) = QuadBounds(quads, i);
            foreach (var f in fragments)
            {
                var sb = new System.Text.StringBuilder();
                CollectChars(f, minX, minY, maxX, maxY, sb, null);
                if (sb.Length > 0)
                    result.Add(new Aspose.Pdf.Text.TextFragment(sb.ToString()));
            }
        }
        return result;
    }

    private static System.Collections.Generic.List<Aspose.Pdf.Text.TextFragment> AbsorbFragments(Page page)
    {
        var absorber = new Aspose.Pdf.Text.TextFragmentAbsorber();
        page.Accept(absorber);
        var list = new System.Collections.Generic.List<Aspose.Pdf.Text.TextFragment>();
        foreach (Aspose.Pdf.Text.TextFragment f in absorber.TextFragments) list.Add(f);
        return list;
    }

    private static (double minX, double minY, double maxX, double maxY) QuadBounds(Point[] q, int i)
    {
        double minX = Math.Min(Math.Min(q[i].X, q[i + 1].X), Math.Min(q[i + 2].X, q[i + 3].X));
        double maxX = Math.Max(Math.Max(q[i].X, q[i + 1].X), Math.Max(q[i + 2].X, q[i + 3].X));
        double minY = Math.Min(Math.Min(q[i].Y, q[i + 1].Y), Math.Min(q[i + 2].Y, q[i + 3].Y));
        double maxY = Math.Max(Math.Max(q[i].Y, q[i + 1].Y), Math.Max(q[i + 2].Y, q[i + 3].Y));
        return (minX, minY, maxX, maxY);
    }

    /// <summary>Append the characters of <paramref name="fragment"/> that the quad
    /// box covers to <paramref name="sb"/>. A character counts as marked when its
    /// glyph box overlaps the quad's X range by more than a small grazing tolerance
    /// (so a glyph that only just touches the boundary is excluded) and its vertical
    /// centre is within the quad's Y band.</summary>
    private static void CollectChars(Aspose.Pdf.Text.TextFragment fragment,
        double minX, double minY, double maxX, double maxY, System.Text.StringBuilder sb, object? _)
    {
        const double grazeTolerance = 0.1; // points: ignore sub-0.1pt boundary overlaps
        foreach (Aspose.Pdf.Text.TextSegment seg in fragment.Segments)
        {
            var chars = seg.Characters;
            var text = seg.Text ?? string.Empty;
            for (var c = 1; c <= chars.Count && c <= text.Length; c++)
            {
                var r = chars[c].Rectangle;
                var overlapX = Math.Min(r.URX, maxX) - Math.Max(r.LLX, minX);
                var cy = (r.LLY + r.URY) / 2.0;
                if (overlapX > grazeTolerance && cy >= minY - 2 && cy <= maxY + 2)
                    sb.Append(text[c - 1]);
            }
        }
    }
}

/// <summary>Highlight text markup annotation.</summary>
public partial class HighlightAnnotation : TextMarkupAnnotation
{
    internal HighlightAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public HighlightAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Highlight"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Highlight;
}

/// <summary>StrikeOut text markup annotation.</summary>
public partial class StrikeOutAnnotation : TextMarkupAnnotation
{
    internal StrikeOutAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public StrikeOutAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("StrikeOut"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.StrikeOut;
}

/// <summary>Underline text markup annotation.</summary>
public partial class UnderlineAnnotation : TextMarkupAnnotation
{
    internal UnderlineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public UnderlineAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Underline"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Underline;
}

/// <summary>Squiggly text markup annotation.</summary>
public partial class SquigglyAnnotation : TextMarkupAnnotation
{
    internal SquigglyAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    public SquigglyAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Squiggly"));
        SetDefaultQuadPoints(rect);
    }
    public new AnnotationType AnnotationType => AnnotationType.Squiggly;
}

public partial class WidgetAnnotation : Annotation
{
    internal WidgetAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Programmatic ctor — creates a bare widget annotation
    /// associated with <paramref name="doc"/>'s reader. The widget
    /// has no /AP/N appearance state until the caller assigns one.</summary>
    public WidgetAnnotation(Document doc) : base(doc, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Widget"));
    }

    /// <summary>Always <see cref="AnnotationType.Widget"/>. Redeclared
    /// with `new` so Aspose.PDF for .NET reflection (DeclaredOnly) sees it on
    /// WidgetAnnotation directly.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Widget;

    /// <summary>Border width, style and dash pattern resolved from the widget's
    /// /BS dictionary (form fields carry their border there, not in /Border).</summary>
    public new Border? Border
    {
        get
        {
            var border = new Border(this);
            var bs = InternalReader.ResolveDict(Dict.Get("BS"));
            if (bs is not null)
            {
                var w = InternalReader.Resolve(bs.Get("W"));
                if (w is PdfInteger wi) border.Width = (int)wi.Value;
                else if (w is PdfReal wr) border.Width = (int)wr.Value;
                border.Style = bs.GetName("S") switch
                {
                    "D" => BorderStyle.Dashed,
                    "B" => BorderStyle.Beveled,
                    "I" => BorderStyle.Inset,
                    "U" => BorderStyle.Underline,
                    _ => BorderStyle.Solid,
                };
                if (InternalReader.Resolve(bs.Get("D")) is PdfArray d && d.Count > 0)
                {
                    int on = d[0] is PdfInteger di ? (int)di.Value : d[0] is PdfReal dr ? (int)dr.Value : 0;
                    int off = d.Count > 1 ? (d[1] is PdfInteger oi ? (int)oi.Value : d[1] is PdfReal orr ? (int)orr.Value : on) : on;
                    border.Dash = new Dash(on, off);
                }
            }
            return border;
        }
        set => base.Border = value;
    }

    /// <summary>Action slots for the widget's /AA tree. Always non-null.
    /// Lazily populated from the annotation's /AA (additional-actions)
    /// dictionary and /A (activation) entry on first access; further
    /// mutations are kept on the same instance. Redeclared with `new` so
    /// the strongly-typed collection surfaces on WidgetAnnotation
    /// (Aspose.PDF for .NET DeclaredOnly reflection).</summary>
    public new AnnotationActionCollection Actions => _actions ??= BuildActions();
    private AnnotationActionCollection? _actions;

    private AnnotationActionCollection BuildActions()
    {
        var col = new AnnotationActionCollection();
        var reader = InternalReader;
        if (reader is null) return col;

        PdfAction? Read(PdfDictionary? source, string key)
        {
            var d = reader.ResolveDict(source?.Get(key));
            return d is null ? null : PdfAction.Create(d, reader);
        }

        col.OnActivated = Read(Dict, "A");

        var aa = reader.ResolveDict(Dict.Get("AA"));
        if (aa is not null)
        {
            col.OnEnter = Read(aa, "E");
            col.OnExit = Read(aa, "X");
            col.OnPressMouseBtn = Read(aa, "D");
            col.OnReleaseMouseBtn = Read(aa, "U");
            col.OnReceiveFocus = Read(aa, "Fo");
            col.OnLostFocus = Read(aa, "Bl");
            col.OnModifyCharacter = Read(aa, "K");
            col.OnFormat = Read(aa, "F");
            col.OnValidate = Read(aa, "V");
            col.OnCalculate = Read(aa, "C");
            col.OnOpenPage = Read(aa, "PO");
            col.OnClosePage = Read(aa, "PC");
            col.OnShowPage = Read(aa, "PV");
            col.OnHidePage = Read(aa, "PI");
        }
        return col;
    }

    /// <summary>Default-appearance string parsed into the typed
    /// <see cref="DefaultAppearance"/> wrapper.</summary>
    public DefaultAppearance DefaultAppearance { get; set; } = new DefaultAppearance();

    /// <summary>Whether the widget's value should be exported on form
    /// submit. Maps to /Ff bit 3 cleared / set.</summary>
    public bool Exportable
    {
        get => ((int)(Dict.Get("Ff") is PdfInteger ff ? ff.Value : 0) & (1 << 2)) == 0;
        set
        {
            var current = (int)(Dict.Get("Ff") is PdfInteger ff ? ff.Value : 0);
            var updated = value ? current & ~(1 << 2) : current | (1 << 2);
            Dict.Set("Ff", new PdfInteger(updated));
        }
    }

    /// <summary>Visual highlighting mode used when the user clicks the
    /// widget (/H entry).</summary>
    public HighlightingMode Highlighting
    {
        get => Dict.GetName("H") switch
        {
            "N" => HighlightingMode.None,
            "I" => HighlightingMode.Invert,
            "O" => HighlightingMode.Outline,
            "P" => HighlightingMode.Push,
            _ => HighlightingMode.None,
        };
        set => Dict.Set("H", new PdfName(value switch
        {
            HighlightingMode.None => "N",
            HighlightingMode.Invert => "I",
            HighlightingMode.Outline => "O",
            HighlightingMode.Push => "P",
            _ => "N",
        }));
    }

    /// <summary>Action invoked when the widget is activated (/A entry).
    /// Stored only — the FOSS renderer doesn't dispatch widget actions.</summary>
    public PdfAction? OnActivated { get; set; }

    /// <summary>Parent <see cref="Forms.Field"/> when this widget is the
    /// visual child of an AcroForm field. Returns null when standalone.</summary>
    public Forms.Field? Parent { get; internal set; }

    /// <summary>Whether the widget rejects input (/Ff bit 1).</summary>
    public bool ReadOnly
    {
        get => ((int)(Dict.Get("Ff") is PdfInteger ff ? ff.Value : 0) & 1) != 0;
        set
        {
            var current = (int)(Dict.Get("Ff") is PdfInteger ff ? ff.Value : 0);
            var updated = value ? current | 1 : current & ~1;
            Dict.Set("Ff", new PdfInteger(updated));
        }
    }

    /// <summary>Whether the widget must be filled before submit (/Ff bit 2).</summary>
    public bool Required
    {
        get => ((int)(Dict.Get("Ff") is PdfInteger ff ? ff.Value : 0) & (1 << 1)) != 0;
        set
        {
            var current = (int)(Dict.Get("Ff") is PdfInteger ff ? ff.Value : 0);
            var updated = value ? current | (1 << 1) : current & ~(1 << 1);
            Dict.Set("Ff", new PdfInteger(updated));
        }
    }

    /// <summary>Serialise this widget's field as JSON to a stream.</summary>
    public System.Collections.Generic.IEnumerable<FieldSerializationResult> ExportToJson(
        System.IO.Stream stream)
        => ExportToJson(stream, null);

    /// <summary>Serialise this widget's field as JSON to a file.</summary>
    public System.Collections.Generic.IEnumerable<FieldSerializationResult> ExportToJson(
        string fileName)
        => ExportToJson(fileName, null);

    /// <summary>Serialise this widget's field as a single
    /// <see cref="FieldExportingData"/> JSON object to a stream.</summary>
    public System.Collections.Generic.IEnumerable<FieldSerializationResult> ExportToJson(
        System.IO.Stream stream, ExportFieldsToJsonOptions? options)
    {
        if (stream is null) throw new System.ArgumentNullException(nameof(stream));
        var field = new Aspose.Pdf.Forms.Field(Dict, InternalReader);
        var data = Aspose.Pdf.Forms.FieldJsonExporter.BuildField(field);
        Aspose.Pdf.Forms.FieldJsonExporter.Write(stream, data, options?.WriteIndented ?? false);
        return new[]
        {
            new FieldSerializationResult
            {
                FieldFullName = field.FullName ?? field.PartialName ?? string.Empty,
                FieldSerializationStatus = FieldSerializationStatus.Success,
            },
        };
    }

    /// <summary>Serialise this widget's field as JSON to a file.</summary>
    public System.Collections.Generic.IEnumerable<FieldSerializationResult> ExportToJson(
        string fileName, ExportFieldsToJsonOptions? options)
    {
        using var fs = new System.IO.FileStream(fileName, System.IO.FileMode.Create, System.IO.FileAccess.Write);
        return ExportToJson(fs, options);
    }

    /// <summary>
    /// Returns the appearance state name that represents the "on" /
    /// "checked" state of this widget (typically "Yes"), looked up from
    /// the /AP/N dict's keys (anything that isn't "Off"). Returns an
    /// empty string when the widget has no appearance states defined.
    /// </summary>
    public string GetCheckedStateName()
    {
        var apDict = InternalReader.ResolveDict(Dict.Get("AP"));
        if (apDict is null) return string.Empty;
        var n = InternalReader.Resolve(apDict.Get("N")) as PdfDictionary;
        if (n is null) return string.Empty;
        foreach (var key in n.Keys)
            if (key != "Off") return key;
        return string.Empty;
    }

    /// <summary>The highlight mode (/H entry). Maps /I→Invert, /O→Outline, /P→Push. Default: Invert.</summary>
    public string HighlightMode
    {
        get
        {
            var h = Dict.GetName("H");
            return h switch
            {
                "I" => "Invert",
                "O" => "Outline",
                "P" => "Push",
                "N" => "None",
                _ => "Invert", // default per spec
            };
        }
    }

    /// <summary>The default appearance string (/DA entry).</summary>
    public string? DefaultAppearanceString => (Dict.Get("DA") as PdfString)?.ToText();

    /// <summary>
    /// The widget's "Normal" appearance (the /AP /N stream). Returns an
    /// <see cref="XForm"/> wrapper so callers can iterate its
    /// content-stream operators via <c>NormalAppearance.Contents</c>.
    /// State-keyed dicts (checkbox /Yes vs /Off) pick the on-state stream.
    /// </summary>
    public override XForm? NormalAppearance
    {
        get
        {
            var ap = InternalReader.ResolveDict(Dict.Get("AP"));
            if (ap is null) return null;
            var nObj = InternalReader.Resolve(ap.Get("N"));
            if (nObj is PdfStream direct) return new XForm(direct, InternalReader);
            if (nObj is PdfDictionary stateDict)
            {
                PdfStream? firstAny = null;
                foreach (var key in stateDict.Keys)
                {
                    var resolved = InternalReader.ResolveStream(stateDict.Get(key));
                    if (resolved is null) continue;
                    firstAny ??= resolved;
                    if (key != "Off") return new XForm(resolved, InternalReader);
                }
                return firstAny is null ? null : new XForm(firstAny, InternalReader);
            }
            return null;
        }
    }

    /// <summary>The field value (/V entry).</summary>
    public string? FieldValue
    {
        get
        {
            var obj = InternalReader.Resolve(Dict.Get("V"));
            return obj switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
        }
    }

    /// <summary>The default field value (/DV entry).</summary>
    public string? DefaultFieldValue
    {
        get
        {
            var obj = InternalReader.Resolve(Dict.Get("DV"));
            return obj switch
            {
                PdfString s => s.ToText(),
                PdfName n => n.Value,
                _ => null,
            };
        }
    }

    /// <summary>The field type (/FT entry).</summary>
    public string? FieldType => Dict.GetName("FT");
}

/// <summary>
/// Represents a redaction annotation. Marks an area for content removal;
/// call <see cref="Redact"/> to flatten and remove underlying text/images.
/// </summary>
public partial class RedactionAnnotation : Annotation
{
    internal RedactionAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a new redaction annotation for the given page rectangle.</summary>
    public RedactionAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Redact"));
        _page = page;
    }

    /// <summary>Document-bound redaction annotation; caller adds it to a page later.</summary>
    public RedactionAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Redact"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Redact;

    /// <summary>Default appearance string (/DA) applied to overlay text.</summary>
    public string DefaultAppearance
    {
        get => (InternalReader.Resolve(Dict.Get("DA")) as PdfString)?.ToText() ?? string.Empty;
        set => Dict.Set("DA", new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Font size used by the overlay text. Stored only.</summary>
    public float FontSize { get; set; }

    /// <summary>Flatten this redaction's overlay onto the page. Stored only — use <see cref="Redact()"/> to actually remove content.</summary>
    public new void Flatten() { }

    private Page? _page;

    /// <summary>The overlay text (/OverlayText entry).</summary>
    public string? OverlayText
    {
        get
        {
            var obj = InternalReader.Resolve(Dict.Get("OverlayText"));
            return obj is PdfString s ? s.ToText() : null;
        }
        set
        {
            if (value is null) Dict.Remove("OverlayText");
            else Dict.Set("OverlayText", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    /// <summary>Whether overlay text should repeat (/Repeat entry).</summary>
    public bool Repeat
    {
        get
        {
            var obj = Dict.Get("Repeat");
            return obj is PdfBoolean b && b.Value;
        }
        set => Dict.Set("Repeat", value ? PdfBoolean.True : PdfBoolean.False);
    }

    /// <summary>Justification: 0=left, 1=center, 2=right (/Q entry).</summary>
    public int Justification
    {
        get => (int)Dict.GetInt("Q");
        set => Dict.Set("Q", new PdfInteger(value));
    }

    /// <summary>Text alignment for overlay text (maps to /Q).</summary>
    public HorizontalAlignment TextAlignment
    {
        get => Justification switch
        {
            1 => HorizontalAlignment.Center,
            2 => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };
        set => Justification = value switch
        {
            HorizontalAlignment.Center => 1,
            HorizontalAlignment.Right => 2,
            _ => 0,
        };
    }

    /// <summary>Fill color (/IC entry).</summary>
    public Color? FillColor
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("IC")) as PdfArray;
            if (arr is null || arr.Count == 0) return null;
            return ColorFromArray(arr);
        }
        set
        {
            if (value is null) Dict.Remove("IC");
            else Dict.Set("IC", ColorToArray(value));
        }
    }

    /// <summary>Border color (/C entry — same as <see cref="Annotation.Color"/>
    /// but typed; kept as a convenience for redaction code that distinguishes
    /// border vs. fill).</summary>
    public Color? BorderColor
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("C")) as PdfArray;
            if (arr is null || arr.Count == 0) return null;
            return ColorFromArray(arr);
        }
        set
        {
            if (value is null) Dict.Remove("C");
            else Dict.Set("C", ColorToArray(value));
        }
    }

    /// <summary>The popup annotation associated with this redaction (/Popup entry), or null.</summary>
    public PopupAnnotation? Popup
    {
        get
        {
            var p = InternalReader.ResolveDict(Dict.Get("Popup"));
            return p is null ? null : new PopupAnnotation(p, InternalReader);
        }
    }

    /// <summary>QuadPoints (/QuadPoints entry) defining sub-rectangles within
    /// the annotation's Rect. Aspose.PDF returns these as a Point[] array
    /// where every two consecutive points form a rectangle's diagonal.</summary>
    public Point[]? QuadPoint
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("QuadPoints")) as PdfArray;
            if (arr is null || arr.Count < 8 || arr.Count % 2 != 0) return null;
            var pts = new Point[arr.Count / 2];
            for (int i = 0; i < pts.Length; i++)
            {
                var x = (arr[i * 2] as PdfReal)?.Value
                        ?? (arr[i * 2] as PdfInteger)?.Value ?? 0;
                var y = (arr[i * 2 + 1] as PdfReal)?.Value
                        ?? (arr[i * 2 + 1] as PdfInteger)?.Value ?? 0;
                pts[i] = new Point(x, y);
            }
            return pts;
        }
        set
        {
            if (value is null || value.Length == 0) Dict.Remove("QuadPoints");
            else
            {
                var arr = new PdfArray();
                foreach (var p in value)
                {
                    arr.Add(new PdfReal(p.X));
                    arr.Add(new PdfReal(p.Y));
                }
                Dict.Set("QuadPoints", arr);
            }
        }
    }

    /// <summary>The /CreationDate as a DateTime.</summary>
    public DateTime CreationDate
    {
        get
        {
            var s = (Dict.Get("CreationDate") as PdfString)?.ToText();
            return ParsePdfDate(s);
        }
        set => Dict.Set("CreationDate", new PdfString(System.Text.Encoding.Latin1.GetBytes(FormatPdfDate(value))));
    }


    /// <summary>
    /// Flatten this annotation and remove underlying content within its rectangle:
    /// physically delete the text whose glyphs fall under the redaction rectangle
    /// (so it can no longer be extracted), then paint the FillColor
    /// over the rectangle as an opaque overlay.
    /// </summary>
    public void Redact()
    {
        // _page is only set when the annotation is constructed directly; annotations
        // reached through Page.Annotations (e.g. imported from XFDF) carry their page
        // via the resolved Page property instead, so fall back to it.
        var page = _page ?? Page;
        if (page is null || Rect is null) return;
        var r = Rect;

        // Physically remove the text under the rectangle so it can no longer be
        // extracted, not just covered. Find the fragments whose
        // bounding box overlaps the redaction rect and delete them through a
        // TextReplacer in redaction mode: a full deletion that normally drops the
        // show operator (reflowing the rest of the line and shifting visible text
        // outside the box) instead leaves a glyph-less advance, so
        // following text keeps its position. Scope each deletion to the fragment's
        // line (TargetY) to avoid touching same-text elsewhere. Guarded so an edit
        // failure still leaves the opaque overlay below.
        try
        {
            var absorber = new Text.TextFragmentAbsorber();
            page.Accept(absorber);
            foreach (Text.TextFragment tf in absorber.TextFragments)
            {
                var fr = tf.Rectangle;
                if (fr is null || string.IsNullOrEmpty(tf.Text)) continue;
                // Vertical overlap with the redaction rect (same line band).
                if (!(fr.LLY < r.URY && fr.URY > r.LLY)) continue;
                // Horizontal overlap required too.
                if (!(fr.LLX < r.URX && fr.URX > r.LLX)) continue;

                if (fr.LLX >= r.LLX - 0.5 && fr.URX <= r.URX + 0.5)
                {
                    // Fragment lies entirely within the rect — redact it whole.
                    tf.RedactFromContent();
                    continue;
                }

                // FOSS returns line-level fragments, so a word-sized redaction rect
                // overlaps a longer line. Redact only the characters whose advance
                // span falls inside the rect's X range (so the rest of the line is
                // kept), width-preserving so following text does not reflow.
                var sub = SubstringInXRange(tf, r.LLX, r.URX);
                if (!string.IsNullOrEmpty(sub))
                {
                    var tr = new Text.TextReplacer { PreserveAdvanceOnDelete = true };
                    if (tf.Position is not null) tr.TargetY = tf.Position.YIndent;
                    tr.Replace(page, sub, string.Empty, false);
                }
            }
        }
        catch { /* fall back to overlay-only redaction */ }

        // Redaction also removes interactive form fields whose widget lies under the
        // redaction rectangle: the field is dropped from the AcroForm
        // /Fields and its widget from the page /Annots, so its value can no longer be
        // read back. Fields outside the rectangle are untouched. Guarded so a form
        // mishap still leaves the opaque overlay below.
        try { RemoveFieldsUnder(r); }
        catch { /* leave fields intact, still draw the overlay */ }

        var fill = FillColor ?? Color.Black;
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetFillColor(fill.R / 255.0, fill.G / 255.0, fill.B / 255.0);
        b.Rectangle(r.LLX, r.LLY, r.URX - r.LLX, r.URY - r.LLY);
        b.Fill();
        b.RestoreState();
        page.AddContentStream(b.Build());
    }

    /// <summary>Delete every AcroForm field that has a widget overlapping the
    /// redaction rectangle <paramref name="r"/> on this page — both the field
    /// entry in /AcroForm /Fields and its widget(s) in the page /Annots.</summary>
    private void RemoveFieldsUnder(Rectangle r)
    {
        if (_page is null) return;
        // Use the page's document reader: a programmatically-created redaction
        // annotation has an empty InternalReader, but the page is bound to the
        // real document and exposes its catalog/AcroForm.
        var reader = _page.Reader;
        var acro = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        if (acro is null || reader.Resolve(acro.Get("Fields")) is not PdfArray fields) return;

        // A field is redacted when its widget's CENTRE lies inside the rectangle,
        // not merely when the rectangles touch: a widget that only clips the edge
        // of the redaction box (e.g. a neighbouring line caught by a couple of
        // points) must survive, matching Aspose.PDF for .NET behaviour.
        bool CentreInside(PdfDictionary? d)
        {
            if (d is null || reader.Resolve(d.Get("Rect")) is not PdfArray arr || arr.Count < 4) return false;
            var fr = Rectangle.FromPdfArray(arr, reader);
            if (fr is null) return false;
            var cx = (fr.LLX + fr.URX) / 2.0;
            var cy = (fr.LLY + fr.URY) / 2.0;
            return cx >= r.LLX && cx <= r.URX && cy >= r.LLY && cy <= r.URY;
        }

        var removedFields = new HashSet<PdfDictionary>();
        var keptFields = new PdfArray();
        foreach (var fref in fields)
        {
            var fd = reader.ResolveDict(fref);
            var hit = CentreInside(fd);
            if (!hit && fd is not null && reader.Resolve(fd.Get("Kids")) is PdfArray kids)
                foreach (var k in kids)
                    if (CentreInside(reader.ResolveDict(k))) { hit = true; break; }
            if (hit && fd is not null) removedFields.Add(fd);
            else keptFields.Add(fref);
        }
        if (removedFields.Count == 0) return;
        acro.Set("Fields", keptFields);

        // Drop the matching widget annotations (the field dict itself, or a kid
        // widget whose /Parent is a removed field) from this page's /Annots.
        if (reader.Resolve(_page.Dict.Get("Annots")) is PdfArray annots)
        {
            var keptAnnots = new PdfArray();
            foreach (var aref in annots)
            {
                var ad = reader.ResolveDict(aref);
                var drop = ad is not null &&
                           (removedFields.Contains(ad) ||
                            removedFields.Contains(reader.ResolveDict(ad.Get("Parent"))!));
                if (!drop) keptAnnots.Add(aref);
            }
            if (keptAnnots.Count > 0) _page.Dict.Set("Annots", keptAnnots);
            else _page.Dict.Remove("Annots");
        }
    }

    // Characters of <paramref name="tf"/> whose advance span lies (by midpoint)
    // within the device-X range [x0,x1] of a redaction rect — used to redact a
    // word out of a longer line fragment without touching the rest of the line.
    // Uses the fragment font's cumulative measured width (falls back to an even
    // split when metrics are unavailable).
    private static string? SubstringInXRange(Text.TextFragment tf, double x0, double x1)
    {
        var rect = tf.Rectangle;
        var text = tf.Text;
        if (rect is null || string.IsNullOrEmpty(text)) return null;
        var font = tf.TextState?.Font;
        var fs = tf.TextState?.FontSize ?? 0;

        double Prefix(int n)
        {
            if (n <= 0) return 0;
            if (font is not null && fs > 0)
            {
                try { return font.MeasureString(text.Substring(0, n), (float)fs); }
                catch { }
            }
            return rect.Width * n / text.Length; // even-split fallback
        }

        int start = -1, end = -1;
        for (int i = 0; i < text.Length; i++)
        {
            double cl = rect.LLX + Prefix(i);
            double cr = rect.LLX + Prefix(i + 1);
            double mid = (cl + cr) / 2;
            if (mid >= x0 && mid <= x1) { if (start < 0) start = i; end = i; }
        }
        return start < 0 ? null : text.Substring(start, end - start + 1);
    }

    private static Color ColorFromArray(PdfArray arr)
    {
        double V(int i) => arr[i] switch
        {
            PdfInteger pi => pi.Value,
            PdfReal pr => pr.Value,
            _ => 0,
        };
        return arr.Count switch
        {
            1 => Color.FromGray(V(0)),
            3 => Color.FromRgb(V(0), V(1), V(2)),
            4 => Color.FromCmyk(V(0), V(1), V(2), V(3)),
            _ => Color.FromRgb(0, 0, 0),
        };
    }

    private static PdfArray ColorToArray(Color c)
    {
        var arr = new PdfArray();
        arr.Add(new PdfReal(c.R / 255.0));
        arr.Add(new PdfReal(c.G / 255.0));
        arr.Add(new PdfReal(c.B / 255.0));
        return arr;
    }
}

/// <summary>Backward-compatible alias for <see cref="RedactionAnnotation"/>.</summary>
public class RedactAnnotation : RedactionAnnotation
{
    internal RedactAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
}

/// <summary>
/// Represents a file attachment annotation.
/// </summary>
public partial class FileAttachmentAnnotation : MarkupAnnotation
{
    internal FileAttachmentAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Create a new file-attachment annotation on <paramref name="page"/>
    /// at <paramref name="rect"/> referencing <paramref name="fileSpec"/>.
    /// The annotation gets a default "Paperclip" icon name; callers can
    /// override via <see cref="IconName"/>'s setter (when added).
    /// </summary>
    public FileAttachmentAnnotation(Page page, Rectangle rect, FileSpecification fileSpec)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("FileAttachment"));
        Dict.Set("Name", new PdfName("Paperclip"));
        if (fileSpec is not null)
            Dict.Set("FS", fileSpec.Dict);
    }

    /// <summary>The icon name (/Name entry), e.g. "Paperclip", "Tag".</summary>
    public string? IconName => Dict.GetName("Name");

    /// <summary>The attached file name from /FS dictionary.</summary>
    public string? FileName
    {
        get
        {
            var fs = InternalReader.ResolveDict(Dict.Get("FS"));
            if (fs is null) return null;
            var obj = InternalReader.Resolve(fs.Get("F"));
            return obj is PdfString s ? s.ToText() : null;
        }
    }

    /// <summary>The attached file specification.</summary>
    public FileSpecification? File
    {
        get
        {
            var fs = InternalReader.ResolveDict(Dict.Get("FS"));
            return fs is not null ? new FileSpecification(fs, InternalReader) : null;
        }
        set
        {
            if (value is null) Dict.Remove("FS");
            else
            {
                value.MaterializeEmbeddedStream();
                Dict.Set("FS", value.Dict);
            }
        }
    }

    /// <summary>Always <see cref="AnnotationType.FileAttachment"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.FileAttachment;

    /// <summary>Named icon style for the attachment marker.</summary>
    public FileIcon Icon
    {
        get => Dict.GetName("Name") switch
        {
            "Graph" => FileIcon.Graph,
            "Paperclip" => FileIcon.Paperclip,
            "Tag" => FileIcon.Tag,
            _ => FileIcon.PushPin,
        };
        set => Dict.Set("Name", new PdfName(value.ToString()));
    }

    /// <summary>Annotation opacity (/CA entry; 0..1).</summary>
    public new double Opacity
    {
        get => (InternalReader.Resolve(Dict.Get("CA")) is PdfReal r) ? r.Value
              : (InternalReader.Resolve(Dict.Get("CA")) is PdfInteger i) ? i.Value
              : 1.0;
        set => Dict.Set("CA", new PdfReal(value));
    }
}

public partial class PopupAnnotation : Annotation
{
    internal PopupAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Document-bound popup ctor; rectangle defaults to empty.</summary>
    public PopupAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Popup"));
    }

    /// <summary>Always <see cref="AnnotationType.Popup"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Popup;

    /// <summary>Programmatic ctor — creates a /Popup annotation at
    /// <paramref name="rect"/> on <paramref name="page"/>.</summary>
    public PopupAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Popup"));
    }

    public bool Open
    {
        get => Dict.Get("Open") is PdfBoolean b ? b.Value : Dict.GetInt("Open") != 0;
        set => Dict.Set("Open", (value ? PdfBoolean.True : PdfBoolean.False));
    }

    /// <summary>The parent markup annotation this popup is attached to,
    /// or null if the popup has no /Parent entry.</summary>
    public Annotation? Parent
    {
        get
        {
            var parentDict = InternalReader.ResolveDict(Dict.Get("Parent"));
            return parentDict is null ? null : Annotation.Create(parentDict, InternalReader, -1);
        }
        set
        {
            if (value is null) Dict.Remove("Parent");
            else Dict.Set("Parent", value.Dict);
        }
    }
}

/// <summary>Named stamp-icon style for <see cref="StampAnnotation"/> (/Name entry, PDF 32000 §12.5.6.14).</summary>
public enum StampIcon
{
    Draft = 0,
    Approved,
    Experimental,
    NotApproved,
    AsIs,
    Expired,
    NotForPublicRelease,
    Confidential,
    Final,
    Sold,
    Departmental,
    ForComment,
    ForPublicRelease,
    TopSecret,
}

public partial class StampAnnotation : MarkupAnnotation
{
    internal StampAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public StampAnnotation(Document document) : base(document)
    {
        Dict.Set("Subtype", new PdfName("Stamp"));
    }

    public StampAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Stamp"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Stamp;

    private StampIcon _icon = StampIcon.Draft;

    /// <summary>Named stamp icon. Setting it records the standard /Name and
    /// regenerates the stamp's normal appearance (a bordered banner with the
    /// stamp's label).</summary>
    public StampIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            Dict.Set("Name", new PdfName(value.ToString()));
            UpdateAppearances();
        }
    }

    private System.IO.Stream? _image;

    /// <summary>The stamp's image. When set programmatically the stored stream is
    /// returned; otherwise, for a stamp loaded from a document, the image is extracted
    /// from the normal appearance (/AP /N) — the first image XObject in its resources —
    /// and returned as a PNG stream (matching the Aspose.PDF for .NET API).</summary>
    public System.IO.Stream? Image
    {
        get => _image ?? ExtractAppearanceImage();
        set => _image = value;
    }

    private System.IO.Stream? ExtractAppearanceImage()
    {
        var form = NormalAppearance;
        if (form is null) return null;
        var imgStream = FindImageXObject(form.StreamDict, form.Reader, 0);
        if (imgStream is null) return null;
        try
        {
            var xi = new Aspose.Pdf.XImage("StampImage", imgStream, form.Reader);
            return new System.IO.MemoryStream(xi.ToPng());
        }
        catch { return null; }
    }

    private static Core.PdfStream? FindImageXObject(Core.PdfDictionary streamDict, IO.PdfReader reader, int depth)
    {
        if (depth > 8) return null;
        var res = reader.ResolveDict(streamDict.Get("Resources"));
        var xobjs = reader.ResolveDict(res?.Get("XObject"));
        if (xobjs is null) return null;
        foreach (var key in xobjs.Keys)
        {
            if (reader.ResolveStream(xobjs.Get(key)) is not { } s) continue;
            var sub = s.Dict.GetName("Subtype");
            if (sub == "Image") return s;
            if (sub == "Form" && FindImageXObject(s.Dict, reader, depth + 1) is { } nested) return nested;
        }
        return null;
    }

    private static (string label, double r, double g, double b) StampStyle(StampIcon icon) => icon switch
    {
        StampIcon.Approved => ("APPROVED", 0.08, 0.51, 0.16),
        StampIcon.Final => ("FINAL", 0.08, 0.51, 0.16),
        StampIcon.ForPublicRelease => ("FOR PUBLIC RELEASE", 0.08, 0.51, 0.16),
        StampIcon.Sold => ("SOLD", 0.12, 0.24, 0.67),
        StampIcon.Departmental => ("DEPARTMENTAL", 0.12, 0.24, 0.67),
        StampIcon.Experimental => ("EXPERIMENTAL", 0.12, 0.24, 0.67),
        StampIcon.NotApproved => ("NOT APPROVED", 0.78, 0.12, 0.12),
        StampIcon.AsIs => ("AS IS", 0.78, 0.12, 0.12),
        StampIcon.Expired => ("EXPIRED", 0.78, 0.12, 0.12),
        StampIcon.NotForPublicRelease => ("NOT FOR PUBLIC RELEASE", 0.78, 0.12, 0.12),
        StampIcon.Confidential => ("CONFIDENTIAL", 0.78, 0.12, 0.12),
        StampIcon.ForComment => ("FOR COMMENT", 0.78, 0.12, 0.12),
        StampIcon.TopSecret => ("TOP SECRET", 0.78, 0.12, 0.12),
        _ => ("DRAFT", 0.78, 0.12, 0.12),
    };

    /// <summary>Regenerate the normal appearance (/AP /N): a bordered banner
    /// carrying the stamp's label in the stamp colour.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) return;
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) return;

        var (label, cr, cg, cb) = StampStyle(_icon);
        var len = System.Math.Max(1, label.Length);
        // ~0.6em average glyph advance for Helvetica caps; size to fit the width.
        var fontSize = System.Math.Max(4.0, System.Math.Min(h * 0.45, 1.55 * w / len));
        var textW = len * fontSize * 0.6;
        var tx = r.LLX + (w - textW) / 2;
        var ty = r.LLY + (h - fontSize) / 2;

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(cr, cg, cb);
        b.SetLineWidth(System.Math.Max(1.0, h * 0.05));
        b.Rectangle(r.LLX + 1, r.LLY + 1, w - 2, h - 2);
        b.Stroke();
        b.SetFillColor(cr, cg, cb);
        b.BeginText();
        b.SetFont("Helv", fontSize);
        b.MoveTextPosition(tx, ty);
        b.ShowText(label);
        b.EndText();
        b.RestoreState();
        SetNormalAppearanceWithHelvetica(b.Build(), r);
    }

    public string? IconName => Dict.GetName("Name");

    /// <summary>The stamp's normal appearance (/AP /N stream) wrapped as an XForm.</summary>
    public override XForm? NormalAppearance
    {
        get
        {
            var ap = InternalReader.ResolveDict(Dict.Get("AP"));
            if (ap is null) return null;
            var nStream = InternalReader.ResolveStream(ap.Get("N"));
            return nStream is null ? null : new XForm(nStream, InternalReader);
        }
    }
}

/// <summary>Cap style used by ink strokes (free-hand drawings).</summary>
public enum CapStyle
{
    /// <summary>Square stroke ends.</summary>
    Rectangular = 0,
    /// <summary>Rounded stroke ends.</summary>
    Rounded = 1,
}

public partial class InkAnnotation : MarkupAnnotation
{
    internal InkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct a page-bound ink annotation with the given stroke paths.</summary>
    public InkAnnotation(Page page, Rectangle rect, IList<Point[]> inkList)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Ink"));
        WriteInkList(inkList);
    }

    /// <summary>Construct a document-bound ink annotation; rectangle is derived from the points.</summary>
    public InkAnnotation(Document document, IList<Point[]> inkList)
        : base(document, RectFromInkList(inkList))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Ink"));
        WriteInkList(inkList);
    }

    /// <summary>Legacy non-generic overload accepting an <see cref="System.Collections.IList"/> of <see cref="Point"/>[].</summary>
    public InkAnnotation(Page page, Rectangle rect, System.Collections.IList inkList)
        : this(page, rect, ToGenericInkList(inkList))
    {
    }

    /// <summary>Always <see cref="AnnotationType.Ink"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Ink;

    /// <summary>Stroke cap style; stored only.</summary>
    public CapStyle CapStyle { get; set; } = CapStyle.Rectangular;

    /// <summary>The /InkList entry: each inner array is one stroke path.</summary>
    public IList<Point[]> InkList
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("InkList")) as PdfArray;
            var result = new List<Point[]>();
            if (arr is null) return result;
            foreach (var item in arr)
            {
                if (InternalReader.Resolve(item) is not PdfArray pts) continue;
                var stroke = new Point[pts.Count / 2];
                for (int i = 0; i + 1 < pts.Count; i += 2)
                    stroke[i / 2] = new Point(GetN(pts[i]), GetN(pts[i + 1]));
                result.Add(stroke);
            }
            return result;
        }
        set => WriteInkList(value);
    }

    /// <summary>Transform every ink point and refresh the bounding rectangle.</summary>
    public new void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        var strokes = InkList;
        var transformed = new List<Point[]>(strokes.Count);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var stroke in strokes)
        {
            var newStroke = new Point[stroke.Length];
            for (int i = 0; i < stroke.Length; i++)
            {
                transform.Transform(stroke[i].X, stroke[i].Y, out var nx, out var ny);
                newStroke[i] = new Point(nx, ny);
                if (nx < minX) minX = nx;
                if (ny < minY) minY = ny;
                if (nx > maxX) maxX = nx;
                if (ny > maxY) maxY = ny;
            }
            transformed.Add(newStroke);
        }
        WriteInkList(transformed);
        if (transformed.Count > 0)
            Rect = new Rectangle(minX, minY, maxX, maxY);
    }

    private void WriteInkList(IList<Point[]> inkList)
    {
        var outer = new PdfArray();
        if (inkList is not null)
        {
            foreach (var stroke in inkList)
            {
                if (stroke is null) continue;
                var inner = new PdfArray();
                foreach (var p in stroke)
                {
                    inner.Add(new PdfReal(p.X));
                    inner.Add(new PdfReal(p.Y));
                }
                outer.Add(inner);
            }
        }
        Dict.Set("InkList", outer);
    }

    private static Rectangle RectFromInkList(IList<Point[]> inkList)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        bool any = false;
        if (inkList is not null)
        {
            foreach (var stroke in inkList)
            {
                if (stroke is null) continue;
                foreach (var p in stroke)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y > maxY) maxY = p.Y;
                    any = true;
                }
            }
        }
        return any ? new Rectangle(minX, minY, maxX, maxY) : new Rectangle(0, 0, 0, 0);
    }

    private static IList<Point[]> ToGenericInkList(System.Collections.IList inkList)
    {
        var result = new List<Point[]>();
        if (inkList is null) return result;
        foreach (var item in inkList)
        {
            if (item is Point[] arr) result.Add(arr);
        }
        return result;
    }

    private static double GetN(PdfObject? obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };
}

public partial class LineAnnotation : MarkupAnnotation
{
    internal LineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public LineAnnotation(Page page, Rectangle rect, Point start, Point end)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Line"));
        var lArr = new PdfArray();
        lArr.Add(new PdfReal(start.X));
        lArr.Add(new PdfReal(start.Y));
        lArr.Add(new PdfReal(end.X));
        lArr.Add(new PdfReal(end.Y));
        Dict.Set("L", lArr);
    }

    /// <summary>
    /// Document-bound LineAnnotation ctor. The annotation rectangle is
    /// derived from the start/end points. The annotation isn't bound to any
    /// page yet — caller adds it to the desired pages via
    /// <c>page.Annotations.Add(...)</c>.
    /// </summary>
    public LineAnnotation(Document document, Point start, Point end)
        : base(document, RectFromPoints(start, end))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Line"));
        var lArr = new PdfArray();
        lArr.Add(new PdfReal(start.X));
        lArr.Add(new PdfReal(start.Y));
        lArr.Add(new PdfReal(end.X));
        lArr.Add(new PdfReal(end.Y));
        Dict.Set("L", lArr);
    }

    /// <summary>Always <see cref="AnnotationType.Line"/>. Redeclared with
    /// `new` so Aspose.PDF for .NET reflection sees it on LineAnnotation directly.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Line;

    /// <summary>Border with width, style and dash pattern resolved from /BS.</summary>
    public new Border? Border
    {
        get
        {
            var border = new Border(this);
            var bs = InternalReader.ResolveDict(Dict.Get("BS"));
            if (bs is not null)
            {
                var w = InternalReader.Resolve(bs.Get("W"));
                if (w is PdfInteger wi) border.Width = (int)wi.Value;
                else if (w is PdfReal wr) border.Width = (int)wr.Value;
                border.Style = bs.GetName("S") switch
                {
                    "D" => BorderStyle.Dashed,
                    "B" => BorderStyle.Beveled,
                    "I" => BorderStyle.Inset,
                    "U" => BorderStyle.Underline,
                    _ => BorderStyle.Solid,
                };
                if (InternalReader.Resolve(bs.Get("D")) is PdfArray d && d.Count > 0)
                {
                    int on = d[0] is PdfInteger di ? (int)di.Value : d[0] is PdfReal dr ? (int)dr.Value : 0;
                    int off = d.Count > 1 ? (d[1] is PdfInteger oi ? (int)oi.Value : d[1] is PdfReal orr ? (int)orr.Value : on) : on;
                    border.Dash = new Dash(on, off);
                }
            }
            return border;
        }
        set => base.Border = value;
    }

    /// <summary>Regenerate the normal appearance (/AP /N) by stroking the
    /// line from <see cref="Starting"/> to <see cref="Ending"/>.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        if (r is null) return;
        var s = Starting; var e = Ending;
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        b.MoveTo(s.X, s.Y);
        b.LineTo(e.X, e.Y);
        b.Stroke();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }

    /// <summary>Regenerate the appearance of <paramref name="annotation"/>.</summary>
    public void UpdateAppearance(LineAnnotation annotation) => annotation?.UpdateAppearances();

    /// <summary>Start point of the line (/L entry, first pair).</summary>
    public Point Starting
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("L")) as PdfArray;
            if (arr is null || arr.Count < 4) return new Point(0, 0);
            return new Point(GetN(arr[0]), GetN(arr[1]));
        }
        set
        {
            var arr = InternalReader.Resolve(Dict.Get("L")) as PdfArray ?? new PdfArray();
            while (arr.Count < 4) arr.Add(new PdfReal(0));
            arr.ReplaceAt(0, new PdfReal(value.X));
            arr.ReplaceAt(1, new PdfReal(value.Y));
            Dict.Set("L", arr);
        }
    }

    /// <summary>End point of the line (/L entry, second pair).</summary>
    public Point Ending
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("L")) as PdfArray;
            if (arr is null || arr.Count < 4) return new Point(0, 0);
            return new Point(GetN(arr[2]), GetN(arr[3]));
        }
        set
        {
            var arr = InternalReader.Resolve(Dict.Get("L")) as PdfArray ?? new PdfArray();
            while (arr.Count < 4) arr.Add(new PdfReal(0));
            arr.ReplaceAt(2, new PdfReal(value.X));
            arr.ReplaceAt(3, new PdfReal(value.Y));
            Dict.Set("L", arr);
        }
    }

    /// <summary>Caption offset from its anchor (/CO entry). Default (0, 0).</summary>
    public Point CaptionOffset
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("CO")) as PdfArray;
            if (arr is null || arr.Count < 2) return new Point(0, 0);
            return new Point(GetN(arr[0]), GetN(arr[1]));
        }
        set
        {
            var arr = new PdfArray();
            arr.Add(new PdfReal(value.X));
            arr.Add(new PdfReal(value.Y));
            Dict.Set("CO", arr);
        }
    }

    /// <summary>Where the caption sits relative to the line (/CP entry).</summary>
    public CaptionPosition CaptionPosition
    {
        get => Dict.GetName("CP") switch
        {
            "Top" => CaptionPosition.Top,
            _ => CaptionPosition.Inline,
        };
        set => Dict.Set("CP", new PdfName(value == CaptionPosition.Top ? "Top" : "Inline"));
    }

    /// <summary>Interior fill colour for the line's endings (/IC entry).</summary>
    public new Color? InteriorColor
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("IC")) as PdfArray;
            if (arr is null) return null;
            if (arr.Count == 3)
                return Color.FromRgb((float)GetN(arr[0]), (float)GetN(arr[1]), (float)GetN(arr[2]));
            return null;
        }
        set
        {
            if (value is null) { Dict.Remove("IC"); return; }
            var arr = new PdfArray();
            arr.Add(new PdfReal(value.R));
            arr.Add(new PdfReal(value.G));
            arr.Add(new PdfReal(value.B));
            Dict.Set("IC", arr);
        }
    }

    /// <summary>Leader-line length perpendicular to the line (/LL entry).</summary>
    public double LeaderLine
    {
        get => GetN(InternalReader.Resolve(Dict.Get("LL")));
        set => Dict.Set("LL", new PdfReal(value));
    }

    /// <summary>Leader-line extension past the line (/LLE entry).</summary>
    public double LeaderLineExtension
    {
        get => GetN(InternalReader.Resolve(Dict.Get("LLE")));
        set => Dict.Set("LLE", new PdfReal(value));
    }

    /// <summary>Leader-line offset from the line endpoint (/LLO entry).</summary>
    public double LeaderLineOffset
    {
        get => GetN(InternalReader.Resolve(Dict.Get("LLO")));
        set => Dict.Set("LLO", new PdfReal(value));
    }

    /// <summary>Whether the line's caption is shown (/Cap entry).</summary>
    public bool ShowCaption
    {
        get => Dict.Get("Cap") is PdfBoolean b && b.Value;
        set => Dict.Set("Cap", value ? PdfBoolean.True : PdfBoolean.False);
    }

    private Measure? _measure;

    /// <summary>Measure-units metadata (/Measure entry). Lazy-constructed
    /// so callers can mutate properties without setting a fresh instance.</summary>
    public Measure Measure
    {
        get => _measure ??= new Measure(this);
        set => _measure = value;
    }

    /// <summary>Apply a transform to the line's start/end points after the
    /// page or container was resized. Updates /L plus the /Rect bbox to
    /// match.</summary>
    public new void ChangeAfterResize(Matrix transform)
    {
        if (transform is null) return;
        var start = Starting;
        var end = Ending;
        transform.Transform(start.X, start.Y, out var sx, out var sy);
        transform.Transform(end.X, end.Y, out var ex, out var ey);
        Starting = new Point(sx, sy);
        Ending = new Point(ex, ey);
        Rect = new Rectangle(
            System.Math.Min(sx, ex),
            System.Math.Min(sy, ey),
            System.Math.Max(sx, ex),
            System.Math.Max(sy, ey));
    }

    private static double GetN(PdfObject? obj) => obj switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    private static Rectangle RectFromPoints(Point a, Point b)
    {
        var llx = Math.Min(a.X, b.X);
        var lly = Math.Min(a.Y, b.Y);
        var urx = Math.Max(a.X, b.X);
        var ury = Math.Max(a.Y, b.Y);
        return new Rectangle(llx, lly, urx, ury);
    }

    public LineIntent Intent
    {
        get => ParseLineIntent(Dict.GetName("IT"));
        set
        {
            var name = value switch
            {
                LineIntent.LineArrow => "LineArrow",
                LineIntent.LineDimension => "LineDimension",
                _ => null,
            };
            if (name is null) Dict.Remove("IT");
            else Dict.Set("IT", new PdfName(name));
        }
    }

    public LineEnding StartingStyle
    {
        get => GetLineEnding(0);
        set => SetLineEnding(0, value);
    }

    public LineEnding EndingStyle
    {
        get => GetLineEnding(1);
        set => SetLineEnding(1, value);
    }

    private LineEnding GetLineEnding(int index)
    {
        if (InternalReader.Resolve(Dict.Get("LE")) is not PdfArray arr || arr.Count <= index)
            return LineEnding.None;
        var name = (InternalReader.Resolve(arr[index]) as PdfName)?.Value;
        return ParseLineEnding(name);
    }

    private void SetLineEnding(int index, LineEnding value)
    {
        var arr = InternalReader.Resolve(Dict.Get("LE")) as PdfArray;
        if (arr is null || arr.Count < 2)
        {
            arr = new PdfArray();
            arr.Add(new PdfName("None"));
            arr.Add(new PdfName("None"));
        }
        var newArr = new PdfArray();
        for (int i = 0; i < 2; i++)
        {
            if (i == index)
            {
                newArr.Add(new PdfName(LineEndingToName(value)));
            }
            else
            {
                var existing = i < arr.Count ? InternalReader.Resolve(arr[i]) : null;
                var name = (existing as PdfName)?.Value ?? "None";
                newArr.Add(new PdfName(name));
            }
        }
        Dict.Set("LE", newArr);
    }

    private static string LineEndingToName(LineEnding le) => le switch
    {
        LineEnding.Square => "Square",
        LineEnding.Circle => "Circle",
        LineEnding.Diamond => "Diamond",
        LineEnding.OpenArrow => "OpenArrow",
        LineEnding.ClosedArrow => "ClosedArrow",
        LineEnding.Butt => "Butt",
        LineEnding.ROpenArrow => "ROpenArrow",
        LineEnding.RClosedArrow => "RClosedArrow",
        LineEnding.Slash => "Slash",
        _ => "None",
    };

    private static LineEnding ParseLineEnding(string? name) => name switch
    {
        "Square" => LineEnding.Square,
        "Circle" => LineEnding.Circle,
        "Diamond" => LineEnding.Diamond,
        "OpenArrow" => LineEnding.OpenArrow,
        "ClosedArrow" => LineEnding.ClosedArrow,
        "Butt" => LineEnding.Butt,
        "ROpenArrow" => LineEnding.ROpenArrow,
        "RClosedArrow" => LineEnding.RClosedArrow,
        "Slash" => LineEnding.Slash,
        _ => LineEnding.None,
    };

    private static LineIntent ParseLineIntent(string? name) => name switch
    {
        "LineArrow" => LineIntent.LineArrow,
        "LineDimension" => LineIntent.LineDimension,
        _ => LineIntent.Undefined,
    };
}

/// <summary>
/// Line annotation ending styles (/LE entry elements).
/// </summary>
public enum LineEnding
{
    None,
    Square,
    Circle,
    Diamond,
    OpenArrow,
    ClosedArrow,
    Butt,
    ROpenArrow,
    RClosedArrow,
    Slash,
}

/// <summary>
/// Line annotation intents (/IT entry).
/// </summary>
public enum LineIntent
{
    Undefined,
    LineArrow,
    LineDimension,
}

/// <summary>Common base for square and circle annotations — a figure drawn
/// inside a rectangle, optionally inset by /RD (PDF 32000 §12.5.6.8).</summary>
public abstract partial class CommonFigureAnnotation : MarkupAnnotation
{
    internal CommonFigureAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected CommonFigureAnnotation(Page page, Rectangle rect) : base(page, rect) { }
    protected CommonFigureAnnotation(Document document, Rectangle rect) : base(document, rect) { }

    /// <summary>The drawn figure rectangle — the annotation rectangle inset by
    /// the /RD (rectangle differences) entry. Equal to <see cref="Annotation.Rect"/>
    /// when /RD is absent.</summary>
    public Rectangle Frame
    {
        get
        {
            var r = Rect ?? new Rectangle(0, 0, 0, 0);
            var rd = InternalReader.Resolve(Dict.Get("RD")) as PdfArray;
            if (rd is null || rd.Count < 4) return new Rectangle(r.LLX, r.LLY, r.URX, r.URY);
            double left = N(rd[0]), top = N(rd[1]), right = N(rd[2]), bottom = N(rd[3]);
            return new Rectangle(r.LLX + left, r.LLY + bottom, r.URX - right, r.URY - top);
        }
        set
        {
            var r = Rect;
            if (value is null || r is null) { Dict.Remove("RD"); return; }
            var rd = new PdfArray();
            rd.Add(new PdfReal(value.LLX - r.LLX)); // left
            rd.Add(new PdfReal(r.URY - value.URY)); // top
            rd.Add(new PdfReal(r.URX - value.URX)); // right
            rd.Add(new PdfReal(value.LLY - r.LLY)); // bottom
            Dict.Set("RD", rd);
        }
    }

    private static double N(PdfObject o) => o is PdfReal r ? r.Value : o is PdfInteger i ? i.Value : 0;

    /// <summary>Generate the normal appearance for a Square or Circle annotation
    /// (PDF 32000 §12.5.6.8): stroke the figure with the border colour, width and
    /// dash from /BS, optionally fill the interior with /IC. The figure is inset by
    /// half the border width so the stroke stays within the annotation rectangle.</summary>
    public override void UpdateAppearances()
    {
        var rect = Rect;
        if (rect is null) return;
        var frame = Frame;
        if (frame.Width <= 0 || frame.Height <= 0) return;

        // Border width and dash pattern from /BS (the modern border-style dict),
        // falling back to the legacy /Border array's third element for the width.
        double bw = -1;
        double[]? dash = null;
        var bs = InternalReader.ResolveDict(Dict.Get("BS"));
        if (bs is not null)
        {
            if (bs.Get("W") is PdfReal wr) bw = wr.Value;
            else if (bs.Get("W") is PdfInteger wi) bw = wi.Value;
            if (bs.GetName("S") == "D" && InternalReader.Resolve(bs.Get("D")) is PdfArray da && da.Count > 0)
            {
                dash = new double[da.Count];
                for (var i = 0; i < da.Count; i++) dash[i] = N(da[i]);
            }
        }
        if (bw < 0 && InternalReader.Resolve(Dict.Get("Border")) is PdfArray bd && bd.Count >= 3)
            bw = N(bd[2]);
        if (bw < 0) bw = 1.0; // neither /BS nor /Border specified a width

        var stroke = Color;
        var fill = InteriorColor;

        // Nothing visible (no border colour with a non-zero width, no interior fill):
        // leave /AP absent so the figure stays invisible, matching a viewer that paints
        // a Square/Circle only when it has a colour. Squares used purely as text anchors
        // (/Border [0 0 0], no /C) must not sprout an opaque outline on flatten.
        bool doStroke = stroke is not null && bw > 0;
        bool doFill = fill is not null;
        if (!doStroke && !doFill) return;

        // Inset by half the line width; if that collapses the figure, stroke the frame as-is.
        double half = bw / 2.0;
        double x = frame.LLX + half, y = frame.LLY + half, w = frame.Width - bw, h = frame.Height - bw;
        if (w <= 0 || h <= 0) { x = frame.LLX; y = frame.LLY; w = frame.Width; h = frame.Height; }

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        if (doFill) b.SetFillColor(fill!);
        if (doStroke)
        {
            b.SetStrokeColor(stroke!);
            b.SetLineWidth(bw);
            if (dash is not null) { b.SetLineCap(1); b.SetDashPattern(dash); }
        }

        if (Dict.GetName("Subtype") == "Circle")
        {
            // Ellipse approximated by four cubic Béziers (kappa = 4/3·(√2−1)).
            const double k = 0.5522847498;
            double cx = x + w / 2, cy = y + h / 2, rx = w / 2, ry = h / 2;
            b.MoveTo(cx + rx, cy);
            b.CurveTo(cx + rx, cy + ry * k, cx + rx * k, cy + ry, cx, cy + ry);
            b.CurveTo(cx - rx * k, cy + ry, cx - rx, cy + ry * k, cx - rx, cy);
            b.CurveTo(cx - rx, cy - ry * k, cx - rx * k, cy - ry, cx, cy - ry);
            b.CurveTo(cx + rx * k, cy - ry, cx + rx, cy - ry * k, cx + rx, cy);
            b.ClosePath();
        }
        else
        {
            b.Rectangle(x, y, w, h);
        }

        if (doFill && doStroke) b.FillAndStroke();
        else if (doFill) b.Fill();
        else b.Stroke();
        b.RestoreState();
        SetNormalAppearance(b.Build(), rect);
    }
}

public partial class SquareAnnotation : CommonFigureAnnotation
{
    internal SquareAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public SquareAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Square"));
    }

    public SquareAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Square"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Square;
}

public partial class CircleAnnotation : CommonFigureAnnotation
{
    internal CircleAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public CircleAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Circle"));
    }

    public CircleAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Circle"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Circle;
}

/// <summary>Intent of a polygon or polyline annotation (/IT entry).</summary>
public enum PolyIntent
{
    /// <summary>Intent is missing or undefined.</summary>
    Undefined,
    /// <summary>Cloud-shaped polygon (PolygonCloud).</summary>
    PolygonCloud,
    /// <summary>Polygon used as a dimension (PolygonDimension).</summary>
    PolygonDimension,
    /// <summary>Polyline used as a dimension (PolyLineDimension).</summary>
    PolyLineDimension,
}

/// <summary>Common base for polygon and polyline annotations — a chain of
/// connected vertices (PDF 32000 §12.5.6.9).</summary>
public abstract partial class PolyAnnotation : MarkupAnnotation
{
    internal PolyAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected PolyAnnotation(Page page, Rectangle rect) : base(page, rect) { }
    protected PolyAnnotation(Document document, Rectangle rect) : base(document, rect) { }

    /// <summary>The vertices of the path (/Vertices entry).</summary>
    public Point[] Vertices
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("Vertices")) as PdfArray;
            if (arr is null) return System.Array.Empty<Point>();
            var pts = new Point[arr.Count / 2];
            for (int i = 0; i + 1 < arr.Count; i += 2)
            {
                double x = arr[i] is PdfReal rx ? rx.Value : arr[i] is PdfInteger ix ? ix.Value : 0;
                double y = arr[i + 1] is PdfReal ry ? ry.Value : arr[i + 1] is PdfInteger iy ? iy.Value : 0;
                pts[i / 2] = new Point(x, y);
            }
            return pts;
        }
        set
        {
            var arr = new PdfArray();
            if (value is not null)
                foreach (var p in value) { arr.Add(new PdfReal(p.X)); arr.Add(new PdfReal(p.Y)); }
            Dict.Set("Vertices", arr);
        }
    }

    /// <summary>The intent of the annotation (/IT entry).</summary>
    public PolyIntent Intent
    {
        get => Dict.GetName("IT") switch
        {
            "PolygonCloud" => PolyIntent.PolygonCloud,
            "PolygonDimension" => PolyIntent.PolygonDimension,
            "PolyLineDimension" => PolyIntent.PolyLineDimension,
            _ => PolyIntent.Undefined,
        };
        set
        {
            if (value == PolyIntent.Undefined) Dict.Remove("IT");
            else Dict.Set("IT", new PdfName(value.ToString()));
        }
    }

    /// <summary>Regenerate the normal appearance (/AP /N) by stroking the
    /// vertex path (and filling it with <see cref="Annotation.InteriorColor"/>
    /// for a closed polygon).</summary>
    public override void UpdateAppearances()
    {
        var verts = Vertices;
        var r = Rect;
        if (verts.Length == 0 || r is null) { base.UpdateAppearances(); return; }
        bool polygon = Dict.GetName("Subtype") == "Polygon";
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        var ic = InteriorColor;
        if (polygon && ic is not null) b.SetFillColor(ic);
        b.MoveTo(verts[0].X, verts[0].Y);
        for (int i = 1; i < verts.Length; i++) b.LineTo(verts[i].X, verts[i].Y);
        if (polygon)
        {
            b.ClosePath();
            if (ic is not null) b.FillAndStroke(); else b.Stroke();
        }
        else b.Stroke();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }

    private protected static Rectangle BoundingRect(Point[] vertices)
    {
        if (vertices is null || vertices.Length == 0) return new Rectangle(0, 0, 0, 0);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var p in vertices)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }
        return new Rectangle(minX, minY, maxX, maxY);
    }
}

public partial class PolygonAnnotation : PolyAnnotation
{
    internal PolygonAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public PolygonAnnotation(Page page, Rectangle rect, Point[] vertices) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Polygon"));
        Vertices = vertices;
    }

    public PolygonAnnotation(Document document, Point[] vertices) : base(document, BoundingRect(vertices))
    {
        Dict.Set("Subtype", new PdfName("Polygon"));
        Vertices = vertices;
    }

    public new AnnotationType AnnotationType => AnnotationType.Polygon;
}

public partial class PolylineAnnotation : PolyAnnotation
{
    internal PolylineAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public PolylineAnnotation(Page page, Rectangle rect, Point[] vertices) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("PolyLine"));
        Vertices = vertices;
    }

    public new AnnotationType AnnotationType => AnnotationType.PolyLine;

    /// <summary>Border with width, style and dash pattern resolved from /BS.</summary>
    public new Border? Border
    {
        get
        {
            var border = new Border(this);
            var bs = InternalReader.ResolveDict(Dict.Get("BS"));
            if (bs is not null)
            {
                var w = InternalReader.Resolve(bs.Get("W"));
                if (w is PdfInteger wi) border.Width = (int)wi.Value;
                else if (w is PdfReal wr) border.Width = (int)wr.Value;
                border.Style = bs.GetName("S") switch
                {
                    "D" => BorderStyle.Dashed,
                    "B" => BorderStyle.Beveled,
                    "I" => BorderStyle.Inset,
                    "U" => BorderStyle.Underline,
                    _ => BorderStyle.Solid,
                };
                if (InternalReader.Resolve(bs.Get("D")) is PdfArray d && d.Count > 0)
                {
                    int on = d[0] is PdfInteger di ? (int)di.Value : d[0] is PdfReal dr ? (int)dr.Value : 0;
                    int off = d.Count > 1 ? (d[1] is PdfInteger oi ? (int)oi.Value : d[1] is PdfReal orr ? (int)orr.Value : on) : on;
                    border.Dash = new Dash(on, off);
                }
            }
            return border;
        }
        set => base.Border = value;
    }
}

/// <summary>Caret-symbol style for <see cref="CaretAnnotation"/>.</summary>
public enum CaretSymbol
{
    /// <summary>No symbol.</summary>
    None = 0,
    /// <summary>Pilcrow / paragraph-mark symbol.</summary>
    Paragraph = 1,
}

public partial class CaretAnnotation : MarkupAnnotation
{
    internal CaretAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public CaretAnnotation(Document document) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Subtype", new PdfName("Caret"));
    }

    public CaretAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Caret"));
    }

    public new AnnotationType AnnotationType => AnnotationType.Caret;

    /// <summary>The caret rectangle inset by the /RD entry; equals
    /// <see cref="Annotation.Rect"/> when /RD is absent.</summary>
    public Rectangle Frame
    {
        get
        {
            var r = Rect ?? new Rectangle(0, 0, 0, 0);
            var rd = InternalReader.Resolve(Dict.Get("RD")) as PdfArray;
            if (rd is null || rd.Count < 4) return new Rectangle(r.LLX, r.LLY, r.URX, r.URY);
            double left = G(rd[0]), top = G(rd[1]), right = G(rd[2]), bottom = G(rd[3]);
            return new Rectangle(r.LLX + left, r.LLY + bottom, r.URX - right, r.URY - top);
        }
        set
        {
            var r = Rect;
            if (value is null || r is null) { Dict.Remove("RD"); return; }
            var rd = new PdfArray();
            rd.Add(new PdfReal(value.LLX - r.LLX));
            rd.Add(new PdfReal(r.URY - value.URY));
            rd.Add(new PdfReal(r.URX - value.URX));
            rd.Add(new PdfReal(value.LLY - r.LLY));
            Dict.Set("RD", rd);
        }
    }

    /// <summary>Caret symbol style (/Sy entry).</summary>
    public CaretSymbol Symbol
    {
        get => Dict.GetName("Sy") == "P" ? CaretSymbol.Paragraph : CaretSymbol.None;
        set
        {
            if (value == CaretSymbol.Paragraph) Dict.Set("Sy", new PdfName("P"));
            else Dict.Remove("Sy");
        }
    }

    private static double G(PdfObject o) => o is PdfReal r ? r.Value : o is PdfInteger i ? i.Value : 0;
}

public partial class SoundAnnotation : MarkupAnnotation
{
    internal SoundAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader)
    {
        _soundData = ParseSoundData(dict, reader);
        Icon = ParseIcon(dict);
    }

    public SoundAnnotation(Page page, Rectangle rect, string soundFile) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Sound"));
        WriteIcon(SoundIcon.Speaker);
        _soundData = LoadAudioBytes(soundFile);
        AttachSoundStream(_soundData);
    }

    public SoundAnnotation(Page page, Rectangle rect, string soundFile, SoundSampleData soundSampleData)
        : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Sound"));
        WriteIcon(SoundIcon.Speaker);
        _soundData = LoadAudioBytes(soundFile);
        if (soundSampleData != null)
        {
            _soundData.Rate = (int)soundSampleData.SamplingRate;
            _soundData.Channels = soundSampleData.NumberOfSoundChannels;
            _soundData.Bits = soundSampleData.BitsPerChannel;
            _soundData.Encoding = soundSampleData.EncodingFormat switch
            {
                SoundSampleDataEncodingFormat.ALaw => SoundEncoding.ALaw,
                SoundSampleDataEncodingFormat.muLaw => SoundEncoding.MuLaw,
                SoundSampleDataEncodingFormat.Signed => SoundEncoding.Signed,
                _ => SoundEncoding.Raw,
            };
        }
        AttachSoundStream(_soundData);
    }

    public new AnnotationType AnnotationType => AnnotationType.Sound;

    private SoundIcon _icon = SoundIcon.Speaker;
    public SoundIcon Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            WriteIcon(value);
        }
    }

    private SoundData _soundData;
    public SoundData SoundData => _soundData;

    // ── Wire to /Sound stream (PDF 32000-1 §12.5.6.16, Table 175) ──

    private void WriteIcon(SoundIcon icon)
    {
        // /Name entry: "Speaker" (default) or "Mic" — Adobe-defined values.
        Dict.Set("Name", new PdfName(icon switch
        {
            SoundIcon.Mic => "Mic",
            _ => "Speaker",
        }));
    }

    private static SoundIcon ParseIcon(PdfDictionary dict)
    {
        var name = dict.GetName("Name");
        return name == "Mic" ? SoundIcon.Mic : SoundIcon.Speaker;
    }

    /// <summary>Build the /Sound stream from <paramref name="data"/> and
    /// attach it to this annotation's dictionary. The stream carries the
    /// audio bytes verbatim plus R/B/C/E sampling parameters per
    /// PDF 32000-1 Table 175. Round-tripped on save → load.</summary>
    private void AttachSoundStream(SoundData data)
    {
        var soundDict = new PdfDictionary();
        soundDict.Set("Type", new PdfName("Sound"));
        soundDict.Set("R", new PdfReal(data.Rate));
        soundDict.Set("C", new PdfInteger(data.Channels));
        soundDict.Set("B", new PdfInteger(data.Bits));
        soundDict.Set("E", new PdfName(SoundEncodingToPdfName(data.Encoding)));
        var bytes = ReadAllBytes(data.Contents);
        var stream = new PdfStream(soundDict, bytes);
        Dict.Set("Sound", stream);
    }

    private SoundData ParseSoundData(PdfDictionary dict, PdfReader reader)
    {
        var sd = new SoundData();
        var soundObj = reader.Resolve(dict.Get("Sound"));
        if (soundObj is not PdfStream stream) return sd;
        var sDict = stream.Dict;
        sd.Rate = (int)(reader.Resolve(sDict.Get("R")) switch
        {
            PdfReal r => r.Value,
            PdfInteger n => n.Value,
            _ => 11025.0,
        });
        sd.Channels = sDict.Get("C") is PdfInteger c ? (int)c.Value : 1;
        sd.Bits = sDict.Get("B") is PdfInteger b ? (int)b.Value : 8;
        sd.Encoding = sDict.GetName("E") switch
        {
            "Raw" => SoundEncoding.Raw,
            "Signed" => SoundEncoding.Signed,
            "muLaw" => SoundEncoding.MuLaw,
            "ALaw" => SoundEncoding.ALaw,
            _ => SoundEncoding.Raw,
        };
        sd.SetContents(reader.DecodeStream(stream));
        return sd;
    }

    private static string SoundEncodingToPdfName(SoundEncoding enc) => enc switch
    {
        SoundEncoding.Signed => "Signed",
        SoundEncoding.MuLaw => "muLaw",
        SoundEncoding.ALaw => "ALaw",
        _ => "Raw",
    };

    private static SoundData LoadAudioBytes(string soundFile)
    {
        var sd = new SoundData();
        if (!string.IsNullOrEmpty(soundFile) && System.IO.File.Exists(soundFile))
        {
            try
            {
                sd.SetContents(System.IO.File.ReadAllBytes(soundFile));
            }
            catch { }
        }
        return sd;
    }

    private static byte[] ReadAllBytes(System.IO.Stream s)
    {
        if (s is null) return System.Array.Empty<byte>();
        using var ms = new System.IO.MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Sound annotations have no typed Visit overload in the
    /// Aspose.PDF for .NET visitor, so Accept here is a no-op kept for reflection
    /// parity.</summary>
    public override void Accept(AnnotationSelector visitor) { _ = visitor; }
}

public partial class MovieAnnotation : Annotation
{
    internal MovieAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public MovieAnnotation(Document document, string movieFile) : base(document, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Movie"));
        File = new FileSpecification(movieFile);
    }

    public MovieAnnotation(Page page, Rectangle rect, string movieFile) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Movie"));
        File = new FileSpecification(movieFile);
    }

    public new AnnotationType AnnotationType => AnnotationType.Movie;

    /// <summary>Display aspect ratio (width × height in points). Stored only.</summary>
    public Aspose.Pdf.Point? Aspect { get; set; }

    /// <summary>The /F (file specification) entry — points at the embedded movie data.</summary>
    public FileSpecification? File { get; set; }

    /// <summary>True when the annotation should display a poster image when not playing.</summary>
    public bool Poster { get; set; }

    /// <summary>Rotation in degrees applied to the movie's playback area.</summary>
    public int Rotate { get; set; }

    /// <summary>Movie title displayed in the player chrome.</summary>
    public new string? Title { get; set; }
}

public partial class ScreenAnnotation : Annotation
{
    internal ScreenAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a screen annotation referencing <paramref name="mediaFile"/>.</summary>
    public ScreenAnnotation(Page page, Rectangle rect, string mediaFile) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Screen"));
        if (!string.IsNullOrEmpty(mediaFile))
            Dict.Set("FS", new FileSpecification(mediaFile).Dict);
    }

    /// <summary>Always <see cref="AnnotationType.Screen"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Screen;

    /// <summary>Render-launch action (/A entry), or null when none is set.</summary>
    public new PdfAction? Action
    {
        get
        {
            var aDict = InternalReader.ResolveDict(Dict.Get("A"));
            return aDict is null ? null : PdfAction.Create(aDict, InternalReader);
        }
    }

    /// <summary>Title/label shown in the viewer chrome (/T entry).</summary>
    public new string? Title
    {
        get => (InternalReader.Resolve(Dict.Get("T")) as PdfString)?.ToText();
        set
        {
            if (value is null) Dict.Remove("T");
            else Dict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }
}

public partial class RichMediaAnnotation : Annotation
{
    internal RichMediaAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public RichMediaAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("RichMedia"));
    }

    public new AnnotationType AnnotationType => AnnotationType.RichMedia;

    public ContentType Type { get; set; } = ContentType.Unknown;
    public ActivationEvent ActivateOn { get; set; } = ActivationEvent.Click;
    public string? CustomFlashVariables { get; set; }

    private byte[]? _content;
    private byte[]? _customPlayer;
    private byte[]? _poster;
    private readonly Dictionary<string, byte[]> _customData = new();

    public System.IO.Stream? Content =>
        _content is null ? null : new System.IO.MemoryStream(_content, writable: false);

    public System.IO.Stream? CustomPlayer
    {
        get => _customPlayer is null ? null : new System.IO.MemoryStream(_customPlayer, writable: false);
        set
        {
            if (value is null) { _customPlayer = null; return; }
            using var ms = new System.IO.MemoryStream();
            value.CopyTo(ms);
            _customPlayer = ms.ToArray();
        }
    }

    public void SetContent(string fileName, System.IO.Stream audio)
    {
        if (audio is null) throw new ArgumentNullException(nameof(audio));
        using var ms = new System.IO.MemoryStream();
        audio.CopyTo(ms);
        _content = ms.ToArray();
        var ext = System.IO.Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        Type = ext switch
        {
            ".mp3" or ".wav" or ".m4a" or ".ogg" or ".aac" => ContentType.Audio,
            ".mp4" or ".mov" or ".avi" or ".webm" or ".mkv" => ContentType.Video,
            _ => ContentType.Unknown,
        };
    }

    public void SetPoster(System.IO.Stream imageStream)
    {
        if (imageStream is null) { _poster = null; return; }
        using var ms = new System.IO.MemoryStream();
        imageStream.CopyTo(ms);
        _poster = ms.ToArray();
    }

    public void AddCustomData(string name, System.IO.Stream data)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (data is null) throw new ArgumentNullException(nameof(data));
        using var ms = new System.IO.MemoryStream();
        data.CopyTo(ms);
        _customData[name] = ms.ToArray();
    }

    /// <summary>Re-emit appearance from stored buffers. The FOSS layer keeps the
    /// media as opaque bytes; no /AP stream is written so this is a no-op.</summary>
    public void Update() { }

    public enum ActivationEvent
    {
        Click = 0,
        PageOpen = 1,
        PageVisible = 2,
    }

    public enum ContentType
    {
        Unknown = 0,
        Audio = 1,
        Video = 2,
    }
}

