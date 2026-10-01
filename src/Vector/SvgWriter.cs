using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Aspose.Pdf.Vector;

/// <summary>Writes graphic elements as one standalone SVG document.</summary>
internal static class SvgWriter
{
    /// <summary>CSS pixels per PDF point: 96 dpi over 72 dpi.</summary>
    internal const double PixelsPerPoint = 4.0 / 3.0;

    /// <summary>Serial number for the emitted SVG document ids ("body_N").</summary>
    private static int _bodyCounter;

    /// <summary>Serial number for the emitted clip-path ids ("clip_N"), unique across documents.</summary>
    private static int _clipCounter;

    /// <summary>A fresh clip-path id.</summary>
    internal static string NextClipId() => "clip_" + System.Threading.Interlocked.Increment(ref _clipCounter);

    /// <summary>The SVG document drawing <paramref name="elements"/> inside <paramref name="box"/>
    /// (page space, points): the box's lower-left corner is the document's origin, the
    /// viewport is the box in CSS pixels.</summary>
    internal static string Document(IEnumerable<GraphicElement> elements, Rectangle box)
    {
        var id = System.Threading.Interlocked.Increment(ref _bodyCounter);
        var width = Math.Max(0.0, box.URX - box.LLX);
        var height = Math.Max(0.0, box.URY - box.LLY);
        var pixelWidth = (int)Math.Ceiling(width * PixelsPerPoint) + 1;
        var pixelHeight = (int)Math.Ceiling(height * PixelsPerPoint) + 1;
        var scale = PixelsPerPoint.ToString("0.####", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" standalone=\"no\"?>\n");
        sb.Append("<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n");
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
            $"version=\"1.1\" id=\"body_{id}\" width=\"{pixelWidth}\" height=\"{pixelHeight}\">\n\n");
        sb.Append($"<g transform=\"matrix({scale} 0 0 {scale} 0 0)\">\n");
        foreach (var element in elements)
            element.AppendSvgContent(sb, box.LLX, box.LLY, height);
        sb.Append("</g>\n");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>The smallest rectangle holding what every element paints over; empty when there are none.</summary>
    internal static Rectangle Union(IEnumerable<GraphicElement> elements)
    {
        double llx = double.MaxValue, lly = double.MaxValue, urx = double.MinValue, ury = double.MinValue;
        var any = false;
        foreach (var element in elements)
        {
            var r = element.PaintBounds;
            llx = Math.Min(llx, r.LLX);
            lly = Math.Min(lly, r.LLY);
            urx = Math.Max(urx, r.URX);
            ury = Math.Max(ury, r.URY);
            any = true;
        }
        return any ? new Rectangle(llx, lly, urx, ury) : Rectangle.Empty;
    }

    /// <summary>Writes <paramref name="svg"/> to <paramref name="path"/>, making the folder when needed.</summary>
    internal static void Save(string path, string svg)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(path, svg);
    }
}
