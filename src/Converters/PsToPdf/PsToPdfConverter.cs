using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

using Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// Converts PostScript and Encapsulated PostScript to PDF by interpreting the
/// program and recording what it paints.
///
/// Supported: the interpreter core — scanner, operand/dictionary/execution stacks,
/// procedures, control flow, save and restore, resources, and reading data out of the
/// program's own source — together with coordinate transformations, path construction
/// including arcs and Beziers, clipping, filling and stroking, user paths, forms, the
/// colour operators (all converted to RGB the way the reference converter writes
/// them), the line-style parameters, sampled images and stencils in every operator
/// spelling, and text: the standard fourteen faces referenced by name, any other face
/// outlined into a Type 3 font from its own program, fonts whose glyphs the program
/// draws itself, and character paths. The page is the default size unless
/// <c>setpagedevice</c> asks otherwise.
///
/// Also supported: tiling patterns, coloured and uncoloured; composite fonts under
/// both the escape rule and the two-byte rule; and the outline of a stroke, which a
/// program fills, clips to, or strokes again.
///
/// The raster-device controls — halftones, screens, transfer functions, undercolour
/// removal — are accepted and ignored, as they are by the reference converter.
/// </summary>
internal static class PsToPdfConverter
{
    /// <summary>Convert a PostScript file.</summary>
    public static Document Convert(string path, PsLoadOptions? options = null) =>
        Convert(File.ReadAllBytes(path), options);

    /// <summary>Convert PostScript source bytes.</summary>
    public static Document Convert(byte[] source, PsLoadOptions? options = null)
    {
        source ??= new byte[0];
        var graphics = new PsGraphics();
        ApplyEncapsulatedExtent(graphics, source);
        var interpreter = new PsInterpreter(graphics)
        {
            Fonts = new PsFontLibrary(options?.FontsFolders ?? new string[0]),
        };
        RunGuarded(interpreter, source);
        return BuildDocument(graphics);
    }

    /// <summary>Convert to the PDF bytes the document loader wants.</summary>
    public static byte[] ConvertToBytes(byte[] source, PsLoadOptions? options = null)
    {
        using var buffer = new MemoryStream();
        Convert(source, options).Save(buffer);
        return buffer.ToArray();
    }

    /// <summary>Convert a file to PDF bytes.</summary>
    public static byte[] ConvertToBytes(string path, PsLoadOptions? options = null) =>
        ConvertToBytes(File.ReadAllBytes(path), options);

    /// <summary>Run the program, letting nothing it does abort the conversion: a
    /// source that faults must still yield the marks it made beforehand.</summary>
    private static void RunGuarded(PsInterpreter interpreter, byte[] source)
    {
        try
        {
            interpreter.Run(source);
        }
        catch (PsErrorSignal e)
        {
            interpreter.RecordError(e.ErrorName);
        }
        catch (PsStopSignal)
        {
        }
        catch (PsQuitSignal)
        {
        }
        catch (PsExitSignal)
        {
        }
    }

    /// <summary>An artwork's declared extent, and the header that makes it binding.
    /// A file only DECLARES itself encapsulated on its first line; a bounding-box
    /// comment anywhere else is prose. One corpus sample discusses encapsulation and
    /// writes such a comment without the header, and honouring it there moves every
    /// mark on the page.</summary>
    private static readonly Regex EncapsulatedHeader = new(
        @"\A%!PS-Adobe-[\d.]+\s+EPSF-", RegexOptions.None);

    private static readonly Regex BoundingBoxComment = new(
        @"^%%BoundingBox:\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)",
        RegexOptions.Multiline);

    /// <summary>How much of the source is scanned for the header and its box. Both
    /// belong at the very top, so a deeper scan would only find quoted artwork.</summary>
    private const int HeaderScanBytes = 8192;

    /// <summary>Size the page to an encapsulated artwork's own extent, and put its
    /// origin at the page's, so the marks land where the artwork expects them.</summary>
    private static void ApplyEncapsulatedExtent(PsGraphics graphics, byte[] source)
    {
        var header = Compat.Latin1.GetString(source, 0, Math.Min(source.Length, HeaderScanBytes));
        if (!EncapsulatedHeader.IsMatch(header)) return;
        var match = BoundingBoxComment.Match(header);
        if (!match.Success) return;
        if (!Number(match, 1, out var loX) || !Number(match, 2, out var loY) ||
            !Number(match, 3, out var hiX) || !Number(match, 4, out var hiY))
            return;
        if (hiX - loX <= 0 || hiY - loY <= 0) return;
        graphics.ResizePage(hiX - loX, hiY - loY);
        graphics.State.Ctm = PsMatrix.Translation(-loX, -loY);
    }

    private static bool Number(Match match, int group, out double value) =>
        double.TryParse(match.Groups[group].Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out value);

    /// <summary>Assemble the pages the interpreter produced.</summary>
    private static Document BuildDocument(PsGraphics graphics)
    {
        var doc = Document.Create();
        foreach (var writer in graphics.FinishedPages())
        {
            var page = doc.Pages.Add(writer.Width, writer.Height);
            writer.ApplyResources(page);
            page.SetContentStream(Compat.Latin1.GetBytes(writer.Content));
        }

        return doc;
    }
}
