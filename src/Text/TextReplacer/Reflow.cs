using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Find <paramref name="needle"/> in <paramref name="hay"/> treating every
    /// run of whitespace in either as one break: the absorber spells a line-straddling
    /// match with the newline it crossed, while the page's own runs carry a plain space
    /// (or nothing at all). Returns the start index and, through
    /// <c>length</c>, how much of the haystack the match covers; -1 when the
    /// text is not there.</summary>
    private static (int result, int length) IndexOfAcrossBreaks(string hay, string needle)
    {
        int length = default;
        length = 0;
        if (string.IsNullOrEmpty(needle)) return (-1, length);
        for (var start = 0; start < hay.Length; start++)
        {
            var i = start;
            var j = 0;
            var ok = true;
            while (j < needle.Length)
            {
                if (char.IsWhiteSpace(needle[j]))
                {
                    while (j < needle.Length && char.IsWhiteSpace(needle[j])) j++;
                    var spanned = 0;
                    while (i < hay.Length && char.IsWhiteSpace(hay[i])) { i++; spanned++; }
                    // A break the page never drew (two runs butted together) still counts:
                    // the newline in the search text stands for the break, not for ink.
                    if (spanned == 0 && i > start && i < hay.Length && j < needle.Length
                        && hay[i] != needle[j]) { ok = false; break; }
                    continue;
                }
                if (i >= hay.Length || hay[i] != needle[j]) { ok = false; break; }
                i++; j++;
            }
            if (!ok) continue;
            length = i - start;
            return (start, length);
        }
        return (-1, length);
    }

    internal bool ReflowFromMatch(Page page, string search, string replacement,
        double matchX, IReadOnlyList<(double y, double lx, double rx)> lines,
        double leftX, double rightMargin, double pitch, double newLineSpacingFactor = 0)
    {
        ReflowCreatedLines = 0;
        if (lines.Count == 0 || string.IsNullOrEmpty(search)) return false;
        var rf = new ReflowState();
        rf.page = page;
        rf.search = search;
        rf.replacement = replacement;
        rf.matchX = matchX;
        rf.lines = lines;
        rf.leftX = leftX;
        rf.rightMargin = rightMargin;
        rf.pitch = pitch;
        rf.newLineSpacingFactor = newLineSpacingFactor;
        rf.reader = rf.page.Reader;
        rf.contentStreams = GetContentStreams(rf.page, rf.reader);
        if (rf.contentStreams.Count == 0) return false;
        rf.streamBytes = CombineStreams(rf.contentStreams);
        rf.fonts = TextAbsorber.ResolveFonts(rf.page.Dict, rf.reader);
        var (rA, rB, rC, rD, rTx, rTy) = PageRotationSeed(rf.page);
        rf.textOps = CollectTextOps(rf.streamBytes, rf.fonts, rf.reader, rA, rB, rC, rD, rTx, rTy);
        if (rf.textOps.Count == 0) return false;

        rf.metricsCache = new Dictionary<PdfDictionary, FontMetrics?>();
        rf.yTol = Math.Max(1.0, Math.Min(3.0, rf.pitch * 0.2));
        rf.affected = new List<(CrossTextOp op, int li, double px)>();
        rf.mapped = new List<(CrossTextOp op, int li, double px)>();
        foreach (var o in rf.textOps)
        {
            if (!CollectReflowTextOp(rf, o)) break;
        }
        if (rf.affected.Count == 0) return false;
        if (!SelectReflowHead(rf)) return false;
        rf.headAdvPad = rf.switchedFace is not null && rf.replacement.Length < rf.search.Length
            ? rf.head.Tc * rf.search.Length * TmScaleOf(rf, rf.head) * ScaleOf(rf, rf.head)
            : 0.0;

        rf.lineBaseY = new double?[rf.lines.Count];
        foreach (var (o, li, _) in rf.mapped) rf.lineBaseY[li] ??= PageY(rf, o);
        for (int li = 1; li < rf.lines.Count; li++) rf.lineBaseY[li] ??= rf.lineBaseY[li - 1] - rf.pitch;
        rf.newPitch = rf.newLineSpacingFactor > 0
            ? rf.newLineSpacingFactor * rf.head.FontSize * TmScaleOf(rf, rf.head) * ScaleOf(rf, rf.head)
            : rf.lines.Count >= 2
                ? (rf.lines[0].y - rf.lines[^1].y) / (rf.lines.Count - 1)
                : rf.pitch;
        rf.pieces = new List<(CrossTextOp op, double x, int line, byte[] bytes, string? sw, int off)>();
        rf.cursor = 0;
        rf.curLi = 0;
        rf.prevOrigEnd = 0;
        rf.prevOrigLine = -1;
        rf.prevOrigText = string.Empty;

        rf.logNotes = rf.page.Reader?.OwnerDocument?.EnableNotificationLogging == true;
        rf.notes = rf.logNotes ? new List<string>() : null;
        rf.pendingPush = null;
        for (int j = 0; j < rf.affected.Count; j++)
        {
            if (!ReflowAffectedLine(rf, j)) break;
        }

        rf.byOp = new Dictionary<CrossTextOp, List<(double x, int line, byte[] bytes, string? sw, int off)>>();
        rf.opOrder = new List<CrossTextOp>();
        BuildReflowInserts(rf);

        rf.result = new MemoryStream();
        rf.lastWritePos = 0;
        rf.delIdx = 0;
        rf.insIdx = 0;
        foreach (var o in rf.opOrder)
        {
            if (!EmitReflowedOp(rf, o)) break;
        }
        CopyTo(rf, rf.streamBytes.Length);

        rf.maxLi = 0;
        foreach (var pc in rf.pieces) if (pc.line > rf.maxLi) rf.maxLi = pc.line;
        ReflowCreatedLines = Math.Max(0, rf.maxLi + 1 - rf.lines.Count);

        // Only a reflow that is actually committed reports its moves.
        if (rf.notes is { Count: > 0 })
            rf.page.NotificationLog += string.Join("\r\n", rf.notes) + "\r\n";

        ReportReflowDebug(rf);

        _replacementCount = 1;
        rf.page.SetContentStream(rf.result.ToArray());
        return true;
    }


}
