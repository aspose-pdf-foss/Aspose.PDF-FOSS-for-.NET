using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Detects the dominant color type of a PDF page by parsing its content stream
/// and inspecting image XObject color spaces.
/// </summary>
internal static class ColorDetectHelper
{
    /// <summary>
    /// Determine the color type of a page based on its content stream operators
    /// and image color spaces.
    /// </summary>
    public static ColorType GetColorType(Page page)
    {
        var reader = page.Reader;
        var contentStreams = GetContentStreams(page, reader);
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));

        var hasRgb = false;
        var hasGray = false;
        var hasCmyk = false;
        var hasAnyColor = false;

        // Analyze content stream operators
        foreach (var streamBytes in contentStreams)
        {
            AnalyzeContentStream(streamBytes, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
        }

        // Analyze image XObject color spaces and resources
        if (resources is not null)
        {
            AnalyzeImageColorSpaces(resources, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
            AnalyzeColorSpaceResources(resources, reader, ref hasRgb, ref hasGray, ref hasCmyk);
        }

        // Analyze annotation appearance stream content (Form XObjects)
        AnalyzeAnnotations(page, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);

        // Determine the dominant color type:
        // RGB or CMYK present -> Rgb
        // Only gray operators -> Grayscale
        // No color operators or only black/white values -> BlackAndWhite
        if (hasRgb || hasCmyk)
            return ColorType.Rgb;
        if (hasGray)
            return ColorType.Grayscale;

        // If color operators were found but none set hasGray/hasRgb/hasCmyk,
        // the only values used were 0 (black) and 1 (white) -> BlackAndWhite.
        // If no color operators at all -> also BlackAndWhite.
        return ColorType.BlackAndWhite;
    }

    private static void AnalyzeContentStream(byte[] streamBytes,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        var lexer = new PdfLexer(streamBytes);
        var operands = new List<PdfObject>();
        int tokenCount = 0;
        const int maxTokens = 10_000_000;

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;
            if (++tokenCount > maxTokens) break; // safety guard against malformed streams

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(new PdfInteger(token.IntValue));
                    break;
                case TokenKind.Real:
                    operands.Add(new PdfReal(token.RealValue));
                    break;
                case TokenKind.Name:
                    operands.Add(new PdfName(token.StringValue!));
                    break;
                case TokenKind.ArrayStart:
                    SkipArray(lexer);
                    operands.Clear();
                    break;
                case TokenKind.DictStart:
                    SkipDict(lexer);
                    operands.Clear();
                    break;
                case TokenKind.LiteralString:
                case TokenKind.HexString:
                    operands.Add(PdfNull.Instance);
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;
                    if (op == "BI")
                    {
                        // Inline image: check its color space
                        AnalyzeInlineImage(lexer, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
                        operands.Clear();
                        break;
                    }

                    ClassifyOperator(op, operands, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    private static void ClassifyOperator(string op, List<PdfObject> operands,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        switch (op)
        {
            // RGB fill/stroke
            case "rg" when operands.Count >= 3:
            {
                hasAnyColor = true;
                var r = Num(operands[0]);
                var g = Num(operands[1]);
                var b = Num(operands[2]);
                if (IsNonTrivialRgb(r, g, b))
                    hasRgb = true;
                else if (IsGrayRgb(r, g, b))
                    hasGray = true;
                break;
            }
            case "RG" when operands.Count >= 3:
            {
                hasAnyColor = true;
                var r = Num(operands[0]);
                var g = Num(operands[1]);
                var b = Num(operands[2]);
                if (IsNonTrivialRgb(r, g, b))
                    hasRgb = true;
                else if (IsGrayRgb(r, g, b))
                    hasGray = true;
                break;
            }

            // Gray fill/stroke
            case "g" when operands.Count >= 1:
            {
                hasAnyColor = true;
                var v = Num(operands[0]);
                if (!IsBlackOrWhite(v))
                    hasGray = true;
                break;
            }
            case "G" when operands.Count >= 1:
            {
                hasAnyColor = true;
                var v = Num(operands[0]);
                if (!IsBlackOrWhite(v))
                    hasGray = true;
                break;
            }

            // CMYK fill/stroke
            case "k" when operands.Count >= 4:
            {
                hasAnyColor = true;
                var c = Num(operands[0]);
                var m = Num(operands[1]);
                var y = Num(operands[2]);
                var kv = Num(operands[3]);
                if (IsNonTrivialCmyk(c, m, y, kv))
                    hasCmyk = true;
                else if (IsCmykGray(c, m, y))
                    hasGray = true;
                break;
            }
            case "K" when operands.Count >= 4:
            {
                hasAnyColor = true;
                var c = Num(operands[0]);
                var m = Num(operands[1]);
                var y = Num(operands[2]);
                var kv = Num(operands[3]);
                if (IsNonTrivialCmyk(c, m, y, kv))
                    hasCmyk = true;
                else if (IsCmykGray(c, m, y))
                    hasGray = true;
                break;
            }

            // Color space-based operators
            case "sc" or "scn":
            case "SC" or "SCN":
            {
                // These use the current color space; with 3+ numeric operands = RGB-like,
                // 4+ = CMYK-like, 1 = gray-like
                hasAnyColor = true;
                if (operands.Count >= 4 && operands.All(o => o is PdfInteger or PdfReal))
                    hasCmyk = true;
                else if (operands.Count >= 3 && operands.Take(3).All(o => o is PdfInteger or PdfReal))
                {
                    var r = Num(operands[0]);
                    var g = Num(operands[1]);
                    var b = Num(operands[2]);
                    if (IsNonTrivialRgb(r, g, b))
                        hasRgb = true;
                    else if (IsGrayRgb(r, g, b))
                        hasGray = true;
                }
                else if (operands.Count >= 1 && operands[0] is PdfInteger or PdfReal)
                {
                    var v = Num(operands[0]);
                    if (!IsBlackOrWhite(v))
                        hasGray = true;
                }
                break;
            }

            // Color space selection — only flag RGB/CMYK; DeviceGray is the default
            // and its actual gray level is determined by g/G operators, not the cs/CS selection.
            case "cs" or "CS" when operands.Count >= 1 && operands[0] is PdfName csName:
            {
                hasAnyColor = true;
                if (csName.Value is not "DeviceGray" and not "CalGray")
                    ClassifyColorSpaceName(csName.Value, ref hasRgb, ref hasGray, ref hasCmyk);
                break;
            }
        }
    }

    private static void ClassifyColorSpaceName(string name,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk)
    {
        switch (name)
        {
            case "DeviceRGB" or "CalRGB":
                hasRgb = true;
                break;
            case "DeviceCMYK":
                hasCmyk = true;
                break;
            case "DeviceGray" or "CalGray":
                hasGray = true;
                break;
        }
    }

    [ThreadStatic] private static HashSet<PdfDictionary>? _visitedResources;

    private static void AnalyzeImageColorSpaces(PdfDictionary resources, PdfReader reader,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        // Cycle detection: prevent infinite recursion on self-referencing Form XObjects
        _visitedResources ??= new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (!_visitedResources.Add(resources)) return;
        try
        {

        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return;

        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is null) continue;

            var subtype = obj.Dict.GetName("Subtype");
            if (subtype == "Image")
            {
                hasAnyColor = true;
                ClassifyImageColorSpace(obj, reader, ref hasRgb, ref hasGray, ref hasCmyk);
            }
            else if (subtype == "Form")
            {
                // Recurse into Form XObjects
                var formResources = reader.ResolveDict(obj.Dict.Get("Resources"));
                if (formResources is not null)
                {
                    AnalyzeImageColorSpaces(formResources, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
                }
                // Also analyze the form's content stream
                var formData = reader.DecodeStream(obj);
                AnalyzeContentStream(formData, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
            }
        }

        }
        finally
        {
            _visitedResources.Remove(resources);
        }
    }

    private static void ClassifyImageColorSpace(PdfStream imageStream, PdfReader reader,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk)
    {
        var bpc = (int)imageStream.Dict.GetInt("BitsPerComponent", 8);
        var csObj = reader.Resolve(imageStream.Dict.Get("ColorSpace"));

        // 1-bit images are black-and-white, not grayscale
        bool isOneBit = bpc == 1;

        if (csObj is PdfName csName)
        {
            if (csName.Value is "DeviceGray" or "CalGray")
            {
                if (!isOneBit && !IsImageEffectivelyBW(imageStream, reader))
                    hasGray = true;
                // else: 1-bit or all-0/255 pixels → black and white, don't set hasGray
            }
            else
            {
                ClassifyColorSpaceName(csName.Value, ref hasRgb, ref hasGray, ref hasCmyk);
            }
        }
        else if (csObj is PdfArray csArr && csArr.Count > 0)
        {
            var baseName = csArr[0] is PdfName n ? n.Value : null;
            if (baseName == "ICCBased" && csArr.Count >= 2)
            {
                var iccStream = reader.ResolveStream(csArr[1]);
                if (iccStream is not null)
                {
                    var nComponents = (int)iccStream.Dict.GetInt("N", 3);
                    switch (nComponents)
                    {
                        case 1:
                            if (!isOneBit && !IsImageEffectivelyBW(imageStream, reader))
                                hasGray = true;
                            break;
                        case 3: hasRgb = true; break;
                        case 4: hasCmyk = true; break;
                    }
                }
            }
            else if (baseName == "Indexed" && csArr.Count >= 2)
            {
                // Indexed color space: classify the base color space
                // 1-bit indexed DeviceGray is black-and-white, not grayscale
                var baseCs = csArr[1];
                var resolved = reader.Resolve(baseCs);
                string? baseColorSpaceName = null;
                if (resolved is PdfName bn)
                    baseColorSpaceName = bn.Value;
                else if (resolved is PdfArray ba && ba.Count > 0 && ba[0] is PdfName ban)
                    baseColorSpaceName = ban.Value;
                if (baseColorSpaceName is not null)
                {
                    if (isOneBit && baseColorSpaceName is "DeviceGray" or "CalGray")
                    {
                        // 1-bit indexed gray = black and white
                    }
                    else
                    {
                        ClassifyColorSpaceName(baseColorSpaceName, ref hasRgb, ref hasGray, ref hasCmyk);
                    }
                }
            }
            else if (baseName is not null)
            {
                ClassifyColorSpaceName(baseName, ref hasRgb, ref hasGray, ref hasCmyk);
            }
        }
    }

    private static void AnalyzeColorSpaceResources(PdfDictionary resources, PdfReader reader,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk)
    {
        var csDict = reader.ResolveDict(resources.Get("ColorSpace"));
        if (csDict is null) return;

        foreach (var key in csDict.Keys)
        {
            var csObj = reader.Resolve(csDict.Get(key));
            if (csObj is PdfArray csArr && csArr.Count > 0)
            {
                var baseName = csArr[0] is PdfName n ? n.Value : null;
                if (baseName == "ICCBased" && csArr.Count >= 2)
                {
                    var iccStream = reader.ResolveStream(csArr[1]);
                    if (iccStream is not null)
                    {
                        var nComponents = (int)iccStream.Dict.GetInt("N", 3);
                        switch (nComponents)
                        {
                            // N=1 ICCBased in resources is ambiguous — it could be used by
                            // 1-bit (B&W) images. Only flag gray from actual color operations
                            // and image analysis (which checks BitsPerComponent).
                            case 1: break; // don't set hasGray from resource declarations alone
                            case 3: hasRgb = true; break;
                            case 4: hasCmyk = true; break;
                        }
                    }
                }
                else if (baseName is not null)
                {
                    // Skip DeviceGray - it's detected via g/G operators and images
                    if (baseName is not "DeviceGray" and not "CalGray")
                        ClassifyColorSpaceName(baseName, ref hasRgb, ref hasGray, ref hasCmyk);
                }
            }
            else if (csObj is PdfName csName)
            {
                if (csName.Value is not "DeviceGray" and not "CalGray")
                    ClassifyColorSpaceName(csName.Value, ref hasRgb, ref hasGray, ref hasCmyk);
            }
        }
    }

    private static void AnalyzeInlineImage(PdfLexer lexer,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        // Parse inline image dict entries until ID keyword
        string? colorSpaceName = null;

        while (true)
        {
            var keyToken = lexer.NextToken();
            if (keyToken.Kind == TokenKind.Eof) return;
            if (keyToken.Kind == TokenKind.Keyword && keyToken.StringValue == "ID") break;
            if (keyToken.Kind != TokenKind.Name) continue;

            var expandedKey = keyToken.StringValue switch
            {
                "CS" => "ColorSpace",
                _ => keyToken.StringValue!,
            };

            var valToken = lexer.NextToken();
            if (valToken.Kind == TokenKind.Keyword && valToken.StringValue == "ID") break;

            if (expandedKey == "ColorSpace" && valToken.Kind == TokenKind.Name)
            {
                colorSpaceName = valToken.StringValue switch
                {
                    "G" => "DeviceGray",
                    "RGB" => "DeviceRGB",
                    "CMYK" => "DeviceCMYK",
                    _ => valToken.StringValue,
                };
            }
        }

        // Skip inline image data (scan for EI)
        lexer.ReadInlineImageData();

        if (colorSpaceName is not null)
        {
            hasAnyColor = true;
            ClassifyColorSpaceName(colorSpaceName, ref hasRgb, ref hasGray, ref hasCmyk);
        }
    }

    /// <summary>
    /// Check if RGB values represent a non-grayscale, non-black/white color.
    /// </summary>
    private static bool IsNonTrivialRgb(double r, double g, double b)
    {
        // If R != G or R != B, it's a true RGB color
        const double eps = 0.001;
        return Math.Abs(r - g) > eps || Math.Abs(r - b) > eps;
    }

    /// <summary>
    /// Check if RGB values are grayscale (R == G == B) but not black or white.
    /// </summary>
    private static bool IsGrayRgb(double r, double g, double b)
    {
        const double eps = 0.001;
        if (Math.Abs(r - g) > eps || Math.Abs(r - b) > eps) return false;
        return !IsBlackOrWhite(r);
    }

    /// <summary>
    /// Check if a value is essentially 0 (black) or 1 (white).
    /// </summary>
    private static bool IsBlackOrWhite(double v)
    {
        const double eps = 0.001;
        return v < eps || v > (1 - eps);
    }

    /// <summary>
    /// Check if CMYK values represent a non-grayscale color.
    /// Gray in CMYK has C=M=Y=0 with only K varying.
    /// </summary>
    private static bool IsNonTrivialCmyk(double c, double m, double y, double k)
    {
        const double eps = 0.001;
        return c > eps || m > eps || y > eps;
    }

    /// <summary>
    /// Check if CMYK is grayscale (C=M=Y=0).
    /// </summary>
    private static bool IsCmykGray(double c, double m, double y)
    {
        const double eps = 0.001;
        return c < eps && m < eps && y < eps;
    }

    /// <summary>
    /// Check if a grayscale image only contains black (0) and white (255) pixels.
    /// Scans the entire decoded image data.
    /// </summary>
    private static bool IsImageEffectivelyBW(PdfStream imageStream, PdfReader reader)
    {
        try
        {
            var data = reader.DecodeStream(imageStream);
            foreach (var b in data)
            {
                if (b != 0 && b != 255)
                    return false;
            }
            return true;
        }
        catch
        {
            return false; // Can't decode → assume not B&W
        }
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static List<byte[]> GetContentStreams(Page page, PdfReader reader)
    {
        var result = new List<byte[]>();
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            result.Add(reader.DecodeStream(stream));
        }
        else if (contentsObj is PdfArray arr)
        {
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                    result.Add(reader.DecodeStream(s));
            }
        }

        return result;
    }

    private static void AnalyzeAnnotations(Page page, PdfReader reader,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        var annotsObj = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        foreach (var annotRef in annotsObj)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            var subtype = annotDict.GetName("Subtype");

            // Analyze the /C (color) entry on the annotation itself.
            AnalyzeAnnotationColorEntry(annotDict, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);

            // Also check the /IC (interior color) entry
            AnalyzeAnnotationColorEntry(annotDict, reader, "IC", ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);

            // For Widget annotations (form fields, buttons), analyze appearance streams
            // since widgets are not referenced from the page content stream as XObjects.
            // For other annotation types (FreeText, Link, etc.), we skip appearance stream
            // analysis because their visual appearance doesn't affect page color classification.
            if (subtype == "Widget")
            {
                var apDict = reader.ResolveDict(annotDict.Get("AP"));
                if (apDict is null) continue;

                foreach (var apKey in new[] { "N", "R", "D" })
                {
                    var apObj = apDict.Get(apKey);
                    if (apObj is null) continue;

                    var resolved = reader.Resolve(apObj);
                    if (resolved is PdfStream apStream)
                    {
                        AnalyzeAppearanceStream(apStream, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
                    }
                    else if (resolved is PdfDictionary stateDict)
                    {
                        foreach (var stateKey in stateDict.Keys)
                        {
                            var stateStream = reader.ResolveStream(stateDict.Get(stateKey));
                            if (stateStream is not null)
                                AnalyzeAppearanceStream(stateStream, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
                        }
                    }
                }
            }
        }
    }

    private static void AnalyzeAppearanceStream(PdfStream apStream, PdfReader reader,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        var streamData = reader.DecodeStream(apStream);
        AnalyzeContentStream(streamData, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);

        // Also check appearance stream resources
        var apResources = reader.ResolveDict(apStream.Dict.Get("Resources"));
        if (apResources is not null)
        {
            AnalyzeImageColorSpaces(apResources, reader, ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
            AnalyzeColorSpaceResources(apResources, reader, ref hasRgb, ref hasGray, ref hasCmyk);
        }
    }

    /// <summary>
    /// Analyze the /C (color) array on an annotation dictionary.
    /// /C with 1 component = DeviceGray, 3 = DeviceRGB, 4 = DeviceCMYK.
    /// </summary>
    private static void AnalyzeAnnotationColorEntry(PdfDictionary annotDict, PdfReader reader,
        ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        AnalyzeAnnotationColorEntry(annotDict, reader, "C", ref hasRgb, ref hasGray, ref hasCmyk, ref hasAnyColor);
    }

    private static void AnalyzeAnnotationColorEntry(PdfDictionary annotDict, PdfReader reader,
        string key, ref bool hasRgb, ref bool hasGray, ref bool hasCmyk, ref bool hasAnyColor)
    {
        var colorObj = reader.Resolve(annotDict.Get(key)) as PdfArray;
        if (colorObj is null || colorObj.Count == 0) return;

        hasAnyColor = true;
        if (colorObj.Count == 1)
        {
            var v = Num(colorObj[0]);
            if (!IsBlackOrWhite(v))
                hasGray = true;
        }
        else if (colorObj.Count == 3)
        {
            var r = Num(colorObj[0]);
            var g = Num(colorObj[1]);
            var b = Num(colorObj[2]);
            if (IsNonTrivialRgb(r, g, b))
                hasRgb = true;
            else if (IsGrayRgb(r, g, b))
                hasGray = true;
        }
        else if (colorObj.Count >= 4)
        {
            var c = Num(colorObj[0]);
            var m = Num(colorObj[1]);
            var y = Num(colorObj[2]);
            var kv = Num(colorObj[3]);
            if (IsNonTrivialCmyk(c, m, y, kv))
                hasCmyk = true;
            else if (IsCmykGray(c, m, y))
                hasGray = true;
        }
    }


    private static void SkipArray(PdfLexer lexer)
    {
        int depth = 1;
        while (depth > 0)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.ArrayStart) depth++;
            if (t.Kind == TokenKind.ArrayEnd) depth--;
        }
    }

    private static void SkipDict(PdfLexer lexer)
    {
        int depth = 1;
        while (depth > 0)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) return;
            if (t.Kind == TokenKind.DictStart) depth++;
            if (t.Kind == TokenKind.DictEnd) depth--;
        }
    }
}
