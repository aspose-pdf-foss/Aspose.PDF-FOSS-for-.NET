using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    // The first versions of the features a conversion to an older plain version takes out: object and
    // cross-reference streams and the JPXDecode filter came with PDF 1.5, the JBIG2Decode filter with 1.4.
    private const string ObjectStreamsVersion = "1.5";
    private const string Jbig2Version = "1.4";

    // The decoded samples of a transcoded image are eight bits a component; a continuous-tone one is
    // stored as a JPEG of a quality that keeps a scan's detail.
    private const int TranscodedBitsPerComponent = 8;
    private const int TranscodedJpegQuality = 90;

    /// <summary>Set by a conversion to a version before 1.5: its save writes a cross-reference table and no
    /// object streams, which a reader of that version could not parse.</summary>
    private bool _savesBelowObjectStreams;

    /// <summary>Takes out of the document what a reader of <paramref name="version"/> cannot read: the
    /// save packs no object streams, and JPEG 2000 (and, before 1.4, JBIG2) images are decoded and stored
    /// Flate-compressed. False, with a logged problem per image, when an image cannot be decoded.</summary>
    private bool DowngradeToPlainVersion(string version, List<PdfAViolation> log)
    {
        _savesBelowObjectStreams = string.CompareOrdinal(version, ObjectStreamsVersion) < 0;
        if (!_savesBelowObjectStreams) return true;
        var jbig2Too = string.CompareOrdinal(version, Jbig2Version) < 0;

        var converted = true;
        foreach (var (objNum, image) in LaterFilterImages(jbig2Too))
        {
            var filter = SoleFilter(image.Dict);
            var done = filter == "JPXDecode" ? TranscodeJpx(image) : TranscodeJbig2(image);
            if (done) continue;
            converted = false;
            log.Add(new PdfAViolation
            {
                Rule = filter!,
                Description = $"The {filter} image cannot be decoded for PDF {version}",
                ObjectId = objNum.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Convertable = false,
            });
        }
        return converted;
    }

    /// <summary>The image streams whose one filter came after the target version, from the file and from
    /// what this session added. A stream is never kept in an object stream, so the file's are among its
    /// uncompressed entries.</summary>
    private List<(int ObjNum, PdfStream Image)> LaterFilterImages(bool jbig2Too)
    {
        var candidates = new List<(int, PdfObject?)>();
        foreach (var kv in _reader.XRefTable.Entries.ToList())
        {
            if (kv.Key == 0 || !kv.Value.InUse || kv.Value.IsCompressed) continue;
            candidates.Add((kv.Key, _reader.Resolve(new PdfIndirectRef(kv.Key, kv.Value.Generation))));
        }
        foreach (var (objNum, obj) in _newObjects) candidates.Add((objNum, obj));
        if (_pages is not null)
            foreach (var (objNum, obj) in _pages.ImportedObjects) candidates.Add((objNum, obj));

        var images = new List<(int, PdfStream)>();
        foreach (var (objNum, obj) in candidates)
        {
            if (obj is not PdfStream stream || stream.Dict.GetName("Subtype") != "Image") continue;
            var filter = SoleFilter(stream.Dict);
            if (filter == "JPXDecode" || (jbig2Too && filter == "JBIG2Decode")) images.Add((objNum, stream));
        }
        return images;
    }

    private static string? SoleFilter(PdfDictionary dict) => dict.Get("Filter") switch
    {
        PdfName name => name.Value,
        PdfArray { Count: 1 } array when array[0] is PdfName name => name.Value,
        _ => null,
    };

    /// <summary>The image's JPEG 2000 codestream decoded to eight-bit samples in its colour space (or the
    /// device space of its component count, where the image names none - the codestream's own space).</summary>
    private bool TranscodeJpx(PdfStream image)
    {
        if (image.Dict.GetInt("SMaskInData", 0) != 0) return false; // its alpha channel would need a mask of its own
        if (JpxDecoder.TryDecode(_reader.DecodeStream(image, image.ObjectNumber, image.Generation)) is not (var pixels, var width, var height, var components))
            return false;
        if (width != image.Dict.GetInt("Width", 0) || height != image.Dict.GetInt("Height", 0)) return false;

        var colorSpace = _reader.Resolve(image.Dict.Get("ColorSpace"));
        if (colorSpace is null)
        {
            string? device = components switch { 1 => "DeviceGray", 3 => "DeviceRGB", 4 => "DeviceCMYK", _ => null };
            if (device is null) return false;
            colorSpace = new PdfName(device);
            image.Dict.Set("ColorSpace", colorSpace);
        }
        else if (ColorSpaceComponents(colorSpace) != components)
        {
            return false;
        }
        // Gray and colour samples become a JPEG, as compact as the JPEG 2000 they came from; samples that
        // are indices or separations must stay exact.
        if (components is 1 or 3 && IsContinuousTone(colorSpace))
            StoreJpeg(image, pixels, width, height, components);
        else
            StoreFlate(image, pixels, TranscodedBitsPerComponent);
        // A JPEG 2000 image's /Decode is ignored; the plain samples would be mapped by it.
        image.Dict.Remove("Decode");
        image.Dict.Remove("SMaskInData");
        return true;
    }

    private static bool IsContinuousTone(PdfObject colorSpace) => colorSpace switch
    {
        PdfName name => name.Value is "DeviceGray" or "DeviceRGB" or "G" or "RGB",
        PdfArray { Count: > 0 } array when array[0] is PdfName name => name.Value is "ICCBased" or "CalGray" or "CalRGB",
        _ => false,
    };

    private static void StoreJpeg(PdfStream image, byte[] samples, int width, int height, int components)
    {
        var data = components == 1
            ? JpegEncoderImpl.EncodeGray(samples, width, height, TranscodedJpegQuality)
            : JpegEncoderImpl.Encode((x, y) =>
            {
                var at = ((long)y * width + x) * 3;
                return (samples[at], samples[at + 1], samples[at + 2]);
            }, width, height, TranscodedJpegQuality);
        image.ReplaceData(data);
        image.Dict.Set("Filter", new PdfName("DCTDecode"));
        image.Dict.Set("Length", new PdfInteger(data.Length));
        image.Dict.Set("BitsPerComponent", new PdfInteger(TranscodedBitsPerComponent));
        image.Dict.Remove("DecodeParms");
    }

    /// <summary>The image's JBIG2 data decoded to one-bit samples, white 1 as DeviceGray reads them.</summary>
    private bool TranscodeJbig2(PdfStream image)
    {
        // An undecodable segment comes back as the data itself, far shorter than the rows it stands for.
        var bits = _reader.DecodeStream(image, image.ObjectNumber, image.Generation);
        var bytesPerRow = ((int)image.Dict.GetInt("Width", 0) + 7) / 8;
        if (bytesPerRow == 0 || bits.Length < bytesPerRow * image.Dict.GetInt("Height", 0)) return false;
        StoreFlate(image, bits, 1);
        return true;
    }

    private static void StoreFlate(PdfStream image, byte[] samples, int bitsPerComponent)
    {
        var data = ManagedDeflater.DeflateSamples(samples);
        image.ReplaceData(data);
        image.Dict.Set("Filter", new PdfName("FlateDecode"));
        image.Dict.Set("Length", new PdfInteger(data.Length));
        image.Dict.Set("BitsPerComponent", new PdfInteger(bitsPerComponent));
        image.Dict.Remove("DecodeParms");
    }

    /// <summary>The number of components a colour space's samples carry; 0 when it is not known.</summary>
    private int ColorSpaceComponents(PdfObject colorSpace)
    {
        var family = colorSpace switch
        {
            PdfName name => name.Value,
            PdfArray { Count: > 0 } array when array[0] is PdfName name => name.Value,
            _ => null,
        };
        return family switch
        {
            "DeviceGray" or "CalGray" or "G" or "Indexed" or "I" or "Separation" => 1,
            "DeviceRGB" or "CalRGB" or "RGB" or "Lab" => 3,
            "DeviceCMYK" or "CMYK" => 4,
            "ICCBased" when colorSpace is PdfArray { Count: > 1 } icc
                && _reader.Resolve(icc[1]) is PdfStream profile => (int)profile.Dict.GetInt("N", 0),
            "DeviceN" when colorSpace is PdfArray { Count: > 1 } deviceN
                && _reader.Resolve(deviceN[1]) is PdfArray names => names.Count,
            _ => 0,
        };
    }
}
