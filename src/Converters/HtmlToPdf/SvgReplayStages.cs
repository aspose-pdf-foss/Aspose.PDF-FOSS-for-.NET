using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Replays one SVG element tag: groups push and pop the graphics state, paths accumulate, shapes emit their fill and stroke under the current effect state, masks and defs are skipped.</summary>
    private static bool ReplaySvgTag(SvgReplayState sr, Match tag)
    {
        if (InsideMaskDef(sr, tag.Index)) return true;
        var attrs = tag.Groups["attrs"].Value;
        if (tag.Groups["close"].Success)
        {
            if (tag.Groups["tag"].Value == "g" && sr.stack.Count > 0) (sr.total, sr.alpha, sr.maskId) = sr.stack.Pop();
            return true;
        }
        switch (tag.Groups["tag"].Value)
        {
            case "g":
            {
                ReplaySvgGroup(sr, attrs);
                break;
            }
            case "path":
            {
                ReplaySvgPath(sr, attrs);
                break;
            }
            case "image":
            {
                ReplaySvgImage(sr, attrs);
                break;
            }
        }
        return true;
    }
}
