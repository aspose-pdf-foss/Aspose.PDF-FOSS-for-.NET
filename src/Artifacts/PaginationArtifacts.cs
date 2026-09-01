using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>Which pages of a document a pagination artifact applies to.</summary>
public enum Subset
{
    /// <summary>Every page in the range.</summary>
    All,
    /// <summary>Odd page numbers only.</summary>
    Odd,
    /// <summary>Even page numbers only.</summary>
    Even,
}

/// <summary>
/// Base class for artifacts stamped across a page range by
/// <see cref="PageCollectionExtensions.AddPagination"/> — e.g. Bates numbering.
/// The running number counts only the pages the artifact is actually applied
/// to (range and <see cref="Subset"/> filtered).
/// </summary>
public abstract class PaginationArtifact
{
    /// <summary>Horizontal placement of the stamped text.</summary>
    public HorizontalAlignment ArtifactHorizontalAlignment { get; set; } = HorizontalAlignment.Right;

    /// <summary>Vertical placement of the stamped text.</summary>
    public VerticalAlignment ArtifactVerticalAlignment { get; set; } = VerticalAlignment.Bottom;

    /// <summary>Which pages inside the range receive the artifact.</summary>
    public Subset Subset { get; set; } = Subset.All;

    /// <summary>First page (1-based) that receives the artifact. Values below 1 mean "from the first page".</summary>
    public int StartPage { get; set; } = 1;

    /// <summary>Last page (1-based) that receives the artifact. Values below 1 mean "to the last page".</summary>
    public int EndPage { get; set; }

    /// <summary>Text styling for the stamped number (font, size, colour).</summary>
    public TextState TextState { get; set; } = new();

    /// <summary>The text stamped for running number <paramref name="number"/>.</summary>
    internal abstract string FormatText(int number);

    /// <summary>The first running number. Values below 1 clamp to 1.</summary>
    internal virtual int FirstNumber => 1;
}

/// <summary>A Bates-numbering pagination artifact: a zero-padded running number
/// with optional prefix and suffix.</summary>
public sealed class BatesNArtifact : PaginationArtifact
{
    /// <summary>Text prepended to the number.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Text appended after the number.</summary>
    public string Suffix { get; set; } = string.Empty;

    /// <summary>The number stamped on the first included page. Values below 1 clamp to 1.</summary>
    public int StartNumber { get; set; } = 1;

    /// <summary>Zero-padded width of the number. Default 6 ("000001"); the effective
    /// width clamps to 3–15.</summary>
    public int NumberOfDigits { get; set; } = 6;

    internal override int FirstNumber => Math.Max(1, StartNumber);

    internal override string FormatText(int number)
        => Prefix + number.ToString("D" + Math.Clamp(NumberOfDigits, 3, 15), CultureInfo.InvariantCulture) + Suffix;
}

/// <summary>Pagination stamping over a <see cref="PageCollection"/> (Bates numbering etc.).</summary>
public static class PageCollectionExtensions
{
    /// <summary>Stamp each artifact in <paramref name="artifacts"/> across the collection.
    /// Every included page receives a Pagination artifact whose running number counts
    /// only the included pages.</summary>
    public static void AddPagination(this PageCollection pages, List<PaginationArtifact> artifacts)
    {
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        if (artifacts is null) throw new ArgumentNullException(nameof(artifacts));
        foreach (var spec in artifacts)
        {
            var number = spec.FirstNumber;
            var first = Math.Max(1, spec.StartPage);
            foreach (var page in pages)
            {
                if (page.Number < first) continue;
                if (spec.EndPage >= 1 && page.Number > spec.EndPage) continue;
                if (spec.Subset == Subset.Odd && page.Number % 2 == 0) continue;
                if (spec.Subset == Subset.Even && page.Number % 2 != 0) continue;

                var artifact = new Artifact(Artifact.ArtifactType.Pagination, Artifact.ArtifactSubtype.BatesN)
                {
                    TextState = spec.TextState,
                    Position = ComputePosition(page, spec, spec.FormatText(number)),
                };
                artifact.SetTextAndState(spec.FormatText(number), spec.TextState);
                page.Artifacts.Add(artifact);
                number++;
            }
        }
    }

    /// <summary>Stamp Bates numbering configured by <paramref name="configure"/> across the collection.</summary>
    public static void AddBatesNumbering(this PageCollection pages, Action<BatesNArtifact> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var bates = new BatesNArtifact();
        configure(bates);
        AddPagination(pages, new List<PaginationArtifact> { bates });
    }

    /// <summary>Stamp the given Bates-numbering artifact across the collection.</summary>
    public static void AddBatesNumbering(this PageCollection pages, BatesNArtifact bates)
    {
        if (bates is null) throw new ArgumentNullException(nameof(bates));
        AddPagination(pages, new List<PaginationArtifact> { bates });
    }

    /// <summary>Remove every Bates-numbering artifact from every page.</summary>
    public static void DeleteBatesNumbering(this PageCollection pages)
    {
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        foreach (var page in pages)
        {
            for (var i = page.Artifacts.Count; i >= 1; i--)
            {
                if (page.Artifacts[i].Subtype == Artifact.ArtifactSubtype.BatesN)
                    page.Artifacts.Delete(i);
            }
        }
    }

    /// <summary>
    /// Re-stamp the document's header/footer pagination from the settings the
    /// file itself carries. PDFs whose running heads were produced by the
    /// Acrobat-compatible header/footer machinery store their configuration as
    /// a <c>HeaderFooterSettings</c> XML inside each header Form XObject's
    /// <c>/PieceInfo /ADBE_CompoundType /DocSettings</c> entry. This method
    /// finds those settings, strips every existing pagination header/footer
    /// artifact, and stamps fresh ones across the current page sequence — so
    /// page numbers, "Page N of M" totals and dates come out right after pages
    /// were inserted, copied or added. No-op when no settings are stored.
    /// </summary>
    public static void UpdatePagination(this PageCollection pages)
    {
        if (pages is null) throw new ArgumentNullException(nameof(pages));
        var settings = HeaderFooterSettings.FindIn(pages);
        if (settings is null) return;

        foreach (var page in pages)
        {
            // The enumerator snapshots, so Delete inside the loop is safe.
            foreach (var artifact in page.Artifacts)
            {
                if (artifact.Type == Artifact.ArtifactType.Pagination &&
                    artifact.Subtype is Artifact.ArtifactSubtype.Header or Artifact.ArtifactSubtype.Footer)
                    page.Artifacts.Delete(artifact);
            }
        }

        // Re-stamp with the face the document's headers already use: the
        // existing header forms carry the full font object (descriptor and
        // program), so referencing it keeps the new headers metric-identical
        // to the originals and independent of locally installed fonts. Only a
        // document with no reusable header font falls back to embedding the
        // named face from the system.
        var sharedFontEntry = settings.FontEntry;
        var sharedFont = settings.FontDict;
        var owner = pages.OwnerDocument;
        if (sharedFont is null && owner is not null)
        {
            try
            {
                var ttf = Text.FontRepository.GetTtfData(settings.FontName);
                if (ttf is { Length: > 0 })
                {
                    sharedFont = new PdfDictionary();
                    Text.FontEmbedder.EmbedIntoFontDict(owner, ttf, sharedFont,
                        settings.FontName.Replace(" ", ""), subset: false);
                    sharedFontEntry = sharedFont;
                }
            }
            catch { sharedFont = null; sharedFontEntry = null; }
        }

        var total = pages.Count;
        var now = DateTime.Now;
        foreach (var page in pages)
        {
            if (!settings.Includes(page.Number, total)) continue;
            foreach (var slot in settings.Slots)
            {
                var text = settings.Compose(slot.Content, page.Number, total, now);
                if (string.IsNullOrEmpty(text)) continue;
                EmitHeaderFooter(page, settings, slot, text, sharedFontEntry, sharedFont);
            }
        }
    }

    // Geometry constants measured from Acrobat-produced header/footer forms
    // (8pt Arial fixtures): the text band's form BBox spans fontSize below its
    // origin and 0.100006·fontSize above it, and the baseline drops
    // 0.7895·fontSize from the origin (6.316pt at 8pt).
    private const double HeaderFooterBBoxAscent = 0.100006;
    private const double HeaderFooterBaselineDrop = 0.7895;

    /// <summary>Stamp one composed header/footer text onto a page in the
    /// Acrobat shape: a Form XObject (carrying the settings XML in its
    /// /PieceInfo) drawn inside an /Artifact /Type /Pagination block.</summary>
    private static void EmitHeaderFooter(Page page, HeaderFooterSettings settings, HeaderFooterSlot slot,
        string text, PdfObject? sharedFontEntry, PdfDictionary? sharedFont)
    {
        var fs = settings.FontSize;
        var fontName = sharedFontEntry is not null && sharedFont is not null
            ? RegisterFontDict(page, sharedFontEntry, sharedFont)
            : Aspose.Pdf.Table.RegisterFont(page, settings.FontName);
        var width = MeasureStandardWidth(text, "Helvetica", fs);

        var x = slot.Horizontal switch
        {
            HorizontalAlignment.Left => settings.MarginLeft,
            HorizontalAlignment.Right => page.Width - settings.MarginRight - width,
            _ => (page.Width - width) / 2,
        };
        var y = slot.IsHeader
            ? page.Height - settings.MarginTop + fs
            : settings.MarginBottom - HeaderFooterBBoxAscent * fs;

        var inner = new StringBuilder();
        inner.Append("0 g 0 G 0 i 0 J []0 d 0 j 1 w 10 M 0 Tc 0 Tw 100 Tz 0 TL 0 Tr 0 Ts\n");
        inner.Append("BT\n");
        inner.Append($"/{fontName} {F(fs)} Tf\n");
        inner.Append(settings.Red == 0 && settings.Green == 0 && settings.Blue == 0
            ? "0 g\n"
            : $"{F(settings.Red)} {F(settings.Green)} {F(settings.Blue)} rg\n");
        inner.Append($"0 {F(-HeaderFooterBaselineDrop * fs)} Td\n");
        inner.Append($"({EscapeLiteral(text)}) Tj\n");
        inner.Append("ET\n");

        var formName = page.AddStampForm(Encoding.ASCII.GetBytes(inner.ToString()),
            new Rectangle(0, -fs, width, HeaderFooterBBoxAscent * fs));
        AttachDocSettings(page, formName, settings.RawXml);

        var block = new StringBuilder();
        block.Append($"/Artifact <</Contents ({EscapeLiteral(text)})/Subtype /Header /Type /Pagination >>BDC \n");
        block.Append("q\n");
        block.Append($"1 0 0 1 {F(x)} {F(y)} cm\n");
        block.Append($"/{formName} Do\n");
        block.Append("Q\n");
        block.Append("EMC\n");
        page.AddContentStream(Encoding.ASCII.GetBytes(block.ToString()));
    }

    /// <summary>Register a font on the page's /Font resources by its raw entry
    /// (an indirect reference stays indirect), reusing the entry when this
    /// exact font dictionary is registered there already.</summary>
    private static string RegisterFontDict(Page page, PdfObject fontEntry, PdfDictionary fontDict)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            page.Dict.Set("Resources", resources);
        }
        var fonts = page.Reader.ResolveDict(resources.Get("Font"));
        if (fonts is null)
        {
            fonts = new PdfDictionary();
            resources.Set("Font", fonts);
        }
        foreach (var key in fonts.Keys)
        {
            if (ReferenceEquals(page.Reader.Resolve(fonts.Get(key)), fontDict))
                return key;
        }
        var name = "F1";
        var counter = 1;
        while (fonts.ContainsKey(name)) name = $"F{++counter}";
        fonts.Set(name, fontEntry);
        return name;
    }

    /// <summary>Store the header/footer settings XML on the freshly registered
    /// form under /PieceInfo /ADBE_CompoundType /DocSettings, so the document
    /// stays re-paginatable (by this method and by Acrobat alike).</summary>
    private static void AttachDocSettings(Page page, string formName, byte[] rawXml)
    {
        var resources = page.Reader.ResolveDict(page.Dict.Get("Resources"));
        var xobjects = page.Reader.Resolve(resources?.Get("XObject")) as PdfDictionary;
        var form = xobjects is null ? null : page.Reader.ResolveStream(xobjects.Get(formName));
        if (form is null) return;

        var adbe = new PdfDictionary();
        adbe.Set("Private", new PdfName("Header"));
        adbe.Set("LastModified", new PdfString(Encoding.ASCII.GetBytes(
            "D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture))));
        adbe.Set("DocSettings", new PdfStream(new PdfDictionary(), rawXml));
        var pieceInfo = new PdfDictionary();
        pieceInfo.Set("ADBE_CompoundType", adbe);
        form.Dict.Set("PieceInfo", pieceInfo);
    }

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string EscapeLiteral(string s) =>
        s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    /// <summary>Advance width of <paramref name="text"/> in points using
    /// Standard-14 AFM metrics (Arial shares Helvetica's widths).</summary>
    internal static double MeasureStandardWidth(string text, string measureFont, double fontSize)
    {
        double sum = 0;
        foreach (var ch in text)
        {
            var w = ch <= 255 ? Standard14Fonts.GetWidth(measureFont, ch) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth(measureFont);
            sum += w;
        }
        return sum * fontSize / 1000.0;
    }

    /// <summary>Explicit stamp position on the default A4 page: 72pt side
    /// margins; top text sits at pageHeight − 36; bottom text at 36 − 1.12·fontSize
    /// (24.8 for the default 10pt state).</summary>
    private static Point ComputePosition(Page page, PaginationArtifact spec, string text)
    {
        double fs = spec.TextState.FontSize > 0 ? spec.TextState.FontSize : 10;
        var measureFont = spec.TextState.FontName is { } fn && Standard14Fonts.IsStandard14(fn)
            ? fn
            : "Helvetica"; // Arial etc. share Helvetica-class digit metrics
        double textW = 0;
        foreach (var ch in text)
        {
            var w = ch <= 255 ? Standard14Fonts.GetWidth(measureFont, ch) : 0;
            if (w <= 0) w = Standard14Fonts.GetDefaultWidth(measureFont);
            textW += w;
        }
        textW = textW * fs / 1000.0;

        double x = spec.ArtifactHorizontalAlignment switch
        {
            HorizontalAlignment.Left => 72,
            HorizontalAlignment.Right => page.Width - textW - 72,
            _ => (page.Width - textW) / 2,
        };
        double y = spec.ArtifactVerticalAlignment switch
        {
            VerticalAlignment.Top => page.Height - 36,
            VerticalAlignment.Bottom => 36 - 1.12 * fs,
            _ => (page.Height - fs) / 2,
        };
        return new Point(x, y);
    }
}

/// <summary>One header/footer band position: header or footer, left/center/right,
/// with the template content composing its text.</summary>
internal sealed record HeaderFooterSlot(bool IsHeader, HorizontalAlignment Horizontal, XElement Content);

/// <summary>
/// The Acrobat-compatible <c>HeaderFooterSettings</c> XML a paginated document
/// carries inside its header Form XObjects
/// (<c>/PieceInfo /ADBE_CompoundType /DocSettings</c>): font, margins, page
/// range/parity, and a text template per band position. Templates mix literal
/// text with <c>&lt;Page&gt;</c> (a <c>&lt;PageIndex&gt;</c> /
/// <c>&lt;PageTotalNum&gt;</c> group shifted by <c>offset</c>) and
/// <c>&lt;Date&gt;</c> (<c>&lt;Month&gt;</c>/<c>&lt;Day&gt;</c>/<c>&lt;Year&gt;</c>
/// with per-part zero-pad formats) elements.
/// </summary>
internal sealed class HeaderFooterSettings
{
    public string FontName { get; private init; } = "Arial";
    public double FontSize { get; private init; } = 8;
    public double Red { get; private init; }
    public double Green { get; private init; }
    public double Blue { get; private init; }
    public double MarginLeft { get; private init; } = 72;
    public double MarginRight { get; private init; } = 72;
    public double MarginTop { get; private init; } = 36;
    public double MarginBottom { get; private init; } = 36;
    private int _rangeStart = -1;
    private int _rangeEnd = -1;
    private bool _even = true;
    private bool _odd = true;
    public byte[] RawXml { get; private init; } = Array.Empty<byte>();
    public List<HeaderFooterSlot> Slots { get; } = new();

    /// <summary>The font object the existing headers draw with (the raw resource
    /// entry, kept indirect when it is one) and its resolved dictionary. Reusing
    /// the document's own face keeps the re-stamped headers metric-identical to
    /// the originals and independent of the fonts installed on this machine.</summary>
    public PdfObject? FontEntry { get; private set; }
    public PdfDictionary? FontDict { get; private set; }

    /// <summary>Locate the stored settings: the first page (in order) whose
    /// resource XObjects carry an ADBE_CompoundType DocSettings entry supplies
    /// them. Null when the document has no stored pagination settings.</summary>
    public static HeaderFooterSettings? FindIn(PageCollection pages)
    {
        foreach (var page in pages)
        {
            var reader = page.Reader;
            var resources = reader.ResolveDict(page.Dict.Get("Resources"));
            var xobjects = reader.ResolveDict(resources?.Get("XObject"));
            if (xobjects is null) continue;
            foreach (var key in xobjects.Keys)
            {
                var stream = reader.ResolveStream(xobjects.Get(key));
                var pieceInfo = reader.ResolveDict(stream?.Dict.Get("PieceInfo"));
                var adbe = reader.ResolveDict(pieceInfo?.Get("ADBE_CompoundType"));
                var docSettings = adbe is null ? null : reader.ResolveStream(adbe.Get("DocSettings"));
                if (docSettings is null) continue;
                var xml = reader.DecodeStream(docSettings);
                var parsed = TryParse(xml);
                if (parsed is null) continue;

                // The header form's own /Font resource carries the face the
                // original headers were drawn with — prefer the entry whose
                // BaseFont matches the settings' font name.
                var formFonts = reader.ResolveDict(
                    reader.ResolveDict(stream!.Dict.Get("Resources"))?.Get("Font"));
                if (formFonts is not null)
                {
                    foreach (var fontKey in formFonts.Keys)
                    {
                        var entry = formFonts.Get(fontKey);
                        if (reader.Resolve(entry) is not PdfDictionary fontDict) continue;
                        var baseFont = fontDict.GetName("BaseFont");
                        var isMatch = baseFont is not null &&
                            baseFont.Contains(parsed.FontName.Replace(" ", ""), StringComparison.Ordinal);
                        if (parsed.FontDict is null || isMatch)
                        {
                            parsed.FontEntry = entry;
                            parsed.FontDict = fontDict;
                        }
                        if (isMatch) break;
                    }
                }
                return parsed;
            }
        }
        return null;
    }

    private static HeaderFooterSettings? TryParse(byte[] xmlBytes)
    {
        XElement root;
        try
        {
            var text = Encoding.UTF8.GetString(xmlBytes);
            root = XDocument.Parse(text.TrimStart('﻿')).Root
                ?? throw new InvalidOperationException();
        }
        catch
        {
            return null;
        }
        if (root.Name.LocalName != "HeaderFooterSettings") return null;

        var font = root.Element("Font");
        var color = root.Element("Color");
        var margin = root.Element("Margin");
        var range = root.Element("PageRange");
        var settings = new HeaderFooterSettings
        {
            FontName = (string?)font?.Attribute("name") ?? "Arial",
            FontSize = Attr(font, "size", 8),
            Red = Attr(color, "r", 0),
            Green = Attr(color, "g", 0),
            Blue = Attr(color, "b", 0),
            MarginLeft = Attr(margin, "left", 72),
            MarginRight = Attr(margin, "right", 72),
            MarginTop = Attr(margin, "top", 36),
            MarginBottom = Attr(margin, "bottom", 36),
            RawXml = xmlBytes,
            _rangeStart = (int)Attr(range, "start", -1),
            _rangeEnd = (int)Attr(range, "end", -1),
            _even = (int)Attr(range, "even", 1) != 0,
            _odd = (int)Attr(range, "odd", 1) != 0,
        };

        foreach (var (band, isHeader) in new[] { ("Header", true), ("Footer", false) })
        {
            var bandEl = root.Element(band);
            if (bandEl is null) continue;
            foreach (var (pos, align) in new[]
            {
                ("Left", HorizontalAlignment.Left),
                ("Center", HorizontalAlignment.Center),
                ("Right", HorizontalAlignment.Right),
            })
            {
                var slotEl = bandEl.Element(pos);
                if (slotEl is null || !slotEl.Nodes().Any()) continue;
                settings.Slots.Add(new HeaderFooterSlot(isHeader, align, slotEl));
            }
        }
        return settings;
    }

    private static double Attr(XElement? el, string name, double fallback)
        => el?.Attribute(name) is { } a
           && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : fallback;

    /// <summary>Whether page <paramref name="pageNumber"/> (1-based) receives
    /// the artifacts under the stored range and parity filters.</summary>
    public bool Includes(int pageNumber, int totalPages)
    {
        var first = _rangeStart < 1 ? 1 : _rangeStart;
        var last = _rangeEnd < 1 ? totalPages : Math.Min(_rangeEnd, totalPages);
        if (pageNumber < first || pageNumber > last) return false;
        return pageNumber % 2 == 0 ? _even : _odd;
    }

    /// <summary>Compose a slot template into the text stamped on
    /// <paramref name="pageNumber"/> of <paramref name="totalPages"/>.</summary>
    public string Compose(XElement slot, int pageNumber, int totalPages, DateTime now)
    {
        var parts = new List<string>();
        foreach (var node in slot.Nodes())
        {
            switch (node)
            {
                case XText t:
                    parts.Add(t.Value);
                    break;
                case XElement { Name.LocalName: "Page" } pageEl:
                {
                    var offset = (int)Attr(pageEl, "offset", 0);
                    var inner = new List<string>();
                    foreach (var child in pageEl.Nodes())
                    {
                        switch (child)
                        {
                            case XText it: inner.Add(it.Value); break;
                            case XElement { Name.LocalName: "PageIndex" } idx:
                                inner.Add(FormatNumber(pageNumber + offset, (string?)idx.Attribute("format")));
                                break;
                            case XElement { Name.LocalName: "PageTotalNum" } tot:
                                inner.Add(FormatNumber(totalPages + offset, (string?)tot.Attribute("format")));
                                break;
                        }
                    }
                    parts.Add(JoinParts(inner));
                    break;
                }
                case XElement { Name.LocalName: "Date" } dateEl:
                {
                    var inner = new List<string>();
                    foreach (var child in dateEl.Nodes())
                    {
                        switch (child)
                        {
                            case XText it: inner.Add(it.Value); break;
                            case XElement { Name.LocalName: "Month" } m:
                                inner.Add(FormatNumber(now.Month, (string?)m.Attribute("format")));
                                break;
                            case XElement { Name.LocalName: "Day" } d:
                                inner.Add(FormatNumber(now.Day, (string?)d.Attribute("format")));
                                break;
                            case XElement { Name.LocalName: "Year" } y:
                                inner.Add(FormatYear(now.Year, (string?)y.Attribute("format")));
                                break;
                        }
                    }
                    parts.Add(JoinParts(inner));
                    break;
                }
            }
        }
        return JoinParts(parts);
    }

    /// <summary>Join template parts the way the original stamping machinery
    /// spaces them: a space separates two parts only where words meet
    /// (letter/digit on both sides of the seam) — "Page", "2", "of", "5"
    /// becomes "Page 2 of 5" while "1", "/", "4" stays "1/4".</summary>
    private static string JoinParts(List<string> parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            if (sb.Length > 0 && char.IsLetterOrDigit(sb[^1]) && char.IsLetterOrDigit(part[0]))
                sb.Append(' ');
            sb.Append(part);
        }
        return sb.ToString();
    }

    /// <summary>Format "2" zero-pads to two digits; everything else renders plain.</summary>
    private static string FormatNumber(int value, string? format)
        => format == "2"
            ? value.ToString("D2", CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Year format "0" omits the year, "2" keeps its last two digits,
    /// anything else renders all four.</summary>
    private static string FormatYear(int year, string? format) => format switch
    {
        "0" => string.Empty,
        "2" => (year % 100).ToString("D2", CultureInfo.InvariantCulture),
        _ => year.ToString(CultureInfo.InvariantCulture),
    };
}
