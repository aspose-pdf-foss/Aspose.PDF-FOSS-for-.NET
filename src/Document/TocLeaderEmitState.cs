namespace Aspose.Pdf;

public sealed partial class Document
{
/// <summary>Per-call working state of the deferred TOC leader emit. One instance per
/// invocation; never shared. The per-TOC-page fields are reset for every TOC page and the
/// per-entry fields for every entry of it.</summary>
private sealed class TocLeaderEmitState
{
    // The flows that laid the headings out, each with its overflow slot range, and the
    // overflow pages those slots map to: together they say which page a heading finally
    // rendered on.
    public List<(FlowLayout flow, int slotStart, int slotEnd)> pendingFlows = null!;
    public List<Page> overflowPageRefs = null!;

    // The TOC page under emit, its registered Helvetica resource name, its continuation
    // pages and its FINAL index in the page sequence.
    public Page tocPage = null!;
    public string tocFontName = null!;
    public List<Page> tocContPages = null!;
    public int tocPageIdxFinal;

    // The entry under emit, as recorded by the layout side.
    public (int slot, byte[] preLeader, double textEnd, double lastY,
        double entrySize, string entryFace, Text.TabLeaderType leader, double rightStop,
        bool showNumbers, bool underline, string prefix, double x0, Page? destPage, int fallbackIdx,
        Rectangle linkRect, Heading heading, string lastLine, double lastX,
        System.Func<string, double>? measure) rec;

    // The page the entry's last line landed on (the TOC page or one of its continuations)
    // and the physical index of the page the entry links to.
    public Page target = null!;
    public int destIdx;

    // The printed page number, its advance, the leader fill glyph and its advance, the
    // font resource the leader shows with, and the content being built for the entry.
    public string pageNumStr = null!;
    public double pageNumWidth;
    public char leaderChar;
    public double dotW;
    public string leaderFontRes = null!;
    public Content.ContentStreamBuilder lb = null!;
}
}
