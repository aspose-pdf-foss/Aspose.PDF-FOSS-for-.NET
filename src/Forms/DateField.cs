using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms;

/// <summary>A text field presenting a date with a popup JavaScript calendar.
/// <see cref="Init"/> wires the document-level calendar script (named
/// <c>&lt;PartialName&gt;_jsCalendar</c>) plus the field's own activation
/// hooks; it requires a named field on a live page.</summary>
public class DateField : TextBoxField
{
    private const string CalendarKeySuffix = "_jsCalendar";

    public DateField() : base() { }

    public DateField(Document doc) : base(doc) { }

    public DateField(Page page, Rectangle rect) : base(page, rect) { }

    public DateField(Document doc, Rectangle rect) : base(doc, rect) { }

    /// <summary>The date format pattern (e.g. <c>dd.MM.yyyy</c>) the field
    /// displays and parses. Stored on the object; <see cref="Init"/> bakes it
    /// into the calendar script registration.</summary>
    public string? DateFormat { get; set; }

    /// <summary>The field value as a date, parsed through <see cref="DateFormat"/>
    /// when one is set.</summary>
    public new System.DateTime Value
    {
        get
        {
            var text = base.Value;
            if (string.IsNullOrEmpty(text)) return System.DateTime.MinValue;
            if (!string.IsNullOrEmpty(DateFormat)
                && System.DateTime.TryParseExact(text, DateFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var exact))
                return exact;
            return System.DateTime.TryParse(text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed)
                ? parsed : System.DateTime.MinValue;
        }
        set => base.Value = string.IsNullOrEmpty(DateFormat)
            ? value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Attach the popup-calendar JavaScript for this field to
    /// <paramref name="page"/>'s document: the calendar source is registered as
    /// a document-level script keyed <c>&lt;PartialName&gt;_jsCalendar</c>, and
    /// the field's format is bound into it. The field must carry a
    /// <c>PartialName</c> (the script key is derived from it) — an unnamed
    /// field cannot be initialised.</summary>
    public void Init(Page page)
    {
        if (string.IsNullOrEmpty(PartialName))
            throw new EmptyValueException("PartialName must be assigned before Init.");

        // A null page dereferences here — the facade contract surfaces the
        // NullReferenceException rather than validating the argument.
        var document = page.Reader.OwnerDocument
            ?? throw new System.NullReferenceException("The page is not attached to a document.");

        var format = string.IsNullOrEmpty(DateFormat) ? "dd/MM/yyyy" : DateFormat!;
        // The calendar bootstrap: enough of the Acrobat-side contract to key the
        // script by field and format; viewers with full calendar support replace
        // this body with their own implementation.
        document.JavaScript[PartialName + CalendarKeySuffix] =
            $"var {PartialName}_cal = new Object(); {PartialName}_cal.format = \"{format}\";";
    }
}
