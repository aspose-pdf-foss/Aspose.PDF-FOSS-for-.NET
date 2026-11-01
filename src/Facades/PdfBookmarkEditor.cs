namespace Aspose.Pdf.Facades;

/// <summary>
/// Facade for bookmark (outline) manipulation: create, extract, delete bookmarks.
/// </summary>
public sealed class PdfBookmarkEditor : IDisposable
{
    private Document? _document;
    private bool _ownsDocument;
    private OutlineBuilder? _builder;

    /// <summary>The document bound to this editor, exposed so it can be
    /// chained into another facade.</summary>
    public Document Document => _document ?? throw new InvalidOperationException("No document bound.");

    /// <summary>Create an unbound PdfBookmarkEditor.</summary>
    public PdfBookmarkEditor() { }

    /// <summary>Create a PdfBookmarkEditor bound to a Document.</summary>
    public PdfBookmarkEditor(Document document)
    {
        _document = document;
        _ownsDocument = false;
    }

    /// <summary>Bind PDF from a file path.</summary>
    public void BindPdf(string path)
    {
        _document?.Dispose();
        _document = Document.Open(File.ReadAllBytes(path));
        _ownsDocument = true;
        _builder = null;
    }

    /// <summary>Bind PDF from a byte array.</summary>
    public void BindPdf(byte[] data)
    {
        _document?.Dispose();
        _document = Document.Open(data);
        _ownsDocument = true;
        _builder = null;
    }

    /// <summary>Bind PDF from a Stream (reads the stream fully into memory).</summary>
    public void BindPdf(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        using var ms = new MemoryStream();
        if (stream.CanSeek) stream.Position = 0;
        stream.CopyTo(ms);
        BindPdf(ms.ToArray());
    }

    /// <summary>Bind PDF from an existing Document.</summary>
    public void BindPdf(Document document)
    {
        _document = document;
        _ownsDocument = false;
        _builder = null;
    }

    /// <summary>
    /// Create a bookmark pointing to the specified page (1-based).
    /// </summary>
    public void CreateBookmarkOfPage(string bookmarkName, int pageNumber)
    {
        CreateBookmarkOfPage(new[] { bookmarkName }, new[] { pageNumber });
    }

    /// <summary>
    /// Create bookmarks for the specified pages (1-based page numbers).
    /// Each bookmark name corresponds to a page number at the same array index.
    /// </summary>
    public void CreateBookmarkOfPage(string[] bookmarkName, int[] pageNumber)
    {
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");
        if (bookmarkName.Length != pageNumber.Length)
            throw new ArgumentException("bookmarkName and pageNumber must have the same length.");

        for (int i = 0; i < bookmarkName.Length; i++)
            AddPageBookmark(bookmarkName[i], pageNumber[i] - 1); // Convert 1-based to 0-based
    }

    /// <summary>Page bookmarks are buffered in a builder, which writes a tree of its own at save.
    /// Once the document's outline tree has unsaved edits they are added to it instead, so that
    /// neither replaces the other.</summary>
    private void AddPageBookmark(string title, int pageIndex, System.Drawing.Color? color = null, bool bold = false, bool italic = false)
    {
        if (_builder is null && _document!.Outlines.IsDirty)
        {
            var item = new OutlineItemCollection(_document.Outlines) { Title = title, Bold = bold, Italic = italic };
            if (color is { } c) item.Color = c;
            if (pageIndex >= 0 && pageIndex < _document.PageCount)
                item.Destination = new Annotations.FitExplicitDestination(_document.Pages[pageIndex + 1]);
            _document.Outlines.Add(item);
            return;
        }
        _builder ??= new OutlineBuilder(_document!);
        var built = _builder.Add(title, pageIndex).SetBold(bold).SetItalic(italic);
        if (color is { } rgb) built.SetColor(rgb.R / 255.0, rgb.G / 255.0, rgb.B / 255.0);
    }

    /// <summary>Create bookmarks for every page (default).</summary>
    public void CreateBookmarks()
    {
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");
        for (int i = 0; i < _document.PageCount; i++)
            AddPageBookmark($"Page {i + 1}", i);
    }

    /// <summary>Create per-page bookmarks, each titled in the colour and the bold / italic style given.</summary>
    public void CreateBookmarks(System.Drawing.Color color, bool boldFlag, bool italicFlag)
    {
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");
        for (int i = 0; i < _document.PageCount; i++)
            AddPageBookmark($"Page {i + 1}", i, color, boldFlag, italicFlag);
    }

    /// <summary>Add a bookmark, with its child items, after the bookmarks the document has.</summary>
    public void CreateBookmarks(Bookmark bookmark)
    {
        if (bookmark is null) throw new ArgumentNullException(nameof(bookmark));
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");
        // Page bookmarks still buffered are written first: the builder's tree would replace this one at save.
        _document.FlushPendingOutlineBuilder();
        _builder = null;
        _document.Outlines.Add(ToOutlineItem(bookmark));
    }

    private OutlineItemCollection ToOutlineItem(Bookmark bm)
    {
        var item = new OutlineItemCollection(_document!.Outlines) { Title = bm.Title ?? string.Empty };
        var pageNumber = Math.Max(1, bm.PageNumber);
        if (pageNumber <= _document.PageCount)
            item.Destination = ToDestination(bm, _document.Pages[pageNumber]);
        if (bm.BoldFlag) item.Bold = true;
        if (bm.ItalicFlag) item.Italic = true;
        // Black is the title colour a bookmark is born with, and the one an outline item without /C shows.
        if (bm.TitleColor.ToArgb() != System.Drawing.Color.Black.ToArgb()) item.Color = bm.TitleColor;
        foreach (var child in bm.ChildItems)
            item.Add(ToOutlineItem(child));
        // After the children: the open state is the sign of their count.
        item.Open = bm.Open;
        return item;
    }

    /// <summary>The destination a bookmark's PageDisplay asks for, with the coordinates that
    /// display takes; a bookmark that names no display fits the page.</summary>
    private static Annotations.ExplicitDestination ToDestination(Bookmark bm, Page page) => bm.PageDisplay switch
    {
        "XYZ" => new Annotations.XYZExplicitDestination(page, bm.PageDisplay_Left, bm.PageDisplay_Top, bm.PageDisplay_Zoom),
        "FitH" => new Annotations.FitHExplicitDestination(page, bm.PageDisplay_Top),
        "FitV" => new Annotations.FitVExplicitDestination(page, bm.PageDisplay_Left),
        "FitR" => new Annotations.FitRExplicitDestination(page, bm.PageDisplay_Left, bm.PageDisplay_Bottom,
            bm.PageDisplay_Right, bm.PageDisplay_Top),
        "FitB" => new Annotations.FitBExplicitDestination(page),
        "FitBH" => new Annotations.FitBHExplicitDestination(page, bm.PageDisplay_Top),
        "FitBV" => new Annotations.FitBVExplicitDestination(page, bm.PageDisplay_Left),
        _ => new Annotations.FitExplicitDestination(page),
    };

    /// <summary>Delete every bookmark in the bound document. The outline tree is
    /// cleared and the change is written into the document so it persists on save.</summary>
    public void DeleteBookmarks()
    {
        _builder = null;
        _document?.Outlines.Delete();
    }

    /// <summary>Delete every top-level bookmark whose Title matches
    /// <paramref name="title"/>, persisting the change to the saved document.</summary>
    public void DeleteBookmarks(string title)
    {
        if (_document is null || title is null) return;
        _document.Outlines.Delete(title);
    }

    /// <summary>Save the document to a stream. When the stream is seekable it is
    /// rewound and truncated first, so saving back into the same stream a document
    /// was bound from overwrites it rather than appending.</summary>
    public void Save(Stream stream)
    {
        if (_document is null)
            throw new InvalidOperationException("No document is bound.");
        var bytes = _document.ToArray();
        if (stream.CanSeek)
        {
            stream.Position = 0;
            stream.SetLength(bytes.Length);
        }
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Save the document to a byte array.</summary>
    public byte[] Save()
    {
        if (_document is null)
            throw new InvalidOperationException("No document is bound.");
        return _document.ToArray();
    }

    /// <summary>Save the document to a file path.</summary>
    public void Save(string path)
    {
        File.WriteAllBytes(path, Save());
    }

    /// <summary>Extract the document's outline tree as a flat
    /// <see cref="Bookmarks"/> collection, walking children depth-first.</summary>
    public Bookmarks ExtractBookmarks() => ExtractBookmarks(upperLevel: false);

    /// <summary>Extract the document's outline tree.</summary>
    /// <param name="upperLevel">When true, returns only the upper-level (root)
    /// outline items; their <see cref="Bookmark.ChildItems"/> still resolve to
    /// the descendant items. When false, descends through every level and
    /// flattens the entire tree into the returned collection.</param>
    public Bookmarks ExtractBookmarks(bool upperLevel)
    {
        var result = new Bookmarks();
        if (_document is null) return result;
        // Materialise any bookmarks added via CreateBookmarkOfPage (buffered in an
        // OutlineBuilder until Save) so they are visible to this read — important when
        // a second editor over the same Document extracts what the first just added.
        _document.FlushPendingOutlineBuilder();
        // Resolve bookmark page numbers against the current page order: a page
        // inserted (e.g. a TOC page at the front) before extraction only updated
        // the Pages list, so without this sync the reader-tree walk would report
        // each destination one page short.
        _document.SyncInMemoryPageTree();
        var namedDests = new NamedDestinationCollection(_document.Reader.Catalog, _document.Reader);
        WalkOutlines(_document.Outlines, level: 1, includeChildren: !upperLevel, namedDests, result);
        return result;
    }

    /// <summary>Find bookmarks (and their descendants) whose <see cref="Bookmark.Title"/>
    /// equals <paramref name="title"/>.</summary>
    public Bookmarks ExtractBookmarks(string title)
    {
        var matches = new Bookmarks();
        if (title is null) return matches;
        foreach (var bm in ExtractBookmarks(upperLevel: false).Where(bm => string.Equals(bm.Title, title, StringComparison.Ordinal)))
        {
            matches.Add(bm);
        }
        return matches;
    }

    /// <summary>Find the document bookmark whose <see cref="Bookmark.Title"/>
    /// matches <paramref name="bookmark"/>'s (trimmed), and return its direct
    /// child items. The argument acts as a title query — only its
    /// <see cref="Bookmark.Title"/> is used, not its own child list.</summary>
    public Bookmarks ExtractBookmarks(Bookmark bookmark)
    {
        if (bookmark is null) return new Bookmarks();
        var target = bookmark.Title?.Trim();
        foreach (var bm in ExtractBookmarks(upperLevel: false))
        {
            if (bm.Title?.Trim() == target)
                return bm.ChildItems;
        }
        return new Bookmarks();
    }

    /// <summary>Export the outline tree to <paramref name="stream"/> as XML.</summary>
    public void ExportBookmarksToXML(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        // The schema: one root <Bookmark>; each outline item is a nested
        // <Title Page="<page> <fit-type> [args…]" Action="…" [Open="…"]> element whose
        // direct text is the title — the exact shape ImportBookmarksWithXML reads back.
        // Latin-1 output keeps non-Latin titles as numeric character references.
        using var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            CloseOutput = false,
            Encoding = Compat.Latin1,
        });
        writer.WriteStartDocument();
        writer.WriteStartElement("Bookmark");
        if (_document is not null)
        {
            _document.SyncInMemoryPageTree();
            var namedDests = new NamedDestinationCollection(_document.Reader.Catalog, _document.Reader);
            foreach (var item in _document.Outlines)
                WriteTitleElement(writer, item, namedDests);
        }
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    /// <summary>Export the outline tree to <paramref name="xmlFile"/>.</summary>
    public void ExportBookmarksToXML(string xmlFile)
    {
        using var fs = File.Create(xmlFile);
        ExportBookmarksToXML(fs);
    }

    private static void WriteTitleElement(System.Xml.XmlWriter writer, OutlineItem item,
        NamedDestinationCollection namedDests)
    {
        writer.WriteStartElement("Title");
        (var page, var dest) = ResolveExportPage(item, namedDests);
        if (page > 0)
            writer.WriteAttributeString("Page", FormatLegacyDestination(page, dest));
        writer.WriteAttributeString("Action", ActionTypeName(item.Action));
        if (item.Children.Count > 0)
            writer.WriteAttributeString("Open", item.Open ? "True" : "False");
        if (!string.IsNullOrEmpty(item.Title))
            writer.WriteString(item.Title);
        foreach (var child in item.Children)
            WriteTitleElement(writer, child, namedDests);
        writer.WriteEndElement();
    }

    /// <summary>The destination page for export — the item's own resolved page, or the
    /// named destination's when /Dest (or the action /D) is a name; <c>dest</c>
    /// carries the explicit destination when one is directly reachable.</summary>
    private static (int result, Annotations.ExplicitDestination? dest) ResolveExportPage(OutlineItem item, NamedDestinationCollection namedDests)
    {
        Annotations.ExplicitDestination? dest = default;
        dest = null;
        var reader = item.Reader;
        if (reader is null)
        {
            dest = item.Destination as Annotations.ExplicitDestination;
            return (dest?.PageNumber ?? 0, dest);
        }
        var destObj = reader.Resolve(item.Dict.Get("Dest"));
        if (destObj is null)
        {
            var actionDict = reader.ResolveDict(item.Dict.Get("A"));
            destObj = actionDict is not null ? reader.Resolve(actionDict.Get("D")) : null;
        }
        if (destObj is Core.PdfArray arr)
            dest = Annotations.ExplicitDestination.FromArray(arr, reader);
        var page = item.DestinationPageNumber;
        if (page == 0)
        {
            var name = destObj switch
            {
                Core.PdfName n => n.Value,
                Core.PdfString s => s.ToText(),
                _ => string.Empty,
            };
            if (name.Length > 0 && namedDests.FindByName(name) is { PageNumber: > 0 } resolved)
                page = resolved.PageNumber;
        }
        return (page, dest);
    }

    /// <summary>The legacy Page attribute — "&lt;page&gt; &lt;fit-type&gt; [args…]",
    /// e.g. "140 XYZ 56.7 735.3 0" — the inverse of <see cref="ParseLegacyDestination"/>.</summary>
    private static string FormatLegacyDestination(int page, Annotations.ExplicitDestination? dest)
    {
        static string N(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.Append(page.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (dest is null) return sb.ToString();
        sb.Append(' ').Append(dest.Type);
        switch (dest)
        {
            case Annotations.XYZExplicitDestination xyz:
                sb.Append(' ').Append(N(xyz.Left)).Append(' ').Append(N(xyz.Top)).Append(' ').Append(N(xyz.Zoom));
                break;
            case Annotations.FitRExplicitDestination r:
                sb.Append(' ').Append(N(r.Left)).Append(' ').Append(N(r.Bottom)).Append(' ').Append(N(r.Right)).Append(' ').Append(N(r.Top));
                break;
            case Annotations.FitHExplicitDestination h:
                sb.Append(' ').Append(N(h.Top));
                break;
            case Annotations.FitBHExplicitDestination bh:
                sb.Append(' ').Append(N(bh.Top));
                break;
            case Annotations.FitVExplicitDestination v:
                sb.Append(' ').Append(N(v.Left));
                break;
            case Annotations.FitBVExplicitDestination bv:
                sb.Append(' ').Append(N(bv.Left));
                break;
            // Fit / FitB carry no coordinates.
        }
        return sb.ToString();
    }

    /// <summary>Read bookmarks from XML produced by <see cref="ExportBookmarksToXML(System.IO.Stream)"/>
    /// and add them to the bound document.</summary>
    public void ImportBookmarksWithXML(Stream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");

        var doc = new System.Xml.XmlDocument();
        doc.Load(stream);
        var root = doc.DocumentElement;
        if (root is null) return;
        // Legacy exporter schema: <Bookmark><Title Page="1 FitV " Action="GoTo">text
        // <Title …>child</Title></Title></Bookmark> — nested Title elements form the
        // outline hierarchy and the Page attribute carries "<page> <fit-type> [args…]".
        if (string.Equals(root.LocalName, "Bookmark", StringComparison.OrdinalIgnoreCase))
        {
            foreach (System.Xml.XmlNode node in root.ChildNodes)
            {
                if (node is System.Xml.XmlElement el) ImportLegacyTitleElement(el, parent: null);
            }
            return;
        }
        foreach (System.Xml.XmlNode node in root.ChildNodes)
        {
            if (node is System.Xml.XmlElement el) ImportBookmarkElement(el);
        }
    }

    /// <summary>Read bookmarks from an XML file and add them to the bound document.</summary>
    public void ImportBookmarksWithXML(string xmlFile)
    {
        using var fs = File.OpenRead(xmlFile);
        ImportBookmarksWithXML(fs);
    }

    private void ImportLegacyTitleElement(System.Xml.XmlElement el, OutlineItem? parent)
    {
        if (!string.Equals(el.LocalName, "Title", StringComparison.OrdinalIgnoreCase)) return;

        // The bookmark's own text = the element's DIRECT text nodes; nested <Title>
        // elements are child bookmarks, whose text must not leak into this title.
        var titleText = new System.Text.StringBuilder();
        foreach (System.Xml.XmlNode node in el.ChildNodes)
        {
            if (node is System.Xml.XmlText or System.Xml.XmlCDataSection)
                titleText.Append(node.Value);
        }

        var item = new OutlineItemCollection(_document!.Outlines)
        {
            Title = titleText.ToString().Trim(),
        };
        if (ParseLegacyDestination(el.GetAttribute("Page")) is { } dest)
            item.Destination = dest;

        if (parent is null) _document.Outlines.Add(item);
        else parent.Add(item);

        foreach (System.Xml.XmlNode node in el.ChildNodes)
        {
            if (node is System.Xml.XmlElement childEl) ImportLegacyTitleElement(childEl, item);
        }
    }

    /// <summary>Parse the legacy Page attribute — "&lt;page&gt; &lt;fit-type&gt; [args…]",
    /// e.g. "1 FitV " or "3 XYZ 0 792 0" — into an explicit destination.</summary>
    private static Annotations.ExplicitDestination? ParseLegacyDestination(string? pageAttr)
    {
        if (string.IsNullOrWhiteSpace(pageAttr)) return null;
        var tokens = pageAttr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return null;
        if (!int.TryParse(tokens[0], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var page) || page < 1)
            return null;

        double? Arg(int i)
        {
            if (i + 1 >= tokens.Length) return null;
            return double.TryParse(tokens[i + 1], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }

        var fit = tokens.Length > 1 ? tokens[1] : "XYZ";
        return fit switch
        {
            "Fit" => new Annotations.FitExplicitDestination(page),
            "FitH" => new Annotations.FitHExplicitDestination(page, Arg(0)),
            "FitV" => new Annotations.FitVExplicitDestination(page, Arg(0)),
            "FitB" => new Annotations.FitBExplicitDestination(page),
            "FitBH" => new Annotations.FitBHExplicitDestination(page, Arg(0)),
            "FitBV" => new Annotations.FitBVExplicitDestination(page, Arg(0)),
            "FitR" when Arg(0) is { } l && Arg(1) is { } b && Arg(2) is { } r && Arg(3) is { } t
                => new Annotations.FitRExplicitDestination(page, l, b, r, t),
            _ => new Annotations.XYZExplicitDestination(page, Arg(0) ?? 0, Arg(1) ?? 0, Arg(2) ?? 0),
        };
    }

    /// <summary>Export bookmarks to a standalone HTML file. Stored only — no
    /// HTML emission is performed in this build.</summary>
    public void ExportBookmarksToHtml(string inPdfFile, string outHtmlFile) { /* stored only: no HTML is emitted */ }

    /// <summary>Extract bookmarks to an HTML page with the given CSS. Stored only.</summary>
    public void ExtractBookmarksToHTML(string pdfFile, string cssFile) { /* stored only: no HTML is emitted */ }

    /// <summary>Rename every bookmark whose title equals <paramref name="sTitle"/>
    /// to <paramref name="dTitle"/>, throughout the outline tree. The change is
    /// written into the outline dictionaries so it persists when the document is saved.</summary>
    public void ModifyBookmarks(string sTitle, string dTitle)
    {
        if (_document is null || sTitle is null || dTitle is null) return;
        foreach (var item in _document.Outlines)
            RenameOutline(item, sTitle, dTitle);
    }

    private static void RenameOutline(OutlineItem item, string sTitle, string dTitle)
    {
        if (string.Equals(item.Title, sTitle, StringComparison.Ordinal))
            item.Title = dTitle;
        foreach (var child in item.Children)
            RenameOutline(child, sTitle, dTitle);
    }

    private void ImportBookmarkElement(System.Xml.XmlElement el)
    {
        if (!string.Equals(el.LocalName, "bookmark", StringComparison.OrdinalIgnoreCase)) return;
        var title = el.GetAttribute("title");
        var pageAttr = el.GetAttribute("page");
        if (int.TryParse(pageAttr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var page) && page > 0)
        {
            AddPageBookmark(title ?? string.Empty, page - 1);
        }
        foreach (System.Xml.XmlNode child in el.ChildNodes)
        {
            if (child is System.Xml.XmlElement childEl) ImportBookmarkElement(childEl);
        }
    }

    private static void WalkOutlines(OutlineCollection outlines, int level,
        bool includeChildren, NamedDestinationCollection namedDests, Bookmarks accumulator)
    {
        foreach (var item in outlines)
        {
            var bm = ToBookmark(item, level, namedDests);
            accumulator.Add(bm);
            if (includeChildren)
                WalkChildren(item, level + 1, namedDests, accumulator, bm.ChildItems);
            else
                PopulateChildItemsRecursive(item, level + 1, namedDests, bm.ChildItems);
        }
    }

    private static void WalkChildren(OutlineItem parent, int level,
        NamedDestinationCollection namedDests,
        Bookmarks flatAccumulator, Bookmarks childAccumulator)
    {
        foreach (var child in parent.Children)
        {
            var bm = ToBookmark(child, level, namedDests);
            flatAccumulator.Add(bm);
            childAccumulator.Add(bm);
            WalkChildren(child, level + 1, namedDests, flatAccumulator, bm.ChildItems);
        }
    }

    private static void PopulateChildItemsRecursive(OutlineItem parent, int level,
        NamedDestinationCollection namedDests, Bookmarks childAccumulator)
    {
        foreach (var child in parent.Children)
        {
            var bm = ToBookmark(child, level, namedDests);
            childAccumulator.Add(bm);
            PopulateChildItemsRecursive(child, level + 1, namedDests, bm.ChildItems);
        }
    }

    private static string ActionTypeName(Annotations.PdfAction? action) => action switch
    {
        Annotations.GoToAction => "GoTo",
        Annotations.GoToRemoteAction => "GoToR",
        Annotations.UriAction or Annotations.GoToURIAction => "URI",
        Annotations.LaunchAction => "Launch",
        Annotations.NamedAction => "Named",
        Annotations.JavascriptAction => "JavaScript",
        Annotations.SubmitFormAction => "SubmitForm",
        Annotations.ImportDataAction => "ImportData",
        Annotations.HideAction => "Hide",
        Annotations.RenditionAction => "Rendition",
        not null => "Unknown",
        // A bookmark with a direct /Dest (or no action at all) is semantically a
        // go-to, so report its Action as "GoTo" rather than leaving it empty.
        _ => "GoTo",
    };

    private static Bookmark ToBookmark(OutlineItem item, int level, NamedDestinationCollection namedDests)
    {
        var action = item.Action;
        var actionType = ActionTypeName(action);
        var pageNumber = item.DestinationPageNumber;
        string destinationName = string.Empty;

        // If DestinationPageNumber didn't resolve, the destination is likely a
        // named destination (PdfName/PdfString in /Dest or /A's /D). Look it up
        // in the catalog's /Dests dict or /Names → /Dests name tree.
        if (pageNumber == 0)
        {
            var dest = item.Reader?.Resolve(item.Dict.Get("Dest"));
            if (dest is null && item.Reader is not null)
            {
                var actionDict = item.Reader.ResolveDict(item.Dict.Get("A"));
                dest = actionDict is not null ? item.Reader.Resolve(actionDict.Get("D")) : null;
            }
            destinationName = dest switch
            {
                Core.PdfName n => n.Value,
                Core.PdfString s => s.ToText(),
                _ => string.Empty,
            };
            if (destinationName.Length > 0)
            {
                var resolved = namedDests.FindByName(destinationName);
                if (resolved is not null && resolved.PageNumber > 0)
                    pageNumber = resolved.PageNumber;
            }
        }

        var bm = new Bookmark
        {
            Title = item.Title,
            PageNumber = pageNumber,
            BoldFlag = item.IsBold,
            ItalicFlag = item.IsItalic,
            Level = level,
            TitleColor = item.Color,
            Action = actionType,
        };
        if (destinationName.Length > 0) bm.Destination = destinationName;
        else
            bm.Destination = pageNumber > 0
                ? pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty;
        if (action is Annotations.GoToRemoteAction remote)
            bm.RemoteFile = remote.StoredFile?.Name;
        PopulatePageDisplay(bm, item);
        return bm;
    }

    // Populate the bookmark's PageDisplay (destination fit-mode name) and the
    // mode-specific coordinates (Left/Top/Right/Bottom/Zoom) from its explicit
    // destination array, found under /Dest or the /A action's /D. Mirrors the
    // PDF 32000 §12.3.2.2 explicit-destination forms via ExplicitDestination.
    private static void PopulatePageDisplay(Bookmark bm, OutlineItem item)
    {
        var reader = item.Reader;
        if (reader is null) return;

        var destObj = reader.Resolve(item.Dict.Get("Dest"));
        if (destObj is null)
        {
            var actionDict = reader.ResolveDict(item.Dict.Get("A"));
            destObj = actionDict is not null ? reader.Resolve(actionDict.Get("D")) : null;
        }
        if (destObj is not Core.PdfArray arr) return;

        var dest = Annotations.ExplicitDestination.FromArray(arr, reader);
        if (dest is null) return;

        bm.PageDisplay = dest.Type;
        switch (dest)
        {
            case Annotations.XYZExplicitDestination xyz:
                bm.PageDisplay_Left = (int)xyz.Left;
                bm.PageDisplay_Top = (int)xyz.Top;
                bm.PageDisplay_Zoom = (int)xyz.Zoom;
                break;
            case Annotations.FitRExplicitDestination r:
                bm.PageDisplay_Left = (int)r.Left;
                bm.PageDisplay_Bottom = (int)r.Bottom;
                bm.PageDisplay_Right = (int)r.Right;
                bm.PageDisplay_Top = (int)r.Top;
                break;
            case Annotations.FitHExplicitDestination h:
                bm.PageDisplay_Top = (int)h.Top;
                break;
            case Annotations.FitBHExplicitDestination bh:
                bm.PageDisplay_Top = (int)bh.Top;
                break;
            case Annotations.FitVExplicitDestination v:
                bm.PageDisplay_Left = (int)v.Left;
                break;
            case Annotations.FitBVExplicitDestination bv:
                bm.PageDisplay_Left = (int)bv.Left;
                break;
            // Fit / FitB carry no coordinates.
        }
    }

    public void Dispose()
    {
        if (_ownsDocument)
            _document?.Dispose();
        _document = null;
    }

    /// <summary>Release the bound document, mirroring <see cref="Dispose"/>.</summary>
    public void Close() => Dispose();
}
