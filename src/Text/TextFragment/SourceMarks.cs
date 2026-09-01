
namespace Aspose.Pdf.Text;

public partial class TextFragment
{
    /// <summary>
    /// The page this fragment was extracted from. When set, modifying <see cref="Text"/>
    /// will update the PDF content stream.
    /// </summary>
    internal Page? SourcePage { get; set; }

    internal void MarkCompanionRule(double rawX, double rawY, double rawW, double rawH,
        double pageX, double pageW, Color colour)
    {
        (CompanionRuleSources ??= new()).Add((rawX, rawY, rawW, rawH));
        (CompanionRules ??= new()).Add((pageX, pageW, colour));
    }

    /// <summary>The source run text that follows this fragment inside its own show
    /// operator, and the page X at which the fragment's LAST source run ends. A source
    /// underline usually spans more than the matched phrase ("Test bold text 26" under one
    /// rule): replacing the phrase re-seats that tail at the replacement's advance, and
    /// switching the underline off must leave the tail's rule standing.</summary>
    internal string? SourceUnderlineTrailingText;

    internal double SourceUnderlineRunEndX;

    /// <summary>Records a captured source underline rectangle and marks this fragment and all
    /// its segments as underlined (without registering save-time underline injection).</summary>
    internal void MarkCapturedUnderlineSource(double x, double y, double w, double h)
    {
        (CapturedUnderlineSources ??= new()).Add((x, y, w, h));
        TextState.SetCapturedUnderline(true);
        if (_segments is not null)
            foreach (var s in _segments)
                s.TextState?.SetCapturedUnderline(true);
    }

    /// <summary>Fill colour of the captured source background, re-used when the highlight
    /// is re-drawn for replaced text.</summary>
    internal Color? CapturedBackgroundColor;

    /// <summary>Records a captured source background (highlight) rectangle without
    /// registering save-time background injection.</summary>
    internal void MarkCapturedBackgroundSource(double x, double y, double w, double h, Color? color)
    {
        (CapturedBackgroundSources ??= new()).Add((x, y, w, h));
        CapturedBackgroundColor ??= color;
    }

    /// <summary>The text as last written to the content stream by TextBuilder.</summary>
    internal string? LastWrittenText { get; set; }

    /// <summary>The <see cref="AttachedLayoutSignature"/> the attached segment was
    /// last written from; a different value at save time means a rewrite.</summary>
    internal string? AttachedSignature { get; set; }

    /// <summary>Whether the append added a trailing space (the list overload does).</summary>
    internal bool AttachedTrailingSpace { get; set; }

    /// <summary>Everything the written run depends on: the texts, positions, faces,
    /// sizes and colours of the fragment and each of its segments.</summary>
    internal string AttachedLayoutSignature()
    {
        var sb = new System.Text.StringBuilder();
        void State(TextState st)
        {
            sb.Append(st.FontName).Append('|').Append(st.Font?.FontName).Append('|')
              .Append(st.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
              .Append(st.IsBold ? 'b' : '-').Append(st.IsItalic ? 'i' : '-').Append('|')
              .Append(st.ForegroundColor?.ToString()).Append('|').Append(st.StrokingColor?.ToString()).Append('|')
              .Append(st.Underline ? 'u' : '-').Append(st.StrikeOut ? 's' : '-').Append('|')
              .Append((int)st.RenderingMode).Append('|')
              .Append(st.CharacterSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('|')
              .Append(st.WordSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }
        sb.Append(_text).Append(';');
        var p = PositionOrNull;
        sb.Append(p?.XIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
          .Append(p?.YIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        State(TextState);
        foreach (var seg in _segments)
        {
            sb.Append('[').Append(seg.Text).Append(';');
            sb.Append(seg.Position?.XIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
              .Append(seg.Position?.YIndent.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            if (seg.TextState is { } st) State(st);
            sb.Append(']');
        }
        return sb.ToString();
    }

    /// <summary>The Form XObject stream the fragment's text was extracted from when
    /// the page content reached it through <c>Do</c> (null for direct page text).
    /// Post-extraction edits that must land in the producing stream — the
    /// BackgroundColor highlight — write here instead of the page content.</summary>
    internal Core.PdfStream? SourceXObjStream { get; set; }
}
