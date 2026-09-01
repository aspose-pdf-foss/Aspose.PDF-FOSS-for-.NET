#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.PdfToMarkdown;

internal static partial class MarkdownRenderer
{
    // An outline destination reduced to what heading matching needs: the target page, the
    // top edge it points at, and the outline nesting depth (= heading level).
    private readonly record struct HeadingDest(int Page, double Top, int Level);

    private sealed record LinkInfo(Rectangle Rect, string Uri);

    private sealed record MdBlock(string Text, bool IsTable, double TopY);

    private readonly struct Elem
    {
        public readonly TextFragment Frag;
        public readonly Rectangle Rect;
        public readonly string ImageToken;
        private readonly byte _extraStyle;

        private Elem(TextFragment frag, Rectangle rect, string token, byte extraStyle = 0)
        {
            Frag = frag; Rect = rect; ImageToken = token; _extraStyle = extraStyle;
        }

        public static Elem ForText(TextFragment f) => new(f, f.Rectangle, null);
        public static Elem ForImage(Rectangle r, string token) => new(null, r, token);
        /// <summary>A text element carrying a forced style bit (a raised reference
        /// detected by geometry rather than by the extractor's own script flag).</summary>
        public static Elem ForScript(TextFragment f, byte extraStyle) => new(f, f.Rectangle, null, extraStyle);

        public bool IsImage => ImageToken != null;
        public double LLX => Rect.LLX;
        public double URX => Rect.URX;
        public double LLY => Rect.LLY;
        public double URY => Rect.URY;
        public double FontSize => IsImage ? 0 : Frag.FontSize;
        public string Text => IsImage ? ImageToken : Frag.Text;

        // Inline style bits: 1=bold, 2=italic, 4=strikethrough, 8=superscript, 16=subscript.
        public byte Style
        {
            get
            {
                if (IsImage) return 0;
                var ts = Frag.TextState;
                byte s = _extraStyle;
                if ((ts.FontStyle & FontStyles.Bold) != 0) s |= 1;
                if ((ts.FontStyle & FontStyles.Italic) != 0) s |= 2;
                if (ts.StrikeOut) s |= 4;
                if (ts.Superscript) s |= 8;
                if (ts.Subscript) s |= 16;
                return s;
            }
        }
    }

    private sealed class Line
    {
        public readonly List<Elem> Elems = new();
        // In multi-column paragraphs an inter-fragment space is inserted
        // by ink-gap even when the previous fragment already ends in a space, producing the
        // doubled spaces seen between column items.
        public bool Columnar;
        public string Text { get; private set; } = string.Empty;
        public List<string> CharUris { get; } = new();
        public List<bool> CharIsImage { get; } = new();
        public List<byte> CharStyle { get; } = new();
        public double FontSize { get; private set; }
        public double Right { get; private set; }
        public double Left { get; private set; }
        public double TopY { get; private set; }
        public bool IsItalic { get; private set; }
        public int CharCount { get; private set; }
        public int BoldChars { get; private set; }
        public int ItalicChars { get; private set; }
        public bool HasMixedTextSizes { get; private set; }
        public double RaisedTopY { get; private set; }
        public string CoreText { get; private set; } = string.Empty;

        public void Finish(List<LinkInfo> links)
        {
            var sizeWeight = new Dictionary<double, int>();
            double minSize = double.MaxValue, maxSize = 0;
            foreach (var e in Elems)
            {
                if (e.IsImage) continue;
                var len = e.Frag.Text?.Length ?? 0;
                var s = Math.Round(e.FontSize * 2) / 2.0;
                sizeWeight.TryGetValue(s, out var w);
                sizeWeight[s] = w + len;

                // Script glyphs are legitimately smaller; only main-baseline text
                // decides whether the line mixes sizes.
                if ((e.Style & 24) == 0 && len > 0 && !string.IsNullOrWhiteSpace(e.Frag.Text))
                {
                    minSize = Math.Min(minSize, e.FontSize);
                    maxSize = Math.Max(maxSize, e.FontSize);
                }

                CharCount += len;
                if ((e.Frag.TextState.FontStyle & FontStyles.Bold) != 0) BoldChars += len;
                if ((e.Frag.TextState.FontStyle & FontStyles.Italic) != 0) ItalicChars += len;
            }
            HasMixedTextSizes = maxSize > 0 && maxSize - minSize > 0.75;
            FontSize = sizeWeight.Count == 0
                ? 0
                : sizeWeight.OrderByDescending(kv => kv.Value).First().Key;
            IsItalic = CharCount > 0 && ItalicChars * 2 > CharCount;
            Reassemble(links);
        }

        /// <summary>Add an inline image to this line and re-lay it out.</summary>
        public void AddImage(Rectangle rect, string token, List<LinkInfo> links)
        {
            Elems.Add(Elem.ForImage(rect, token));
            Reassemble(links);
        }

        private void Reassemble(List<LinkInfo> links)
        {
            CharUris.Clear();
            CharIsImage.Clear();
            CharStyle.Clear();
            var ordered = Elems.OrderBy(e => e.LLX).ToList();
            AssembleText(ordered, links);
            Right = ordered.Count == 0 ? 0 : ordered.Max(e => e.URX);
            Left = ordered.Count == 0 ? 0 : ordered.Min(e => e.LLX);
            // A raised script glyph must not lift the line's seat (block anchoring and
            // heading matching read TopY), but the paragraph pitch DOES honour it: the
            // line-height grew by the superscript's intrusion, so the gap to the line
            // above is measured from the raised baseline (RaisedTopY).
            var seated = ordered.Where(e => e.IsImage || (e.Style & 24) == 0).ToList();
            if (seated.Count == 0) seated = ordered;
            TopY = seated.Count == 0 ? 0 : seated.Max(e => e.LLY);
            RaisedTopY = ordered.Count == 0 ? 0 : ordered.Max(e => e.LLY);
        }

        // Concatenate the line's elements left-to-right, reconstructing inter-word spaces
        // from horizontal gaps. Image tokens are space-delimited so they read as their own
        // word; a wide text→image gap contributes an extra (word-boundary) space.
        private void AssembleText(List<Elem> ordered, List<LinkInfo> links)
        {
            var sb = new StringBuilder();
            var core = new StringBuilder();
            Elem? prev = null;
            string prevUri = null;
            byte prevStyle = 0;

            void Emit(string s, string uri, bool isImage, byte style)
            {
                foreach (var ch in s)
                {
                    sb.Append(ch);
                    CharUris.Add(uri);
                    CharIsImage.Add(isImage);
                    CharStyle.Add(style);
                }
                if (!isImage) core.Append(s);
            }

            foreach (var e in ordered)
            {
                var uri = LinkFor(e, links);
                var style = e.Style;
                if (prev != null)
                {
                    var gap = e.LLX - prev.Value.URX;
                    var em = Math.Max(Math.Max(prev.Value.FontSize, e.FontSize), 12.0);
                    var lastCh = sb.Length > 0 ? sb[sb.Length - 1] : '\0';
                    var nextCh = e.IsImage ? ' ' : (e.Text.Length > 0 ? e.Text[0] : '\0');
                    // An image already carries its own delimiting spaces, so it only needs an
                    // extra word-boundary space when it sits in a wide gap (a word slot), not
                    // when it abuts the preceding text. Multi-column text uses the
                    // inter-fragment rule (gap > height·0.13636) so that a genuine
                    // column gap synthesizes a space even after a trailing space, while normal
                    // word spacing does not double.
                    var wide = e.IsImage
                        ? gap > em * 0.5
                        : Columnar
                            ? gap > prev.Value.Rect.Height * 0.13636
                            : gap > em * SpaceGapRatio;
                    // HTML image tags glue: no space between two tags, and text after a
                    // tag attaches directly (`/>ipsum`); only a text→tag word slot keeps
                    // its space.
                    if (e.IsImage && prev.Value.IsImage && IsHtmlImageToken(e.ImageToken))
                        wide = false;
                    if (!e.IsImage && prev.Value.IsImage && IsHtmlImageToken(prev.Value.ImageToken))
                        wide = false;
                    // A capital's right side-bearing opens an ink-box gap that can be as wide
                    // as a real space (a bold Times 'H' before a lowercase letter clears ~0.2em),
                    // and the exact advance width is unavailable for this font. Suppress the
                    // synthesized space for an upper→lower transition under ~0.22em — a genuine
                    // inter-word gap in the same context runs wider.
                    if (wide && !e.IsImage && gap < em * 0.22
                        && char.IsUpper(lastCh) && char.IsLower(nextCh))
                        wide = false;
                    if (wide && Columnar && !e.IsImage)
                    {
                        if (lastCh == ' ' && gap <= em * 0.5)
                            wide = false;   // after a space, only a real column gap doubles it
                        else if (nextCh is ',' or '.' or ';' or ':')
                            wide = false;   // punctuation attaches to the preceding word
                        else if (char.IsUpper(nextCh) && !prev.Value.IsImage
                                 && prev.Value.Frag?.Text?.Trim() is { Length: 1 } pt
                                 && char.IsUpper(pt[0]))
                            wide = false;   // small-caps initial glues to its following run
                    }
                    if (wide && (Columnar || lastCh != ' ') && (e.IsImage || nextCh != ' '))
                    {
                        sb.Append(' ');
                        CharUris.Add(prevUri == uri && !e.IsImage ? uri : null);
                        CharIsImage.Add(false);
                        // A synthesized space between two runs of the same style stays inside
                        // that style span (e.g. "acinia interdum leo" under one ~~***…***~~).
                        CharStyle.Add(!e.IsImage && prevStyle == style ? style : (byte)0);
                    }
                }

                if (e.IsImage)
                {
                    // An HTML `<img>` tag glues to its neighbours (only a wide-gap word
                    // slot above synthesizes a space); a markdown token carries its own
                    // delimiting spaces.
                    var htmlTag = IsHtmlImageToken(e.ImageToken);
                    if (!htmlTag) Emit(" ", null, false, 0);
                    Emit(e.ImageToken, null, true, 0);
                    if (!htmlTag) Emit(" ", null, false, 0);
                }
                else
                {
                    Emit(e.Text, uri, false, style);
                }

                prev = e;
                prevUri = uri;
                prevStyle = style;
            }

            Text = sb.ToString();
            CoreText = core.ToString();
        }
    }
}
