using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;
using LS = Aspose.Pdf.LogicalStructure;

namespace Aspose.Pdf.Tagged;

/// <summary>
/// Renders the visible page content for a document authored purely through
/// <see cref="ITaggedContent"/> (headers, paragraphs, spans, lists, tables,
/// figures and links). A from-scratch tagged document starts with no pages;
/// without a render pass it would save with an empty page canvas even though
/// the logical structure carries the content.
///
/// The layout model:
///   - A4 page (595×842) with content area x ∈ [90, 505] and top y = 770.
///   - A text line of size s in a font with descent ratio d (the FontDescriptor
///     /Descent ÷ 1000) puts its baseline at flowTop − (1−d)·s and advances the
///     flow by exactly s; the clip box around a line is 1.16·s tall with its
///     bottom at baseline − d·s.
///   - Default fonts: Times New Roman 12 for body text, Arial Bold for headers
///     (H = 16 pt, H1 = 18 pt) filled 0.3 0.3 0.3.
///   - Default block margins: P = 2 top / 2 bottom, leveled headers = size/2
///     top and bottom, everything else 0. An explicit margin (AdjustPosition /
///     StructureTextState.MarginInfo) replaces the block's defaults. Adjacent
///     block margins add (no collapsing).
///   - Tables: fixed 100 pt columns starting at the content left + margin,
///     1 em row pitch, no cell padding; a cell-level margin shifts the cell
///     inside its column. Figures: explicit size, or pixels × 72/300 (300 dpi
///     is assumed regardless of the image's own density), or
///     pixels × 72/resolution when SetImage(path, resolution) was used.
///   - Spans flow as blocks unless PositionSettings.IsInLineParagraph continues
///     the current line at the pen position (+ own left margin). List roles
///     (L/LI/Lbl/LBody) are transparent; the leaf spans position everything.
///
/// Every rendered leaf is wrapped in marked content (/Tag &lt;&lt;/MCID n&gt;&gt; BDC …
/// EMC) and the structure tree is wired to it on the way out: elements become
/// indirect objects with /P and /Pg links, leaves carry their MCIDs in /K,
/// pages get /StructParents, links get a Link annotation + OBJR, and the
/// StructTreeRoot receives the /ParentTree + /ParentTreeNextKey.
/// </summary>
internal static class TaggedContentRenderer
{
    /// <summary>Try to lay out and render the authored structure tree onto
    /// pages. Never throws — on any failure the document is left as-is (an
    /// empty canvas), preserving the previous behaviour.</summary>
    internal static void TryRender(Document document, LS.StructureElement root)
    {
        try
        {
            new Engine(document).Render(root);
        }
        catch
        {
            // Rendering is best-effort; the structure tree is already linked.
        }
    }

    private sealed class Engine
    {
        private const double PageW = 595.0;
        private const double PageH = 842.0;
        private const double ContentLeft = 90.0;
        private const double ContentRight = 505.0;
        private const double ContentTop = 770.0;
        private const double ContentBottom = 72.0;
        private const double LineBox = 1.16;      // clip-box height in em
        private const double DefaultColumnWidth = 100.0;

        private readonly Document _doc;

        // One /Font resource dictionary shared by every rendered page, so the
        // Type0 embedder reuses a single embedded program per face.
        private readonly PdfDictionary _sharedFontDict = new();

        private FontData _bodyFont = null!;    // Times New Roman
        private FontData _headerFont = null!;  // Arial Bold

        // ── per-page state ────────────────────────────────────────────
        private Page? _page;
        private ContentStreamBuilder? _cs;
        private int _nextMcid;
        private List<(int Mcid, LS.StructureElement El)> _mcids = new();
        private readonly List<(Page Page, List<(int Mcid, LS.StructureElement El)> Mcids)> _renderedPages = new();
        private bool _pageHasContent;

        // ── flow state ────────────────────────────────────────────────
        private double _flowTop;
        private bool _lineOpen;
        private double _lineBaseline;
        private double _lineSize;
        private double _penX;
        private double _lineBottomMargin;

        // Link elements that need an annotation, with the rect of their text.
        private readonly List<(LS.LinkElement El, Page Page, double LLX, double LLY, double URX, double URY)> _links = new();

        private readonly Dictionary<byte[], double> _descentCache = new(ReferenceEqualityComparer.Instance);

        internal Engine(Document doc) => _doc = doc;

        internal void Render(LS.StructureElement root)
        {
            var body = FontRepository.FindFont("Times New Roman")?.SourceFontData;
            var header = ResolveStyled("Arial", FontStyles.Bold)
                         ?? FontRepository.FindFont("Arial")?.SourceFontData;
            if (body?.TtfData is null || header?.TtfData is null) return;
            _bodyFont = body;
            _headerFont = header;

            WalkChildren(root);
            CloseLine();
            FlushPage();
            if (_renderedPages.Count == 0) return;

            WireStructure(root);
        }

        // ── tree walk ─────────────────────────────────────────────────

        private void WalkChildren(LS.StructureElement parent)
        {
            foreach (var child in parent.ChildElements)
            {
                switch (child)
                {
                    case LS.HeaderElement h:
                        RenderHeader(h);
                        break;
                    case LS.ParagraphElement p:
                        RenderParagraph(p);
                        break;
                    case LS.TableElement t:
                        RenderTable(t);
                        break;
                    case LS.IllustrationElement fig:
                        RenderFigure(fig);
                        break;
                    case LS.LinkElement link:
                        RenderLink(link);
                        break;
                    default:
                        if (!string.IsNullOrEmpty(child.ActualText))
                            RenderFlowSpan(child);
                        else
                            WalkChildren(child); // transparent container (L/LI/Lbl/LBody/Part/Sect/Div…)
                        break;
                }
            }
        }

        // ── page / line management ────────────────────────────────────

        private void EnsurePage()
        {
            if (_page is null) NewPage();
        }

        private void NewPage()
        {
            CloseLine();
            FlushPage();
            _page = _doc.Pages.Add(PageW, PageH);
            EnsurePageFontResources(_page);
            _cs = new ContentStreamBuilder();
            _nextMcid = 0;
            _mcids = new List<(int, LS.StructureElement)>();
            _flowTop = ContentTop;
            _pageHasContent = false;
        }

        private void FlushPage()
        {
            if (_page is null || _cs is null) return;
            _page.AddContentStream(_cs.Build());
            _renderedPages.Add((_page, _mcids));
            _page = null;
            _cs = null;
        }

        private void EnsurePageFontResources(Page page)
        {
            var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
            if (resources is null)
            {
                resources = new PdfDictionary();
                page.Dict.Set("Resources", resources);
            }
            resources.Set("Font", _sharedFontDict);
        }

        private void BlockStart(bool isInNewPage)
        {
            CloseLine();
            if (isInNewPage && _page is not null && _pageHasContent)
                NewPage();
            EnsurePage();
        }

        /// <summary>Close the open text line: advance the flow past it plus any
        /// bottom margins its inline spans carried.</summary>
        private void CloseLine()
        {
            if (!_lineOpen) return;
            _flowTop -= _lineSize + _lineBottomMargin;
            _lineOpen = false;
            _lineBottomMargin = 0;
        }

        /// <summary>Break the page when a box of <paramref name="height"/> won't
        /// fit above the bottom margin (only once the page has content).</summary>
        private void PageBreakIfNeeded(double height)
        {
            if (_page is null) { EnsurePage(); return; }
            if (_pageHasContent && _flowTop - height < ContentBottom)
                NewPage();
        }

        // ── shared helpers ────────────────────────────────────────────

        private static FontData? ResolveStyled(string family, FontStyles style)
        {
            try
            {
                if (style == 0) return FontRepository.FindFont(family)?.SourceFontData;
                return FontRepository.FindFont(family, style)?.SourceFontData;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Font for an element's inline text: its own StructureTextState
        /// (font/style), else the supplied base, else Times New Roman. Bold/italic
        /// picks the styled family variant so the glyph metrics track the face.</summary>
        private (FontData Font, float Size) ResolveRunFont(LS.StructureElement el, FontData? baseFont, float baseSize)
        {
            var st = el.StructureTextState;
            var font = st.Font?.SourceFontData ?? baseFont ?? _bodyFont;
            var size = st.FontSize > 0 ? st.FontSize : baseSize;
            var bold = (st.FontStyle & FontStyles.Bold) != 0;
            var italic = (st.FontStyle & FontStyles.Italic) != 0;
            if ((bold || italic) && font.FontName is { } name)
            {
                var styled = ResolveStyled(name, (bold ? FontStyles.Bold : 0) | (italic ? FontStyles.Italic : 0));
                if (styled?.TtfData is not null) font = styled;
            }
            if (font.TtfData is null) font = _bodyFont;
            return (font, size);
        }

        /// <summary>Baseline geometry uses the FontDescriptor
        /// /Descent value: round(hhea descent · 1000 / unitsPerEm) / 1000.</summary>
        private double DescentRatio(FontData font)
        {
            var ttf = font.TtfData!;
            if (_descentCache.TryGetValue(ttf, out var cached)) return cached;
            double ratio = 0.216;
            try
            {
                var parser = new TrueTypeParser(ttf);
                parser.Parse();
                if (parser.UnitsPerEm > 0 && parser.Descent != 0)
                    ratio = Math.Round(Math.Abs((double)parser.Descent) * 1000.0 / parser.UnitsPerEm) / 1000.0;
            }
            catch { }
            _descentCache[ttf] = ratio;
            return ratio;
        }

        private double Measure(FontData font, float size, string text)
            => Type0FontEmbedder.MeasureText(_sharedFontDict, font.TtfData!, font.FontName ?? "Font", text, size);

        /// <summary>Emit one positioned text chunk (BT…ET) in the current
        /// marked-content/clip context.</summary>
        private void EmitTextChunk(FontData font, float size, double x, double baseline,
            double r, double g, double b, string text)
        {
            var (resName, hex) = Type0FontEmbedder.Embed(
                _sharedFontDict, font.TtfData!, font.FontName ?? "Font", text);
            var cs = _cs!;
            cs.BeginText();
            cs.SetFont(resName, size);
            cs.SetFillColor(r, g, b);
            cs.SetTextMatrix(1, 0, 0, 1, x, baseline);
            cs.ShowTextHex(hex);
            cs.SetFillGray(0);
            cs.EndText();
        }

        private void RecordMcid(int mcid, LS.StructureElement el) => _mcids.Add((mcid, el));

        /// <summary>Resolve an element's layout request: explicit margin (which
        /// replaces the block's defaults), page break and inline flags.</summary>
        private static (MarginInfo? Margin, bool NewPage, bool Inline) Position(LS.StructureElement el)
            => (el._positionMargin ?? el.StructureTextState.MarginInfo, el._posInNewPage, el._posInline);

        private static string BlockText(LS.StructureElement block)
        {
            if (!string.IsNullOrEmpty(block.ActualText)) return block.ActualText;
            var sb = new System.Text.StringBuilder();
            foreach (var child in block.ChildElements)
                if (!string.IsNullOrEmpty(child.ActualText))
                    sb.Append(child.ActualText);
            return sb.ToString();
        }

        // ── headers ───────────────────────────────────────────────────

        private static int HeaderLevel(LS.StructureElement header)
        {
            var s = header.StructureType; // "H1".."H6" or "H"
            if (s.Length == 2 && s[0] == 'H' && s[1] >= '1' && s[1] <= '6')
                return s[1] - '0';
            return 0; // unleveled "H"
        }

        private void RenderHeader(LS.HeaderElement header)
        {
            var text = BlockText(header);
            if (string.IsNullOrEmpty(text)) return;

            int level = HeaderLevel(header);
            double size = level switch { 0 => 16, 1 => 18, 2 => 16, 3 => 14, 4 => 12, 5 => 11, _ => 10 };
            // Leveled headers carry a default size/2 margin above and below;
            // the unleveled "H" has none. Explicit margins replace the defaults.
            var (margin, newPage, _) = Position(header);
            double mTop = margin?.Top ?? (level > 0 ? size / 2 : 0);
            double mBottom = margin?.Bottom ?? (level > 0 ? size / 2 : 0);
            double mLeft = margin?.Left ?? 0;

            BlockStart(newPage);
            _flowTop -= mTop;
            PageBreakIfNeeded(size);

            double d = DescentRatio(_headerFont);
            double x = ContentLeft + mLeft;
            double baseline = _flowTop - (1 - d) * size;
            var mcid = _nextMcid++;

            var cs = _cs!;
            cs.SaveState();
            cs.BeginMarkedContent(header.StructureType, mcid);
            cs.SaveState();
            cs.Rectangle(x, baseline - d * size, ContentRight - x, LineBox * size);
            cs.Clip();
            EmitTextChunk(_headerFont, (float)size, x, baseline, 0.3, 0.3, 0.3, text);
            cs.RestoreState();
            cs.EndMarkedContent();
            cs.RestoreState();

            RecordMcid(mcid, header);
            _pageHasContent = true;
            _flowTop -= size + mBottom;
        }

        // ── paragraphs (with inline runs + wrapping) ──────────────────

        private readonly record struct Run(LS.StructureElement El, string Text, FontData Font, float Size);
        private readonly record struct Chunk(int RunIdx, double X, double Baseline, string Text);

        private void RenderParagraph(LS.StructureElement para)
        {
            var baseFont = para.StructureTextState.Font?.SourceFontData ?? _bodyFont;
            if (baseFont.TtfData is null) baseFont = _bodyFont;
            float baseSize = para.StructureTextState.FontSize > 0 ? para.StructureTextState.FontSize : 12f;

            // Inline runs: the paragraph's children (spans/quotes), or the
            // paragraph's own text.
            var runs = new List<Run>();
            if (para.ChildElements.Count > 0)
            {
                foreach (var child in para.ChildElements)
                {
                    var t = child.ActualText;
                    if (string.IsNullOrEmpty(t)) continue;
                    var (font, size) = ResolveRunFont(child, baseFont, baseSize);
                    runs.Add(new Run(child, t, font, size));
                }
            }
            else if (!string.IsNullOrEmpty(para.ActualText))
            {
                runs.Add(new Run(para, para.ActualText, baseFont, baseSize));
            }
            if (runs.Count == 0) return;

            // Default paragraph margins are 2 pt above and below; an explicit
            // margin replaces them.
            var (margin, newPage, _) = Position(para);
            double mTop = margin?.Top ?? 2;
            double mBottom = margin?.Bottom ?? 2;
            double left = ContentLeft + (margin?.Left ?? 0);
            double right = ContentRight - (margin?.Right ?? 0);
            if (right <= left + 10) right = left + 10;

            BlockStart(newPage);
            _flowTop -= mTop;
            PageBreakIfNeeded(baseSize);

            double d = DescentRatio(baseFont);
            double pitch = baseSize; // 1 em leading
            double baseline = _flowTop - (1 - d) * baseSize;

            // Greedy wrap with eager spaces: a space is emitted while it fits,
            // a word that doesn't fit opens the next line (the separator space
            // is consumed by the break).
            var chunks = new List<Chunk>();
            int lineCount = 1;
            double pen = left;
            var sb = new System.Text.StringBuilder();
            int sbRun = -1;
            double sbX = left;

            void FlushChunk()
            {
                if (sbRun >= 0 && sb.Length > 0)
                    chunks.Add(new Chunk(sbRun, sbX, baseline, sb.ToString()));
                sb.Clear();
            }

            for (var r = 0; r < runs.Count; r++)
            {
                var run = runs[r];
                FlushChunk();
                sbRun = r;
                sbX = pen;
                var text = run.Text;
                int i = 0;
                while (i < text.Length)
                {
                    if (text[i] == ' ')
                    {
                        double spaceW = Measure(run.Font, run.Size, " ");
                        if (pen + spaceW <= right + 0.01)
                        {
                            sb.Append(' ');
                            pen += spaceW;
                        }
                        else
                        {
                            FlushChunk();
                            baseline -= pitch;
                            lineCount++;
                            pen = left;
                            sbX = left;
                        }
                        i++;
                        continue;
                    }
                    int end = text.IndexOf(' ', i);
                    if (end < 0) end = text.Length;
                    var word = text.Substring(i, end - i);
                    double wordW = Measure(run.Font, run.Size, word);
                    if (pen > left + 0.01 && pen + wordW > right + 0.01)
                    {
                        FlushChunk();
                        baseline -= pitch;
                        lineCount++;
                        pen = left;
                        sbX = left;
                    }
                    sb.Append(word);
                    pen += wordW;
                    i = end;
                }
            }
            FlushChunk();

            // Emit: outer q; first run's BDC; one whole-block clip; each run's
            // chunks inside its own BDC; the clip closes before the last EMC.
            double lastBaseline = baseline;
            double clipBottom = lastBaseline - d * baseSize;
            double clipHeight = lineCount * LineBox * baseSize;

            var cs = _cs!;
            cs.SaveState();
            int currentRun = -1;
            bool clipEmitted = false;
            foreach (var chunk in chunks)
            {
                if (chunk.RunIdx != currentRun)
                {
                    if (currentRun >= 0) cs.EndMarkedContent();
                    currentRun = chunk.RunIdx;
                    var el = runs[currentRun].El;
                    var mcid = _nextMcid++;
                    cs.BeginMarkedContent(el.StructureType, mcid);
                    RecordMcid(mcid, el);
                    if (!clipEmitted)
                    {
                        cs.SaveState();
                        cs.Rectangle(left, clipBottom, ContentRight - left, clipHeight);
                        cs.Clip();
                        clipEmitted = true;
                    }
                }
                var run = runs[chunk.RunIdx];
                EmitTextChunk(run.Font, run.Size, chunk.X, chunk.Baseline, 0, 0, 0, chunk.Text);
            }
            if (clipEmitted) cs.RestoreState();
            if (currentRun >= 0) cs.EndMarkedContent();
            cs.RestoreState();

            _pageHasContent = true;
            _flowTop -= lineCount * pitch + mBottom;
        }

        // ── flowing spans (list content, standalone spans) ────────────

        private void RenderFlowSpan(LS.StructureElement el)
        {
            var text = el.ActualText;
            if (string.IsNullOrEmpty(text)) return;

            var (margin, newPage, inline) = Position(el);
            var (font, size) = ResolveRunFont(el, null, 12f);
            double d = DescentRatio(font);

            double x;
            if (inline && _lineOpen)
            {
                x = _penX + (margin?.Left ?? 0);
            }
            else
            {
                CloseLine();
                if (newPage && _page is not null && _pageHasContent) NewPage();
                EnsurePage();
                _flowTop -= margin?.Top ?? 0;
                PageBreakIfNeeded(size);
                x = ContentLeft + (margin?.Left ?? 0);
                _lineBaseline = _flowTop - (1 - d) * size;
                _lineSize = size;
                _lineOpen = true;
            }

            var mcid = _nextMcid++;
            var cs = _cs!;
            cs.SaveState();
            cs.BeginMarkedContent(el.StructureType, mcid);
            cs.SaveState();
            cs.Rectangle(x, _lineBaseline - d * size, ContentRight - x, LineBox * size);
            cs.Clip();
            EmitTextChunk(font, size, x, _lineBaseline, 0, 0, 0, text);
            cs.RestoreState();
            cs.EndMarkedContent();
            cs.RestoreState();

            RecordMcid(mcid, el);
            _pageHasContent = true;
            _penX = x + Measure(font, size, text);
            _lineBottomMargin += margin?.Bottom ?? 0;
        }

        // ── links ─────────────────────────────────────────────────────

        private void RenderLink(LS.LinkElement link)
        {
            var text = BlockText(link);
            if (string.IsNullOrEmpty(text)) return;

            var (margin, newPage, _) = Position(link);
            var (font, size) = ResolveRunFont(link, null, 12f);
            double d = DescentRatio(font);

            BlockStart(newPage);
            _flowTop -= margin?.Top ?? 0;
            PageBreakIfNeeded(size);

            double x = ContentLeft + (margin?.Left ?? 0);
            double baseline = _flowTop - (1 - d) * size;
            double width = Measure(font, size, text);
            var mcid = _nextMcid++;

            var cs = _cs!;
            cs.SaveState();
            cs.BeginMarkedContent(link.StructureType, mcid);
            // Underline bar: text-width wide, size/20 thick, its bottom at
            // baseline − 0.9·descent.
            cs.SaveState();
            cs.SetFillColor(0, 0, 1);
            cs.SetMatrix(1, 0, 0, 1, x, baseline - 0.9 * d * size);
            cs.Rectangle(0, 0, width, size / 20.0);
            cs.FillEvenOdd();
            cs.RestoreState();
            cs.SaveState();
            cs.Rectangle(x, baseline - d * size, ContentRight - x, LineBox * size);
            cs.Clip();
            EmitTextChunk(font, size, x, baseline, 0, 0, 1, text);
            cs.RestoreState();
            cs.EndMarkedContent();
            cs.RestoreState();

            RecordMcid(mcid, link);
            _pageHasContent = true;
            _flowTop -= size + (margin?.Bottom ?? 0);

            // The annotation rectangle is 1.1 em tall with its bottom at
            // baseline − descent.
            _links.Add((link, _page!, x, baseline - d * size, x + width, baseline + (1.1 - d) * size));
        }

        // ── figures ───────────────────────────────────────────────────

        private void RenderFigure(LS.IllustrationElement fig)
        {
            var path = fig.ImagePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch { return; }

            ImageStamp stamp;
            var isPng = data.Length >= 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
            try
            {
                stamp = isPng ? ImageStamp.FromPngData(data) : ImageStamp.FromJpegStream(new MemoryStream(data));
            }
            catch { return; }

            // Explicit dimensions win; otherwise pixels scale at 72/300 (300 dpi
            // is assumed regardless of the image's own density),
            // or 72/resolution when SetImage(path, resolution) supplied one.
            double w, h;
            if (fig.ImageWidth > 0 && fig.ImageHeight > 0)
            {
                w = fig.ImageWidth;
                h = fig.ImageHeight;
            }
            else
            {
                double scale = 72.0 / (fig.Resolution > 0 ? fig.Resolution : 300);
                w = stamp.PixelWidth * scale;
                h = stamp.PixelHeight * scale;
            }

            var (margin, newPage, _) = Position(fig);
            BlockStart(newPage);
            _flowTop -= margin?.Top ?? 0;
            PageBreakIfNeeded(h);

            double x = ContentLeft + (margin?.Left ?? 0);
            double top = _flowTop;
            var resName = stamp.RegisterXObject(_page!);
            var mcid = _nextMcid++;

            var cs = _cs!;
            cs.SaveState();
            cs.BeginMarkedContent(fig.StructureType, mcid);
            cs.SaveState();
            cs.SetMatrix(w, 0, 0, h, x, top - h);
            cs.DrawXObject(resName);
            cs.RestoreState();
            cs.EndMarkedContent();
            cs.RestoreState();

            RecordMcid(mcid, fig);
            _pageHasContent = true;
            _flowTop = top - h - (margin?.Bottom ?? 0);
        }

        // ── tables ────────────────────────────────────────────────────

        private void RenderTable(LS.TableElement table)
        {
            // Rows in authored order: row groups (THead/TBody/TFoot) are
            // transparent, direct TR children participate too.
            var rows = new List<LS.TableTRElement>();
            foreach (var child in table.ChildElements)
            {
                if (child is LS.TableTRElement tr) rows.Add(tr);
                else if (child is LS.TableTHeadElement or LS.TableTBodyElement or LS.TableTFootElement)
                    foreach (var sub in child.ChildElements)
                        if (sub is LS.TableTRElement str) rows.Add(str);
            }
            if (rows.Count == 0) return;

            var (margin, newPage, _) = Position(table);
            BlockStart(newPage);
            _flowTop -= margin?.Top ?? 0;

            double tableLeft = ContentLeft + (margin?.Left ?? 0);
            const double rowSize = 12.0;
            int repeating = Math.Max(0, Math.Min(table.RepeatingRowsCount, rows.Count));

            var cs = _cs!;
            cs.SaveState();
            // The whole table gets wrapped in its own marked content;
            // the id stays unreferenced in the structure tree (the cells carry
            // the real content links).
            cs.BeginMarkedContent(table.StructureType, _nextMcid++);

            for (var r = 0; r < rows.Count; r++)
            {
                if (_pageHasContent && _flowTop - rowSize < ContentBottom)
                {
                    // Continue on a fresh page, re-rendering the repeating
                    // header rows (visual only — their structure elements keep
                    // the first occurrence's marked content).
                    cs.EndMarkedContent();
                    cs.RestoreState();
                    NewPage();
                    cs = _cs!;
                    cs.SaveState();
                    cs.BeginMarkedContent(table.StructureType, _nextMcid++);
                    for (var hr = 0; hr < repeating && hr < r; hr++)
                        RenderTableRow(cs, rows[hr], tableLeft, rowSize, register: false);
                }
                RenderTableRow(cs, rows[r], tableLeft, rowSize, register: true);
            }

            cs.EndMarkedContent();
            cs.RestoreState();
            _pageHasContent = true;
            _flowTop -= margin?.Bottom ?? 0;
        }

        private void RenderTableRow(ContentStreamBuilder cs, LS.TableTRElement row,
            double tableLeft, double rowSize, bool register)
        {
            double colX = tableLeft;
            foreach (var cell in row.ChildElements)
            {
                if (cell is not (LS.TableTDElement or LS.TableTHElement)) continue;
                var text = BlockText(cell);
                var (cellMargin, _, _) = Position(cell);
                double x = colX + (cellMargin?.Left ?? 0);
                double width = DefaultColumnWidth - (cellMargin?.Left ?? 0);
                if (!string.IsNullOrEmpty(text))
                {
                    var (font, size) = ResolveRunFont(cell, null, 12f);
                    double d = DescentRatio(font);
                    double baseline = _flowTop - (1 - d) * size;
                    var mcid = _nextMcid++;
                    cs.SaveState();
                    cs.BeginMarkedContent(cell.StructureType, mcid);
                    cs.SaveState();
                    cs.Rectangle(x, _flowTop - rowSize, width, LineBox * size);
                    cs.Clip();
                    EmitTextChunk(font, size, x, baseline, 0, 0, 0, text);
                    cs.RestoreState();
                    cs.EndMarkedContent();
                    cs.RestoreState();
                    if (register) RecordMcid(mcid, cell);
                }
                colX += DefaultColumnWidth;
            }
            _flowTop -= rowSize;
            _pageHasContent = true;
        }

        // ── structure wiring ──────────────────────────────────────────

        /// <summary>Wire the structure tree to the rendered marked content:
        /// every element becomes an indirect object with /P (and /Pg for
        /// content leaves), leaf /K entries carry the MCIDs, pages get
        /// /StructParents, links get their annotation + OBJR, and the
        /// StructTreeRoot receives /ParentTree + /ParentTreeNextKey.</summary>
        private void WireStructure(LS.StructureElement root)
        {
            var structRootDict = (root._parent as LS.StructTreeRootElement)?._dict
                ?? _doc.Reader.ResolveDict(_doc.Catalog.Get("StructTreeRoot"));
            if (structRootDict is null) return;

            // Number every element in the tree (the /K arrays and the parent
            // tree reference them indirectly).
            var refs = new Dictionary<PdfDictionary, PdfIndirectRef>(ReferenceEqualityComparer.Instance);
            void NumberTree(LS.StructureElement el)
            {
                if (!refs.ContainsKey(el._dict))
                {
                    var objNum = _doc.AllocateObjectNumber();
                    _doc.AddNewObject(objNum, el._dict);
                    refs[el._dict] = new PdfIndirectRef(objNum, 0);
                }
                foreach (var child in el.ChildElements) NumberTree(child);
            }
            NumberTree(root);

            // /P links + /K child references.
            void LinkTree(LS.StructureElement el)
            {
                foreach (var child in el.ChildElements)
                {
                    child._dict.Set("P", refs[el._dict]);
                    LinkTree(child);
                }
                if (el._dict.Get("K") is PdfArray k)
                {
                    for (var i = 0; i < k.Count; i++)
                        if (k[i] is PdfDictionary kd && refs.TryGetValue(kd, out var r))
                            k.ReplaceAt(i, r);
                }
            }
            if (_doc.Catalog.Get("StructTreeRoot") is PdfIndirectRef structRootRef)
                root._dict.Set("P", structRootRef);
            LinkTree(root);
            if (structRootDict.Get("K") is PdfArray rootK)
            {
                for (var i = 0; i < rootK.Count; i++)
                    if (rootK[i] is PdfDictionary kd && refs.TryGetValue(kd, out var r))
                        rootK.ReplaceAt(i, r);
            }

            // Leaf /K MCIDs + /Pg + the per-page parent-tree arrays.
            var nums = new PdfArray();
            var nextKey = 0;
            foreach (var (page, mcids) in _renderedPages)
            {
                var key = nextKey++;
                page.Dict.Set("StructParents", new PdfInteger(key));

                var maxMcid = -1;
                foreach (var (mcid, _) in mcids) maxMcid = Math.Max(maxMcid, mcid);
                var pageArr = new PdfArray();
                for (var i = 0; i <= maxMcid; i++) pageArr.Add(PdfNull.Instance);

                foreach (var (mcid, el) in mcids)
                {
                    if (refs.TryGetValue(el._dict, out var elRef))
                        pageArr.ReplaceAt(mcid, elRef);
                    AddLeafMcid(el, mcid);
                    _doc.PendingStructPgFixups.Add((el._dict, page));
                }
                nums.Add(new PdfInteger(key));
                nums.Add(pageArr);
            }

            // Link annotations + OBJR children.
            foreach (var (link, page, llx, lly, urx, ury) in _links)
            {
                var annot = new PdfDictionary();
                annot.Set("Type", new PdfName("Annot"));
                annot.Set("Subtype", new PdfName("Link"));
                var rect = new PdfArray();
                rect.Add(new PdfReal(llx));
                rect.Add(new PdfReal(lly));
                rect.Add(new PdfReal(urx));
                rect.Add(new PdfReal(ury));
                annot.Set("Rect", rect);
                annot.Set("F", new PdfInteger(4));
                var bs = new PdfDictionary();
                bs.Set("W", new PdfInteger(0));
                annot.Set("BS", bs);
                annot.Set("BE", new PdfDictionary());
                if (link.Hyperlink?.Url is { } url)
                {
                    var action = new PdfDictionary();
                    action.Set("S", new PdfName("URI"));
                    action.Set("URI", new PdfString(System.Text.Encoding.ASCII.GetBytes(url)));
                    annot.Set("A", action);
                }
                if (!string.IsNullOrEmpty(link.AlternateDescriptions))
                    annot.Set("Contents", new PdfString(System.Text.Encoding.UTF8.GetBytes(link.AlternateDescriptions!)));

                var annotNum = _doc.AllocateObjectNumber();
                _doc.AddNewObject(annotNum, annot);
                var annotRef = new PdfIndirectRef(annotNum, 0);

                if (_doc.Reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots)
                {
                    annots = new PdfArray();
                    page.Dict.Set("Annots", annots);
                }
                annots.Add(annotRef);

                var key = nextKey++;
                annot.Set("StructParent", new PdfInteger(key));
                if (refs.TryGetValue(link._dict, out var linkRef))
                {
                    nums.Add(new PdfInteger(key));
                    nums.Add(linkRef);
                }

                var objr = new PdfDictionary();
                objr.Set("Type", new PdfName("OBJR"));
                objr.Set("Obj", annotRef);
                if (link._dict.Get("K") is PdfArray linkK) linkK.Add(objr);
            }

            var parentTree = new PdfDictionary();
            parentTree.Set("Nums", nums);
            structRootDict.Set("ParentTree", parentTree);
            structRootDict.Set("ParentTreeNextKey", new PdfInteger(nextKey));
        }

        /// <summary>Add a marked-content id to a leaf element's /K (a bare int
        /// for the first, an array once there are several).</summary>
        private static void AddLeafMcid(LS.StructureElement el, int mcid)
        {
            switch (el._dict.Get("K"))
            {
                case null:
                    el._dict.Set("K", new PdfInteger(mcid));
                    break;
                case PdfInteger first:
                    var arr = new PdfArray();
                    arr.Add(first);
                    arr.Add(new PdfInteger(mcid));
                    el._dict.Set("K", arr);
                    break;
                case PdfArray existing:
                    existing.Add(new PdfInteger(mcid));
                    break;
            }
        }
    }
}
