using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
// The steps of the in-cell metric <img>: the natural-box reserve, the jpeg source read, the absolute placement.

    /// <summary>Reserves the image box from the natural size of a file-backed source when no size is declared.</summary>
    private static void ReserveMetricImgNaturalBox(MetricImgOpenState mi)
    {
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1"
            && mi.tok.Attributes is { } trA && trA.TryGetValue("src", out var trSrc))
            TraceImgOpen(mi, trSrc, Console.Error);
        if (mi.mps.cell!.ImgWPt <= 0 && mi.mps.cell.ImgHPt <= 0
            && mi.tok.Attributes is { } iaN
            && iaN.TryGetValue("src", out var nsrc)
            && (!nsrc.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || nsrc.Contains("base64,R0lGOD", StringComparison.Ordinal)
                || MislabelledPngDataUri(nsrc))
            && LoadConverterImage(DecodeEntities(nsrc), mi.loadOptions) is { Length: > 0 } nbytes
            && TryReadImagePixelSize(nbytes) is (var npw, var nph))
        {
            mi.mps.cell.ImgBytes = nbytes;
            mi.mps.cell.ImgWPt = npw * PxPt;
            mi.mps.cell.ImgHPt = nph * PxPt;
        }
        // A file the loader cannot find, with no alt to show, is the browser's broken-image
        // box: a 34 pt square, its 1 pt bevel dark on top and left, light below and right
        // (probed on the missing header logo: frame 98.25..132.25, the row 34 + its paddings).
        // …and a missing picture the markup SIZED is the broken-image box at that size, its alt
        // unseen, inline after the line's text (measured on the valuation report: 20 × 16 px print
        // and mail icons beside their link text, no alt drawn).
        // …and a REMOTE picture with no alt is the same broken-image box at its declared size, a
        // 6 px gutter on either side of its bevel (probed on the safety data sheet's header logo:
        // a 72 × 79 px remote gif in a 1 px column draws its bevel 100.55..156.55 and holds the
        // column at 65 = 4.5 + 56 + 4.5; a remote picture WITH alt keeps the alt-text model).
        else if (mi.stdSerif && mi.mps.cell.ImgBytes is null
            && mi.tok.Attributes is { } iaB
            && iaB.TryGetValue("src", out var bsrc)
            && !bsrc.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            // (…unless the reference fetched it through the document's remote base: no box then)
            && !ImageWasFetchedByReference(bsrc)
            && (!bsrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase)
                || (!iaB.TryGetValue("alt", out var ralt) || string.IsNullOrWhiteSpace(ralt))
                    && (mi.mps.cell.ImgWPt > 0 || mi.mps.cell.ImgHPt > 0))
            && (!iaB.TryGetValue("alt", out var balt) || string.IsNullOrWhiteSpace(balt)
                || mi.mps.cell.ImgWPt > 0 || mi.mps.cell.ImgHPt > 0)
            // (a source that yields no DECODABLE picture is broken too: a remote host answering a
            // fetch with an error page)
            && (LoadConverterImage(DecodeEntities(bsrc), mi.loadOptions) is not { Length: > 0 } brokenBytes
                || TryReadImagePixelSize(brokenBytes) is null))
        {
            mi.mps.cell.ImgPlaceholder = true;
            mi.mps.cell.ImgRemoteBroken = bsrc.TrimStart().StartsWith("http", StringComparison.OrdinalIgnoreCase);
            if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_CELL") == "1")
                Console.Error.WriteLine($"[imgph] src={bsrc[..Math.Min(40, bsrc.Length)]} w={mi.mps.cell.ImgWPt:0.#} h={mi.mps.cell.ImgHPt:0.#} remote={mi.mps.cell.ImgRemoteBroken}");
            if (mi.mps.cell.ImgWPt <= 0 && mi.mps.cell.ImgHPt <= 0)
            {
                mi.mps.cell.ImgWPt = BrokenImageBoxPt;
                mi.mps.cell.ImgHPt = BrokenImageBoxPt;
            }
            else
            {
                if (mi.mps.cell.ImgWPt <= 0) mi.mps.cell.ImgWPt = mi.mps.cell.ImgHPt;
                if (mi.mps.cell.ImgHPt <= 0) mi.mps.cell.ImgHPt = mi.mps.cell.ImgWPt;
                // (the bevel stands OUTSIDE the declared box: a 20 × 16 px icon frames 17 × 14 pt)
                mi.mps.cell.ImgWPt += 2 * BrokenImageBevelPt;
                mi.mps.cell.ImgHPt += 2 * BrokenImageBevelPt;
                mi.mps.cell.ImgAfterText = !MetricTextIsBlank(mi.text);
            }
        }
    }

    /// <summary>The broken-image box a missing picture without alt text draws, in pt (probed: 34 × 34).</summary>
    /// <summary>ASPOSE_TRACE_CELL=1: the state an in-cell image opens with.</summary>
    private static void TraceImgOpen(MetricImgOpenState mi, string src, System.IO.TextWriter err)
    {
        byte[]? loaded = null;
        try { loaded = LoadConverterImage(DecodeEntities(src), mi.loadOptions); } catch (Exception e) { err.WriteLine("[imgopen] load threw " + e.GetType().Name); }
        err.WriteLine($"[imgopen] src={src[..Math.Min(40, src.Length)]} serif={mi.stdSerif} bytes={(mi.mps.cell!.ImgBytes?.Length ?? 0)} w={mi.mps.cell.ImgWPt:0.#} h={mi.mps.cell.ImgHPt:0.#} loaded={(loaded?.Length ?? 0)} alt={(mi.tok.Attributes is { } a && a.TryGetValue("alt", out var al) ? "'" + al + "'" : "-")}");
    }

    private const double BrokenImageBoxPt = 34.0;
    /// <summary>The broken-image bevel: 1 pt, dark (#555) top and left, light (#aaa) bottom and right.</summary>
    private const double BrokenImageBevelPt = 1.0;
    /// <summary>The 6 px gutter a remote broken image's bevel keeps on either side in its cell
    /// (probed: bevel 100.55..156.55 in a cell starting at 96; the column holds 4.5 + 56 + 4.5).</summary>
    private const double RemoteBrokenImageGutterPt = 4.5;
    private const double BrokenImageDarkGray = 0.333;
    private const double BrokenImageLightGray = 0.667;

    /// <summary>Reads an inline base64 jpeg source into the cell.</summary>
    private static void ReadMetricImgJpegSource(MetricImgOpenState mi)
    {
        if (mi.tok.Attributes is { } iaJ && iaJ.TryGetValue("src", out var jsrc)
            && Regex.Match(jsrc, @"^data:image/jpe?g;base64,(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
                is { Success: true } jdm)
        {
            byte[]? jb = null;
            try { jb = System.Convert.FromBase64String(jdm.Groups[1].Value); }
            catch { }
            if (jb is not null && JpegDims(jb) is { w: > 0, h: > 0 } jd)
            {
                var jNatW = jd.w * PxPt;
                var jDrawW = Math.Min(jNatW, ImgRuleMaxWidthPt(mi.css) ?? JpegViewportPt);
                mi.mps.cell!.ImgBytes = jb;
                mi.mps.cell.ImgWPt = jDrawW;
                mi.mps.cell.ImgHPt = jDrawW * jd.h / jd.w;
            }
        }
    }

    /// <summary>Places an inline base64 png source absolutely when a pending left fraction asks for it.</summary>
    private static void PlaceMetricImgAbsolute(MetricImgOpenState mi)
    {
        if (mi.mps.pendingAbsLeftFrac >= 0 && mi.mps.cell!.AbsPng is null
            && mi.tok.Attributes is { } iaP
            && iaP.TryGetValue("src", out var psrc)
            && Regex.Match(psrc, @"^data:image/png;base64,(.+)$",
                RegexOptions.IgnoreCase | RegexOptions.Singleline)
                is { Success: true } pdm)
        {
            try
            {
                mi.mps.cell.AbsPng = System.Convert.FromBase64String(pdm.Groups[1].Value);
                mi.mps.cell.AbsPngLeftFrac = mi.mps.pendingAbsLeftFrac;
            }
            catch { }
        }
    }
}
