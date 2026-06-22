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

    /// <summary>Height constraint for the stamp box. Stored only — the
    /// renderer auto-sizes around the text.</summary>
    public double Height { get; set; }

    /// <summary>When true, the stamp text is scaled to fit
    /// <see cref="Width"/> × <see cref="Height"/>. Stored only; matches
    /// the Aspose.PDF for .NET public API.</summary>
    public bool Scale { get; set; }

    /// <summary>Zoom factor applied to the stamp. Stored only; matches
    /// the Aspose.PDF for .NET public API.</summary>
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
            FontSize = (float)formattedText.FontSize;
    }

    internal override byte[] BuildContentStream(Page page)
    {
        // Pull effective font/size/colour from TextState first (mirrors the
        // Aspose.PDF for .NET API where setting TextState.* on a stamp wins over the
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
        var fontResName = EnsureFontResource(page, baseFontName, diffMap);

        // Wrapping is enabled by the WordWrap bool OR a non-NoWrap WordWrapMode
        // (Aspose.PDF for .NET exposes both; this sets only the bool).
        var wrapping = WordWrap || WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap;

        // Scale-to-fit: a stamp with Scale=true and an explicit Width×Height box
        // lays its text out at the base font, then non-uniformly scales that block
        // to exactly fill the box, anchored at (XIndent, YIndent) — matching
        // Aspose.PDF for .NET, which emits `sx 0 0 sy XIndent YIndent cm` over a natural-size
        // form. Wrapped text fills width at scale ~1 and stretches
        // vertically; un-wrapped text is laid as a single line and squished to width.
        if (Scale && Width > 0 && Height > 0)
            return BuildScaledToBox(page, encoded, baseFontName, fontResName, fontSize, color, wrapping);

        // Rotated stamp with an explicit Width×Height box: Aspose.PDF for .NET scales the text
        // non-uniformly to fill the box (sx=Width/textW, sy=Height/textH), centres the
        // box per Horizontal/VerticalAlignment, then rotates it about the box centre.
        // The plain path below only applies the horizontal scale and
        // rotates about the block anchor, which mis-sizes/positions the result.
        double rot = RotateAngle != 0 ? RotateAngle : (double)Rotate;
        if (Math.Abs(rot) > 0.01 && Width > 0 && Height > 0)
            return BuildBoxRotated(page, encoded, baseFontName, fontResName, fontSize, color, wrapping, rot);

        // Break the text into display rows: wrap to the stamp width when wrapping
        // is on, otherwise split on the explicit '\n' line breaks that a
        // FormattedText (AddNewLineText) or a multi-line Value carries. The old
        // no-wrap path emitted the raw '\n' byte inside a single Tj string, which
        // is not a line break in PDF — every line collapsed onto one row.
        var wrapWidth = WrapWidth;
        var rows = (wrapWidth > 0 && WordWrapMode != TextFormattingOptions.WordWrapMode.NoWrap)
            ? WrapEncoded(encoded, baseFontName, fontSize, wrapWidth)
            : SplitRows(encoded);

        // Natural (un-scaled) row widths and the block width (the widest row).
        var rowWidths = rows.Select(r => MeasureRow(r, baseFontName, fontSize)).ToList();
        var blockWidth = rowWidths.Count > 0 ? rowWidths.Max() : 0.0;

        // A stamp with an explicit Width stretches/condenses its text horizontally
        // to fill that width (matches Aspose.PDF for .NET, which scales the whole stamp form
        // by Width / naturalWidth). No Width ⇒ draw at natural size.
        var scaleX = (Width > 0 && blockWidth > 0) ? Width / blockWidth : 1.0;
        var scaledBlockWidth = blockWidth * scaleX;

        // Leading of one em (Aspose.PDF for .NET spaces stamp lines by exactly the font size).
        var lineHeight = fontSize;

        // Position the block on the page. The block's left/top is derived from the
        // SCALED width so Right/Center alignment lands the right/centre at the page
        // edge/centre, and the first baseline sits one line below the top edge.
        var (originX, topBaseline) = ComputeBlockOrigin(page, scaledBlockWidth, fontSize, lineHeight, rows.Count);

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
        if (Math.Abs(rotateDeg) > 0.01)
            builder.SetMatrix(cos * scaleX, sin * scaleX, -sin, cos, originX, topBaseline);
        else
            builder.SetMatrix(scaleX, 0, 0, 1, originX, topBaseline);

        builder.BeginText()
            .SetFillColor(color.R / 255.0, color.G / 255.0, color.B / 255.0)
            .SetFont(fontResName, fontSize);

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
            builder.SetTextMatrix(1, 0, 0, 1, localX, -li * lineHeight)
                   .ShowTextBytes(rows[li]);
        }

        builder.EndText().RestoreState();

        return builder.Build();
    }

    // Scale=true layout: lay the text out at the base font in a natural-size block,
    // then emit one cm that non-uniformly scales that block to fill the Width×Height
    // box at (XIndent, YIndent). Wrapped text breaks to Width (so scaleX ≈ 1 and only
    // the height stretches); un-wrapped text is a single line (newlines → spaces) that
    // is squished horizontally to Width and stretched to Height. Mirrors Aspose.PDF for .NET.
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
    private static double MeasureRow(byte[] row, string baseFont, double fontSize)
    {
        var std14 = Aspose.Pdf.Text.Standard14Fonts.IsStandard14(baseFont);
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
            _ => fn,
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
        Page page, double scaledBlockWidth, double fontSize, double lineHeight, int rowCount)
    {
        var pageWidth = page.Width;
        var pageHeight = page.Height;
        var blockHeight = Math.Max(1, rowCount) * lineHeight;

        var originX = HorizontalAlignment switch
        {
            HorizontalAlignment.Center => (pageWidth - scaledBlockWidth) / 2,
            HorizontalAlignment.Right => pageWidth - scaledBlockWidth - XIndent,
            _ => XIndent,
        };

        // topBaseline is the first line's baseline. Top: one line below the top
        // edge; Bottom: leave room for the remaining lines above the bottom edge;
        // Center: centre the whole block vertically.
        var topBaseline = VerticalAlignment switch
        {
            VerticalAlignment.Top => pageHeight - TopMargin - YIndent - fontSize,
            VerticalAlignment.Center => (pageHeight + blockHeight) / 2 - fontSize,
            _ => YIndent + BottomMargin + blockHeight - fontSize,
        };

        return (originX, topBaseline);
    }
}
