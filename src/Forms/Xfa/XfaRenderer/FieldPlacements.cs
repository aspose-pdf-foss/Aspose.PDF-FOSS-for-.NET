using System.Xml;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Forms.Xfa;

/// <summary>Where the render laid one field out: the page, the rectangle of its widget and what the
/// widget needs to stand for the field there. The page content already shows the field; the placement
/// is what turns that picture into a field a viewer can fill.</summary>
internal sealed class XfaFieldPlacement
{
    // AcroForm field flags (ISO 32000-1 tables 221, 226, 228, 230).
    public const long FlagReadOnly = 1L << 0;
    public const long FlagMultiline = 1L << 12;
    public const long FlagNoToggleToOff = 1L << 14;
    public const long FlagRadio = 1L << 15;
    public const long FlagPushButton = 1L << 16;
    public const long FlagCombo = 1L << 17;

    /// <summary>The field's SOM path. An option of an exclusion group carries the group's path.</summary>
    public string Path = string.Empty;
    /// <summary>True for an option of an exclusion group: a widget of the group's field.</summary>
    public bool IsOption;
    /// <summary>One-based page the field was painted on.</summary>
    public int Page;
    /// <summary>True while the path is still relative to a named master page area.</summary>
    public bool OnMaster;
    /// <summary>The widget rectangle in page coordinates (origin bottom-left).</summary>
    public double X, Y, W, H;
    /// <summary>The upright box the field was painted in, before any turn and uncut by the page edge.</summary>
    public double BoxX, BoxY, BoxW, BoxH;
    /// <summary>The turn of a rotated field in degrees counter-clockwise (0 = upright) and the matrix
    /// that takes its upright box to where it lies on the page.</summary>
    public int Turn;
    public double[]? Matrix;
    /// <summary>AcroForm field type and flags of the field's control.</summary>
    public string Ft = "Tx";
    public long Ff;
    /// <summary>Check box or option: the name of its on state, whether it is on, and the content of
    /// the two appearances, drawn in page coordinates with the operators the page itself uses.</summary>
    public string? OnState;
    public bool On;
    public byte[]? OnAppearance;
    public byte[]? OffAppearance;
    /// <summary>Text size and colour of the value, and its alignment as a quadding number.</summary>
    public double FontSize;
    public double[] TextColor = { 0, 0, 0 };
    public int Quadding;
    /// <summary>The colour the field stands on: what a viewer fills the widget with before it draws a new value.</summary>
    public double[] Background = { 1, 1, 1 };
    /// <summary>A button's caption.</summary>
    public string? Caption;
    /// <summary>A choice list's items, the saved value and the shown text of each.</summary>
    public List<(string Value, string Text)>? Options;
    /// <summary>The value the field shows: a text field's text, a choice list's saved value.</summary>
    public string? Value;
    /// <summary>True when the data bound the field's instance: a field it shows empty has no value to look
    /// up elsewhere.</summary>
    public bool ValueBound;
    /// <summary>An image field's picture: the image object the page draws, its name there, and the
    /// operators that draw it where the page does, which its widget shows as its appearance.</summary>
    public PdfStream? Image;
    public string? ImageName;
    public byte[]? ImageAppearance;
    /// <summary>The operators that draw the value where the render laid it out, and the font resources they
    /// name: the widget's normal appearance. Null while the value is on the page - a field without one, or a
    /// field continued on another page, cut by the page's edge or turned.</summary>
    public byte[]? ValueAppearance;
    public PdfDictionary? ValueFonts;
    /// <summary>True once the field's value has to stay on the page: its widget covers part of it at most.</summary>
    public bool ValueOnPage;
    /// <summary>True for a field shown on screen only (relevant="-print"): its widget is not printed, and what the
    /// render drew of it is the widget's appearance, not the page's.</summary>
    public bool ScreenOnly;
}

internal static partial class XfaRenderer
{
    /// <summary>Half the width the outline of a check box is stroked with: how far the stroke reaches inside it.</summary>
    private const double CheckOutlineReach = 0.3;

    /// <summary>The SOM path the fields of a master page are named under - the root subform and the named page
    /// sets down to the page area - and the page area's name. A page area with no name adds nothing: its
    /// fields are named like those of the body.</summary>
    private static (string prefix, string name) MasterBasePath(XmlElement master, string rootPath)
    {
        var sets = new List<string>();
        for (var a = master.ParentNode as XmlElement; a is not null && a.LocalName == "pageSet"; a = a.ParentNode as XmlElement)
        {
            var setName = a.GetAttribute("name");
            if (setName.Length > 0) sets.Add($"{setName}[{SiblingIndex(a)}]");
        }
        sets.Reverse();
        return (sets.Count == 0 ? rootPath : rootPath + "." + string.Join(".", sets), master.GetAttribute("name"));
    }

    /// <summary>The placements of the whole render in page order, each on its final page. A field of a master page
    /// is named by the page area and the count of pages that area served before this one.</summary>
    private static List<XfaFieldPlacement> PlacementsInPageOrder(XfaRenderState xr)
    {
        var all = new List<XfaFieldPlacement>();
        var served = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < xr.newPages.Count; i++)
        {
            if (!xr.placements.TryGetValue(xr.newPages[i].items, out var page)) continue;
            int occurrence = 0;
            if (page.master.Length > 0)
            {
                served.TryGetValue(page.master, out occurrence);
                served[page.master] = occurrence + 1;
            }
            foreach (var p in page.fields)
            {
                p.Page = i + 1;
                if (p.OnMaster) p.Path = $"{page.master}[{occurrence}].{p.Path}";
                all.Add(p);
            }
        }
        return all;
    }

    /// <summary>Notes where a painted field lies, for the widget that will stand for it, and hands the note
    /// back; none for an option of a group without a name, a field without a control of its own, or a barcode,
    /// which are pictures only. An image field is noted only once it holds a picture: the caller paints one first.</summary>
    private static XfaFieldPlacement? RecordPlacement(PaintFieldState pf)
    {
        var placed = pf.ctx.Placements;
        if (placed is null || pf.ui is null or "barcode") return null;
        var group = pf.e.ParentNode is XmlElement { LocalName: "exclGroup" } g ? g : null;
        var name = pf.e.GetAttribute("name");
        if (group is not null && group.GetAttribute("name").Length == 0) return null;
        var path = pf.path;
        // A field without a name is still one a user fills: it is named as SOM names it, by its place among the
        // unnamed fields beside it, unless its data reference already gave it that step.
        if (group is null && PathSegment(pf.e) is null)
        {
            var own = $"#field[{UnnamedFieldIndex(pf.e)}]";
            path = path.Length > 0 ? path + "." + own : own;
        }
        if (group is not null && name.Length > 0)
        {
            // An option's own path ends in its name: the group's is the path without it.
            var own = $".{name}[{SiblingIndex(pf.e)}]";
            if (path.EndsWith(own, StringComparison.Ordinal)) path = path.Substring(0, path.Length - own.Length);
        }
        bool onMaster = pf.ctx.MasterPrefix is not null && pf.ctx.MasterName.Length > 0;
        if (pf.ctx.MasterPrefix is not null && !onMaster) path = pf.ctx.MasterPrefix + "." + path;
        var p = new XfaFieldPlacement
        {
            Path = path, IsOption = group is not null, Page = pf.ctx.PageNum, OnMaster = onMaster,
            FontSize = pf.fs, TextColor = FontColor(pf.e), Background = GroundUnder(pf.e),
        };
        var (x, y, w, h) = (pf.x, pf.y, pf.w, pf.h);
        var readOnly = (group ?? pf.e).GetAttribute("access") is "readOnly" or "protected" or "nonInteractive";
        if (pf.check || pf.radio)
        {
            (x, y, w, h) = (pf.glyphX, pf.glyphY, pf.glyphSize, pf.glyphSize);
            p.Ft = "Btn";
            p.Ff = group is null ? 0 : XfaFieldPlacement.FlagRadio | XfaFieldPlacement.FlagNoToggleToOff;
            p.On = pf.glyphOn;
            p.OnState = pf.glyphOnValue;
        }
        else if (pf.ui is "button" or "imageEdit")
        {
            // An image field is a button whose appearance is its picture.
            p.Ft = "Btn";
            p.Ff = XfaFieldPlacement.FlagPushButton;
            p.Caption = pf.cap;
            readOnly = false;
        }
        else
        {
            (x, y, w, h) = (pf.bx, pf.by, pf.bw, pf.bh);
            RecordValueField(pf, p);
        }
        if (readOnly) p.Ff |= XfaFieldPlacement.FlagReadOnly;
        (p.BoxX, p.BoxY, p.BoxW, p.BoxH) = (x, pf.ctx.PageH - (y + h), w, h);
        // A turned box is painted upright first: where its widget lies is known once it is turned.
        if (!pf.ctx.Turning && !FitOnPage(pf.ctx, p, p.BoxX, p.BoxY, p.BoxX + w, p.BoxY + h)) return null;
        if (pf.check || pf.radio) BuildCheckAppearances(pf, p);
        // The value of a text or choice field is drawn by its widget, as a viewer draws it: a value typed in
        // later replaces it instead of standing over a copy painted on the page. A turned field keeps it on the page.
        if (p.Ft is "Tx" or "Ch" && pf.valueItems is { } shown && !pf.ctx.Turning)
            foreach (var item in shown) item.ValueOf = p;
        placed.Add(p);
        return p;
    }

    /// <summary>The place of a field without a name among the unnamed fields of the container its path names,
    /// counted through the unnamed subforms in between, which add no step to the path: two unnamed fields
    /// a level apart would otherwise take one name.</summary>
    private static int UnnamedFieldIndex(XmlElement field)
    {
        var scope = (XmlElement)field.ParentNode!;
        while (scope.LocalName == "subform" && PathSegment(scope) is null && scope.ParentNode is XmlElement up) scope = up;
        var index = 0;
        foreach (var other in UnnamedFieldsIn(scope))
        {
            if (ReferenceEquals(other, field)) break;
            index++;
        }
        return index;
    }

    private static IEnumerable<XmlElement> UnnamedFieldsIn(XmlElement container)
    {
        foreach (var child in container.ChildNodes.OfType<XmlElement>())
        {
            if (child.LocalName == "field" && child.GetAttribute("name").Length == 0) yield return child;
            else if (child.LocalName == "subform" && PathSegment(child) is null)
                foreach (var nested in UnnamedFieldsIn(child)) yield return nested;
        }
    }

    /// <summary>Gives the placement the part of a rectangle that lies on the page; false when none does.</summary>
    private static bool FitOnPage(Ctx ctx, XfaFieldPlacement p, double llx, double lly, double urx, double ury)
    {
        double left = Math.Max(0, llx), right = Math.Min(ctx.PageW, urx);
        double bottom = Math.Max(0, lly), top = Math.Min(ctx.PageH, ury);
        if (right - left <= 0 || top - bottom <= 0) return false;
        (p.X, p.Y, p.W, p.H) = (left, bottom, right - left, top - bottom);
        return true;
    }

    /// <summary>Cuts the placements made from index <paramref name="first"/> on at the height
    /// <paramref name="cut"/> (page coordinates, y up): what the field shows below it is continued on
    /// another page, and the widget covers what stays.</summary>
    private static void CutPlacementsBelow(Ctx ctx, int first, double cut)
    {
        var placed = ctx.Placements;
        if (placed is null) return;
        for (int i = placed.Count - 1; i >= first; i--)
        {
            var p = placed[i];
            if (p.Turn != 0 || p.Y >= cut) continue;
            double top = p.Y + p.H;
            p.ValueOnPage = true;
            if (top - cut <= 0) { placed.RemoveAt(i); continue; }
            (p.Y, p.H) = (cut, top - cut);
        }
    }

    /// <summary>Settles the placements a turned box made while it was painted upright, from index
    /// <paramref name="first"/> on: the widget's rectangle is the box its upright one becomes under the
    /// turn, and the placement keeps the turn for the widget and its appearance.</summary>
    private static void TurnPlacements(Ctx ctx, int first, int turn, double[] m)
    {
        var placed = ctx.Placements;
        if (placed is null) return;
        for (int i = placed.Count - 1; i >= first; i--)
        {
            var p = placed[i];
            double x1 = m[0] * p.BoxX + m[2] * p.BoxY + m[4], y1 = m[1] * p.BoxX + m[3] * p.BoxY + m[5];
            double x2 = m[0] * (p.BoxX + p.BoxW) + m[2] * (p.BoxY + p.BoxH) + m[4], y2 = m[1] * (p.BoxX + p.BoxW) + m[3] * (p.BoxY + p.BoxH) + m[5];
            (p.Turn, p.Matrix, p.ValueOnPage) = (turn, m, true);
            if (!FitOnPage(ctx, p, Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2))) placed.RemoveAt(i);
        }
    }

    /// <summary>The type, value and text layout of a field that shows a value: a text, a choice or a signature.</summary>
    private static void RecordValueField(PaintFieldState pf, XfaFieldPlacement p)
    {
        var control = FirstChild(pf.e, "ui")?.ChildNodes.OfType<XmlElement>().FirstOrDefault();
        p.Quadding = FirstChild(pf.e, "para")?.GetAttribute("hAlign") switch { "center" => 1, "right" => 2, _ => 0 };
        p.Value = string.IsNullOrWhiteSpace(pf.val) ? null : pf.val.Trim();
        p.ValueBound = pf.bound;
        if (pf.ui == "signature")
            p.Ft = "Sig";
        else if (pf.ui == "choiceList")
        {
            p.Ft = "Ch";
            p.Ff = control?.GetAttribute("open") is "always" or "multiSelect" ? 0 : XfaFieldPlacement.FlagCombo;
            p.Options = ChoiceOptions(pf.e);
            // The page shows the item's text; the field holds the value the item saves.
            if (p.Value is not null && p.Options?.FirstOrDefault(o => o.Text == p.Value) is { Value.Length: > 0 } item) p.Value = item.Value;
        }
        else if (control?.GetAttribute("multiLine") == "1")
            p.Ff = XfaFieldPlacement.FlagMultiline;
        var uiBorder = control is null ? null : FirstChild(control, "border");
        if (uiBorder is not null && uiBorder.GetAttribute("presence") is not ("hidden" or "invisible")
            && ColorOf(FirstChild(uiBorder, "fill")) is { } fill)
            p.Background = fill;
    }

    /// <summary>The items of a choice list as pairs of saved value and shown text; a list that saves what it shows has both alike.</summary>
    private static List<(string Value, string Text)>? ChoiceOptions(XmlElement e)
    {
        var lists = e.ChildNodes.OfType<XmlElement>().Where(c => c.LocalName == "items").ToList();
        if (lists.Count == 0) return null;
        var saveList = lists.FirstOrDefault(l => l.GetAttribute("save") == "1") ?? (lists.Count > 1 ? lists[1] : null);
        var textList = lists.FirstOrDefault(l => l.GetAttribute("save") != "1") ?? lists[0];
        var texts = textList.ChildNodes.OfType<XmlElement>().Select(c => c.InnerText).ToList();
        var values = saveList?.ChildNodes.OfType<XmlElement>().Select(c => c.InnerText.Trim()).ToList();
        var options = new List<(string, string)>(texts.Count);
        for (int i = 0; i < texts.Count; i++)
            options.Add((values is not null && i < values.Count ? values[i] : texts[i], texts[i]));
        return options;
    }

    /// <summary>The colour under a field: its own fill, else the fill of the nearest container that has one, else the white of the page.</summary>
    private static double[] GroundUnder(XmlElement e)
    {
        for (XmlElement? a = e; a is not null && a.LocalName is not ("template" or "pageSet"); a = a.ParentNode as XmlElement)
            if (FillColor(a) is { } fill) return fill;
        return new double[] { 1, 1, 1 };
    }

    /// <summary>The two appearances of a check box or option, in page coordinates so that they fall on
    /// what the page draws to the last digit. The state the page shows adds nothing to it; the other
    /// state changes the picture: a mark where the page shows none, or the ground laid over the mark
    /// the page shows.</summary>
    private static void BuildCheckAppearances(PaintFieldState pf, XfaFieldPlacement p)
    {
        double size = pf.glyphSize, x = pf.glyphX, y = pf.ctx.PageH - (pf.glyphY + size);
        var b = new Content.ContentStreamBuilder();
        if (pf.glyphOn)
        {
            // The ground starts midway between the outline's stroke and the mark: it hides the mark and leaves the outline.
            double inset = (CheckOutlineReach + size * CheckMarkStart) / 2;
            var ground = new Item { Kind = "fill", X = x + inset, Y = y + inset, W = size - 2 * inset, H = size - 2 * inset, Color = p.Background };
            if (pf.glyphRound) ground.Radius = ground.W / 2;
            EmitXfaItem(b, ground, null!);
        }
        else
            EmitXfaItem(b, CheckMark(pf.glyphMark, pf.glyphX, pf.glyphY, size, pf.ctx.PageH), null!);
        p.OnAppearance = pf.glyphOn ? Array.Empty<byte>() : b.Build();
        p.OffAppearance = pf.glyphOn ? b.Build() : Array.Empty<byte>();
    }
}
