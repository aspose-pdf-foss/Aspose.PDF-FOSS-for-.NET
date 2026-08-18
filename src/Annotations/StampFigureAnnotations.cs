using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

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
    /// and returned as a PNG stream.</summary>
    public System.IO.Stream? Image
    {
        get => _image ?? ExtractAppearanceImage();
        set
        {
            _image = value;
            // Embed the image into the normal appearance at its native resolution so the
            // stamp renders and round-trips through save (a reopened stamp's Image then
            // extracts the full-size source rather than nothing).
            if (value is not null) BuildImageAppearance(value);
        }
    }

    /// <summary>Generate the normal appearance (/AP /N) as a Form XObject that draws
    /// <paramref name="image"/> at native resolution, scaled to fill the stamp rectangle.
    /// The image XObject keeps the source pixel dimensions (DCTDecode pass-through for JPEG),
    /// so the resolution survives the save/reload round-trip.</summary>
    private void BuildImageAppearance(System.IO.Stream image)
    {
        var r = Rect;
        if (r is null) return;
        var w = r.URX - r.LLX;
        var h = r.URY - r.LLY;
        if (w <= 0 || h <= 0) return;

        byte[] bytes;
        if (image.CanSeek) image.Seek(0, System.IO.SeekOrigin.Begin);
        using (var ms = new System.IO.MemoryStream()) { image.CopyTo(ms); bytes = ms.ToArray(); }
        if (image.CanSeek) image.Seek(0, System.IO.SeekOrigin.Begin);
        if (bytes.Length == 0) return;

        Core.PdfStream imgXObject;
        try { imgXObject = new Aspose.Pdf.ImageStamp(new System.IO.MemoryStream(bytes)).BuildImageXObject(); }
        catch { return; } // not a decodable image — leave the stored stream untouched

        static string F(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        // Map the unit image space into the BBox: q w 0 0 h 0 0 cm /Im0 Do Q.
        var content = System.Text.Encoding.ASCII.GetBytes($"q {F(w)} 0 0 {F(h)} 0 0 cm /Im0 Do Q");

        var form = new Core.PdfDictionary();
        form.Set("Type", new Core.PdfName("XObject"));
        form.Set("Subtype", new Core.PdfName("Form"));
        form.Set("FormType", new Core.PdfInteger(1));
        var bb = new Core.PdfArray();
        bb.Add(new Core.PdfReal(0)); bb.Add(new Core.PdfReal(0));
        bb.Add(new Core.PdfReal(w)); bb.Add(new Core.PdfReal(h));
        form.Set("BBox", bb);

        var xobjs = new Core.PdfDictionary();
        xobjs.Set("Im0", imgXObject);
        var res = new Core.PdfDictionary();
        res.Set("XObject", xobjs);
        form.Set("Resources", res);
        form.Set("Length", new Core.PdfInteger(content.Length));

        var ap = InternalReader.ResolveDict(Dict.Get("AP")) ?? new Core.PdfDictionary();
        ap.Set("N", new Core.PdfStream(form, content));
        Dict.Set("AP", ap);
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

    /// <summary>Regenerate the normal appearance (/AP /N) by stroking every
    /// /InkList path with the annotation colour and border width. The
    /// appearance BBox (and /Rect) inflate by half the stroke width so a thick
    /// stroke and its round caps are not clipped at the path extremes.</summary>
    public override void UpdateAppearances()
    {
        var strokes = InkList;
        if (strokes.Count == 0) return;
        double lw = GetBorderWidthValue();
        if (lw <= 0) lw = 1;

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        b.SetLineWidth(lw);
        if (CapStyle == CapStyle.Rounded) { b.SetLineCap(1); b.SetLineJoin(1); }
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var stroke in strokes)
        {
            if (stroke.Length == 0) continue;
            b.MoveTo(stroke[0].X, stroke[0].Y);
            for (var i = 1; i < stroke.Length; i++) b.LineTo(stroke[i].X, stroke[i].Y);
            b.Stroke();
            foreach (var p in stroke)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        b.RestoreState();
        if (double.IsInfinity(minX)) return;

        var half = lw / 2.0;
        var bbox = new Rectangle(minX - half, minY - half, maxX + half, maxY + half);
        var rArr = new PdfArray();
        rArr.Add(new PdfReal(bbox.LLX)); rArr.Add(new PdfReal(bbox.LLY));
        rArr.Add(new PdfReal(bbox.URX)); rArr.Add(new PdfReal(bbox.URY));
        Dict.Set("Rect", rArr);
        SetNormalAppearance(b.Build(), bbox);
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
    /// `new` so DeclaredOnly reflection sees it on LineAnnotation directly.</summary>
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
    // Open-arrow head proportions relative to the line width:
    // for width w the V vertex sits 1.328·w behind the line
    // endpoint, the wings 5.894·w behind and ±1.573·w off-axis, and the shaft is
    // pulled back 1·w so it meets the head. Verified for w = 1, 3, 5.
    private const double ArrowApex = 1.328, ArrowBack = 5.894, ArrowHalf = 1.573;

    public override void UpdateAppearances()
    {
        if (Rect is null) return;
        var s = Starting; var e = Ending;
        double w = GetBorderWidthValue();
        double lw = w <= 0 ? 1 : w;

        double[]? dash = null;
        if (GetBorderStyleValue() == Aspose.Pdf.Annotations.BorderStyle.Dashed
            && GetBorderDashValue() is { Length: > 0 } dp)
            dash = System.Array.ConvertAll(dp, v => (double)v);

        bool startArrow = IsArrowEnding(GetLineEnding(0));
        bool endArrow = IsArrowEnding(GetLineEnding(1));

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        b.SetLineWidth(lw);
        if (dash is not null) b.SetDashPattern(dash);

        // Shaft, pulled back by lw at any arrowed end so it meets the head vertex.
        var (ax, ay) = MovePointToward(s.X, s.Y, e.X, e.Y, startArrow ? lw : 0);
        var (bx, by) = MovePointToward(e.X, e.Y, s.X, s.Y, endArrow ? lw : 0);
        b.MoveTo(ax, ay); b.LineTo(bx, by); b.Stroke();

        if (endArrow) DrawOpenArrowHead(b, s.X, s.Y, e.X, e.Y, lw);
        if (startArrow) DrawOpenArrowHead(b, e.X, e.Y, s.X, s.Y, lw);
        b.RestoreState();

        // Grow the annotation rectangle (and the appearance BBox, which maps 1:1 to
        // it) 10·w around the segment so the arrow head and a thick stroke are never
        // cropped when the appearance is placed — matching the expected /Rect.
        double minX = System.Math.Min(s.X, e.X), maxX = System.Math.Max(s.X, e.X);
        double minY = System.Math.Min(s.Y, e.Y), maxY = System.Math.Max(s.Y, e.Y);
        double m = 10 * lw;
        var bbox = new Rectangle(minX - m, minY - m, maxX + m, maxY + m);

        var rArr = new PdfArray();
        rArr.Add(new PdfReal(bbox.LLX)); rArr.Add(new PdfReal(bbox.LLY));
        rArr.Add(new PdfReal(bbox.URX)); rArr.Add(new PdfReal(bbox.URY));
        Dict.Set("Rect", rArr);

        SetNormalAppearance(b.Build(), bbox);
    }

    private static bool IsArrowEnding(LineEnding le) =>
        le is LineEnding.OpenArrow or LineEnding.ClosedArrow
           or LineEnding.ROpenArrow or LineEnding.RClosedArrow;

    // Move (px,py) toward (qx,qy) by dist points (no-op for dist ≤ 0 or coincident points).
    private static (double, double) MovePointToward(double px, double py, double qx, double qy, double dist)
    {
        if (dist <= 0) return (px, py);
        double dx = qx - px, dy = qy - py;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return (px, py);
        return (px + dx / len * dist, py + dy / len * dist);
    }

    // Stroke an open-V arrow head at (tipX,tipY) pointing away from (fromX,fromY).
    private static void DrawOpenArrowHead(Content.ContentStreamBuilder b,
        double fromX, double fromY, double tipX, double tipY, double w)
    {
        double dx = tipX - fromX, dy = tipY - fromY;
        double len = System.Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9) return;
        double ux = dx / len, uy = dy / len;   // unit from->tip
        double vx = -uy, vy = ux;               // perpendicular
        double apexX = tipX - ArrowApex * w * ux, apexY = tipY - ArrowApex * w * uy;
        // Emit the wings in a fixed order (−v wing first) so the dash pattern,
        // which runs along the stroked path, lands on stable positions.
        double w1x = tipX - ArrowBack * w * ux - ArrowHalf * w * vx;
        double w1y = tipY - ArrowBack * w * uy - ArrowHalf * w * vy;
        double w2x = tipX - ArrowBack * w * ux + ArrowHalf * w * vx;
        double w2y = tipY - ArrowBack * w * uy + ArrowHalf * w * vy;
        b.MoveTo(w1x, w1y); b.LineTo(apexX, apexY); b.LineTo(w2x, w2y); b.Stroke();
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

    internal static string LineEndingToName(LineEnding le) => le switch
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

    internal static LineEnding ParseLineEnding(string? name) => name switch
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

    /// <summary>Resize-with-normalization helper (PdfFileEditor.ResizeContents): regenerate
    /// the figure's /N appearance, and when <see cref="UpdateAppearances"/> draws nothing —
    /// a colourless figure or a collapsed rectangle (e.g. a zero-area Square used as a text
    /// anchor) — still emit a minimal valid but empty appearance form so the annotation
    /// carries a normalized /N instead of a degenerate/absent one. Flatten and normal
    /// rendering keep the visibility-gated <see cref="UpdateAppearances"/> behaviour.</summary>
    internal void EnsureNormalizedAppearance()
    {
        UpdateAppearances();
        var na = NormalAppearance;
        if (na is not null && na.Contents.Count > 0) return;

        var r = Rect ?? new Rectangle(0, 0, 0, 0);
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.RestoreState();
        SetNormalAppearance(b.Build(), r);
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
        // The appearance itself stays OPAQUE: the annotation's /CA is applied by the
        // renderer (and by viewers) when the appearance is drawn — baking it here
        // would apply the opacity twice. The flatten path, which removes the
        // annotation, wraps the stamped appearance in its own /CA graphics state.
        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        var width = GetBorderWidthValue();
        if (width != 1) b.SetLineWidth(width);
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
