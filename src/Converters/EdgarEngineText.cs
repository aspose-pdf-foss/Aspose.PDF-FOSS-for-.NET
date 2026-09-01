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
        // Advance the cursor for a new line box; returns its baseline (td).
        double PlaceLineBox(double asc, double desc, double marginTop, double borderTop)
        {
            double top;
            if (_atPageTop)
            {
                // margins collapse into the body top only on the very first page;
                // pages after a forced break keep the pending margins.
                _margins.Add(marginTop);
                top = _y + (_dropTopMargins ? 0 : _margins.Max()) + borderTop;
                _atPageTop = false;
            }
            else
            {
                _margins.Add(marginTop);
                top = _y + _prevBorderBottom + _margins.Max() + borderTop;
            }
            _margins.Clear();
            var baseline = top + asc;
            _y = baseline + desc;

            return baseline;
        }

        bool Fits(double asc, double desc, double marginTop, double borderTop)
        {
            if (_atPageTop) return true;
            var top = _y + _prevBorderBottom + Math.Max(_margins.Count > 0 ? _margins.Max() : 0, marginTop) + borderTop;
            return top + asc + desc <= BottomLimit + 0.01;
        }

        void AddRun(Run r, double x, double baselineTd)
        {
            _pg.Runs.Add(new PlacedRun { X = x, BaselineTd = baselineTd, Run = r });
            if (r.AnchorsBefore is not null)
            {
                foreach (var aid in r.AnchorsBefore)
                {
                    var a = Anchors[aid];
                    if (a.PageIdx < 0)
                    {
                        a.PageIdx = _pages.Count - 1;
                        a.TopPdf = PageH - baselineTd;
                        _pg.AnchorPoints.Add((_annotSeq++, aid, 96, PageH - baselineTd));
                    }
                }
                r.AnchorsBefore = null;
            }
        }

        List<Run> CollectRuns(Node el, Style blockStyle, List<int>? pendingAnchors = null)
        {
            var runs = new List<Run>();
            pendingAnchors ??= new List<int>();
            Collect(el, blockStyle, runs, pendingAnchors, -1);
            // whitespace collapse across the whole paragraph
            CollapseWs(runs);
            return runs;
        }

        void Collect(Node n, Style st, List<Run> runs, List<int> pendingAnchors, int linkId)
        {
            foreach (var c in n.Children)
            {
                if (c.Tag == "")
                {
                    var text = DecodeEntities(c.Text);
                    if (text.Length == 0) continue;
                    var face = GetFace(st.Family, st.Bold, st.Italic);
                    if (face is null) continue;
                    var r = new Run
                    {
                        Text = text,
                        Face = face,
                        Size = st.Sup ? Math.Round(st.Size * 0.85, 2) : st.Size,
                        Color = st.Color,
                        Sup = st.Sup,
                        LinkId = linkId,
                    };
                    if (pendingAnchors.Count > 0 && text.Trim().Length > 0)
                    {
                        r.AnchorsBefore = new List<int>(pendingAnchors);
                        pendingAnchors.Clear();
                    }
                    runs.Add(r);
                    continue;
                }
                switch (c.Tag)
                {
                    case "b" or "strong":
                    {
                        var s2 = st.Clone(); s2.Bold = true;
                        ApplyStyleAttr(c.Attr("style"), s2);
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "i" or "em":
                    {
                        var s2 = st.Clone(); s2.Italic = true;
                        ApplyStyleAttr(c.Attr("style"), s2);
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "sup":
                    {
                        var s2 = st.Clone(); s2.Sup = true;
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "font":
                    {
                        var s2 = st.Clone();
                        var color = c.Attr("color");
                        if (color.Length > 0 && TryColor(color, out var cc)) s2.Color = cc;
                        var sizeAttr = c.Attr("size");
                        if (sizeAttr.Length > 0 && int.TryParse(sizeAttr, out var hsz))
                            s2.Size = hsz switch { 1 => 7.5, 2 => 10, 3 => 12, 4 => 13.5, 5 => 18, 6 => 24, 7 => 36, _ => s2.Size };
                        ApplyStyleAttr(c.Attr("style"), s2);
                        Collect(c, s2, runs, pendingAnchors, linkId);
                        break;
                    }
                    case "a":
                    {
                        var nameAttr = c.Attr("name");
                        if (nameAttr.Length > 0)
                        {
                            if (!AnchorIdx.ContainsKey(nameAttr))
                            {
                                Anchors.Add(new AnchorInfo { Name = nameAttr });
                                AnchorIdx[nameAttr] = Anchors.Count - 1;
                            }
                            pendingAnchors.Add(AnchorIdx[nameAttr]);
                        }
                        var href = c.Attr("href");
                        var lid = linkId;
                        if (href.StartsWith("#")) lid = GetLinkId(href.Substring(1));
                        Collect(c, st, runs, pendingAnchors, lid);
                        break;
                    }
                    case "br":
                        runs.Add(new Run { Text = "\n", Face = GetFace(st.Family, st.Bold, st.Italic)!, Size = st.Size });
                        break;
                    case "img":
                        // inline images: none in this dialect outside block paragraphs
                        break;
                    default:
                        Collect(c, st, runs, pendingAnchors, linkId);
                        break;
                }
            }
        }

        static void CollapseWs(List<Run> runs)
        {
            bool prevSpace = true; // leading whitespace collapses away
            foreach (var r in runs)
            {
                if (r.Text == "\n") { prevSpace = true; continue; }
                var sb = new StringBuilder(r.Text.Length);
                foreach (var ch in r.Text)
                {
                    if (ch is ' ' or '\t' or '\r' or '\n')
                    {
                        if (!prevSpace) sb.Append(' ');
                        prevSpace = true;
                    }
                    else
                    {
                        sb.Append(ch);
                        prevSpace = ch == ' ' ? false : false;
                    }
                }
                r.Text = sb.ToString();
            }
            // trailing whitespace of the block collapses away
            for (int i = runs.Count - 1; i >= 0; i--)
            {
                if (runs[i].Text == "\n") continue;
                runs[i].Text = runs[i].Text.TrimEnd(' ');
                if (runs[i].Text.Length > 0) break;
            }
            runs.RemoveAll(r => r.Text.Length == 0 && r.AnchorsBefore is null);
        }

        void LayoutParagraph(Node el, Style st, bool anonymous)
        {
            // block-level image paragraph?
            var imgs = el.Children.Where(c => c.Tag == "img").ToList();
            var runs = CollectRuns(el, st);
            bool hasText = runs.Any(r => r.Text.Trim(' ', ' ').Length > 0);
            if (imgs.Count > 0 && !hasText)
            {
                LayoutImageBlock(imgs[0], st);
                return;
            }

            if (runs.Count == 0 || runs.All(r => r.Text.Length == 0))
            {
                // empty paragraph: no line box, margins pass through (collapse)
                if (!anonymous)
                {
                    _margins.Add(st.MarginTop);
                    _margins.Add(st.MarginBottom);
                }
                return;
            }

            LayoutRuns(runs, st);
        }

        void LayoutRuns(List<Run> runs, Style st)
        {
            // dominant face/size for line metrics: the largest (size, then asc)
            var metricRun = runs.Where(r => r.Text.Length > 0).OrderByDescending(r => r.Size).First();
            var (pitch, asc, desc) = LineBox(metricRun.Face, metricRun.Size, st.LineHeight);

            var x0 = 96 + st.MarginLeft;
            var width = _contentW - st.MarginLeft;
            var firstIndent = st.TextIndent;

            // build word stream: (text, run) glyph clusters split at breakable spaces
            var lines = WrapRuns(runs, width, firstIndent);

            for (int li = 0; li < lines.Count; li++)
            {
                if (!Fits(asc, desc, li == 0 ? st.MarginTop : 0, li == 0 ? st.BorderTopW : 0))
                    BreakPage(explicitHeader: false);
                var baseline = PlaceLineBox(asc, desc, li == 0 ? st.MarginTop : 0, li == 0 ? st.BorderTopW : 0);
                double lineW = lines[li].Sum(p => p.W);
                double x = x0 + (li == 0 ? firstIndent : 0);
                if (st.Align == "center") x = x0 + (width - lineW) / 2;
                else if (st.Align == "right") x = x0 + width - lineW;

                foreach (var piece in lines[li])
                {
                    var r = piece.Run;
                    var supRaise = r.Sup ? 1.26 : 0;
                    AddRun(new Run { Text = piece.Text, Face = r.Face, Size = r.Size, Color = r.Color, Sup = r.Sup, LinkId = r.LinkId, AnchorsBefore = r.AnchorsBefore },
                        x, baseline - supRaise);
                    r.AnchorsBefore = null;
                    if (r.LinkId >= 0 && piece.Text.Trim(' ', (char)0xA0).Length > 0)
                        AddLinkRect(r.LinkId, x, baseline, x + piece.W, r.Face, r.Size);
                    x += piece.W;
                }
            }

            // border-bottom line under the block
            if (st.BorderBottomW > 0)
            {
                _pg.Rects.Add(new RectFill
                {
                    X = x0, TopTd = _y + st.BorderBottomW / 2, W = width, H = 0,
                    Color = st.BorderBottomColor, Stroke = true, LineW = st.BorderBottomW,
                });
            }
            EndBlock(st.MarginBottom, st.BorderBottomW);
        }

        List<List<Piece>> WrapRuns(List<Run> runs, double width, double firstIndent)
        {
            var lines = new List<List<Piece>>();
            var line = new List<Piece>();
            double lineW = 0;
            double avail = width - firstIndent;

            void EndLine()
            {
                if (line.Count > 0) lines.Add(line);
                line = new List<Piece>();
                lineW = 0;
                avail = width;
            }

            foreach (var run in runs)
            {
                if (run.Text == "\n") { EndLine(); continue; }
                int i = 0;
                var text = run.Text;
                while (i < text.Length)
                {
                    // segment = leading space + word (unbreakable incl. nbsp)
                    int j = i;
                    if (text[j] == ' ') j++;
                    while (j < text.Length && text[j] != ' ') j++;
                    var seg = text.Substring(i, j - i);
                    var segW = run.Face.Measure(seg, run.Size);
                    if (line.Count > 0 && lineW + segW > avail + 1e-6 && seg.Trim(' ').Length > 0)
                    {
                        EndLine();
                        var trimmed = seg.TrimStart(' ');
                        AddPiece(trimmed, run);
                    }
                    else
                    {
                        AddPiece(seg, run);
                    }
                    i = j;
                }
            }
            EndLine();
            return lines;

            void AddPiece(string s, Run run)
            {
                if (s.Length == 0) return;
                var w = run.Face.Measure(s, run.Size);
                if (line.Count > 0 && line[^1].Run == run)
                {
                    // merge; re-measure across the boundary for kerning continuity
                    var merged = line[^1].Text + s;
                    var mw = run.Face.Measure(merged, run.Size);
                    lineW += mw - line[^1].W;
                    line[^1].Text = merged;
                    line[^1].W = mw;
                }
                else
                {
                    var piece = new Piece { Text = s, W = w, Run = run };
                    line.Add(piece);
                    lineW += w;
                }
            }
        }

        Document Emit()
        {
            var doc = Document.Create();
            var ic = CultureInfo.InvariantCulture;
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

            // annotations in document order per page
            for (int pi = 0; pi < _pages.Count; pi++)
            {
                var pg = _pages[pi];
                var page = doc.Pages[pi + 1];
                var items = new List<(int order, System.Action emit)>();
                foreach (var (order, linkId, llx, lly, urx, ury) in pg.LinkRects)
                {
                    var link = Links[linkId];
                    items.Add((order, (System.Action)(() =>
                    {
                        if (!AnchorIdx.TryGetValue(link.TargetName, out var aid)) return;
                        var a = Anchors[aid];
                        if (a.PageIdx < 0) return;
                        var action = Annotations.PdfAction.CreateGoTo(a.PageIdx, 96.0, a.TopPdf, null);
                        page.Annotations.AddLinkAnnotation(new Rectangle(llx, lly, urx, ury), action);
                    })));
                }
                foreach (var (order, anchorId, x, y) in pg.AnchorPoints)
                {
                    items.Add((order, (System.Action)(() =>
                    {
                        var dict = new PdfDictionary();
                        dict.Set("Type", new PdfName("Annot"));
                        dict.Set("Subtype", new PdfName("Link"));
                        var rectArr = new PdfArray();
                        rectArr.Add(new PdfReal(x)); rectArr.Add(new PdfReal(y));
                        rectArr.Add(new PdfReal(x)); rectArr.Add(new PdfReal(y));
                        dict.Set("Rect", rectArr);
                        var border = new PdfArray();
                        border.Add(new PdfInteger(0)); border.Add(new PdfInteger(0)); border.Add(new PdfInteger(0));
                        dict.Set("Border", border);
                        page.Annotations.AddImportedDict(dict);
                    })));
                }
                foreach (var it in items.OrderBy(t => t.order)) it.emit();
            }

            return doc;
        }

        static string BuildKernedTj(Face face, string text, byte[] hex, double size)
        {
            // hex = 2 bytes per UTF-16 unit from the embedder; interleave kern moves
            var sb = new StringBuilder();
            sb.Append('[');
            var seg = new StringBuilder();
            void Flush()
            {
                if (seg.Length > 0) { sb.Append('<').Append(seg).Append('>'); seg.Clear(); }
            }
            for (int i = 0; i < text.Length && 2 * i + 1 < hex.Length; i++)
            {
                if (i > 0)
                {
                    var k = face.Parser.GetKernAdjustment(face.Gid(text[i - 1]), face.Gid(text[i]));
                    if (k != 0)
                    {
                        Flush();
                        var adj = -(k * 1000.0 / face.Upm);
                        sb.Append(' ').Append(((float)adj).ToString("0.######", CultureInfo.InvariantCulture)).Append(' ');
                    }
                }
                seg.Append(hex[2 * i].ToString("X2")).Append(hex[2 * i + 1].ToString("X2"));
            }
            Flush();
            sb.Append("] TJ\n");
            return sb.ToString();
        }

    }
}
