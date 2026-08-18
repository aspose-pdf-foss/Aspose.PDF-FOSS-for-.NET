using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class WidgetAnnotation : Annotation
{
    internal WidgetAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Detached ctor — a document-less widget used as a configuration holder
    /// (see <see cref="Annotation()"/>). Tags the dict as a Widget annotation.</summary>
    protected WidgetAnnotation() : base()
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Widget"));
    }

    /// <summary>Programmatic ctor — creates a bare widget annotation
    /// associated with <paramref name="doc"/>'s reader. The widget
    /// has no /AP/N appearance state until the caller assigns one.</summary>
    public WidgetAnnotation(Document doc) : base(doc, new Rectangle(0, 0, 0, 0))
    {
        Dict.Set("Type", new PdfName("Annot"));
        Dict.Set("Subtype", new PdfName("Widget"));
    }

    /// <summary>Always <see cref="AnnotationType.Widget"/>. Redeclared
    /// with `new` so DeclaredOnly reflection sees it on
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
    /// (DeclaredOnly reflection).</summary>
    public new AnnotationActionCollection Actions => _actions ??= BuildActions();
    private AnnotationActionCollection? _actions;

    private AnnotationActionCollection BuildActions()
    {
        var col = new AnnotationActionCollection();
        var reader = InternalReader;
        if (reader is null)
        {
            // Freshly created widget: nothing to load, but bind so property
            // assignments still write through to /A and /AA.
            col.Bind(Dict, null);
            return col;
        }

        PdfAction? Read(PdfDictionary? source, string key)
        {
            var d = reader.ResolveDict(source?.Get(key));
            return d is null ? null : PdfAction.Create(d, reader);
        }

        col.Load("A", Read(Dict, "A"));

        var aa = reader.ResolveDict(Dict.Get("AA"));
        if (aa is not null)
        {
            col.Load("E", Read(aa, "E"));
            col.Load("X", Read(aa, "X"));
            col.Load("D", Read(aa, "D"));
            col.Load("U", Read(aa, "U"));
            col.Load("Fo", Read(aa, "Fo"));
            col.Load("Bl", Read(aa, "Bl"));
            col.Load("K", Read(aa, "K"));
            col.Load("F", Read(aa, "F"));
            col.Load("V", Read(aa, "V"));
            col.Load("C", Read(aa, "C"));
            col.Load("PO", Read(aa, "PO"));
            col.Load("PC", Read(aa, "PC"));
            col.Load("PV", Read(aa, "PV"));
            col.Load("PI", Read(aa, "PI"));
        }
        col.Bind(Dict, reader);
        return col;
    }

    private DefaultAppearance? _defaultAppearance;

    /// <summary>Default-appearance (font, size, colour) for this widget. Write-through:
    /// the setter serialises a /DA string onto the widget dict (so a per-widget appearance
    /// survives save and drives the regenerated /AP), and the getter reads /DA back.</summary>
    public DefaultAppearance DefaultAppearance
    {
        get
        {
            if (_defaultAppearance is not null) return _defaultAppearance;
            var da = (Dict.Get("DA") as PdfString)?.ToText();
            return _defaultAppearance = (ParseDefaultAppearanceString(da) ?? new DefaultAppearance());
        }
        set
        {
            _defaultAppearance = value;
            if (value is not null)
                Dict.Set("DA", new PdfString(System.Text.Encoding.Latin1.GetBytes(SerializeDefaultAppearance(value))));
        }
    }

    /// <summary>Serialise a <see cref="DefaultAppearance"/> to a PDF /DA string
    /// (<c>/Font size Tf  r g b rg</c>).</summary>
    private static string SerializeDefaultAppearance(DefaultAppearance da)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.####", ci);
        var c = da.TextColor;
        return $"/{da.FontName} {F(da.FontSize)} Tf {F(c.R / 255.0)} {F(c.G / 255.0)} {F(c.B / 255.0)} rg";
    }

    /// <summary>Parse a /DA string back into a typed <see cref="DefaultAppearance"/>
    /// (font name, size and colour), or null when the string is empty/unparseable.</summary>
    private static DefaultAppearance? ParseDefaultAppearanceString(string? da)
    {
        if (string.IsNullOrEmpty(da)) return null;
        var p = da!.Split(new[] { ' ', '\n', '\t', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        string font = "Helvetica";
        double size = 12;
        var color = System.Drawing.Color.Black;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        for (int i = 0; i < p.Length; i++)
        {
            if (p[i] == "Tf" && i >= 2)
            {
                font = p[i - 2].TrimStart('/');
                double.TryParse(p[i - 1], System.Globalization.NumberStyles.Float, ci, out size);
            }
            else if (p[i] == "rg" && i >= 3
                && double.TryParse(p[i - 3], System.Globalization.NumberStyles.Float, ci, out var r)
                && double.TryParse(p[i - 2], System.Globalization.NumberStyles.Float, ci, out var g)
                && double.TryParse(p[i - 1], System.Globalization.NumberStyles.Float, ci, out var b))
            {
                color = System.Drawing.Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
            }
            else if (p[i] == "g" && i >= 1
                && double.TryParse(p[i - 1], System.Globalization.NumberStyles.Float, ci, out var gray))
            {
                int v = (int)(gray * 255);
                color = System.Drawing.Color.FromArgb(v, v, v);
            }
        }
        return new DefaultAppearance(font, size, color);
    }

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

    /// <summary>Action invoked when the widget is activated (/A entry). Backed by the
    /// annotation dictionary via <see cref="Actions"/> so it reads a loaded widget's
    /// action and assignments survive save → reload. When this dict is a field whose
    /// /A lives on its single widget kid, falls back to the kid's action (mirrors
    /// <see cref="Forms.Field.OnActivated"/> for callers holding the base type).</summary>
    public PdfAction? OnActivated
    {
        get
        {
            var action = Actions.OnActivated;
            if (action is not null) return action;
            var reader = InternalReader;
            if (reader is not null
                && reader.Resolve(Dict.Get("Kids")) is PdfArray { Count: 1 } kids)
            {
                var kid = reader.ResolveDict(kids[0]);
                var actionDict = reader.ResolveDict(kid?.Get("A"));
                if (actionDict is not null) return PdfAction.Create(actionDict, reader);
            }
            return null;
        }
        set => Actions.OnActivated = value;
    }

    private Forms.Field? _parentField;
    private bool _parentResolved;

    /// <summary>Parent <see cref="Forms.Field"/> when this widget is the
    /// visual child of an AcroForm field. Returns null when standalone. When not
    /// set explicitly, resolved from the widget's /Parent dictionary so a widget
    /// enumerated straight off a page still reports its owning field.</summary>
    public Forms.Field? Parent
    {
        get
        {
            if (_parentField is not null || _parentResolved) return _parentField;
            _parentResolved = true;
            var pd = InternalReader?.ResolveDict(Dict?.Get("Parent"));
            if (pd is not null)
                _parentField = Forms.Field.Create(pd, InternalReader!);
            return _parentField;
        }
        internal set { _parentField = value; _parentResolved = true; }
    }

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
    /// the annotation's Rect. Returned as a Point[] array
    /// where every two consecutive points form a rectangle's diagonal.</summary>
    public Point[]? QuadPoint
    {
        get
        {
            var arr = InternalReader.Resolve(Dict.Get("QuadPoints")) as PdfArray;
            // Return an empty array (not null) for an absent/short /QuadPoints so
            // callers can iterate the result without a null guard.
            if (arr is null || arr.Count < 8 || arr.Count % 2 != 0) return [];
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
                    // X+Y scoping pins the edit to this fragment's operator; an
                    // unscoped substring like a single letter would otherwise be
                    // deleted from every operator on the line.
                    var tr = new Text.TextReplacer { PreserveAdvanceOnDelete = true };
                    if (tf.HasExplicitPosition)
                    {
                        tr.TargetY = tf.Position!.YIndent;
                        tr.TargetX = tf.Position!.XIndent;
                    }
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

        EmitOverlayText(page, r);
    }

    /// <summary>Draw the redaction's /OverlayText as real, searchable page text so it survives the
    /// redaction (the content underneath was removed). It is laid into the first /QuadPoints quad
    /// (or the annotation rect when there are no quads), in Helvetica at the annotation font size
    /// (default 10), horizontally aligned per /Q, with the baseline one font-size below the quad
    /// top.</summary>
    private void EmitOverlayText(Page page, Rectangle r)
    {
        var overlay = OverlayText;
        if (string.IsNullOrEmpty(overlay)) return;

        var quads = QuadPoint;
        double minX, maxX, top;
        if (quads is { Length: >= 4 })
        {
            minX = Math.Min(Math.Min(quads[0].X, quads[1].X), Math.Min(quads[2].X, quads[3].X));
            maxX = Math.Max(Math.Max(quads[0].X, quads[1].X), Math.Max(quads[2].X, quads[3].X));
            top = Math.Max(Math.Max(quads[0].Y, quads[1].Y), Math.Max(quads[2].Y, quads[3].Y));
        }
        else { minX = r.LLX; maxX = r.URX; top = r.URY; }

        double fs = FontSize > 0 ? FontSize : 10;
        var ci = System.Globalization.CultureInfo.InvariantCulture;

        // The /DA string carries the authored overlay font and size
        // ("0.412 0.412 0.412 RG /ArialUnicodeMS 18 Tf").
        // Only the FACE is taken from /DA — the size stays the annotation's own, so
        // an overlay that was already being drawn keeps the metrics it laid out with.
        string? daFontName = null;
        var daMatch = System.Text.RegularExpressions.Regex.Match(
            DefaultAppearance ?? string.Empty, @"/(\S+)\s+([0-9.]+)\s+Tf");
        if (daMatch.Success) daFontName = daMatch.Groups[1].Value;

        // Overlay text beyond Latin-1 (CJK, combined diacritics) cannot ride the
        // WinAnsi Helvetica path below — those bytes flatten to '?'. Embed the /DA
        // font (resolved through FontRepository, so registered memory sources apply)
        // as a Type0/Identity-H composite with /ToUnicode, so the drawn text extracts
        // back verbatim. Latin-1-only overlays keep the legacy Helvetica emission.
        var needsUnicode = false;
        foreach (var ch in overlay) if (ch > 255) { needsUnicode = true; break; }
        var ttf = needsUnicode && daFontName is not null
            ? Aspose.Pdf.Text.FontRepository.GetTtfData(daFontName) : null;

        double w;
        string fontRes;
        string showOp;
        if (ttf is not null)
        {
            var fontDict = GetOrCreatePageFontDict(page);
            var (resName, hexIds) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                fontDict, ttf, daFontName!, overlay, stripSpacesInBaseFont: true);
            fontRes = resName;
            w = Aspose.Pdf.Text.Type0FontEmbedder.MeasureText(fontDict, ttf, daFontName!, overlay, fs);
            var hex = new System.Text.StringBuilder(hexIds.Length * 2 + 2);
            hex.Append('<');
            foreach (var bt in hexIds) hex.Append(bt.ToString("X2", ci));
            hex.Append('>');
            showOp = hex.ToString();
        }
        else
        {
            w = 0;
            foreach (char ch in overlay)
            {
                var cw = ch <= 255 ? Aspose.Pdf.Text.Standard14Fonts.GetWidth("Helvetica", ch) : 0;
                if (cw <= 0) cw = Aspose.Pdf.Text.Standard14Fonts.GetDefaultWidth("Helvetica");
                w += cw;
            }
            w = w * fs / 1000.0;
            fontRes = RegisterOverlayFont(page);
            showOp = "(" + overlay.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ")";
        }

        double tx = TextAlignment switch
        {
            HorizontalAlignment.Center => (minX + maxX) / 2 - w / 2,
            HorizontalAlignment.Right => maxX - w,
            _ => minX,
        };
        double baseline = top - fs;
        var tc = Color;

        string F(double v) => v.ToString("0.####", ci);
        var sb = new System.Text.StringBuilder();
        sb.Append("BT\n");
        sb.Append($"{F(tc.R / 255.0)} {F(tc.G / 255.0)} {F(tc.B / 255.0)} rg\n");
        sb.Append($"/{fontRes} {F(fs)} Tf\n");
        sb.Append($"1 0 0 1 {F(tx)} {F(baseline)} Tm\n");
        sb.Append($"{showOp} Tj\n");
        sb.Append("ET\n");
        page.AddContentStream(System.Text.Encoding.Latin1.GetBytes(sb.ToString()));
    }

    /// <summary>Get (or create) the page's /Resources/Font dictionary.</summary>
    private static PdfDictionary GetOrCreatePageFontDict(Page page)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) { resources = new PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) { fontDict = new PdfDictionary(); resources.Set("Font", fontDict); }
        return fontDict;
    }

    /// <summary>Register a WinAnsi Helvetica font on the page carrying a /FontDescriptor with the
    /// Standard-14 ascent/descent, and return its resource name (reusing an existing matching entry).
    /// The descriptor is what lets the text absorber report the overlay fragment at its descent line
    /// (baseline − descent) — a plain descriptor-less Helvetica would surface
    /// at the raw baseline.</summary>
    internal static string RegisterOverlayFont(Page page)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) { resources = new PdfDictionary(); page.Dict.Set("Resources", resources); }
        var fontDict = page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null) { fontDict = new PdfDictionary(); resources.Set("Font", fontDict); }

        foreach (var key in fontDict.Keys)
            if (page.Reader.Resolve(fontDict.Get(key)) is PdfDictionary ex
                && ex.GetName("BaseFont") == "Helvetica" && ex.Get("FontDescriptor") is not null)
                return key;

        var name = "FRov";
        int n = 0;
        while (fontDict.ContainsKey(name)) name = "FRov" + (++n);

        var desc = new PdfDictionary();
        desc.Set("Type", new PdfName("FontDescriptor"));
        desc.Set("FontName", new PdfName("Helvetica"));
        desc.Set("Flags", new PdfInteger(32));
        desc.Set("Ascent", new PdfInteger(718));
        desc.Set("Descent", new PdfInteger(-207));
        desc.Set("CapHeight", new PdfInteger(718));
        desc.Set("StemV", new PdfInteger(88));

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName("Helvetica"));
        font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        font.Set("FontDescriptor", desc);
        fontDict.Set(name, font);
        return name;
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
        // points) must survive.
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
        {
            // Same as the File setter: write the pending bytes into /EF so the
            // attachment's content (not just its name) survives save → reload.
            fileSpec.MaterializeEmbeddedStream();
            Dict.Set("FS", fileSpec.Dict);
        }
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
