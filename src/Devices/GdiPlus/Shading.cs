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

public sealed partial class GdiPlusPageRenderer
{
    private void DrawShading(string name, GraphicsState state)
    {
        if (_scope.Shadings is null) return;
        var obj = _scope.Shadings.Get(name);
        if (obj is null) return;
        var shading = ShadingBase.Parse(obj, _reader);

        // A shading dictionary's optional /BBox (PDF 32000 §8.7.4.3, Table 78) is applied as a
        // temporary clipping boundary — expressed in the shading's target (current user) space —
        // while the shading is painted. The bare `sh` operator otherwise fills the whole current
        // clip; without honouring this BBox a banner gradient defined for a narrow band floods
        // the entire page and obscures all content drawn beneath it.
        System.Drawing.Region? savedClip = null;
        if (shading?.BBox is { Length: >= 4 } bb)
        {
            savedClip = _g.Clip;
            var savedT = _g.Transform;
            using (var world = WorldMatrix(state.Ctm))
            {
                _g.Transform = world;
                float bx = (float)Math.Min(bb[0], bb[2]), by = (float)Math.Min(bb[1], bb[3]);
                float bw = (float)Math.Abs(bb[2] - bb[0]), bh = (float)Math.Abs(bb[3] - bb[1]);
                _g.IntersectClip(new RectangleF(bx, by, bw, bh));
            }
            _g.Transform = savedT;
        }
        try
        {
            // The bare `sh` operator paints the current clip region with the shading. Honour it
            // for axial gradients (e.g. a panel filled by `… re W n … /Sh0 sh`). Under an
            // ExtGState soft mask the paint composites per pixel through the mask's alpha
            // (gradient overlap shadows); if the mask fails to resolve, keep
            // the historical skip — an opaque fill over the content beneath is worse than
            // the missing overlay.
            if (shading is AxialShading ax)
            {
                var axMask = state.SoftMask is { } axSm ? GetSoftMaskAlpha(axSm) : null;
                if (state.SoftMask is null || axMask is not null)
                    DrawAxialShading(ax, state, fillRegion: true, bareSh: true, softMask: axMask);
            }
            else if (shading is RadialShading ra) DrawRadialShading(ra, state);
            // Mesh shadings (Types 4-7): tessellate the triangle/patch geometry and flat-fill
            // each cell with its interpolated colour. The bare `sh` paints the current clip,
            // so an active soft mask is left to compose elsewhere rather than painted opaque.
            else if (state.SoftMask is null
                && shading is FreeFormGouraudShading or LatticeFormGouraudShading
                            or CoonsPatchShading or TensorPatchShading)
                DrawMeshShading(shading, state);
            // Function-based (Type 1) shadings are not yet translated to GDI+ brushes.
        }
        finally
        {
            if (savedClip is not null) { _g.Clip = savedClip; savedClip.Dispose(); }
        }
    }

    private const int ShadingStops = 64;

    private static readonly bool ShDebug2 = Environment.GetEnvironmentVariable("Q_SH_DEBUG") == "1";

    private GdiColor[]? SampleShading(Functions.PdfFunction? fn, double[] domain, string cs, byte alpha,
        Functions.PdfFunction? tint = null, string? altName = null)
    {
        if (fn is null) { if (ShDebug2) Console.WriteLine("[shs] fn null"); return null; }
        double lo = domain.Length > 0 ? domain[0] : 0;
        double hi = domain.Length > 1 ? domain[1] : 1;
        var colors = new GdiColor[ShadingStops];
        var input = new double[1];
        for (int i = 0; i < ShadingStops; i++)
        {
            double t = i / (double)(ShadingStops - 1);
            input[0] = lo + t * (hi - lo);
            var col = fn.Evaluate(input);
            if (col is null) { if (ShDebug2) Console.WriteLine($"[shs] eval null at t={t}"); return null; }
            SoftwarePageRenderer.ComponentsToRgb(col, cs, out var r, out var g, out var b, tint, altName);
            colors[i] = GdiColor.FromArgb(alpha, r, g, b);
        }
        if (ShDebug2) Console.WriteLine($"[shs] cs={cs} alt={altName} c0={colors[0]} c63={colors[^1]}");
        return colors;
    }

    private void DrawAxialShading(AxialShading ax, GraphicsState state, bool fillRegion = false, bool bareSh = false,
        byte[]? softMask = null)
    {
        // Only paint when filling a defined region (a shading-pattern fill clipped to its
        // path). The bare `sh` operator paints the whole current clip; some documents use it
        // for opaque field-highlight overlays that, without the transparency they carry,
        // would cover content drawn under them. Leave that path until soft masks compose.
        if (!fillRegion) return;
        // A bare `sh` shading in a MULTI-SPOT ink space (DeviceN, or another tint space
        // resolving to CMYK) is ink laid over the page — a `sh` vignette painted over a
        // photo, say. Composite it with an overprint Multiply so its no-ink end (which
        // converts to white) leaves the content beneath unchanged instead of knocking it
        // out; over bare paper Multiply equals an opaque paint. A SPOT-colour
        // (/Separation) shading is the opposite case: a decorative panel whose plate
        // replaces whatever sits under it, so it keeps the opaque paint. Plain
        // process-CMYK (DeviceCMYK) bare shadings are opaque paint too: a gradient bar
        // authored directly in process inks replaces the flat fill beneath it — its
        // to-white end must land WHITE, not "flat fill showing through" (Multiply would
        // preserve the base ink and shift the whole panel's colour).
        bool subtractive = bareSh && (ax.ColorSpaceName is "DeviceN"
                                      || (ax.ColorSpaceName is not "Separation" and not "DeviceCMYK"
                                          && ax.AltSpaceName is "DeviceCMYK"));
        // The masked path applies /ca per pixel in the composite loop; keep the
        // sampled stops opaque so scratch coverage stays the clip's own AA.
        var alpha = softMask is null ? (byte)Clamp255(state.FillAlpha) : (byte)255;
        var colors = SampleShading(ax.Function, ax.Domain, ax.ColorSpaceName, alpha, ax.TintTransform, ax.AltSpaceName);
        if (colors is null) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        try
        {
            var p0 = new PointF((float)ax.X0, (float)ax.Y0);
            var p1 = new PointF((float)ax.X1, (float)ax.Y1);
            var dx = p1.X - p0.X; var dy = p1.Y - p0.Y;
            if (dx * dx + dy * dy < 1e-6) return;
            // GDI+ LinearGradientBrush throws "Parameter is not valid" for a perfectly
            // axis-aligned line (zero-width/height brush rectangle). Nudge the end point a
            // hair off-axis - a hundredth of a DEVICE pixel, so the tilt stays negligible
            // however large the shading space's unit is: a gradient authored on a unit axis
            // that its cm scales to a 108 pt band would skew by 6% across the band under a
            // fixed nudge of a hundredth of a shading unit.
            var scale = Math.Sqrt(Math.Abs(world.Elements[0] * world.Elements[3] - world.Elements[1] * world.Elements[2]));
            float nudge = (float)(scale > 1e-9 ? 1e-2 / scale : 1e-2);
            if (Math.Abs(dx) < 1e-3) p1.X += nudge;
            if (Math.Abs(dy) < 1e-3) p1.Y += nudge;

            var bounds = _g.ClipBounds;
            if (ShDebug2) Console.WriteLine($"[axial] ctm=({state.Ctm[0]:0.##},{state.Ctm[3]:0.##},{state.Ctm[4]:0.##},{state.Ctm[5]:0.##}) bounds={bounds} subtractive={subtractive}");
            if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 100000 || bounds.Height > 100000) { if (ShDebug2) Console.WriteLine("[axial] bounds reject"); return; }

            // Emulate /Extend (hold edge colours beyond the axis). GDI+ LinearGradientBrush
            // has no Clamp wrap mode (setting it throws), so extend the gradient line to
            // span the fill bounds' projection onto the axis and pad the stops with the end
            // colours. Then the default tiling never shows: the [0,1] gradient occupies its
            // sub-range and the flat edge colours fill the rest.
            float ux = p1.X - p0.X, uy = p1.Y - p0.Y;
            float len2 = ux * ux + uy * uy;
            float tmin = 0f, tmax = 1f;
            foreach (var c in new[] { new PointF(bounds.Left, bounds.Top), new PointF(bounds.Right, bounds.Top),
                                       new PointF(bounds.Left, bounds.Bottom), new PointF(bounds.Right, bounds.Bottom) })
            {
                float t = ((c.X - p0.X) * ux + (c.Y - p0.Y) * uy) / len2;
                if (t < tmin) tmin = t;
                if (t > tmax) tmax = t;
            }
            var ep0 = new PointF(p0.X + tmin * ux, p0.Y + tmin * uy);
            var ep1 = new PointF(p0.X + tmax * ux, p0.Y + tmax * uy);
            float span = tmax - tmin;
            using var brush = new LinearGradientBrush(ep0, ep1, colors[0], colors[^1]);
            var bcol = new List<GdiColor>();
            var bpos = new List<float>();
            float prev = -1f;
            void Add(float pp, GdiColor cc) { pp = Math.Clamp(pp, 0f, 1f); if (pp <= prev) pp = prev + 1e-4f; if (pp > 1f) pp = 1f; if (pp <= prev) return; bpos.Add(pp); bcol.Add(cc); prev = pp; }
            // /Extend controls whether the area beyond each axis end is painted: hold the
            // edge colour where true, leave transparent (hard stop) where false.
            var clear = GdiColor.FromArgb(0, 0, 0, 0);
            float t0 = -tmin / span, t1 = (1f - tmin) / span; // mapped positions of axis ends
            if (ax.Extend[0]) Add(0f, colors[0]);
            else { Add(0f, clear); if (t0 > 2e-3f) Add(t0 - 1e-3f, clear); }
            for (int i = 0; i < colors.Length; i++)
                Add((i / (float)(colors.Length - 1) - tmin) / span, colors[i]);
            if (ax.Extend[1]) Add(1f, colors[^1]);
            else { if (t1 < 1f - 2e-3f) Add(t1 + 1e-3f, clear); Add(1f, clear); }
            brush.InterpolationColors = new ColorBlend(bcol.Count) { Colors = bcol.ToArray(), Positions = bpos.ToArray() };
            if (softMask is not null)
                MaskedBrushFill(brush, bounds, world, softMask, state.FillAlpha);
            else if (subtractive)
                MultiplyBrushFill(brush, bounds, world);
            else
                _g.FillRectangle(brush, bounds);
        }
        catch { /* GDI+ rejects some degenerate gradients */ }
        finally { _g.Transform = saved; }
    }

    /// <summary>Fill the current clip with <paramref name="brush"/> through an overprint-style
    /// Multiply (out = dst·src/255) rather than an opaque paint, so a subtractive `sh` overlay
    /// tints the content beneath instead of knocking it out. Mirrors <see cref="BlitImageMultiply"/>
    /// but for a brush fill; <paramref name="boundsWorld"/> is in the current (world) space and
    /// <paramref name="world"/> is the active device transform.</summary>
    private void MultiplyBrushFill(System.Drawing.Brush brush, RectangleF boundsWorld, GdiMatrix world)
    {
        int w = _bitmap.Width, h = _bitmap.Height;
        var corners = new[]
        {
            new PointF(boundsWorld.Left, boundsWorld.Top), new PointF(boundsWorld.Right, boundsWorld.Top),
            new PointF(boundsWorld.Left, boundsWorld.Bottom), new PointF(boundsWorld.Right, boundsWorld.Bottom),
        };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(_blendScratch))
        {
            sg.Clear(GdiColor.Transparent);
            sg.SmoothingMode = SmoothingMode.AntiAlias;
            sg.PixelOffsetMode = PagePom;
            sg.Transform = world;
            sg.Clip = _g.Clip;
            if (ShDebug2) Console.WriteLine($"[mbf] dev=({x0},{y0})-({x1},{y1}) clip={sg.ClipBounds}");
            sg.FillRectangle(brush, boundsWorld);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = rect.Width * 4;
            var drow = new byte[rowBytes];
            var srow = new byte[rowBytes];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, drow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srow, 0, rowBytes);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = x * 4;
                    int sa = srow[i + 3];
                    if (sa == 0) continue;
                    double a = sa / 255.0;
                    double dn = drow[i + 3] / 255.0;
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    for (int c = 0; c < 3; c++)
                    {
                        int s = srow[i + c];
                        double bb = dn > 0.0 ? (1 - dn) * s + dn * (drow[i + c] * s / 255.0) : s;
                        drow[i + c] = (byte)((bb * a + drow[i + c] * inv) / outA + 0.5);
                    }
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            _blendScratch.UnlockBits(src);
        }
    }

    /// <summary>Fill the current clip with <paramref name="brush"/> composited per pixel
    /// through an ExtGState soft mask (PDF 32000 §11.6.5.4): a = coverage · /ca · mask.
    /// The scratch paint mirrors <see cref="MultiplyBrushFill"/> (same transform/clip, so
    /// coverage matches a native fill); the Normal "over" loop mirrors
    /// <see cref="FillPathBlended"/> with the source colour read per pixel.</summary>
    private void MaskedBrushFill(System.Drawing.Brush brush, RectangleF boundsWorld, GdiMatrix world,
        byte[] softMask, double ca)
    {
        if (ca <= 0.0) return;
        int w = _bitmap.Width, h = _bitmap.Height;
        var corners = new[]
        {
            new PointF(boundsWorld.Left, boundsWorld.Top), new PointF(boundsWorld.Right, boundsWorld.Top),
            new PointF(boundsWorld.Left, boundsWorld.Bottom), new PointF(boundsWorld.Right, boundsWorld.Bottom),
        };
        world.TransformPoints(corners);
        float fminX = corners[0].X, fminY = corners[0].Y, fmaxX = corners[0].X, fmaxY = corners[0].Y;
        foreach (var c in corners) { fminX = Math.Min(fminX, c.X); fminY = Math.Min(fminY, c.Y); fmaxX = Math.Max(fmaxX, c.X); fmaxY = Math.Max(fmaxY, c.Y); }
        int x0 = Math.Max(0, (int)Math.Floor(fminX)), y0 = Math.Max(0, (int)Math.Floor(fminY));
        int x1 = Math.Min(w, (int)Math.Ceiling(fmaxX)), y1 = Math.Min(h, (int)Math.Ceiling(fmaxY));
        if (x1 <= x0 || y1 <= y0) return;

        _blendScratch ??= new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var sg = Graphics.FromImage(_blendScratch))
        {
            sg.Clear(GdiColor.Transparent);
            sg.SmoothingMode = SmoothingMode.AntiAlias;
            sg.PixelOffsetMode = PagePom;
            sg.Transform = world;
            sg.Clip = _g.Clip;
            sg.FillRectangle(brush, boundsWorld);
        }

        _g.Flush();
        var rect = new System.Drawing.Rectangle(x0, y0, x1 - x0, y1 - y0);
        var dst = _bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var src = _blendScratch.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = rect.Width * 4;
            var drow = new byte[rowBytes];
            var srow = new byte[rowBytes];
            for (int y = 0; y < rect.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(dst.Scan0 + y * dst.Stride, drow, 0, rowBytes);
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srow, 0, rowBytes);
                bool dirty = false;
                for (int x = 0; x < rect.Width; x++)
                {
                    int i = x * 4;
                    int cov = srow[i + 3];
                    if (cov == 0) continue;
                    double a = cov / 255.0 * ca * (softMask[(y0 + y) * w + (x0 + x)] / 255.0);
                    if (a <= 0.0) continue;
                    double dn = drow[i + 3] / 255.0;
                    double outA = a + dn * (1 - a);
                    if (outA <= 0.0) continue;
                    double inv = dn * (1 - a);
                    drow[i]     = (byte)((srow[i] * a + drow[i] * inv) / outA + 0.5);
                    drow[i + 1] = (byte)((srow[i + 1] * a + drow[i + 1] * inv) / outA + 0.5);
                    drow[i + 2] = (byte)((srow[i + 2] * a + drow[i + 2] * inv) / outA + 0.5);
                    drow[i + 3] = (byte)(outA * 255 + 0.5);
                    dirty = true;
                }
                if (dirty)
                    System.Runtime.InteropServices.Marshal.Copy(drow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            _bitmap.UnlockBits(dst);
            _blendScratch.UnlockBits(src);
        }
    }

    private void DrawRadialShading(RadialShading ra, GraphicsState state)
    {
        var alpha = (byte)Clamp255(state.FillAlpha);
        var colors = SampleShading(ra.Function, ra.Domain, ra.ColorSpaceName, alpha, ra.TintTransform, ra.AltSpaceName);
        if (colors is null) return;

        // Approximate with the larger of the two circles; center colour = domain start.
        bool outerIsOne = ra.R1 >= ra.R0;
        double cx = outerIsOne ? ra.X1 : ra.X0;
        double cy = outerIsOne ? ra.Y1 : ra.Y0;
        double rr = Math.Max(ra.R0, ra.R1);
        if (rr <= 1e-6) return;

        var saved = _g.Transform;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        try
        {
            var centerColor = outerIsOne ? colors[0] : colors[^1];
            var edgeColor = outerIsOne ? colors[^1] : colors[0];

            // /Extend beyond the outer circle pads with the edge colour (PDF 32000
            // §8.7.4.5.4; SVG-derived gradients rely on it). Without the pad the
            // region outside r is left unpainted — a "white ring" artifact.
            var extendOuter = ra.Extend is { Length: >= 2 } && (outerIsOne ? ra.Extend[1] : ra.Extend[0]);
            if (extendOuter)
            {
                var bounds = _g.ClipBounds;
                if (bounds.Width > 0 && bounds.Height > 0 && bounds.Width < 100000 && bounds.Height < 100000)
                {
                    using var pad = new SolidBrush(edgeColor);
                    _g.FillRectangle(pad, bounds);
                }
            }

            using var ellipse = new GraphicsPath();
            ellipse.AddEllipse((float)(cx - rr), (float)(cy - rr), (float)(2 * rr), (float)(2 * rr));
            // Focal point = the smaller circle's center (usually the highlight).
            var focal = outerIsOne ? new PointF((float)ra.X0, (float)ra.Y0) : new PointF((float)ra.X1, (float)ra.Y1);
            using var brush = new PathGradientBrush(ellipse) { CenterPoint = focal };
            brush.CenterColor = centerColor;
            brush.SurroundColors = new[] { edgeColor };
            // A non-zero inner radius (the focal circle's own /Coords radius)
            // compresses the colour ramp into [r0..r1]: everything inside the
            // focal circle holds the start colour. In PathGradientBrush position
            // space (0 = boundary, 1 = centre) the ramp ends at 1 − r0/r1.
            var innerR = Math.Min(ra.R0, ra.R1);
            var knee = innerR > 1e-6 ? (float)(1 - innerR / rr) : 1f;
            if (colors.Length > 2 || knee < 1f)
            {
                var n = Math.Max(colors.Length, 2);
                var extra = knee < 1f ? 1 : 0;
                var blend = new ColorBlend(n + extra);
                for (var i = 0; i < n; i++)
                {
                    var src = colors.Length > 2 ? colors : new[] { colors[0], colors[^1] };
                    blend.Colors[i] = outerIsOne ? src[src.Length - 1 - i] : src[i];
                    blend.Positions[i] = knee * (i / (float)(n - 1));
                }
                if (extra == 1)
                {
                    blend.Colors[n] = centerColor;
                    blend.Positions[n] = 1f;
                }
                brush.InterpolationColors = blend;
            }
            _g.FillPath(brush, ellipse);
        }
        catch { /* degenerate radial */ }
        finally { _g.Transform = saved; }
    }

    // ── Mesh shadings (Types 4-7, §8.7.4.5) ─────────────────────────
    //
    // GDI+ has no Gouraud/patch gradient primitive, so the mesh geometry is
    // tessellated and each small cell is flat-filled with its (bilinearly /
    // averaged) colour. At the cell sizes used here the facet steps are visually
    // indistinguishable from a smooth gradient. The current GDI clip (installed by the
    // `… W n … sh` idiom) and the shading /BBox bound the paint, so cells that
    // fall outside the shape are clipped away by GDI+.
    private void DrawMeshShading(ShadingBase shading, GraphicsState state)
    {
        var saved = _g.Transform;
        var savedSmoothing = _g.SmoothingMode;
        using var world = WorldMatrix(state.Ctm);
        _g.Transform = world;
        // Flat-filled adjacent cells share exact edges; anti-aliasing those edges
        // would leave hairline seams, so disable it for the duration of the mesh.
        _g.SmoothingMode = SmoothingMode.None;
        var alpha = (byte)Clamp255(state.FillAlpha);
        try
        {
            switch (shading)
            {
                case FreeFormGouraudShading g: DrawTriangleMesh(g.Vertices, g.Triangles, shading, alpha); break;
                case LatticeFormGouraudShading l: DrawTriangleMesh(l.Vertices, l.Triangles, shading, alpha); break;
                case CoonsPatchShading c: DrawPatchMesh(c.Patches, shading, alpha); break;
                case TensorPatchShading t: DrawPatchMesh(t.Patches, shading, alpha); break;
            }
        }
        catch { /* malformed mesh — skip rather than abort the page */ }
        finally { _g.SmoothingMode = savedSmoothing; _g.Transform = saved; }
    }

    private GdiColor MeshColor(double[]? comp, ShadingBase shading, byte alpha)
    {
        if (comp is null || comp.Length == 0) return GdiColor.FromArgb(alpha, 0, 0, 0);
        SoftwarePageRenderer.ComponentsToRgb(comp, shading.ColorSpaceName, out var r, out var g, out var b,
            shading.TintTransform, shading.AltSpaceName);
        return GdiColor.FromArgb(alpha, r, g, b);
    }

    private void DrawTriangleMesh(MeshVertex[] verts, (int A, int B, int C)[] tris, ShadingBase shading, byte alpha)
    {
        var pts = new PointF[3];
        foreach (var (a, b, c) in tris)
        {
            if (a < 0 || b < 0 || c < 0 || a >= verts.Length || b >= verts.Length || c >= verts.Length) continue;
            var va = verts[a]; var vb = verts[b]; var vc = verts[c];
            var avg = AvgColor(va.Color, vb.Color, vc.Color);
            pts[0] = new PointF((float)va.X, (float)va.Y);
            pts[1] = new PointF((float)vb.X, (float)vb.Y);
            pts[2] = new PointF((float)vc.X, (float)vc.Y);
            using var brush = new SolidBrush(MeshColor(avg, shading, alpha));
            _g.FillPolygon(brush, pts);
        }
    }

    private void DrawPatchMesh(MeshPatch[] patches, ShadingBase shading, byte alpha)
    {
        const int N = 8; // tessellation grid per patch (N×N cells)
        var quad = new PointF[4];
        foreach (var p in patches)
        {
            for (var i = 0; i < N; i++)
            {
                double u0 = (double)i / N, u1 = (double)(i + 1) / N;
                for (var j = 0; j < N; j++)
                {
                    double v0 = (double)j / N, v1 = (double)(j + 1) / N;
                    EvalPatch(p, u0, v0, out var x00, out var y00);
                    EvalPatch(p, u1, v0, out var x10, out var y10);
                    EvalPatch(p, u1, v1, out var x11, out var y11);
                    EvalPatch(p, u0, v1, out var x01, out var y01);
                    var col = BilinearColor(p.CornerColors, (u0 + u1) * 0.5, (v0 + v1) * 0.5);
                    quad[0] = new PointF((float)x00, (float)y00);
                    quad[1] = new PointF((float)x10, (float)y10);
                    quad[2] = new PointF((float)x11, (float)y11);
                    quad[3] = new PointF((float)x01, (float)y01);
                    using var brush = new SolidBrush(MeshColor(col, shading, alpha));
                    _g.FillPolygon(brush, quad);
                }
            }
        }
    }

    // Tensor-product surface S(u,v) = ΣΣ B_i(u) B_j(v) P[i,j] over the 4×4 control net.
    private static void EvalPatch(MeshPatch p, double u, double v, out double x, out double y)
    {
        double sx = 0, sy = 0;
        for (var i = 0; i < 4; i++)
        {
            var bu = Bernstein(i, u);
            for (var j = 0; j < 4; j++)
            {
                var w = bu * Bernstein(j, v);
                sx += w * p.Px[i, j];
                sy += w * p.Py[i, j];
            }
        }
        x = sx; y = sy;
    }

    private static double Bernstein(int i, double t) => i switch
    {
        0 => (1 - t) * (1 - t) * (1 - t),
        1 => 3 * (1 - t) * (1 - t) * t,
        2 => 3 * (1 - t) * t * t,
        3 => t * t * t,
        _ => 0,
    };

    // Corner colours are stored in UV order 00, 03, 33, 30 (MeshPatch); bilinear-blend
    // them across the patch parameter square.
    private static double[]? BilinearColor(double[][] corners, double u, double v)
    {
        var c00 = corners[0]; var c01 = corners[1]; var c11 = corners[2]; var c10 = corners[3];
        if (c00 is null || c01 is null || c11 is null || c10 is null) return c00 ?? c01 ?? c11 ?? c10;
        var n = c00.Length;
        var outp = new double[n];
        double w00 = (1 - u) * (1 - v), w01 = (1 - u) * v, w11 = u * v, w10 = u * (1 - v);
        for (var k = 0; k < n; k++)
            outp[k] = w00 * c00[k] + w01 * c01[k] + w11 * c11[k] + w10 * c10[k];
        return outp;
    }

    private static double[]? AvgColor(double[] a, double[] b, double[] c)
    {
        if (a is null || b is null || c is null) return a ?? b ?? c;
        var n = Math.Min(a.Length, Math.Min(b.Length, c.Length));
        var outp = new double[n];
        for (var k = 0; k < n; k++) outp[k] = (a[k] + b[k] + c[k]) / 3.0;
        return outp;
    }
}
