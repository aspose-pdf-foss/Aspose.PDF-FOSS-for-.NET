using System.Text;
using System.Text.RegularExpressions;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The side margin a body background keeps clear (the page's own left/right margin,
    /// unlike the 72 pt band it leaves at the top and bottom).</summary>
    private const double CanvasSideMarginPt = 90.0;

    /// <summary>The body background paints under the content, unused fonts are pruned, the logical structure is built on request, and a single-page or scaled document is fitted to its page.</summary>
    private static void FinishCanvasAndPageFit(ConvertState cv, HtmlLoadOptions? options)
    {
        if (cv.bodyBackground is { } canvasFill)
        {
            var canvasLeft = cv.marginsExplicit ? cv.marginLeft : CanvasSideMarginPt;
            // The canvas keeps the page's own SIDE margin, not the top/bottom one: measured on
            // every reference document in the corpus that colours its body, the fill ends one left
            // margin short of the sheet - on sheets of 594.72, 629.76, 678.72, 752.16 and 1096.32
            // the fill's right edge is 505.92, 540.48, 589.44, 663.36 and 1007.04, i.e. 90 within
            // a render pixel every time. At 72 the fill was the widest ink on the page and it,
            // not the content, sized a grown sheet.
            var canvasRight = cv.pageWidth - (cv.marginsExplicit ? cv.marginRight : CanvasSideMarginPt);
            var canvasTop = cv.pageHeight - (cv.marginsExplicit ? cv.marginTop : 72.0);
            var canvasBottom = cv.marginsExplicit ? cv.marginBottom : 72.0;
            foreach (var bp in cv.doc.Pages)
                DrawBox(bp, canvasLeft, canvasBottom, canvasRight - canvasLeft,
                    canvasTop - canvasBottom, null, 0, canvasFill, prepend: true);
        }

        if (cv.flow.usedCustomFont) PruneUnusedFonts(cv.doc);

        // Build a logical-structure (tagged) tree when the caller asked for it.
        // The tree mirrors the HTML element hierarchy (headings, paragraphs, lists,
        // figures, links) rather than the flattened layout blocks.
        if (options?.CreateLogicalStructure == true)
            BuildLogicalStructure(cv.doc, cv.html);

        // IsRenderToSinglePage fix-up: the flow above ran unbroken against the
        // coordinate ceiling; the single sheet's height is the whole number of
        // authored content bands the flow fills (never less than one), and the
        // content shifts down from the ceiling onto it.
        if (cv.singlePage && cv.doc.Pages.Count == 1)
        {
            var band = cv.singlePageRealH - cv.marginTop - cv.marginBottom;
            if (band > 0)
            {
                var contentBottomTd = cv.pageHeight - cv.flow.y;
                var bands = Math.Max(1, (int)Math.Ceiling((contentBottomTd - cv.marginTop) / band));
                var finalH = bands * band;
                var shift = cv.pageHeight - finalH;
                var spInv = System.Globalization.CultureInfo.InvariantCulture;
                var spPage = cv.doc.Pages[1];
                spPage.PrependContentStream(Encoding.ASCII.GetBytes(Compat.Format(spInv,
                    $"q 1 0 0 1 0 {-shift:F2} cm\n")));
                spPage.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                spPage.MediaBox = new Rectangle(0, 0, cv.pageWidth, finalH);
            }
        }

        // ScaleToPageWidth: shrink each natural-width page uniformly onto the
        // authored sheet — content pinned at the left page margin and the page
        // top (x' = mL + (x−mL)·s, top-down y' = y·s).
        if (cv.scalePendingS is > 0 and < 1)
        {
            var s = cv.scalePendingS;
            var sInv = System.Globalization.CultureInfo.InvariantCulture;
            var pmL = cv.pageMargin?.Left ?? 0;
            foreach (var sp in cv.doc.Pages)
            {
                sp.PrependContentStream(Encoding.ASCII.GetBytes(Compat.Format(sInv,
                    $"q {s:F5} 0 0 {s:F5} {pmL * (1 - s):F2} {cv.scaleReqPageH * (1 - s):F2} cm\n")));
                sp.AddContentStream(Encoding.ASCII.GetBytes("Q\n"));
                sp.MediaBox = new Rectangle(0, 0, cv.scaleReqPageW, cv.scaleReqPageH);
            }
        }
    }

    /// <summary>Grid radio pages get their fields, the first page its footer image, and every pending link its annotation or anchor jump.</summary>
    private static void FinishRadioGroupsFooterAndLinks(ConvertState cv, HtmlLoadOptions? options)
    {
        foreach (var (gridRbf, gridRbfPage) in cv.profile.gridRadioPages)
            try { cv.doc.Form.Add(gridRbf, gridRbfPage.Number); }
            catch { /* best-effort radio emission */ }

        // Render the running <header>/<footer> on every page (pulled out of the flow above so
        // they repeat rather than appearing once). Emitted after body content in the reserved
        // top/bottom bands.
        if (!string.IsNullOrEmpty(cv.runHeader) || !string.IsNullOrEmpty(cv.runFooter))
        {
            var invHf = System.Globalization.CultureInfo.InvariantCulture;
            foreach (Page pg in cv.doc.Pages)
            {
                EnsureFonts(pg, cv.docFontDict);
                void EmitRunning(string text, double ty)
                {
                    var s = new StringBuilder();
                    s.AppendLine("BT");
                    s.Append($"/F1 11.0 Tf ");
                    s.Append($"1 0 0 1 {cv.marginLeft.ToString("F2", invHf)} {ty.ToString("F2", invHf)} Tm ");
                    s.Append($"({EscapePdfString(text)}) Tj ");
                    s.AppendLine("ET");
                    pg.AddContentStream(Encoding.ASCII.GetBytes(s.ToString()));
                }
                if (!string.IsNullOrEmpty(cv.runHeader)) EmitRunning(cv.runHeader!, cv.pageHeight - 24);
                if (!string.IsNullOrEmpty(cv.runFooter)) EmitRunning(cv.runFooter!, 24);
            }
        }

        // The hoisted image-only <div id="footer"> logo: page 1, bottom margin,
        // CSS size (see the extraction above; the div was removed from the flow).
        if (cv.page1FooterImgSrc is not null && cv.doc.Pages.Count > 0)
        {
            var footBytes = LoadConverterImage(cv.page1FooterImgSrc, options);
            if (footBytes is not null)
            {
                var fw = cv.page1FooterImgW;
                var fh = cv.page1FooterImgH;
                if ((fw <= 0 || fh <= 0) && TryReadImagePixelSize(footBytes) is (var npw, var nph))
                {
                    if (fw <= 0) fw = npw * 0.75;  // CSS px → pt
                    if (fh <= 0) fh = nph * 0.75;
                }
                if (fw > 0 && fh > 0)
                    try
                    {
                        cv.doc.Pages[1].AddImage(footBytes, new Rectangle(
                            cv.marginLeft, cv.marginBottom, cv.marginLeft + fw, cv.marginBottom + fh));
                    }
                    catch { /* unreadable image: footer logo is skipped */ }
            }
        }

        // Second pass: emit link annotations now that every anchor target's page is
        // known. A #fragment href resolves to the page/y its named anchor rendered
        // on (an internal GoTo); any other href becomes an external URI link.
        foreach (var (lp, rect, url, text) in cv.pendingLinks)
        {
            Aspose.Pdf.Annotations.Annotation? annot = null;
            if (url.StartsWith("#", StringComparison.Ordinal))
            {
                var frag = url.Substring(1);
                if (frag.Length > 0 && cv.anchorTargets.TryGetValue(frag, out var tgt))
                    annot = lp.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                tgt.page.Number, 0, tgt.y, 0)));
                else if (frag.Length == 0 || frag.Equals("top", StringComparison.OrdinalIgnoreCase))
                    // "#" and "#top" are the HTML convention for the document top.
                    annot = lp.Annotations.AddLinkAnnotation(rect,
                        new Aspose.Pdf.Annotations.GoToAction(
                            new Aspose.Pdf.Annotations.XYZExplicitDestination(
                                1, 0, cv.pageHeight - cv.marginTop, 0)));
                // A dangling #fragment (no matching anchor) emits no link.
            }
            else
            {
                annot = lp.Annotations.AddLinkAnnotation(rect, url);
            }
            // The anchor's visible text becomes the link's /Contents (its tooltip).
            if (annot != null && !string.IsNullOrEmpty(text))
                annot.Contents = text;
        }
    }

    /// <summary>Every block lays out in order, the float-first operators are replayed, and the radio groups are emitted.</summary>
    private static void LayOutBlocks(ConvertState cv, HtmlLoadOptions? options)
    {
        foreach (var block in cv.blocks)
        {
            // a title column or control width declared in percent resolves here,
            // where the content width is known
            // the UA body content box: the sheet between symmetric page margins
            // (measured on the label/input form printed on Letter: 32 % lands 138 pt in)
            var percentBase = cv.pageWidth - 2 * cv.marginLeft;
            if (block.LeftIndentFrac > 0)
            {
                block.LeftIndent += block.LeftIndentFrac * percentBase
                    + (block.InputInColumn ? UaColumnGapPt : 0);
                block.LeftIndentFrac = 0;
            }
            if (block.InputWidthFrac > 0)
            {
                block.InputWidth = block.InputWidthFrac * percentBase;
                block.InputWidthFrac = 0;
            }
            RenderBlock(cv, options, cv.inlineSvgs, block);
        }
        // Unbalanced band/box markers must not leak a narrowed content box past the flow.
        cv.marginLeft = cv.flowMarginLeft;
        cv.flow.contentWidth = cv.flowContentWidth;

        // Prepend float-table ops (in reverse, so their relative order is preserved) —
        // float content leads its page's content stream, the paint
        // order that the fragment-index tests depend on.
        for (var fi = cv.floatFirstOps.Count - 1; fi >= 0; fi--)
            cv.floatFirstOps[fi].page.PrependContentStream(cv.floatFirstOps[fi].ops);

        // When custom (font-family) faces were embedded, drop the eager Standard-14
        // Helvetica/Courier resources that no content stream actually references, so the
        // document's font set reflects only the faces in use. Conversions that don't use
        // font-family keep the original eager fonts untouched (no behavioural change).
        // Build one RadioButtonField per HTML radio group (by name); each <input> becomes a
        // RadioButtonOptionField kid (circle style, visible border) so it surfaces on
        // Form.Fields after save+reload.
        EmitRadioGroups(cv.doc, cv.radioOptions);
    }

    /// <summary>A dead external stylesheet, the Word-filtered export and the wiki export each set the flow flags their dialect needs before the blocks lay out.</summary>
    private static void ResolveDeadCssAndDialects(ConvertState cv)
    {
        if (cv.profile.uaStdSerif && cv.profile.deadExternalCss)
        {
            // The section is its own page: the diagram table sits at most a
            // heading and an intro behind its page break (an unrelated wrapper
            // table elsewhere can carry the class string too — skip those).
            for (var ti = 0; ti < cv.blocks.Count; ti++)
            {
                if (!cv.blocks[ti].IsTable || !IsSpMatrixTable(cv.blocks[ti].TableHtml)) continue;
                var s0 = ti;
                while (s0 > 0 && !cv.blocks[s0].PageBreakBefore && ti - s0 <= 3) s0--;
                if (!cv.blocks[s0].PageBreakBefore || ti - s0 > 3) continue;
                var s1 = ti + 1;
                while (s1 < cv.blocks.Count && !cv.blocks[s1].PageBreakBefore) s1++;
                cv.spBlocks = new HashSet<Block>();
                for (var sbi = s0; sbi < s1; sbi++) cv.spBlocks.Add(cv.blocks[sbi]);
                cv.spFirst = cv.blocks[s0];
                break;
            }
        }

        // A top-level table opening after a body TEXT line sits a small gap
        // below it (measured: the summary table's top = the rating
        // line's bottom + 3.5 — identical in the shipped template and the
        // current engine's render).
        cv.flow.prevBlockWasText = false;
        cv.flow.lastWasMetricTable = false;
        cv.flow.lastBreakWasUaSpacer = false;
        // Float-label pairing state (see the consume site below).
        cv.flow.pendingFloatLabelPt = 0.0;
        cv.flow.pendingFloatLabelY = 0.0;

        // Word-filtered pages: an image too tall for one sheet spills over the page
        // boundary wherever it starts, so it begins on a FRESH sheet — and the quoted
        // section that introduces it (the rule-topped header block and the lines under
        // it) travels with it rather than being stranded at the foot of the previous
        // page. Collect the header blocks whose section ends in such an image; the flow
        // breaks before them.
        cv.msoKeepWithImage = null;
        if (cv.profile.msoFilteredDoc)
        {
            var blockList = cv.blocks as System.Collections.Generic.IList<Block> ?? new System.Collections.Generic.List<Block>(cv.blocks);
            var contentBand = cv.pageHeight - cv.marginTop - cv.marginBottom;
            for (var bi = 0; bi < blockList.Count; bi++)
            {
                var b = blockList[bi];
                if (b.ImageHeight <= 0 || string.IsNullOrEmpty(b.ImageSrc)) continue;
                var bw0 = b.ImageWidth * 0.75;
                var bh0 = b.ImageHeight * 0.75;
                if (cv.flow.contentWidth > 0 && bw0 > cv.flow.contentWidth) bh0 *= cv.flow.contentWidth / bw0;
                if (bh0 <= contentBand) continue;   // fits a sheet: the normal break applies
                for (var back = bi - 1; back >= 0 && bi - back <= 12; back--)
                {
                    if (!blockList[back].BorderTopOnly) continue;
                    // The section starts at the run of blank paragraphs that separates it
                    // from the message above — those trailing blanks travel with it, so the
                    // section lands the same distance down the fresh sheet as it sat below
                    // the previous message.
                    var head = back;
                    while (head > 0 && IsBlankFlowBlock(cv, blockList[head - 1])) head--;
                    (cv.msoKeepWithImage ??= new System.Collections.Generic.HashSet<Block>()).Add(blockList[head]);
                    break;
                }
            }
        }
        // MediaWiki rhythm state: whether the previous rendered block was a list
        // item, and whether a pin-button line just rendered (its widget box hands
        // extra lead to the block after it).
        cv.flow.wikiPrevListItem = false;
        cv.flow.wikiAfterButtons = false;
        PaintFlowLinks(cv);
    }

    /// <summary>Links of the MediaWiki and UA flows take the UA link ink and an underline run per anchor.</summary>
    private static void PaintFlowLinks(ConvertState cv)
    {
        // MediaWiki links draw in the UA default link ink, underlined (probed:
        // pure-blue runs with an fs/10 stroke). The plain serif writer carries
        // block-level ink only, and the menu items are fully-linked lines.
        if (cv.wikiExportDoc)
            foreach (var wb in cv.blocks)
                if (wb.Anchors is { Count: > 0 })
                {
                    wb.UnderlineRuns ??= new();
                    var wLinked = 0;
                    foreach (var (aS, aL, _) in wb.Anchors)
                    {
                        wb.UnderlineRuns.Add((aS, aL));
                        wLinked += aL;
                    }
                    if (wLinked >= wb.Text.TrimEnd().Length)
                        wb.ForeColor = WikiLinkInk;
                }
        // The UA flow's links draw in the UA link ink, underlined (probed: pure-blue
        // runs with a 0.1 em stroke 0.1 em under the baseline, the face inherited).
        if (cv.profile.uaStdSerif && !cv.wikiExportDoc && !cv.profile.dwFormDoc)
            foreach (var ub in cv.blocks)
                if (ub.Anchors is { Count: > 0 })
                {
                    ub.UnderlineRuns ??= new();
                    ub.ColorRuns ??= new();
                    var uLinked = 0;
                    foreach (var (aS, aL, _) in ub.Anchors)
                    {
                        ub.UnderlineRuns.Add((aS, aL));
                        ub.ColorRuns.Add((aS, aL, WikiLinkInk));
                        uLinked += aL;
                    }
                    if (uLinked >= ub.Text.TrimEnd().Length)
                        ub.ForeColor = WikiLinkInk;
                }
    }

    /// <summary>The flow's rule, table, band, box and column scopes start closed, and the UA top-margin pending flag is set.</summary>
    private static void ResetFlowScopes(ConvertState cv)
    {
        cv.flow.afterRuleDrop = false;
        // Set after a synthesized form-row table: flow text following it (the next
        // section heading) needs its line-box drop plus the section gap kept
        // between a form block and the heading below it.
        cv.flow.afterFhTable = false;
        cv.flowMarginLeft = cv.marginLeft;
        cv.flowContentWidth = cv.flow.contentWidth;
        cv.heightFloorStack = new Stack<(double Y, Page P)>();

        cv.bandStack = new Stack<(double SavedML, double SavedCW, double TopY, double MinEndY, Page StartPage)>();
        cv.boxStack = new Stack<(double XLeft, double TopY, double Width, double BorderPt, Page Page, double Gray, double PadSide, double SavedML, double SavedCW)>();
        cv.colScopeStack = new Stack<(double SavedML, double SavedCW)>();
        // A float column is an overflow-hidden box: content past the page bottom is
        // CLIPPED, never paginated — the column ends at the page's content bottom and
        // the rest of its blocks are dropped until the next column/band boundary.
        cv.flow.bandColClipped = false;
        // The last page any block drew on. A page-break-before must start a fresh page
        // whenever the current page already has content — the flow cursor alone can't
        // tell (a table continuation slice can leave y above the nominal content top).
        cv.flow.contentPage = null;

        // The full-document UA-default flow collapses the first flow block's top margin
        // into the document top margin (browser margin-collapse); true until that block
        // is laid out. The fieldset worksheet's body PADDING blocks that collapse.
        cv.flow.uaTopMarginPending = cv.profile.uaStdSerif && !cv.fieldsetDoc;

        // UA-flow floats: consecutive floated text blocks share ONE line (a right
        // float takes no vertical space of its own — the next float lays out level
        // with it). Every float in such a run except the last rewinds the cursor.
        if (cv.profile.uaStdSerif)
            for (var fi = 0; fi + 1 < cv.blocks.Count; fi++)
                if ((cv.blocks[fi].FloatLeft || cv.blocks[fi].FloatRight)
                    && (cv.blocks[fi + 1].FloatLeft || cv.blocks[fi + 1].FloatRight)
                    // …and only where each is a float in its OWN right. Blocks that merely sit
                    // inside one floated container inherit its flag (CSS does not inherit float;
                    // the flag is carried down so a nested image still leaves the flow), and
                    // levelling THOSE stacks a container's paragraphs on one line.
                    && !cv.blocks[fi].FloatInherited && !cv.blocks[fi + 1].FloatInherited
                    && !cv.blocks[fi].IsTable && !cv.blocks[fi + 1].IsTable
                    && !string.IsNullOrEmpty(cv.blocks[fi].Text)
                    && !string.IsNullOrEmpty(cv.blocks[fi + 1].Text))
                    cv.blocks[fi].NoAdvanceY = true;

        // Real-metric measurement face for a text block inside a float-column band: its
        // declared family, the bold variant for a bold run, falling back to the plain
        // family then Arial. Used only by the band flow (see the wrap call below).
        cv.spBlocks = null;
        cv.spFirst = null;
    }

    /// <summary>The per-conversion collections and float cursors start empty.</summary>
    private static void ResetFlowCollections(ConvertState cv, HtmlLoadOptions? options)
    {
        cv.sb = new StringBuilder();

        cv.embeddedFonts = new Dictionary<string, (string resName, Core.PdfIndirectRef fontRef)>(StringComparer.OrdinalIgnoreCase);
        cv.fontFileCache = new Dictionary<string, (int objNum, string embedName)>(StringComparer.Ordinal);


        cv.radioOptions = new List<(string group, bool chk, Page page, Rectangle rect)>();

        // Radio groups built for GRID tables (the radio factory): one
        // RadioButtonField per HTML `name`, its options handed to the cells as
        // inline glyphs. Registered on doc.Form after the flow pass — the table
        // render pass places each option's widget at its drawn glyph.
        cv.profile.gridRadioGroups = new Dictionary<string, Aspose.Pdf.Forms.RadioButtonField>(StringComparer.Ordinal);
        cv.profile.gridRadioPages = new List<(Aspose.Pdf.Forms.RadioButtonField rbf, Page page)>();
        cv.profile.gridRadioCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        cv.flow.gridRadioAnon = 0;

        cv.anchorTargets = new Dictionary<string, (Page page, double y)>(StringComparer.Ordinal);
        cv.pendingLinks = new List<(Page page, Aspose.Pdf.Rectangle rect, string url, string? text)>();

        cv.floatFirstOps = new List<(Page page, byte[] ops)>();

        cv.fsStack = new Stack<(Page page, double topY, double gapX0, double gapX1)>();
        cv.bgSpanStack = new Stack<(Page page, double topY, Color fill, double widthPt, double widthFrac,
            double padTop, double padBottom, double padLeft)>();
        cv.flow.fsIndentLive = 0.0;
        cv.profile.fsBoxW = cv.fieldsetDoc ? cv.fsBodyPct * (cv.pageWidth - 180.0) - 3.0
            : cv.uaFieldsetBoxes ? cv.pageWidth - cv.marginLeft - cv.marginRight - UaBodyMarginPt - 2 * UaFieldsetSideInsetPt : 0.0;

        cv.flow.lastWasHardBreak = false;
        // Set when the flow cursor rests on a just-rendered table's bottom edge:
        // the next TEXT block's first baseline drops one line box first.
        cv.flow.pendingTableDrop = false;
        // …and whether that table drew collapsed borders: its bottom stroke
        // rides below the layout cursor, so the drop deepens one border seat.
        cv.flow.pendingTableDropBordered = false;
        // Browser margin-collapse (UA-default flow): the gap between two adjacent
        // flow blocks is max(prev margin-bottom, next margin-top), not their sum.
        cv.flow.uaPrevMarginBottom = 0;
        cv.flow.lastWasRow = false;
        cv.flow.prevRowMarginBottomPx = 0;
        // Bottom margin, line height and font size the LAST text block applied — read
        // by the form-dialect <hr>/image branches to rewind the legacy full-line-box
        // advance to the CSS box bottom (baseline + descent) before their own spacing:
        // a rule sits desc + max(prev margin-bottom, rule margin-top) below the text
        // baseline, not a whole line box further.
        cv.flow.prevFlowMarginBottom = 0;
        cv.flow.prevFlowLineHeight = 0;
        cv.flow.prevFlowFontSize = 0;
        // A left-floated image the flow has passed under, but not yet below: lines that
        // start above floatBottomY are laid out to its right.
        cv.flow.floatBottomY = double.NegativeInfinity;
        cv.flow.floatIndentPt = 0;
        // The mirror of floatIndentPt for a RIGHT float: the lines level with it are
        // shortened from the right instead of being pushed in from the left. It carries
        // its OWN vertical span, because a right float that could not sit beside the left
        // one drops below it and so is level with different lines.
        cv.flow.floatRightInsetPt = 0;
        // Top of the element whose box a declared height reserves (float flow only).
        cv.flow.certElementTopY = double.NaN;
        cv.flow.floatRightTopY = double.PositiveInfinity;
        cv.flow.floatRightBottomY = double.PositiveInfinity;
    }

    /// <summary>The escaped-attribute dialect: its button fill and text colour and the flow flags it sets.</summary>
    private static void ResolveEscapedAttrDialect(ConvertState cv)
    {
        if (cv.profile.escapedAttrDoc)
        {
            cv.dialectButtonFill = ParseCssColor("#F0F0F0");
            // The sheet splits the button rule: one rule carries the gradient (its
            // first stop is the fill), another the caption colour — read them all.
            var haveFill = false; var haveFg = false;
            foreach (Match btnRule in Regex.Matches(cv.html,
                @"(?<![\w.#-])button\s*\{([^}]*)\}", RegexOptions.IgnoreCase))
            {
                var btnBody = btnRule.Groups[1].Value;
                if (!haveFill)
                {
                    var bgHex = Regex.Match(btnBody, "#[0-9a-fA-F]{6}");
                    if (bgHex.Success && ParseCssColor(bgHex.Value) is { } btnFill)
                    {
                        cv.dialectButtonFill = btnFill;
                        haveFill = true;
                    }
                }
                if (!haveFg)
                {
                    var fgDecl = Regex.Match(btnBody, @"(?<![\w-])color\s*:\s*([^;}]+)", RegexOptions.IgnoreCase);
                    if (fgDecl.Success && ParseCssColor(fgDecl.Groups[1].Value.Trim()) is { } btnFg)
                    {
                        cv.dialectButtonTextRg = FormattableString.Invariant(
                            $"{btnFg.R / 255.0:0.###} {btnFg.G / 255.0:0.###} {btnFg.B / 255.0:0.###} rg");
                        haveFg = true;
                    }
                }
            }
        }
    }

    /// <summary>The standard-serif top margin and its zero-margin drop, and the dialect button defaults.</summary>
    private static void SeatUaSerifTopMargin(ConvertState cv)
    {
        if (cv.profile.uaStdSerif && !cv.marginsExplicit
            // Only the bare STANDARDS doctype charges: a legacy/system form
            // (`<!DOCTYPE HTML SYSTEM>`) keeps quirks (probed: 88.80 either way
            // for it, 96.24 only under `<!DOCTYPE html>`).
            && Regex.IsMatch(cv.html, @"^\s*<!DOCTYPE\s+html\s*>", RegexOptions.IgnoreCase)
            && cv.blocks.Count > 0 && !cv.blocks[0].IsTable && cv.blocks[0].FontRes == "F1"
            && cv.blocks[0].FontSize is <= 0 or 12.0 && !string.IsNullOrWhiteSpace(cv.blocks[0].Text)
            // …and the charge belongs to a LEADING <p> ELEMENT: a document whose
            // first rendered content is a bare anchor/div line keeps the quirks
            // seat even in standards mode (probed: doctype + bare text = 88.80,
            // doctype + <p> = 96.24). The first renderable element decides.
            && Regex.Match(
                    Regex.Replace(cv.html, @"<(script|style|head)[^>]*>[\s\S]*?</\1>", "",
                        RegexOptions.IgnoreCase),
                    @"<(p|h[1-6]|div|a|ul|ol|table|img|span|blockquote|pre|input|button|nav|label)\b",
                    RegexOptions.IgnoreCase) is { Success: true } firstElem
            && firstElem.Groups[1].Value.Equals("p", StringComparison.OrdinalIgnoreCase))
        {
            cv.flow.y -= UaDoctypeLeadParagraphPt;
            // This charge IS the standards-mode first-block seat, probed as the
            // whole 96.24 − 88.80 delta — the html5-bare first-block
            // MAX-collapse below models the same physics and must not stack a
            // second charge on top of it for the p-first documents this covers.
            cv.doctypeLeadCharged = true;
        }
        // A browser aligns the TOP of the first line box to the content top, so the first
        // baseline sits further from the top for a larger first line (its line box is taller).
        // The default margin was calibrated to the HTML renderer for a default-size
        // (~11 pt) first line; when the first line's font is larger, lower the first baseline
        // by its font-size excess to give a top-aligned first line. Scoped
        // to the default-margin path so explicit-margin conversions stay byte-identical.
        // Print-cover documents own their geometry through the CSS box model (the
        // cover classes' margins and line factors) — the calibrated first-line
        // drops above and below must not shift their box chain.
        if (!cv.marginsExplicit && !cv.uaFlow && !cv.profile.printGrid && !cv.printCoverDoc && !cv.profile.sheetBoxFlow)
        {
            const double DefaultFirstFontSize = 11.0;
            const double FirstLineLeadingPerPt = 0.7647; // excess-pt → baseline drop
            var firstFontSize = cv.blocks[0].FontSize;
            // The slope above was calibrated on the system faces the reference lays those
            // documents in. A first line in a face the caller registered through a folder
            // source has no such calibration: the reference lays it as the CSS line box of
            // the face's own hhea metrics under the page's 72 pt plus the UA body inset
            // (measured on a 28 pt script face at line-height 115%: baseline 102.33 = 78 +
            // (32.2 - 33.93)/2 + 25.20, where the slope put it at 102.00).
            if (RegisteredFaceFirstBaseline(cv.blocks[0], firstFontSize) is { } registeredSeat)
                cv.flow.y = cv.pageHeight - registeredSeat;
            else if (firstFontSize > DefaultFirstFontSize)
                cv.flow.y -= (firstFontSize - DefaultFirstFontSize) * FirstLineLeadingPerPt;
        }
        // An explicit zero TOP margin still lays a page's first line INSIDE the
        // page: its baseline drops by its own line box (+ its block margin), so the
        // ink top lands at the content top instead of clipping off the page edge.
        cv.profile.hasZeroTopMargin = cv.marginsExplicit && cv.marginTop < 1e-9;
        cv.flow.pendingTopDrop = cv.profile.hasZeroTopMargin;
        // Shared 32×32 broken-image placeholder (drawn for unloadable images in the
        // escaped-attr dialect and on Word-filtered pages).
        // Word-filtered pages: the first broken image is the absolutely
        // positioned banner (its own seat), later ones sit at the content edge.
        cv.flow.msoBrokenImgCount = 0;
        // The block just drawn was the escaped dialect's section rule: an inline RUN
        // right after it drops extra so its control box clears the rule (measured:
        // rule → run baseline = 17.2, a bare run line would sit at 11.9).
        cv.flow.afterEscapedRule = false;
        cv.dialectButtonFill = null;
        cv.dialectButtonTextRg = "0 0 0 rg";
    }

    /// <summary>The output document, its first page and fonts, the title, the CSS page margins and the flow cursor's first seat.</summary>
    private static void OpenDocumentAndSeatFlow(ConvertState cv)
    {
        cv.docFontDict = new Core.PdfDictionary();
        cv.doc = Document.Create();
        cv.flow = new HtmlFlowCursor();
        cv.flow.page = cv.doc.Pages.Add(cv.pageWidth, cv.pageHeight);
        EnsureFonts(cv.flow.page, cv.docFontDict);

        // UA-serif flow with a NEGATIVE-margin element: the expected render
        // clips page content at one body margin left of the content origin
        // (its content stream opens with `90 0 505 842 re W n`) — the box that
        // slid left of the clip loses its overhang there. Emitted un-nested so
        // it governs every stream appended after it; pages without negative
        // indents never carry ink there, so they need no clip.
        // DataWorks: the absolute header bar's 2px #ccc bottom rule spans the
        // content box plus one UA body margin (measured 96..533.3 at y 108.9),
        // sitting two header lines plus the spans' 5px bottom margin under the
        // content top.
        if (cv.profile.dwFormDoc)
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                $"q {DwRuleGray:0.###} {DwRuleGray:0.###} {DwRuleGray:0.###} rg {cv.marginLeft:0.##} {cv.pageHeight - cv.marginTop - DwHeaderRuleDropPt - DwHeaderRuleHPt:0.##} {cv.pageWidth - cv.marginLeft - 90.0 + UaBodyMarginPt:0.##} {DwHeaderRuleHPt:0.##} re f Q\n")));

        if (cv.profile.uaStdSerif && cv.blocks.Any(b => b.LeftIndent < 0))
            cv.flow.page.AddContentStream(Encoding.ASCII.GetBytes(FormattableString.Invariant(
                $"{cv.marginLeft - UaBodyMarginPt:0.##} 0 {cv.pageWidth - cv.marginLeft + UaBodyMarginPt:0.##} {cv.pageHeight:0.##} re W n\n")));

        cv.titleMatch = Regex.Match(cv.html, @"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (cv.titleMatch.Success)
            cv.doc.Info.Title = DecodeEntities(cv.titleMatch.Groups[1].Value).Trim();

        ApplyCssPageMargins(cv);
        cv.flow.contentWidth = cv.pageWidth - cv.marginLeft - cv.marginRight;
        if (Environment.GetEnvironmentVariable("ASPOSE_TRACE_PAGEW") == "1") Console.Error.WriteLine($"[flowbox] page={cv.pageWidth:0.##} ml={cv.marginLeft:0.##} mr={cv.marginRight:0.##} mt={cv.marginTop:0.##} cw={cv.flow.contentWidth:0.##}");
        // The certificate dialect's vertical page margin: the page's own 72 plus the UA
        // body inset, which is what puts the first floated logo at y = 78 on
        // an 842 pt sheet (measured: ours sat 11 pt lower on a 89 pt margin). It has to be
        // set BEFORE the flow cursor is taken from it.
        // …and a sectioned report that OPENS with a grid opens it at the same 78: the page's 72
        // plus the body inset (measured: the job list's first line box starts at 78, not 89). A
        // report opening with flow text keeps the calibrated 89 (the twelve-page report's pages).
        if (!cv.marginsExplicit && (cv.profile.floatBothSidesDoc
            || (cv.profile.sectionedReport && cv.blocks.Count > 0 && cv.blocks[0].IsTable)
            || (cv.profile.uaGridBoxes && !cv.profile.bodyZeroMargin && cv.blocks.Count > 0 && (cv.blocks[0].IsTable || cv.blocks[0].MarginTop <= 0))))
            cv.marginTop = 72.0 + UaBodyMarginPt;
        cv.flow.y = cv.pageHeight - cv.marginTop;
        // `@page :first` gave the opening sheet a top margin of its own.
        cv.flow.y += cv.cssPageFirstTopLift;
        // Metric flow: the BODY top margin indents the first page only — continuation
        // pages resume at the raw content top.
        // (a dialect whose page margin already carries the UA body inset spends it once:
        // the pt-styled fragment's first baseline sits 78 + its line drop under the top)
        var bodyInsetInMargin = !cv.marginsExplicit
            && (cv.profile.ptStyledFragment || cv.profile.redlineDiffDoc || cv.profile.floatBothSidesDoc);
        if (cv.profile.metricFlow && !bodyInsetInMargin) cv.flow.y -= cv.bodyMarT;
        // body{margin:0}: with no body margin to collapse into, the first block's own UA
        // top margin (1em on a paragraph) reaches the canvas and pushes the first line
        // down; the flow otherwise drops a leading block's margin at the top of a page.
        // A leading EMPTY block carries no margin of its own, so it must not shift the
        // flow. Zeroed-body geometry only — the default-margin path bakes the drop into
        // its calibrated 89 pt top.
        if (cv.profile.bodyZeroMargin && !cv.profile.metricFlow && !cv.printCoverDoc && !cv.profile.sheetBoxFlow
            && cv.blocks.Count > 0 && !cv.blocks[0].IsTable
            && cv.blocks[0].FontSize > 0 && !string.IsNullOrWhiteSpace(cv.blocks[0].Text))
            cv.flow.y -= cv.blocks[0].FontSize;
        cv.doctypeLeadCharged = false;
    }

    /// <summary>The first baseline, from the page top, of an opening line set in a face the caller
    /// registered through a <see cref="Text.FolderFontSource"/>: the CSS line box (the block's
    /// declared line-height, else the browser's normal box) seating the face's hhea ascent under
    /// the page margin and the UA body inset. Null for a system face, a face without hhea
    /// metrics, or a block with no size.</summary>
    private static double? RegisteredFaceFirstBaseline(Block first, double fontSizePt)
    {
        if (fontSizePt <= 0 || first.FontFamily is not { Length: > 0 } family) return null;
        // A caller-added folder only: the repository's own per-user fonts folder and the system
        // source are where the calibrated faces live, so a family either of them resolves keeps
        // the slope.
        var registered = false;
        foreach (var source in Text.FontRepository.Sources)
        {
            if (source is Text.SystemFontSource system && system.FindFont(family, ignoreCase: true) is not null) return null;
            if (source is Text.FolderFontSource { IsDefaultUserFolder: true } userFolder && userFolder.FindFont(family, ignoreCase: true) is not null) return null;
            if (source is Text.FolderFontSource folder && folder.FindFont(family, ignoreCase: true) is not null)
                registered = true;
        }
        if (!registered) return null;
        Text.TrueTypeParser parser;
        try
        {
            var ttf = Text.FontRepository.GetTtfData(family);
            if (ttf is null) return null;
            parser = new Text.TrueTypeParser(ttf);
            parser.Parse();
        }
        catch { return null; }
        if (parser.UnitsPerEm <= 0 || parser.Ascent <= 0) return null;
        var ascent = fontSizePt * parser.Ascent / parser.UnitsPerEm;
        var descent = fontSizePt * -parser.Descent / parser.UnitsPerEm;
        var lineBox = first.DeclaredLineFactor && first.LineFactor > 0
            ? fontSizePt * first.LineFactor
            : NormalLineHeightPt(fontSizePt);
        return 72.0 + UaBodyMarginPt + (lineBox - (ascent + descent)) / 2 + ascent;
    }

    /// <summary>The form-document dialect: its grid geometry, faces and strut pitch.</summary>
    private static void ResolveFormDocLayout(ConvertState cv)
    {
        if (cv.profile.dwFormDoc)
        {
            var dwMinItemIndent = double.MaxValue;
            foreach (var blk in cv.blocks)
                if (blk.IsListItem && blk.LeftIndent < dwMinItemIndent) dwMinItemIndent = blk.LeftIndent;
            foreach (var blk in cv.blocks)
            {
                blk.FontFamily = "Times New Roman";
                if (blk.FontSize > 0)
                    blk.FontSize = blk.FontSize >= 17 ? DwH1FontPt : DwBodyFontPt;
                blk.LineFactor = RedlineLineFactor;
                if (blk.IsListItem && blk.Marker == "•" && dwMinItemIndent < double.MaxValue
                    && blk.LeftIndent > dwMinItemIndent + 1)
                    blk.Marker = "◦";
                // UA list geometry: 40px padding-inline-start per level (the
                // legacy flow indents 20pt) — scale to the browser's 30pt.
                if (blk.IsListItem && blk.LeftIndent > 0)
                    blk.LeftIndent = blk.LeftIndent * 1.5;
                if (blk.Anchors is { Count: > 0 })
                {
                    blk.ColorRuns ??= new();
                    blk.DecorRuns ??= new();
                    var dwLinkLen = 0;
                    foreach (var (aS, aL, _) in blk.Anchors)
                    {
                        blk.ColorRuns.Add((aS, aL, DwLinkColor));
                        blk.DecorRuns.Add((aS, aL, 1, null));
                        dwLinkLen += aL;
                    }
                    // A fully-linked block takes the anchor ink as its block
                    // colour (the plain writer has no per-run colours).
                    if (dwLinkLen >= blk.Text.TrimEnd().Length)
                        blk.ForeColor = DwLinkColor;
                }
            }
            // The stayTop header bar is position:absolute — OUT of the flow.
            // Its left span (the bold-italic reference) and its right 80px
            // print-link box both render at the flow top and give their y
            // back; the bar's 2px #ccc bottom rule is drawn separately.
            // The dwroot page opens with the absolute stayTop bar: its first
            // flow block is the bold-italic reference (float:left span), the
            // second the print link's ALT text (the 80px float:right span).
            // Both render at the flow top and give their y back (the bar is
            // position:absolute); the link takes the classic anchor styling.
            // UA ul margins: the first item of a list run carries the 1em
            // block-start margin (the legacy flow spaces lists differently).
            for (var li2 = 0; li2 < cv.blocks.Count; li2++)
            {
                if (cv.blocks[li2].IsListItem && (li2 == 0 || !cv.blocks[li2 - 1].IsListItem))
                    cv.blocks[li2].MarginTop = Math.Max(cv.blocks[li2].MarginTop, DwBodyFontPt);
                if (cv.blocks[li2].IsListItem
                    && (li2 + 1 == cv.blocks.Count || !cv.blocks[li2 + 1].IsListItem))
                    cv.blocks[li2].MarginBottom = Math.Max(cv.blocks[li2].MarginBottom, DwBodyFontPt);
            }
            var hFirst = cv.blocks.FindIndex(bb => bb.Text.Length > 0 && !bb.IsTable);
            if (hFirst >= 0 && hFirst + 1 < cv.blocks.Count
                && cv.blocks[hFirst + 1].Text.Length > 0 && !cv.blocks[hFirst + 1].IsTable)
            {
                var hRef = cv.blocks[hFirst];
                hRef.NoAdvanceY = true;
                hRef.EmBold = true;
                hRef.EmItalic = true;
                hRef.BoldRuns = new() { (0, hRef.Text.Length) };
                hRef.ItalicRuns = new() { (0, hRef.Text.Length) };
                var hLink = cv.blocks[hFirst + 1];
                hLink.NoAdvanceY = true;
                hLink.LeftIndent = cv.pageWidth - cv.marginLeft - 90.0 - DwPrintLinkBoxPt;
                hLink.WidthPx = DwPrintLinkBoxPt / 0.75;
                hLink.ForeColor = DwLinkColor;
                hLink.ColorRuns = new() { (0, hLink.Text.Length, DwLinkColor) };
                hLink.DecorRuns = new() { (0, hLink.Text.Length, 1, null) };
            }
        }
    }

    /// <summary>The UA flow's body face, sizes and line pitch, and the standard-serif body face when the document declares one.</summary>
    private static void ResolveUaFlowAndFaces(ConvertState cv, HtmlLoadOptions? options)
    {
        if (cv.uaFlow)
        {
            double widestImg = 0;
            foreach (var b in cv.blocks)
            {
                if (string.IsNullOrEmpty(b.ImageSrc)
                    // a %-max-width image clamps to the content box instead
                    || b.ImageMaxWFrac > 0) continue;
                // An inline-SVG placeholder widens by its viewport (CSS px) —
                // the 600px chart map opens a 96 + 450 + 90 sheet (measured).
                if (b.ImageSrc.StartsWith("inline-svg:", StringComparison.Ordinal))
                {
                    if (b.ImageWidth > 0 && b.ImageWidth * 0.75 > widestImg)
                        widestImg = b.ImageWidth * 0.75;
                    continue;
                }
                double iwPt = 0;
                // Word-filtered pages: only an image whose BYTES resolve grows
                // the sheet (the snip-capture email's base64 payload); the
                // 817px absolutely positioned banner whose file is missing
                // keeps the measured column sheet (it overflows and clips).
                if (b.ImageWidth > 0 && !cv.profile.msoFilteredDoc) iwPt = b.ImageWidth * 0.75;
                else if (cv.profile.msoFilteredDoc)
                {
                    // Word-filtered: only an image whose bytes RESOLVE grows the
                    // sheet, at its ATTRIBUTE width when one upscales the bitmap
                    // (the snip-capture email: a 615px PNG drawn at width=1920).
                    var ib = LoadConverterImage(b.ImageSrc, options);
                    if (ib is not null)
                        iwPt = b.ImageWidth > 0 ? b.ImageWidth * 0.75
                            : TryReadImagePixelSize(ib) is (var ipw2, _) && ipw2 > 0
                                ? ipw2 * 0.75 : 0;
                }
                else
                {
                    var ib = LoadConverterImage(b.ImageSrc, options);
                    if (ib is not null && TryReadImagePixelSize(ib) is (var ipw, _) && ipw > 0)
                        iwPt = ipw * 0.75;
                }
                if (iwPt > widestImg) widestImg = iwPt;
            }
            if (widestImg > 0)
            {
                // The widened filtered sheet closes at the bare UA right margin
                // (96 + image + 90, measured); other flows keep their own.
                var widenRight = cv.profile.msoFilteredDoc ? 90.0 : cv.marginRight;
                var widenPage = cv.marginLeft + widestImg + widenRight;
                if (widenPage > cv.pageWidth)
                {
                    cv.pageWidth = widenPage;
                    if (cv.profile.msoFilteredDoc) cv.marginRight = widenRight;
                }
            }
        }

        // The body rule's face travels on every family-free block: the UA-flow
        // draw embeds it per run (the same path a <font face> block takes), so
        // the whole document sets in the body face instead of the UA serif. A
        // block whose captured family cannot draw (the stack's unresolvable
        // FIRST member) falls to the same face — that face IS the stack's first
        // resolvable member, exactly the CSS fallback the expected render takes.
        if (cv.profile.uaStdSerif && cv.uaBodyFace is not null)
            foreach (var ubb in cv.blocks)
                if (string.IsNullOrEmpty(ubb.FontFamily)
                    || PosFace(ubb.FontFamily).ttf is null)
                    ubb.FontFamily = cv.uaBodyFace;
    }

    /// <summary>Page size and margins: report, styled-fragment and redline documents take their dialect margins, and a letter-width page without explicit margins takes the UA defaults.</summary>
    private static void ResolvePageGeometry(ConvertState cv)
    {
        if (cv.profile.ssrsReportDoc)
        {
            cv.marginTop = 72.0;
            cv.marginBottom = 72.0;
        }
        // The pt-styled fragment opens at the UA top: page margin plus the
        // quirks body's default 8px margin (probed: the address card's first
        // 8 pt row seats its text at 78.0), and its content frame is SYMMETRIC
        // (probed: the address card clamps at width − 96 on the right, the
        // mirror of the 96 left margin).
        if (cv.profile.ptStyledFragment && !cv.marginsExplicit)
        {
            cv.marginTop = 72.0 + UaBodyMarginPt;
            cv.marginRight = cv.marginLeft;
        }
        // A UA-grid document's content frame is symmetric (measured on the resume and the royalty
        // statement: every 100% grid and every centred heading spans 96..pageWidth-96).
        if (cv.profile.uaGridSheet && !cv.marginsExplicit)
            cv.marginRight = cv.marginLeft;
        // The redline diff document's content frame is symmetric, and it opens
        // at the UA top (probed: the divider bar's top edge at 72 + the 6 pt
        // body margin = 78, paragraphs spanning 96..pageWidth-96).
        if (cv.profile.redlineDiffDoc && !cv.marginsExplicit)
        {
            cv.marginTop = 72.0 + UaBodyMarginPt;
            cv.marginRight = cv.marginLeft;
        }
        // SSRS report export: an oversized data-URI JPEG draws at the engine's
        // image viewport (612 pt — measured: the 1024 px photo lands 612×459 pt,
        // overflowing its column) and the sheet widens to hold it inside the
        // cell's 2 pt padding.
        if (cv.profile.ssrsReportDoc && !(cv.pageInfo?.WidthAssigned ?? false)
            && Regex.Match(cv.html, @"data:image/jpe?g;base64,([A-Za-z0-9+/=]+)",
                RegexOptions.IgnoreCase) is { Success: true } sjm)
        {
            try
            {
                var (sjw, sjh) = JpegDims(
                    System.Convert.FromBase64String(sjm.Groups[1].Value));
                if (sjw > 0)
                {
                    var sjNeed = cv.marginLeft + 2.0
                        + Math.Min(sjw * 0.75, ImgRuleMaxWidthPt(cv.css) ?? JpegViewportPt) + 2.0;
                    if (sjNeed > cv.pageWidth) cv.pageWidth = sjNeed;
                }
            }
            catch { }
        }

        // A body that declares its own width in PHYSICAL units widens the sheet to its
        // content. The width term is CONTENT-MEASURED
        // and pixel-quantized — it grows with the rows' laid-out extent and an
        // hr contributes its own percentage box — but a document whose label/span rows
        // wrap (hundreds of characters) SATURATES the measure at the declared body
        // width, and every such page then fits max(A4, 96 + bodyW + 87.3): the 96 is
        // this engine's own content left, the 87.3 the fitted right band, and the
        // sheet never falls below the A4 default (a 10 cm body keeps a 595 page).
        // Pixel-width bodies keep the ordinary flow; explicit page setup wins.
        ResolveDefaultPageMargins(cv);
    }
}
