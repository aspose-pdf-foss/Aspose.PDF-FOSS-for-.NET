using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Content;

internal sealed partial class ContentStreamParser
{
    /// <summary>
    /// Parse an inline image (BI . ID <data> EI).
    /// Abbreviated keys: W=Width, H=Height, BPC=BitsPerComponent, CS=ColorSpace, F=Filter.
    /// </summary>
    private void ParseInlineImage(PdfLexer lexer)
    {
        var dict = new PdfDictionary();

        // Parse key-value pairs until "ID" keyword
        while (true)
        {
            var keyToken = lexer.NextToken();
            if (keyToken.Kind == TokenKind.Eof) return;
            if (keyToken.Kind == TokenKind.Keyword && keyToken.StringValue == "ID") break;
            if (keyToken.Kind != TokenKind.Name) continue;

            var valToken = lexer.NextToken();
            if (valToken.Kind == TokenKind.Keyword && valToken.StringValue == "ID") break;

            // Expand abbreviated key names
            var key = ExpandInlineImageKey(keyToken.StringValue!);

            PdfObject value = valToken.Kind switch
            {
                TokenKind.Integer => new PdfInteger(valToken.IntValue),
                TokenKind.Real => new PdfReal(valToken.RealValue),
                TokenKind.Name => new PdfName(ExpandInlineImageValue(valToken.StringValue!)),
                TokenKind.LiteralString => new PdfString(valToken.BytesValue!),
                TokenKind.HexString => new PdfString(valToken.BytesValue!, isHex: true),
                TokenKind.Boolean => valToken.BoolValue ? PdfBoolean.True : PdfBoolean.False,
                TokenKind.ArrayStart => ParseArray(lexer),
                // /DP (DecodeParms) is a dictionary — e.g. a CCITT glyph image carries
                // <</K -1 /Columns 44>>; without parsing it the filter decodes with the
                // wrong parameters and the image is garbage.
                TokenKind.DictStart => ParseDict(lexer),
                _ => PdfNull.Instance,
            };

            dict.Set(key, value);
        }

        // After ID keyword, skip exactly one whitespace byte, then read raw data until "EI".
        // Prefer an exact payload length over scanning for an EI marker (which can
        // collide with the binary image bytes): unfiltered images compute it from the
        // geometry, RunLengthDecode images from their self-terminating EOD marker.
        var imageData = lexer.ReadInlineImageData(ComputeInlineImageLength(dict), IsRunLengthOnly(dict));
        OnInlineImage?.Invoke(dict, imageData);
    }

    /// <summary>True when the inline image's only filter is RunLengthDecode, whose
    /// stream is self-terminating so its exact length is recoverable.</summary>
    private static bool IsRunLengthOnly(PdfDictionary dict)
    {
        // /Filter values may be abbreviated (/RL) or written in full; array form
        // carries unexpanded names, the direct-name form is already expanded.
        static bool IsRl(string? n) => n is "RunLengthDecode" or "RL";
        return dict.Get("Filter") switch
        {
            PdfName n => IsRl(n.Value),
            PdfArray { Count: 1 } a => IsRl((a[0] as PdfName)?.Value),
            _ => false,
        };
    }

    /// <summary>
    /// Exact byte length of an unfiltered inline image payload, or -1 when it
    /// cannot be determined (filtered data, or a colour space whose component
    /// count is not statically known) — in which case the lexer scans for EI.
    /// </summary>
    private static int ComputeInlineImageLength(PdfDictionary dict)
    {
        // Any filter means the in-stream bytes are encoded; their length is not
        // derivable from the image geometry, so defer to the EI scan.
        if (dict.Get("Filter") != null) return -1;

        var w = dict.GetInt("Width");
        var h = dict.GetInt("Height");
        if (w <= 0 || h <= 0) return -1;

        int components;
        long bpc;
        if (dict.GetBool("ImageMask"))
        {
            components = 1;
            bpc = 1;
        }
        else
        {
            bpc = dict.GetInt("BitsPerComponent");
            if (bpc <= 0) return -1;
            components = InlineImageComponents(dict.Get("ColorSpace"));
            if (components <= 0) return -1;
        }

        long rowBytes = ((long)w * components * bpc + 7) / 8;
        long total = rowBytes * h;
        if (total <= 0 || total > int.MaxValue) return -1;
        return (int)total;
    }

    /// <summary>Component count for inline-image colour spaces we can size statically.</summary>
    private static int InlineImageComponents(PdfObject? cs)
    {
        // Abbreviated device names are already expanded by ExpandInlineImageValue.
        if (cs is PdfName name)
        {
            return name.Value switch
            {
                "DeviceGray" or "CalGray" => 1,
                "DeviceRGB" or "CalRGB" or "Lab" => 3,
                "DeviceCMYK" => 4,
                _ => -1,
            };
        }

        // Indexed colour spaces are 1 component per sample: [/Indexed base hival lookup].
        if (cs is PdfArray { Count: > 0 } arr && arr[0] is PdfName { Value: "Indexed" or "I" })
            return 1;

        // Named resource colour space or anything else: defer to the EI scan.
        return -1;
    }

    private static string ExpandInlineImageKey(string key) => key switch
    {
        "W" => "Width",
        "H" => "Height",
        "BPC" => "BitsPerComponent",
        "CS" => "ColorSpace",
        "D" => "Decode",
        "DP" => "DecodeParms",
        "F" => "Filter",
        "IM" => "ImageMask",
        "I" => "Interpolate",
        _ => key,
    };

    private static string ExpandInlineImageValue(string value) => value switch
    {
        "G" => "DeviceGray",
        "RGB" => "DeviceRGB",
        "CMYK" => "DeviceCMYK",
        "I" => "Indexed",
        "AHx" => "ASCIIHexDecode",
        "A85" => "ASCII85Decode",
        "LZW" => "LZWDecode",
        "Fl" => "FlateDecode",
        "RL" => "RunLengthDecode",
        "CCF" => "CCITTFaxDecode",
        "DCT" => "DCTDecode",
        _ => value,
    };
}
