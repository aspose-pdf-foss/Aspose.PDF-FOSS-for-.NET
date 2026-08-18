using System.Collections.Generic;
using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>A text fragment prepared for comparison: its comparable text plus the
    /// page geometry needed to map character offsets back to page rectangles.</summary>
    internal class Fragment
    {
        private List<Rectangle>? _charRects;

        /// <summary>The text this fragment contributes to the comparison string.</summary>
        public string Text { get; protected set; }

        /// <summary>Bounding rectangle of the fragment's glyphs on the page.</summary>
        public Rectangle Rect { get; protected set; }

        protected TextFragment TextFragment { get; }

        internal Fragment(TextFragment textFragment)
        {
            TextFragment = textFragment;
            Text = textFragment.Text ?? string.Empty;
            Rect = textFragment.Rectangle ?? Rectangle.Trivial;
        }

        /// <summary>Page rectangle of the character at <paramref name="charPosition"/>
        /// (0-based within <see cref="Text"/>). Positions are clamped, so a caller that
        /// walks past the end still gets the last glyph's rectangle.</summary>
        internal virtual Rectangle FindCharRect(int charPosition)
        {
            var rects = EnsureCharRects();
            if (rects.Count == 0) return Rect;
            if (charPosition < 0) charPosition = 0;
            if (charPosition >= rects.Count) charPosition = rects.Count - 1;
            return rects[charPosition];
        }

        /// <summary>Per-character rectangles for the fragment's glyph text, taken from the
        /// absorber's <see cref="TextSegment.Characters"/> when populated and distributed
        /// evenly across the segment box otherwise.</summary>
        protected List<Rectangle> EnsureCharRects()
        {
            if (_charRects is not null) return _charRects;
            _charRects = new List<Rectangle>();
            foreach (var segment in TextFragment.Segments)
            {
                var segText = segment.Text ?? string.Empty;
                if (segText.Length == 0) continue;
                if (segment.Characters.Count == segText.Length)
                {
                    foreach (var ch in segment.Characters)
                        _charRects.Add(ch.Rectangle);
                    continue;
                }
                var box = segment.Rectangle ?? TextFragment.Rectangle ?? Rectangle.Trivial;
                var step = box.Width / segText.Length;
                for (var i = 0; i < segText.Length; i++)
                    _charRects.Add(new Rectangle(
                        box.LLX + step * i, box.LLY, box.LLX + step * (i + 1), box.URY));
            }
            return _charRects;
        }
    }
}
