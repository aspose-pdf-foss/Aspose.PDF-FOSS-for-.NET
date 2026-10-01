using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

public sealed partial class TextReplacer
{
    /// <summary>Walk a content stream and collect every text-showing operator with the
    /// full state needed to match, measure, split, or re-anchor it: decoded text + raw
    /// bytes, byte span, text matrix + composed CTM, font state (dict/ToUnicode/size/Tc),
    /// TJ kern total, and the positioning-Tm operand span. Shared by the cross-operator
    /// replace and the run-move reflow.</summary>
    private List<CrossTextOp> CollectTextOps(byte[] streamBytes,
        Dictionary<string, PdfDictionary> fonts, PdfReader reader,
        double initCtmA = 1, double initCtmB = 0, double initCtmC = 0, double initCtmD = 1,
        double initCtmTx = 0, double initCtmTy = 0)
    {
        var ct = new CollectTextOpsState();
        ct.streamBytes = streamBytes;
        ct.fonts = fonts;
        ct.reader = reader;
        ct.initCtmA = initCtmA;
        ct.initCtmB = initCtmB;
        ct.initCtmC = initCtmC;
        ct.initCtmD = initCtmD;
        ct.initCtmTx = initCtmTx;
        ct.initCtmTy = initCtmTy;
        ct.textOps = new List<CrossTextOp>();
        ct.lexer2 = new PdfLexer(ct.streamBytes);
        ct.ops2 = new List<(TokenKind kind, PdfObject obj, int startPos, int endPos)>();
        ct.curToUnicode = null;
        ct.curFontDict = null;
        ct.curFontName = null;
        ct.curFontSize = 12;
        ct.curTc = 0;
        ct.curBtStart = -1;
        ct.tmA = 1;
        ct.tmB = 0;
        ct.tmC = 0;
        ct.tmD = 1;
        ct.tmTx = 0;
        ct.tmTy = 0;
        ct.tlLeading = 0;
        ct.tlmTx = 0;
        ct.tlmTy = 0;
        ct.ctmA = ct.initCtmA;
        ct.ctmB = ct.initCtmB;
        ct.ctmC = ct.initCtmC;
        ct.ctmD = ct.initCtmD;
        ct.ctmTx = ct.initCtmTx;
        ct.ctmTy = ct.initCtmTy;
        ct.ctmStack = new Stack<(double, double, double, double, double, double)>();
        ct.tsStack = new Stack<(double size, string? name, PdfDictionary? dict,
            Dictionary<int, string>? toUni, double tc, double tw, double leading)>();
        ct.curTw = 0;
        ct.pendingTm = (has: false, xStart: 0, xEnd: 0, xVal: 0.0);
        ct.pendingTd = (has: false, xStart: 0, xEnd: 0, xVal: 0.0);

        ct.metricsByDict = new Dictionary<PdfDictionary, FontMetrics?>();
        while (true)
        {
            if (!ScanTextOpToken(ct)) break;
        }
        return ct.textOps;
    }
}
