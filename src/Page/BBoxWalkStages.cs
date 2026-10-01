using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
// A step of the content-box walk.
    private static void ResetPath(BBoxWalkState bw)
    {
        bw.pathStarted = false;
        bw.pminX = bw.pminY = bw.pmaxX = bw.pmaxY = 0;
    }

    // Project the user-space path bbox to page space and return axis-aligned
    // page-space bounds.
    private static (double minX, double minY, double maxX, double maxY) ProjectPathBBox(BBoxWalkState bw)
    {
        var (x1, y1) = bw.ctm.Apply(bw.pminX, bw.pminY);
        var (x2, y2) = bw.ctm.Apply(bw.pmaxX, bw.pminY);
        var (x3, y3) = bw.ctm.Apply(bw.pmaxX, bw.pmaxY);
        var (x4, y4) = bw.ctm.Apply(bw.pminX, bw.pmaxY);
        return (Math.Min(Math.Min(x1, x2), Math.Min(x3, x4)),
                Math.Min(Math.Min(y1, y2), Math.Min(y3, y4)),
                Math.Max(Math.Max(x1, x2), Math.Max(x3, x4)),
                Math.Max(Math.Max(y1, y2), Math.Max(y3, y4)));
    }

    private static void EmitPathBBox(BBoxWalkState bw, BBoxAccumulator acc)
    {
        if (!bw.pathStarted) return;
        var (mnx, mny, mxx, mxy) = ProjectPathBBox(bw);
        // Intersect with active clip — content outside the clip is not painted.
        mnx = Math.Max(mnx, bw.clipMinX);
        mny = Math.Max(mny, bw.clipMinY);
        mxx = Math.Min(mxx, bw.clipMaxX);
        mxy = Math.Min(mxy, bw.clipMaxY);
        if (mnx <= mxx && mny <= mxy)
        {
            acc.IncludePoint(mnx, mny);
            acc.IncludePoint(mxx, mxy);
        }
        ResetPath(bw);
        bw.clipPending = false;
    }

    // Tighten the active clip with the current path bbox (page space).
    private static void ApplyPendingClip(BBoxWalkState bw)
    {
        if (!bw.clipPending || !bw.pathStarted) { bw.clipPending = false; return; }
        var (mnx, mny, mxx, mxy) = ProjectPathBBox(bw);
        bw.clipMinX = Math.Max(bw.clipMinX, mnx);
        bw.clipMinY = Math.Max(bw.clipMinY, mny);
        bw.clipMaxX = Math.Min(bw.clipMaxX, mxx);
        bw.clipMaxY = Math.Min(bw.clipMaxY, mxy);
        bw.clipPending = false;
    }

    /// <summary>The state operators of the walk: the q/Q stacks, a pending clip, the CTM and the text object.</summary>
    private static bool WalkStateOperator(BBoxWalkState bw, string op)
    {
        switch (op)
        {
            case "q":
                bw.ctmStack.Push(bw.ctm);
                bw.clipStack.Push((bw.clipMinX, bw.clipMinY, bw.clipMaxX, bw.clipMaxY));
                break;
            case "Q":
                if (bw.ctmStack.Count > 0) bw.ctm = bw.ctmStack.Pop();
                if (bw.clipStack.Count > 0)
                    (bw.clipMinX, bw.clipMinY, bw.clipMaxX, bw.clipMaxY) = bw.clipStack.Pop();
                break;
            case "W" or "W*":
                bw.clipPending = true;
                break;
            case "cm" when bw.operands.Count >= 6:
                bw.ctm = new Cm(
                    Num(bw.operands[0]), Num(bw.operands[1]),
                    Num(bw.operands[2]), Num(bw.operands[3]),
                    Num(bw.operands[4]), Num(bw.operands[5])).Multiply(bw.ctm);
                break;

            case "BT": bw.inText = true; ResetPath(bw); break;
            case "ET": bw.inText = false; break;

        }
        return false;
    }

    /// <summary>The path construction operators, each point projected into the running path box.</summary>
    private static bool WalkPathOperator(BBoxWalkState bw, string op)
    {
        void IncludePathPoint(double x, double y)
        {
            if (!bw.pathStarted)
            {
                bw.pminX = bw.pmaxX = x;
                bw.pminY = bw.pmaxY = y;
                bw.pathStarted = true;
                return;
            }
            if (x < bw.pminX) bw.pminX = x;
            if (y < bw.pminY) bw.pminY = y;
            if (x > bw.pmaxX) bw.pmaxX = x;
            if (y > bw.pmaxY) bw.pmaxY = y;
        }

        switch (op)
        {
            // Path-construction operators (skip while inside BT/ET — those
            // operands are text positioning, not path geometry).
            case "m" when !bw.inText && bw.operands.Count >= 2:
                bw.curX = Num(bw.operands[0]); bw.curY = Num(bw.operands[1]);
                IncludePathPoint(bw.curX, bw.curY);
                break;
            case "l" when !bw.inText && bw.operands.Count >= 2:
                bw.curX = Num(bw.operands[0]); bw.curY = Num(bw.operands[1]);
                IncludePathPoint(bw.curX, bw.curY);
                break;
            case "c" when !bw.inText && bw.operands.Count >= 6:
            {
                // Cubic Bézier from current point through (x1,y1) and
                // (x2,y2) to (x3,y3). Use exact extrema rather than the
                // convex hull — control points can be placed far outside
                // the actual curve (common for thin-stroke shapes), and
                // including them would inflate the bbox dramatically.
                var x1 = Num(bw.operands[0]); var y1 = Num(bw.operands[1]);
                var x2 = Num(bw.operands[2]); var y2 = Num(bw.operands[3]);
                var x3 = Num(bw.operands[4]); var y3 = Num(bw.operands[5]);
                CubicExtremaInclude(IncludePathPoint, bw.curX, bw.curY, x1, y1, x2, y2, x3, y3);
                bw.curX = x3; bw.curY = y3;
                break;
            }
            case "v" when !bw.inText && bw.operands.Count >= 4:
            {
                // First control = current point.
                var x2 = Num(bw.operands[0]); var y2 = Num(bw.operands[1]);
                var x3 = Num(bw.operands[2]); var y3 = Num(bw.operands[3]);
                CubicExtremaInclude(IncludePathPoint, bw.curX, bw.curY, bw.curX, bw.curY, x2, y2, x3, y3);
                bw.curX = x3; bw.curY = y3;
                break;
            }
            case "y" when !bw.inText && bw.operands.Count >= 4:
            {
                // Second control = endpoint.
                var x1 = Num(bw.operands[0]); var y1 = Num(bw.operands[1]);
                var x3 = Num(bw.operands[2]); var y3 = Num(bw.operands[3]);
                CubicExtremaInclude(IncludePathPoint, bw.curX, bw.curY, x1, y1, x3, y3, x3, y3);
                bw.curX = x3; bw.curY = y3;
                break;
            }
            case "re" when !bw.inText && bw.operands.Count >= 4:
            {
                var x = Num(bw.operands[0]);
                var y = Num(bw.operands[1]);
                var w = Num(bw.operands[2]);
                var h = Num(bw.operands[3]);
                IncludePathPoint(x, y);
                IncludePathPoint(x + w, y + h);
                bw.curX = x; bw.curY = y;
                break;
            }
            case "h": /* close: no new point */ break;

        }
        return false;
    }

    /// <summary>The painting operators: a painted path lands in the box, a bare `n` only serves its pending clip.</summary>
    private static bool WalkPaintOperator(BBoxWalkState bw, BBoxAccumulator acc, string op)
    {
        switch (op)
        {
            // Painting operators — apply pending clip THEN emit bbox.
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                ApplyPendingClip(bw);
                EmitPathBBox(bw, acc);
                break;
            case "n":
                // No-op end-of-path. Apply pending clip first, then drop the path.
                ApplyPendingClip(bw);
                ResetPath(bw);
                break;

        }
        return false;
    }

    /// <summary>A form XObject walked in its own matrix, an image's unit square projected, an inline image skipped.</summary>
    private static bool WalkXObjectOperator(BBoxWalkState bw, BBoxAccumulator acc, PdfDictionary ownerDict, PdfReader reader, int depth, string op)
    {
        switch (op)
        {
            // External XObject reference.
            case "Do" when bw.operands.Count >= 1 && bw.operands[0] is PdfName doName:
            {
                var xobjs = TextAbsorber.ResolveXObjects(ownerDict, reader);
                if (xobjs is not null)
                {
                    var xstr = reader.ResolveStream(xobjs.Get(doName.Value));
                    if (xstr is not null)
                    {
                        var subtype = xstr.Dict.GetName("Subtype");
                        if (subtype == "Image")
                            IncludeUnitSquareClipped(bw, acc);
                        else if (subtype == "Form")
                        {
                            WalkFormXObject(bw, acc, reader, depth, xstr);
                        }
                    }
                }
                break;
            }

            // Inline images — BI..ID..EI. The image is painted into the
            // unit square pre-CTM, same as Do/Image.
            case "BI":
            {
                SkipInlineImageBody(bw.lexer);
                IncludeUnitSquareClipped(bw, acc);
                break;
            }
        }
        return false;
    }

    /// <summary>An image is painted into the unit square (0,0)-(1,1) pre-CTM: the square's
    /// projected corners, cut to the active clip - a page-sized picture under a small clip
    /// inks only the window the clip leaves (the content box does not reach the hidden rest).</summary>
    private static void IncludeUnitSquareClipped(BBoxWalkState bw, BBoxAccumulator acc)
    {
        var (ix1, iy1) = bw.ctm.Apply(0, 0);
        var (ix2, iy2) = bw.ctm.Apply(1, 0);
        var (ix3, iy3) = bw.ctm.Apply(1, 1);
        var (ix4, iy4) = bw.ctm.Apply(0, 1);
        var mnx = Math.Max(Math.Min(Math.Min(ix1, ix2), Math.Min(ix3, ix4)), bw.clipMinX);
        var mny = Math.Max(Math.Min(Math.Min(iy1, iy2), Math.Min(iy3, iy4)), bw.clipMinY);
        var mxx = Math.Min(Math.Max(Math.Max(ix1, ix2), Math.Max(ix3, ix4)), bw.clipMaxX);
        var mxy = Math.Min(Math.Max(Math.Max(iy1, iy2), Math.Max(iy3, iy4)), bw.clipMaxY);
        if (mnx > mxx || mny > mxy) return;
        acc.IncludePoint(mnx, mny);
        acc.IncludePoint(mxx, mxy);
    }

    /// <summary>A form XObject: its matrix composed into the CTM, its content walked, and its contribution clipped to its own box.</summary>
    private static void WalkFormXObject(BBoxWalkState bw, BBoxAccumulator acc, PdfReader reader, int depth, PdfStream xstr)
    {
        // Form XObjects carry their own /Matrix and /BBox.
        // Compose Matrix into the CTM, recurse with the form's content,
        // then clip the form's contribution to its /BBox in form-space
        // (the spec requires content outside /BBox to be clipped).
        var formCtm = bw.ctm;
        if (xstr.Dict.Get("Matrix") is PdfArray mArr && mArr.Count >= 6)
        {
            formCtm = new Cm(
                NumOf(mArr[0]), NumOf(mArr[1]),
                NumOf(mArr[2]), NumOf(mArr[3]),
                NumOf(mArr[4]), NumOf(mArr[5])).Multiply(bw.ctm);
        }
        var xbytes = reader.DecodeStream(xstr);

        // Build a per-form accumulator so we can clip its
        // contribution to /BBox before merging into the outer acc.
        var formAcc = new BBoxAccumulator();
        WalkContentForBBox(xbytes, xstr.Dict, reader, formCtm, formAcc, depth + 1);
        if (formAcc.HasAny)
        {
            // Translate /BBox to page-space using formCtm and intersect.
            var bboxArr = xstr.Dict.Get("BBox") as PdfArray;
            if (bboxArr is not null && bboxArr.Count >= 4)
            {
                var bx1 = NumOf(bboxArr[0]); var by1 = NumOf(bboxArr[1]);
                var bx2 = NumOf(bboxArr[2]); var by2 = NumOf(bboxArr[3]);
                var (px1, py1) = formCtm.Apply(bx1, by1);
                var (px2, py2) = formCtm.Apply(bx2, by1);
                var (px3, py3) = formCtm.Apply(bx2, by2);
                var (px4, py4) = formCtm.Apply(bx1, by2);
                var bboxMinX = Math.Min(Math.Min(px1, px2), Math.Min(px3, px4));
                var bboxMaxX = Math.Max(Math.Max(px1, px2), Math.Max(px3, px4));
                var bboxMinY = Math.Min(Math.Min(py1, py2), Math.Min(py3, py4));
                var bboxMaxY = Math.Max(Math.Max(py1, py2), Math.Max(py3, py4));
                var ix1 = Math.Max(formAcc.MinX, bboxMinX);
                var iy1 = Math.Max(formAcc.MinY, bboxMinY);
                var ix2 = Math.Min(formAcc.MaxX, bboxMaxX);
                var iy2 = Math.Min(formAcc.MaxY, bboxMaxY);
                if (ix1 <= ix2 && iy1 <= iy2)
                {
                    acc.IncludePoint(ix1, iy1);
                    acc.IncludePoint(ix2, iy2);
                }
            }
            else
            {
                acc.IncludePoint(formAcc.MinX, formAcc.MinY);
                acc.IncludePoint(formAcc.MaxX, formAcc.MaxY);
            }
        }
    }
}
