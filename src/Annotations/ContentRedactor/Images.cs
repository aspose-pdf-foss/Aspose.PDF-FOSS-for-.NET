using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf.Annotations;

internal sealed partial class ContentRedactor
{
    /// <summary>A copy of the image drawn under <paramref name="ctm"/> whose pixels meeting the area
    /// are painted the fill colour, and its masks' made opaque there, so the shape of what was
    /// there goes too; null when its samples cannot be read.</summary>
    private PdfStream? BlankedImage(PdfStream image, double[] ctm, ResourceCopy resources, bool isMask = false)
    {
        if (UnitArea(ctm) is not { } area) return null;
        byte[] decoded;
        try { decoded = _reader.DecodeStream(image); }
        catch (Exception) { return null; }
        if (Painted(image.Dict, decoded, area, resources, isMask, image.RawData) is not { } result) return null;
        var (dict, data) = result;

        foreach (var maskKey in new[] { "SMask", "Mask" })
        {
            if (_reader.Resolve(image.Dict.Get(maskKey)) is not PdfStream mask) continue;
            if (BlankedImage(mask, ctm, resources, isMask: true) is { } blankedMask) dict.Set(maskKey, blankedMask);
            else dict.Remove(maskKey);
        }
        return new PdfStream(dict, data);
    }

    /// <summary>Plan an inline image as <see cref="PlanImage"/> plans a drawn one; the new BI ... EI
    /// replaces it when its operation ends.</summary>
    private void PlanInlineImage(Walk walk, PdfDictionary dict, byte[] data, GraphicsState state)
    {
        var ctm = state.Ctm;
        var coverage = CoverageOf(UnitSquare.Select(p => Transform(ctm, p.X, p.Y)).ToList());
        if (coverage == Coverage.Outside) return;
        walk.InlineReplacement = [];
        if (coverage == Coverage.Inside || UnitArea(ctm) is not { } area) return;
        byte[] decoded;
        try { decoded = StreamFilter.Decode(data, dict); }
        catch (Exception) { return; }
        if (Painted(dict, decoded, area, walk.Resources, isMask: false) is not { } result) return;
        var (painted, samples) = result;
        walk.InlineReplacement = InlineImage(painted, samples);
    }

    /// <summary>BI ... ID ... EI for an image, its data hex-encoded over its own filter so it holds
    /// no EI a reader could stop at.</summary>
    private static byte[] InlineImage(PdfDictionary dict, byte[] data)
    {
        var filter = dict.Get("Filter")!;
        dict.Set("Filter", new PdfArray([new PdfName("ASCIIHexDecode"), filter]));
        if (dict.Get("DecodeParms") is { } parms) dict.Set("DecodeParms", new PdfArray([PdfNull.Instance, parms]));

        using var output = new MemoryStream();
        var writer = new IO.PdfWriter(output);
        output.Write(Encoding.ASCII.GetBytes("\nBI\n"), 0, 4);
        foreach (var key in dict.Keys)
        {
            writer.WriteObject(new PdfName(key));
            output.WriteByte((byte)' ');
            writer.WriteObject(dict.Get(key)!);
            output.WriteByte((byte)'\n');
        }
        var hex = new StringBuilder("ID\n", data.Length * 2 + 10);
        foreach (var b in data) hex.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        hex.Append(">\nEI\n");
        var tail = Encoding.ASCII.GetBytes(hex.ToString());
        output.Write(tail, 0, tail.Length);
        return output.ToArray();
    }

    private static void EndInlineImage(Walk walk, int end)
    {
        if (walk.InlineReplacement is { } replacement)
        {
            walk.Edits.Add(new Edit(walk.PreviousEnd, end, replacement));
            foreach (var mark in walk.OpenMarks) mark.Redacted = true;
        }
        walk.InlineReplacement = null;
        walk.PreviousEnd = end;
    }

    /// <summary>The area's corners in an image's unit square, or null when the image collapses.</summary>
    private List<(double X, double Y)>? UnitArea(double[] ctm) =>
        Invert(ctm) is { } inverse ? _corners.Select(c => Transform(inverse, c.X, c.Y)).ToList() : null;

    /// <summary>The columns of each row whose pixels lie wholly in the area (unit-square space; row
    /// 0 is the top of the image): first and last; rows with none are left out. A pixel only partly
    /// in the area shows beside it anyway, so it keeps its colour - and a coarse image does not
    /// spill painted pixels past the area.</summary>
    private static IEnumerable<(int Row, int First, int Last)> CoveredPixels(int width, int height,
        List<(double X, double Y)> area)
    {
        for (var row = 0; row < height; row++)
        {
            double bottom = (double)(height - 1 - row) / height, top = (double)(height - row) / height;
            if (Span(area, bottom) is not { } low || Span(area, top) is not { } high) continue;
            var first = Math.Max(0, (int)Math.Ceiling(Math.Max(low.From, high.From) * width - PixelSlack));
            var last = Math.Min(width - 1, (int)Math.Floor(Math.Min(low.To, high.To) * width + PixelSlack) - 1);
            if (first <= last) yield return (row, first, last);
        }
    }

    /// <summary>Where the line at height <paramref name="y"/> runs through a convex polygon.</summary>
    private static (double From, double To)? Span(List<(double X, double Y)> polygon, double y)
    {
        double from = double.MaxValue, to = double.MinValue;
        for (var i = 0; i < polygon.Count; i++)
        {
            var p = polygon[i];
            var q = polygon[(i + 1) % polygon.Count];
            if ((p.Y - y) * (q.Y - y) > 0 || p.Y == q.Y && p.Y != y) continue;
            if (p.Y == q.Y)
            {
                from = Math.Min(from, Math.Min(p.X, q.X));
                to = Math.Max(to, Math.Max(p.X, q.X));
                continue;
            }
            var x = p.X + (y - p.Y) / (q.Y - p.Y) * (q.X - p.X);
            from = Math.Min(from, x);
            to = Math.Max(to, x);
        }
        return from <= to ? (from, to) : null;
    }
}
