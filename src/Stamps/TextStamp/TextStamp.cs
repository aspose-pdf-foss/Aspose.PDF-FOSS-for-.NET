using System.Linq;
using System.Text;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Facades;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Stamps;

/// <summary>
/// A text stamp that can be applied to PDF pages.
/// </summary>
public partial class TextStamp : Stamp
{
    /// <summary>Default font used when <see cref="FontName"/> is not set explicitly.</summary>
    public static global::Aspose.Pdf.Text.Font DefaultFont => FontInfo.DefaultHelvetica;

    /// <summary>Default font size used when <see cref="FontSize"/> is not set explicitly.</summary>
    public static float DefaultFontSize => 12.0f;

    /// <summary>The text to stamp.</summary>
    public string Text { get; set; }

    /// <summary>Alias for <see cref="Text"/>.</summary>
    public string Value
    {
        get => Text;
        set => Text = value;
    }

    /// <summary>Font name (must exist in page resources or be a Standard 14 font).</summary>
    public string FontName { get; set; } = "Helvetica";

    /// <summary>Font size in points.</summary>
    public float FontSize { get; internal set; } = 12f;

    /// <summary>Text color. Defaults to black.</summary>
    public Color Color { get; set; } = Aspose.Pdf.Color.FromArgb(0, 0, 0);

    /// <summary>Text formatting state. When set, <see cref="FontSize"/> and <see cref="Color"/> are derived from it.</summary>
    public TextState TextState { get; set; } = new TextState();

    /// <summary>Whether to wrap text at <see cref="Width"/> boundary.</summary>
    public bool WordWrap { get; set; }

    /// <summary>Word-wrap mode applied to the stamp text. Stored only — the
    /// renderer treats anything other than <see cref="TextFormattingOptions.WordWrapMode.NoWrap"/>
    /// as wrapping enabled.</summary>
    public TextFormattingOptions.WordWrapMode WordWrapMode { get; set; } = TextFormattingOptions.WordWrapMode.NoWrap;

    /// <summary>Width constraint used for word wrapping (in points).</summary>
    public double Width { get; set; }

    /// <summary>Effective wrap width (points) used to break the stamp text into
    /// rows. The base uses <see cref="Width"/>; the compat surface overrides this
    /// to prefer its <c>MaxRowWidth</c>.</summary>
    protected virtual double WrapWidth => Width;

    /// <summary>When true, the stamp shrinks/grows its font size so the word-wrapped
    /// text fits the <see cref="Width"/>×<see cref="Height"/> box. Off in the base;
    /// the compat surface maps it onto <c>AutoAdjustFontSizeToFitStampRectangle</c>.</summary>
    protected virtual bool AutoFitToBox => false;

    /// <summary>Bisection stop interval (points) for the auto-fit font-size search.</summary>
    protected virtual double AutoFitPrecision => 0.1;

    /// <summary>Height constraint for the stamp box. Stored only — the
    /// renderer auto-sizes around the text.</summary>
    public double Height { get; set; }

    /// <summary>When true, the stamp text is scaled to fit
    /// <see cref="Width"/> × <see cref="Height"/>. Stored only, for API
    /// compatibility.</summary>
    public bool Scale { get => _scale ?? false; set => _scale = value; }

    // Scale DEFAULTS to true for the Type0 (CID) stamp layout: an
    // unset Scale stretches the block to Width and, given a Height, scales the
    // pitch to it — only an EXPLICIT Scale=false turns that off (measured: rd3_wrapA
    // stretches by default; Scale=false keeps natural row widths). The
    // public getter keeps its false default because the WinAnsi BuildScaledToBox
    // gate was calibrated against it.
    private bool? _scale;
    private protected bool CidScaleEnabled => _scale ?? true;

    /// <summary>Zoom factor applied to the stamp. Stored only, for API
    /// compatibility.</summary>
    public double Zoom { get; set; } = 1.0;

    /// <summary>
    /// Horizontal alignment of the text lines inside the stamp box.
    /// Distinct from <see cref="Stamp.HorizontalAlignment"/> which positions the whole stamp on the page.
    /// </summary>
    public HorizontalAlignment TextAlignment { get; set; } = HorizontalAlignment.None;

    public TextStamp(string text)
    {
        Text = text;
    }

    /// <summary>
    /// Create a TextStamp from a <see cref="FormattedText"/> (flattens its lines to a single string,
    /// joined by '\n').
    /// </summary>
    public TextStamp(FormattedText formattedText)
    {
        Text = formattedText is null
            ? string.Empty
            : string.Join("\n", formattedText.Lines.Select(l => l.Text));
        if (formattedText is not null)
        {
            // TextState is the effective source of font/size at render time (its
            // defaults win over the bare stamp properties), so the FormattedText's
            // font and size must land there, not only on FontSize/FontName.
            FontSize = (float)formattedText.FontSize;
            TextState.FontSize = (float)formattedText.FontSize;
            if (!string.IsNullOrEmpty(formattedText.FontName))
            {
                FontName = formattedText.FontName;
                TextState.FontName = formattedText.FontName;
            }
            // The template's backdrop colour paints behind the stamp text
            // (edge to edge of the stamp box).
            if (!formattedText.BackgroundColor.IsEmpty)
                TextState.BackgroundColor = formattedText.BackgroundColor;
        }
    }

    /// <summary>The replacement font program (raw TrueType bytes + name) used to render
    /// glyphs the primary font lacks. Null when no fallback is configured. Overridden by the
    /// public <c>Aspose.Pdf.TextStamp</c>, which exposes the <c>ReplacementFont</c> property.</summary>
    protected virtual (byte[] ttf, string name)? ReplacementFontProgram => null;

    /// <summary>True when the caller declared YIndent to be the text BASELINE rather than
    /// the box edge — the bottom seat then lands exactly on it, with no descent inset.
    /// Overridden by the public <c>Aspose.Pdf.TextStamp</c> (TreatYIndentAsBaseLine).</summary>
    protected virtual bool YIndentIsBaseline => false;

    /// <summary>When the stamp's base font is a non-embedded Standard-14 face, resolve the
    /// matching TrueType substitute (Arial / Times New Roman / Courier New, honouring
    /// bold/italic) and return its program, so a stamp carrying glyphs outside WinAnsi can be
    /// drawn with an embedded Type0 font — the only way those glyphs actually render.</summary>
    private (byte[] ttf, string name)? TryResolveUnicodeFallback()
    {
        var fn = TextState?.FontName ?? FontName ?? "Helvetica";
        var family = fn;
        foreach (var suffix in new[] { "-BoldOblique", "-BoldItalic", "-Bold", "-Oblique", "-Italic" })
            if (family.EndsWith(suffix, StringComparison.Ordinal)) { family = family.Substring(0, family.Length - suffix.Length); break; }
        if (!Std14ToTrueType.TryGetValue(family, out var ttFamily)) return null;

        var bold = TextState?.IsBold ?? false;
        var italic = TextState?.IsItalic ?? false;
        var style = (bold ? FontStyles.Bold : 0) | (italic ? FontStyles.Italic : 0);
        var font = Aspose.Pdf.Text.FontRepository.TryFindFont(ttFamily, style, true)
                   ?? Aspose.Pdf.Text.FontRepository.TryFindFont(ttFamily, true);
        if (font is null) return null;
        var ttf = font.SourceFontData?.TtfData;
        if (ttf is null || ttf.Length == 0) return null;
        return (ttf, font.FontName);
    }

    /// <summary>A plain single-line stamp with no scale / rotation / wrap / background box —
    /// the shape <see cref="BuildCidStamp"/> renders. Gates the auto Unicode fallback so
    /// feature-rich stamps keep the single-byte path.</summary>
    private bool IsPlainBlockStamp()
    {
        var wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;
        var rot = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        return !Scale && Math.Abs(rot) < 0.01 && !wrapping
            && (TextState?.BackgroundColor is null || TextState.BackgroundColor.IsEmpty)
            && !string.IsNullOrEmpty(Text) && !Text.Contains('\n');
    }

    // Per-font-program glyph coverage, shared across stamps (parsing a face is
    // costly; the resolver caches the byte[] per name so reference identity holds).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], GlyphOutlineParser>
        _runCoverage = new();

    /// <summary>The system face for a code point the stamp's primary face lacks,
    /// chosen by script block and verified to actually cover the code point.</summary>
    private static (byte[] ttf, string name)? ResolveSupplementaryFont(int cp)
    {
        var candidates = cp switch
        {
            >= 0x13000 and <= 0x1345F => new[] { "Segoe UI Historic" },                 // Egyptian hieroglyphs
            >= 0x20000 and <= 0x3FFFF => new[] { "SimSun-ExtB", "SimSun-ExtG" },        // CJK Ext-B and later
            >= 0x1F300 and <= 0x1FAFF => new[] { "Segoe UI Emoji", "Segoe UI Symbol" }, // emoji / pictographs
            >= 0x10000 => new[] { "Segoe UI Historic", "Segoe UI Symbol" },             // other historic scripts
            _ => new[] { "Arial", "SimSun", "MS Gothic", "Segoe UI Symbol" },           // uncovered BMP
        };
        foreach (var name in candidates)
        {
            var ttf = SystemFontResolver.Resolve(name);
            if (ttf is { Length: > 12 } && CoversCp(ttf, cp)) return (ttf, name);
        }
        return null;
    }

    /// <summary>Split one text row into (face, text) runs: the primary face keeps
    /// everything it covers (spaces always stay with the current run), while code
    /// points it lacks pull in their script's face when one resolves — so a mixed
    /// CJK-Ext-B / hieroglyph / Latin stamp draws every script with real glyphs.</summary>
    private static System.Collections.Generic.List<(byte[] ttf, string name, string text)>
        SplitFontRuns(string row, byte[] primaryTtf, string primaryName)
    {
        var runs = new System.Collections.Generic.List<(byte[] ttf, string name, string text)>();
        var sb = new StringBuilder();
        var curTtf = primaryTtf;
        var curName = primaryName;
        void Flush()
        {
            if (sb.Length > 0) { runs.Add((curTtf, curName, sb.ToString())); sb.Clear(); }
        }
        for (var i = 0; i < row.Length; i++)
        {
            int cp = row[i];
            var pair = false;
            if (char.IsHighSurrogate(row[i]) && i + 1 < row.Length && char.IsLowSurrogate(row[i + 1]))
            {
                cp = char.ConvertToUtf32(row[i], row[i + 1]);
                pair = true;
            }
            var tgtTtf = primaryTtf;
            var tgtName = primaryName;
            if (cp == ' ')
            {
                tgtTtf = curTtf; tgtName = curName;
            }
            else if (!CoversCp(primaryTtf, cp) && ResolveSupplementaryFont(cp) is { } fb)
            {
                tgtTtf = fb.ttf; tgtName = fb.name;
            }
            if (!ReferenceEquals(tgtTtf, curTtf)) { Flush(); curTtf = tgtTtf; curName = tgtName; }
            sb.Append(row[i]);
            if (pair) { sb.Append(row[i + 1]); i++; }
        }
        Flush();
        return runs;
    }

    /// <summary>The candidate system CJK faces for the script of <paramref name="text"/>,
    /// resolved to standalone TTF bytes (ttc entries extracted). Null when none load.</summary>
    internal static (byte[] ttf, string name)? TryResolveCjkTtf(string text)
    {
        bool kana = false, hangul = false, han = false, extb = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                // CJK Ext-B and later planes (these are stamped in MingLiU-ExtB
                // even when the caller declared a covering face).
                if (char.ConvertToUtf32(ch, text[i + 1]) is >= 0x20000 and <= 0x3FFFF) extb = true;
                i++;
                continue;
            }
            if (ch is >= '぀' and <= 'ヿ') kana = true;
            else if (ch is >= '가' and <= '힯' or >= 'ᄀ' and <= 'ᇿ') hangul = true;
            else if (ch is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') han = true;
        }
        if (!kana && !hangul && !han && !extb) return null;
        var candidates = kana
            ? new[] { "MS-Gothic", "MS Gothic", "Yu Gothic", "Meiryo" }
            : hangul
            ? new[] { "Malgun Gothic", "Gulim", "Batang" }
            : han
            ? new[] { "SimSun", "MS-Gothic", "Microsoft YaHei" }
            : new[] { "MingLiU-ExtB", "SimSun-ExtB" };
        foreach (var name in candidates)
        {
            var ttf = Aspose.Pdf.Text.SystemFontResolver.Resolve(name);
            if (ttf is { Length: > 12 }) return (ttf, name.Replace("-", " "));
        }
        return null;
    }

    /// <summary>Greedy row fill for the Type0 stamp path at NATURAL advances against
    /// <paramref name="budget"/>: char-level tokens under DiscretionaryHyphenation
    /// (a break can land mid-word), whole words otherwise, and no kinsoku — a closing
    /// bracket happily leads a row. An overlong single token keeps its own row.</summary>
    private static System.Collections.Generic.List<System.Collections.Generic.List<(byte[] ttf, string name, string text)>>
        FillCidRows(PdfDictionary fontDict,
                    System.Collections.Generic.List<(byte[] ttf, string name, string text)> runs,
                    double fontSize, double budget, bool charLevel)
    {
        // Tokenise the run list, keeping each token's face.
        var tokens = new System.Collections.Generic.List<(byte[] ttf, string name, string text)>();
        foreach (var (runTtf, runName, runText) in runs)
        {
            if (charLevel)
            {
                for (var i = 0; i < runText.Length; i++)
                {
                    var len = char.IsHighSurrogate(runText[i]) && i + 1 < runText.Length ? 2 : 1;
                    tokens.Add((runTtf, runName, runText.Substring(i, len)));
                    i += len - 1;
                }
            }
            else
            {
                // Words keep their trailing space (its width charges the line it ends).
                var start = 0;
                for (var i = 0; i < runText.Length; i++)
                {
                    if (runText[i] == ' ')
                    {
                        tokens.Add((runTtf, runName, runText.Substring(start, i - start + 1)));
                        start = i + 1;
                    }
                }
                if (start < runText.Length)
                    tokens.Add((runTtf, runName, runText.Substring(start)));
            }
        }

        var rows = new System.Collections.Generic.List<System.Collections.Generic.List<(byte[] ttf, string name, string text)>>();
        var cur = new System.Collections.Generic.List<(byte[] ttf, string name, string text)>();
        double curW = 0;
        foreach (var tok in tokens)
        {
            var tokW = Aspose.Pdf.Text.Type0FontEmbedder.MeasureText(
                fontDict, tok.ttf, tok.name, tok.text, fontSize, stripSpacesInBaseFont: true);
            if (cur.Count > 0 && curW + tokW > budget + 1e-6)
            {
                rows.Add(MergeRowRuns(cur));
                cur = new();
                curW = 0;
                // A bare space never opens a row.
                if (tok.text == " ") continue;
            }
            cur.Add(tok);
            curW += tokW;
        }
        if (cur.Count > 0) rows.Add(MergeRowRuns(cur));
        if (rows.Count == 0) rows.Add(new());
        return rows;
    }

    /// <summary>Merge consecutive same-face tokens back into runs (one Tf + one show op
    /// per face stretch, as the un-wrapped path emits).</summary>
    private static System.Collections.Generic.List<(byte[] ttf, string name, string text)>
        MergeRowRuns(System.Collections.Generic.List<(byte[] ttf, string name, string text)> tokens)
    {
        var merged = new System.Collections.Generic.List<(byte[] ttf, string name, string text)>();
        foreach (var tok in tokens)
        {
            if (merged.Count > 0 && ReferenceEquals(merged[^1].ttf, tok.ttf))
                merged[^1] = (merged[^1].ttf, merged[^1].name, merged[^1].text + tok.text);
            else
                merged.Add(tok);
        }
        // The row's trailing spaces carry no ink and would misreport the row width.
        if (merged.Count > 0)
        {
            var last = merged[^1];
            var trimmed = last.text.TrimEnd(' ');
            if (trimmed.Length == 0 && merged.Count > 1) merged.RemoveAt(merged.Count - 1);
            else merged[^1] = (last.ttf, last.name, trimmed);
        }
        return merged;
    }

    // Width (points) of one encoded row at the given size, using Standard-14
    // metrics for base-14 fonts and a 0.5-em fallback otherwise (matches the
    // estimate used by the wrapper).
    // Cache of system TrueType faces (per family) used only to read unrounded
    // advance widths. The integer /W and AFM tables drop the sub-unit precision
    // that exact stamp geometry (a background box sized to the text) needs.
    private static readonly System.Collections.Generic.Dictionary<string, Aspose.Pdf.Text.TrueTypeParser?> _metricParsers = new();
    private static readonly object _metricParsersLock = new();

    // Page-space anchor of the text block: the X of its left edge and the Y of
    // its TOP line's baseline. Drawing proceeds downward from this baseline.
    // Uses the block's real (metrics-derived, post-scale) width so Right/Center
    // alignment keeps the text inside the page instead of overflowing — the old
    // 0.5-em-per-char estimate placed a 42pt right-aligned stamp off the page.
    private (double originX, double topBaseline) ComputeBlockOrigin(
        Page page, double scaledBlockWidth, double fontSize, double lineHeight, int rowCount,
        string baseFontName)
    {
        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var blockHeight = Math.Max(1, rowCount) * lineHeight;

        var originX = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (pageWidth - scaledBlockWidth) / 2,
            HorizontalAlignment.Right => pageWidth - scaledBlockWidth - XIndent - RightMargin,
            // LeftMargin stands in when no XIndent is set — the same fallback the
            // CID-stamp block placement applies.
            _ => XIndent > 0 ? XIndent : LeftMargin,
        };

        // topBaseline is the first line's baseline. Top: one line below the top
        // edge; Bottom: leave room for the remaining lines above the bottom edge;
        // Center: centre the whole block vertically.
        // BottomMargin measures to the bottom of the text box (the descender), so
        // the last baseline seats one font descent above it — unless the caller
        // declared YIndent to BE the baseline, which seats exactly there.
        var d = Aspose.Pdf.Text.Standard14Fonts.GetDescent(baseFontName);
        var descentInset = (d < 0 ? -d / 1000.0 : 0.2) * fontSize;
        var topBaseline = VerticalAlignment switch
        {
            VerticalAlignment.Top => pageHeight - TopMargin - YIndent - fontSize,
            VerticalAlignment.Center => (pageHeight + blockHeight) / 2 - fontSize,
            VerticalAlignment.Bottom => YIndent + BottomMargin + blockHeight - fontSize
                + (YIndentIsBaseline ? 0 : descentInset),
            _ => YIndent + BottomMargin + blockHeight - fontSize,
        };

        return (originX, topBaseline);
    }
}
