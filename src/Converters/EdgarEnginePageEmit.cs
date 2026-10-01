using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
namespace Aspose.Pdf.Converters;

internal static partial class EdgarHtmlRenderer
{
    sealed partial class Engine
    {
        /// <summary>Each laid-out page becomes a document page: its runs, rules and images are written into the page's own content stream.</summary>
        private void EmitPagesToDocument(Document doc, CultureInfo ic)
        {
            static string F(double v) => ((double)(float)v).ToString("0.######", CultureInfo.InvariantCulture);
            foreach (var pg in _pages)
            {
                var page = doc.Pages.Add(_pageW, PageH);
                var fontDict = Table.ResolvePageFontDict(page);
                var sb = new StringBuilder();
                sb.Append("q\n1 0 0 -1 0 ").Append(F(PageH)).Append(" cm\n");
                // body background (white) over the page content box
                sb.Append("q\n1 1 1 rg\n");
                sb.Append(F(PageMargin)).Append(' ').Append(F(TopMargin)).Append(' ')
                  .Append(F(_pageW - 2 * PageMargin)).Append(' ').Append(F(PageH - 2 * TopMargin)).Append(" re\nf*\nQ\n");

                foreach (var rect in pg.Rects)
                {
                    double r = ((rect.Color >> 16) & 0xFF) / 255.0, g = ((rect.Color >> 8) & 0xFF) / 255.0, b = (rect.Color & 0xFF) / 255.0;
                    if (rect.Stroke)
                    {
                        sb.Append("q\n").Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" RG\n");
                        sb.Append(F(rect.LineW)).Append(" w\n");
                        sb.Append(F(rect.X)).Append(' ').Append(F(rect.TopTd)).Append(" m\n");
                        sb.Append(F(rect.X + rect.W)).Append(' ').Append(F(rect.TopTd)).Append(" l\nS\nQ\n");
                    }
                    else
                    {
                        sb.Append("q\n").Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" rg\n");
                        sb.Append(F(rect.X)).Append(' ').Append(F(rect.TopTd)).Append(' ')
                          .Append(F(rect.W)).Append(' ').Append(F(rect.H)).Append(" re\nf\nQ\n");
                    }
                }

                foreach (var run in pg.Runs)
                {
                    var text = run.Run.Text;
                    if (text.Length == 0) continue;
                    var face = run.Run.Face;
                    var (res, hex) = Text.Type0FontEmbedder.Embed(fontDict, face.Ttf, face.Display, text, stripSpacesInBaseFont: true);
                    double r = ((run.Run.Color >> 16) & 0xFF) / 255.0, g = ((run.Run.Color >> 8) & 0xFF) / 255.0, b = (run.Run.Color & 0xFF) / 255.0;
                    sb.Append("BT\n/").Append(res).Append(' ').Append(run.Run.Size.ToString("0.###", ic)).Append(" Tf\n");
                    sb.Append(F(r)).Append(' ').Append(F(g)).Append(' ').Append(F(b)).Append(" rg\n");
                    sb.Append("1 0 0 -1 ").Append(F(run.X)).Append(' ').Append(F(run.BaselineTd)).Append(" Tm\n");
                    sb.Append(BuildKernedTj(face, text, hex, run.Run.Size));
                    sb.Append("0 g\nET\n");
                }

                sb.Append("Q\n");
                page.AddContentStream(Encoding.ASCII.GetBytes(sb.ToString()));

                foreach (var img in pg.Images)
                {
                    try
                    {
                        page.AddImage(img.Data, new Rectangle(img.X, PageH - img.TopTd - img.H, img.X + img.W, PageH - img.TopTd));
                    }
                    catch { }
                }
            }
        }
    }
}
