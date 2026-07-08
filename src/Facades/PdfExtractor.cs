using System.Text;
using Aspose.Pdf.Text;

namespace Aspose.Pdf
{
    /// <summary>Text extraction mode — matches docs.aspose.com "Pure" (0) / "Raw" (1).</summary>
    public enum ExtractTextMode
    {
        /// <summary>Extract only text (no formatting).</summary>
        Pure = 0,
        /// <summary>Extract text with formatting information.</summary>
        Raw = 1,
    }

    /// <summary>Image extraction mode — matches docs.aspose.com Aspose.Pdf.ExtractImageMode.</summary>
    public enum ExtractImageMode
    {
        /// <summary>All image XObjects defined in page resources.</summary>
        DefinedInResources,
        /// <summary>Only images actually rendered (Do operators).</summary>
        ActuallyUsed,
    }
}

namespace Aspose.Pdf.Facades
{
    /// <summary>
    /// Facade for extracting text and images from a PDF document.
    /// Public surface mirrors docs.aspose.com/pdf/net/ Aspose.Pdf.Facades.PdfExtractor.
    /// Typical flow: <c>BindPdf</c> → <c>ExtractText</c> → <c>GetText(...)</c>.
    /// </summary>
    public sealed class PdfExtractor : IDisposable
    {
        private Document? _document;
        private bool _ownsDocument;
        private string? _extractedText;
        private int _nextPageCursor;
        private bool _extracted;

        /// <summary>1-based start page for extraction. 0 = from the first page.</summary>
        public int StartPage { get; set; }

        /// <summary>1-based end page for extraction. 0 = to the last page.</summary>
        public int EndPage { get; set; }

        /// <summary>Text extraction mode. Matches the Aspose.Pdf API shape —
        /// int-typed because tests do both `= (int)` and `= ExtractTextMode.Pure`
        /// (via implicit enum-to-int conversion). 0 = Pure (strip formatting),
        /// 1 = Raw (keep it).</summary>
        public int ExtractTextMode { get; set; } = (int)Aspose.Pdf.ExtractTextMode.Pure;

        /// <summary>Image extraction strategy.</summary>
        public ExtractImageMode ExtractImageMode { get; set; } = ExtractImageMode.DefinedInResources;

        /// <summary>Rendering resolution for image extraction (DPI). Stored
        /// for API-parity; the FOSS image-extraction stubs don't consult it.</summary>
        public int Resolution { get; set; } = 150;

        /// <summary>True when the most recently extracted text contains a
        /// right-to-left script run (Hebrew, Arabic, Syriac, Thaana, etc.),
        /// i.e. the text is bidirectional. False until <see cref="ExtractText()"/>
        /// has run.</summary>
        public bool IsBidi => _extractedText is not null && ContainsRightToLeft(_extractedText);

        private static bool ContainsRightToLeft(string text)
        {
            foreach (var ch in text)
            {
                // Hebrew/Arabic/Syriac/Thaana/NKo/Samaritan/Mandaic/Arabic-Extended
                // (U+0590–U+08FF) and the Hebrew/Arabic presentation-forms blocks.
                if ((ch >= '֐' && ch <= 'ࣿ') ||
                    (ch >= 'יִ' && ch <= '﷿') ||
                    (ch >= 'ﹰ' && ch <= '﻿'))
                    return true;
            }
            return false;
        }

        /// <summary>Password used when binding an encrypted PDF. Applied by
        /// <see cref="BindPdf(string)"/> / <see cref="BindPdf(Stream)"/> so the
        /// document's content can be decrypted for extraction.</summary>
        public string? Password { get; set; }

        private Aspose.Pdf.Text.TextSearchOptions? _textSearchOptions;

        /// <summary>Text-search options applied during <see cref="ExtractText()"/>.
        /// Lazy-initialized on first access so callers can mutate properties
        /// (e.g. <c>extractor.TextSearchOptions.LimitToPageBounds = true</c>)
        /// without setting a fresh instance first.</summary>
        public Aspose.Pdf.Text.TextSearchOptions TextSearchOptions
        {
            get => _textSearchOptions ??= new Aspose.Pdf.Text.TextSearchOptions();
            set => _textSearchOptions = value;
        }

        public PdfExtractor() { }

        public PdfExtractor(Document document) { BindPdf(document); }

        public void BindPdf(string inputFile)
        {
            _document = string.IsNullOrEmpty(Password)
                ? Document.Open(inputFile)
                : Document.Open(inputFile, Password);
            _ownsDocument = true;
            Reset();
        }

        public void BindPdf(Document document)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            _ownsDocument = false;
            Reset();
        }

        public void BindPdf(Stream inputStream)
        {
            _document = string.IsNullOrEmpty(Password)
                ? new Document(inputStream)
                : new Document(inputStream, Password);
            _ownsDocument = true;
            Reset();
        }

        /// <summary>
        /// Walks every page in the bound document with <see cref="TextAbsorber"/> and
        /// concatenates the extracted text. Must be called before <c>GetText</c>.
        /// </summary>
        public void ExtractText()
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");

            var absorber = new TextAbsorber();
            // Honour the facade's mode: Raw keeps the source stream's own
            // whitespace and skips the Pure-mode column grid entirely.
            if (ExtractTextMode == (int)Aspose.Pdf.ExtractTextMode.Raw)
                absorber.ExtractionOptions = new Aspose.Pdf.Text.TextExtractionOptions(
                    Aspose.Pdf.Text.TextExtractionOptions.TextFormattingMode.Raw);
            var from = StartPage > 0 ? StartPage : 1;
            var to = EndPage > 0 ? EndPage : _document.PageCount;
            for (var i = from; i <= to; i++)
                absorber.Visit(_document.Pages.At(i));

            _extractedText = ReorderRtlLines(absorber.Text);
            _extracted = true;
            _nextPageCursor = from;
        }

        /// <summary>
        /// The absorber emits each RTL word in logical glyph order but keeps the WORDS in
        /// visual (left-to-right) order, so a Hebrew/Arabic line comes out with its words
        /// reversed. For every line whose base direction is RTL (first strong-directional
        /// character is RTL), reverse the word order to logical order while keeping maximal
        /// runs of left-to-right words (e.g. numbers) in their original internal order — the
        /// bounded slice of the Unicode Bidi Algorithm that extracted RTL text needs.
        /// LTR-base lines (the overwhelming majority of documents) are returned unchanged
        /// byte-for-byte, so non-RTL extraction is unaffected.
        /// </summary>
        private static string ReorderRtlLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var lines = text.Split('\n');
            var changed = false;
            for (var li = 0; li < lines.Length; li++)
            {
                var line = lines[li];
                // Preserve a trailing '\r' so "\r\n" separators survive intact.
                var cr = line.EndsWith("\r", StringComparison.Ordinal) ? "\r" : "";
                var core = cr.Length > 0 ? line[..^1] : line;
                if (BaseDirectionIsRtl(core))
                {
                    lines[li] = ReverseWordOrder(core) + cr;
                    changed = true;
                }
            }
            return changed ? string.Join("\n", lines) : text;
        }

        /// <summary>Base direction of a line = direction of its first strong character
        /// (Unicode Bidi rule P2/P3). Numbers are not strong, so a line that opens with a
        /// number followed by Hebrew is still RTL-based.</summary>
        private static bool BaseDirectionIsRtl(string s)
        {
            foreach (var c in s)
            {
                if (Aspose.Pdf.Text.BidiReorderer.IsRtlChar(c)) return true;
                if (IsStrongLtr(c)) return false;
            }
            return false;
        }

        private static bool IsStrongLtr(char c) =>
            char.IsLetter(c) && !Aspose.Pdf.Text.BidiReorderer.IsRtlChar(c);

        /// <summary>Reverse the whitespace-separated words of an RTL line, keeping each
        /// maximal run of consecutive left-to-right words (numbers, Latin) in its original
        /// order so embedded numbers read forward.</summary>
        private static string ReverseWordOrder(string line)
        {
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length <= 1) return line;
            var isLtr = new bool[words.Length];
            for (var i = 0; i < words.Length; i++) isLtr[i] = WordIsLtr(words[i]);

            var outWords = new List<string>(words.Length);
            var idx = words.Length - 1;
            while (idx >= 0)
            {
                if (isLtr[idx])
                {
                    var j = idx;
                    while (j - 1 >= 0 && isLtr[j - 1]) j--;
                    for (var k = j; k <= idx; k++) outWords.Add(words[k]);
                    idx = j - 1;
                }
                else { outWords.Add(words[idx]); idx--; }
            }
            return string.Join(" ", outWords);
        }

        /// <summary>A word is treated as left-to-right when its first strong-or-numeric
        /// character is Latin/number; a word of only neutrals (punctuation) is not LTR, so
        /// it flows with the RTL reversal.</summary>
        private static bool WordIsLtr(string w)
        {
            foreach (var c in w)
            {
                if (Aspose.Pdf.Text.BidiReorderer.IsRtlChar(c)) return false;
                if (IsStrongLtr(c) || (c >= '0' && c <= '9')) return true;
            }
            return false;
        }

        /// <summary>Extract text from the bound PDF and save it to the given file (UTF-16 LE bytes).</summary>
        public void ExtractText(string outputFile)
        {
            ExtractText();
            File.WriteAllBytes(outputFile, Encoding.Unicode.GetBytes(_extractedText!));
        }

        /// <summary>Extract text from the bound PDF using the given encoding.
        /// The shape is <c>ExtractText(Encoding)</c>; the encoding
        /// affects how bytes are written when calling <see cref="GetText(Stream)"/>.</summary>
        public void ExtractText(Encoding encoding)
        {
            _getTextEncoding = encoding ?? Encoding.Unicode;
            ExtractText();
        }

        private Encoding _getTextEncoding = Encoding.Unicode;

        /// <summary>Writes the extracted text as bytes into <paramref name="outputStream"/>,
        /// using the encoding set by the most recent <see cref="ExtractText(Encoding)"/>
        /// call (defaults to UTF-16 LE).</summary>
        public void GetText(Stream outputStream)
        {
            EnsureExtracted();
            var bytes = _getTextEncoding.GetBytes(_extractedText!);
            outputStream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Writes the extracted text as bytes, optionally dropping
        /// non-ASCII codepoints first.</summary>
        public void GetText(Stream outputStream, bool filterNotAscii)
        {
            EnsureExtracted();
            var text = _extractedText!;
            if (filterNotAscii)
            {
                var sb = new System.Text.StringBuilder(text.Length);
                foreach (var ch in text)
                    if (ch < 128) sb.Append(ch);
                text = sb.ToString();
            }
            var bytes = _getTextEncoding.GetBytes(text);
            outputStream.Write(bytes, 0, bytes.Length);
        }

        /// <summary>Writes the extracted text to the given file path.</summary>
        public void GetText(string outputFile)
        {
            EnsureExtracted();
            File.WriteAllBytes(outputFile, _getTextEncoding.GetBytes(_extractedText!));
        }

        /// <summary>True if there is another page's text available via <see cref="GetNextPageText"/>.</summary>
        public bool HasNextPageText()
        {
            if (_document is null) return false;
            var to = EndPage > 0 ? EndPage : _document.PageCount;
            return _nextPageCursor >= 1 && _nextPageCursor <= to;
        }

        /// <summary>Writes the next page's text to the given file path (UTF-16 LE bytes).</summary>
        public void GetNextPageText(string outputFile)
        {
            using var fs = File.Create(outputFile);
            GetNextPageText(fs);
        }

        /// <summary>Writes the next page's text to the given stream (UTF-16 LE bytes).</summary>
        public void GetNextPageText(Stream outputStream)
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            if (!HasNextPageText())
                throw new InvalidOperationException("No more pages to extract.");

            var absorber = new TextAbsorber();
            absorber.Visit(_document.Pages.At(_nextPageCursor));
            var bytes = Encoding.Unicode.GetBytes(absorber.Text);
            outputStream.Write(bytes, 0, bytes.Length);
            _nextPageCursor++;
        }

        // Image extraction. ExtractImage walks every page in StartPage..EndPage
        // and gathers the page's Image XObjects into a flat list; GetNextImage
        // pops one image per call, advancing the cursor. The bool return on the
        // Get* overloads matches the reflection signature: true when
        // an image was written, false once the cursor is past the last image.
        // ExtractImageMode.ActuallyUsed (only Do-rendered images) is treated as
        // DefinedInResources for now — surfacing every page-level XObject is a
        // superset that satisfies every existing test.

        private System.Collections.Generic.List<ImageXObject>? _extractedImages;
        private int _imageCursor;

        public void ExtractImage()
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");

            // Dedupe by underlying PDF stream: a shared image referenced from
            // multiple pages (e.g. a tiling-pattern asset reused by both pages)
            // should surface once -- the contract is
            // "one entry per image object", not "one entry per page reference".
            var list = new System.Collections.Generic.List<ImageXObject>();
            var seen = new System.Collections.Generic.HashSet<Core.PdfStream>();
            var from = StartPage > 0 ? StartPage : 1;
            var to = EndPage > 0 ? EndPage : _document.PageCount;
            for (var i = from; i <= to; i++)
            {
                var page = _document.Pages.At(i);
                if (ExtractImageMode == ExtractImageMode.ActuallyUsed)
                {
                    // ActuallyUsed surfaces only images the page actually paints, in the
                    // order their `Do` operators run (PDF 32000 §8.10), recursing into
                    // Form XObjects. DefinedInResources (below) instead lists every image
                    // declared in /Resources regardless of whether it is drawn.
                    var resources = Devices.SoftwarePageRenderer.ResolveInheritedPageResources(page.Dict, page.Reader);
                    var content = Devices.SoftwarePageRenderer.GetPageContent(page.Dict, page.Reader);
                    CollectDrawnImages(resources, content, page.Reader, list, seen, 0);
                }
                else
                {
                    // DefinedInResources: every image declared in the page's resources,
                    // recursing into Form XObjects (an image nested in a form counts).
                    var pageList = new System.Collections.Generic.List<ImageXObject>();
                    CollectDefinedImages(page, pageList, seen);
                    list.AddRange(pageList);
                }
            }
            _extractedImages = list;
            _imageCursor = 0;
        }

        /// <summary>Split a resource dictionary's /XObject entries into forms and images, in dict order.</summary>
        private static (System.Collections.Generic.List<(string name, Core.PdfStream stream)> forms,
                        System.Collections.Generic.List<(string name, Core.PdfStream stream)> images)
            SplitXObjects(Core.PdfDictionary? resources, IO.PdfReader reader)
        {
            var forms = new System.Collections.Generic.List<(string, Core.PdfStream)>();
            var images = new System.Collections.Generic.List<(string, Core.PdfStream)>();
            var xobj = reader.ResolveDict(resources?.Get("XObject"));
            if (xobj is null) return (forms, images);
            foreach (var key in xobj.Keys)
            {
                var st = reader.ResolveStream(xobj.Get(key));
                if (st is null) continue;
                var sub = st.Dict.GetName("Subtype");
                if (sub == "Form") forms.Add((key, st));
                else if (sub == "Image") images.Add((key, st));
            }
            return (forms, images);
        }

        /// <summary>
        /// Collect the images declared in a page's resources, recursing into Form XObjects.
        /// Enumeration order: page forms are walked in reverse and each
        /// form's images are inserted at the front; page images are then inserted at the front.
        /// </summary>
        private static void CollectDefinedImages(Page page, System.Collections.Generic.List<ImageXObject> list,
            System.Collections.Generic.HashSet<Core.PdfStream> seen)
        {
            var reader = page.Reader;
            var resources = Devices.SoftwarePageRenderer.ResolveInheritedPageResources(page.Dict, reader);
            var (forms, images) = SplitXObjects(resources, reader);
            for (int i = forms.Count - 1; i >= 0; i--)
                CollectFormImages(forms[i].stream, reader, list, seen, 0);
            for (int i = images.Count - 1; i >= 0; i--)
                if (seen.Add(images[i].stream))
                    list.Insert(0, new ImageXObject(images[i].name, images[i].stream, reader));
            // Tiling patterns are content streams with their own /Resources; producers
            // (e.g. some word-processor exporters) emit raster images there as the pattern's only paint,
            // so an image can live only inside a pattern and never under /XObject.
            CollectPatternImages(resources, reader, list, seen, 0);
        }

        private static void CollectFormImages(Core.PdfStream form, IO.PdfReader reader,
            System.Collections.Generic.List<ImageXObject> list,
            System.Collections.Generic.HashSet<Core.PdfStream> seen, int depth)
        {
            if (depth > 32) return;
            var fres = reader.ResolveDict(form.Dict.Get("Resources"));
            var (forms, images) = SplitXObjects(fres, reader);
            for (int i = forms.Count - 1; i >= 0; i--)
            {
                var st = forms[i].stream;
                if (!seen.Add(st)) continue;
                CollectFormImages(st, reader, list, seen, depth + 1);
                var (_, childImages) = SplitXObjects(reader.ResolveDict(st.Dict.Get("Resources")), reader);
                for (int j = childImages.Count - 1; j >= 0; j--)
                    if (seen.Add(childImages[j].stream))
                        list.Insert(0, new ImageXObject(childImages[j].name, childImages[j].stream, reader));
            }
            // This form's own direct images and any images inside its patterns.
            for (int j = images.Count - 1; j >= 0; j--)
                if (seen.Add(images[j].stream))
                    list.Insert(0, new ImageXObject(images[j].name, images[j].stream, reader));
            CollectPatternImages(fres, reader, list, seen, depth + 1);
        }

        /// <summary>Collect images that live inside a resource dictionary's tiling
        /// (Type-1) patterns, recursing through each pattern's own /Resources.</summary>
        private static void CollectPatternImages(Core.PdfDictionary? resources, IO.PdfReader reader,
            System.Collections.Generic.List<ImageXObject> list,
            System.Collections.Generic.HashSet<Core.PdfStream> seen, int depth)
        {
            if (resources is null || depth > 32) return;
            var patternDict = reader.ResolveDict(resources.Get("Pattern"));
            if (patternDict is null) return;
            foreach (var key in patternDict.Keys)
            {
                // Tiling patterns are streams (PatternType 1) with /Resources;
                // shading patterns (PatternType 2) resolve to null and are skipped.
                var pat = reader.ResolveStream(patternDict.Get(key));
                if (pat is null || !seen.Add(pat)) continue;
                var pres = reader.ResolveDict(pat.Dict.Get("Resources"));
                var (pforms, pimages) = SplitXObjects(pres, reader);
                for (int i = pforms.Count - 1; i >= 0; i--)
                    CollectFormImages(pforms[i].stream, reader, list, seen, depth + 1);
                for (int j = pimages.Count - 1; j >= 0; j--)
                    if (seen.Add(pimages[j].stream))
                        list.Insert(0, new ImageXObject(pimages[j].name, pimages[j].stream, reader));
                CollectPatternImages(pres, reader, list, seen, depth + 1);
            }
        }

        /// <summary>
        /// Walk a content stream collecting the image XObjects it paints, in draw order,
        /// deduped by underlying stream, recursing into Form XObjects.
        /// </summary>
        private static void CollectDrawnImages(Core.PdfDictionary? resources, byte[] content,
            IO.PdfReader reader, System.Collections.Generic.List<ImageXObject> list,
            System.Collections.Generic.HashSet<Core.PdfStream> seen, int depth)
        {
            if (depth > 32 || content.Length == 0) return;
            var xobjects = Devices.SoftwarePageRenderer.ResolveAllXObjects(resources, reader);
            if (xobjects.Count == 0) return;

            var order = new System.Collections.Generic.List<string>();
            var parser = new Content.ContentStreamParser(reader);
            parser.OnImageDrawn += (name, _) => order.Add(name);
            try
            {
                var fonts = Devices.SoftwarePageRenderer.ResolveFontDicts(resources, reader);
                var extg = Devices.SoftwarePageRenderer.ResolveExtGStates(resources, reader);
                var cs = reader.ResolveDict(resources?.Get("ColorSpace"));
                var props = reader.ResolveDict(resources?.Get("Properties"));
                parser.Parse(content, fonts, null, extg, cs, props);
            }
            catch { /* a malformed stream still yields whatever Do names parsed so far */ }

            foreach (var name in order)
            {
                if (!xobjects.TryGetValue(name, out var xobj)) continue;
                var subtype = xobj.Dict.GetName("Subtype");
                if (subtype == "Image")
                {
                    if (seen.Add(xobj)) list.Add(new ImageXObject(name, xobj, reader));
                }
                else if (subtype == "Form")
                {
                    var formRes = reader.ResolveDict(xobj.Dict.Get("Resources")) ?? resources;
                    byte[] formContent;
                    try { formContent = reader.DecodeStream(xobj); }
                    catch { continue; }
                    CollectDrawnImages(formRes, formContent, reader, list, seen, depth + 1);
                }
            }
        }

        /// <summary>Extract every image to the given directory, naming files
        /// <c>image_N.jpg</c> for JPEG sources and <c>image_N.png</c> otherwise.</summary>
        public void ExtractImage(string outputDirectory)
        {
            ExtractImage();
            if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
                Directory.CreateDirectory(outputDirectory);
            var idx = 0;
            while (HasNextImage())
            {
                var img = _extractedImages![_imageCursor];
                var ext = img.IsJpeg ? "jpg" : "png";
                var path = Path.Combine(outputDirectory ?? string.Empty, $"image_{idx++}.{ext}");
                GetNextImage(path);
            }
        }

        /// <summary>True while <see cref="GetNextImage(string)"/> has a remaining image to write.</summary>
        public bool HasNextImage() =>
            _extractedImages is not null && _imageCursor < _extractedImages.Count;

        public bool GetNextImage(Stream outputStream)
        {
            if (!HasNextImage()) return false;
            _extractedImages![_imageCursor++].Save(outputStream);
            return true;
        }

        public bool GetNextImage(string outputFile)
        {
            if (!HasNextImage()) return false;
            _extractedImages![_imageCursor++].Save(outputFile);
            return true;
        }

        public bool GetNextImage(Stream outputStream, System.Drawing.Imaging.ImageFormat format)
        {
            if (!HasNextImage()) return false;
            _extractedImages![_imageCursor++].Save(outputStream, format);
            return true;
        }

        public bool GetNextImage(string outputFile, System.Drawing.Imaging.ImageFormat format)
        {
            if (!HasNextImage()) return false;
            using var fs = File.Create(outputFile);
            _extractedImages![_imageCursor++].Save(fs, format);
            return true;
        }

        // ── Attachment extraction — reads /Names/EmbeddedFiles (and FileAttachment
        //    annotations) via Document.EmbeddedFiles. ExtractAttachment selects which
        //    attachments a subsequent GetAttachment(...) writes; GetAttachNames lists
        //    every attachment regardless of the current selection.

        // null = nothing explicitly selected yet → Get* operates over all attachments.
        private System.Collections.Generic.List<Aspose.Pdf.FileSpecification>? _selectedAttachments;

        private System.Collections.Generic.List<Aspose.Pdf.FileSpecification> AllAttachments()
        {
            if (_document is null)
                throw new InvalidOperationException("No document bound. Call BindPdf first.");
            var list = new System.Collections.Generic.List<Aspose.Pdf.FileSpecification>();
            var ef = _document.EmbeddedFiles;
            for (var i = 1; i <= ef.Count; i++) list.Add(ef[i]);

            // Some PDFs ship their attachments only as /FileAttachment annotations
            // on a page (the /Names → /EmbeddedFiles tree is absent). Walk every
            // page's annotations and include any FileSpecification dictionaries
            // not already represented in the name-tree set. Dedupe by reference
            // equality on the underlying dict — name-tree entries that ALSO
            // appear as widget annotations would otherwise emit twice.
            var seen = new System.Collections.Generic.HashSet<Aspose.Pdf.Core.PdfDictionary>(
                System.Collections.Generic.ReferenceEqualityComparer.Instance);
            foreach (var f in list) seen.Add(f.Dict);
            foreach (var page in _document.Pages)
            {
                var annots = page.Annotations;
                for (var i = 1; i <= annots.Count; i++)
                {
                    if (annots[i] is Aspose.Pdf.Annotations.FileAttachmentAnnotation fa
                        && fa.File is { } fileSpec
                        && seen.Add(fileSpec.Dict))
                    {
                        list.Add(fileSpec);
                    }
                }
            }
            return list;
        }

        // "<4>The SmartMoney.com Daily Views.pdf" -> "The SmartMoney.com Daily Views.pdf".
        // GetAttachNames can return a "<N>" key prefix that differs from the
        // file name; strip it so callers can pass the key straight back to ExtractAttachment.
        private static string StripKeyPrefix(string key)
        {
            if (string.IsNullOrEmpty(key) || key[0] != '<') return key;
            var close = key.IndexOf('>');
            return close >= 0 ? key.Substring(close + 1) : key;
        }

        /// <summary>Select every embedded file in the bound document; a subsequent
        /// <see cref="GetAttachment(string)"/> writes all of them.</summary>
        public void ExtractAttachment() => _selectedAttachments = AllAttachments();

        /// <summary>Select a single embedded file by name for extraction.
        /// <paramref name="attachmentFileName"/> may carry a
        /// <c>"&lt;N&gt;"</c> key prefix, which is ignored when matching.</summary>
        public void ExtractAttachment(string attachmentFileName)
        {
            var name = StripKeyPrefix(attachmentFileName);
            Aspose.Pdf.FileSpecification? match = null;
            foreach (var f in AllAttachments())
            {
                if (f.Name == name || f.Name == attachmentFileName) { match = f; break; }
            }
            _selectedAttachments = new System.Collections.Generic.List<Aspose.Pdf.FileSpecification>();
            if (match is not null) _selectedAttachments.Add(match);
        }

        /// <summary>Names of every embedded file in the bound document.</summary>
        public System.Collections.Generic.IList<string> GetAttachNames()
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var f in AllAttachments()) names.Add(f.Name);
            return names;
        }

        /// <summary>FileSpecification entries for every embedded file in the bound document.</summary>
        public System.Collections.Generic.List<Aspose.Pdf.FileSpecification> GetAttachmentInfo()
            => AllAttachments();

        /// <summary>Return the selected attachments' content as MemoryStreams
        /// (all attachments when none were explicitly selected).</summary>
        public MemoryStream[] GetAttachment()
        {
            var selected = _selectedAttachments ?? AllAttachments();
            var result = new System.Collections.Generic.List<MemoryStream>();
            foreach (var f in selected)
            {
                var data = f.GetData();
                if (data is not null) result.Add(new MemoryStream(data));
            }
            return result.ToArray();
        }

        /// <summary>Write the selected attachments (all when none were explicitly
        /// selected) into the <paramref name="outputPath"/> directory, one file per
        /// attachment named after its file name. Strips any directory component
        /// from the embedded name so attachments authored with relative paths
        /// like '..//..//two_pilots.bmp' don't escape the target dir.</summary>
        public void GetAttachment(string outputPath)
        {
            var selected = _selectedAttachments ?? AllAttachments();
            var dir = string.IsNullOrEmpty(outputPath) ? "." : outputPath;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            foreach (var f in selected)
            {
                var data = f.GetData();
                if (data is null) continue;
                var raw = f.Name ?? string.Empty;
                // Normalise separators so Path.GetFileName picks up both '/'
                // and '\' authored prefixes; then strip any remaining dotted
                // path navigation that snuck through Path.GetFileName's logic.
                var safeName = Path.GetFileName(raw.Replace('/', Path.DirectorySeparatorChar)
                                                    .Replace('\\', Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(safeName)) continue;
                File.WriteAllBytes(Path.Combine(dir, safeName), data);
            }
        }

        private void EnsureExtracted()
        {
            // Docs show the ExtractText→GetText sequence, but some samples call GetText
            // without an explicit ExtractText (e.g. "CheckIfPdfContainsTextOrImages").
            // Extract lazily so both usage patterns work.
            if (!_extracted) ExtractText();
        }

        private void Reset()
        {
            _extractedText = null;
            _extracted = false;
            _nextPageCursor = 0;
            _selectedAttachments = null;
            _extractedImages = null;
            _imageCursor = 0;
        }

        public void Close()
        {
            if (_ownsDocument) _document?.Dispose();
            _document = null;
            Reset();
        }

        public void Dispose() => Close();
    }
}
