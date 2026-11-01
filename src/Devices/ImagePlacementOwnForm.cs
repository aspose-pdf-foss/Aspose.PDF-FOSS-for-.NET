using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>Where a placement inside a form XObject sits on its page: the names that lead from the page's
/// resources down to the form (the outermost first), which of the page's same-named Do operators draws the
/// outermost one, how often the page draws each name, and - shared by every placement of the page - the
/// form copies the Hide calls made so far and the names those calls gave a renamed Do.</summary>
internal sealed class OwnFormUse
{
    private OwnFormUse(IReadOnlyList<string> path, int ordinal, Dictionary<string, int> pageUses,
        Dictionary<string, PdfStream> copies, Dictionary<string, List<string>> renamed)
    {
        Path = path;
        Ordinal = ordinal;
        PageUses = pageUses;
        Copies = copies;
        Renamed = renamed;
    }

    internal IReadOnlyList<string> Path { get; }
    internal int Ordinal { get; }
    internal Dictionary<string, int> PageUses { get; }

    /// <summary>The private copy made for a use, by its key: the outermost name and ordinal, then the nested names.</summary>
    internal Dictionary<string, PdfStream> Copies { get; }

    /// <summary>The fresh names given to Do operators that drew a page-level form, by the form's original name.</summary>
    internal Dictionary<string, List<string>> Renamed { get; }

    /// <summary>The page's own content: no form above it yet.</summary>
    internal static OwnFormUse PageRoot(Dictionary<string, int> pageUses) =>
        new(Array.Empty<string>(), -1, pageUses, new Dictionary<string, PdfStream>(StringComparer.Ordinal),
            new Dictionary<string, List<string>>(StringComparer.Ordinal));

    /// <summary>The use of form <paramref name="name"/> drawn inside this one.</summary>
    internal OwnFormUse Inside(string name, int ordinal)
    {
        var path = new string[Path.Count + 1];
        for (var i = 0; i < Path.Count; i++) path[i] = Path[i];
        path[Path.Count] = name;
        return new OwnFormUse(path, ordinal, PageUses, Copies, Renamed);
    }
}

/// <summary>Hide of a placement drawn inside a form XObject the page's content reaches. The form may be drawn
/// on other pages, or more than once on this one, so the page gets a copy of its own with the image removed,
/// and the other uses keep the original.</summary>
public sealed partial class ImagePlacement
{
    internal OwnFormUse? FormUse;

    private const string FormNamePrefix = "Fm";

    /// <summary>Blank the drawing instruction in a copy of the form made for this use: the page's own entry
    /// points at the copy (a fresh entry, which the one Do is renamed to, when the page draws the form more
    /// than once), and each nested form gets a copy of its own under the copy above it. A copy an earlier
    /// Hide made for the same use is edited where it is.</summary>
    private bool HideInOwnForm()
    {
        if (FormUse is null || Page is null || SourceReader is null) return false;
        var reader = SourceReader;
        var holder = PageXObjects(reader);
        if (holder is null) return false;
        var path = FormUse.Path;
        var key = path[0] + "#" + FormUse.Ordinal;
        if (!FormUse.Copies.TryGetValue(key, out var form))
        {
            form = PrivateCopy(reader.ResolveStream(holder.Get(path[0])));
            if (form is null) return false;
            PointPageUseAt(holder, path[0], form);
            FormUse.Copies[key] = form;
        }
        for (var i = 1; i < path.Count; i++)
        {
            key += "/" + path[i];
            // A form without resources of its own draws with the ones it was drawn from.
            if (reader.ResolveDict(form.Dict.Get("Resources")) is { } own) holder = reader.ResolveDict(own.Get("XObject"));
            if (holder is null) return false;
            if (!FormUse.Copies.TryGetValue(key, out var inner))
            {
                inner = PrivateCopy(reader.ResolveStream(holder.Get(path[i])));
                if (inner is null) return false;
                holder.Set(path[i], Register(inner));
                FormUse.Copies[key] = inner;
            }
            form = inner;
        }
        Page.MarkDirty();
        SourceStream = form;
        return OverwriteSpan(string.Empty);
    }

    /// <summary>The page's /XObject dictionary, as the page's own: a page drawing with an inherited resources
    /// dictionary gets a copy of it, and an /XObject dictionary held as a separate object (which other pages
    /// may share) is copied into the page's resources.</summary>
    private PdfDictionary? PageXObjects(PdfReader reader)
    {
        var resources = Forms.Form.EnsureOwnPageResources(Page!.Dict, reader);
        var xobjects = reader.ResolveDict(resources.Get("XObject"));
        if (xobjects is not null && resources.Get("XObject") is PdfIndirectRef)
        {
            xobjects = Copy(xobjects);
            resources.Set("XObject", xobjects);
        }
        return xobjects;
    }

    /// <summary>Make this placement's use of the page-level form <paramref name="name"/> draw <paramref name="copy"/>:
    /// through the entry itself when the page draws the form once, else through a fresh entry its Do is renamed to.</summary>
    private void PointPageUseAt(PdfDictionary holder, string name, PdfStream copy)
    {
        var reference = Register(copy);
        FormUse!.PageUses.TryGetValue(name, out var uses);
        if (uses <= 1)
        {
            holder.Set(name, reference);
            return;
        }
        FormUse.Renamed.TryGetValue(name, out var renamed);
        var drawings = new List<Operators.Do>();
        var ops = Page!.Contents;
        for (var i = 1; i <= ops.Count; i++)
        {
            if (ops[i] is Operators.Do d && (d.Name == name || renamed?.Contains(d.Name) == true)) drawings.Add(d);
        }
        if (FormUse.Ordinal < 0 || FormUse.Ordinal >= drawings.Count)
        {
            holder.Set(name, reference);
            return;
        }
        var n = 1;
        while (holder.ContainsKey(FormNamePrefix + n)) n++;
        var fresh = FormNamePrefix + n;
        holder.Set(fresh, reference);
        drawings[FormUse.Ordinal].Name = fresh;
        ops.MarkEdited();
        if (renamed is null) FormUse.Renamed[name] = renamed = new List<string>();
        renamed.Add(fresh);
    }

    /// <summary>A copy of a form for one use: its content decoded, since a new object is outside the file's
    /// filters and encryption, and its resources and their /XObject dictionary copied one level, so an entry
    /// re-pointed in the copy leaves the original's alone.</summary>
    private PdfStream? PrivateCopy(PdfStream? form)
    {
        if (form is null) return null;
        var reader = SourceReader!;
        byte[] content;
        try { content = reader.DecodeStream(form); }
        catch (Exception e) when (e is InvalidDataException or NotSupportedException or IndexOutOfRangeException)
        {
            return null;
        }
        var dict = Copy(form.Dict);
        dict.Remove("Filter");
        dict.Remove("DecodeParms");
        dict.Set("Length", new PdfInteger(content.Length));
        if (reader.ResolveDict(form.Dict.Get("Resources")) is { } resources)
        {
            var own = Copy(resources);
            if (reader.ResolveDict(resources.Get("XObject")) is { } xobjects) own.Set("XObject", Copy(xobjects));
            dict.Set("Resources", own);
        }
        var copy = new PdfStream(dict, Array.Empty<byte>());
        copy.ReplaceData(content); // plain bytes of this session's making, never the file's ciphertext
        return copy;
    }

    /// <summary>Enter the copy as a new object of the document. The original may be drawn nowhere after the
    /// re-pointing; the save's reachability pass then leaves it out.</summary>
    private PdfObject Register(PdfStream copy)
    {
        var document = SourceReader!.OwnerDocument;
        if (document is null) return copy;
        copy.ObjectNumber = document.AllocateObjectNumber();
        document.AddNewObject(copy.ObjectNumber, copy, registerOverlay: true);
        SourceReader.MayHaveOrphansOnSave = true;
        return new PdfIndirectRef(copy.ObjectNumber, 0);
    }

    private static PdfDictionary Copy(PdfDictionary dict)
    {
        var copy = new PdfDictionary();
        foreach (var key in dict.Keys) copy.Set(key, dict.Get(key)!);
        return copy;
    }
}
