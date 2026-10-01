using System.Collections.Generic;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Per-call working state of the letter-preview render. One instance per
    /// invocation; never shared.</summary>
    private sealed class LetterPreviewState
    {
        public System.Globalization.CultureInfo invc = null!;
        public string html = null!;
        public Document doc = null!;
        public Page page = null!;
        public System.Text.StringBuilder sb = null!;
        public (double asc, double sum) tm;      // Tahoma's OS/2 win metrics
        public double pageWidth, pageHeight;
        public double marginL, marginR, marginT, marginB;
        public double contentL, contentR;        // the letter table's box (the sheet's content box)
        public double innerL, innerR;            // div#content's content box
        public double y;                         // the flow cursor, from the page top
        public double limit;                     // the sheet's bottom edge a block must end above
        public bool firstPage;
        public List<(string text, double pct)> thead = new();
        public string footer = "";
        public List<LpItem> items = new();
    }

    private enum LpKind { HeaderFloat, Br, Hr, CenterDiv, Paragraph, Table, TtoDiv }

    /// <summary>One child of div#content in source order.</summary>
    private sealed class LpItem
    {
        public LpKind Kind;
        public string Text = "";                 // header float / centred div / paragraph title
        public double Fs;                        // the item's own font size
        public bool Bold;
        public double RightInset;                // a header float's right edge inset from the inner box
        public double MarginTop, Pad;            // a centred div's own margin/padding
        public List<string> Segments = new();    // a paragraph's lines after the title (br-separated)
        public bool TitleAlone;                  // the title stands on its own line (a br follows it)
        public LpTable? Table;
        public string H2 = "";                   // the TTO block's heading
        public string Authorise = "";            // …and its closing note
    }

    /// <summary>A parsed table: rows of cells, the solved column widths and the laid-out rows.</summary>
    private sealed class LpTable
    {
        public string Id = "";
        public int Depth;                        // the em chain depth of the table element
        public bool Fixed;                       // table-layout: fixed (the TTO grid)
        public bool Bordered;                    // collapsed 1px cell borders (the TTO grid)
        public bool Inner;                       // a table nested in a letter table's cell
        public bool BlockThead;                  // the GP-details thead displayed as a block
        public double Spacing;                   // border-spacing between cells
        public double PctWidth = 1;              // width: N% of the containing box
        public List<List<LpCell>> Rows = new();
        public int Cols;
        public double[] ColW = System.Array.Empty<double>();
        public double X, W;
        public double[] RowH = System.Array.Empty<double>();
        public double Height;
        public double MinW, MaxW;                // the columns' min- and max-content sums, spacings included
    }

    private sealed class LpCell
    {
        public bool Th;
        public int Col, ColSpan = 1, RowSpan = 1;
        public double? Pct;                      // a declared percent width
        public bool NoWrap, Top;                 // nowrap; vertical-align top
        public bool Bold, Left;                  // inline weight; text-align left
        public double FsScale = 1;               // an inline em font size against the parent
        public bool PadLeftZero;                 // an inline padding-left: 0
        public LpTable? Inner;                   // a nested table as the whole content
        public List<string> Segments = new();    // br-separated text otherwise
        public double PadT, PadR, PadB, PadL;
        public double Fs;
        public double MinW, MaxW;                // min- and max-content widths, inset included
        public List<LpLine> Lines = new();
        public double ContentH;                  // lines (or the inner table) height
    }

    /// <summary>A laid-out line: its runs, the metric line box and the baseline offset.</summary>
    private sealed class LpLine
    {
        public List<LpRun> Runs = new();
        public double Width, Height, Above;
    }

    private sealed class LpRun
    {
        public string Face = "";
        public double Fs;
        public string Text = "";
        public double Width;
        public bool Underline;
    }
}
