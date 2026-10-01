using System;
using System.Collections.Generic;

namespace Aspose.Pdf.Converters.PsToPdf;

/// <summary>
/// The graphics half of the interpreter: the state stack, the current path, and the
/// page each <c>showpage</c> hands off. Path points are transformed by the CTM as
/// they arrive, so this class is the only one that knows about user space.
/// </summary>
internal sealed class PsGraphics
{
    /// <summary>The page size the reference converter defaults to when the source
    /// declares none — ISO A4, in points.</summary>
    public const double DefaultPageWidth = 595;

    /// <summary>The default page height in points.</summary>
    public const double DefaultPageHeight = 842;

    private readonly List<PsGraphicsState> _stack = new();
    private readonly List<PsPageWriter> _pages = new();
    private PsPageWriter _writer;

    /// <summary>Start a converter on a page of the given size.</summary>
    public PsGraphics(double width = DefaultPageWidth, double height = DefaultPageHeight)
    {
        PageWidth = width;
        PageHeight = height;
        State = new PsGraphicsState { Ctm = PsMatrix.Identity };
        _writer = new PsPageWriter(PageWidth, PageHeight);
    }

    /// <summary>The page width in points.</summary>
    public double PageWidth { get; private set; }

    /// <summary>The page height in points.</summary>
    public double PageHeight { get; private set; }

    /// <summary>The current graphics state.</summary>
    public PsGraphicsState State { get; private set; }

    /// <summary>The current path, in device space.</summary>
    public PsPath Path => State.Path;

    /// <summary>The writer for the page being built.</summary>
    public PsPageWriter Writer => _writer;

    /// <summary>Every page handed off by <c>showpage</c>, plus the one in progress
    /// when it carries marks.</summary>
    public IReadOnlyList<PsPageWriter> Pages => _pages;

    /// <summary>Resize the page before anything has been painted, which is what an
    /// EPS bounding box and <c>setpagedevice</c> both amount to.</summary>
    public void ResizePage(double width, double height)
    {
        if (width <= 0 || height <= 0) return;
        if (_writer.HasContent) return;
        PageWidth = width;
        PageHeight = height;
        _writer = new PsPageWriter(PageWidth, PageHeight);
    }

    /// <summary>Push a copy of the current state.</summary>
    public void GSave() => _stack.Add(State.Clone());

    /// <summary>Pop the most recently saved state, if any.</summary>
    public void GRestore()
    {
        if (_stack.Count == 0) return;
        State = _stack[_stack.Count - 1];
        _stack.RemoveAt(_stack.Count - 1);
    }

    /// <summary>Pop every saved state down to the given depth.</summary>
    public void GRestoreAll()
    {
        while (_stack.Count > 0) GRestore();
    }

    /// <summary>How many states are stacked, so that <c>restore</c> can unwind to the
    /// depth its matching <c>save</c> recorded.</summary>
    public int StackDepth => _stack.Count;

    /// <summary>Unwind the state stack to a recorded depth.</summary>
    public void UnwindTo(int depth)
    {
        while (_stack.Count > depth) GRestore();
    }

    /// <summary>Map a user-space point through the CTM.</summary>
    public void ToDevice(double x, double y, out double dx, out double dy) =>
        State.Ctm.Transform(x, y, out dx, out dy);

    /// <summary>Map a device-space point back to user space.</summary>
    public void ToUser(double x, double y, out double ux, out double uy) =>
        State.Ctm.Invert().Transform(x, y, out ux, out uy);

    /// <summary>Begin a subpath at a user-space point.</summary>
    public void MoveTo(double x, double y)
    {
        ToDevice(x, y, out var dx, out var dy);
        Path.MoveTo(dx, dy);
    }

    /// <summary>Add a straight segment to a user-space point.</summary>
    public void LineTo(double x, double y)
    {
        ToDevice(x, y, out var dx, out var dy);
        Path.LineTo(dx, dy);
    }

    /// <summary>Add a cubic Bezier through user-space controls.</summary>
    public void CurveTo(double x1, double y1, double x2, double y2, double x3, double y3)
    {
        ToDevice(x1, y1, out var d1X, out var d1Y);
        ToDevice(x2, y2, out var d2X, out var d2Y);
        ToDevice(x3, y3, out var d3X, out var d3Y);
        Path.CurveTo(d1X, d1Y, d2X, d2Y, d3X, d3Y);
    }

    /// <summary>The current point in user space, or false when there is none.</summary>
    public bool TryGetCurrentPoint(out double x, out double y)
    {
        x = y = 0;
        if (!Path.HasCurrentPoint) return false;
        ToUser(Path.CurrentX, Path.CurrentY, out x, out y);
        return true;
    }

    /// <summary>Replace the current path, which is what the operators that rebuild it
    /// wholesale — <c>flattenpath</c>, <c>clippath</c>, the rectangle family — do.</summary>
    public void SetPath(PsPath? path) => State.Path = path ?? new PsPath();

    /// <summary>Send what is painted next to a writer of its own rather than to the
    /// page, so a pattern's cell can be built by running the program's own procedure.
    /// Returns the writer the page was using, to be handed back to
    /// <see cref="EndCapture"/>.</summary>
    public PsPageWriter BeginCapture(PsPageWriter into)
    {
        var previous = _writer;
        _writer = into;
        return previous;
    }

    /// <summary>Stop capturing and put the page's own writer back.</summary>
    public void EndCapture(PsPageWriter previous) => _writer = previous;

    /// <summary>Whether what is painted reaches the page, which the null device
    /// installed for measuring says it does not.</summary>
    public bool Marking => !State.Discarding;

    /// <summary>Install the null device: nothing painted from here on is kept, and the
    /// state starts afresh, with no transformation, no clip and no path — which is what
    /// makes the measuring idiom read back the extent of one string and nothing else.</summary>
    public void NullDevice()
    {
        State.Discarding = true;
        State.Ctm = PsMatrix.Identity;
        State.Path = new PsPath();
        InitClip();
    }

    /// <summary>Paint the current path and clear it, as every paint operator does.</summary>
    public void Paint(PsPaintMode mode)
    {
        if (Marking && !Path.IsEmpty) _writer.PaintPath(Path, State, mode);
        State.Path = new PsPath();
    }

    /// <summary>Intersect the clip with the current path. The path is kept whole
    /// rather than intersected geometrically: a later block re-states it, and PDF
    /// intersects clips itself.</summary>
    public void Clip(bool evenOdd)
    {
        if (Path.IsEmpty) return;
        State.Clips.Add(new PsClip(Path.Clone(), evenOdd));
    }

    /// <summary>Drop the clip back to the whole page.</summary>
    public void InitClip()
    {
        State.Clips = new List<PsClip>();
    }

    /// <summary>The path that outlines the current clip, or the page box when nothing
    /// is clipped. This is what <c>clippath</c> installs as the current path.</summary>
    public PsPath ClipPathOutline()
    {
        if (State.Clip != null) return State.Clip.Clone();
        var page = new PsPath();
        page.MoveTo(0, 0);
        page.LineTo(PageWidth, 0);
        page.LineTo(PageWidth, PageHeight);
        page.LineTo(0, PageHeight);
        page.ClosePath();
        return page;
    }

    /// <summary>Finish the page, keeping it when it carries marks, and start a fresh
    /// one. An unmarked page is still kept when it is the only one, because a program
    /// that paints nothing must still produce a blank page.</summary>
    public void ShowPage()
    {
        _writer.OpenPage();
        _pages.Add(_writer);
        _writer = new PsPageWriter(PageWidth, PageHeight);
        // The page is finished with, and so is everything the program set up to paint
        // it: the transformation goes back to the device's own, the saved states are
        // dropped, and a later restore therefore cannot bring a transformation back
        // across the page break — a poster program that translates once by its margin
        // and then tiles has that margin on its FIRST page alone.
        // The path and the font are not part of that: a program that replays its own
        // path once per page builds the path before the first page is finished and
        // walks the same path, in the same face, after it.
        var kept = State;
        _stack.Clear();
        State = new PsGraphicsState { Ctm = PsMatrix.Identity, Path = kept.Path, Font = kept.Font };
    }

    /// <summary>Every page the program produced. A source that never called
    /// <c>showpage</c> still yields the page it painted, and a source that painted
    /// nothing yields one blank page.</summary>
    public IReadOnlyList<PsPageWriter> FinishedPages()
    {
        var pages = new List<PsPageWriter>(_pages);
        if (_writer.HasContent) pages.Add(_writer);
        if (pages.Count == 0)
        {
            _writer.OpenPage();
            pages.Add(_writer);
        }

        return pages;
    }
}
