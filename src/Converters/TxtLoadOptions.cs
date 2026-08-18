using System.IO;
using System.Text;

namespace Aspose.Pdf
{
    /// <summary>
    /// Options for loading a plain-text (.txt) file as a PDF document. The text is
    /// laid out line-by-line at the configured font size and margins. Lives in the
    /// Aspose.Pdf namespace to match the public API surface.
    /// </summary>
    public sealed class TxtLoadOptions : LoadOptions
    {
        /// <summary>Font size (points) used for the rendered text. Default 11.</summary>
        public double FontSize { get; set; } = 11;
    }
}

namespace Aspose.Pdf.Converters
{
    /// <summary>
    /// Converts a plain-text file into a PDF document: each input line becomes a
    /// flow paragraph, so the text stays selectable/extractable in the output.
    /// </summary>
    internal static class TxtToPdfConverter
    {
        public static byte[] Convert(byte[] data, TxtLoadOptions options)
        {
            var text = Decode(data);
            var doc = new Document();
            var page = doc.Pages.Add();
            page.PageInfo.Margin = new MarginInfo(40, 40, 40, 40);

            var fontSize = options.FontSize > 0 ? options.FontSize : 11;
            // Normalise line endings, then emit one paragraph per line. Empty lines
            // become a single space so the blank line still advances the cursor.
            foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var tf = new Text.TextFragment(line.Length == 0 ? " " : line);
                tf.TextState.FontSize = (float)fontSize;
                page.Paragraphs.Add(tf);
            }

            return doc.ToArray();
        }

        public static byte[] Convert(string path, TxtLoadOptions options)
            => Convert(File.ReadAllBytes(path), options);

        // Honour a UTF-8/UTF-16 BOM; otherwise decode as UTF-8 (the common case and
        // a superset of ASCII).
        private static string Decode(byte[] data)
        {
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                return Encoding.UTF8.GetString(data, 3, data.Length - 3);
            if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
                return Encoding.Unicode.GetString(data, 2, data.Length - 2);
            if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(data, 2, data.Length - 2);
            return Encoding.UTF8.GetString(data);
        }
    }
}
