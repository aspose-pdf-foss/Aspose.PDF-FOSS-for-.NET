using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class MarkupAnnotation : Annotation
{
    internal MarkupAnnotation(PdfDictionary dict, PdfReader reader) : base(dict, reader) { }
    protected MarkupAnnotation(Page page, Rectangle rect) : base(page, rect) { CreationDate = System.DateTime.Now; }
    protected MarkupAnnotation(Document document, Rectangle rect) : base(document, rect) { CreationDate = System.DateTime.Now; }

    /// <summary>Document-bound ctor for creating a markup annotation that
    /// isn't yet attached to a specific page; callers add it later via
    /// <c>page.Annotations.Add(annot)</c>.</summary>
    public MarkupAnnotation(Document document) : base(document, rect: null!) { CreationDate = System.DateTime.Now; }

    /// <summary>Set default QuadPoints from the annotation rectangle (4 corners).</summary>
    protected void SetDefaultQuadPoints(Rectangle rect)
    {
        if (rect is null) return;
        var arr = new PdfArray();
        // QuadPoints order: x1,y1 x2,y2 x3,y3 x4,y4 (LL, LR, UL, UR per spec, but commonly: UL, UR, LL, LR)
        arr.Add(new PdfReal(rect.LLX)); arr.Add(new PdfReal(rect.URY)); // upper-left
        arr.Add(new PdfReal(rect.URX)); arr.Add(new PdfReal(rect.URY)); // upper-right
        arr.Add(new PdfReal(rect.LLX)); arr.Add(new PdfReal(rect.LLY)); // lower-left
        arr.Add(new PdfReal(rect.URX)); arr.Add(new PdfReal(rect.LLY)); // lower-right
        Dict.Set("QuadPoints", arr);
    }

    // ── Review / marked-state surface (PDF 32000 §12.5.6.3) ─────────────────

    /// <summary>Read the /State entry on the annotation's properties
    /// dictionary. Returns <see cref="Aspose.Pdf.Annotations.AnnotationState.None"/> when no
    /// state has been recorded.</summary>
    public AnnotationState GetState()
    {
        return ResolveReviewStateValue("State") switch
        {
            "Marked" => Aspose.Pdf.Annotations.AnnotationState.Marked,
            "Unmarked" => Aspose.Pdf.Annotations.AnnotationState.Unmarked,
            "Accepted" => Aspose.Pdf.Annotations.AnnotationState.Accepted,
            "Rejected" => Aspose.Pdf.Annotations.AnnotationState.Rejected,
            "Cancelled" => Aspose.Pdf.Annotations.AnnotationState.Cancelled,
            "Completed" => Aspose.Pdf.Annotations.AnnotationState.Completed,
            "None" => Aspose.Pdf.Annotations.AnnotationState.None,
            // No /State anywhere (e.g. after ClearState) reads back as None.
            _ => Aspose.Pdf.Annotations.AnnotationState.None,
        };
    }

    /// <summary>Read the /StateModel entry, mapping the missing /entry to
    /// <see cref="Aspose.Pdf.Annotations.AnnotationStateModel.Undefined"/>.</summary>
    public AnnotationStateModel GetStateModel() => ResolveReviewStateValue("StateModel") switch
    {
        "Marked" => Aspose.Pdf.Annotations.AnnotationStateModel.Marked,
        "Review" => Aspose.Pdf.Annotations.AnnotationStateModel.Review,
        _ => Aspose.Pdf.Annotations.AnnotationStateModel.Undefined,
    };

    // /State and /StateModel are text strings (PDF §12.5.6.4), not names.
    private static PdfString StateString(string value) =>
        new PdfString(System.Text.Encoding.Latin1.GetBytes(value));

    /// <summary>Set /State to Marked or Unmarked plus /StateModel = Marked.</summary>
    public void SetMarkedState(bool marked)
    {
        Dict.Set("State", StateString(marked ? "Marked" : "Unmarked"));
        Dict.Set("StateModel", StateString("Marked"));
    }

    /// <summary>Set the review state. The state is recorded on this
    /// annotation (/State + /StateModel = Review) and, when the annotation
    /// is attached to a page, also on a reply annotation (/IRT → this) that
    /// <see cref="FindStateAnnotation"/> resolves after a save/reload, per
    /// PDF 32000 §12.5.6.3.</summary>
    public void SetReviewState(AnnotationState state)
    {
        Dict.Set("State", StateString(state.ToString()));
        Dict.Set("StateModel", StateString("Review"));
        AttachStateReply(state.ToString(), "Review", Title ?? string.Empty);
    }

    /// <summary>Set the review state along with the reviewer's username
    /// (recorded in /T per the PDF spec).</summary>
    public void SetReviewState(AnnotationState state, string userName)
    {
        Dict.Set("State", StateString(state.ToString()));
        Dict.Set("StateModel", StateString("Review"));
        if (!string.IsNullOrEmpty(userName))
            Dict.Set("T", new PdfString(System.Text.Encoding.UTF8.GetBytes(userName)));
        AttachStateReply(state.ToString(), "Review",
            string.IsNullOrEmpty(userName) ? (Title ?? string.Empty) : userName);
    }

    /// <summary>Remove any recorded /State and /StateModel.</summary>
    public void ClearState()
    {
        Dict.Remove("State");
        Dict.Remove("StateModel");
        ClearReviewStateOnReplies();
    }

    /// <summary>Find the state-tracking annotation linked to this markup
    /// (the most-recent /IRT reply annotation that carries a /State entry,
    /// per PDF 32000 §12.5.6.3). Returns null when no such reply exists.</summary>
    public TextAnnotation? FindStateAnnotation() => FindStateReply();

    // ── Common markup-annotation properties (PDF 32000 §12.5.6.2) ───────────

    /// <summary>Creation timestamp recorded in /CreationDate.</summary>
    public System.DateTime CreationDate
    {
        get
        {
            var raw = (Dict.Get("CreationDate") as PdfString)?.ToText();
            return string.IsNullOrEmpty(raw) ? System.DateTime.MinValue
                : ParsePdfDate(raw) ?? System.DateTime.MinValue;
        }
        set => Dict.Set("CreationDate",
            new PdfString(System.Text.Encoding.Latin1.GetBytes(
                "D:" + value.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z")));
    }

    /// <summary>Opacity (0..1) carried in /CA.</summary>
    public new double Opacity
    {
        get
        {
            var ca = InternalReader.Resolve(Dict.Get("CA"));
            return ca switch
            {
                PdfReal r => r.Value,
                PdfInteger i => i.Value,
                _ => 1.0,
            };
        }
        set => Dict.Set("CA", new PdfReal(value));
    }

    /// <summary>Associated popup annotation (/Popup).</summary>
    public PopupAnnotation? Popup
    {
        get
        {
            var p = InternalReader.ResolveDict(Dict.Get("Popup"));
            return p is null ? null : new PopupAnnotation(p, InternalReader);
        }
        set
        {
            if (value is null) Dict.Remove("Popup");
            else Dict.Set("Popup", value.Dict);
        }
    }

    /// <summary>Reply relationship to <see cref="InReplyTo"/> (/RT).</summary>
    public new ReplyType ReplyType
    {
        get => Dict.GetName("RT") switch
        {
            "R" => ReplyType.Reply,
            "Group" => ReplyType.Group,
            _ => ReplyType.Undefined,
        };
        set => Dict.Set("RT", new PdfName(value == ReplyType.Reply ? "R"
            : value == ReplyType.Group ? "Group" : ""));
    }

    /// <summary>Rich-text contents (/RC), XHTML-formatted.</summary>
    public string? RichText
    {
        get => (Dict.Get("RC") as PdfString)?.ToText();
        set => Dict.Set("RC",
            new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Subject line (/Subj).</summary>
    public new string? Subject
    {
        get => (Dict.Get("Subj") as PdfString)?.ToText();
        set => Dict.Set("Subj",
            new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Author / title carried in /T.</summary>
    public new string? Title
    {
        get => (Dict.Get("T") as PdfString)?.ToText();
        set => Dict.Set("T",
            new PdfString(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty)));
    }

    /// <summary>Parse a PDF date string (D:YYYYMMDDHHmmSS) to .NET DateTime
    /// (UTC); returns null on malformed input. Local to MarkupAnnotation —
    /// the base Annotation type also declares one but with a nullable
    /// parameter, so a `new` keyword shields the local version.</summary>
    private static new System.DateTime? ParsePdfDate(string s)
    {
        if (s.StartsWith("D:")) s = s.Substring(2);
        if (s.Length >= 14
            && System.DateTime.TryParseExact(s.Substring(0, 14), "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var dt))
            return dt;
        return null;
    }
}
