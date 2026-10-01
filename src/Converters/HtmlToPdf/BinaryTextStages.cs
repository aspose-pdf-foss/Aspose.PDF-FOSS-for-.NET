using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Emits the collected paragraphs line by line: each item at its advance, a new line when the width is spent.</summary>
    private static void EmitBinaryParagraphs(BinaryTextState bt)
    {
        foreach (var para in bt.paragraphs)
        {
            double x = bt.left;
            var lineHasContent = false;
            foreach (var (w, items) in para)
            {
                var isSpaceSep = items.Count == 1 && items[0].ch is null
                                 && Math.Abs(items[0].adv - 3.0) < 0.001;
                if (!lineHasContent && isSpaceSep)
                    continue;   // leading space at a line start is dropped
                if (lineHasContent && !isSpaceSep && x + w > bt.limit + 0.01)
                {
                    NextLine(bt);
                    x = bt.left;
                    lineHasContent = false;
                }
                // Emit the segment: visible glyph runs, split around invisible advances.
                var run = new StringBuilder();
                double runX = x;
                foreach (var (adv, ch) in items)
                {
                    if (ch is { } gch)
                    {
                        run.Append(gch);
                        x += adv;
                    }
                    else
                    {
                        EmitRun(bt, runX, bt.baseline, run.ToString());
                        run.Clear();
                        x += adv;
                        runX = x;
                    }
                }
                EmitRun(bt, runX, bt.baseline, run.ToString());
                if (!isSpaceSep) lineHasContent = true;
            }
            NextLine(bt);
        }
    }

    /// <summary>Walks the decoded text into items: glyph advances, segment and paragraph breaks at the binary layout's markers.</summary>
    private static void LayoutBinaryText(BinaryTextState bt)
    {
        for (var i = 0; i < bt.text.Length; i++)
        {
            var c = bt.text[i];
            if (c is ' ' or '\t' or '\r' or '\n')
            {
                EndSegment(bt);
                bt.pendingSpace = true;
                continue;
            }
            switch (c)
            {
                case '\f':
                case '\v':
                    EndParagraph(bt);
                    continue;
                case ' ':
                    AddItem(bt, 3.0, null);
                    continue;
                case '':
                    AddItem(bt, 6.0, null);
                    continue;
            }
            if (c < 0x20)
            {
                AddItem(bt, c == 0x1D ? 0.0 : 9.0, null);
                continue;
            }
            // Extra break opportunities: before '\', after '-'.
            if (c == '\\') EndSegment(bt);
            AddItem(bt, Adv(bt, c), c);
            if (c == '-') EndSegment(bt);
        }
    }
}
