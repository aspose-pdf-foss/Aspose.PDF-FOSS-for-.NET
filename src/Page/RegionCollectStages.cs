using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

public static partial class PageExtensions
{
// The stages of the region content collect: the path reset, the point transform, the intersecting emit and one operator.
    private static void ResetPath(RegionCollectState rc) { rc.path.Clear(); rc.started = false; }

    private static void Pt(RegionCollectState rc, double x, double y)
    {
        if (!rc.started) { rc.minX = rc.maxX = x; rc.minY = rc.maxY = y; rc.started = true; return; }
        if (x < rc.minX) rc.minX = x; if (y < rc.minY) rc.minY = y;
        if (x > rc.maxX) rc.maxX = x; if (y > rc.maxY) rc.maxY = y;
    }

    private static void EmitIfIntersecting(RegionCollectState rc, string paintOp)
    {
        if (rc.started)
        {
            // Project the user-space path bbox through the active CTM to page space.
            var (x1, y1) = rc.ctm.Apply(rc.minX, rc.minY);
            var (x2, y2) = rc.ctm.Apply(rc.maxX, rc.minY);
            var (x3, y3) = rc.ctm.Apply(rc.maxX, rc.maxY);
            var (x4, y4) = rc.ctm.Apply(rc.minX, rc.maxY);
            double pmnX = Math.Min(Math.Min(x1, x2), Math.Min(x3, x4));
            double pmnY = Math.Min(Math.Min(y1, y2), Math.Min(y3, y4));
            double pmxX = Math.Max(Math.Max(x1, x2), Math.Max(x3, x4));
            double pmxY = Math.Max(Math.Max(y1, y2), Math.Max(y3, y4));
            bool hit = pmnX <= rc.region.URX && pmxX >= rc.region.LLX
                    && pmnY <= rc.region.URY && pmxY >= rc.region.LLY;
            if (hit)
            {
                // Reproduce the path translated by (deltaX, deltaY) in page space:
                // page-point' = (p x CTM) x Translate. Emitting Translate then CTM
                // composes the effective matrix CTM x Translate from the page base.
                rc.dup.Append("q\n");
                rc.dup.Append("1 0 0 1 ").Append(F(rc.deltaX)).Append(' ').Append(F(rc.deltaY)).Append(" cm\n");
                rc.dup.Append(F(rc.ctm.A)).Append(' ').Append(F(rc.ctm.B)).Append(' ').Append(F(rc.ctm.C)).Append(' ')
                   .Append(F(rc.ctm.D)).Append(' ').Append(F(rc.ctm.E)).Append(' ').Append(F(rc.ctm.F)).Append(" cm\n");
                rc.gs.EmitInto(rc.dup);
                rc.dup.Append(rc.path);
                rc.dup.Append(paintOp).Append("\nQ\n");
            }
        }
        ResetPath(rc);
    }

    /// <summary></summary>
    private static bool CollectRegionOperator(RegionCollectState rc)
    {
        var t = rc.lexer.NextToken();
        if (t.Kind == TokenKind.Eof) return false;
        switch (t.Kind)
        {
            case TokenKind.Integer: rc.operands.Add(new PdfInteger(t.IntValue)); break;
            case TokenKind.Real: rc.operands.Add(new PdfReal(t.RealValue)); break;
            case TokenKind.LiteralString: rc.operands.Add(new PdfString(t.BytesValue!)); break;
            case TokenKind.HexString: rc.operands.Add(new PdfString(t.BytesValue!, isHex: true)); break;
            case TokenKind.Name: rc.operands.Add(new PdfName(t.StringValue!)); break;
            case TokenKind.ArrayStart: rc.operands.Add(ParseArray(rc.lexer)); break;
            case TokenKind.Keyword:
            {
                var op = t.StringValue!;
                switch (op)
                {
                    case "q":
                        rc.ctmStack.Push(rc.ctm); rc.gsStack.Push(rc.gs.Clone()); break;
                    case "Q":
                        if (rc.ctmStack.Count > 0) rc.ctm = rc.ctmStack.Pop();
                        if (rc.gsStack.Count > 0) rc.gs = rc.gsStack.Pop();
                        break;
                    case "cm" when rc.operands.Count >= 6:
                        rc.ctm = new Mat(Num(rc.operands[0]), Num(rc.operands[1]), Num(rc.operands[2]),
                                      Num(rc.operands[3]), Num(rc.operands[4]), Num(rc.operands[5])).Multiply(rc.ctm);
                        break;

                    case "BT": rc.inText = true; ResetPath(rc); break;
                    case "ET": rc.inText = false; break;

                    // Graphics-state setters — remember the latest of each kind.
                    case "w": rc.gs.Width = OpLine(rc.operands, op); break;
                    case "J": rc.gs.Cap = OpLine(rc.operands, op); break;
                    case "j": rc.gs.Join = OpLine(rc.operands, op); break;
                    case "M": rc.gs.Miter = OpLine(rc.operands, op); break;
                    case "d": rc.gs.Dash = OpLine(rc.operands, op); break;
                    case "CS": rc.gs.StrokeCs = OpLine(rc.operands, op); break;
                    case "cs": rc.gs.FillCs = OpLine(rc.operands, op); break;
                    case "G" or "RG" or "K" or "SC" or "SCN": rc.gs.StrokeColor = OpLine(rc.operands, op); break;
                    case "g" or "rg" or "k" or "sc" or "scn": rc.gs.FillColor = OpLine(rc.operands, op); break;

                    // Path construction (ignore inside text objects).
                    case "m" when !rc.inText && rc.operands.Count >= 2:
                        rc.curX = Num(rc.operands[0]); rc.curY = Num(rc.operands[1]); Pt(rc, rc.curX, rc.curY);
                        rc.path.Append(OpLine(rc.operands, op)).Append('\n'); break;
                    case "l" when !rc.inText && rc.operands.Count >= 2:
                        rc.curX = Num(rc.operands[0]); rc.curY = Num(rc.operands[1]); Pt(rc, rc.curX, rc.curY);
                        rc.path.Append(OpLine(rc.operands, op)).Append('\n'); break;
                    case "c" when !rc.inText && rc.operands.Count >= 6:
                        Pt(rc, Num(rc.operands[0]), Num(rc.operands[1])); Pt(rc, Num(rc.operands[2]), Num(rc.operands[3]));
                        rc.curX = Num(rc.operands[4]); rc.curY = Num(rc.operands[5]); Pt(rc, rc.curX, rc.curY);
                        rc.path.Append(OpLine(rc.operands, op)).Append('\n'); break;
                    case "v" when !rc.inText && rc.operands.Count >= 4:
                        Pt(rc, rc.curX, rc.curY); Pt(rc, Num(rc.operands[0]), Num(rc.operands[1]));
                        rc.curX = Num(rc.operands[2]); rc.curY = Num(rc.operands[3]); Pt(rc, rc.curX, rc.curY);
                        rc.path.Append(OpLine(rc.operands, op)).Append('\n'); break;
                    case "y" when !rc.inText && rc.operands.Count >= 4:
                        Pt(rc, Num(rc.operands[0]), Num(rc.operands[1]));
                        rc.curX = Num(rc.operands[2]); rc.curY = Num(rc.operands[3]); Pt(rc, rc.curX, rc.curY);
                        rc.path.Append(OpLine(rc.operands, op)).Append('\n'); break;
                    case "re" when !rc.inText && rc.operands.Count >= 4:
                    {
                        double x = Num(rc.operands[0]), y = Num(rc.operands[1]),
                               w = Num(rc.operands[2]), h = Num(rc.operands[3]);
                        Pt(rc, x, y); Pt(rc, x + w, y + h); rc.curX = x; rc.curY = y;
                        rc.path.Append(OpLine(rc.operands, op)).Append('\n'); break;
                    }
                    case "h" when !rc.inText:
                        rc.path.Append("h\n"); break;
                    case "W" or "W*" when !rc.inText:
                        // Keep clip operators inside the duplicated path so the copy
                        // clips itself the same way; never duplicate a clip-only path.
                        rc.path.Append(op).Append('\n'); break;

                    // Painting operators end the path.
                    case "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*":
                        EmitIfIntersecting(rc, op); break;
                    case "n":
                        ResetPath(rc); break;
                }
                rc.operands.Clear();
                break;
            }
            default:
                rc.operands.Clear();
                break;
        }
        return true;
    }
}
