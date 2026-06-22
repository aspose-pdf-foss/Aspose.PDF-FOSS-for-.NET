using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

/// <summary>
/// The type of a PDF action.
/// </summary>
public enum ActionType
{
    Unknown,
    GoTo,
    GoToR,
    URI,
    Launch,
    Named,
    JavaScript,
    Hide,
    SubmitForm,
    ResetForm,
    ImportData,
    Rendition,
}

/// <summary>
/// Base class for PDF actions.
/// </summary>
public class PdfAction : IAppointment
{
    private readonly PdfDictionary _dict;
    private readonly PdfReader _reader;

    internal PdfAction(PdfDictionary dict, PdfReader reader)
    {
        _dict = dict;
        _reader = reader;
    }

    /// <summary>
    /// Internal constructor for building actions without a reader (factory use).
    /// </summary>
    internal PdfAction(PdfDictionary dict)
    {
        _dict = dict;
        _reader = null!;
    }

    /// <summary>The action type.</summary>
    public ActionType Type => ParseType(_dict.GetName("S"));

    /// <summary>The /Next chain — actions that fire after this one.</summary>
    public Annotations.ActionCollection Next
    {
        get
        {
            var col = new Annotations.ActionCollection();
            if (_reader is null) return col;
            var nextObj = _reader.Resolve(_dict.Get("Next"));
            if (nextObj is PdfDictionary single)
            {
                col.Add(Create(single, _reader));
            }
            else if (nextObj is PdfArray arr)
            {
                foreach (var item in arr)
                {
                    var d = _reader.ResolveDict(item);
                    if (d is not null) col.Add(Create(d, _reader));
                }
            }
            return col;
        }
    }

    /// <summary>For JavaScript actions, returns the embedded script; otherwise an empty string.</summary>
    public string GetECMAScriptString()
        => this is JavascriptAction js ? js.Script : string.Empty;

    internal PdfDictionary Dict => _dict;
    internal PdfReader Reader => _reader;

    internal static PdfAction Create(PdfDictionary dict, PdfReader reader)
    {
        var s = dict.GetName("S");
        return s switch
        {
            "GoTo" => new GoToAction(dict, reader),
            "URI" => new GoToURIAction(dict, reader),
            "GoToR" => new GoToRemoteAction(dict, reader),
            "Launch" => new LaunchAction(dict, reader),
            "Named" => new NamedAction(dict, reader),
            "JavaScript" => new JavascriptAction(dict, reader),
            "SubmitForm" => new SubmitFormAction(dict, reader),
            "ImportData" => new ImportDataAction(dict, reader),
            _ => new PdfAction(dict, reader),
        };
    }

    // ── Static Factory Methods ─────────────────────────────────────────────

    /// <summary>
    /// Create a GoTo action targeting a page index with the specified fit type.
    /// The page reference will be resolved when the action is attached to a document.
    /// </summary>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="fitType">Fit type: "Fit", "FitH", "FitV", or "XYZ".</param>
    public static PdfAction CreateGoTo(int pageIndex, string fitType = "Fit")
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoTo"));

        // Store the target page index in a temporary integer so the destination
        // array can be finalized when the action is attached to a document.
        // For now, we build the /D array with the page index as an integer
        // placeholder — the writer or the document will replace it with the
        // actual page indirect reference.
        var dest = new PdfArray();
        dest.Add(new PdfInteger(pageIndex));
        dest.Add(new PdfName(fitType));
        dict.Set("D", dest);

        return new PdfAction(dict);
    }

    /// <summary>
    /// Create a GoTo action targeting a page with XYZ fit (specific position and zoom).
    /// </summary>
    /// <param name="pageIndex">0-based page index.</param>
    /// <param name="left">Left coordinate, or null for default.</param>
    /// <param name="top">Top coordinate, or null for default.</param>
    /// <param name="zoom">Zoom factor (1.0 = 100%), or null for default.</param>
    public static PdfAction CreateGoTo(int pageIndex, double? left, double? top, double? zoom)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoTo"));

        var dest = new PdfArray();
        dest.Add(new PdfInteger(pageIndex));
        dest.Add(new PdfName("XYZ"));
        dest.Add(left.HasValue ? new PdfReal(left.Value) : PdfNull.Instance);
        dest.Add(top.HasValue ? new PdfReal(top.Value) : PdfNull.Instance);
        dest.Add(zoom.HasValue ? new PdfReal(zoom.Value) : PdfNull.Instance);
        dict.Set("D", dest);

        return new PdfAction(dict);
    }

    /// <summary>
    /// Create a URI action that opens the specified URL.
    /// </summary>
    public static PdfAction CreateUri(string uri)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("URI"));
        dict.Set("URI", new PdfString(Encoding.Latin1.GetBytes(uri)));
        return new PdfAction(dict);
    }

    /// <summary>
    /// Create a JavaScript action that executes the specified script.
    /// </summary>
    public static PdfAction CreateJavaScript(string script)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("JavaScript"));
        dict.Set("JS", new PdfString(Encoding.Latin1.GetBytes(script)));
        return new PdfAction(dict);
    }

    /// <summary>
    /// Create a Named action (e.g., NextPage, PrevPage, FirstPage, LastPage).
    /// </summary>
    public static PdfAction CreateNamed(string name)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("Named"));
        dict.Set("N", new PdfName(name));
        return new PdfAction(dict);
    }

    /// <summary>
    /// Create a Launch action that opens the specified file.
    /// </summary>
    public static PdfAction CreateLaunch(string filePath)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("Launch"));
        dict.Set("F", new PdfString(Encoding.Latin1.GetBytes(filePath)));
        return new PdfAction(dict);
    }

    private static ActionType ParseType(string? s) => s switch
    {
        "GoTo" => ActionType.GoTo,
        "GoToR" => ActionType.GoToR,
        "URI" => ActionType.URI,
        "Launch" => ActionType.Launch,
        "Named" => ActionType.Named,
        "JavaScript" => ActionType.JavaScript,
        "Hide" => ActionType.Hide,
        "SubmitForm" => ActionType.SubmitForm,
        "ResetForm" => ActionType.ResetForm,
        "ImportData" => ActionType.ImportData,
        "Rendition" => ActionType.Rendition,
        _ => ActionType.Unknown,
    };
}

public sealed class GoToAction : PdfAction
{
    internal GoToAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>
    /// Create a GoTo action targeting the given explicit destination (API parity).
    /// </summary>
    public GoToAction(Annotations.ExplicitDestination destination)
        : base(BuildDict(destination))
    {
    }

    /// <summary>
    /// Create a GoTo action targeting the first view of the given page (API parity).
    /// </summary>
    public GoToAction(Page page)
        : base(BuildPageDict(page))
    {
    }

    /// <summary>
    /// Create a GoTo action targeting the page at the given 1-based page number (API parity).
    /// </summary>
    public GoToAction(int page)
        : base(BuildDict(new Annotations.FitExplicitDestination(page)))
    {
    }

    /// <summary>Create an empty GoTo action (API parity).</summary>
    public GoToAction()
        : base(EmptyGoToDict())
    {
    }

    /// <summary>Named-destination GoTo action (/D entry is a /name).</summary>
    public GoToAction(Document doc, string name)
        : base(BuildNamedDestDict(name))
    {
        _ = doc;
    }

    /// <summary>Direct GoTo action with an explicit destination type and value array.</summary>
    public GoToAction(Page page, Annotations.ExplicitDestinationType type, double[] values)
        : base(BuildDict(Annotations.ExplicitDestination.CreateDestination(page, type, values)))
    {
    }

    private static PdfDictionary BuildNamedDestDict(string name)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoTo"));
        if (!string.IsNullOrEmpty(name))
            dict.Set("D", new PdfName(name));
        return dict;
    }

    private static PdfDictionary BuildDict(Annotations.ExplicitDestination destination)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoTo"));
        // ExplicitDestination.ToPdfArray emits page index in 0-based form.
        dict.Set("D", destination.ToPdfArrayPublic());
        return dict;
    }

    private static PdfDictionary BuildPageDict(Page page)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoTo"));
        // Default fit for a page-only GoTo is /Fit (fit whole page to window),
        // matching the public API behavior.
        var dest = new PdfArray();
        dest.Add(new PdfInteger((page?.Number ?? 1) - 1));
        dest.Add(new PdfName("Fit"));
        dict.Set("D", dest);
        return dict;
    }

    private static PdfDictionary EmptyGoToDict()
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoTo"));
        return dict;
    }

    /// <summary>The destination this action points to. May be an explicit destination
    /// or any other <see cref="Annotations.IAppointment"/> implementor.</summary>
    public Annotations.IAppointment? Destination
    {
        get
        {
            // /D may be an indirect reference, a named destination (string/name), or a destination array.
            var dest = Reader is null ? Dict.Get("D") : Reader.Resolve(Dict.Get("D"));
            if (dest is PdfArray arr)
            {
                var ed = Annotations.ExplicitDestination.FromArray(arr, Reader);
                if (ed is not null && Reader is not null && ed.PageNumber > 0)
                    ed.Page = ResolvePageByNumber(Reader, ed.PageNumber);
                return ed;
            }
            // Named destination (/D is a string or name): resolve through the catalog's
            // /Dests dict or /Names→/Dests name tree (PDF 32000 §12.3.2.3).
            if (Reader is not null && dest is PdfString or PdfName)
            {
                var name = dest is PdfString s ? s.ToText() : ((PdfName)dest).Value;
                return new NamedDestinationCollection(Reader.Catalog, Reader)[name]
                    ?? new NamedDestination(name);
            }
            return null;
        }
        set
        {
            if (value is null) Dict.Remove("D");
            else if (value is Annotations.ExplicitDestination ed) Dict.Set("D", ed.ToPdfArrayPublic());
            // Other IAppointment kinds aren't currently serialised — stored as no-op.
        }
    }

    internal static Page? ResolvePageByNumber(PdfReader reader, int pageNumber)
    {
        var catalog = reader.Catalog;
        var pagesDict = reader.ResolveDict(catalog.Get("Pages"));
        if (pagesDict is null) return null;
        var counter = 0;
        return FindPageByIndex(reader, pagesDict, pageNumber, ref counter);
    }

    private static Page? FindPageByIndex(PdfReader reader, PdfDictionary node, int targetOneBasedIndex, ref int counter)
    {
        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return null;
        foreach (var kid in kids)
        {
            var kidDict = reader.ResolveDict(kid);
            if (kidDict is null) continue;
            var type = kidDict.GetName("Type");
            if (type == "Page")
            {
                counter++;
                if (counter == targetOneBasedIndex)
                    return new Page(kidDict, reader, counter - 1);
            }
            else if (type == "Pages")
            {
                var found = FindPageByIndex(reader, kidDict, targetOneBasedIndex, ref counter);
                if (found is not null) return found;
            }
        }
        return null;
    }

    /// <summary>The destination page number (0-based), or -1 if not resolved.</summary>
    public int DestinationPageIndex
    {
        get
        {
            var dest = Reader.Resolve(Dict.Get("D"));
            if (dest is not PdfArray arr || arr.Count == 0)
                return -1;

            var pageRef = arr[0];

            // If the first element is an integer, it was created by our factory
            if (pageRef is PdfInteger pageInt)
                return (int)pageInt.Value;

            // If it's an indirect reference, resolve to a page dict and look up in the page tree
            if (pageRef is PdfIndirectRef indRef)
            {
                var pageDict = Reader.ResolveDict(pageRef);
                if (pageDict is not null)
                    return FindPageIndex(pageDict, indRef.ObjectNumber);
            }

            return -1;
        }
    }

    /// <summary>The fit type of the destination (e.g., "Fit", "FitH", "XYZ").</summary>
    public string? FitType
    {
        get
        {
            var dest = Reader.Resolve(Dict.Get("D"));
            if (dest is PdfArray arr && arr.Count > 1 && arr[1] is PdfName name)
                return name.Value;
            return null;
        }
    }

    private int FindPageIndex(PdfDictionary targetPageDict, int targetObjNum)
    {
        // Walk the page tree to find the index of the target page
        var catalog = Reader.Catalog;
        var pagesDict = Reader.ResolveDict(catalog.Get("Pages"));
        if (pagesDict is null) return -1;

        var index = 0;
        return FindPageInTree(pagesDict, targetObjNum, ref index) ? index : -1;
    }

    private bool FindPageInTree(PdfDictionary node, int targetObjNum, ref int index)
    {
        var type = node.GetName("Type");
        if (type == "Page")
        {
            // Can't directly compare dict identity — we'd need object numbers
            // This path is reached when node is directly embedded (rare)
            return false;
        }

        var kids = Reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is null) return false;

        foreach (var kid in kids)
        {
            if (kid is PdfIndirectRef kidRef)
            {
                if (kidRef.ObjectNumber == targetObjNum)
                    return true;

                var kidDict = Reader.ResolveDict(kid);
                if (kidDict is null) continue;

                var kidType = kidDict.GetName("Type");
                if (kidType == "Page")
                {
                    if (kidRef.ObjectNumber == targetObjNum)
                        return true;
                    index++;
                }
                else if (kidType == "Pages")
                {
                    if (FindPageInTree(kidDict, targetObjNum, ref index))
                        return true;
                }
            }
            else
            {
                var kidDict = Reader.ResolveDict(kid);
                if (kidDict is not null)
                {
                    var kidType = kidDict.GetName("Type");
                    if (kidType == "Page")
                        index++;
                    else if (kidType == "Pages")
                    {
                        if (FindPageInTree(kidDict, targetObjNum, ref index))
                            return true;
                    }
                }
            }
        }

        return false;
    }
}

public sealed class UriAction : PdfAction
{
    internal UriAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public string? Uri
    {
        get
        {
            var obj = Reader.Resolve(Dict.Get("URI"));
            return obj is PdfString s ? s.ToText() : null;
        }
    }
}

/// <summary>
/// Alias for UriAction, matching the public API name.
/// Creates a URI action (GoTo URI) that navigates to a web URL.
/// </summary>
public sealed class GoToURIAction : PdfAction
{
    /// <summary>
    /// Create a new GoToURIAction targeting the specified URI.
    /// </summary>
    public GoToURIAction(string uri) : base(BuildDict(uri))
    {
    }

    internal GoToURIAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>The target URI.</summary>
    public string? URI
    {
        get
        {
            var obj = Reader?.Resolve(Dict.Get("URI"));
            if (obj is PdfString s) return s.ToText();
            // For newly created actions where Reader is null, read directly
            return Dict.Get("URI") is PdfString ps ? ps.ToText() : null;
        }
        set
        {
            if (value is null) Dict.Remove("URI");
            else Dict.Set("URI", new PdfString(System.Text.Encoding.UTF8.GetBytes(value)));
        }
    }

    private static PdfDictionary BuildDict(string uri)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("URI"));
        dict.Set("URI", new PdfString(Encoding.Latin1.GetBytes(uri)));
        return dict;
    }
}

/// <summary>
/// Go-to-remote action — jumps to a destination in a different PDF file
///.
/// </summary>
public sealed class GoToRemoteAction : PdfAction
{
    internal GoToRemoteAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a GoToRemote action targeting a file path with the given destination.</summary>
    public GoToRemoteAction(string remotePdf, Annotations.ExplicitDestination destination)
        : base(BuildRemoteDict(remotePdf, destination))
    {
    }

    /// <summary>Create a GoToRemote action targeting a file path with a page/fit destination.</summary>
    public GoToRemoteAction(string remotePdf, int remotePageNumber)
        : base(BuildRemoteDict(remotePdf, new Annotations.FitExplicitDestination(remotePageNumber)))
    {
    }

    /// <summary>When the action should open the destination in a new viewer window.</summary>
    public ExtendedBoolean NewWindow
    {
        get => Dict.Get("NewWindow") is PdfBoolean b ? (b.Value ? ExtendedBoolean.True : ExtendedBoolean.False) : ExtendedBoolean.Undefined;
        set
        {
            switch (value)
            {
                case ExtendedBoolean.True: Dict.Set("NewWindow", PdfBoolean.True); break;
                case ExtendedBoolean.False: Dict.Set("NewWindow", PdfBoolean.False); break;
                default: Dict.Remove("NewWindow"); break;
            }
        }
    }

    private static PdfDictionary BuildRemoteDict(string fileName, Annotations.ExplicitDestination destination)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("GoToR"));
        dict.Set("F", new PdfString(Encoding.Latin1.GetBytes(fileName)));
        dict.Set("D", destination.ToPdfArrayPublic());
        return dict;
    }

    /// <summary>The target file specification (/F entry).</summary>
    public FileSpecification? File
    {
        get
        {
            var fObj = Reader?.Resolve(Dict.Get("F")) ?? Dict.Get("F");
            return fObj switch
            {
                PdfString s => FileSpecification.CreateFromName(s.ToText()),
                PdfDictionary fsDict => FileSpecification.FromExistingDict(fsDict, Reader),
                _ => null,
            };
        }
        set
        {
            if (value is null) Dict.Remove("F");
            else Dict.Set("F", value.Dict);
        }
    }

    /// <summary>The destination within the target file.</summary>
    public Annotations.IAppointment? Destination
    {
        get
        {
            var d = Reader?.Resolve(Dict.Get("D"));
            return d is PdfArray arr ? Annotations.ExplicitDestination.FromArray(arr, Reader) : null;
        }
        set
        {
            if (value is null) Dict.Remove("D");
            else if (value is Annotations.ExplicitDestination ed) Dict.Set("D", ed.ToPdfArrayPublic());
        }
    }
}

public sealed class LaunchAction : PdfAction
{
    internal LaunchAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    public LaunchAction(string file) : base(BuildDict(file))
    {
    }

    /// <summary>Document-bound launch action; <paramref name="document"/> is stored for future binding.</summary>
    public LaunchAction(Document document, string file) : base(BuildDict(file))
    {
        _ = document;
    }

    private static PdfDictionary BuildDict(string file)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("Launch"));
        dict.Set("F", new PdfString(System.Text.Encoding.UTF8.GetBytes(file ?? string.Empty)));
        return dict;
    }

    public string File
    {
        get
        {
            var obj = Reader?.Resolve(Dict.Get("F")) ?? Dict.Get("F");
            // /F may be a simple string, a file-specification dictionary
            // (/UF or /F path), or a platform /Win sub-dictionary (/Win /F).
            if (obj is PdfString s) return s.ToText();
            if (obj is PdfDictionary fs)
            {
                var path = ResolveFileString(fs, "UF") ?? ResolveFileString(fs, "F");
                if (path is not null) return path;
                var winInner = Reader?.ResolveDict(fs.Get("Win")) ?? fs.Get("Win") as PdfDictionary;
                if (winInner is not null)
                {
                    var winPath = ResolveFileString(winInner, "F");
                    if (winPath is not null) return winPath;
                }
            }
            // A Launch action may instead carry the file only under /Win.
            var win = Reader?.ResolveDict(Dict.Get("Win")) ?? Dict.Get("Win") as PdfDictionary;
            if (win is not null)
            {
                var winPath = ResolveFileString(win, "F");
                if (winPath is not null) return winPath;
            }
            return string.Empty;
        }
        set => Dict.Set("F", new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    private string? ResolveFileString(PdfDictionary dict, string key)
    {
        var v = Reader?.Resolve(dict.Get(key)) ?? dict.Get(key);
        return v is PdfString s ? s.ToText() : null;
    }

    /// <summary>When the action should open the file in a new viewer window.</summary>
    public ExtendedBoolean NewWindow
    {
        get => Dict.Get("NewWindow") is PdfBoolean b ? (b.Value ? ExtendedBoolean.True : ExtendedBoolean.False) : ExtendedBoolean.Undefined;
        set
        {
            switch (value)
            {
                case ExtendedBoolean.True: Dict.Set("NewWindow", PdfBoolean.True); break;
                case ExtendedBoolean.False: Dict.Set("NewWindow", PdfBoolean.False); break;
                default: Dict.Remove("NewWindow"); break;
            }
        }
    }
}

public sealed class NamedAction : PdfAction
{
    internal NamedAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Construct from a predefined viewer action (PDF 32000 §12.6.4.11).</summary>
    public NamedAction(Annotations.PredefinedAction action) : base(BuildDict(action.ToString()))
    {
    }

    private static PdfDictionary BuildDict(string name)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("Named"));
        dict.Set("N", new PdfName(name ?? string.Empty));
        return dict;
    }

    /// <summary>Named action name (/N entry).</summary>
    public string Name
    {
        get => Dict.GetName("N") ?? string.Empty;
        set => Dict.Set("N", new PdfName(value ?? string.Empty));
    }
}

public sealed class JavascriptAction : PdfAction
{
    internal JavascriptAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create a JavaScript action with the given script source (API parity).</summary>
    public JavascriptAction(string javaScript) : base(BuildDict(javaScript))
    {
    }

    private static PdfDictionary BuildDict(string script)
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("JavaScript"));
        dict.Set("JS", new PdfString(System.Text.Encoding.UTF8.GetBytes(script ?? string.Empty)));
        return dict;
    }

    public string Script
    {
        get
        {
            var obj = Reader?.Resolve(Dict.Get("JS")) ?? Dict.Get("JS");
            if (obj is PdfString s) return s.ToText();
            if (obj is PdfStream stream && Reader is not null)
            {
                var data = Reader.DecodeStream(stream);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            return string.Empty;
        }
        set => Dict.Set("JS", new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }
}

/// <summary>
/// Submit-form action — sends form field data to a URL or remote file
/// (PDF 32000-1:2008 §12.7.5.2).
/// </summary>
public sealed class SubmitFormAction : PdfAction
{
    /// <summary>Treat the listed field array as an exclude-list rather than
    /// an include-list (bit 1 of /Flags per PDF spec table 237).</summary>
    public const int Exclude = 1;

    /// <summary>Submit empty fields as well as filled ones
    /// (bit 2 of /Flags).</summary>
    public const int IncludeNoValueFields = 2;

    /// <summary>Submit field names and values in HTML form-encoded
    /// format (bit 3 of /Flags).</summary>
    public const int ExportFormat = 4;

    /// <summary>Use HTTP GET instead of POST when submitting the form
    /// (bit 4 of /Flags).</summary>
    public const int GetMethod = 8;

    /// <summary>Include the submit-button click coordinates with the
    /// posted data (bit 5 of /Flags).</summary>
    public const int SubmitCoordinates = 16;

    /// <summary>Submit the field data as XFDF rather than FDF
    /// (bit 6 of /Flags). Spelled "Xfdf" to match the Aspose.PDF for .NET member
    /// name; the wire format is uppercase XFDF.</summary>
    public const int Xfdf = 32;

    /// <summary>Include incremental-save update entries in the FDF
    /// payload (bit 7 of /Flags).</summary>
    public const int IncludeAppendSaves = 64;

    /// <summary>Include markup annotations in the FDF payload
    /// (bit 8 of /Flags).</summary>
    public const int IncludeAnnotations = 128;

    /// <summary>Submit the entire PDF as the request body
    /// (bit 9 of /Flags). Spelled "SubmitPdf" to match the Aspose.PDF for .NET
    /// member name.</summary>
    public const int SubmitPdf = 256;

    /// <summary>Convert dates to standard PDF canonical format before
    /// submission (bit 10 of /Flags).</summary>
    public const int CanonicalFormat = 512;

    /// <summary>Exclude non-user-edited markup annotations when
    /// /IncludeAnnotations is set (bit 11 of /Flags).</summary>
    public const int ExclNonUserAnnots = 1024;

    /// <summary>Exclude the /F (font) entry from each annotation when
    /// /IncludeAnnotations is set (bit 12 of /Flags).</summary>
    public const int ExclFKey = 2048;

    /// <summary>Embed the PDF inside the submitted FDF payload
    /// (bit 14 of /Flags).</summary>
    public const int EmbedForm = 8192;

    internal SubmitFormAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>Create an empty SubmitForm action. The Url and Flags
    /// must be set before the action is attached to a button.</summary>
    public SubmitFormAction() : base(BuildDict()) { }

    private static PdfDictionary BuildDict()
    {
        var dict = new PdfDictionary();
        dict.Set("S", new PdfName("SubmitForm"));
        return dict;
    }

    /// <summary>The destination URL/file specification (/F entry).</summary>
    public FileSpecification? Url
    {
        get
        {
            var fObj = Reader?.Resolve(Dict.Get("F")) ?? Dict.Get("F");
            return fObj switch
            {
                PdfString s => FileSpecification.CreateFromName(s.ToText()),
                PdfDictionary fsDict => FileSpecification.FromExistingDict(fsDict, Reader),
                _ => null,
            };
        }
        set
        {
            if (value is null) Dict.Remove("F");
            else Dict.Set("F", value.Dict);
        }
    }

    /// <summary>The flags integer (/Flags entry) — bitwise OR of
    /// submit-form flags per PDF spec table 237.</summary>
    public int Flags
    {
        get
        {
            var obj = Reader?.Resolve(Dict.Get("Flags")) ?? Dict.Get("Flags");
            return obj is PdfInteger n ? (int)n.Value : 0;
        }
        set => Dict.Set("Flags", new PdfInteger(value));
    }
}

/// <summary>An import-data action (/S /ImportData): imports field data from the
/// FDF/XFDF file identified by the action's /F file specification.</summary>
public sealed class ImportDataAction : PdfAction
{
    internal ImportDataAction(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }

    /// <summary>The file specification (/F) naming the data file to import.
    /// Accepts both the literal-string and the /Filespec-dictionary forms.</summary>
    public FileSpecification? Data
    {
        get
        {
            var f = Reader?.Resolve(Dict.Get("F")) ?? Dict.Get("F");
            return f switch
            {
                PdfDictionary fd => FileSpecification.FromExistingDict(fd, Reader),
                PdfString ps => FileSpecification.CreateFromName(ps.ToText()),
                _ => null,
            };
        }
    }
}
