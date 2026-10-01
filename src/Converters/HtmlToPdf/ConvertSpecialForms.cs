using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The special HTML forms tried before the general conversion: binary text, STL raster backgrounds, positioned spans and STL positioned classes.</summary>
    private static Document? TryConvertSpecialHtmlForm(string html, HtmlLoadOptions? options)
    {
        var c0Controls = 0;
        foreach (var bch in html)
            if (bch < 0x20 && bch is not ('\t' or '\n' or '\r')) c0Controls++;
        if (c0Controls >= 4 && TryConvertBinaryText(html) is { } binaryDoc)
            return binaryDoc;

        var stlPositioned = IsStlPositionedHtml(html);
        var stlCssOptions = options?.BasePathAutoDerived == true ? null : options;
        var stlCssResolvable = stlPositioned && !string.IsNullOrWhiteSpace(GatherStlCss(html, stlCssOptions));
        if (stlCssResolvable && HasStlRasterBackground(html))
            return ConvertStlPositioned(html, options);
        if (IsPositionedSpanHtml(html) || stlPositioned)
        {
            // The pdf-text dialect carries its geometry inline (always self-contained);
            // the stl_ dialect only re-imports fixed when its stylesheet resolved.
            if (!stlPositioned || stlCssResolvable)
            {
                var fixedDoc = TryConvertPositionedFixedLayout(html, options);
                if (fixedDoc is not null) return fixedDoc;
            }
            return ConvertPositionedSpans(html, options);
        }

        // The em-unit class-positioned export: a fixed-size container div per source
        // page, every line seated by its stylesheet class in ems of the container's own
        // pt font-size, the glyphs in @font-face programs (see StlEmClassFixed.cs). It
        // imports like the other fixed layouts, onto its own band geometry.
        if (IsStlEmClassCandidate(html)
            && TryConvertPositionedFixedLayout(html, options) is { } emClassDoc) return emClassDoc;

        // The class-positioned stl_ export: geometry entirely in the stylesheet
        // (pt-unit absolute classes), vector ink in an svg <object> background —
        // an older flavour of the same PDF→HTML round-trip.
        if (TryRenderStlClassPositioned(html, options) is { } stlClsDoc)
            return stlClsDoc;
        // The absolutely positioned page: one sized, centred absolute container holding
        // nothing but absolutely placed class-styled lines (see AbsolutePage.cs).
        if (TryConvertAbsolutePage(html, options) is { } absPageDoc)
            return absPageDoc;
        return null;
    }
}
