using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;
using GdiColor = System.Drawing.Color;
using GdiMatrix = System.Drawing.Drawing2D.Matrix;
using GraphicsState = Aspose.Pdf.Content.GraphicsState;
using GdiState = System.Drawing.Drawing2D.GraphicsState;

namespace Aspose.Pdf.Devices;

public sealed partial class GdiPlusPageRenderer : IPageRenderer
{
    /// <summary>Strokes the path with a solid pen: a device pen astronomically wider than the page is skipped, an anisotropic transform widens the outline and fills it instead, else the pen draws it; linear compositing when configured.</summary>
    private void StrokePathGdi(GraphicsPath path, GraphicsState state, int pageSpan)
    {
        var ctmScale = Math.Sqrt(Math.Abs(
            state.Ctm[0] * state.Ctm[3] - state.Ctm[1] * state.Ctm[2])) * _scale;
        var devPen = state.LineWidth * (ctmScale > 0 ? ctmScale : _scale);
        if (!Compat.IsFinite(devPen) || devPen > GeometrySanity(pageSpan))
            return;

        using var pen = BuildPen(state);
        // On transparency pages, composite the stroke in straight sRGB
        // — the page default (HighQuality = gamma-corrected) blends
        // partial-coverage stroke pixels visibly lighter over a painted backdrop, the
        // same reason the semi-transparent fill branch already forces AssumeLinear.
        var savedScq = _g.CompositingQuality;
        if (StrokeLinear) _g.CompositingQuality = CompositingQuality.AssumeLinear;
        // Under a non-uniform CTM the pen stays ROUND on the page, as wide as the
        // transform makes a horizontal unit: a vertical line under
        // `3.4 0 0 0.29 0 0 cm` comes out 3.4× its nominal width (manual-gradient
        // bands drawn as adjacent stroked lines rely on this to tile into a solid
        // fill), and so does the horizontal line beside it — a rectangle stroked
        // under `4 0 0 1 0 0 cm` gets a frame of one thickness all the way round.
        // GDI+ would carry the transform into the pen and make it elliptical, so for
        // strongly anisotropic CTMs the path is widened on the page instead and the
        // outline filled.
        bool drawnWidened = false;
        if (Environment.GetEnvironmentVariable("Q_ANISO") != "0"
            && state.LineWidth > 0 && PenGeometry.CtmAnisotropy(state.Ctm) > PenGeometry.AnisotropyLimit)
        {
            drawnWidened = FillRoundPenOutline(path, state, pen);
        }
        if (!drawnWidened)
            _g.DrawPath(pen, path);
        _g.CompositingQuality = savedScq;
    }

    /// <summary>Widen the path on the page with a round pen of the width the
    /// transform gives it, and fill what that outlines. Reports false when the
    /// outline could not be built, so the pen can draw instead.</summary>
    private bool FillRoundPenOutline(GraphicsPath path, GraphicsState state, Pen pen)
    {
        var devWidth = state.LineWidth * PenGeometry.CtmPenScale(state.Ctm) * _scale;
        if (!Compat.IsFinite(devWidth) || devWidth <= 0) return false;
        var saved = _g.Transform;
        try
        {
            using var widened = (GraphicsPath)path.Clone();
            widened.Transform(saved);
            using var devPen = new Pen(pen.Color, (float)devWidth)
            {
                StartCap = pen.StartCap,
                EndCap = pen.EndCap,
                LineJoin = pen.LineJoin,
                MiterLimit = pen.MiterLimit,
            };
            widened.Widen(devPen);
            _g.ResetTransform();
            using var sb = new SolidBrush(pen.Color);
            _g.FillPath(sb, widened);
            return true;
        }
        catch
        {
            // Widen can reject degenerate subpaths — fall back to the pen.
            return false;
        }
        finally
        {
            _g.Transform = saved;
            saved.Dispose();
        }
    }

    /// <summary>Fills the path with its flat colour: through the blended filler under a soft mask or a non-Normal blend, else a solid brush, a sub-pixel sliver painted as a faint rectangle, and the aliased-fill mode on the exact world matrix when enabled.</summary>
    private void FillPathFlat(GraphicsPath path, GdiMatrix world, RectangleF pathBounds, GraphicsState state)
    {
        var blend = Rasterizer.BlendModes.Parse(state.BlendMode);
        var softMask = state.SoftMask is { } sm ? GetSoftMaskAlpha(sm) : null;
        if (softMask is not null)
        {
            // An ExtGState soft mask modulates this fill's coverage per pixel
            // (PDF 32000 §11.6.5.4); composite by hand through the mask's alpha.
            FillPathBlended(path, world, blend, state, softMask);
        }
        else if (blend != Rasterizer.BlendMode.Normal)
        {
            // GDI+ has no per-pixel PDF blend modes, so composite the fill into
            // the backing bitmap by hand (PDF 32000 §11.3.5). Scoped to the rare
            // non-Normal case; Normal fills keep the fast native path below.
            FillPathBlended(path, world, blend, state);
        }
        else
        {
            using var brush = new SolidBrush(ColorFrom(state.FillR, state.FillG, state.FillB, state.FillAlpha));
            // A sub-pixel fill renders at its TRUE geometric coverage — the
            // reference rasterizer draws a 0.24pt frame rule at 150 dpi as the
            // 159/223 coverage split, never as a solid 1px bar (probed with a
            // 0.03..1pt bar ladder at 150 and 300 dpi: coverage is exact, with
            // a floor of ~1/8 px so a vanishingly thin rule stays faintly
            // visible instead of dissolving to nothing).
            var db = pathBounds;
            var thinnest = Math.Min(db.Width, db.Height);
            if (db.Width > 0f && db.Height > 0f && thinnest < 1f)
            {
                // GDI+'s own AA is unreliable below one device pixel (a
                // 0.2px-tall rule can dissolve to nothing), so sub-pixel
                // fills draw as a deterministic >=1px bar whose ALPHA is the
                // geometric coverage (floored at ~1/8 so a vanishingly thin
                // rule stays faintly visible) - the probed reference law is
                // true coverage, never a solid bump.
                using var faint = new SolidBrush(ColorFrom(state.FillR, state.FillG, state.FillB,
                    state.FillAlpha * Math.Max(thinnest, 0.125f)));
                var cur = _g.Transform;
                _g.ResetTransform();
                _g.FillRectangle(faint, db.X, db.Y, Math.Max(db.Width, 1f), Math.Max(db.Height, 1f));
                _g.Transform = cur;
                cur.Dispose();
            }
            else
            {
                // Blend a semi-transparent fill in straight sRGB.
                // The page keeps gamma-corrected compositing for text AA;
                // applying it to /ca fills composites them visibly lighter than the
                // platform convention. Scoped to the shape-fill call so glyph
                // rendering is unaffected.
                // A near-white opaque fill is composited the same way: gamma-corrected
                // coverage blending darkens even a white-over-white anti-aliased edge
                // by one level (a white redaction rect over a white scan reads 0xFEFEFE
                // at its border); a straight-sRGB blend of coverage α is α·255+(1−α)·255
                // = 255 exactly, so the halo disappears.
                bool nearWhiteFill = state.FillR > 0.99 && state.FillG > 0.99 && state.FillB > 0.99;
                var savedCq = _g.CompositingQuality;
                if (state.FillAlpha < 0.999 || nearWhiteFill) _g.CompositingQuality = CompositingQuality.AssumeLinear;
                if (AliasedVectorFills)
                {
                    // Aliased fill rule: run start = ceil(edge·s) inclusive-on-
                    // exact, run end = ceil(edge·s) exclusive, at the EXACT
                    // dpi/72 device mapping (AliasedWorldMatrix). GDI+'s non-AA
                    // rasterization under PixelOffsetMode.None implements that
                    // corner-lattice rule natively (calibrated on a 220-dpi bar
                    // page: 43/43 edges + the vertical runs land exactly); the
                    // Q_BCSHIFT/Q_BCPOM knobs exist to recalibrate the rule
                    // if a counter-example shows up.
                    var savedSm2 = _g.SmoothingMode;
                    var savedPom2 = _g.PixelOffsetMode;
                    var savedTx2 = _g.Transform;
                    _g.SmoothingMode = SmoothingMode.None;
                    _g.PixelOffsetMode = BcPomNone ? PixelOffsetMode.None : PixelOffsetMode.Half;
                    using (var exactWorld = AliasedWorldMatrix(state.Ctm))
                    {
                        exactWorld.Translate(BcShift.dx, BcShift.dy, MatrixOrder.Append);
                        _g.Transform = exactWorld;
                        _g.FillPath(brush, path);
                    }
                    _g.Transform = savedTx2;
                    savedTx2.Dispose();
                    _g.SmoothingMode = savedSm2;
                    _g.PixelOffsetMode = savedPom2;
                }
                else
                    _g.FillPath(brush, path);
                _g.CompositingQuality = savedCq;
            }
        }
    }
}
