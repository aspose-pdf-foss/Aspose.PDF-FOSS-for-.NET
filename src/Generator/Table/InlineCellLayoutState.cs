using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public partial class Table
{
/// <summary>Per-call working state of the enclosing method. One instance per
/// invocation; never shared.</summary>
private sealed class InlineCellLayoutState
{
    public List<List<Aspose.Pdf.Table.InlineItem>> rows = null!;
    public List<Aspose.Pdf.Table.InlineItem> current = null!;
    public double x;
    // Generator cells pitch inline rows at the run size (K = 1.0, like every
    // other cell line); the other dialects keep the 1.2 em row.
    public bool generatorPitch;
    public double maxH;
    public double contentW;
    public Dictionary<string, (byte[]? ttf, string? name)> faceCache = null!;
    public bool lineHasText;
    public int rowItemSources;
    public int rowRightCount;
    public double marginL;
    // A multi-segment fragment lays its segments out as consecutive inline runs on
    // the SAME line, each keeping its own size / colour / baseline (sub-superscript),
    // instead of being flattened to one merged run. Every segment is emitted —
    // including the parameterless TextFragment() ctor's default empty leading segment,
    // which the generator renders as an empty run (a leading empty fragment).
    public List<Aspose.Pdf.Text.TextSegment> segs = null!;
    public int textCount;
    public Aspose.Pdf.Text.TextState ss = null!;
    public double baseFs;
    // The fragment's size is the LINE's: every segment seats against it
    // (a smaller run bottom-aligns on its own descent). A generator
    // fragment that never named a size seats its segments on their
    // own (8 pt Arial segments under an unsized fragment sit on an
    // 8 pt box, not the cell default's).
    public double lineFs;
    public Color? segColor;
    // A segment carries its OWN weight, slant and underline: a
    // multi-segment cell is exactly how a caller mixes them within
    // one line, and flattening the run to the fragment's style drops
    // every emphasis the markup asked for.
    public bool segBold;
    public bool segItalic;
    public bool segUnderline;
    // Newline characters break the inline row: each empty piece is
    // an empty run at the pen position (before the break, and again
    // at the new line's start — both are emitted).
    public string[] segPieces = null!;
    public string segPiece = null!;
    // Per-segment embedded font (e.g. NotoSans / NotoSansArabic supplied on the
    // segment's TextState): the run is drawn with that font embedded as Type0, so
    // it is measured with the font's real glyph advances. Arabic is shaped
    // (contextual presentation forms + bidi visual order).
    public byte[]? segTtf;
    public string? segFontName;
    public bool isArabic;
    public double itemH;
    public bool sup;
    public bool sub;
}
}
