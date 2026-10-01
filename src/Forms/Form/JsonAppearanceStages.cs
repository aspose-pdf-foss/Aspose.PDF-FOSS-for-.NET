using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
// The stages of the JSON appearance build: the bbox, matrix, fonts and images of one appearance state.

    /// <summary>ReadAppearanceBBox: one guarded read of the appearance state JSON.</summary>
    private static void ReadAppearanceBBox(JsonAppearanceState ja)
    {
        if (ja.stateEl.TryGetProperty("BBox", out var bbEl)
            && bbEl.ValueKind == System.Text.Json.JsonValueKind.Array && bbEl.GetArrayLength() >= 4)
        {
            var bb = new PdfArray();
            var i = 0;
            foreach (var n in bbEl.EnumerateArray())
            {
                if (i++ >= 4) break;
                bb.Add(new PdfReal(n.GetDouble()));
            }
            ja.sd.Set("BBox", bb);
        }
    }

    /// <summary>ReadAppearanceMatrix: one guarded read of the appearance state JSON.</summary>
    private static void ReadAppearanceMatrix(JsonAppearanceState ja)
    {
        if (ja.stateEl.TryGetProperty("Matrix", out var mxEl)
            && mxEl.ValueKind == System.Text.Json.JsonValueKind.Array && mxEl.GetArrayLength() >= 6)
        {
            var mx = new PdfArray();
            var i = 0;
            foreach (var n in mxEl.EnumerateArray())
            {
                if (i++ >= 6) break;
                mx.Add(new PdfReal(n.GetDouble()));
            }
            ja.sd.Set("Matrix", mx);
        }
    }

    /// <summary>ReadAppearanceFonts: one guarded read of the appearance state JSON.</summary>
    private static void ReadAppearanceFonts(JsonAppearanceState ja)
    {
        if (ja.stateEl.TryGetProperty("Fonts", out var fontsEl)
            && fontsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var fEl in fontsEl.EnumerateArray())
            {
                if (fEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var alias = fEl.GetString();
                if (string.IsNullOrEmpty(alias)) continue;
                var fontEntry = new PdfDictionary();
                fontEntry.Set("Type", new PdfName("Font"));
                fontEntry.Set("Subtype", new PdfName("Type1"));
                fontEntry.Set("BaseFont", new PdfName(MapStandardAlias(alias!)));
                ja.fontDict.Set(alias!, fontEntry);
            }
        }
    }

    /// <summary>ReadAppearanceImages: one guarded read of the appearance state JSON.</summary>
    private static void ReadAppearanceImages(JsonAppearanceState ja)
    {
        if (ja.stateEl.TryGetProperty("Images", out var imgsEl)
            && imgsEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var xobjDict = new PdfDictionary();
            foreach (var imgEl in imgsEl.EnumerateArray())
            {
                if (imgEl.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (!imgEl.TryGetProperty("Name", out var nmEl)
                    || nmEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                if (!imgEl.TryGetProperty("Data", out var dEl)
                    || dEl.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                byte[] samples;
                try { samples = System.Convert.FromBase64String(dEl.GetString()!); }
                catch { continue; }

                var idict = new PdfDictionary();
                idict.Set("Type", new PdfName("XObject"));
                idict.Set("Subtype", new PdfName("Image"));
                if (imgEl.TryGetProperty("Width", out var wEl) && wEl.TryGetInt32(out var w))
                    idict.Set("Width", new PdfInteger(w));
                if (imgEl.TryGetProperty("Height", out var hEl) && hEl.TryGetInt32(out var h))
                    idict.Set("Height", new PdfInteger(h));
                if (imgEl.TryGetProperty("BitsPerComponent", out var bEl) && bEl.TryGetInt32(out var bpc))
                    idict.Set("BitsPerComponent", new PdfInteger(bpc));
                if (imgEl.TryGetProperty("ColorSpace", out var csEl)
                    && csEl.ValueKind == System.Text.Json.JsonValueKind.String)
                    idict.Set("ColorSpace", new PdfName(csEl.GetString()!));
                if (imgEl.TryGetProperty("ImageMask", out var imEl)
                    && imEl.ValueKind == System.Text.Json.JsonValueKind.True)
                    idict.Set("ImageMask", PdfBoolean.True);
                if (imgEl.TryGetProperty("Decode", out var decEl)
                    && decEl.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var da = new PdfArray();
                    foreach (var n in decEl.EnumerateArray()) da.Add(new PdfReal(n.GetDouble()));
                    if (da.Count > 0) idict.Set("Decode", da);
                }
                xobjDict.Set(nmEl.GetString()!, new PdfStream(idict, samples));
            }
            if (xobjDict.Count > 0) ja.resources.Set("XObject", xobjDict);
        }
    }
}
