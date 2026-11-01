using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

/// <summary>
/// The appearance a page renderer paints for a form field widget when the file does not carry a usable
/// one: a widget with no appearance stream at all, or, in a form that sets NeedAppearances, one whose
/// stream is stale - an empty or zero-sized box the filling tool never refreshed. An appearance that
/// shows something is honoured even under NeedAppearances: every file the generator writes sets the
/// flag and carries a correct appearance, and that appearance is what the page shows. The value is laid
/// out on a copy of the widget, so the document keeps its dictionaries as they were and a later save
/// writes nothing the render made up.
/// </summary>
internal static class WidgetRenderAppearance
{
    /// <summary>True when the form asks viewers to build the appearances of its fields.</summary>
    internal static bool FormNeedsAppearances(PdfReader reader)
    {
        var acroForm = reader.ResolveDict(reader.Catalog.Get("AcroForm"));
        return acroForm is not null && reader.Resolve(acroForm.Get("NeedAppearances")) is PdfBoolean { Value: true };
    }

    /// <summary>A transient appearance of the widget's value, or null when the file's own appearance is
    /// the one to paint. Only a text or choice field with a value is laid out: check boxes and radio
    /// buttons draw the state their authored appearances define.</summary>
    internal static PdfStream? Build(PdfDictionary widget, PdfReader reader, bool formNeedsAppearances)
    {
        var ap = reader.ResolveDict(widget.Get("AP"));
        var normal = ap is null ? null : reader.Resolve(ap.Get("N"));
        if (normal is PdfDictionary) return null;
        if (normal is PdfStream stream && (!formNeedsAppearances || !IsStale(stream, reader))) return null;
        var copy = new PdfDictionary();
        foreach (var key in widget.Keys)
        {
            if (key != "AP") copy.Set(key, widget.Get(key)!);
        }
        var field = Field.Create(copy, reader);
        if (field is not (TextBoxField or ChoiceField) || string.IsNullOrEmpty(field.Value)) return null;
        field.GenerateAppearance();
        var built = reader.ResolveDict(copy.Get("AP"));
        return built is null ? null : reader.ResolveStream(built.Get("N"));
    }

    /// <summary>True when the appearance cannot show a value: its box has no area, or its content shows
    /// no string at all (a filling tool wrote an empty box and left the value to the viewer).</summary>
    private static bool IsStale(PdfStream form, PdfReader reader)
    {
        if (reader.Resolve(form.Dict.Get("BBox")) is PdfArray bbox && bbox.Count >= 4)
        {
            var box = Rectangle.FromPdfArray(bbox, reader);
            if (box.Width <= 0 || box.Height <= 0) return true;
        }
        return !ShowsAString(reader.DecodeStream(form));
    }

    /// <summary>True when the content holds a literal string with a visible character or a hex string with
    /// a digit: the operand of a text-showing operator. A dictionary's double angle bracket is not a string.</summary>
    private static bool ShowsAString(byte[] content)
    {
        for (var i = 0; i + 1 < content.Length; i++)
        {
            if (content[i] == (byte)'(' && content[i + 1] != (byte)')' && !char.IsWhiteSpace((char)content[i + 1]))
                return true;
            if (content[i] == (byte)'<' && Uri.IsHexDigit((char)content[i + 1]))
                return true;
        }
        return false;
    }
}
