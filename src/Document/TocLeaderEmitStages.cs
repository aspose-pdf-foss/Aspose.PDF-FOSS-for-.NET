namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>Emit one deferred TOC entry against the final page sequence: its leader
    /// run and page number on the page its last line landed on, then the GoTo link.</summary>
    private void EmitTocLeaderEntry(TocLeaderEmitState te)
    {
        var rec = te.rec;
        te.target = rec.slot == 0 ? te.tocPage : te.tocContPages[rec.slot - 1];
        // An entry of an authored tagged document is one marked-content sequence, and its link
        // annotation is kept for the structure wiring that follows the layout.
        var tagged = rec.heading.TaggedEntry;
        var mcid = tagged is not null ? NextTocMcid(te.target) : -1;
        if (tagged is not null)
            te.target.AddContentStream(System.Text.Encoding.ASCII.GetBytes($"/Span <</MCID {mcid}>> BDC\n"));
        if (rec.preLeader.Length > 0) te.target.AddContentStream(rec.preLeader);
        if (!ResolveTocEntryDestination(te))
        {
            if (tagged is not null) te.target.AddContentStream(System.Text.Encoding.ASCII.GetBytes("EMC\n"));
            return;
        }

        te.pageNumStr = TocDisplayPageNumber(te);
        te.pageNumWidth = MeasureTocEntryText(te, te.pageNumStr);
        // The leader fill glyph: '.' for a Dot leader, '_' for a Solid one
        // (underscore advances abut, so the run reads as a continuous rule).
        te.leaderChar = rec.leader == Text.TabLeaderType.Solid ? '_' : '.';
        te.dotW = MeasureTocEntryText(te, te.leaderChar.ToString());
        te.leaderFontRes = rec.entryFace == "Helvetica"
            ? te.tocFontName : Table.RegisterFont(te.target, rec.entryFace);
        te.lb = new Content.ContentStreamBuilder();
        if (rec.underline) DrawTocEntryUnderline(te);
        if (rec.lastLine.Length > 0 && te.dotW > 0) DrawScaledTocLeaderLine(te);
        else DrawRightAlignedTocLeader(te);
        te.target.AddContentStream(te.lb.Build());
        var link = AddTocEntryLink(te);
        if (tagged is { } entry)
        {
            te.target.AddContentStream(System.Text.Encoding.ASCII.GetBytes("EMC\n"));
            PendingTocTags.Add(new Tagged.TocTag(entry.Header, entry.Item, te.target, mcid, link.Dict, rec.heading.Text));
        }
    }

    /// <summary>The physical index of the page the entry links to: the recorded destination
    /// page, else the page its heading finally rendered on, else the layout-time fallback
    /// shifted past the TOC continuations. False when none resolves to a real page.</summary>
    private bool ResolveTocEntryDestination(TocLeaderEmitState te)
    {
        var rec = te.rec;
        te.destIdx = rec.destPage is not null ? Pages.IndexOf(rec.destPage) : 0;
        if (te.destIdx <= 0) te.destIdx = FinalTocHeadingIdx(te, rec.heading);
        if (te.destIdx <= 0)
            te.destIdx = rec.fallbackIdx > te.tocPageIdxFinal
                ? rec.fallbackIdx + te.tocContPages.Count
                : rec.fallbackIdx;
        return te.destIdx > 0;
    }

    /// <summary>The page a heading FINALLY landed on: the flow that laid it out recorded
    /// its slot; map the slot through that flow's overflow range.</summary>
    private int FinalTocHeadingIdx(TocLeaderEmitState te, Heading h)
    {
        foreach (var (flow, slotStart, slotEnd) in te.pendingFlows)
        {
            if (!flow.TryGetParagraphPosition(h, out var pos)) continue;
            var hp = pos.slot < 0 ? flow.CurrentPage
                : slotStart + pos.slot < slotEnd ? te.overflowPageRefs[slotStart + pos.slot] : null;
            return hp is not null ? Pages.IndexOf(hp) : 0;
        }
        return 0;
    }

    /// <summary>The page number the entry prints. IsCountTocPages=false: the printed
    /// numbers skip the TOC chain itself (TOC page + its continuations) — the first
    /// content page after a one-page TOC prints as "1" — while the GoTo link keeps the
    /// physical index.</summary>
    private static string TocDisplayPageNumber(TocLeaderEmitState te)
    {
        var displayIdx = te.destIdx;
        if (te.tocPage.TocInfo?.IsCountTocPages == false)
        {
            var chainEnd = te.tocPageIdxFinal + te.tocContPages.Count;
            var sub = te.destIdx > chainEnd ? te.tocContPages.Count + 1
                : te.destIdx >= te.tocPageIdxFinal ? te.destIdx - te.tocPageIdxFinal + 1 : 0;
            if (te.destIdx - sub >= 1) displayIdx = te.destIdx - sub;
        }
        return displayIdx.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Entries whose segment declared its own font measure through that font's
    /// real advances (see the layout-side comment).</summary>
    private static double MeasureTocEntryText(TocLeaderEmitState te, string s) =>
        te.rec.measure?.Invoke(s) ?? MeasureEntry(s, te.rec.entrySize, te.rec.entryFace);

    /// <summary>The entry's underlined variant draws filled 0.5 pt rectangles under each
    /// run (prefix at natural width, text to its end).</summary>
    private static void DrawTocEntryUnderline(TocLeaderEmitState te)
    {
        var rec = te.rec;
        var lb = te.lb;
        var uy = rec.lastY - 0.207 * rec.entrySize + 0.207;
        var prefixNat = MeasureEntry(rec.prefix, rec.entrySize, rec.entryFace);
        if (prefixNat > 0)
            lb.SaveState().SetFillColor(0, 0, 0)
                .Rectangle(rec.x0, uy, prefixNat, 0.5).FillEvenOdd().RestoreState();
        lb.SaveState().SetFillColor(0, 0, 0)
            .Rectangle(rec.x0 + prefixNat, uy, rec.textEnd - rec.x0 - prefixNat, 0.5)
            .FillEvenOdd().RestoreState();
    }

    /// <summary>One text show at (x, lastY), horizontally scaled by tz percent. CJK runs
    /// (no glyphs in the Standard-14 set) embed a script-matched face and show as CID hex,
    /// exactly like the entry's earlier lines.</summary>
    private static void TocLeaderShow(TocLeaderEmitState te, double x, string s, double tz)
    {
        if (s.Length == 0) return;
        var rec = te.rec;
        var lb = te.lb;
        var scaled = System.Math.Abs(tz - 100) > 1e-9;
        if (s.Length > 0 && ContainsCjkText(s)
            && Text.CjkFallbackFont.ResolveEmbeddableBytes(s) is { Length: > 0 } cjkTtf)
        {
            var cjkDict = Table.ResolvePageFontDict(te.target);
            var (cjkRes, cjkHex) = Text.Type0FontEmbedder.Embed(
                cjkDict, cjkTtf, "CJK", s.Replace('\t', ' '),
                stripSpacesInBaseFont: true);
            lb.BeginText().SetFont(cjkRes, rec.entrySize).SetFillColor(0, 0, 0)
                .SetTextMatrix(1, 0, 0, 1, x, rec.lastY);
            if (scaled) lb.SetHorizontalScaling(tz);
            lb.ShowTextHex(cjkHex);
            // Tz is graphics state — reset before closing the block.
            if (scaled) lb.SetHorizontalScaling(100);
            lb.EndText();
            return;
        }
        lb.BeginText().SetFont(te.leaderFontRes, rec.entrySize).SetFillColor(0, 0, 0)
            .SetTextMatrix(1, 0, 0, 1, x, rec.lastY);
        if (scaled) lb.SetHorizontalScaling(tz);
        lb.ShowText(s);
        if (scaled) lb.SetHorizontalScaling(100);
        lb.EndText();
    }

    /// <summary>The entry's final line is drawn HERE, horizontally scaled to fit the
    /// column: the numbering prefix paints unscaled at the line start, while the text,
    /// the leader dots and the page number carry one shared Tz. With
    ///   q = (colW − prefixW − textW − numW) / dotW,
    ///   D = round(q) − 1,
    ///   Tz = 100·colW / (prefixW + textW + D·dotW + numW),
    /// the dots+number show starts flush at the SCALED text end — so RAW extraction reads
    /// "…Heading 1....2" with no space, and a CJK entry's dots begin just right of where
    /// its natural width ends. The prefix sits in the denominator but paints unscaled
    /// (numbered lines end a hair short of the right stop).</summary>
    private static void DrawScaledTocLeaderLine(TocLeaderEmitState te)
    {
        var rec = te.rec;
        var linePrefix = rec.prefix.Length > 0
            && rec.lastLine.StartsWith(rec.prefix, StringComparison.Ordinal)
            ? rec.prefix : string.Empty;
        var lineText = rec.lastLine.Substring(linePrefix.Length);
        var prefixW = MeasureTocEntryText(te, linePrefix);
        var textW = MeasureTocEntryText(te, lineText);
        var colW = rec.rightStop - rec.lastX;
        var q = (colW - prefixW - textW - te.pageNumWidth) / te.dotW;
        var dotCount = (int)System.Math.Round(q, System.MidpointRounding.AwayFromZero) - 1;
        if (dotCount < 0) dotCount = 0;
        var natural = prefixW + textW + dotCount * te.dotW + te.pageNumWidth;
        var tz = natural > 0 ? 100.0 * colW / natural : 100.0;
        TocLeaderShow(te, rec.lastX + prefixW, lineText, tz);
        TocLeaderShow(te, rec.lastX + prefixW + textW * tz / 100.0,
            new string(te.leaderChar, dotCount) + te.pageNumStr, tz);
    }

    /// <summary>Legacy right-aligned model for the paths the scaled draw does not cover:
    /// TabLeaderType.None keeps the page number alone on the column stop;
    /// IsShowPageNumbers=false closes the entry with an empty show at the text end (as the
    /// fragment path does); an underlined entry keeps its natural-width text (drawn with
    /// the entry) and right-aligns its leader.</summary>
    private static void DrawRightAlignedTocLeader(TocLeaderEmitState te)
    {
        var rec = te.rec;
        var remainder = rec.rightStop - rec.textEnd - te.pageNumWidth;
        var dotCount = te.dotW > 0 && remainder > 0
            ? (int)System.Math.Round(remainder / te.dotW, System.MidpointRounding.AwayFromZero) - 1
            : 0;
        if (dotCount < 0) dotCount = 0;
        if (rec.leader == Text.TabLeaderType.None) dotCount = 0;
        var leaderText = rec.showNumbers
            ? new string(te.leaderChar, dotCount) + te.pageNumStr : string.Empty;
        var drawStart = rec.showNumbers
            ? rec.rightStop - te.pageNumWidth - dotCount * te.dotW : rec.textEnd;
        te.lb.BeginText().SetFont(te.leaderFontRes, rec.entrySize).SetFillColor(0, 0, 0)
            .SetTextMatrix(1, 0, 0, 1, drawStart, rec.lastY)
            .ShowText(leaderText)
            .EndText();
    }

    /// <summary>The GoTo destination is the top-left of the target page (0, page-height)
    /// mapped back through its rotation — matching the heading-branch links, so validators
    /// that expect the corner destination accept the TOC link too.</summary>
    private Aspose.Pdf.Annotations.Annotation AddTocEntryLink(TocLeaderEmitState te)
    {
        var destPage = Pages.At(te.destIdx);
        var destRect = destPage.GetPageRect(true);
        var (destLeft, destTop) = destPage.RotationMatrix
            .InverseTransformPoint(0, destRect.Height);
        return te.target.Annotations.AddLinkAnnotation(te.rec.linkRect,
            new Aspose.Pdf.Annotations.GoToAction(
                new Aspose.Pdf.Annotations.XYZExplicitDestination(te.destIdx, destLeft, destTop, 0)));
    }
}
