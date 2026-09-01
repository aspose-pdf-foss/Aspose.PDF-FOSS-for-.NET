using System;
using System.Linq;

namespace Aspose.Pdf.PdfToMarkdown
{
    /// <summary>Decides whether a line opens with a heading NUMERATION — "1 Header",
    /// "1.2.3 Header", "I.II Header" — and how deep it is.
    ///
    /// The rule (derived from the test cases):
    /// <list type="bullet">
    ///   <item>the numeration is the text before the FIRST space, split on '.';</item>
    ///   <item>every group must be non-empty and either all-Arabic ("1", "1.2.3")
    ///         or all-Roman ("I", "I.II.III.IV") — mixed or malformed groups are not
    ///         a numeration;</item>
    ///   <item>a space MUST separate the numeration from the heading text, so
    ///         "1.2.3Header" and "IHeader" are ordinary words;</item>
    ///   <item>text must actually follow it: "1.2.3" and "1.2.3   " are not headings;</item>
    ///   <item>the level is the number of groups.</item>
    /// </list></summary>
    internal partial class HeaderNumerationChecker
    {
        private const string RomanDigits = "IVXLCDM";

        internal HeaderNumerationChecker()
        {
        }

        internal bool IsHeaderWithNumeration(string input, out int headingLevel)
        {
            headingLevel = -1;
            if (string.IsNullOrEmpty(input)) return false;

            var space = input.IndexOf(' ');
            if (space <= 0) return false;                       // no separator, or leading space

            var text = input.Substring(space + 1);
            if (string.IsNullOrWhiteSpace(text)) return false;  // numeration with nothing after it

            var groups = input.Substring(0, space).Split('.');
            if (groups.Length == 0 || groups.Any(string.IsNullOrEmpty)) return false;

            var arabic = groups.All(g => g.All(char.IsDigit));
            var roman = groups.All(g => g.All(c => RomanDigits.IndexOf(char.ToUpperInvariant(c)) >= 0));
            if (!arabic && !roman) return false;

            headingLevel = groups.Length;
            return true;
        }
    }
}
