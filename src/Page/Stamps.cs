using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;
using Aspose.Pdf.Operators;
using Aspose.Pdf.Shading;
using Aspose.Pdf.Stamps;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

public sealed partial class Page
{
    /// <summary>Apply an image stamp to this page. Delegates to
    /// <see cref="ImageStamp.ApplyTo(Page)"/>.
    /// <para>The stamp's XIndent/YIndent are DISPLAYED-frame coordinates, the same
    /// frame <see cref="Annotations.Annotation.GetRectangle(bool)"/> reports and the
    /// one a viewer shows — so on a page carrying /Rotate the page-rotation frame is
    /// composed into the placement matrix, exactly as
    /// <see cref="AddImage(byte[], Rectangle)"/> does. Without it a stamp anchored to a
    /// rotated page's annotation lands sideways and off-box. Unrotated pages are
    /// unaffected (the frame is the identity).</para></summary>
    public void AddStamp(ImageStamp stamp)
    {
        if (stamp is null) throw new ArgumentNullException(nameof(stamp));
        stamp.CompensatePageRotation = true;
        stamp.ApplyTo(this);
    }

    /// <summary>Apply a page stamp to this page. Delegates to
    /// <see cref="PdfPageStamp.ApplyTo(Page)"/> rather than the generic
    /// <see cref="AddStamp(Aspose.Pdf.Stamps.Stamp)"/> path: a PdfPageStamp registers
    /// its source-page Form XObject in this page's /Resources/XObject and emits a
    /// `… /Fm0 Do …` draw call, but the generic path re-wraps that draw in an inner
    /// Form XObject whose resources deliberately omit /XObject, leaving /Fm0 unresolved
    /// so the stamped content disappears. ApplyTo writes the draw call
    /// straight to the page content where /Fm0 is in scope.</summary>
    public void AddStamp(PdfPageStamp stamp)
    {
        if (stamp is null) throw new ArgumentNullException(nameof(stamp));
        stamp.ApplyTo(this);
    }

    public void AddStamp(Aspose.Pdf.Stamps.Stamp stamp)
    {
        // Register Helvetica in the page resources and pass the resolved
        // resource name into the stamp. If the page already uses "F1" for an
        // embedded subset, RegisterFont returns "F2"/"F3"/etc so the stamp's
        // SetFont op binds to Helvetica rather than the existing subset.
        var fontName = Table.RegisterFont(this);
        var stampBytes = stamp.BuildContentStream(this, fontName);

        // Wrap the stamp content in a Form XObject and reference it from the page
        // content with a Do operator. Emitting the stamp as a form (rather than
        // inline content) keeps the page content stream a simple reference and
        // surfaces the stamp under the page's /Resources/XObject (page.Resources.Forms).
        var formName = AddStampForm(stampBytes, stampId: stamp.StampId,
            startAtExistingCount: stamp.NameFormAfterExistingXObjects);
        // Embed a %StampId comment ahead of the Do reference when the stamp carries an
        // id, so PdfContentEditor.GetStamps / DeleteStampById can identify it on reload.
        var idComment = stamp.StampId != 0 ? $"%StampId={stamp.StampId}\n" : "";
        // Embed a %StampRect comment with the stamp's pre-computed page-space bounds
        // (e.g. header/footer bands), so GetStamps reports the exact geometry on reload.
        var rectComment = stamp.MetaRect is { } mr ? $"%StampRect={Format(mr.LLX)} " +
            $"{Format(mr.LLY)} {Format(mr.URX)} {Format(mr.URY)}\n" : "";
        var refBytes = System.Text.Encoding.ASCII.GetBytes($"{idComment}{rectComment}q /{formName} Do Q\n");

        // Add the stamp reference as its own content stream rather than rewriting
        // /Contents. AddContentStream / PrependContentStream preserve an existing
        // multi-stream /Contents array (common for imported pages); the previous
        // stream-only logic fell through to "no existing content" for an array and
        // erased the page's own content: an imported page whose
        // /Contents was an 8-stream array was reduced to just the stamp reference.
        if (stamp.IsBackground)
            PrependContentStream(refBytes);
        else
            AddContentStream(refBytes);
    }

    /// <summary>Wrap a stamp's content bytes in a Form XObject, register it under a
    /// fresh /FmN name in this page's /Resources/XObject, and return that name. The
    /// form shares the page's font / graphics-state resources so its content resolves
    /// the same resource names it referenced when built.</summary>
    internal string AddStampForm(byte[] content, Rectangle? bboxRect = null, int stampId = 0, bool startAtExistingCount = false)
    {
        // Resolve an indirect /Resources in place; a bare cast would miss a
        // PdfReference and replace the real dictionary with an empty one,
        // dropping the page's fonts and content references.
        var resources = _reader.ResolveDict(_dict.Get("Resources"));
        if (resources is null)
        {
            resources = new PdfDictionary();
            _dict.Set("Resources", resources);
        }

        // Form-local resources: share the page's font / graphics-state / pattern /
        // colour-space / shading entries. /XObject is deliberately excluded so the
        // form can't end up referencing itself.
        var formResources = new PdfDictionary();
        foreach (var key in new[] { "Font", "ExtGState", "Pattern", "ColorSpace", "Shading" })
        {
            var entry = resources.Get(key);
            if (entry is not null) formResources.Set(key, entry);
        }

        var box = bboxRect ?? MediaBox;
        var bbox = new PdfArray();
        bbox.Add(new PdfReal(box.LLX));
        bbox.Add(new PdfReal(box.LLY));
        bbox.Add(new PdfReal(box.URX));
        bbox.Add(new PdfReal(box.URY));

        var formDict = new PdfDictionary();
        formDict.Set("Type", new PdfName("XObject"));
        formDict.Set("Subtype", new PdfName("Form"));
        formDict.Set("FormType", new PdfInteger(1));
        formDict.Set("BBox", bbox);
        formDict.Set("Resources", formResources);
        formDict.Set("StampId", new PdfInteger(stampId));
        var formStream = new PdfStream(formDict, content);

        // Register the form as an indirect object (not inline in /XObject): a full save
        // promotes inline streams, but an incremental (append-only) save writes only the
        // objects registered as new — so a stamp added to a document opened from a
        // writable stream would otherwise vanish on Save().
        var doc = _reader.OwnerDocument;
        PdfObject formEntry = formStream;
        if (doc is not null && doc.HasWritableSourceStream)
        {
            var fnum = doc.AllocateObjectNumber();
            doc.AddNewObject(fnum, formStream, registerOverlay: true);
            formEntry = new PdfIndirectRef(fnum, 0);
        }

        var xobjects = _reader.Resolve(resources.Get("XObject")) as PdfDictionary;
        if (xobjects is null)
        {
            xobjects = new PdfDictionary();
            resources.Set("XObject", xobjects);
        }

        // Stamp form XObjects are numbered from Fm0 for the public Page.AddStamp
        // path; the PdfFileStamp facade starts at the page's existing /XObject
        // entry count instead (see Stamps.Stamp.NameFormAfterExistingXObjects).
        var counter = startAtExistingCount ? xobjects.Count : 0;
        var name = $"Fm{counter}";
        while (xobjects.ContainsKey(name)) name = $"Fm{++counter}";
        xobjects.Set(name, formEntry);
        return name;
    }

    /// <summary>
    /// Flatten all annotations on this page — render their visual appearance
    /// into the page content stream and remove them from the annotations array.
    /// </summary>
    /// <summary>
    /// Flattens all annotations into the page's content stream.
    /// Each annotation's appearance stream (AP/N) is drawn at the annotation's Rect position
    /// by computing a CTM that maps the appearance's BBox to the Rect. The annotation is then
    /// removed from the page's /Annots array. Popup annotations are skipped (they have no
    /// visual appearance). After flattening, annotations no longer exist as interactive objects.
    /// </summary>
    public void Flatten()
    {
        var annotsObj = _reader.Resolve(_dict.Get("Annots")) as PdfArray;
        if (annotsObj is null || annotsObj.Count == 0) return;

        // A page that belongs to a document with a form flattens through the form: each
        // annotation becomes an XObject in the page's own resources (so the flattened
        // widgets are reachable as Resources.Forms) and the fields that lived here leave
        // the AcroForm. Without an owning document there is nothing to retire, and the
        // inline path below still folds the appearances into the content stream.
        if (_reader.OwnerDocument is { } ownerDoc
            && _reader.ResolveDict(_reader.Catalog.Get("AcroForm")) is not null)
        {
            ownerDoc.Form.FlattenSinglePage(ownerDoc, this);
            return;
        }

        var appendContent = new MemoryStream();

        foreach (var annotRef in annotsObj)
        {
            var annotDict = _reader.ResolveDict(annotRef);
            if (annotDict is null) continue;

            // Popup annotations are auxiliary UI elements with no drawn appearance
            var subtype = annotDict.GetName("Subtype");
            if (subtype == "Popup") continue;

            var appearanceStream = ResolveAppearanceStream(annotDict);
            if (appearanceStream is null)
            {
                // Shape/markup annotations are often stored without an /AP (the viewer
                // synthesises one). Generate it from the annotation's geometry so the
                // figure is baked into the page instead of vanishing on flatten.
                var typed = Aspose.Pdf.Annotations.Annotation.Create(annotDict, _reader);
                if (Aspose.Pdf.Annotations.Annotation.CanSynthesiseAppearance(typed))
                    typed.UpdateAppearances();
                appearanceStream = ResolveAppearanceStream(annotDict);
                if (appearanceStream is null) continue;
            }

            var rectArr = _reader.Resolve(annotDict.Get("Rect")) as PdfArray;
            if (rectArr is null || rectArr.Count < 4) continue;

            var rect = Rectangle.FromPdfArray(rectArr);
            var streamData = _reader.DecodeStream(appearanceStream);

            // Compute the CTM that maps the appearance BBox to the annotation Rect.
            // Scale factors map BBox dimensions to Rect dimensions; translation offsets
            // position the appearance at Rect.LLX/LLY, compensating for BBox origin.
            var (sx, sy, tx, ty) = ComputeAppearanceCtm(rect, appearanceStream);

            var writer = new StreamWriter(appendContent, System.Text.Encoding.ASCII, leaveOpen: true);
            writer.Write(
                $"q {Format(sx)} 0 0 {Format(sy)} {Format(tx)} {Format(ty)} cm\n");
            writer.Flush();
            appendContent.Write(streamData);
            writer.Write("\nQ\n");
            writer.Flush();

            // Merge the appearance stream's Resources into the page's Resources
            // so that fonts/images referenced by the appearance remain available
            Forms.Form.MergeAnnotResources(_dict, appearanceStream.Dict, _reader);
        }

        // Remove all annotations — they are now baked into the content stream
        _dict.Remove("Annots");
        _annotations = null;

        if (appendContent.Length > 0)
            AppendToContentStream(appendContent.ToArray());
    }
}
