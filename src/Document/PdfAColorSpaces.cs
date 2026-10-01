using Aspose.Pdf.Core;
using Aspose.Pdf.Optimization;

namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The number of components a colour space uses, which is what identifies its
    /// device family: one for grey, three for RGB, four for CMYK. Zero means the space says
    /// nothing about a device.</summary>
    private const int GrayComponents = 1, RgbComponents = 3, CmykComponents = 4;

    /// <summary>
    /// ISO 19005-2 clause 6.2.4.3: a device-specific colour space may be used only when an
    /// OutputIntent describes that device. A file whose output intent is an RGB profile and
    /// whose content paints through CMYK has no way to say what those inks mean, so the
    /// colours are undefined.
    /// </summary>
    /// <remarks>The intent's family is read from the number of components its destination
    /// profile declares. Only named colour spaces and transparency groups are examined -
    /// they state a family outright; the operators that select a device colour directly are
    /// covered by the intent they are painted under.</remarks>
    private void ReportUnmatchedDeviceColorSpaces(PdfFormatConversionOptions options)
    {
        var intent = OutputIntentComponents();
        if (intent == 0) return;

        var reported = new HashSet<int>();
        void Report(PdfObject? reference, int components)
        {
            if (components == 0 || components == intent) return;
            var objectNumber = (reference as PdfIndirectRef)?.ObjectNumber ?? 0;
            if (objectNumber != 0 && !reported.Add(objectNumber)) return;
            options.ConversionLog.Add(new PdfAViolation
            {
                Rule = "DeviceColorSpace",
                Clause = "6.2.4.3",
                Description = "Device specific color space is used, but no corresponding "
                    + "OutputIntent is present",
                ObjectId = objectNumber == 0 ? null : objectNumber.ToString(),
            });
        }

        foreach (var page in Pages)
        foreach (var resources in PageResourceDictionaries(page))
        {
            if (_reader.ResolveDict(resources.Get("ColorSpace")) is { } spaces)
                foreach (var key in spaces.Keys)
                    Report(spaces.Get(key), ColorSpaceComponents(spaces.Get(key), 0));

            if (_reader.ResolveDict(resources.Get("XObject")) is not { } xObjects) continue;
            foreach (var key in xObjects.Keys)
            {
                var xObject = _reader.ResolveStream(xObjects.Get(key))?.Dict;
                if (xObject is null) continue;
                // A transparency group blends in a space of its own, whatever the page's;
                // the group dictionary is what states it, so that is what is reported.
                if (_reader.ResolveDict(xObject.Get("Group")) is { } group)
                    Report(xObject.Get("Group"), ColorSpaceComponents(group.Get("CS"), 0));
                if (xObject.GetName("Subtype") == "Image")
                    Report(xObjects.Get(key), ColorSpaceComponents(xObject.Get("ColorSpace"), 0));
            }
        }
    }

    /// <summary>The number of components the document's output intent describes, taken from
    /// its destination profile; zero when the file states no intent.</summary>
    private int OutputIntentComponents()
    {
        if (_reader.Resolve(_reader.Catalog.Get("OutputIntents")) is not PdfArray intents) return 0;
        foreach (var entry in intents)
        {
            var profile = _reader.ResolveStream(_reader.ResolveDict(entry)?.Get("DestOutputProfile"));
            if (profile is not null) return (int)profile.Dict.GetInt("N");
        }
        return 0;
    }

    /// <summary>The number of components a colour space paints with, following the base of
    /// an indexed or pattern space and the alternate of a separation, DeviceN or ICC space.
    /// Zero when the space is unknown or names no device.</summary>
    private int ColorSpaceComponents(PdfObject? space, int depth)
    {
        if (depth > 8) return 0;
        switch (_reader.Resolve(space))
        {
            case PdfName name:
                return name.Value switch
                {
                    "DeviceGray" or "CalGray" or "G" => GrayComponents,
                    "DeviceRGB" or "CalRGB" or "RGB" => RgbComponents,
                    "DeviceCMYK" or "CMYK" => CmykComponents,
                    _ => 0,
                };
            case PdfArray { Count: > 0 } array when _reader.Resolve(array[0]) is PdfName family:
                return family.Value switch
                {
                    "ICCBased" when array.Count > 1 =>
                        (int)(_reader.ResolveStream(array[1])?.Dict.GetInt("N") ?? 0),
                    "Indexed" or "I" or "Pattern" when array.Count > 1 =>
                        ColorSpaceComponents(array[1], depth + 1),
                    "Separation" when array.Count > 2 => ColorSpaceComponents(array[2], depth + 1),
                    "DeviceN" when array.Count > 2 => ColorSpaceComponents(array[2], depth + 1),
                    _ => ColorSpaceComponents(array[0], depth + 1),
                };
            default:
                return 0;
        }
    }
}
