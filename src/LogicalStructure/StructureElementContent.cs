using System.Text.RegularExpressions;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Tagged;

namespace Aspose.Pdf.LogicalStructure;

public partial class StructureElement
{
    // Two shown strings on one baseline further apart than this share of the font size are
    // separate words (a space is narrower than a quarter em in few faces; kerning is far less).
    private const double WordGapEms = 0.15;
    // Baselines closer than this share of the font size are one line.
    private const double SameLineEms = 0.5;
    // A gap on one line wider than this share of the font size sets text apart (a tab stop,
    // a page number across from a running title): the text after it starts a run of its own.
    private const double TabGapEms = 2.0;
    // Text an element's later lines start under, standing on its first line more than this share of the size after the
    // text before it, hangs past a label (a reference's text beside a long key): it starts a run of its own as well.
    private const double HangGapEms = 1.0;
    // Lines starting within this (points) of one another start together.
    private const double LineStartSlack = 1.0;
    // A piece that goes on along the line but stands this share of a size or more off its
    // baseline - and less than a line's pitch - is raised or lowered: a mark set in full size
    // over a heading, an index. It is a run of its own, so a reader can place it.
    private const double ShiftedEms = 0.15;
    private const double LinePitchEms = 0.8;

    private static bool Shifted(MarkedContentScan.Piece? previous, MarkedContentScan.Piece next)
    {
        if (previous is null) return false;
        var size = Math.Max(Math.Max(previous.Size, next.Size), 1);
        var dy = Math.Abs(next.Y - previous.Y);
        var alongTheLine = next.X0 >= Math.Max(previous.X0, previous.X1) - 1;
        return alongTheLine && dy >= ShiftedEms * size && dy < LinePitchEms * size;
    }
    // A run's box below and above its baseline, as shares of its size.
    private const double DescentEms = 0.2;
    private const double AscentEms = 0.8;
    // A FontWeight at or above this is bold (600 = semibold).
    private const int BoldWeight = 600;
    private const int ItalicFlag = 1 << 6;
    private const int ForceBoldFlag = 1 << 18;
    // Tr 2 and 6 fill and stroke the glyphs: how bold is faked without a bold face.
    private const int FillStrokeMode = 2;
    private const int FillStrokeClipMode = 6;

    private static readonly string[] BoldNameParts = ["Bold", "Black", "Heavy", "Semibold", "Demi"];
    // A face naming its weight in two letters after the family (HelveticaNeueLTPro-Bd, -BdIt, -BdCn).
    private static readonly Regex BoldSuffix = new(@"[-,]Bd(?=$|[A-Z])", RegexOptions.CultureInvariant);
    private static readonly string[] ItalicNameParts = ["Ital", "Obli", "Slant"];
    // The TeX Computer Modern faces name their weight and slope in two or three letters
    // (CMBX10 bold extended, CMTI10 text italic, CMMI10 math italic, CMBXTI10 bold italic);
    // the URW Nimbus faces call their bold "Medi" (NimbusRomNo9L-Medi).
    private static readonly Regex TexBold = new(@"^CM(BX|B|SSBX|MIB|BSY)(TI|SL)?\d", RegexOptions.CultureInvariant);
    private static readonly Regex TexItalic = new(@"^CM(TI|MI|MIB|SL|SSI|ITT|FI|FIB|BXTI|BXSL|SLTT)\d", RegexOptions.CultureInvariant);
    private const string UrwPrefix = "Nimbus";
    private const string UrwBoldPart = "Medi";
    // Word halves a line break hyphenates that keep the hyphen when joined: a compound's
    // first half rather than a syllable (self-attention, not no-toriously).
    private static readonly HashSet<string> CompoundHeads = new(StringComparer.OrdinalIgnoreCase)
    {
        "self", "multi", "well", "cross", "semi", "anti", "non", "co", "quasi", "pseudo", "meta",
        "high", "low", "long", "short", "half", "real", "fine", "one", "two", "three", "first",
        "second", "top", "end", "open", "e",
    };

    /// <summary>
    /// The content this element marks, in reading order: its marked-content sequences in the
    /// order of its /K, and (when <paramref name="includeDescendants"/>) the content of its
    /// child elements where they stand among them. Text is returned as runs of one style with
    /// the spaces between words and lines restored; images as their XObjects.
    ///
    /// ⚠ FOSS-only: the reference has no read from a structure element to its content.
    /// </summary>
    /// <param name="includeDescendants">Whether the content of child elements is included
    /// (each item names the element that holds it).</param>
    public IList<MarkedContentItem> GetMarkedContent(bool includeDescendants = true)
    {
        var result = new List<MarkedContentItem>();
        var doc = FindSourceDocument();
        var reader = doc?.Reader ?? _reader;
        if (reader is null) return result;

        var context = new ContentReadContext(reader, doc);
        CollectPieces(context, context.InheritedPageDict(this), includeDescendants);
        BuildItems(reader, context.Pieces, result);
        return result;
    }

    /// <summary>What one read of marked content shares: the reader, the pages by their
    /// dictionaries, the /Pg entries a tagging has decided but not yet written, and the pieces
    /// collected in reading order.</summary>
    private sealed class ContentReadContext
    {
        private readonly Document? _doc;
        private readonly Dictionary<PdfDictionary, Page?> _pages = new();
        private readonly Dictionary<PdfDictionary, Page> _pending = new();
        private readonly Dictionary<PdfDictionary, Page> _pendingPages = new();

        public ContentReadContext(PdfReader reader, Document? doc)
        {
            Reader = reader;
            _doc = doc;
            // A tagging names the page of each element and reference when the document is
            // saved; until then the page waits in the document's list of fixups.
            if (doc is not null)
                (_pending, _pendingPages) = doc.PendingStructPgMap();
        }

        public PdfReader Reader { get; }
        public List<(StructureElement? Owner, Page? Page, MarkedContentScan.Piece Piece)> Pieces { get; } = new();

        /// <summary>The page dictionary <paramref name="dict"/> names by /Pg, written or pending.</summary>
        public PdfDictionary? PageDictOf(PdfDictionary dict)
            => Reader.Resolve(dict.Get("Pg")) as PdfDictionary
               ?? (_pending.TryGetValue(dict, out var page) ? page.Dict : null);

        /// <summary>The /Pg the element's content is on: its own, or the nearest ancestor's.</summary>
        public PdfDictionary? InheritedPageDict(StructureElement element)
        {
            for (var el = element; el is not null; el = el._parent)
                if (PageDictOf(el._dict) is { } pg) return pg;
            return null;
        }

        public Page? PageOf(PdfDictionary pageDict)
        {
            if (_pages.TryGetValue(pageDict, out var known)) return known;
            if (_pendingPages.TryGetValue(pageDict, out var direct))
                return _pages[pageDict] = direct;
            return _pages[pageDict] = _doc is null ? null : MapPage(_doc, pageDict);
        }
    }

    private void CollectPieces(ContentReadContext context, PdfDictionary? pageDict, bool includeDescendants)
    {
        var reader = context.Reader;
        if (context.PageDictOf(_dict) is { } own) pageDict = own;
        var k = reader.Resolve(_dict.Get("K"));
        var kids = new List<PdfObject?>();
        if (k is PdfArray arr) kids.AddRange(arr);
        else if (k is not null) kids.Add(k);
        foreach (var kidObj in kids)
        {
            switch (reader.Resolve(kidObj))
            {
                case PdfInteger mcid when pageDict is not null:
                    AddPieces(MarkedContentScan.ForPage(reader, pageDict), (int)mcid.Value, context.PageOf(pageDict));
                    break;
                case PdfDictionary kd when kd.GetName("Type") == "MCR":
                {
                    if (reader.Resolve(kd.Get("MCID")) is not PdfInteger m) break;
                    var pg = context.PageDictOf(kd) ?? pageDict;
                    var page = pg is not null ? context.PageOf(pg) : null;
                    if (reader.Resolve(kd.Get("Stm")) is PdfStream stm)
                    {
                        var placement = pg is not null ? MarkedContentScan.FormPlacement(reader, pg, stm) : null;
                        AddPieces(MarkedContentScan.ForStream(reader, stm, placement), (int)m.Value, page);
                    }
                    else if (pg is not null)
                        AddPieces(MarkedContentScan.ForPage(reader, pg), (int)m.Value, page);
                    break;
                }
                case PdfDictionary kd when includeDescendants && kd.GetName("Type") is null or "StructElem":
                    foreach (var child in ChildElements)
                        if (child is StructureElement se && ReferenceEquals(se._dict, kd))
                        {
                            se.CollectPieces(context, pageDict, includeDescendants);
                            break;
                        }
                    break;
            }
        }

        void AddPieces(Dictionary<int, List<MarkedContentScan.Piece>> map, int mcid, Page? page)
        {
            if (map.TryGetValue(mcid, out var list))
                foreach (var piece in list)
                    context.Pieces.Add((this, page, piece));
        }
    }

    /// <summary>Joins consecutive strings of one element in one style into a run, restoring
    /// the space a word gap or a line change stands for.</summary>
    /// <param name="runStarts">Indexes of pieces that start a run of their own (the first piece
    /// of each artifact), the space before them still restored; null: none.</param>
    /// <param name="itemStarts">When given, receives for each item the index of its first piece.</param>
    internal static void BuildItems(PdfReader reader,
        List<(StructureElement? Owner, Page? Page, MarkedContentScan.Piece Piece)> pieces, List<MarkedContentItem> result,
        ISet<int>? runStarts = null, List<int>? itemStarts = null)
    {
        var fonts = new Dictionary<PdfDictionary, (Aspose.Pdf.Text.Font Font, string Name, bool Bold, bool Italic)>();
        MarkedContentScan.Piece? previous = null;
        MarkedContentScan.Piece? inked = null; // the last piece showing text
        MarkedContentItem? run = null;
        MarkedContentScan.Piece? runStyle = null;
        var lineStarts = LaterLineStarts(pieces);
        for (var index = 0; index < pieces.Count; index++)
        {
            var (owner, page, piece) = pieces[index];
            if (runStarts is not null && runStarts.Contains(index)) run = null;
            if (piece.IsDrawing)
            {
                // Consecutive drawing pieces of one element are one drawing: their union (a piece opening an artifact of its
                // own - a stroke drawn apart between an element's text - opens a drawing of its own).
                if (result.Count > 0 && result[^1] is { Kind: MarkedContentKind.Drawing } last
                    && ReferenceEquals(last.Element, owner) && ReferenceEquals(last.Page, page) && run is null
                    && runStarts?.Contains(index) != true)
                {
                    var r = last.Rectangle;
                    last.Rectangle = new Rectangle(Math.Min(r.LLX, piece.Llx), Math.Min(r.LLY, piece.Lly), Math.Max(r.URX, piece.Urx), Math.Max(r.URY, piece.Ury));
                    // (paths of other colours paint it in none)
                    if (last.ForegroundColor is { } colour && !colour.Equals(Color.FromRgb(piece.R, piece.G, piece.B))) last.ForegroundColor = null;
                    continue;
                }
                result.Add(new MarkedContentItem(MarkedContentKind.Drawing, owner, page, piece.Mcid)
                {
                    Rectangle = new Rectangle(piece.Llx, piece.Lly, piece.Urx, piece.Ury),
                    ForegroundColor = Color.FromRgb(piece.R, piece.G, piece.B),
                });
                itemStarts?.Add(index);
                run = null;
                continue;
            }
            if (piece.IsImage)
            {
                result.Add(new MarkedContentItem(MarkedContentKind.Image, owner, page, piece.Mcid)
                {
                    Image = new XImage(piece.ImageName ?? string.Empty, piece.ImageStream!, reader),
                    ImageMatrix = piece.ImageMatrix,
                    Rectangle = new Rectangle(piece.Llx, piece.Lly, piece.Urx, piece.Ury),
                });
                itemStarts?.Add(index);
                run = null;
                continue;
            }
            if (piece.Text.Length == 0) continue;
            // (a blank stretched over the gap - spaces run out to the text after them - parts nothing: the gap is measured
            // from the text before it)
            // (a turned string is read along its own baseline: the pieces are compared as if the line ran left to right)
            var (along, before0, inked0) = (OnLine(piece, piece.Angle)!, OnLine(previous, piece.Angle), OnLine(inked, piece.Angle));
            var tabbed = TabGap(before0, along)
                         || previous is not null && string.IsNullOrWhiteSpace(previous.Text) && TabGap(inked0, along)
                         || piece.Angle == 0 && HangsPastLabel(inked, piece, owner is not null && lineStarts.TryGetValue(owner, out var lines) ? lines : null);
            var shifted = Shifted(before0, along);
            var before = run?.Text ?? (result.Count > 0 && result[^1].Kind == MarkedContentKind.Text ? result[^1].Text : string.Empty);
            var (separator, dropHyphen) = Separator(before0, along, before);
            if (dropHyphen)
            {
                // The word goes on where the hyphen stood: the break is noted there.
                var broken = run ?? result[^1];
                broken.Text = broken.Text.Substring(0, broken.Text.Length - 1);
                broken.NoteHyphenBreak(broken.Text.Length);
            }
            // A run ends with its line: the next line's text starts a run of its own (a reader can tell
            // where the author's lines broke) - unless a word goes on across the break (nothing parts them) at a hyphen
            // of its own (self-/attention); one broken there with a hyphen that goes ends its run, the break noted.
            var joined = separator.Length == 0 && before.Length > 0 && !char.IsWhiteSpace(before[^1]) && !char.IsWhiteSpace(piece.Text[0])
                         && !dropHyphen;
            var wrapped = before0 is not null && !joined && Math.Abs(along.Y - before0.Y) >= SameLineEms * Math.Max(before0.Size, along.Size);
            var text = separator + piece.Text;
            previous = piece;
            if (!string.IsNullOrWhiteSpace(piece.Text)) inked = piece;
            if (run is not null && ReferenceEquals(run.Element, owner) && ReferenceEquals(run.Page, page)
                && SameStyle(runStyle!, piece) && !tabbed && !shifted && !wrapped)
            {
                run.Text += text;
                var r = run.Rectangle;
                var box = BoxOf(piece);
                run.Rectangle = new Rectangle(Math.Min(r.LLX, box.LLX), Math.Min(r.LLY, box.LLY), Math.Max(r.URX, box.URX), Math.Max(r.URY, box.URY));
                continue;
            }
            var style = piece.FontDict is null ? default : Style(reader, piece, fonts);
            run = new MarkedContentItem(MarkedContentKind.Text, owner, page, piece.Mcid)
            {
                Text = text,
                Font = style.Font,
                FontName = style.Name ?? string.Empty,
                FontSize = (float)piece.Size,
                IsBold = style.Bold || piece.RenderMode is FillStrokeMode or FillStrokeClipMode,
                IsItalic = style.Italic,
                ForegroundColor = Color.FromRgb(piece.R, piece.G, piece.B),
                Rectangle = BoxOf(piece),
                Rotation = piece.Angle,
            };
            runStyle = piece;
            result.Add(run);
            itemStarts?.Add(index);
        }
    }

    /// <summary>For each element, its first line's baseline and where the lines of its text after the first start (points
    /// from the page's left edge): the unturned pieces opening a line under the text before them.</summary>
    private static Dictionary<StructureElement, (double FirstLine, List<double> Starts)> LaterLineStarts(
        List<(StructureElement? Owner, Page? Page, MarkedContentScan.Piece Piece)> pieces)
    {
        var result = new Dictionary<StructureElement, (double FirstLine, List<double> Starts)>();
        MarkedContentScan.Piece? inked = null;
        foreach (var (owner, _, piece) in pieces)
        {
            if (piece.IsDrawing || piece.IsImage || string.IsNullOrWhiteSpace(piece.Text)) continue;
            if (owner is not null && piece.Angle == 0)
            {
                if (!result.TryGetValue(owner, out var lines)) result[owner] = lines = (piece.Y, new List<double>());
                else if (inked is not null && inked.Angle == 0
                         && inked.Y - piece.Y >= SameLineEms * Math.Max(Math.Max(inked.Size, piece.Size), 1))
                    lines.Starts.Add(piece.X0);
            }
            inked = piece;
        }
        return result;
    }

    /// <summary>Whether <paramref name="next"/> goes on along its element's first line, more than an em after the text
    /// before it, starting where the element's later lines start: text hanging past the label opening it.</summary>
    private static bool HangsPastLabel(MarkedContentScan.Piece? inked, MarkedContentScan.Piece next,
        (double FirstLine, List<double> Starts)? lines)
    {
        if (inked is null || lines is not { } element || inked.Angle != 0) return false;
        var size = Math.Max(Math.Max(inked.Size, next.Size), 1);
        return Math.Abs(next.Y - inked.Y) < SameLineEms * size && Math.Abs(inked.Y - element.FirstLine) < SameLineEms * size
               && next.X0 - inked.X1 > HangGapEms * size && element.Starts.Any(x => Math.Abs(x - next.X0) <= LineStartSlack);
    }

    /// <summary>A piece as it stands along a baseline turned <paramref name="angle"/> degrees counter-clockwise: its start,
    /// end and baseline as if that line ran left to right (the piece itself when the line is not turned).</summary>
    private static MarkedContentScan.Piece? OnLine(MarkedContentScan.Piece? piece, int angle)
    {
        if (piece is null || angle == 0) return piece;
        var (cos, sin) = (Math.Cos(angle * Math.PI / 180), Math.Sin(angle * Math.PI / 180));
        return new MarkedContentScan.Piece
        {
            Text = piece.Text, Size = piece.Size, Angle = piece.Angle,
            X0 = piece.X0 * cos + piece.Y * sin, X1 = piece.X1 * cos + piece.Y1 * sin,
            Y = piece.Y * cos - piece.X0 * sin, Y1 = piece.Y1 * cos - piece.X1 * sin,
        };
    }

    /// <summary>The box a piece's text covers on the page as it is shown: from its baseline's start to its end, a descent
    /// under the baseline and an ascent over it - turned with the baseline.</summary>
    private static Rectangle BoxOf(MarkedContentScan.Piece piece)
    {
        var (cos, sin) = (Math.Cos(piece.Angle * Math.PI / 180), Math.Sin(piece.Angle * Math.PI / 180));
        var (under, over) = (-DescentEms * piece.Size, AscentEms * piece.Size);
        var xs = new[] { piece.X0 - sin * under, piece.X0 - sin * over, piece.X1 - sin * under, piece.X1 - sin * over };
        var ys = new[] { piece.Y + cos * under, piece.Y + cos * over, piece.Y1 + cos * under, piece.Y1 + cos * over };
        return new Rectangle(xs.Min(), ys.Min(), xs.Max(), ys.Max());
    }

    private static bool SameStyle(MarkedContentScan.Piece a, MarkedContentScan.Piece b)
        => ReferenceEquals(a.FontDict, b.FontDict) && Math.Abs(a.Size - b.Size) < 0.05
           && a.R == b.R && a.G == b.G && a.B == b.B && a.RenderMode == b.RenderMode && a.Angle == b.Angle;

    /// <summary>Whether <paramref name="next"/> stands on the line of <paramref name="previous"/>
    /// set apart from it by a gap wider than a few spaces.</summary>
    private static bool TabGap(MarkedContentScan.Piece? previous, MarkedContentScan.Piece next)
    {
        if (previous is null) return false;
        var size = Math.Max(Math.Max(previous.Size, next.Size), 1);
        return Math.Abs(next.Y - previous.Y) < SameLineEms * size && next.X0 - previous.X1 > TabGapEms * size;
    }

    /// <summary>The space between two shown strings: one for a word gap on the same line or
    /// for a new line, none when either side already has it or a line ends in a hyphen that
    /// joins a word - and whether that hyphen goes (<see cref="DropsHyphen"/>).</summary>
    /// <param name="before">The text read so far up to and including <paramref name="previous"/>
    /// (a string shown glyph by glyph makes the hyphen a piece of its own).</param>
    private static (string Space, bool DropHyphen) Separator(MarkedContentScan.Piece? previous, MarkedContentScan.Piece next, string before)
    {
        if (previous is null) return (string.Empty, false);
        if (before.Length < previous.Text.Length) before = previous.Text;
        var after = next.Text;
        if (before.Length == 0 || char.IsWhiteSpace(before[^1]) || char.IsWhiteSpace(after[0])) return (string.Empty, false);
        var size = Math.Max(Math.Min(previous.Size, next.Size), 1);
        var sameLine = Math.Abs(next.Y - previous.Y) < SameLineEms * Math.Max(previous.Size, next.Size)
                       && next.X0 >= previous.X0;
        if (sameLine) return (next.X0 - previous.X1 > WordGapEms * size ? " " : string.Empty, false);
        var hyphenated = before.Length >= 2 && before[^1] is '-' or SoftHyphen && char.IsLetter(before[^2])
                         && char.IsLetter(after[0]);
        return hyphenated ? (string.Empty, DropsHyphen(before, after)) : (" ", false);
    }

    private const char SoftHyphen = '\u00AD';

    /// <summary>Whether the hyphen ending <paramref name="before"/> only broke a word over the
    /// line: it goes when both halves are lower-case letters and neither half holds a hyphen of
    /// its own or is a compound's head (state-of-the-art, self-attention keep theirs); a soft
    /// hyphen always goes.</summary>
    private static bool DropsHyphen(string before, string after)
    {
        if (before[^1] == SoftHyphen) return true;
        if (!char.IsLower(before[^2]) || !char.IsLower(after[0])) return false;
        var head = before.Substring(0, before.Length - 1);
        head = head.Substring(head.LastIndexOf(' ') + 1);
        if (head.IndexOf('-') >= 0 || CompoundHeads.Contains(head)) return false;
        var tailEnd = after.IndexOf(' ');
        var tail = tailEnd < 0 ? after : after.Substring(0, tailEnd);
        return tail.IndexOf('-') < 0;
    }

    private static (Aspose.Pdf.Text.Font Font, string Name, bool Bold, bool Italic) Style(PdfReader reader,
        MarkedContentScan.Piece piece, Dictionary<PdfDictionary, (Aspose.Pdf.Text.Font, string, bool, bool)> cache)
    {
        var dict = piece.FontDict!;
        if (cache.TryGetValue(dict, out var known)) return known;
        var name = dict.GetName("BaseFont") ?? string.Empty;
        if (name.Length > 7 && name[6] == '+') name = name.Substring(7);
        foreach (var suffix in new[] { "-Identity-H", "-Identity-V" })
            if (name.EndsWith(suffix, StringComparison.Ordinal)) name = name.Substring(0, name.Length - suffix.Length);

        var descriptor = reader.ResolveDict(dict.Get("FontDescriptor"));
        if (descriptor is null && reader.Resolve(dict.Get("DescendantFonts")) is PdfArray { Count: > 0 } descendants)
            descriptor = reader.ResolveDict(reader.ResolveDict(descendants[0])?.Get("FontDescriptor"));
        var flags = descriptor is not null ? (int)descriptor.GetInt("Flags") : 0;
        var weight = descriptor is not null && reader.Resolve(descriptor.Get("FontWeight")) is PdfInteger w ? w.Value : 0;
        var angle = descriptor is not null && reader.Resolve(descriptor.Get("ItalicAngle")) is PdfObject a
            ? a switch { PdfInteger i => i.Value, PdfReal r => r.Value, _ => 0 } : 0;

        var bold = weight >= BoldWeight || (flags & ForceBoldFlag) != 0
                   || BoldNameParts.Any(p => name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                   || BoldSuffix.IsMatch(name)
                   || TexBold.IsMatch(name)
                   || (name.StartsWith(UrwPrefix, StringComparison.Ordinal) && name.Contains(UrwBoldPart, StringComparison.Ordinal));
        var italic = angle != 0 || (flags & ItalicFlag) != 0
                     || ItalicNameParts.Any(p => name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                     || TexItalic.IsMatch(name);
        var result = (new Aspose.Pdf.Text.Font(piece.FontKey ?? string.Empty, dict, reader), name, bold, italic);
        cache[dict] = result;
        return result;
    }
}
