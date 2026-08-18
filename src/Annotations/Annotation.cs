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

    /// <summary>Detached ctor for a document-less annotation used purely as a
    /// configuration holder (e.g. a generator <see cref="Forms.RadioButtonOptionField"/>
    /// before it is placed into a real form). Backed by a fresh empty dict and the
    /// shared empty reader; the object carries no page until it is added.</summary>
    protected Annotation()
    {
        _dict = new PdfDictionary();
        _reader = PdfReader.Empty;
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
            OnRectChanged();
        }
    }

    /// <summary>Hook invoked after the <see cref="Rect"/> setter writes a new rectangle.
    /// The base is a no-op; form fields override it to regenerate their /AP appearance so
    /// the value re-lays-out inside the resized widget box.</summary>
    internal virtual void OnRectChanged() { }

    /// <summary>The annotation rectangle inset by the /RD (rectangle differences) entry —
    /// the region actually drawn inside <see cref="Rect"/>. /RD is [dLLX dLLY dURX dURY],
    /// each the distance from the corresponding <see cref="Rect"/> edge inward. Equals
    /// <see cref="Rect"/> when no /RD is present.</summary>
    public Rectangle? InnerRect
    {
        get
        {
            if (Rect is not { } r) return null;
            if (_reader.Resolve(_dict.Get("RD")) is PdfArray rd && rd.Count >= 4)
            {
                double N(int i) => _reader.Resolve(rd[i]) switch
                {
                    PdfReal pr => pr.Value,
                    PdfInteger pi => pi.Value,
                    _ => 0,
                };
                return new Rectangle(r.LLX + N(0), r.LLY + N(1), r.URX - N(2), r.URY - N(3));
            }
            return r;
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

    /// <summary>Horizontal alignment (exposed in addition
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
                EnsureWidgetAppearanceDaFont(stream);
                var form = new XForm(stream, _reader);
                _appearance[key] = form;
                // A direct /AP/N (or /D, /R) stream is itself the appearance for that
                // key — expose it under the bare key in States too, so callers can read
                // e.g. field.States["N"] on a plain text/push-button field (not just on
                // state-keyed checkbox/radio widgets handled below).
                _states[key] = form;
            }
            else if (obj is Core.PdfDictionary stateDict)
            {
                foreach (var stateKey in stateDict.Keys)
                {
                    if (_reader.Resolve(stateDict.Get(stateKey)) is Core.PdfStream stStream)
                    {
                        EnsureWidgetAppearanceDaFont(stStream);
                        var form = new XForm(stStream, _reader);
                        _appearance[key + "." + stateKey] = form;
                        _states[stateKey] = form;
                    }
                }
            }
        }
    }

    /// <summary>Acrobat's /DA short font aliases mapped to their Standard-14 PostScript
    /// base font (PDF 32000 §12.7.3.3). Used to declare a synthesised appearance font when
    /// a widget /DA names one of these but the appearance stream doesn't declare it.</summary>
    private static readonly Dictionary<string, string> StandardDaAlias = new(StringComparer.Ordinal)
    {
        ["Helv"] = "Helvetica", ["HeBo"] = "Helvetica-Bold", ["HeOb"] = "Helvetica-Oblique", ["HeBO"] = "Helvetica-BoldOblique",
        ["TiRo"] = "Times-Roman", ["TiBo"] = "Times-Bold", ["TiIt"] = "Times-Italic", ["TiBI"] = "Times-BoldItalic",
        ["Cour"] = "Courier", ["CoBo"] = "Courier-Bold", ["CoOb"] = "Courier-Oblique", ["CoBO"] = "Courier-BoldOblique",
        ["ZaDb"] = "ZapfDingbats", ["Symb"] = "Symbol",
    };

    /// <summary>A widget's /AP appearance paints its value with the font named in the field
    /// /DA (e.g. <c>/MyriadPro-Regular 10 Tf</c>), but many authoring tools leave that font
    /// undeclared in the appearance stream's own <c>/Resources/Font</c> — it lives only in the
    /// AcroForm <c>/DR</c>. Declare it on the appearance stream so the appearance is
    /// self-contained and the /DA font is discoverable via <c>GetResources().Fonts[name]</c>
    /// (matching Acrobat). A Standard-14 /DA alias (Helv, ZaDb, Cour…) is
    /// synthesised to its PostScript base font; any other name is pulled — as a shared
    /// indirect reference, never cloned — from the AcroForm <c>/DR/Font</c>. Existing entries
    /// are left untouched, so this only ever fills a gap.</summary>
    private void EnsureWidgetAppearanceDaFont(Core.PdfStream stream)
    {
        if (_dict.GetName("Subtype") != "Widget") return;

        var daStr = ResolveInheritedDa();
        if (string.IsNullOrEmpty(daStr)) return;
        var fontName = ParseDaFontToken(daStr!);
        if (string.IsNullOrEmpty(fontName)) return;

        var res = _reader.ResolveDict(stream.Dict.Get("Resources"));
        if (res is null) { res = new PdfDictionary(); stream.Dict.Set("Resources", res); }
        var fonts = _reader.ResolveDict(res.Get("Font"));
        if (fonts is null) { fonts = new PdfDictionary(); res.Set("Font", fonts); }
        if (fonts.ContainsKey(fontName!)) return;

        if (StandardDaAlias.TryGetValue(fontName!, out var baseFont))
        {
            var f = new PdfDictionary();
            f.Set("Type", new PdfName("Font"));
            f.Set("Subtype", new PdfName("Type1"));
            f.Set("BaseFont", new PdfName(baseFont));
            f.Set("Encoding", new PdfName("WinAnsiEncoding"));
            fonts.Set(fontName!, f);
            return;
        }

        var drFont = AcroFormDrFont(fontName!);
        if (drFont is not null) fonts.Set(fontName!, drFont);
    }

    /// <summary>The effective /DA for this widget: its own, else inherited from an AcroForm
    /// field /Parent chain, else the AcroForm-wide default. Null when none applies.</summary>
    private string? ResolveInheritedDa()
    {
        var da = (_reader.Resolve(_dict.Get("DA")) as PdfString)?.ToText();
        if (!string.IsNullOrEmpty(da)) return da;

        var seen = new HashSet<int>();
        var parentObj = _dict.Get("Parent");
        while (parentObj is not null)
        {
            if (parentObj is PdfIndirectRef r && !seen.Add(r.ObjectNumber)) break;
            var parent = _reader.ResolveDict(parentObj);
            if (parent is null) break;
            da = (_reader.Resolve(parent.Get("DA")) as PdfString)?.ToText();
            if (!string.IsNullOrEmpty(da)) return da;
            parentObj = parent.Get("Parent");
        }

        try
        {
            var acro = _reader.ResolveDict(_reader.Catalog?.Get("AcroForm"));
            return (_reader.Resolve(acro?.Get("DA")) as PdfString)?.ToText();
        }
        catch { return null; }
    }

    /// <summary>Extract the font resource name (the token before <c>Tf</c>, sans leading
    /// slash) from a /DA string. Empty when the string has no <c>… Tf</c> operator.</summary>
    private static string ParseDaFontToken(string da)
    {
        var p = da.Split(new[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < p.Length; i++)
            if (p[i] == "Tf" && i >= 2)
                return p[i - 2].TrimStart('/');
        return string.Empty;
    }

    /// <summary>The AcroForm <c>/DR/Font</c> entry for <paramref name="name"/> as the raw
    /// stored object (a shared indirect reference, preserved so the appearance points at the
    /// same font object rather than a clone). Null when absent.</summary>
    private PdfObject? AcroFormDrFont(string name)
    {
        try
        {
            var acro = _reader.ResolveDict(_reader.Catalog?.Get("AcroForm"));
            var dr = _reader.ResolveDict(acro?.Get("DR"));
            var fonts = _reader.ResolveDict(dr?.Get("Font"));
            return fonts?.Get(name);
        }
        catch { return null; }
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
    /// into a converted document. A global flag (static), set via
    /// <c>Annotation.UpdateAppearanceOnConvert</c> / <c>Field.UpdateAppearanceOnConvert</c>.
    /// Stored only.</summary>
    public static bool UpdateAppearanceOnConvert { get; set; } = true;

    /// <summary>Whether the embedded font (if any) should be subsetted.
    /// Static global toggle; stored only — the
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

    /// <summary>Read the border width as an int for the write-through <see cref="Border.Width"/>
    /// accessor: /BS /W, else /Border[2], else the default 1.</summary>
    internal int GetBorderWidthValue()
    {
        var bs = _reader.ResolveDict(_dict.Get("BS"));
        if (bs?.Get("W") is PdfInteger bi) return (int)bi.Value;
        if (bs?.Get("W") is PdfReal br) return (int)System.Math.Round(br.Value);
        if (_reader.Resolve(_dict.Get("Border")) is PdfArray arr && arr.Count >= 3)
        {
            if (arr[2] is PdfInteger ai) return (int)ai.Value;
            if (arr[2] is PdfReal ar) return (int)System.Math.Round(ar.Value);
        }
        return 1;
    }

    /// <summary>Persist the border width to both /Border ([0 0 W]) and /BS (/W) so a later read
    /// or appearance generation sees the explicit value — including an explicit 0.</summary>
    internal void SetBorderWidthValue(int width)
    {
        var arr = new PdfArray();
        arr.Add(new PdfInteger(0)); arr.Add(new PdfInteger(0)); arr.Add(new PdfInteger(width));
        _dict.Set("Border", arr);
        var bs = _reader.ResolveDict(_dict.Get("BS")) ?? new PdfDictionary();
        bs.Set("W", new PdfInteger(width));
        _dict.Set("BS", bs);
    }

    /// <summary>Read the border style (/BS /S) for the write-through
    /// <see cref="Border.Style"/> accessor. Defaults to Solid.</summary>
    internal Aspose.Pdf.Annotations.BorderStyle GetBorderStyleValue()
    {
        var bs = _reader.ResolveDict(_dict.Get("BS"));
        return bs?.GetName("S") switch
        {
            "D" => Aspose.Pdf.Annotations.BorderStyle.Dashed,
            "B" => Aspose.Pdf.Annotations.BorderStyle.Beveled,
            "I" => Aspose.Pdf.Annotations.BorderStyle.Inset,
            "U" => Aspose.Pdf.Annotations.BorderStyle.Underline,
            _ => Aspose.Pdf.Annotations.BorderStyle.Solid,
        };
    }

    /// <summary>Persist the border style to /BS /S for the write-through
    /// <see cref="Border.Style"/> accessor.</summary>
    internal void SetBorderStyleValue(Aspose.Pdf.Annotations.BorderStyle style)
    {
        var bs = _reader.ResolveDict(_dict.Get("BS")) ?? new PdfDictionary();
        bs.Set("S", new PdfName(style switch
        {
            Aspose.Pdf.Annotations.BorderStyle.Dashed => "D",
            Aspose.Pdf.Annotations.BorderStyle.Beveled => "B",
            Aspose.Pdf.Annotations.BorderStyle.Inset => "I",
            Aspose.Pdf.Annotations.BorderStyle.Underline => "U",
            _ => "S",
        }));
        _dict.Set("BS", bs);
    }

    /// <summary>Read the border dash pattern (/BS /D) for the write-through
    /// <see cref="Border.Dash"/> accessor, or null when none.</summary>
    internal int[]? GetBorderDashValue()
    {
        var bs = _reader.ResolveDict(_dict.Get("BS"));
        if (_reader.Resolve(bs?.Get("D")) is PdfArray d && d.Count > 0)
        {
            var res = new int[d.Count];
            for (int i = 0; i < d.Count; i++)
                res[i] = d[i] switch
                {
                    PdfInteger pi => (int)pi.Value,
                    PdfReal pr => (int)System.Math.Round(pr.Value),
                    _ => 0,
                };
            return res;
        }
        return null;
    }

    /// <summary>Persist the border dash pattern to /BS /D for the write-through
    /// <see cref="Border.Dash"/> accessor (removes /D when null/empty).</summary>
    internal void SetBorderDashValue(int[]? pattern)
    {
        var bs = _reader.ResolveDict(_dict.Get("BS")) ?? new PdfDictionary();
        if (pattern is { Length: > 0 })
        {
            var d = new PdfArray();
            foreach (var seg in pattern) d.Add(new PdfInteger(seg));
            bs.Set("D", d);
        }
        else bs.Remove("D");
        _dict.Set("BS", bs);
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
            // A fully-transparent colour means "no colour": the /C entry is
            // omitted entirely rather than written as a white triple, so
            // appearance builders skip the background fill.
            if (value.AByte == 0)
            {
                _dict.Remove("C");
            }
            else
            {
                var arr = new PdfArray();
                arr.Add(new PdfReal(value.R / 255.0));
                arr.Add(new PdfReal(value.G / 255.0));
                arr.Add(new PdfReal(value.B / 255.0));
                _dict.Set("C", arr);
            }
            OnColorChanged();
        }
    }

    /// <summary>Hook invoked after the annotation's /C colour is set. The base is a
    /// no-op; FreeText overrides it to regenerate its appearance so the cached /AP
    /// reflects the new background colour.</summary>
    private protected virtual void OnColorChanged() { }

    /// <summary>Drop the cached appearance/state views so a subsequent access
    /// rebuilds from the current /AP.</summary>
    private protected void InvalidateAppearanceCache()
    {
        _appearance = null;
        _states = null;
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

    /// <summary>Install an EMPTY normal appearance (/AP /N with a no-op content
    /// stream). Keeps the annotation invisible even for subtypes the renderer
    /// would otherwise give a default appearance and stops the save-time
    /// appearance materialiser (which only fills a missing /AP) from painting
    /// one — flow-placed Circle/Text figures render as blank blocks.</summary>
    internal void SetEmptyAppearance()
    {
        var r = Rect ?? new Rectangle(0, 0, 0, 0);
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
    }

    /// <summary>Store <paramref name="content"/> as this annotation's normal
    /// appearance (/AP /N), wrapped in a Form XObject with the given bounding box.
    /// When <paramref name="opacity"/> is below 1, the form's resources carry an
    /// /ExtGState /GS0 with that stroke+fill alpha — the content is expected to
    /// select it via "/GS0 gs".</summary>
    private protected void SetNormalAppearance(byte[] content, Rectangle bbox, double opacity = 1.0)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(bbox.LLX)); bb.Add(new PdfReal(bbox.LLY));
        bb.Add(new PdfReal(bbox.URX)); bb.Add(new PdfReal(bbox.URY));
        form.Set("BBox", bb);
        var resources = new PdfDictionary();
        if (opacity < 1.0)
        {
            var gs = new PdfDictionary();
            gs.Set("Type", new PdfName("ExtGState"));
            gs.Set("CA", new PdfReal(opacity));
            gs.Set("ca", new PdfReal(opacity));
            var egs = new PdfDictionary();
            egs.Set("GS0", gs);
            resources.Set("ExtGState", egs);
        }
        form.Set("Resources", resources);
        form.Set("Length", new PdfInteger(content.Length));
        var stream = new PdfStream(form, content);
        var ap = _reader.ResolveDict(_dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", stream);
        _dict.Set("AP", ap);
        // Drop the cached appearance views so a subsequent Appearance / NormalAppearance
        // access rebuilds from the freshly written /AP (otherwise a stale empty /N is returned).
        _appearance = null;
        _states = null;
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

    /// <summary>Fully-qualified annotation name. For most annotation types this is the
    /// unique /NM entry; for a widget annotation that is (part of) a form field it is the
    /// fully-qualified field name — the /T values from this dict up through /Parent joined
    /// by '.', matching <see cref="Field.FullName"/>.</summary>
    public string? FullName
    {
        get
        {
            var nm = Name;
            if (!string.IsNullOrEmpty(nm)) return nm;
            // No /NM: if this dict participates in a field hierarchy (/T or /Parent),
            // build the fully-qualified field name from the /T chain.
            var parts = new System.Collections.Generic.List<string>();
            var d = _dict;
            int guard = 0;
            while (d is not null && guard++ < 64)
            {
                if (_reader.Resolve(d.Get("T")) is PdfString t)
                {
                    var s = t.ToText();
                    if (!string.IsNullOrEmpty(s)) parts.Insert(0, s);
                }
                d = _reader.ResolveDict(d.Get("Parent"));
            }
            return parts.Count > 0 ? string.Join(".", parts) : nm;
        }
    }

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

    /// <summary>Typed alias of <see cref="Flags"/> matching the
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
            // Bind after populating so subsequent Add/Delete write through to the annotation dict's
            // /A entry (and thus persist on save) — a fresh unbound collection dropped them.
            coll.Bind(_dict);
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
        if (ResolveAppearanceStream() is null && this is SquareAnnotation or CircleAnnotation or TextAnnotation or HighlightAnnotation or PolyAnnotation)
            UpdateAppearances();
        var appearanceStream = ResolveAppearanceStream();
        if (appearanceStream is not null)
        {
            var rectArr = _reader.Resolve(_dict.Get("Rect")) as PdfArray;
            if (rectArr is not null && rectArr.Count >= 4)
            {
                var rect = Rectangle.FromPdfArray(rectArr);

                // Per PDF 32000 §12.5.5 the appearance is placed by mapping the
                // *transformed* appearance box (BBox corners run through the form's
                // /Matrix, then their upright bounding box) onto the annotation /Rect —
                // NOT the raw BBox. Ignoring /Matrix mis-places any appearance whose
                // Matrix is not the identity (e.g. a Line/callout leader whose Matrix
                // translates its BBox to the origin), drawing it at the wrong spot.
                var bboxArr = _reader.Resolve(appearanceStream.Dict.Get("BBox")) as PdfArray;
                double bboxX = 0, bboxY = 0, bboxW = rect.Width, bboxH = rect.Height;
                if (bboxArr is { Count: >= 4 })
                {
                    var bbox = Rectangle.FromPdfArray(bboxArr);
                    var mtx = _reader.Resolve(appearanceStream.Dict.Get("Matrix")) as PdfArray;
                    double ma = 1, mb = 0, mc = 0, md = 1, me = 0, mf = 0;
                    if (mtx is { Count: >= 6 })
                    {
                        ma = PdfArrayHelper.GetDouble(mtx, 0); mb = PdfArrayHelper.GetDouble(mtx, 1);
                        mc = PdfArrayHelper.GetDouble(mtx, 2); md = PdfArrayHelper.GetDouble(mtx, 3);
                        me = PdfArrayHelper.GetDouble(mtx, 4); mf = PdfArrayHelper.GetDouble(mtx, 5);
                    }
                    // Transform the four BBox corners and take their bounding box.
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    foreach (var (cx, cy) in new[] { (bbox.LLX, bbox.LLY), (bbox.URX, bbox.LLY), (bbox.URX, bbox.URY), (bbox.LLX, bbox.URY) })
                    {
                        double px = ma * cx + mc * cy + me;
                        double py = mb * cx + md * cy + mf;
                        if (px < minX) minX = px; if (px > maxX) maxX = px;
                        if (py < minY) minY = py; if (py > maxY) maxY = py;
                    }
                    bboxX = minX; bboxY = minY;
                    bboxW = maxX - minX; bboxH = maxY - minY;
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
                // Flatten removes the annotation, and with it the /CA the renderer
                // would have applied — bake the opacity into the stamped content via
                // a page-level ExtGState instead.
                var gsOp = Opacity < 1.0
                    ? $"/{RegisterPageOpacityGState(pageDict, Opacity)} gs "
                    : "";
                using var appendContent = new MemoryStream();
                var writer = new StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
                if (isFormXobject)
                {
                    var xformName = Forms.Form.RegisterAppearanceAsXForm(pageDict, appearanceStream, _reader);
                    writer.Write($"q {gsOp}{Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm /{xformName} Do Q\n");
                    writer.Flush();
                }
                else
                {
                    // Fallback for non-Form appearance streams — inline the bytes and
                    // merge the annotation's /Resources into the page's /Resources so
                    // the operators have access to their fonts/xobjects.
                    var streamData = _reader.DecodeStream(appearanceStream);
                    writer.Write($"q {gsOp}{Fmt(sx)} 0 0 {Fmt(sy)} {Fmt(tx)} {Fmt(ty)} cm\n");
                    writer.Flush();
                    appendContent.Write(streamData);
                    writer.Write("\nQ\n");
                    writer.Flush();
                    Forms.Form.MergeAnnotResources(pageDict, appearanceStream.Dict, _reader);
                }

                // /Contents may be a single stream or an array of streams — decode
                // both forms so the underlying page content survives the rewrite.
                byte[] existingData = Aspose.Pdf.PdfPageStamp.GetPageContent(pageDict, _reader);

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

    /// <summary>Register a fill+stroke alpha ExtGState on the page's resources and
    /// return its name. Used when flattening bakes an annotation's /CA into content.</summary>
    private string RegisterPageOpacityGState(PdfDictionary pageDict, double opacity)
    {
        var resources = _reader.ResolveDict(pageDict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            pageDict.Set("Resources", resources);
        }
        var egs = _reader.ResolveDict(resources.Get("ExtGState"));
        if (egs is null)
        {
            egs = new PdfDictionary();
            resources.Set("ExtGState", egs);
        }
        var name = "GSf0";
        var counter = 0;
        while (egs.ContainsKey(name)) name = $"GSf{++counter}";
        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        gs.Set("CA", new PdfReal(opacity));
        gs.Set("ca", new PdfReal(opacity));
        egs.Set(name, gs);
        return name;
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
    public Characteristics Characteristics => _characteristics ??= BuildCharacteristics();

    /// <summary>Build the characteristics, populating Background/Border from the
    /// annotation's /MK appearance-characteristics dictionary (/BG and /BC) when present.
    /// Absent entries keep the defaults (transparent background, black border).</summary>
    private Characteristics BuildCharacteristics()
    {
        var c = new Characteristics();
        if (_reader.ResolveDict(_dict.Get("MK")) is { } mk)
        {
            if (ReadMkColor(mk, "BG") is { } bg) c.Background = bg;
            if (ReadMkColor(mk, "BC") is { } bc) c.Border = bc;
        }
        // Attach after seeding so the initial population doesn't echo back into /MK.
        c.WriteThrough = SetMkColor;
        return c;
    }

    /// <summary>Write an appearance-characteristics colour into the annotation's
    /// /MK dictionary as an RGB triple. A fully transparent colour removes the
    /// entry (the /MK convention for "no colour").</summary>
    private void SetMkColor(string key, System.Drawing.Color color)
    {
        var mk = _reader.ResolveDict(_dict.Get("MK"));
        if (color.A == 0)
        {
            mk?.Remove(key);
            return;
        }
        if (mk is null)
        {
            mk = new PdfDictionary();
            _dict.Set("MK", mk);
        }
        var arr = new PdfArray();
        arr.Add(new PdfReal(color.R / 255.0));
        arr.Add(new PdfReal(color.G / 255.0));
        arr.Add(new PdfReal(color.B / 255.0));
        mk.Set(key, arr);
    }

    /// <summary>The widget's background colour from its /MK /BG appearance
    /// characteristic. Returns <see cref="System.Drawing.Color.Transparent"/>
    /// when no /BG entry is present (the /MK convention for "no fill").</summary>
    internal System.Drawing.Color GetBackgroundColor()
    {
        if (_reader.ResolveDict(_dict.Get("MK")) is { } mk && ReadMkColor(mk, "BG") is { } bg)
            return bg;
        return System.Drawing.Color.Transparent;
    }

    private System.Drawing.Color? ReadMkColor(PdfDictionary mk, string key)
    {
        if (_reader.Resolve(mk.Get(key)) is not PdfArray arr || arr.Count == 0) return null;
        int To255(double v) => (int)System.Math.Round(System.Math.Clamp(v, 0, 1) * 255);
        double[] v = new double[arr.Count];
        for (int i = 0; i < arr.Count; i++) v[i] = PdfArrayHelper.GetDouble(arr, i);
        return arr.Count switch
        {
            1 => System.Drawing.Color.FromArgb(To255(v[0]), To255(v[0]), To255(v[0])),
            3 => System.Drawing.Color.FromArgb(To255(v[0]), To255(v[1]), To255(v[2])),
            4 => System.Drawing.Color.FromArgb(To255((1 - v[0]) * (1 - v[3])), To255((1 - v[1]) * (1 - v[3])), To255((1 - v[2]) * (1 - v[3]))),
            _ => (System.Drawing.Color?)null,
        };
    }

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
            "3D" => new PDF3DAnnotation(dict, reader),
            // "Watermark" has a separate non-Annotation class — keep as generic
            // Every other (un-modelled / vendor-specific) subtype, e.g. /BatesN,
            // falls back to GenericAnnotation so it stays castable and round-trips.
            _ => new GenericAnnotation(dict, reader),
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
