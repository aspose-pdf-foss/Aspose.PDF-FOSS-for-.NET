using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileMend
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class MendTextState
{
    public System.Text.StringBuilder sb = null!;
    // Register font in page resources. CustomFontFile takes precedence so a
    // .ttf path supplied via FormattedText is actually embedded; otherwise
    // EnsureFont falls back to a Type1 base font by name.
    public string fontName = null!;
    // A face chosen by the FontStyle enum (a Standard-14 base font) lays out on the
    // probed Standard-14 geometry below; a string-named system font or a custom font
    // file keeps the legacy placement (baseline lly - size + 13, x inset 0.2 * size).
    public bool namedFace;
    // Text carrying characters WinAnsi cannot address (Hebrew, CJK, …) cannot be
    // written through a simple WinAnsi font — Cp1252 turns every such glyph into
    // '?'. The whole fragment is promoted to an embedded Identity-H
    // subset of the matching system face instead (Times-Roman text reads back
    // under "XXXXXX+TimesNewRoman"), so extraction round-trips the original text.
    public bool needsUnicode;
    public byte[]? unicodeTtf;
    public string unicodeFace = null!;
    public PdfDictionary? unicodeFontDict;
    // Probed AddText geometry (Standard-14 faces, 10-30 pt, both the
    // rect and the point overloads; the facade rect is in the AS-DISPLAYED frame):
    //   bar bottom = lly - size + 11
    //   bar height = (Ascender - Descender)/1000 * size + 4   (the face's AFM metrics)
    //   baseline   = bar bottom + 2 + |Descender|/1000 * size
    //   text x     = llx + 2;  bar x = llx, bar width = urx - llx (page width when no rect)
    //   line pitch = size + 2 (+ the previous line's extra spacing)
    // On a /Rotate page the whole block is drawn under the display->media rotation
    // (the matrix Page.AddImage uses), and the bar additionally extends DOWN by the
    // descent, so its top and the baseline stay where the unrotated rule puts them.
    public double size;
    public double defaultLeading;
    public string metricsFont = null!;
    public bool std14;
    public double ascender;
    public double descender;
    public Rectangle pageMediaBox = null!;
    public int rot;
    public bool swapAxes;
    public double mediaW;
    public double mediaH;
    public double displayW;
    public double displayH;
    public string rotCm = null!;
    public double barBottom;
    public double barH;
    public double startY;
    public double textX;
    public double bgX;
    public double bgW;
    // Set foreground color
    public Color fg = null!;
    public Page page = default!;
    public FormattedText ft = default!;
    public float llx = 0;
    public float lly = 0;
    public float urx = 0;
    public float ury = 0;
}
}
