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
    /// <summary>A plain HTML fragment lays out as wrapped text lines with its inline styles and links.</summary>
    private void LayoutHtmlPlainFragment(HtmlFragmentLayoutState hl, List<byte[]> inlineSvgs)
    {
        // Inline emphasis wrapping the WHOLE fragment (<u><i>…</i></u>,
        // <i><u>…</u></i>, <b>…</b>) reaches this tag-stripped branch — only
        // the block renderer maps emphasis runs, so a bare wrapped fragment
        // lost both the italic face and the underline. Unwrap nested
        // whole-content emphasis tags onto the fragment state instead.
        var (wrapUnder, wrapItalic, wrapBold) = InlineWrapStyles(hl.htmlContent);
        var plainText = HtmlFragment.StripHtmlTags(hl.htmlContent);
        if (!string.IsNullOrWhiteSpace(plainText))
        {
            var frag = new Text.TextFragment(plainText);
            if (hl.html.TextState is { } htmlTs)
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
            // A full-document fragment that names nothing sets in the UA serif on
            // its normal line box (12 pt on a 13.5 pt pitch, the first baseline
            // seated inside the box) — see UaDefaultFaceFor.
            var plainFs = hl.html.TextState is { FontSizeTouched: true } pts && pts.FontSize > 0 ? pts.FontSize : 0;
            var bareFaced = plainFs > 0 || hl.html.TextState?.Font is not null
                || hl.html.TextState?.FontData is not null || wrapUnder || wrapItalic || wrapBold;
            if (UaDefaultFaceFor(hl.html, hl.page, plainFs, null) is { } uaFace)
            {
                bareFaced = true;
                frag.TextState.Font = uaFace;
                frag.TextState.FontSize = (float)UaSerifPt;
                frag.TextState.LineSpacing = (float)(UaSerifPitchPt - UaSerifPt);
                frag.TextState.LineSpacingSynthetic = true;
                frag.TextState.CssLineBoxSeat = true;
            }
            // Inline <a href> runs survive tag stripping as plain
            // text; find each anchor's text in the stripped output
            // and re-attach its hyperlink so the flow writer emits
            // a Link annotation over the rendered run.
            System.Collections.Generic.List<(int Start, int Length, string Url)>? plainAnchors = null;
            foreach (System.Text.RegularExpressions.Match am in
                System.Text.RegularExpressions.Regex.Matches(hl.htmlContent,
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
            if (hl.html.Hyperlink is not null && !hl.htmlFragmentLinkEmitted)
            {
                frag.Hyperlink = hl.html.Hyperlink;
                hl.htmlFragmentLinkEmitted = true;
            }
            if (!hl.flow.WriteTextFragment(frag))
            {
                frag.Position = new Text.Position(hl.marginLeft, hl.page.Height - hl.marginTop - frag.TextState.FontSize);
                hl.tb.AppendTextInline(frag);
            }
            if (frag.Rectangle is { } r)
                hl.html.Rectangle = new System.Drawing.RectangleF(
                    (float)r.LLX, (float)r.LLY, (float)r.Width, (float)r.Height);
            // A BARE fragment (no face or size of its own) reports the UA serif's
            // metrics: its width in Times New Roman 12 and, as its height, that line's
            // baseline drop (probed: "Hello WOrld..." reports 71.77 x 10.7988) - the
            // geometry the reference engine lays it out on, whatever face this page's
            // calibrated renders draw it in.
            if (frag.Rectangle is not null && !bareFaced)
                ReportUaSerifRectangle(hl.html, plainText);
        }
        RenderHtmlImages(hl.htmlContent, hl.flow, hl.marginLeft, hl.marginRight, inlineSvgs);
    }

    /// <summary>A bare fragment holding del / ins runs sets in the UA serif on the UA
    /// line, each run boxed in its background and ruled through (del) or under (ins)
    /// on the HTML dialect's geometry (probed: a 12 pt line, backgrounds 13.289 tall
    /// from 0.105 under the line top, a 1.2 pt strike 3.1 above the baseline and a
    /// 1.2 pt underline 1.2 below it, the text wrapping as one paragraph).</summary>
    private bool LayoutDelInsRuns(HtmlFragmentLayoutState hl, List<(string Text, Color? Bg, bool Del, bool Ins)> diRuns)
    {
        var face = SafeFindFont("Times New Roman");
        if (face?.SourceFontData?.TtfData is not { Length: > 0 }) return false;
        var runs = new List<FlowLayout.InlineRun>();
        foreach (var (text, bg, del, ins) in diRuns)
            runs.Add(new FlowLayout.InlineRun
            {
                Text = text, Size = UaSerifPt, Pitch = UaSerifPitchPt, Group = 0,
                State = new Text.TextState { Font = face, ForegroundColor = hl.htmlColor },
                Background = bg, HtmlDeco = del || ins, Strike = del, Underline = ins,
            });
        hl.flow.WriteInlineParagraph(runs, HorizontalAlignment.Left);
        return true;
    }

    /// <summary>The emphasis a plain fragment's outermost inline wrappers declare:
    /// every u / i / em / b / strong that encloses the whole content, innermost included.</summary>
    private static (bool Under, bool Italic, bool Bold) InlineWrapStyles(string htmlContent)
    {
        bool under = false, italic = false, bold = false;
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
                case "u": under = true; break;
                case "i" or "em": italic = true; break;
                default: bold = true; break;
            }
            inlineWrap = wm.Groups[2].Value.Trim();
        }
        return (under, italic, bold);
    }

    /// <summary>The rectangle a bare in-page fragment reports: the plain text's
    /// pair-kerned width in the UA serif at the UA size and the UA line's baseline
    /// drop as its height.</summary>
    private static void ReportUaSerifRectangle(HtmlFragment html, string plainText)
    {
        var width = Table.UaSerifKernedWidth(plainText, UaSerifPt);
        if (width <= 0) return;
        var drop = Table.UaSerifLineBox(UaSerifPt).Drop;
        if (drop <= 0) return;
        var r = html.Rectangle;
        html.Rectangle = new System.Drawing.RectangleF(r.X, r.Y, (float)width, (float)drop);
    }

    /// <summary>A monospace pre-formatted report lays out as verbatim Courier line boxes on its own pitch.</summary>
    private void LayoutMonoFontLineBoxes(HtmlFragmentLayoutState hl, double mfPt, List<List<(string text, bool bold)>> mfLines)
    {
        // Monospace pre-formatted report: verbatim Courier line boxes
        // (every &nbsp; a real column space, every <br/> a hard line)
        // on the dialect's 1.377 em line pitch. The report's content
        // box starts 90 pt from the page top (the dialect's own top
        // margin), below the ambient flow top when that sits higher.
        var mfPitch = mfPt * 1.377;
        var mfAscent = 0.562 * mfPt; // Courier cap ascent
        if (hl.flow.CurrentY > hl.page.Height - 90)
            hl.flow.AdvanceY(hl.flow.CurrentY - (hl.page.Height - 90));
        foreach (var mline in mfLines)
        {
            if (hl.flow.CurrentY - mfPitch < hl.flow.BottomMargin) hl.flow.ForceNewPage();
            if (mline.Count > 0)
            {
                var mb = new Content.ContentStreamBuilder();
                mb.SaveState();
                double mx = hl.marginLeft;
                foreach (var (mtext, mbold) in mline)
                {
                    if (mtext.Length > 0 && !string.IsNullOrWhiteSpace(mtext))
                    {
                        var mres = Table.RegisterFont(hl.flow.CurrentPage,
                            mbold ? "Courier-Bold" : "Courier");
                        mb.BeginText().SetFont(mres, mfPt)
                          .MoveTextPosition(mx, hl.flow.CurrentY - mfAscent)
                          .ShowText(mtext).EndText();
                    }
                    mx += mtext.Length * 0.6 * mfPt; // fixed-pitch advance
                }
                mb.RestoreState();
                hl.flow.InjectContentAtCursor(mb.Build());
            }
            hl.flow.AdvanceY(mfPitch);
        }
    }

    /// <summary>Nested styled spans lay out as one text fragment per style run.</summary>
    private void LayoutNestedStyledSpans(HtmlFragmentLayoutState hl, List<(string Text, double SizePt, Color? Bg)> nsRuns)
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
            if (hl.html.TextState?.Font is { } nsFont) nsState.Font = nsFont;
            var sz = nsSize > 0 ? nsSize : 10;
            if (sz > nsMax) nsMax = sz;
            nsStyled.Add(new FlowLayout.StyledRun
            {
                Text = nsText, Size = sz, State = nsState,
            });
        }
        hl.flow.WriteStyledParagraph(nsStyled, nsMax * 0.12);
    }

    /// <summary>A fragment holding a table lays it out as a table; false when the paragraph is finished here.</summary>
    private bool LayoutHtmlTableFragment(HtmlFragmentLayoutState hl, List<byte[]> inlineSvgs)
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
                hl.htmlContent, @"<html[\s>]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && hl.html.TextState?.Font is null
            && string.IsNullOrEmpty(hl.html.TextState?.FontName);
        if (uaSerifFrag)
            foreach (System.Text.RegularExpressions.Match uwm in
                System.Text.RegularExpressions.Regex.Matches(hl.htmlContent,
                    @"<table\b[^>]*\bwidth\s*=\s*[""']?(\d+)(?![\d%])",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                uaWideBoxPt = Math.Max(uaWideBoxPt,
                    double.Parse(uwm.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture) * 0.75);
        uaSerifFrag &= uaWideBoxPt > hl.page.Width - hl.marginLeft - hl.marginRight;
        // The segments concatenate back to htmlContent, so a running
        // offset places each one in the source — which is how the
        // framed-block spans above are expressed.
        // Verdana form-grid document: a width-percent wrapper div
        // whose cells declare inline Verdana spans throughout —
        // every table chunk below takes the dialect.
        var vgDoc = System.Text.RegularExpressions.Regex.IsMatch(
                hl.htmlContent, @"^\s*<div[^>]*style\s*=\s*'[^']*width:\s*\d+%",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && System.Text.RegularExpressions.Regex.Matches(
                hl.htmlContent, @"font-family:\s*Verdana",
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
                System.Text.RegularExpressions.Regex.Matches(hl.htmlContent,
                    @"<\s*(/?)div\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                vgDepth += dm.Groups[1].Value.Length > 0 ? -1 : 1;
                if (vgDepth == 0) { vgWrapClose = dm.Index; break; }
            }
        }
        hl.chunkAt = 0;
        // Every prose piece on the UA flow: the fragment's sheet-styled tables take the UA
        // table model between them (a section report); one piece the flow declines keeps
        // the whole fragment on the calibrated engines.
        hl.uaFlowProse = Converters.HtmlToPdfConverter.SegmentHtmlTables(hl.htmlContent)
            .All(s => s.isTable || IsUaFlowHtml(s.html) || IsContentFreeHtml(s.html));
        foreach (var (isTable, chunk) in Converters.HtmlToPdfConverter.SegmentHtmlTables(hl.htmlContent))
        {
            if (!LayoutHtmlTableChunk(hl, inlineSvgs, vgDoc, uaSerifFrag, uaWideBoxPt, vgWrapClose, isTable, chunk)) break;
        }
        return true;
    }

    /// <summary>The centered filing-letter dialect: letterhead at natural size, then Times lines on a 4 em rhythm; false when the paragraph is finished here.</summary>
    private bool LayoutFilingLetter(HtmlFragmentLayoutState hl, List<Converters.HtmlToPdfConverter.FilingItem> flItems)
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
            if (fl.ExtraGap > 0) hl.flow.AdvanceY(fl.ExtraGap);
            if (fl.ImgSrc is not null)
            {
                var fbytes = LoadHtmlImageBytes(fl.ImgSrc);
                if (fbytes is not null)
                {
                    // css-pixel sizing: the letter scales its
                    // letterhead at 0.75 pt per image pixel
                    var (fw, fh) = TryGetImageNaturalSizePt(fbytes, false) ?? (0, 0);
                    fw *= 0.75;
                    fh *= 0.75;
                    if (fw <= 0 || fh <= 0) { fw = 187.5; fh = 75; }
                    var fx = (hl.page.Width - fw) / 2;
                    var fTop = hl.flow.CurrentY;
                    hl.flow.CurrentPage.AddImage(fbytes,
                        new Rectangle(fx, fTop - fh, fx + fw, fTop));
                    hl.flow.AdvanceY(fh);
                }
                continue;
            }
            if (fl.Blank) { hl.flow.AdvanceY(flPitch); continue; }
            if (fl.Text is not { } ftext) continue;
            var fres = Table.RegisterFont(hl.flow.CurrentPage, "Times-Roman");
            var maxW = hl.page.Width - 144 - fl.IndentPt;
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
                if (hl.flow.CurrentY - flDrop < hl.flow.BottomMargin) hl.flow.ForceNewPage();
                var lw = FlMeasure(lt);
                var lx = fl.AlignLeft ? 72 + fl.IndentPt
                    : Math.Max(72, (hl.page.Width - lw) / 2);
                var fb = new Content.ContentStreamBuilder();
                fb.SaveState();
                fb.BeginText().SetFont(fres, flFs)
                  .MoveTextPosition(lx, hl.flow.CurrentY - flDrop)
                  .ShowText(lt).EndText();
                fb.RestoreState();
                hl.flow.InjectContentAtCursor(fb.Build());
                hl.flow.AdvanceY(flPitch);
            }
        }
        return true;
    }

    /// <summary>A fragment whose load options declare page margins lays out in its own box inside the page's content box; false when the paragraph is finished here.</summary>
    private bool LayoutMarginAssignedFragment(HtmlFragmentLayoutState hl, PageInfo mfPi)
    {
        var mf = new MarginFragmentState();
        mf.mfSize = hl.html.TextState is { } mfTs && mfTs.FontSize > 0 ? (double)mfTs.FontSize : 10.0;
        mf.mfFirst = mfPi.Margin;
        mf.mfRest = mfPi.AnyMarginAssigned ? mfPi.AnyMargin : mfPi.Margin;
        mf.mfLeft = hl.marginLeft + mf.mfFirst.Left;
        mf.mfRight = hl.page.Width - hl.marginRight - mf.mfFirst.Right;
        mf.mfWidth = Math.Max(1, mf.mfRight - mf.mfLeft);
        mf.mfBottom = hl.page.Height - hl.marginBottom - mf.mfFirst.Bottom;
        mf.mfTopFirst = hl.marginTop + mf.mfFirst.Top;
        mf.mfTopRest = hl.marginTop + mf.mfRest.Top;

        mf.mfStrut = Converters.HtmlToPdfConverter.Std14Face(
            hl.html.TextState?.Font?.FontName, false, false);

        mf.mfLines = new List<(List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)> Pieces,
            double Above, double Below)>();

        foreach (var mfPara in Converters.HtmlToPdfConverter.ParseFlowParagraphs(hl.htmlContent))
        {
            if (!WrapMarginAssignedParagraph(hl, mf, mfPara)) break;
        }

        mf.mfY = mf.mfTopFirst;
        mf.mfPageTop = mf.mfTopFirst;
        mf.mfBuilder = new Content.ContentStreamBuilder();
        mf.mfBuilder.SaveState();
        mf.mfDrew = false;

        mf.mfPrevBelow = 0.0;
        mf.mfOnPage = 0;
        for (var li = 0; li < mf.mfLines.Count; li++)
        {
            if (!LayoutMarginAssignedLine(hl, mf, li)) break;
        }
        mf.mfBuilder.RestoreState();
        if (mf.mfDrew) hl.flow.InjectContentAtCursor(mf.mfBuilder.Build());
        mf.mfEnd = hl.page.Height - (mf.mfY + mf.mfPrevBelow);
        if (hl.flow.CurrentY > mf.mfEnd) hl.flow.AdvanceY(hl.flow.CurrentY - mf.mfEnd);
        return true;
    }

    /// <summary>Lays out one segment of a mixed-content fragment: a table as a table, text between tables as flowed lines.</summary>
    private bool LayoutHtmlTableChunk(HtmlFragmentLayoutState hl, List<byte[]> inlineSvgs, bool vgDoc, bool uaSerifFrag, double uaWideBoxPt, int vgWrapClose, bool isTable, string chunk)
    {
        if (vgDoc && Environment.GetEnvironmentVariable("ASPOSE_HTML_DEBUG_FG") is not null)
            Console.WriteLine($"[chunk] table={isTable} len={chunk.Length} " +
                $"'{System.Text.RegularExpressions.Regex.Replace(chunk.Length > 70 ? chunk[..70] : chunk, @"\s+", " ")}'");
        var chunkEnd = hl.chunkAt + chunk.Length;
        HtmlFramesOpening(hl, hl.chunkAt, chunkEnd, chunk);
        if (isTable && uaSerifFrag)
        {
            RenderUaSerifTable(chunk, uaWideBoxPt, hl.flow, hl.page, hl.marginLeft);
        }
        else if (isTable && !vgDoc && TryLayoutUaCssTable(hl, chunk))
        {
            // A sheet-styled table between UA-flow prose pieces lays out on the UA table model.
        }
        else if (isTable)
        {
            LayoutTableSegment(hl, inlineSvgs, vgDoc, uaSerifFrag, uaWideBoxPt, vgWrapClose, chunk);
        }
        else if (uaSerifFrag) RenderUaSerifChunk(chunk, uaWideBoxPt, hl.html, hl.flow, hl.marginLeft);
        else if (!vgDoc && TryLayoutUaFlowFragment(hl, chunk, lastPiece: chunkEnd >= hl.htmlContent.Length))
        {
            // A prose piece between the tables of a mixed fragment sets on the UA
            // flow's line boxes (a section report: Times 12 strut, its tables between).
        }
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
                hl.flow.AdvanceY(VgBrBoxPt(hl, vgWrapClose, hl.chunkAt + brm.Index));
        else if (vgDoc && TryVgFlowText(hl, vgWrapClose, chunk, hl.chunkAt))
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
                    hl.flow.AdvanceY(VgBrBoxPt(hl, vgWrapClose, hl.chunkAt + brm.Index));
            RenderHtmlBlocks(chunk, hl.html, hl.flow, hl.page, hl.tb, hl.htmlColor, inlineSvgs, hl, hl.htmlFrameIndent, hl.marginLeft, hl.marginRight, hl.marginTop);
        }
        HtmlFramesClosing(hl, hl.chunkAt, chunkEnd);
        hl.chunkAt = chunkEnd;
        return true;
    }

    /// <summary>A table segment of a mixed-content fragment builds its table and lays it out, on the Verdana grid when the document is one.</summary>
    private void LayoutTableSegment(HtmlFragmentLayoutState hl, List<byte[]> inlineSvgs, bool vgDoc, bool uaSerifFrag, double uaWideBoxPt, int vgWrapClose, string chunk)
    {
        var isLayout = Converters.HtmlToPdfConverter.IsLayoutTableHtml(chunk);
        var chunkCss = isLayout
            ? Converters.HtmlToPdfConverter.ParseStyleSheet(hl.htmlContent)
            : TableSheetForChunk(hl.htmlContent, chunk);
        // a percentage width resolves against the body's own
        // declared width when the document states one
        var layoutAvail = hl.page.Width - hl.marginLeft - hl.marginRight;
        if (uaSerifFrag) layoutAvail = uaWideBoxPt;
        if (isLayout
            && Converters.HtmlToPdfConverter.DeclaredBodyWidthPt(hl.htmlContent) is > 0 and var bw)
            layoutAvail = bw;
        // The document's own `body { }` type is the grid's base
        // too: a table inherits the page's face and size rather
        // than falling back to the 11 pt Standard-14 default
        // while the prose around it sets in the declared face.
        var tblBodyCss = Converters.HtmlToPdfConverter.BodyCssFont(hl.htmlContent);
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
            hl.htmlContent, @"^\s*<div[^>]*style\s*=\s*'[^']*width:\s*(\d+)%",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var vgSize = System.Text.RegularExpressions.Regex.Match(
            chunk, @"font-size:\s*([\d.]+)\s*pt",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!vgSize.Success)
            vgSize = System.Text.RegularExpressions.Regex.Match(
                hl.htmlContent, @"font-size:\s*([\d.]+)\s*pt",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var verdanaGrid = !isLayout && vgDoc && vgWrap.Success
            && vgSize.Success;
        var vgPt = verdanaGrid
            ? double.Parse(vgSize.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture)
            : 0;
        var vgInWrap = hl.chunkAt < vgWrapClose;
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
        (var t, _) = Converters.HtmlToPdfConverter.BuildTableFromHtml(chunk, layoutAvail, hl.html.HtmlLoadOptions, inlineSvgs, chunkCss, false, false, verdanaGrid ? vgStrutPt : 0, verdanaGrid ? vgPt : tblBodyCss.SizePt, false, isLayout, // …and the face's own `line-height: normal` box, // so a cell line steps on the same rhythm the
            // prose does (Arial 12 → 13.5, not a bare 12).
            cssRunFace: verdanaGrid ? null : tblBodyCss.Face, defaultCellFace: verdanaGrid ? null : tblBodyCss.Face, formGridDialect: verdanaGrid, formGridStrutPt: vgStrutPt, formGridStrutDropPt: vgStrutDropPt);
        if (verdanaGrid && t is not null)
        {
            LayoutVerdanaGridTable(t, layoutAvail, chunk);
        }
        if (t is not null)
        {
            PlaceTableSegment(hl, t, layoutAvail, isLayout, chunk);
        }
    }

    /// <summary>A table on the Verdana grid document takes the grid's strut and row pitch before it is laid out.</summary>
    private void LayoutVerdanaGridTable(Table t, double layoutAvail, string chunk)
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

    /// <summary>Writes one wrapped line of a margin-assigned fragment, breaking to a new page at its bottom margin.</summary>
    private bool LayoutMarginAssignedLine(HtmlFragmentLayoutState hl, MarginFragmentState mf, int li)
    {
        var (pieces, above, below) = mf.mfLines[li];
        var baseline = mf.mfOnPage == 0 ? mf.mfPageTop + above : mf.mfY + mf.mfPrevBelow + above;
        if (baseline + below > mf.mfBottom + 0.01)
        {
            MfNewPage(mf, hl);
            mf.mfOnPage = 0;
            baseline = mf.mfPageTop + above;
        }
        foreach (var (text, run, face, x, w) in pieces)
        {
            if (text.Trim().Length == 0) continue;
            var res = Table.RegisterFont(hl.flow.CurrentPage, face);
            var py = hl.page.Height - baseline;
            if (run.Back is { } bg)
            {
                // the highlight fills the run's content area:
                // no half-leading, just ascent+descent
                var hTop = baseline - Converters.HtmlToPdfConverter.FaceAscent(face, mf.mfSize);
                var hH = Converters.HtmlToPdfConverter.FaceAscent(face, mf.mfSize)
                         + Converters.HtmlToPdfConverter.FaceDescent(face, mf.mfSize);
                mf.mfBuilder.SetFillColor(bg)
                         .Rectangle(mf.mfLeft + x, hl.page.Height - hTop - hH, w, hH)
                         .Fill();
            }
            if (run.Fore is { } fg) mf.mfBuilder.SetFillColor(fg);
            else mf.mfBuilder.SetFillGray(0);
            mf.mfBuilder.BeginText().SetFont(res, mf.mfSize)
                     .MoveTextPosition(mf.mfLeft + x, py)
                     .ShowText(text).EndText();
            mf.mfDrew = true;
        }
        mf.mfY = baseline - above;
        mf.mfPrevBelow = below;
        mf.mfY = baseline;      // track the baseline for the next step
        mf.mfOnPage++;
        return true;
    }

    /// <summary>Wraps one flow paragraph of a margin-assigned fragment into lines at its declared width.</summary>
    private bool WrapMarginAssignedParagraph(HtmlFragmentLayoutState hl, MarginFragmentState mf, Converters.HtmlToPdfConverter.FlowPara mfPara)
    {
        var pieces = new List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)>();
        var x = 0.0;
        var above = Converters.HtmlToPdfConverter.FaceAbove(mf.mfStrut, mf.mfSize);
        var below = Converters.HtmlToPdfConverter.FaceBelow(mf.mfStrut, mf.mfSize);
        var lineStarted = false;

        void MfFlush()
        {
            // trailing space never holds a line open
            while (pieces.Count > 0 && pieces[^1].Item1.Trim().Length == 0)
                pieces.RemoveAt(pieces.Count - 1);
            mf.mfLines.Add((new List<(string Text, Converters.HtmlToPdfConverter.FlowRun Run, string Face, double X, double W)>(pieces),
                above, below));
            pieces.Clear();
            x = 0;
            above = Converters.HtmlToPdfConverter.FaceAbove(mf.mfStrut, mf.mfSize);
            below = Converters.HtmlToPdfConverter.FaceBelow(mf.mfStrut, mf.mfSize);
            lineStarted = false;
        }


        foreach (var run in mfPara.Runs)
        {
            if (run.HardBreak) { MfFlush(); continue; }
            var face = MfFace(mf, hl, run);
            var runAbove = Converters.HtmlToPdfConverter.FaceAbove(face, mf.mfSize);
            var runBelow = Converters.HtmlToPdfConverter.FaceBelow(face, mf.mfSize);
            // split into words, keeping each word's leading space
            foreach (System.Text.RegularExpressions.Match wm in System.Text.RegularExpressions.Regex.Matches(run.Text, @" *[^ ]+| +"))
            {
                var word = wm.Value;
                var atLineStart = !lineStarted;
                var draw = atLineStart ? word.TrimStart(' ') : word;
                if (draw.Length == 0) continue;
                var w = MfWidthOf(mf, hl, draw, face);
                if (lineStarted && x + w > mf.mfWidth + 0.01 && draw.Trim().Length > 0)
                {
                    MfFlush();
                    draw = word.TrimStart(' ');
                    if (draw.Length == 0) continue;
                    w = MfWidthOf(mf, hl, draw, face);
                }
                pieces.Add((draw, run, face, x, w));
                x += w;
                lineStarted = true;
                above = Math.Max(above, runAbove);
                below = Math.Max(below, runBelow);
            }
        }
        MfFlush();
        return true;
    }

    /// <summary>Lays the built table into the flow and advances past its markup.</summary>
    private void PlaceTableSegment(HtmlFragmentLayoutState hl, Table t, double layoutAvail, bool isLayout, string chunk)
    {
        if (isLayout)
        {
            if (hl.html.Margin is { Top: > 0 } lmt) hl.flow.AdvanceY(lmt.Top);
            // breaks written between rows sit above the table
            var fostered = Converters.HtmlToPdfConverter.FosterParentedBreaks(chunk);
            if (fostered > 0) hl.flow.AdvanceY(fostered * 11.25);
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
            var usedH = RenderLayoutTable(t, -1, boxFloor, hl.flow.CurrentY, hl.flow, hl.marginLeft, hl.renderedTables);
            hl.flow.AdvanceY(usedH);
        }
        else RenderHtmlTable(t, hl.flow, hl.page, hl.marginLeft, hl.marginTop, hl.overflowPages, hl.overflowImages);
    }
}
