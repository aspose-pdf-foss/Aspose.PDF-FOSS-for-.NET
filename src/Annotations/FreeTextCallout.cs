using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;

namespace Aspose.Pdf.Annotations;

public partial class FreeTextAnnotation
{
    /// <summary>The callout of a callout free text that has a leader (/IT FreeTextCallout with /CL): its box and
    /// text move into the inner rectangle /RD leaves, read as its left, bottom, right and top differences, and the
    /// leader is drawn through the /CL points. A turned free text keeps the plain box.</summary>
    private Point[]? PrepareCallout(FreeTextAppearanceState ft)
    {
        if (ft.rotated || Intent != FreeTextIntent.FreeTextCallout || Callout is not { Length: >= 2 } points) return null;
        double left = 0, bottom = 0, right = 0, top = 0;
        if (InternalReader.Resolve(Dict.Get("RD")) is PdfArray rd && rd.Count >= 4)
        {
            left = Math.Max(0, PdfArrayHelper.GetDouble(rd, 0));
            bottom = Math.Max(0, PdfArrayHelper.GetDouble(rd, 1));
            right = Math.Max(0, PdfArrayHelper.GetDouble(rd, 2));
            top = Math.Max(0, PdfArrayHelper.GetDouble(rd, 3));
        }
        ft.calloutDx = left;
        ft.calloutDy = bottom;
        ft.w = Math.Max(1.0, ft.w - left - right);
        ft.h = Math.Max(1.0, ft.h - bottom - top);
        ft.avail = Math.Max(1.0, ft.w - 2 * ft.inset);
        return points;
    }

    /// <summary>Open the inner rectangle the box and text of a callout are drawn in.</summary>
    private static void BeginCalloutBox(FreeTextAppearanceState ft) =>
        ft.sb.Append("q 1 0 0 1 ").Append(FtFmt(ft, ft.calloutDx)).Append(' ').Append(FtFmt(ft, ft.calloutDy)).Append(" cm\n");

    /// <summary>Close the inner rectangle and draw the leader: the /CL polyline in the border colour, with the
    /// arrow ending at its start pulled back to meet the head, as a line annotation draws one.</summary>
    private void EndCalloutBox(FreeTextAppearanceState ft, Point[] points)
    {
        ft.sb.Append("Q\n");
        var width = ft.borderWidth > 0 ? ft.borderWidth : 1;
        var pts = points.Select(p => (x: p.X - ft.rect!.LLX, y: p.Y - ft.rect.LLY)).ToArray();
        var arrow = LineAnnotation.IsArrowEnding(StartingStyle);
        var (sx, sy) = LineAnnotation.MovePointToward(pts[0].x, pts[0].y, pts[1].x, pts[1].y, arrow ? width : 0);

        var b = new Content.ContentStreamBuilder();
        b.SaveState();
        b.SetStrokeColor(ft.color.R / 255.0, ft.color.G / 255.0, ft.color.B / 255.0);
        b.SetLineWidth(width);
        b.MoveTo(sx, sy);
        for (var i = 1; i < pts.Length; i++) b.LineTo(pts[i].x, pts[i].y);
        b.Stroke();
        if (arrow) LineAnnotation.DrawOpenArrowHead(b, pts[1].x, pts[1].y, pts[0].x, pts[0].y, width);
        b.RestoreState();
        ft.sb.Append(Compat.Latin1.GetString(b.Build()));
    }
}
