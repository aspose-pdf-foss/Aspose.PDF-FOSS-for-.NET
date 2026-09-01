using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

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
    /// When true the rectangle is mapped from unrotated page space — where /Rect
    /// lives — into the page's DISPLAYED frame, i.e. the coordinate system a
    /// viewer shows after applying the page's /Rotate, so the returned box can be
    /// handed straight to a rotation-compensating placement (image stamp, signature
    /// widget). Without the page box the mapping is undefined, so a page-less
    /// annotation falls back to the plain corner swap of the annotation's own
    /// /Rotate; otherwise returns <see cref="Rect"/> as-is.</summary>
    public Rectangle? GetRectangle(bool considerRotation)
    {
        var r = Rect;
        if (r is null) return null;
        if (!considerRotation) return r;
        var rotate = (int)(_dict.Get("Rotate") is PdfInteger pi ? pi.Value : 0);
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict
            ?? (_ownerPage ?? _creationPage)?.Dict;
        if (pageDict is not null)
        {
            var pageRotate = (int)(_reader.Resolve(pageDict.Get("Rotate")) is PdfInteger pri ? pri.Value : 0);
            // The page's rotation is what the viewer applies; an annotation-only
            // /Rotate turns the annotation's CONTENT inside its (unmoved) box.
            if (pageRotate == 0) return r;
            if (_reader.Resolve(pageDict.Get("MediaBox")) is PdfArray mbArr && mbArr.Count >= 4)
                return MapToDisplayed(r, pageRotate, Rectangle.FromPdfArray(mbArr));
        }
        if (rotate == 0) return r;
        var clone = new Rectangle(r.LLX, r.LLY, r.URX, r.URY);
        clone.Rotate(rotate);
        return clone;
    }

    /// <summary>Map <paramref name="pageRect"/> from unrotated page space into the
    /// page's displayed frame — the inverse of the `cm` a rotated page's content is
    /// drawn under (90 → "0 1 -1 0 URX 0", 270 → "0 -1 1 0 0 URY").</summary>
    private static Rectangle MapToDisplayed(Rectangle pageRect, int rotate, Rectangle mediaBox)
    {
        double wu = mediaBox.Width, hu = mediaBox.Height;
        (double x, double y) Map(double x, double y) => (((rotate % 360) + 360) % 360) switch
        {
            90 => (y, wu - x),
            180 => (wu - x, hu - y),
            270 => (hu - y, x),
            _ => (x, y),
        };
        var (x1, y1) = Map(pageRect.LLX, pageRect.LLY);
        var (x2, y2) = Map(pageRect.URX, pageRect.URY);
        return new Rectangle(System.Math.Min(x1, x2), System.Math.Min(y1, y2),
            System.Math.Max(x1, x2), System.Math.Max(y1, y2));
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

    private static string Fmt(double v) =>
        v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The raw annotation dictionary.</summary>
    internal PdfDictionary Dict => _dict;
    internal PdfReader InternalReader => _reader;

    /// <summary>Low-level view of the annotation's underlying PDF dictionary
    /// (the corpus' <c>EngineDict</c> assert surface — HasKey / indexer /
    /// ToDictionary / ToPdfString chains over what the file actually carries).</summary>
    internal Forms.FieldDictionaryView EngineDict =>
        Forms.FieldDictionaryView.For(_dict, _reader ?? Aspose.Pdf.IO.PdfReader.Empty);

    private Characteristics? _characteristics;
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
            // Prefer the owning document's page collection: a freshly created
            // document tracks its added pages there, and a bare tree walk over
            // the reader yields page-dict instances the identity scans below
            // cannot match against.
            var pages = _reader.OwnerDocument?.Pages ?? new PageCollection(_reader);
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

    /// <summary>The page that owns this annotation, or null when it can't be
    /// resolved (e.g. an annotation not yet attached to any page).</summary>
    public Page? Page
    {
        get
        {
            if (_ownerPage is { } op) return op;
            if (_creationPage is { } cp) return cp;
            var idx = PageIndex;
            if (idx < 1) return null;
            // Same page source as PageIndex: the owning document's collection sees
            // pages a freshly created document holds only in memory.
            var pages = _reader.OwnerDocument?.Pages ?? new PageCollection(_reader);
            return idx <= pages.Count ? pages[idx] : null;
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
