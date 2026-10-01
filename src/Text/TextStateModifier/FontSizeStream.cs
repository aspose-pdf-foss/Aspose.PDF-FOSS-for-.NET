using System.Globalization;
using System.Text;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Text;

internal sealed partial class TextStateModifier
{
    private byte[]? ModifyFontSizeInStream(byte[] streamBytes, string text, double oldSize,
        double newSize, PdfDictionary pageDict, PdfReader reader, bool allowCollateral,
        LineReseat? reseat = null, bool splitSubRun = true)
    {
        var fz = new FontSizeStreamState(streamBytes, text, oldSize, newSize, pageDict, reader,
            allowCollateral, reseat, splitSubRun);
        while (ScanFontSizeToken(fz)) { }
        if (!BuildShowConcat(fz)) return null;
        if (RewriteScopedFontSize(fz) is { } rewritten) return rewritten;
        return ApplyFontSizePatches(fz);
    }

    /// <summary>Concatenates the scanned shows and records each one's span in that text, so a
    /// phrase split over several shows can be matched once. False means the stream showed
    /// nothing. An absorbed right-to-left fragment reads in logical order while the stream shows
    /// its glyphs in drawn (visual) order; when the logical text is not in the stream, the drawn
    /// order is its reverse.</summary>
    private static bool BuildShowConcat(FontSizeStreamState fz)
    {
        if (fz.shows.Count == 0) return false;
        fz.concat = new StringBuilder();
        fz.spans = new (int start, int end)[fz.shows.Count];
        for (var si = 0; si < fz.shows.Count; si++)
        {
            fz.spans[si] = (fz.concat.Length, fz.concat.Length + fz.shows[si].decoded.Length);
            fz.concat.Append(fz.shows[si].decoded);
        }
        fz.concatStr = fz.concat.ToString();
        if (fz.concatStr.IndexOf(fz.text, StringComparison.Ordinal) < 0 && HasRightToLeft(fz.text))
        {
            var drawn = fz.text.ToCharArray();
            Array.Reverse(drawn);
            fz.text = new string(drawn);
        }
        return true;
    }

    /// <summary>Match the phrase over the concatenated show text, then collect every Tf site
    /// (with the expected old size) that governs a show overlapping the first match. Single-show
    /// matches reduce to one patch; phrases split across shows/Tf re-issue patch each covering Tf
    /// once. A non-null result is the finished stream: the covering Tf governed text outside the
    /// match, so the match was given a Tf of its own instead. Otherwise the collected patches are
    /// left on <paramref name="fz"/> for the in-place rewrite.</summary>
    private byte[]? RewriteScopedFontSize(FontSizeStreamState fz)
    {
        fz.patches = new SortedDictionary<int, (int end, double newTf)>();
        for (var idx = fz.concatStr.IndexOf(fz.text, StringComparison.Ordinal); idx >= 0;
             idx = fz.concatStr.IndexOf(fz.text, idx + 1, StringComparison.Ordinal))
        {
            var matchEnd = idx + fz.text.Length;
            var (sizeMatched, collateral) = CollectFontSizePatches(fz, idx, matchEnd);
            if (sizeMatched && !collateral && fz.patches.Count > 0) break;
            fz.patches.Clear();
            // The covering Tf also governs text outside the match, so it cannot be
            // rewritten in place. Give the match its OWN Tf instead: open the new size
            // just before its first show and restore the old one just after its last.
            // That is the expected shape — a resized replacement carries
            // its own Tf rather than resizing the line it sits on. Only for a run of
            // shows that lies WHOLLY inside the match and is contiguous in the stream,
            // so nothing outside the match can fall inside the new scope.
            if (!sizeMatched || !collateral) continue;
            if (ScopedTfInsertion(fz.streamBytes, fz.shows, fz.spans, idx, matchEnd, fz.oldSize, fz.newSize)
                    is { } scoped)
                return scoped;
            // The match is PART of one show: split that show around it.
            if (fz.splitSubRun && SingleShowCovering(fz, idx, matchEnd) is { } si1
                && SplitShowScopedTf(fz, fz.shows[si1], idx - fz.spans[si1].start, matchEnd - fz.spans[si1].start,
                    fz.oldSize, fz.newSize) is { } split)
                return split;
        }
        return null;
    }

    /// <summary>The Tf patches one match asks for: whether any show over it is drawn at the
    /// expected old size, and whether a candidate Tf also governs shows OUTSIDE the match (e.g.
    /// the whole paragraph under one Tf), which patching would resize as collateral.</summary>
    private static (bool sizeMatched, bool collateral) CollectFontSizePatches(
        FontSizeStreamState fz, int idx, int matchEnd)
    {
        var sizeMatched = false;
        for (var si = 0; si < fz.shows.Count; si++)
        {
            if (fz.spans[si].end <= idx || fz.spans[si].start >= matchEnd) continue;
            var s = fz.shows[si];
            if (Math.Abs(s.effSize - fz.oldSize) >= 0.5) continue;
            sizeMatched = true;
            // Resize only when every show governed by the candidate Tf lies inside the
            // match — otherwise the scoped insertion gives the match its own Tf instead.
            for (var sj = 0; fz.allowCollateral == false && sj < fz.shows.Count; sj++)
            {
                if (fz.shows[sj].tfStart != s.tfStart) continue;
                if (fz.spans[sj].start < idx || fz.spans[sj].end > matchEnd) return (true, true);
            }
            // newSize is the desired effective size; recover the raw Tf value
            // through the same Tm scale that produced this show's effective size.
            var tmScale = s.effSize / Math.Max(0.0001, RawTfFor(fz.streamBytes, s));
            fz.patches[s.tfStart] = (s.tfEnd, fz.newSize / Math.Max(0.0001, tmScale));
            fz.wholeShow = fz.patches.Count == 1 && fz.spans[si].start == idx && fz.spans[si].end == matchEnd ? si : -1;
        }
        return (sizeMatched, false);
    }

    /// <summary>Rewrites the collected Tf sites in place, re-seating the line first when a whole
    /// show was resized. Null means nothing matched at the expected size.</summary>
    private byte[]? ApplyFontSizePatches(FontSizeStreamState fz)
    {
        if (fz.patches.Count == 0) return null;
        fz.result = fz.streamBytes;
        // The line re-seat inserts after the resized show's Tf; apply it first, from the
        // end of the stream back, so the Tf patch below still finds its offsets.
        if (fz.wholeShow >= 0 && fz.reseat is { } rs && ReseatLine(fz, fz.wholeShow, rs) is { } seats)
            foreach (var (pos, bytes) in seats.OrderByDescending(e => e.pos))
                fz.result = InsertBytes(fz.result, pos, bytes);
        foreach (var kv in fz.patches.Reverse())
            fz.result = PatchFontSize(fz.result, kv.Key, kv.Value.end, kv.Value.newTf);
        return fz.result;
    }


    /// <summary>The index of the one show whose text overlaps [start, end), or null when the
    /// match spans several shows.</summary>
    private static int? SingleShowCovering(FontSizeStreamState fz, int start, int end)
    {
        int? found = null;
        for (var si = 0; si < fz.shows.Count; si++)
        {
            if (fz.spans[si].end <= start || fz.spans[si].start >= end) continue;
            if (found is not null) return null;
            found = si;
        }
        return found;
    }

    /// <summary>Whether the text carries Hebrew or Arabic letters (the scripts the absorber reorders).</summary>
    private static bool HasRightToLeft(string text)
    {
        foreach (var c in text)
            if (c is >= '֐' and <= 'ࣿ' or >= 'יִ' and <= '﷿' or >= 'ﹰ' and <= '﻿')
                return true;
        return false;
    }

    /// <summary>The seat of a show: the last <c>a b c d e f Tm</c> between the previous show
    /// and this one, with its operand values; null when the show is seated any other way.</summary>
    private static (double a, double b, double c, double d, double e, double f)? SeatOf(FontSizeStreamState fz, int si)
    {
        var from = si > 0 ? fz.shows[si - 1].showEnd : 0;
        var to = fz.shows[si].showStart;
        if (to <= from) return null;
        var text = Compat.Latin1.GetString(fz.streamBytes, from, to - from);
        System.Text.RegularExpressions.Match? last = null;
        foreach (System.Text.RegularExpressions.Match m in GeneralTm.Matches(text)) last = m;
        if (last is null) return null;
        var v = new double[6];
        for (var i = 0; i < 6; i++)
            if (!double.TryParse(last.Groups[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
        return (v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    private static readonly System.Text.RegularExpressions.Regex GeneralTm =
        new(@"(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+(-?\d+(?:\.\d+)?)\s+Tm\b");

    /// <summary>Re-seat the line a whole-show resize sits on, the probed rule of the reference:
    /// the resized show keeps its seat; every contiguous following show on the same baseline
    /// moves right by the growth of the resized advance, in the frame's units and divided
    /// once more by the frame's x-scale as the reference does; a show whose right edge would
    /// then pass the page edge (the resized one included) moves to the line's start one pitch
    /// (1.1 x its own size) below its baseline, the shows moved in one pass butting end to
    /// end, and nothing below the line moves. Each affected show gets its new seat as a Tm
    /// before it and its old seat restored after it. Null when the line is not Tm-seated.</summary>
    private static List<(int pos, byte[] bytes)>? ReseatLine(FontSizeStreamState fz, int si, LineReseat rs)
    {
        var s = fz.shows[si];
        if (SeatOf(fz, si) is not { } seat) return null;
        var rects = rs.ShowRects();
        if (rects is null || rects.Count != fz.shows.Count) return null;
        var own = rects[si];
        var width = own.urx - own.llx;
        var oldW = rs.Measure(s.decoded, fz.oldSize);
        if (oldW <= 0 || width <= 0) return null;
        var sx = width / oldW;                                     // page units per frame unit along x
        var growthPage = width * (fz.newSize / fz.oldSize - 1);
        var pitchSign = seat.d < 0 ? 1.0 : -1.0;                   // "below" in the frame's y
        var limit = rs.PageRight - PageEdgeMargin;
        var lineStart = seat.e;
        for (var j = si - 1; j >= 0 && SeatOf(fz, j) is { } t && Math.Abs(t.f - seat.f) <= SeatTolerance; j--)
            lineStart = Math.Min(lineStart, t.e);
        var edits = new List<(int pos, byte[] bytes)>();
        var cursor = lineStart;
        if (own.llx + width * fz.newSize / fz.oldSize > limit)
        {
            var rawNew = fz.newSize * RawTfFor(fz.streamBytes, s) / Math.Max(0.0001, s.effSize);
            Reseat(edits, fz, si, seat, cursor, seat.f + pitchSign * LinePitch * rawNew);
            cursor += rs.Measure(s.decoded, fz.newSize);
        }
        var shift = growthPage / sx / sx;
        for (var j = si + 1; j < fz.shows.Count && SeatOf(fz, j) is { } t && Math.Abs(t.f - seat.f) <= SeatTolerance; j++)
        {
            if (t.e <= seat.e) continue;
            var f = fz.shows[j];
            var r = rects[j];
            if (r.urx + shift * sx > limit)
            {
                Reseat(edits, fz, j, t, cursor, t.f + pitchSign * LinePitch * RawTfFor(fz.streamBytes, f));
                cursor += (r.urx - r.llx) / sx;
            }
            else Reseat(edits, fz, j, t, t.e + shift, t.f);
        }
        return edits.Count == 0 ? null : edits;
    }

    /// <summary>Queue a show's new seat before it and its old seat after it.</summary>
    private static void Reseat(List<(int pos, byte[] bytes)> edits, FontSizeStreamState fz, int si,
        (double a, double b, double c, double d, double e, double f) seat, double x, double y)
    {
        var sh = fz.shows[si];
        static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
        var head = " " + N(seat.a) + " " + N(seat.b) + " " + N(seat.c) + " " + N(seat.d) + " ";
        edits.Add((sh.showStart, Encoding.ASCII.GetBytes(head + N(x) + " " + N(y) + " Tm ")));
        edits.Add((sh.showEnd, Encoding.ASCII.GetBytes(head + N(seat.e) + " " + N(seat.f) + " Tm ")));
    }

    private static byte[] InsertBytes(byte[] original, int pos, byte[] bytes)
    {
        var result = new byte[original.Length + bytes.Length];
        Array.Copy(original, 0, result, 0, pos);
        bytes.CopyTo(result, pos);
        Array.Copy(original, pos, result, pos + bytes.Length, original.Length - pos);
        return result;
    }

    /// <summary>Two seats share a baseline within this (frame units).</summary>
    private const double SeatTolerance = 0.02;
    /// <summary>A moved show drops by this fraction of its own font size (probed: 7.2 -> 7.92, 16 -> 17.6).</summary>
    private const double LinePitch = 1.1;
    /// <summary>A show whose right edge passes the page edge less this (pt) is moved; the probe
    /// bounds the edge at (589.1, 594.74) on a 595-pt page, so 5 pt inside the sheet.</summary>
    private const double PageEdgeMargin = 5.0;
}
