using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

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

        var startEnding = GetLineEnding(0);
        var endEnding = GetLineEnding(1);
        bool startArrow = IsArrowEnding(startEnding);
        bool endArrow = IsArrowEnding(endEnding);
        // ★ A SLASH ending draws NO head — measured on the flattened
        // appearance, which emits the shaft alone — but it still SHORTENS the shaft by one
        // line width at that end, exactly as an arrow ending does. Drawing a cap there
        // (PDF 32000-1 §12.5.6.7 describes one) puts ink on the page that the expected
        // appearance does not carry.
        bool startInset = startArrow || startEnding == LineEnding.Slash;
        bool endInset = endArrow || endEnding == LineEnding.Slash;

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(Color);
        b.SetLineWidth(lw);
        if (dash is not null) b.SetDashPattern(dash);

        // Shaft, pulled back by lw at any arrowed end so it meets the head vertex.
        var (ax, ay) = MovePointToward(s.X, s.Y, e.X, e.Y, startInset ? lw : 0);
        var (bx, by) = MovePointToward(e.X, e.Y, s.X, s.Y, endInset ? lw : 0);
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

    /// <summary>True when a Measure DOM was materialised for this line —
    /// the add-to-page flush serialises it into /Measure then.</summary>
    internal bool HasMeasure => _measure is not null;

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
