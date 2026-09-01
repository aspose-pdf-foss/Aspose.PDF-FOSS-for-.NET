using System.Runtime.InteropServices;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Devices.Rasterizer;
using Aspose.Pdf.IO;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Devices;

public sealed partial class SoftwarePageRenderer
{
    /// <summary>Resolve ColorSpace entry to a simple device color space name.</summary>
    private static string ResolveColorSpaceName(PdfObject? csObj, PdfReader reader)
        => ResolveImageColorSpace(csObj, reader).BaseName;

    /// <summary>Resolved image colour space: base device name and, for Indexed, the palette bytes.</summary>
    internal readonly struct ImageColorSpaceInfo
    {
        public string BaseName { get; init; }
        public byte[]? Palette { get; init; }
        public int PaletteComponents { get; init; } // bytes per palette entry (1=Gray, 3=RGB, 4=CMYK)
        // For a /Separation or /DeviceN image: the tint transform plus its
        // alternate-space family, so each sample tuple maps tint → alternate
        // components → RGB. Null for ordinary device spaces.
        public Functions.PdfFunction? TintTransform { get; init; }
        public string? AltSpaceName { get; init; }
        // Colorant count of the tint space (1 for /Separation, N for /DeviceN).
        public int TintComponents { get; init; }
    }

    /// <summary>
    /// Resolve a /ColorSpace entry. For Indexed, walks down to the base device space and extracts
    /// the palette lookup bytes (spec §8.6.6.3: [/Indexed base hival lookup], lookup is a string or stream).
    /// </summary>
    internal static ImageColorSpaceInfo ResolveImageColorSpace(PdfObject? csObj, PdfReader reader)
    {
        if (csObj is PdfName name)
            return new ImageColorSpaceInfo { BaseName = name.Value, PaletteComponents = ComponentsForBase(name.Value) };

        csObj = reader.Resolve(csObj);
        if (csObj is PdfName name2)
            return new ImageColorSpaceInfo { BaseName = name2.Value, PaletteComponents = ComponentsForBase(name2.Value) };

        if (csObj is PdfArray arr && arr.Count > 0)
        {
            var first = arr[0] is PdfName fn ? fn.Value : null;
            if (first == "ICCBased" && arr.Count > 1)
            {
                var profileStream = reader.ResolveStream(arr[1]);
                if (profileStream is not null)
                {
                    var n = (int)profileStream.Dict.GetInt("N");
                    var derived = n switch { 1 => "DeviceGray", 4 => "DeviceCMYK", _ => "DeviceRGB" };
                    return new ImageColorSpaceInfo { BaseName = derived, PaletteComponents = ComponentsForBase(derived) };
                }
            }
            if (first == "Indexed" && arr.Count >= 4)
            {
                var baseInfo = ResolveImageColorSpace(arr[1], reader);
                var lookupObj = reader.Resolve(arr[3]);
                var palette = ExtractPaletteBytes(lookupObj, reader);

                // A /Separation or /DeviceN base stores TINT samples in the palette - one
                // byte per colorant per entry, which only becomes colour through the tint
                // transform. Bake the palette to RGB here so both rasterisers' palette
                // lookups (which know Gray/RGB/CMYK layouts only) work unchanged; without
                // this the entries are read as 3-byte RGB and the image draw dies out of
                // bounds (dropping the image from the page). The transform is evaluated
                // once per palette ENTRY rather than through a 256-value LUT, which is
                // what lets a MULTI-colorant /DeviceN base work at all: a LUT can only be
                // keyed on one input, so a 2-colorant base used to fall through here with
                // 2 bytes per entry and then be read as 3.
                if (baseInfo.TintTransform is not null && baseInfo.TintComponents >= 1 && palette is not null)
                {
                    var tintComps = baseInfo.TintComponents;
                    var entries = palette.Length / tintComps;
                    var rgbPalette = new byte[entries * 3];
                    var tint = new double[tintComps];
                    for (int i = 0; i < entries; i++)
                    {
                        for (int k = 0; k < tintComps; k++) tint[k] = palette[i * tintComps + k] / 255.0;
                        var alt = baseInfo.TintTransform.Evaluate(tint);
                        byte r, g, b;
                        // No transform output: fall back to the tint read as an inverted
                        // plate, which is what BuildSeparationLut does for the same case.
                        if (alt is null) r = g = b = (byte)(255 - palette[i * tintComps]);
                        else ComponentsToRgb(alt, baseInfo.AltSpaceName ?? "DeviceGray", out r, out g, out b);
                        rgbPalette[i * 3] = r; rgbPalette[i * 3 + 1] = g; rgbPalette[i * 3 + 2] = b;
                    }
                    return new ImageColorSpaceInfo
                    {
                        BaseName = "DeviceRGB",
                        Palette = rgbPalette,
                        PaletteComponents = 3,
                    };
                }

                return new ImageColorSpaceInfo
                {
                    BaseName = baseInfo.BaseName,
                    Palette = palette,
                    PaletteComponents = baseInfo.PaletteComponents,
                };
            }
            // PDF 32000 §8.6.5.2-3: CalRGB / CalGray are device-space colours with
            // an attached calibration (Gamma/WhitePoint/Matrix). For raster image
            // decode they behave exactly like DeviceRGB / DeviceGray — without this
            // alias the blit dispatch below sees "CalRGB" / "CalGray" and silently
            // drops the image, producing a blank page for screenshot PDFs whose
            // single content op is "/Img0 Do".
            if (first == "CalRGB")
                return new ImageColorSpaceInfo { BaseName = "DeviceRGB", PaletteComponents = 3 };
            if (first == "CalGray")
                return new ImageColorSpaceInfo { BaseName = "DeviceGray", PaletteComponents = 1 };
            // /Separation (1 colorant) and single-colorant /DeviceN images carry a tint
            // transform that turns the stored sample into colour in an alternate space
            // (PDF 32000 §8.6.6.4). Capture it so the spot sample is rendered as the
            // spot colour, not as a raw grayscale plate.
            if ((first == "Separation" && arr.Count >= 4)
                || (first == "DeviceN" && arr.Count >= 4 && reader.Resolve(arr[1]) is PdfArray))
            {
                int tintComps = first == "Separation" ? 1
                    : (reader.Resolve(arr[1]) as PdfArray)?.Count ?? 1;
                var tint = Functions.PdfFunction.Parse(arr[3], reader);
                var alt = ResolveAltSpaceFamily(arr[2], reader);
                if (tint is not null && alt is not null && tintComps >= 1)
                    return new ImageColorSpaceInfo { BaseName = "Separation", PaletteComponents = tintComps, TintTransform = tint, AltSpaceName = alt, TintComponents = tintComps };
            }
            if (first is not null)
                return new ImageColorSpaceInfo { BaseName = first, PaletteComponents = ComponentsForBase(first) };
        }
        return new ImageColorSpaceInfo { BaseName = "DeviceRGB", PaletteComponents = 3 };
    }

    /// <summary>
    /// Resolve the alternate-space family of a /Separation or /DeviceN colorspace's
    /// tint output to DeviceGray/DeviceRGB/DeviceCMYK (ICCBased maps by component
    /// count). Returns null when unrecognised.
    /// </summary>
    internal static string? ResolveAltSpaceFamily(PdfObject? obj, PdfReader reader)
    {
        var resolved = reader.Resolve(obj);
        if (resolved is PdfName n)
        {
            if (n.Value is "DeviceCMYK" or "DeviceRGB" or "DeviceGray") return n.Value;
            if (n.Value == "CalGray") return "DeviceGray";
            if (n.Value == "CalRGB") return "DeviceRGB";
            return null;
        }
        if (resolved is PdfArray a && a.Count > 0 && a[0] is PdfName fam)
        {
            if (fam.Value == "ICCBased" && a.Count > 1 && reader.ResolveStream(a[1]) is { } icc)
            {
                var iccN = (int)icc.Dict.GetInt("N");
                return iccN switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
            }
            if (fam.Value == "CalGray") return "DeviceGray";
            if (fam.Value == "CalRGB") return "DeviceRGB";
            if (fam.Value == "Lab") return "Lab";
        }
        return null;
    }

    /// <summary>
    /// Build a 256-entry RGB lookup table for a single-component /Separation image:
    /// sample byte → tint (0..1) → alternate components → RGB. <paramref name="invert"/>
    /// applies a /Decode [1 0] reversal. Returns 256×3 packed RGB bytes.
    /// </summary>
    /// <summary>
    /// Expand a 1/2/4/8-bpc single-component /Separation (or /DeviceN) image to packed RGB
    /// using a 256-entry tint LUT. Sub-byte samples are scaled to the LUT's 0..255 index
    /// range (so a 1-bpc sample of 1 maps to LUT[255] = full tint).
    /// </summary>
    internal static byte[] SeparationSamplesToRgb(byte[] data, int w, int h, int bpc, byte[] lut256)
    {
        var rgb = new byte[w * h * 3];
        var rowBytes = (w * bpc + 7) / 8;
        var maxv = (1 << bpc) - 1;
        for (int y = 0; y < h; y++)
        {
            int rowBase = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                int bitPos = x * bpc;
                int bi = rowBase + (bitPos >> 3);
                int sample = bi < data.Length ? (data[bi] >> (8 - bpc - (bitPos & 7))) & maxv : 0;
                int idx = bpc == 8 ? sample : sample * 255 / maxv;
                int o = (y * w + x) * 3;
                rgb[o] = lut256[idx * 3]; rgb[o + 1] = lut256[idx * 3 + 1]; rgb[o + 2] = lut256[idx * 3 + 2];
            }
        }
        return rgb;
    }

    /// <summary>
    /// PDF/X overprint-simulation LUT for a spot (Separation / 1-colorant DeviceN)
    /// image: the sample is the colorant COVERAGE — no ink (tint 0) stays paper
    /// white so the overprint Multiply leaves the backdrop untouched, and full
    /// tint takes the colorant's own (tint-1) alternate colour. The alternate
    /// value at tint 0 (which can be non-white, e.g. a Pantone plate whose /C0 is
    /// a visible wash) is deliberately NOT used — that is the composited,
    /// non-PDF/X rendering of the same plate.
    /// </summary>
    internal static byte[] BuildSeparationOverprintLut(ImageColorSpaceInfo cs, bool invert)
    {
        byte fr = 0, fg = 0, fb = 0;
        var full = cs.TintTransform!.Evaluate(new[] { 1.0 });
        if (full is not null) ComponentsToRgb(full, cs.AltSpaceName ?? "DeviceGray", out fr, out fg, out fb);
        var lut = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            var t = (invert ? 255 - i : i) / 255.0;
            lut[i * 3] = (byte)Math.Round(255 + (fr - 255) * t);
            lut[i * 3 + 1] = (byte)Math.Round(255 + (fg - 255) * t);
            lut[i * 3 + 2] = (byte)Math.Round(255 + (fb - 255) * t);
        }
        return lut;
    }

    /// <summary>True when the document IDENTIFIES as PDF/X — the gate for
    /// overprint simulation (PDF/X output honours overprint, plain viewing
    /// composites). The marker is the document-info <c>GTS_PDFXVersion</c> key
    /// (the PDF/X-1 identification), NOT the catalog /OutputIntents: a file whose
    /// latest revision dropped the identification but kept the (inheritable)
    /// output intent renders composited.</summary>
    internal static bool HasPdfXOutputIntent(PdfReader reader)
    {
        try
        {
            return reader.ResolveDict(reader.Trailer.Get("Info"))
                ?.ContainsKey("GTS_PDFXVersion") == true;
        }
        catch { /* malformed trailer: treat as non-PDF/X */ }
        return false;
    }

    internal static byte[] BuildSeparationLut(ImageColorSpaceInfo cs, bool invert)
    {
        var lut = new byte[256 * 3];
        var input = new double[1];
        for (int i = 0; i < 256; i++)
        {
            input[0] = (invert ? 255 - i : i) / 255.0;
            byte r, g, b;
            var alt = cs.TintTransform!.Evaluate(input);
            if (alt is null) { r = g = b = (byte)(255 - i); }
            else ComponentsToRgb(alt, cs.AltSpaceName ?? "DeviceGray", out r, out g, out b);
            lut[i * 3] = r; lut[i * 3 + 1] = g; lut[i * 3 + 2] = b;
        }
        return lut;
    }

    private static int ComponentsForBase(string baseName) => baseName switch
    {
        "DeviceGray" or "G" or "CalGray" => 1,
        "DeviceCMYK" or "CMYK" => 4,
        _ => 3,
    };

    private static byte[]? ExtractPaletteBytes(PdfObject? lookup, PdfReader reader)
    {
        // Lookup can be a literal PdfString or a referenced stream.
        if (lookup is PdfString str) return str.Value;
        if (lookup is PdfStream ps)
        {
            try { return reader.DecodeStream(ps); }
            catch { return null; }
        }
        return null;
    }
}
