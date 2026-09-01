using System.Text;
using System.Text.RegularExpressions;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfContentEditor
{
    private static byte[] GetPageContentBytes(Page page, Document doc)
    {
        var contentsObj = doc.Reader.Resolve(page.Dict.Get("Contents"));
        if (contentsObj is PdfStream stream)
            return doc.Reader.DecodeStream(stream);
        if (contentsObj is PdfArray arr)
        {
            using var ms = new MemoryStream();
            foreach (var item in arr)
            {
                var resolved = doc.Reader.Resolve(item);
                if (resolved is PdfStream s)
                {
                    var data = doc.Reader.DecodeStream(s);
                    ms.Write(data);
                    ms.WriteByte((byte)'\n');
                }
            }
            return ms.ToArray();
        }
        return [];
    }

    private Document EnsureBound()
    {
        if (_document is null)
            throw new InvalidOperationException("No document bound. Call BindPdf first.");
        return _document;
    }

    /// <summary>
    /// Replace text in the bound document according to <see cref="ReplaceTextStrategy"/>.
    /// Returns true if at least one replacement was made.
    /// </summary>
    public bool ReplaceText(string srcString, string destString)
    {
        var doc = EnsureBound();
        // The facade stores an RTL replacement in drawn (visual) order, like the
        // reference writer; the in-memory TextFragment/TextSegment setters do not
        // (they honour the caller's TextEditOptions).
        destString = Aspose.Pdf.Text.BidiReorderer.ToVisualIfRtl(destString);
        if (TextSearchOptions?.Rectangle is not null)
            return ReplaceTextInRectangle(doc, null, srcString, destString);
        var replacer = new TextReplacer
        {
            ReplaceFirstOnly = ReplaceTextStrategy.ReplaceScope == ReplaceTextStrategy.Scope.ReplaceFirst
                && TextReplaceOptions.ReplaceScope == TextReplaceOptions.Scope.REPLACE_FIRST,
            // Facade ReplaceText owns the whole replacement: font-switch a run whose glyphs
            // are absent from the source embedded subset to a fallback (Times), so they render.
            AllowSubsetGlyphFallback = true,
            // Anchoring (keep trailing text at its absolute position when the
            // replacement is narrower/wider than the match) is a request the caller
            // makes by setting ReplaceAdjustment.None explicitly. The untouched facade
            // default reflows the line, closing the gap instead.
            AnchorTrailingOnReplace = _textReplaceOptionsAssigned
                && TextReplaceOptions.ReplaceAdjustmentAction
                == TextReplaceOptions.ReplaceAdjustment.None,
        };
        replacer.Replace(doc, srcString, destString, ReplaceTextStrategy.IsRegularExpressionUsed);
        return replacer.ReplacementCount > 0;
    }

    /// <summary>Replace text on a specific page (1-based).</summary>
    public bool ReplaceText(string srcString, int thePage, string destString)
    {
        var doc = EnsureBound();
        // The facade stores an RTL replacement in drawn (visual) order, like the
        // reference writer; the in-memory TextFragment/TextSegment setters do not
        // (they honour the caller's TextEditOptions).
        destString = Aspose.Pdf.Text.BidiReorderer.ToVisualIfRtl(destString);
        if (thePage < 1 || thePage > doc.PageCount) return false;
        if (TextSearchOptions?.Rectangle is not null)
            return ReplaceTextInRectangle(doc, thePage, srcString, destString);
        var replacer = new TextReplacer
        {
            ReplaceFirstOnly = ReplaceTextStrategy.ReplaceScope == ReplaceTextStrategy.Scope.ReplaceFirst
                && TextReplaceOptions.ReplaceScope == TextReplaceOptions.Scope.REPLACE_FIRST,
            // Facade ReplaceText owns the whole replacement: font-switch a run whose glyphs
            // are absent from the source embedded subset to a fallback (Times), so they render.
            AllowSubsetGlyphFallback = true,
            // Anchoring (keep trailing text at its absolute position when the
            // replacement is narrower/wider than the match) is a request the caller
            // makes by setting ReplaceAdjustment.None explicitly. The untouched facade
            // default reflows the line, closing the gap instead.
            AnchorTrailingOnReplace = _textReplaceOptionsAssigned
                && TextReplaceOptions.ReplaceAdjustmentAction
                == TextReplaceOptions.ReplaceAdjustment.None,
        };
        replacer.Replace(doc.Pages.At(thePage), srcString, destString, IsRegexSearch);
        return replacer.ReplacementCount > 0;
    }

    /// <summary>
    /// Region-scoped replacement used when <see cref="TextSearchOptions"/>.Rectangle is
    /// set: only occurrences whose text fragment falls inside that rectangle are replaced.
    /// Matching fragments are located through <see cref="TextFragmentAbsorber"/> (which
    /// honours the rectangle) and rewritten via the fragment's Text setter, which scopes
    /// each rewrite to the producing operator's page-space position.
    /// </summary>
    private bool ReplaceTextInRectangle(Document doc, int? thePage, string srcString, string destString)
    {
        var opts = new TextSearchOptions(TextSearchOptions.Rectangle!)
        {
            IsRegularExpression = IsRegexSearch,
            CaseSensitive = TextSearchOptions.CaseSensitive,
            WholeWord = TextSearchOptions.WholeWord,
        };
        var absorber = new TextFragmentAbsorber(srcString, opts);
        if (thePage is int p)
            doc.Pages.At(p).Accept(absorber);
        else
            absorber.Visit(doc);

        bool replaceFirst = ReplaceTextStrategy.ReplaceScope == ReplaceTextStrategy.Scope.ReplaceFirst
            && TextReplaceOptions.ReplaceScope == TextReplaceOptions.Scope.REPLACE_FIRST;
        int count = 0;
        foreach (TextFragment frag in absorber.TextFragments)
        {
            var page = frag.Page;
            if (page is null) continue;
            // Scope the rewrite to this fragment's exact page-space position (X and
            // Y), not just its baseline Y: a rectangle can include some matches on a
            // line while excluding others on the same baseline, so a Y-only scope
            // (as used by the generic TextFragment.Text setter) would bleed into the
            // neighbouring out-of-rectangle word.
            // Cross-operator ON so a word drawn glyph-by-glyph (one Tj per glyph) is matched;
            // TargetY/TargetX (set below) keep the cross-op replacement scoped to this fragment.
            var replacer = new TextReplacer { AllowSubsetGlyphFallback = true, AllowCrossOperator = true };
            if (frag.PositionOrNull is { } pos)
            {
                replacer.TargetY = pos.YIndent;
                replacer.TargetX = pos.XIndent;
            }
            replacer.Replace(page, srcString, destString, IsRegexSearch);
            if (replacer.ReplacementCount > 0) count += replacer.ReplacementCount;
            if (replaceFirst && count > 0) break;
        }
        return count > 0;
    }

    /// <summary>Replace text with explicit <see cref="TextState"/> formatting (font/size/colour).</summary>
    public bool ReplaceText(string srcString, string destString, TextState textState)
    {
        var doc = EnsureBound();
        if (!ReplaceText(srcString, destString)) return false;
        ApplyReplacementState(doc, null, destString, textState);
        return true;
    }

    /// <summary>Replace text on a specific page with explicit <see cref="TextState"/>.</summary>
    public bool ReplaceText(string srcString, int thePage, string destString, TextState textState)
    {
        var doc = EnsureBound();
        if (!ReplaceText(srcString, thePage, destString)) return false;
        ApplyReplacementState(doc, thePage, destString, textState);
        return true;
    }

    /// <summary>Replace text and override the font size of the replacement run.</summary>
    public bool ReplaceText(string srcString, string destString, int fontSize)
    {
        var doc = EnsureBound();
        if (!ReplaceText(srcString, destString)) return false;
        ApplyReplacementState(doc, null, destString, new TextState { FontSize = fontSize });
        return true;
    }

    /// <summary>Whether the current search/replace should treat the source as a regex —
    /// driven by either the legacy <see cref="ReplaceTextStrategy"/> flag or the
    /// <see cref="TextSearchOptions"/> the caller set before replacing.</summary>
    private bool IsRegexSearch =>
        ReplaceTextStrategy.IsRegularExpressionUsed || TextSearchOptions.IsRegularExpression;

    /// <summary>Apply the replacement run's font size / colour / font by re-finding the
    /// inserted text and pushing the state onto each matched fragment. The fragment's
    /// TextState setters propagate to the content stream (via TextStateModifier), so the
    /// change survives the save. Font embedding is the caller's font's responsibility.</summary>
    private static void ApplyReplacementState(Document doc, int? thePage, string destString, TextState? textState)
    {
        if (textState is null || string.IsNullOrEmpty(destString)) return;
        var absorber = new TextFragmentAbsorber(destString);
        if (thePage is int p && p >= 1 && p <= doc.PageCount)
            doc.Pages.At(p).Accept(absorber);
        else
            absorber.Visit(doc);

        // Drive the content-stream rewrite through TextStateModifier (text + page
        // based): fragment-level TextState setters don't propagate because their
        // OwnerSegment is unset, so set the run's Tf size / fill colour directly.
        var modifier = new TextStateModifier();
        var done = new HashSet<Page>();
        foreach (TextFragment frag in absorber.TextFragments)
        {
            var pg = frag.Page;
            if (pg is null || !done.Add(pg)) continue;
            // Apply colour and size first (they match the run by its current encoding);
            // the font change re-encodes the run, so it must run last or the colour/size
            // text-match would no longer find the run.
            // The replacement run is written in the state's own rendering mode and the
            // mode in effect before it is restored afterwards — a recoloured run inside
            // an invisible (Tr 3) text layer has to be told to paint.
            if (textState.ForegroundColor is not null)
                modifier.ModifyForegroundColor(pg, destString, textState.ForegroundColor,
                    renderingMode: (int)textState.RenderingMode);
            // A family/style-only state carries the ctor's 10pt placeholder, not a
            // requested size: the replacement keeps the matched run's own size unless
            // the caller set one (the size ctors / int overload do).
            if (textState.FontSizeTouched && textState.FontSize > 0)
                modifier.ModifyFontSize(pg, destString, frag.TextState.FontSize, textState.FontSize);
            // Only swap the font when the caller actually requested a family. TextState.Font
            // always resolves to a default (Helvetica), so key on the explicitly-set FontName
            // (the family/style ctors set it) — otherwise a colour-or-size-only replacement
            // would needlessly re-font the run.
            if (!string.IsNullOrEmpty(textState.FontName))
            {
                // Resolve the styled variant from the requested family + FontStyle
                // (e.g. Times + Bold -> Times-Bold, Courier + Italic -> Courier-Oblique).
                // Prefer a host TrueType so the swap carries a glyph program ModifyFont can
                // embed; the metric-only Standard-14 stub has none and would no-op.
                var resolved = Aspose.Pdf.Text.FontRepository.FindEmbeddableStyledFont(textState.FontName!, textState.FontStyle)
                           ?? Aspose.Pdf.Text.FontRepository.TryFindFont(textState.FontName!, textState.FontStyle);
                var font = resolved ?? textState.Font;
                if (font is not null)
                {
                    // Only a resolved repository face carries a program to embed. The
                    // fallback is the run's own font, already in the document — asking to
                    // embed it would pull in a system face the caller never named.
                    if (resolved is not null) resolved.IsEmbedded = true;
                    modifier.ModifyFont(pg, destString, font);
                }
            }
        }
    }

    /// <summary>
    /// Replace all occurrences of text across all pages.
    /// Returns the modified PDF bytes.
    /// </summary>
    public byte[] ReplaceText(byte[] input, string searchText, string replaceText)
    {
        using var doc = Document.Open(input);
        var replacer = new TextReplacer();
        replacer.Replace(doc, searchText, replaceText);
        return doc.ToArray();
    }

    /// <summary>
    /// Replace all occurrences of text across all pages using search options.
    /// Returns the modified PDF bytes.
    /// </summary>
    public byte[] ReplaceText(byte[] input, string searchText, string replaceText, TextSearchOptions options)
    {
        using var doc = Document.Open(input);
        var absorber = new TextFragmentAbsorber(searchText, options);
        absorber.Visit(doc);

        // For each found fragment, do a content-stream level replacement
        // We use TextReplacer for actual stream modification, building the effective pattern
        var pattern = options.IsRegularExpression ? searchText : Regex.Escape(searchText);
        if (options.WholeWord)
            pattern = @"\b" + pattern + @"\b";
        var regexOptions = options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;

        foreach (var page in doc.Pages)
        {
            var text = ExtractPageText(doc, page);
            if (Regex.IsMatch(text, pattern, regexOptions))
            {
                var replacer = new TextReplacer();
                replacer.Replace(page, searchText, replaceText);
            }
        }
        return doc.ToArray();
    }

    private static string ExtractPageText(Document doc, Page page)
    {
        var absorber = new TextAbsorber();
        absorber.Visit(page);
        return absorber.Text;
    }

    /// <summary>
    /// Replace text on a specific page (1-based).
    /// </summary>
    public byte[] ReplaceTextOnPage(byte[] input, int pageNumber, string searchText, string replaceText)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var replacer = new TextReplacer();
        replacer.Replace(doc.Pages.At(pageNumber), searchText, replaceText);
        return doc.ToArray();
    }

    /// <summary>
    /// Extract text from a specific page.
    /// </summary>
    public string ExtractText(byte[] input, int pageNumber)
    {
        using var doc = Document.Open(input);
        if (pageNumber < 1 || pageNumber > doc.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber));

        var absorber = new TextAbsorber();
        absorber.Visit(doc.Pages.At(pageNumber));
        return absorber.Text;
    }

    /// <summary>
    /// Extract text from all pages.
    /// </summary>
    public string ExtractText(byte[] input)
    {
        using var doc = Document.Open(input);
        var absorber = new TextAbsorber();
        absorber.Visit(doc);
        return absorber.Text;
    }
}
