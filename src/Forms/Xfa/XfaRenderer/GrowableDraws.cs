using System.Runtime.CompilerServices;
using System.Xml;

namespace Aspose.Pdf.Forms.Xfa;

internal static partial class XfaRenderer
{
    // The width each text draw without a declared one was found to take, kept with the
    // element: the layout asks a box for its width many times while it measures and places.
    private static readonly ConditionalWeakTable<XmlElement, StrongBox<double>> _grownWidths = new();

    /// <summary>The width of a text draw that declares none: it grows to its text - the
    /// widest of its lines with the side insets, not less than its minimum width - whatever
    /// its alignment, which has nothing to act on without a declared width (measured: a
    /// right- or centre-aligned draw with no w starts at its own x, exactly as wide as its
    /// text; one with a minW and a right alignment ends at x + minW). 0 for any other box.</summary>
    private static double GrownTextWidth(XmlElement e)
    {
        if (e.LocalName != "draw" || e.HasAttribute("w") || e.HasAttribute("rotate")) return 0;
        if (_grownWidths.TryGetValue(e, out var known)) return known.Value;
        double width = 0;
        double natural = NaturalTextWidth(e);
        if (natural > 0) width = Math.Max(natural, Len(e.GetAttribute("minW"), 0));
        _grownWidths.Add(e, new StrongBox<double>(width));
        return width;
    }

    /// <summary>The width a draw's text takes on one line with its side margins, or 0 for a draw with no text.
    /// It is measured as the paint pass measures the text when it wraps it: plain text in the face and at the
    /// width share it is drawn with, rich text run by run.</summary>
    private static double NaturalTextWidth(XmlElement e)
    {
        var (_, _, ml, mr) = Margins(e);
        double widest = 0;
        var rich = FirstChild(e, "value") is { } value ? Descendants(value, "exData").FirstOrDefault() : null;
        if (rich is not null && rich.SelectNodes(".//*[local-name()='p']")!.Count > 0)
        {
            // A text that embeds the value of another box is as wide as that value: not known here.
            if (rich.SelectSingleNode(".//@*[local-name()='embed']") is not null) return 0;
            widest = WidestRichLine(e, rich);
        }
        else
        {
            var text = InnerText(e, "value");
            if (string.IsNullOrWhiteSpace(text)) return 0;
            double fs = FontSize(e), wide = FontWideFactor(e);
            bool bold = FontBold(e), serif = FontSerif(e);
            var family = ResolvedFamily(e, bold);
            foreach (var line in text.Split('\n'))
                widest = Math.Max(widest, family is null ? TextWidth(line.Trim(), fs, bold, serif) * wide : FamTextWidth(line.Trim(), fs, family, bold) * FontHorizontalScale(e));
        }
        return widest <= 0 ? 0 : widest + ml + mr + TextCellSlack;
    }

    /// <summary>Room added to a text cell sized from its text, so rounding never wraps its last word.</summary>
    private const double TextCellSlack = 2;

    /// <summary>The width of the widest line of a draw's rich text when nothing makes it wrap.</summary>
    private static double WidestRichLine(XmlElement e, XmlElement exData)
    {
        var typeface = FirstChild(e, "font")?.GetAttribute("typeface") ?? "";
        var (paragraphs, _) = MeasureRichParas(new Ctx(), e, double.MaxValue, exData, FontSizeN(e) ?? 10, FontBold(e),
            typeface.Length > 0 && !IsSansTypeface(typeface), "left");
        double widest = 0;
        foreach (var (_, lines, _) in paragraphs)
            foreach (var line in lines)
            {
                var words = line.Where(w => w.Text != "\t").ToList();
                double width = line.Sum(w => w.Width) + words.Skip(1).Sum(w => SpaceWidth(w.Style));
                widest = Math.Max(widest, width);
            }
        return widest;
    }
}
