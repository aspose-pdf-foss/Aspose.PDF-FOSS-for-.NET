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
public class TextStamp : Stamp
{
    /// <summary>Default font used when <see cref="FontName"/> is not set explicitly.</summary>
    public static Font DefaultFont => FontInfo.DefaultHelvetica;

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
    public Color Color { get; set; } = Aspose.Pdf.Color.FromRgb(0, 0, 0);

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
    public bool Scale { get; set; }

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

    /// <summary>True when at least one character of <paramref name="text"/> could not be
    /// encoded by the primary font (it collapsed to '?' although the source char was not '?').</summary>
    private static bool HasUnencodableGlyphs(string text, byte[] encoded)
    {
        var n = Math.Min(text.Length, encoded.Length);
        for (var i = 0; i < n; i++)
            if (text[i] != '?' && encoded[i] == (byte)'?')
                return true;
        return false;
    }

    /// <summary>Standard-14 family → the TrueType face a viewer substitutes for it. A
    /// non-embedded Standard-14 font can't actually display glyphs outside WinAnsi (the
    /// viewer's substitute lacks them), so for non-WinAnsi stamp text the matching TrueType
    /// is embedded instead.</summary>
    private static readonly Dictionary<string, string> Std14ToTrueType =
        new(StringComparer.Ordinal)
        {
            ["Helvetica"] = "Arial",
            ["Times-Roman"] = "Times New Roman",
            ["Times"] = "Times New Roman",
            ["Courier"] = "Courier New",
        };

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
        var font = Aspose.Pdf.Text.FontRepository.FindFont(ttFamily, style, true)
                   ?? Aspose.Pdf.Text.FontRepository.FindFont(ttFamily, true);
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

    /// <summary>Resolve (creating if needed) the page's /Resources /Font dictionary so a new
    /// font can be registered there; AddStampForm later shares it into the stamp form.</summary>
    private static PdfDictionary GetPageFontDict(Page page)
    {
        var resources = page.Dict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fontDict = resources.Get("Font") as PdfDictionary
            ?? page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }
        return fontDict;
    }

    /// <summary>True when the text contains any supplementary-plane code point
    /// (encoded as a UTF-16 surrogate pair).</summary>
    private static bool HasSupplementaryChars(string text)
    {
        foreach (var ch in text)
            if (char.IsSurrogate(ch)) return true;
        return false;
    }

    // Per-font-program glyph coverage, shared across stamps (parsing a face is
    // costly; the resolver caches the byte[] per name so reference identity holds).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], GlyphOutlineParser>
        _runCoverage = new();

    private static bool CoversCp(byte[] ttf, int cp)
    {
        try
        {
            var parser = _runCoverage.GetValue(ttf, static t => new GlyphOutlineParser(t));
            return parser.CMap.TryGetValue(cp, out var gid) && gid != 0;
        }
        catch { return false; }
    }

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
        bool kana = false, hangul = false, han = false;
        foreach (var ch in text)
        {
            if (ch is >= '぀' and <= 'ヿ') kana = true;
            else if (ch is >= '가' and <= '힯' or >= 'ᄀ' and <= 'ᇿ') hangul = true;
            else if (ch is >= '一' and <= '鿿' or >= '㐀' and <= '䶿') han = true;
        }
        if (!kana && !hangul && !han) return null;
        var candidates = kana
            ? new[] { "MS-Gothic", "MS Gothic", "Yu Gothic", "Meiryo" }
            : hangul
            ? new[] { "Malgun Gothic", "Gulim", "Batang" }
            : new[] { "SimSun", "MS-Gothic", "Microsoft YaHei" };
        foreach (var name in candidates)
        {
            var ttf = Aspose.Pdf.Text.SystemFontResolver.Resolve(name);
            if (ttf is { Length: > 12 }) return (ttf, name.Replace("-", " "));
        }
        return null;
    }

    /// <summary>Draw the whole stamp with an embedded Type0 replacement font (Identity-H +
    /// ToUnicode), so Unicode/CJK text both renders and round-trips through text extraction.
    /// Handles multi-line text: rows stack downward one em apart, each aligned within the
    /// block per <see cref="TextAlignment"/>, the block placed per the stamp alignments
    /// (margins honoured) — measured with the embedded font's /W advances so extraction
    /// re-measures the exact same widths.</summary>
    private byte[] BuildCidStamp(Page page, byte[] ttf, string fontName, double fontSize, Color color)
    {
        var fontDict = GetPageFontDict(page);
        var rows = Text.Replace("\r\n", "\n").Split('\n');
        var rowRuns = new System.Collections.Generic.List<(string res, byte[] hex, double width)>[rows.Length];
        var widths = new double[rows.Length];
        for (var i = 0; i < rows.Length; i++)
        {
            rowRuns[i] = new();
            foreach (var (runTtf, runName, runText) in SplitFontRuns(rows[i], ttf, fontName))
            {
                var (res, hex) = Aspose.Pdf.Text.Type0FontEmbedder.Embed(
                    fontDict, runTtf, runName, runText, stripSpacesInBaseFont: true);
                var w = Aspose.Pdf.Text.Type0FontEmbedder.MeasureText(
                    fontDict, runTtf, runName, runText, fontSize, stripSpacesInBaseFont: true);
                rowRuns[i].Add((res, hex, w));
                widths[i] += w;
            }
        }
        var blockW = widths.Length > 0 ? widths.Max() : 0.0;

        // Block placement: alignment when set (margins honoured), else the legacy
        // XIndent/YIndent (or top-left inset) placement.
        var x = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (page.Width - blockW) / 2,
            HorizontalAlignment.Right => page.Width - blockW - XIndent - RightMargin,
            _ => XIndent > 0 ? XIndent : LeftMargin,
        };
        var y = VerticalAlignment switch
        {
            VerticalAlignment.Top => page.Height - TopMargin - YIndent - fontSize,
            VerticalAlignment.Center => (page.Height + rows.Length * fontSize) / 2 - fontSize,
            VerticalAlignment.Bottom => YIndent + BottomMargin + rows.Length * fontSize - fontSize,
            _ => YIndent > 0 ? YIndent : page.Height - TopMargin - fontSize,
        };

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0);
        for (var i = 0; i < rows.Length; i++)
        {
            var rowX = TextAlignment switch
            {
                HorizontalAlignment.Right => x + blockW - widths[i],
                HorizontalAlignment.Center => x + (blockW - widths[i]) / 2,
                _ => x,
            };
            builder.SetTextMatrix(1, 0, 0, 1, rowX, y - i * fontSize);
            foreach (var (res, hex, _) in rowRuns[i])
                builder.SetFont(res, fontSize).ShowTextHex(hex);
        }
        builder
            .EndText();
        builder.RestoreState();
        return builder.Build();
    }

    internal override byte[] BuildContentStream(Page page)
    {
        // Pull effective font/size/colour from TextState first (setting
        // TextState.* on a stamp wins over the
        // bare TextStamp.FontSize/Color), falling back to the stamp's own
        // properties for callers that don't touch TextState.
        var baseFontName = ResolveBaseFontName();
        var fontSize = TextState?.FontSize > 0 ? (double)TextState.FontSize : FontSize;
        var color = TextState?.ForegroundColor ?? Color;

        // Encode Text into single-byte PDF string bytes against WinAnsi, and
        // collect any code-point / glyph-name pairs the resulting font must
        // declare via /Differences so non-WinAnsi chars (Polish ę/ą/ś/ł/ń/ź/ż/ć,
        // Czech č, etc.) render instead of falling back to '?'.
        var encoded = EncodeForWinAnsi(Text, out var diffMap);

        // Auto-fit: pick the largest font size at which the word-wrapped text still
        // fits the Width×Height box (bisection to AutoFitPrecision). Do this before
        // any layout so the chosen size flows into the render below and is readable
        // via the FontSize property once the stamp has been added.
        if (AutoFitToBox && Width > 0 && Height > 0)
        {
            fontSize = ComputeAutoFitFontSize(baseFontName, encoded);
            FontSize = (float)fontSize;
        }

        // When the primary font can't represent some glyphs (e.g. CJK/Unicode collapses to
        // '?') and a replacement font program is configured, embed it as a Type0/CIDFontType2
        // font and draw the whole stamp with it so the text renders and round-trips through
        // extraction (the recurring non-Latin1 stamp-text path).
        var replacement = ReplacementFontProgram;
        if (replacement is { } rf && HasUnencodableGlyphs(Text, encoded))
            return BuildCidStamp(page, rf.ttf, rf.name, fontSize, color);

        // CJK stamp text with no explicit replacement font: the configured Latin face
        // has no such glyphs (they collapsed to '?'), so embed a system CJK face as a
        // Type0 font — mirroring the generator's CJK fallback — so the text renders
        // and round-trips through extraction.
        if (replacement is null && HasUnencodableGlyphs(Text, encoded)
            && TryResolveCjkTtf(Text) is { } cjk)
            return BuildCidStamp(page, cjk.ttf, cjk.name, fontSize, color);

        // Supplementary-plane text (CJK Ext-B, Egyptian hieroglyphs, emoji …) with no
        // BMP-CJK face matched above: the Latin base face's TrueType substitute anchors
        // the stamp and each supplementary run brings its own script face (resolved per
        // code point inside BuildCidStamp), so the text renders where a face exists and
        // always round-trips through extraction via per-CID ToUnicode.
        if (replacement is null && HasUnencodableGlyphs(Text, encoded)
            && HasSupplementaryChars(Text) && TryResolveUnicodeFallback() is { } ufb)
            return BuildCidStamp(page, ufb.ttf, ufb.name, fontSize, color);

        // No explicit replacement font, but the text carries glyphs outside WinAnsi (Polish
        // ę/ą/ś/…, etc.) that a non-embedded Standard-14 base font can't display: embed the
        // matching TrueType substitute as a Type0 font so the glyphs render and round-trip
        // through extraction. Gated to a plain single-line stamp (BuildCidStamp's shape).
        if (replacement is null && diffMap.Count > 0 && IsPlainBlockStamp()
            && TryResolveUnicodeFallback() is { } auto)
            return BuildCidStamp(page, auto.ttf, auto.name, fontSize, color);

        var fontResName = EnsureFontResource(page, baseFontName, diffMap);

        // Wrapping is enabled by the WordWrap bool OR a non-NoWrap WordWrapMode
        // (both are exposed; this ctor path sets only the bool).
        var wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;

        // Scale-to-fit: a stamp with Scale=true and an explicit Width×Height box
        // lays its text out at the base font, then non-uniformly scales that block
        // to exactly fill the box, anchored at (XIndent, YIndent) —
        // emitting `sx 0 0 sy XIndent YIndent cm` over a natural-size
        // form. Wrapped text fills width at scale ~1 and stretches
        // vertically; un-wrapped text is laid as a single line and squished to width.
        if (Scale && Width > 0 && Height > 0)
            return BuildScaledToBox(page, encoded, baseFontName, fontResName, fontSize, color, wrapping);

        // Rotated stamp with an explicit Width×Height box: the text scales
        // non-uniformly to fill the box (sx=Width/textW, sy=Height/textH), the box is
        // centred per Horizontal/VerticalAlignment, then rotated about the box centre.
        // The plain path below only applies the horizontal scale and
        // rotates about the block anchor, which mis-sizes/positions the result.
        double rot = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        if (Math.Abs(rot) > 0.01 && Width > 0 && Height > 0)
            return BuildBoxRotated(page, encoded, baseFontName, fontResName, fontSize, color, wrapping, rot);

        // Word-wrapped stamp with a background box and Scale=false: wrap the text to the
        // inner width (Width minus L/R margins), grow the box to the widest wrapped line,
        // and emit the box as the leading `q / x y w h re / rg / RG / f*`
        // block — the text follows inside it.
        var bgEarly = TextState?.BackgroundColor;
        if (!Scale && wrapping && Width > 0 && bgEarly is { IsEmpty: false })
            return BuildWrappedBackgroundBox(page, encoded, baseFontName, fontResName, fontSize, color, bgEarly!);

        // Break the text into display rows: wrap to the stamp width when wrapping
        // is on, otherwise split on the explicit '\n' line breaks that a
        // FormattedText (AddNewLineText) or a multi-line Value carries. The old
        // no-wrap path emitted the raw '\n' byte inside a single Tj string, which
        // is not a line break in PDF — every line collapsed onto one row.
        // When wrapping is requested but no explicit stamp width is set, wrap to the
        // page's available extent along the text's advance axis so a WordWrap stamp lays
        // out as multiple on-page lines instead of one line running off the edge — which
        // page-bounds text extraction would then crop. The extent is the UNROTATED
        // MediaBox dimension: the stamp's own Rotate turns the advance vertical at 90/270
        // (Rotation values are degrees, %180==90 catches both), while page /Rotate is a
        // view transform only — extraction bounds are the unrotated MediaBox, so page.Width
        // /Height (which swap for a rotated page) must not choose the wrap axis.
        var stampDeg = (int)Math.Round(Math.Abs(RotateAngle != 0 ? RotateAngle : (double)Rotate));
        var wrapVertical = stampDeg % 180 == 90;
        var mb = page.MediaBox;
        var advanceDim = wrapVertical ? mb.Height : mb.Width;
        var wrapLead = wrapVertical ? (YIndent > 0 ? YIndent : BottomMargin) : (XIndent > 0 ? XIndent : LeftMargin);
        var wrapTrail = wrapVertical ? TopMargin : RightMargin;
        var wrapWidth = WrapWidth > 0
            ? WrapWidth
            : (WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap
                ? Math.Max(0.0, advanceDim - wrapLead - wrapTrail)
                : 0.0);
        var rows = (wrapWidth > 0 && WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap)
            ? WrapEncoded(encoded, baseFontName, fontSize, wrapWidth)
            : SplitRows(encoded);

        // Natural (un-scaled) row widths and the block width (the widest row).
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;

        // A stamp with an explicit Width stretches/condenses its text horizontally
        // to fill that width (the whole stamp form scales
        // by Width / naturalWidth). No Width ⇒ draw at natural size.
        var scaleX = (Width > 0 && blockWidth > 0) ? Width / blockWidth : 1.0;
        var scaledBlockWidth = blockWidth * scaleX;

        // Leading of one em (stamp lines are spaced by exactly the font size).
        var lineHeight = fontSize;

        // Position the block on the page. The block's left/top is derived from the
        // SCALED width so Right/Center alignment lands the right/centre at the page
        // edge/centre, and the first baseline sits one line below the top edge.
        var (originX, topBaseline) = ComputeBlockOrigin(page, scaledBlockWidth, fontSize, lineHeight, rows.Count, baseFontName);

        var builder = new ContentStreamBuilder();
        builder.SaveState();

        if (Opacity < 1.0)
        {
            var gs = new Content.ExtGState
            {
                FillAlpha = Opacity,
                StrokeAlpha = Opacity,
            };
            var gsName = page.AddExtGState(gs);
            builder.SetExtGState(gsName);
        }

        // Place + scale the block with a single cm: rotate about the block anchor
        // when requested, then apply the horizontal fill-scale. Drawing happens in
        // block-local coordinates (top line baseline at y=0, growing downward).
        double rotateDeg = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        var cos = Math.Cos(rotateDeg * Math.PI / 180);
        var sin = Math.Sin(rotateDeg * Math.PI / 180);

        var bgColor = TextState?.BackgroundColor;
        var hasBg = bgColor is { IsEmpty: false };

        // A multi-line block without a background box anchors
        // at its BOTTOM — the last row's baseline sits one font-descent above
        // the block origin and each row's Tm carries the absolute in-block Y
        // ((N-1-li)·lineHeight + descent). The cm translation is lowered by the same
        // amount, so the net page placement is unchanged; only the Tm/cm split moves.
        var bottomAnchor = 0.0;
        if (rows.Count > 1 && !hasBg)
        {
            var d = Aspose.Pdf.Text.Standard14Fonts.GetDescent(baseFontName);
            var descentInset = (d < 0 ? -d / 1000.0 : 0.2) * fontSize;
            bottomAnchor = (rows.Count - 1) * lineHeight + descentInset;
        }
        var cmY = topBaseline - bottomAnchor;
        // A 90/270-rotated stamp advances along page Y; the corner-anchored matrix below
        // would swing the block off the page (advance one way off the baseline, rows into
        // −X). Re-anchor so the block's rotated page-space bounding box honours
        // Horizontal/VerticalAlignment inside the unrotated MediaBox, keeping every glyph
        // on-page. Upright 0/180 stamps keep their existing anchor.
        var rotQuarter = ((int)Math.Round(Math.Abs(rotateDeg))) % 360;
        if (rotQuarter == 90 || rotQuarter == 270)
        {
            var advExtent = scaledBlockWidth;                                                // page-Y span
            var crossExtent = (rows.Count - 1) * lineHeight + (hasBg ? 1.1 : 1.0) * fontSize; // page-X span
            var pageXMin = HorizontalAlignment == HorizontalAlignment.Right
                ? mb.Width - RightMargin - crossExtent
                : HorizontalAlignment == HorizontalAlignment.Center
                    ? (mb.Width - crossExtent) / 2
                    : (XIndent > 0 ? XIndent : LeftMargin);
            var pageYMin = VerticalAlignment == VerticalAlignment.Top
                ? mb.Height - TopMargin - advExtent
                : VerticalAlignment == VerticalAlignment.Center
                    ? (mb.Height - advExtent) / 2
                    : (YIndent > 0 ? YIndent : BottomMargin);
            originX = pageXMin + crossExtent;                          // rows map to X in [pageXMin, pageXMin+cross]
            cmY = rotQuarter == 90 ? pageYMin : pageYMin + advExtent;  // advance spans [pageYMin, pageYMin+adv]
        }
        else if (rotQuarter == 180)
        {
            // A 180-flipped block runs its (horizontal) advance and rows in the negative
            // direction from the corner anchor — HAlign.Left would push the text off the
            // left/top edge. Re-anchor so the flipped page-space box honours alignment
            // inside the unrotated MediaBox (advance along page X, rows down page Y).
            var advExtent = scaledBlockWidth;                                                // page-X span
            var crossExtent = (rows.Count - 1) * lineHeight + (hasBg ? 1.1 : 1.0) * fontSize; // page-Y span
            var pageXMin = HorizontalAlignment == HorizontalAlignment.Right
                ? mb.Width - RightMargin - advExtent
                : HorizontalAlignment == HorizontalAlignment.Center
                    ? (mb.Width - advExtent) / 2
                    : (XIndent > 0 ? XIndent : LeftMargin);
            var pageYMin = VerticalAlignment == VerticalAlignment.Top
                ? mb.Height - TopMargin - crossExtent
                : VerticalAlignment == VerticalAlignment.Center
                    ? (mb.Height - crossExtent) / 2
                    : (YIndent > 0 ? YIndent : BottomMargin);
            originX = pageXMin + advExtent;    // page X spans [pageXMin, pageXMin+adv]
            cmY = pageYMin + crossExtent;      // page Y spans [pageYMin, pageYMin+cross]
        }
        // An OFF-AXIS rotated stamp (e.g. 45°) is anchored by its ROTATED BOUNDING BOX,
        // not by the baseline start: the stamp's content box rotates about
        // the box origin and translates so the rotated box's min corner lands at
        // (XIndent, YIndent) (the matrix composes size·scale·rotation·shift(point);
        // the anchor is offset by the rotated box extents). Pinning the baseline start
        // (originX, cmY) leaves the stamp shifted by the rotated box's overhang. Applied
        // only to the SIMPLE case it is derived for — a single-line, XIndent/YIndent-placed
        // stamp with no alignment override, background box, wrap or width; quarter rotations
        // (90/180/270) use the alignment re-anchor above instead.
        var rotAnchorSimple = Math.Abs(rotateDeg) > 0.01
            && rotQuarter != 90 && rotQuarter != 180 && rotQuarter != 270
            && (HorizontalAlignment == HorizontalAlignment.Left
                || HorizontalAlignment == HorizontalAlignment.None)
            && !hasBg
            && rows.Count == 1
            && Width <= 0;
        if (rotAnchorSimple)
        {
            var boxW = scaledBlockWidth;
            var descF = Aspose.Pdf.Text.Standard14Fonts.GetDescent(baseFontName);
            var descent = (descF < 0 ? -descF / 1000.0 : 0.2) * fontSize;
            // Content box in block-local space: x∈[0,boxW]; y spans one line box above
            // the top baseline (≈1.13 em, the Position.YIndent plus the fragment
            // height) down to a font descent below the bottom baseline.
            var yTop = 1.13 * fontSize;
            var yBot = -bottomAnchor - descent;
            double minX = double.MaxValue;
            foreach (var (lx, ly) in new[] { (0.0, yBot), (boxW, yBot), (boxW, yTop), (0.0, yTop) })
            {
                var rx = lx * cos - ly * sin;
                if (rx < minX) minX = rx;
            }
            // X is anchored by the rotated box overhang; Y stays on the baseline
            // (TreatYIndentAsBaseLine), which needs no re-anchoring.
            builder.SetMatrix(cos * scaleX, sin * scaleX, -sin, cos, originX - minX, cmY);
        }
        else if (Math.Abs(rotateDeg) > 0.01)
        {
            builder.SetMatrix(cos * scaleX, sin * scaleX, -sin, cos, originX, cmY);
        }
        else
        {
            builder.SetMatrix(scaleX, 0, 0, 1, originX, cmY);
        }

        // Optional background box: when TextState.BackgroundColor is set, fill a
        // rectangle behind the text in the block-local (already rotated/placed)
        // space. The box spans the block width and one 1.1-em line box per row;
        // the text baseline is raised by the descent so the glyphs sit inside it.
        var bgYOffset = 0.0;
        if (hasBg)
        {
            const double descentFactor = 0.211; // baseline inset from the box bottom
            const double boxHeightFactor = 1.1; // one line box = 1.1 em
            bgYOffset = (rows.Count - 1) * lineHeight + descentFactor * fontSize;
            var boxHeight = (rows.Count - 1) * lineHeight + boxHeightFactor * fontSize;
            // Inner save so the rectangle's preceding operator is `q`, not the cm.
            builder.SaveState();
            builder.Rectangle(0, 0, blockWidth, boxHeight);
            builder.SetFillColor(bgColor!);
            builder.SetStrokeColor(bgColor!);
            builder.FillEvenOdd();
        }

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);

        // Apply the stamp's character/word spacing (Tc/Tw) so letter-spaced stamps
        // render spaced; guarded so default spacing keeps byte-identical output.
        if (TextState?.CharacterSpacing is { } cs and not 0f)
            builder.SetCharSpacing(cs);
        if (TextState?.WordSpacing is { } ws and not 0f)
            builder.SetWordSpacing(ws);

        for (var li = 0; li < rows.Count; li++)
        {
            // Align each row within the (un-scaled) block per TextAlignment; the cm
            // scale above stretches these local offsets to the scaled block width.
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            builder.SetTextMatrix(1, 0, 0, 1, localX, -li * lineHeight + bgYOffset + bottomAnchor)
                   .ShowTextBytes(rows[li]);
        }

        builder.EndText();
        if (hasBg) builder.RestoreState(); // close the inner save
        builder.RestoreState();

        return builder.Build();
    }

    // Scale=true layout: lay the text out at the base font in a natural-size block,
    // then emit one cm that non-uniformly scales that block to fill the Width×Height
    // box at (XIndent, YIndent). Wrapped text breaks to Width (so scaleX ≈ 1 and only
    // the height stretches); un-wrapped text is a single line (newlines → spaces) that
    // is squished horizontally to Width and stretched to Height.
    private byte[] BuildScaledToBox(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, bool wrapping)
    {
        var rows = wrapping
            ? WrapEncoded(encoded, baseFontName, fontSize, Width)
            : new List<byte[]> { JoinToOneLine(encoded) };
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        var lineHeight = fontSize;
        var blockHeight = Math.Max(1, rows.Count) * lineHeight;
        if (blockWidth <= 0) blockWidth = Width;

        var sX = Width / blockWidth;
        var sY = Height / blockHeight;
        // Baseline of the bottom row inside the natural block (leave the font's
        // descent below it); rows stack upward from there.
        var descent = fontSize * 0.2;

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.SetMatrix(sX, 0, 0, sY, XIndent, YIndent);

        // Background box: fills the whole natural block in block-local space, so the
        // cm scale above stretches it to exactly the Width×Height box.
        if (TextState?.BackgroundColor is { IsEmpty: false } bg)
        {
            builder.SaveState();
            builder.Rectangle(0, 0, blockWidth, blockHeight);
            builder.SetFillColor(bg);
            builder.SetStrokeColor(bg);
            builder.FillEvenOdd();
            builder.RestoreState();
        }

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            // Row 0 is the top line; the bottom line sits at `descent`.
            var localY = (rows.Count - 1 - li) * lineHeight + descent;
            builder.SetTextMatrix(1, 0, 0, 1, localX, localY).ShowTextBytes(rows[li]);
        }
        builder.EndText().RestoreState();
        return builder.Build();
    }

    // Rotated, box-fitted stamp: scale the natural text block to fill
    // the Width×Height box (sx=Width/blockWidth, sy=Height/blockHeight), place the box
    // per Horizontal/VerticalAlignment (or XIndent/YIndent when alignment is unset),
    // and rotate about the box centre. The emitted cm is
    //   [sx·cosθ, sx·sinθ, -sy·sinθ, sy·cosθ, tx, ty]
    // with (tx,ty) chosen so the (scaled) box centre maps to the target page centre.
    private byte[] BuildBoxRotated(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, bool wrapping, double rotateDeg)
    {
        var rows = wrapping
            ? WrapEncoded(encoded, baseFontName, fontSize, Width)
            : new List<byte[]> { JoinToOneLine(encoded) };
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        if (blockWidth <= 0) blockWidth = Width;
        var lineHeight = fontSize;
        var blockHeight = Math.Max(1, rows.Count) * lineHeight;

        var sX = Width / blockWidth;
        var sY = Height / blockHeight;
        double sw = Width, sh = Height; // scaled box dimensions

        // Box centre on the page: alignment when set, else XIndent/YIndent corner.
        double cx = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => page.Width / 2.0,
            HorizontalAlignment.Right => page.Width - sw / 2.0 - XIndent,
            _ => XIndent + sw / 2.0,
        };
        double cy = VerticalAlignment switch
        {
            VerticalAlignment.Center => page.Height / 2.0,
            VerticalAlignment.Top => page.Height - sh / 2.0 - YIndent,
            _ => YIndent + sh / 2.0,
        };

        var cos = Math.Cos(rotateDeg * Math.PI / 180);
        var sin = Math.Sin(rotateDeg * Math.PI / 180);
        // tx,ty: map the scaled box centre (sw/2, sh/2) through the rotation to (cx,cy).
        var tx = cx - (cos * (sw / 2.0) - sin * (sh / 2.0));
        var ty = cy - (sin * (sw / 2.0) + cos * (sh / 2.0));

        var builder = new ContentStreamBuilder();
        builder.SaveState();
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.SetMatrix(sX * cos, sX * sin, -sY * sin, sY * cos, tx, ty);
        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        var descent = fontSize * 0.2;
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = blockWidth - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            var localY = (rows.Count - 1 - li) * lineHeight + descent;
            builder.SetTextMatrix(1, 0, 0, 1, localX, localY).ShowTextBytes(rows[li]);
        }
        builder.EndText().RestoreState();
        return builder.Build();
    }

    // Word-wrapped stamp with a background box (Scale=false). Wrap to the inner width
    // (Width minus left/right margins), grow the box to the widest wrapped line, and emit:
    //   q  x y w h re  r g b rg  r g b RG  f*  BT ... ET  Q
    // so the rectangle is the first painted operator. The box
    // is placed per the stamp's Horizontal/Vertical alignment; the text fills it top-down.
    private byte[] BuildWrappedBackgroundBox(Page page, byte[] encoded, string baseFontName,
        string fontResName, double fontSize, Color color, Color bgColor)
    {
        var innerW = Width - LeftMargin - RightMargin;
        if (innerW <= 0) innerW = Width;

        // Break at spaces only: a word wider than the inner width is NOT hyphenated — it sits on
        // its own line and overflows, which (with Scale=false) is what grows the box width.
        var rows = WrapAtSpaces(encoded, baseFontName, fontSize, innerW);
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;
        // With Scale=false a word wider than the inner width grows the box rather than being
        // squeezed, so the box width is the widest wrapped line.
        var boxW = Math.Max(innerW, blockWidth);
        var lineHeight = fontSize;
        var descent = fontSize * 0.1;                 // baseline inset below the last line
        var boxH = rows.Count * lineHeight + descent;

        double boxX = HorizontalAlignment switch
        {
            HorizontalAlignment.Right => page.Width - RightMargin - boxW,
            HorizontalAlignment.Center => (page.Width - boxW) / 2.0,
            _ => LeftMargin,
        };
        double boxY = VerticalAlignment switch
        {
            VerticalAlignment.Bottom => BottomMargin,
            VerticalAlignment.Center => (page.Height - boxH) / 2.0,
            _ => page.Height - TopMargin - boxH,       // Top (default)
        };

        var builder = new ContentStreamBuilder();
        builder.SaveState();                            // q
        if (Opacity < 1.0)
        {
            var gsName = page.AddExtGState(new Content.ExtGState { FillAlpha = Opacity, StrokeAlpha = Opacity });
            builder.SetExtGState(gsName);
        }
        builder.Rectangle(boxX, boxY, boxW, boxH);      // re
        builder.SetFillColor(bgColor);                  // rg
        builder.SetStrokeColor(bgColor);                // RG
        builder.FillEvenOdd();                          // f*

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);
        var topBaseline = boxY + boxH - fontSize;       // first line near the box top
        for (var li = 0; li < rows.Count; li++)
        {
            var pad = boxW - rowWidths[li];
            var localX = TextAlignment switch
            {
                HorizontalAlignment.Right => pad,
                HorizontalAlignment.Center => pad / 2,
                _ => 0.0,
            };
            builder.SetTextMatrix(1, 0, 0, 1, boxX + localX, topBaseline - li * lineHeight)
                   .ShowTextBytes(rows[li]);
        }
        builder.EndText();
        builder.RestoreState();                         // Q
        return builder.Build();
    }

    // Greedy space-only word wrap: pack whole words onto a line until the next word would
    // exceed maxW; a single word wider than maxW gets its own (overflowing) line. Explicit
    // '\n' forces a break. Words are measured with the real face metrics (MeasureRow).
    // Largest font size (bisected to AutoFitPrecision) at which the word-wrapped
    // text still fits Width×Height. The block is N wrapped lines tall, each line
    // one em of leading with a 1.1-em box on the single line, so the block height
    // is (N + 0.1)·fontSize. Width binds only when a single word is itself wider
    // than the box (an unbreakable word ⇒ the search collapses to 0).
    private double ComputeAutoFitFontSize(string baseFont, byte[] encoded)
    {
        var prec = AutoFitPrecision > 0 ? AutoFitPrecision : 0.1;
        double lo = 0, hi = 2000;
        if (!AutoFitFits(hi, baseFont, encoded))
        {
            while (hi - lo > prec)
            {
                var mid = (lo + hi) / 2;
                if (AutoFitFits(mid, baseFont, encoded)) lo = mid; else hi = mid;
            }
            return lo;
        }
        return hi;
    }

    private bool AutoFitFits(double f, string baseFont, byte[] encoded)
    {
        if (f <= 0) return true;
        var rows = AutoFitWrap(encoded, baseFont, f, Width);
        double blockWidth = 0;
        foreach (var r in rows)
        {
            var w = MeasureRow(r, baseFont, f);
            if (w > blockWidth) blockWidth = w;
        }
        var blockHeight = (rows.Count + 0.1) * f;
        return blockWidth <= Width + 1e-6 && blockHeight <= Height + 1e-6;
    }

    // Greedy word wrap for the auto-fit measurement. Unlike the render-side
    // WrapAtSpaces, the fit test ignores each line's TRAILING space: a word joins
    // the current line when (line + inter-word space + word), with no trailing
    // space, still fits — the box-fit rule.
    // Rows are returned already trailing-trimmed so their measured width is exact.
    private static List<byte[]> AutoFitWrap(byte[] enc, string baseFont, double fontSize, double maxW)
    {
        var rows = new List<byte[]>();
        foreach (var lineSeg in SplitRows(enc))
        {
            var words = new List<byte[]>();
            var w = new List<byte>();
            foreach (var b in lineSeg)
            {
                w.Add(b);
                if (b == (byte)' ') { words.Add(w.ToArray()); w = new List<byte>(); }
            }
            if (w.Count > 0) words.Add(w.ToArray());

            var cur = new List<byte>();
            foreach (var word in words)
            {
                if (cur.Count == 0) { cur.AddRange(word); continue; }
                var trial = new List<byte>(cur); trial.AddRange(word);
                if (MeasureRow(TrimTrailingSpace(trial), baseFont, fontSize) > maxW)
                {
                    rows.Add(TrimTrailingSpace(cur));
                    cur = new List<byte>(word);
                }
                else cur = trial;
            }
            rows.Add(TrimTrailingSpace(cur));
        }
        return rows.Count == 0 ? new List<byte[]> { enc } : rows;
    }

    private static List<byte[]> WrapAtSpaces(byte[] enc, string baseFont, double fontSize, double maxW)
    {
        var rows = new List<byte[]>();
        foreach (var lineSeg in SplitRows(enc))
        {
            var words = new List<byte[]>();
            var w = new List<byte>();
            foreach (var b in lineSeg)
            {
                w.Add(b);
                if (b == (byte)' ') { words.Add(w.ToArray()); w = new List<byte>(); }
            }
            if (w.Count > 0) words.Add(w.ToArray());

            var cur = new List<byte>();
            foreach (var word in words)
            {
                if (cur.Count == 0) { cur.AddRange(word); continue; }
                var trial = new List<byte>(cur); trial.AddRange(word);
                if (MeasureRow(trial.ToArray(), baseFont, fontSize) > maxW)
                {
                    rows.Add(TrimTrailingSpace(cur));
                    cur = new List<byte>(word);
                }
                else cur = trial;
            }
            rows.Add(TrimTrailingSpace(cur));
        }
        return rows.Count == 0 ? new List<byte[]> { enc } : rows;
    }

    private static byte[] TrimTrailingSpace(List<byte> row)
    {
        var n = row.Count;
        while (n > 0 && row[n - 1] == (byte)' ') n--;
        return row.GetRange(0, n).ToArray();
    }

    // Flatten an encoded buffer to a single line: newlines become spaces.
    private static byte[] JoinToOneLine(byte[] enc)
    {
        var outp = new byte[enc.Length];
        for (var i = 0; i < enc.Length; i++)
            outp[i] = enc[i] == (byte)'\n' ? (byte)' ' : enc[i];
        return outp;
    }

    // Split an encoded (1-byte-per-char) buffer on '\n' into rows, dropping the
    // newline bytes. Always yields at least one row.
    private static List<byte[]> SplitRows(byte[] enc)
    {
        var rows = new List<byte[]>();
        var cur = new List<byte>();
        foreach (var b in enc)
        {
            if (b == (byte)'\n') { rows.Add(cur.ToArray()); cur.Clear(); }
            else cur.Add(b);
        }
        rows.Add(cur.ToArray());
        return rows;
    }

    // Width (points) of one encoded row at the given size, using Standard-14
    // metrics for base-14 fonts and a 0.5-em fallback otherwise (matches the
    // estimate used by the wrapper).
    // Cache of system TrueType faces (per family) used only to read unrounded
    // advance widths. The integer /W and AFM tables drop the sub-unit precision
    // that exact stamp geometry (a background box sized to the text) needs.
    private static readonly System.Collections.Generic.Dictionary<string, Aspose.Pdf.Text.TrueTypeParser?> _metricParsers = new();
    private static readonly object _metricParsersLock = new();

    private static Aspose.Pdf.Text.TrueTypeParser? ResolveMetricParser(string family)
    {
        lock (_metricParsersLock)
        {
            if (_metricParsers.TryGetValue(family, out var cached)) return cached;
            Aspose.Pdf.Text.TrueTypeParser? parser = null;
            try
            {
                var ttf = Aspose.Pdf.Text.SystemFontResolver.Resolve(family);
                if (ttf is { Length: > 0 })
                {
                    var p = new Aspose.Pdf.Text.TrueTypeParser(ttf);
                    p.Parse();
                    if (p.UnitsPerEm > 0 && p.GlyphWidths.Length > 0)
                        parser = p;
                }
            }
            catch { parser = null; }
            _metricParsers[family] = parser;
            return parser;
        }
    }

    private static double MeasureRow(byte[] row, string baseFont, double fontSize)
    {
        // Real font families (e.g. Arial — even though it aliases onto Helvetica's
        // AFM) are measured from the resolved face's unrounded hmtx advances when a
        // system face is available: the integer-1/1000 path rounds e.g. Arial 'T'
        // (610.84) to 611, losing the precision an exact text-width assertion needs.
        // Genuine Core-14 names (Helvetica/Times/Courier) keep their AFM table.
        var std14 = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(baseFont);
        if (!Aspose.Pdf.Text.Standard14Fonts.IsCoreName(baseFont))
        {
            var parser = ResolveMetricParser(baseFont);
            if (parser is not null)
            {
                var text = Aspose.Pdf.Text.Cp1252.GetString(row);
                double units = 0;
                foreach (var ch in text)
                {
                    if (parser.CMap.TryGetValue(ch, out var gid) && gid >= 0 && gid < parser.GlyphWidths.Length)
                        units += parser.GlyphWidths[gid];
                    else
                        units += parser.UnitsPerEm * 0.5;
                }
                return units * fontSize / parser.UnitsPerEm;
            }
        }
        double w = 0;
        foreach (var b in row)
            w += (std14 ? Aspose.Pdf.Text.Standard14Fonts.GetWidth(baseFont, b) : 500) / 1000.0 * fontSize;
        return w;
    }

    // Break the (1-byte-per-char) encoded stamp text into rows no wider than
    // <paramref name="maxW"/> points. Prefer breaking at spaces; a word longer
    // than the row is split with a trailing hyphen (discretionary hyphenation).
    // Explicit '\n' always starts a new row.
    private static List<byte[]> WrapEncoded(byte[] enc, string baseFont, double fontSize, double maxW)
    {
        var std14 = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(baseFont);
        double W(int b) => (std14 ? Aspose.Pdf.Text.Standard14Fonts.GetWidth(baseFont, b) : 500) / 1000.0 * fontSize;

        var rows = new List<byte[]>();
        var cur = new List<byte>();
        double curW = 0;
        var lastSpace = -1; // index in cur of the most recent space

        foreach (var b in enc)
        {
            if (b == (byte)'\n')
            {
                rows.Add(cur.ToArray());
                cur.Clear(); curW = 0; lastSpace = -1;
                continue;
            }
            var bw = W(b);
            if (cur.Count > 0 && curW + bw > maxW)
            {
                if (lastSpace >= 0)
                {
                    rows.Add(cur.GetRange(0, lastSpace).ToArray());
                    cur = cur.GetRange(lastSpace + 1, cur.Count - lastSpace - 1);
                    curW = 0; foreach (var rb in cur) curW += W(rb);
                    lastSpace = -1;
                }
                else
                {
                    // Long unbreakable word: end the row with a hyphen and carry the
                    // current char to a fresh row. curW is already <= maxW, so the
                    // hyphen adds at most one glyph's width.
                    var hy = new List<byte>(cur) { (byte)'-' };
                    rows.Add(hy.ToArray());
                    cur.Clear(); curW = 0; lastSpace = -1;
                }
            }
            cur.Add(b); curW += bw;
            if (b == (byte)' ') lastSpace = cur.Count - 1;
        }
        if (cur.Count > 0) rows.Add(cur.ToArray());
        return rows.Count == 0 ? new List<byte[]> { enc } : rows;
    }

    // Single-byte encoder targeting WinAnsiEncoding. Chars that Windows-1252
    // already maps go through as-is; chars that don't (Polish/Czech/etc.) get
    // assigned a custom byte code in the 0x80-0x9F (and as needed 0x7F/0xA0)
    // range and an AGL glyph name returned via `diffMap` so the caller can
    // emit /Encoding /Differences. Truly unrepresentable chars fall back to '?'.
    private static byte[] EncodeForWinAnsi(string text, out List<(byte code, string glyph)> diffMap)
    {
        diffMap = new List<(byte, string)>();
        if (string.IsNullOrEmpty(text)) return Array.Empty<byte>();
        // Managed Windows-1252 (Cp1252): chars not in WinAnsi report as
        // unmappable so we route them through the AGL /Differences path instead
        // of silently transliterating them — the renderer never draws the wrong
        // letter, and we always know when a glyph is missing.
        var bytes = new byte[text.Length];
        // Pick byte codes in WinAnsi's "control" / unused range first so we
        // don't clobber existing glyphs (euro, smartquote, …) that the same
        // stamp text might also include. Start at 0x81 (unused), then 0x8D,
        // 0x8F, 0x90, 0x9D (also unused), then fill 0x80-0x9F by index.
        // We do not reach the 256-byte limit in practice for stamp strings.
        var unusedSlots = new byte[] { 0x81, 0x8D, 0x8F, 0x90, 0x9D, 0x80, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8E, 0x91, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9A, 0x9B, 0x9C, 0x9E, 0x9F };
        var nextSlot = 0;
        var assigned = new Dictionary<char, byte>();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            // ASCII + WinAnsi-supplement: encode straight.
            // Windows-1252 returns 0x3F ('?') for unrepresentable chars by
            // default, so we have to probe each char individually rather than
            // GetBytes the whole string in one shot.
            if (ch < 0x80)
            {
                bytes[i] = (byte)ch;
                continue;
            }
            if (Aspose.Pdf.Text.Cp1252.TryGetByte(ch, out var wb))
            {
                bytes[i] = wb;
                continue;
            }
            // Not in WinAnsi — map via AGL glyph name through /Differences.
            if (assigned.TryGetValue(ch, out var code))
            {
                bytes[i] = code;
                continue;
            }
            var glyph = AglGlyphName(ch);
            if (glyph is null || nextSlot >= unusedSlots.Length)
            {
                bytes[i] = (byte)'?';
                continue;
            }
            code = unusedSlots[nextSlot++];
            assigned[ch] = code;
            diffMap.Add((code, glyph));
            bytes[i] = code;
        }
        return bytes;
    }

    // Inverse of AglGlyphName for the chars we actually map via /Differences,
    // used to build the /ToUnicode CMap so the content-stream decoder turns
    // our custom byte codes back into real Unicode chars. Has to stay in
    // sync with AglGlyphName — the lookup is keyed on the glyph name we
    // emitted so a future glyph addition only needs both directions covered.
    private static int? AglUnicodeForGlyph(string glyph) => glyph switch
    {
        "Aogonek" => 0x0104, "aogonek" => 0x0105,
        "Cacute" => 0x0106, "cacute" => 0x0107,
        "Eogonek" => 0x0118, "eogonek" => 0x0119,
        "Lslash" => 0x0141, "lslash" => 0x0142,
        "Nacute" => 0x0143, "nacute" => 0x0144,
        "Sacute" => 0x015A, "sacute" => 0x015B,
        "Zacute" => 0x0179, "zacute" => 0x017A,
        "Zdotaccent" => 0x017B, "zdotaccent" => 0x017C,
        "Ccaron" => 0x010C, "ccaron" => 0x010D,
        "Dcaron" => 0x010E, "dcaron" => 0x010F,
        "Ecaron" => 0x011A, "ecaron" => 0x011B,
        "Ncaron" => 0x0147, "ncaron" => 0x0148,
        "Rcaron" => 0x0158, "rcaron" => 0x0159,
        "Tcaron" => 0x0164, "tcaron" => 0x0165,
        "Abreve" => 0x0102, "abreve" => 0x0103,
        "Hungarumlaut" => 0x0150, "ohungarumlaut" => 0x0151,
        "Uhungarumlaut" => 0x0170, "uhungarumlaut" => 0x0171,
        "Gbreve" => 0x011E, "gbreve" => 0x011F,
        "Idotaccent" => 0x0130, "dotlessi" => 0x0131,
        "Scedilla" => 0x015E, "scedilla" => 0x015F,
        "Amacron" => 0x0100, "amacron" => 0x0101,
        "Emacron" => 0x0112, "emacron" => 0x0113,
        "Imacron" => 0x012A, "imacron" => 0x012B,
        "Omacron" => 0x014C, "omacron" => 0x014D,
        "Umacron" => 0x016A, "umacron" => 0x016B,
        _ => null,
    };

    // Build a minimal /ToUnicode CMap stream containing one bfchar entry
    // per /Differences slot we emitted. The format follows PDF 32000 §9.10.3
    // (Identity-CIDSystemInfo, single-byte codespace). The page-level content
    // parser parses bfchar/bfrange entries to populate its toUnicode map,
    // which DrawText.Latin1 fallback otherwise can't do.
    private static PdfStream BuildToUnicodeCMap(List<(byte code, string glyph)> diffMap)
    {
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /Adobe-Identity-UCS def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
        sb.Append(diffMap.Count).Append(" beginbfchar\n");
        foreach (var (code, glyph) in diffMap)
        {
            var u = AglUnicodeForGlyph(glyph) ?? '?';
            sb.Append('<').Append(code.ToString("X2")).Append("> <")
              .Append(u.ToString("X4")).Append(">\n");
        }
        sb.Append("endbfchar\nendcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\nend\nend\n");
        return new PdfStream(new PdfDictionary(), Encoding.ASCII.GetBytes(sb.ToString()));
    }

    // Subset of the Adobe Glyph List covering Latin-Extended-A/B chars
    // that Standard-14 PostScript fonts ship with but WinAnsiEncoding
    // doesn't expose by default. Returning null lets the caller fall
    // back to '?' for chars no Standard-14 font can render anyway.
    private static string? AglGlyphName(char ch) => ch switch
    {
        // Polish: ę,ą,ś,ł,ń,ź,ż,ć
        'Ą' => "Aogonek",   'ą' => "aogonek",
        'Ć' => "Cacute",    'ć' => "cacute",
        'Ę' => "Eogonek",   'ę' => "eogonek",
        'Ł' => "Lslash",    'ł' => "lslash",
        'Ń' => "Nacute",    'ń' => "nacute",
        'Ś' => "Sacute",    'ś' => "sacute",
        'Ź' => "Zacute",    'ź' => "zacute",
        'Ż' => "Zdotaccent",'ż' => "zdotaccent",
        // Czech / Slovak (caron forms not in WinAnsi)
        'Č' => "Ccaron",    'č' => "ccaron",
        'Ď' => "Dcaron",    'ď' => "dcaron",
        'Ě' => "Ecaron",    'ě' => "ecaron",
        'Ň' => "Ncaron",    'ň' => "ncaron",
        'Ř' => "Rcaron",    'ř' => "rcaron",
        'Ť' => "Tcaron",    'ť' => "tcaron",
        // S-caron and Z-caron ARE in WinAnsi (0x8A/0x9A, 0x8E/0x9E) — handled by the encoder.
        // Romanian / Turkish breves
        'Ă' => "Abreve",    'ă' => "abreve",
        'Ő' => "Hungarumlaut",'ő' => "ohungarumlaut",
        'Ű' => "Uhungarumlaut",'ű' => "uhungarumlaut",
        'Ğ' => "Gbreve",    'ğ' => "gbreve",
        'İ' => "Idotaccent",'ı' => "dotlessi",
        'Ş' => "Scedilla",  'ş' => "scedilla",
        // Macrons (Baltic)
        'Ā' => "Amacron",   'ā' => "amacron",
        'Ē' => "Emacron",   'ē' => "emacron",
        'Ī' => "Imacron",   'ī' => "imacron",
        'Ō' => "Omacron",   'ō' => "omacron",
        'Ū' => "Umacron",   'ū' => "umacron",
        _ => null,
    };

    // Apply Bold/Italic flags from TextState (or IsBold/IsItalic) onto the
    // base font name, mapping Helvetica/Courier/Times to their Standard-14
    // variants. Falls back to the FontName property when TextState is unset
    // or holds the default Helvetica.
    private string ResolveBaseFontName()
    {
        var fn = TextState?.FontName ?? FontName ?? "Helvetica";
        var bold = TextState?.IsBold ?? false;
        var italic = TextState?.IsItalic ?? false;

        // Strip any already-baked style suffix so callers can pass
        // "Helvetica-Bold" via FontName and still get Bold|Italic
        // honoured from FontStyle without doubling up.
        var family = fn;
        foreach (var suffix in new[] { "-BoldOblique", "-BoldItalic", "-Bold", "-Oblique", "-Italic" })
        {
            if (family.EndsWith(suffix, StringComparison.Ordinal))
            {
                family = family.Substring(0, family.Length - suffix.Length);
                break;
            }
        }

        return family switch
        {
            "Helvetica" => (bold, italic) switch
            {
                (true, true) => "Helvetica-BoldOblique",
                (true, false) => "Helvetica-Bold",
                (false, true) => "Helvetica-Oblique",
                _ => "Helvetica",
            },
            "Times-Roman" or "Times" => (bold, italic) switch
            {
                (true, true) => "Times-BoldItalic",
                (true, false) => "Times-Bold",
                (false, true) => "Times-Italic",
                _ => "Times-Roman",
            },
            "Courier" => (bold, italic) switch
            {
                (true, true) => "Courier-BoldOblique",
                (true, false) => "Courier-Bold",
                (false, true) => "Courier-Oblique",
                _ => "Courier",
            },
            // Non-standard-14 families (e.g. Arial): qualify the BaseFont with a
            // comma-separated style suffix so the requested style is reflected in
            // the font's reported name. Font.FontName strips the comma
            // ("Arial,Bold" → "ArialBold"), matching the style-qualified name a
            // styled text stamp is expected to carry.
            _ => (bold, italic) switch
            {
                (true, true) => family + ",BoldItalic",
                (true, false) => family + ",Bold",
                (false, true) => family + ",Italic",
                _ => fn,
            },
        };
    }

    // Register a Standard-14 font in the page /Resources /Font dictionary
    // and return its resource name. When `diffMap` is non-empty, the entry
    // gets an /Encoding dict with /BaseEncoding /WinAnsiEncoding plus a
    // /Differences array mapping the custom byte codes to AGL glyph names;
    // a separate /F* slot is allocated per distinct (BaseFont, diffMap) pair
    // so two stamps using the same Helvetica with different Polish chars
    // each get their own encoding table.
    private static string EnsureFontResource(Page page, string baseFontName,
        List<(byte code, string glyph)>? diffMap = null)
    {
        // /Resources and /Font are frequently indirect references; resolve them
        // (rather than a bare `as PdfDictionary` cast that yields null) so we
        // don't replace the real dictionary — and drop the page's existing
        // fonts — with a fresh empty one.
        var resources = page.Dict.Get("Resources") as PdfDictionary
            ?? page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }

        var fontDict = resources.Get("Font") as PdfDictionary
            ?? page.Reader.ResolveDict(resources.Get("Font"));
        if (fontDict is null)
        {
            fontDict = new PdfDictionary();
            resources.Set("Font", fontDict);
        }

        var hasDiffs = diffMap is { Count: > 0 };

        foreach (var key in fontDict.Keys)
        {
            var entry = fontDict.Get(key) as PdfDictionary
                ?? page.Reader.ResolveDict(fontDict.Get(key));
            if (entry is null) continue;
            var existing = entry.GetName("BaseFont");
            if (!string.Equals(existing, baseFontName, StringComparison.Ordinal)) continue;

            // Reuse only when the encoding mode matches. A vanilla WinAnsi
            // entry can't be shared by a stamp that also needs /Differences,
            // and vice versa; we don't try to grow an existing /Differences
            // array (the test corpus never needs it).
            var enc = entry.Get("Encoding");
            var entryHasDiffs = enc is PdfDictionary;
            if (entryHasDiffs == hasDiffs && !hasDiffs)
                return key;
        }

        var name = "F1";
        var counter = 1;
        while (fontDict.ContainsKey(name))
            name = $"F{++counter}";

        var font = new PdfDictionary();
        font.Set("Type", new PdfName("Font"));
        font.Set("Subtype", new PdfName("Type1"));
        font.Set("BaseFont", new PdfName(baseFontName));
        if (hasDiffs)
        {
            var encoding = new PdfDictionary();
            encoding.Set("Type", new PdfName("Encoding"));
            encoding.Set("BaseEncoding", new PdfName("WinAnsiEncoding"));
            var diffs = new PdfArray();
            // /Differences format: [ <code> /glyph1 /glyph2 ... <code2> /glyph ... ]
            // Each integer resets the "next code" pointer; following names map
            // to consecutive code points. We emit one integer per glyph for
            // simplicity (codes aren't necessarily consecutive in our
            // unused-slot allocation order).
            foreach (var (code, glyph) in diffMap!)
            {
                diffs.Add(new PdfInteger(code));
                diffs.Add(new PdfName(glyph));
            }
            encoding.Set("Differences", diffs);
            font.Set("Encoding", encoding);
            // Emit a matching /ToUnicode CMap so the content-stream decoder
            // (which only honours /ToUnicode, not /Differences→AGL) can map
            // our custom byte codes back to real Unicode for the renderer's
            // parser.CMap[unicode]→GID lookup. Without this the Polish glyph
            // would route to char 0x81/0x8D/… and find no Helvetica.ttf cmap
            // entry, drawing nothing.
            font.Set("ToUnicode", BuildToUnicodeCMap(diffMap));
        }
        else
        {
            font.Set("Encoding", new PdfName("WinAnsiEncoding"));
        }
        fontDict.Set(name, font);

        return name;
    }

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
