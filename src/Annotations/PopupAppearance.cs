using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// Synthesises the on-screen appearance of an open /Popup annotation — the note
/// box that shows the parent markup annotation's author, modification date and
/// text (PDF 32000-1:2008 §12.5.6.14). Popups carry no /AP of their own (they are
/// interactive UI), so a renderer that wants to bake the open comment into a page
/// image must generate one. Closed popups draw nothing.
/// </summary>
internal static class PopupAppearance
{
    /// <summary>
    /// Build a Form XObject for an open /Popup, or return null when the popup is
    /// closed, has no rectangle, or carries no comment text/title to show.
    /// </summary>
    public static PdfStream? BuildOpenPopupForm(PdfDictionary popup, PdfReader reader)
    {
        if (!popup.GetBool("Open")) return null;

        // The visible text lives on the parent markup annotation; fall back to the
        // popup itself for the rare file that stores the comment inline.
        var source = reader.ResolveDict(popup.Get("Parent")) ?? popup;

        var title = (reader.Resolve(source.Get("T")) as PdfString)?.ToText() ?? "";
        var contents = (reader.Resolve(source.Get("Contents")) as PdfString)?.ToText() ?? "";
        var date = FormatDate((reader.Resolve(source.Get("M")) as PdfString)?.ToText());
        if (title.Length == 0 && contents.Length == 0) return null;

        if (reader.Resolve(popup.Get("Rect")) is not PdfArray rect || rect.Count < 4) return null;
        double rx1 = Num(rect[0]), ry1 = Num(rect[1]), rx2 = Num(rect[2]), ry2 = Num(rect[3]);
        double w = System.Math.Abs(rx2 - rx1), h = System.Math.Abs(ry2 - ry1);
        if (w <= 1 || h <= 1) return null;

        // Background is a light tint of the annotation colour (Acrobat lightens /C to
        // ~85% white); the border and separator use the full colour. Default to the
        // classic sticky-note yellow when no colour is set.
        var (cr, cg, cb) = ReadColor(reader, source.Get("C")) ?? (1.0, 1.0, 0.0);
        double Tint(double c) => c + (1.0 - c) * 0.85;
        string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string bg = $"{Fmt(Tint(cr))} {Fmt(Tint(cg))} {Fmt(Tint(cb))}";
        string fg = $"{Fmt(cr)} {Fmt(cg)} {Fmt(cb)}";

        const double pad = 5;
        const double fs = 10;
        const double leading = fs * 1.2;

        var helv = StandardFont("Helvetica");
        var helvBold = StandardFont("Helvetica-Bold");
        var metrics = Aspose.Pdf.Text.FontMetrics.FromFontDict(helv, reader);
        double avail = System.Math.Max(1.0, w - 2 * pad);

        var sb = new StringBuilder();
        // Box background + border (inset 0.5 so the stroke stays inside the BBox).
        sb.Append(bg).Append(" rg\n0 0 ").Append(Fmt(w)).Append(' ').Append(Fmt(h)).Append(" re f\n");
        sb.Append(fg).Append(" RG\n1 w\n0.5 0.5 ").Append(Fmt(w - 1)).Append(' ').Append(Fmt(h - 1)).Append(" re S\n");

        double y = h - pad - fs;
        sb.Append("BT\n0 0 0 rg\n");
        // Title bar: author (bold) then date.
        if (title.Length > 0)
        {
            sb.Append("/HB ").Append(Fmt(fs)).Append(" Tf\n");
            sb.Append(Fmt(pad)).Append(' ').Append(Fmt(y)).Append(" Td (").Append(Escape(title)).Append(") Tj\n");
            sb.Append("0 ").Append(Fmt(-leading)).Append(" Td\n");
            y -= leading;
        }
        else sb.Append(Fmt(pad)).Append(' ').Append(Fmt(y)).Append(" Td\n");

        if (date.Length > 0)
        {
            sb.Append("/HF ").Append(Fmt(fs)).Append(" Tf (").Append(Escape(date)).Append(") Tj\n");
            sb.Append("0 ").Append(Fmt(-leading)).Append(" Td\n");
            y -= leading;
        }
        sb.Append("ET\n");

        // Separator line between the title bar and the comment text.
        double sepY = y - 2;
        sb.Append(fg).Append(" RG\n0.5 w\n").Append(Fmt(pad)).Append(' ').Append(Fmt(sepY))
          .Append(" m ").Append(Fmt(w - pad)).Append(' ').Append(Fmt(sepY)).Append(" l S\n");

        // Comment body, word-wrapped to the box width.
        if (contents.Length > 0)
        {
            var lines = Wrap(contents, metrics, fs, avail);
            sb.Append("BT\n0 0 0 rg\n/HF ").Append(Fmt(fs)).Append(" Tf\n").Append(Fmt(leading)).Append(" TL\n");
            sb.Append(Fmt(pad)).Append(' ').Append(Fmt(sepY - 2 - fs)).Append(" Td\n");
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) sb.Append("T*\n");
                sb.Append('(').Append(Escape(lines[i])).Append(") Tj\n");
            }
            sb.Append("ET\n");
        }

        var form = new PdfStream(new PdfDictionary(), Encoding.Latin1.GetBytes(sb.ToString()));
        form.Dict.Set("Type", new PdfName("XObject"));
        form.Dict.Set("Subtype", new PdfName("Form"));
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(0)); bbox.Add(new PdfReal(0));
        bbox.Add(new PdfReal(w)); bbox.Add(new PdfReal(h));
        form.Dict.Set("BBox", bbox);
        var fonts = new PdfDictionary();
        fonts.Set("HF", helv);
        fonts.Set("HB", helvBold);
        var res = new PdfDictionary();
        res.Set("Font", fonts);
        form.Dict.Set("Resources", res);
        return form;
    }

    private static PdfDictionary StandardFont(string baseFont)
    {
        var f = new PdfDictionary();
        f.Set("Type", new PdfName("Font"));
        f.Set("Subtype", new PdfName("Type1"));
        f.Set("BaseFont", new PdfName(baseFont));
        f.Set("Encoding", new PdfName("WinAnsiEncoding"));
        return f;
    }

    private static (double r, double g, double b)? ReadColor(PdfReader reader, PdfObject? o)
    {
        if (reader.Resolve(o) is not PdfArray a) return null;
        switch (a.Count)
        {
            case 1: { double v = Num(a[0]); return (v, v, v); }
            case 3: return (Num(a[0]), Num(a[1]), Num(a[2]));
            case 4:
                double c = Num(a[0]), m = Num(a[1]), yv = Num(a[2]), k = Num(a[3]);
                return ((1 - c) * (1 - k), (1 - m) * (1 - k), (1 - yv) * (1 - k));
            default: return null;
        }
    }

    private static double Num(PdfObject? o) => o switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => 0,
    };

    /// <summary>Format a PDF date string (D:YYYYMMDDHHmmSS…) for display. The literal
    /// digits are shown without timezone conversion; an unparseable value is returned
    /// verbatim.</summary>
    private static string FormatDate(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        var s = raw!.StartsWith("D:") ? raw.Substring(2) : raw;
        if (s.Length < 8) return raw!;
        int Get(int off, int len, int def) =>
            s.Length >= off + len && int.TryParse(s.Substring(off, len), out var v) ? v : def;
        int year = Get(0, 4, 0), mon = Get(4, 2, 1), day = Get(6, 2, 1);
        int hh = Get(8, 2, 0), mm = Get(10, 2, 0), ss = Get(12, 2, 0);
        if (year == 0) return raw!;
        string ap = hh < 12 ? "AM" : "PM";
        int h12 = hh % 12; if (h12 == 0) h12 = 12;
        return $"{mon}/{day}/{year} {h12}:{mm:00}:{ss:00} {ap}";
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");

    private static System.Collections.Generic.List<string> Wrap(
        string text, Aspose.Pdf.Text.FontMetrics metrics, double fontSize, double avail)
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var words = rawLine.Split(' ');
            var current = "";
            foreach (var word in words)
            {
                if (current.Length == 0) { current = word; continue; }
                var candidate = current + " " + word;
                if (metrics.MeasureString(candidate, fontSize) <= avail) current = candidate;
                else { result.Add(current); current = word; }
            }
            result.Add(current);
        }
        return result;
    }
}
