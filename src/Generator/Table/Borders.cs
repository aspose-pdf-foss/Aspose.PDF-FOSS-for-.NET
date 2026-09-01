using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
    /// <summary>Form-grid border: the band paints INSIDE the given box, each side
    /// stroked over the box's FULL extent so the corners paint — the
    /// side lines run corner to corner; four inset segments would leave a notch
    /// at every corner.</summary>
    private static void DrawFormGridBorder(ContentStreamBuilder builder, BorderInfo border,
        double x, double y, double w, double h)
    {
        var half = border.Width / 2;
        DrawSide(builder, border, border.RawBottom, x, y + half, x + w, y + half);
        DrawSide(builder, border, border.RawTop, x, y + h - half, x + w, y + h - half);
        DrawSide(builder, border, border.RawLeft, x + half, y, x + half, y + h);
        DrawSide(builder, border, border.RawRight, x + w - half, y, x + w - half, y + h);
    }

    private static void DrawBorder(ContentStreamBuilder builder, BorderInfo border, double x, double y, double w, double h)
    {
        // A border authored with width 0 draws NOTHING — the generator treats it as
        // "no rule", not as a hairline (a spacer table built with
        // `new BorderInfo(BorderSide.Box, 0)` leaves no box at all). Only a border whose
        // sides carry no explicit stroke width of their own is silent: an assigned
        // GraphInfo with a positive LineWidth still paints.
        if (border.Width <= 0
            && !(border.RawTop?.LineWidth > 0) && !(border.RawBottom?.LineWidth > 0)
            && !(border.RawLeft?.LineWidth > 0) && !(border.RawRight?.LineWidth > 0))
            return;

        // A doubled side reserved its clearance in the box: the single rules draw around
        // the box that is left once those bands are taken off, and the second rules and
        // their end caps fill the bands.
        if (HasDoubledSide(border))
        {
            DrawDoubledBorder(builder, border, x, y, w, h, pitch: false);
            return;
        }

        // Rounded box: when a radius is set on a full-box border, stroke a single rounded-corner
        // rectangle path instead of four straight sides (BorderInfo.RoundedBorderRadius).
        if (border.RoundedBorderRadius > 0 && border.Side.HasFlag(BorderSide.Box))
        {
            DrawRoundedBox(builder, border, x, y, w, h);
            return;
        }

        // A uniformly-styled full box with a dash pattern is stroked as one continuous rectangle
        // path so the dashes wrap around the corners in phase — matching the generator.
        // Drawing the four sides separately would restart the dash at every corner and drift the
        // segments out of alignment with the template.
        if (border.Side.HasFlag(BorderSide.Box) && IsUniformBox(border) && border.RawTop?.DashArray is { Length: > 0 })
        {
            var gi = border.RawTop!;
            builder.SetLineWidth(gi.LineWidth);
            if (gi.StrokeColor is { } bsc)
                builder.SetStrokeColor(bsc.R, bsc.G, bsc.B);
            else
                builder.SetStrokeColor(border.Color);
            builder.SetDashPattern(Array.ConvertAll(gi.DashArray!, d => (double)d), gi.DashPhase);
            builder.Rectangle(x, y, w, h).Stroke();
            builder.SetDashPattern(Array.Empty<double>(), 0);
            return;
        }

        // A side draws when the Side flags name it OR its GraphInfo was
        // explicitly assigned (the generator enables a side on assignment);
        // the per-side GraphInfo supplies stroke styling either way.
        // Every side paints INSIDE the cell box: the stroke's outer edge sits
        // on the box edge (adjacent cells show abutting double bands). Probed
        // 2026-08-23 on a `new BorderInfo(BorderSide.Top)` cell rule: a 1 pt
        // rule centres 0.5 below the row top, a 0.3 pt one 0.15 below.
        double Inset(bool assigned, bool flagged, GraphInfo? gi) =>
            (gi?.LineWidth > 0 ? gi.LineWidth : border.Width) / 2;
        if (border.Side.HasFlag(BorderSide.Bottom) || border.BottomAssigned)
        {
            var ib = Inset(border.BottomAssigned, border.Side.HasFlag(BorderSide.Bottom), border.RawBottom);
            DrawSide(builder, border, border.RawBottom, x, y + ib, x + w, y + ib);
        }
        if (border.Side.HasFlag(BorderSide.Top) || border.TopAssigned)
        {
            var it = Inset(border.TopAssigned, border.Side.HasFlag(BorderSide.Top), border.RawTop);
            DrawSide(builder, border, border.RawTop, x, y + h - it, x + w, y + h - it);
        }
        if (border.Side.HasFlag(BorderSide.Left) || border.LeftAssigned)
        {
            var il = Inset(border.LeftAssigned, border.Side.HasFlag(BorderSide.Left), border.RawLeft);
            DrawSide(builder, border, border.RawLeft, x + il, y, x + il, y + h);
        }
        if (border.Side.HasFlag(BorderSide.Right) || border.RightAssigned)
        {
            var ir = Inset(border.RightAssigned, border.Side.HasFlag(BorderSide.Right), border.RawRight);
            DrawSide(builder, border, border.RawRight, x + w - ir, y, x + w - ir, y + h);
        }
    }

    /// <summary>The border every cell of the table assigns, when they all assign the
    /// same drawn width; null when the table has no border, mixes widths, or leaves any
    /// cell bare — a grid is only uniform when every cell draws the same rule.</summary>
    private BorderInfo? UniformAssignedCellBorder()
    {
        if (_uniformCellBorderResolved) return _uniformCellBorder;
        _uniformCellBorderResolved = true;
        BorderInfo? found = null;
        var cells = 0;
        for (var ri = 0; ri < Rows.Count; ri++)
        {
            var row = Rows.At(ri);
            for (var ci = 0; ci < row.Cells.Count; ci++)
            {
                var cb = row.Cells[ci].Border;
                if (cb is null || cb.Side == BorderSide.None) return _uniformCellBorder = null;
                cells++;
                if (found is null) { found = cb; continue; }
                if (Math.Abs(DrawnSideWidth(cb, BorderSide.Left, cb.LeftAssigned, cb.RawLeft)
                             - DrawnSideWidth(found, BorderSide.Left, found.LeftAssigned, found.RawLeft)) > 1e-6
                    || cb.Side != found.Side)
                    return _uniformCellBorder = null;
            }
        }
        return _uniformCellBorder = cells > 0 ? found : null;
    }

    /// <summary>True for a table the generator API built directly — no HTML/XML
    /// dialect flag set — where the probed cell model applies: border-in-pitch
    /// columns, baselines seated by the face's own descent, a text clip per cell,
    /// and nested grids rendered in place.</summary>
    private bool GeneratorCellModel => CellBorderInPitch && !XmlGeneratorModel && GeneratorDialect;

    /// <summary>The table was built through the generator API and carries no HTML dialect
    /// flag — the same population as <see cref="GeneratorCellModel"/> minus its
    /// requirement that a cell rule join the column pitch. The XML generator counts as
    /// one of these for the rules the two share (a source pixel is a point in both).</summary>
    private bool GeneratorDialect => !HtmlEngineMetrics && !UaCellBoxes && !CssRunBoxes
        && !HtmlLayoutWrap && !NestedTableRender && !FormGridCells && !HtmlOverDeclaredDraw
        && !DwFormCells && !RedlineCellSeat && !HtmlWrapInsetsCellMargins
        && !HonorCellFontFaces && !HonorCellTtfFaces;

    /// <summary>The Helvetica AFM descender (207/1000) — the lift every dialect seats
    /// cell baselines by when no face is named.</summary>
    private const double HelveticaDescentEm = 0.207;

    /// <summary>Height of one line of a cell's text clip, in ems of the line's font
    /// size (probed at 7–20 pt in Helvetica and Calibri: 11.6 for 10 pt, 23.2 for two
    /// 10 pt lines).</summary>
    private const double CellClipLineEm = 1.16;

    /// <summary>A superscript run extends the clip ABOVE the line box by this many ems,
    /// the same in both probed faces (13.0616 for a 10 pt Helvetica line).</summary>
    private const double SuperscriptClipAboveEm = 0.14616;

    /// <summary>Bound a cell's drawn text block with the generator's clip: <c>q q x y w h
    /// re W n … Q Q</c> spliced in at <paramref name="mark"/> (where the block started)
    /// and closed here. The box spans the cell's inner width, is
    /// <see cref="CellClipLineEm"/> per text line tall, and its bottom sits one face
    /// descent below the last baseline; sub/superscript runs extend it (see
    /// <see cref="SuperscriptClipAboveEm"/>). Runs are read back from the builder's
    /// baseline record, so every text path the cell drew through is covered.</summary>
    private static void EmitCellTextClip(ContentStreamBuilder builder, int mark, double x, double w,
        double descentEm, string? face, bool mixedSizes = false)
    {
        var shows = builder.TextShows;
        if (shows.Count == 0 || w <= 0) { builder.ResetTextExtent(); return; }
        double fs = 0;
        foreach (var s in shows) if (s.Size > fs) fs = s.Size;
        if (fs <= 0) { builder.ResetTextExtent(); return; }
        if (mixedSizes)
        {
            // Every show is a main line at its own size: the box runs from one line
            // height above the highest baseline to one descent below the lowest.
            double mixTop = double.NegativeInfinity, mixBottom = double.PositiveInfinity;
            foreach (var s in shows)
            {
                if (s.Size <= 0) continue;
                var t = s.Y + (CellClipLineEm - descentEm) * s.Size;
                var bo = s.Y - descentEm * s.Size;
                if (t > mixTop) mixTop = t;
                if (bo < mixBottom) mixBottom = bo;
            }
            if (mixTop > mixBottom)
            {
                builder.InsertAt(mark,
                    $"q\nq\n{Fmt(x)} {Fmt(mixBottom)} {Fmt(w)} {Fmt(mixTop - mixBottom)} re\nW\nn\n");
                builder.RestoreState().RestoreState();
            }
            builder.ResetTextExtent();
            return;
        }
        // Main-line runs keep (near) the line's size; a sub/superscript run is the
        // SubSuperScale shrink of it, seated off the line's baseline.
        var mainFloor = fs * 0.7;
        var baselines = new List<double>();
        var hasSub = false; var hasSup = false;
        foreach (var s in shows)
        {
            if (s.Size >= mainFloor)
            {
                // Segments of one line bottom-align on their own descents (a 13 pt run
                // beside a 15 pt one seats 0.5 pt lower), so a baseline is "the same
                // line" well inside a sub/superscript shift.
                var seen = false;
                foreach (var b in baselines) if (Math.Abs(b - s.Y) < 0.3 * fs) { seen = true; break; }
                if (!seen) baselines.Add(s.Y);
            }
        }
        if (baselines.Count == 0) { builder.ResetTextExtent(); return; }
        double lastBase = double.PositiveInfinity;
        foreach (var b in baselines) if (b < lastBase) lastBase = b;
        foreach (var s in shows)
        {
            if (s.Size >= mainFloor) continue;
            foreach (var b in baselines)
            {
                if (s.Y < b - 0.05 * fs && s.Y > b - fs) hasSub = true;
                if (s.Y > b + 0.05 * fs && s.Y < b + fs) hasSup = true;
            }
        }
        var bottom = lastBase - descentEm * fs - (hasSub ? SubscriptClipBelowEm(face) * fs : 0);
        // n line boxes of CellClipLineEm — the calibrated height, which assumes the
        // lines pitch no wider than their box. A cell whose paragraph declared a
        // LineSpacing pitches WIDER than that, so the measured baseline span plus one
        // line box is the real extent; taking the larger leaves every calibrated
        // (pitch ≤ box) cell byte-identical and stops a leaded cell clipping its own
        // opening lines away.
        double firstBase = double.NegativeInfinity;
        foreach (var b in baselines) if (b > firstBase) firstBase = b;
        var h = Math.Max(baselines.Count * CellClipLineEm * fs,
                         firstBase - lastBase + CellClipLineEm * fs)
            + (hasSup ? SuperscriptClipAboveEm * fs : 0)
            + (hasSub ? (SubscriptClipBelowEm(face) + SubscriptClipAboveEm(face, hasSup)) * fs : 0);
        builder.InsertAt(mark,
            $"q\nq\n{Fmt(x)} {Fmt(bottom)} {Fmt(w)} {Fmt(h)} re\nW\nn\n");
        builder.RestoreState().RestoreState();
        builder.ResetTextExtent();
        static string Fmt(double v) => Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture);
    }

    private const double SubscriptWithSuperscriptTopFactor = 1.9135;

    private static double SubscriptClipBelowEm(string? face) => SubscriptClip(face).Below;

    private static double SubscriptClipAboveEm(string? face, bool withSuperscript) =>
        SubscriptClip(face).Above * (withSuperscript ? SubscriptWithSuperscriptTopFactor : 1);

    /// <summary>Stroke width a border side paints with; 0 when the side does not draw.</summary>
    private static double DrawnSideWidth(BorderInfo b, BorderSide flag, bool assigned, GraphInfo? gi)
        => b.Side.HasFlag(flag) || assigned ? Math.Max(0, gi?.LineWidth > 0 ? gi.LineWidth : b.Width) : 0;

    /// <summary>Clearance a <see cref="GraphInfo.IsDoubled"/> side claims OUTSIDE the
    /// box its single rule would occupy: the second rule is drawn two line widths clear
    /// of the first, so the pair takes <c>w</c> (the gap) + <c>w</c> (the outer stroke)
    /// beyond it. Measured at three widths — a doubled box around a
    /// 100 pt column measures 106 pt outside to outside at 1 pt, 110 at 2 pt and 103 at
    /// 0.5 pt, i.e. <c>declared + 3w</c> on each doubled side.</summary>
    private static double DoubledOutset(BorderInfo b, BorderSide flag, bool assigned, GraphInfo? gi)
        => gi?.IsDoubled == true ? 2 * DrawnSideWidth(b, flag, assigned, gi) : 0;

    private static bool HasDoubledSide(BorderInfo? border)
    {
        if (border is null) return false;
        var (l, b, r, t) = DoubledOutsets(border);
        return l > 0 || b > 0 || r > 0 || t > 0;
    }

    /// <summary>Total width a border side takes from the cell box: its stroke plus any
    /// doubled clearance.</summary>
    private static double OccupiedSideWidth(BorderInfo b, BorderSide flag, bool assigned, GraphInfo? gi)
        => DrawnSideWidth(b, flag, assigned, gi) + DoubledOutset(b, flag, assigned, gi);

    /// <summary>Pitch-mode cell rules: a uniformly styled full box is stroked as ONE
    /// closed rectangle path (<c>re S</c>, as the generator emits it) so the corners
    /// join — four butt-capped side lines leave the corner square bare; partial
    /// boxes keep the per-side lines.</summary>
    private static void DrawPitchBorder(ContentStreamBuilder builder, BorderInfo border,
        double x, double y, double w, double h)
    {
        if (HasDoubledSide(border))
        {
            DrawDoubledBorder(builder, border, x, y, w, h, pitch: true);
            return;
        }
        if (border.Side == BorderSide.Box && IsUniformBox(border)
            && !(border.RawTop?.DashArray is { Length: > 0 }) && border.RoundedBorderRadius <= 0)
        {
            var width = border.RawTop?.LineWidth > 0 ? border.RawTop.LineWidth : border.Width;
            if (width <= 0) return;
            if (border.RawTop?.StrokeColor is { } sc) builder.SetStrokeColor(sc.R, sc.G, sc.B);
            else builder.SetStrokeColor(border.Color);
            builder.SetLineWidth(width);
            builder.Rectangle(x, y, w, h).Stroke();
            return;
        }
        DrawBorder(builder, border, x, y, w, h);
    }

    /// <summary>Vertical space a table's own border takes that
    /// <see cref="OuterBorderWidth"/> does not account for. That one reports the FULL
    /// BOX only, because the box is what insets the columns on every side. A rule on a
    /// single edge insets nothing but still occupies the page: a
    /// <c>BorderSide.Top</c> rule above the table is 0.5 pt of height, which is the
    /// difference between a 96 pt entry and the expected 96.5.</summary>
    private double EdgeBorderHeight()
    {
        if (Border is not { } b || b.Side.HasFlag(BorderSide.Box)) return 0;
        var (_, bot, _, top) = SideInsets(b, half: false);
        return top + bot;
    }

    private double OuterBorderWidth()
    {
        if (Border is not { } b || !b.Side.HasFlag(BorderSide.Box)) return 0;
        var w = b.RawTop?.LineWidth > 0 ? b.RawTop.LineWidth : b.Width;
        return w > 0 ? w : 0;
    }

    /// <summary>True when the four sides paint alike by VALUE — same stroke width, same
    /// colour, no dash on any of them. Weaker than <see cref="IsUniformBox"/>, which
    /// wants one shared GraphInfo instance; sides styled one at a time through the lazy
    /// per-side getters are separate objects that still draw the same rule.</summary>
    private static bool SidesStyledAlike(BorderInfo border)
    {
        var sides = new[] { border.RawTop, border.RawBottom, border.RawLeft, border.RawRight };
        double? width = null;
        Drawing.Color? color = null;
        var first = true;
        foreach (var gi in sides)
        {
            if (gi?.DashArray is { Length: > 0 }) return false;
            var w = gi?.LineWidth > 0 ? gi.LineWidth : border.Width;
            var c = gi?.StrokeColor;
            if (first) { width = w; color = c; first = false; continue; }
            if (Math.Abs(w - width!.Value) > 1e-6) return false;
            if (c is null != color is null) return false;
            if (c is { } cc && color is { } pc
                && (Math.Abs(cc.R - pc.R) > 1e-6 || Math.Abs(cc.G - pc.G) > 1e-6
                    || Math.Abs(cc.B - pc.B) > 1e-6))
                return false;
        }
        return true;
    }

    // True when every side carries the same styling — either no per-side GraphInfo at all, or the
    // single shared instance produced by the BorderInfo(BorderSide, GraphInfo) constructor.
    private static bool IsUniformBox(BorderInfo border)
    {
        var t = border.RawTop;
        return ReferenceEquals(t, border.RawBottom)
            && ReferenceEquals(t, border.RawLeft)
            && ReferenceEquals(t, border.RawRight);
    }

    /// <summary>Draw a border at least one of whose sides is <see cref="GraphInfo.IsDoubled"/>.
    /// The box handed in already carries each doubled side's clearance (see
    /// <see cref="DoubledOutset"/>), so the single rules go round the box that is left
    /// once the bands are taken off, and each doubled side adds a second rule on the
    /// band's outer edge plus, where the perpendicular side is NOT doubled, a short cap
    /// closing the band back onto the inner rule. Probed side by side against the
    /// reference across all five doubling combinations.</summary>
    private static void DrawDoubledBorder(ContentStreamBuilder builder, BorderInfo border,
        double x, double y, double w, double h, bool pitch)
    {
        var (ol, ob, or, ot) = DoubledOutsets(border);
        // Inner (single-rule) box: the outer box less every doubled side's band.
        double ix = x + ol, iy = y + ob, iw = w - ol - or, ih = h - ob - ot;
        if (iw <= 0 || ih <= 0) return;

        double SideW(BorderSide flag, bool assigned, GraphInfo? gi)
            => DrawnSideWidth(border, flag, assigned, gi);
        var wl = SideW(BorderSide.Left, border.LeftAssigned, border.RawLeft);
        var wr = SideW(BorderSide.Right, border.RightAssigned, border.RawRight);
        var wt = SideW(BorderSide.Top, border.TopAssigned, border.RawTop);
        var wb = SideW(BorderSide.Bottom, border.BottomAssigned, border.RawBottom);
        // Outer and inner box edges.
        double oL = x, oR = x + w, oB = y, oT = y + h;
        double iL = ix, iR = ix + iw, iB = iy, iT = iy + ih;

        // Every side doubled and identically styled: BOTH boxes close as one rectangle
        // path each (two `re S` under one prologue).
        // Four butt-capped lines would leave every corner bare. Reference equality is
        // too strict a uniformity test here — the four sides of a `new BorderInfo(All)`
        // whose IsDoubled flags were set one by one are separate, equal GraphInfos.
        if (ol > 0 && ob > 0 && or > 0 && ot > 0 && border.Side == BorderSide.Box
            && SidesStyledAlike(border) && border.RoundedBorderRadius <= 0)
        {
            if (border.RawTop?.StrokeColor is { } dsc) builder.SetStrokeColor(dsc.R, dsc.G, dsc.B);
            else builder.SetStrokeColor(border.Color);
            builder.SetLineWidth(wt);
            builder.Rectangle(iL + wl / 2, iB + wb / 2, iw - wl, ih - wb).Stroke();
            builder.Rectangle(oL + wl / 2, oB + wb / 2, w - wl, h - wb).Stroke();
            return;
        }

        // The inner box is stroked from a border whose sides are NOT doubled — a plain
        // clone shares the same GraphInfo instances and would recurse straight back here.
        var plain = border.WithoutDoubling();
        if (pitch) DrawPitchBorder(builder, plain, ix, iy, iw, ih);
        else DrawBorder(builder, plain, ix, iy, iw, ih);
        // Extents of the outer verticals: they stop against the OUTERMOST horizontal
        // rule present, exactly as the single box's sides stop against theirs.
        var vTop = (ot > 0 ? oT : iT) - wt;
        var vBot = (ob > 0 ? oB : iB) + wb;

        // Sides in a fixed order — top, right, bottom, left — and within each,
        // the cap at the rule's END point, the rule, then the cap at its START point.
        if (ot > 0)
        {
            var yOuter = oT - wt / 2;
            var yInner = iT - wt / 2;
            if (or <= 0) DrawSide(builder, border, border.RawTop, iR - wr / 2, yOuter, iR - wr / 2, yInner);
            DrawSide(builder, border, border.RawTop, oL, yOuter, oR, yOuter);
            if (ol <= 0) DrawSide(builder, border, border.RawTop, iL + wl / 2, yOuter, iL + wl / 2, yInner);
        }
        if (or > 0)
        {
            var xOuter = oR - wr / 2;
            var xInner = iR - wr / 2;
            if (ob <= 0) DrawSide(builder, border, border.RawRight, xInner, iB + wb / 2, xOuter, iB + wb / 2);
            DrawSide(builder, border, border.RawRight, xOuter, vTop, xOuter, vBot);
            if (ot <= 0) DrawSide(builder, border, border.RawRight, xInner, iT - wt / 2, xOuter, iT - wt / 2);
        }
        if (ob > 0)
        {
            var yOuter = oB + wb / 2;
            var yInner = iB + wb / 2;
            if (ol <= 0) DrawSide(builder, border, border.RawBottom, iL + wl / 2, yInner, iL + wl / 2, yOuter);
            DrawSide(builder, border, border.RawBottom, oR, yOuter, oL, yOuter);
            if (or <= 0) DrawSide(builder, border, border.RawBottom, iR - wr / 2, yInner, iR - wr / 2, yOuter);
        }
        if (ol > 0)
        {
            var xOuter = oL + wl / 2;
            var xInner = iL + wl / 2;
            if (ot <= 0) DrawSide(builder, border, border.RawLeft, xInner, iT - wt / 2, xOuter, iT - wt / 2);
            DrawSide(builder, border, border.RawLeft, xOuter, vBot, xOuter, vTop);
            if (ob <= 0) DrawSide(builder, border, border.RawLeft, xInner, iB + wb / 2, xOuter, iB + wb / 2);
        }
    }

    private static void DrawSide(ContentStreamBuilder builder, BorderInfo border, GraphInfo? gi,
        double x1, double y1, double x2, double y2)
    {
        builder.SetLineWidth(gi is not null ? gi.LineWidth : border.Width);
        if (gi?.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        else
            builder.SetStrokeColor(border.Color);

        var dash = gi?.DashArray;
        var dashed = dash is { Length: > 0 };
        if (dashed)
            builder.SetDashPattern(Array.ConvertAll(dash!, d => (double)d), gi!.DashPhase);

        builder.MoveTo(x1, y1).LineTo(x2, y2).Stroke();

        if (dashed)
            builder.SetDashPattern(Array.Empty<double>(), 0); // reset to a solid line
    }

    // 0.5523 ≈ (4/3)·(√2−1): the Bézier control-point ratio that approximates a quarter circle.
    private const double RoundCornerKappa = 0.5522847498307936;

    // Super/subscript segments in a cell render at a reduced size with a baseline shift
    // (fractions of the base font size), matching the generator's metrics.
    private const double SubSuperScale = 0.583;

    private const double SuperscriptRise = 0.421;

    private const double SubscriptRise = 0.245;

    private static void DrawRoundedBox(ContentStreamBuilder builder, BorderInfo border, double x, double y, double w, double h)
    {
        var gi = border.RawTop; // a box created from a GraphInfo shares one instance across all sides
        builder.SetLineWidth(gi is not null ? gi.LineWidth : border.Width);
        if (gi?.StrokeColor is { } sc)
            builder.SetStrokeColor(sc.R, sc.G, sc.B);
        else
            builder.SetStrokeColor(border.Color);

        var dash = gi?.DashArray;
        var dashed = dash is { Length: > 0 };
        if (dashed)
            builder.SetDashPattern(Array.ConvertAll(dash!, d => (double)d), gi!.DashPhase);

        // Clamp the radius so the corner arcs never overlap on a small box.
        var r = Math.Min(border.RoundedBorderRadius, Math.Min(w, h) / 2);
        var k = r * RoundCornerKappa;

        builder.MoveTo(x + r, y)
            .LineTo(x + w - r, y)
            .CurveTo(x + w - r + k, y, x + w, y + r - k, x + w, y + r) // bottom-right
            .LineTo(x + w, y + h - r)
            .CurveTo(x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h) // top-right
            .LineTo(x + r, y + h)
            .CurveTo(x + r - k, y + h, x, y + h - r + k, x, y + h - r) // top-left
            .LineTo(x, y + r)
            .CurveTo(x, y + r - k, x + r - k, y, x + r, y) // bottom-left
            .ClosePath()
            .Stroke();

        if (dashed)
            builder.SetDashPattern(Array.Empty<double>(), 0);
    }
}
