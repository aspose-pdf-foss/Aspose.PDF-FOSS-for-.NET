using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;

namespace Aspose.Pdf;

/// <summary>
/// Converts all RGB color operators and image color spaces on a page to DeviceGray.
/// Uses the standard luminance formula: gray = 0.299*R + 0.587*G + 0.114*B.
/// </summary>
public sealed partial class RgbToDeviceGrayConversionStrategy
{
    /// <summary>Creates a conversion strategy; call <c>Convert</c> for each page to convert.</summary>
    public RgbToDeviceGrayConversionStrategy() { }

#pragma warning disable S2325 // public API: the published instance member
    /// <summary>
    /// Convert all RGB colors to DeviceGray on the given page.
    /// This rewrites the content stream and converts RGB image XObjects to grayscale.
    /// </summary>
    public void Convert(Page page)
#pragma warning restore S2325
    {
        var reader = page.Reader;

        // Convert content streams
        ConvertContentStreams(page, reader);

        // Convert image XObjects and form XObjects
        ConvertImageXObjects(page, reader);

        // Convert color space resources
        ConvertColorSpaceResources(page, reader);

        // Convert annotation appearance streams
        ConvertAnnotationAppearances(page, reader);

        // Note: we do not register DefaultGray in page resources.
        // Color type detection relies on actual content stream operators.
    }

    private static void ConvertAnnotationAppearances(Page page, PdfReader reader)
    {
        var annotsObj = reader.Resolve(page.Dict.Get("Annots")) as PdfArray;
        if (annotsObj is null) return;

        foreach (var annotRef in annotsObj)
        {
            var annotDict = reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            // Convert annotation color arrays (/C, /IC) — must happen for ALL annotations,
            // including those without appearance streams (e.g., Link annotations).
            ConvertColorArray(annotDict, "C");
            ConvertColorArray(annotDict, "IC");

            var apDict = reader.ResolveDict(annotDict.Get("AP"));
            if (apDict is null) continue;

            // Process Normal (N), Rollover (R), and Down (D) appearances
            foreach (var apKey in new[] { "N", "R", "D" })
            {
                var apObj = apDict.Get(apKey);
                if (apObj is null) continue;

                var resolved = reader.Resolve(apObj);
                if (resolved is PdfStream apStream && apStream.Dict.GetName("Subtype") is null or "Form")
                {
                    ConvertFormXObject(apStream, reader);
                }
                else if (resolved is PdfDictionary stateDict)
                {
                    // State dict: each key maps to an appearance stream
                    foreach (var stateKey in stateDict.Keys)
                    {
                        var stateStream = reader.ResolveStream(stateDict.Get(stateKey));
                        if (stateStream is not null)
                            ConvertFormXObject(stateStream, reader);
                    }
                }
            }
        }
    }

    private static void ConvertColorArray(PdfDictionary dict, string key)
    {
        var obj = dict.Get(key);
        if (obj is not PdfArray arr || arr.Count < 3) return;

        // Convert RGB array to single gray value
        if (arr.Count >= 3)
        {
            var r = Num(arr[0]);
            var g = Num(arr[1]);
            var b = Num(arr[2]);
            var gray = RgbToGray(r, g, b);

            var newArr = new PdfArray();
            newArr.Add(new PdfReal(gray));
            dict.Set(key, newArr);
        }
    }

    private static double Num(PdfObject obj) => obj switch
    {
        PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0,
    };

    private static void ConvertContentStreams(Page page, PdfReader reader)
    {
        var contentsObj = reader.Resolve(page.Dict.Get("Contents"));

        if (contentsObj is PdfStream stream)
        {
            var data = reader.DecodeStream(stream);
            var converted = ConvertContentStreamBytes(data);
            page.SetContentStream(converted);
        }
        else if (contentsObj is PdfArray arr)
        {
            // Merge all content streams, convert, and replace with single stream
            var merged = new MemoryStream();
            foreach (var item in arr)
            {
                var s = reader.ResolveStream(item);
                if (s is not null)
                {
                    var data = reader.DecodeStream(s);
                    merged.Write(data);
                    merged.WriteByte((byte)'\n');
                }
            }
            var converted = ConvertContentStreamBytes(merged.ToArray());
            page.SetContentStream(converted);
        }
    }

    /// <summary>
    /// Parse and rewrite a content stream, converting RGB operators to gray.
    /// </summary>
    private static byte[] ConvertContentStreamBytes(byte[] input)
    {
        var lexer = new PdfLexer(input);
        var output = new StringBuilder();
        var operands = new List<string>();

        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == TokenKind.Eof) break;

            switch (token.Kind)
            {
                case TokenKind.Integer:
                    operands.Add(token.IntValue.ToString(CultureInfo.InvariantCulture));
                    break;
                case TokenKind.Real:
                    operands.Add(FormatDouble(token.RealValue));
                    break;
                case TokenKind.Name:
                    operands.Add("/" + token.StringValue);
                    break;
                case TokenKind.LiteralString:
                    operands.Add("(" + EscapeString(token.BytesValue!) + ")");
                    break;
                case TokenKind.HexString:
                    operands.Add("<" + BitConverter.ToString(token.BytesValue!).Replace("-", "") + ">");
                    break;
                case TokenKind.Boolean:
                    operands.Add(token.BoolValue ? "true" : "false");
                    break;
                case TokenKind.ArrayStart:
                    operands.Add(ReadArrayAsString(lexer));
                    break;
                case TokenKind.DictStart:
                    operands.Add(ReadDictAsString(lexer));
                    break;
                case TokenKind.Keyword:
                {
                    var op = token.StringValue!;

                    if (op == "BI")
                    {
                        // Inline image: copy through, converting color space
                        WriteInlineImage(lexer, output);
                        operands.Clear();
                        break;
                    }

                    ConvertColorOperator(output, operands, op);

                    operands.Clear();
                    break;
                }
                default:
                    operands.Clear();
                    break;
            }
        }

        // Latin-1 (not ASCII) so chars 0x80-0xFF round-trip byte-perfect — required
        // because EscapeString/WriteInlineImage put PDF literal-string and inline-image
        // bytes into the StringBuilder as (char)b. ASCII would silently rewrite them to '?',
        // dropping every CID-encoded text byte and corrupting inline image data.
        return Compat.Latin1.GetBytes(output.ToString());
    }

    private static void WriteOriginal(StringBuilder output, List<string> operands, string op)
    {
        foreach (var operand in operands)
            output.Append(operand).Append(' ');
        output.Append(op).Append('\n');
    }

    /// <summary>Copy an inline image through; an RGB one whose samples can be decoded is written
    /// with gray samples, unfiltered. The colour space alone is never rewritten: three components
    /// per pixel read as gray draw three noise pixels for each one of the picture.</summary>
    private static void WriteInlineImage(PdfLexer lexer, StringBuilder output)
    {
        // The dictionary as written, and with its keys and names expanded as the filters read it.
        var written = new List<(string key, PdfObject value)>();
        var expanded = new PdfDictionary();
        while (true)
        {
            var keyToken = lexer.NextToken();
            if (keyToken.Kind == TokenKind.Eof) return;
            if (keyToken.Kind == TokenKind.Keyword && keyToken.StringValue == "ID") break;
            if (keyToken.Kind != TokenKind.Name) continue;

            var valToken = lexer.NextToken();
            if (valToken.Kind == TokenKind.Keyword && valToken.StringValue == "ID") break;
            var key = keyToken.StringValue!;
            var value = ReadInlineValue(lexer, valToken);
            written.Add((key, value));
            expanded.Set(Content.ContentStreamParser.ExpandInlineImageKey(key),
                value is PdfName name ? new PdfName(Content.ContentStreamParser.ExpandInlineImageValue(name.Value)) : value);
        }

        var imageData = lexer.ReadInlineImageData();
        var gray = GrayInlineSamples(expanded, imageData);

        output.Append("BI\n");
        foreach (var (key, value) in written)
        {
            var expandedKey = Content.ContentStreamParser.ExpandInlineImageKey(key);
            // The gray samples are written unfiltered: the filter, its parameters and an RGB
            // decode array describe the data they replaced.
            if (gray is not null && expandedKey is "Filter" or "DecodeParms" or "Decode") continue;
            output.Append('/').Append(key).Append(' ');
            if (gray is not null && expandedKey == "ColorSpace")
                output.Append("/G");
            else
                AppendInlineValue(output, value);
            output.Append('\n');
        }
        output.Append("ID ");
        // Write raw bytes as Latin-1
        foreach (var b in gray ?? imageData)
            output.Append((char)b);
        output.Append("\nEI\n");
    }

    /// <summary>The gray samples of an 8-bit RGB inline image, or null when the image is not
    /// one or its samples cannot be decoded (a JPEG or JPEG 2000 codestream stays as it is).</summary>
    private static byte[]? GrayInlineSamples(PdfDictionary image, byte[] data)
    {
        if (image.GetName("ColorSpace") is not ("DeviceRGB" or "CalRGB")) return null;
        if (image.GetInt("BitsPerComponent", 8) != 8 || IsCodestream(image.Get("Filter"))) return null;
        var pixelCount = (int)(image.GetInt("Width") * image.GetInt("Height"));
        if (pixelCount <= 0) return null;
        byte[] decoded;
        try { decoded = StreamFilter.Decode(data, image); }
        catch (Exception) { return null; }
        return decoded.Length >= pixelCount * 3 ? GraySamples(decoded, pixelCount) : null;
    }

    private static bool IsCodestream(PdfObject? filter) => filter switch
    {
        PdfName name => name.Value is "DCTDecode" or "DCT" or "JPXDecode" or "JPX",
        PdfArray filters => filters.Any(IsCodestream),
        _ => false,
    };

    /// <summary>One inline-image value: a number, name, string, boolean, array or dictionary.</summary>
    private static PdfObject ReadInlineValue(PdfLexer lexer, Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Integer: return new PdfInteger(token.IntValue);
            case TokenKind.Real: return new PdfReal(token.RealValue);
            case TokenKind.Name: return new PdfName(token.StringValue!);
            case TokenKind.Boolean: return token.BoolValue ? PdfBoolean.True : PdfBoolean.False;
            case TokenKind.LiteralString: return new PdfString(token.BytesValue!);
            case TokenKind.HexString: return new PdfString(token.BytesValue!, isHex: true);
            case TokenKind.ArrayStart:
            {
                var array = new PdfArray();
                for (var item = lexer.NextToken(); item.Kind is not (TokenKind.ArrayEnd or TokenKind.Eof); item = lexer.NextToken())
                    array.Add(ReadInlineValue(lexer, item));
                return array;
            }
            case TokenKind.DictStart:
            {
                var dict = new PdfDictionary();
                for (var key = lexer.NextToken(); key.Kind is not (TokenKind.DictEnd or TokenKind.Eof); key = lexer.NextToken())
                {
                    if (key.Kind != TokenKind.Name) continue;
                    dict.Set(key.StringValue!, ReadInlineValue(lexer, lexer.NextToken()));
                }
                return dict;
            }
            default: return PdfNull.Instance;
        }
    }

    private static void AppendInlineValue(StringBuilder output, PdfObject value)
    {
        switch (value)
        {
            case PdfInteger i: output.Append(i.Value.ToString(CultureInfo.InvariantCulture)); break;
            case PdfReal r: output.Append(FormatDouble(r.Value)); break;
            case PdfName n: output.Append('/').Append(n.Value); break;
            case PdfBoolean b: output.Append(b.Value ? "true" : "false"); break;
            case PdfString s when s.IsHex: output.Append('<').Append(BitConverter.ToString(s.Value).Replace("-", "")).Append('>'); break;
            case PdfString s: output.Append('(').Append(EscapeString(s.Value)).Append(')'); break;
            case PdfArray array:
                output.Append('[');
                foreach (var item in array) { AppendInlineValue(output, item); output.Append(' '); }
                output.Append(']');
                break;
            case PdfDictionary dict:
                output.Append("<< ");
                foreach (var key in dict.Keys) { output.Append('/').Append(key).Append(' '); AppendInlineValue(output, dict.Get(key)!); output.Append(' '); }
                output.Append(">>");
                break;
            default: output.Append("null"); break;
        }
    }

    private static void ConvertImageXObjects(Page page, PdfReader reader)
    {
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;

        ConvertShadingsAndPatterns(resources, reader);
        var xobjectDict = reader.ResolveDict(resources.Get("XObject"));
        if (xobjectDict is null) return;

        foreach (var key in xobjectDict.Keys)
        {
            var obj = reader.ResolveStream(xobjectDict.Get(key));
            if (obj is null) continue;

            var subtype = obj.Dict.GetName("Subtype");
            if (subtype == "Image")
            {
                ConvertImageToGray(obj, reader);
            }
            else if (subtype == "Form")
            {
                ConvertFormXObject(obj, reader);
            }
        }
    }

    private static void ConvertFormXObject(PdfStream formStream, PdfReader reader)
    {
        // Convert form XObject's content stream
        var formData = reader.DecodeStream(formStream);
        var converted = ConvertContentStreamBytes(formData);
        formStream.ReplaceData(converted);
        formStream.Dict.Remove("Filter");
        formStream.Dict.Remove("DecodeParms");
        formStream.Dict.Set("Length", new PdfInteger(converted.Length));

        // Convert nested resources
        var formResources = reader.ResolveDict(formStream.Dict.Get("Resources"));
        if (formResources is not null)
        {
            // Convert nested XObjects (images and forms)
            var nestedXObjects = reader.ResolveDict(formResources.Get("XObject"));
            if (nestedXObjects is not null)
            {
                foreach (var fkey in nestedXObjects.Keys)
                {
                    var fobj = reader.ResolveStream(nestedXObjects.Get(fkey));
                    if (fobj is null) continue;
                    var fsubtype = fobj.Dict.GetName("Subtype");
                    if (fsubtype == "Image")
                        ConvertImageToGray(fobj, reader);
                    else if (fsubtype == "Form")
                        ConvertFormXObject(fobj, reader);
                }
            }

            // Convert color space resources in the form
            ConvertColorSpaceDict(formResources, reader);
            ConvertShadingsAndPatterns(formResources, reader);
        }
    }

    private static void ConvertColorSpaceResources(Page page, PdfReader reader)
    {
        var resources = reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null) return;
        ConvertColorSpaceDict(resources, reader);
    }

    private static void ConvertColorSpaceDict(PdfDictionary resources, PdfReader reader)
    {
        var csDict = reader.ResolveDict(resources.Get("ColorSpace"));
        if (csDict is null) return;

        foreach (var key in csDict.Keys.ToArray())
        {
            var csObj = reader.Resolve(csDict.Get(key));
            if (csObj is PdfArray csArr && csArr.Count > 0 && csArr[0] is PdfName csName)
            {
                if (csName.Value == "ICCBased" && csArr.Count >= 2)
                {
                    var iccStream = reader.ResolveStream(csArr[1]);
                    if (iccStream is not null && iccStream.Dict.GetInt("N", 3) == 3)
                    {
                        // Replace RGB ICC profile reference with DeviceGray
                        csDict.Set(key, new PdfName("DeviceGray"));
                    }
                }
                else if (csName.Value is "DeviceRGB" or "CalRGB")
                {
                    csDict.Set(key, new PdfName("DeviceGray"));
                }
                else if (GrayIndexedSpace(csArr, reader) is { } grayIndexed)
                {
                    // The fill indices stay; their palette turns grey.
                    csDict.Set(key, grayIndexed);
                }
            }
            else if (csObj is PdfName name && name.Value is "DeviceRGB" or "CalRGB")
            {
                csDict.Set(key, new PdfName("DeviceGray"));
            }
        }
    }

    /// <summary>
    /// Convert RGB to gray using standard luminance formula.
    /// </summary>
    private static double RgbToGray(double r, double g, double b) =>
        0.299 * r + 0.587 * g + 0.114 * b;

    /// <summary>One gray byte per pixel from 8-bit RGB triples.</summary>
    private static byte[] GraySamples(byte[] rgb, int pixelCount)
    {
        var gray = new byte[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            var srcIdx = i * 3;
            var r = rgb[srcIdx] / 255.0;
            var g = rgb[srcIdx + 1] / 255.0;
            var b = rgb[srcIdx + 2] / 255.0;
            gray[i] = (byte)(RgbToGray(r, g, b) * 255.0 + 0.5);
        }
        return gray;
    }

    private static string FormatDouble(double v) =>
        v.ToString("0.######", CultureInfo.InvariantCulture);

    private static double ParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double? TryParseDouble(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string EscapeString(byte[] bytes)
    {
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                default: sb.Append((char)b); break;
            }
        }
        return sb.ToString();
    }

    private static string ReadArrayAsString(PdfLexer lexer)
    {
        var sb = new StringBuilder("[");
        int depth = 1;
        while (depth > 0)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) break;
            if (t.Kind == TokenKind.ArrayStart) { depth++; sb.Append('['); continue; }
            if (t.Kind == TokenKind.ArrayEnd) { depth--; if (depth > 0) { sb.Append(']'); } continue; }
            if (sb.Length > 1) sb.Append(' ');
            AppendTokenToStringBuilder(sb, t);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string ReadDictAsString(PdfLexer lexer)
    {
        var sb = new StringBuilder("<< ");
        int depth = 1;
        while (depth > 0)
        {
            var t = lexer.NextToken();
            if (t.Kind == TokenKind.Eof) break;
            if (t.Kind == TokenKind.DictStart) { depth++; sb.Append("<< "); continue; }
            if (t.Kind == TokenKind.DictEnd) { depth--; if (depth > 0) { sb.Append(">> "); } continue; }
            AppendTokenToStringBuilder(sb, t);
            sb.Append(' ');
        }
        sb.Append(">>");
        return sb.ToString();
    }

    private static void AppendTokenToStringBuilder(StringBuilder sb, Token token)
    {
        switch (token.Kind)
        {
            case TokenKind.Integer:
                sb.Append(token.IntValue.ToString(CultureInfo.InvariantCulture));
                break;
            case TokenKind.Real:
                sb.Append(FormatDouble(token.RealValue));
                break;
            case TokenKind.Name:
                sb.Append('/').Append(token.StringValue);
                break;
            case TokenKind.LiteralString:
                sb.Append('(').Append(EscapeString(token.BytesValue!)).Append(')');
                break;
            case TokenKind.HexString:
                sb.Append('<').Append(BitConverter.ToString(token.BytesValue!).Replace("-", "")).Append('>');
                break;
            case TokenKind.Boolean:
                sb.Append(token.BoolValue ? "true" : "false");
                break;
        }
    }

    /// <summary>One colour operator rewritten to its DeviceGray form: rg/RG to g/G, the RGB colour spaces to DeviceGray, sc/scn components to a gray.</summary>
    private static void ConvertColorOperator(StringBuilder output, List<string> operands, string op)
    {
        switch (op)
        {
            // RGB fill: rg r g b -> g gray
            case "rg" when operands.Count >= 3:
            {
                var r = ParseDouble(operands[operands.Count - 3]);
                var g = ParseDouble(operands[operands.Count - 2]);
                var b = ParseDouble(operands[operands.Count - 1]);
                var gray = RgbToGray(r, g, b);
                // Write any prefix operands before the last 3
                for (int i = 0; i < operands.Count - 3; i++)
                    output.Append(operands[i]).Append(' ');
                output.Append(FormatDouble(gray)).Append(" g\n");
                break;
            }

            // RGB stroke: RG R G B -> G gray
            case "RG" when operands.Count >= 3:
            {
                var r = ParseDouble(operands[operands.Count - 3]);
                var g = ParseDouble(operands[operands.Count - 2]);
                var b = ParseDouble(operands[operands.Count - 1]);
                var gray = RgbToGray(r, g, b);
                for (int i = 0; i < operands.Count - 3; i++)
                    output.Append(operands[i]).Append(' ');
                output.Append(FormatDouble(gray)).Append(" G\n");
                break;
            }

            // Color space selection: cs DeviceRGB -> cs DeviceGray
            case "cs" when operands.Count >= 1 && operands[operands.Count - 1] == "/DeviceRGB":
            {
                for (int i = 0; i < operands.Count - 1; i++)
                    output.Append(operands[i]).Append(' ');
                output.Append("/DeviceGray cs\n");
                break;
            }
            case "CS" when operands.Count >= 1 && operands[operands.Count - 1] == "/DeviceRGB":
            {
                for (int i = 0; i < operands.Count - 1; i++)
                    output.Append(operands[i]).Append(' ');
                output.Append("/DeviceGray CS\n");
                break;
            }

            // sc/scn with 3 operands (RGB context) -> convert to 1 operand
            case "sc" or "scn" when operands.Count >= 3:
            {
                // Check if last 3 operands are numeric
                if (TryParseDouble(operands[operands.Count - 3]) is { } r &&
                    TryParseDouble(operands[operands.Count - 2]) is { } gv &&
                    TryParseDouble(operands[operands.Count - 1]) is { } bv)
                {
                    var gray = RgbToGray(r, gv, bv);
                    for (int i = 0; i < operands.Count - 3; i++)
                        output.Append(operands[i]).Append(' ');
                    output.Append(FormatDouble(gray)).Append(' ').Append(op).Append('\n');
                }
                else
                {
                    WriteOriginal(output, operands, op);
                }
                break;
            }

            case "SC" or "SCN" when operands.Count >= 3:
            {
                if (TryParseDouble(operands[operands.Count - 3]) is { } r &&
                    TryParseDouble(operands[operands.Count - 2]) is { } gv &&
                    TryParseDouble(operands[operands.Count - 1]) is { } bv)
                {
                    var gray = RgbToGray(r, gv, bv);
                    for (int i = 0; i < operands.Count - 3; i++)
                        output.Append(operands[i]).Append(' ');
                    output.Append(FormatDouble(gray)).Append(' ').Append(op).Append('\n');
                }
                else
                {
                    WriteOriginal(output, operands, op);
                }
                break;
            }

            default:
                WriteOriginal(output, operands, op);
                break;
        }
    }
}
