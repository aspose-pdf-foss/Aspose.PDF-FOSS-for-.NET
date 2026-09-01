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
