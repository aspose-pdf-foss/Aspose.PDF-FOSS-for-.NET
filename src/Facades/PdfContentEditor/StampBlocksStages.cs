using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
    /// <summary>The stages of the stamp block search: one content block at a time.</summary>
    private static void FindStampBlock(StampBlocksState sk, Aspose.Pdf.Facades.PdfContentEditor.QBlock block)
    {
        // (a) %StampId= and/or %StampRect= comment immediately preceding the block.
        // Either marker identifies a stamp block; %StampRect carries the exact
        // page-space bounds (header/footer bands) so we can report them verbatim.
        var extendedStart = block.Start;
        var commentId = 0;
        var hasComment = false;
        Rectangle? commentRect = null;
        if (block.Start > 0)
        {
            var searchStart = Math.Max(0, block.Start - 90);
            var preceding = sk.content.Substring(searchStart, block.Start - searchStart);
            // %StampId, when present, sits immediately before the block or just
            // before an optional %StampRect line — anchor at the end so a previous
            // block's id inside the window is not picked up.
            var idLineMatch = Regex.Match(preceding,
                @"%StampId=(\d+)\s*(?:%StampRect=[^\n]*)?\s*$");
            var rectMatch = Regex.Match(preceding,
                @"%StampRect=(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*$");
            if (idLineMatch.Success)
            {
                hasComment = true;
                commentId = int.Parse(idLineMatch.Groups[1].Value);
                extendedStart = searchStart + idLineMatch.Index;
            }
            if (rectMatch.Success)
            {
                hasComment = true;
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                commentRect = new Rectangle(
                    double.Parse(rectMatch.Groups[1].Value, ci), double.Parse(rectMatch.Groups[2].Value, ci),
                    double.Parse(rectMatch.Groups[3].Value, ci), double.Parse(rectMatch.Groups[4].Value, ci));
                if (!idLineMatch.Success) extendedStart = searchStart + rectMatch.Index;
            }
        }

        var blockContent = sk.content.Substring(block.Start, block.End - block.Start);

        // (b) /Name Do referencing an XObject. Capture the first XObject drawn by
        // the block (used to recover the stamp image) and detect the
        // /StampId dictionary marker.
        var hasDictMarker = false;
        var dictId = 0;
        var isImage = false;
        string? xname = null;
        PdfStream? xobject = null;
        if (sk.xobjects is not null)
        {
            foreach (Match dm in Regex.Matches(blockContent, @"/([A-Za-z0-9_.\-]+)\s+Do\b"))
            {
                var name = dm.Groups[1].Value;
                if (sk.doc.Reader.Resolve(sk.xobjects.Get(name)) is not PdfStream xs) continue;
                if (xobject is null)
                {
                    xobject = xs;
                    xname = name;
                    isImage = xs.Dict.GetName("Subtype") == "Image";
                }
                if (xs.Dict.ContainsKey("StampId"))
                {
                    hasDictMarker = true;
                    dictId = (int)xs.Dict.GetInt("StampId", 0);
                    isImage = xs.Dict.GetName("Subtype") == "Image";
                    xname = name;
                    xobject = xs;
                    break;
                }
            }
        }

        // (c) Unmarked image stamp: a block whose only operators are q/Q/gs/cm and a
        // single image Do is the canonical image-placement shape emitted for an
        // image stamp. GetStamps must rediscover these even when the %StampId marker
        // was never written (or was stripped by an earlier re-serialisation), matching
        // the GetStamps contract, which reports such blocks with StampId 0.
        var isCleanImageStamp = isImage && xobject is not null &&
                                IsCleanImagePlacementBlock(blockContent);

        if (!hasComment && !hasDictMarker && !isCleanImageStamp) return;

        // A %StampHidden=1 marker sits immediately before the comment cluster when the
        // stamp was hidden via HideStampById. Detect it and extend Start to cover it so
        // a later DeleteStamp/MoveStamp rewrite preserves (or removes) it as one unit.
        var hidden = false;
        if (extendedStart >= StampHiddenMarker.Length &&
            string.CompareOrdinal(sk.content, extendedStart - StampHiddenMarker.Length,
                StampHiddenMarker, 0, StampHiddenMarker.Length) == 0)
        {
            hidden = true;
            extendedStart -= StampHiddenMarker.Length;
        }

        // The %StampId comment (when present) names the id; otherwise use the dict id.
        var stampId = commentId != 0 ? commentId : dictId;
        sk.result.Add(new StampBlock(extendedStart, block.End, stampId, isImage, xname, xobject, commentRect, hidden));
    }
}
