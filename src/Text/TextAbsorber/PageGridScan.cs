using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextAbsorber
{
// The page-grid content scan: per-operator glyph widths, sizes and positions over a content stream and its form XObjects.
    // Scan one content stream, accumulating glyph advances and font-size
    // populations into the shared maps above. `recurse` gates descent into
    // Form XObjects (Do) so the extra measurement only kicks in for pages
    // whose direct content stream carries too little text — see the
    // two-pass invocation after the definition. `fonts`/`resDict` are the
    // font and resource dictionaries in scope for THIS stream (a form
    // supplies its own), and icm* is the CTM in effect at the stream's
    // start (identity for page content, the CTM at the Do for a form —
    // the form's own /Matrix is ignored, matching the extraction loop).
    private static void Scan(PageGridState pg, PdfReader reader, double[]? bounds, byte[] streamBytes, Dictionary<string, PdfDictionary> fonts,
        PdfDictionary resDict, double icmA, double icmB, double icmC,
        double icmD, double icmE, double icmF, int rdepth, bool recurse)
    {
        var gs = new GridScanState();
        gs.reader = reader;
        gs.bounds = bounds;
        gs.fonts = fonts;
        gs.resDict = resDict;
        gs.rdepth = rdepth;
        gs.recurse = recurse;
        gs.lexer = new PdfLexer(streamBytes);
        gs.operands = new List<PdfObject>();
        gs.metrics = null;
        gs.tlmX = 0;
        gs.cmTx = icmE;
        gs.tlmY = icmF;
        gs.preTL = 0;
        gs.tmScaleX = 1.0;
        gs.fsScale = 1.0;
        gs.preHorizScale = 1.0;
        gs.preTc = 0;
        gs.preTw = 0;
        gs.preRot = false;
        gs.cmA = icmA;
        gs.cmB = icmB;
        gs.cmC = icmC;
        gs.cmD = icmD;
        gs.cmE = icmE;
        gs.cmF = icmF;
        gs.cmFullStack = new Stack<(double a, double b, double c, double d, double e, double f)>();
        gs.cmStack = new Stack<double>();
        while (true)
        {
            var tok = gs.lexer.NextToken();
            if (tok.Kind == TokenKind.Eof) break;
            switch (tok.Kind)
            {
                case TokenKind.Integer: gs.operands.Add(new Core.PdfInteger(tok.IntValue)); break;
                case TokenKind.Real: gs.operands.Add(new Core.PdfReal(tok.RealValue)); break;
                case TokenKind.LiteralString: gs.operands.Add(new Core.PdfString(tok.BytesValue!)); break;
                case TokenKind.HexString: gs.operands.Add(new Core.PdfString(tok.BytesValue!, isHex: true)); break;
                case TokenKind.Name: gs.operands.Add(new Core.PdfName(tok.StringValue!)); break;
                case TokenKind.ArrayStart: gs.operands.Add(ParseContentArray(gs.lexer)); break;
                case TokenKind.Keyword:
                    var op = tok.StringValue!;
                    ScanOperator(gs, pg, op);
                    gs.operands.Clear();
                    break;
                default: gs.operands.Clear(); break;
            }
        }
    }
}
