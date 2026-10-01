
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>Hyphenation reflow for a RESTYLED replacement (the caller assigned a
    /// new font/size before setting the text). The reflow
    /// model: the match line's prefix keeps its exact position; the
    /// replacement and every following run drop onto a FRESH baseline one new-font-size
    /// step below the match baseline, flowing from the paragraph's left margin with
    /// greedy word-wrap against (page width − left inset). Retained source text keeps
    /// its own font/size; only replacement spans switch to the new style. Wrap units
    /// split at spaces and may span styles (a replacement glued to source text wraps
    /// as one unit).</summary>
    private bool StyledCascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText)
    {
        var sc = new StyledCascadeState();
        sc.page = page;
        sc.paraLines = paraLines;
        sc.matchLine = matchLine;
        sc.myLLX = myLLX;
        sc.oldText = oldText;
        sc.newText = newText;
        if (!ResolveHead(sc)) return false;
        if (!CollectStyledRuns(sc)) return false;
        if (!GroupUnits(sc)) return false;
        PlacePieces(sc);
        WriteStyledLines(sc);
        return true;
    }

    private double? CascadeFromMatch(Page page,
        System.Collections.Generic.List<(TextFragment f, double y, double lx, double rx)> paraLines,
        int matchLine, double myLLX, string oldText, string newText,
        double pageRightMargin,
        System.Collections.Generic.List<(double y, double lx, double rx)> bandPara)
    {
        // Lowest baseline (paragraph-line Y space) of any line the repack CREATED
        // below the paragraph's last existing baseline; NaN when everything fit.
        double appendedBottom = double.NaN;
        if (matchLine < 0 || matchLine >= paraLines.Count) return null;
        var cf = new CascadeState();
        cf.appendedBottom = appendedBottom;
        cf.page = page;
        cf.paraLines = paraLines;
        cf.matchLine = matchLine;
        cf.myLLX = myLLX;
        cf.oldText = oldText;
        cf.newText = newText;
        cf.pageRightMargin = pageRightMargin;
        cf.bandPara = bandPara;

        // A replacement RESTYLED by the caller (font/size assigned before the text)
        // can't ride the byte-level run mover — the rewritten run must switch to the
        // new face. The restyled content drops onto a FRESH line below
        // the match (the prefix keeps its line) and flows at the new size.
        if (StyledCascadeFromMatch(cf.page, cf.paraLines, cf.matchLine, cf.myLLX, cf.oldText, cf.newText))
        {
            if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
                Console.Error.WriteLine("[reflow-path] styled");
            return appendedBottom;
        }

        // Exact path first: MOVE the original runs (keeping their bytes,
        // fonts, kerning and per-run Tc) and rewrite only the matched operator,
        // re-encoded in its own font. Positions are then preserved to hundredths
        // of a point. Falls back to the coarser delete-and-re-emit below when the page
        // structure defeats it (CID font, replacement glyphs missing from the subset,
        // match not carried by a single run).
        {
            if (TryMoveRuns(cf)) { appendedBottom = cf.appendedBottom; return appendedBottom; }
        }
        if (Environment.GetEnvironmentVariable("ASPOSE_FOSS_FIT_DEBUG") == "1")
            Console.Error.WriteLine("[reflow-path] cascade-fallback");

        if (!CollectMovedRuns(cf)) return null;

        if (!ResolveDominantFont(cf)) return null;

        if (!TokeniseSource(cf)) return null;
        PackWords(cf);

        EmitPackedLines(cf);
        appendedBottom = cf.appendedBottom;
        return appendedBottom;
    }
}
