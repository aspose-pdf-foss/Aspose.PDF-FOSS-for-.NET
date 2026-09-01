using Aspose.Pdf.Core;
using Aspose.Pdf.Functions;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Annotations;

public partial class Annotation
{
    /// <summary>Read a review-state text-string entry (/State or /StateModel)
    /// from this annotation, falling back to a reply annotation (/IRT → this)
    /// that carries one. PDF §12.5.6.4 stores review states on a separate reply
    /// annotation, not on the annotation being reviewed; the last reply wins.</summary>
    internal string? ResolveReviewStateValue(string key)
    {
        if (_dict.Get(key) is PdfString own && own.ToText() is { Length: > 0 } ownText)
            return ownText;

        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (pageDict is null || _reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots)
            return null;

        string? latest = null;
        foreach (var item in annots)
        {
            var reply = _reader.ResolveDict(item);
            if (reply is null || ReferenceEquals(reply, _dict)) continue;
            if (!IsReplyToThis(reply)) continue;
            if (reply.Get(key) is PdfString s && s.ToText() is { Length: > 0 } t)
                latest = t;
        }
        return latest;
    }

    /// <summary>True when <paramref name="reply"/> is a reply (/IRT) to this
    /// annotation. Matches by object identity, falling back to the annotation
    /// name (/NM) — FOSS may serialize /IRT as an inline copy of the target
    /// rather than an indirect reference, which breaks identity on reload.</summary>
    private bool IsReplyToThis(PdfDictionary reply)
    {
        var irt = _reader.ResolveDict(reply.Get("IRT"));
        if (irt is null) return false;
        if (ReferenceEquals(irt, _dict)) return true;
        var myName = (_dict.Get("NM") as PdfString)?.ToText();
        var irtName = (irt.Get("NM") as PdfString)?.ToText();
        return !string.IsNullOrEmpty(myName) && myName == irtName;
    }

    /// <summary>Locate the most-recent reply annotation (/IRT → this) that
    /// carries a /State entry, wrapping it as a <see cref="TextAnnotation"/>.
    /// Resolves the owning page from /P, the cached page dict, or the page the
    /// annotation was created/attached to (so it works in-memory and after a
    /// save/reload). Returns null when none exists.</summary>
    internal TextAnnotation? FindStateReply()
    {
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict
            ?? (_ownerPage ?? _creationPage)?.Dict;
        if (pageDict is null || _reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots)
            return null;

        TextAnnotation? latest = null;
        foreach (var item in annots)
        {
            var reply = _reader.ResolveDict(item);
            if (reply is null || ReferenceEquals(reply, _dict)) continue;
            if (!IsReplyToThis(reply)) continue;
            if (reply.Get("State") is PdfString)
                latest = new TextAnnotation(reply, _reader);
        }
        return latest;
    }

    /// <summary>Create and attach the reply annotation (/IRT → this) that
    /// records a review state, mirroring how viewers store review states on a
    /// separate annotation. No-op when this annotation isn't attached to a
    /// page (the /State written on the annotation itself still resolves).</summary>
    internal void AttachStateReply(string state, string model, string author)
    {
        var page = _ownerPage ?? _creationPage;
        if (page is null) return;

        var reply = new TextAnnotation(page, Rect ?? new Rectangle(0, 0, 0, 0))
        {
            Contents = state + " set by " + author,
        };
        if (!string.IsNullOrEmpty(author)) reply.Title = author;
        reply.Dict.Set("State", new PdfString(System.Text.Encoding.Latin1.GetBytes(state)));
        reply.Dict.Set("StateModel", new PdfString(System.Text.Encoding.Latin1.GetBytes(model)));
        reply.Dict.Set("IRT", _dict);
        page.Annotations.Add(reply);
    }

    /// <summary>Remove /State and /StateModel from this annotation and from any
    /// reply annotations (/IRT → this) that carry them — used by ClearState so
    /// the cleared state survives save/reload.</summary>
    internal void ClearReviewStateOnReplies()
    {
        var pageDict = _reader.ResolveDict(_dict.Get("P")) ?? _pageDict;
        if (pageDict is null || _reader.Resolve(pageDict.Get("Annots")) is not PdfArray annots)
            return;
        foreach (var item in annots)
        {
            var reply = _reader.ResolveDict(item);
            if (reply is null || ReferenceEquals(reply, _dict)) continue;
            if (!IsReplyToThis(reply)) continue;
            reply.Remove("State");
            reply.Remove("StateModel");
        }
    }

    /// <summary>Locate the 1-based page index that owns <paramref name="dict"/>,
    /// via its /P entry or by scanning each page's /Annots. Returns -1 if not found.</summary>
    private int FindPageIndexOf(PdfDictionary dict, PdfDictionary? pageHint, PageCollection pages)
    {
        var pageDict = _reader.ResolveDict(dict.Get("P")) ?? pageHint;
        if (pageDict is not null)
        {
            for (var i = 1; i <= pages.Count; i++)
                if (ReferenceEquals(pages[i].Dict, pageDict)) return i;
        }
        for (var i = 1; i <= pages.Count; i++)
        {
            var annots = _reader.Resolve(pages[i].Dict.Get("Annots")) as Core.PdfArray;
            if (annots is null) continue;
            foreach (var item in annots)
            {
                if (ReferenceEquals(_reader.ResolveDict(item), dict))
                    return i;
            }
        }
        return -1;
    }
}
