// PDF mesh shadings — PDF32000_2008 §8.7.4.5.5 (Type 4 Gouraud),
// §8.7.4.5.6 (Type 5 lattice Gouraud), §8.7.4.5.7 (Type 6 Coons patch),
// §8.7.4.5.8 (Type 7 tensor-product patch).
//
// Shared bit-packed vertex stream format: each vertex is /BitsPerFlag
// (Types 4/6/7) + 2×/BitsPerCoordinate + N×/BitsPerComponent bits, where
// N is the number of colour components (1 if /Function is present, else
// the colour-space dimension). Packed integers map to ranges via
// /Decode = [xmin xmax ymin ymax c0min c0max ... cNmin cNmax].

using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Shading;

/// <summary>One mesh vertex (decoded position + colour components in
/// the shading's output colour space).</summary>
internal readonly struct MeshVertex(double x, double y, double[] color)
{
    public double X { get; } = x;
    public double Y { get; } = y;
    public double[] Color { get; } = color;
}

/// <summary>Free-form Gouraud-shaded triangle mesh (Type 4, §8.7.4.5.5).
/// Vertex stream with edge flags producing a triangle strip; each vertex
/// carries its own colour. Flag 0 starts a new triangle (3 vertices),
/// flags 1/2 reuse two vertices of the previous triangle.</summary>
public sealed class FreeFormGouraudShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.FreeFormGouraud;
    /// <summary>Triangles as (a, b, c) indices into <see cref="Vertices"/>.</summary>
    internal (int A, int B, int C)[] Triangles { get; }
    internal MeshVertex[] Vertices { get; }

    internal FreeFormGouraudShading(PdfStream stream, PdfReader reader)
        : base(stream.Dict, reader)
    {
        var dict = stream.Dict;
        var data = reader.DecodeStream(stream);
        var bpc = (int)dict.GetInt("BitsPerCoordinate");
        var bpcc = (int)dict.GetInt("BitsPerComponent");
        var bpf = (int)dict.GetInt("BitsPerFlag");
        var function = PdfFunction.Parse(dict.Get("Function"), reader);
        var decode = MeshShadingHelper.ParseDecode(dict, function);

        var (verts, tris) = ParseStream(data, bpc, bpcc, bpf, decode, function);
        Vertices = verts;
        Triangles = tris;
    }

    private static (MeshVertex[], (int, int, int)[]) ParseStream(
        byte[] data, int bpc, int bpcc, int bpf, double[] decode, PdfFunction? function)
    {
        var br = new MeshBitReader(data);
        var verts = new List<MeshVertex>();
        var tris = new List<(int, int, int)>();
        int? a = null, b = null, c = null;
        while (br.HasBits(bpf + 2 * bpc + (function is null ? (decode.Length - 4) / 2 * bpcc : bpcc)))
        {
            var flag = (int)br.ReadBits(bpf);
            var v = MeshShadingHelper.ReadVertex(br, bpc, bpcc, decode, function);
            if (v is null) break;
            verts.Add(v.Value);
            var idx = verts.Count - 1;
            switch (flag)
            {
                case 0:
                    a = idx;
                    // Read the next two flag-0 partners.
                    if (!br.HasBits(bpf)) break;
                    br.ReadBits(bpf); // discard (must be 0 by spec)
                    var v2 = MeshShadingHelper.ReadVertex(br, bpc, bpcc, decode, function);
                    if (v2 is null) break;
                    verts.Add(v2.Value); b = verts.Count - 1;
                    if (!br.HasBits(bpf)) break;
                    br.ReadBits(bpf);
                    var v3 = MeshShadingHelper.ReadVertex(br, bpc, bpcc, decode, function);
                    if (v3 is null) break;
                    verts.Add(v3.Value); c = verts.Count - 1;
                    tris.Add((a.Value, b.Value, c.Value));
                    break;
                case 1:
                    // Reuse previous BC as new AB; new vertex is the new C.
                    if (b.HasValue && c.HasValue)
                    {
                        a = b; b = c; c = idx;
                        tris.Add((a.Value, b.Value, c.Value));
                    }
                    break;
                case 2:
                    // Reuse previous AC as new AB; new vertex is the new C.
                    if (a.HasValue && c.HasValue)
                    {
                        b = c; c = idx;
                        tris.Add((a.Value, b.Value, c.Value));
                    }
                    break;
            }
        }
        return (verts.ToArray(), tris.ToArray());
    }
}

/// <summary>Lattice-form Gouraud-shaded triangle mesh (Type 5,
/// §8.7.4.5.6). Vertices in row-major lattice with VerticesPerRow per
/// row; each 2×2 cell yields two triangles.</summary>
public sealed class LatticeFormGouraudShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.LatticeFormGouraud;
    internal (int A, int B, int C)[] Triangles { get; }
    internal MeshVertex[] Vertices { get; }

    internal LatticeFormGouraudShading(PdfStream stream, PdfReader reader)
        : base(stream.Dict, reader)
    {
        var dict = stream.Dict;
        var data = reader.DecodeStream(stream);
        var bpc = (int)dict.GetInt("BitsPerCoordinate");
        var bpcc = (int)dict.GetInt("BitsPerComponent");
        var vpr = (int)dict.GetInt("VerticesPerRow");
        var function = PdfFunction.Parse(dict.Get("Function"), reader);
        var decode = MeshShadingHelper.ParseDecode(dict, function);
        if (vpr < 2) { Vertices = Array.Empty<MeshVertex>(); Triangles = Array.Empty<(int, int, int)>(); return; }

        var br = new MeshBitReader(data);
        var verts = new List<MeshVertex>();
        while (true)
        {
            var v = MeshShadingHelper.ReadVertex(br, bpc, bpcc, decode, function);
            if (v is null) break;
            verts.Add(v.Value);
        }
        Vertices = verts.ToArray();

        var tris = new List<(int, int, int)>();
        var rows = Vertices.Length / vpr;
        for (var r = 0; r < rows - 1; r++)
        {
            for (var col = 0; col < vpr - 1; col++)
            {
                var i00 = r * vpr + col;
                var i01 = r * vpr + col + 1;
                var i10 = (r + 1) * vpr + col;
                var i11 = (r + 1) * vpr + col + 1;
                tris.Add((i00, i01, i11));
                tris.Add((i00, i11, i10));
            }
        }
        Triangles = tris.ToArray();
    }
}

/// <summary>One Coons (Type 6) or tensor-product (Type 7) patch — 4
/// corner colours and a 4×4 grid of control points. For Type 6 the
/// 4 interior points are derived from the 12 boundary points so this
/// representation is uniform across both types.</summary>
internal sealed class MeshPatch
{
    /// <summary>4×4 tensor-product control points, [u, v].</summary>
    public double[,] Px = new double[4, 4];
    public double[,] Py = new double[4, 4];
    /// <summary>Colour at each of the 4 corners (UV order: 00, 03, 33, 30).</summary>
    public double[][] CornerColors = new double[4][];
}

/// <summary>Coons patch mesh (Type 6, §8.7.4.5.7).</summary>
public sealed class CoonsPatchShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.CoonsPatch;
    internal MeshPatch[] Patches { get; }

    internal CoonsPatchShading(PdfStream stream, PdfReader reader)
        : base(stream.Dict, reader)
    {
        var dict = stream.Dict;
        var data = reader.DecodeStream(stream);
        var bpc = (int)dict.GetInt("BitsPerCoordinate");
        var bpcc = (int)dict.GetInt("BitsPerComponent");
        var bpf = (int)dict.GetInt("BitsPerFlag");
        var function = PdfFunction.Parse(dict.Get("Function"), reader);
        var decode = MeshShadingHelper.ParseDecode(dict, function);
        Patches = ParsePatches(data, bpc, bpcc, bpf, decode, function, tensor: false);
    }

    internal static MeshPatch[] ParsePatches(byte[] data, int bpc, int bpcc, int bpf,
        double[] decode, PdfFunction? function, bool tensor)
    {
        var br = new MeshBitReader(data);
        var patches = new List<MeshPatch>();
        MeshPatch? prev = null;
        var ncc = function is null ? (decode.Length - 4) / 2 : 1;

        while (br.HasBits(bpf))
        {
            var flag = (int)br.ReadBits(bpf);
            var newPoints = flag == 0 ? (tensor ? 16 : 12) : (tensor ? 12 : 8);
            var newColors = flag == 0 ? 4 : 2;

            var pts = new (double x, double y)[newPoints];
            for (var i = 0; i < newPoints; i++)
            {
                if (!br.HasBits(2 * bpc)) return patches.ToArray();
                var x = MeshShadingHelper.DecodeCoord(br.ReadBits(bpc), bpc, decode[0], decode[1]);
                var y = MeshShadingHelper.DecodeCoord(br.ReadBits(bpc), bpc, decode[2], decode[3]);
                pts[i] = (x, y);
            }
            var cols = new double[newColors][];
            for (var i = 0; i < newColors; i++)
            {
                cols[i] = MeshShadingHelper.ReadColor(br, bpcc, decode, function);
                if (cols[i] is null) return patches.ToArray();
            }

            var patch = new MeshPatch();
            if (flag == 0 || prev is null)
            {
                BuildPatch(patch, pts, tensor);
                patch.CornerColors = cols;
            }
            else
            {
                // Edge sharing per §8.7.4.5.7 Figure 31: 4 boundary points and
                // 2 corner colours come from the previous patch's flag-indexed
                // edge.
                CopyShared(prev, patch, flag, tensor);
                FillNewBoundary(patch, pts, flag, tensor);
                // Map new corner colours: positions depend on flag.
                AssignSharedColors(prev, patch, flag);
                patch.CornerColors[2] = cols[0]; // see AssignSharedColors mapping
                patch.CornerColors[3] = cols[1];
            }
            patches.Add(patch);
            prev = patch;
        }
        return patches.ToArray();
    }

    // Grid [u,v] slot for each of the 16 tensor control points in PDF stream order
    // (§8.7.4.5.8): p11 p12 p13 p14 p24 p34 p44 p43 p42 p41 p31 p21 p22 p23 p33 p32.
    private static readonly int[] TensorU = { 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 2, 1, 1, 1, 2, 2 };
    private static readonly int[] TensorV = { 0, 1, 2, 3, 3, 3, 3, 2, 1, 0, 0, 0, 1, 2, 2, 1 };

    private static void BuildPatch(MeshPatch p, (double x, double y)[] pts, bool tensor)
    {
        if (tensor)
        {
            // Tensor: 16 control points read in the boustrophedon order of §8.7.4.5.8
            //   p11 p12 p13 p14  p24 p34 p44  p43 p42 p41  p31 p21  p22 p23 p33 p32
            // (1-based p_ij). Map each stream point k onto its [u,v] grid slot
            // (0-based, u↔first index, v↔second). Points 12-15 are the interior.
            for (var k = 0; k < 16 && k < pts.Length; k++)
            {
                p.Px[TensorU[k], TensorV[k]] = pts[k].x;
                p.Py[TensorU[k], TensorV[k]] = pts[k].y;
            }
        }
        else
        {
            // Coons: 12 boundary points c1..c12 (going around the patch).
            // Spec order: bottom (c1 = c00), right (... = c33), top-reverse, left-reverse.
            // Boundary order per §8.7.4.5.7:
            //   c1..c4   = (0,0) → (3,0)  bottom
            //   c5..c7   = (3,1), (3,2), (3,3)
            //   c8..c10  = (2,3), (1,3), (0,3)
            //   c11..c12 = (0,2), (0,1)
            // Then derive 4 interior points.
            var c = pts;
            void Set(int u, int v, (double, double) p2) { p.Px[u, v] = p2.Item1; p.Py[u, v] = p2.Item2; }
            Set(0, 0, c[0]); Set(1, 0, c[1]); Set(2, 0, c[2]); Set(3, 0, c[3]);
            Set(3, 1, c[4]); Set(3, 2, c[5]); Set(3, 3, c[6]);
            Set(2, 3, c[7]); Set(1, 3, c[8]); Set(0, 3, c[9]);
            Set(0, 2, c[10]); Set(0, 1, c[11]);
            // Interior points per Coons-to-tensor conversion (PDF 32000 §8.7.4.5.7).
            // p11 = S(1/3, 1/3) using the Coons-patch formula expanded to 4×4 tensor.
            // We compute each interior point as the Coons-patch evaluation at u,v = 1/3, 2/3.
            DeriveCoonsInteriors(p);
        }
    }

    private static void DeriveCoonsInteriors(MeshPatch p)
    {
        // Coons patch interior formula (PDF 32000 §8.7.4.5.7 Table 91):
        //   p11 = (1/9)*( -4*p00 + 6*(p01+p10) - 2*(p03+p30) + 3*(C04 + C40) - 1*(... ))
        // Simpler equivalent: evaluate Coons-patch S(u,v) at (1/3,1/3), (2/3,1/3), (1/3,2/3), (2/3,2/3)
        // and back-solve for the 4 interior tensor points by inverting Bernstein at those four (u,v).
        double[] us = { 1.0 / 3.0, 2.0 / 3.0, 1.0 / 3.0, 2.0 / 3.0 };
        double[] vs = { 1.0 / 3.0, 1.0 / 3.0, 2.0 / 3.0, 2.0 / 3.0 };
        int[] iu = { 1, 2, 1, 2 };
        int[] iv = { 1, 1, 2, 2 };
        double SCoons(double u, double v, double[,] g)
        {
            // Boundary cubic Béziers.
            double Bx(double t, double p0, double p1, double p2, double p3)
                => (1 - t) * (1 - t) * (1 - t) * p0
                 + 3 * (1 - t) * (1 - t) * t * p1
                 + 3 * (1 - t) * t * t * p2
                 + t * t * t * p3;
            var bottom = Bx(u, g[0, 0], g[1, 0], g[2, 0], g[3, 0]);
            var top = Bx(u, g[0, 3], g[1, 3], g[2, 3], g[3, 3]);
            var left = Bx(v, g[0, 0], g[0, 1], g[0, 2], g[0, 3]);
            var right = Bx(v, g[3, 0], g[3, 1], g[3, 2], g[3, 3]);
            var c00 = g[0, 0]; var c30 = g[3, 0]; var c03 = g[0, 3]; var c33 = g[3, 3];
            // Coons surface: ruled(v) along left/right + ruled(u) along bottom/top - bilinear(corners).
            var ruleV = (1 - u) * left + u * right;
            var ruleU = (1 - v) * bottom + v * top;
            var bili = (1 - u) * (1 - v) * c00 + u * (1 - v) * c30
                     + (1 - u) * v * c03 + u * v * c33;
            return ruleU + ruleV - bili;
        }
        // Bernstein basis matrix at our four (u,v) for the 4 interior (i,j)∈{1,2}²
        // is known and invertible; the closed-form derives:
        //   p11 = (1/9)( 9*S00 - 3*(top + bottom contributions at u=1/3 etc.) ... )
        // Simpler / numerically safe approach: solve a 4x4 system.
        // System: S(u,v) = Σ B_i(u)B_j(v) p_ij. Known: all p_ij except {p11,p21,p12,p22}.
        // We collect contributions from known boundary points and isolate the 4 unknowns.
        var px = new double[4, 4]; var py = new double[4, 4];
        // Init known boundary points.
        int[] boundaryI = { 0, 1, 2, 3, 3, 3, 3, 2, 1, 0, 0, 0 };
        int[] boundaryJ = { 0, 0, 0, 0, 1, 2, 3, 3, 3, 3, 2, 1 };
        for (var k = 0; k < 12; k++) { px[boundaryI[k], boundaryJ[k]] = p.Px[boundaryI[k], boundaryJ[k]]; py[boundaryI[k], boundaryJ[k]] = p.Py[boundaryI[k], boundaryJ[k]]; }
        double Bern(int i, double t) => i switch
        {
            0 => (1 - t) * (1 - t) * (1 - t),
            1 => 3 * (1 - t) * (1 - t) * t,
            2 => 3 * (1 - t) * t * t,
            3 => t * t * t,
            _ => 0
        };
        // Build 4x4 system: for each of the 4 sample points k:
        //   S_k = sum over interior (a,b) of B_a(uk) B_b(vk) * p[a,b] + boundary_contrib_k
        var mat = new double[4, 5]; // [4][unknowns + RHS_x]; do _x and _y separately
        var matY = new double[4, 5];
        for (var k = 0; k < 4; k++)
        {
            var uk = us[k]; var vk = vs[k];
            // Boundary contribution.
            double bcX = 0, bcY = 0;
            for (var i = 0; i < 4; i++)
                for (var j = 0; j < 4; j++)
                {
                    if (i is 1 or 2 && j is 1 or 2) continue; // interior unknowns
                    bcX += Bern(i, uk) * Bern(j, vk) * px[i, j];
                    bcY += Bern(i, uk) * Bern(j, vk) * py[i, j];
                }
            // Unknown coefficients in column order: p11, p21, p12, p22.
            mat[k, 0] = Bern(1, uk) * Bern(1, vk);
            mat[k, 1] = Bern(2, uk) * Bern(1, vk);
            mat[k, 2] = Bern(1, uk) * Bern(2, vk);
            mat[k, 3] = Bern(2, uk) * Bern(2, vk);
            mat[k, 4] = SCoons(uk, vk, px) - bcX;
            matY[k, 0] = mat[k, 0]; matY[k, 1] = mat[k, 1]; matY[k, 2] = mat[k, 2]; matY[k, 3] = mat[k, 3];
            matY[k, 4] = SCoons(uk, vk, py) - bcY;
        }
        var sx = GaussSolve(mat);
        var sy = GaussSolve(matY);
        if (sx is null || sy is null) return;
        p.Px[1, 1] = sx[0]; p.Px[2, 1] = sx[1]; p.Px[1, 2] = sx[2]; p.Px[2, 2] = sx[3];
        p.Py[1, 1] = sy[0]; p.Py[2, 1] = sy[1]; p.Py[1, 2] = sy[2]; p.Py[2, 2] = sy[3];
    }

    private static double[]? GaussSolve(double[,] aug)
    {
        const int n = 4;
        for (var i = 0; i < n; i++)
        {
            // Pivot.
            var maxRow = i;
            for (var k = i + 1; k < n; k++)
                if (Math.Abs(aug[k, i]) > Math.Abs(aug[maxRow, i])) maxRow = k;
            if (maxRow != i)
                for (var c2 = 0; c2 <= n; c2++)
                    (aug[i, c2], aug[maxRow, c2]) = (aug[maxRow, c2], aug[i, c2]);
            if (Math.Abs(aug[i, i]) < 1e-12) return null;
            // Eliminate below.
            for (var k = i + 1; k < n; k++)
            {
                var f = aug[k, i] / aug[i, i];
                for (var c2 = i; c2 <= n; c2++)
                    aug[k, c2] -= f * aug[i, c2];
            }
        }
        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var s = aug[i, n];
            for (var c2 = i + 1; c2 < n; c2++) s -= aug[i, c2] * x[c2];
            x[i] = s / aug[i, i];
        }
        return x;
    }

    private static void CopyShared(MeshPatch prev, MeshPatch cur, int flag, bool tensor)
    {
        // The 4 boundary points along the shared edge are reused; corner-color
        // mapping handled separately. Map per spec Figure 30/31:
        //   flag 1: share previous (i,3) i=0..3 as new (i,0) — top edge → bottom edge.
        //   flag 2: share previous (3,j) j=0..3 as new (j,0) — right edge → bottom edge.
        //   flag 3: share previous (i,0) i=3..0 as new (i,0) — bottom edge reversed.
        // (Implementation here is symmetric for tensor and Coons because both
        //  reuse the 4 boundary points along the shared edge.)
        switch (flag)
        {
            case 1:
                for (var i = 0; i < 4; i++) { cur.Px[i, 0] = prev.Px[i, 3]; cur.Py[i, 0] = prev.Py[i, 3]; }
                break;
            case 2:
                for (var i = 0; i < 4; i++) { cur.Px[i, 0] = prev.Px[3, 3 - i]; cur.Py[i, 0] = prev.Py[3, 3 - i]; }
                break;
            case 3:
                for (var i = 0; i < 4; i++) { cur.Px[i, 0] = prev.Px[3 - i, 0]; cur.Py[i, 0] = prev.Py[3 - i, 0]; }
                break;
        }
    }

    private static void FillNewBoundary(MeshPatch p, (double x, double y)[] pts, int flag, bool tensor)
    {
        // After 4 shared points, the remaining 8 (Coons) or 12 (tensor) bytes
        // describe the rest of the patch. For Coons:
        //   pts[0..2] = (3,1), (3,2), (3,3)
        //   pts[3..5] = (2,3), (1,3), (0,3)
        //   pts[6..7] = (0,2), (0,1)
        // Then derive interior.
        if (!tensor)
        {
            void Set(int u, int v, (double, double) q) { p.Px[u, v] = q.Item1; p.Py[u, v] = q.Item2; }
            Set(3, 1, pts[0]); Set(3, 2, pts[1]); Set(3, 3, pts[2]);
            Set(2, 3, pts[3]); Set(1, 3, pts[4]); Set(0, 3, pts[5]);
            Set(0, 2, pts[6]); Set(0, 1, pts[7]);
            DeriveCoonsInteriors(p);
            return;
        }
        // Tensor: 12 remaining points in boustrophedon order minus shared row.
        // Spec order for shared flag-1 patch (skipping the 4 already-set bottom points):
        //   (3,1), (2,1), (1,1), (0,1) — second row up (reversed)
        //   (0,2), (1,2), (2,2), (3,2)
        //   (3,3), (2,3), (1,3), (0,3)
        int[] iu = { 3, 2, 1, 0,   0, 1, 2, 3,   3, 2, 1, 0 };
        int[] iv = { 1, 1, 1, 1,   2, 2, 2, 2,   3, 3, 3, 3 };
        for (var k = 0; k < 12; k++) { p.Px[iu[k], iv[k]] = pts[k].x; p.Py[iu[k], iv[k]] = pts[k].y; }
    }

    private static void AssignSharedColors(MeshPatch prev, MeshPatch cur, int flag)
    {
        // Corner colour storage in this implementation: [c00, c30, c33, c03] order.
        // After edge-share, the two existing corners' colours come from the
        // shared edge's endpoints in the previous patch, and the two new
        // colours go to the other corners.
        cur.CornerColors = new double[4][];
        switch (flag)
        {
            case 1: // Shared edge: previous top (c03, c33) becomes new bottom (c00, c30).
                cur.CornerColors[0] = prev.CornerColors[3]; cur.CornerColors[1] = prev.CornerColors[2];
                break;
            case 2: // Shared edge: previous right (c30, c33) becomes new bottom (c00, c30).
                cur.CornerColors[0] = prev.CornerColors[1]; cur.CornerColors[1] = prev.CornerColors[2];
                break;
            case 3: // Shared edge: previous bottom (c30, c00) reversed → new bottom (c00, c30).
                cur.CornerColors[0] = prev.CornerColors[1]; cur.CornerColors[1] = prev.CornerColors[0];
                break;
        }
    }
}

/// <summary>Tensor-product patch mesh (Type 7, §8.7.4.5.8). Same edge-flag
/// stream shape as Coons (Type 6) but 16 explicit control points per
/// patch — no derivation needed.</summary>
public sealed class TensorPatchShading : ShadingBase
{
    public override ShadingType ShadingType => ShadingType.TensorProductPatch;
    internal MeshPatch[] Patches { get; }

    internal TensorPatchShading(PdfStream stream, PdfReader reader)
        : base(stream.Dict, reader)
    {
        var dict = stream.Dict;
        var data = reader.DecodeStream(stream);
        var bpc = (int)dict.GetInt("BitsPerCoordinate");
        var bpcc = (int)dict.GetInt("BitsPerComponent");
        var bpf = (int)dict.GetInt("BitsPerFlag");
        var function = PdfFunction.Parse(dict.Get("Function"), reader);
        var decode = MeshShadingHelper.ParseDecode(dict, function);
        Patches = CoonsPatchShading.ParsePatches(data, bpc, bpcc, bpf, decode, function, tensor: true);
    }
}

// ── Helpers ──────────────────────────────────────────────────────────

/// <summary>Forward-direction bit-aligned reader over a PDF mesh data
/// stream. PDF specifies big-endian bit order, MSB first, contiguous
/// across byte boundaries.</summary>
internal sealed class MeshBitReader(byte[] data)
{
    private readonly byte[] _data = data;
    private int _bytePos;
    private int _bitPos; // 0..7, MSB = 0

    public bool HasBits(int n)
    {
        var totalBitsRemaining = (_data.Length - _bytePos) * 8 - _bitPos;
        return totalBitsRemaining >= n;
    }

    public uint ReadBits(int n)
    {
        if (n <= 0) return 0;
        if (n > 32) n = 32;
        uint result = 0;
        while (n > 0)
        {
            if (_bytePos >= _data.Length) return result;
            var available = 8 - _bitPos;
            var take = Math.Min(available, n);
            var shift = available - take;
            var mask = (1u << take) - 1u;
            result = (result << take) | ((uint)(_data[_bytePos] >> shift) & mask);
            _bitPos += take;
            if (_bitPos >= 8) { _bytePos++; _bitPos = 0; }
            n -= take;
        }
        return result;
    }
}

internal static class MeshShadingHelper
{
    public static double[] ParseDecode(PdfDictionary dict, PdfFunction? function)
    {
        if (dict.Get("Decode") is PdfArray arr)
            return PdfArrayHelper.ToDoubleArray(arr);
        return function is null ? [0, 1, 0, 1, 0, 1, 0, 1, 0, 1] : [0, 1, 0, 1, 0, 1];
    }

    public static double DecodeCoord(uint v, int bits, double min, double max)
    {
        // C# left-shift on uint is mod-32: (1u<<32) == 1u, so the naive
        // span = (1u<<32) - 1 evaluates to 0 instead of 0xFFFFFFFF. Handle
        // 32 explicitly.
        var span = bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
        return span == 0 ? min : min + (double)v * (max - min) / span;
    }

    public static double[] ReadColor(MeshBitReader br, int bpcc, double[] decode, PdfFunction? function)
    {
        var ncc = function is null ? (decode.Length - 4) / 2 : 1;
        var raw = new double[ncc];
        for (var i = 0; i < ncc; i++)
        {
            if (!br.HasBits(bpcc)) return null!;
            var v = br.ReadBits(bpcc);
            var min = decode[4 + 2 * i];
            var max = decode[4 + 2 * i + 1];
            var span = bpcc >= 32 ? uint.MaxValue : (1u << bpcc) - 1u;
            raw[i] = span == 0 ? min : min + (double)v * (max - min) / span;
        }
        if (function is not null)
        {
            var output = function.Evaluate(raw);
            return output ?? raw;
        }
        return raw;
    }

    public static MeshVertex? ReadVertex(MeshBitReader br, int bpc, int bpcc, double[] decode, PdfFunction? function)
    {
        if (!br.HasBits(2 * bpc)) return null;
        var xv = br.ReadBits(bpc);
        var yv = br.ReadBits(bpc);
        var x = DecodeCoord(xv, bpc, decode[0], decode[1]);
        var y = DecodeCoord(yv, bpc, decode[2], decode[3]);
        var col = ReadColor(br, bpcc, decode, function);
        if (col is null) return null;
        return new MeshVertex(x, y, col);
    }
}
