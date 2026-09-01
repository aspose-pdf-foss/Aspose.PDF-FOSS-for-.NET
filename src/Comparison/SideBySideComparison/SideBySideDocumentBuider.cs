using System;
using System.Collections.Generic;
using Aspose.Pdf.Operators;

namespace Aspose.Pdf.Comparison.SideBySideComparison
{
    /// <summary>Builds the side-by-side result document. A compared page pair contributes two
    /// pages, each keeping its source page's own geometry: first the left-hand version carrying
    /// its deletions, then the right-hand version carrying its insertions. The change highlights
    /// are opaque fills laid down BENEATH the stamped page, so the edited glyphs stay legible on
    /// top of the marker colour.</summary>
    internal static class SideBySideDocumentBuider
    {
        // Wording of the change-mark annotations. The title reads "Change ID: 7", the contents
        // "inserted: cre" / "deleted: uage".
        private const string ChangeIdLabel = "Change ID";
        private const string InsertedLabel = "inserted";
        private const string DeletedLabel = "deleted";

        internal static void BuildResult(string pathToTargetPdf, Page? page1, Page? page2,
            List<EditContainer> firstEdits, List<EditContainer> secondEdits,
            SideBySideComparisonOptions options)
        {
            using var target = new Document();
            BuildResult(target, page1, page2, firstEdits, secondEdits, options);
            target.Save(pathToTargetPdf);
        }

        internal static void BuildResult(Document targetPdf, Page? page1, Page? page2,
            List<EditContainer> firstEdits, List<EditContainer> secondEdits,
            SideBySideComparisonOptions options)
        {
            AddSide(targetPdf, page1, page2, firstEdits, options);
            AddSide(targetPdf, page2, page1, secondEdits, options);
        }

        /// <summary>Appends one side of the comparison: the source page reproduced at its own
        /// media box with its edits highlighted. A missing counterpart page still contributes a
        /// blank sheet so the two sides stay paired up in the result.</summary>
        private static void AddSide(Document targetPdf, Page? source, Page? counterpart,
            List<EditContainer> edits, SideBySideComparisonOptions options)
        {
            var box = source?.MediaBox ?? counterpart?.MediaBox
                ?? new Rectangle(0, 0, 595, 842);   // A4 fallback

            var sheet = targetPdf.Pages.Add();
            sheet.SetMediaBox(new Rectangle(box.LLX, box.LLY, box.URX, box.URY));

            if (source is not null) StampPage(sheet, source);
            AddChangeMarkAnnotations(sheet, edits, options);

            // Highlights go underneath: the stamped page draws its glyphs over the marker
            // fill instead of the fill masking the very text it marks. They are prepended as
            // a raw stream rather than pushed through Contents — the stamp itself edits the
            // raw stream, and a pending operator collection would not survive that.
            var marks = new List<Operator>();
            AppendMarks(marks, edits, options);
            if (marks.Count == 0) return;

            var text = new System.Text.StringBuilder("q\n");
            foreach (var op in marks) text.Append(op.ToPdf()).Append('\n');
            text.Append("Q\n");
            sheet.PrependContentStream(System.Text.Encoding.ASCII.GetBytes(text.ToString()));
        }

        private static void StampPage(Page sheet, Page source)
        {
            var stamp = new PdfPageStamp(source)
            {
                XIndent = 0,
                YIndent = 0,
                Width = source.MediaBox.Width,
                Height = source.MediaBox.Height,
                CarryAnnotations = false,
            };
            stamp.ApplyTo(sheet);
        }

        /// <summary>Under AdditionalChangeMarks, each caret marking the COUNTERPART's edit also
        /// becomes a highlight annotation describing that change — the machine readable half of
        /// the marker, letting a consumer walk the changes without re-diffing. The title numbers
        /// the change within the comparison and the contents name the operation and quote the
        /// text it applies to. The caret itself is page content, so these annotations carry no
        /// appearance of their own and add nothing to the rendered page.</summary>
        private static void AddChangeMarkAnnotations(Page sheet, List<EditContainer> edits,
            SideBySideComparisonOptions options)
        {
            if (!options.AdditionalChangeMarks) return;
            foreach (var edit in edits)
            {
                if (!edit.IsAdditionalMark || edit.Rects.Count == 0) continue;
                var text = edit.Operation.Text;
                if (string.IsNullOrEmpty(text)) continue;

                var deleted = edit.Operation.Operation == Operation.Delete;
                var rect = edit.Rects[0];
                var annotation = new Annotations.HighlightAnnotation(sheet, rect)
                {
                    Title = ChangeIdLabel + ": " + edit.Id,
                    Contents = (deleted ? DeletedLabel : InsertedLabel) + ": " + text,
                    Color = deleted ? options.DeleteColor : options.InsertColor,
                };
                sheet.Annotations.Add(annotation);
            }
        }

        private static void AppendMarks(List<Operator> ops, List<EditContainer> edits,
            SideBySideComparisonOptions options)
        {
            foreach (var edit in edits)
            {
                var color = edit.Operation.Operation == Operation.Delete
                    ? options.DeleteColor
                    : options.InsertColor;
                foreach (var rect in edit.Rects)
                {
                    if (rect.IsTrivial || rect.Width <= 0 || rect.Height <= 0) continue;
                    ops.Add(new SetRGBColor(color.R / 255.0, color.G / 255.0, color.B / 255.0));
                    ops.Add(new Re(rect.LLX, rect.LLY, rect.Width, rect.Height));
                    ops.Add(new Fill());
                }
            }
        }
    }
}
