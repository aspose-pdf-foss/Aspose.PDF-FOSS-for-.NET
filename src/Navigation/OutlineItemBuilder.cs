using System;
using System.Text;
using Aspose.Pdf.Annotations;
using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Builder for a single outline item with fluent API.
/// </summary>
public sealed class OutlineItemBuilder
{
    private readonly List<OutlineItemBuilder> _children = [];

    internal OutlineItemBuilder(string title, int pageIndex)
    {
        Title = title;
        PageIndex = pageIndex;
    }

    public string Title { get; set; }
    public int PageIndex { get; }
    public bool IsOpen { get; private set; } = true;
    public bool IsBold { get; private set; }
    public bool IsItalic { get; private set; }
    internal double? ColorR { get; private set; }
    internal double? ColorG { get; private set; }
    internal double? ColorB { get; private set; }
    internal IReadOnlyList<OutlineItemBuilder> Children => _children;

    /// <summary>Add a child bookmark.</summary>
    public OutlineItemBuilder AddChild(string title, int pageIndex)
    {
        var child = new OutlineItemBuilder(title, pageIndex);
        _children.Add(child);
        return child;
    }

    /// <summary>Add a child bookmark pointing to a page.</summary>
    public OutlineItemBuilder AddChild(string title, Page page)
        => AddChild(title, page.Index);

    /// <summary>Set whether this bookmark is initially open/expanded.</summary>
    public OutlineItemBuilder SetOpen(bool open) { IsOpen = open; return this; }

    /// <summary>Set the bookmark text color (RGB 0.0-1.0).</summary>
    public OutlineItemBuilder SetColor(double r, double g, double b)
    {
        ColorR = r; ColorG = g; ColorB = b;
        return this;
    }

    /// <summary>Set bold style.</summary>
    public OutlineItemBuilder SetBold(bool bold) { IsBold = bold; return this; }

    /// <summary>Set italic style.</summary>
    public OutlineItemBuilder SetItalic(bool italic) { IsItalic = italic; return this; }
}
