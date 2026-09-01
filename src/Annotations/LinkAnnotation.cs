using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class LinkAnnotation : Annotation
{
    internal LinkAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a new link annotation for the given page and rectangle.</summary>
    public LinkAnnotation(Page page, Rectangle rect) : base(page, rect)
    {
        Dict.Set("Subtype", new PdfName("Link"));
    }

    /// <summary>Wrap an existing link-annotation object (e.g. the object an
    /// <see cref="Aspose.Pdf.LogicalStructure.OBJRElement"/> references) in the given document.</summary>
    public LinkAnnotation(object obj, Document document)
        : base(ResolveAnnotDict(obj, document), document.Reader)
    {
    }

    private static PdfDictionary ResolveAnnotDict(object obj, Document document)
    {
        var resolved = obj switch
        {
            PdfDictionary d => d,
            PdfObject p => document.Reader.ResolveDict(p),
            _ => null,
        };
        return resolved ?? throw new ArgumentException("Object does not resolve to an annotation dictionary.", nameof(obj));
    }

    /// <summary>Highlighting mode when the link is activated (/H entry, live —
    /// an assignment persists and reads back after save/reload; absent means
    /// the spec default Invert).</summary>
    public HighlightingMode Highlighting
    {
        get => Dict.GetName("H") switch
        {
            "N" => HighlightingMode.None,
            "O" => HighlightingMode.Outline,
            "P" => HighlightingMode.Push,
            _ => HighlightingMode.Invert,
        };
        set => Dict.Set("H", new PdfName(value switch
        {
            HighlightingMode.None => "N",
            HighlightingMode.Outline => "O",
            HighlightingMode.Push => "P",
            _ => "I",
        }));
    }

    /// <summary>The destination for this link annotation (ExplicitDestination, NamedDestination, or null).</summary>
    public IAppointment? Destination
    {
        get
        {
            var destObj = InternalReader.Resolve(Dict.Get("Dest"));
            if (destObj is PdfArray arr)
                return ExplicitDestination.FromArray(arr, InternalReader);
            if (destObj is PdfString s)
                return new NamedDestination(s.ToText());
            if (destObj is PdfName n)
                return new NamedDestination(n.Value);
            // Check action for GoTo
            var action = InternalReader.ResolveDict(Dict.Get("A"));
            if (action is not null && action.GetName("S") == "GoTo")
            {
                var d = InternalReader.Resolve(action.Get("D"));
                if (d is PdfArray destArr)
                    return ExplicitDestination.FromArray(destArr, InternalReader);
                if (d is PdfString ds)
                    return new NamedDestination(ds.ToText());
            }
            return null;
        }
        set
        {
            if (value is ExplicitDestination ed)
            {
                Dict.Set("Dest", ed.ToPdfArray());
                Dict.Remove("A"); // Remove action when setting explicit destination
            }
            else if (value is NamedDestination nd)
            {
                Dict.Set("Dest", new PdfString(System.Text.Encoding.Latin1.GetBytes(nd.Name)));
                Dict.Remove("A");
            }
            else if (value is null)
            {
                Dict.Remove("Dest");
            }
        }
    }

    public string? Uri
    {
        get
        {
            var action = InternalReader.ResolveDict(Dict.Get("A"));
            if (action is null) return null;
            if (action.GetName("S") != "URI") return null;
            var uri = InternalReader.Resolve(action.Get("URI"));
            return uri is PdfString s ? s.ToText() : null;
        }
    }

    /// <summary>
    /// Target page number (1-based) for GoTo/GoToR link actions, or null if the action
    /// is not a page link (e.g. URI, JavaScript).
    /// </summary>
    public int? TargetPageNumber
    {
        get
        {
            var action = InternalReader.ResolveDict(Dict.Get("A"));
            if (action is not null)
            {
                var subtype = action.GetName("S");
                if (subtype != "GoTo" && subtype != "GoToR")
                    return null; // URI, JavaScript, etc.
                var dest = InternalReader.Resolve(action.Get("D"));
                return ResolveDestPageNumber(dest);
            }
            // Direct /Dest on annotation
            return ResolveDestPageNumber(InternalReader.Resolve(Dict.Get("Dest")));
        }
    }

    private int? ResolveDestPageNumber(PdfObject? dest)
    {
        if (dest is null) return null;
        if (dest is PdfArray arr && arr.Count > 0)
        {
            var pageRef = InternalReader.Resolve(arr[0]);
            if (pageRef is PdfInteger idx)
                return (int)(idx.Value + 1); // 0-based to 1-based for GoToR remote
        }
        return null;
    }

    /// <summary>Always <see cref="AnnotationType.Link"/>.</summary>
    public new AnnotationType AnnotationType => AnnotationType.Link;

    /// <summary>The /A entry parsed as a <see cref="PdfAction"/>. Setting writes the action dictionary.</summary>
    public new PdfAction? Action
    {
        get
        {
            var aDict = InternalReader.ResolveDict(Dict.Get("A"));
            return aDict is null ? null : PdfAction.Create(aDict, InternalReader);
        }
        set
        {
            if (value is null) Dict.Remove("A");
            else Dict.Set("A", value.Dict);
        }
    }
}
