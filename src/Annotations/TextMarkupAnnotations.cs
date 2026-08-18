using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class LinkAnnotation : Annotation
{
    internal LinkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a new link annotation for the given page and rectangle.</summary>
    public LinkAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Link"));
    }

    /// <summary>Wrap an existing link-annotation object (e.g. the object an
    /// <see cref="Aspose.Pdf.LogicalStructure.OBJRElement"/> references) in the given document.</summary>
    public LinkAnnotation(object obj, Document document)
        : base(ResolveAnnotDict(obj, document), document.Reader)
    {
    }

    private static PdfDictionary ResolveAnnotDict(object obj, Document document)
    {
        var resolved = obj switch
        {
            PdfDictionary d => d,
            PdfObject p => document.Reader.ResolveDict(p),
            _ => null,
        };
        return resolved ?? throw new ArgumentException("Object does not resolve to an annotation dictionary.", nameof(obj));
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
    /// standard icon path for the current /Name; /C fills the icon body while
    /// outlines stay black.</summary>
    public override void UpdateAppearances()
    {
        var rect = Rect;
        if (rect is null) return;
        var c = Color;
        var content = TextAnnotationIcons.ContentFor(
            Dict.GetName("Name") ?? "Note",
            c is null ? null : (c.R / 255.0, c.G / 255.0, c.B / 255.0));

        var data = System.Text.Encoding.ASCII.GetBytes(content);
        // The materialised appearance keeps the historical 35x35 box: flatten
        // stretches BBox onto /Rect, and the flattened icon renders at
        // 20/35 of the rectangle, not edge to edge.
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
        _defaultAppearance = appearance ?? DefaultFreeTextAppearance();
        Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(_defaultAppearance.ToAppearanceString())));
    }

    /// <summary>Document-bound ctor for creating a FreeTextAnnotation that
    /// isn't yet attached to a specific page; the caller adds it via
    /// <c>page.Annotations.Add(annot)</c>.</summary>
    public FreeTextAnnotation(Document document, DefaultAppearance appearance)
        : base(document, rect: null!)
    {
        Dict.Set("Subtype", new PdfName("FreeText"));
        _defaultAppearance = appearance ?? DefaultFreeTextAppearance();
        Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(_defaultAppearance.ToAppearanceString())));
    }

    /// <summary>Fallback /DA for a FreeText annotation created with no explicit
    /// appearance: Helvetica ("Helv") at 10pt black. A null appearance must still
    /// produce a valid, non-zero font size rather than an empty or zero-size /DA.</summary>
    private static DefaultAppearance DefaultFreeTextAppearance() => new("Helv", 10);

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
    /// <see cref="DefaultAppearance"/>. The construction-time appearance is stored;
    /// for an annotation read from a document (no stored object) the /DA string is
    /// parsed so the font, size and colour drive the generated appearance.</summary>
    public DefaultAppearance DefaultAppearanceObject =>
        _defaultAppearance ??= ParseDefaultAppearance(DefaultAppearance) ?? new DefaultAppearance();

    /// <summary>Parse a /DA appearance string (e.g. "/Helv 16 Tf 0 0 1 rg") into a typed
    /// <see cref="DefaultAppearance"/>. Handles the rg/g/k colour operators and the common
    /// Standard-14 resource abbreviations. Returns null when nothing recognisable is found.</summary>
    private static DefaultAppearance? ParseDefaultAppearance(string? da)
    {
        if (string.IsNullOrWhiteSpace(da)) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        var t = da.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool TryD(string s, out double v) => double.TryParse(s, System.Globalization.NumberStyles.Float, ci, out v);
        string fontName = "Helvetica"; double size = 12; var color = System.Drawing.Color.Black;
        bool got = false;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == "Tf" && i >= 2)
            {
                if (t[i - 2].StartsWith("/")) fontName = NormalizeDaFontName(t[i - 2].Substring(1));
                if (TryD(t[i - 1], out var s) && s > 0) size = s;
                got = true;
            }
            else if (t[i] == "rg" && i >= 3 && TryD(t[i - 3], out var r) && TryD(t[i - 2], out var g) && TryD(t[i - 1], out var b))
            { color = System.Drawing.Color.FromArgb(C(r), C(g), C(b)); got = true; }
            else if (t[i] == "g" && i >= 1 && TryD(t[i - 1], out var gray))
            { color = System.Drawing.Color.FromArgb(C(gray), C(gray), C(gray)); got = true; }
            else if (t[i] == "k" && i >= 4 && TryD(t[i - 4], out var c) && TryD(t[i - 3], out var m) && TryD(t[i - 2], out var y) && TryD(t[i - 1], out var k))
            { color = System.Drawing.Color.FromArgb(C((1 - c) * (1 - k)), C((1 - m) * (1 - k)), C((1 - y) * (1 - k))); got = true; }
        }
        return got ? new DefaultAppearance(fontName, size, color) : null;

        static int C(double v) => Math.Max(0, Math.Min(255, (int)Math.Round(v * 255)));
    }

    private static string NormalizeDaFontName(string n) => n switch
    {
        "Helv" => "Helvetica",
        "HeBo" => "Helvetica-Bold",
        "HeOb" => "Helvetica-Oblique",
        "TiRo" => "Times-Roman",
        "TiBo" => "Times-Bold",
        "TiIt" => "Times-Italic",
        "Cour" => "Courier",
        "CoBo" => "Courier-Bold",
        "Symb" => "Symbol",
        "ZaDb" => "ZapfDingbats",
        _ => n,
    };

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

    /// <summary>Rich text string (XHTML) for the annotation (/RC entry). Setting it parses the
    /// XHTML span styles (font-weight/font-style/text-decoration and base font/size/colour) into
    /// the plain text plus per-range style runs, and regenerates the styled appearance.</summary>
    public new string? RichText
    {
        get => GetString("RC");
        set
        {
            if (value is null) Dict.Remove("RC");
            else Dict.Set("RC", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
            // Capture the rich-text styling (plain text, per-range runs, base size/colour) so a
            // following SetTextStyle renders it, but do NOT take over appearance generation here:
            // rich text alone falls back to the normal save-time appearance (matching the
            // established output), and the styled path is driven by explicit SetTextStyle calls.
            ApplyRichTextStyles(value);
        }
    }

    /// <summary>Always <see cref="AnnotationType.FreeText"/>. Redeclared on
    /// the derived class so DeclaredOnly reflection sees it.</summary>
    public new AnnotationType AnnotationType => AnnotationType.FreeText;

    /// <summary>
    /// Build the /AP /N appearance stream from <see cref="Annotation.Contents"/>,
    /// laying the text out as word-wrapped lines inside the annotation rectangle using
    /// the /DA font, size and colour. No-op when an appearance already exists or there
    /// is no text. Invoked by the save pipeline so a freshly-created FreeText annotation
    /// renders (and exposes <see cref="Annotation.NormalAppearance"/>).
    /// </summary>
    /// <summary>Changing a FreeText annotation's /C background colour invalidates its
    /// stored /AP, which still paints the old background. Drop the existing appearance
    /// and rebuild it from /Contents + /DA so it reflects the new colour — the FreeText
    /// appearance regenerates on a colour change
    /// (dropping any previously-embedded font for the /DA's standard font). Only rebuilt
    /// when there is text to render; otherwise the existing appearance is left intact.</summary>
    private protected override void OnColorChanged()
    {
        var text = Contents;
        if (string.IsNullOrEmpty(text)) text = PlainTextFromRichText(RichText);
        if (string.IsNullOrEmpty(text)) return;
        Dict.Remove("AP");
        InvalidateAppearanceCache();
        GenerateAppearance();
    }

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
        // Text inset scales with the border: 2pt inside a standard 1pt border, flush
        // with the rectangle for a borderless (W=0) typewriter annotation. Calibrated
        // so that a 12pt Helvetica line of 130.08pt in a 136.05pt
        // bordered rect stays on one line (inset ≤ 2.98), while a 12pt Courier line
        // of 93.6pt in a 96.3pt borderless rect also stays whole (inset ≤ 1.36).
        var inset = 2.0 * borderWidth;
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
        // rectangle with it (behind border and text) when
        // present. Only the unrotated rect is filled —
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
        // sits centred on the rectangle edge.
        // A FreeText with no /BS or /Border entry gets the PDF-default 1pt border
        // (PDF 32000-1 §12.5.4 /Border default [0 0 1]) — only an explicit zero
        // width suppresses it.
        var bsDict = InternalReader.ResolveDict(Dict.Get("BS"));
        if (borderWidth > 0)
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
            // anchored to the rectangle's leading edge.
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
        // Alignment is taken from the persisted /Q justification (which survives a
        // re-wrap of the annotation on save), falling back to the in-memory TextStyle.
        var align = Justification switch
        {
            Justification.Right => Aspose.Pdf.HorizontalAlignment.Right,
            Justification.Center => Aspose.Pdf.HorizontalAlignment.Center,
            _ => ts?.HorizontalAlignment ?? Aspose.Pdf.HorizontalAlignment.Left,
        };
        if (rotated || align == Aspose.Pdf.HorizontalAlignment.Left)
        {
            if (rotated)
                sb.Append("2 ").Append(Fmt(-fontSize)).Append(" Td\n");
            else
                sb.Append(Fmt(inset)).Append(' ').Append(Fmt(h - inset - fontSize)).Append(" Td\n");
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append("T*\n");
                sb.Append('(').Append(EscapePdfString(lines[i])).Append(") Tj\n");
            }
        }
        else
        {
            // Center/Right alignment: each line is offset by its own measured width,
            // so emit a per-line Td (relative to the previous line's position).
            double prevX = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var lineW = metrics.MeasureString(lines[i], fontSize);
                double lineX = align == Aspose.Pdf.HorizontalAlignment.Right
                    ? w - lineW
                    : (w - lineW) / 2;
                double dy = i == 0 ? h - inset - fontSize : -leading;
                sb.Append(Fmt(lineX - prevX)).Append(' ').Append(Fmt(dy)).Append(" Td\n");
                sb.Append('(').Append(EscapePdfString(lines[i])).Append(") Tj\n");
                prevX = lineX;
            }
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
    /// its own line (no mid-word breaking).</summary>
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
    public LineEnding StartingStyle
    {
        get => GetCalloutLineEnding(0);
        set => SetCalloutLineEnding(0, value);
    }

    /// <summary>Ending line-ending style (/LE second entry; callout intent only).</summary>
    public LineEnding EndingStyle
    {
        get => GetCalloutLineEnding(1);
        set => SetCalloutLineEnding(1, value);
    }

    // A FreeText callout has a single line ending — the arrowhead at the callout's
    // pointed end. It may be stored as a single /LE name (the PDF-spec form) or as a
    // two-element [head tail] array (the form Acrobat/XFDF round-trips produce, with the
    // ending carried in one slot). Report that single ending for both StartingStyle and
    // EndingStyle, which matches the callout model.
    private LineEnding GetCalloutLineEnding(int index)
    {
        var le = InternalReader.Resolve(Dict.Get("LE"));
        if (le is PdfName name)
            return LineAnnotation.ParseLineEnding(name.Value);
        if (le is PdfArray arr)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                var v = (InternalReader.Resolve(arr[i]) as PdfName)?.Value;
                if (v is not null && v != "None")
                    return LineAnnotation.ParseLineEnding(v);
            }
        }
        return LineEnding.None;
    }

    private void SetCalloutLineEnding(int index, LineEnding value)
    {
        var arr = InternalReader.Resolve(Dict.Get("LE")) as PdfArray;
        var start = "None";
        var end = "None";
        if (arr is { Count: >= 1 } && InternalReader.Resolve(arr[0]) is PdfName s) start = s.Value;
        if (arr is { Count: >= 2 } && InternalReader.Resolve(arr[1]) is PdfName e) end = e.Value;
        else if (InternalReader.Resolve(Dict.Get("LE")) is PdfName single) { start = single.Value; end = single.Value; }
        if (index == 0) start = LineAnnotation.LineEndingToName(value);
        else end = LineAnnotation.LineEndingToName(value);
        var newArr = new PdfArray();
        newArr.Add(new PdfName(start));
        newArr.Add(new PdfName(end));
        Dict.Set("LE", newArr);
    }

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

    /// <summary>Bundled text style (font / size / colour / alignment) applied when rendering the
    /// rich-text contents. A mutable stored object: <c>annot.TextStyle.FontSize = 18</c> persists
    /// and drives the generated appearance.</summary>
    public TextStyle TextStyle { get; set; } = new TextStyle();

    /// <summary>Apply style flags to the whole text run. The <paramref name="fontName"/>/
    /// <paramref name="fontSize"/>/<paramref name="fontColor"/> arguments mirror the caller's
    /// current <see cref="TextStyle"/>; they are recorded on <see cref="TextStyle"/> but the
    /// rendered base font is kept as the annotation's established /DA (so a bold base such as
    /// "Arial Bold" survives), with the size/colour applied.</summary>
    public void SetTextStyle(RichTextFontStyles textStyles, string fontName, double fontSize, System.Drawing.Color fontColor)
    {
        TextStyle = new TextStyle { FontName = fontName, FontSize = fontSize, Color = fontColor };
        var baseFont = DefaultAppearanceObject.FontName;
        DefaultAppearance = new DefaultAppearance(baseFont, fontSize, fontColor).ToAppearanceString();
        _defaultAppearance = new DefaultAppearance(baseFont, fontSize, fontColor);
        // The whole-run overload replaces any existing per-range styles with these flags
        // (a later whole-text SetTextStyle overrides earlier rich-text spans).
        var len = StyledSourceTextLength();
        _styleRuns.Clear();
        _styleRuns.Add((0, len, textStyles));
        RegenerateStyledAppearance();
    }

    /// <summary>Apply rich-text style flags to the substring [fromInd, toInd).
    /// <see cref="RichTextFontStyles.ClearExisting"/> (value 0) on its own clears the range;
    /// any other flags are OR-ed into the range. The annotation appearance is regenerated.</summary>
    public void SetTextStyle(int fromInd, int toInd, RichTextFontStyles textStyles)
    {
        _styleRuns.Add((fromInd, toInd, textStyles));
        RegenerateStyledAppearance();
    }

    // Ordered list of per-range style applications (from, to, styles). Replayed in order to
    // build a per-character style array. styles == 0 (ClearExisting alone) clears the range.
    private readonly System.Collections.Generic.List<(int from, int to, RichTextFontStyles styles)> _styleRuns = new();

    private int StyledSourceTextLength()
    {
        var t = Contents;
        if (string.IsNullOrEmpty(t)) t = PlainTextFromRichText(RichText);
        return t?.Length ?? 0;
    }

    private RichTextFontStyles[] ResolveCharStyles(int len)
    {
        var arr = new RichTextFontStyles[len];
        foreach (var (from, to, styles) in _styleRuns)
        {
            int a = System.Math.Max(0, System.Math.Min(from, to));
            int b = System.Math.Min(len, System.Math.Max(from, to));
            for (int i = a; i < b; i++)
                if (styles == 0) arr[i] = 0; else arr[i] |= styles; // 0 == ClearExisting (clear range)
        }
        return arr;
    }

    private static string VariantFontName(string baseFont, bool bold, bool italic)
    {
        var f = (baseFont ?? "").Replace(" ", "");
        // A bold/italic base font name (e.g. "Arial Bold") contributes the style itself.
        if (f.IndexOf("Bold", System.StringComparison.OrdinalIgnoreCase) >= 0) bold = true;
        if (f.IndexOf("Italic", System.StringComparison.OrdinalIgnoreCase) >= 0
            || f.IndexOf("Oblique", System.StringComparison.OrdinalIgnoreCase) >= 0) italic = true;
        if (f.IndexOf("Times", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return bold && italic ? "Times-BoldItalic" : bold ? "Times-Bold" : italic ? "Times-Italic" : "Times-Roman";
        if (f.IndexOf("Courier", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return bold && italic ? "Courier-BoldOblique" : bold ? "Courier-Bold" : italic ? "Courier-Oblique" : "Courier";
        return bold && italic ? "Helvetica-BoldOblique" : bold ? "Helvetica-Bold" : italic ? "Helvetica-Oblique" : "Helvetica";
    }

    private bool BorderExplicitlyZero()
    {
        var bs = InternalReader.ResolveDict(Dict.Get("BS"));
        if (bs is not null)
        {
            if (bs.Get("W") is PdfInteger bi) return bi.Value == 0;
            if (bs.Get("W") is PdfReal br) return br.Value == 0;
        }
        if (InternalReader.Resolve(Dict.Get("Border")) is PdfArray arr && arr.Count >= 3)
        {
            if (arr[2] is PdfInteger ai) return ai.Value == 0;
            if (arr[2] is PdfReal ar) return ar.Value == 0;
        }
        return false;
    }

    /// <summary>Parse XHTML rich-text styling into the plain text, per-range style runs and a
    /// base font size/colour. Returns true when styled markup was found (so the caller should
    /// regenerate the styled appearance); false for plain text or no markup.</summary>
    private bool ApplyRichTextStyles(string? xhtml)
    {
        if (string.IsNullOrEmpty(xhtml) || xhtml!.IndexOf('<') < 0) return false;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var sbText = new System.Text.StringBuilder();
        var runs = new System.Collections.Generic.List<(int from, int to, RichTextFontStyles st)>();
        var stack = new System.Collections.Generic.Stack<(bool b, bool i, bool u)>();
        stack.Push((false, false, false));
        string? baseFont = null; double baseSize = 0; System.Drawing.Color? baseColor = null;
        int p = 0;
        while (p < xhtml.Length)
        {
            if (xhtml[p] == '<')
            {
                int gt = xhtml.IndexOf('>', p); if (gt < 0) break;
                string tag = xhtml.Substring(p + 1, gt - p - 1);
                if (tag.StartsWith("/")) { if (stack.Count > 1) stack.Pop(); }
                else if (!tag.StartsWith("?") && !tag.StartsWith("!"))
                {
                    var cur = stack.Peek();
                    bool b = cur.b, it = cur.i, u = cur.u;
                    var sm = System.Text.RegularExpressions.Regex.Match(tag, "style\\s*=\\s*\"([^\"]*)\"");
                    if (sm.Success)
                    {
                        var css = sm.Groups[1].Value;
                        if (css.Replace(" ", "").Contains("font-weight:bold")) b = true;
                        if (css.Replace(" ", "").Contains("font-style:italic")) it = true;
                        if (css.Contains("underline")) u = true;
                        var fs = System.Text.RegularExpressions.Regex.Match(css, "font-size:\\s*([0-9.]+)pt");
                        if (fs.Success && baseSize == 0) double.TryParse(fs.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out baseSize);
                        var ff = System.Text.RegularExpressions.Regex.Match(css, "font-family:\\s*([^;\"]+)");
                        if (ff.Success && baseFont == null) baseFont = ff.Groups[1].Value.Trim();
                        var cm = System.Text.RegularExpressions.Regex.Match(css, "color:\\s*#([0-9A-Fa-f]{6})");
                        if (cm.Success && baseColor == null)
                        {
                            var h = cm.Groups[1].Value;
                            baseColor = System.Drawing.Color.FromArgb(
                                System.Convert.ToInt32(h.Substring(0, 2), 16),
                                System.Convert.ToInt32(h.Substring(2, 2), 16),
                                System.Convert.ToInt32(h.Substring(4, 2), 16));
                        }
                    }
                    if (!tag.EndsWith("/")) stack.Push((b, it, u));
                }
                p = gt + 1;
            }
            else
            {
                int lt = xhtml.IndexOf('<', p); if (lt < 0) lt = xhtml.Length;
                var raw = xhtml.Substring(p, lt - p)
                    .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").Replace("&#xD;", "").Replace("&#xA;", "");
                if (raw.Length > 0)
                {
                    var cur = stack.Peek();
                    RichTextFontStyles st = 0;
                    if (cur.b) st |= RichTextFontStyles.Bold;
                    if (cur.i) st |= RichTextFontStyles.Italic;
                    if (cur.u) st |= RichTextFontStyles.Underline;
                    int start = sbText.Length;
                    sbText.Append(raw);
                    if (st != 0) runs.Add((start, sbText.Length, st));
                }
                p = lt;
            }
        }
        var plain = sbText.ToString();
        if (string.IsNullOrEmpty(Contents)) Contents = plain;
        // Keep the construction-time base font (it may already encode weight, e.g. "Arial Bold");
        // only the size/colour are taken from the rich text.
        if (baseSize > 0 || baseColor != null)
        {
            var f = DefaultAppearanceObject.FontName;
            var s = baseSize > 0 ? baseSize : DefaultAppearanceObject.FontSize;
            var c = baseColor ?? DefaultAppearanceObject.TextColor;
            DefaultAppearance = new DefaultAppearance(f, s, c).ToAppearanceString();
            _defaultAppearance = new DefaultAppearance(f, s, c);
        }
        _styleRuns.AddRange(runs);
        return runs.Count > 0 || baseSize > 0 || baseColor != null;
    }

    /// <summary>Regenerate the /AP /N appearance honouring per-range rich-text styles
    /// (bold/italic/underline) set via <see cref="SetTextStyle(int,int,RichTextFontStyles)"/>,
    /// the /DA font/size/colour, and a default 1pt border (unless the border was set to 0).
    /// Word-wraps within the rectangle, measuring each run with its styled font variant.</summary>
    internal void RegenerateStyledAppearance()
    {
        var rect = Rect;
        if (rect is null || rect.Width <= 0 || rect.Height <= 0) return;
        var text = Contents;
        if (string.IsNullOrEmpty(text)) text = PlainTextFromRichText(RichText);
        if (string.IsNullOrEmpty(text)) return;
        text = text!.Replace("\r\n", "\n").Replace("\r", "\n");

        var da = DefaultAppearanceObject;
        string baseFont = string.IsNullOrWhiteSpace(da.FontName) ? "Helvetica" : da.FontName!;
        double size = da.FontSize > 0 ? da.FontSize : 12.0;
        var color = da.TextColor;

        double border = BorderExplicitlyZero() ? 0 : System.Math.Max(1.0, ReadBorderWidth());
        double inset = border + 2.0;
        double w = rect.Width, h = rect.Height;
        double avail = System.Math.Max(1.0, w - 2 * inset);
        double leading = size * 1.15;

        var styles = ResolveCharStyles(text.Length);

        // Per-variant font dict + metrics cache.
        var fontDicts = new System.Collections.Generic.Dictionary<string, PdfDictionary>();
        var metricsCache = new System.Collections.Generic.Dictionary<string, Aspose.Pdf.Text.FontMetrics>();
        PdfDictionary FontDict(string v) { if (!fontDicts.TryGetValue(v, out var d)) { d = MakeFreeTextFontDict(v); fontDicts[v] = d; } return d; }
        Aspose.Pdf.Text.FontMetrics Metrics(string v) { if (!metricsCache.TryGetValue(v, out var m)) { m = Aspose.Pdf.Text.FontMetrics.FromFontDict(FontDict(v), InternalReader); metricsCache[v] = m; } return m; }
        string VarOf(RichTextFontStyles s) => VariantFontName(baseFont,
            (s & RichTextFontStyles.Bold) != 0, (s & RichTextFontStyles.Italic) != 0);
        double CharW(char c, RichTextFontStyles s) => Metrics(VarOf(s)).MeasureString(c.ToString(), size);

        // Tokenise into words / spaces / newlines (carrying each char's style), then greedily
        // wrap into output lines no wider than `avail`.
        var outLines = new System.Collections.Generic.List<System.Collections.Generic.List<(char ch, RichTextFontStyles st)>>();
        var line = new System.Collections.Generic.List<(char, RichTextFontStyles)>();
        double lineW = 0;
        var word = new System.Collections.Generic.List<(char ch, RichTextFontStyles st)>();
        double wordW = 0;
        void FlushWord()
        {
            if (word.Count == 0) return;
            if (lineW > 0 && lineW + wordW > avail) { outLines.Add(line); line = new(); lineW = 0; }
            foreach (var t in word) { line.Add(t); }
            lineW += wordW; word.Clear(); wordW = 0;
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i]; var s = styles[i];
            if (c == '\n') { FlushWord(); outLines.Add(line); line = new(); lineW = 0; continue; }
            if (c == ' ')
            {
                FlushWord();
                double sw = CharW(' ', s);
                if (lineW > 0) { line.Add((c, s)); lineW += sw; } // skip leading spaces after a wrap
                continue;
            }
            word.Add((c, s)); wordW += CharW(c, s);
        }
        FlushWord();
        if (line.Count > 0 || outLines.Count == 0) outLines.Add(line);

        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.###", ci);
        var sb = new System.Text.StringBuilder();

        // Border box (default 1pt unless set to 0), stroked in the text colour.
        if (border > 0)
        {
            sb.Append("q\n").Append(F(color.R / 255.0)).Append(' ').Append(F(color.G / 255.0)).Append(' ')
              .Append(F(color.B / 255.0)).Append(" RG\n").Append(F(border)).Append(" w\n")
              .Append(F(border / 2)).Append(' ').Append(F(border / 2)).Append(' ')
              .Append(F(w - border)).Append(' ').Append(F(h - border)).Append(" re\nS\nQ\n");
        }

        var underlines = new System.Collections.Generic.List<(double x, double y, double len)>();
        sb.Append("/Tx BMC\nq\nBT\n");
        sb.Append(F(color.R / 255.0)).Append(' ').Append(F(color.G / 255.0)).Append(' ').Append(F(color.B / 255.0)).Append(" rg\n");
        double y0 = h - inset - size;
        for (int li = 0; li < outLines.Count; li++)
        {
            double baseY = y0 - li * leading;
            if (baseY < -size) break; // ran past the bottom of the box
            double x = inset;
            var cells = outLines[li];
            int j = 0;
            while (j < cells.Count)
            {
                var st = cells[j].st;
                var run = new System.Text.StringBuilder();
                double runW = 0;
                while (j < cells.Count && cells[j].st == st) { run.Append(cells[j].ch); runW += CharW(cells[j].ch, st); j++; }
                string v = VarOf(st);
                sb.Append("/").Append(ResName(v)).Append(' ').Append(F(size)).Append(" Tf\n");
                sb.Append("1 0 0 1 ").Append(F(x)).Append(' ').Append(F(baseY)).Append(" Tm\n");
                sb.Append('(').Append(EscapePdfString(run.ToString())).Append(") Tj\n");
                if ((st & RichTextFontStyles.Underline) != 0)
                    underlines.Add((x, baseY - size * 0.12, runW));
                x += runW;
            }
        }
        sb.Append("ET\n");
        foreach (var (ux, uy, ulen) in underlines)
            sb.Append(F(color.R / 255.0)).Append(' ').Append(F(color.G / 255.0)).Append(' ').Append(F(color.B / 255.0)).Append(" RG\n")
              .Append(F(System.Math.Max(0.5, size * 0.06))).Append(" w\n")
              .Append(F(ux)).Append(' ').Append(F(uy)).Append(" m\n").Append(F(ux + ulen)).Append(' ').Append(F(uy)).Append(" l\nS\n");
        sb.Append("Q\nEMC\n");

        var apStream = new PdfStream(new PdfDictionary(), System.Text.Encoding.Latin1.GetBytes(sb.ToString()));
        apStream.Dict.Set("Type", new PdfName("XObject"));
        apStream.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        apStream.Dict.Set("BBox", bbox);
        var fonts = new PdfDictionary();
        foreach (var kv in fontDicts) fonts.Set(ResName(kv.Key), kv.Value);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        apStream.Dict.Set("Resources", res);
        var ap = new PdfDictionary();
        ap.Set("N", apStream);
        Dict.Set("AP", ap);
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

        // Quad boxes.
        var boxes = new System.Collections.Generic.List<(double minX, double minY, double maxX, double maxY)>();
        for (var i = 0; i + 3 < quads.Length; i += 4) boxes.Add(QuadBounds(quads, i));
        if (boxes.Count == 0) return result;

        // Assign each marked character to a SINGLE best quad (largest X-overlap within
        // the quad's Y band). Adjacent quads can overlap by a fraction of a point, so
        // collecting per-quad independently would double-count boundary glyphs. Group the
        // chars by (best quad, source text run) — one output fragment per group, yielding
        // a fragment per marked run, not per quad.
        const double grazeTolerance = 0.1;
        var groups = new System.Collections.Generic.Dictionary<(int q, int fi),
            System.Collections.Generic.List<(char ch, double cx)>>();
        var order = new System.Collections.Generic.List<(int q, int fi)>();
        var fi = 0;
        foreach (var f in fragments)
        {
            var runIndex = fi++;
            foreach (Aspose.Pdf.Text.TextSegment seg in f.Segments)
            {
                var chars = seg.Characters;
                var text = seg.Text ?? string.Empty;
                for (var c = 1; c <= chars.Count && c <= text.Length; c++)
                {
                    var r = chars[c].Rectangle;
                    var cy = (r.LLY + r.URY) / 2.0;
                    var bestQ = -1; var bestOv = grazeTolerance;
                    for (var q = 0; q < boxes.Count; q++)
                    {
                        var (minX, minY, maxX, maxY) = boxes[q];
                        if (cy < minY - 2 || cy > maxY + 2) continue;
                        var overlapX = System.Math.Min(r.URX, maxX) - System.Math.Max(r.LLX, minX);
                        if (overlapX > bestOv) { bestOv = overlapX; bestQ = q; }
                    }
                    if (bestQ < 0) continue;
                    var key = (bestQ, runIndex);
                    if (!groups.TryGetValue(key, out var list))
                    {
                        list = new System.Collections.Generic.List<(char, double)>();
                        groups[key] = list;
                        order.Add(key);
                    }
                    list.Add((text[c - 1], (r.LLX + r.URX) / 2.0));
                }
            }
        }

        // One piece per (quad, run) group, tagged with the quad's position for re-ordering.
        var pieces = new System.Collections.Generic.List<(double midY, double minX, double maxX, string text)>();
        double rightMargin = double.MinValue;
        foreach (var key in order)
        {
            var list = groups[key];
            if (list.Count == 0) continue;
            list.Sort((a, b) => a.cx.CompareTo(b.cx));
            var sb = new System.Text.StringBuilder();
            var pieceMinX = double.MaxValue;
            foreach (var (ch, cx) in list) { sb.Append(ch); if (cx < pieceMinX) pieceMinX = cx; }
            var (_, minY, maxX, maxY) = boxes[key.q];
            pieces.Add(((minY + maxY) / 2.0, pieceMinX, maxX, sb.ToString()));
            if (maxX > rightMargin) rightMargin = maxX;
        }
        if (pieces.Count == 0) return result;

        // Reading order: group pieces into lines by vertical centre (top first), then
        // order each line left-to-right — /QuadPoints order need not be the visual order.
        pieces.Sort((a, b) => b.midY.CompareTo(a.midY));
        var lines = new System.Collections.Generic.List<System.Collections.Generic.List<(double midY, double minX, double maxX, string text)>>();
        const double lineTol = 6.0;
        foreach (var p in pieces)
        {
            if (lines.Count > 0 && System.Math.Abs(lines[^1][0].midY - p.midY) <= lineTol)
                lines[^1].Add(p);
            else
                lines.Add(new System.Collections.Generic.List<(double, double, double, string)> { p });
        }

        for (var li = 0; li < lines.Count; li++)
        {
            var line = lines[li];
            line.Sort((a, b) => a.minX.CompareTo(b.minX));
            double lineRight = double.MinValue;
            foreach (var p in line) if (p.maxX > lineRight) lineRight = p.maxX;

            // A line ending well short of the block's right edge is a hard break (a label
            // or paragraph end), not a soft wrap: emit a trailing space on its last quad
            // to separate it from the next line. Wrapped lines that reach the margin join
            // with no gap. Each quad remains its own fragment (the fragment count matters).
            bool spaceAfterLine = li < lines.Count - 1 && lineRight < rightMargin - 20.0;
            for (var pi = 0; pi < line.Count; pi++)
            {
                var text = line[pi].text;
                if (spaceAfterLine && pi == line.Count - 1) text += " ";
                result.Add(new Aspose.Pdf.Text.TextFragment(text));
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

    /// <summary>Regenerate the normal appearance (/AP /N): each /QuadPoints quad is
    /// painted as a filled-and-stroked rectangle in the annotation colour, under a
    /// Multiply blend graphics state so the highlighted text stays legible underneath.</summary>
    public override void UpdateAppearances()
    {
        var r = Rect;
        var quads = QuadPoints;
        if (r is null || quads.Length < 4) { base.UpdateAppearances(); return; }
        var color = Color;
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetExtGState("TransGs");
        b.SetFillColor(color);
        b.SetStrokeColor(color);
        for (int i = 0; i + 3 < quads.Length; i += 4)
        {
            double minX = Math.Min(Math.Min(quads[i].X, quads[i + 1].X), Math.Min(quads[i + 2].X, quads[i + 3].X));
            double maxX = Math.Max(Math.Max(quads[i].X, quads[i + 1].X), Math.Max(quads[i + 2].X, quads[i + 3].X));
            double minY = Math.Min(Math.Min(quads[i].Y, quads[i + 1].Y), Math.Min(quads[i + 2].Y, quads[i + 3].Y));
            double maxY = Math.Max(Math.Max(quads[i].Y, quads[i + 1].Y), Math.Max(quads[i + 2].Y, quads[i + 3].Y));
            b.MoveTo(minX, minY);
            b.LineTo(minX, maxY);
            b.LineTo(maxX, maxY);
            b.LineTo(maxX, minY);
            b.ClosePath();
            b.FillAndStroke();
        }
        b.RestoreState();
        SetHighlightAppearance(b.Build(), r);
    }

    // Build the /AP /N form XObject carrying the /TransGs ExtGState (Multiply blend)
    // referenced by the appearance content.
    private void SetHighlightAppearance(byte[] content, Rectangle bbox)
    {
        var form = new PdfDictionary();
        form.Set("Type", new PdfName("XObject"));
        form.Set("Subtype", new PdfName("Form"));
        form.Set("FormType", new PdfInteger(1));
        var bb = new PdfArray();
        bb.Add(new PdfReal(bbox.LLX)); bb.Add(new PdfReal(bbox.LLY));
        bb.Add(new PdfReal(bbox.URX)); bb.Add(new PdfReal(bbox.URY));
        form.Set("BBox", bb);

        var gs = new PdfDictionary();
        gs.Set("Type", new PdfName("ExtGState"));
        gs.Set("BM", new PdfName("Multiply"));
        gs.Set("ca", new PdfReal(1));
        gs.Set("CA", new PdfReal(1));
        var extg = new PdfDictionary();
        extg.Set("TransGs", gs);
        var res = new PdfDictionary();
        res.Set("ExtGState", extg);
        form.Set("Resources", res);
        form.Set("Length", new PdfInteger(content.Length));

        var ap = InternalReader.ResolveDict(Dict.Get("AP")) ?? new PdfDictionary();
        ap.Set("N", new PdfStream(form, content));
        Dict.Set("AP", ap);
    }
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
