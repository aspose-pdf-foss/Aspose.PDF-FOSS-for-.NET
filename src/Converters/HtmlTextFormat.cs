namespace Aspose.Pdf.Converters;

/// <summary>
/// Line-ending policy for generated HTML and CSS. The markup is assembled with
/// <c>StringBuilder.AppendLine</c>, which takes <c>Environment.NewLine</c> - CRLF on
/// Windows, a bare LF everywhere else. The break is part of the OUTPUT FORMAT, though,
/// not of the host: calling code matches CSS declarations against literal CRLF and sizes
/// the saved file against fixed bounds, so the same document has to serialise to the same
/// bytes on every platform. Pin it to CRLF, which is what the format has always carried.
/// </summary>
internal static class HtmlTextFormat
{
    private const string Crlf = "\r\n";

    /// <summary>Every line break in <paramref name="text"/> as CRLF, whichever form it
    /// arrived in. A lone CR is left alone - inside a quoted CSS or attribute value it is
    /// content rather than a break.</summary>
    public static string Crlfify(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('\n') < 0) return text;
        var sb = new System.Text.StringBuilder(text.Length + 32);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '\n') { sb.Append(c); continue; }
            if (i == 0 || text[i - 1] != '\r') sb.Append(Crlf);
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
