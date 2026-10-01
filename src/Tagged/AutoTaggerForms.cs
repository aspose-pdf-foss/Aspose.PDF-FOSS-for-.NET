using System;
using System.Collections.Generic;
using System.Linq;
using Aspose.Pdf.Content;
using Aspose.Pdf.Core;

namespace Aspose.Pdf.Tagged;

internal static partial class AutoTagger
{
    /// <summary>A form XObject the tagger looked into: its stream, the reference the page draws
    /// it by (the /Stm of the marked-content references into it) and its decoded content, which
    /// the marking rewrites.</summary>
    private sealed class FormWork
    {
        public PdfStream Stream = null!;
        public PdfObject Ref = null!;
        public byte[] Bytes = [];
    }

    /// <summary>How often each form XObject is drawn over the document, and which ones were
    /// looked into. A form drawn more than once (a logo on every page, a symbol repeated) shows
    /// one content in several places, which one structure element cannot stand for: it stays an
    /// artifact wherever it is drawn. A form drawn once is content like the page's own: a whole
    /// page a stamp was laid over, a cover photo the producer wrapped, a vector figure.</summary>
    private sealed class FormUse
    {
        public readonly Dictionary<PdfStream, int> Drawn = new(ReferenceEqualityComparer.Instance);
        public readonly HashSet<PdfStream> LookedInto = new(ReferenceEqualityComparer.Instance);
        /// <summary>Whether a form drawn more than once draws and says nothing, and whether it paints a ground its
        /// marks and labels stand on (see <see cref="IsSymbol"/>).</summary>
        public readonly Dictionary<PdfStream, (bool Draws, bool Grounded)> Draws = new(ReferenceEqualityComparer.Instance);

        public FormUse(Document document)
        {
            foreach (var page in document.Pages)
                foreach (var op in PageContentScan.Scan(page).Ops)
                    if (op.Form is { } form) Count(form);
        }

        public void Count(PdfStream form) => Drawn[form] = (Drawn.TryGetValue(form, out var n) ? n : 0) + 1;

        public bool Once(PdfStream form) => Drawn.TryGetValue(form, out var n) && n == 1;
    }

    // A form whose box covers this share of the page holds the page's content (a page laid under a stamp).
    private const double PageFormShare = 0.8;
    // A line whose ends lie within this many points of a figure form's box is a label inside it.
    private const double LabelTolerance = 2.0;

    /// <summary>Look into the form XObjects the page draws once. A form holding text that is
    /// the page's (its box covers the page) or that outweighs its drawing, and a form without
    /// text (a wrapped picture), have their operations spliced in after the Do, placed on the
    /// page, so the tagger marks them inside the form. A form drawing more than it says - a
    /// vector figure with its labels - is a figure drawn whole. Forms nest: a form's forms are
    /// looked into the same way, <see cref="PageContentScan.MaxFormDepth"/> deep.</summary>
    private static void ExpandForms(PageWork pw, FormUse forms, List<PageContentScan.Rule> rules, bool again = false)
    {
        var reader = pw.Page.Reader;
        var pageRect = pw.Page.GetPageRect(true);
        var pageResources = PageContentScan.InheritedResources(reader, pw.Page.Dict);
        var ops = new List<ContentOp>();
        Splice(pw.Ops, 0, 0);
        pw.Ops = ops;

        void Splice(List<ContentOp> source, int stream, int depth)
        {
            foreach (var op in source)
            {
                op.Stream = stream;
                ops.Add(op);
                if (op.Form is not { } form || op.FormRef is null || op.FormCtm is null || depth >= PageContentScan.MaxFormDepth) continue;
                if (!forms.Once(form))
                {
                    // A symbol repeated is a figure drawn whole wherever it stands.
                    if (IsSymbol(reader, form, op, pageRect, pageResources, forms)) op.AsFigure = true;
                    continue;
                }
                if (!forms.LookedInto.Add(form)) continue;
                var ctm = GraphicsState.MultiplyMatrices(PageContentScan.FormMatrix(reader, form), op.FormCtm);
                var formRules = new List<PageContentScan.Rule>();
                var (bytes, inner) = PageContentScan.ScanForm(reader, form, ctm, pageResources, formRules);
                // (looked into again, a form's forms were counted the first time)
                if (!again)
                    foreach (var nested in inner)
                        if (nested.Form is { } f) forms.Count(f);
                if (inner.Count == 0) continue;
                if (IsFigure(op, inner, pageRect))
                {
                    // Its ruling is a picture's, no table's.
                    op.AsFigure = true;
                    continue;
                }
                rules.AddRange(formRules);
                pw.Forms.Add(new FormWork { Stream = form, Ref = op.FormRef, Bytes = bytes });
                op.Expanded = true;
                Splice(inner, pw.Forms.Count, depth + 1);
            }
        }
    }

    /// <summary>The page's operations read again after its content was rewritten (its shows cut at a table's column
    /// edges): the forms it looked into are looked into again and their operations spliced in as before - their ruling
    /// was taken the first time - and its figures listed again.</summary>
    private static void RescanPage(PageWork pw, FormUse forms)
    {
        (pw.Bytes, pw.Ops) = PageContentScan.Scan(pw.Page);
        foreach (var form in pw.Forms) forms.LookedInto.Remove(form.Stream);
        pw.Forms.Clear();
        ExpandForms(pw, forms, [], again: true);
        pw.Figures = CollectFigures(pw.Ops);
    }

    // A symbol is at most this many points on each side (a drawing is more), and at least this many.
    private const double MaxSymbolSide = 24.0;
    private const double MinSymbolSide = 3.0;
    // An icon - a ground with its mark and label on it, a "CAUTION" sign a note runs round - is at most this many points
    // on each side (four lines of text), its ground covering at least this share of what it draws.
    private const double MaxIconSide = 48.0;
    private const double IconGroundShare = 0.5;

    /// <summary>Whether a form drawn more than once is a symbol - a check box, a bullet, an icon: small, standing
    /// on the page, drawing and saying nothing - or an icon painting a ground its mark and label stand on. Each place it
    /// is drawn at is a figure of its own; what it holds is not looked into (one content in several places).</summary>
    private static bool IsSymbol(IO.PdfReader reader, PdfStream form, ContentOp op, Aspose.Pdf.Rectangle pageRect,
        PdfDictionary? pageResources, FormUse forms)
    {
        double w = op.Urx - op.Llx, h = op.Ury - op.Lly;
        if (w < MinSymbolSide || h < MinSymbolSide || w > MaxIconSide || h > MaxIconSide) return false;
        if (op.Llx < pageRect.LLX || op.Urx > pageRect.URX || op.Lly < pageRect.LLY || op.Ury > pageRect.URY) return false;
        if (!forms.Draws.TryGetValue(form, out var kind))
        {
            var inner = PageContentScan.ScanForm(reader, form, PageContentScan.FormMatrix(reader, form), pageResources, null).Ops;
            var painted = inner.Where(o => o.Kind is ContentOpKind.PathPaint or ContentOpKind.Shading or ContentOpKind.Image or ContentOpKind.InlineImage).ToList();
            var grounded = false;
            if (painted.Count > 0)
            {
                var drawn = Math.Max(0, inner.Max(o => o.Urx) - inner.Min(o => o.Llx)) * Math.Max(0, inner.Max(o => o.Ury) - inner.Min(o => o.Lly));
                grounded = drawn > 0 && painted.Any(o => (o.Urx - o.Llx) * (o.Ury - o.Lly) >= IconGroundShare * drawn);
            }
            kind = (painted.Count > 0 && inner.All(o => o.Kind != ContentOpKind.TextShow), grounded);
            forms.Draws[form] = kind;
        }
        return (kind.Draws && w <= MaxSymbolSide && h <= MaxSymbolSide) || kind.Grounded;
    }

    /// <summary>A form is a figure when it draws more than it says: its paths and shadings
    /// outnumber its text shows, and its box covers less than a page.</summary>
    private static bool IsFigure(ContentOp form, List<ContentOp> inner, Aspose.Pdf.Rectangle pageRect)
    {
        var text = inner.Count(o => o.Kind == ContentOpKind.TextShow);
        if (text == 0) return false;
        var drawing = inner.Count(o => o.Kind is ContentOpKind.PathPaint or ContentOpKind.Shading);
        var cover = Math.Max(0, Math.Min(form.Urx, pageRect.URX) - Math.Max(form.Llx, pageRect.LLX))
                    * Math.Max(0, Math.Min(form.Ury, pageRect.URY) - Math.Max(form.Lly, pageRect.LLY));
        return drawing > text && cover < PageFormShare * pageRect.Width * pageRect.Height;
    }

    /// <summary>Whether a line stands inside a figure form's box: its label.</summary>
    private static bool InsideForm(Line line, ContentOp form)
    {
        if (line.Frags.Count == 0) return false;
        var middle = line.Y + line.Size / 2;
        return line.MinX >= form.Llx - LabelTolerance && line.Frags[^1].R <= form.Urx + LabelTolerance
               && middle >= form.Lly && middle <= form.Ury;
    }

    /// <summary>A form's content rewritten with marks: the stream holds the new bytes plain (the
    /// writer encodes them afresh), its old filters gone.</summary>
    private static void ReplaceFormContent(PdfStream form, byte[] content)
    {
        form.ReplaceData(content);
        form.Dict.Remove("Filter");
        form.Dict.Remove("DecodeParms");
    }
}
