using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

/// <summary>Rewrites a page's content with marked content around every painting operation:
/// structure content in <c>/Tag &lt;&lt;/MCID n&gt;&gt; BDC … EMC</c>, everything else in
/// <c>/Artifact BMC … EMC</c> (PDF 32000-1 §14.6, §14.8.2.2).
///
/// A sequence never straddles a text object boundary: it either wraps whole BT … ET objects
/// (when every painting operation in them belongs to it) or lives inside one. Nor does it
/// straddle a q or a Q (marked content and saved graphics states must nest). Path
/// construction belongs to the operator that ends the path, so a sequence opens before the
/// path's first segment. Marks of a previous structure tree (BDC with an MCID) are dropped
/// with their EMC; content already inside an /Artifact stays as it is.</summary>
internal static class ContentMarker
{
    /// <summary>The target of an operation: structure content of a slot (&gt;= 0), an artifact,
    /// or nothing to mark.</summary>
    public const int Neutral = -1;
    public const int Artifact = -2;
    /// <summary>A running header or a page footer: pagination artifacts (§14.8.2.2.2).</summary>
    public const int HeaderArtifact = -3;
    public const int FooterArtifact = -4;
    /// <summary>Content left unmarked: Do of a form XObject whose content is marked inside the form.</summary>
    public const int Bare = -5;

    public static bool Paints(ContentOp op) => op.Kind is ContentOpKind.TextShow or ContentOpKind.Image
        or ContentOpKind.Form or ContentOpKind.InlineImage or ContentOpKind.PathPaint or ContentOpKind.Shading;

    /// <summary>Rewrite <paramref name="bytes"/>. <paramref name="targets"/> gives each painting
    /// operation its slot or <see cref="Artifact"/>; <paramref name="tagOf"/> names the BDC tag
    /// of a slot. Returns the new content and, per slot, the MCIDs its content received in
    /// order. <paramref name="carried"/> gives, for a mark of the previous tree, the
    /// properties its content keeps (/ActualText, /Alt, /E, /Lang).</summary>
    public static (byte[] Content, Dictionary<int, List<int>> Mcids) Rewrite(
        byte[] bytes, List<ContentOp> ops, int[] targets, Func<int, string> tagOf,
        Func<ContentOp, IReadOnlyDictionary<string, PdfString>?>? carried = null, Dictionary<int, int>? opens = null,
        Func<int, int?>? readAt = null)
    {
        // The place in reading order, among its target's text, of the text shown last in the open sequence (readAt: a
        // show's rank there): text that is not the next to read starts a sequence of its own, which the tree can reference
        // in reading order.
        int? lastRead = null;
        bool NotNext(int op) => op >= 0 && lastRead is { } last && readAt?.Invoke(op) is { } read && read != last + 1;
        int FirstShow(int from) => ops.FindIndex(from, o => o.Kind is ContentOpKind.TextShow or ContentOpKind.EndText) is var k
                                   && k >= 0 && ops[k].Kind == ContentOpKind.TextShow ? k : -1;
        var effective = EffectiveTargets(ops, targets);
        var textTargets = TextObjectTargets(ops, effective);

        var output = new MemoryStream(bytes.Length + ops.Count * 8);
        var mcids = new Dictionary<int, List<int>>();
        var nextMcid = 0;
        // The operation being written: a sequence opened there opens at it (reported through opens: MCID -> operation).
        var at = 0;
        int? open = null;
        var openInText = false;
        var artifactDepth = 0;
        // Source marks: true = kept (its EMC is written), false = dropped.
        var sourceMarks = new Stack<(bool Kept, bool Artifact)>();

        void Write(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            output.Write(b, 0, b.Length);
        }
        void Close()
        {
            if (open is null) return;
            Write("\nEMC\n");
            open = null;
        }
        void Open(int target, bool inText)
        {
            lastRead = null;
            if (target == Artifact)
            {
                Write("\n/Artifact BMC\n");
            }
            else if (target is HeaderArtifact or FooterArtifact)
            {
                var sub = target == HeaderArtifact ? "Header" : "Footer";
                Write($"\n/Artifact <</Type /Pagination /Subtype /{sub}>> BDC\n");
            }
            else
            {
                var mcid = nextMcid++;
                if (!mcids.TryGetValue(target, out var list)) mcids[target] = list = new List<int>();
                list.Add(mcid);
                opens?.Add(mcid, at);
                Write($"\n/{tagOf(target)} <</MCID {mcid}>> BDC\n");
            }
            open = target;
            openInText = inText;
        }
        void Copy(ContentOp op) => output.Write(bytes, op.Start, op.End - op.Start);

        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            at = i;
            switch (op.Kind)
            {
                case ContentOpKind.BeginMark:
                    if (op.MarkDropped)
                    {
                        sourceMarks.Push((false, false));
                        continue;
                    }
                    if (op.MarkHasMcid && op.MarkTag != "Artifact")
                    {
                        // What the old tree said about this content (its replacement text, its
                        // description, its language) outlives the tree, in a mark of its own.
                        if (carried?.Invoke(op) is { Count: > 0 } props)
                        {
                            Close();
                            sourceMarks.Push((true, false));
                            Write("\n/Span <<" + string.Concat(props.Select(p => $" /{p.Key} <{Hex(p.Value.Value)}>")) + " >> BDC\n");
                            continue;
                        }
                        sourceMarks.Push((false, false));
                        continue;
                    }
                    Close();
                    sourceMarks.Push((true, op.MarkTag == "Artifact"));
                    if (op.MarkTag == "Artifact") artifactDepth++;
                    // An artifact is not structure content: an MCID on one is a stray that would
                    // name content of the new tree, so the mark is written without it.
                    if (op.MarkHasMcid && op.MarkProps is { } artifactProps)
                        Write("\n/Artifact <<" + string.Concat(artifactProps.Keys.Where(k => k != "MCID")
                            .Select(k => Operand(artifactProps.Get(k)) is { } v ? $" /{k} {v}" : "")) + " >> BDC\n");
                    else
                        Copy(op);
                    continue;
                case ContentOpKind.EndMark:
                    var mark = sourceMarks.Count > 0 ? sourceMarks.Pop() : (true, false);
                    if (!mark.Item1) continue;
                    Close();
                    if (mark.Item2) artifactDepth--;
                    Copy(op);
                    continue;
                case ContentOpKind.BeginText:
                    if (artifactDepth == 0)
                    {
                        var single = textTargets.TryGetValue(i, out var t) ? t : null;
                        if (open is not null && (open != single || NotNext(FirstShow(i)))) Close();
                        if (open is null && single is { } s) Open(s, inText: false);
                    }
                    Copy(op);
                    continue;
                case ContentOpKind.EndText:
                    if (open is not null && openInText) Close();
                    Copy(op);
                    continue;
            }

            // Marked content and q ... Q nest (a reader keeps the marked-content stack with the
            // graphics state): a sequence never straddles a q or a Q, so it closes before either.
            if (op.Name is "q" or "Q") Close();

            var target = artifactDepth > 0 ? Neutral : effective[i];
            // A form marked inside stands in no sequence of this stream: its marks nest in none.
            if (target == Bare) Close();
            else if (target != Neutral && (open != target || (op.Kind == ContentOpKind.TextShow && openInText == op.InText && NotNext(i))))
            {
                Close();
                Open(target, op.InText);
            }
            Copy(op);
            if (op.Kind == ContentOpKind.TextShow && readAt?.Invoke(i) is { } read) lastRead = read;
        }
        Close();
        if (ops.Count > 0 && ops[^1].End < bytes.Length)
            output.Write(bytes, ops[^1].End, bytes.Length - ops[^1].End);
        return (output.ToArray(), mcids);
    }

    /// <summary>A direct property-list value as content-stream syntax (null: not writable here).</summary>
    private static string? Operand(PdfObject? value) => value switch
    {
        PdfName n => "/" + n.Value,
        PdfInteger i => i.ToString(),
        PdfReal r => r.Value.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture),
        PdfBoolean b => b.Value ? "true" : "false",
        PdfString s => $"<{Hex(s.Value)}>",
        PdfArray a => a.Select(Operand).All(v => v is not null) ? "[" + string.Join(" ", a.Select(Operand)) + "]" : null,
        _ => null,
    };

    private static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("X2"));
        return sb.ToString();
    }

    /// <summary>Each operation's target: painting operations their own, path construction the
    /// target of the operator that ends the path (a clip path is neutral), all else neutral.</summary>
    private static int[] EffectiveTargets(List<ContentOp> ops, int[] targets)
    {
        var eff = new int[ops.Count];
        for (var i = 0; i < ops.Count; i++)
            eff[i] = Paints(ops[i]) ? (targets[i] != Neutral ? targets[i] : ops[i].Expanded ? Bare : Artifact) : Neutral;

        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind != ContentOpKind.PathBuild) continue;
            // The path object runs to its painting (or n) operator; a clip operator (W, W*) may
            // stand between, and belongs to the path too.
            var j = i;
            while (j < ops.Count && ops[j].Kind is ContentOpKind.PathBuild or ContentOpKind.Clip) j++;
            var end = j < ops.Count ? ops[j] : null;
            var t = end is { Kind: ContentOpKind.PathPaint } ? eff[j] : Neutral;
            for (var k = i; k < j; k++) eff[k] = t;
            i = j - 1;
        }
        return eff;
    }

    /// <summary>For each BT whose text object paints for exactly one target and holds no
    /// marked content of its own, that target: such a text object can be wrapped whole.</summary>
    private static Dictionary<int, int?> TextObjectTargets(List<ContentOp> ops, int[] eff)
    {
        var result = new Dictionary<int, int?>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind != ContentOpKind.BeginText) continue;
            var set = new HashSet<int>();
            var marks = false;
            var j = i + 1;
            for (; j < ops.Count && ops[j].Kind != ContentOpKind.EndText; j++)
            {
                if (ops[j].Kind is ContentOpKind.BeginMark or ContentOpKind.EndMark) marks = true;
                if (eff[j] != Neutral) set.Add(eff[j]);
            }
            result[i] = !marks && set.Count == 1 ? set.First() : null;
            i = j;
        }
        return result;
    }
}
