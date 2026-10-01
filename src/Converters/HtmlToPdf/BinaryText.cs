using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>Render a NON-HTML binary file loaded through HtmlLoadOptions:
    /// HTML5-tokenize (a &lt;letter… tag swallows to the next '&gt;',
    /// an unterminated one swallows the rest), lay the remaining mojibake out as one
    /// anonymous Times New Roman 12 pt paragraph, and size the page to the min-content
    /// width: pageW = 90+6 + W + 90 where W is the widest unbreakable segment. Whitespace
    /// collapses to one space; FF/VT are forced line breaks; other C0 controls are
    /// invisible fixed advances (9 pt; GS 0, DEL 6, NBSP 3).</summary>
    private static Document? TryConvertBinaryText(string html)
    {
        var bt = new BinaryTextState();
        bt.html = html;
        var ttf = Text.SystemFontResolver.Resolve("Times New Roman");
        if (ttf is null) return null;
        bt.ttf = ttf;
        try { bt.gp = new Text.GlyphOutlineParser(bt.ttf); } catch { return null; }
        bt.upm = bt.gp.UnitsPerEm > 0 ? bt.gp.UnitsPerEm : 1000;
        bt.textBuf = new StringBuilder(bt.html.Length);
        for (var i = 0; i < bt.html.Length;)
        {
            var c = bt.html[i];
            if (c == '<' && i + 3 < bt.html.Length && bt.html[i + 1] == '!' && bt.html[i + 2] == '-' && bt.html[i + 3] == '-')
            {
                var e = bt.html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                i = e < 0 ? bt.html.Length : e + 3;
                continue;
            }
            if (c == '<' && i + 1 < bt.html.Length
                && (char.IsLetter(bt.html[i + 1]) || bt.html[i + 1] is '/' or '!' or '?'))
            {
                var e = bt.html.IndexOf('>', i + 1);
                i = e < 0 ? bt.html.Length : e + 1;
                continue;
            }
            bt.textBuf.Append(c);
            i++;
        }
        bt.text = bt.textBuf.ToString();

        bt.paragraphs = new List<List<(double w, List<(double adv, char? ch)> items)>>();
        bt.curPara = new List<(double, List<(double, char?)>)>();
        bt.curSeg = new List<(double adv, char? ch)>();
        bt.curSegW = 0;
        bt.pendingSpace = false;

        LayoutBinaryText(bt);
        EndParagraph(bt);

        bt.maxSeg = 0;
        foreach (var para in bt.paragraphs)
            foreach (var (w, _) in para)
                if (w > bt.maxSeg) bt.maxSeg = w;
        bt.left = 96.0;
        bt.right = 90.0;
        bt.pageW = Math.Max(595.0, bt.left + bt.maxSeg + bt.right);
        bt.pageH = 842.0;
        bt.limit = bt.left + Math.Max(bt.maxSeg, bt.pageW - bt.left - bt.right);

        bt.doc = Document.Create();
        bt.fontDict = new Core.PdfDictionary();
        bt.page = bt.doc.Pages.Add(bt.pageW, bt.pageH);
        EnsureFonts(bt.page, bt.fontDict);

        bt.invc = System.Globalization.CultureInfo.InvariantCulture;
        bt.baseline = 88.91;
        bt.sb = new StringBuilder();

        EmitBinaryParagraphs(bt);
        bt.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        return bt.doc;
    }
}
