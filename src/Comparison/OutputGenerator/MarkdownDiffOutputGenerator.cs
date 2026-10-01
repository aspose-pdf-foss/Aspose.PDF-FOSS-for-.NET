using System.Collections.Generic;
using System.IO;
using System.Text;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Writes a comparison result as Markdown: unchanged text as-is, inserted text
    /// in bold, deleted text in bold and struck through. The reference shape, reproduced
    /// here: a marked run is trimmed inside its markers and its own leading and trailing
    /// spaces stay outside them; where a marker would touch a neighbour a single space is
    /// put between; an inserted run of whitespace is written as it is, a deleted one is
    /// dropped, and a deleted line break is shown as a pilcrow so the removal stays visible.</summary>
    public class MarkdownDiffOutputGenerator : IStringOutputGenerator, IFileOutputGenerator
    {
        /// <summary>Stands in for a deleted line break, which would otherwise vanish.</summary>
        private const string Pilcrow = "¶";

        /// <summary>Create a generator with the default marking.</summary>
        public MarkdownDiffOutputGenerator() { }

        /// <inheritdoc/>
        public string GenerateOutput(List<DiffOperation> differences)
        {
            var sb = new StringBuilder();
            if (differences is null) return string.Empty;
            for (var i = 0; i < differences.Count; i++)
            {
                var d = differences[i];
                if (d is null) continue;
                var next = i + 1 < differences.Count ? differences[i + 1]?.Text : null;
                Append(sb, d, next);
            }
            return sb.ToString();
        }

        /// <inheritdoc/>
        public string GenerateOutput(List<List<DiffOperation>> differences)
        {
            // One paragraph per group, separated by a blank line so each page reads apart.
            var sb = new StringBuilder();
            if (differences is not null)
            {
                var first = true;
                foreach (var group in differences)
                {
                    if (!first) sb.Append("\r\n\r\n");
                    first = false;
                    sb.Append(GenerateOutput(group));
                }
            }
            return sb.ToString();
        }

        /// <inheritdoc/>
        public void GenerateOutput(List<DiffOperation> differences, string targetFilePath)
            => WriteFile(GenerateOutput(differences), targetFilePath);

        /// <inheritdoc/>
        public void GenerateOutput(List<List<DiffOperation>> differences, string targetFilePath)
            => WriteFile(GenerateOutput(differences), targetFilePath);

        private static void Append(StringBuilder sb, DiffOperation d, string? nextText)
        {
            var text = d.Text ?? string.Empty;
            switch (d.Operation)
            {
                case Operation.Insert:
                    if (text.Trim().Length == 0) sb.Append(text);
                    else AppendMarked(sb, text, "**", "**", nextText);
                    break;
                case Operation.Delete:
                    if (text == "\r\n" || text == "\n") sb.Append(Pilcrow);
                    else if (text.Trim().Length > 0) AppendMarked(sb, text, "**~~", "~~**", nextText);
                    break;
                default:
                    sb.Append(text);
                    break;
            }
        }

        /// <summary>Emphasis cannot open or close on a space, so the run is trimmed inside the
        /// markers and its own surrounding spaces are written outside them. A marker that
        /// would otherwise touch the text before or after it gets a single space between.</summary>
        private static void AppendMarked(StringBuilder sb, string text, string open, string close, string? nextText)
        {
            var core = text.Trim();
            var start = text.IndexOf(core, System.StringComparison.Ordinal);
            var leading = text.Substring(0, start);
            var trailing = text.Substring(start + core.Length);

            if (leading.Length == 0 && sb.Length > 0 && !char.IsWhiteSpace(sb[sb.Length - 1])) sb.Append(' ');
            sb.Append(leading).Append(open).Append(core).Append(close).Append(trailing);
            if (trailing.Length == 0 && !string.IsNullOrEmpty(nextText) && !char.IsWhiteSpace(nextText![0])) sb.Append(' ');
        }

        private static void WriteFile(string content, string targetFilePath)
        {
            var dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(targetFilePath, content, new UTF8Encoding(false));
        }
    }
}
