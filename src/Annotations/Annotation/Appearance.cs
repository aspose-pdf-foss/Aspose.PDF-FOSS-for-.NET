using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class Annotation
{
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

    /// <summary>Whether to regenerate the appearance stream when an annotation is saved
    /// into a converted document. A global flag (static), set via
    /// <c>Annotation.UpdateAppearanceOnConvert</c> / <c>Field.UpdateAppearanceOnConvert</c>.
    /// Stored only.</summary>
    public static bool UpdateAppearanceOnConvert { get; set; } = true;

    /// <summary>Whether the embedded font (if any) should be subsetted.
    /// Static global toggle; stored only — the
    /// appearance writer always embeds the full referenced glyph set.</summary>
    public static bool UseFontSubset { get; set; }

    /// <summary>Drop the cached appearance/state views so a subsequent access
    /// rebuilds from the current /AP.</summary>
    private protected void InvalidateAppearanceCache()
    {
        _appearance = null;
        _states = null;
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

    /// <summary>True when <paramref name="annotation"/> is a shape/markup annotation whose
    /// <see cref="UpdateAppearances"/> draws the figure from its own geometry, so a file
    /// that stores it WITHOUT an /AP (leaving the viewer to synthesise one) can still be
    /// flattened or rendered. Shared by the per-annotation and the whole-page flatten
    /// paths — a subtype missing from one of them silently vanishes on that path.</summary>
    internal static bool CanSynthesiseAppearance(Annotation annotation) =>
        annotation is SquareAnnotation or CircleAnnotation or TextAnnotation
                   or HighlightAnnotation or PolyAnnotation or LineAnnotation;

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
}
