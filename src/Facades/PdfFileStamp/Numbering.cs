using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.Stamps;

namespace Aspose.Pdf.Facades;

public sealed partial class PdfFileStamp
{
    public void AddHeader(FormattedText formattedText, float topMargin) =>
        AddHeader(formattedText, topMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddHeader(FormattedText formattedText, float topMargin, float leftMargin, float rightMargin) =>
        ApplyTextBand(formattedText, top: true, primaryMargin: topMargin, leftMargin, rightMargin);

    public void AddHeader(Stream imageStream, float topMargin) =>
        AddHeader(imageStream, topMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddHeader(Stream inputStream, float topMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(ReadAll(inputStream), top: true, primaryMargin: topMargin, leftMargin, rightMargin);

    public void AddHeader(string imageFile, float topMargin) =>
        AddHeader(imageFile, topMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddHeader(string imageFile, float topMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(File.ReadAllBytes(imageFile), top: true, primaryMargin: topMargin, leftMargin, rightMargin);

    public void AddFooter(FormattedText formattedText, float bottomMargin) =>
        AddFooter(formattedText, bottomMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddFooter(FormattedText formattedText, float bottomMargin, float leftMargin, float rightMargin) =>
        ApplyTextBand(formattedText, top: false, primaryMargin: bottomMargin, leftMargin, rightMargin);

    public void AddFooter(Stream imageStream, float bottomMargin) =>
        AddFooter(imageStream, bottomMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddFooter(Stream imageStream, float bottomMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(ReadAll(imageStream), top: false, primaryMargin: bottomMargin, leftMargin, rightMargin);

    public void AddFooter(string imageFile, float bottomMargin) =>
        AddFooter(imageFile, bottomMargin, leftMargin: 36f, rightMargin: 36f);

    public void AddFooter(string imageFile, float bottomMargin, float leftMargin, float rightMargin) =>
        ApplyImageBand(File.ReadAllBytes(imageFile), top: false, primaryMargin: bottomMargin, leftMargin, rightMargin);

    public void AddPageNumber(FormattedText formattedText) =>
        AddPageNumber(formattedText.Text, PosBottomMiddle, 36f, 36f, 36f, 36f, formattedText);

    public void AddPageNumber(FormattedText formattedText, int position) =>
        AddPageNumber(formattedText.Text, position, 36f, 36f, 36f, 36f, formattedText);

    public void AddPageNumber(FormattedText formattedText, int position,
        float leftMargin, float rightMargin, float topMargin, float bottomMargin) =>
        AddPageNumber(formattedText.Text, position, leftMargin, rightMargin, topMargin, bottomMargin, formattedText);

    public void AddPageNumber(FormattedText formattedText, float x, float y) =>
        ApplyPageNumberAtXY(formattedText.Text, x, y, formattedText);

    public void AddPageNumber(string formatString) =>
        AddPageNumber(formatString, PosBottomMiddle, 36f, 36f, 36f, 36f, sourceText: null);

    public void AddPageNumber(string formatString, int position) =>
        AddPageNumber(formatString, position, 36f, 36f, 36f, 36f, sourceText: null);

    public void AddPageNumber(string formatString, int position,
        float leftMargin, float rightMargin, float topMargin, float bottomMargin) =>
        AddPageNumber(formatString, position, leftMargin, rightMargin, topMargin, bottomMargin, sourceText: null);

    public void AddPageNumber(string formatString, float x, float y) =>
        ApplyPageNumberAtXY(formatString, x, y, sourceText: null);

    private void AddPageNumber(string formatString, int position,
        float leftMargin, float rightMargin, float topMargin, float bottomMargin,
        FormattedText? sourceText)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var (hAlign, vAlign, useTop) = ResolvePosition(position);
        var pageCount = _document.PageCount;
        for (var i = 1; i <= pageCount; i++)
        {
            var page = _document.Pages[i];
            var rendered = RenderPageNumberText(formatString, StartingNumber + i - 1, pageCount, NumberingStyle);
            var stamp = BuildTextStamp(rendered, sourceText);
            stamp.HorizontalAlignment = hAlign;
            stamp.VerticalAlignment = vAlign;
            stamp.XIndent = leftMargin > 0 ? leftMargin : (rightMargin > 0 ? -rightMargin : 0);
            stamp.YIndent = useTop ? topMargin : bottomMargin;
            stamp.RotateAngle = PageNumberRotation;
            page.AddStamp(stamp);
        }
    }

    private void ApplyPageNumberAtXY(string formatString, float x, float y, FormattedText? sourceText)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var pageCount = _document.PageCount;
        for (var i = 1; i <= pageCount; i++)
        {
            var page = _document.Pages[i];
            var rendered = RenderPageNumberText(formatString, StartingNumber + i - 1, pageCount, NumberingStyle);
            var stamp = BuildTextStamp(rendered, sourceText);
            stamp.HorizontalAlignment = HorizontalAlignment.None;
            stamp.VerticalAlignment = VerticalAlignment.None;
            stamp.XIndent = x;
            stamp.YIndent = y;
            stamp.RotateAngle = PageNumberRotation;
            page.AddStamp(stamp);
        }
    }

    private void ApplyTextBand(FormattedText formattedText, bool top, float primaryMargin, float leftMargin, float rightMargin)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        var fontSize = formattedText.FontSize;
        // FormattedText.Text is only the first line; a header/footer built from a
        // multi-line FormattedText (AddNewLineText) must carry every line so the
        // stamp renders each as its own row.
        var bandText = string.Join("\n", formattedText.Lines.Select(l => l.Text));
        foreach (var page in _document.Pages)
        {
            var stamp = BuildTextStamp(bandText, formattedText);
            stamp.HorizontalAlignment = HorizontalAlignment.Center;
            stamp.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            stamp.XIndent = leftMargin - rightMargin;
            stamp.YIndent = primaryMargin;
            stamp.MetaRect = TextBandRect(formattedText, top, primaryMargin, leftMargin, rightMargin,
                page.MediaBox.Width, page.MediaBox.Height);
            page.AddStamp(stamp);
        }
    }

    /// <summary>Bounding rectangle a header/footer text band occupies: the text line is
    /// centred within the [leftMargin, pageW-rightMargin] span; a footer sits with its box
    /// bottom at <paramref name="primaryMargin"/> and a header with its box top one tenth of
    /// the font size below <c>pageH - topMargin</c> (the half-leading), each box being exactly
    /// the font size tall.</summary>
    private static Rectangle TextBandRect(FormattedText ft, bool top, float primaryMargin,
        float leftMargin, float rightMargin, double pageW, double pageH)
    {
        double fontSize = ft.FontSize;
        double w;
        try
        {
            var font = Aspose.Pdf.Text.FontRepository.TryFindFont(ft.FontName ?? "Helvetica");
            w = font is not null ? font.MeasureString(ft.Text, fontSize) : ft.Text.Length * fontSize * 0.5;
        }
        catch { w = ft.Text.Length * fontSize * 0.5; }
        double llx = leftMargin + (pageW - leftMargin - rightMargin - w) / 2;
        double lly = top ? (pageH - primaryMargin - fontSize * 0.1 - fontSize) : primaryMargin;
        return new Rectangle(llx, lly, llx + w, lly + fontSize);
    }

    private void ApplyImageBand(byte[] imageBytes, bool top, float primaryMargin, float leftMargin, float rightMargin)
    {
        if (_document is null) throw new InvalidOperationException("No document bound.");
        // Auto-detect JPEG vs PNG vs (Windows) GDI-decodable formats by header bytes,
        // falling back to raw RGB only for genuinely raw pixel payloads. A header/footer
        // image is normally an encoded PNG/JPEG file, never a raw width×height×3 buffer.
        var isJpeg = imageBytes.Length >= 2 && imageBytes[0] == 0xFF && imageBytes[1] == 0xD8;
        var isPng = imageBytes.Length >= 8 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50
                    && imageBytes[2] == 0x4E && imageBytes[3] == 0x47;
        foreach (var page in _document.Pages)
        {
            ImageStamp stamp;
            if (isJpeg) stamp = ImageStamp.FromJpeg(imageBytes);
            else if (isPng) stamp = ImageStamp.FromPngData(imageBytes);
            else if (((OperatingSystem.IsWindows() ? ImageStamp.TryFromGdiPlusDecoder(imageBytes) : null)
                     ?? ImageStamp.TryFromManagedDecoder(imageBytes)) is { } gdiStamp)
                stamp = gdiStamp;
            else stamp = ImageStamp.FromRgb(imageBytes, 100, 100);
            var w = stamp.DisplayWidth > 0 ? stamp.DisplayWidth : 100;
            var h = stamp.DisplayHeight > 0 ? stamp.DisplayHeight : 100;
            var pageW = page.MediaBox.Width;
            var pageH = page.MediaBox.Height;
            // Centre within the [leftMargin, pageW-rightMargin] span; footer box bottom at
            // the margin, header box top at pageH-margin.
            stamp.X = leftMargin + (pageW - leftMargin - rightMargin - w) / 2;
            stamp.Y = top ? (pageH - h - primaryMargin) : primaryMargin;
            stamp.MetaRect = new Rectangle(stamp.X, stamp.Y, stamp.X + w, stamp.Y + h);
            stamp.ApplyTo(page);
        }
    }

    private TextStamp BuildTextStamp(string text, FormattedText? source)
    {
        var stamp = new TextStamp(text) { StampId = StampId, NameFormAfterExistingXObjects = true };
        if (source is not null)
        {
            // TextState is the effective source of font/size at render time (its
            // defaults win over the bare stamp properties), so the FormattedText's
            // size and font must land there too, not only on FontSize/FontName.
            stamp.FontSize = (float)source.FontSize;
            stamp.TextState.FontSize = (float)source.FontSize;
            if (!string.IsNullOrEmpty(source.FontName))
            {
                stamp.FontName = source.FontName;
                stamp.TextState.FontName = source.FontName;
            }
            if (source.ForegroundColor is not null)
                stamp.Color = source.ForegroundColor;
        }
        return stamp;
    }

    private static string RenderPageNumberText(string formatString, int n, int total, NumberingStyle style)
    {
        var numStr = style switch
        {
            NumberingStyle.LowerAlpha => ToAlpha(n, false),
            NumberingStyle.UpperAlpha => ToAlpha(n, true),
            NumberingStyle.LowerRoman => ToRoman(n).ToLowerInvariant(),
            NumberingStyle.UpperRoman => ToRoman(n),
            NumberingStyle.None => "",
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        try
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, formatString, numStr, total);
        }
        catch (FormatException)
        {
            return numStr;
        }
    }

    private static string ToAlpha(int n, bool upper)
    {
        if (n <= 0) return "";
        var sb = new StringBuilder();
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)((upper ? 'A' : 'a') + (n % 26)));
            n /= 26;
        }
        return sb.ToString();
    }

    private static string ToRoman(int n)
    {
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var letters = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var sb = new StringBuilder();
        for (var i = 0; i < values.Length && n > 0; i++)
            while (n >= values[i]) { sb.Append(letters[i]); n -= values[i]; }
        return sb.ToString();
    }

    /// <summary>
    /// Add a text stamp to all pages.
    /// </summary>
    public byte[] AddTextStamp(byte[] input, string text,
        HorizontalAlignment hAlign = HorizontalAlignment.Center,
        VerticalAlignment vAlign = VerticalAlignment.Bottom,
        double fontSize = 12, string fontName = "Helvetica")
    {
        using var doc = Document.Open(input);
        var stamp = new TextStamp(text)
        {
            FontSize = (float)fontSize,
            FontName = fontName,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add page numbers to all pages.
    /// </summary>
    public byte[] AddPageNumbers(byte[] input,
        string format = "Page {0} of {1}",
        HorizontalAlignment hAlign = HorizontalAlignment.Center,
        VerticalAlignment vAlign = VerticalAlignment.Bottom,
        double fontSize = 10)
    {
        using var doc = Document.Open(input);
        var stamp = new PageNumberStamp
        {
            Format = format,
            FontSize = fontSize,
            HorizontalAlignment = hAlign,
            VerticalAlignment = vAlign,
        };
        stamp.ApplyToAll(doc);
        return doc.ToArray();
    }

    /// <summary>
    /// Add a header text to all pages.
    /// </summary>
    public byte[] AddHeader(byte[] input, string text,
        double fontSize = 10, double margin = 36)
    {
        using var doc = Document.Open(input);
        var stamp = new TextStamp(text)
        {
            FontSize = (float)fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            YIndent = margin,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add a footer text to all pages.
    /// </summary>
    public byte[] AddFooter(byte[] input, string text,
        double fontSize = 10, double margin = 36)
    {
        using var doc = Document.Open(input);
        var stamp = new TextStamp(text)
        {
            FontSize = (float)fontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            YIndent = margin,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add a watermark text to all pages.
    /// </summary>
    public byte[] AddWatermark(byte[] input, string text,
        double fontSize = 48, double rotation = 45, double opacity = 0.3)
    {
        using var doc = Document.Open(input);
        var stamp = new WatermarkStamp(text)
        {
            FontSize = fontSize,
            Rotate = rotation,
            Opacity = opacity,
        };

        foreach (var page in doc.Pages)
            page.AddStamp(stamp);

        return doc.ToArray();
    }

    /// <summary>
    /// Add an RGB image stamp to all pages.
    /// </summary>
    public byte[] AddImageStamp(byte[] input, byte[] rgbPixels, int width, int height,
        double displayWidth = 0, double displayHeight = 0,
        double x = 100, double y = 100)
    {
        using var doc = Document.Open(input);
        var stamp = ImageStamp.FromRgb(rgbPixels, width, height);
        stamp.X = x;
        stamp.Y = y;
        stamp.DisplayWidth = displayWidth > 0 ? displayWidth : width;
        stamp.DisplayHeight = displayHeight > 0 ? displayHeight : height;

        foreach (var page in doc.Pages)
            stamp.ApplyTo(page);

        return doc.ToArray();
    }

    /// <summary>
    /// Add a grayscale image stamp to all pages.
    /// </summary>
    public byte[] AddGrayscaleImageStamp(byte[] input, byte[] grayPixels, int width, int height,
        double displayWidth = 0, double displayHeight = 0,
        double x = 100, double y = 100)
    {
        using var doc = Document.Open(input);
        var stamp = ImageStamp.FromGrayscale(grayPixels, width, height);
        stamp.X = x;
        stamp.Y = y;
        stamp.DisplayWidth = displayWidth > 0 ? displayWidth : width;
        stamp.DisplayHeight = displayHeight > 0 ? displayHeight : height;

        foreach (var page in doc.Pages)
            stamp.ApplyTo(page);

        return doc.ToArray();
    }
}
