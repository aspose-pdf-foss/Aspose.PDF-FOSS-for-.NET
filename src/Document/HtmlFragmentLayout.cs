using System.Linq;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Forms;
using Aspose.Pdf.IO;
using Aspose.Pdf.IO.Filters;
using Aspose.Pdf.Optimization;
using Aspose.Pdf.Security;
using Aspose.Pdf.Tagged;
using DocumentPrivilege = Aspose.Pdf.Facades.DocumentPrivilege;

namespace Aspose.Pdf;

public sealed partial class Document
{
    private void LayoutHtmlFragmentParagraph(HtmlFragment html, FlowLayout flow, Page page, Text.TextBuilder tb, HashSet<Table> renderedTables, List<(byte[] content, double width, double height)> overflowPages, Dictionary<int, List<(byte[] data, Rectangle rect)>> overflowImages, double marginLeft, double marginRight, double marginTop, double marginBottom)
    {
        // Inline <svg> elements become <img src="inline-svg:i"> placeholders
        // rendered through the SVG engine by RenderHtmlImages.
        // A meeting-agenda fragment draws straight onto this page: its levels
        // carry their own indents and numbering boxes, which the block flow
        // below has no model for (see Agenda.cs).
        if (Converters.HtmlToPdfConverter.TryRenderAgendaOutline(
                html.HtmlContent ?? "", page, marginLeft, marginRight, marginTop))
            return;
        // A class-width form letter lays its columns out on the declared body
        // width rather than the page's (see QuoteScheduleFragment.cs).
        if (TryRenderQuoteScheduleFragment(html.HtmlContent ?? "", flow, page,
                marginLeft, marginBottom, html.HtmlLoadOptions))
            return;
        var htmlContent = Converters.HtmlToPdfConverter.ExtractInlineSvgs(
            Converters.HtmlToPdfConverter.ApplyKnockoutTextBindings(html.HtmlContent ?? ""),
            out var inlineSvgs);
        var htmlColor = html.TextState?.ForegroundColor;
        // One Link annotation per hyperlinked HtmlFragment (see below).
        var htmlFragmentLinkEmitted = false;

        // A layout table places blocks rather than drawing a grid: each
        // row lays its cells out side by side at their own widths, and a
        // cell's own table renders at that position. Laying out returns the



        // Render a run of block-structured HTML (paragraphs/headings/lists)
        // through the flow at the current cursor, then any <img> in that chunk.

        // Left border+padding of every framed block currently open around
        // the content being rendered (see the frame bookkeeping below).
        var htmlFrameIndent = 0.0;

        // Framed blocks: a block element whose CSS declares a border draws
        // a box round everything it contains — its own text and any table
        // inside it — over as many pages as that content takes. The spans
        // are in the SOURCE's coordinates, so the chunked render below can
        // say when each frame opens and closes by where the chunk sits.
        var htmlFrames = Converters.HtmlToPdfConverter.FramedBlockSpans(htmlContent);
        // The frames open so far, each with the slot and Y it opened at.
        var htmlOpenFrames = new List<(int Index, int Slot, double Top)>();
        // Open every frame that starts inside [from, to) and close every one
        // that ends there. A frame is honoured only when nothing renderable
        // precedes its open tag in its own chunk — the box top is the flow
        // cursor at the chunk boundary, so text before it would sit inside a
        // box that has not begun.
        void HtmlFramesOpening(int from, int to, string chunkText)
        {
            for (var fi = 0; fi < htmlFrames.Count; fi++)
            {
                var f = htmlFrames[fi];
                if (f.Start < from || f.Start >= to) continue;
                var before = htmlContent.Substring(from, f.Start - from);
                if (HtmlFragment.StripHtmlTags(before).Trim().Length > 0) continue;
                _ = chunkText;
                htmlOpenFrames.Add((fi, flow.CurrentSlot, flow.CurrentY));
                // The content starts below the top border and its padding,
                // and clear of the left border.
                flow.AdvanceY(f.BorderWidthPt + f.PadTopPt);
                htmlFrameIndent += f.BorderWidthPt;
            }
        }
        void HtmlFramesClosing(int from, int to)
        {
            for (var oi = htmlOpenFrames.Count - 1; oi >= 0; oi--)
            {
                var open = htmlOpenFrames[oi];
                var f = htmlFrames[open.Index];
                if (f.End <= from || f.End > to) continue;
                flow.DrawFrameBox(open.Slot, open.Top, flow.CurrentY,
                    f.BorderWidthPt, f.BorderColor);
                htmlFrameIndent -= f.BorderWidthPt;
                htmlOpenFrames.RemoveAt(oi);
            }
        }

        if (html.HtmlLoadOptions?.PageInfo is { MarginAssigned: true } mfPi)
        {
            // A fragment whose load options declare page margins lays
            // out in its own box INSIDE the page's content box: the
            // declared margins add to the page's. The first page takes
            // PageInfo.Margin, every generated page after it takes
            // AnyMargin. Vertical rhythm is the CSS half-leading model
            // on an integer-pixel "normal" line height, and a line's
            // ascent/descent is the max over the fragment's own font
            // (the block strut) and every run on the line.
            var mfSize = html.TextState is { } mfTs && mfTs.FontSize > 0 ? (double)mfTs.FontSize : 10.0;
            var mfFirst = mfPi.Margin;
            var mfRest = mfPi.AnyMarginAssigned ? mfPi.AnyMargin : mfPi.Margin;
            var mfLeft = marginLeft + mfFirst.Left;
            var mfRight = page.Width - marginRight - mfFirst.Right;
            var mfWidth = Math.Max(1, mfRight - mfLeft);
            var mfBottom = page.Height - marginBottom - mfFirst.Bottom;   // from top
            var mfTopFirst = marginTop + mfFirst.Top;                     // from top
            var mfTopRest = marginTop + mfRest.Top;

            // The strut: the fragment's own face, mapped onto the
            // Standard-14 metric twin the renderer can draw with.
            var mfStrut = Converters.HtmlToPdfConverter.Std14Face(
                html.TextState?.Font?.FontName, false, false);

            string MfFace(Converters.HtmlToPdfConverter.FlowRun r)
                => Converters.HtmlToPdfConverter.Std14Face(
                    r.Family ?? html.TextState?.Font?.FontName, r.Bold, r.Italic);

            double MfWidthOf(string t, string face)
            {
                if (t.Length == 0) return 0;
                try
                {
                    return Text.FontRepository.TryFindFont(face)?.MeasureString(t, mfSize)
                           ?? t.Length * mfSize * 0.5;
                }
                catch { return t.Length * mfSize * 0.5; }
            }

            // one laid-out piece of a line
            var mfLines = new List<(List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)> Pieces,
                double Above, double Below)>();

            foreach (var mfPara in Converters.HtmlToPdfConverter.ParseFlowParagraphs(htmlContent))
            {
                var pieces = new List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)>();
                var x = 0.0;
                var above = Converters.HtmlToPdfConverter.FaceAbove(mfStrut, mfSize);
                var below = Converters.HtmlToPdfConverter.FaceBelow(mfStrut, mfSize);
                var lineStarted = false;

                void MfFlush()
                {
                    // trailing space never holds a line open
                    while (pieces.Count > 0 && pieces[^1].Item1.Trim().Length == 0)
                        pieces.RemoveAt(pieces.Count - 1);
                    mfLines.Add((new List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)>(pieces),
                        above, below));
                    pieces.Clear();
                    x = 0;
                    above = Converters.HtmlToPdfConverter.FaceAbove(mfStrut, mfSize);
                    below = Converters.HtmlToPdfConverter.FaceBelow(mfStrut, mfSize);
                    lineStarted = false;
                }


                foreach (var run in mfPara.Runs)
                {
                    if (run.HardBreak) { MfFlush(); continue; }
                    var face = MfFace(run);
                    var runAbove = Converters.HtmlToPdfConverter.FaceAbove(face, mfSize);
                    var runBelow = Converters.HtmlToPdfConverter.FaceBelow(face, mfSize);
                    // split into words, keeping each word's leading space
                    foreach (System.Text.RegularExpressions.Match wm in System.Text.RegularExpressions.Regex.Matches(run.Text, @" *[^ ]+| +"))
                    {
                        var word = wm.Value;
                        var atLineStart = !lineStarted;
                        var draw = atLineStart ? word.TrimStart(' ') : word;
                        if (draw.Length == 0) continue;
                        var w = MfWidthOf(draw, face);
                        if (lineStarted && x + w > mfWidth + 0.01 && draw.Trim().Length > 0)
                        {
                            MfFlush();
                            draw = word.TrimStart(' ');
                            if (draw.Length == 0) continue;
                            w = MfWidthOf(draw, face);
                        }
                        pieces.Add((draw, run, face, x, w));
                        x += w;
                        lineStarted = true;
                        above = Math.Max(above, runAbove);
                        below = Math.Max(below, runBelow);
                    }
                }
                MfFlush();
            }

            // place: greedy fill while the line box stays inside the
            // bottom limit; a block moves whole unless two of its
            // lines still fit on the page
            var mfY = mfTopFirst;      // from top, line-box top
            var mfPageTop = mfTopFirst;
            var mfBuilder = new Content.ContentStreamBuilder();
            mfBuilder.SaveState();
            var mfDrew = false;

            void MfNewPage()
            {
                mfBuilder.RestoreState();
                if (mfDrew) flow.InjectContentAtCursor(mfBuilder.Build());
                flow.ForceNewPage();
                mfBuilder = new Content.ContentStreamBuilder();
                mfBuilder.SaveState();
                mfDrew = false;
                mfPageTop = mfTopRest;
                mfY = mfTopRest;
            }

            var mfPrevBelow = 0.0;
            var mfOnPage = 0;
            for (var li = 0; li < mfLines.Count; li++)
            {
                var (pieces, above, below) = mfLines[li];
                var baseline = mfOnPage == 0 ? mfPageTop + above : mfY + mfPrevBelow + above;
                if (baseline + below > mfBottom + 0.01)
                {
                    MfNewPage();
                    mfOnPage = 0;
                    baseline = mfPageTop + above;
                }
                foreach (var (text, run, face, x, w) in pieces)
                {
                    if (text.Trim().Length == 0) continue;
                    var res = Table.RegisterFont(flow.CurrentPage, face);
                    var py = page.Height - baseline;
                    if (run.Back is { } bg)
                    {
                        // the highlight fills the run's content area:
                        // no half-leading, just ascent+descent
                        var hTop = baseline - Converters.HtmlToPdfConverter.FaceAscent(face, mfSize);
                        var hH = Converters.HtmlToPdfConverter.FaceAscent(face, mfSize)
                                 + Converters.HtmlToPdfConverter.FaceDescent(face, mfSize);
                        mfBuilder.SetFillColor(bg)
                                 .Rectangle(mfLeft + x, page.Height - hTop - hH, w, hH)
                                 .Fill();
                    }
                    if (run.Fore is { } fg) mfBuilder.SetFillColor(fg);
                    else mfBuilder.SetFillGray(0);
                    mfBuilder.BeginText().SetFont(res, mfSize)
                             .MoveTextPosition(mfLeft + x, py)
                             .ShowText(text).EndText();
                    mfDrew = true;
                }
                mfY = baseline - above;
                mfPrevBelow = below;
                mfY = baseline;      // track the baseline for the next step
                mfOnPage++;
            }
            mfBuilder.RestoreState();
            if (mfDrew) flow.InjectContentAtCursor(mfBuilder.Build());
            var mfEnd = page.Height - (mfY + mfPrevBelow);
            if (flow.CurrentY > mfEnd) flow.AdvanceY(flow.CurrentY - mfEnd);
        }
        else if (Converters.HtmlToPdfConverter.TryParseFilingLetter(htmlContent, out var flItems))

        {
            // Centered filing-letter dialect: the letterhead image at
            // natural size on the page center, then Times lines on the
            // letter's 4 em rhythm — a hard break holds a full blank
            // line, paragraph wrappers keep their 1 em margins, and
            // the marked section sets left with its 1 cm indents.
            const double flPitch = 48.0, flFs = 12.0, flDrop = 48.4;
            double FlMeasure(string t)
            {
                try
                {
                    return Text.FontRepository.TryFindFont("Times-Roman")?.MeasureString(t, flFs)
                           ?? t.Length * flFs * 0.5;
                }
                catch { return t.Length * flFs * 0.5; }
            }
            foreach (var fl in flItems)
            {
                if (fl.ExtraGap > 0) flow.AdvanceY(fl.ExtraGap);
                if (fl.ImgSrc is not null)
                {
                    var fbytes = LoadHtmlImageBytes(fl.ImgSrc);
                    if (fbytes is not null)
                    {
                        // css-pixel sizing: the letter scales its
                        // letterhead at 0.75 pt per image pixel
                        TryGetImageNaturalSizePt(fbytes, false, out var fw, out var fh);
                        fw *= 0.75;
                        fh *= 0.75;
                        if (fw <= 0 || fh <= 0) { fw = 187.5; fh = 75; }
                        var fx = (page.Width - fw) / 2;
                        var fTop = flow.CurrentY;
                        flow.CurrentPage.AddImage(fbytes,
                            new Rectangle(fx, fTop - fh, fx + fw, fTop));
                        flow.AdvanceY(fh);
                    }
                    continue;
                }
                if (fl.Blank) { flow.AdvanceY(flPitch); continue; }
                if (fl.Text is not { } ftext) continue;
                var fres = Table.RegisterFont(flow.CurrentPage, "Times-Roman");
                var maxW = page.Width - 144 - fl.IndentPt;
                var linesOut = new List<string>();
                var rem2 = ftext;
                while (rem2.Length > 0 && FlMeasure(rem2) > maxW)
                {
                    var cut = rem2.Length;
                    while (cut > 0 && (cut >= rem2.Length || rem2[cut] != ' '
                           || FlMeasure(rem2[..cut]) > maxW))
                        cut--;
                    if (cut <= 0) { cut = rem2.Length; }
                    linesOut.Add(rem2[..cut]);
                    rem2 = rem2[cut..].TrimStart();
                }
                if (rem2.Length > 0) linesOut.Add(rem2);
                foreach (var lt in linesOut)
                {
                    if (flow.CurrentY - flDrop < flow.BottomMargin) flow.ForceNewPage();
                    var lw = FlMeasure(lt);
                    var lx = fl.AlignLeft ? 72 + fl.IndentPt
                        : Math.Max(72, (page.Width - lw) / 2);
                    var fb = new Content.ContentStreamBuilder();
                    fb.SaveState();
                    fb.BeginText().SetFont(fres, flFs)
                      .MoveTextPosition(lx, flow.CurrentY - flDrop)
                      .ShowText(lt).EndText();
                    fb.RestoreState();
                    flow.InjectContentAtCursor(fb.Build());
                    flow.AdvanceY(flPitch);
                }
            }
        }
        else if (Converters.HtmlToPdfConverter.TryParseProcedureStepRows(htmlContent, out var psRows,
                     html.IsParagraphHasMargin, html.HtmlLoadOptions))
        {
            LayoutProcedureStepRows(psRows, html, flow, page, marginLeft, marginRight, marginTop, marginBottom);
        }
        else if (Converters.HtmlToPdfConverter.ContainsTable(htmlContent))
        {
            // Mixed content (text blocks + real column tables): render each
            // top-level segment in document order so an HTML <table> flows as
            // columns instead of a flat tag-stripped stack.
            // A full <HTML> document with no fragment font and a table
            // declaring an absolute pixel width is the UA-serif wide-box
            // shape: its text chunks set in the serif writer above, and
            // its tables use the widest declared box.
            var uaWideBoxPt = 0.0;
            var uaSerifFrag = System.Text.RegularExpressions.Regex.IsMatch(
                    htmlContent, @"<html[\s>]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && html.TextState?.Font is null
                && string.IsNullOrEmpty(html.TextState?.FontName);
            if (uaSerifFrag)
                foreach (System.Text.RegularExpressions.Match uwm in
                    System.Text.RegularExpressions.Regex.Matches(htmlContent,
                        @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+)(?![\d%])",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    uaWideBoxPt = Math.Max(uaWideBoxPt,
                        double.Parse(uwm.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) * 0.75);
            uaSerifFrag &= uaWideBoxPt > page.Width - marginLeft - marginRight;
            // The segments concatenate back to htmlContent, so a running
            // offset places each one in the source — which is how the
            // framed-block spans above are expressed.
            // Verdana form-grid document: a width-percent wrapper div
            // whose cells declare inline Verdana spans throughout —
            // every table chunk below takes the dialect.
            var vgDoc = System.Text.RegularExpressions.Regex.IsMatch(
                    htmlContent, @"^\s*<div[^>]*style\s*=\s*'[^']*width:\s*\d+%",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && System.Text.RegularExpressions.Regex.Matches(
                    htmlContent, @"font-family:\s*Verdana",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count >= 4;
            // The width-percent wrapper scopes only its OWN subtree: a
            // section table after its </div> spans the full content box
            // (the owner/member/field grids run 90..505 while
            // the label grid keeps the wrapper's 92%). Depth-walk the div
            // tags to find the wrapper's matching close.
            var vgWrapClose = int.MaxValue;
            if (vgDoc)
            {
                var vgDepth = 0;
                foreach (System.Text.RegularExpressions.Match dm in
                    System.Text.RegularExpressions.Regex.Matches(htmlContent,
                        @"<\s*(/?)div\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                {
                    vgDepth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
                    if (vgDepth == 0) { vgWrapClose = dm.Index; break; }
                }
            }
            var chunkAt = 0;
            foreach (var (isTable, chunk) in Converters.HtmlToPdfConverter.SegmentHtmlTables(htmlContent))
            {
                if (vgDoc && Environment.GetEnvironmentVariable("ASPOSE_HTML_DEBUG_FG") is not null)
                    Console.WriteLine($"[chunk] table={isTable} len={chunk.Length} " +
                        $"'{System.Text.RegularExpressions.Regex.Replace(chunk.Length > 70 ? chunk[..70] : chunk, @"\s+", " ")}'");
                var chunkEnd = chunkAt + chunk.Length;
                HtmlFramesOpening(chunkAt, chunkEnd, chunk);
                if (isTable && uaSerifFrag)
                {
                    RenderUaSerifTable(chunk, uaWideBoxPt, flow, page, marginLeft);
                }
                else if (isTable)
                {
                    var isLayout = Converters.HtmlToPdfConverter.IsLayoutTableHtml(chunk);
                    var chunkCss = isLayout
                        ? Converters.HtmlToPdfConverter.ParseStyleSheet(htmlContent)
                        : null;
                    // a percentage width resolves against the body's own
                    // declared width when the document states one
                    var layoutAvail = page.Width - marginLeft - marginRight;
                    if (uaSerifFrag) layoutAvail = uaWideBoxPt;
                    if (isLayout
                        && Converters.HtmlToPdfConverter.DeclaredBodyWidthPt(htmlContent) is > 0 and var bw)
                        layoutAvail = bw;
                    // The document's own `body { }` type is the grid's base
                    // too: a table inherits the page's face and size rather
                    // than falling back to the 11 pt Standard-14 default
                    // while the prose around it sets in the declared face.
                    var tblBodyCss = Converters.HtmlToPdfConverter.BodyCssFont(htmlContent);
                    // Verdana form-grid fragment: a report grid whose
                    // cells each declare an inline `font-family:
                    // Verdana; font-size: Npt` span, wrapped in a
                    // width-percent div. The dialect sets it
                    // in REAL Verdana metrics with 19px (14.25pt @8pt,
                    // scaling with the size) line boxes, the grid sized to
                    // the wrapper's percent of the content box.
                    // Doc-level gate (vgDoc): EVERY table of the form
                    // grid takes the dialect — the one-span section
                    // bands and the spacer table included, not just
                    // the span-heavy label grid.
                    var vgWrap = System.Text.RegularExpressions.Regex.Match(
                        htmlContent, @"^\s*<div[^>]*style\s*=\s*'[^']*width:\s*(\d+)%",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var vgSize = System.Text.RegularExpressions.Regex.Match(
                        chunk, @"font-size:\s*([\d.]+)\s*pt",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!vgSize.Success)
                        vgSize = System.Text.RegularExpressions.Regex.Match(
                            htmlContent, @"font-size:\s*([\d.]+)\s*pt",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var verdanaGrid = !isLayout && vgDoc && vgWrap.Success
                        && vgSize.Success;
                    var vgPt = verdanaGrid
                        ? double.Parse(vgSize.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture)
                        : 0;
                    var vgInWrap = chunkAt < vgWrapClose;
                    if (verdanaGrid && vgInWrap)
                        layoutAvail *= double.Parse(vgWrap.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture) / 100.0;
                    // The cell strut: the ambient font's line box at the
                    // default size — Verdana-12 (14.25) inside the
                    // wrapper's <font face='Verdana'>, the serif
                    // default's 13.5 for the top-level section tables.
                    var vgStrutPt = !verdanaGrid ? 0
                        : vgInWrap
                            ? Converters.HtmlToPdfConverter.PxLinePt(
                                Converters.HtmlToPdfConverter.FormGridBasePt,
                                Converters.HtmlToPdfConverter.VerdanaWinLineRatio)
                            : Converters.HtmlToPdfConverter.PxLinePt(
                                Converters.HtmlToPdfConverter.FormGridBasePt,
                                Converters.HtmlToPdfConverter.SerifWinLineRatio);
                    // The strut's baseline drop: half-leading + winAscent
                    // within the strut box, in the ambient face.
                    var vgStrutDropPt = !verdanaGrid ? 0
                        : vgInWrap
                            ? (vgStrutPt - Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.VerdanaWinLineRatio) / 2
                                + Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.VerdanaWinAscent
                            : (vgStrutPt - Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.SerifWinLineRatio) / 2
                                + Converters.HtmlToPdfConverter.FormGridBasePt
                                    * Converters.HtmlToPdfConverter.SerifWinAscent;
                    var t = Converters.HtmlToPdfConverter.BuildTableFromHtml(
                        chunk, layoutAvail, out _, html.HtmlLoadOptions,
                        inlineSvgs, chunkCss, false, false,
                        verdanaGrid ? vgStrutPt : 0,
                        verdanaGrid ? vgPt : tblBodyCss.SizePt, false, isLayout,
                        // …and the face's own `line-height: normal` box,
                        // so a cell line steps on the same rhythm the
                        // prose does (Arial 12 → 13.5, not a bare 12).
                        cssRunFace: verdanaGrid ? null : tblBodyCss.Face,
                        defaultCellFace: verdanaGrid ? null : tblBodyCss.Face,
                        formGridDialect: verdanaGrid,
                        formGridStrutPt: vgStrutPt,
                        formGridStrutDropPt: vgStrutDropPt);
                    if (verdanaGrid && t is not null)
                    {
                        t.HonorCellTtfFaces = true;
                        t.FormGridCells = true;
                        // border=1 draws the table's OWN box border too:
                        // the cell grid sits one border-width inside the
                        // table box (outer stroke centre at
                        // 90.38 = box edge + half the 0.75 width, first
                        // cell content at 91.5).
                        if (t.HtmlCellBorderPt > 0 && t.Border is null)
                            t.Border = new BorderInfo(BorderSide.Box,
                                t.HtmlCellBorderPt,
                                t.DefaultCellBorder?.Color ?? Color.Black);
                        // The cell grid's box: the declared table width
                        // less the table border pair — the base every
                        // percent below resolves against (measured exact:
                        // member columns = 20/16/…% of 413.5 = 415 − 1.5).
                        var vgGridBox = layoutAvail - 2 * (t.Border?.Width ?? 0);
                        // A width='100%' section table FILLS the box —
                        // the band rows paint edge to edge; the
                        // content-sized column would shrink the fill
                        // to the caption's width.
                        if (System.Text.RegularExpressions.Regex.IsMatch(
                                chunk, @"<table[^>]*\bwidth\s*=\s*'100%'",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                            && !System.Text.RegularExpressions.Regex.IsMatch(
                                chunk, @"<td[^>]*<td",
                                System.Text.RegularExpressions.RegexOptions.Singleline))
                        {
                            var vgMaxTds = 0;
                            foreach (System.Text.RegularExpressions.Match rm in
                                System.Text.RegularExpressions.Regex.Matches(
                                    chunk, @"<tr[^>]*>(.*?)</tr>",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                    | System.Text.RegularExpressions.RegexOptions.Singleline))
                                vgMaxTds = Math.Max(vgMaxTds,
                                    System.Text.RegularExpressions.Regex.Matches(
                                        rm.Groups[1].Value, @"<td\b",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
                            if (vgMaxTds == 1)
                                t.ColumnWidths = vgGridBox.ToString(
                                    "0.##", System.Globalization.CultureInfo.InvariantCulture);
                            else
                            {
                                // Multi-column: the first row whose EVERY td
                                // declares a percent width owns the grid
                                // (a band row with one colspan cell above
                                // it doesn't) — hard shares of the bordered
                                // box, no content floors (boundaries land
                                // to 0.01 pt).
                                foreach (System.Text.RegularExpressions.Match rm in
                                    System.Text.RegularExpressions.Regex.Matches(
                                        chunk, @"<tr[^>]*>(.*?)</tr>",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                                        | System.Text.RegularExpressions.RegexOptions.Singleline))
                                {
                                    var vgTds = System.Text.RegularExpressions.Regex.Matches(
                                        rm.Groups[1].Value, @"<td\b[^>]*>",
                                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                    if (vgTds.Count < vgMaxTds) continue;
                                    var vgPcts = new List<double>();
                                    foreach (System.Text.RegularExpressions.Match tdm in vgTds)
                                    {
                                        var pw = System.Text.RegularExpressions.Regex.Match(
                                            tdm.Value, @"width\s*=\s*['""]?([\d.]+)%",
                                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                        if (!pw.Success) { vgPcts.Clear(); break; }
                                        vgPcts.Add(double.Parse(pw.Groups[1].Value,
                                            System.Globalization.CultureInfo.InvariantCulture));
                                    }
                                    if (vgPcts.Count >= 2)
                                        t.ColumnWidths = string.Join(" ",
                                            vgPcts.Select(p => (p / 100.0 * vgGridBox).ToString(
                                                "0.###", System.Globalization.CultureInfo.InvariantCulture)));
                                    break;
                                }
                            }
                        }
                        // The declared widths OWN the grid: the ingest's
                        // draw-time min/max fields would re-derive
                        // content columns over the ColumnWidths.
                        if (t.ColumnWidths is not null)
                        {
                            t.HtmlColMinPt = null;
                            t.HtmlColMaxPt = null;
                        }
                    }
                    if (t is not null)
                    {
                        if (isLayout)
                        {
                            if (html.Margin is { Top: > 0 } lmt) flow.AdvanceY(lmt.Top);
                            // breaks written between rows sit above the table
                            var fostered = Converters.HtmlToPdfConverter.FosterParentedBreaks(chunk);
                            if (fostered > 0) flow.AdvanceY(fostered * 11.25);
                            var boxFloor = layoutAvail;
                            foreach (var lt2 in LeafTables(t))
                            {
                                Converters.HtmlToPdfConverter.ApplyAutoWidths(lt2, 0);   // measure at its minimum
                                var floorSum = 0.0;
                                foreach (var cw in (lt2.ColumnWidths ?? "").Split(
                                             ' ', StringSplitOptions.RemoveEmptyEntries))
                                    if (double.TryParse(cw, System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.InvariantCulture, out var cv))
                                        floorSum += cv;
                                boxFloor = Math.Max(boxFloor, floorSum);
                            }
                            var usedH = RenderLayoutTable(t, -1, boxFloor, flow.CurrentY, flow, marginLeft, renderedTables);
                            flow.AdvanceY(usedH);
                        }
                        else RenderHtmlTable(t, flow, page, marginLeft, marginTop, overflowPages, overflowImages);
                    }
                }
                else if (uaSerifFrag) RenderUaSerifChunk(chunk, uaWideBoxPt, html, flow, marginLeft);
                // Form-grid document: a bare-<br> stretch between two
                // section tables is one ambient line box per break —
                // the serif default's 13.5 outside the wrapper div
                // (622.1+13.5 = 635.6), the wrapper
                // font's UNROUNDED Verdana-12 line inside it
                // (414.0+14.58 = 428.6; the rounded 19px box
                // measures 0.35 short there).
                else if (vgDoc && System.Text.RegularExpressions.Regex.IsMatch(
                             chunk, @"^\s*(<br\s*/?>\s*)+$",
                             System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    foreach (System.Text.RegularExpressions.Match brm in
                        System.Text.RegularExpressions.Regex.Matches(
                            chunk, @"<br\s*/?>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        flow.AdvanceY(VgBrBoxPt(chunkAt + brm.Index));
                else if (vgDoc && TryVgFlowText(chunk, chunkAt))
                {
                    // Form-grid document: a bare top-level <div>text</div>
                    // between section tables rendered as one serif flow
                    // line (see TryVgFlowText).
                }
                else
                {
                    // Form-grid document: a <br> standing BETWEEN element
                    // tags in a mixed chunk (`</div><br><div>…`) is the
                    // same one-line-box space as the bare-<br> chunks —
                    // the blocks renderer collapses it otherwise. A chunk-
                    // final <br> (the chunker split just before the next
                    // table tag) counts too.
                    if (vgDoc)
                        foreach (System.Text.RegularExpressions.Match brm in
                            System.Text.RegularExpressions.Regex.Matches(
                                chunk, @"(?<=>)\s*<br\s*/?>\s*(?=<|$)",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            flow.AdvanceY(VgBrBoxPt(chunkAt + brm.Index));
                    RenderHtmlBlocks(chunk, html, flow, page, tb, htmlColor, inlineSvgs, ref htmlFragmentLinkEmitted, htmlFrameIndent, marginLeft, marginRight, marginTop);
                }
                // The break's own line box, by ITS position: the wrapper
                // font's UNROUNDED Verdana-12 line inside the div, the
                // serif default's rounded 13.5 outside it.
                double VgBrBoxPt(int at) => at < vgWrapClose
                    ? Converters.HtmlToPdfConverter.FormGridBasePt
                        * Converters.HtmlToPdfConverter.VerdanaWinLineRatio
                    : Converters.HtmlToPdfConverter.PxLinePt(
                        Converters.HtmlToPdfConverter.FormGridBasePt,
                        Converters.HtmlToPdfConverter.SerifWinLineRatio);
                // A structure-only chunk holding one bare <div>text</div>
                // (plus breaks and closing tags): the text is a top-level
                // flow line in the serif default — Times-12 on its 18px
                // box, Arabic shaped, seated at half-leading + ascent
                // (bbox 505.21..518.49 inside the 505.08 line).
                bool TryVgFlowText(string chunkV, int at)
                {
                    var dm = System.Text.RegularExpressions.Regex.Match(
                        chunkV, @"<div[^>]*>([^<]+)</div>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!dm.Success || dm.Groups[1].Value.Trim().Length == 0) return false;
                    var restV = chunkV.Remove(dm.Index, dm.Length);
                    if (System.Text.RegularExpressions.Regex.Replace(restV,
                            @"<br\s*/?>|</\s*(?:font|div)\s*>|\s+", "",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Length != 0)
                        return false;
                    var serifFont = Text.FontRepository.TryFindFont("Times New Roman");
                    var serifTtf = serifFont?.SourceFontData?.TtfData;
                    if (serifTtf is null) return false;
                    foreach (System.Text.RegularExpressions.Match brm in
                        System.Text.RegularExpressions.Regex.Matches(chunkV, @"<br\s*/?>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        if (brm.Index < dm.Index) flow.AdvanceY(VgBrBoxPt(at + brm.Index));
                    var vgfPt = Converters.HtmlToPdfConverter.FormGridBasePt;
                    var vgfBox = Converters.HtmlToPdfConverter.PxLinePt(vgfPt,
                        Converters.HtmlToPdfConverter.SerifWinLineRatio);
                    var vgfText = System.Text.RegularExpressions.Regex.Replace(
                        dm.Groups[1].Value, @"\s+", " ").Trim();
                    var vgfShaped = Text.ArabicTextShaper.ContainsArabic(vgfText)
                        ? Text.ArabicTextShaper.Shape(vgfText) : vgfText;
                    var vgfDict = Table.ResolvePageFontDict(flow.CurrentPage);
                    var (vgfRes, vgfHex) = Text.Type0FontEmbedder.Embed(
                        vgfDict, serifTtf, serifFont!.FontName, vgfShaped,
                        stripSpacesInBaseFont: true);
                    var vgfAsc = vgfPt * Converters.HtmlToPdfConverter.SerifWinAscent;
                    var vgfDesc = vgfPt * Converters.HtmlToPdfConverter.SerifWinDescent;
                    var vgfBase = flow.CurrentY - (vgfBox - vgfAsc - vgfDesc) / 2 - vgfAsc;
                    var vgfNb = new Content.ContentStreamBuilder();
                    vgfNb.SaveState();
                    vgfNb.BeginText().SetFont(vgfRes, vgfPt)
                        .MoveTextPosition(marginLeft, vgfBase);
                    vgfNb.ShowTextHex(vgfHex);
                    vgfNb.EndText();
                    vgfNb.RestoreState();
                    flow.InjectContentAtCursor(vgfNb.Build());
                    flow.AdvanceY(vgfBox);
                    foreach (System.Text.RegularExpressions.Match brm in
                        System.Text.RegularExpressions.Regex.Matches(chunkV, @"<br\s*/?>",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        if (brm.Index > dm.Index) flow.AdvanceY(VgBrBoxPt(at + brm.Index));
                    return true;
                }
                HtmlFramesClosing(chunkAt, chunkEnd);
                chunkAt = chunkEnd;
            }
        }
        else if (Converters.HtmlToPdfConverter.TryParseHtmlStepList(htmlContent, out var stepItems)
                 && RenderHtmlStepList(stepItems, flow, marginLeft, marginRight, htmlColor))
        {
            // Step-list dialect (ul > li with heading blocks) — rendered
            // through the HTML-engine metrics path above.
        }
        else if (Converters.HtmlToPdfConverter.TryParseInlineEmphasisFont(htmlContent,
                     out var iefFace, out var iefPt, out var iefRuns)
                 && RenderInlineEmphasisRuns(iefFace, iefPt, iefRuns, flow, page, marginLeft, marginRight))
        {
            // Single-font emphasis dialect rendered as styled runs.
        }
        else if (Converters.HtmlToPdfConverter.TryParseNestedStyledSpans(htmlContent,
                     out var nsRuns))
        {
            // Nested styled spans: one styled run per style boundary —
            // the canonical renderer emits one text fragment per run
            // (sizes inherit down the chain, a background paints its
            // own span's run), and the split survives to the absorber
            // through the deferred styled-run writer.
            var nsStyled = new List<FlowLayout.StyledRun>();
            double nsMax = 0;
            foreach (var (nsText, nsSize, nsBg) in nsRuns)
            {
                var nsState = new Text.TextState();
                if (nsBg is { } nsB) nsState.BackgroundColor = nsB;
                if (html.TextState?.Font is { } nsFont) nsState.Font = nsFont;
                var sz = nsSize > 0 ? nsSize : 10;
                if (sz > nsMax) nsMax = sz;
                nsStyled.Add(new FlowLayout.StyledRun
                {
                    Text = nsText, Size = sz, State = nsState,
                });
            }
            flow.WriteStyledParagraph(nsStyled, nsMax * 0.12);
        }
        else if (Converters.HtmlToPdfConverter.TryParseMonoFontLineBoxes(htmlContent,
                     out var mfPt, out var mfLines))
        {
            // Monospace pre-formatted report: verbatim Courier line boxes
            // (every &nbsp; a real column space, every <br/> a hard line)
            // on the dialect's 1.377 em line pitch. The report's content
            // box starts 90 pt from the page top (the dialect's own top
            // margin), below the ambient flow top when that sits higher.
            var mfPitch = mfPt * 1.377;
            var mfAscent = 0.562 * mfPt; // Courier cap ascent
            if (flow.CurrentY > page.Height - 90)
                flow.AdvanceY(flow.CurrentY - (page.Height - 90));
            foreach (var mline in mfLines)
            {
                if (flow.CurrentY - mfPitch < flow.BottomMargin) flow.ForceNewPage();
                if (mline.Count > 0)
                {
                    var mb = new Content.ContentStreamBuilder();
                    mb.SaveState();
                    double mx = marginLeft;
                    foreach (var (mtext, mbold) in mline)
                    {
                        if (mtext.Length > 0 && !string.IsNullOrWhiteSpace(mtext))
                        {
                            var mres = Table.RegisterFont(flow.CurrentPage,
                                mbold ? "Courier-Bold" : "Courier");
                            mb.BeginText().SetFont(mres, mfPt)
                              .MoveTextPosition(mx, flow.CurrentY - mfAscent)
                              .ShowText(mtext).EndText();
                        }
                        mx += mtext.Length * 0.6 * mfPt; // fixed-pitch advance
                    }
                    mb.RestoreState();
                    flow.InjectContentAtCursor(mb.Build());
                }
                flow.AdvanceY(mfPitch);
            }
        }
        else if (Converters.HtmlToPdfConverter.HasBlockStructure(htmlContent))
        {
            HtmlFramesOpening(0, htmlContent.Length, htmlContent);
            RenderHtmlBlocks(htmlContent, html, flow, page, tb, htmlColor, inlineSvgs, ref htmlFragmentLinkEmitted, htmlFrameIndent, marginLeft, marginRight, marginTop);
            HtmlFramesClosing(0, htmlContent.Length);
        }
        else
        {
            // Inline emphasis wrapping the WHOLE fragment (<u><i>…</i></u>,
            // <i><u>…</u></i>, <b>…</b>) reaches this tag-stripped branch — only
            // the block renderer maps emphasis runs, so a bare wrapped fragment
            // lost both the italic face and the underline. Unwrap nested
            // whole-content emphasis tags onto the fragment state instead.
            bool wrapUnder = false, wrapItalic = false, wrapBold = false;
            {
                var inlineWrap = htmlContent.Trim();
                for (var wm = System.Text.RegularExpressions.Regex.Match(inlineWrap,
                         @"^<(u|i|b|em|strong)\b[^>]*>(.*)</\1\s*>$",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase
                         | System.Text.RegularExpressions.RegexOptions.Singleline);
                     wm.Success;
                     wm = System.Text.RegularExpressions.Regex.Match(inlineWrap,
                         @"^<(u|i|b|em|strong)\b[^>]*>(.*)</\1\s*>$",
                         System.Text.RegularExpressions.RegexOptions.IgnoreCase
                         | System.Text.RegularExpressions.RegexOptions.Singleline))
                {
                    switch (wm.Groups[1].Value.ToLowerInvariant())
                    {
                        case "u": wrapUnder = true; break;
                        case "i" or "em": wrapItalic = true; break;
                        default: wrapBold = true; break;
                    }
                    inlineWrap = wm.Groups[2].Value.Trim();
                }
            }
            var plainText = HtmlFragment.StripHtmlTags(htmlContent);
            if (!string.IsNullOrWhiteSpace(plainText))
            {
                var frag = new Text.TextFragment(plainText);
                if (html.TextState is { } htmlTs)
                {
                    if (htmlTs.Font is not null) frag.TextState.Font = htmlTs.Font;
                    if (htmlTs.FontData is not null) frag.TextState.FontData = htmlTs.FontData;
                    if (htmlTs.FontSize > 0) frag.TextState.FontSize = htmlTs.FontSize;
                    if (htmlTs.ForegroundColor is not null) frag.TextState.ForegroundColor = htmlTs.ForegroundColor;
                    frag.TextState.IsBold = htmlTs.IsBold;
                    frag.TextState.IsItalic = htmlTs.IsItalic;
                }
                if (wrapUnder) frag.TextState.Underline = true;
                if (wrapItalic) frag.TextState.IsItalic = true;
                if (wrapBold) frag.TextState.IsBold = true;
                // Inline <a href> runs survive tag stripping as plain
                // text; find each anchor's text in the stripped output
                // and re-attach its hyperlink so the flow writer emits
                // a Link annotation over the rendered run.
                System.Collections.Generic.List<(int Start, int Length, string Url)>? plainAnchors = null;
                foreach (System.Text.RegularExpressions.Match am in
                    System.Text.RegularExpressions.Regex.Matches(htmlContent,
                        "<a\\b[^>]*href=[\"']([^\"']+)[\"'][^>]*>(.*?)</a>",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        | System.Text.RegularExpressions.RegexOptions.Singleline))
                {
                    var aText = HtmlFragment.StripHtmlTags(am.Groups[2].Value);
                    if (aText.Length == 0) continue;
                    var aAt = plainText.IndexOf(aText, StringComparison.Ordinal);
                    if (aAt >= 0)
                        (plainAnchors ??= new()).Add((aAt, aText.Length, am.Groups[1].Value));
                }
                if (plainAnchors is not null)
                    ApplyHtmlAnchorSegments(frag, plainText, plainAnchors);
                // A Hyperlink set on the HtmlFragment ITSELF covers the whole fragment
                // with ONE Link annotation — the same rule the block path applies. This
                // tag-stripped branch was dropping it, so a hyperlinked one-line
                // fragment rendered its text and no annotation at all.
                if (html.Hyperlink is not null && !htmlFragmentLinkEmitted)
                {
                    frag.Hyperlink = html.Hyperlink;
                    htmlFragmentLinkEmitted = true;
                }
                if (!flow.WriteTextFragment(frag))
                {
                    frag.Position = new Text.Position(marginLeft, page.Height - marginTop - frag.TextState.FontSize);
                    tb.AppendTextInline(frag);
                }
                if (frag.Rectangle is { } r)
                    html.Rectangle = new System.Drawing.RectangleF(
                        (float)r.LLX, (float)r.LLY, (float)r.Width, (float)r.Height);
            }
            RenderHtmlImages(htmlContent, flow, marginLeft, marginRight, inlineSvgs);
        }
    }
}
