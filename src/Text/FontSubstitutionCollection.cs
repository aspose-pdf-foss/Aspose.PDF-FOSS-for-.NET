using System;
using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf.Text;

/// <summary>Base type for font substitutions held in <see cref="FontSubstitutionCollection"/>.</summary>
public class FontSubstitution
{
}

public class CustomFontSubstitutionBase : FontSubstitution
{
    public class OriginalFontSpecification
    {
        public string OriginalFontName { get; internal set; } = string.Empty;
        public bool IsEmbedded { get; internal set; }

        /// <summary>Whether substitution must happen — e.g. the original font isn't installable. Stored only.</summary>
        public bool IsSubstitutionUnavoidable { get; internal set; }

        internal OriginalFontSpecification() { }

        public OriginalFontSpecification(string originalFontName, bool isEmbedded)
        {
            OriginalFontName = originalFontName ?? string.Empty;
            IsEmbedded = isEmbedded;
        }
    }

    public virtual bool TrySubstitute(OriginalFontSpecification originalFontSpecification, out Font? substitutionFont)
    {
        substitutionFont = null;
        return false;
    }
}

public sealed class SimpleFontSubstitution : CustomFontSubstitutionBase
{
    public string OriginalFontName { get; }
    public string SubstitutionFontName { get; }

    public SimpleFontSubstitution(string originalFontName, string substitutionFontName)
    {
        OriginalFontName = originalFontName ?? throw new ArgumentNullException(nameof(originalFontName));
        SubstitutionFontName = substitutionFontName ?? throw new ArgumentNullException(nameof(substitutionFontName));
    }

    /// <summary>Whether the substitution is forced by save-option configuration. Stored only.</summary>
    public bool IsForcedBySaveOption { get; }

    public SimpleFontSubstitution(string originalFontName, string substitutionFontName, bool isForcedBySaveOption)
        : this(originalFontName, substitutionFontName)
    {
        IsForcedBySaveOption = isForcedBySaveOption;
    }

    public override bool TrySubstitute(OriginalFontSpecification originalFontSpecification, out Font? substitutionFont)
    {
        if (originalFontSpecification is not null
            && string.Equals(originalFontSpecification.OriginalFontName, OriginalFontName, StringComparison.Ordinal))
        {
            substitutionFont = FontRepository.FindFont(SubstitutionFontName);
            return substitutionFont is not null;
        }
        substitutionFont = null;
        return false;
    }
}

public sealed class FontSubstitutionCollection : IReadOnlyCollection<FontSubstitution>
{
    private readonly List<FontSubstitution> _items = new();

    internal FontSubstitutionCollection() { }

    public int Count => _items.Count;

    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    public FontSubstitution this[int index] => _items[index];

    public void Add(FontSubstitution fontSubstitution)
    {
        if (fontSubstitution is null) throw new ArgumentNullException(nameof(fontSubstitution));
        _items.Add(fontSubstitution);
    }

    /// <summary>Append a CustomFontSubstitutionBase (subclass of <see cref="FontSubstitution"/>).</summary>
    public void Add(CustomFontSubstitutionBase substitution) => Add((FontSubstitution)substitution);

    public bool Contains(FontSubstitution item) => _items.Contains(item);

    public void CopyTo(FontSubstitution[] array, int index) => _items.CopyTo(array, index);

    public bool Remove(FontSubstitution item)
    {
        if (item is null) return false;
        return _items.Remove(item);
    }

    public bool Remove(CustomFontSubstitutionBase substitution) => Remove((FontSubstitution)substitution);

    public void Delete(CustomFontSubstitutionBase substitution) => Remove(substitution);

    public void Clear() => _items.Clear();

    public IEnumerator<FontSubstitution> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal Font? TryResolve(string originalFontName, bool isEmbedded)
    {
        if (_items.Count == 0 || string.IsNullOrEmpty(originalFontName)) return null;
        var spec = new CustomFontSubstitutionBase.OriginalFontSpecification(originalFontName, isEmbedded);
        foreach (var s in _items)
        {
            if (s is CustomFontSubstitutionBase csub
                && csub.TrySubstitute(spec, out var font) && font is not null) return font;
        }
        return null;
    }
}
