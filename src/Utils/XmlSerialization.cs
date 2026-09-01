using System.Globalization;
using System.Text;
using System.Xml;
using Aspose.Pdf.Drawing;
using Aspose.Pdf.Text;

namespace Aspose.Pdf;

/// <summary>
/// Writes a document's generator DOM back out as Aspose.Pdf template XML — the
/// counterpart of <see cref="XmlBinding"/>, which reads that dialect.
/// </summary>
/// <remarks>
/// <para>The pair is only useful if it round-trips, so this writer deliberately emits the
/// vocabulary the reader consumes and nothing else: an element the binder would ignore
/// is content silently lost on the way back in. Lengths go out as bare points (the
/// binder's default unit) and colours as <c>#RRGGBB</c>, both of which it parses
/// exactly.</para>
/// <para>The root carries <c>RoundTrip="true"</c>. A template someone AUTHORED is read
/// with the template-era calibrations (a table laid out in the XML-generator dialect,
/// a box's Left/Top measured from the content area, cell text with its stylesheet
/// indentation collapsed); a template this writer produced describes a DOM that already
/// exists, so those calibrations would deform it. The flag tells the binder which of the
/// two it is holding.</para>
/// </remarks>
internal static class XmlSerialization
{
    public static void Save(Document document, Stream output)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
        };
        using var writer = XmlWriter.Create(output, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("Document");
        writer.WriteAttributeString("RoundTrip", "true");
        // The DOCUMENT's own page margins: layout falls back to them per side for any
        // page that declared none of its own, and a caller may set them AFTER the pages
        // were added (a document sets a 105 pt top margin only). Leaving them
        // out let the reloaded document lay every page out on the 72/90 defaults.
        if (document.PageInfo?.Margin is { IsTouched: true } docMargin)
        {
            writer.WriteStartElement("DocumentPageInfo");
            writer.WriteStartElement("Margin");
            // ONE ATTRIBUTE PER TOUCHED SIDE. A document margin is authored side by
            // side (one sets left, right and top and leaves the bottom to the 72 pt
            // default), so writing all four would turn an untouched side into an
            // authored zero and hand the reloaded pages no bottom margin at all.
            if (docMargin.LeftTouched) writer.WriteAttributeString("Left", Num(docMargin.Left));
            if (docMargin.RightTouched) writer.WriteAttributeString("Right", Num(docMargin.Right));
            if (docMargin.TopTouched) writer.WriteAttributeString("Top", Num(docMargin.Top));
            if (docMargin.BottomTouched) writer.WriteAttributeString("Bottom", Num(docMargin.Bottom));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        foreach (var page in document.Pages)
            WritePage(writer, page);
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static void WritePage(XmlWriter w, Page page)
    {
        w.WriteStartElement("Page");
        WritePageInfo(w, page);
        // Only the background artifact: the rest of the artifact surface (watermarks,
        // pagination stamps) is written into the page content by whoever added it, so
        // it is already part of the saved page rather than DOM state to reproduce.
        foreach (var artifact in page.Artifacts)
            if (artifact is BackgroundArtifact { BackgroundImage: not null } background)
                WriteBackground(w, background);
        if (page.Header is { } header) WriteBand(w, "Header", header);
        if (page.Footer is { } footer) WriteBand(w, "Footer", footer);
        foreach (var paragraph in page.Paragraphs)
            WriteParagraph(w, paragraph);
        w.WriteEndElement();
    }

    private static void WritePageInfo(XmlWriter w, Page page)
    {
        w.WriteStartElement("PageInfo");
        // The MEDIA BOX, not PageInfo.Width/Height: a page turned landscape keeps
        // reporting the portrait size the caller authored (the generator sizes content
        // from that read) while the sheet itself is already sideways. The sheet is the
        // geometry to reproduce, and writing it means IsLandscape must NOT go out too —
        // that would turn the page a second time on the way back in.
        w.WriteAttributeString("Width", Num(page.MediaBox.Width));
        w.WriteAttributeString("Height", Num(page.MediaBox.Height));
        WriteMargin(w, "Margin", page.PageInfo.Margin);
        w.WriteEndElement();
    }

    private static void WriteBackground(XmlWriter w, BackgroundArtifact background)
    {
        var bytes = ReadStreamBytes(background.BackgroundImage);
        if (bytes is null) return;
        w.WriteStartElement("BackgroundArtifact");
        w.WriteStartElement("Data");
        w.WriteBase64(bytes, 0, bytes.Length);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteBand(XmlWriter w, string name, HeaderFooter band)
    {
        w.WriteStartElement(name);
        WriteMargin(w, "Margin", band.Margin);
        foreach (var paragraph in band.Paragraphs)
            WriteParagraph(w, paragraph);
        w.WriteEndElement();
    }

    private static void WriteParagraph(XmlWriter w, BaseParagraph paragraph)
    {
        switch (paragraph)
        {
            case Heading heading: WriteHeading(w, heading); break;
            case TextFragment tf: WriteTextFragment(w, tf); break;
            case Table table: WriteTable(w, table); break;
            case FloatingBox box: WriteFloatingBox(w, box); break;
            case Image image: WriteImage(w, image); break;
            case Graph graph: WriteGraph(w, graph); break;
            case HtmlFragment html: WriteHtmlFragment(w, html); break;
            // Anything else has no binder-side counterpart yet; writing an element the
            // reader ignores would look like a successful round-trip while dropping the
            // content, so it is left out until both halves exist.
        }
    }

    /// <summary>Common <see cref="BaseParagraph"/> attributes. Alignment is written
    /// only when it names a real edge — the enum's <c>None</c> is "unset", and
    /// re-declaring it would pin the layout default as an authored choice.</summary>
    /// <remarks>⚠ Read through <see cref="Effective"/>, never off the base type.
    /// TextFragment, FloatingBox and HtmlFragment SHADOW several of these with
    /// <c>new</c> properties, and layout reads the shadowing ones — a base-typed read
    /// reports the untouched base field and quietly writes the wrong document.</remarks>
    private static void WriteParagraphAttributes(XmlWriter w, BaseParagraph p)
    {
        var a = Effective(p);
        if (a.Horizontal != HorizontalAlignment.None)
            w.WriteAttributeString("HorizontalAlignment", a.Horizontal.ToString());
        if (a.Vertical != VerticalAlignment.None)
            w.WriteAttributeString("VerticalAlignment", a.Vertical.ToString());
        if (a.IsInLineParagraph) w.WriteAttributeString("IsInLineParagraph", "true");
        if (a.IsInNewPage) w.WriteAttributeString("IsInNewPage", "true");
        if (a.IsKeptWithNext) w.WriteAttributeString("IsKeptWithNext", "true");
        if (p.IsFirstParagraphInColumn) w.WriteAttributeString("IsFirstParagraphInColumn", "true");
        if (a.ZIndex != 0) w.WriteAttributeString("ZIndex", a.ZIndex.ToString(CultureInfo.InvariantCulture));
        WriteHyperlinkAttributes(w, a.Hyperlink);
    }

    /// <summary>The paragraph flags as the LAYOUT sees them, taken off the concrete
    /// type so a shadowing <c>new</c> property wins over the hidden base field.</summary>
    private static (HorizontalAlignment Horizontal, VerticalAlignment Vertical,
        bool IsInLineParagraph, bool IsInNewPage, bool IsKeptWithNext, int ZIndex,
        Hyperlink? Hyperlink) Effective(BaseParagraph p) => p switch
    {
        // TextFragment.Hyperlink is set-only on the public surface; HyperlinkValue is
        // the read side of the same field.
        TextFragment tf => (tf.HorizontalAlignment, tf.VerticalAlignment, tf.IsInLineParagraph,
            tf.IsInNewPage, p.IsKeptWithNext, p.ZIndex, tf.HyperlinkValue),
        FloatingBox b => (b.HorizontalAlignment, b.VerticalAlignment, p.IsInLineParagraph,
            p.IsInNewPage, p.IsKeptWithNext, b.ZIndex, p.Hyperlink),
        HtmlFragment h => (p.HorizontalAlignment, p.VerticalAlignment, p.IsInLineParagraph,
            h.IsInNewPage, h.IsKeptWithNext, p.ZIndex, p.Hyperlink),
        _ => (p.HorizontalAlignment, p.VerticalAlignment, p.IsInLineParagraph,
            p.IsInNewPage, p.IsKeptWithNext, p.ZIndex, p.Hyperlink),
    };

    /// <summary>A hyperlink's destination, flattened onto the owning element. Only the
    /// three concrete kinds carry one; a bare <see cref="Hyperlink"/> is a marker with
    /// nothing to write, and a local link to a paragraph OBJECT cannot be named in XML
    /// (its page number can).</summary>
    private static void WriteHyperlinkAttributes(XmlWriter w, Hyperlink? link)
    {
        switch (link)
        {
            case WebHyperlink { Url: { Length: > 0 } url }:
                w.WriteAttributeString("HyperlinkUrl", url);
                break;
            case FileHyperlink { FileName: { Length: > 0 } file }:
                w.WriteAttributeString("HyperlinkFile", file);
                break;
            case LocalHyperlink { TargetPageNumber: > 0 } local:
                w.WriteAttributeString("HyperlinkPage",
                    local.TargetPageNumber.ToString(CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void WriteHeading(XmlWriter w, Heading heading)
    {
        w.WriteStartElement("Heading");
        WriteParagraphAttributes(w, heading);
        w.WriteAttributeString("Level", heading.Level.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("Style", heading.Style.ToString());
        if (heading.IsAutoSequence) w.WriteAttributeString("IsAutoSequence", "true");
        if (heading.IsInList) w.WriteAttributeString("IsInList", "true");
        WriteMargin(w, "Margin", heading.Margin);
        WriteTextState(w, heading.TextState, wrapped: null);
        foreach (var segment in heading.Segments)
            WriteTextState(w, segment.TextState, wrapped: segment.Text ?? string.Empty);
        w.WriteEndElement();
    }

    // ---- text ------------------------------------------------------------------

    /// <summary>A fragment writes its own state as a bare <c>&lt;TextState&gt;</c> and
    /// each segment as a state element WRAPPING the segment. Both shapes are the
    /// binder's, and both are read on the path that keeps a font name literal — the
    /// state-nested-in-segment shape instead resolves the name through the system
    /// repository (Helvetica lands on the Times faces), which a round-trip must not do.
    /// </summary>
    private static void WriteTextFragment(XmlWriter w, TextFragment tf)
    {
        w.WriteStartElement("TextFragment");
        WriteParagraphAttributes(w, tf);
        // The fragment's OWN text, which is not the join of its segments: a fragment
        // constructed from a string keeps it, one built by adding segments to an empty
        // fragment has none, and the flow reads it. Segments still carry the content —
        // this only records which of the two shapes built the fragment.
        if (tf.Text is { Length: > 0 } ownText) w.WriteAttributeString("Text", ownText);
        // A note body built from a plain string is only as wide as its text; a
        // caller-built note paragraph claims the whole band. Losing the distinction
        // re-wraps the note.
        if (tf.AutoNoteText) w.WriteAttributeString("AutoNoteText", "true");
        WriteMargin(w, "Margin", tf.Margin);
        WriteTextState(w, tf.TextState, wrapped: null);
        foreach (var segment in tf.Segments)
            WriteTextState(w, segment.TextState, wrapped: segment.Text ?? string.Empty, segment.Hyperlink);
        WriteNote(w, "FootNote", tf.FootNote);
        WriteNote(w, "EndNote", tf.EndNote);
        w.WriteEndElement();
    }

    private static void WriteNote(XmlWriter w, string name, Note? note)
    {
        if (note is null) return;
        w.WriteStartElement(name);
        if (note.Text is { Length: > 0 } marker) w.WriteAttributeString("Text", marker);
        WriteTextState(w, note.TextState, wrapped: null);
        foreach (var paragraph in note.Paragraphs)
            WriteParagraph(w, paragraph);
        w.WriteEndElement();
    }

    /// <summary>One <c>&lt;TextState&gt;</c>. With <paramref name="wrapped"/> non-null the
    /// element carries a <c>&lt;TextSegment&gt;</c> holding that text.</summary>
    private static void WriteTextState(XmlWriter w, TextState? state, string? wrapped,
        Hyperlink? hyperlink = null)
    {
        if (state is null && wrapped is null) return;
        w.WriteStartElement("TextState");
        if (state is not null)
        {
            if (state.FontSizeTouched) w.WriteAttributeString("FontSize", Num(state.FontSize));
            if (FontAttribute(state) is { } fontName) w.WriteAttributeString("Font", fontName);
            if (state.ForegroundColor is { } fg) w.WriteAttributeString("ForegroundColor", Hex(fg));
            if (state.StrokingColor is { } stroke) w.WriteAttributeString("StrokingColor", Hex(stroke));
            // Bold/italic go out as the numeric FontStyle flags rather than by
            // decorating the font name: the binder reads the flags directly, and a
            // decorated name would have to resolve to a real face to survive.
            var style = (state.IsBold ? 1 : 0) | (state.IsItalic ? 2 : 0);
            if (style != 0) w.WriteAttributeString("FontStyle", style.ToString(CultureInfo.InvariantCulture));
            if (state.Underline) w.WriteAttributeString("Underline", "true");
            if (state.IsSuperscript) w.WriteAttributeString("IsSuperscript", "true");
            if (state.IsSubscript) w.WriteAttributeString("IsSubscript", "true");
            if (state.LineSpacing > 0) w.WriteAttributeString("LineSpacing", Num(state.LineSpacing));
            if (state.CharacterSpacing != 0) w.WriteAttributeString("CharacterSpacing", Num(state.CharacterSpacing));
            if (state.WordSpacing != 0) w.WriteAttributeString("WordSpacing", Num(state.WordSpacing));
            if (state.HorizontalScaling != 100) w.WriteAttributeString("HorizontalScaling", Num(state.HorizontalScaling));
            if (state.Rotation != 0) w.WriteAttributeString("Rotation", Num(state.Rotation));
            if (state.TextRise != 0) w.WriteAttributeString("TextRise", Num(state.TextRise));
            // A fragment's alignment can be declared on its TEXT STATE rather than on
            // the paragraph (TextState.HorizontalAlignment), and the round trip used to
            // drop it — a centred title came back left-aligned.
            if (state.HorizontalAlignment != HorizontalAlignment.Left
                && state.HorizontalAlignment != HorizontalAlignment.None)
                w.WriteAttributeString("TextHorizontalAlignment", state.HorizontalAlignment.ToString());
        }
        WriteHyperlinkAttributes(w, hyperlink);
        if (wrapped is not null)
        {
            w.WriteStartElement("TextSegment");
            w.WriteString(wrapped);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>The font name to write, or null to leave the attribute off. The binder
    /// validates every Font attribute through the repository and throws when one does
    /// not resolve, so a name that cannot be found again (a face loaded from bytes) is
    /// better left unwritten than turned into a bind-time exception.</summary>
    private static string? FontAttribute(TextState state)
    {
        var name = state.FontName;
        if (string.IsNullOrEmpty(name)) return null;
        return FontRepository.TryFindFont(name) is null ? null : name;
    }

    private static void WriteHtmlFragment(XmlWriter w, HtmlFragment html)
    {
        w.WriteStartElement("HtmlFragment");
        WriteParagraphAttributes(w, html);
        WriteMargin(w, "Margin", html.Margin);
        w.WriteStartElement("HtmlContent");
        w.WriteCData(html.HtmlContent ?? string.Empty);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    // ---- tables ----------------------------------------------------------------

    private static void WriteTable(XmlWriter w, Table table)
    {
        w.WriteStartElement("Table");
        WriteParagraphAttributes(w, table);
        if (table.ColumnWidths is { Length: > 0 } widths)
            w.WriteAttributeString("ColumnWidths", widths);
        if (table.RepeatingRowsCount > 0)
            w.WriteAttributeString("RepeatingRowsCount", table.RepeatingRowsCount.ToString(CultureInfo.InvariantCulture));
        if (table.RepeatingColumnsCount > 0)
            w.WriteAttributeString("RepeatingColumnsCount", table.RepeatingColumnsCount.ToString(CultureInfo.InvariantCulture));
        if (table.ColumnAdjustment != ColumnAdjustment.Customized)
            w.WriteAttributeString("ColumnAdjustment", table.ColumnAdjustment.ToString());
        w.WriteAttributeString("Alignment", table.Alignment.ToString());
        if (table.BackgroundColor is { } bg) w.WriteAttributeString("BackgroundColor", Hex(bg));
        if (table.Broken != TableBroken.None) w.WriteAttributeString("Broken", table.Broken.ToString());
        if (!table.IsBroken) w.WriteAttributeString("IsBroken", "false");
        if (table.IsBordersIncluded) w.WriteAttributeString("IsBordersIncluded", "true");
        if (table.CornerStyle != BorderCornerStyle.None)
            w.WriteAttributeString("CornerStyle", table.CornerStyle.ToString());
        if (table.DefaultColumnWidth is { Length: > 0 } defaultWidth)
            w.WriteAttributeString("DefaultColumnWidth", defaultWidth);
        if (table.Left != 0) w.WriteAttributeString("Left", Num(table.Left));
        if (table.Top != 0) w.WriteAttributeString("Top", Num(table.Top));

        WriteMargin(w, "Margin", table.Margin);
        WriteBorder(w, "Border", table.Border);
        WriteBorder(w, "DefaultCellBorder", table.DefaultCellBorder);
        WriteMargin(w, "DefaultCellPadding", table.DefaultCellPadding);
        WriteCellTextState(w, "DefaultCellTextState", table.DefaultCellTextState);

        foreach (Row row in table.Rows)
            WriteRow(w, row);
        w.WriteEndElement();
    }

    private static void WriteRow(XmlWriter w, Row row)
    {
        w.WriteStartElement("Row");
        if (row.BackgroundColor is { } bg) w.WriteAttributeString("BackgroundColor", Hex(bg));
        if (row.MinRowHeight > 0) w.WriteAttributeString("MinRowHeight", Num(row.MinRowHeight));
        if (row.FixedRowHeight > 0) w.WriteAttributeString("FixedRowHeight", Num(row.FixedRowHeight));
        if (row.VerticalAlignment != VerticalAlignment.None)
            w.WriteAttributeString("VerticalAlignment", row.VerticalAlignment.ToString());
        // Only a CALLER's demand round-trips. The property doubles as the layout's
        // report of where the row landed (and the row collection's own crude estimate
        // at Add time), and writing that out would come back as a demand to break a
        // page under every row that merely happened to start one.
        if (row.IsInNewPageAuthored) w.WriteAttributeString("IsInNewPage", "true");
        if (row.IsRowBroken) w.WriteAttributeString("IsRowBroken", "true");
        WriteBorder(w, "Border", row.Border);
        WriteBorder(w, "DefaultCellBorder", row.DefaultCellBorder);
        WriteMargin(w, "DefaultCellPadding", row.DefaultCellPadding);
        WriteCellTextState(w, "DefaultCellTextState", row.DefaultCellTextState);
        foreach (Cell cell in row.Cells)
            WriteCell(w, cell);
        w.WriteEndElement();
    }

    private static void WriteCell(XmlWriter w, Cell cell)
    {
        w.WriteStartElement("Cell");
        if (cell.ColSpan != 1) w.WriteAttributeString("ColSpan", cell.ColSpan.ToString(CultureInfo.InvariantCulture));
        if (cell.RowSpan != 1) w.WriteAttributeString("RowSpan", cell.RowSpan.ToString(CultureInfo.InvariantCulture));
        if (cell.IsNoBorder) w.WriteAttributeString("IsNoBorder", "true");
        // Wrapping is ON by default, so it is the OFF case that has to survive the
        // round trip (a caller crops a non-wrapping cell and re-reads the document).
        if (!cell.IsWordWrapped) w.WriteAttributeString("IsWordWrapped", "false");
        if (cell.IsOverrideByFragment) w.WriteAttributeString("IsOverrideByFragment", "true");
        if (cell.BackgroundColor is { } bg) w.WriteAttributeString("BackgroundColor", Hex(bg));
        if (cell.Alignment != HorizontalAlignment.None)
            w.WriteAttributeString("Alignment", cell.Alignment.ToString());
        if (cell.VerticalAlignment != VerticalAlignment.None)
            w.WriteAttributeString("VerticalAlignment", cell.VerticalAlignment.ToString());
        WriteBorder(w, "Border", cell.Border);
        WriteMargin(w, "Margin", cell.Margin);
        WriteCellTextState(w, "DefaultCellTextState", cell.DefaultCellTextState);
        // The cell's own artwork is part of the document: a template that dropped it
        // round-trips to a blank band (a logo, a header bar).
        if (cell.BackgroundImage is { } cellBg)
        {
            w.WriteStartElement("BackgroundImage");
            WriteImage(w, cellBg);
            w.WriteEndElement();
        }
        foreach (var paragraph in cell.Paragraphs)
            WriteParagraph(w, paragraph);
        w.WriteEndElement();
    }

    // ---- boxes and images ------------------------------------------------------

    private static void WriteFloatingBox(XmlWriter w, FloatingBox box)
    {
        w.WriteStartElement("FloatingBox");
        WriteParagraphAttributes(w, box);
        if (box.Width > 0) w.WriteAttributeString("Width", Num(box.Width));
        if (box.Height > 0) w.WriteAttributeString("Height", Num(box.Height));
        // Left/Top only mean anything for an absolutely-positioned box; on a flowed
        // one they are the untouched zeros, and writing them would pin it in place.
        if (box.PositioningMode == ParagraphPositioningMode.Absolute)
        {
            w.WriteAttributeString("Left", Num(box.Left));
            w.WriteAttributeString("Top", Num(box.Top));
        }
        if (box.BackgroundColor is { } bg) w.WriteAttributeString("BackgroundColor", Hex(bg));
        if (!box.IsNeedRepeating) w.WriteAttributeString("IsNeedRepeating", "false");
        if (box.ColumnInfo is { ColumnCount: > 1 } columns)
        {
            w.WriteAttributeString("ColumnCount", columns.ColumnCount.ToString(CultureInfo.InvariantCulture));
            if (columns.ColumnWidths is { Length: > 0 } cw) w.WriteAttributeString("ColumnWidths", cw);
            if (columns.ColumnSpacing is { Length: > 0 } cs) w.WriteAttributeString("ColumnSpacing", cs);
        }
        WriteMargin(w, "Margin", box.Margin);
        WriteMargin(w, "Padding", box.Padding);
        WriteBorder(w, "Border", box.Border);
        foreach (var paragraph in box.Paragraphs)
            WriteParagraph(w, paragraph);
        w.WriteEndElement();
    }

    /// <summary>An <c>&lt;Image&gt;</c> normally names a FILE. An image built from a
    /// STREAM has no name to give, so its bytes go out base64 in a <c>&lt;Data&gt;</c>
    /// child — the picture is part of the document, and a template that dropped it
    /// would round-trip to a page with a hole in it.</summary>
    private static void WriteImage(XmlWriter w, Image image)
    {
        var inlined = string.IsNullOrEmpty(image.File) ? ReadImageBytes(image) : null;
        if (string.IsNullOrEmpty(image.File) && inlined is null) return;
        w.WriteStartElement("Image");
        WriteParagraphAttributes(w, image);
        if (!string.IsNullOrEmpty(image.File)) w.WriteAttributeString("File", image.File);
        if (image.FixWidth > 0) w.WriteAttributeString("FixWidth", Num(image.FixWidth));
        if (image.FixHeight > 0) w.WriteAttributeString("FixHeight", Num(image.FixHeight));
        if (image.ImageScale > 0) w.WriteAttributeString("ImageScale", Num(image.ImageScale));
        if (image.IsBlackWhite) w.WriteAttributeString("IsBlackWhite", "true");
        if (image.IsApplyResolution) w.WriteAttributeString("IsApplyResolution", "true");
        WriteMargin(w, "Margin", image.Margin);
        if (inlined is not null)
        {
            w.WriteStartElement("Data");
            w.WriteBase64(inlined, 0, inlined.Length);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>The stream's bytes, read from the start and leaving the position where
    /// it was found — the caller still owns the stream and may read it after the save.
    /// </summary>
    private static byte[]? ReadImageBytes(Image image) => ReadStreamBytes(image.ImageStream);

    private static byte[]? ReadStreamBytes(Stream? stream)
    {
        if (stream is null) return null;
        var position = stream.CanSeek ? stream.Position : -1L;
        if (stream.CanSeek) stream.Position = 0;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        if (position >= 0) stream.Position = position;
        return buffer.Length > 0 ? buffer.ToArray() : null;
    }

    // ---- graphs ----------------------------------------------------------------

    private static void WriteGraph(XmlWriter w, Graph graph)
    {
        w.WriteStartElement("Graph");
        WriteParagraphAttributes(w, graph);
        w.WriteAttributeString("Width", Num(graph.Width));
        w.WriteAttributeString("Height", Num(graph.Height));
        if (!graph.IsChangePosition) w.WriteAttributeString("IsChangePosition", "false");
        if (graph.LeftAssigned) w.WriteAttributeString("Left", Num(graph.Left));
        if (graph.TopAssigned) w.WriteAttributeString("Top", Num(graph.Top));
        // The graph's OWN GraphInfo carries the box transform — rotation, skew and the
        // scaling rates. Only the SHAPES' GraphInfo used to be written, so a rotated or
        // scaled graph came back untransformed.
        if (graph.GraphInfo is { } gi)
        {
            if (gi.RotationAngle != 0) w.WriteAttributeString("RotationAngle", Num(gi.RotationAngle));
            if (gi.SkewAngleX != 0) w.WriteAttributeString("SkewAngleX", Num(gi.SkewAngleX));
            if (gi.SkewAngleY != 0) w.WriteAttributeString("SkewAngleY", Num(gi.SkewAngleY));
            if (gi.ScalingRateX != 1) w.WriteAttributeString("ScalingRateX", Num(gi.ScalingRateX));
            if (gi.ScalingRateY != 1) w.WriteAttributeString("ScalingRateY", Num(gi.ScalingRateY));
        }
        WriteMargin(w, "Margin", graph.Margin);
        WriteBorder(w, "Border", graph.Border);
        foreach (var shape in graph.Shapes)
            WriteShape(w, shape);
        w.WriteEndElement();
    }

    private static void WriteShape(XmlWriter w, Shape shape)
    {
        switch (shape)
        {
            case Line line:
                w.WriteStartElement("Line");
                w.WriteAttributeString("PositionArray", Positions(line.PositionArray));
                break;

            case Arc arc:
                w.WriteStartElement("Arc");
                w.WriteAttributeString("PosX", Num(arc.CenterX));
                w.WriteAttributeString("PosY", Num(arc.CenterY));
                w.WriteAttributeString("Radius", Num(arc.RadiusX));
                w.WriteAttributeString("RadiusY", Num(arc.RadiusY));
                w.WriteAttributeString("Alpha", Num(arc.Alpha));
                w.WriteAttributeString("Beta", Num(arc.Beta));
                break;

            case Circle circle:
                w.WriteStartElement("Circle");
                w.WriteAttributeString("PosX", Num(circle.CenterX));
                w.WriteAttributeString("PosY", Num(circle.CenterY));
                w.WriteAttributeString("Radius", Num(circle.Radius));
                break;

            case Ellipse ellipse:
                w.WriteStartElement("Ellipse");
                w.WriteAttributeString("Left", Num(ellipse.Left));
                w.WriteAttributeString("Bottom", Num(ellipse.Bottom));
                w.WriteAttributeString("Width", Num(ellipse.Width));
                w.WriteAttributeString("Height", Num(ellipse.Height));
                break;

            case Aspose.Pdf.Drawing.Rectangle rect:
                w.WriteStartElement("Rectangle");
                w.WriteAttributeString("Left", Num(rect.Left));
                w.WriteAttributeString("Bottom", Num(rect.Bottom));
                w.WriteAttributeString("Width", Num(rect.Width));
                w.WriteAttributeString("Height", Num(rect.Height));
                if (rect.RoundedCornerRadius != 0)
                    w.WriteAttributeString("RoundedCornerRadius", Num(rect.RoundedCornerRadius));
                break;

            case Curve curve:
                w.WriteStartElement("Curve");
                w.WriteAttributeString("PositionArray", Positions(new[]
                {
                    (float)curve.X1, (float)curve.Y1, (float)curve.Cx1, (float)curve.Cy1,
                    (float)curve.Cx2, (float)curve.Cy2, (float)curve.X2, (float)curve.Y2,
                }));
                break;

            case Aspose.Pdf.Drawing.Path path:
                w.WriteStartElement("Path");
                WriteGraphInfo(w, shape.GraphInfo);
                foreach (var child in path.Shapes)
                    WriteShape(w, child);
                w.WriteEndElement();
                return;

            default:
                // No binder-side counterpart; see the note on WriteParagraph.
                return;
        }

        WriteGraphInfo(w, shape.GraphInfo);
        w.WriteEndElement();
    }

    private static void WriteGraphInfo(XmlWriter w, Aspose.Pdf.GraphInfo? info)
    {
        if (info is null) return;
        w.WriteStartElement("GraphInfo");
        w.WriteAttributeString("LineWidth", Num(info.LineWidth));
        if (info.Color is { } stroke) w.WriteAttributeString("Color", Hex(stroke));
        if (info.FillColor is { } fill) w.WriteAttributeString("FillColor", Hex(fill));
        if (info.DashArray is { Length: > 0 } dashes)
        {
            w.WriteAttributeString("DashArray", string.Join(" ",
                dashes.Select(d => d.ToString(CultureInfo.InvariantCulture))));
            w.WriteAttributeString("DashPhase", Num(info.DashPhase));
        }
        w.WriteEndElement();
    }

    // ---- shared leaf elements --------------------------------------------------

    /// <summary>A margin/padding element, written only when the caller actually set a
    /// side: an untouched <see cref="MarginInfo"/> reports the layout defaults, and
    /// writing those out would turn a fallback into an authored zero.</summary>
    private static void WriteMargin(XmlWriter w, string name, MarginInfo? margin)
    {
        if (margin is null || !margin.IsTouched) return;
        w.WriteStartElement(name);
        w.WriteAttributeString("Left", Num(margin.Left));
        w.WriteAttributeString("Right", Num(margin.Right));
        w.WriteAttributeString("Top", Num(margin.Top));
        w.WriteAttributeString("Bottom", Num(margin.Bottom));
        w.WriteEndElement();
    }

    /// <summary>A border, one child element per drawing side. <c>&lt;All&gt;</c> is the
    /// binder's spelling for the full box; the per-side elements carry their own width
    /// and colour when the sides were styled apart.</summary>
    private static void WriteBorder(XmlWriter w, string name, BorderInfo? border)
    {
        if (border is null || !border.HasAnySide) return;
        var sides = border.EffectiveSides;
        w.WriteStartElement(name);
        if (sides == BorderSide.Box && border.RawTop is null && border.RawBottom is null
            && border.RawLeft is null && border.RawRight is null)
        {
            WriteBorderSide(w, "All", border.Width, border.Color);
        }
        else
        {
            if (sides.HasFlag(BorderSide.Top)) WriteBorderSide(w, "Top", border, border.RawTop);
            if (sides.HasFlag(BorderSide.Bottom)) WriteBorderSide(w, "Bottom", border, border.RawBottom);
            if (sides.HasFlag(BorderSide.Left)) WriteBorderSide(w, "Left", border, border.RawLeft);
            if (sides.HasFlag(BorderSide.Right)) WriteBorderSide(w, "Right", border, border.RawRight);
        }
        w.WriteEndElement();
    }

    private static void WriteBorderSide(XmlWriter w, string side, BorderInfo border, Aspose.Pdf.GraphInfo? styled)
        => WriteBorderSide(w, side, styled is not null ? styled.LineWidth : border.Width,
            styled?.Color ?? border.Color, styled?.IsDoubled == true);

    private static void WriteBorderSide(XmlWriter w, string side, double width, Color? color,
        bool doubled = false)
    {
        w.WriteStartElement(side);
        w.WriteAttributeString("LineWidth", Num(width));
        if (color is not null) w.WriteAttributeString("Color", Hex(color));
        // The doubled flag is geometry, not decoration — a side that loses it comes back
        // two rules and their clearance narrower.
        if (doubled) w.WriteAttributeString("IsDoubled", "true");
        w.WriteEndElement();
    }

    /// <summary>A <c>DefaultCellTextState</c>-shaped element. The binder seeds it from
    /// the existing state, so only the properties the caller actually set are written —
    /// the ctor's 10 pt placeholder is not an authored size.</summary>
    private static void WriteCellTextState(XmlWriter w, string name, TextState? state)
    {
        if (state is null) return;
        var font = FontAttribute(state);
        if (!state.FontSizeTouched && font is null && state.ForegroundColor is null
            && state.LineSpacing <= 0)
            return;
        w.WriteStartElement(name);
        if (state.FontSizeTouched) w.WriteAttributeString("FontSize", Num(state.FontSize));
        if (font is not null) w.WriteAttributeString("Font", font);
        if (state.ForegroundColor is { } fg) w.WriteAttributeString("ForegroundColor", Hex(fg));
        if (state.LineSpacing > 0) w.WriteAttributeString("LineSpacing", Num(state.LineSpacing));
        w.WriteEndElement();
    }

    private static string Positions(float[]? coords)
    {
        if (coords is null || coords.Length == 0) return string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < coords.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(coords[i].ToString("0.####", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    private static string Num(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Hex(Aspose.Pdf.Color c) =>
        string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
}
