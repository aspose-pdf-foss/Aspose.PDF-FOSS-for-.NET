using Aspose.Pdf.Text;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>A fragment carrying whitespace synthesized from page geometry
    /// (ParseSpaces mode): trailing spaces for an inter-word gap, or a line break when the
    /// next fragment starts a new line. Synthetic characters have no glyphs — their
    /// rectangles are laid out to the right of the fragment's own box.</summary>
    internal class FragmentWithSpaces : Fragment
    {
        private readonly int _glyphLength;
        private readonly int _spaceCount;
        private readonly double _spacesWidth;

        internal FragmentWithSpaces(TextFragment textFragment)
            : base(textFragment)
        {
            _glyphLength = Text.Length;
        }

        internal FragmentWithSpaces(TextFragment textFragment, bool lineBreakNeeded)
            : base(textFragment)
        {
            _glyphLength = Text.Length;
            if (lineBreakNeeded) Text += "\n";
        }

        internal FragmentWithSpaces(TextFragment textFragment, int spaceCount, double spacesWidth)
            : base(textFragment)
        {
            _glyphLength = Text.Length;
            if (spaceCount > 0)
            {
                _spaceCount = spaceCount;
                _spacesWidth = spacesWidth;
                Text += new string(' ', spaceCount);
            }
        }

        internal override Rectangle FindCharRect(int charPosition)
        {
            if (charPosition < _glyphLength) return base.FindCharRect(charPosition);

            // Synthetic space / line-break characters: slots after the last glyph.
            var step = _spaceCount > 0 ? _spacesWidth / _spaceCount : 0;
            var slot = charPosition - _glyphLength;
            return new Rectangle(
                Rect.URX + step * slot, Rect.LLY, Rect.URX + step * (slot + 1), Rect.URY);
        }
    }
}
