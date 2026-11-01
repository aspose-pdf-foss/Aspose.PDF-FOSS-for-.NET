namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    internal sealed partial class Block
    {
        /// <summary>The block's base direction is right to left (dir="rtl" or direction: rtl on it or an
        /// ancestor): its lines are ordered as an RTL paragraph and set against the right edge.</summary>
        public bool Rtl;
        /// <summary>unicode-bidi: bidi-override: the characters are laid in the base direction as they stand.</summary>
        public bool BidiOverride;
    }

    /// <summary>The base direction a dir attribute names: true for rtl, false for ltr, null for anything else.</summary>
    private static bool? DirAttributeRtl(Token tok)
    {
        if (tok.Attributes is null || !tok.Attributes.TryGetValue("dir", out var dir)) return null;
        return dir.Trim().ToLowerInvariant() switch
        {
            "rtl" => true,
            "ltr" => false,
            _ => null,
        };
    }

    /// <summary>True for a line of a right-to-left block of a left-to-right page that is drawn in visual order:
    /// one whose text carries no styled runs, which index its logical order.</summary>
    private static bool DrawsRtlVisually(BlockTextState bt) =>
        bt.block.Rtl && !bt.profile.rtlDoc && bt.block.BoldRuns is not { Count: > 0 } && bt.block.ItalicRuns is not { Count: > 0 }
        && bt.block.ColorRuns is not { Count: > 0 } && bt.block.SizeRuns is not { Count: > 0 } && bt.block.FamilyRuns is not { Count: > 0 }
        && bt.block.DecorRuns is not { Count: > 0 } && bt.block.UnderlineRuns is not { Count: > 0 } && bt.block.StrikeRuns is not { Count: > 0 };

    /// <summary>A line of a right-to-left block in visual order: reversed as it stands under bidi-override,
    /// else ordered by the bidirectional algorithm on a right-to-left paragraph.</summary>
    private static string RtlBlockVisualLine(Block block, string line)
    {
        if (!block.BidiOverride) return VisualizeRtlParagraph(line);
        var chars = line.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>The run a bdo element closes, written in the direction the element names: a right-to-left run's
    /// characters reversed in place, so the line lays them out as the override reads them.</summary>
    private static void CloseBdoRun(ParseBlocksState pb)
    {
        if (pb.openBdoRuns.Count == 0) return;
        var (start, rtl) = pb.openBdoRuns.Pop();
        if (!rtl || start >= pb.currentText.Length) return;
        var run = pb.currentText.ToString(start, pb.currentText.Length - start).ToCharArray();
        Array.Reverse(run);
        pb.currentText.Remove(start, run.Length).Insert(start, run);
    }
}
