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
    /// ISO 19005 clause 6.2.9: an image XObject's /Intent must name one of the four
    /// standard rendering intents. Files written by careless producers carry an empty name
    /// there, which is why the message can read back as an empty quoted value.
    /// </summary>
    /// <remarks>Reported against the image object in the xObjects section, once per image
    /// however many pages use it.</remarks>
    private void ReportInvalidRenderingIntents(PdfFormatConversionOptions options)
    {
        var reported = new HashSet<int>();
        foreach (var page in Pages)
        foreach (var resources in PageResourceDictionaries(page))
        {
            if (_reader.ResolveDict(resources.Get("XObject")) is not { } xObjects) continue;

            foreach (var key in xObjects.Keys)
            {
                var xObjectRef = xObjects.Get(key);
                var xObject = _reader.ResolveStream(xObjectRef)?.Dict;
                if (xObject?.GetName("Subtype") != "Image") continue;
                if (_reader.Resolve(xObject.Get("Intent")) is not PdfName intent) continue;
                if (StandardRenderingIntents.Contains(intent.Value)) continue;

                var objectNumber = (xObjectRef as PdfIndirectRef)?.ObjectNumber ?? 0;
                if (objectNumber != 0 && !reported.Add(objectNumber)) continue;
                options.ConversionLog.Add(new PdfAViolation
                {
                    Rule = "RenderingIntent",
                    Clause = "6.2.9",
                    Description = $"Rendering intent '{intent.Value}' is invalid. Should be one of "
                        + "RelativeColorimetric, AbsoluteColorimetric, Perceptual or Saturation",
                    ObjectId = objectNumber == 0 ? null : objectNumber.ToString(),
                });
            }
        }
    }
}
