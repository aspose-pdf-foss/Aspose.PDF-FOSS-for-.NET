using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextFragmentAbsorber
{
    /// <summary>The q/Q stack, the CTM, the line width and XObjects walked in their own matrix.</summary>
    private static void ExtractRunsStateOperator(ExtractRunsState xr, string op)
    {
        switch (op)
        {
            // ── Graphics state: CTM tracking ──
            case "q":
                xr.ctmStack.Push(xr.ctm);
                xr.clipStack.Push(xr.currentClip);
                // Save text state parameters (part of the graphics state per PDF spec).
                xr.gsStack.Push((xr.leading, xr.charSpacing, xr.wordSpacing, xr.hScaling, xr.textRise, xr.renderMode,
                    xr.currentFillColor, xr.currentStrokeColor, xr.currentFontName, xr.currentFontNameForGuard,
                    xr.fontSize, xr.fontDict, xr.toUnicode, xr.metrics, xr.currentFontInfo,
                    xr.currentIsBold, xr.currentIsItalic, xr.fontIsBold, xr.currentFontMissing));
                break;
            case "Q":
                RestoreStateOp(xr);
                break;
            case "cm":
                if (xr.operands.Count >= 6)
                {
                    var m = new Matrix(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]),
                        GetNum(xr.operands[2]), GetNum(xr.operands[3]),
                        GetNum(xr.operands[4]), GetNum(xr.operands[5]));
                    xr.ctm = m.Multiply(xr.ctm);
                }
                break;

            case "w":
                if (xr.fillRects is not null && xr.operands.Count >= 1)
                    xr.currentLineWidth = GetNum(xr.operands[0]);
                break;

            // ── XObject invocation ──
            case "Do":
                DrawXObjectOp(xr);
                break;
        }
    }

    /// <summary>The fill and stroke colour operators the runs carry.</summary>
    private static void ExtractRunsColorOperator(ExtractRunsState xr, string op)
    {
        switch (op)
        {
            // ── Nonstroking (fill) color operators ──
            // Tracked unconditionally so each fragment's ForegroundColor
            // reflects the glyph fill colour in effect when it was drawn,
            // not only under SearchForTextRelatedGraphics.
            case "g":
                if (xr.operands.Count >= 1)
                    xr.currentFillColor = GrayAsRgb(GetNum(xr.operands[0]));
                break;
            case "rg":
                if (xr.operands.Count >= 3)
                    xr.currentFillColor = Color.FromRgb(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]), GetNum(xr.operands[2]));
                break;
            case "k":
                if (xr.operands.Count >= 4)
                    xr.currentFillColor = Color.FromCmyk(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]),
                        GetNum(xr.operands[2]), GetNum(xr.operands[3]));
                break;
            case "sc":
            case "scn":
                // Pick by operand count: 1=gray, 3=rgb, 4=cmyk.
                // Pattern color spaces (where scn takes a /Name) fall through unchanged.
                if (xr.operands.Count == 1 && xr.operands[0] is not PdfName)
                    xr.currentFillColor = GrayAsRgb(GetNum(xr.operands[0]));
                else if (xr.operands.Count == 3)
                    xr.currentFillColor = Color.FromRgb(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]), GetNum(xr.operands[2]));
                else if (xr.operands.Count == 4)
                    xr.currentFillColor = Color.FromCmyk(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]),
                        GetNum(xr.operands[2]), GetNum(xr.operands[3]));
                break;

            // ── Stroking color operators ──
            // Captured unconditionally onto each run's StrokingColor so a
            // round-tripped TextState.StrokingColor (e.g. stroked text) survives.
            case "G":
                if (xr.operands.Count >= 1)
                    xr.currentStrokeColor = GrayAsRgb(GetNum(xr.operands[0]));
                break;
            case "RG":
                if (xr.operands.Count >= 3)
                    xr.currentStrokeColor = Color.FromRgb(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]), GetNum(xr.operands[2]));
                break;
            case "K":
                if (xr.operands.Count >= 4)
                    xr.currentStrokeColor = Color.FromCmyk(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]),
                        GetNum(xr.operands[2]), GetNum(xr.operands[3]));
                break;
            case "SC":
            case "SCN":
                if (xr.operands.Count == 1 && xr.operands[0] is not PdfName)
                    xr.currentStrokeColor = GrayAsRgb(GetNum(xr.operands[0]));
                else if (xr.operands.Count == 3)
                    xr.currentStrokeColor = Color.FromRgb(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]), GetNum(xr.operands[2]));
                else if (xr.operands.Count == 4)
                    xr.currentStrokeColor = Color.FromCmyk(
                        GetNum(xr.operands[0]), GetNum(xr.operands[1]),
                        GetNum(xr.operands[2]), GetNum(xr.operands[3]));
                break;

        }
    }

    /// <summary>The path construction, clipping and painting operators the runs are clipped and backed by.</summary>
    private static void ExtractRunsPathOperator(ExtractRunsState xr, string op)
    {
        switch (op)
        {
            // ── Path construction ──
            case "re":
                if (xr.operands.Count >= 4)
                {
                    var rx = GetNum(xr.operands[0]);
                    var ry = GetNum(xr.operands[1]);
                    var rw = GetNum(xr.operands[2]);
                    var rh = GetNum(xr.operands[3]);
                    if (xr.fillRects is not null || xr.coverRects is not null)
                        xr.pendingPathRects.Add((rx, ry, rw, rh, xr.ctm));
                    xr.pathSubpaths++;
                    AddPathPoint(xr, rx, ry, xr.ctm);
                    AddPathPoint(xr, rx + rw, ry, xr.ctm);
                    AddPathPoint(xr, rx + rw, ry + rh, xr.ctm);
                    AddPathPoint(xr, rx, ry + rh, xr.ctm);
                }
                break;
            case "m":
            case "l":
                // Of all the path operators only `m` validates its operand
                // count: a moveto with 0, 1 or 5 operands throws, while a
                // malformed `l`/`re`/`c` parses leniently (measured on
                // synthetic streams). An `m` the lexer split off a fused
                // lexeme is damaged-stream salvage, not an authored
                // operator — those stay lenient (a corrupt-flate page whose
                // salvage tail is junk must still extract).
                if (op == "m" && xr.operands.Count != 2 && !xr.lexer.LastKeywordFused)
                    throw new ArgumentException("Invalid parameters count for m operator.");
                if (xr.fillRects is not null || xr.coverRects is not null)
                    xr.currentPathHasNonRect = true;
                if (xr.operands.Count >= 2)
                {
                    if (xr.fillRects is not null || xr.coverRects is not null)
                        xr.strokePts.Add((GetNum(xr.operands[0]), GetNum(xr.operands[1]), xr.ctm));
                    if (op == "m") xr.pathSubpaths++;
                    AddPathPoint(xr, GetNum(xr.operands[0]), GetNum(xr.operands[1]), xr.ctm);
                }
                break;
            case "c":
            case "v":
            case "y":
                if (xr.fillRects is not null || xr.coverRects is not null) xr.currentPathHasNonRect = true;
                // Control points over-approximate the curve's bbox — safe for
                // both clip tracking and cover capture.
                for (var pi = 0; pi + 1 < xr.operands.Count; pi += 2)
                    AddPathPoint(xr, GetNum(xr.operands[pi]), GetNum(xr.operands[pi + 1]), xr.ctm);
                break;
            case "h":
                if (xr.fillRects is not null || xr.coverRects is not null) xr.currentPathHasNonRect = true;
                break;

            // ── Clipping ──
            case "W":
            case "W*":
                // The clip takes effect at the next path-painting operator,
                // intersecting the active clip with this path's bbox.
                xr.pendingClip = true;
                break;

            // ── Path painting: emit pending rects as fill rects. ──
            // f/F/f*/B/b/B*/b* paint the current path (with fill); n/S/s do not fill.
            case "f":
            case "F":
            case "f*":
            case "B":
            case "B*":
            case "b":
            case "b*":
                FillPathOp(xr);
                break;
            case "S":
            case "s":
                StrokePathOp(xr);
                break;
            case "n":
                ApplyPendingClip(xr);
                ResetPathBbox(xr);
                xr.pendingPathRects.Clear();
                xr.currentPathHasNonRect = false;
                xr.strokePts.Clear();
                break;

        }
    }

    /// <summary>The text object, its font, spacing, scale, rise and line moves.</summary>
    private static void ExtractRunsTextStateOperator(ExtractRunsState xr, string op)
    {
        switch (op)
        {
            // ── Text block delimiters ──
            case "BT":
                BeginTextOp(xr);
                break;

            // ── Text state operators ──
            case "Tf":
                SetFontOp(xr);
                break;
            case "TL":
                if (xr.operands.Count >= 1)
                    xr.leading = GetNum(xr.operands[0]);
                break;
            case "Tr":
                if (xr.operands.Count >= 1)
                {
                    xr.renderMode = (int)GetNum(xr.operands[0]);
                    // Rendering mode 2 (fill+stroke) visually simulates bold text.
                    // Restore font-intrinsic bold when mode changes away from 2.
                    xr.currentIsBold = xr.renderMode == 2 || xr.fontIsBold;
                }
                break;
            case "Tc":
                if (xr.operands.Count >= 1)
                    xr.charSpacing = GetNum(xr.operands[0]);
                break;
            case "Tw":
                if (xr.operands.Count >= 1)
                    xr.wordSpacing = GetNum(xr.operands[0]);
                break;
            case "Tz":
                if (xr.operands.Count >= 1)
                    xr.hScaling = GetNum(xr.operands[0]) / 100.0;
                break;
            case "Ts":
                if (xr.operands.Count >= 1)
                    xr.textRise = GetNum(xr.operands[0]);
                break;

            // ── Text positioning operators ──
            case "Td":
                MoveTextLineOp(xr);
                break;
            case "TD":
                MoveTextLineSetLeadingOp(xr);
                break;
            case "Tm":
                SetTextMatrixOp(xr);
                break;
            case "T*":
                NextTextLineOp(xr);
                break;

        }
    }

    /// <summary>The text-showing operators, each string becoming runs.</summary>
    private static void ExtractRunsTextShowOperator(ExtractRunsState xr, string op)
    {
        switch (op)
        {
            // ── Text showing operators ──
            case "Tj":
                ShowTextOp(xr);
                break;
            case "TJ":
                ShowTextArrayOp(xr);
                break;
            case "'":
                ShowTextNextLineOp(xr);
                break;
            case "\"":
                ShowTextSpacedNextLineOp(xr);
                break;

        }
    }

    /// <summary>The run extractor's starting state for one content stream: its font, text state, matrices and stacks.</summary>
    private static void OpenExtractRunsState(ExtractRunsState xr)
    {
        xr.currentFontMissing = false;
        xr.toUnicode = null;
        xr.fontDict = null;
        xr.metrics = null;
        xr.currentFontInfo = null;
        xr.fontSize = 12;
        xr.tx = 0;
        xr.ty = 0;
        xr.txLine = 0;
        xr.tyLine = 0;
        xr.leading = 0;
        xr.charSpacing = 0;
        xr.tmBaseTy = 0;
        xr.wordSpacing = 0;
        xr.hScaling = 1.0;
        xr.textRise = 0;
        xr.tmA = 1.0;
        xr.tmB = 0.0;
        xr.tmC = 0.0;
        xr.tmD = 1.0;

        xr.ctm = xr.inheritedCtm ?? Matrix.Identity;
        xr.ctmStack = new Stack<Matrix>();

        xr.gsStack = new Stack<(double leading, double charSpacing, double wordSpacing,
            double hScaling, double textRise, int renderMode, Color fillColor, Color? strokeColor,
            string? fontName, string? fontNameGuard, double fontSize, PdfDictionary? fontDict,
            Dictionary<int, string>? toUnicode, FontMetrics? metrics, Font? fontInfo,
            bool isBold, bool isItalic, bool fontBold, bool fontMissing)>();

        xr.currentFillColor = Color.Black;

        xr.currentStrokeColor = null;

        xr.pendingPathRects = new List<(double x, double y, double w, double h, Matrix ctmAtRe)>();
        xr.currentPathHasNonRect = false;
        xr.pathMinX = double.PositiveInfinity;
        xr.pathMinY = double.PositiveInfinity;
        xr.pathMaxX = double.NegativeInfinity;
        xr.pathMaxY = double.NegativeInfinity;
        xr.pathSubpaths = 0;
        xr.currentClip = xr.inheritedClip;
        xr.clipStack = new Stack<(double Llx, double Lly, double Urx, double Ury)?>();
        xr.pendingClip = false;
        xr.strokePts = new List<(double x, double y, Matrix ctm)>();
        xr.currentLineWidth = 1.0;

        xr.renderMode = 0;

        xr.currentIsBold = false;
        xr.currentIsItalic = false;
        xr.fontIsBold = false;

        xr.lastEmittedY = double.NaN;
        xr.lastEmittedPageY = double.NaN;
        xr.lastEmittedFs = double.NaN;

    }
}
