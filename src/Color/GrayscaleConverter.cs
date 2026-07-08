using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Converts a page's colours to grayscale in place: content-stream colour operators,
/// image XObjects, named colour-space resources, and annotation appearances.
/// </summary>
internal static class GrayscaleConverter
{
    private const string Num = @"(-?\d*\.?\d+)";
    private const string Ws = @"\s+";

    public static void ConvertPage(Page page)
    {
        var reader = page.Reader;
        if (reader is null) return;

        var content = page.GetContentStreamBytes();
        if (content is not null)
            page.SetContentStream(ConvertContentBytes(content));

        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is not null)
            ConvertResources(resources, reader, visited);

        ConvertAnnotations(page, reader, visited);
    }

    /// <summary>Rewrite RGB/CMYK colour-setting operators in a content stream to their
    /// grayscale equivalents.</summary>
    private static byte[] ConvertContentBytes(byte[] content)
    {
        var s = Encoding.Latin1.GetString(content);
        s = Regex.Replace(s, $@"{Num}{Ws}{Num}{Ws}{Num}{Ws}rg(?![A-Za-z])", m => Emit(RgbToGray(m), "g"));
        s = Regex.Replace(s, $@"{Num}{Ws}{Num}{Ws}{Num}{Ws}RG(?![A-Za-z])", m => Emit(RgbToGray(m), "G"));
        s = Regex.Replace(s, $@"{Num}{Ws}{Num}{Ws}{Num}{Ws}{Num}{Ws}k(?![A-Za-z])", m => Emit(CmykToGray(m), "g"));
        s = Regex.Replace(s, $@"{Num}{Ws}{Num}{Ws}{Num}{Ws}{Num}{Ws}K(?![A-Za-z])", m => Emit(CmykToGray(m), "G"));
        // sc/scn (and stroke SC/SCN) set a colour in the current colour space. When the
        // space's resource has been converted to DeviceGray, an RGB (3-operand) or CMYK
        // (4-operand) colour must collapse to a single gray component to stay valid — and
        // so the colour detector no longer classifies the run as RGB/CMYK. Match 4-operand
        // first so a CMYK colour isn't partially consumed by the 3-operand rule. Numeric
        // operands only: a pattern operand (e.g. "/P0 scn") is left untouched.
        s = Regex.Replace(s, $@"{Num}{Ws}{Num}{Ws}{Num}{Ws}{Num}{Ws}(scn|sc|SCN|SC)(?![A-Za-z])",
            m => Emit(CmykToGray(m), m.Groups[5].Value));
        s = Regex.Replace(s, $@"{Num}{Ws}{Num}{Ws}{Num}{Ws}(scn|sc|SCN|SC)(?![A-Za-z])",
            m => Emit(RgbToGray(m), m.Groups[4].Value));
        return Encoding.Latin1.GetBytes(s);
    }

    private static double RgbToGray(Match m) =>
        0.299 * D(m.Groups[1].Value) + 0.587 * D(m.Groups[2].Value) + 0.114 * D(m.Groups[3].Value);

    private static double CmykToGray(Match m)
    {
        double c = D(m.Groups[1].Value), mm = D(m.Groups[2].Value), y = D(m.Groups[3].Value), k = D(m.Groups[4].Value);
        double r = (1 - c) * (1 - k), g = (1 - mm) * (1 - k), b = (1 - y) * (1 - k);
        return 0.299 * r + 0.587 * g + 0.114 * b;
    }

    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    private static string Emit(double v, string op)
    {
        if (v < 0) v = 0; else if (v > 1) v = 1;
        return v.ToString("0.####", CultureInfo.InvariantCulture) + " " + op;
    }

    /// <summary>Convert images, named colour spaces, and nested form XObjects in a /Resources dict.</summary>
    private static void ConvertResources(PdfDictionary resources, PdfReader reader, HashSet<PdfDictionary> visited)
    {
        // Named colour-space resources: RGB/CMYK families become DeviceGray.
        if (reader.ResolveDict(resources.Get("ColorSpace")) is { } csDict)
            foreach (var key in csDict.Keys.ToList())
            {
                var v = reader.Resolve(csDict.Get(key));
                if (IsRgbOrCmyk(v, reader))
                    csDict.Set(key, new PdfName("DeviceGray"));
            }

        // XObjects: convert image samples, recurse into form XObjects' own content/resources.
        if (reader.ResolveDict(resources.Get("XObject")) is { } xobjDict)
            foreach (var key in xobjDict.Keys.ToList())
            {
                if (reader.Resolve(xobjDict.Get(key)) is not PdfStream xs) continue;
                var subtype = xs.Dict.GetName("Subtype");
                if (subtype == "Image")
                    new XImage(key, xs, reader).ConvertToGrayscale();
                else if (subtype == "Form" && visited.Add(xs.Dict))
                {
                    var formBytes = reader.DecodeStream(xs);
                    var converted = ConvertContentBytes(formBytes);
                    ReplaceStream(xs, converted);
                    if (reader.ResolveDict(xs.Dict.Get("Resources")) is { } formRes)
                        ConvertResources(formRes, reader, visited);
                }
            }
    }

    private static void ConvertAnnotations(Page page, PdfReader reader, HashSet<PdfDictionary> visited)
    {
        if (reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) return;
        foreach (var a in annots)
        {
            if (reader.ResolveDict(a) is not { } annot) continue;
            if (reader.ResolveDict(annot.Get("AP")) is not { } ap) continue;
            foreach (var apKey in ap.Keys)
            {
                var entry = reader.Resolve(ap.Get(apKey));
                if (entry is PdfStream s && visited.Add(s.Dict))
                {
                    ReplaceStream(s, ConvertContentBytes(reader.DecodeStream(s)));
                    if (reader.ResolveDict(s.Dict.Get("Resources")) is { } res)
                        ConvertResources(res, reader, visited);
                }
                else if (entry is PdfDictionary states)
                {
                    foreach (var sk in states.Keys)
                        if (reader.Resolve(states.Get(sk)) is PdfStream ss && visited.Add(ss.Dict))
                        {
                            ReplaceStream(ss, ConvertContentBytes(reader.DecodeStream(ss)));
                            if (reader.ResolveDict(ss.Dict.Get("Resources")) is { } res)
                                ConvertResources(res, reader, visited);
                        }
                }
            }
        }
    }

    private static bool IsRgbOrCmyk(PdfObject? cs, PdfReader reader)
    {
        switch (cs)
        {
            case PdfName n: return n.Value is "DeviceRGB" or "CalRGB" or "DeviceCMYK";
            case PdfArray arr when arr.Count > 0:
                var head = reader.Resolve(arr[0]) as PdfName;
                if (head?.Value is "CalRGB") return true;
                if (head?.Value is "ICCBased" && reader.ResolveStream(arr[1]) is { } icc)
                    return icc.Dict.GetInt("N") is 3 or 4;
                return false;
            default: return false;
        }
    }

    private static void ReplaceStream(PdfStream stream, byte[] decoded)
    {
        stream.Dict.Remove("Filter");
        stream.Dict.Remove("DecodeParms");
        stream.Dict.Set("Length", new PdfInteger(decoded.Length));
        stream.ReplaceData(decoded);
    }
}
