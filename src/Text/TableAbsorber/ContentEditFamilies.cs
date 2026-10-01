using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TableAbsorber
{
    /// <summary>The q/Q stack and the CTM the filter tracks.</summary>
    private static void FilterStateOperator(ContentFilterState cf, string op)
    {
        switch (op)
        {
            // ── Graphics state ──
            case "q":
                cf.ctmStack.Push((cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF));
                break;
            case "Q":
                if (cf.ctmStack.Count > 0)
                    (cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF) = cf.ctmStack.Pop();
                break;
            case "cm":
                // Concatenate matrix: CTM' = operand × CTM (PDF 32000 §8.3.4)
                if (cf.operands.Count >= 6)
                    ConcatenateCtm(cf);
                break;

        }
    }

    /// <summary>The path operators: points collected, a painted path inside the table removed, a clip kept.</summary>
    private static void FilterPathOperator(ContentFilterState cf, int tokenStart, int tokenEnd, string op)
    {
        switch (op)
        {
            // ── Path construction (PDF 32000 §8.5.2) ──
            case "m" or "l":
                if (cf.pathStart < 0) cf.pathStart = tokenStart;
                if (cf.operands.Count >= 2)
                    cf.pathPoints.Add(TransformPoint(cf.operands[0], cf.operands[1], cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF));
                break;
            case "re":
                // Rectangle: add opposite corners to capture the full extent
                if (cf.pathStart < 0) cf.pathStart = tokenStart;
                if (cf.operands.Count >= 4)
                {
                    var rx = Num(cf.operands[0]); var ry = Num(cf.operands[1]);
                    var rw = Num(cf.operands[2]); var rh = Num(cf.operands[3]);
                    cf.pathPoints.Add(ApplyMatrix(rx, ry, cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF));
                    cf.pathPoints.Add(ApplyMatrix(rx + rw, ry + rh, cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF));
                }
                break;
            case "c" or "v" or "y":
                // Curve operators — only the endpoint matters for hit testing
                if (cf.pathStart < 0) cf.pathStart = tokenStart;
                if (cf.operands.Count >= 2)
                    cf.pathPoints.Add(TransformPoint(cf.operands[^2], cf.operands[^1], cf.ctmA, cf.ctmB, cf.ctmC, cf.ctmD, cf.ctmE, cf.ctmF));
                break;
            case "h":
                if (cf.pathStart < 0) cf.pathStart = tokenStart;
                break;

            // ── Path painting — finalize and check if path falls inside table ──
            case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*" or "n":
                // textOnly: the caller is sweeping up TEXT a delete could not match, so
                // the drawing around it stays. A path is dropped for merely GRAZING the
                // rectangle (any one point inside), which makes a rule running under a
                // line of text — its top edge a fraction of a point inside the band —
                // collateral damage of replacing that line.
                // decorationOnly: the caller is removing a rule that BELONGS to the text
                // it just replaced, so the path must lie WHOLLY inside that text's band.
                // A rule belonging to a line cannot be wider than the line; a page rule
                // running under it overruns the band and stays.
                if (!cf.textOnly && cf.pathStart >= 0 && cf.pathPoints.Count > 0
                    && (cf.decorationOnly
                        ? AllPointsInRect(cf.pathPoints, cf.tableRect)
                        : AnyPointInRect(cf.pathPoints, cf.tableRect)))
                    cf.removals.Add((cf.pathStart, tokenEnd));
                cf.pathStart = -1;
                cf.pathPoints.Clear();
                break;

            case "W" or "W*":
                // Clipping — preserve as-is
                break;

        }
    }

    /// <summary>The text operators: the text object's start, its matrix and the line moves the filter follows.</summary>
    private static void FilterTextOperator(ContentFilterState cf, int tokenStart, int tokenEnd, string op)
    {
        switch (op)
        {
            // ── Text block (PDF 32000 §9.4) ──
            case "BT":
                cf.btStart = tokenStart;
                cf.textPoints.Clear();
                cf.tx = cf.txLine = cf.ty = cf.tyLine = 0;
                cf.tmA = 1; cf.tmB = 0; cf.tmC = 0; cf.tmD = 1;
                cf.leading = 0;
                break;
            case "ET":
                if (!cf.decorationOnly && cf.btStart >= 0 && cf.textPoints.Count > 0
                    && (cf.wholeBlocksOnly
                        ? AllPointsInRect(cf.textPoints, cf.tableRect)
                        : AnyPointInRect(cf.textPoints, cf.tableRect)))
                    cf.removals.Add((cf.btStart, tokenEnd));
                cf.btStart = -1;
                cf.textPoints.Clear();
                break;

            // ── Text positioning operators ──
            case "TL":
                if (cf.operands.Count >= 1) cf.leading = Num(cf.operands[0]);
                break;
            case "Td":
                if (cf.operands.Count >= 2)
                    UpdateTextPosition(cf);
                break;
            case "TD":
                // TD sets leading and moves — equivalent to: -ty2 TL tx ty Td
                if (cf.operands.Count >= 2)
                {
                    cf.leading = -Num(cf.operands[1]);
                    UpdateTextPosition(cf);
                }
                break;
            case "T*":
                // Move to start of next line using current leading
                cf.txLine = cf.tmC * (-cf.leading) + cf.txLine;
                cf.tyLine = cf.tmD * (-cf.leading) + cf.tyLine;
                cf.tx = cf.txLine; cf.ty = cf.tyLine;
                break;
            case "Tm":
                if (cf.operands.Count >= 6)
                {
                    cf.tmA = Num(cf.operands[0]); cf.tmB = Num(cf.operands[1]);
                    cf.tmC = Num(cf.operands[2]); cf.tmD = Num(cf.operands[3]);
                    cf.tx = cf.txLine = Num(cf.operands[4]);
                    cf.ty = cf.tyLine = Num(cf.operands[5]);
                }
                break;

        }
    }
}
