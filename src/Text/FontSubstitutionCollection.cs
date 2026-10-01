using System;
using System.Collections;
using System.Collections.Generic;

namespace Aspose.Pdf.Text;

/// <summary>Base type for font substitutions held in <see cref="FontSubstitutionCollection"/>.</summary>
public class FontSubstitution
{
    /// <summary>Creates an empty font substitution.</summary>
    public FontSubstitution() { }
}

/// <summary>Base class for a user-defined font substitution: override <c>TrySubstitute</c> to supply a replacement font for an original font name.</summary>
public class CustomFontSubstitutionBase : FontSubstitution
{
    /// <summary>Creates a substitution whose default <c>TrySubstitute</c> never substitutes.</summary>
    public CustomFontSubstitutionBase() { }

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

/// <summary>A font substitution that replaces one font name with another: when the original name matches exactly (case-sensitive), the substitute font is looked up in <c>FontRepository</c>.</summary>
public sealed class SimpleFontSubstitution : CustomFontSubstitutionBase
{
    /// <summary>Gets the name of the font to be replaced.</summary>
    public string OriginalFontName { get; }
    public string SubstitutionFontName { get; }

    /// <summary>Creates a substitution that replaces <c>originalFontName</c> with <c>substitutionFontName</c>; throws when either name is <c>null</c>.</summary>
    public SimpleFontSubstitution(string originalFontName, string substitutionFontName)
    {
        OriginalFontName = originalFontName ?? throw new ArgumentNullException(nameof(originalFontName));
        SubstitutionFontName = substitutionFontName ?? throw new ArgumentNullException(nameof(substitutionFontName));
    }

    /// <summary>Whether the substitution is forced by save-option configuration. Stored only.</summary>
    public bool IsForcedBySaveOption { get; }

    /// <summary>Creates a substitution that replaces <c>originalFontName</c> with <c>substitutionFontName</c> and records whether it is forced by save options (stored only).</summary>
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
            substitutionFont = FontRepository.TryFindFont(SubstitutionFontName);
            return substitutionFont is not null;
        }
        substitutionFont = null;
        return false;
    }
}

/// <summary>The list of font substitutions registered in <c>FontRepository.Substitutions</c>. Entries are stored; font lookup in this library does not currently apply them.</summary>
public sealed class FontSubstitutionCollection : IReadOnlyCollection<FontSubstitution>
{
    private readonly List<FontSubstitution> _items = new();

    internal FontSubstitutionCollection() { }

    /// <summary>Gets the number of substitutions in the collection.</summary>
    public int Count => _items.Count;

    public bool IsSynchronized => false;
    public object SyncRoot { get; } = new();

    /// <summary>Gets the substitution at the given zero-based index.</summary>
    public FontSubstitution this[int index] => _items[index];

    /// <summary>Adds a substitution to the end of the collection; a <c>null</c> argument is ignored.</summary>
    public void Add(FontSubstitution fontSubstitution)
    {
        // A null add is ignored, matching Remove's null tolerance: a null entry could
        // never substitute anything, and callers built against a wider API surface
        // pass null where a substitution type exists there but not here.
        if (fontSubstitution is null) return;
        _items.Add(fontSubstitution);
    }

    /// <summary>Append a CustomFontSubstitutionBase (subclass of <see cref="FontSubstitution"/>).</summary>
    public void Add(CustomFontSubstitutionBase substitution) => Add((FontSubstitution)substitution);

    /// <summary>Returns <c>true</c> when the collection contains the given substitution.</summary>
    public bool Contains(FontSubstitution item) => _items.Contains(item);

    /// <summary>Copies the substitutions into <c>array</c>, starting at the zero-based <c>index</c>.</summary>
    public void CopyTo(FontSubstitution[] array, int index) => _items.CopyTo(array, index);

    /// <summary>Removes the given substitution; returns <c>true</c> when it was found and <c>false</c> for <c>null</c>.</summary>
    public bool Remove(FontSubstitution item)
    {
        if (item is null) return false;
        return _items.Remove(item);
    }

    /// <summary>Removes the given custom substitution; returns <c>true</c> when it was found.</summary>
    public bool Remove(CustomFontSubstitutionBase substitution) => Remove((FontSubstitution)substitution);

    /// <summary>Removes the given custom substitution, if present.</summary>
    public void Delete(CustomFontSubstitutionBase substitution) => Remove(substitution);

    /// <summary>Removes all substitutions from the collection.</summary>
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
