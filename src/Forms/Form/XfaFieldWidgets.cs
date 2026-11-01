using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form
{
    /// <summary>Annotation flag Print: the widget is part of the printed page.</summary>
    private const int WidgetPrintFlag = 4;
    /// <summary>/MK /TP: the button shows its icon and no caption.</summary>
    private const int IconOnlyPosition = 1;

    /// <summary>State of the pass that turns the laid-out fields of a converted XFA form into fields with widgets.</summary>
    private sealed class XfaWidgetPass
    {
        public Document Doc = null!;
        public PdfArray Fields = null!;
        /// <summary>The fields made so far by path: a field painted again under the same path (a header repeated
        /// on the next page) keeps the first; the options of a group share its field.</summary>
        public Dictionary<string, (PdfIndirectRef Ref, PdfDictionary Dict)> ByPath = new(StringComparer.Ordinal);
        /// <summary>The on states the options of each group took, to keep them apart.</summary>
        public Dictionary<PdfDictionary, HashSet<string>> GroupStates = new();
        public Dictionary<int, PdfArray> Annots = new();
        /// <summary>Each page's font resources, made one object for the page and its widgets' appearances.</summary>
        public Dictionary<PdfDictionary, PdfIndirectRef> PageFonts = new(ReferenceEqualityComparer.Instance);    }

    /// <summary>Lists one field for every field instance the render laid out - a repeated row's fields once per
    /// instance, a master page's once per page, each named by its instance path - with a widget annotation at
    /// the rectangle it was painted in on its page. The field carries the value and the widget draws it, as the
    /// render laid it out, with the text style a viewer needs for a new one; a check box or option carries the
    /// two states of its glyph. An exclusion group is one field whose options are its widgets.
    /// False when the render placed nothing, so the caller lists the template's fields instead.</summary>
    private bool PlaceXfaFieldWidgets(PdfReader reader, PdfDictionary acroForm, List<Xfa.XfaFieldPlacement> placements)
    {
        var doc = OwnerDocument;
        if (doc is null || placements.Count == 0) return false;
        var pass = new XfaWidgetPass { Doc = doc };
        if (reader.Resolve(acroForm.Get("Fields")) is PdfArray listed) pass.Fields = listed;
        else acroForm.Set("Fields", pass.Fields = new PdfArray());
        int pageCount = doc.Pages.Count;
        foreach (var p in placements.Where(p => p.Page >= 1 && p.Page <= pageCount))
        {
            if (p.IsOption)
            {
                if (!pass.ByPath.TryGetValue(p.Path, out var group)) group = NewXfaField(pass, p);
                PlaceXfaOption(pass, group, p);
            }
            else if (!pass.ByPath.ContainsKey(p.Path))
            {
                var field = NewXfaField(pass, p);
                DressXfaWidget(pass, field.Dict, p);
                AnnotsOf(pass, p.Page).Add(field.Ref);
            }
            else KeepXfaValueOnPage(doc, p);
        }
        if (pass.ByPath.Count == 0)
        {
            foreach (var p in placements.Where(p => p.Page >= 1 && p.Page <= pageCount)) KeepXfaValueOnPage(doc, p);
            return false;
        }
        // Every widget carries its appearance: nothing is left for a viewer to build over the painted values.
        acroForm.Remove("NeedAppearances");
        EnsureDefaultResources(acroForm);
        MarkAcroFormDirty();
        return true;
    }

    /// <summary>A new field of the form under the placement's path: its type and flags, and the value the render
    /// showed - else, for a field the data did not bind, the value the data holds under the path, which the page
    /// could not bind strictly. A bound field shown empty stays empty: a row of an empty data group must not take
    /// the first filled row's value.</summary>
    private (PdfIndirectRef Ref, PdfDictionary Dict) NewXfaField(XfaWidgetPass pass, Xfa.XfaFieldPlacement p)
    {
        var value = p.Value ?? (p.Ft is "Tx" or "Ch" && !p.ValueBound ? GetXfaFieldValue(p.Path) : null);
        var dict = FlatXfaFieldDict(new Xfa.XfaFlatField { Path = p.Path, Ft = p.Ft, Ff = p.Ff, Value = value });
        int num = pass.Doc.AllocateObjectNumber();
        pass.Doc.AddNewObject(num, dict, registerOverlay: true);
        var field = (new PdfIndirectRef(num, 0), dict);
        pass.Fields.Add(field.Item1);
        pass.ByPath[p.Path] = field;
        return field;
    }

    private static PdfArray AnnotsOf(XfaWidgetPass pass, int pageNumber)
    {
        if (pass.Annots.TryGetValue(pageNumber, out var annots)) return annots;
        var pageDict = pass.Doc.Pages[pageNumber].Dict;
        annots = pageDict.Get("Annots") as PdfArray ?? new PdfArray();
        pageDict.Set("Annots", annots);
        pass.Annots[pageNumber] = annots;
        return annots;
    }

    /// <summary>The entries that make the dictionary a widget on its page, and what its kind of field shows there.</summary>
    private static void DressXfaWidget(XfaWidgetPass pass, PdfDictionary widget, Xfa.XfaFieldPlacement p)
    {
        var rect = new PdfArray();
        rect.Add(new PdfReal(p.X)); rect.Add(new PdfReal(p.Y));
        rect.Add(new PdfReal(p.X + p.W)); rect.Add(new PdfReal(p.Y + p.H));
        widget.Set("Type", new PdfName("Annot"));
        widget.Set("Subtype", new PdfName("Widget"));
        widget.Set("Rect", rect);
        widget.Set("F", new PdfInteger(p.ScreenOnly ? 0 : WidgetPrintFlag));
        widget.Set("P", pass.Doc.Pages[p.Page].Dict);
        long flags = (widget.ContainsKey("Ff") ? widget.GetInt("Ff") : 0) | (p.Ff & (Xfa.XfaFieldPlacement.FlagReadOnly | Xfa.XfaFieldPlacement.FlagMultiline));
        if (flags != 0 && !p.IsOption) widget.Set("Ff", new PdfInteger(flags));
        var ap = new PdfDictionary();
        if (p.OnAppearance is not null && p.OffAppearance is not null && p.Ft == "Btn")
        {
            var on = XfaOnStateName(pass, widget, p);
            var states = new PdfDictionary();
            states.Set(on, XfaWidgetAppearance(rect, p, p.OnAppearance));
            states.Set("Off", XfaWidgetAppearance(rect, p, p.OffAppearance));
            ap.Set("N", states);
            if (p.Turn != 0)
            {
                var turned = new PdfDictionary();
                turned.Set("R", new PdfInteger(p.Turn));
                widget.Set("MK", turned);
            }
            widget.Set("AS", new PdfName(p.On ? on : "Off"));
            if (!p.IsOption) widget.Set("V", new PdfName(p.On ? on : "Off"));
            else if (p.On && pass.Doc.Reader.ResolveDict(widget.Get("Parent")) is { } group) group.Set("V", new PdfName(on));
        }
        else if (p.Image is not null && p.ImageName is not null && p.ImageAppearance is not null)
        {
            // An image field's widget shows the picture the page draws, through the page's own image object.
            var normal = XfaWidgetAppearance(rect, p, p.ImageAppearance);
            var xobjects = new PdfDictionary();
            xobjects.Set(p.ImageName, p.Image);
            ((PdfDictionary)normal.Dict.Get("Resources")!).Set("XObject", xobjects);
            ap.Set("N", normal);
            DressXfaValueWidget(widget, p);
            // The picture is the button's icon, shown without a caption, as a viewer's image button carries it.
            var mk = pass.Doc.Reader.ResolveDict(widget.Get("MK")) ?? new PdfDictionary();
            mk.Set("I", normal);
            mk.Set("TP", new PdfInteger(IconOnlyPosition));
            widget.Set("MK", mk);
        }
        else
        {
            // The widget draws the value the render laid out, in the page's fonts; a field whose value stayed on
            // the page adds nothing to it until it is given a new value.
            var normal = XfaWidgetAppearance(rect, p, p.Ft is "Tx" or "Ch" ? XfaValueContent(p) : p.ValueAppearance ?? Array.Empty<byte>());
            if (p.ValueAppearance is not null && p.ValueFonts is not null)
                ((PdfDictionary)normal.Dict.Get("Resources")!).Set("Font", SharedPageFonts(pass, p));
            ap.Set("N", normal);
            DressXfaValueWidget(widget, p);
        }
        widget.Set("AP", ap);
    }

    /// <summary>The marked text block of a text or choice widget: the value the render drew for it, if any.</summary>
    private static byte[] XfaValueContent(Xfa.XfaFieldPlacement p)
    {
        var open = Encoding.ASCII.GetBytes("/Tx BMC\n");
        var close = Encoding.ASCII.GetBytes("EMC\n");
        var value = p.ValueAppearance ?? Array.Empty<byte>();
        var content = new byte[open.Length + value.Length + close.Length];
        open.CopyTo(content, 0);
        value.CopyTo(content, open.Length);
        close.CopyTo(content, open.Length + value.Length);
        return content;
    }

    /// <summary>The page's font resources as one object the page and the appearances of its widgets name, written once.</summary>
    private static PdfIndirectRef SharedPageFonts(XfaWidgetPass pass, Xfa.XfaFieldPlacement p)
    {
        var fonts = p.ValueFonts!;
        if (pass.PageFonts.TryGetValue(fonts, out var shared)) return shared;
        int num = pass.Doc.AllocateObjectNumber();
        pass.Doc.AddNewObject(num, fonts, registerOverlay: true);
        pass.PageFonts[fonts] = shared = new PdfIndirectRef(num, 0);
        if (pass.Doc.Pages[p.Page].Dict.Get("Resources") is PdfDictionary resources && ReferenceEquals(resources.Get("Font"), fonts))
            resources.Set("Font", shared);
        return shared;
    }

    /// <summary>A placement that gets no widget of its own - a field repeated under a path already listed -
    /// keeps the value the render drew for it on its page.</summary>
    private static void KeepXfaValueOnPage(Document doc, Xfa.XfaFieldPlacement p)
    {
        if (p.ValueAppearance is not { Length: > 0 } drawn) return;
        doc.Pages[p.Page].AddContentStream(drawn);
        p.ValueAppearance = null;
    }

    /// <summary>What a viewer needs to draw a new value in a text or choice widget - the text style, the alignment
    /// and the ground to lay over the value the page shows - a choice list's items, and a button's caption.</summary>
    private static void DressXfaValueWidget(PdfDictionary widget, Xfa.XfaFieldPlacement p)
    {
        var mk = new PdfDictionary();
        if (p.Ft is "Tx" or "Ch")
        {
            var c = p.TextColor;
            widget.Set("DA", new PdfString(Encoding.ASCII.GetBytes(string.Format(CultureInfo.InvariantCulture,
                "/Helv {0:0.##} Tf {1:0.###} {2:0.###} {3:0.###} rg", p.FontSize, c[0], c[1], c[2]))));
            if (p.Quadding != 0) widget.Set("Q", new PdfInteger(p.Quadding));
            var ground = new PdfArray();
            foreach (var component in p.Background) ground.Add(new PdfReal(component));
            mk.Set("BG", ground);
        }
        if (p.Ft == "Ch" && p.Options is { Count: > 0 } options && !widget.ContainsKey("Opt"))
        {
            var opt = new PdfArray();
            foreach (var (value, text) in options)
            {
                if (value == text) { opt.Add(Field.EncodePdfTextString(text)); continue; }
                var pair = new PdfArray();
                pair.Add(Field.EncodePdfTextString(value));
                pair.Add(Field.EncodePdfTextString(text));
                opt.Add(pair);
            }
            widget.Set("Opt", opt);
        }
        if (p.Ft == "Btn" && !string.IsNullOrWhiteSpace(p.Caption))
            mk.Set("CA", Field.EncodePdfTextString(p.Caption!.Trim()));
        // A turned field says so: a new value is then drawn turned like the one the page shows.
        if (p.Turn != 0) mk.Set("R", new PdfInteger(p.Turn));
        if (mk.Count > 0) widget.Set("MK", mk);
    }

    /// <summary>An option of an exclusion group: a widget of its own under the group's field.</summary>
    private static void PlaceXfaOption(XfaWidgetPass pass, (PdfIndirectRef Ref, PdfDictionary Dict) group, Xfa.XfaFieldPlacement p)
    {
        if (group.Dict.GetName("FT") != "Btn" || p.OnAppearance is null) return;
        var kid = new PdfDictionary();
        kid.Set("Parent", group.Ref);
        if (pass.Doc.Reader.Resolve(group.Dict.Get("Kids")) is not PdfArray kids)
        {
            kids = new PdfArray();
            group.Dict.Set("Kids", kids);
        }
        DressXfaWidget(pass, kid, p);
        int num = pass.Doc.AllocateObjectNumber();
        pass.Doc.AddNewObject(num, kid, registerOverlay: true);
        var reference = new PdfIndirectRef(num, 0);
        kids.Add(reference);
        AnnotsOf(pass, p.Page).Add(reference);
    }

    /// <summary>The name of a check box's or option's on state: the on value the template gives it when that is
    /// a plain token, kept apart from the states of the other options of its group.</summary>
    private static string XfaOnStateName(XfaWidgetPass pass, PdfDictionary widget, Xfa.XfaFieldPlacement p)
    {
        const string Fallback = "Yes";
        var name = p.OnState is { Length: > 0 } s && s != "Off" && s.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.') ? s : Fallback;
        if (!p.IsOption || pass.Doc.Reader.ResolveDict(widget.Get("Parent")) is not { } group) return name;
        if (!pass.GroupStates.TryGetValue(group, out var taken)) pass.GroupStates[group] = taken = new HashSet<string>(StringComparer.Ordinal);
        var unique = name;
        for (int n = taken.Count; !taken.Add(unique); n++) unique = name + n.ToString(CultureInfo.InvariantCulture);
        return unique;
    }

    /// <summary>A form XObject over the widget's own rectangle: what it draws is in page coordinates. For a
    /// turned field the box is the upright one the field was painted in and the matrix is its turn, which
    /// takes that box onto the rectangle.</summary>
    private static PdfStream XfaWidgetAppearance(PdfArray rect, Xfa.XfaFieldPlacement p, byte[] content)
    {
        var dict = new PdfDictionary();
        dict.Set("Type", new PdfName("XObject"));
        dict.Set("Subtype", new PdfName("Form"));
        dict.Set("FormType", new PdfInteger(1));
        var bbox = new PdfArray();
        if (p.Matrix is { } turn)
        {
            bbox.Add(new PdfReal(p.BoxX)); bbox.Add(new PdfReal(p.BoxY));
            bbox.Add(new PdfReal(p.BoxX + p.BoxW)); bbox.Add(new PdfReal(p.BoxY + p.BoxH));
            var matrix = new PdfArray();
            foreach (var entry in turn) matrix.Add(new PdfReal(entry));
            dict.Set("Matrix", matrix);
        }
        else
            foreach (var corner in rect) bbox.Add(corner);
        dict.Set("BBox", bbox);
        dict.Set("Resources", new PdfDictionary());
        dict.Set("Length", new PdfInteger(content.Length));
        return new PdfStream(dict, content);
    }
}
