using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

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
            _ => Color.FromArgb(0, 0, 0),
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
