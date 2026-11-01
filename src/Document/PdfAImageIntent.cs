using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The four rendering intents ISO 32000-1 defines. A PDF/A file may name no
    /// other, because a reader has no defined behaviour for one it does not know.</summary>
    private static readonly HashSet<string> StandardRenderingIntents = new(StringComparer.Ordinal)
    {
        "RelativeColorimetric", "AbsoluteColorimetric", "Perceptual", "Saturation",
    };

    /// <summary>
    /// One walk over the images the pages draw (pattern cells included), for what PDF/A states about an image: its
    /// /Intent names one of the four standard rendering intents (clause 6.2.9), and it does not
    /// ask for interpolation (ISO 19005-1 clause 6.2.4, parts 2 and 3: 6.2.8), and an Indexed
    /// colour space it is stored in has a lookup table of the length the space declares.
    /// </summary>
    /// <remarks>Each image is looked at once however many pages use it. Whether any image
    /// can interpolate is read from the file's bytes first, so a file that never writes the
    /// key has no soft mask loaded for the rule. A part-1 target with layers also learns
    /// here, from the resources it is passing, whether a membership dictionary hides any of
    /// what the pages draw.</remarks>
    private void CheckPageImages(PdfAConvertState pa)
    {
        var mayInterpolate = _data.AsSpan().IndexOf("/Interpolate"u8) >= 0;
        var layered = pa.part == "1" && _reader.Catalog.ContainsKey("OCProperties");
        var seen = new HashSet<int>();
        var contents = new HashSet<PdfStream>(ReferenceEqualityComparer.Instance);
        foreach (var page in Pages)
        {
            foreach (var content in PageContentStreams(page)) ForbidInlineInterpolation(content, contents, pa);
            // The cells of the tiling patterns a page paints with draw images too.
            foreach (var resources in PageResourceDictionaries(page, throughPatterns: true))
            {
                if (_reader.ResolveDict(resources.Get("Pattern")) is { } patterns)
                    foreach (var key in patterns.Keys)
                        if (_reader.ResolveStream(patterns.Get(key)) is { } cell) ForbidInlineInterpolation(cell, contents, pa);
                if (layered && _reader.ResolveDict(resources.Get("Properties")) is { } properties)
                    foreach (var key in properties.Keys)
                        pa.membershipHides |= ShownOnlyWhileALayerIsOff(properties.Get(key));
                if (_reader.ResolveDict(resources.Get("XObject")) is not { } xObjects) continue;

                foreach (var key in xObjects.Keys)
                {
                    var xObjectRef = xObjects.Get(key);
                    var xObject = _reader.ResolveStream(xObjectRef)?.Dict;
                    if (layered) pa.membershipHides |= ShownOnlyWhileALayerIsOff(xObject?.Get("OC"));
                    if (xObject?.GetName("Subtype") == "Form" && _reader.ResolveStream(xObjectRef) is { } form)
                        ForbidInlineInterpolation(form, contents, pa);
                    if (xObject?.GetName("Subtype") != "Image") continue;
                    var objectNumber = (xObjectRef as PdfIndirectRef)?.ObjectNumber ?? 0;
                    if (objectNumber != 0 && !seen.Add(objectNumber)) continue;

                    ReportInvalidRenderingIntent(xObject, objectNumber, pa.options);
                    TrimIndexedLookup(xObject);
                    if (pa.part == "1" && pa.fix) ReplacePartOneSoftMask(xObject);
                    if (!mayInterpolate) continue;
                    ForbidInterpolation(xObject, objectNumber, pa);
                    // A soft mask is an image of its own, reached only through the image it masks.
                    var maskRef = xObject.Get("SMask");
                    var maskNumber = (maskRef as PdfIndirectRef)?.ObjectNumber ?? 0;
                    if (_reader.ResolveStream(maskRef)?.Dict is { } mask && (maskNumber == 0 || seen.Add(maskNumber)))
                        ForbidInterpolation(mask, maskNumber, pa);
                }
            }
        }
    }

    /// <summary>The content streams of a page: its one stream, or each of an array.</summary>
    private IEnumerable<PdfStream> PageContentStreams(Page page)
    {
        var contents = _reader.Resolve(page.Dict.Get("Contents"));
        if (contents is PdfStream single) yield return single;
        else if (contents is PdfArray parts)
            foreach (var part in parts)
                if (_reader.ResolveStream(part) is { } stream) yield return stream;
    }

    /// <summary>The inline images of a content stream that ask for interpolation (/I or /Interpolate true) are told
    /// not to: the key stays, false, as for an image object (clause 6.2.4, parts 2 and 3: 6.2.8). Each stream is
    /// read once, and only a stream that holds an inline image is tokenised.</summary>
    private void ForbidInlineInterpolation(PdfStream content, HashSet<PdfStream> visited, PdfAConvertState pa)
    {
        if (!visited.Add(content)) return;
        byte[] data;
        try { data = _reader.DecodeStream(content); } catch { return; /* undecodable: left as the file has it */ }
        if (data.AsSpan().IndexOf("BI"u8) < 0 || InterpolatingInlineImages(data) is not { Count: > 0 } flags) return;
        foreach (var _ in flags)
            pa.options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "ImageInterpolation",
                Clause = pa.part == "1" ? "6.2.4" : "6.2.8",
                Description = "The image interpolation is prohibited",
            });
        var rewritten = new List<byte>(data.Length + flags.Count);
        var from = 0;
        foreach (var at in flags)
        {
            rewritten.AddRange(new ArraySegment<byte>(data, from, at - from));
            rewritten.AddRange("false"u8.ToArray());
            from = at + "true".Length;
        }
        rewritten.AddRange(new ArraySegment<byte>(data, from, data.Length - from));
        content.Dict.Remove("Filter");
        content.Dict.Remove("DecodeParms");
        content.Dict.Set("Length", new PdfInteger(rewritten.Count));
        content.ReplaceData(rewritten.ToArray());
    }

    /// <summary>Where, in a content stream, each inline image header sets /I or /Interpolate to true: the offsets
    /// of those true values. The image data after each ID is stepped over, so its bytes are never read as operators.</summary>
    private static List<int> InterpolatingInlineImages(byte[] content)
    {
        var offsets = new List<int>();
        var lexer = new IO.PdfLexer(content);
        while (true)
        {
            var token = lexer.NextToken();
            if (token.Kind == IO.TokenKind.Eof) return offsets;
            if (token.Kind != IO.TokenKind.Keyword || token.StringValue != "BI") continue;
            var interpolateKey = false;
            while (true)
            {
                var entry = lexer.NextToken();
                if (entry.Kind == IO.TokenKind.Eof) return offsets;
                if (entry.Kind == IO.TokenKind.Keyword && entry.StringValue == "ID") break;
                if (interpolateKey && entry.Kind == IO.TokenKind.Boolean && entry.BoolValue) offsets.Add((int)entry.Position);
                interpolateKey = entry.Kind == IO.TokenKind.Name && entry.StringValue is "I" or "Interpolate";
            }
            lexer.ReadInlineImageData();
        }
    }

    /// <summary>Clause 6.2.9: files written by careless producers carry an empty name in
    /// /Intent, which is why the message can read back as an empty quoted value.</summary>
    private void ReportInvalidRenderingIntent(PdfDictionary image, int objectNumber,
        PdfFormatConversionOptions options)
    {
        if (_reader.Resolve(image.Get("Intent")) is not PdfName intent) return;
        if (StandardRenderingIntents.Contains(intent.Value)) return;
        options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "RenderingIntent",
            Clause = "6.2.9",
            Description = $"Rendering intent '{intent.Value}' is invalid. Should be one of "
                + "RelativeColorimetric, AbsoluteColorimetric, Perceptual or Saturation",
            ObjectId = objectNumber == 0 ? null : objectNumber.ToString(),
        });
    }

    /// <summary>An image that asks for interpolation is told not to: the key stays, false.</summary>
    private void ForbidInterpolation(PdfDictionary image, int objectNumber, PdfAConvertState pa)
    {
        if (_reader.Resolve(image.Get("Interpolate")) is not PdfBoolean { Value: true }) return;
        pa.options.ConversionLog.Add(new PdfAViolation
        {
            Rule = "ImageInterpolation",
            Clause = pa.part == "1" ? "6.2.4" : "6.2.8",
            Description = "The image interpolation is prohibited",
            ObjectId = objectNumber == 0 ? null : objectNumber.ToString(),
        });
        image.Set("Interpolate", PdfBoolean.False);
    }

    /// <summary>The entries of an Indexed colour space array: family, base, hival, lookup.</summary>
    private const int IndexedSpaceEntries = 4;
    private const int IndexedLookupSlot = 3;
    private const int MaxIndexedHival = 255;

    /// <summary>An Indexed colour space whose lookup string runs past the last colour hival
    /// admits is cut to the table the space declares: the bytes beyond it colour nothing, and
    /// a validator counts them as a malformed table.</summary>
    private void TrimIndexedLookup(PdfDictionary image)
    {
        if (_reader.Resolve(image.Get("ColorSpace")) is not PdfArray { Count: IndexedSpaceEntries } space) return;
        if (_reader.Resolve(space[0]) is not PdfName { Value: "Indexed" or "I" }) return;
        if (_reader.Resolve(space[2]) is not PdfInteger { Value: >= 0 and <= MaxIndexedHival } hival) return;
        if (_reader.Resolve(space[IndexedLookupSlot]) is not PdfString lookup) return;

        var length = IndexedBaseComponents(space[1]) * ((int)hival.Value + 1);
        if (length == 0 || lookup.Value.Length <= length) return;
        var table = new byte[length];
        Array.Copy(lookup.Value, table, length);
        space.ReplaceAt(IndexedLookupSlot, new PdfString(table, lookup.IsHex));
    }

    /// <summary>The number of components one colour of an Indexed space's base takes in the
    /// lookup table; zero when the base is not one this conversion can count.</summary>
    private int IndexedBaseComponents(PdfObject? baseSpace)
    {
        switch (_reader.Resolve(baseSpace))
        {
            case PdfName name:
                return ColorSpaceComponents(name, 0);
            case PdfArray { Count: > 1 } array when _reader.Resolve(array[0]) is PdfName family:
                return family.Value switch
                {
                    "CalGray" or "Separation" => GrayComponents,
                    "CalRGB" or "Lab" => RgbComponents,
                    "ICCBased" => (int)(_reader.ResolveStream(array[1])?.Dict.GetInt("N") ?? 0),
                    "DeviceN" => (_reader.Resolve(array[1]) as PdfArray)?.Count ?? 0,
                    _ => 0,
                };
            default:
                return 0;
        }
    }
}
