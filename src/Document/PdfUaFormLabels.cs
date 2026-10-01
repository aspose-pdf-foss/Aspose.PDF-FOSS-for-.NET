using Aspose.Pdf.Core;

namespace Aspose.Pdf;

public sealed partial class Document
{
    // A label longer than this is a sentence near the field, not its label.
    private const int MaxFieldLabelLength = 80;
    // A label shorter than this many letters is a stray fragment (an "X", a split word).
    private const int MinLabelLetters = 2;
    // How far from its field a label may sit, in multiples of the field's height.
    private const double LabelReachInHeights = 6;
    // A gap between two fragments of one label, in multiples of the field's height.
    private const double LabelWordGapInHeights = 1.5;
    // How far above its field a label line may sit, in multiples of the field's height.
    private const double LabelAboveInHeights = 1.5;
    // How far a label above may start from the field's left edge, in points.
    private const double LabelAboveIndent = 12;
    // How far a field may overlap the text beside it and still stand between two labels, in points.
    private const double FieldEdgeSlack = 2;
    // Field /Ff bit 17 of a button field: a push button (neither check box nor radio button).
    private const int PushButtonFlag = 1 << 16;
    // Annotation /F bit 2: hidden.
    private const int HiddenAnnotationFlag = 1 << 1;

    /// <summary>
    /// PDF/UA-1 clause 7.18.1: every form field says what it is for, in its /TU (the name
    /// assistive technology announces). A field without one takes the label a sighted reader
    /// reads beside it: the text on its own line to its left (to its right for a check box or
    /// radio button, whose label follows it), else the line just above it. Only a clear label
    /// is taken - short, close, not reaching across another field; a field without one is left
    /// for its author to describe.
    /// </summary>
    private void LabelFormFieldsForUa(PdfFormatConversionOptions options)
    {
        foreach (var page in Pages)
        {
            if (_reader.Resolve(page.Dict.Get("Annots")) is not PdfArray annots) continue;
            var widgets = new List<(PdfDictionary Widget, Rectangle Box)>();
            foreach (var entry in annots)
                if (_reader.ResolveDict(entry) is { } annot && annot.GetName("Subtype") == "Widget"
                    && _reader.Resolve(annot.Get("Rect")) is PdfArray { Count: 4 } rect
                    && (((_reader.Resolve(annot.Get("F")) as PdfInteger)?.Value ?? 0) & HiddenAnnotationFlag) == 0)
                    widgets.Add((annot, Normalized(Rectangle.FromPdfArray(rect))));
            if (widgets.Count == 0) continue;

            List<(Rectangle Box, string Text)>? pageText = null;
            foreach (var (widget, box) in widgets)
            {
                var field = TerminalField(widget);
                if (FieldChainHasTooltip(field)) continue;
                pageText ??= LabelPieces(page, widgets.Select(w => w.Box).ToList());
                var others = widgets.Where(w => !ReferenceEquals(w.Widget, widget)).Select(w => w.Box).ToList();
                var label = FieldLabel(box, IsCheckOrRadio(field), pageText, others) ?? ReadableFieldName(field);
                if (label is not null) field.Set("TU", Forms.Field.EncodePdfTextString(label));
                else
                    options.ConversionLog.Add(new Optimization.PdfAViolation
                    {
                        Rule = "FieldDescription",
                        Clause = "7.18.1",
                        Section = "Annotations",
                        PageNumber = page.Number,
                        Description = $"Form field '{FieldDisplayName(field)}' has no description (TU): it has no label beside it and no readable name, so the author has to describe it",
                        Convertable = false,
                    });
            }
        }
    }

    /// <summary>The page's text in pieces a label can be made of: a line of text is cut where
    /// its characters stand further apart than words do - where a field sits between them (a
    /// form line reads "Name [___] Date [___]" as one run of text), or a column gap.</summary>
    private static List<(Rectangle Box, string Text)> LabelPieces(Page page, List<Rectangle> fields)
    {
        var pieces = new List<(Rectangle Box, string Text)>();
        var absorber = new Text.TextFragmentAbsorber();
        try { page.Accept(absorber); }
        catch { return pieces; }

        foreach (Text.TextFragment fragment in absorber.TextFragments)
            foreach (Text.TextSegment segment in fragment.Segments)
            {
                var text = segment.Text ?? string.Empty;
                if (segment.Characters.Count != text.Length || text.Length == 0)
                {
                    if (segment.Rectangle is { } whole && !InsideAny(whole, fields) && text.Trim().Length > 0)
                        pieces.Add((whole, text));
                    continue;
                }
                Rectangle? box = null;
                var sb = new System.Text.StringBuilder();
                void Flush()
                {
                    if (box is not null && sb.ToString().Trim().Length > 0) pieces.Add((box, sb.ToString()));
                    box = null;
                    sb.Clear();
                }
                for (var i = 0; i < text.Length; i++)
                {
                    var r = Normalized(segment.Characters[i + 1].Rectangle);
                    var charHeight = Math.Max(1, r.URY - r.LLY);
                    // A space belongs to the text but not to its extent: trailing blanks would
                    // otherwise reach over the field that follows.
                    if (char.IsWhiteSpace(text[i]))
                    {
                        if (box is not null) sb.Append(text[i]);
                        continue;
                    }
                    if (box is not null && (r.LLX - box.URX > charHeight || FieldBetween(box, r, fields))) Flush();
                    box = box is null ? r : new Rectangle(Math.Min(box.LLX, r.LLX), Math.Min(box.LLY, r.LLY),
                        Math.Max(box.URX, r.URX), Math.Max(box.URY, r.URY));
                    sb.Append(text[i]);
                }
                Flush();
            }
        return pieces;
    }

    /// <summary>True when a field stands on the line between the text so far and the next
    /// character (a check box between two option labels).</summary>
    private static bool FieldBetween(Rectangle before, Rectangle next, List<Rectangle> fields) =>
        fields.Any(f => f.LLX >= before.URX - 1 && f.URX <= next.LLX + 1
            && f.LLY < next.URY && f.URY > next.LLY);

    /// <summary>The field's full name as a form shows it: its /T and its ancestors', joined by dots.</summary>
    private string FieldDisplayName(PdfDictionary field)
    {
        var parts = new List<string>();
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (var node = field; node is not null && seen.Add(node); node = _reader.ResolveDict(node.Get("Parent")))
            if (_reader.Resolve(node.Get("T")) is PdfString t) parts.Insert(0, t.ToText());
        return parts.Count > 0 ? string.Join(".", parts) : "(unnamed)";
    }

    // Names a form editor gives fields by itself: a type word and a counter.
    private static readonly System.Text.RegularExpressions.Regex GeneratedFieldName = new(
        @"^(text|text ?field|field|check ?box|radio|radio ?button|button|push ?button|combo ?box|list ?box|" +
        @"drop ?down|untitled|undefined|group)[\s_.-]*\d*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    // The fewest letters a field name needs to say anything.
    private const int MinNameLetters = 3;
    // The share of a field name that must be letters (the rest spaces and punctuation).
    private const double MinNameLetterShare = 0.7;

    /// <summary>The field's own name (/T) when an author wrote it in words ("Petitioner",
    /// "Your Full Name") - not a name the form editor made up ("Text1", "Check Box3") nor an
    /// identifier ("2206", "201s", "addr_line_1"); null otherwise.</summary>
    private string? ReadableFieldName(PdfDictionary field)
    {
        if (_reader.Resolve(field.Get("T")) is not PdfString t) return null;
        var name = string.Join(" ", t.ToText().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var letters = name.Count(char.IsLetter);
        if (letters < MinNameLetters || name.Contains('_') || GeneratedFieldName.IsMatch(name)) return null;
        var nonSpace = name.Count(c => !char.IsWhiteSpace(c));
        return letters >= nonSpace * MinNameLetterShare ? CleanLabel(name) : null;
    }

    private static Rectangle Normalized(Rectangle r) =>
        new(Math.Min(r.LLX, r.URX), Math.Min(r.LLY, r.URY), Math.Max(r.LLX, r.URX), Math.Max(r.LLY, r.URY));

    /// <summary>The field a widget stands for: the widget itself when it carries the field
    /// (a merged widget with /T), else its /Parent.</summary>
    private PdfDictionary TerminalField(PdfDictionary widget) =>
        widget.Get("T") is null && _reader.ResolveDict(widget.Get("Parent")) is { } parent ? parent : widget;

    private bool FieldChainHasTooltip(PdfDictionary field)
    {
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (var node = field; node is not null && seen.Add(node); node = _reader.ResolveDict(node.Get("Parent")))
            if (_reader.Resolve(node.Get("TU")) is PdfString tu && tu.ToText().Trim().Length > 0) return true;
        return false;
    }

    private bool IsCheckOrRadio(PdfDictionary field)
    {
        string? type = null;
        long flags = 0;
        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        for (var node = field; node is not null && seen.Add(node); node = _reader.ResolveDict(node.Get("Parent")))
        {
            type ??= node.GetName("FT");
            if (flags == 0 && _reader.Resolve(node.Get("Ff")) is PdfInteger ff) flags = ff.Value;
        }
        return type == "Btn" && (flags & PushButtonFlag) == 0;
    }

    /// <summary>The field's label: its own line first (the side its label is read on, then the
    /// other), then the line just above it; null when none is clear.</summary>
    private static string? FieldLabel(Rectangle field, bool labelFollows,
        List<(Rectangle Box, string Text)> pageText, List<Rectangle> otherFields)
    {
        var height = Math.Max(1, field.URY - field.LLY);
        var sameLine = pageText.Where(t =>
        {
            var cy = (t.Box.LLY + t.Box.URY) / 2;
            return cy >= field.LLY && cy <= field.URY && !InsideAny(t.Box, otherFields) && !Inside(t.Box, field);
        }).ToList();

        // A check box or radio button reads its label after it (or, less often, before); a
        // text field reads it before or above - the text after a text field is the next
        // field's label.
        if (labelFollows)
            return SideLabel(field, height, sameLine, otherFields, toTheLeft: false)
                ?? SideLabel(field, height, sameLine, otherFields, toTheLeft: true);
        return SideLabel(field, height, sameLine, otherFields, toTheLeft: true)
            ?? LabelAbove(field, height, pageText, otherFields);
    }

    /// <summary>The run of fragments on the field's line that ends (or starts) nearest the
    /// field, gathered while the gaps stay word-sized and no other field stands between.</summary>
    private static string? SideLabel(Rectangle field, double height, List<(Rectangle Box, string Text)> line,
        List<Rectangle> otherFields, bool toTheLeft)
    {
        var reach = height * LabelReachInHeights;
        var wordGap = height * LabelWordGapInHeights;
        var middle = (field.LLY + field.URY) / 2;
        double Off((Rectangle Box, string Text) t) => Math.Abs((t.Box.LLY + t.Box.URY) / 2 - middle);
        var candidates = (toTheLeft
                ? line.Where(t => t.Box.URX <= field.LLX + 1).OrderByDescending(t => Math.Round(t.Box.URX)).ThenBy(Off)
                : line.Where(t => t.Box.LLX >= field.URX - 1).OrderBy(t => Math.Round(t.Box.LLX)).ThenBy(Off)).ToList();
        var run = new List<(Rectangle Box, string Text)>();
        var edge = toTheLeft ? field.LLX : field.URX;
        foreach (var t in candidates)
        {
            // Two lines stacked beside the field: the one taken first (the nearer) stands.
            if (run.Any(r => r.Box.LLX < t.Box.URX && t.Box.LLX < r.Box.URX)) continue;
            var gap = toTheLeft ? edge - t.Box.URX : t.Box.LLX - edge;
            if (gap > (run.Count == 0 ? reach : wordGap)) break;
            var from = Math.Min(edge, toTheLeft ? t.Box.URX : t.Box.LLX);
            var to = Math.Max(edge, toTheLeft ? t.Box.URX : t.Box.LLX);
            if (otherFields.Any(o => o.LLX >= from - FieldEdgeSlack && o.URX <= to + FieldEdgeSlack
                    && o.LLY < field.URY && o.URY > field.LLY)) break;
            run.Add(t);
            edge = toTheLeft ? t.Box.LLX : t.Box.URX;
        }
        if (toTheLeft) run.Reverse();
        return CleanLabel(string.Join(" ", run.Select(t => t.Text.Trim())));
    }

    /// <summary>The nearest line just above the field that starts at the field's left edge.</summary>
    private static string? LabelAbove(Rectangle field, double height, List<(Rectangle Box, string Text)> pageText,
        List<Rectangle> otherFields)
    {
        var above = pageText.Where(t => t.Box.LLY >= field.URY - 1 && t.Box.LLY <= field.URY + height * LabelAboveInHeights
                && Math.Abs(t.Box.LLX - field.LLX) <= LabelAboveIndent && !InsideAny(t.Box, otherFields))
            .ToList();
        if (above.Count == 0) return null;
        var lineY = above.Min(t => t.Box.LLY);
        var line = above.Where(t => Math.Abs(t.Box.LLY - lineY) < height / 2).OrderBy(t => t.Box.LLX);
        return CleanLabel(string.Join(" ", line.Select(t => t.Text.Trim())));
    }

    private static bool Inside(Rectangle box, Rectangle area)
    {
        var cx = (box.LLX + box.URX) / 2;
        var cy = (box.LLY + box.URY) / 2;
        return cx >= area.LLX && cx <= area.URX && cy >= area.LLY && cy <= area.URY;
    }

    private static bool InsideAny(Rectangle box, List<Rectangle> areas) => areas.Any(a => Inside(box, a));

    /// <summary>The label as a name: whitespace collapsed, a trailing colon or asterisk
    /// dropped; null when nothing readable is left or it is too long to be a label.</summary>
    private static string? CleanLabel(string text)
    {
        var label = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().TrimEnd(':', '*', ' ');
        if (label.Length > MaxFieldLabelLength || label.Count(char.IsLetter) < MinLabelLetters) return null;
        return label;
    }
}
