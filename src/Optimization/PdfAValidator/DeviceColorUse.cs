using Aspose.Pdf.Core;

namespace Aspose.Pdf.Optimization;

internal static partial class PdfAValidator
{
    /// <summary>The device colour families a content stream can paint through.</summary>
    [Flags]
    private enum DeviceColor
    {
        None = 0,
        Gray = 1,
        Rgb = 2,
        Cmyk = 4,
        Any = Gray | Rgb | Cmyk,
    }

    /// <summary>
    /// True when the document may paint through a device colour space that nothing describes:
    /// such a file needs an output intent (ISO 19005-1 clause 6.2.3, 19005-2 clause 6.2.4.3).
    /// A page whose resources redirect a device family with a Default colour space paints
    /// that family through the calibrated space instead. Only the pages' own content and
    /// image dictionaries are read; what this cannot see into - a form, an inline image, a
    /// shading, an annotation's appearance - counts as device colour, so a file is said to
    /// need no intent only when everything it paints was looked at.
    /// </summary>
    private static bool MayPaintUndescribedDeviceColor(Document document)
    {
        var reader = document.Reader;
        foreach (var page in document.Pages)
        {
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            var spaces = reader.ResolveDict(resources?.Get("ColorSpace"));
            var open = DeviceColor.None;
            if (spaces?.ContainsKey("DefaultGray") != true) open |= DeviceColor.Gray;
            if (spaces?.ContainsKey("DefaultRGB") != true) open |= DeviceColor.Rgb;
            if (spaces?.ContainsKey("DefaultCMYK") != true) open |= DeviceColor.Cmyk;
            if (open == DeviceColor.None) continue;

            if (reader.Resolve(page.Dict.Get("Annots")) is PdfArray annotations
                && annotations.Any(annotation => reader.ResolveDict(annotation)?.ContainsKey("AP") == true))
                return true;
            if (reader.ResolveDict(resources?.Get("XObject")) is { } xObjects)
                foreach (var key in xObjects.Keys)
                {
                    var xObject = reader.ResolveStream(xObjects.Get(key))?.Dict;
                    if (xObject?.GetName("Subtype") != "Image") return true;
                    if (xObject.GetBool("ImageMask")) continue;
                    if ((FamilyOf(reader, xObject.Get("ColorSpace")) & open) != 0) return true;
                }

            IReadOnlyList<string> operators;
            try { operators = page.Contents.OperatorTexts(); }
            catch { return true; }
            if (ContentPaintsDeviceColor(operators, open, reader, spaces)) return true;
        }
        return false;
    }

    /// <summary>Walks one page's operators with the fill and stroke colour of the graphics
    /// state: both start in DeviceGray, a colour operator replaces one, q and Q save and
    /// restore them. True when a paint operator uses a family in <paramref name="open"/>.</summary>
    private static bool ContentPaintsDeviceColor(IReadOnlyList<string> operators, DeviceColor open,
        IO.PdfReader reader, PdfDictionary? spaces)
    {
        var fill = DeviceColor.Gray;
        var stroke = DeviceColor.Gray;
        var saved = new Stack<(DeviceColor Fill, DeviceColor Stroke)>();
        foreach (var text in operators)
        {
            var space = text.LastIndexOf(' ');
            var name = space < 0 ? text : text.Substring(space + 1);
            switch (name)
            {
                case "q": saved.Push((fill, stroke)); break;
                case "Q": if (saved.Count > 0) (fill, stroke) = saved.Pop(); break;
                case "g": fill = DeviceColor.Gray; break;
                case "G": stroke = DeviceColor.Gray; break;
                case "rg": fill = DeviceColor.Rgb; break;
                case "RG": stroke = DeviceColor.Rgb; break;
                case "k": fill = DeviceColor.Cmyk; break;
                case "K": stroke = DeviceColor.Cmyk; break;
                case "cs": fill = SelectedFamily(text, reader, spaces); break;
                case "CS": stroke = SelectedFamily(text, reader, spaces); break;
                case "f" or "F" or "f*" or "Tj" or "TJ" or "'" or "\"":
                    if ((fill & open) != 0) return true;
                    break;
                case "S" or "s":
                    if ((stroke & open) != 0) return true;
                    break;
                case "B" or "B*" or "b" or "b*":
                    if (((fill | stroke) & open) != 0) return true;
                    break;
                case "sh" or "BI":
                    return true;
            }
        }
        return false;
    }

    /// <summary>The family of the colour space a <c>cs</c> or <c>CS</c> operator selects: a
    /// device space by its name, any other name through the page's colour space resources.</summary>
    private static DeviceColor SelectedFamily(string operatorText, IO.PdfReader reader, PdfDictionary? spaces)
    {
        var end = operatorText.IndexOf(' ');
        if (end <= 1 || operatorText[0] != '/') return DeviceColor.Any;
        var name = new PdfName(operatorText.Substring(1, end - 1));
        return spaces?.Get(name.Value) is { } resource ? FamilyOf(reader, resource) : FamilyOf(reader, name);
    }

    /// <summary>The device family a colour space paints through: none for a calibrated or
    /// profiled space, the base's for an indexed one, and any for a space that is not looked
    /// into (a separation, a pattern, a space that is missing).</summary>
    private static DeviceColor FamilyOf(IO.PdfReader reader, PdfObject? space) => reader.Resolve(space) switch
    {
        PdfName { Value: "DeviceGray" or "G" } => DeviceColor.Gray,
        PdfName { Value: "DeviceRGB" or "RGB" } => DeviceColor.Rgb,
        PdfName { Value: "DeviceCMYK" or "CMYK" } => DeviceColor.Cmyk,
        PdfArray { Count: > 0 } array => reader.Resolve(array[0]) switch
        {
            PdfName { Value: "ICCBased" or "CalRGB" or "CalGray" or "Lab" } => DeviceColor.None,
            PdfName { Value: "Indexed" or "I" } when array.Count > 1 => FamilyOf(reader, array[1]),
            _ => DeviceColor.Any,
        },
        _ => DeviceColor.Any,
    };
}
