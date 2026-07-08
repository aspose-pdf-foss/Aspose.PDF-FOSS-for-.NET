using Aspose.Pdf.Core;
using Aspose.Pdf.IO;

namespace Aspose.Pdf;

/// <summary>
/// Numbering style for page labels.
/// </summary>
public enum NumberingStyle
{
    None,
    Decimal,
    UpperRoman,
    LowerRoman,
    UpperAlpha,
    LowerAlpha,
    /// <summary>Aspose.Pdf alias for <see cref="Decimal"/>.</summary>
    NumeralsArabic = Decimal,
    /// <summary>Aspose.Pdf alias for <see cref="UpperRoman"/>.</summary>
    NumeralsRomanUppercase = UpperRoman,
    /// <summary>Aspose.Pdf alias for <see cref="LowerRoman"/>.</summary>
    NumeralsRomanLowercase = LowerRoman,
    /// <summary>Aspose.Pdf alias for <see cref="UpperAlpha"/>.</summary>
    LettersUppercase = UpperAlpha,
    /// <summary>Aspose.Pdf alias for <see cref="LowerAlpha"/>.</summary>
    LettersLowercase = LowerAlpha,
}

/// <summary>
/// Represents a page label range.
/// </summary>
public sealed class PageLabel
{
    /// <summary>0-based page index where this label range starts.</summary>
    public int StartPage { get; internal set; }

    /// <summary>0-based page index of the last page this range applies to, or -1 for end of document.</summary>
    public int LastPageIndex { get; internal set; } = -1;

    /// <summary>The numbering style.</summary>
    public NumberingStyle Style { get; set; }

    /// <summary>Aspose.Pdf alias for <see cref="Style"/>.</summary>
    public NumberingStyle NumberingStyle
    {
        get => Style;
        set => Style = value;
    }

    /// <summary>Aspose.Pdf alias for <see cref="Start"/>.</summary>
    public int StartingValue
    {
        get => Start;
        set => _start = value;
    }

    /// <summary>The numbering style as a string (e.g., "NumeralsArabic", "NumeralsRomanUppercase").</summary>
    public string NumberingStyleName => Style switch
    {
        NumberingStyle.Decimal => "NumeralsArabic",
        NumberingStyle.UpperRoman => "NumeralsRomanUppercase",
        NumberingStyle.LowerRoman => "NumeralsRomanLowercase",
        NumberingStyle.UpperAlpha => "LettersUppercase",
        NumberingStyle.LowerAlpha => "LettersLowercase",
        _ => "None",
    };

    /// <summary>The label prefix string (empty string if not set).</summary>
    public string? Prefix { get; init; }

    private int _start = 1;

    /// <summary>The starting number for this range.</summary>
    public int Start
    {
        get => _start;
        init => _start = value;
    }

    /// <summary>Format a label for a page at the given 0-based index.</summary>
    public string FormatLabel(int pageIndex)
    {
        var num = Start + (pageIndex - StartPage);
        var formatted = Style switch
        {
            NumberingStyle.Decimal => num.ToString(),
            NumberingStyle.UpperRoman => ToRoman(num),
            NumberingStyle.LowerRoman => ToRoman(num).ToLowerInvariant(),
            NumberingStyle.UpperAlpha => ToAlpha(num),
            NumberingStyle.LowerAlpha => ToAlpha(num).ToLowerInvariant(),
            _ => "",
        };
        return (Prefix ?? "") + formatted;
    }

    /// <summary>Format a positive integer according to the given numbering style.</summary>
    public static string FormatNumber(int num, NumberingStyle style) => style switch
    {
        NumberingStyle.Decimal => num.ToString(),
        NumberingStyle.UpperRoman => ToRoman(num),
        NumberingStyle.LowerRoman => ToRoman(num).ToLowerInvariant(),
        NumberingStyle.UpperAlpha => ToAlpha(num),
        NumberingStyle.LowerAlpha => ToAlpha(num).ToLowerInvariant(),
        _ => "",
    };

    /// <summary>Convert a positive integer to an uppercase Roman numeral string.</summary>
    public static string ToRoman(int num)
    {
        if (num <= 0 || num >= 4000) return num.ToString();
        ReadOnlySpan<(int value, string numeral)> map =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
            (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
            (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];
        var result = "";
        foreach (var (value, numeral) in map)
        {
            while (num >= value) { result += numeral; num -= value; }
        }
        return result;
    }

    /// <summary>Convert a positive integer to an alphabetic label (A=1, Z=26, AA=27, etc.).</summary>
    public static string ToAlpha(int num)
    {
        if (num <= 0) return num.ToString();
        var result = "";
        while (num > 0)
        {
            num--;
            result = (char)('A' + (num % 26)) + result;
            num /= 26;
        }
        return result;
    }
}

/// <summary>
/// Collection of page label ranges from the /PageLabels number tree.
/// </summary>
public sealed class PageLabelCollection : IEnumerable<PageLabel>
{
    private readonly List<PageLabel> _labels = [];

    /// <summary>True once a label has been added/updated/removed through this
    /// collection, so the document re-serialises the /PageLabels tree on save.</summary>
    internal bool IsDirty { get; private set; }

    /// <summary>Empty collection for a document that has no /PageLabels yet, so
    /// <c>doc.PageLabels</c> is usable for adding labels rather than null.</summary>
    internal PageLabelCollection() { }

    internal PageLabelCollection(PdfDictionary pageLabelTree, PdfReader reader)
    {
        ParseNumberTree(pageLabelTree, reader);
        _labels.Sort((a, b) => a.StartPage.CompareTo(b.StartPage));
        // Compute LastPageIndex for each label range
        for (var i = 0; i < _labels.Count; i++)
        {
            _labels[i].LastPageIndex = i + 1 < _labels.Count
                ? _labels[i + 1].StartPage - 1
                : -1; // extends to end of document
        }
    }

    /// <summary>All label ranges.</summary>
    public IReadOnlyList<PageLabel> Labels => _labels;

    /// <summary>Number of label ranges.</summary>
    public int Count => _labels.Count;

    /// <summary>Get a label range by 0-based index.</summary>
    public PageLabel At(int index) => _labels[index];

    /// <summary>Get the formatted display label for a 0-based page index.</summary>
    public string GetLabelForPage(int pageIndex) => FormatLabel(pageIndex);

    public IEnumerator<PageLabel> GetEnumerator() => _labels.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Find the label range that contains <paramref name="pageIndex"/> (0-based).</summary>
    public PageLabel? GetLabel(int pageIndex)
    {
        PageLabel? active = null;
        foreach (var label in _labels)
        {
            if (label.StartPage <= pageIndex)
                active = label;
            else
                break;
        }
        return active;
    }

    /// <summary>The 0-based page indices at which a label range begins.</summary>
    public int[] GetPages()
    {
        var arr = new int[_labels.Count];
        for (int i = 0; i < _labels.Count; i++) arr[i] = _labels[i].StartPage;
        return arr;
    }

    /// <summary>Remove the label range starting at <paramref name="pageIndex"/> (0-based).</summary>
    public bool RemoveLabel(int pageIndex)
    {
        for (int i = 0; i < _labels.Count; i++)
        {
            if (_labels[i].StartPage == pageIndex)
            {
                _labels.RemoveAt(i);
                IsDirty = true;
                return true;
            }
        }
        return false;
    }

    /// <summary>Replace the label range at <paramref name="pageIndex"/> with <paramref name="pageLabel"/>.</summary>
    public void UpdateLabel(int pageIndex, PageLabel pageLabel)
    {
        if (pageLabel is null) throw new ArgumentNullException(nameof(pageLabel));
        IsDirty = true;
        // The label range starts at the given page index — bind it to the
        // argument (callers build the PageLabel without setting StartPage).
        pageLabel.StartPage = pageIndex;
        for (int i = 0; i < _labels.Count; i++)
        {
            if (_labels[i].StartPage == pageIndex)
            {
                _labels[i] = pageLabel;
                return;
            }
        }
        _labels.Add(pageLabel);
        _labels.Sort((a, b) => a.StartPage.CompareTo(b.StartPage));
    }

    /// <summary>Serialise the current label ranges into the document's catalog as a
    /// /PageLabels number tree. Called on save when <see cref="IsDirty"/>.</summary>
    internal void Serialize(Document document)
    {
        if (_labels.Count == 0)
        {
            document.Catalog.Remove("PageLabels");
            return;
        }
        var nums = new PdfArray();
        foreach (var label in _labels.OrderBy(l => l.StartPage))
        {
            nums.Add(new PdfInteger(label.StartPage));
            var dict = new PdfDictionary();
            var styleStr = label.Style switch
            {
                NumberingStyle.Decimal => "D",
                NumberingStyle.UpperRoman => "R",
                NumberingStyle.LowerRoman => "r",
                NumberingStyle.UpperAlpha => "A",
                NumberingStyle.LowerAlpha => "a",
                _ => null,
            };
            if (styleStr is not null) dict.Set("S", new PdfName(styleStr));
            if (label.Prefix is not null)
                dict.Set("P", new PdfString(System.Text.Encoding.Latin1.GetBytes(label.Prefix)));
            if (label.Start != 1) dict.Set("St", new PdfInteger(label.Start));
            nums.Add(dict);
        }
        var treeDictObjNum = document.AllocateObjectNumber() + 50;
        var treeDict = new PdfDictionary();
        treeDict.Set("Nums", nums);
        document.AddNewObject(treeDictObjNum, treeDict);
        document.Catalog.Set("PageLabels", new PdfIndirectRef(treeDictObjNum, 0));
    }

    /// <summary>Format the page label for a 0-based page index.</summary>
    public string FormatLabel(int pageIndex)
    {
        var active = GetLabel(pageIndex);
        return active?.FormatLabel(pageIndex) ?? (pageIndex + 1).ToString();
    }

    /// <summary>Format the label of the last page in the label range that contains
    /// <paramref name="pageIndex"/> (0-based), within a document of
    /// <paramref name="pageCount"/> pages. This is the "$P" (section total) macro:
    /// the section's final page rendered in that section's own numbering, so on a
    /// document with no labels it degrades to the total page count.</summary>
    public string GetRangeLastLabel(int pageIndex, int pageCount)
    {
        var active = GetLabel(pageIndex);
        // Start of the next range after the active one (or after the implicit
        // pre-first-range section when no range is active yet).
        var activeStart = active?.StartPage ?? 0;
        var nextStart = pageCount;
        foreach (var l in _labels)
            if (l.StartPage > activeStart) { nextStart = l.StartPage; break; }
        var lastIndex = Math.Min(nextStart - 1, pageCount - 1);
        if (lastIndex < 0) lastIndex = 0;
        return FormatLabel(lastIndex);
    }

    private void ParseNumberTree(PdfDictionary node, PdfReader reader)
    {
        var nums = reader.Resolve(node.Get("Nums")) as PdfArray;
        if (nums is not null)
        {
            for (var i = 0; i + 1 < nums.Count; i += 2)
            {
                var pageIndex = nums[i] is PdfInteger pi ? (int)pi.Value : 0;
                var labelDict = reader.ResolveDict(nums[i + 1]);
                if (labelDict is not null)
                {
                    _labels.Add(ParseLabelDict(labelDict, pageIndex, reader));
                }
            }
        }

        var kids = reader.Resolve(node.Get("Kids")) as PdfArray;
        if (kids is not null)
        {
            foreach (var kid in kids)
            {
                var kidDict = reader.ResolveDict(kid);
                if (kidDict is not null)
                    ParseNumberTree(kidDict, reader);
            }
        }
    }

    private static PageLabel ParseLabelDict(PdfDictionary dict, int startPage, PdfReader reader)
    {
        var style = dict.GetName("S") switch
        {
            "D" => NumberingStyle.Decimal,
            "R" => NumberingStyle.UpperRoman,
            "r" => NumberingStyle.LowerRoman,
            "A" => NumberingStyle.UpperAlpha,
            "a" => NumberingStyle.LowerAlpha,
            _ => NumberingStyle.None,
        };

        var prefix = reader.Resolve(dict.Get("P")) is PdfString ps ? ps.ToText() : null;
        var start = (int)dict.GetInt("St", 1);

        return new PageLabel
        {
            StartPage = startPage,
            Style = style,
            Prefix = prefix,
            Start = start,
        };
    }
}

/// <summary>
/// Builder for creating page labels on a document.
/// Registers with the document for auto-finalization on save.
/// </summary>
public sealed class PageLabelBuilder
{
    private readonly Document _document;
    private readonly List<PageLabel> _labels = [];

    public PageLabelBuilder(Document document)
    {
        _document = document;
        document.RegisterPageLabelBuilder(this);
    }

    /// <summary>Add a page label range starting at the given 0-based page index.</summary>
    public PageLabelBuilder Add(int startPage, NumberingStyle style,
        string? prefix = null, int start = 1)
    {
        _labels.Add(new PageLabel
        {
            StartPage = startPage,
            Style = style,
            Prefix = prefix,
            Start = start,
        });
        return this;
    }

    /// <summary>Build the /PageLabels number tree and register it with the document.</summary>
    internal void Build()
    {
        if (_labels.Count == 0) return;

        var nums = new PdfArray();
        foreach (var label in _labels.OrderBy(l => l.StartPage))
        {
            nums.Add(new PdfInteger(label.StartPage));

            var dict = new PdfDictionary();
            var styleStr = label.Style switch
            {
                NumberingStyle.Decimal => "D",
                NumberingStyle.UpperRoman => "R",
                NumberingStyle.LowerRoman => "r",
                NumberingStyle.UpperAlpha => "A",
                NumberingStyle.LowerAlpha => "a",
                _ => null,
            };
            if (styleStr is not null)
                dict.Set("S", new PdfName(styleStr));
            if (label.Prefix is not null)
                dict.Set("P", new PdfString(System.Text.Encoding.Latin1.GetBytes(label.Prefix)));
            if (label.Start != 1)
                dict.Set("St", new PdfInteger(label.Start));

            nums.Add(dict);
        }

        var treeDictObjNum = _document.AllocateObjectNumber() + 50;
        var treeDict = new PdfDictionary();
        treeDict.Set("Nums", nums);
        _document.AddNewObject(treeDictObjNum, treeDict);
        _document.Catalog.Set("PageLabels", new PdfIndirectRef(treeDictObjNum, 0));
    }
}
