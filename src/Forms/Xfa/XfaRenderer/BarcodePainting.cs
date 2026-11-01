using System.Globalization;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    /// <summary>The module a barcode is drawn with when it declares none (the form language's default).</summary>
    private const double DefaultBarcodeModule = 0.25 * Mm;
    private const double DefaultCode39Ratio = 3.0;
    private const string Code128TypePrefix = "code128";
    /// <summary>The most of a barcode field's height its readable text line may take.</summary>
    private const double BarcodeTextShare = 0.3;
    private const double BarcodeTextLineEm = 1.15;

    /// <summary>Paints a barcode field as its bars: Code 128 in the set its type names, Code 39, or EAN-13, EAN-8 or UPC-A.
    /// The symbol starts at the field's left edge with the module the barcode declares, narrowed
    /// when the field is not wide enough, and its value is written under (or over) the bars unless
    /// the barcode asks for no text. False for a type the renderer has no bars for - the caller
    /// paints the field as before.</summary>
    private static bool PaintBarcodeField(PaintFieldState pf)
    {
        var barcode = FirstChild(pf.e, "ui")?.ChildNodes.OfType<XmlElement>().FirstOrDefault(c => c.LocalName == "barcode");
        var type = barcode?.GetAttribute("type") ?? "";
        List<(double left, double width)> bars;
        double total;
        var data = pf.val?.Trim() ?? "";
        if (barcode is not null && type.StartsWith(Code128TypePrefix, StringComparison.Ordinal))
        {
            var values = Code128Symbol.Encode(data, type.Length > Code128TypePrefix.Length ? type[Code128TypePrefix.Length] : ' ');
            bars = Code128Symbol.Bars(values);
            total = Code128Symbol.TotalModules(values);
        }
        else if (barcode is not null && type == "code3Of9")
        {
            double ratio = double.TryParse(barcode.GetAttribute("wideNarrowRatio"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r > 0
                ? r : DefaultCode39Ratio;
            var framed = Code39Symbol.Frame(data);
            bars = Code39Symbol.Bars(framed, ratio);
            total = Code39Symbol.TotalModules(framed, ratio) - 1;       // the gap after the stop character is not part of the symbol
        }
        else if (barcode is not null && type is "ean13" or "ean8" or "upcA")
        {
            // Data that is no symbol of the type draws no bars, as a viewer draws none.
            var modules = EanSymbol.Modules(type, data) ?? "";
            bars = EanSymbol.Bars(modules);
            total = modules.Length;
        }
        else return false;
        if (data.Length == 0 || total <= 0) return true;                // nothing to encode: an empty symbol is no bars
        double module = Math.Min(Len(barcode.GetAttribute("moduleWidth"), DefaultBarcodeModule), pf.w / total);
        var textAt = barcode.GetAttribute("textLocation");
        bool text = textAt != "none";
        double fs = text ? Math.Min(pf.fs, pf.h * BarcodeTextShare / BarcodeTextLineEm) : 0;
        double textH = fs * BarcodeTextLineEm;
        bool textAbove = textAt.StartsWith("above", StringComparison.Ordinal);
        double barTop = pf.y + (textAbove ? textH : 0), barH = pf.h - textH;
        double[] black = { 0, 0, 0 };
        foreach (var (left, width) in bars)
            pf.ctx.Items.Add(new Item { Kind = "fill", X = pf.x + left * module, Y = pf.ctx.PageH - (barTop + barH), W = width * module, H = barH, Color = black });
        if (text)
        {
            double textTop = textAbove ? pf.y : barTop + barH;
            double textX = pf.x + Math.Max(0, (total * module - TextWidth(data, fs, false)) / 2);
            pf.ctx.Items.Add(new Item { Kind = "text", X = textX, Y = pf.ctx.PageH - (textTop + fs), W = total * module, Text = data, FontSize = fs });
        }
        return true;
    }
}
