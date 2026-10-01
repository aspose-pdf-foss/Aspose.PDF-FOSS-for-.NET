using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Converters;

internal static partial class HtmlToPdfConverter
{
    /// <summary>The serif document classes write the line with per-run faces, sizes and colours; false when the block must stop.</summary>
    private static bool WriteSerifClassRuns(BlockTextState bt, string line, string uafFam, byte[] uafTtf, Core.PdfDictionary uafDict)
    {
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        if (bt.block.BoldRuns is { Count: > 0 } || bt.block.ItalicRuns is { Count: > 0 }
            || bt.block.ColorRuns is { Count: > 0 } || bt.block.SizeRuns is { Count: > 0 }
            // small-caps and symbol-PUA lines need the per-segment
            // emitter even without emphasis runs
            || (bt.profile.redlineDiffDoc && (bt.block.SmallCaps || HasSymbolPua(line))))
        {
            if (!WriteEmphasisRuns(bt, line, uafFam, uafTtf, uafDict)) return false;
        }
        else
        {
            var (uafRn, uafHex) = Text.Type0FontEmbedder.Embed(uafDict, uafTtf,
                uafFam.Replace(" ", "")
                + (bt.block.FontRes == "F2" || bt.block.EmBold ? "Bold" : "")
                // The face the certificate dialect resolved may be an
                // ITALIC one; its label has to say so or two different
                // programs share a name in the resource dictionary.
                + ((bt.profile.floatBothSidesDoc || bt.profile.sheetTypographyDoc) && (bt.block.FontRes == "F3" || bt.block.EmItalic)
                    ? "Italic" : ""),
                line, stripSpacesInBaseFont: true);
            bt.sb.Append($"/{uafRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
                bt.sb.Append(Compat.Format(bt.invc, $"{bt.block.LetterSpacingPt:0.##} Tc "));
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append(KernedTj(uafTtf, uafHex));
            if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
                bt.sb.Append("0 Tc ");
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        return true;
    }

    /// <summary>A max-width block's line is written scaled into its width.</summary>
    private static void WriteMaxWidthRuns(BlockTextState bt, string line)
    {
        // Report label/span dialect: drawn in the dialect's own face —
        // the real Segoe UI embedded when the system provides it (exact
        // shapes and advances); otherwise each word anchors at its
        // position in the baked Segoe metrics so the Standard-14 ink
        // never drifts more than one word's difference.
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        if (HeaderFooter.TryAppendReportLineOps(bt.sb, bt.docFontDict, line,
                bt.lineXPos, bt.lnY, bt.metrics.blockFontSize, bt.block.FontRes == "F2"))
        {
            // drawn kerned and word-anchored in the dialect's own face
        }
        else
        {
            bt.sb.Append($"/{bt.fontRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            var rwx = bt.lineXPos;
            foreach (var rword in line.Split(' '))
            {
                if (rword.Length > 0)
                {
                    bt.sb.Append($"1 0 0 1 {rwx.ToString("F2", bt.invc)} {bt.lnY} Tm ");
                    bt.sb.Append($"({EscapePdfString(rword)}) Tj ");
                }
                rwx += HeaderFooter.MeasureReportText(rword + " ", bt.metrics.blockFontSize,
                    bt.block.FontRes == "F2");
            }
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>A UA-flow line is written run by run with its inline faces.</summary>
    private static void WriteUaFlowRuns(BlockTextState bt, string line, byte[] uaTtf, Core.PdfDictionary uaFontDict)
    {
        // UA flow draws with the real serif face (embedded Type0) —
        // TimesNewRoman/-Bold output rather than Standard-14 Helvetica.
        // The escaped-attr dialect is serif UA output too (the real
        // TimesNewRoman faces are embedded — bold-italic included:
        // <b><i> notes render TimesNewRomanBoldItalic). The pt-report
        // flow embeds its own body face under that face's name.
        var (uaRn, uaHex) = Text.Type0FontEmbedder.Embed(uaFontDict, uaTtf,
            (bt.profile.ptReportDoc ? bt.profile.metricFace.Replace(" ", "") : "TimesNewRoman")
            + (bt.block.FontRes == "F2" || (bt.profile.escapedAttrDoc && bt.block.EmBold) ? "Bold" : "")
            + (bt.profile.escapedAttrDoc && (bt.block.EmItalic || bt.block.FontRes == "F3") ? "Italic" : ""),
            line, stripSpacesInBaseFont: true);
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        bt.sb.Append($"/{uaRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
        bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
        bt.sb.Append(KernedTj(uaTtf, uaHex));
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>A line needing a CJK or RTL face is written through its embedded TrueType.</summary>
    private static void WriteCjkRuns(BlockTextState bt, string line, Core.PdfDictionary cjkFontDict)
    {
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        // Thai mark stacking: a tone mark over an ABOVE vowel seats
        // higher than the run's baseline — the vowel keeps the
        // baseline slot, the tone stacks above it (measured:
        // +2.42 pt at 11 pt, drawn a small nudge right of
        // the pen). Such marks are zero-advance, so each becomes its
        // own raised run at the pen position while the remainder
        // continues where the prefix ended. Lines without the pair
        // keep the single-run emit byte-for-byte.
        var thaiChunks = SplitThaiStackedTones(bt.uniSource);
        // An RTL document's line with a bold run inside it draws segment by segment,
        // the bold Arabic word in the bold face of the same family; consecutive Tj
        // runs advance the pen, so no segment is measured.
        if (RtlEmphasisSegments(bt, line) is { } rtlSegs
            && PosFace(RtlLineFamily(bt) + " Bold").ttf is { } rtlBoldTtf)
        {
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            foreach (var (segText, segBold) in rtlSegs)
            {
                var segTtf = segBold ? rtlBoldTtf : bt.cjkTtf!;
                var (srn, shex) = Text.Type0FontEmbedder.Embed(
                    cjkFontDict, segTtf, RtlLineFamily(bt) + (segBold ? " Bold" : ""), segText, stripSpacesInBaseFont: true);
                bt.sb.Append($"/{srn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                bt.sb.Append(KernedTj(segTtf, shex));
            }
        }
        else if (thaiChunks is not null)
        {
            var penX = bt.lineXPos;
            foreach (var (chunkText, raised) in thaiChunks)
            {
                var (crn, chex) = Text.Type0FontEmbedder.Embed(
                    cjkFontDict, bt.cjkTtf!, bt.cjkName, chunkText, stripSpacesInBaseFont: true);
                var cx = raised ? penX + ThaiToneNudgeEm * bt.metrics.blockFontSize : penX;
                var cy = raised
                    ? (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y) + ThaiToneRaiseEm * bt.metrics.blockFontSize
                    : (bt.profile.metricFlow && bt.metrics.metricDrop > 0 ? bt.flow.y - bt.metrics.metricDrop : bt.flow.y);
                bt.sb.Append($"/{crn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                bt.sb.Append($"1 0 0 1 {cx.ToString("F2", bt.invc)} {cy.ToString("F2", bt.invc)} Tm ");
                bt.sb.Append(KernedTj(bt.cjkTtf!, chex));
                if (!raised)
                    penX += MeasureFaceText(bt.cjkName, chunkText, bt.metrics.blockFontSize);
            }
        }
        else
        {
            var (rn, hex) = Text.Type0FontEmbedder.Embed(
                cjkFontDict, bt.cjkTtf!, bt.cjkName, bt.uniSource, stripSpacesInBaseFont: true);
            bt.sb.Append($"/{rn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append(KernedTj(bt.cjkTtf!, hex));
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>Per-segment Unicode fallback: consecutive Tj runs in each segment's own face advance the text position naturally, so no per-segment measurement is needed.</summary>
    private static void WriteUnicodeSegmentRuns(BlockTextState bt, Core.PdfDictionary segFontDict)
    {
        bt.sb.Clear();
        bt.sb.AppendLine("BT");
        bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
        foreach (var (segText, segFont) in SegmentByFont(bt.uniSource))
        {
            var segTtf = segFont?.SourceFontData?.TtfData;
            if (segTtf is not null)
            {
                var (rn, hex) = Text.Type0FontEmbedder.Embed(
                    segFontDict, segTtf, segFont!.FontName ?? "Unicode", segText, stripSpacesInBaseFont: true);
                bt.sb.Append($"/{rn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                bt.sb.Append(KernedTj(segTtf, hex));
            }
            else
            {
                bt.sb.Append($"/{bt.fontRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                bt.sb.Append($"({EscapePdfString(segText)}) Tj ");
            }
        }
        bt.sb.AppendLine("ET");
        bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
    }

    /// <summary>Dispatches the line to the run writer its font needs and its document class prescribes; false when the block must stop.</summary>
    private static bool WriteLineRuns(BlockTextState bt, string line)
    {
        // The sheet-typography flow draws in the sheet's resolved face (embedded, per-run
        // emphasis and colour) - a line's odd character (a zero-width space) is that
        // face's own glyph, not a fallback face for the whole line.
        if (bt.profile.sheetTypographyDoc && !string.IsNullOrEmpty(bt.block.FontFamily)
            && PosFace(FloatFlowMeasureFace(bt.block)) is { ttf: not null } sheetFace
            && FaceCoversLine(sheetFace.parser, line))
        {
            if (!WriteSerifGridRuns(bt, line)) return false;
        }
        else if (bt.cjkTtf is not null
            && bt.flow.page.Dict.Get("Resources") as Core.PdfDictionary is { } cjkRes
            && cjkRes.Get("Font") as Core.PdfDictionary is { } cjkFontDict)
        {
            WriteCjkRuns(bt, line, cjkFontDict);
        }
        else if (NeedsUnicode(bt.uniSource)
            // redline symbol-PUA lines stay with the face writer below,
            // which draws those sub-runs in the symbol face itself
            && !(bt.profile.redlineDiffDoc && HasSymbolPua(line))
            && bt.flow.page.Dict.Get("Resources") as Core.PdfDictionary is { } segRes
            && segRes.Get("Font") as Core.PdfDictionary is { } segFontDict)
        {
            WriteUnicodeSegmentRuns(bt, segFontDict);
        }
        else if (bt.profile.uaStdSerif || bt.profile.printGrid || bt.profile.redlineDiffDoc || bt.profile.floatBothSidesDoc
            // …and a Word mail's family block with emphasis runs (its bold header labels) draws per run
            || (bt.profile.wordMailDoc && !string.IsNullOrEmpty(bt.block.FontFamily)
                && (bt.block.BoldRuns is { Count: > 0 } || bt.block.ItalicRuns is { Count: > 0 })))
        {
            if (!WriteSerifGridRuns(bt, line)) return false;
        }
        else if ((bt.uaFlow || bt.profile.escapedAttrDoc || bt.profile.ptReportDoc)
            && PosFace(
                (bt.profile.escapedAttrDoc ? "Times New Roman" : bt.profile.metricFace)
                + (bt.block.FontRes == "F2" || (bt.profile.escapedAttrDoc && bt.block.EmBold) ? " Bold" : "")
                + (bt.profile.escapedAttrDoc && (bt.block.EmItalic || bt.block.FontRes == "F3") ? " Italic" : "")
                ).ttf is { } uaTtf
            && bt.flow.page.Dict.Get("Resources") is Core.PdfDictionary uaRes
            && uaRes.Get("Font") is Core.PdfDictionary uaFontDict)
        {
            WriteUaFlowRuns(bt, line, uaTtf, uaFontDict);
        }
        else if (bt.block.MaxWidthPt > 0)
        {
            WriteMaxWidthRuns(bt, line);
        }
        else
        {
            // Justified block: stretch word gaps so every line but the
            // paragraph's last fills the content box. Word-spacing only —
            // wrap points and pagination stay identical to the unjustified
            // layout. Skipped when the crude wrap left implausible slack.
            var justTw = 0.0;
            if (bt.block.AlignJustify && bt.lineIdx < bt.metrics.lines.Length - 1)
            {
                var spaces = 0;
                foreach (var ch in line) if (ch == ' ') spaces++;
                if (spaces > 0)
                {
                    var natural = MeasureFaceText(
                        string.IsNullOrEmpty(bt.block.FontFamily) ? "Arial" : bt.block.FontFamily!,
                        line, bt.metrics.blockFontSize);
                    var slack = bt.flow.contentWidth - bt.block.LeftIndent - natural;
                    if (slack > 0 && slack < (bt.flow.contentWidth - bt.block.LeftIndent) * 0.35)
                        justTw = slack / spaces;
                }
            }
            bt.sb.Clear();
            bt.sb.AppendLine("BT");
            bt.sb.Append($"/{bt.fontRes} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
            if (justTw > 0) bt.sb.Append($"{justTw.ToString("F3", bt.invc)} Tw ");
            bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
            bt.sb.Append($"({EscapePdfString(line)}) Tj ");
            if (justTw > 0) bt.sb.Append("0 Tw ");
            bt.sb.AppendLine("ET");
            bt.flow.page.AddContentStream(Encoding.ASCII.GetBytes(bt.sb.ToString()));
        }
        return true;
    }

    /// <summary>A line carrying bold, italic or colour runs is written run by run in the matching faces; false when the block must stop.</summary>
    private static bool WriteEmphasisRuns(BlockTextState bt, string line, string uafFam, byte[] uafTtf, Core.PdfDictionary uafDict)
    {
        bt.fCurCol = null;
        if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
            bt.sb.Append(Compat.Format(bt.invc, $"{bt.block.LetterSpacingPt:0.##} Tc "));
        bt.sb.Append($"1 0 0 1 {bt.lnX} {bt.lnY} Tm ");
        int fLineStart = bt.metrics.cumChar, fLineEnd = bt.metrics.cumChar + line.Length;
        bt.fPos = fLineStart;
        while (bt.fPos < fLineEnd)
        {
            if (!WriteEmphasisRun(bt, line, uafFam, uafTtf, uafDict, fLineStart, fLineEnd)) break;
        }
        if (bt.profile.redlineDiffDoc && bt.block.LetterSpacingPt != 0)
            bt.sb.Append("0 Tc ");
        // a line ending inside a colour run must not leak
        // its ink into the following content
        if (bt.fCurCol is not null)
        {
            var fBase = bt.block.ForeColor ?? Color.FromArgb(0, 0, 0);
            bt.sb.Append(Compat.Format(bt.invc,
                $"{fBase.R / 255.0:0.###} {fBase.G / 255.0:0.###} {fBase.B / 255.0:0.###} rg "));
        }
        return true;
    }

    /// <summary>Writes the next run of the line - one face, one colour - and advances past it; false at the line's end.</summary>
    private static bool WriteEmphasisRun(BlockTextState bt, string line, string uafFam, byte[] uafTtf, Core.PdfDictionary uafDict, int fLineStart, int fLineEnd)
    {
        bt.fSegEnd = fLineEnd;
        var fBold = InFaceRuns(bt, bt.block.BoldRuns, bt.fPos);
        var fItal = InFaceRuns(bt, bt.block.ItalicRuns, bt.fPos);
        var fRunCol = ColorInRuns(bt, bt.fPos);
        var fRunPt = SizeInRuns(bt, bt.fPos);
        if (fRunCol?.Equals(bt.fCurCol) != true && (fRunCol is not null || bt.fCurCol is not null))
        {
            var fEff = fRunCol ?? bt.block.ForeColor ?? Color.FromArgb(0, 0, 0);
            bt.sb.Append(Compat.Format(bt.invc,
                $"{fEff.R / 255.0:0.###} {fEff.G / 255.0:0.###} {fEff.B / 255.0:0.###} rg "));
            bt.fCurCol = fRunCol;
        }
        var fRunFam = FamilyInRuns(bt, bt.fPos);
        var fSegText = line.Substring(bt.fPos - fLineStart, bt.fSegEnd - bt.fPos);
        // A run can be BOTH bold and italic - the certificate
        // heading is <i><b>…</b></i> - and a real face has that
        // variant where the Standard-14 table has no slot for it.
        var fVariant = (fBold ? " Bold" : "") + (fItal ? " Italic" : "");
        // A face run draws in its own family (the Word export's Symbol / Wingdings list label,
        // its Times New Roman filler): a symbol-encoded face through its F0xx cmap.
        if (fRunFam is not null && !fRunFam.Equals(uafFam, StringComparison.OrdinalIgnoreCase)
            && RunFace(fRunFam) is { ttf: not null } runFace)
        {
            var runTtf = (fVariant.Length > 0 ? PosFace(fRunFam + fVariant).ttf : null) ?? runFace.ttf;
            // (a space stays a space: an nbsp through the F0xx cmap would be the Euro glyph)
            if (IsSymbolEncodedFace(fRunFam)) fSegText = DtpToSymbolPua(runFace.parser, fSegText.Replace('\u00A0', ' '));
            WriteEmphasisSegment(bt, fSegText, runTtf, fVariant, fRunFam, uafDict, fRunPt);
            bt.fPos = bt.fSegEnd;
            return true;
        }
        // "<family> Bold" may not be an indexed NAME —
        // fall back to the styled repository lookup
        // (tahomabd.ttf answers to family+style, not to
        // the "Tahoma Bold" full name).
        var fSegTtf = uafTtf;
        if (fVariant.Length > 0)
        {
            fSegTtf = PosFace(uafFam + fVariant).ttf;
            if (fSegTtf is null)
                try
                {
                    fSegTtf = Text.FontRepository.FindFont(uafFam,
                            fBold ? Text.FontStyles.Bold : Text.FontStyles.Italic,
                            ignoreCase: true)
                        ?.SourceFontData?.TtfData;
                }
                catch { fSegTtf = null; }
            fSegTtf ??= uafTtf;
        }
        // Symbol PUA runs (U+F0xx — the Wingdings box
        // glyphs) draw with the symbol face at full size.
        WriteEmphasisSegment(bt, fSegText, fSegTtf, fVariant, uafFam, uafDict, fRunPt);
        bt.fPos = bt.fSegEnd;
        return true;
    }

    /// <summary>Writes one run's text in its face: symbol PUA glyphs, small caps, or the plain embedded face.</summary>
    private static void WriteEmphasisSegment(BlockTextState bt, string fSegText, byte[] fSegTtf, string fVariant, string uafFam, Core.PdfDictionary uafDict, double sizePt = 0)
    {
        if (bt.profile.redlineDiffDoc && HasSymbolPua(fSegText))
        {
            var puaPos = 0;
            while (puaPos < fSegText.Length)
            {
                var isPua = IsSymbolPua(fSegText[puaPos]);
                var puaEnd = puaPos + 1;
                while (puaEnd < fSegText.Length
                       && IsSymbolPua(fSegText[puaEnd]) == isPua) puaEnd++;
                var puaText = fSegText[puaPos..puaEnd];
                var puaTtf = isPua ? PosFace("Wingdings").ttf : null;
                if (isPua && puaTtf is not null)
                {
                    var (pRn, pHex) = Text.Type0FontEmbedder.Embed(uafDict, puaTtf,
                        "Wingdings", puaText, stripSpacesInBaseFont: true);
                    bt.sb.Append($"/{pRn} {bt.metrics.blockFontSize.ToString("F1", bt.invc)} Tf ");
                    bt.sb.Append(KernedTj(puaTtf!, pHex));
                }
                else
                {
                    var nPos = 0;
                    while (nPos < puaText.Length)
                    {
                        var nLower = bt.block.SmallCaps && char.IsLower(puaText[nPos]);
                        var nEnd = nPos + 1;
                        while (nEnd < puaText.Length
                               && (bt.block.SmallCaps && char.IsLower(puaText[nEnd])) == nLower) nEnd++;
                        var nText = puaText[nPos..nEnd];
                        var (nRn, nHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                            uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                            nLower ? nText.ToUpperInvariant() : nText,
                            stripSpacesInBaseFont: true);
                        bt.sb.Append($"/{nRn} {(nLower ? bt.metrics.blockFontSize * RedlineSmallCapsEm : bt.metrics.blockFontSize).ToString("F2", bt.invc)} Tf ");
                        bt.sb.Append(KernedTj(fSegTtf, nHex));
                        nPos = nEnd;
                    }
                }
                puaPos = puaEnd;
            }
        }
        else if (bt.profile.redlineDiffDoc && bt.block.SmallCaps)
        {
            // small-caps: lowercase sub-runs draw UPPERCASE
            // at the small ratio on the shared baseline
            var scPos = 0;
            while (scPos < fSegText.Length)
            {
                var scLower = char.IsLower(fSegText[scPos]);
                var scEnd = scPos + 1;
                while (scEnd < fSegText.Length
                       && char.IsLower(fSegText[scEnd]) == scLower) scEnd++;
                var scText = fSegText[scPos..scEnd];
                var (scRn, scHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                    uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                    scLower ? scText.ToUpperInvariant() : scText,
                    stripSpacesInBaseFont: true);
                bt.sb.Append($"/{scRn} {(scLower ? bt.metrics.blockFontSize * RedlineSmallCapsEm : bt.metrics.blockFontSize).ToString("F2", bt.invc)} Tf ");
                bt.sb.Append(KernedTj(fSegTtf, scHex));
                scPos = scEnd;
            }
        }
        else
        {
            var (fRn, fHex) = Text.Type0FontEmbedder.Embed(uafDict, fSegTtf,
                uafFam.Replace(" ", "") + fVariant.Replace(" ", ""),
                fSegText, stripSpacesInBaseFont: true);
            bt.sb.Append($"/{fRn} {(sizePt > 0 ? sizePt : bt.metrics.blockFontSize).ToString("F2", bt.invc)} Tf ");
            bt.sb.Append(KernedTj(fSegTtf, fHex));
        }
    }

    /// <summary>Whether the face maps every non-ASCII character of the line (a CJK run in a Latin
    /// sheet face falls back to the flow's script faces instead of drawing notdef boxes).</summary>
    private static bool FaceCoversLine(Text.GlyphOutlineParser? parser, string line)
    {
        if (parser is null) return false;
        foreach (var ch in line)
            if (ch > 0x7F && !char.IsWhiteSpace(ch) && ch != '\u200B'
                && !(parser.CMap.TryGetValue(ch, out var g) && g != 0)) return false;
        return true;
    }

    /// <summary>The UA serif face (Times New Roman, its bold for a bold block) when its cmap covers every
    /// character of the text; null when it does not or cannot be found.</summary>
    private static Text.Font? UaSerifFaceCovering(string text, bool bold, string family = "Times New Roman")
    {
        var name = bold ? family + " Bold" : family;
        if (!_uniFontCache.TryGetValue(name, out var entry))
        {
            Text.Font? f = null; Dictionary<int, int>? cmap = null;
            try
            {
                f = Text.FontRepository.TryFindFont(name);
                if (f?.SourceFontData?.TtfData is { } ttf) cmap = new Text.GlyphOutlineParser(ttf).CMap;
            }
            catch { f = null; cmap = null; }
            entry = (f, cmap);
            _uniFontCache[name] = entry;
        }
        if (entry.font?.SourceFontData is null || entry.cmap is not { } map) return null;
        foreach (var ch in text)
            if (!char.IsWhiteSpace(ch) && (!map.TryGetValue(ch, out var gid) || gid == 0)) return null;
        return entry.font;
    }
}
