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

        _builder ??= new OutlineBuilder(_document);

        for (int i = 0; i < bookmarkName.Length; i++)
            _builder.Add(bookmarkName[i], pageNumber[i] - 1); // Convert 1-based to 0-based
    }

    /// <summary>Create bookmarks for every page (default).</summary>
    public void CreateBookmarks()
    {
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");
        _builder ??= new OutlineBuilder(_document);
        for (int i = 0; i < _document.PageCount; i++)
            _builder.Add($"Page {i + 1}", i);
    }

    /// <summary>Create per-page bookmarks with explicit colour / bold / italic styling.
    /// Styling is stored only — outline rendering does not currently honour colour.</summary>
    public void CreateBookmarks(System.Drawing.Color color, bool boldFlag, bool italicFlag)
        => CreateBookmarks();

    /// <summary>Create bookmarks from a Bookmark tree (recursive add of children).</summary>
    public void CreateBookmarks(Bookmark bookmark)
    {
        if (bookmark is null) throw new ArgumentNullException(nameof(bookmark));
        if (_document is null)
            throw new InvalidOperationException("No document is bound. Call BindPdf first.");
        _builder ??= new OutlineBuilder(_document);
        AddBookmarkRecursive(_builder, bookmark);
    }

    private static void AddBookmarkRecursive(OutlineBuilder builder, Bookmark bm)
    {
        var item = builder.Add(bm.Title ?? string.Empty, Math.Max(0, bm.PageNumber - 1));
        ApplyBookmarkStyle(item, bm);
        foreach (var child in bm.ChildItems)
            AddChildRecursive(item, child);
    }

    private static void AddChildRecursive(OutlineItemBuilder parent, Bookmark bm)
    {
        var item = parent.AddChild(bm.Title ?? string.Empty, Math.Max(0, bm.PageNumber - 1));
        ApplyBookmarkStyle(item, bm);
        foreach (var child in bm.ChildItems)
            AddChildRecursive(item, child);
    }

    private static void ApplyBookmarkStyle(OutlineItemBuilder item, Bookmark bm)
    {
        item.SetOpen(bm.Open);
        if (bm.BoldFlag) item.SetBold(true);
        if (bm.ItalicFlag) item.SetItalic(true);
    }

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
        foreach (var bm in ExtractBookmarks(upperLevel: false))
        {
            if (string.Equals(bm.Title, title, StringComparison.Ordinal))
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
        using var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            CloseOutput = false,
        });
        writer.WriteStartDocument();
        writer.WriteStartElement("bookmarks");
        if (_document is not null)
        {
            foreach (var bm in ExtractBookmarks(upperLevel: true))
                WriteBookmarkXml(writer, bm);
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
        _builder ??= new OutlineBuilder(_document);
        foreach (System.Xml.XmlNode node in root.ChildNodes)
        {
            if (node is System.Xml.XmlElement el) ImportBookmarkElement(el);
        }
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

    /// <summary>Read bookmarks from an XML file and add them to the bound document.</summary>
    public void ImportBookmarksWithXML(string xmlFile)
    {
        using var fs = File.OpenRead(xmlFile);
        ImportBookmarksWithXML(fs);
    }

    /// <summary>Export bookmarks to a standalone HTML file. Stored only — no
    /// HTML emission is performed in this build.</summary>
    public void ExportBookmarksToHtml(string inPdfFile, string outHtmlFile) { }

    /// <summary>Extract bookmarks to an HTML page with the given CSS. Stored only.</summary>
    public void ExtractBookmarksToHTML(string pdfFile, string cssFile) { }

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

    private static void WriteBookmarkXml(System.Xml.XmlWriter writer, Bookmark bm)
    {
        writer.WriteStartElement("bookmark");
        if (!string.IsNullOrEmpty(bm.Title)) writer.WriteAttributeString("title", bm.Title);
        if (bm.PageNumber > 0)
            writer.WriteAttributeString("page", bm.PageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteAttributeString("level", bm.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (bm.BoldFlag) writer.WriteAttributeString("bold", "true");
        if (bm.ItalicFlag) writer.WriteAttributeString("italic", "true");
        foreach (var child in bm.ChildItems)
            WriteBookmarkXml(writer, child);
        writer.WriteEndElement();
    }

    private void ImportBookmarkElement(System.Xml.XmlElement el)
    {
        if (!string.Equals(el.LocalName, "bookmark", StringComparison.OrdinalIgnoreCase)) return;
        var title = el.GetAttribute("title");
        var pageAttr = el.GetAttribute("page");
        if (int.TryParse(pageAttr, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var page) && page > 0)
        {
            _builder!.Add(title ?? string.Empty, page - 1);
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

    private static Bookmark ToBookmark(OutlineItem item, int level, NamedDestinationCollection namedDests)
    {
        var action = item.Action;
        var actionType = action switch
        {
            Annotations.GoToAction => "GoTo",
            Annotations.GoToRemoteAction => "GoToR",
            Annotations.UriAction or Annotations.GoToURIAction => "URI",
            Annotations.LaunchAction => "Launch",
            Annotations.NamedAction => "Named",
            Annotations.JavascriptAction => "JavaScript",
            Annotations.SubmitFormAction => "SubmitForm",
            not null => "Unknown",
            // A bookmark with a direct /Dest (or no action at all) is semantically a
            // go-to, so report its Action as "GoTo" rather than leaving it empty.
            _ => "GoTo",
        };
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
        bm.Destination = destinationName.Length > 0
            ? destinationName
            : (pageNumber > 0
                ? pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty);
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
