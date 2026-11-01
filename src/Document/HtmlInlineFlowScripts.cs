namespace Aspose.Pdf;

public sealed partial class Document
{
    /// <summary>The scripts a run's characters are re-faced by when the run's own face lacks them.</summary>
    private enum InlineScript { None, Han, Kana, Hangul }

    /// <summary>The first code point past which a run may hold characters of the re-faced scripts.</summary>
    private const char FirstScriptChar = (char)0x1100;

    private static InlineScript InlineScriptOf(char c)
    {
        if (c is >= (char)0x3040 and <= (char)0x30FF) return InlineScript.Kana;
        if (c is >= (char)0xAC00 and <= (char)0xD7AF or >= (char)0x1100 and <= (char)0x11FF) return InlineScript.Hangul;
        if (c is >= (char)0x3400 and <= (char)0x9FFF or >= (char)0xF900 and <= (char)0xFAFF or >= (char)0x3000 and <= (char)0x303F or >= (char)0xFF00 and <= (char)0xFFEF)
            return InlineScript.Han;
        return InlineScript.None;
    }

    /// <summary>A run's text cut where its face stops showing the characters: a Han, kana or Hangul
    /// character the face has no glyph for is set in the installed face that shows its script,
    /// consecutive ones of a face in one piece, and every other character stays in the run's face
    /// (a face that shows the whole run - a CJK face asked for by name - keeps it whole).</summary>
    private static List<(string Text, UaFace Face)> InlineFacePieces(HtmlInlineFlowState st, HtmlInlineItem item, UaFace face, string text)
    {
        var pieces = new List<(string Text, UaFace Face)>();
        var needs = false;
        foreach (var ch in text)
            if (ch >= FirstScriptChar && InlineScriptOf(ch) != InlineScript.None && face.Gid(ch) <= 0) { needs = true; break; }
        if (!needs) { pieces.Add((text, face)); return pieces; }
        var start = 0;
        UaFace? cur = null;
        for (var i = 0; i <= text.Length; i++)
        {
            UaFace? at = null;
            if (i < text.Length)
            {
                var script = InlineScriptOf(text[i]);
                at = script == InlineScript.None || face.Gid(text[i]) > 0 ? face : InlineScriptFace(st, script, item.Bold) ?? face;
            }
            if (i < text.Length && i > start && ReferenceEquals(at, cur)) continue;
            if (i > start) pieces.Add((text.Substring(start, i - start), cur!));
            start = i;
            cur = at;
        }
        return pieces;
    }

    /// <summary>The installed face that shows a script, at the run's weight where the face has one, loaded
    /// once per flow under a resource name of its own.</summary>
    private static UaFace? InlineScriptFace(HtmlInlineFlowState st, InlineScript script, bool bold)
    {
        var key = "script|" + script + (bold ? "|b" : "");
        if (st.faces.TryGetValue(key, out var known)) return known;
        var sample = script switch
        {
            InlineScript.Han => ((char)0x4E00).ToString(),
            InlineScript.Kana => ((char)0x30A2).ToString(),
            _ => ((char)0xD55C).ToString(),
        };
        UaFace? face = null;
        if (Text.FontRepository.SubstituteForMissingGlyphs(sample, null)?.FontName is { Length: > 0 } family)
            face = TryLoadInlineFace(st, family, bold, false) ?? TryLoadInlineFace(st, family, false, false);
        if (face is not null && face.Gid(sample[0]) <= 0) face = null;
        st.faces[key] = face;
        return face;
    }
}
