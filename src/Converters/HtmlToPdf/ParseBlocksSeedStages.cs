using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The root block style the stack starts from, and the body rule's typography over it.</summary>
    private static void SeedRootBlockStyle(ParseBlocksState pb, IReadOnlyDictionary<string, Dictionary<string, string>>? css, double contentWidthPt)
    {
        pb.styleStack.Push(new BlockStyle
        {
            // UA base = 16px serif (12pt); a caller-set body size replaces the default.
            FontSize = pb.bodyFontSize > 0 ? pb.bodyFontSize : pb.uaDefaults ? 12 : 11,
            FontRes = "F1", MarginTop = 0, MarginBottom = 0, LeftIndent = 0,
            AbsOriginLeftPt = 0, AbsOriginWidthPt = contentWidthPt,
            FormDialect = pb.formDialect,
            ArticleRhythm = pb.articleRhythm,
            UaSerif = pb.browserUa,
            UaBoxes = pb.uaBoxes,
            InPageFragment = pb.inlineEmphasisRuns,
            // The body's absolute line-height is the root line box the whole flow inherits.
            LineBoxPt = pb.bodyLineBoxPt,
        });
        // The body rule's line-height and its first RESOLVABLE face seed the root (calibrated
        // flow), the way its size does: the blocks draw and wrap in the sheet's face.
        if (pb.sheetElementTypography && css is not null && css.TryGetValue("body", out var bodyRule))
        {
            if (bodyRule.TryGetValue("line-height", out var bodyLh))
                ApplySheetLineHeight(pb.styleStack.Peek(), bodyLh.Trim());
            if (bodyRule.TryGetValue("font-family", out var bodyFam)
                && ResolvableFamily(bodyFam) is { } bodyFace)
                pb.styleStack.Peek().FontFamily = pb.sheetBodyFace = bodyFace;
        }
    }
}
