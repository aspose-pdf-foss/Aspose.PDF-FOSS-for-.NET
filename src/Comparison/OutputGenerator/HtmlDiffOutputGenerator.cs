using System.Collections.Generic;
using System.IO;
using System.Text;
using Aspose.Pdf.Comparison.Diff;

namespace Aspose.Pdf.Comparison
{
    /// <summary>Writes a comparison result as HTML in the reference shape: a style sheet with
    /// an <c>inserted</c>, a <c>deleted</c>, an <c>equal</c> and a <c>strikethrough</c> class,
    /// then one block per edit list whose unchanged text is written plain and whose edits are
    /// spans of those classes. Unchanged text keeps its line breaks as plain newlines, so the
    /// lines flow; a deletion that is exactly a line break is shown as a pilcrow, an insertion
    /// that is exactly a line break as a real break; the spaces a marked run starts or ends
    /// with are non-breaking so they stay visible under their colour.</summary>
    public class HtmlDiffOutputGenerator : IStringOutputGenerator, IFileOutputGenerator
    {
        // The reference sheet, verbatim: deleted text is #990000 on #ffcc99, inserted text
        // #003300 on #ccff66, equal text unstyled. A caller's OutputTextStyle replaces a
        // class body.
        private const string DefaultInsertedCss = "color: #003300;\nbackground-color: #ccff66;\n";
        private const string DefaultDeletedCss = "color: #990000;\nbackground-color:#ffcc99;\n";
        private const string Pilcrow = "¶";
        private const string NonBreakingSpace = "&nbsp;";

        private readonly OutputTextStyle? _style;

        /// <summary>Create a generator with the default colours.</summary>
        public HtmlDiffOutputGenerator() { }

        /// <summary>Create a generator drawing each kind of edit in the given style.</summary>
        public HtmlDiffOutputGenerator(OutputTextStyle textStyle) => _style = textStyle;

        /// <summary>Draw deleted text with a line through it, on top of its colours.</summary>
        public bool StrikethroughDeleted { get; set; }

        /// <inheritdoc/>
        public string GenerateOutput(List<DiffOperation> differences)
        {
            var sb = new StringBuilder();
            OpenDocument(sb);
            AppendGroup(sb, differences);
            CloseDocument(sb);
            return sb.ToString();
        }

        /// <inheritdoc/>
        public string GenerateOutput(List<List<DiffOperation>> differences)
        {
            var sb = new StringBuilder();
            OpenDocument(sb);
            if (differences is not null)
                foreach (var group in differences)
                    AppendGroup(sb, group);
            CloseDocument(sb);
            return sb.ToString();
        }

        /// <inheritdoc/>
        public void GenerateOutput(List<DiffOperation> differences, string targetFilePath)
            => WriteFile(GenerateOutput(differences), targetFilePath);

        /// <inheritdoc/>
        public void GenerateOutput(List<List<DiffOperation>> differences, string targetFilePath)
            => WriteFile(GenerateOutput(differences), targetFilePath);

        private void OpenDocument(StringBuilder sb)
        {
            sb.Append("<!doctype html>\n<html>\n<head>\n<style>\n\n");
            AppendClass(sb, "inserted", CssFor(_style?.InsertedStyle, DefaultInsertedCss));
            AppendClass(sb, "deleted", CssFor(_style?.DeletedStyle, DefaultDeletedCss));
            AppendClass(sb, "equal", CssFor(_style?.EqualStyle, "\n"));
            AppendClass(sb, "strikethrough", "text-decoration: line-through;\n");
            sb.Append("</style>\n</head>\n<body>\n");
        }

        private static void AppendClass(StringBuilder sb, string name, string body)
            => sb.Append('.').Append(name).Append("\n{\n").Append(body).Append("}\n\n");

        private static void CloseDocument(StringBuilder sb) => sb.Append("</body>\n</html>\n");

        /// <summary>One edit list is one block, set off from the next by a bottom margin.</summary>
        private void AppendGroup(StringBuilder sb, List<DiffOperation>? differences)
        {
            sb.Append("<div style=\"margin-bottom: 20pt;\">\n");
            if (differences is not null)
                foreach (var d in differences)
                    if (d is not null) Append(sb, d);
            sb.Append("\n</div>\n");
        }

        private void Append(StringBuilder sb, DiffOperation d)
        {
            var text = d.Text ?? string.Empty;
            switch (d.Operation)
            {
                case Operation.Insert:
                    if (IsLineBreak(text)) sb.Append(text).Append("<br/>");
                    else AppendSpan(sb, text, "inserted");
                    break;
                case Operation.Delete:
                    var strike = StrikethroughDeleted || (_style?.StrikethroughDeleted ?? false);
                    var cls = strike ? "deleted strikethrough" : "deleted";
                    if (IsLineBreak(text)) AppendSpan(sb, Pilcrow, cls);
                    else AppendSpan(sb, text, cls);
                    break;
                default:
                    if (_style?.EqualStyle is null) sb.Append(Escape(text));
                    else AppendSpan(sb, text, "equal");
                    break;
            }
        }

        private static bool IsLineBreak(string text) => text == "\r\n" || text == "\n";

        /// <summary>A marked run. The spaces it starts and ends with become non-breaking so
        /// they are drawn under the run's colour; everything else, line breaks included, is
        /// written as it is.</summary>
        private static void AppendSpan(StringBuilder sb, string text, string cssClass)
        {
            var start = 0;
            while (start < text.Length && text[start] == ' ') start++;
            var end = text.Length;
            while (end > start && text[end - 1] == ' ') end--;

            sb.Append("<span class = \"").Append(cssClass).Append("\">");
            for (var i = 0; i < start; i++) sb.Append(NonBreakingSpace);
            sb.Append(Escape(text.Substring(start, end - start)));
            for (var i = end; i < text.Length; i++) sb.Append(NonBreakingSpace);
            sb.Append("</span>");
        }

        private static string CssFor(TextStyle? style, string fallback)
        {
            var css = style?.ToCssStyle();
            return string.IsNullOrEmpty(css) ? fallback : css!;
        }

        /// <summary>Only the three characters HTML cannot carry as text. Everything else -
        /// the typographic quotes, the pilcrow - is written as the UTF-8 it is, as the
        /// reference output does.</summary>
        private static string Escape(string text)
            => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static void WriteFile(string content, string targetFilePath)
        {
            var dir = Path.GetDirectoryName(targetFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(targetFilePath, content, new UTF8Encoding(false));
        }
    }
}
