using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf.Forms;

public sealed partial class Form : ICollection<Aspose.Pdf.Annotations.WidgetAnnotation>
{
    /// <summary>The stages of flattening one page's fields: one annotation at a time, then the appended content.</summary>
    private void AppendFlattenedContent(FlattenPageState fp)
    {
        var existingData = fp.page.GetContentStreamBytes() ?? [];

        // Bracket the original page content in a balanced q … Q before appending the
        // flattened field fragments. A page content stream may leave the CTM in a
        // non-default state — e.g. a leading global "0.12 0 0 0.12 0 0 cm" scale that
        // is never wrapped in q/Q (some form authoring tools draw the whole page in an
        // 8.33× coordinate space) — so appending fragments raw would draw every widget
        // appearance through that leftover transform (scaled + shoved into a page
        // corner). The outer q/Q restores the base CTM so each "q … cm /FRMn Do Q"
        // fragment is placed at its true /Rect. Harmless when the page content is
        // already clean (a no-op save/restore around it).
        byte[] pre = existingData.Length > 0 ? Encoding.ASCII.GetBytes("q\n") : [];
        byte[] mid = existingData.Length > 0 ? Encoding.ASCII.GetBytes("\nQ\n") : [];
        var frag = fp.appendContent.ToArray();

        var combined = new byte[pre.Length + existingData.Length + mid.Length + frag.Length];
        int off = 0;
        pre.CopyTo(combined, off); off += pre.Length;
        existingData.CopyTo(combined, off); off += existingData.Length;
        mid.CopyTo(combined, off); off += mid.Length;
        frag.CopyTo(combined, off);

        fp.page.SetContentStream(combined);
    }

    /// <summary>The stages of flattening one page's fields: one annotation at a time, then the appended content.</summary>
    private void FlattenAnnotation(FlattenPageState fp, PdfObject annotRef)
    {
        var annotDict = fp.reader.ResolveDict(annotRef);
        if (annotDict is null)
        {
            fp.remaining.Add(annotRef);
            return;
        }

        var subtype = annotDict.GetName("Subtype");
        // Merged field-widget dicts may omit /Subtype but still have field
        // properties (/FT, /T, or /Parent pointing to a field hierarchy).
        bool isWidget = subtype == "Widget"
            || (subtype is null && (annotDict.ContainsKey("FT") || annotDict.ContainsKey("T")
                || annotDict.ContainsKey("Parent")));
        // Form-field flatten (document/form) folds only widgets into the page content and
        // leaves other annotation types (markup, line, link, …) for their own annotation
        // path. FlattenAllFields (flattenNonWidgets) instead flattens every annotation with
        // an appearance so the FRM{n} index lines up with the page /Annots order.
        if (!isWidget && !fp.flattenNonWidgets)
        {
            fp.remaining.Add(annotRef);
            return;
        }

        // HideButtons: drop push-button widgets entirely (neither rendered
        // into page content nor kept as an annotation) so the flattened
        // output shows no buttons.
        if (isWidget && fp.hideButtons && IsPushButtonWidget(annotDict, fp.reader))
            return;

        // PDF/A flatten: hidden widgets (F bit 2) and push buttons are dropped
        // without a page-content fragment; other widgets stamp regardless of
        // the Print bit (a filled text field with F=0 still shows its value
        // in the flattened output).
        if (isWidget && fp.skipInvisible)
        {
            var fFlags = annotDict.GetInt("F");
            if ((fFlags & 2) != 0) return;
            if (IsPushButtonWidget(annotDict, fp.reader)) return;
            // A visible signature widget is never folded into content: the
            // signature field survives PDF/A conversion (hidden ones were
            // dropped above).
            var widgetFt = annotDict.GetName("FT")
                ?? fp.reader.ResolveDict(annotDict.Get("Parent"))?.GetName("FT");
            if (widgetFt == "Sig") { fp.remaining.Add(annotRef); return; }
            if (fp.fieldWidgets is not null && !fp.fieldWidgets.Remove(annotDict)) return;
        }

        // Build the content fragment that folds this widget's appearance into the page
        // (registering it as FRM{n}). Null when the widget has no usable appearance — a
        // widget is then dropped (orphan field), a non-widget kept in /Annots.
        var fragment = BuildWidgetFlattenFragment(fp.page.Dict, annotDict, fp.reader, $"FRM{fp.frmCounter}");
        if (fragment is null)
        {
            // Flattening a PAGE consumes its whole /Annots array: an annotation that
            // draws nothing still LEAVES, it is not left behind as live markup. The
            // document/form flatten instead keeps non-widgets for the annotation path
            // that runs after it.
            if (!isWidget && !fp.dropUnstamped) fp.remaining.Add(annotRef);
            return;
        }
        fp.frmCounter++;
        var writer = Compat.LeaveOpenWriter(fp.appendContent, System.Text.Encoding.ASCII);
        writer.Write(fragment);
        writer.Flush();
    }
}
